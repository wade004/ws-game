using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="IInventoryHost"/> 默认实现（见 07 第 1.3 节 <c>InventoryHost</c>）。
    /// <para>
    /// 判断记录 1——"格子"的定义：本实现把 <see cref="InventoryOptions.MaxSlots"/> 理解为"该单位
    /// 背包里 <see cref="ItemInstance"/> 条目的个数上限"（同模板物品在未达 <c>stack_size</c> 前继续
    /// 叠加到已有条目不占用新格子；达到上限后开新条目才占用一个新格子），07/08 均未给出"格子"的
    /// 精确定义，这是最贴近传统 RPG 背包体验的解释。
    /// </para>
    /// <para>
    /// 判断记录 2——<see cref="InventoryFullPolicy.Reject"/> 下的原子性：先算出"整批 <paramref
    /// name="count"/> 全部加入后需要新增多少个格子"，若超出剩余容量，整次调用不落地任何变化
    /// （不做"先填满已有堆叠、格子超了才拒绝新开格子"的半途而废），保证调用方看到的返回值与背包
    /// 实际状态严格一致。<see cref="InventoryFullPolicy.Partial"/> 则反过来：先填满已有堆叠，再按
    /// 剩余格子数尽量开新条目，未能容纳的部分丢弃，只要有加入任何数量就返回 true。
    /// </para>
    /// <para>
    /// 判断记录 3——实例 id 的生成：<c>item.inst_&lt;递增&gt;</c>，计数器是本 <see cref="InventoryHost"/>
    /// 实例级别的全局递增序列（跨单位共享同一个计数器），保证同一进程内全局唯一、构造后每次运行
    /// 结果确定（不使用 <see cref="Guid"/>/<see cref="System.Random"/>，同 00 架构总则"禁止非确定性
    /// 输入"）。
    /// </para>
    /// </summary>
    public sealed class InventoryHost : IInventoryHost, IBatchableInventoryHost
    {
        private readonly IEventBus _bus;
        private readonly InventoryOptions _options;
        private readonly Dictionary<Id, DataRecord> _templates = new Dictionary<Id, DataRecord>();
        private readonly Dictionary<Id, List<ItemInstance>> _bags = new Dictionary<Id, List<ItemInstance>>();

        private long _nextInstanceSeq = 1;

        // R01 根治：批量事务状态（见 IInventoryTransaction 判断记录）。事务开启期间，AddItemCore/
        // RemoveItem 产生的通知事件改走 EnqueueEvent 缓存到 _txEvents，不直接送入 _bus；Commit 时按序
        // 补发，Rollback 时连同 _bags/_nextInstanceSeq 一起整体恢复到 BeginBatch 之前的快照。
        private bool _inTransaction;
        private Dictionary<Id, List<ItemInstance>>? _txSnapshot;
        private long _txSnapshotSeq;
        private readonly List<IEvent> _txEvents = new List<IEvent>();

        public InventoryHost(IDataRegistryView registry, IEventBus bus, InventoryOptions? options = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new InventoryOptions();

            foreach (var record in registry.GetAll("item.template"))
            {
                _templates[record.GetId("id")] = record;
            }
        }

        public void RegisterUnit(Id unitId)
        {
            if (!_bags.ContainsKey(unitId))
            {
                _bags[unitId] = new List<ItemInstance>();
            }
        }

        public void UnregisterUnit(Id unitId) => _bags.Remove(unitId);

        public bool AddItem(Id unitId, Id templateId, int count) => AddItemCore(unitId, templateId, count, out _);

        /// <summary>C05 根治：见 <see cref="IInventoryHost.TryAddItem"/> 判断记录——本类型支持
        /// <see cref="InventoryFullPolicy.Partial"/> 部分吞没语义，必须覆盖默认实现，如实返回
        /// <see cref="AddItemCore"/> 算出的实际落地量 <c>toAdd</c>，而不是把请求的 <paramref
        /// name="count"/> 原样当作实际量。</summary>
        public bool TryAddItem(Id unitId, Id templateId, int count, out int actualCount) =>
            AddItemCore(unitId, templateId, count, out actualCount);

        private bool AddItemCore(Id unitId, Id templateId, int count, out int actualCount)
        {
            actualCount = 0;

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "AddItem 的 count 必须为正数");
            }

            if (!_templates.TryGetValue(templateId, out var template))
            {
                throw new ArgumentException($"未知的物品模板 \"{templateId}\"", nameof(templateId));
            }

            var stackSize = (int)template.GetInt("stack_size");
            if (stackSize < 1)
            {
                stackSize = 1;
            }

            var bag = GetOrCreateBag(unitId);

            // 判断记录 2：先算出容量够不够，再决定是否落地任何变化。
            var freeInExisting = 0;
            foreach (var instance in bag)
            {
                if (instance.TemplateId.Equals(templateId) && instance.Count < stackSize)
                {
                    freeInExisting += stackSize - instance.Count;
                }
            }

            var overflow = Math.Max(0, count - freeInExisting);
            var newSlotsNeeded = overflow == 0 ? 0 : (overflow + stackSize - 1) / stackSize;
            var availableSlots = _options.MaxSlots <= 0
                ? int.MaxValue
                : Math.Max(0, _options.MaxSlots - bag.Count);

            int toAdd;
            if (newSlotsNeeded <= availableSlots)
            {
                toAdd = count;
            }
            else if (_options.FullPolicy == InventoryFullPolicy.Reject)
            {
                return false;
            }
            else
            {
                // Partial：尽量填满已有堆叠，剩余格子按 stackSize 开新条目，超出部分丢弃。
                toAdd = Math.Min(count, freeInExisting + availableSlots * stackSize);
                if (toAdd <= 0)
                {
                    return false;
                }
            }

            var remaining = toAdd;
            Id? touchedInstanceId = null;
            // N12 收边补齐（外部审计 68c9bed，P2）：逐项记录本次调用实际把多少数量分摊到了哪个
            // 实例（既有堆叠续填、新开堆叠都算一项）——见 ItemAddedEvent.Removals 判断记录。
            var removals = new List<(Id InstanceId, int Count)>();
            for (var i = 0; i < bag.Count && remaining > 0; i++)
            {
                var instance = bag[i];
                if (!instance.TemplateId.Equals(templateId) || instance.Count >= stackSize)
                {
                    continue;
                }

                var space = stackSize - instance.Count;
                var fill = Math.Min(space, remaining);
                bag[i] = new ItemInstance(instance.InstanceId, instance.TemplateId, instance.Count + fill, instance.Extra);
                remaining -= fill;
                touchedInstanceId = instance.InstanceId;
                removals.Add((instance.InstanceId, fill));
            }

            while (remaining > 0)
            {
                var take = Math.Min(stackSize, remaining);
                var instanceId = NextInstanceId();
                bag.Add(new ItemInstance(instanceId, templateId, take));
                touchedInstanceId = instanceId;
                remaining -= take;
                removals.Add((instanceId, take));
            }

            // toAdd > 0 时必然至少填过一个既有堆叠或新建过一个实例，touchedInstanceId 不为 null，
            // removals 至少有一项。
            EnqueueEvent(new ItemAddedEvent(unitId, touchedInstanceId!.Value, templateId, toAdd, removals));
            actualCount = toAdd;
            return true;
        }

        public bool RemoveItem(Id unitId, Id instanceId, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "RemoveItem 的 count 必须为正数");
            }

            if (!_bags.TryGetValue(unitId, out var bag))
            {
                return false;
            }

            for (var i = 0; i < bag.Count; i++)
            {
                var instance = bag[i];
                if (!instance.InstanceId.Equals(instanceId))
                {
                    continue;
                }

                if (count > instance.Count)
                {
                    return false;
                }

                if (count == instance.Count)
                {
                    bag.RemoveAt(i);
                }
                else
                {
                    bag[i] = new ItemInstance(instance.InstanceId, instance.TemplateId, instance.Count - count, instance.Extra);
                }

                EnqueueEvent(new ItemRemovedEvent(unitId, instanceId, count, "removed"));
                return true;
            }

            return false;
        }

        public IReadOnlyList<ItemInstance> ListItems(Id unitId) =>
            _bags.TryGetValue(unitId, out var bag) ? bag.ToList() : Array.Empty<ItemInstance>();

        public int CountOf(Id unitId, Id templateId)
        {
            if (!_bags.TryGetValue(unitId, out var bag))
            {
                return 0;
            }

            var total = 0;
            foreach (var instance in bag)
            {
                if (instance.TemplateId.Equals(templateId))
                {
                    total += instance.Count;
                }
            }

            return total;
        }

        public ItemInstance? FindInstance(Id unitId, Id instanceId)
        {
            if (!_bags.TryGetValue(unitId, out var bag))
            {
                return null;
            }

            foreach (var instance in bag)
            {
                if (instance.InstanceId.Equals(instanceId))
                {
                    return instance;
                }
            }

            return null;
        }

        /// <summary>该模板的 <c>item.template</c> 记录；不存在返回 null（供 <see cref="EquipmentHost"/>
        /// 复用同一份缓存，不必各自重新加载一遍，见判断记录）。</summary>
        internal DataRecord? GetTemplate(Id templateId) => _templates.TryGetValue(templateId, out var t) ? t : null;

        /// <summary>用存档数据整体替换该单位的背包内容（供 <see cref="InventoryPersistable.Load"/>
        /// 使用）。同步推进 <see cref="_nextInstanceSeq"/>，避免读档后新分配的实例 id 与存档里已有
        /// 的实例 id 撞车（见 <see cref="AdvanceSeqPast"/>）。</summary>
        internal void ReplaceBag(Id unitId, IReadOnlyList<ItemInstance> items)
        {
            var bag = GetOrCreateBag(unitId);
            bag.Clear();
            bag.AddRange(items);
            foreach (var item in items)
            {
                AdvanceSeqPast(item.InstanceId);
            }
        }

        /// <summary>把一个带有明确实例 id 的既有实例直接注入背包（供 <see
        /// cref="EquipmentPersistable.Load"/> 使用：先把存档里的装备实例注入背包，再调用
        /// <see cref="EquipmentHost.Equip"/> 走一遍完整的装备联动，见该类型顶部判断记录"属性/
        /// 技能/光环不存快照，读档后重新执行装备联动"）。不做堆叠合并、不发 <c>item.added</c>——
        /// 这不是"获得新物品"，只是把既有数据结构放回内存。</summary>
        internal void InjectInstance(Id unitId, ItemInstance instance)
        {
            var bag = GetOrCreateBag(unitId);
            bag.Add(instance);
            AdvanceSeqPast(instance.InstanceId);
        }

        private void AdvanceSeqPast(Id instanceId)
        {
            const string prefix = "item.inst_";
            var value = instanceId.Value;
            if (value.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(value.Substring(prefix.Length), out var n) && n >= _nextInstanceSeq)
            {
                _nextInstanceSeq = n + 1;
            }
        }

        /// <summary>把整份实例（不论堆叠数）从背包移出，供 <see cref="EquipmentHost.Equip"/> 把要
        /// 装备的物品从背包摘走使用。不发 <c>item.removed</c>（穿脱本身不是"移除"语义，见 07 第
        /// 1.4 节——只有 <c>item.equipped</c>/<c>item.unequipped</c>）。</summary>
        internal bool TryTakeWhole(Id unitId, Id instanceId, out ItemInstance instance)
        {
            if (_bags.TryGetValue(unitId, out var bag))
            {
                for (var i = 0; i < bag.Count; i++)
                {
                    if (bag[i].InstanceId.Equals(instanceId))
                    {
                        instance = bag[i];
                        bag.RemoveAt(i);
                        return true;
                    }
                }
            }

            instance = default;
            return false;
        }

        /// <summary>把一个已存在的实例（通常是被卸下的装备）放回背包，不做同模板堆叠合并——装备类
        /// 物品在校验规则下 <c>stack_size == 1</c>，天然不需要合并；只受格子数上限约束，是否还有
        /// 剩余容量与 <see cref="InventoryOptions.FullPolicy"/> 无关（判断记录：单个不可拆分的实例
        /// 放不下就是放不下，Reject/Partial 在"是否放入这一个不可拆分的实例"这件事上退化为同一种
        /// 行为，只有多个可堆叠数量的批量加入才存在"部分成功"的空间，见 <see cref="AddItem"/>）。</summary>
        internal bool TryPutBack(Id unitId, ItemInstance instance)
        {
            var bag = GetOrCreateBag(unitId);
            if (_options.MaxSlots > 0 && bag.Count >= _options.MaxSlots)
            {
                return false;
            }

            bag.Add(instance);
            return true;
        }

        /// <summary>是否还有至少一个格子的空余（供 <see cref="EquipmentHost.Unequip"/> 在实际改动
        /// 任何状态前先判断，见该方法判断记录：单个不可拆分实例的归还不区分 <see
        /// cref="InventoryFullPolicy.Reject"/>/<see cref="InventoryFullPolicy.Partial"/>）。不创建
        /// 该单位的背包条目（纯只读查询，未注册单位视为空背包）。</summary>
        internal bool HasRoomForOne(Id unitId)
        {
            if (_options.MaxSlots <= 0)
            {
                return true;
            }

            var count = _bags.TryGetValue(unitId, out var bag) ? bag.Count : 0;
            return count < _options.MaxSlots;
        }

        private List<ItemInstance> GetOrCreateBag(Id unitId)
        {
            if (!_bags.TryGetValue(unitId, out var bag))
            {
                bag = new List<ItemInstance>();
                _bags[unitId] = bag;
            }

            return bag;
        }

        private Id NextInstanceId() => new Id($"item.inst_{_nextInstanceSeq++}");

        /// <summary>R01 根治：事务开启期间缓存事件、不立即送入总线；未开启事务时行为与此前完全一致
        /// （立即 <see cref="IEventBus.Enqueue"/>），见 <see cref="IInventoryTransaction"/> 判断记录。</summary>
        private void EnqueueEvent(IEvent evt)
        {
            if (_inTransaction)
            {
                _txEvents.Add(evt);
            }
            else
            {
                _bus.Enqueue(evt);
            }
        }

        /// <summary>见 <see cref="IBatchableInventoryHost.BeginBatch"/>。对 <see cref="_bags"/> 做一次
        /// 浅拷贝快照（每个单位的 <see cref="List{ItemInstance}"/> 另开一份列表，<see cref="ItemInstance"/>
        /// 本身是不可变值类型，元素不需要再深拷贝）连同 <see cref="_nextInstanceSeq"/> 一并记录，供
        /// <see cref="RollbackBatch"/> 整体恢复。
        /// <para>
        /// 第五轮外部审核相邻缺口根治：已在事务中时不再报错，而是返回一个 <c>isRoot: false</c> 的
        /// 透传 <see cref="Transaction"/>（见其判断记录）——加入外层事务，不重新拍快照、不清空
        /// <see cref="_txEvents"/>（外层快照/事件仍是唯一权威版本）。</para>
        /// </summary>
        public IInventoryTransaction BeginBatch()
        {
            if (_inTransaction)
            {
                return new Transaction(this, isRoot: false);
            }

            _inTransaction = true;
            _txSnapshot = new Dictionary<Id, List<ItemInstance>>();
            foreach (var kv in _bags)
            {
                _txSnapshot[kv.Key] = new List<ItemInstance>(kv.Value);
            }
            _txSnapshotSeq = _nextInstanceSeq;
            _txEvents.Clear();
            return new Transaction(this, isRoot: true);
        }

        private void CommitBatch()
        {
            _inTransaction = false;
            var events = _txEvents.ToList();
            _txEvents.Clear();
            _txSnapshot = null;
            foreach (var evt in events)
            {
                _bus.Enqueue(evt);
            }
        }

        private void RollbackBatch()
        {
            _inTransaction = false;
            _bags.Clear();
            if (_txSnapshot != null)
            {
                foreach (var kv in _txSnapshot)
                {
                    _bags[kv.Key] = kv.Value;
                }
            }
            _nextInstanceSeq = _txSnapshotSeq;
            _txEvents.Clear();
            _txSnapshot = null;
        }

        /// <summary>见 <see cref="IInventoryTransaction"/>。<see cref="Commit"/>/<see
        /// cref="Dispose"/> 均只生效一次——先 Commit 后 Dispose 时 Dispose 是 no-op（不会把已提交的
        /// 事务再回滚一次），重复 Commit 同理。
        /// <para>
        /// 第五轮外部审核相邻缺口根治：<paramref name="isRoot"/> 为 false 时（见
        /// <see cref="BeginBatch"/> 判断记录——加入外层已开启的事务），<see cref="Commit"/>/
        /// <see cref="Dispose"/> 都只标记本地 <see cref="_finished"/>、不调用
        /// <see cref="InventoryHost.CommitBatch"/>/<see cref="InventoryHost.RollbackBatch"/>：
        /// 真正的提交/回滚只能由最外层持有的那个 <c>isRoot: true</c> 实例触发，避免内层调用方
        /// （如 <c>RewardDispatcher.GrantItems</c>）在不知情外层事务存在的情况下提前把外层也一并
        /// 提交/回滚掉。</para>
        /// </summary>
        private sealed class Transaction : IInventoryTransaction
        {
            private readonly InventoryHost _host;
            private readonly bool _isRoot;
            private bool _finished;

            public Transaction(InventoryHost host, bool isRoot)
            {
                _host = host;
                _isRoot = isRoot;
            }

            public void Commit()
            {
                if (_finished)
                {
                    return;
                }

                _finished = true;
                if (_isRoot)
                {
                    _host.CommitBatch();
                }
            }

            public void Dispose()
            {
                if (_finished)
                {
                    return;
                }

                _finished = true;
                if (_isRoot)
                {
                    _host.RollbackBatch();
                }
            }
        }
    }
}

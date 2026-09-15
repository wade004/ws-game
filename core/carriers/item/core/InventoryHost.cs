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
        private readonly IDataRegistryView _registry;
        private readonly InventoryOptions _options;
        private readonly Dictionary<Id, DataRecord> _templates = new Dictionary<Id, DataRecord>();
        private readonly Dictionary<Id, List<ItemInstance>> _bags = new Dictionary<Id, List<ItemInstance>>();

        /// <summary>T-N2-9：<see cref="InventoryOptions.MaxSlotsStat"/> 非 null 时用于解析容量的
        /// 只读属性查询（<c>(unitId, statId) =&gt; 当前值</c>），见 <see cref="GetCapacity"/> 判断
        /// 记录。未提供该委托的构造重载下恒为 null。</summary>
        private readonly Func<Id, Id, double>? _statLookup;

        private long _nextInstanceSeq = 1;

        // R01 根治：批量事务状态（见 IInventoryTransaction 判断记录）。事务开启期间，AddItemCore/
        // RemoveItem 产生的通知事件改走 EnqueueEvent 缓存到 _txEvents，不直接送入 _bus；Commit 时按序
        // 补发，Rollback 时连同 _bags/_nextInstanceSeq 一起整体恢复到 BeginBatch 之前的快照。
        private bool _inTransaction;
        private Dictionary<Id, List<ItemInstance>>? _txSnapshot;
        private long _txSnapshotSeq;
        private readonly List<IEvent> _txEvents = new List<IEvent>();

        public InventoryHost(IDataRegistryView registry, IEventBus bus, InventoryOptions? options = null)
            : this(registry, bus, options, statLookup: null)
        {
        }

        /// <summary>
        /// T-N2-9（ADR-0034 决策 8；07 第 1.3 节修订段）新增重载：见硬性规则 5"ABI 只允许新增"——
        /// 既有 3 参构造函数原样保留（同 <see cref="EquipmentHost"/> 构造重载"新增重载而不是给既有
        /// 构造函数追加可选参数"一贯判断记录：给既有 <c>.ctor</c> 追加带默认值的新参数在物理 IL
        /// 签名层面仍是破坏性变更），新增本 4 参重载在末尾追加 <paramref name="statLookup"/>。
        /// <paramref name="statLookup"/> 供 <see cref="InventoryOptions.MaxSlotsStat"/> 非 null 时
        /// 解析容量使用（签名 <c>(unitId, statId) =&gt; 当前值</c>，不复用 <see
        /// cref="Core.Numbers.PowerSet.StatLookup"/> 具名委托类型——见该参数判断记录）；
        /// <see cref="InventoryOptions.MaxSlotsStat"/> 为 null（默认）时本参数不会被调用，传 null
        /// 与既有 3 参构造函数行为完全一致。
        /// <para>
        /// 判断记录（不复用 <c>Core.Numbers.PowerSet.StatLookup</c>，改用裸 <see
        /// cref="Func{Id, Id, TResult}"/>）：<c>core/carriers/item</c>（L3）依赖 <c>core/numbers/
        /// stat_block</c>（L1，<see cref="EquipmentHost"/> 已经直接引用 <see
        /// cref="Core.Numbers.StatBlock.IStatHost"/>）没有分层问题，但 <c>StatLookup</c> 定义在
        /// <c>core/numbers/power_set</c>——与背包容量是完全不相关的另一个 L1 模块，只是恰好委托
        /// 形状相同（<c>(Id, Id) =&gt; double</c>）。为它单独引入一条跨模块依赖只为借用一个类型名，
        /// 不如直接用 <see cref="Func{Id, Id, TResult}"/> 表达同一形状——两个模块各自独立解决同一个
        /// "属性来源上限，需要延迟到属性系统构造完成后才能提供真实查询"的构造期时序问题（见组装根
        /// <c>CarriersAssembly</c> 对应位置判断记录），互不引用，允许各自独立演进。
        /// </para>
        /// <para>
        /// 判断记录（构造期不要求 <see cref="InventoryOptions.MaxSlotsStat"/> 与本参数同时非
        /// null/null）：本类型不在构造期校验两者是否匹配一致——<see cref="InventoryOptions"/> 由
        /// 调用方在构造 <see cref="InventoryHost"/> 之前独立 new 出来，构造期做交叉校验需要额外的
        /// 前置条件耦合；改为在真正需要解析容量时（<see cref="GetCapacity"/>）才检查，MaxSlotsStat
        /// 非 null 但本参数为 null 时在那一刻抛 <see cref="InvalidOperationException"/>（见该方法
        /// 判断记录），与 <c>PowerHost</c> 对未注入 <c>StatLookup</c> 的既有处理时机一致（构造期不
        /// 报错，首次真正用到时才报错）。
        /// </para>
        /// </summary>
        public InventoryHost(IDataRegistryView registry, IEventBus bus, InventoryOptions? options, Func<Id, Id, double>? statLookup)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new InventoryOptions();
            _statLookup = statLookup;

            ReloadTemplates();

            // P2-05 关联根治（外部审计 audit-c9ff301-20260909，见 Core.Rules.Skill.SkillHost/
            // SkillDefCache.InvalidateAll 同一类判断记录）：_templates 此前只在构造期从 registry
            // 读取一次、永久常驻，开发期 DataHotReload 对 item.template 表 reload 后本类会继续用旧
            // 模板（stats/grants/weapon_profile 等），直到进程重建全新 host 才会看到新值。本类型
            // 本就持有 registry 引用（不像 QuestHost/DialogHost 那样只拿到预解析的 IEnumerable），
            // 可以直接内部订阅、自行重新查询，不需要装配根代劳。
            _bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, _ => ReloadTemplates());
        }

        /// <summary>
        /// 分阶段落地计划 T-N2-9（ADR-0034 决策 8；07 第 1.3 节修订段"背包容量来源"）：<see
        /// cref="IInventoryHost.GetCapacity"/> 显式实现——不依赖接口默认值（见该成员判断记录），本类
        /// 型是唯一需要精确容量语义的生产实现。<see cref="int.MaxValue"/> 表示不限，仅当来源为固定值
        /// 且 <see cref="InventoryOptions.MaxSlots"/> ≤ 0 时出现（历史语义不变）。
        /// <para>
        /// 判断记录（属性来源没有"不限"语义，向下取整并夹取到下限 0）：见 <see
        /// cref="InventoryOptions.MaxSlotsStat"/> 判断记录"下限口径——设计层裁定（2026-09-15）：
        /// 采纳"——固定值路径的
        /// "≤0 表示不限"是本类型历史既有行为，不能因为新增属性来源就悄悄改变；但属性来源解析出的是
        /// 一个真实的属性当前值，0 或负数（如被减益压低后）在这里就是"容量已经是 0"，不应退化为
        /// "不限"，否则"容量不足时拒绝新增"的契约意图会被这个 sentinel 悄悄绕过。
        /// </para>
        /// </summary>
        public int GetCapacity(Id unitId)
        {
            if (_options.MaxSlotsStat is Id statId)
            {
                if (_statLookup == null)
                {
                    throw new InvalidOperationException(
                        $"InventoryOptions.MaxSlotsStat 已设置为 \"{statId}\"，但构造 InventoryHost 时未提供属性查询委托（statLookup），无法解析容量");
                }

                var floored = (int)Math.Floor(_statLookup(unitId, statId));
                return floored < 0 ? 0 : floored;
            }

            return _options.MaxSlots <= 0 ? int.MaxValue : _options.MaxSlots;
        }

        private void ReloadTemplates()
        {
            _templates.Clear();
            foreach (var record in _registry.GetAll("item.template"))
            {
                _templates[record.GetId("id")] = record;
            }
        }

        /// <summary>
        /// T-N2-7（ADR-0032 决策 8；10 第 2.5 节修订段）：供 <see cref="ItemInstanceJson.FromJson"/>
        /// 在旧存档缺失 <c>quality</c> key 时兼容读取——按模板自身 <c>quality</c> 字段解析（见该方法
        /// 判断记录），同新建实例（<see cref="AddItemCore"/>）取缺省品质的同一口径，两处不重复各写
        /// 一份解析逻辑。<paramref name="templateId"/> 在当前已加载数据里找不到对应模板时返回一个
        /// "未解析"的 <see cref="Id"/>（<c>Value == null</c>）——同本类其余路径既有的"不强行校验
        /// 模板存在性"宽松度（<see cref="AddItemCore"/> 对未知模板会抛异常，但那是"新增物品"场景；
        /// 这里是"读一份可能引用了已被数据更新移除的旧模板 id 的历史存档"场景，不应让整次读档失败，
        /// 只是这一件物品的品质解析退化为未解析状态）。
        /// </summary>
        internal Id ResolveTemplateQuality(Id templateId) =>
            _templates.TryGetValue(templateId, out var template) ? template.GetId("quality") : default;

        public void RegisterUnit(Id unitId)
        {
            if (!_bags.ContainsKey(unitId))
            {
                _bags[unitId] = new List<ItemInstance>();
            }
        }

        public void UnregisterUnit(Id unitId) => _bags.Remove(unitId);

        public bool AddItem(Id unitId, Id templateId, int count) =>
            AddItemCore(unitId, templateId, count, null, null, out _);

        /// <summary>C05 根治：见 <see cref="IInventoryHost.TryAddItem"/> 判断记录——本类型支持
        /// <see cref="InventoryFullPolicy.Partial"/> 部分吞没语义，必须覆盖默认实现，如实返回
        /// <see cref="AddItemCore"/> 算出的实际落地量 <c>toAdd</c>，而不是把请求的 <paramref
        /// name="count"/> 原样当作实际量。</summary>
        public bool TryAddItem(Id unitId, Id templateId, int count, out int actualCount) =>
            AddItemCore(unitId, templateId, count, null, null, out actualCount);

        /// <summary>
        /// T-N2-8b（T-N2-8 已知缺口收口；ADR-0032 决策 7/8）：显式覆盖 <see
        /// cref="IInventoryHost.AddItem(Id, Id, int, Id?, IReadOnlyList{Id})"/>——不落回接口默认值
        /// （那会丢弃身份，见该成员判断记录），转发到 <see cref="AddItemCore"/> 同一份实现，只是带上
        /// 显式品质/词缀。</summary>
        public bool AddItem(Id unitId, Id templateId, int count, Id? qualityId, IReadOnlyList<Id>? affixes) =>
            AddItemCore(unitId, templateId, count, qualityId, affixes, out _);

        /// <summary>同上，带 <paramref name="actualCount"/> 版本。</summary>
        public bool TryAddItem(Id unitId, Id templateId, int count, Id? qualityId, IReadOnlyList<Id>? affixes, out int actualCount) =>
            AddItemCore(unitId, templateId, count, qualityId, affixes, out actualCount);

        /// <summary>
        /// T-N2-8b 改造：<paramref name="qualityId"/>/<paramref name="affixes"/> 均为 null 时是既有
        /// 3 参 <see cref="AddItem(Id, Id, int)"/>/<see cref="TryAddItem(Id, Id, int, out int)"/> 的
        /// 转发路径（行为逐字节不变，见下方"默认身份"判断记录）；非 null 时是新增的带身份重载路径。
        /// <para>
        /// 判断记录（"默认身份"——决定能否续填/合并既有堆叠的唯一标准）：解析出
        /// <c>resolvedQuality = qualityId ?? templateQuality</c>、<c>resolvedAffixes = affixes 非空
        /// 时取之，否则视为空</c>，当且仅当 <c>resolvedQuality == templateQuality &amp;&amp;
        /// resolvedAffixes.Count == 0</c> 时判定为"默认身份"——与调用方是否显式传了 <paramref
        /// name="qualityId"/>/<paramref name="affixes"/> 无关，只看解析结果是否与模板缺省一致（
        /// <c>Core.Gameplay.Loot.LootHost.ResolveDefaultOutcome</c> 一类"缺省照模板品质解析"的路径
        /// 会显式传入一个等于模板品质的 <see cref="Id"/>，不是 <c>null</c>，同样应判定为默认身份）。
        /// 只有默认身份的物品才会续填/合并既有的默认身份堆叠——带词缀或非模板品质的物品视为与模板
        /// 默认形态不同的身份，即使 <paramref name="templateId"/> 相同也不与任何既有堆叠合并（哪怕
        /// 两次加入的品质/词缀完全一样），总是新开格子；这是本任务范围内设计层已拍板的简化取舍（见
        /// <c>core/carriers/item/README.md</c> 判断记录）——同一品质同一词缀组合的战利品反复掉落时
        /// 不会自动堆叠成一条，代价是格子占用更多，换来的是不需要引入"词缀顺序无关的集合相等"这一更
        /// 复杂的堆叠判定。
        /// </para>
        /// </summary>
        private bool AddItemCore(Id unitId, Id templateId, int count, Id? qualityId, IReadOnlyList<Id>? affixes, out int actualCount)
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

            var templateQuality = template.GetId("quality");
            var resolvedQuality = qualityId ?? templateQuality;
            var resolvedAffixes = affixes != null && affixes.Count > 0 ? affixes : null;
            var isDefaultIdentity = resolvedQuality.Equals(templateQuality) && resolvedAffixes == null;

            var bag = GetOrCreateBag(unitId);

            // 判断记录 2：先算出容量够不够，再决定是否落地任何变化。非默认身份的物品不参与"既有堆叠
            // 续填"计算（见本方法判断记录"默认身份"），freeInExisting 恒为 0。
            var freeInExisting = 0;
            if (isDefaultIdentity)
            {
                foreach (var instance in bag)
                {
                    if (instance.TemplateId.Equals(templateId) && instance.Count < stackSize &&
                        instance.Quality.Equals(templateQuality) && instance.Affixes.Count == 0)
                    {
                        freeInExisting += stackSize - instance.Count;
                    }
                }
            }

            var overflow = Math.Max(0, count - freeInExisting);
            var newSlotsNeeded = overflow == 0 ? 0 : (overflow + stackSize - 1) / stackSize;
            // T-N2-9：容量统一改读 GetCapacity（固定值/属性来源二选一，见该方法判断记录），不再
            // 直接读 _options.MaxSlots——int.MaxValue 分支与既有"不限"语义等价保留。
            var capacity = GetCapacity(unitId);
            var availableSlots = capacity == int.MaxValue
                ? int.MaxValue
                : Math.Max(0, capacity - bag.Count);

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
            if (isDefaultIdentity)
            {
                for (var i = 0; i < bag.Count && remaining > 0; i++)
                {
                    var instance = bag[i];
                    if (!instance.TemplateId.Equals(templateId) || instance.Count >= stackSize ||
                        !instance.Quality.Equals(templateQuality) || instance.Affixes.Count != 0)
                    {
                        continue;
                    }

                    var space = stackSize - instance.Count;
                    var fill = Math.Min(space, remaining);
                    // T-N2-7：续填既有堆叠必须原样带上该实例已有的 Quality/Affixes（不能只传 4 参旧
                    // 构造函数——那会把品质/词缀身份悄悄重置为"未解析"/空，见 ItemInstance 类型顶部
                    // 判断记录）。
                    bag[i] = new ItemInstance(
                        instance.InstanceId, instance.TemplateId, instance.Count + fill,
                        instance.Quality, instance.Affixes, instance.Extra);
                    remaining -= fill;
                    touchedInstanceId = instance.InstanceId;
                    removals.Add((instance.InstanceId, fill));
                }
            }

            while (remaining > 0)
            {
                var take = Math.Min(stackSize, remaining);
                var instanceId = NextInstanceId();
                // T-N2-7/T-N2-8b：新开堆叠的品质/词缀取本次调用解析出的身份（默认身份下与模板自身
                // quality 字段、无词缀完全一致，行为同改造前）。
                bag.Add(new ItemInstance(instanceId, templateId, take, resolvedQuality, resolvedAffixes));
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
                    // T-N2-7：部分移除同样要保留品质/词缀身份（同上方续填堆叠判断记录）。
                    bag[i] = new ItemInstance(
                        instance.InstanceId, instance.TemplateId, instance.Count - count,
                        instance.Quality, instance.Affixes, instance.Extra);
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
            // T-N2-9：容量统一改读 GetCapacity，见 AddItemCore 同一类判断记录。
            var capacity = GetCapacity(unitId);
            if (capacity != int.MaxValue && bag.Count >= capacity)
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
            // T-N2-9：容量统一改读 GetCapacity，见 AddItemCore 同一类判断记录。
            var capacity = GetCapacity(unitId);
            if (capacity == int.MaxValue)
            {
                return true;
            }

            var count = _bags.TryGetValue(unitId, out var bag) ? bag.Count : 0;
            return count < capacity;
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

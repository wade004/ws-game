using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Summon
{
    /// <summary>
    /// <see cref="ISummonHost"/> 的默认实现（见 07_载体层_物品生物物件.md 第 4 节"召唤与宠物"）。
    /// 复用 <see cref="ICreatureFactory"/> 生成/移除召唤物实体（见
    /// <c>core/carriers/common/contracts/ICreatureFactory.cs</c> 顶部注释"ISummonHost 的实现均可
    /// 复用本接口生成生物实体，避免各自重复一遍装配流程"），本类只额外维护"召唤物 → 拥有者 +
    /// 剩余时长"这一份运行期索引与阵营继承。
    /// </summary>
    public sealed class SummonHost : ISummonHost
    {
        private sealed class SummonRecord
        {
            public Id OwnerId;
            public double? Remaining;
        }

        private readonly ICreatureFactory _factory;
        private readonly IWorldSim _world;
        private readonly IUnitAccess _units;
        private readonly IEventBus _bus;
        private readonly SummonOptions _options;

        private readonly Dictionary<string, SummonRecord> _summons = new Dictionary<string, SummonRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Id>> _byOwner = new Dictionary<string, List<Id>>(StringComparer.Ordinal);
        private readonly List<Id> _order = new List<Id>();

        public SummonHost(
            ICreatureFactory factory,
            IWorldSim world,
            IUnitAccess units,
            IEventBus bus,
            SummonOptions? options = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new SummonOptions();
        }

        /// <summary>内部驱动版本：供 <see cref="SummonTickHandler"/> 按插入顺序确定性遍历全部当前
        /// 存活的召唤物 id（惯例同 <c>core/rules/ai</c> <c>AiHost.RegisteredUnitIds</c>），不属于
        /// <see cref="ISummonHost"/> 契约签名。</summary>
        public IReadOnlyList<Id> ActiveSummonIds => _order;

        // -----------------------------------------------------------------
        // ISummonHost
        // -----------------------------------------------------------------

        public Id Summon(Id ownerId, Id creatureTemplateId, Vec2 position, double? duration = null)
        {
            var ownerEntity = _world.GetEntity(ownerId)
                ?? throw new ArgumentException($"拥有者 \"{ownerId}\" 不存在于世界模拟中", nameof(ownerId));

            var facing = _units.GetFacing(ownerId);
            var entityId = _factory.Spawn(creatureTemplateId, ownerEntity.MapId, position, facing, ownerId);

            if (_options.InheritOwnerFaction &&
                _world.GetEntity(entityId) is Core.Carriers.Unit.Unit summonUnit)
            {
                summonUnit.FactionId = _units.GetFaction(ownerId);
            }

            _summons[entityId.Value] = new SummonRecord { OwnerId = ownerId, Remaining = duration };
            _order.Add(entityId);

            if (!_byOwner.TryGetValue(ownerId.Value, out var list))
            {
                list = new List<Id>();
                _byOwner[ownerId.Value] = list;
            }
            list.Add(entityId);

            _bus.Enqueue(new SummonCreatedEvent(entityId, ownerId));

            return entityId;
        }

        /// <summary>主动取消（见 <see cref="ISummonHost.Dismiss"/> 契约签名不携带 reason 参数），
        /// 内部固定按 <c>"dismissed"</c> 归类转给 <see cref="Dismiss(Id, string)"/>。</summary>
        public void Dismiss(Id summonId) => Dismiss(summonId, "dismissed");

        /// <summary>
        /// 内部驱动版本：带具体销毁原因的 Dismiss。<see cref="ISummonHost.Dismiss(Id)"/> 契约本身
        /// 不携带 reason 参数，但 <c>ICreatureFactory.Despawn</c> 的 <c>reason</c> 是自由字符串分类
        /// （见该接口判断记录，调用方不止刷新表一处），<see cref="SummonTickHandler"/> 需要区分
        /// "到期"/"拥有者丢失"/"主动取消"三种分类各自的 <c>creature.despawned</c> reason——本方法是
        /// 任务书拍板要求的扩展入口，惯例同 <c>core/rules/ai</c> 的 <c>AiHost</c> 在
        /// <c>Core.Rules.Common.IAiHost</c> 契约之外追加"内部驱动版本"方法的做法。
        /// </summary>
        public void Dismiss(Id summonId, string reason)
        {
            if (reason == null) throw new ArgumentNullException(nameof(reason));
            if (!_summons.TryGetValue(summonId.Value, out var record))
            {
                throw new ArgumentException($"\"{summonId}\" 不是已登记的召唤物", nameof(summonId));
            }

            _factory.Despawn(summonId, reason);

            _summons.Remove(summonId.Value);
            _order.Remove(summonId);

            if (_byOwner.TryGetValue(record.OwnerId.Value, out var list))
            {
                list.Remove(summonId);
                if (list.Count == 0)
                {
                    _byOwner.Remove(record.OwnerId.Value);
                }
            }

            _bus.Enqueue(new SummonExpiredEvent(summonId, record.OwnerId));
        }

        public Id? GetOwner(Id summonId) =>
            _summons.TryGetValue(summonId.Value, out var record) ? record.OwnerId : (Id?)null;

        /// <summary>判断记录（"排序稳定"）：按 <see cref="Summon"/> 调用顺序（插入顺序）返回，
        /// 多次调用对同一批召唤物返回同样的顺序——07 第 4 节补充信息表"测试方式"未要求按
        /// <see cref="Id"/> 字典序排序，插入顺序已经满足"稳定"（确定性、可重复）这一验收点。</summary>
        public IReadOnlyList<Id> GetSummons(Id ownerId) =>
            _byOwner.TryGetValue(ownerId.Value, out var list) ? list.ToArray() : Array.Empty<Id>();

        // -----------------------------------------------------------------
        // 内部驱动版本：供 SummonTickHandler 推进到期计时
        // -----------------------------------------------------------------

        /// <summary>按 <paramref name="dt"/> 推进该召唤物的剩余时长；<see cref="SummonRecord.Remaining"/>
        /// 为 null（"跟随 owner 直到主动取消/死亡"，见 07 第 4 节）时恒返回 false、不推进任何计时。
        /// 返回 true 表示本次推进后剩余时长已 &lt;= 0（到期）。</summary>
        public bool AdvanceAndCheckExpired(Id summonId, double dt)
        {
            if (!_summons.TryGetValue(summonId.Value, out var record) || !record.Remaining.HasValue)
            {
                return false;
            }

            record.Remaining -= dt;
            return record.Remaining.Value <= 0;
        }
    }
}

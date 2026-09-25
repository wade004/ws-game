using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="IUnitAccess"/> 的真实实现（05/06 文档要求"L3 载体层把 Unit 运行期实例接入"，见
    /// <c>Core.Rules.Common.IUnitAccess</c> 顶部"实现方：L3 载体层"）——基于 <see cref="IWorldSim"/>
    /// 里的 <see cref="Unit"/> 实体，与 <c>core/rules/tests/Integration/WorldUnitAccess.cs</c>（该集成
    /// 测试专用的 <c>TestUnit</c> 版本）同构，本类型是它的正式对应物。
    /// </summary>
    public sealed class WorldUnitAccess : IUnitAccess
    {
        private readonly IWorldSim _world;
        private readonly ISpatialQuery? _spatial;
        private readonly HealthFractionSetter? _healthFractionSetter;
        private readonly IEventBus? _bus;

        /// <summary>
        /// <paramref name="spatial"/> 可选：提供时 <see cref="SetPosition"/> 在写入实体位置后经
        /// <see cref="ISpatialQuery.UpdatePosition"/> 同步新位置（ADR-0016 决策 7：单位移动时调用
        /// UpdatePosition 同步空间索引），未提供时只写位置，不做任何空间索引同步。首次登记
        /// （<see cref="ISpatialQuery.Register"/>）不在本类型职责内——单位创建时机由
        /// <c>core/carriers/assembly/EntitySpatialSyncHost</c> 订阅 <c>entity.created</c> 统一处理
        /// （创建、移动、销毁三个时机分属不同类型，见该类型判断记录）。
        /// <paramref name="healthFractionSetter"/> 可选（W1 收边补齐，见 <see cref="Revive"/>）：
        /// 未注入时 <see cref="Revive"/> 只恢复存活状态与坐标，不触碰生命值。
        /// <para>
        /// ADR-0088：本重载不注入 <see cref="IEventBus"/>——<see cref="SetFaction"/> 需要发布
        /// <c>unit.faction_changed</c>，未注入总线时调用它会抛异常（见该方法判断记录）。生产装配
        /// 请改用下方注入 <see cref="IEventBus"/> 的重载；本重载保留给不需要改阵营能力的既有调用方
        /// （测试假实现等），行为与本次改动之前完全一致。
        /// </para>
        /// </summary>
        public WorldUnitAccess(IWorldSim world, ISpatialQuery? spatial = null, HealthFractionSetter? healthFractionSetter = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatial = spatial;
            _healthFractionSetter = healthFractionSetter;
            _bus = null;
        }

        /// <summary>ADR-0088（消费方第三十三批反馈2根治）：新增重载，注入 <see cref="IEventBus"/>
        /// 使 <see cref="SetFaction"/> 能发布 <c>unit.faction_changed</c>（见该方法判断记录）。
        /// 生产装配（<c>CarriersAssembly</c>）已改为使用本重载。</summary>
        public WorldUnitAccess(IWorldSim world, IEventBus bus, ISpatialQuery? spatial = null, HealthFractionSetter? healthFractionSetter = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _spatial = spatial;
            _healthFractionSetter = healthFractionSetter;
        }

        public bool Exists(Id unitId) => _world.GetEntity(unitId) is Unit;

        public IReadOnlyList<Id> AllUnits =>
            _world.QueryEntities(new EntityFilter(predicate: e => e is Unit))
                .Select(e => e.EntityId)
                .ToList();

        public Vec2 GetPosition(Id unitId) => Require(unitId).Position;

        /// <summary>写入位置后，若注入了 <see cref="ISpatialQuery"/>，同步更新空间索引里的位置
        /// （见构造函数判断记录）。</summary>
        public void SetPosition(Id unitId, Vec2 position)
        {
            Require(unitId).Position = position;
            _spatial?.UpdatePosition(unitId, position);
        }

        public Id GetFaction(Id unitId) => Require(unitId).FactionId;

        public int GetLevel(Id unitId) => Require(unitId).Level;

        /// <summary>
        /// CORE-170-02 根治（architecture/落地计划/audit-8160178-20260908，P2）：<see
        /// cref="Core.Numbers.Progression.LevelSync"/> 委托的真正实现——把 <see
        /// cref="Core.Numbers.Progression.ProgressionHost"/> 内部权威等级写回 <see cref="Unit.Level"/>
        /// 实体字段，使 <see cref="GetLevel"/> 此后读到的值与 <c>ProgressionHost.GetLevel</c> 恒一致
        /// （见该委托类型判断记录）。不在 <see cref="IUnitAccess"/> 接口上（同 <see cref="Revive"/>
        /// 判断记录同一惯例：本方法只供组合根装配期的委托闭包调用，不是 skill/combat/targeting/ai
        /// 四个模块经 <see cref="IUnitAccess"/> 契约会用到的操作，不必扩大公开契约）。
        /// <para>
        /// <paramref name="unitId"/> 在 <see cref="IWorldSim"/> 中不存在（极早期装配阶段的孤立调用，
        /// 正常生产链路的调用时机——<c>ProgressionHost.RegisterUnit</c>/<c>AddXp</c>/<c>RestoreState</c>
        /// 全部发生在对应单位实体已经 <see cref="IWorldSim.AddEntity"/> 之后——不会出现这种情况）时
        /// 安全 no-op，不抛异常，与 <see cref="SetAlive"/>/<see cref="SetPosition"/> 遇到未知单位会
        /// 抛出的 <see cref="Require"/> 惯例不同——本方法的调用方是 <see
        /// cref="Core.Numbers.Progression.LevelSync"/> 委托闭包，其调用时机由
        /// <c>core/numbers/progression</c>（L1）单方面决定，L1 模块本就不知道、也不该被要求先确认
        /// L3 世界模拟里是否存在这个单位——让委托闭包自己吞掉这种边界情况，比让 L1 承担"调用一个
        /// 跨层委托可能抛异常"的负担更符合本仓库既有的委托注入分层惯例。
        /// </para>
        /// </summary>
        public void SetLevel(Id unitId, int level)
        {
            if (_world.GetEntity(unitId) is Unit unit)
            {
                unit.Level = level;
            }
        }

        /// <summary>
        /// ADR-0088（消费方第三十三批反馈2阻塞项根治）：运行期改变单位阵营的框架入口——此前框架
        /// 未提供此类入口，消费方只能绕过直接写 <see cref="Unit.FactionId"/> 字段，导致仇恨表/AI
        /// 无法感知阵营变化（仇恨表不清、AI 继续追一个已经变友方的目标）。写入新阵营后发布
        /// <see cref="UnitFactionChangedEvent"/>（旧值与新值相同不发，同
        /// <see cref="Core.Numbers.Faction.FactionMatrix.SetReaction"/> 判断记录同一惯例——阵营变化
        /// 是一次性状态跃迁，不是 tick 内的高频事件，故用 <see cref="IEventBus.PublishImmediate"/>
        /// 同步立即派发，使订阅方（<see cref="Core.Rules.Combat.ThreatTable"/>）能在本次调用返回前
        /// 就完成仇恨表清理，不依赖调用方之后记得 <c>DispatchPending</c>）。
        /// <para>
        /// 不在 <see cref="IUnitAccess"/> 接口上（同 <see cref="SetLevel"/>/<see cref="Revive"/>
        /// 判断记录同一惯例：这不是 skill/combat/targeting/ai 四个模块经 <see cref="IUnitAccess"/>
        /// 契约会用到的操作，而是供消费方游戏逻辑——如变身/魅惑一类效果——直接调用的窄契约，不必
        /// 扩大公开契约、也不要求全部 <see cref="IUnitAccess"/> 假实现跟着实现）。要求构造时已注入
        /// <see cref="IEventBus"/>（见上方新增的双参数重载）；未注入时抛
        /// <see cref="InvalidOperationException"/>，不静默跳过事件发布（运行时路径不静默降级）。
        /// </para>
        /// </summary>
        public void SetFaction(Id unitId, Id newFactionId)
        {
            if (_bus == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(WorldUnitAccess)}.{nameof(SetFaction)} 需要构造时注入 {nameof(IEventBus)}" +
                    "（见带 bus 参数的构造函数重载）");
            }

            var unit = Require(unitId);
            var oldFactionId = unit.FactionId;
            if (oldFactionId.Value == newFactionId.Value)
            {
                return;
            }

            unit.FactionId = newFactionId;
            _bus.PublishImmediate(new UnitFactionChangedEvent(unitId, oldFactionId, newFactionId));
        }

        public double GetFacing(Id unitId) => Require(unitId).Facing;

        public bool IsAlive(Id unitId) => Require(unitId).Alive;

        /// <summary>只同步 <see cref="Unit.Alive"/> 字段，不销毁实体、不改变
        /// <see cref="Entity.Lifecycle"/>（死亡是逻辑状态，不是生命周期状态——尸体按 05 第 2 节对照
        /// 表"不单列 Corpse 类型"，继续以 <c>alive = false</c> 的形态存在于世界模拟中，直到刷新表/
        /// 复活策略另行处理，见 06 死亡与复活）。<see cref="Entity.Lifecycle"/> 本就是
        /// <c>internal set</c>（仅 <c>Core.Foundation.SimLoop</c> 程序集可写），本方法不持有、也无法
        /// 触碰该字段，天然满足"不销毁实体"的要求。</summary>
        public void SetAlive(Id unitId, bool alive) => Require(unitId).Alive = alive;

        public Id? GetTemplateId(Id unitId) => Require(unitId).TemplateId;

        public IReadOnlyList<Id> GetTags(Id unitId) => Require(unitId).Tags;

        /// <summary>覆盖 <see cref="IUnitAccess.GetMapId"/> 的默认接口实现（默认返回 null）：本类型
        /// 基于真实的 <see cref="Entity.MapId"/>，能够返回真实值（同
        /// <c>core/rules/tests/Integration/WorldUnitAccess.GetMapId</c> 判断记录）。</summary>
        public Id? GetMapId(Id unitId) => Require(unitId).MapId;

        /// <summary>
        /// T-N1-6：覆盖 <see cref="IUnitAccess.GetSourceKind"/> 的默认接口实现（默认返回
        /// <see cref="SourceKind.Unknown"/>）——复用既有"单位是玩家还是生物"的判定依据
        /// <see cref="Entity.Kind"/>（<see cref="PlayerUnit.Kind"/> 恒为 <see cref="EntityKinds.Player"/>、
        /// <see cref="CreatureUnit.Kind"/> 恒为 <see cref="EntityKinds.Creature"/>，见
        /// <c>core/carriers/unit/tests/PlayerAndCreatureUnitTests.cs</c>），不新造标记字段。
        /// <para>
        /// 判断记录（不用 <see cref="Require"/>，<paramref name="unitId"/> 不存在时返回
        /// <see cref="SourceKind.Unknown"/> 而不是抛异常，与本类型其余访问器"未知单位一律抛出"的
        /// 既有惯例不同）：本方法的实际调用方（<c>CastPipeline.ExecuteEffectsOnly</c>/
        /// <c>AuraHost.FirePeriodic</c>/<c>ProjectileHost.ApplyOnHitEffects</c>）在构造
        /// <see cref="EffectContext"/> 时调用它查询来源，而来源单位在光环仍生效期间被销毁、周期效果
        /// 仍会继续结算是既有支持的边界情形（见 <c>EffectDispatcher.ApplyDamageOrHeal</c> 判断记录
        /// C02"来源销毁后光环仍会继续按周期结算……缩放贡献按 0 处理，效果本身继续正常结算/落地，
        /// 不中断周期 tick 循环、不抛异常"）——若本方法对已销毁来源抛异常，会让这条既有安全退化路径
        /// 在本次改动后反而崩溃，与"本任务只透传字段，不改变既有行为"的要求相悖；来源不存在时按
        /// <see cref="SourceKind.Unknown"/> 处理与 C02 判断记录"缩放贡献按 0 处理"同一条"确定安全"
        /// 退化路径，二者互不冲突。
        /// </para>
        /// </summary>
        public SourceKind GetSourceKind(Id unitId) =>
            _world.GetEntity(unitId) is Unit unit
                ? unit.Kind switch
                {
                    EntityKinds.Player => SourceKind.Player,
                    EntityKinds.Creature => SourceKind.Creature,
                    _ => SourceKind.Unknown,
                }
                : SourceKind.Unknown;

        /// <summary>
        /// W1 收边补齐（拍板 3 前置：死亡复活链路，见 <c>architecture/adr/</c> DECISIONS 拍板 3
        /// "DeathPolicyHost：respawn_point 策略……置于当前地图 spawn_points[0]、恢复生命并发
        /// unit.respawned"）：复活单位的窄契约——恢复 <see cref="Unit.Alive"/>、写入复活坐标
        /// <paramref name="position"/>（与 <see cref="SetPosition"/> 同样经 <see cref="ISpatialQuery"/>
        /// 同步空间索引），并（若已注入 <see cref="HealthFractionSetter"/>）按
        /// <paramref name="healthFraction"/>（<c>[0,1]</c>，越界自动夹取）恢复生命值。
        /// <para>
        /// 本方法只是一个可供调用的窄契约，<b>不</b>自行判断死亡复活策略、<b>不</b>自行订阅
        /// <c>unit.died</c>、<b>不</b>自行发布 <c>unit.respawned</c>——这些属于死亡复活的执行主体
        /// （L4 <c>core/gameplay/death.DeathPolicyHost</c>，见 06 第 4.6 节、DECISIONS 拍板 3）的
        /// 职责，本模块（L3 载体层）不持有 <c>ITurnScheduler</c>/存档系统/主菜单跳转等 L4 依赖，
        /// 无法承担整条复活流程，只提供它需要的"改写单位运行期状态"这一步。
        /// </para>
        /// </summary>
        public void Revive(Id unitId, Vec2 position, double healthFraction)
        {
            var unit = Require(unitId);
            unit.Alive = true;
            unit.Position = position;
            _spatial?.UpdatePosition(unitId, position);
            _healthFractionSetter?.Invoke(unitId, Math.Clamp(healthFraction, 0.0, 1.0));
        }

        private Unit Require(Id unitId)
        {
            if (_world.GetEntity(unitId) is Unit unit)
            {
                return unit;
            }

            throw new InvalidOperationException($"WorldUnitAccess: 单位 \"{unitId}\" 不存在或不是 Unit");
        }
    }
}

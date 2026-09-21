using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 一次 <see cref="IInteractionTargetRegistry.TryFindNearest"/> 查询命中的目标所属类别——见该
    /// 接口类型注释"分类只依赖 Entity.Kind"判断记录：三个取值分别对应
    /// <see cref="Core.Foundation.SimLoop.EntityKinds.Gobj"/>/<see
    /// cref="Core.Foundation.SimLoop.EntityKinds.Creature"/>/<see
    /// cref="Core.Foundation.SimLoop.EntityKinds.Loot"/>。
    /// </summary>
    public enum InteractionTargetKind
    {
        /// <summary>场景物件（<c>core/carriers/gobj</c>）。</summary>
        GameObject,

        /// <summary>生物（<c>core/carriers/creature</c>）。</summary>
        Creature,

        /// <summary>地面掉落物（<c>core/gameplay/loot</c>）。</summary>
        Loot,
    }

    /// <summary>一次 <see cref="IInteractionTargetRegistry.TryFindNearest"/> 命中结果。</summary>
    public readonly struct InteractionTarget
    {
        /// <summary>目标的运行期实体 id（<c>gobj_instance_id</c>/<c>creature_instance_id</c>/
        /// <c>loot_instance_id</c>——键名对应 <see cref="Kind"/>，见 <c>interact</c> 意图三条原生
        /// 分流各自的 Args 形状约定）。</summary>
        public Id EntityId { get; }

        public InteractionTargetKind Kind { get; }

        /// <summary>查询发起者与本目标的距离（世界平面坐标，见 05 第 3 节）。</summary>
        public double Distance { get; }

        public InteractionTarget(Id entityId, InteractionTargetKind kind, double distance)
        {
            EntityId = entityId;
            Kind = kind;
            Distance = distance;
        }
    }

    /// <summary>
    /// ADR-0062（消费方反馈第五批第 1 条续）：统一的"最近可交互目标"查询——场景物件
    /// （<c>core/carriers/gobj</c>）、生物（<c>core/carriers/creature</c>）、地面掉落物
    /// （<c>core/gameplay/loot</c>）三类目标共用同一个查询入口，接入方按 F 键只需走一次本查询 +
    /// 发一次 <c>interact</c> 意图，不需要像此前那样自己遍历 <c>LootHost.ActiveLootIds</c> 算最近
    /// 距离作为回退（反馈原文同时提到"用 <c>IInteractionTargetRegistry.TryFindNearest</c> 找最近
    /// NPC/物件目标"——核实后本仓库其实从未真正提供过这个类型，本接口据反馈原文命名补上这个缺口）。
    /// <para>
    /// 判断记录（放在 <c>core/carriers/common</c>，不新开模块，不依赖 L4）：目标分类只依赖
    /// <see cref="Core.Foundation.SimLoop.Entity.Kind"/>（<see cref="Core.Foundation.SimLoop.EntityKinds"/>
    /// 三个既有常量 Gobj/Creature/Loot），不需要引用 <c>core/gameplay/loot</c> 等 L4 模块的具体类型
    /// （<c>DroppedLootEntity</c> 等）——本接口与其默认实现因此可以完全留在 L3
    /// （<c>core/carriers</c>），不违反本目录 README"L3 不依赖 L4"的既有边界。
    /// </para>
    /// <para>
    /// 判断记录（不另开登记表，直接查 <see cref="Core.Foundation.SimLoop.IWorldSim.QueryEntities"/>
    /// 现场结果）：地面掉落物生成/被拾取/过期时，<c>LootHost</c> 已经在经
    /// <see cref="Core.Foundation.SimLoop.IWorldSim.AddEntity"/>/<see
    /// cref="Core.Foundation.SimLoop.IWorldSim.MarkForDestruction"/> 维护它在世界模拟里的存在性
    /// （gobj/creature 同理）；本接口的默认实现（<c>Core.Carriers.Assembly.InteractionTargetRegistry</c>）
    /// 每次查询都直接读 <c>IWorldSim</c> 当前状态，不另外维护一份需要手动登记/注销、可能与世界模拟
    /// 状态脱节的登记表——不会出现"掉落物已被拾取，仍被最近目标查询命中"的悬挂引用。
    /// </para>
    /// </summary>
    public interface IInteractionTargetRegistry
    {
        /// <summary>在 <paramref name="unitId"/> 当前所在地图内查找距离最近的可交互目标（见
        /// <see cref="InteractionTarget"/>）。<paramref name="maxRange"/> 为 <c>null</c> 表示不限制
        /// 距离，否则超出该距离的候选不参与比较。<paramref name="unitId"/> 不存在、所在地图未知、或
        /// 地图内没有满足条件的候选时返回 <c>false</c>（<paramref name="target"/> 为默认值）。多个
        /// 候选距离相同时取 <c>EntityId</c> 序数最小者（<see cref="Core.Foundation.SimLoop.IWorldSim.QueryEntities"/>
        /// 既有的确定性排序，不依赖字典枚举顺序）。</summary>
        bool TryFindNearest(Id unitId, double? maxRange, out InteractionTarget target);
    }
}

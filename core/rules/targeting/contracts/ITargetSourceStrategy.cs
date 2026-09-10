using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
// 类型别名判断记录（同 DataRecord.cs "类型别名判断记录"惯例，见 TargetChainDef.cs 同名注释）：
// 本类需要一个名为 Shape 的属性，与 Core.Foundation.EngineAdapter.Shape 类型同名；类内其余位置
// 一律用别名 EngineShape 引用该类型。
using EngineShape = Core.Foundation.EngineAdapter.Shape;

namespace Core.Rules.Targeting
{
    /// <summary>
    /// 一次目标来源求值可用的只读上下文（见 06_规则层_属性技能战斗AI.md 第 5 节
    /// <c>TargetChainDef.source</c>、01_分层与依赖.md 第 8 节第 3 种合法调用方式"策略注入回调"）。
    /// <para>
    /// <see cref="Shape"/> 判断记录：架构记法给的字段是 <c>Shape?</c>（链未声明 <c>shape</c> 时
    /// 允许为空），但 <see cref="Core.Rules.Targeting.TargetHost"/> 在构造本上下文前已经把
    /// "链未声明 shape" 的情形回退为以施法者当前坐标为圆心、半径
    /// <see cref="TargetingOptions.DefaultRadius"/> 的 circle（见任务书"无 shape 用 circle
    /// radius=Options.DefaultRadius"），因此本字段在策略实现里实际总是有值；保留 <c>Shape?</c>
    /// 类型只是为了忠实于任务书给出的字段签名，不代表策略需要自行处理"确实为空"的分支。
    /// </para>
    /// </summary>
    public sealed class TargetContext
    {
        /// <summary>发起本次目标解析的施法者/单位。</summary>
        public Id CasterId { get; }

        /// <summary>调用方显式传入的"当前目标"（见 <see cref="Core.Rules.Common.ITargetHost.Resolve(Id, Id, Id?)"/>），
        /// 供 <c>current_target</c> 一类来源策略使用；调用方未传入时为 null。</summary>
        public Id? CurrentTarget { get; }

        /// <summary>已按施法者当前坐标/朝向重新锚定过的范围形状（见本类型上方判断记录）。</summary>
        public EngineShape? Shape { get; }

        /// <summary>本次解析的锚点坐标，通常等于施法者当前坐标（见 <c>ISkillHost.FindUnits</c> 的
        /// <c>origin</c> 参数同一惯例，见 common/README.md 判断记录 2）。</summary>
        public Vec2 Origin { get; }

        public IUnitAccess Units { get; }

        public ISpatialQuery Spatial { get; }

        public IFactionMatrix Factions { get; }

        public IPowerHost Powers { get; }

        /// <summary>仇恨表；调用方未提供时为 null，<c>threat_top</c> 等依赖仇恨表的来源在此情形下
        /// 按"无候选"处理（见 <see cref="Core.Rules.Targeting.TargetingOptions"/>）。</summary>
        public IThreatTable? Threat { get; }

        /// <summary>
        /// ADR-0013 决策 6、04 第 3.1 节 <c>grid_snap</c> 落地：非 <c>null</c> 时，依赖形状查询的
        /// 内置策略（<c>nearest_in_shape</c>/<c>all_in_shape</c>，见
        /// <c>Core.Rules.Targeting.BuiltinTargetStrategies</c>）改用
        /// <see cref="Core.Foundation.EngineAdapter.GridSnapShapeQuery.QueryShapeAtCellCenters"/>——
        /// 候选是否落在范围内，按其所属格子中心点判定，而不是按候选的原始坐标——而不是直接调用
        /// <see cref="Spatial"/> 的 <c>QueryShape</c>。<c>null</c>（<see cref="TargetHost"/> 未装配
        /// 离散步/未声明 <c>grid_snap</c> 时的默认值，见 <see cref="Core.Rules.Targeting.TargetingOptions"/>
        /// 判断记录）表示不启用，行为与格子吸附落地之前逐字节一致。</summary>
        public IGridSnapPolicy? GridSnapPolicy { get; }

        /// <summary><see cref="GridSnapPolicy"/> 非 null 时对应的格子尺寸（<c>found.time_model.
        /// grid_snap.cell_size</c>）；<see cref="GridSnapPolicy"/> 为 null 时本字段无意义。</summary>
        public double? GridSnapCellSize { get; }

        public TargetContext(
            Id casterId,
            Id? currentTarget,
            EngineShape? shape,
            Vec2 origin,
            IUnitAccess units,
            ISpatialQuery spatial,
            IFactionMatrix factions,
            IPowerHost powers,
            IThreatTable? threat)
            : this(casterId, currentTarget, shape, origin, units, spatial, factions, powers, threat,
                gridSnapPolicy: null, gridSnapCellSize: null)
        {
        }

        /// <summary>
        /// ADR-0013 决策 6 补齐（格子吸附）新增的重载——不修改上面那个既有公开构造函数的物理参数
        /// 列表（同 <c>Core.Foundation.DataRegistry.FieldSchema</c> 类型判断记录"新能力一律用新增
        /// 成员/新构造重载承载，不再改动既有构造函数的参数列表"，ABI 兼容惯例）。
        /// </summary>
        public TargetContext(
            Id casterId,
            Id? currentTarget,
            EngineShape? shape,
            Vec2 origin,
            IUnitAccess units,
            ISpatialQuery spatial,
            IFactionMatrix factions,
            IPowerHost powers,
            IThreatTable? threat,
            IGridSnapPolicy? gridSnapPolicy,
            double? gridSnapCellSize)
        {
            CasterId = casterId;
            CurrentTarget = currentTarget;
            Shape = shape;
            Origin = origin;
            Units = units ?? throw new ArgumentNullException(nameof(units));
            Spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            Factions = factions ?? throw new ArgumentNullException(nameof(factions));
            Powers = powers ?? throw new ArgumentNullException(nameof(powers));
            Threat = threat;
            GridSnapPolicy = gridSnapPolicy;
            GridSnapCellSize = gridSnapCellSize;
        }
    }

    /// <summary>
    /// 目标来源策略：目标选择链 <c>source</c> 字段对应的实现（见 06 第 5 节
    /// <c>TargetChainDef.source</c>）。架构固定的是"来源枚举 + 过滤 + 排序 + 回退"这一组合结构，
    /// 具体来源实现（无论内置还是游戏层自定义）一律经 <see cref="TargetStrategyRegistry"/> 注入，
    /// <see cref="Core.Rules.Targeting.TargetHost"/> 不识别任何具体策略名字面量（见 00_架构总则.md
    /// 第 4 节原则 10"策略注入"、01 第 8 节第 3 种合法调用方式）。
    /// </summary>
    public interface ITargetSourceStrategy
    {
        /// <summary><c>target.chain_def.source</c> 字段引用的策略名，须在
        /// <see cref="TargetStrategyRegistry"/> 中唯一。</summary>
        string Name { get; }

        /// <summary>
        /// 收集本策略的候选目标，顺序即该策略认为的"自然顺序"（如 <c>nearest_in_shape</c> 按
        /// 距离升序、<c>party_lowest_hp_pct</c> 按生命值百分比升序，二者都以 Id 序数作为同值时的
        /// 决胜顺序保证确定性）；链未显式声明 <c>sort_by</c> 时，<see cref="TargetHost"/> 原样保留
        /// 这个顺序，只做过滤与截断，不额外排序（见 targeting/README.md 判断记录）。
        /// </summary>
        IReadOnlyList<Id> Collect(TargetContext ctx);
    }
}

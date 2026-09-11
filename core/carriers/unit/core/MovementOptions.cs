using System;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="MovementTickHandler"/> 在一次 <c>BeginPathTo</c> 建路失败、或某单位持有路径时
    /// <see cref="Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion"/> 重算失败后的
    /// 处理策略（见 05 第 6 节勘误、<see cref="MovementHost.OnMoveFailedDetailed"/> 判断记录）。
    /// </summary>
    public enum PathFailurePolicy
    {
        /// <summary>保留旧路径继续推进（默认，向后兼容——本任务之前唯一的行为：寻路失败只回调
        /// <see cref="MovementHost.OnMoveFailed"/>，不改变 <see cref="MovementState"/>）。</summary>
        KeepOldPath,

        /// <summary>清空路径、把状态收回 <see cref="MoveMode.Idle"/>，并触发
        /// <see cref="MovementHost.OnMoveStopped"/>（<see cref="MoveStopReason.PathFailed"/>）。</summary>
        Stop,
    }

    /// <summary>
    /// <see cref="MovementTickHandler"/> 在某单位持有路径、且
    /// <see cref="Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion"/> 相对建路时记录的
    /// <see cref="MovementState.NavVersion"/> 发生变化（且非 0，见该方法判断记录）时采用的处理策略
    /// （见 05 第 6 节勘误）。
    /// </summary>
    public enum BlockingChangePolicy
    {
        /// <summary>直接对剩余目标重新调用一次寻路，用新路径整体替换（默认）；重算失败按
        /// <see cref="PathFailurePolicy"/> 处理。</summary>
        Replan,

        /// <summary>先对剩余路段逐段做视线判定，判定命中受阻才重算；不受阻只更新已验证的版本号，
        /// 路径本身不变（比 <see cref="Replan"/> 更省寻路开销）。重算失败按
        /// <see cref="PathFailurePolicy"/> 处理。</summary>
        Revalidate,

        /// <summary>直接停止：清空路径、把状态收回 <see cref="MoveMode.Idle"/>，触发
        /// <see cref="MovementHost.OnMoveStopped"/>（<see cref="MoveStopReason.BlockingChanged"/>），
        /// 不尝试重算。</summary>
        Stop,

        /// <summary>忽略版本变化，照常沿既有路径推进（口味调整：游戏层认为不值得为动态阻挡自动
        /// 重验，例如阻挡矩形只用于视觉表现、不影响该游戏的移动判定）。</summary>
        Ignore,
    }

    /// <summary>
    /// <c>MovementTickHandler</c> 的口味配置项（见任务书拍板：速度属性 id 与缺省速度、到达判定
    /// 阈值，均可按具体游戏口味调整，不属于架构层面的固定语义，惯例同 <c>core/rules/ai</c> 的
    /// <c>AiOptions</c>）。
    /// </summary>
    public sealed class MovementOptions
    {
        /// <summary>速度来源属性 id（见 05 第 6.2 节"速度来源：来自该 Unit 的 StatBlock 中的移动速度
        /// 属性"），默认 <c>stat.move_speed</c>。</summary>
        public Id MoveSpeedStat { get; set; } = new Id("stat.move_speed");

        /// <summary><see cref="MoveSpeedStat"/> 缺失（<c>IStatHost.GetStat</c> 返回非正值，判断记录见
        /// <c>MovementTickHandler.ResolveSpeed</c>）时使用的缺省速度，默认 4（同
        /// <c>core/rules/ai</c> <c>AiOptions.MoveSpeed</c> 默认值同量级）。</summary>
        public double DefaultSpeed { get; set; } = 4.0;

        /// <summary>"已到达"路点判定的距离阈值，默认 0.01（同 <c>MoveIntentHandler</c> 一类最小实现
        /// 惯例，取一个远小于典型移动速度×步长的量级，避免因浮点误差导致永远差一点点到不了）。</summary>
        public double ArrivalEpsilon { get; set; } = 0.01;

        /// <summary>
        /// 离散模式（ADR-0013）下每回合移动预算的距离换算：<c>movement_budget_rule: distance</c>
        /// 时，本回合可移动距离 = 该单位速度属性 × 本值（见 06_规则层_属性技能战斗AI.md"每回合
        /// 移动预算"、03 第 4.2 节步骤 4"离散步下按该行动者的每回合移动预算结算位移，而非按连续
        /// 时间的速度积分"）。判断记录：把"每回合等效秒数"设为可配置项而不是固定距离常量，复用
        /// 现有"速度属性 × 时间"的计算路径（<see cref="MovementTickHandler"/> 内部不需要为离散模式
        /// 另写一套位移公式），默认 1.0（一回合 ≈ 一秒的移动量，具体数值由游戏层按口味调整）。
        /// <c>movement_budget_rule: action_points</c>（以行动点计的移动预算）见
        /// <see cref="MovementBudgetRule"/>/<see cref="MovementActionCostPerUnit"/>：落地后，本字段
        /// 仍然是"该行动者这一步按速度会移动多远"的距离计算基准（<c>action_points</c> 规则只是在
        /// 这个距离之上叠加一层"够不够行动点"的门槛，见 <c>MovementTickHandler</c> 判断记录），不
        /// 是被替换掉的旧机制。
        /// </summary>
        public double DiscreteTurnEquivalentSeconds { get; set; } = 1.0;

        /// <summary>
        /// ADR-0013 补齐：离散模式下每回合移动预算的计算方式，取值同 <c>found.time_model.movement_budget_rule</c>
        /// （<c>"distance"</c> 或 <c>"action_points"</c>），默认 <c>"distance"</c>（本任务之前唯一
        /// 落地过的规则，行为不变）。
        /// <para>
        /// 判断记录（构造后回填而非构造期传入）：本模块（<c>core/carriers/unit</c>）构造早于
        /// <c>core/gameplay/assembly.TimeModelSwitch</c> 读出 <c>found.time_model</c> 数据的时机
        /// （惯例同 <c>Core.Rules.Combat.CombatOptions.LeaveCombatDelay</c> 判断记录、
        /// <c>Core.Rules.Combat.CombatTickHandler</c> 判断记录"事件订阅而非直接引用"的姊妹做法），
        /// 本字段与下面三个字段都设计成"构造后可写属性"，由 <c>GameplayAssembly</c> 在装配出
        /// <c>TimeModelSwitch</c> 之后回填同一个 <see cref="MovementOptions"/> 实例（
        /// <c>Core.Carriers.Assembly.CarriersAssembly.MovementOptions</c> 属性把它对外暴露）。
        /// </para>
        /// </summary>
        public string MovementBudgetRule { get; set; } = "distance";

        /// <summary><see cref="MovementBudgetRule"/> 为 <c>"action_points"</c> 时使用：移动 1 单位
        /// 距离消耗的行动点数（见 <see cref="MovementBudgetRule"/> 判断记录、04 第 3.1 节勘误
        /// <c>movement_action_cost_per_unit</c>）。默认 0（配合默认的 <c>"distance"</c> 规则时不会
        /// 被读取）。</summary>
        public double MovementActionCostPerUnit { get; set; } = 0.0;

        /// <summary>
        /// <see cref="MovementBudgetRule"/> 为 <c>"action_points"</c> 时，离散步移动前调用本委托
        /// 尝试扣减 <c>(actorId, 本次位移所需行动点)</c>；返回 <c>false</c> 表示预算不足，
        /// <see cref="MovementTickHandler"/> 据此拒绝本次移动意图（不产生任何位移）并调用
        /// <see cref="RequestEndTurn"/>。为空（未装配离散模式，或调用方未回填）时
        /// <see cref="MovementTickHandler"/> 不做任何行动点检查，行为与本任务之前一致。典型绑定：
        /// <c>Core.Foundation.SimLoop.TurnScheduler.TryConsumeActionPoints</c>（见该方法判断记录
        /// "与 TurnScheduler 的 action_points 策略共享同一预算"）。
        /// </summary>
        public Func<Id, double, bool>? TryConsumeActionPoints { get; set; }

        /// <summary>行动点耗尽、移动意图被拒绝时调用，结束该行动者的回合（06 第 6.2 节"直到本回合
        /// 行动点/移动预算耗尽...调用 TurnScheduler.endTurn"）。典型绑定：
        /// <c>Core.Foundation.SimLoop.TurnScheduler.EndTurn</c>。</summary>
        public Action<Id>? RequestEndTurn { get; set; }

        /// <summary>
        /// 加固任务（05 §3.6 碰撞层规划落地）：单位间是否互相阻挡移动的策略开关，默认 <c>false</c>
        /// （05 §3.6 原文"默认关闭，允许单位重叠，简化 2.5D 拥挤场景处理"）；13 §4 第 20 行"单位间
        /// 移动阻挡（<c>unit_block</c>）"口味清单项即接到本字段（见 <c>games/_template/Runtime/
        /// GameOptions.UnitBlockEnabled</c>）。为 <c>true</c> 时 <see cref="MovementTickHandler"/> 在
        /// 应用每次位移前，查询目标落点附近携带
        /// <see cref="Core.Foundation.EngineAdapter.CollisionLayers.UnitBlock"/> 标签、非自身的对象，
        /// 命中则本次不产生位移（见 <see cref="UnitBlockRadius"/>、<c>MovementTickHandler</c> 判断
        /// 记录"停在原地，不做滑动"）。
        /// </summary>
        public bool UnitBlocking { get; set; } = false;

        /// <summary>
        /// <see cref="UnitBlocking"/> 为 <c>true</c> 时，阻挡判定查询目标落点的半径。判断记录：
        /// 默认 0.5——不直接复用 <c>EntitySpatialSyncHost.KindConfig.Radius</c>（`creature`/`player`
        /// 默认注册半径 0.1）的理由是二者语义不同：注册半径是"这个对象在空间索引里占多大"，服务于
        /// 通用范围查询（技能命中、AI 感知等，允许较小近似）；本字段是"两个单位中心距离多近算互相
        /// 阻挡"，语义更接近"两个单位的物理体积不能重叠"，0.1 会导致单位几乎贴脸才互相阻挡、观感上
        /// 仍然像"允许重叠"，与"确实阻挡移动"的口味意图不符，故取一个更接近典型单位间距的默认值；
        /// 具体数值仍是口味配置项，游戏层可按自己的单位密度调整。
        /// </summary>
        public double UnitBlockRadius { get; set; } = 0.5;

        /// <summary>
        /// 游戏侧通用能力需求（05 第 6 节勘误）：<see cref="MovementTickHandler.BeginPathTo"/> 建路
        /// 失败（<see cref="Core.Foundation.EngineAdapter.INavigation2D.FindPath"/> 返回 null）或
        /// 阻挡版本变化后重算失败时的处理策略，默认 <see cref="PathFailurePolicy.KeepOldPath"/>
        /// （向后兼容——本任务之前唯一的行为）。
        /// </summary>
        public PathFailurePolicy PathFailurePolicy { get; set; } = PathFailurePolicy.KeepOldPath;

        /// <summary>
        /// 游戏侧通用能力需求（05 第 6 节勘误）：某单位持有路径期间
        /// <see cref="Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion"/> 发生变化时的
        /// 处理策略，默认 <see cref="BlockingChangePolicy.Replan"/>。仅当装配的
        /// <see cref="Core.Foundation.EngineAdapter.INavigation2D"/> 实现支持版本追踪（
        /// <c>GetBlockingVersion</c> 返回非 0）时才会被读取——未装配导航或导航实现恒返回 0（默认
        /// 接口实现）时，本字段不影响任何行为，与本任务之前完全一致。
        /// </summary>
        public BlockingChangePolicy BlockingChangePolicy { get; set; } = BlockingChangePolicy.Replan;

        /// <summary>
        /// ADR-0013 决策 6、04 第 3.1 节 <c>found.time_model.grid_snap</c> 落地：离散步（
        /// <see cref="Core.Foundation.SimLoop.SimStepKind.Discrete"/>）下，若 <see cref="GridSnapCellSize"/>
        /// 非 <c>null</c>，<see cref="MovementTickHandler"/> 在本次位移推进计算出最终坐标后、写回
        /// <c>Core.Rules.Common.IUnitAccess.SetPosition</c> 之前，用本策略把坐标吸附到所属
        /// 格子的中心点（见 <see cref="GridSnapCellSize"/> 判断记录"何时生效"）。默认
        /// <see cref="Core.Foundation.Common.GridSnapPolicy"/>（以原点为基准的正方形网格），游戏层
        /// 可替换成自己需要的网格几何（见该接口类型判断记录）。本字段始终非空（不像
        /// <see cref="GridSnapCellSize"/> 用 <c>null</c> 表达"未启用"）——是否吸附完全由
        /// <see cref="GridSnapCellSize"/> 是否为 <c>null</c> 决定，本字段只提供"启用时具体怎么吸附"
        /// 的算法，调用方不需要在两处分别判空。
        /// </summary>
        public Core.Foundation.Common.IGridSnapPolicy GridSnapPolicy { get; set; } = new Core.Foundation.Common.GridSnapPolicy();

        /// <summary>
        /// <c>found.time_model.grid_snap.cell_size</c>（见 <see cref="GridSnapPolicy"/> 判断
        /// 记录）。<c>null</c>（默认）表示未启用格子吸附，<see cref="MovementTickHandler"/> 的行为与
        /// 格子吸附落地之前逐字节一致——即便 <see cref="GridSnapPolicy"/> 已经装配也不会被调用。
        /// <para>
        /// 判断记录（构造后回填，且只在离散步生效）：本字段与 <see cref="MovementBudgetRule"/> 同一
        /// 惯例——<c>Core.Gameplay.Assembly.GameplayAssembly</c> 在 <c>TimeModelSwitch</c> 造好之后
        /// 按 <c>TimeModelSwitch.CombatModel.GridSnapCellSize</c> 回填（<c>CombatModel</c> 为 null
        /// 时保持默认 <c>null</c>，不回填）。是否真正吸附额外要求"当前这一步是离散步"（
        /// <c>step.Kind == SimStepKind.Discrete</c>，<see cref="MovementTickHandler.Execute"/> 已持有
        /// 该信息，不需要在本类型重复判断"当前是否处于离散模式"）——连续模式下即便本字段非空也绝不
        /// 吸附，保证"探索期间偶尔切回连续模式时，历史遗留的战斗期声明不会意外影响探索移动"这条边界
        /// （呼应任务书"连续模式与 grid_snap=false 时行为与现在逐字节一致"）。
        /// </para>
        /// </summary>
        public double? GridSnapCellSize { get; set; }

        /// <summary>
        /// ADR-0026《技能位移的连续模式》：<c>skill.def.effects[].kind == "move"</c> 且
        /// <c>params.motion == "continuous"</c> 时，若数据未声明 <c>params.sample_step</c>（或声明为
        /// 非正值，视为"未声明"），受控位移逐 tick 推进采用的采样步长兜底值（见
        /// <c>Core.Rules.Common.ControlledDisplacementRequest.SampleStep</c> 判断记录"默认取导航网格
        /// 尺寸或固定值"）。判断记录：<see cref="Core.Foundation.EngineAdapter.INavigation2D"/> 契约
        /// 本身不暴露"网格尺寸"这个概念（见该接口成员列表），本模块因此只能提供"固定值"分支；默认
        /// 0.5——与 <see cref="UnitBlockRadius"/> 同量级（典型单位体积尺度），足够细以避免单个采样步
        /// 内穿过一堵薄墙却漏检（同 <c>MovementTickHandler.ApplyDirectionalMove</c> 对候选终点整体做
        /// 一次 Raycast 的既有精度——采样只是把"阻挡后停在哪"的粒度控制得更细，不是弥补 Raycast 本身
        /// 的精度缺口），具体数值仍是口味配置项，游戏层可按自己的场景尺度调整。
        /// </summary>
        public double DefaultDisplacementSampleStep { get; set; } = 0.5;
    }
}

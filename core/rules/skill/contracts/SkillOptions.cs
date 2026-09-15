using Core.Foundation.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 同一 <see cref="Core.Rules.Skill.AuraHost"/> 运行时槽位——键为 <c>(target, aura_def,
    /// sourceKey)</c>，见该类型 <c>_slots</c> 判断记录——叠加数超过 <c>max_stacks</c> 时的处理策略
    /// （见 06 第 3.8 节"忽略新实例的叠加请求，或按策略替换为最新参数（策略配置项：
    /// ignore|refresh_only|replace）"）。<c>stack_category</c> 不参与该槽位键，只在加载期供
    /// <c>StackCategoryConflictRule</c> 做静态内容校验（见架构 ADR-0023"光环叠加类别为静态校验
    /// 分组"）；不同 <c>aura_def</c> 即使 <c>stack_category</c> 相同，也各自独立按本策略处理、
    /// 互不影响。
    /// </summary>
    public enum StackOverflowPolicy
    {
        /// <summary>忽略本次叠加请求：不刷新持续时间、不增加层数、不替换参数。</summary>
        Ignore,

        /// <summary>只刷新持续时间，不增加层数（层数保持在 <c>max_stacks</c>）。</summary>
        RefreshOnly,

        /// <summary>撤销旧实例的全部效果，用本次请求的参数重新创建一个新实例（层数重置为 1）。</summary>
        Replace,
    }

    /// <summary>
    /// <c>core/rules/skill</c> 模块的构造期口味配置（见落地方案 T2-4/T2-5/T2-6 行、06 第 3.6/3.8
    /// 节"策略配置项"）。全部字段均有默认值，但默认取值本身不代表任何具体游戏的口味决策——
    /// 每款游戏应在自己的口味配置清单中显式声明这些字段的取值（呼应 T2-4 禁止事项"禁止把公共
    /// 冷却写死为启用或关闭"）。
    /// </summary>
    public sealed class SkillOptions
    {
        /// <summary>
        /// 公共冷却（GCD）是否启用（见 06 第 3.6 节"公共冷却开关：是否存在跨技能的统一冷却……
        /// 均为策略配置项；关闭时步骤 4 恒通过"）。默认 <c>false</c>——不预设任何游戏一定需要
        /// 公共冷却，禁止把该配置项的默认取值当作产品决策（T2-4 禁止事项）。
        /// </summary>
        public bool GcdEnabled { get; set; } = false;

        /// <summary>
        /// 公共冷却时长（以数据集声明的时间单位计，见 04 第 3.1 节）。仅 <see cref="GcdEnabled"/>
        /// 为 true 且技能 <c>respects_gcd</c> 为 true 时生效。本字段的具体数值同样由游戏口味配置
        /// 清单给出，此处默认值只是一个占位式合理起点，不代表任何产品决策。
        /// </summary>
        public double GcdDuration { get; set; } = 1.5;

        /// <summary>
        /// 法术队列窗口长度（见 06 第 3.6 节"法术队列……队列窗口长度是策略配置项"）：读条剩余
        /// 时间小于等于本值时，再次 <c>CastSkill</c> 会进入队列而不是立即失败。默认 0.3。
        /// </summary>
        public double QueueWindow { get; set; } = 0.3;

        /// <summary>叠加超限策略（见 <see cref="StackOverflowPolicy"/>）。默认
        /// <see cref="StackOverflowPolicy.RefreshOnly"/>（06 第 3.8 节"默认刷新持续时间"语义
        /// 的延伸——超限时至少保证持续时间被刷新）。</summary>
        public StackOverflowPolicy StackOverflowPolicy { get; set; } = StackOverflowPolicy.RefreshOnly;

        /// <summary>
        /// 是否允许同一光环定义的多个来源分别计时（见 06 第 3.8 节"是否允许多来源分别计时是
        /// （建议）扩展位，默认关闭以简化状态管理"）。关闭时，同一 <c>target</c> 身上同一
        /// <c>aura_def</c> 无论来源是谁都视为同一个实例槽位；开启时按 <c>(defId, sourceId)</c>
        /// 分别维护独立实例。默认 false。
        /// </summary>
        public bool AllowMultiSourceTiming { get; set; } = false;

        /// <summary>单条 <c>skill.def</c>/<c>skill.aura_def</c> 效果列表长度上限（见 04 第 5 节
        /// "效果数上限……策略配置项，默认建议值见 06"）。默认 8。</summary>
        public int MaxEffectsPerSkill { get; set; } = 8;

        /// <summary>
        /// Proc 触发链递归深度上限（见落地方案 T2-6 禁止事项"禁止触发链无限递归（需有防护测试
        /// 用例）"）：A 触发 B、B 触发 A 一类循环链，深度超过本值时拒绝并记诊断。默认 8。
        /// </summary>
        public int MaxTriggerDepth { get; set; } = 8;

        /// <summary>Proc 命中判定使用的 <see cref="Core.Foundation.Rng.IRngHost"/> 分流 id
        /// （见 03 第 9 节 <c>RngHost</c>"按 stream 分流生成随机数以保证可复现"）。默认
        /// <c>skill.proc</c>。</summary>
        public Id RngStream { get; set; } = new Id("skill.proc");

        /// <summary>
        /// W1 收边补齐（06 第 3.1 节 <c>action_cost</c>、ADR-0013）：离散步内尝试扣减
        /// <c>unitId</c> 的行动点，成功返回 <c>true</c>；不足返回 <c>false</c>。签名与
        /// <c>Core.Foundation.SimLoop.TurnScheduler.TryConsumeActionPoints</c>/
        /// <c>core/carriers/unit.MovementOptions.TryConsumeActionPoints</c> 完全一致（同一账本，
        /// 供 <c>GameplayAssembly</c> 直接把同一个委托传给两处，见该二者判断记录）。为 null（本模块
        /// 未装配离散接线，或游戏当前是连续模式）时 <see cref="CastPipeline"/> 不做任何行动点检查，
        /// 与"连续模式忽略本字段"一致。是否真的处于离散步由 <see cref="IsDiscreteStep"/> 单独判断——
        /// 两者都需要为真才会真正扣减，避免连续模式下误消耗一个"离散语义"的资源。
        /// </summary>
        public System.Func<Id, double, bool>? TryConsumeActionPoints { get; set; }

        /// <summary>
        /// W1 收边补齐（06 第 3.6 节"离散模式下的解释：公共冷却在离散模式下恒关闭"、ADR-0013 决策
        /// 6）：调用方（<c>GameplayAssembly</c>/<c>TimeModelSwitch</c>）装配后，<see cref="CastPipeline"/>
        /// 在当前步骤为离散步（返回 <c>true</c>）时——步骤 4 恒通过 GCD 检查、步骤 9 不再启动 GCD 计时
        /// ——不改变 <see cref="GcdEnabled"/> 本身（该项仍决定连续模式下 GCD 是否生效）。为 null（未
        /// 装配）时视为恒为连续模式，行为与本次改动之前完全一致（GCD 是否生效只看
        /// <see cref="GcdEnabled"/>），呼应"没有调用方主动声明离散步，就不应该有任何行为变化"。
        /// </summary>
        public System.Func<bool>? IsDiscreteStep { get; set; }

        /// <summary>
        /// ADR-0013 决策 6、04 第 3.1 节 <c>grid_snap</c> 落地：<see cref="IsDiscreteStep"/> 返回
        /// <c>true</c> 且 <see cref="GridSnapCellSize"/> 非 <c>null</c> 时，
        /// <see cref="Core.Rules.Common.ISkillHost.FindUnits"/> 改用
        /// <see cref="Core.Foundation.EngineAdapter.GridSnapShapeQuery.QueryShapeAtCellCenters"/>——
        /// 候选按其所属格子中心点是否落在查询形状内判定，而不是按候选的原始坐标。默认
        /// <see cref="Core.Foundation.Common.GridSnapPolicy"/>。
        /// </summary>
        public Core.Foundation.Common.IGridSnapPolicy GridSnapPolicy { get; set; } = new Core.Foundation.Common.GridSnapPolicy();

        /// <summary><c>found.time_model.grid_snap.cell_size</c>；<c>null</c>（默认）表示未声明
        /// <c>grid_snap</c>，<see cref="GridSnapPolicy"/> 不会被调用，<c>FindUnits</c> 行为与格子
        /// 吸附落地之前逐字节一致。由 <c>Core.Gameplay.Assembly.GameplayAssembly</c> 按战斗时间模型
        /// 回填（惯例同 <see cref="TryConsumeActionPoints"/> 判断记录）。</summary>
        public double? GridSnapCellSize { get; set; }

        /// <summary>
        /// T-N3-3（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2；06
        /// 第 3.10 节）：本局游戏使用的 <c>skill.budget_rule</c> 记录 id——<c>weapon_damage_pct</c>
        /// 效果原语按本 id 查询 <c>beat_seconds</c>（一拍常数，见
        /// <see cref="Core.Rules.Skill.EffectDispatcher"/> 的 <c>ResolveBeatSeconds</c> 判断记录）。
        /// 默认 <c>skill.budget_rule.default</c>——与其余口味配置项同一惯例（默认值本身不代表任何
        /// 具体游戏的口味决策，每款游戏应在自己的口味配置清单中显式声明，见本类型顶部类注释）；
        /// 指向的记录在已加载数据里找不到时（表未注册、或该 id 没有对应记录）不抛异常，一拍常数
        /// 按缺省 1.0 处理并记一条警告，不阻断结算。默认值字面量与
        /// <see cref="Core.Rules.Skill.EffectDispatcher"/>.<c>DefaultBudgetRuleId</c>（<c>_options</c>
        /// 为 <c>null</c> 时的回退值，见该字段判断记录）保持一致，两处均已互相交叉引用。
        /// </summary>
        public Id BudgetRuleId { get; set; } = new Id("skill.budget_rule.default");

        /// <summary>
        /// T-N3-5（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 10；06
        /// 第 3.1 节 2026-09-14 修订段"急速缩短动作时长……'急速是否缩短动作时长及其下限'为策略配置项
        /// （默认不受影响）"；13 新游戏接入指南"公共冷却与节拍锁"行同一措辞）：急速是否缩短动作时长
        /// （<c>skill.def.cast_time</c>，见 <see cref="Core.Rules.Skill.CastPipeline.ComputeCastTime"/>
        /// 判断记录）。硬性规则"禁止默认开启急速缩短"：默认 <c>false</c>，且默认值本身不代表任何
        /// 具体游戏的口味决策（同本类型其余策略项惯例，见类注释）。为 <c>false</c>（默认）时
        /// <see cref="Core.Rules.Skill.CastPipeline.ComputeCastTime"/> 完全不读 <see cref="HasteStat"/>/
        /// <see cref="MinActionSeconds"/>/<see cref="MaxHastePct"/> 三者，行为与 T-N3-5 之前逐位一致
        /// （回归）。
        /// </summary>
        public bool HasteAffectsActionTime { get; set; } = false;

        /// <summary>
        /// T-N3-5：急速属性的 <c>stat.definition</c> id，供 <see cref="HasteAffectsActionTime"/> 开启时
        /// 折算动作时长。默认 <c>null</c>（与 <see cref="HasteAffectsActionTime"/> 默认关闭配套——两者
        /// 任一不满足，<see cref="Core.Rules.Skill.CastPipeline.ComputeCastTime"/> 均不做急速折算，见该
        /// 方法判断记录）。经 <see cref="Core.Numbers.StatBlock.IStatHost.GetStat"/> 聚合出的最终值按
        /// "百分比数值"解释（如 20 表示 20%，与 <c>Core.Rules.Combat</c> 既有 <c>DamageDonePctStat</c>
        /// 一类百分比属性同一惯例——<c>category: percent</c> 且登记 <c>conversion_ref</c> 时，评级点数
        /// 经换算曲线已经折算成这个"百分比数值"，<see cref="Core.Rules.Skill.CastPipeline"/> 不重复
        /// 换算，直接消费 <c>GetStat</c> 返回值）。指向的属性未在 <c>stat.definition</c> 登记，或施法者
        /// 未在 <see cref="Core.Numbers.StatBlock.IStatHost"/> 注册时，按 0（不折算）处理，不阻断施法
        /// （同 C02 判断记录"来源单位未注册按 0 处理"惯例）。
        /// </summary>
        public Id? HasteStat { get; set; }

        /// <summary>
        /// T-N3-5（06 第 3.1 节"下限同时受表现层动画最短时长约束"）：急速缩短动作时长后的下限，
        /// 时间单位同 <c>cast_time</c>（见 04 第 3.1 节"时间字段语义"）。L2 规则层不依赖表现层，拿不到
        /// 真实的动画最短时长（01 依赖矩阵），本字段是该约束在规则层唯一能承接的落点——真实数值应由
        /// 游戏口味配置清单给出，此处默认值只是占位式合理起点，不代表任何产品决策（同
        /// <see cref="GcdDuration"/> 惯例）。默认 0（不设下限）。只在 <see cref="HasteAffectsActionTime"/>
        /// 开启且技能确实被急速缩短（缩短后时长 &lt; 折算前原始值）时才生效——haste 折算前该技能
        /// <c>cast_time</c> 本就低于本字段时不受影响，见
        /// <see cref="Core.Rules.Skill.CastPipeline.ComputeCastTime"/> 判断记录"下限只夹住急速造成的
        /// 缩短，不是所有技能的最短动作时长"（落地方案 T-N3-5 契约疑点，上报待设计层确认）。
        /// </summary>
        public double MinActionSeconds { get; set; } = 0.0;

        /// <summary>
        /// T-N3-5（ADR-0031 决策 10"急速是否缩短动作时长及其下限"；
        /// [ADR-0032](../../../../architecture/adr/0032-装备预算消耗与词缀份额.md) 决策 4"成长由急速
        /// 属性作用于动作时长，上限为急速硬上限"）：急速对动作时长生效的硬上限，单位同
        /// <see cref="HasteStat"/>"百分比数值"惯例（如 100 表示 100%，动作时长最多压缩到原始值的
        /// 一半）。<see cref="HasteStat"/> 读到的原始值先夹到 <c>[0, MaxHastePct]</c>（负值——理论上的
        /// "减速"效果——本任务不处理，按 0 处理，不允许急速反而拉长动作时长，见
        /// <see cref="Core.Rules.Skill.CastPipeline.ComputeCastTime"/> 判断记录）再参与折算。默认
        /// 100.0——契约只说"存在硬上限"，两处引用均未给出具体数值（同 <see cref="GcdDuration"/> 惯例，
        /// 占位式合理起点，不代表任何产品决策，落地方案 T-N3-5 契约疑点，上报待设计层确认）。
        /// </summary>
        public double MaxHastePct { get; set; } = 100.0;

        /// <summary>
        /// T-N3-7（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 5；06
        /// 第 3.8 节 2026-09-14 修订段"瘟疫刷新规则"）：同来源同光环再次施加（刷新）时，保留旧实例
        /// 剩余持续时间的比例——<c>新持续时间 = 定义持续时间 + min(剩余时长, 定义持续时间 × 比例)</c>
        /// （见 <see cref="Core.Rules.Skill.AuraHost"/>.<c>ComputeRefreshedRemaining</c> 判断记录，
        /// 两侧均按当前时间模式计时单位计算）。取值范围 [0,1]，越界值由消费点防御性夹取（同本类型
        /// <see cref="MaxHastePct"/> 消费点"负值按 0 处理"惯例，setter 本身不拦）。默认 0.3（契约
        /// 原文"默认三成"——ADR-0031 决策 5"3.8 节刷新规则新增策略配置项'刷新时保留剩余时长的
        /// 比例'，默认三成，设零即现有行为"；改动点清单 S9"默认 0.3"同一口径）。判断记录（本字段
        /// 默认值与本类型其余口味配置项惯例不同——此处不是"占位式合理起点、不代表任何产品决策"，
        /// 而是 ADR-0031 已拍板的具体默认数值，属于本 ADR 本身的既定契约，不是执行层自行拍板；
        /// 只要不显式覆盖，任何装配本模块的游戏默认即获得"瘟疫刷新"这一新行为，不是"默认关闭、
        /// 游戏自行选择是否开启"的新增能力）。设为 0 时精确退化为本字段引入之前的"默认刷新持续
        /// 时间"（剩余时长全部丢弃、只用定义持续时间）既有行为，见该方法判断记录"回归"一节。
        /// <para>
        /// 硬性规则"禁止引入快照策略项"：本字段只影响"刷新时新持续时间如何计算"这一个数字，不是
        /// "施加时快照全部属性"式的策略开关——刷新后剩余持续时间内的周期效果仍按 06 第 3.3 节
        /// "动态计算，不做快照"逐跳重算（来源仍注册时），与本字段的取值无关，两者是完全独立的两件
        /// 事（见 <c>ComputeRefreshedRemaining</c> 判断记录"与周期效果动态计算的关系"）。
        /// </para>
        /// </summary>
        public double PlagueRefreshRatio { get; set; } = 0.3;
    }
}

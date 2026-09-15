using Core.Foundation.Common;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// 由调用方（游戏侧/T-N4-4 分档与难度倍率计算方）注入的经验倍率委托——见
    /// <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/> 文档：ADR-0033 决策 4
    /// "经验加成只有难度层一种；日后若加其他加成，规则为相加不相乘"的预留扩展点，返回值与既有
    /// <c>creature.tier_definition.xp_multiplier</c>/<c>diff.tier.xp_multiplier</c> 的乘积规则一致
    /// （相乘）；<paramref name="unitId"/> 是领取经验的单位，<paramref name="sourceId"/> 是
    /// <c>prog.xp_source</c> 的来源 id。默认不设置（<see cref="ProgressionOptions.ExtraXpMultiplierProvider"/>
    /// 为 <c>null</c>），等价于恒为 1（不影响现有倍率计算）。
    /// <para>
    /// T-N4-4 变更记录（签名新增 <paramref name="tierId"/>）：T-N4-2 落地时本委托签名只有
    /// <c>(unitId, sourceId)</c>，<see cref="XpContext"/> 类型注释当时已把"该委托签名不含
    /// <c>tierId</c>，若 T-N4-4 落地时发现真的需要按 <c>TierId</c> 查倍率，委托签名或注入方式
    /// 需要那时再按需扩展"登记为契约疑点上报——本任务落地"分档经验倍率"（读
    /// <c>creature.tier_definition.xp_multiplier</c>）确实需要知道死亡单位的分档 id，
    /// <see cref="Core.Numbers.Progression.ProgressionHost.GrantXp"/> 的 <c>kind=kill</c> 分支
    /// 调用本委托时手上正好有 <see cref="XpContext.TierId"/>（调用方经
    /// <see cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener"/> 传入），因此新增
    /// <paramref name="tierId"/> 参数直传，不改变已有语义。ABI 判断记录：本委托类型是阶段 N4
    /// 内多个任务共同完善、随 1.34.0 一次性发布的全新类型（本类型顶部"契约疑点上报"同一惯例），
    /// 本阶段内的委托签名调整不构成"已发布 ABI"的破坏性变更；<paramref name="tierId"/> 为
    /// <c>null</c> 表示领取经验事件未能解析出分档 id（如死亡单位未登记模板，见
    /// <c>CreatureDeathXpListener.ResolveTierId</c> 判断记录），调用方应退化为"无分档加成"。
    /// </para>
    /// </summary>
    public delegate double ProgressionXpMultiplierProvider(Id unitId, Id sourceId, Id? tierId);

    /// <summary>
    /// <c>core/numbers/progression</c> 模块的构造期口味配置（分阶段落地计划 T-N4-1；
    /// [ADR-0033](../../../../architecture/adr/0033-等级经验模块正文与当量来源.md) 决策
    /// 2/3/4/7/9；06_规则层_属性技能战斗AI.md 第 2.5 节"策略配置项：经验来源权重、最大等级"）。
    /// <para>
    /// <b>本任务只登记契约壳（字段声明 + 默认值 + 判断记录），不消费</b>——
    /// <see cref="Core.Numbers.Progression.ProgressionHost"/> 尚未提供接受本类型的构造重载，各
    /// 字段的实际消费方留给对应任务（见各字段文档标注）。与
    /// <see cref="Core.Rules.Skill.SkillOptions"/> 同一惯例（本模块并行开发期不引用
    /// <c>Core.Rules.Skill</c> 具体类型，此处只是文字类比）："全部字段均有默认值，但默认取值
    /// 本身不代表任何具体游戏的口味决策"。
    /// </para>
    /// <para>
    /// 契约疑点上报：分阶段落地计划 T-N4-4/T-N4-5 任务行给出的"涉及文件"清单均未列出本文件
    /// （T-N4-4：<c>CreatureSchemas.cs</c>/<c>DifficultySchemas.cs</c>/<c>GameplayAssembly.cs</c>/
    /// <c>QuestSchemas.cs</c>/<c>EncounterSchemas.cs</c>/<c>RewardDispatcher.cs</c>/
    /// <c>RewardBundle.cs</c>；T-N4-5：<c>RulesAssembly.cs</c>/<c>IPowerHost.cs</c>/
    /// <c>core/gameplay/death/tests/</c>），但本任务（T-N4-1）的任务书原文明确要求
    /// <c>ProgressionOptions</c> 先登记"XpMultiplier 注入（T-N4-4）"与"升级回满开关（T-N4-5）"
    /// 两项字段——本类先按该要求落地两个占位字段（<see cref="ExtraXpMultiplierProvider"/>/
    /// <see cref="RefillOnLevelUp"/>）。若 T-N4-4/T-N4-5 执行时发现字段设计与实际消费方式不符
    /// （例如难度倍率改为经 <c>XpContext</c> 参数直接传递，不经 <see cref="ProgressionOptions"/>
    /// 注入），以彼时任务书与设计层裁定为准调整或废弃本字段——本类是全新类型、由阶段 N4 内多个
    /// 任务共同完善后随 1.34.0 一次性发布，本阶段内的字段调整不是"已发布 ABI"的破坏性变更。
    /// </para>
    /// </summary>
    public sealed class ProgressionOptions
    {
        /// <summary>
        /// 全局等级上限（06 第 2.5 节"策略配置项……最大等级"）。<c>0</c>（新缺省）表示"不设置
        /// 全局上限，运行期升级判定完全由单位绑定曲线的 <c>prog.level_curve.max_level</c> 决定"
        /// （等价于本字段生效前的既有行为）；显式正值表示"把满级判定收紧到
        /// <c>min(本值, 曲线自身 max_level)</c>"——只能收紧、不能放宽到超出曲线数据本身（曲线仍是
        /// 数据权威）。消费方（T-N4-2 起）：<see cref="ProgressionHost"/> 的"有效满级"计算（供
        /// <see cref="ProgressionHost.GetXpToNext"/>/<see cref="ProgressionHost.AddXp"/>/
        /// <see cref="ProgressionHost.GrantXp"/> 共用），仅在调用方使用接受本类型的构造重载时
        /// 生效——旧构造函数（未带本类型）等价于传入一个全字段缺省的 <see cref="ProgressionOptions"/>
        /// （即 <c>MaxLevel=0</c>），行为与本次改动之前完全一致。
        /// <para>
        /// 变更记录（T-N4-2 设计层裁定）：本字段在 T-N4-1 落地时默认值为 <c>1</c>，且注释明确写
        /// "不驱动、也不覆盖任何曲线的判定"（彼时 <see cref="ProgressionHost"/> 尚未提供接受本
        /// 类型的构造重载，本字段只是登记壳）。T-N4-2 新增该构造重载并开始真正消费本字段，语义
        /// 因此改为本条注释描述的"0=不设上限（等价于从曲线推导），正值=收紧上限"，默认值同步由
        /// <c>1</c> 改为 <c>0</c>——若仍沿用旧默认值 <c>1</c>，任何未显式配置本字段、只是想用新
        /// 构造重载传别的选项（如 <see cref="ExtraXpMultiplierProvider"/>）的调用方会被意外收紧到
        /// 1 级封顶，这不是任何游戏的口味决策，必须随"本字段开始被消费"这一事实同时修正默认值
        /// （本类是阶段 N4 内多个任务共同完善、随 1.34.0 一次性发布的全新类型，字段调整不构成
        /// "已发布 ABI"的破坏性变更，同类型注释"契约疑点上报"同一惯例）。
        /// </para>
        /// </summary>
        public int MaxLevel { get; set; } = 0;

        /// <summary>
        /// 升级回满开关（ADR-0033 决策 7"升级回满：<c>progression.level_up</c> 触发生命与资源
        /// 回满，由资源池订阅实现"；分阶段落地计划 T-N4-5）。默认 <c>true</c>——采纳 ADR 推荐
        /// 默认，但按框架惯例（策略配置项一律可关闭，不预设任何游戏一定要接受该口味）开放游戏层
        /// 关闭。消费实现（订阅 <c>progression.level_up</c> 并调用 <c>IPowerHost</c> 回满能力）
        /// 留 T-N4-5；本任务只登记字段，见类型注释"契约疑点上报"。
        /// </summary>
        public bool RefillOnLevelUp { get; set; } = true;

        /// <summary>
        /// 探索经验一次性标志的全局默认命名空间前缀（ADR-0033 决策 3"<c>once_key</c> 一次性
        /// 标志键前缀"）。判断记录：<c>prog.xp_source.once_key</c> 已经是"逐来源标志键前缀"
        /// 本身的字段登记（每条 <c>kind=discovery</c> 的来源各自声明自己的前缀）；本字段是
        /// 兜底默认值——某条来源缺省未填 <c>once_key</c> 时，T-N4-3 的探索经验监听器可选择
        /// 回退到本前缀拼接来源 id，避免因内容作者遗漏该字段导致标志散落在世界状态根命名空间、
        /// 与其它系统的标志键冲突。默认 <c>"prog.explore"</c>。消费实现留 T-N4-3；本任务只登记
        /// 字段。
        /// </summary>
        public string DefaultOnceKeyPrefix { get; set; } = "prog.explore";

        /// <summary>
        /// 经验倍率注入的预留扩展点（ADR-0033 决策 4"经验加成只有难度层一种；日后若加其他
        /// 加成，规则为相加不相乘"）：默认 <c>null</c>（不生效，等价于恒为 1，与本次改动之前的
        /// 行为一致）；非 <c>null</c> 时由调用方提供的 <see cref="ProgressionXpMultiplierProvider"/>
        /// 返回一个额外倍率，与 <c>creature.tier_definition.xp_multiplier</c>/
        /// <c>diff.tier.xp_multiplier</c> 的乘积规则一致（相乘）。
        /// <para>
        /// 消费方（T-N4-2 起）：<see cref="ProgressionHost.GrantXp"/> 的 <c>kind=kill</c> 分支按
        /// <c>baseAmount × (本委托返回值 ?? 1) × ΔFactor</c> 计算（ADR-0033 决策 3"击杀 = 击杀
        /// 基数(怪物等级) × 分档经验倍率 × 难度经验倍率 × 等级差表.经验系数(Δ)"——本委托是"分档
        /// 经验倍率 × 难度经验倍率"这一项的注入口，<c>quest</c>/<c>discovery</c> 两个分支不读取
        /// 本委托，见 ADR 原文对应公式没有这两项）。
        /// </para>
        /// <para>
        /// T-N4-4 变更记录（真正接入分档/难度倍率）：
        /// <c>core/gameplay/assembly.GameplayAssembly</c> 从本任务起把本字段（未显式配置时）
        /// 默认接为 <c>(tierId 对应的 Core.Carriers.Creature.CreatureFactory.TryGetXpMultiplier
        /// 结果 ?? 1) × Core.Gameplay.Difficulty.IDifficultyHost.XpMultiplier</c>——真正读
        /// <c>creature.tier_definition.xp_multiplier</c>/<c>diff.tier.xp_multiplier</c> 两张表；
        /// 调用方显式设置本字段时尊重调用方的选择，装配根不覆盖（见该文件"接线"步骤判断记录）。
        /// </para>
        /// </summary>
        public ProgressionXpMultiplierProvider? ExtraXpMultiplierProvider { get; set; } = null;

        /// <summary>
        /// 击杀经验来源 id（分阶段落地计划 T-N4-3；ADR-0033 决策 3"击杀 = 击杀基数(怪物等级) ×
        /// 分档经验倍率 × 难度经验倍率 × 等级差表.经验系数(Δ)"）：<c>null</c>（默认）时消费方
        /// 落到约定 id（见
        /// <see cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener.DefaultKillXpSourceId"/>
        /// <c>prog.xp_source.kill</c>）。
        /// <para>
        /// 契约疑点上报/临时判断：06 第 2.5 节与 ADR-0033 均未给出"<c>kind=kill</c> 的来源固定用哪一条
        /// <c>prog.xp_source</c> 记录 id"的字面结论——三种来源当量公式本身与 <c>SourceId</c> 无关
        /// （由调用方在 <see cref="IProgressionHost.GrantXp"/> 显式传入），本字段只是给
        /// <c>core/gameplay/progression_bridge.CreatureDeathXpListener</c> 这一类"框架自带的默认
        /// 监听器"一个可配置的承接点，避免它们各自硬编码约定 id。若某游戏需要按不同怪物类型使用不同
        /// 的击杀经验来源（如精英/首领各一条），本字段承载不了这种场景，需要调用方绕开默认监听器、
        /// 自行订阅 <c>unit.died</c> 并显式选择 <c>sourceId</c>，留待真正出现该需求时再扩展（同类型
        /// 注释"契约疑点上报"惯例，本类是阶段 N4 内多个任务共同完善、随 1.34.0 一次性发布的全新
        /// 类型，字段调整不构成"已发布 ABI"的破坏性变更）。本任务（T-N4-3）不消费本字段之外任何
        /// 装配层接线（<c>core/gameplay/assembly.GameplayAssembly</c> 是否要把某个真正配置好的
        /// <see cref="ProgressionOptions"/> 实例接进 <c>RulesAssembly.Progression</c> 构造点，留待
        /// 未来任务统一处理）。
        /// </para>
        /// </summary>
        public Id? KillXpSourceId { get; set; } = null;

        /// <summary>
        /// 探索经验来源 id（分阶段落地计划 T-N4-3；ADR-0033 决策 3"探索 = 一只怪当量 × 击杀基数
        /// (区域等级)，首次进入由世界状态标志保证"）：<c>null</c>（默认）时消费方落到约定 id（见
        /// <see cref="Core.Gameplay.ProgressionBridge.AreaTriggerDiscoveryXpListener.DefaultDiscoveryXpSourceId"/>
        /// <c>prog.xp_source.discovery</c>）。
        /// <para>
        /// 判断记录（单一全局来源 id，不按区域各注册一条）：三种来源当量公式按调用时传入的
        /// <see cref="XpContext.SourceLevel"/> 折算，<c>discovery</c> 分支本身不读取
        /// <see cref="XpContext.TierId"/>/来源 id 之外的任何区域专属信息；各区域的差异（等级、一次性
        /// 标志）由 <c>AreaTriggerDiscoveryXpListener</c> 在触发器进入回调里逐次传入
        /// <see cref="XpContext.SourceLevel"/> 与世界状态标志键（按触发器 id 区分）区分，因此不需要
        /// 为每个区域各登记一条 <c>prog.xp_source</c> 记录——同 <see cref="KillXpSourceId"/> 同一处
        /// 契约疑点上报惯例。
        /// </para>
        /// </summary>
        public Id? DiscoveryXpSourceId { get; set; } = null;

        /// <summary>
        /// 任务经验来源 id（分阶段落地计划 T-N4-4；ADR-0033 决策 3"任务 = 当量 N × 击杀基数
        /// (任务等级) × 经验系数(Δ)"）：<c>null</c>（默认）时消费方落到约定 id
        /// <c>prog.xp_source.quest</c>（同 T-N4-3 <see cref="KillXpSourceId"/>/
        /// <see cref="DiscoveryXpSourceId"/> 一贯约定）。
        /// <para>
        /// 消费方：<c>core/gameplay/common.RewardDispatcher</c>——任务/遭遇奖励包
        /// （<c>Core.Gameplay.Common.RewardBundle</c>）新增 <c>XpEquivalent</c>/<c>RewardLevel</c>
        /// 字段非空时，改经 <see cref="IProgressionHost.GrantXp"/>（而不是旧 <c>AddXp</c> 绝对数
        /// 路径）发放，<c>sourceId</c> 固定用本字段（或缺省约定 id），不使用 <see
        /// cref="Core.Gameplay.Common.IRewardDispatcher.Grant"/> 收到的 <c>sourceId</c> 参数
        /// （那个参数是"任务/遭遇/成就的 id"，供 <c>currency</c>/<c>skills</c>/<c>world_flags</c>/
        /// <c>talent_points</c> 四类奖励标注发放来源，语义上不是 <c>prog.xp_source</c> 记录 id，
        /// 两者刻意区分——同 <see cref="KillXpSourceId"/> "单一全局来源 id，不按内容各注册一条"
        /// 判断记录）。
        /// </para>
        /// </summary>
        public Id? QuestXpSourceId { get; set; } = null;
    }
}

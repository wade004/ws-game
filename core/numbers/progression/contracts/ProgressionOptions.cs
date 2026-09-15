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
    /// </summary>
    public delegate double ProgressionXpMultiplierProvider(Id unitId, Id sourceId);

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
        /// 全局等级上限（06 第 2.5 节"策略配置项……最大等级"）。判断记录：运行期升级判定的权威
        /// 上限始终是单位实际绑定曲线的 <c>prog.level_curve.max_level</c>（ADR-0033 决策 2，
        /// 曲线单调有限阻断校验，<see cref="ProgLevelCurveValidationRule"/>）——本字段不驱动、
        /// 也不覆盖任何曲线的判定，只是给"这个游戏的等级上限是多少"这一整体性元数据一个统一
        /// 读取点（供编辑器/其它系统展示，或做"全部曲线的 max_level 是否与本值一致"一类内容
        /// 一致性检查，消费方留待需要时另立任务，本任务不实现）。默认 1（占位式合理起点，不
        /// 代表任何产品决策，同 <c>SkillOptions.GcdDuration</c> 判断记录同一惯例）。
        /// </summary>
        public int MaxLevel { get; set; } = 1;

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
        /// <c>diff.tier.xp_multiplier</c> 的乘积规则一致（相乘）。消费实现（是否真的经本字段
        /// 注入，还是改为经 T-N4-2 的 <c>XpContext</c> 参数传递）留 T-N4-4 确认；本任务只登记
        /// 契约壳，见类型注释"契约疑点上报"。
        /// </summary>
        public ProgressionXpMultiplierProvider? ExtraXpMultiplierProvider { get; set; } = null;
    }
}

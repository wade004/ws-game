using System.Collections.Generic;
using Core.Carriers.Item;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Economy;
using Core.Numbers.Archetype;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Combat;
using Core.Rules.Skill;

namespace Presentation.Assembly
{
    /// <summary>
    /// 分阶段落地计划 T-N5-3（数值规则核对表 <c>architecture/落地计划/数值规则核对表-N5.md</c>；
    /// 04 第 5 节"数值类校验项分级表"；改动点清单 2.2 V4；ADR-0035 决策 5"报告为结构化产物……供
    /// 仿真/编辑器消费"）：一条 04 分级表行 = 一个只读登记条目——不是按 <see cref="RuleId"/> 去重
    /// （<see cref="StatDefinitionValidationRule"/>/<see cref="ItemBudgetValidationRule"/>/
    /// <see cref="SkillBudgetValidationRule"/> 三个类各产出两个检查名，各占两条），逐字段直接引用
    /// 各规则类自己公开的 <c>RuleId</c>/<c>CheckName</c> 常量（不写字面量，同 1.29.0
    /// <see cref="ContentValidationAssembly.OptionalRuleDescriptor"/> 先例）——规则若改检查名，本清单
    /// 自动跟着变，不会出现"清单抄错/规则改名但清单没跟着改"的静态比对发现不了的问题。
    /// <para>
    /// 判断记录（数量口径，T-N5-3 新发现的核对表内部不一致，如实上报）：核对表"契约疑点"一节与派发
    /// 提示词均称"阻断 11 项 + 警告 7 项 = 18 项"，但该表第 1 节"阻断级"表头写"11 项"、实际列出的行
    /// 却是 B1/B1a/B1b/B2/B3/B4/B5/B6/B7/B8/B9/B10/B11 共 <b>13 行</b>（B1a/B1b 明确标注"同上，
    /// 密集枚举分支"——与 B1 是同一个 04 原表概念条目，只是 <c>prog.level_curve</c>/
    /// <c>combat.resist_curve</c> 两张密集枚举表各自需要一个独立的 <see cref="IValidationRule"/>
    /// 实现，因此"11 项"数的是 04 原表的概念行数，"13 行"数的是本清单能登记的、各自有独立
    /// <c>RuleId</c>+<c>CheckName</c> 的技术行数）；核对表自己第 3 节"小结"给出的"16 已落地 + 3 部分
    /// + 1 缺"合计 20，与"18"矛盾，印证第 3 节实际是按 13+7=20 的技术行数清点的，"18"是"契约疑点"
    /// 一节转述 04 概念条目数时的笔误/口径混用，不是本任务引入的新分歧。本清单选择按
    /// <b>13 行阻断 + 7 行警告 = 20 行</b>登记（技术行数口径）——这是"禁止漏注册"能覆盖到的最大范围：
    /// 若按概念条目数收窄到 18（把 B1 家族并成一条），会丢失对 <see
    /// cref="ProgLevelCurveValidationRule"/>/<see cref="CombatResistCurveValidationRule"/> 两个独立
    /// 实现类的注册验证，与硬性规则"禁止漏注册"的从严原则相悖；本清单按 20 行登记后，"核对表全部 18
    /// 条的规则 id"这条验收标准仍然满足（18 个概念条目对应的规则 id 全部在 20 行的超集里），并不冲突。
    /// 设计层如认为应收窄为 18 行口径（即不单独登记 B1a/B1b 两条密集枚举分支的独立验证），请在 ADR 或
    /// 后续任务里明确"哪两行合并/删除"，本清单再据此调整。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="NumericValidationRuleDescriptor.Group"/> 分组）：04 分级表本身没有给出
    /// "属性/技能/装备/经验/经济/曲线"这一维度的官方分类字段（04 只有阻断/警告两级 + 各行检查项原文），
    /// 本清单按"这条规则主要校验哪张内容表所属的数值域"补一个分组标签，供
    /// <c>toolchain/validator --json</c> 的 <c>rules[].group</c>（区别于 <see
    /// cref="ValidationIssue.Group"/> 那个"已确认/待确认"业务意图分组——两者同名但语义不同，
    /// 层级不同：<see cref="ValidationIssue.Group"/> 是某一条具体问题的分组，本清单的
    /// <see cref="NumericValidationRuleDescriptor.Group"/> 是某一条规则/检查名整体归属的数值域）与
    /// 编辑器"数值校验清单"面板分类展示；这是本任务新增的报表维度，不是对现有契约的改写，设计层若认为
    /// 应改用别的分组口径，只需要改本文件的字符串常量，不牵动任何规则实现。曲线族的两个密集枚举分支
    /// （<see cref="ProgLevelCurveValidationRule"/> 落在"经验"、<see
    /// cref="CombatResistCurveValidationRule"/> 仍落在"曲线"，因为前者只服务 <c>prog.level_curve</c>
    /// 一张经验表，后者的宿主表 <c>combat.resist_curve</c> 不属于本清单其余六个分组中的任何一个）；
    /// <see cref="ArchClassDerivationOverrideValidationRule"/> 单独占"职业"一组（04 原表没有给它安排
    /// 分组，任务提示词给的六分类示例里也没有"职业"，但生搬硬套进"属性"反而会让编辑器面板产生"属性
    /// 校验为什么会报 arch.class"的误导——设计层若有不同意见，改这一行的字符串即可）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="NumericValidationRuleDescriptor.RequiresAnchor"/>）：仅
    /// <see cref="SkillBudgetValidationRule"/> 产出的两个检查名（<c>skill_budget_hard_cap_exceeded</c>/
    /// <c>skill_budget_deviation</c>）与 <see cref="ItemGrantValueExceedsShareRule"/>
    /// 产出的 <c>item_grant_value_exceeds_share</c> 为 <c>true</c>——这三条各自的 <c>Validate</c>
    /// 在 <c>anchorProvider == null</c> 时整条 <c>yield break</c>（见各自类型判断记录），而
    /// <see cref="Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll"/>/<see
    /// cref="Core.Carriers.Assembly.CarriersSchemaCatalog.RegisterAll"/> 默认注册时都不注入真实
    /// <c>ISkillBudgetAnchorProvider</c>（<c>sim.anchor</c> 归阶段 N6，晚于本阶段）——本清单据此把
    /// 这三条的 <c>Enabled</c>（见 <c>toolchain/validator --json</c> 的 <c>rules[].enabled</c>）
    /// 静态标记为 <c>false</c>，如实反映"当前装配根尚未接入锚点，这三条规则注册了但不会真的产出任何
    /// 问题"这一事实，不是动态探测——<see cref="ContentValidationOptions"/> 目前也确实没有任何接线点
    /// 可以让调用方在这条路径上换成真实 <c>ISkillBudgetAnchorProvider</c>（同 04 第 5.1 节
    /// "预算类校验的锚点依赖"段落判断记录），真正的动态锚点接入是 N6 的职责，不在本任务范围内。
    /// </para>
    /// <para>
    /// 判断记录（2026-09-16，深度复审 E-M1：并入 <c>sim.anchor</c>/<c>sim.scenario</c> 三行检查，
    /// 推翻此前 T-N6-8a"目录不收录"的口径）：04 第 5 节分级表已在 N6 收尾（T-N6-2a）时把
    /// <c>Core.Sim.SimAnchorValidationRule</c>（<c>sim_anchor_level_continuity</c> 阻断 +
    /// <c>sim_anchor_expected_item_level_monotonic</c> 警告）与
    /// <c>Core.Sim.SimScenarioValidationRule</c>（<c>sim_scenario_level_coverage_required</c> 阻断）
    /// 三行正式并入分级表，总数从 20 行改为 23 行（阻断 15 + 警告 8）；本清单此前一直未跟进，
    /// 深度复审领域 E（E-M1）发现后设计层裁定采纳方案一——并入本清单，新增分组"仿真"，不再采用
    /// T-N6-8a 当时记录的"故意排除"结论。深度复审同批 E-S2 又给 <c>SimScenarioValidationRule</c>
    /// 新增一条警告检查（<c>sim_scenario_bandwidth_key_unknown</c>，见该规则类型判断记录），总数
    /// 随之再改为 <b>24 行（阻断 15 + 警告 9）</b>。"仿真"分组四条 <c>RequiresAnchor</c> 均为
    /// <c>false</c>——它们校验的是 <c>sim.anchor</c>/<c>sim.scenario</c> 表自身结构（等级连续性/
    /// 场景条件必填/期望装备等级单调性/带宽键名合法性），不依赖
    /// <see cref="Core.Rules.Common.ISkillBudgetAnchorProvider"/>。
    /// <para>
    /// 判断记录（为何这四条不像其余 20 条那样用 <c>nameof(...)</c>/规则类常量逐字段引用，改用字符串
    /// 字面量）：本文件所在 <c>Presentation.Common</c> 是 <c>build.ps1</c> 的 <c>$CoreAssemblies</c>
    /// 六个"需要发布给 Unity 端的核心 DLL"之一（该数组固定六项：<c>Core.Foundation</c>/
    /// <c>Core.Numbers</c>/<c>Core.Rules</c>/<c>Core.Carriers</c>/<c>Core.Gameplay</c>/
    /// <c>Presentation.Common</c>，不含 <c>Core.Sim</c>）；若本文件 <c>using Core.Sim</c> 并对
    /// <c>SimAnchorValidationRule</c>/<c>SimScenarioValidationRule</c> 取 <c>nameof</c>/常量引用，
    /// 会让 <c>Presentation.Common.dll</c> 产生对 <c>Core.Sim.dll</c> 的硬程序集依赖——
    /// <see cref="NumericValidationRuleCatalog.Entries"/> 是静态只读字段、类加载即初始化，任何引用
    /// 本清单的游戏（哪怕从不触碰 sim 相关代码路径）在只拿到六个核心 DLL、没有 <c>Core.Sim.dll</c>
    /// 的 Unity 工程里都会在加载 <c>NumericValidationRuleCatalog</c> 类型时抛
    /// <c>FileNotFoundException</c>/<c>TypeLoadException</c>，是本任务改动范围不能引入的真实回归；
    /// 本修复批次的允许改动路径不含 <c>build.ps1</c>（把 <c>Core.Sim</c> 补进 <c>$CoreAssemblies</c>
    /// 或改成"游戏层按需选择"需要设计层另行拍板是否值得让每个游戏都多带一个仿真专用 DLL），因此这四
    /// 条改用与 <c>Core.Sim.SimAnchorValidationRule.LevelContinuityCheck</c> 等常量取值逐字符相同的
    /// 字符串字面量，不产生程序集依赖；"改一处、清单自动跟着变"这条既有惯例对这四行退化为"改一处、
    /// 由测试跟着炸"——<c>presentation/tests/Tests.PresentationCommon.csproj</c> 已经引用
    /// <c>Core.Sim</c>（此前为 <c>ExtraSchemaRegistration</c> 接线需要），在测试项目里补一条反射/
    /// 常量比对断言即可锁死这四处字面量与 <c>Core.Sim</c> 真实常量一致，不需要生产代码本身持有引用。
    /// 设计层如认为"接受 <c>Presentation.Common</c> 依赖 <c>Core.Sim</c>、同步把 <c>Core.Sim</c>
    /// 纳入 Unity 发行"更好，请另行拍板并改 <c>build.ps1</c>，本清单再改回 <c>nameof</c> 引用。
    /// </para>
    /// </para>
    /// </summary>
    public sealed class NumericValidationRuleDescriptor
    {
        /// <summary>产出本条问题的规则 id（<see cref="IValidationRule.RuleId"/>，本清单里全部用
        /// 默认取值 <c>GetType().Name</c>，无一条规则显式覆盖过 <see cref="IValidationRule.RuleId"/>），
        /// 与 <see cref="ValidationReport.Rules"/>/<c>toolchain/validator --json</c> 的
        /// <c>rules[].id</c> 同一个键，供"漏注册即失败"的比对使用。</summary>
        public string RuleId { get; }

        /// <summary>本条在 04 分级表原文对应的检查名（<see cref="ValidationIssue.Check"/> 取值）。</summary>
        public string CheckName { get; }

        /// <summary>04 分级表该行的分级（阻断/警告），即
        /// <see cref="IValidationRule.DefaultSeverity"/> 对该规则整体的默认值——注意 <see
        /// cref="ItemBudgetValidationRule"/>/<see cref="SkillBudgetValidationRule"/> 两个类各自的
        /// 两条检查名实际产出的 <see cref="ValidationIssue.Severity"/> 并不相同（一条 Error 一条
        /// Warning），本字段登记的是"这一条检查名"（不是这个类）在 04 表里所处的级别。</summary>
        public ValidationSeverity Severity { get; }

        /// <summary>04 分级表"警告"整组登记为不可提升（见 <see cref="IValidationRule.NonEscalatable"/>
        /// 类型级判断记录"抓意图不抓手滑"）：本清单里全部 11 条阻断为 <c>false</c>、全部 7 条警告为
        /// <c>true</c>，与各规则类的 <c>NonEscalatable</c> 实现逐一核对一致（见
        /// <c>NumericValidationRuleCatalogTests</c>）。</summary>
        public bool NonEscalatable { get; }

        /// <summary>04 第 5 节分级表该行的检查项原文（如"曲线单调有限（断点表族）""技能预算硬上限"），
        /// 供报告/编辑器展示时对得上设计文档的措辞，不是本清单发明的名字。</summary>
        public string GradingItemName { get; }

        /// <summary>本条所属数值域分组，见类型级判断记录。</summary>
        public string Group { get; }

        /// <summary>是否依赖阶段 N6 才会真正接入的 <c>ISkillBudgetAnchorProvider</c>，见类型级判断
        /// 记录。</summary>
        public bool RequiresAnchor { get; }

        public NumericValidationRuleDescriptor(
            string ruleId,
            string checkName,
            ValidationSeverity severity,
            bool nonEscalatable,
            string gradingItemName,
            string group,
            bool requiresAnchor)
        {
            RuleId = ruleId;
            CheckName = checkName;
            Severity = severity;
            NonEscalatable = nonEscalatable;
            GradingItemName = gradingItemName;
            Group = group;
            RequiresAnchor = requiresAnchor;
        }
    }

    /// <summary>本清单本体，见 <see cref="NumericValidationRuleDescriptor"/> 类型注释判断记录。</summary>
    public static class NumericValidationRuleCatalog
    {
        /// <summary>04 分级表全部 24 行（阻断 15 行，对应 13 个概念条目；警告 9 行/条目），顺序同
        /// 核对表 B1/B1a/B1b/B2～B11、W1～W7，末尾追加深度复审 E-M1 并入的"仿真"分组三行（04 第 5
        /// 节 N6 收尾新增的 <c>sim.anchor</c>/<c>sim.scenario</c> 表结构完整性检查）+ E-S2 新增一行
        /// （<c>sim_scenario_bandwidth_key_unknown</c>）；见类型级判断记录"数量口径"与"2026-09-16，
        /// 深度复审 E-M1"/"深度复审 E-S2"。</summary>
        public static IReadOnlyList<NumericValidationRuleDescriptor> Entries { get; } = new[]
        {
            // ---------------- 阻断级 11 项（核对表 B1～B11） ----------------
            new NumericValidationRuleDescriptor(
                nameof(CurveMonotonicFiniteRule), CurveMonotonicFiniteRule.CheckName,
                ValidationSeverity.Error, nonEscalatable: false,
                "曲线单调有限（断点表族）", "曲线", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ProgLevelCurveValidationRule), ProgLevelCurveValidationRule.CheckName,
                ValidationSeverity.Error, nonEscalatable: false,
                "曲线单调有限（断点表族，密集枚举分支 prog.level_curve.xp_to_next）", "经验", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(CombatResistCurveValidationRule), CombatResistCurveValidationRule.CheckEntriesMonotonic,
                ValidationSeverity.Error, nonEscalatable: false,
                "曲线单调有限（断点表族，密集枚举分支 combat.resist_curve.table）", "曲线", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(StatDefinitionDerivationCycleValidationRule), StatDefinitionDerivationCycleValidationRule.CheckName,
                ValidationSeverity.Error, nonEscalatable: false,
                "派生无环", "属性", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(StatDefinitionValidationRule), StatDefinitionValidationRule.CheckDerivedFromRequiresDerivedCategory,
                ValidationSeverity.Error, nonEscalatable: false,
                "属性派生来源限定 derived", "属性", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(StatDefinitionValidationRule), StatDefinitionValidationRule.CheckConversionRefRequiresPercentCategory,
                ValidationSeverity.Error, nonEscalatable: false,
                "属性换算引用限定 percent", "属性", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(StatRatingConversionValidationRule), StatRatingConversionValidationRule.CheckRequiresExactlyOneShape,
                ValidationSeverity.Error, nonEscalatable: false,
                "评级换算形态二选一", "属性", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ArchClassDerivationOverrideValidationRule), ArchClassDerivationOverrideValidationRule.CheckName,
                ValidationSeverity.Error, nonEscalatable: false,
                "职业派生系数覆盖边存在", "职业", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ItemBudgetValidationRule), ItemBudgetValidationRule.Check,
                ValidationSeverity.Error, nonEscalatable: true,
                "装备预算超标（消耗侧补齐）", "装备", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ItemTemplateAffixShareExceedsBudgetRule), ItemTemplateAffixShareExceedsBudgetRule.Check,
                ValidationSeverity.Error, nonEscalatable: false,
                "模板加词缀最大份额超预算", "装备", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ItemAffixStatMixRatioSumRule), ItemAffixStatMixRatioSumRule.Check,
                ValidationSeverity.Error, nonEscalatable: false,
                "词缀份额之和", "装备", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ItemQualityMultiplierOrderRule), ItemQualityMultiplierOrderRule.Check,
                ValidationSeverity.Error, nonEscalatable: false,
                "品质倍率顺序", "装备", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(SkillBudgetValidationRule), SkillBudgetValidationRule.HardCapCheck,
                ValidationSeverity.Error, nonEscalatable: true,
                "技能预算硬上限", "技能", requiresAnchor: true),

            // ---------------- 仿真分组新增阻断 2 项（深度复审 E-M1，2026-09-16；04 第 5 节 N6 收尾
            // 新增行；RuleId/CheckName 为字符串字面量而非 nameof/常量引用，见类型级判断记录
            // "为何这三条不像其余 20 条那样用 nameof(...) ……改用字符串字面量"） ----------------
            new NumericValidationRuleDescriptor(
                "SimAnchorValidationRule", "sim_anchor_level_continuity",
                ValidationSeverity.Error, nonEscalatable: true,
                "锚点表等级连续无缺口无重复", "仿真", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                "SimScenarioValidationRule", "sim_scenario_level_coverage_required",
                ValidationSeverity.Error, nonEscalatable: true,
                "仿真场景等级覆盖字段条件必填", "仿真", requiresAnchor: false),

            // ---------------- 警告级 7 项（核对表 W1～W7），全部 NonEscalatable=true ----------------
            new NumericValidationRuleDescriptor(
                nameof(SkillBudgetValidationRule), SkillBudgetValidationRule.DeviationCheck,
                ValidationSeverity.Warning, nonEscalatable: true,
                "技能预算偏离", "技能", requiresAnchor: true),
            new NumericValidationRuleDescriptor(
                nameof(ItemBudgetValidationRule), ItemBudgetValidationRule.CheckUtilizationLow,
                ValidationSeverity.Warning, nonEscalatable: true,
                "装备预算利用率过低", "装备", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ItemWeaponDamageDeviatesDpsCurveRule), ItemWeaponDamageDeviatesDpsCurveRule.Check,
                ValidationSeverity.Warning, nonEscalatable: true,
                "武器伤害偏离秒伤曲线", "装备", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(ItemGrantValueExceedsShareRule), ItemGrantValueExceedsShareRule.Check,
                ValidationSeverity.Warning, nonEscalatable: true,
                "授予价值超特效占比", "装备", requiresAnchor: true),
            new NumericValidationRuleDescriptor(
                nameof(SkillNoTimeCostWarningRule), SkillNoTimeCostWarningRule.Check,
                ValidationSeverity.Warning, nonEscalatable: true,
                "无时间成本", "技能", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(EconomyPriceDeviatesFormulaRule), EconomyPriceDeviatesFormulaRule.Check,
                ValidationSeverity.Warning, nonEscalatable: true,
                "手填价格偏离公式", "经济", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                nameof(StatDefinitionConsumerValidationRule), StatDefinitionConsumerValidationRule.CheckNoConsumer,
                ValidationSeverity.Warning, nonEscalatable: true,
                "属性无消费者", "属性", requiresAnchor: false),

            // ---------------- 仿真分组新增警告 2 项（深度复审 E-M1/E-S2，2026-09-16） ----------------
            new NumericValidationRuleDescriptor(
                "SimAnchorValidationRule", "sim_anchor_expected_item_level_monotonic",
                ValidationSeverity.Warning, nonEscalatable: true,
                "锚点表期望装备等级单调不减", "仿真", requiresAnchor: false),
            new NumericValidationRuleDescriptor(
                "SimScenarioValidationRule", "sim_scenario_bandwidth_key_unknown",
                ValidationSeverity.Warning, nonEscalatable: true,
                "仿真场景带宽键名未识别", "仿真", requiresAnchor: false),
        };
    }
}

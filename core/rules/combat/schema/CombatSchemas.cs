using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Rules.Combat
{
    /// <summary>
    /// <c>combat.hit_table_config</c>/<c>combat.resist_curve</c> 的 <see cref="TableSchema"/>
    /// 声明（见 06_规则层_属性技能战斗AI.md 第 4.2/4.3/4.7 节、04_数据与内容管线.md 第 1.1 节
    /// 表清单"命中表启用项配置""抗性/护甲到减免百分比的换算曲线"）。调用方在构造
    /// <see cref="Core.Foundation.DataRegistry.IDataRegistry"/> 后需要
    /// <c>RegisterSchema(CombatSchemas.HitTableConfig)</c>/<c>RegisterSchema(CombatSchemas.ResistCurve)</c>
    /// 才能加载对应数据文件（本模块不自动注册，惯例同 <c>StatSchemas</c>/<c>FacSchemas</c>）。
    /// </summary>
    public static class CombatSchemas
    {
        private static readonly string[] ResistCurveKindValues = { "saturation", "table" };

        /// <summary>
        /// 六个命中表分支（miss/dodge/parry/glancing_blow/block/crit）共用同一个字段形状：
        /// <c>{enabled: Bool, stat: Optional&lt;Id&gt;, base: Number}</c>（见 06 第 4.2 节）。
        /// 判断记录：本模块用 <see cref="FieldKind.Object"/> 声明这六个字段——04
        /// <c>data_registry</c> 对 <c>Object</c> 类型"只做存在且是对象检查"，六个分支各自的
        /// <c>enabled</c>/<c>stat</c>/<c>base</c> 子字段合法性（概率落在 [0,1]）由本模块自己的
        /// <see cref="CombatHitTableValidationRule"/> 校验，不复用 <c>data_registry</c> 的
        /// <c>field_type</c> 检查（该检查不支持嵌套对象内部字段）。
        /// </summary>
        public static readonly string[] HitTableBranchFields =
        {
            "miss", "dodge", "parry", "glancing_blow", "block", "crit",
        };

        /// <summary>六个命中表分支共用的子字段清单（<see cref="HitTableBranch.Parse"/> 权威解析：
        /// 三者均容错缺失/类型不符，非必填）。<c>stat</c> 登记为 <c>Reference(stat.definition)</c>：
        /// stat_block 属 L1，本模块（L2）依赖方向合法。概率 <c>base</c> 落在 [0,1] 这条业务判断
        /// 登记层无法表达，继续留在 <see cref="CombatHitTableValidationRule"/>。</summary>
        private static IReadOnlyList<FieldSchema> HitTableBranchFieldList() => new[]
        {
            new FieldSchema("enabled", FieldKind.Bool, required: false, description: "缺省 false"),
            new FieldSchema("stat", FieldKind.Reference, required: false, referenceTable: "stat.definition",
                description: "提供时概率取该属性当前值，缺省用 base 常量"),
            // 依据（ADR-0021）：CombatHitTableValidationRule 既有判断 "baseNum.Value < 0.0 ||
            // baseNum.Value > 1.0"（check "hit_table_base_range" 一类，见该类型），与字段自身文档
            // "须落在 [0,1]" 一致，补登为加载期 field_range；该规则保留（判断记录同 LootSchemas）。
            new FieldSchema("base", FieldKind.Number, required: false, description: "缺省 0；须落在 [0,1]，见 CombatHitTableValidationRule")
                .WithRange(FieldRange.Range(min: 0, max: 1)),
        };

        /// <summary>
        /// T-N1-8（ADR-0030 决策 6；06 第 4.2 节修订段）：<c>miss</c> 分支专用字段列表——在六分支
        /// 共用的 <see cref="HitTableBranchFieldList"/> 三字段基础上追加 <c>hit_stat</c>（攻击者命中
        /// 属性，见 <see cref="HitTableBranch.HitStat"/> 判断记录）。只有 <c>miss</c> 分支登记这个
        /// 额外字段——其余五分支的字段形状不变，避免内容作者误以为每个分支都能配 <c>hit_stat</c>。
        /// </summary>
        private static IReadOnlyList<FieldSchema> MissBranchFieldList()
        {
            var fields = new List<FieldSchema>(HitTableBranchFieldList())
            {
                new FieldSchema("hit_stat", FieldKind.Reference, required: false, referenceTable: "stat.definition",
                    description: "T-N1-8：攻击者命中属性（06 第 4.2 节修订段：未命中率 = 基础未命中 − " +
                        "攻击者命中属性 + 未命中加成(Δ)）。提供时未命中率额外减去该属性当前值；目标闪避" +
                        "属性已由 dodge 分支承担，本字段不重复叠加，见 HitTableBranch.HitStat 判断记录"),
            };
            return fields;
        }

        public static TableSchema HitTableConfig { get; } = new TableSchema(
            name: "combat.hit_table_config",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "combat.hit_table.<name>"),
                new FieldSchema("miss", FieldKind.Object, required: true, fields: MissBranchFieldList(),
                    description: "{enabled, stat?, base, hit_stat?}，见 06 第 4.2 节；hit_stat 见 T-N1-8 判断记录"),
                new FieldSchema("dodge", FieldKind.Object, required: true, fields: HitTableBranchFieldList(),
                    description: "{enabled, stat?, base}，闪避判定分支，见 06 第 4.2 节"),
                new FieldSchema("parry", FieldKind.Object, required: true, fields: HitTableBranchFieldList(),
                    description: "{enabled, stat?, base}，招架判定分支，见 06 第 4.2 节"),
                new FieldSchema("glancing_blow", FieldKind.Object, required: true, fields: HitTableBranchFieldList(),
                    description: "{enabled, stat?, base}，偏斜命中（部分减伤）判定分支，见 06 第 4.2 节"),
                new FieldSchema("block", FieldKind.Object, required: true, fields: HitTableBranchFieldList(),
                    description: "{enabled, stat?, base}，格挡判定分支，见 06 第 4.2 节"),
                new FieldSchema("crit", FieldKind.Object, required: true, fields: HitTableBranchFieldList(),
                    description: "{enabled, stat?, base}，暴击判定分支，见 06 第 4.2 节"),
                new FieldSchema("crit_multiplier_stat", FieldKind.Id, required: false,
                    description: "暴击倍率来源属性，未提供时用 crit_multiplier_base"),
                // 依据（ADR-0021）：CombatHitTableValidationRule 既有判断
                // "critMul < 0.0"（check "hit_table_crit_multiplier_base_range"）。
                new FieldSchema("crit_multiplier_base", FieldKind.Number, required: false,
                    description: "暴击倍率默认值；具体默认由口味清单给出，本表只声明字段，缺省 2.0；非负")
                    .WithRange(FieldRange.Range(min: 0)),
                // 依据（ADR-0021）：CombatHitTableValidationRule 既有判断 "glancingPct < 0.0"（check
                // "hit_table_glancing_damage_pct_range"）——只检查非负，没有上限检查（字段文档写
                // "0~1" 但规则代码本身不卡 1 这一端，登记范围以运行期实际校验为准，不超出既有行为）。
                new FieldSchema("glancing_damage_pct", FieldKind.Number, required: false,
                    description: "偏斜命中时保留的伤害比例（0~1），非负")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("block_value_stat", FieldKind.Id, required: false,
                    description: "格挡固定减免量来源属性"),
            }).WithOwnership(SchemaLayer.Rules, "combat");

        public static TableSchema ResistCurve { get; } = new TableSchema(
            name: "combat.resist_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "combat.resist.<name>"),
                // 判断记录：school 字段不声明为 FieldKind.Reference——06 第 4.3 节只固定"输入护甲/
                // 抗性值、输出 0~1 减免百分比"这一契约，未规定 school 需要指向哪张登记表；
                // core/rules/skill 是并行开发的独立模块，本模块不预设它登记 school 定义表的表名，
                // 避免产生跨模块数据表依赖（任务书"并行注意"要求不引用 skill 具体类型，这里推广到
                // 不假设 skill 的数据表结构）。合法性只做 Id 格式检查。
                new FieldSchema("school", FieldKind.Id, required: true,
                    description: "school.<name>；school.physical 用护甲，其余用抗性，见 06 第 4.3 节"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: ResistCurveKindValues,
                    description: "saturation：饱和曲线；table：分段线性插值"),
                new FieldSchema("k", FieldKind.Number, required: false,
                    description: "kind=saturation 时的饱和系数：reduction = value / (value + k × attackerLevel)"),
                new FieldSchema("entries", FieldKind.Array, required: false,
                    item: new FieldSchema("<resist_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("value", FieldKind.Number, required: true,
                            description: "护甲/抗性输入值，分段插值的横坐标"),
                        // 依据（ADR-0021）：字段自身文档"对应的减免百分比（0~1）"；
                        // CombatResistCurveValidationRule 目前只检查 entries 的单调性，未检查逐项
                        // 数值范围（只有 max_reduction 有专门检查），登记范围据文档口径补齐，落地前
                        // 已用 validate_data.py --strict 核对现有框架/示例数据不违反。
                        new FieldSchema("reduction", FieldKind.Number, required: true,
                            description: "对应的减免百分比（0~1），分段插值的纵坐标")
                            .WithRange(FieldRange.Range(min: 0, max: 1)),
                    },
                    description: "分段线性插值的一个采样点 {value, reduction}"),
                    description: "kind=table 时 [{value, reduction}, ...]，按 value 升序（非空/单调性" +
                        "业务判断留在 CombatResistCurveValidationRule；kind 与 entries/k 是表顶层平级" +
                        "字段，Variants 不适用——同 GobjSchemas.TypeDataSchema 判断记录，条件必填继续" +
                        "由该规则表达）"),
                // 依据（ADR-0021）：CombatResistCurveValidationRule 既有判断
                // "maxReduction < 0.0 || maxReduction > 1.0"（check "resist_curve_max_reduction_range"）。
                new FieldSchema("max_reduction", FieldKind.Number, required: false,
                    description: "减免上限，缺省 0.75，须落在 [0,1]")
                    .WithRange(FieldRange.Range(min: 0, max: 1)),
            }).WithOwnership(SchemaLayer.Rules, "combat");

        /// <summary>
        /// T-N1-8（ADR-0030 决策 6；06 第 4.2 节修订段；04 第 1.1 节表清单"combat.level_diff_table"
        /// 行）：等级差规则表——三条曲线加一条界线，横轴 Δ = 目标有效等级 − 攻击者有效等级（双向
        /// 生效，可负）。<c>miss_bonus</c>/<c>crit_suppression</c>/<c>xp_factor</c> 三条横轴都是 Δ
        /// （<see cref="CurveAxis.LevelDiff"/>）；<c>grey_line</c> 单独一条，横轴是攻击者有效等级本身
        /// （<see cref="CurveAxis.Level"/>，见该字段判断记录）。
        /// <para>
        /// 判断记录：
        /// </para>
        /// <list type="number">
        /// <item><description><b><c>xp_factor</c>/<c>grey_line</c> 本任务只登记 schema 与样例，不在
        /// <c>Resolver</c> 内求值消费</b>：04 第 1.1 节该表行把"经验系数""灰名界线"与"未命中加成""暴击
        /// 压制"并列为同一张表的四列，但经验系数、灰名（目标名字五色）的实际消费者是阶段 N4
        /// Progression 模块（尚未落地）与表现层——分阶段落地计划阶段 N1 任务清单原文"经验系数与灰名
        /// 界线两条曲线本任务只登记 schema 与样例，消费者在 N4（Progression）接入"，本模块（combat，
        /// L2）不依赖尚不存在的 progression 消费逻辑，只保证数据形状先落地、供 N4 直接引用同一张表，
        /// 不必再迁移一次。</description></item>
        /// <item><description><b><c>grey_line</c> 横轴取 <see cref="CurveAxis.Level"/> 而非
        /// <see cref="CurveAxis.LevelDiff"/>，纵轴是"允许的 Δ 下界绝对值"而不是 Δ 下界本身</b>：
        /// 04_经验.md"等级差衰减"与 06 第 4.2 节原文只给出定性描述"灰名界线随攻击者等级放宽"，未给出
        /// 具体公式（本任务契约缺口，见本模块 README"契约缺口/未决问题"一节，标"待设计层确认"）。本表
        /// 选取的最简形态：横轴是攻击者有效等级本身（打怪的那一方等级越高，灰名容许的等级差越宽），
        /// 纵轴登记为非负的"允许 Δ 下界绝对值"（消费者用 <c>-y</c> 还原成 Δ 下界：低于该 Δ 视为灰名），
        /// 而不是直接登记可能为负的 Δ 下界本身——04 第 5 节"曲线单调有限"阻断规则要求全部断点表曲线
        /// 纵轴沿横轴不递减（<c>CurveMonotonicFiniteRule</c>），"随等级放宽"意味着允许的差值幅度随
        /// 等级增大而增大，登记为非负幅度天然满足这条通用规则；若直接登记 Δ 下界本身（随等级增大而
        /// 更负），会与"纵轴不递减"这条通用阻断规则冲突，因此不选后者。真实消费公式（灰名门槛具体如何
        /// 从这条曲线换算）留给阶段 N4 实现时确认，本任务只保证这一形态在 04 第 5 节的通用校验下不
        /// 阻断。</description></item>
        /// <item><description><b>不新增专属校验规则</b>：四条曲线都是标准断点表形态
        /// （<see cref="CurveSchema.BreakpointsField"/>），单调有限由全局注册的
        /// <c>CurveMonotonicFiniteRule</c>（<c>presentation/assembly/PresentationSchemaCatalog</c>）
        /// 统一覆盖，<c>xp_factor</c>/<c>grey_line</c> 的纵轴非负由 <see cref="FieldRange"/> 登记覆盖，
        /// 不需要为本表单开一条 <c>CombatValidationRules</c> 规则（同 <c>combat.hit_table_config</c>/
        /// <c>combat.resist_curve</c> 仍保留各自规则的原因不同——那两张表有登记层表达不了的业务判断，
        /// 本表没有）。</description></item>
        /// </list>
        /// </summary>
        public static TableSchema LevelDiffTable { get; } = new TableSchema(
            name: "combat.level_diff_table",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "combat.level_diff.<name>"),
                CurveSchema.BreakpointsField("miss_bonus", CurveAxis.LevelDiff, required: true,
                    description: "未命中加成断点表：按 Δ 线性插值、越界夹取到端点（06 第 4.2 节修订段" +
                        "\"未命中率 = 基础未命中 − 攻击者命中属性 + 未命中加成(Δ)\"；ADR-0030 决策 6；" +
                        "推荐两段斜率：|Δ|<=2 每级加得少，>=3 陡增并封顶——数值由内容数据给出，本表只" +
                        "登记形态）",
                    xDescription: "Δ = 目标有效等级 − 攻击者有效等级，可为负",
                    yDescription: "叠加到未命中率上的加成，可为负（Δ<0 时通常为负，表示更容易命中）"),
                CurveSchema.BreakpointsField("crit_suppression", CurveAxis.LevelDiff, required: true,
                    description: "暴击压制断点表：按 Δ 线性插值、越界夹取到端点（06 第 4.2 节修订段" +
                        "\"暴击率 = 攻击者暴击属性 − 暴击压制(Δ)\"；ADR-0030 决策 6）",
                    xDescription: "Δ = 目标有效等级 − 攻击者有效等级，可为负",
                    yDescription: "从暴击率里扣减的压制量，可为负（Δ<0 时通常为负，表示更容易暴击）"),
                CurveSchema.BreakpointsField("xp_factor", CurveAxis.LevelDiff, required: true,
                    description: "经验系数断点表（04 第 1.1 节表清单；本任务只登记 schema 与样例，消费" +
                        "者在阶段 N4 Progression 接入，见本类型判断记录 1）",
                    xDescription: "Δ = 目标有效等级 − 攻击者有效等级，可为负",
                    yDescription: "经验倍率，非负",
                    yRange: FieldRange.Range(min: 0)),
                CurveSchema.BreakpointsField("grey_line", CurveAxis.Level, required: true,
                    description: "灰名界线断点表：随攻击者有效等级放宽（04_经验.md\"等级差衰减\"；本任务" +
                        "只登记 schema 与样例，消费公式待阶段 N4 确认，见本类型判断记录 2）",
                    xDescription: "攻击者有效等级",
                    yDescription: "灰名判定允许的 Δ 下界绝对值（消费者用 -y 还原 Δ 下界），非负、随等级不递减",
                    yRange: FieldRange.Range(min: 0)),
            }).WithOwnership(SchemaLayer.Rules, "combat");
    }
}

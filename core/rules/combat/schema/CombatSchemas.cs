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
            new FieldSchema("base", FieldKind.Number, required: false, description: "缺省 0；须落在 [0,1]，见 CombatHitTableValidationRule"),
        };

        public static TableSchema HitTableConfig { get; } = new TableSchema(
            name: "combat.hit_table_config",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "combat.hit_table.<name>"),
                new FieldSchema("miss", FieldKind.Object, required: true, fields: HitTableBranchFieldList(),
                    description: "{enabled, stat?, base}，见 06 第 4.2 节"),
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
                new FieldSchema("crit_multiplier_base", FieldKind.Number, required: false,
                    description: "暴击倍率默认值；具体默认由口味清单给出，本表只声明字段，缺省 2.0"),
                new FieldSchema("glancing_damage_pct", FieldKind.Number, required: false,
                    description: "偏斜命中时保留的伤害比例（0~1）"),
                new FieldSchema("block_value_stat", FieldKind.Id, required: false,
                    description: "格挡固定减免量来源属性"),
            });

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
                        new FieldSchema("reduction", FieldKind.Number, required: true,
                            description: "对应的减免百分比（0~1），分段插值的纵坐标"),
                    },
                    description: "分段线性插值的一个采样点 {value, reduction}"),
                    description: "kind=table 时 [{value, reduction}, ...]，按 value 升序（非空/单调性" +
                        "业务判断留在 CombatResistCurveValidationRule；kind 与 entries/k 是表顶层平级" +
                        "字段，Variants 不适用——同 GobjSchemas.TypeDataSchema 判断记录，条件必填继续" +
                        "由该规则表达）"),
                new FieldSchema("max_reduction", FieldKind.Number, required: false,
                    description: "减免上限，缺省 0.75"),
            });
    }
}

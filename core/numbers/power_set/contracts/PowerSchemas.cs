using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// 本模块拥有的数据表 schema（见 06_规则层_属性技能战斗AI.md 第 2.1 节、
    /// schema/README.md"表清单补录"判断记录）。
    /// </summary>
    public static class PowerSchemas
    {
        /// <summary><c>max_source.kind</c> 合法取值（<c>PowerTypeDefinition</c> 构造函数权威解析）。</summary>
        public static readonly string[] MaxSourceKindValues = { "fixed", "stat" };

        /// <summary><c>arch.power_type.max_source</c>：判别字段 <c>kind</c> 与被判别的 <c>value</c>/
        /// <c>stat</c> 同处一个对象内，Variants 适用。<c>stat</c> 登记为 <c>Reference(stat.definition)</c>：
        /// <c>stat_block</c>/<c>power_set</c> 同属 <c>Core.Numbers</c> 程序集（同层），登记为
        /// Reference 不违反分层（比 <c>PowerTypeDefinition</c> 当前只做 Id 格式校验更严格，属本轮
        /// 新增的引用完整性校验）。</summary>
        public static readonly FieldSchema MaxSourceSchema = new FieldSchema(
            "max_source", FieldKind.Object, required: true, variants: BuildMaxSourceVariants(),
            description: "{kind: fixed|stat, value: Number?, stat: Id?}");

        private static VariantSchema BuildMaxSourceVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(System.StringComparer.Ordinal)
            {
                ["fixed"] = new[]
                {
                    new FieldSchema("value", FieldKind.Number, required: true,
                        description: "kind=fixed 时的固定资源上限值"),
                },
                ["stat"] = new[]
                {
                    new FieldSchema("stat", FieldKind.Reference, required: true, referenceTable: "stat.definition",
                        description: "kind=stat 时引用 stat.definition，资源上限取该属性当前值"),
                },
            };
            return new VariantSchema("kind", cases);
        }

        /// <summary>
        /// <c>arch.power_type</c>：资源类型定义表（字段见 schema/README.md）。
        /// </summary>
        public static readonly TableSchema PowerType = new TableSchema(
            name: "arch.power_type",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "资源类型 id，格式 arch.power.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键"),
                MaxSourceSchema,
                new FieldSchema("regen_in_combat", FieldKind.Number, required: false,
                    description: "战斗内每时间单位回复量，默认 0"),
                new FieldSchema("regen_out_of_combat", FieldKind.Number, required: false,
                    description: "脱战每时间单位回复量，默认 0"),
                new FieldSchema("decay_out_of_combat", FieldKind.Number, required: false,
                    description: "脱战每时间单位衰减量，默认 0"),
                new FieldSchema("refill_on_leave_combat", FieldKind.Bool, required: false,
                    description: "脱战时是否立即回满，默认 false"),
                new FieldSchema("start_full", FieldKind.Bool, required: false,
                    description: "单位注册时资源是否初始为满，默认 true"),
                new FieldSchema("allow_overflow", FieldKind.Bool, required: false,
                    description: "是否允许超出上限，默认 false"),
                new FieldSchema("min", FieldKind.Number, required: false,
                    description: "下限，默认 0"),
            });
    }
}

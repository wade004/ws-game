using Core.Foundation.DataRegistry;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// 本模块拥有的数据表 schema（见 06_规则层_属性技能战斗AI.md 第 2.1 节、
    /// schema/README.md"表清单补录"判断记录）。
    /// </summary>
    public static class PowerSchemas
    {
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
                new FieldSchema("max_source", FieldKind.Object, required: true,
                    description: "上限来源：{kind: fixed|stat, value: Number?, stat: Id?}"),
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

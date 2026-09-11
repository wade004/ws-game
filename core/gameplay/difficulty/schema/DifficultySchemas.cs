using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Difficulty
{
    /// <summary>
    /// <c>diff.tier</c> 的 <see cref="TableSchema"/> 声明（见 08 第 5.1 节字段表 + 任务书拍板补录
    /// <c>name_key</c>、<c>sort_weight</c>，见 <see cref="DifficultyTierDefinition"/> 判断记录）。
    /// <para>
    /// 判断记录（<c>modifier_aura_refs</c>/<c>affix_pool_ref</c> 用 <see cref="FieldKind.IdList"/>/
    /// <see cref="FieldKind.Id"/> 而非 <see cref="FieldKind.Reference"/>）：与
    /// <c>core/carriers/creature/core/CreatureSchemas.cs</c> 判断记录同一取舍——它们指向的表
    /// （<c>aura.def</c>/词缀池表）不在本任务数据集范围内，声明为 Reference 会让
    /// <c>reference_integrity</c> 校验项因目标表未加载而恒报错，阻断测试数据通过校验。
    /// </para>
    /// </summary>
    public static class DifficultySchemas
    {
        public static readonly TableSchema Tier = new TableSchema(
            name: "diff.tier",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "diff.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键（08 第 5.1 节未列出，任务书拍板补录）"),
                new FieldSchema("modifier_aura_refs", FieldKind.IdList, required: false,
                    description: "施加给该难度下敌对单位（或全体单位）的修正光环")
                    .WithFreeIds("指向 skill.aura_def，但该表不在本模块既有测试装配的数据集范围内，声明为引用会让 reference_integrity 因目标表未加载而恒报错，见类型判断记录"),
                new FieldSchema("affix_pool_ref", FieldKind.Id, required: false,
                    description: "词缀池的挂载位标识（本版未定义词缀池表，不登记 SoftReferenceTable，仅登记挂载点；消费方反馈第 30 条核实）"),
                new FieldSchema("loot_multiplier", FieldKind.Number, required: true,
                    description: "掉落数量/概率的整体倍率"),
                new FieldSchema("sort_weight", FieldKind.Number, required: false,
                    description: "仅供内容管线排序展示，运行期不读取，缺省 0"),
            }).WithOwnership(SchemaLayer.Gameplay, "difficulty");
    }
}

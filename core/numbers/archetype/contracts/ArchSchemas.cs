using Core.Foundation.DataRegistry;

namespace Core.Numbers.Archetype
{
    /// <summary>
    /// <c>arch.class</c> / <c>arch.race</c> / <c>arch.talent_tree</c> 的 <see cref="TableSchema"/>
    /// （见 01_分层与依赖.md L1 模块表 <c>archetype</c> 行、04_数据与内容管线.md 第 1.1 节表清单
    /// "职业模板：主属性、资源类型、技能书、天赋树引用" / "种族模板：被动光环引用、基础属性修正" /
    /// "天赋树结构：节点、前置、天赋点消耗"、06_规则层_属性技能战斗AI.md 第 2.1 节"arch.class
    /// 引用一组资源类型定义"、07_载体层_物品生物物件.md 第 5 节"职业 = 主属性引用 + 资源类型
    /// 引用 + 技能书 + 天赋树引用"）。
    /// <para>
    /// 判断记录：04 未给出这三张表的完整字段表，字段为实现期按任务书 T2-3 给出的最小字段集
    /// 补录，取舍见 <c>schema/README.md</c>——尤其是"哪些跨模块引用字段暂不声明为
    /// <see cref="FieldKind.Reference"/>"一节：<c>stat_block</c>（<c>stat.definition</c>）、
    /// <c>power_set</c>（资源类型定义）、L2 <c>skill</c>（<c>skill.book</c>/<c>skill.aura.*</c>）
    /// 均与本任务并行开发或尚未实现，本模块不静态耦合它们的表结构，只把字段声明为普通
    /// <see cref="FieldKind.Id"/>/<see cref="FieldKind.IdList"/>；待对应模块登记 schema 后，
    /// 集成方可用 <see cref="IDataRegistry.DeclareReference"/> 动态补上引用完整性检查，不需要
    /// 改动本模块的 <see cref="TableSchema"/>。<c>talent_tree_ref</c>/<c>level_curve_ref</c>
    /// 引用的表（<c>arch.talent_tree</c>、<c>prog.level_curve</c>）都在本任务范围内，直接声明为
    /// <see cref="FieldKind.Reference"/>。
    /// </para>
    /// </summary>
    public static class ArchSchemas
    {
        public static readonly TableSchema Class = new TableSchema(
            name: "arch.class",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("primary_stat", FieldKind.Id, required: true,
                    description: "引用 stat.*；stat_block 模块尚未登记 schema，暂不声明为 Reference"),
                new FieldSchema("base_stats", FieldKind.Object, required: true,
                    description: "Object<stat_id, Number>，ApplyTo 经 StatBaseWriter 写入"),
                new FieldSchema("power_types", FieldKind.IdList, required: true,
                    description: "引用 arch.power.*；power_set 模块尚未登记 schema，暂不声明为 Reference"),
                new FieldSchema("skill_book_ref", FieldKind.Id, required: false,
                    description: "引用 skill.book.*；L2 skill 模块尚未实现，暂不声明为 Reference"),
                new FieldSchema("talent_tree_ref", FieldKind.Reference, required: false, referenceTable: "arch.talent_tree"),
                new FieldSchema("level_curve_ref", FieldKind.Reference, required: false, referenceTable: "prog.level_curve"),
            });

        public static readonly TableSchema Race = new TableSchema(
            name: "arch.race",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("stat_mods", FieldKind.Object, required: true,
                    description: "Object<stat_id, Number>，ApplyTo 经 StatModifierWriter 以 flat 写入，来源为种族 id"),
                new FieldSchema("passive_auras", FieldKind.IdList, required: false,
                    description: "引用 skill.aura.*；L2 skill 模块尚未实现，暂不声明为 Reference；本模块只保存不应用"),
            });

        public static readonly TableSchema TalentTree = new TableSchema(
            name: "arch.talent_tree",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("nodes", FieldKind.Array, required: true,
                    description: "Array<{id:String, prerequisites:[String], cost:Int, grants:Object}>；" +
                        "前置存在性与无环由 ArchTalentTreeCycleValidationRule 校验"),
            });
    }
}

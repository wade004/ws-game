using Core.Foundation.DataRegistry;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <c>creature.template</c> / <c>creature.tier_definition</c> 的 <see cref="TableSchema"/>
    /// 声明（见 07_载体层_物品生物物件.md 第 2.1 节字段表；<c>creature.tier_definition</c> 是
    /// 07 未展开、任务书拍板补录的表，见判断记录）。
    /// <para>
    /// 判断记录（<c>creature.tier_definition</c> 补录）：07 第 2.1 节 <c>tier</c> 字段只说明"强度
    /// 分档（如 normal/elite/rare/boss，由数据定义具体分档集合）"，未给出该表的字段结构；任务书
    /// 按落地方案拍板补录字段：<c>id</c>（<c>creature.tier.&lt;name&gt;</c>）、<c>name_key</c>、
    /// <c>stat_multiplier</c>（默认 1，供 <see cref="CreatureFactory"/> 按 <c>base_stats × 该值</c>
    /// 换算最终基础属性）、<c>control_immune</c>（06 第 3.9 节"Boss/精英级免疫标志"的落地位）、
    /// <c>sort_weight</c>（仅供内容管线/编辑器排序展示，运行期 <see cref="CreatureFactory"/> 不读取
    /// 该字段，因此本模块代码内不为它声明对应的运行期存储字段）。
    /// </para>
    /// <para>
    /// 判断记录（<c>ai_rotation_ref</c>/<c>ai_behavior_ref</c>/<c>loot_table_ref</c>/<c>display_ref</c>
    /// 用 <see cref="FieldKind.Id"/> 而非 <see cref="FieldKind.Reference"/>）：这四个字段指向的表
    /// （<c>ai.rotation</c>/<c>ai.behavior_profile</c>/<c>loot.table</c>/<c>display.map</c>）均不在
    /// 本次任务的数据集范围内（本任务只新建 <c>core/carriers/creature</c>/<c>core/carriers/summon</c>
    /// 两个目录，不新增/加载其它模块的数据表），若声明为 <see cref="FieldKind.Reference"/>，
    /// <c>reference_integrity</c> 校验项会因目标表未加载而恒报错，阻断任何测试数据通过校验；
    /// <c>stat_growth_ref</c> 则不同——它引用的 <c>prog.level_curve</c> 表结构已由
    /// <c>core/numbers/progression</c>（同一并行任务集里较早完成、且本模块运行期本就需要独立解析
    /// 该表来源用于成长累加，见 <c>CreatureFactory</c>）明确给出，声明为 <see cref="FieldKind.Reference"/>
    /// 更贴合"该表引用应存在"的语义，测试装配时按需加载该表即可满足校验。消费方反馈第 29 条：
    /// 四者虽不参与加载期引用完整性校验，但目标表名确定（见上），已各自登记
    /// <see cref="FieldSchema.WithSoftReference"/>——只传达"这个 Id 语义上指向哪张表"给内容工具做
    /// 自动补全/跳转，不改变本判断记录说明的分层边界与校验范围。<c>on_hit_reaction_ref</c>/
    /// <c>on_death_reaction_ref</c> 不在此列：消费方反馈第 29 条全仓核实无任何消费方读取这两个
    /// 字段、也没有对应的目标表，按"预留字段"处理，不登记软引用（见各自 Description）。
    /// </para>
    /// </summary>
    public static class CreatureSchemas
    {
        public static readonly TableSchema Template = new TableSchema(
            name: "creature.template",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "creature.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键（07 第 2.1 节未列出，任务书拍板补录）"),
                new FieldSchema("level", FieldKind.Int, required: true,
                    description: "生物等级，配合 stat_growth_ref 计算随等级增长的最终属性"),
                new FieldSchema("tier", FieldKind.Reference, required: true,
                    referenceTable: "creature.tier_definition",
                    description: "强度分档引用"),
                new FieldSchema("base_stats", FieldKind.Object, required: true,
                    description: "Map<StatKey, Number>")
                    .WithMap(MapSchema.ReferenceKeyTable("stat.definition",
                        new FieldSchema("value", FieldKind.Number, required: true,
                            description: "该属性的基础值，CreatureTemplate.FromRecord 逐键 Id.TryParse + JsonNumber 解析，非法即抛 DataFieldException（ADR-0024 第二批登记，键改为加载期即报 reference_integrity，早于该异常）"))),
                new FieldSchema("stat_growth_ref", FieldKind.Reference, required: false,
                    referenceTable: "prog.level_curve",
                    description: "与 prog.level_curve 同类结构，供生物按等级成长属性"),
                new FieldSchema("faction_id", FieldKind.Id, required: true,
                    description: "所属阵营 id，决定与其它生物/角色的敌对关系"),
                new FieldSchema("npc_flags", FieldKind.IdList, required: false,
                    description: "职能标志位，取值为 npc_flag.<name> 形式，见 07 第 2.2 节六值（消费方反馈第 28 条：由 NpcFlagIds.All 登记为固定取值集合，取代此前手写的 CreatureContentValidationRule 校验）")
                    .WithAllowedValues(NpcFlagIds.All),
                new FieldSchema("ai_rotation_ref", FieldKind.Id, required: false,
                    description: "指向 ai.rotation 的技能循环配置，可为空；跨模块不做引用完整性校验（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "ai.rotation"),
                new FieldSchema("ai_behavior_ref", FieldKind.Id, required: false,
                    description: "指向 ai.behavior_profile 的行为配置，可为空；跨模块不做引用完整性校验（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "ai.behavior_profile"),
                new FieldSchema("loot_table_ref", FieldKind.Id, required: false,
                    description: "指向 loot.table 的掉落表，可为空；跨模块不做引用完整性校验（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "loot.table"),
                new FieldSchema("display_ref", FieldKind.Id, required: true,
                    description: "指向 display.map 的显示资源（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "display.map"),
                new FieldSchema("immunities", FieldKind.IdList, required: false,
                    description: "免疫的学派/效果类型/控制类别")
                    .WithFreeIds("混合词汇（学派/效果类型/控制类别），不指向单一已登记表的既有记录"),
                new FieldSchema("on_hit_reaction_ref", FieldKind.Id, required: false,
                    description: "预留：受击时触发的反应配置引用。消费方反馈第 29 条核实全仓无任何消费方读取该字段，也没有对应的目标表——当前填写不生效，不登记软引用；待反应系统落地并确定目标表后再补登记"),
                new FieldSchema("on_death_reaction_ref", FieldKind.Id, required: false,
                    description: "预留：死亡时触发的反应配置引用。消费方反馈第 29 条核实全仓无任何消费方读取该字段，也没有对应的目标表——当前填写不生效，不登记软引用；待反应系统落地并确定目标表后再补登记"),
            }).WithOwnership(SchemaLayer.Carriers, "creature");

        public static readonly TableSchema TierDefinition = new TableSchema(
            name: "creature.tier_definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "creature.tier.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "分档显示名文本键"),
                new FieldSchema("stat_multiplier", FieldKind.Number, required: false,
                    description: "缺省 1"),
                new FieldSchema("control_immune", FieldKind.Bool, required: false,
                    description: "Boss/精英级免疫标志（06 第 3.9 节），缺省 false"),
                new FieldSchema("sort_weight", FieldKind.Number, required: false,
                    description: "仅供内容管线排序展示，运行期不读取，缺省 0"),
            }).WithOwnership(SchemaLayer.Carriers, "creature");
    }
}

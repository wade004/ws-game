using Core.Foundation.DataRegistry;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <c>skill.*</c> 五张表的 <see cref="TableSchema"/> 声明（见 06 第 3.1/3.3/3.4/3.5 节字段表、
    /// 04 第 1.1 节表清单、schema/README.md 字段表）。调用方在构造
    /// <see cref="Core.Foundation.DataRegistry.IDataRegistry"/> 后需要
    /// <c>RegisterSchema(SkillSchemas.Def)</c> 等依次注册才能加载对应数据文件（本模块不自动
    /// 注册，注册时机由宿主统一掌控，与 <c>power_set.PowerSchemas</c>/<c>stat_block.StatSchemas</c>
    /// 同一惯例）。字段类型说明见 schema/README.md。
    /// </summary>
    public static class SkillSchemas
    {
        /// <summary><c>kind</c> 字段合法取值（06 第 3.1 节）。</summary>
        public static readonly string[] SkillKindValues = { "active", "passive" };

        /// <summary><c>interrupt_flags[]</c> 元素合法取值（06 第 3.1 节"movement|damage_taken|..."，
        /// 任务书补齐第三项 <c>control</c>）。</summary>
        public static readonly string[] InterruptFlagValues = { "movement", "damage_taken", "control" };

        public static TableSchema Def { get; } = new TableSchema(
            name: "skill.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.<name>"),
                new FieldSchema("school", FieldKind.Id, required: true, description: "学派"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: SkillKindValues,
                    description: "active|passive"),
                new FieldSchema("range", FieldKind.Number, required: true, description: "射程，0 表示无限制/作用于自身"),
                new FieldSchema("tags", FieldKind.IdList, required: false, description: "标签集合"),
                new FieldSchema("cast_time", FieldKind.Number, required: true, description: "读条时间，0 表示瞬发"),
                new FieldSchema("channel_time", FieldKind.Number, required: false, description: "引导时长，与 cast_time 互斥"),
                new FieldSchema("cost", FieldKind.Array, required: false,
                    description: "[{power_type: Id, amount: Number}, ...]"),
                new FieldSchema("cooldown_category", FieldKind.Id, required: false, description: "冷却分类引用"),
                new FieldSchema("cooldown_duration", FieldKind.Number, required: false, description: "冷却时长，缺省 0"),
                new FieldSchema("charges", FieldKind.Object, required: false,
                    description: "{max: Int, recharge_time: Number}"),
                new FieldSchema("action_cost", FieldKind.Number, required: false, description: "离散模式行动点消耗"),
                new FieldSchema("respects_gcd", FieldKind.Bool, required: true, description: "是否受公共冷却影响"),
                new FieldSchema("target_shape_ref", FieldKind.Id, required: true,
                    description: "指向 target.chain_def（本模块按此语义解析，见 README）"),
                new FieldSchema("effects", FieldKind.Array, required: true,
                    description: "[{kind: String, params: Object}, ...]"),
                new FieldSchema("interrupt_flags", FieldKind.Array, required: false,
                    description: "[movement|damage_taken|control, ...]"),
            });

        public static TableSchema AuraDef { get; } = new TableSchema(
            name: "skill.aura_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.aura_def.<name>"),
                new FieldSchema("duration", FieldKind.Number, required: false, description: "空表示永久直到被移除"),
                new FieldSchema("max_stacks", FieldKind.Int, required: false, description: "缺省 1"),
                new FieldSchema("stack_category", FieldKind.Id, required: false, description: "叠加冲突检测用类别"),
                new FieldSchema("dispel_type", FieldKind.Id, required: false, description: "供 dispel 效果按类别筛选"),
                new FieldSchema("effects", FieldKind.Array, required: true,
                    description: "[{kind: String, params: Object}, ...]，kind 取值见 06 第 3.3 节"),
            });

        public static TableSchema ProcDef { get; } = new TableSchema(
            name: "skill.proc_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.proc_def.<name>"),
                new FieldSchema("trigger_event", FieldKind.Id, required: true, description: "监听的事件 key"),
                new FieldSchema("condition", FieldKind.Expr, required: false, description: "触发条件"),
                new FieldSchema("trigger_skill", FieldKind.Id, required: true, description: "触发后释放的技能"),
                new FieldSchema("internal_cooldown", FieldKind.Number, required: false, description: "触发器自身冷却"),
                new FieldSchema("proc_chance", FieldKind.Number, required: true, description: "触发概率 0~1"),
            });

        public static readonly string[] SpellModDimensionValues =
            { "cast_time", "cost", "cooldown", "crit_chance", "effect_value", "charges" };

        public static readonly string[] SpellModOpValues = { "flat", "pct" };

        public static TableSchema SpellModDef { get; } = new TableSchema(
            name: "skill.spell_mod_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.spell_mod_def.<name>"),
                new FieldSchema("target_dimension", FieldKind.Enum, required: true, enumValues: SpellModDimensionValues),
                new FieldSchema("op", FieldKind.Enum, required: true, enumValues: SpellModOpValues),
                new FieldSchema("value", FieldKind.Number, required: true),
                new FieldSchema("affects", FieldKind.Object, required: false,
                    description: "{schools: [Id], tags: [Id], skill_ids: [Id]}"),
            });

        public static TableSchema Book { get; } = new TableSchema(
            name: "skill.book",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.book.<name>"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    description: "[{level: Int, skill_id: Id}, ...]"),
            });
    }
}

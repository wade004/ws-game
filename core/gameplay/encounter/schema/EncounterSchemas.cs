using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Quest;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def</c>/<c>encounter.level</c> 的 <see cref="TableSchema"/> 声明（见 08 第
    /// 4.1、4.2 节全部字段、schema/README.md 字段表）。
    /// <para>
    /// ADR-0019 首批登记（F1b）：<c>units</c>/<c>waves</c>/<c>phases</c>/<c>arena_rules</c>/
    /// <c>initiative_override</c> 五个复合字段的子结构（<see cref="FieldSchema.Fields"/>/
    /// <see cref="FieldSchema.Item"/>/<see cref="FieldSchema.Variants"/>）见下 <c>UnitItemSchema</c>/
    /// <c>WaveItemSchema</c>/<c>PhaseItemSchema</c>/<c>BoundsShapeSchema</c>/
    /// <c>InitiativeOverrideSchema</c>，逐参数对照与判断记录完整列在 schema/README.md"子结构登记表"
    /// 一节；本类型内注释只点出登记层面的取舍要点，不重复完整论证。<c>waves[].trigger_condition</c>/
    /// <c>phases[].enter_condition</c> 两处嵌套 Expr 字段登记后由 <c>DataRegistry</c> 递归校验覆盖，
    /// <see cref="EncounterContentValidationRule"/> 里原本手写的对应部分已退役（见该类型判断记录、
    /// schema/README.md"退役规则"一节）。<c>units[]</c> 的 <c>spawn_ref</c>/<c>template_ref</c> 二选一、
    /// <c>spawn_ref</c>/<c>spawn_refs[]</c> 的 domain 校验属登记表达不了的跨字段/跨表业务判断，继续
    /// 保留在 <see cref="EncounterContentValidationRule"/>。
    /// </para>
    /// <para>
    /// 判断记录（<c>encounter.level.encounter_sequence</c> 未升级为 Array+Item=Reference）：该字段是
    /// 有序 <c>encounter.def</c> 引用列表，但不含嵌套 Object/Array 结构——ADR-0019 的 Fields/Item/
    /// Variants 机制面向"复合字段的子结构"（04 第 3.2 节），<see cref="FieldKind.IdList"/> 本身不支持
    /// 挂载 Item（见 <see cref="FieldSchema"/> 构造期"Item 仅 Array 字段可设"检查），把它改造成
    /// <see cref="FieldKind.Array"/> + <c>Item=Reference(encounter.def)</c> 属于额外升级
    /// （用引用完整性替换纯格式校验），不是"给复合字段登记缺失的子结构"，超出本轮任务书点名的范围
    /// （任务书原句"encounter.level 的有序遭遇序列若为复合"——核实后结论是不复合，见本判断记录），
    /// 且会让"encounter.level 与 encounter.def 必须同一个 DataRegistry 里加载"成为强约束，与
    /// <c>entry_difficulty_options</c>（同样是 IdList、指向 <c>diff.tier</c>，两者应保持一致处理）
    /// 一起维持现状，不在本轮变更。
    /// </para>
    /// </summary>
    public static class EncounterSchemas
    {
        private static readonly string[] CombatModeValues = { "continuous", "discrete" };

        /// <summary><c>arena_rules.bounds_shape.kind</c> 合法取值（见
        /// <see cref="Core.Foundation.EngineAdapter.ShapeKind"/> 四值、<c>EncounterShapeJson.Parse</c>
        /// switch 分支；与 <c>AreaTriggerSchemas.ShapeKindValues</c> 同源但本模块不跨目录复用该常量，
        /// 避免引入 encounter↔area_trigger 的横向耦合，见 <c>EncounterSchemaCoverageTests</c> 用测试
        /// 锁死本数组与 <see cref="Core.Foundation.EngineAdapter.ShapeKind"/> 全集一致）。</summary>
        public static readonly string[] BoundsShapeKindValues = { "circle", "cone", "line", "rect" };

        /// <summary><c>initiative_override.policy</c> 合法取值（见 <c>TimeModelSwitch.SetPendingOverride</c>
        /// switch 分支——未知取值该方法直接抛 <see cref="InvalidOperationException"/>，登记为 Enum 把这类
        /// 拼写错误提前到数据加载期拦下）。</summary>
        public static readonly string[] InitiativeOverridePolicyValues = { "initiative_stat", "action_points", "fixed_order" };

        // -----------------------------------------------------------------
        // units[]：EncounterDefinition.ParseUnit 唯一权威。spawn_ref/template_ref 二选一是跨字段
        // 业务规则，FieldSchema.Fields 无法表达"二选一"，继续留给 EncounterContentValidationRule；
        // 本登记只表达"两者均为可选标量，position 可选 Vec2"。
        // -----------------------------------------------------------------
        public static readonly FieldSchema UnitItemSchema = new FieldSchema(
            "<unit>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("spawn_ref", FieldKind.Id, required: false,
                    description: "spawn.table 条目引用，与 template_ref 二选一（EncounterContentValidationRule 校验）。" +
                        "判断记录：domain 须为 spawn，但退回 Id 而非 Reference(ReferenceDomain: \"spawn\")——" +
                        "本模块与 core/gameplay/spawn 之间刻意只通过 SpawnRequester 委托解耦，不直接依赖该模块" +
                        "（见 README 判断记录 2），Reference 的跨表存在性检查会与这一决耦意图冲突；domain 校验" +
                        "本身继续保留在 EncounterContentValidationRule。"),
                new FieldSchema("template_ref", FieldKind.Reference, required: false, referenceTable: "creature.template",
                    description: "内联生成的生物模板，与 spawn_ref 二选一。creature.template 属 L3，encounter（L4）" +
                        "经既有 Core.Gameplay.csproj→Core.Carriers 项目引用可 Reference（04 §5.1 口径）；" +
                        "CreatureFactory.Spawn 对未知模板硬抛 ArgumentException（RequireTemplate），登记为 Reference" +
                        "与运行时语义一致，不同于 spawn_ref 的决耦考虑。"),
                new FieldSchema("position", FieldKind.Vec2, required: false,
                    description: "内联模板缺省时退化为 Vec2.Zero（见 EncounterUnitSpec 判断记录）；spawn_ref 来源忽略本字段。"),
            },
            description: "{spawn_ref?: Id, template_ref?: Reference(creature.template), position?: Vec2}");

        // -----------------------------------------------------------------
        // waves[]：EncounterDefinition.ParseWave 唯一权威。
        // -----------------------------------------------------------------
        public static readonly FieldSchema WaveItemSchema = new FieldSchema(
            "<wave>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("trigger_condition", FieldKind.Expr, required: true,
                    description: "波次触发条件；ADR-0019 起随 Array.Item 递归被内置 expr_parsable 校验项覆盖，" +
                        "EncounterContentValidationRule 原手写的同名检查已退役（见该类型判断记录）。"),
                new FieldSchema("spawn_refs", FieldKind.Array, required: false,
                    item: new FieldSchema("<spawn_ref>", FieldKind.Id, required: true,
                        description: "spawn.table 条目引用；退回 Id，domain 校验保留在 EncounterContentValidationRule，判断记录同 units[].spawn_ref"),
                    description: "spawn.table 条目引用列表；判断记录同 units[].spawn_ref——退回 Id，" +
                        "domain 校验保留在 EncounterContentValidationRule。"),
            },
            description: "{trigger_condition: Expr, spawn_refs?: [Id, ...]}");

        // -----------------------------------------------------------------
        // phases[]：EncounterDefinition.ParsePhase 唯一权威。
        // -----------------------------------------------------------------
        public static readonly FieldSchema PhaseItemSchema = new FieldSchema(
            "<phase>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("enter_condition", FieldKind.Expr, required: true,
                    description: "阶段进入条件；ADR-0019 起随 Array.Item 递归被内置 expr_parsable 校验项覆盖，" +
                        "EncounterContentValidationRule 原手写的同名检查已退役（见该类型判断记录）。"),
                new FieldSchema("ai_rotation_override", FieldKind.Object, required: false,
                    description: "Map<Id, Id>（键为具体参战单位 id 或模板 id，见 EncounterHost.ApplyPhase 判断记录）。" +
                        "判断记录（Map 型待后续契约扩展，ADR-0019 首批范围外）：现有 FieldSchema.Fields 只表达固定键" +
                        "清单，无法表达任意键的 Map；本轮不为此扩展 FieldKind，本字段维持\"存在且是对象\"的向后兼容" +
                        "校验（见 schema/README.md\"Map 型待后续契约扩展\"一节）。"),
                new FieldSchema("on_enter_hook", FieldKind.Id, required: false,
                    description: "found.hook 钩子 id。判断记录（分层边界，同 skill.script.hook_id）：found.hook" +
                        "当前无实现级 schema 登记，Reference 到未加载表恒判定引用失效，暂退回 Id，待补齐登记后再升级。"),
            },
            description: "{enter_condition: Expr, ai_rotation_override?: Map<Id, Id>, on_enter_hook?: Id}");

        // -----------------------------------------------------------------
        // arena_rules.bounds_shape：EncounterShapeJson.Parse 唯一权威，按 kind 分派四种形状。
        // -----------------------------------------------------------------
        private static VariantSchema BuildBoundsShapeVariants()
        {
            var origin = new FieldSchema("origin", FieldKind.Vec2, required: true,
                description: "circle 的 center；cone/line/rect 的 origin（EncounterShapeJson.ParseVec2 命名统一为 origin）。");

            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["circle"] = new[]
                {
                    origin,
                    new FieldSchema("radius", FieldKind.Number, required: true,
                        description: "圆形场地半径"),
                },
                ["cone"] = new[]
                {
                    origin,
                    new FieldSchema("direction", FieldKind.Number, required: true,
                        description: "扇形朝向角，弧度制（EncounterShapeMath 角度统一按弧度制处理）"),
                    new FieldSchema("angle", FieldKind.Number, required: true,
                        description: "扇形张角，弧度制"),
                    new FieldSchema("radius", FieldKind.Number, required: true,
                        description: "扇形半径"),
                },
                ["line"] = new[]
                {
                    origin,
                    new FieldSchema("direction", FieldKind.Number, required: true,
                        description: "条形朝向角，弧度制（EncounterShapeMath 角度统一按弧度制处理）"),
                    new FieldSchema("length", FieldKind.Number, required: true,
                        description: "条形沿朝向方向的长度"),
                    new FieldSchema("width", FieldKind.Number, required: true,
                        description: "条形垂直于朝向方向的宽度"),
                },
                ["rect"] = new[]
                {
                    origin,
                    new FieldSchema("half_extents", FieldKind.Vec2, required: true,
                        description: "矩形场地半宽半高（各轴方向从中心到边界的距离）"),
                    new FieldSchema("rotation", FieldKind.Number, required: true,
                        description: "矩形绕 origin 的旋转角，弧度制"),
                },
            };

            return new VariantSchema("kind", cases);
        }

        /// <summary><c>arena_rules.bounds_shape</c>：<c>EncounterShapeJson.RequireNumber</c>/
        /// <c>ParseVec2</c> 对每种形状的全部子字段均无缺省值（缺失即抛 <see cref="FormatException"/>），
        /// 因此四个分支下的全部子字段均登记为必填。</summary>
        public static readonly FieldSchema BoundsShapeSchema = new FieldSchema(
            "bounds_shape", FieldKind.Object, required: true, variants: BuildBoundsShapeVariants(),
            description: "{kind: circle|cone|line|rect, origin, ...视 kind 而定}，见 EncounterShapeJson.Parse");

        private static readonly IReadOnlyList<FieldSchema> InitiativeOverrideParamsFields = new[]
        {
            new FieldSchema("initiative_stat", FieldKind.Reference, required: false, referenceTable: "stat.definition",
                description: "覆盖离散模式先攻属性（TimeModelSwitch.EffectiveInitiativeStat）；stat.definition 属 L1，" +
                    "encounter（L4）可 Reference。"),
            new FieldSchema("action_points_per_turn", FieldKind.Number, required: false,
                description: "覆盖离散模式每回合行动点上限。"),
        };

        /// <summary><c>encounter.def.initiative_override</c>：仅 <c>combat_mode_override == "discrete"</c>
        /// 时有意义，见 <c>TimeModelSwitch.SetPendingOverride</c>。<c>policy</c> 未知取值该方法直接抛
        /// <see cref="InvalidOperationException"/>（不同于 <c>combat_mode_override</c> 的宽容降级），
        /// 登记为 Enum 把拼写错误提前到数据加载期拦下。</summary>
        public static readonly FieldSchema InitiativeOverrideSchema = new FieldSchema(
            "initiative_override", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("policy", FieldKind.Enum, required: false, enumValues: InitiativeOverridePolicyValues,
                    description: "缺省不覆盖先攻策略，仍按 CombatModel 默认判断。"),
                new FieldSchema("params", FieldKind.Object, required: false, fields: InitiativeOverrideParamsFields,
                    description: "{initiative_stat?: Reference(stat.definition), action_points_per_turn?: Number}"),
            },
            description: "{policy?: initiative_stat|action_points|fixed_order, params?: Object}，" +
                "同上由 GameplayAssembly/TimeModelSwitch 真实执行");

        public static readonly TableSchema Def = new TableSchema(
            name: "encounter.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "encounter.<name>"),
                new FieldSchema("units", FieldKind.Array, required: true, item: UnitItemSchema,
                    description: "List<{spawn_ref? | template_ref?, position?}>，二选一，见 UnitItemSchema 判断记录"),
                new FieldSchema("waves", FieldKind.Array, required: false, item: WaveItemSchema,
                    description: "List<{trigger_condition: Expr, spawn_refs: List<Id>}>"),
                new FieldSchema("phases", FieldKind.Array, required: false, item: PhaseItemSchema,
                    description: "List<{enter_condition: Expr, ai_rotation_override: Map<Id,Id>, on_enter_hook?}>"),
                new FieldSchema("arena_rules", FieldKind.Object, required: false, fields: new[]
                    {
                        BoundsShapeSchema,
                        new FieldSchema("reset_if_leave", FieldKind.Bool, required: false, description: "缺省 false"),
                    },
                    description: "{bounds_shape, reset_if_leave: Bool}，见 EncounterShapeJson"),
                new FieldSchema("victory_condition", FieldKind.Expr, required: true,
                    description: "胜利判定表达式，求值为真即判定本次遭遇胜利"),
                new FieldSchema("defeat_condition", FieldKind.Expr, required: true,
                    description: "失败判定表达式，求值为真即判定本次遭遇失败"),
                // F3 元数据门禁收口：此前直接用 RewardSchemaFields.Rewards()（裸 Object，不带
                // Fields 子结构），与 quest.def/achv.def 的 rewards 字段（同一份 RewardBundle 形状，
                // 见 Core.Gameplay.Common.RewardSchemaFields 类型注释"三张表都有一个结构相同、语义
                // 相同的 rewards 字段"）不一致，被 SchemaAudit 判为 composite_without_substructure——
                // RewardBundle.FromRecord 对 items/xp/currency/skills/world_flags/talent_points
                // 有明确的固定子键解析逻辑（格式错误抛 FormatException，不是自由形状），改为与
                // achv.def 同款做法，直接复用 QuestSchemas.RewardsFields（见 QuestSchemas.cs
                // "上游可以考虑后续把本字段搬进 RewardSchemaFields.Rewards()"一句判断记录——三张表
                // 现在全部收敛到同一份 Fields 声明，不再有第三种写法）。
                new FieldSchema("rewards", FieldKind.Object, required: false, fields: QuestSchemas.RewardsFields,
                    description: "{items, xp, currency, skills, world_flags, talent_points}，见 Core.Gameplay.Common.RewardBundle；F3 收口复用 QuestSchemas.RewardsFields（与 quest.def/achv.def 同一份子结构声明）"),
                new FieldSchema("combat_mode_override", FieldKind.Enum, required: false, enumValues: CombatModeValues,
                    description: "覆盖场景默认 combat_time_model，仅本遭遇生效；由 GameplayAssembly/TimeModelSwitch 在离散模式下真实执行"),
                InitiativeOverrideSchema,
            });

        public static readonly TableSchema Level = new TableSchema(
            name: "encounter.level",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "encounter.level.<name>"),
                new FieldSchema("map_ref", FieldKind.Id, required: true,
                    description: "指向 world.map（该表不在本任务数据集范围内，用 Id 而非 Reference，见判断记录）"),
                new FieldSchema("encounter_sequence", FieldKind.IdList, required: true,
                    description: "有序 encounter.def 引用；未升级为 Array+Item=Reference，见本类型顶部判断记录"),
                new FieldSchema("entry_difficulty_options", FieldKind.IdList, required: false,
                    description: "可选难度档位（diff.tier 引用）；处理惯例同 encounter_sequence"),
            });
    }
}

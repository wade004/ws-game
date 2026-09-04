using Core.Foundation.DataRegistry;
using Core.Gameplay.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def</c>/<c>encounter.level</c> 的 <see cref="TableSchema"/> 声明（见 08 第
    /// 4.1、4.2 节全部字段）。
    /// <para>
    /// 判断记录（<c>units</c>/<c>waves</c>/<c>phases</c>/<c>arena_rules</c>/<c>initiative_override</c>
    /// 用 <see cref="FieldKind.Array"/>/<see cref="FieldKind.Object"/>）：均为嵌套结构，04 记法内置
    /// 字段类型无法表达（同 <c>RewardSchemaFields</c>/<c>AchievementSchemas.Def.criteria</c> 判断
    /// 记录），内部结构校验交给 <see cref="EncounterDefinition.FromRecord"/>（结构错误）与
    /// <see cref="EncounterContentValidationRule"/>（业务规则：<c>units[]</c> 二选一、
    /// <c>spawn_refs</c>/<c>spawn_ref</c> 域名 <c>spawn</c>、嵌套 Expr 可解析）。
    /// </para>
    /// <para>
    /// 判断记录（<c>victory_condition</c>/<c>defeat_condition</c> 用 <see cref="FieldKind.Expr"/>）：
    /// 这两个是顶层字段，DataRegistry 内置的 <c>expr_parsable</c> 校验项能直接覆盖（需组装层配置
    /// <see cref="DataRegistryOptions.ExprSchema"/>）；<c>waves[].trigger_condition</c>/
    /// <c>phases[].enter_condition</c> 嵌套在 <see cref="FieldKind.Array"/> 内，内置校验触及不到，
    /// 由 <see cref="EncounterContentValidationRule"/> 补上。
    /// </para>
    /// </summary>
    public static class EncounterSchemas
    {
        private static readonly string[] CombatModeValues = { "continuous", "discrete" };

        public static readonly TableSchema Def = new TableSchema(
            name: "encounter.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "encounter.<name>"),
                new FieldSchema("units", FieldKind.Array, required: true,
                    description: "List<{spawn_ref? | template_ref?, position?}>，二选一，见本类型判断记录"),
                new FieldSchema("waves", FieldKind.Array, required: false,
                    description: "List<{trigger_condition: Expr, spawn_refs: List<Id>}>"),
                new FieldSchema("phases", FieldKind.Array, required: false,
                    description: "List<{enter_condition: Expr, ai_rotation_override: Map<Id,Id>, on_enter_hook?}>"),
                new FieldSchema("arena_rules", FieldKind.Object, required: false,
                    description: "{bounds_shape, reset_if_leave: Bool}，见 EncounterShapeJson"),
                new FieldSchema("victory_condition", FieldKind.Expr, required: true),
                new FieldSchema("defeat_condition", FieldKind.Expr, required: true),
                RewardSchemaFields.Rewards(),
                new FieldSchema("combat_mode_override", FieldKind.Enum, required: false, enumValues: CombatModeValues,
                    description: "覆盖场景默认 combat_time_model，仅本遭遇生效；本项目离散模式未启用，只登记不使用"),
                new FieldSchema("initiative_override", FieldKind.Object, required: false,
                    description: "{policy, params}，同上只登记不使用"),
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
                    description: "有序 encounter.def 引用"),
                new FieldSchema("entry_difficulty_options", FieldKind.IdList, required: false,
                    description: "可选难度档位（diff.tier 引用）"),
            });
    }
}

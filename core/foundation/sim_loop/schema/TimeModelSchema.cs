using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <c>found.time_model</c> 表的 <see cref="TableSchema"/> 声明（见 04_数据与内容管线.md 第 3.1
    /// 节"时间字段语义"字段表、ADR-0013 决策 1）。内容表，主键 <c>id</c>（不在 04 第 3 节"记录主键
    /// 约定"列出的三张登记表之列，domain 前缀 <c>found</c> 等于表名首段，走标准内容表规则）。调用方
    /// 在构造 <see cref="IDataRegistry"/> 后需要 <c>RegisterSchema(TimeModelSchema.Table)</c>
    /// 才能加载对应数据文件——本模块不自动注册，见 <c>core/rules/assembly/RulesSchemaCatalog.cs</c>
    /// <c>RegisterL0Schemas</c> 的实际登记点。
    /// </summary>
    public static class TimeModelSchema
    {
        public static readonly string[] ScopeValues = { "exploration", "combat" };

        public static readonly string[] ModeValues = { "continuous", "discrete" };

        /// <summary>先攻策略合法取值（见 04 第 3.1 节 <c>initiative_policy</c>、ADR-0013 决策 3）：
        /// <c>atb</c> 是预留扩展位，登记为合法枚举值（校验器认可），但
        /// <see cref="TurnScheduler.Configure"/> 遇到时抛 <see cref="System.NotSupportedException"/>。</summary>
        public static readonly string[] InitiativePolicyValues = { "initiative_stat", "action_points", "fixed_order", "atb" };

        public static readonly string[] MovementBudgetRuleValues = { "distance", "action_points" };

        public static TableSchema Table { get; } = new TableSchema(
            name: "found.time_model",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "found.time_model.<name>，探索与战斗各自登记一条"),
                new FieldSchema("scope", FieldKind.Enum, required: true, enumValues: ScopeValues,
                    description: "声明本条作用于探索还是战斗"),
                new FieldSchema("mode", FieldKind.Enum, required: true, enumValues: ModeValues,
                    description: "时间模型：连续（固定步长，时间单位为秒）或离散（回合，时间单位为回合）"),
                new FieldSchema("seconds_per_turn", FieldKind.Number, required: false,
                    description: "mode: discrete 时必填：连续/离散切换时刻的时间单位换算系数，默认 6"),
                new FieldSchema("initiative_policy", FieldKind.Enum, required: false, enumValues: InitiativePolicyValues,
                    description: "mode: discrete 时必填：先攻策略"),
                new FieldSchema("initiative_stat", FieldKind.Reference, required: false,
                    referenceTable: "stat.definition",
                    description: "initiative_policy: initiative_stat 时必填：指向 stat.definition 的先攻属性 id"),
                new FieldSchema("movement_budget_rule", FieldKind.Enum, required: false, enumValues: MovementBudgetRuleValues,
                    description: "mode: discrete 时必填：离散模式下每回合移动预算的计算方式"),
                new FieldSchema("grid_snap", FieldKind.Object, required: false,
                    description: "若启用格子吸附，声明 {cell_size: Number}；范围形状按格子中心采样"),
                // 04 第 3.1 节勘误（ADR-0013 补齐任务）：initiative_policy 或 movement_budget_rule
                // 任一为 action_points 时，两者共享同一份"每回合行动点总额度"，见
                // TimeModelDefinition.ActionPointsPerTurn 判断记录。
                new FieldSchema("action_points_per_turn", FieldKind.Number, required: false,
                    description: "initiative_policy 或 movement_budget_rule 为 action_points 时使用的每回合行动点总额度，默认 1"),
                new FieldSchema("movement_action_cost_per_unit", FieldKind.Number, required: false,
                    description: "movement_budget_rule: action_points 时必填：移动 1 单位距离消耗的行动点数"),
            });
    }

    /// <summary>
    /// <c>found.time_model</c> 的模块专属校验规则（见 04 第 3.1 节"mode: discrete 时必填"系列约束，
    /// 04 第 5 节校验器扩展点惯例同 <c>StatDefinitionValidationRule</c>）。调用方需要
    /// <c>registry.RegisterValidationRule(new TimeModelValidationRule())</c> 才会生效。
    /// <para>
    /// 判断记录（04 第 5 节"时间字段与时间模型一致"未在本规则内覆盖）：该项要求校验
    /// <c>cast_time</c>/<c>cooldown_duration</c>/光环 <c>duration</c>/<c>interval</c> 等分散在
    /// <c>skill.def</c>/<c>skill.aura_def</c> 等多张 L2 表里的字段是否为整数，属于跨表、跨模块的
    /// 校验规则，且需要先解析出该记录"所属作用域是探索还是战斗"（技能可能双模式通用），涉及的
    /// 数据建模决策超出本任务"落地离散时间模型运行时机制"的范围，本次不实现，留待后续任务按
    /// 04 第 5 节该项要求专门设计（已在交付报告"做不了的事"一节列出）。
    /// </para>
    /// </summary>
    public sealed class TimeModelValidationRule : IValidationRule
    {
        public const string CheckDiscreteRequiresSecondsPerTurn = "time_model_discrete_requires_seconds_per_turn";

        public const string CheckDiscreteRequiresInitiativePolicy = "time_model_discrete_requires_initiative_policy";

        public const string CheckInitiativeStatPolicyRequiresInitiativeStat = "time_model_initiative_stat_policy_requires_initiative_stat";

        public const string CheckDiscreteRequiresMovementBudgetRule = "time_model_discrete_requires_movement_budget_rule";

        public const string CheckSecondsPerTurnPositive = "time_model_seconds_per_turn_positive";

        /// <summary>04 第 3.1 节勘误（ADR-0013 补齐任务）。</summary>
        public const string CheckActionPointsMovementRuleRequiresCostPerUnit = "time_model_action_points_movement_rule_requires_cost_per_unit";

        public const string CheckActionPointsPerTurnPositive = "time_model_action_points_per_turn_positive";

        public const string CheckMovementActionCostPerUnitPositive = "time_model_movement_action_cost_per_unit_positive";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = view.GetAll("found.time_model");
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var mode = record.TryGetString("mode", out var modeValue) ? modeValue : null;

                if (mode != "discrete")
                {
                    continue;
                }

                if (!record.TryGetNumber("seconds_per_turn", out var secondsPerTurn))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "found.time_model", CheckDiscreteRequiresSecondsPerTurn,
                        "mode 为 discrete 时 seconds_per_turn 必填", recordKey: record.Key, field: "seconds_per_turn");
                }
                else if (secondsPerTurn <= 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "found.time_model", CheckSecondsPerTurnPositive,
                        $"seconds_per_turn 必须为正数，实际 {secondsPerTurn}", recordKey: record.Key, field: "seconds_per_turn");
                }

                if (!record.TryGetString("initiative_policy", out var initiativePolicy))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "found.time_model", CheckDiscreteRequiresInitiativePolicy,
                        "mode 为 discrete 时 initiative_policy 必填", recordKey: record.Key, field: "initiative_policy");
                }
                else if (initiativePolicy == "initiative_stat" && !record.TryGetId("initiative_stat", out _))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "found.time_model", CheckInitiativeStatPolicyRequiresInitiativeStat,
                        "initiative_policy 为 initiative_stat 时 initiative_stat 必填", recordKey: record.Key, field: "initiative_stat");
                }

                if (!record.TryGetString("movement_budget_rule", out var movementBudgetRule))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "found.time_model", CheckDiscreteRequiresMovementBudgetRule,
                        "mode 为 discrete 时 movement_budget_rule 必填", recordKey: record.Key, field: "movement_budget_rule");
                    movementBudgetRule = null;
                }

                // 04 第 3.1 节勘误（ADR-0013 补齐任务）：movement_budget_rule 为 action_points 时，
                // movement_action_cost_per_unit 必填且须为正数。
                if (movementBudgetRule == "action_points")
                {
                    if (!record.TryGetNumber("movement_action_cost_per_unit", out var costPerUnit))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "found.time_model", CheckActionPointsMovementRuleRequiresCostPerUnit,
                            "movement_budget_rule 为 action_points 时 movement_action_cost_per_unit 必填",
                            recordKey: record.Key, field: "movement_action_cost_per_unit");
                    }
                    else if (costPerUnit <= 0)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "found.time_model", CheckMovementActionCostPerUnitPositive,
                            $"movement_action_cost_per_unit 必须为正数，实际 {costPerUnit}",
                            recordKey: record.Key, field: "movement_action_cost_per_unit");
                    }
                }

                if (record.TryGetNumber("action_points_per_turn", out var actionPointsPerTurn) && actionPointsPerTurn <= 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "found.time_model", CheckActionPointsPerTurnPositive,
                        $"action_points_per_turn 必须为正数，实际 {actionPointsPerTurn}",
                        recordKey: record.Key, field: "action_points_per_turn");
                }
            }
        }
    }
}

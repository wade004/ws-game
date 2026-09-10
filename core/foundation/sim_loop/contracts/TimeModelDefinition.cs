using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 一条 <c>found.time_model</c> 记录的强类型视图（见 04_数据与内容管线.md 第 3.1 节字段表、
    /// <see cref="TimeModelSchema"/>）。只做结构抽取，不做校验（校验由
    /// <see cref="TimeModelValidationRule"/> 在数据加载期完成，本类型假定传入的记录已通过校验）。
    /// </summary>
    public sealed class TimeModelDefinition
    {
        public Id Id { get; }

        /// <summary><c>"exploration"</c> 或 <c>"combat"</c>。</summary>
        public string Scope { get; }

        public TimeModelMode Mode { get; }

        /// <summary><see cref="Mode"/> 为 <see cref="TimeModelMode.Discrete"/> 时有意义，默认 6
        /// （04 第 3.1 节 <c>seconds_per_turn</c> "默认 6"）。</summary>
        public double SecondsPerTurn { get; }

        /// <summary><see cref="Mode"/> 为 <see cref="TimeModelMode.Discrete"/> 时有意义。</summary>
        public InitiativePolicy InitiativePolicy { get; }

        /// <summary><see cref="InitiativePolicy"/> 为 <see cref="InitiativePolicy.InitiativeStat"/> 时有意义。</summary>
        public Id? InitiativeStat { get; }

        /// <summary><see cref="Mode"/> 为 <see cref="TimeModelMode.Discrete"/> 时有意义：
        /// <c>"distance"</c> 或 <c>"action_points"</c>。</summary>
        public string? MovementBudgetRule { get; }

        /// <summary>04 第 3.1 节勘误：<c>initiative_policy: action_points</c> 或
        /// <c>movement_budget_rule: action_points</c> 任一为 <c>action_points</c> 时使用的每回合
        /// 行动点总额度，默认 1（同 <see cref="TurnScheduler"/> 未显式配置时的默认值）。</summary>
        public double ActionPointsPerTurn { get; }

        /// <summary>04 第 3.1 节勘误：<see cref="MovementBudgetRule"/> 为 <c>"action_points"</c>
        /// 时必填，移动 1 单位距离消耗的行动点数；其余情形为 <c>null</c>。</summary>
        public double? MovementActionCostPerUnit { get; }

        /// <summary>
        /// 04 第 3.1 节 <c>grid_snap</c> 字段（<c>Optional&lt;{cell_size: Number}&gt;</c>，ADR-0013
        /// 决策 6、13 第 4 节"网格吸附"）：声明本时间模型是否启用格子吸附、若启用则格子尺寸是多少。
        /// <c>null</c> 表示未声明 <c>grid_snap</c>（不启用，行为与格子吸附落地之前完全一致）；非
        /// <c>null</c> 时即 <c>grid_snap.cell_size</c>（已由 <see cref="TimeModelValidationRule"/>
        /// 在数据加载期校验为正数，见该类型判断记录、<see cref="TimeModelSchema"/> 的
        /// <c>grid_snap.cell_size</c> 字段登记）。消费方：<c>Core.Carriers.Unit.MovementOptions.
        /// GridSnapCellSize</c>（离散步移动结束吸附到格子中心）、<c>Core.Rules.Targeting.
        /// TargetingOptions.GridSnapCellSize</c>/<c>Core.Rules.Skill.SkillOptions.GridSnapCellSize</c>
        /// （范围形状按格子中心采样）——三处均由 <c>Core.Gameplay.Assembly.GameplayAssembly</c> 在
        /// <c>TimeModelSwitch</c> 造好之后回填，惯例同 <see cref="MovementBudgetRule"/> 判断记录。
        /// </summary>
        public double? GridSnapCellSize { get; }

        private TimeModelDefinition(
            Id id, string scope, TimeModelMode mode, double secondsPerTurn,
            InitiativePolicy initiativePolicy, Id? initiativeStat, string? movementBudgetRule,
            double actionPointsPerTurn, double? movementActionCostPerUnit, double? gridSnapCellSize)
        {
            Id = id;
            Scope = scope;
            Mode = mode;
            SecondsPerTurn = secondsPerTurn;
            InitiativePolicy = initiativePolicy;
            InitiativeStat = initiativeStat;
            MovementBudgetRule = movementBudgetRule;
            ActionPointsPerTurn = actionPointsPerTurn;
            MovementActionCostPerUnit = movementActionCostPerUnit;
            GridSnapCellSize = gridSnapCellSize;
        }

        public static TimeModelDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");
            var scope = record.GetString("scope");
            var modeText = record.GetString("mode");
            var mode = modeText == "discrete" ? TimeModelMode.Discrete : TimeModelMode.Continuous;

            var secondsPerTurn = record.TryGetNumber("seconds_per_turn", out var spt) ? spt : 6.0;

            var initiativePolicy = InitiativePolicy.FixedOrder;
            if (record.TryGetString("initiative_policy", out var policyText))
            {
                initiativePolicy = policyText switch
                {
                    "initiative_stat" => InitiativePolicy.InitiativeStat,
                    "action_points" => InitiativePolicy.ActionPoints,
                    "fixed_order" => InitiativePolicy.FixedOrder,
                    "atb" => InitiativePolicy.Atb,
                    _ => throw new DataFieldException(record.Table.Name, record.Key, "initiative_policy", $"未知取值 \"{policyText}\""),
                };
            }

            Id? initiativeStat = record.TryGetId("initiative_stat", out var statId) ? statId : (Id?)null;

            var movementBudgetRule = record.TryGetString("movement_budget_rule", out var mbr) ? mbr : null;

            var actionPointsPerTurn = record.TryGetNumber("action_points_per_turn", out var appt) ? appt : 1.0;
            double? movementActionCostPerUnit = record.TryGetNumber("movement_action_cost_per_unit", out var macpu) ? macpu : (double?)null;

            // 04 第 3.1 节 grid_snap 字段：{cell_size: Number}，未声明时 gridSnapCellSize 为 null
            // （见 GridSnapCellSize 属性判断记录）。cell_size 的正数约束由 TimeModelValidationRule
            // 在数据加载期校验（见该类型判断记录），本方法假定传入的记录已通过校验，只做结构抽取。
            double? gridSnapCellSize = null;
            if (record.TryGetObject("grid_snap", out var gridSnapObj)
                && gridSnapObj.TryGetValue("cell_size", out var cellSizeRaw)
                && cellSizeRaw is Core.Foundation.Common.Json.JsonNumber cellSizeNum)
            {
                gridSnapCellSize = cellSizeNum.Value;
            }

            return new TimeModelDefinition(
                id, scope, mode, secondsPerTurn, initiativePolicy, initiativeStat, movementBudgetRule,
                actionPointsPerTurn, movementActionCostPerUnit, gridSnapCellSize);
        }
    }
}

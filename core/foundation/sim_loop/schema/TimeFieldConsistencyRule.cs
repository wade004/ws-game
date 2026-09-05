using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 一条"时间字段"声明（见 04_数据与内容管线.md 第 3.1 节"时间字段语义"、第 5 节"时间字段与
    /// 时间模型一致"）：某张表的某个字段是"以数据集声明的时间单位计"的时间字段，归属探索或战斗
    /// 作用域（<see cref="Scope"/> 取值 <c>"exploration"</c> 或 <c>"combat"</c>，对应
    /// <c>found.time_model.scope</c>）。
    /// <para>
    /// 判断记录（为什么本类型只提供"声明 + 通用取值访问器"，不硬编码任何具体表/字段名）：
    /// <c>cast_time</c>/<c>cooldown_duration</c>/光环 <c>duration</c>/资源 <c>regen</c>/
    /// <c>respawn_timer</c> 等具体字段分别属于 L1（<c>arch.power_type</c>）、L2
    /// （<c>skill.def</c>/<c>skill.aura_def</c>/<c>skill.proc_def</c>）、L4
    /// （<c>spawn.table</c>）各模块的表结构——本类型所在的 <c>core/foundation/sim_loop</c> 是 L0，
    /// 01_分层与依赖.md 禁止 L0 反向依赖 L1/L2/L4 的具体表名/字段名。本类型只提供"登记一个
    /// (表名, 字段标签, 作用域, 取值访问器)"的通用形状，<see cref="TimeFieldConsistencyRule"/>
    /// 只认识这个通用形状；真正认识 <c>skill.def.cast_time</c> 一类具体字段名的代码留在各自模块的
    /// 组装 catalog（<c>core/rules/assembly/RulesSchemaCatalog.cs</c>、
    /// <c>core/gameplay/assembly/GameplaySchemaCatalog.cs</c>）里按需构造 <see cref="TimeFieldDeclaration"/>
    /// 列表并注册 <see cref="TimeFieldConsistencyRule"/>，不在本类型内新增任何对上层表的引用。
    /// </para>
    /// </summary>
    public sealed class TimeFieldDeclaration
    {
        /// <summary>字段所属的表名（如 <c>skill.def</c>）。</summary>
        public string Table { get; }

        /// <summary>字段标签，供 <see cref="ValidationIssue.Field"/>/错误消息定位（嵌套字段用点号
        /// 拼接，如 <c>charges.recharge_time</c>）。</summary>
        public string FieldLabel { get; }

        /// <summary>归属的时间模型作用域：<c>"exploration"</c> 或 <c>"combat"</c>。</summary>
        public string Scope { get; }

        /// <summary>从记录里取出该时间字段的数值；字段未提供（可选字段缺失）时返回 <c>null</c>，
        /// 视为"本条记录不适用该项检查"，不产生问题。</summary>
        public Func<DataRecord, double?> Extract { get; }

        public TimeFieldDeclaration(string table, string fieldLabel, string scope, Func<DataRecord, double?> extract)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            FieldLabel = fieldLabel ?? throw new ArgumentNullException(nameof(fieldLabel));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Extract = extract ?? throw new ArgumentNullException(nameof(extract));
        }
    }

    /// <summary>
    /// "时间字段与时间模型一致"校验规则（04 第 5 节校验器检查项清单、第 3.1 节"作用域声明为
    /// discrete 时，该记录引用到的全部时间字段必须是整数"）：按 <see cref="TimeFieldDeclaration"/>
    /// 登记表逐条检查——某字段归属的作用域在 <c>found.time_model</c> 里声明为 <c>discrete</c> 时，
    /// 该字段的取值必须是整数（以回合计）；作用域声明为 <c>continuous</c>，或该作用域压根没有
    /// 登记 <c>found.time_model</c> 记录时，不做整数限制（惯例同
    /// <c>Core.Gameplay.Assembly.TimeModelSwitch.CombatModel</c> 判断记录"缺失战斗时间模型时恒不
    /// 切换"——数据缺失不是错误，只是该项检查不适用）。
    /// <para>
    /// 判断记录（光环周期 <c>tick_interval</c> 未覆盖）：04 第 3.1 节例举的时间字段还包括"光环…
    /// 周期 interval"，但 <c>skill.aura_def.effects[].params.interval</c> 是嵌在不透明的
    /// "kind + params" 数组元素里的字段（<c>effects</c> 的具体形状由 <c>kind</c> 决定，
    /// schema 层不为每种 <c>kind</c> 分别建模，见 <c>SkillSchemas.AuraDef</c> 注释），本规则的
    /// <see cref="TimeFieldDeclaration"/> 只能表达"表的顶层/一层嵌套字段"，取不到这类"数组元素
    /// 内按 kind 变化的字段"，与既有 <c>TimeModelValidationRule</c> 判断记录里搁置同一问题的原因
    /// 一致，留待后续任务专门为 <c>effects[]</c> 设计取值路径后再补齐。
    /// </para>
    /// </summary>
    public sealed class TimeFieldConsistencyRule : IValidationRule
    {
        public const string CheckTimeFieldMustBeInteger = "time_field_must_be_integer_in_discrete_scope";

        private const double IntegerEpsilon = 1e-9;

        private readonly IReadOnlyList<TimeFieldDeclaration> _declarations;

        public TimeFieldConsistencyRule(IReadOnlyList<TimeFieldDeclaration> declarations)
        {
            _declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var scopeModes = LoadScopeModes(view);

            for (var i = 0; i < _declarations.Count; i++)
            {
                var declaration = _declarations[i];

                if (!scopeModes.TryGetValue(declaration.Scope, out var mode) || mode != "discrete")
                {
                    continue;
                }

                var records = view.GetAll(declaration.Table);
                for (var r = 0; r < records.Count; r++)
                {
                    var record = records[r];
                    var value = declaration.Extract(record);
                    if (value == null)
                    {
                        continue;
                    }

                    var rounded = Math.Round(value.Value);
                    if (Math.Abs(value.Value - rounded) > IntegerEpsilon)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, declaration.Table, CheckTimeFieldMustBeInteger,
                            $"作用域 \"{declaration.Scope}\" 的 found.time_model.mode 为 discrete 时，字段 " +
                            $"\"{declaration.FieldLabel}\" 必须为整数（以回合计），实际 {value.Value}",
                            recordKey: record.Key, field: declaration.FieldLabel);
                    }
                }
            }
        }

        private static Dictionary<string, string> LoadScopeModes(IDataRegistryView view)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var records = view.GetAll("found.time_model");
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record.TryGetString("scope", out var scope) && record.TryGetString("mode", out var mode))
                {
                    result[scope] = mode;
                }
            }
            return result;
        }
    }
}

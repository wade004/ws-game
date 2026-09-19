using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>严重级别：<see cref="Error"/> 一律阻断（见 04 第 5 节"任何一项失败即视为本次
    /// 内容变更不可合入"）；<see cref="Warning"/> 是否阻断取决于
    /// <see cref="DataRegistryOptions.Strictness"/>（登记为 <see cref="IValidationRule.NonEscalatable"/>
    /// 的规则产出的 Warning 除外，见 <see cref="ValidationReport.IsBlocking"/>）。</summary>
    public enum ValidationSeverity
    {
        Warning,
        Error,
    }

    /// <summary>
    /// 一条校验问题：严重级别 + 定位信息（表/记录主键/字段，后两者可选）+ 检查项名
    /// （固定取值集合见 04 第 5 节：<c>envelope</c>、<c>schema_version</c>、<c>primary_key</c>、
    /// <c>required_field</c>、<c>field_type</c>、<c>reference_integrity</c>、<c>text_key_exists</c>、
    /// <c>expr_parsable</c>，以及 <see cref="IValidationRule"/> 扩展点自行命名的检查项）+ 消息。
    /// <para>
    /// 分阶段落地计划 T-N0-2（落地清单 2.2 V2）新增三个可选定位/分组信息：<see cref="Group"/>
    /// （报告分组，如"已确认"/"待确认"）、<see cref="Note"/>（内容作者写在数据里的说明原文，如
    /// 技能预算超模说明）、<see cref="RuleId"/>（产出本条的规则 id，由 <see cref="DataRegistry"/>
    /// 在收集时按 <see cref="IValidationRule.RuleId"/> 补上，规则自己已填时不覆盖；字段级内置检查
    /// 项为 null）。三者经新增的九参数构造函数传入；既有六参数构造函数保持原物理签名（P2-01
    /// 教训：可选参数是编译期特性，给既有构造函数追加参数即产生新物理签名、破坏二进制兼容），
    /// 三者一律为 null。
    /// </para>
    /// </summary>
    public readonly struct ValidationIssue
    {
        public ValidationSeverity Severity { get; }

        public string Table { get; }

        public string? RecordKey { get; }

        public string? Field { get; }

        public string Check { get; }

        public string Message { get; }

        /// <summary>报告分组（可选），供文本/JSON 报告与问题面板分组展示；取值由产出规则自行约定
        /// （如"已确认"/"待确认"），本模块不解释。</summary>
        public string? Group { get; }

        /// <summary>说明原文（可选）：内容数据里随该记录一起给出的人写说明（如技能预算超模说明），
        /// 原样带进报告，供评审者不必回到数据文件即可看到作者意图。</summary>
        public string? Note { get; }

        /// <summary>产出本条问题的规则 id（<see cref="IValidationRule.RuleId"/>）；字段级内置检查项
        /// 为 null。</summary>
        public string? RuleId { get; }

        private static readonly string[] NoAffectedNodeIds = Array.Empty<string>();

        /// <summary>消费方反馈第 57 条（2026-09-18）新增：本条问题涉及的图节点 id 列表，默认空集合
        /// （不是 null，调用方不需要额外 null 检查）。跨节点路径类诊断（如
        /// <c>story_tree_cycle</c>/<c>quest_prerequisite_cycle</c>/<c>talent_prerequisite_cycle</c>
        /// 成环诊断）按环上出现顺序填入环上全部节点 id；单节点类诊断（如
        /// <c>story_tree_duplicate_node_id</c>/<c>story_tree_dangling_next_node</c>/
        /// <c>quest_prerequisite_unknown</c>/<c>talent_prerequisite_missing</c>）填入该节点自身 id。
        /// 只新增只读属性 + 新构造重载，不改既有构造函数签名与相等性语义（ABI 只新增）。</summary>
        public IReadOnlyList<string> AffectedNodeIds { get; }

        public ValidationIssue(
            ValidationSeverity severity,
            string table,
            string check,
            string message,
            string? recordKey = null,
            string? field = null)
            : this(severity, table, check, message, recordKey, field, group: null, note: null, ruleId: null)
        {
        }

        /// <summary>T-N0-2 新增的完整构造：九个参数全部不带默认值（若也写成可选参数，会与上面的
        /// 六参数构造函数在"只传 4～6 个参数"的调用点产生重载二义性——同 <see cref="FieldSchema"/>
        /// 七参数兼容构造的判断记录）。</summary>
        public ValidationIssue(
            ValidationSeverity severity,
            string table,
            string check,
            string message,
            string? recordKey,
            string? field,
            string? group,
            string? note,
            string? ruleId)
            : this(severity, table, check, message, recordKey, field, group, note, ruleId, affectedNodeIds: null)
        {
        }

        /// <summary>消费方反馈第 57 条新增的十参数构造：<paramref name="affectedNodeIds"/> 为 null 时
        /// 落到共享的空数组单例（同一实例，保证未显式传入该字段的既有调用点两两之间的值相等语义不受
        /// 影响——两侧都引用同一个 <see cref="NoAffectedNodeIds"/>）。同上一处判断记录，不写成可选
        /// 参数，避免与九参数构造函数产生重载二义性。</summary>
        public ValidationIssue(
            ValidationSeverity severity,
            string table,
            string check,
            string message,
            string? recordKey,
            string? field,
            string? group,
            string? note,
            string? ruleId,
            IReadOnlyList<string>? affectedNodeIds)
        {
            Severity = severity;
            Table = table ?? throw new ArgumentNullException(nameof(table));
            Check = check ?? throw new ArgumentNullException(nameof(check));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            RecordKey = recordKey;
            Field = field;
            Group = group;
            Note = note;
            RuleId = ruleId;
            AffectedNodeIds = affectedNodeIds ?? NoAffectedNodeIds;
        }

        /// <summary>返回一份 <see cref="RuleId"/> 换成 <paramref name="ruleId"/>、其余字段（含
        /// <see cref="AffectedNodeIds"/>）不变的副本（值类型，原实例不受影响）。<see cref="DataRegistry"/>
        /// 用它给规则产出的问题补规则 id。</summary>
        public ValidationIssue WithRuleId(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId)) throw new ArgumentException("ruleId 不能为空", nameof(ruleId));
            return new ValidationIssue(Severity, Table, Check, Message, RecordKey, Field, Group, Note, ruleId, AffectedNodeIds);
        }

        /// <summary>消费方反馈第 57 条新增：返回一份 <see cref="AffectedNodeIds"/> 换成
        /// <paramref name="affectedNodeIds"/>、其余字段不变的副本（值类型，原实例不受影响）。产出图
        /// 结构诊断的校验规则用它标注本条问题涉及的节点 id。</summary>
        public ValidationIssue WithAffectedNodeIds(IReadOnlyList<string> affectedNodeIds)
        {
            if (affectedNodeIds == null) throw new ArgumentNullException(nameof(affectedNodeIds));
            return new ValidationIssue(Severity, Table, Check, Message, RecordKey, Field, Group, Note, RuleId, affectedNodeIds);
        }

        /// <summary>
        /// 人类可读的一行诊断文本（供控制台/日志展示），<b>不是稳定契约、不承诺格式跨版本不变</b>
        /// （消费方反馈第 71 条，2026-09-19）：拼接顺序、分隔符、`Table[RecordKey].Field` 这种定位
        /// 片段的具体写法都可能在不打大版本号的情况下调整。需要按字段程序化消费校验结果的调用方，
        /// 一律改读本类型的结构化只读属性（<see cref="Severity"/>/<see cref="Table"/>/
        /// <see cref="RecordKey"/>/<see cref="Field"/>/<see cref="Check"/>/<see cref="Message"/> 等，
        /// 已是公开属性，无需解析本方法输出），或走命令行入口的结构化输出——
        /// `toolchain/validator --json`（<c>Program.cs</c> 常规校验/`--schema-audit` 两条路径均支持，
        /// 见该文件判断记录）与 `toolchain/asset_import/check_cmd.py check --json`
        /// （消费方反馈第 62 条，1.43.0 落地）。截至本条判断记录写下时，运行期宿主
        /// （<c>games/_template/Runtime/GameBootstrap.cs</c>、<c>DataHotReload.cs</c>）仍只把本方法
        /// 的输出写入日志，没有把 <see cref="ValidationIssue"/> 结构化投影到事件总线或落盘文件；
        /// 只能拿到运行期日志的消费方目前只能解析本方法的文本输出，这是已知缺口，见
        /// `architecture/落地计划/` 对应的消费方反馈回复草稿。
        /// </summary>
        public override string ToString()
        {
            var loc = RecordKey == null ? Table : (Field == null ? $"{Table}[{RecordKey}]" : $"{Table}[{RecordKey}].{Field}");
            return $"[{Severity}] {Check} {loc}: {Message}";
        }
    }

    /// <summary>
    /// 一条已注册规则在一次校验中的摘要（分阶段落地计划 T-N0-2/T-N0-6；落地清单 2.2 V1/V3）：
    /// 规则 id、默认级别、是否不可提升、本次命中条数。<see cref="ValidationReport.Rules"/> 按注册顺序
    /// 列出全部跑过的规则（含命中 0 条的），供报告 <c>rules[]</c> 段与工具展示。
    /// </summary>
    public readonly struct ValidationRuleSummary
    {
        public string RuleId { get; }

        public ValidationSeverity DefaultSeverity { get; }

        public bool NonEscalatable { get; }

        public int HitCount { get; }

        public ValidationRuleSummary(string ruleId, ValidationSeverity defaultSeverity, bool nonEscalatable, int hitCount)
        {
            if (string.IsNullOrEmpty(ruleId)) throw new ArgumentException("ruleId 不能为空", nameof(ruleId));
            if (hitCount < 0) throw new ArgumentOutOfRangeException(nameof(hitCount));
            RuleId = ruleId;
            DefaultSeverity = defaultSeverity;
            NonEscalatable = nonEscalatable;
            HitCount = hitCount;
        }
    }

    /// <summary>
    /// 一次加载/校验的完整报告（见 04 第 4 节 <c>loadAll()</c>/<c>validate()</c> 返回值）。
    /// <see cref="IsBlocking"/> 由构造时传入的严格级别与 <see cref="ErrorCount"/>/
    /// <see cref="WarningCount"/> 计算：存在 Error 一律阻断；<see cref="DataRegistryStrictness.WarningsBlock"/>
    /// 下存在"可提升"的 Warning 也阻断——登记为 <see cref="IValidationRule.NonEscalatable"/> 的规则产出的
    /// Warning（<see cref="NonEscalatableWarningCount"/>）不计入（T-N0-2；04 第 5 节数值类校验项分级表
    /// 警告级"抓意图不抓手滑"）。
    /// </summary>
    public sealed class ValidationReport
    {
        private static readonly ValidationRuleSummary[] NoRules = Array.Empty<ValidationRuleSummary>();

        public IReadOnlyList<ValidationIssue> Issues { get; }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        /// <summary><see cref="WarningCount"/> 中来自 <see cref="IValidationRule.NonEscalatable"/> 规则的
        /// 条数；这部分在 <see cref="DataRegistryStrictness.WarningsBlock"/> 下不计入阻断。</summary>
        public int NonEscalatableWarningCount { get; }

        public bool IsBlocking { get; }

        /// <summary>本次校验跑过的全部已注册规则摘要（按注册顺序，含命中 0 条的规则）；经两参数
        /// 构造函数构造的报告为空列表。</summary>
        public IReadOnlyList<ValidationRuleSummary> Rules { get; }

        public ValidationReport(IReadOnlyList<ValidationIssue> issues, DataRegistryStrictness strictness)
            : this(issues, strictness, NoRules)
        {
        }

        /// <summary>T-N0-2 新增：附带规则摘要的构造。<paramref name="rules"/> 里登记为
        /// <see cref="ValidationRuleSummary.NonEscalatable"/> 的规则 id，用于把
        /// <paramref name="issues"/> 中 <see cref="ValidationIssue.RuleId"/> 命中这些 id 的 Warning
        /// 从阻断判定里排除。</summary>
        public ValidationReport(IReadOnlyList<ValidationIssue> issues, DataRegistryStrictness strictness, IReadOnlyList<ValidationRuleSummary> rules)
        {
            Issues = issues ?? throw new ArgumentNullException(nameof(issues));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));

            HashSet<string>? nonEscalatable = null;
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].NonEscalatable)
                {
                    nonEscalatable ??= new HashSet<string>(StringComparer.Ordinal);
                    nonEscalatable.Add(rules[i].RuleId);
                }
            }

            int errors = 0, warnings = 0, nonEscalatableWarnings = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                if (issue.Severity == ValidationSeverity.Error)
                {
                    errors++;
                }
                else
                {
                    warnings++;
                    if (nonEscalatable != null && issue.RuleId != null && nonEscalatable.Contains(issue.RuleId))
                    {
                        nonEscalatableWarnings++;
                    }
                }
            }
            ErrorCount = errors;
            WarningCount = warnings;
            NonEscalatableWarningCount = nonEscalatableWarnings;
            IsBlocking = errors > 0 || (strictness == DataRegistryStrictness.WarningsBlock && warnings - nonEscalatableWarnings > 0);
        }
    }
}

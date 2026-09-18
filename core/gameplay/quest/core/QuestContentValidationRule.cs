using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>quest.def</c> 专属内容校验规则（见 <see cref="IValidationRule"/>"模块专属校验规则的
    /// 扩展点"）。
    /// <para>
    /// ADR-0019 / F1b 收窄判断记录：本规则此前整条委托给 <see cref="QuestDefinition.FromRecord"/>
    /// 解析、把任意解析异常（含纯结构性问题：<c>type</c> 缺失/非法、<c>target_ref</c> 格式错、
    /// <c>count</c> 非整数、<c>start_method</c>/<c>turn_in_method</c>/<c>repeatable</c> 取值非法、
    /// <c>prerequisite</c>/<c>eventFilter</c> Expr 语法错等）打包成一条泛化的"quest.def 解析失败"
    /// 消息报告。<c>QuestSchemas.ObjectiveItemSchema</c>/<c>RewardsFields</c>（F1b 首批登记）已经把
    /// 这些纯结构检查登记为机器可读的 <c>Fields</c>/<c>Variants</c>/<c>Reference</c>/<c>Expr</c>，
    /// <c>DataRegistry</c> 加载期已经独立、逐字段地报出
    /// <c>required_field</c>/<c>field_type</c>/<c>variant_discriminator</c>/<c>reference_integrity</c>/
    /// <c>expr_parsable</c>——若本规则继续整条委托 <c>FromRecord</c>，会对同一处缺陷重复报告一次
    /// （任务书"同一缺陷不得双报"）。本规则因此收窄为只做登记表达不了的纯业务判断（数值范围/等值
    /// 约束一类），全部直接读取原始 JSON，不再调用 <c>FromRecord</c>/<c>ExprParser</c>：
    /// </para>
    /// <list type="bullet">
    /// <item>消费方反馈第 60 条根治：<c>objectives</c> 数组至少一条（<c>QuestDefinition</c> 构造期
    /// 硬约束）此前由本规则报 <c>objectives_min_count</c>，现已改在 <c>QuestSchemas.Def</c> 的
    /// <c>objectives</c> 字段登记 <see cref="FieldSchema.WithItemCount"/>（<c>min: 1</c>），由
    /// <c>DataRegistry</c> 通用字段校验的 <c>field_item_count</c> 检查项报告，本规则不再重复报告。</item>
    /// <item><c>objectives[].count</c> 必须为正整数（<c>QuestObjective</c> 构造期硬约束，
    /// <see cref="FieldKind.Int"/> 不区分正负）——<c>objective_count_positive</c>。</item>
    /// <item><c>objectives[].type</c> 为 <c>explore</c>/<c>escort</c>/<c>talk</c> 时 <c>count</c> 必须
    /// 恒为 1（业务判断，见 <see cref="QuestObjectiveTypes.RequiresCountOne"/>；<c>Fields</c> 无法
    /// 表达"某取值下数值必须等于常量"）——<c>objective_count_must_be_one</c>。</item>
    /// <item><c>objectives[].type</c> 为 <c>kill</c>/<c>escort</c> 时 <c>target_ref</c> 的 domain 段
    /// 必须是 <c>"creature"</c>（<c>QuestObjectiveTypes.RequiredTargetDomain</c> 判断记录；
    /// <c>QuestSchemas.ObjectiveItemSchema</c> 这两处退回 <see cref="FieldKind.Id"/> 而不是更强的
    /// <see cref="FieldKind.Reference"/>，理由见该类型判断记录：避免与既有测试
    /// <c>Tests.Gameplay.Assembly.GameplayAssemblyOwnerDayVendorExtensionPointTests</c> 冲突）——
    /// <c>objective_target_domain_mismatch</c>。</item>
    /// <item><c>rewards.xp</c>/<c>rewards.talent_points</c> 不能为负数（<c>RewardBundle</c> 构造期
    /// 硬约束）——<c>reward_xp_non_negative</c>/<c>reward_talent_points_non_negative</c>。</item>
    /// <item><c>rewards.items[].count</c> 必须为正整数（<c>ItemStack</c> 构造期硬约束）——
    /// <c>reward_item_count_positive</c>。</item>
    /// <item><c>rewards.world_flags[].value</c> 必须存在（<c>RewardBundle.ParseWorldFlags</c> 硬
    /// 约束；<c>value</c> 是 Bool｜Number｜Id 联合类型，<see cref="FieldKind"/> 无法表达，
    /// <c>QuestSchemas.RewardsFields</c> 未登记该子字段）——<c>reward_world_flag_value_required</c>。</item>
    /// <item>
    /// 消费方反馈第 38 条（2026-09-13，见
    /// architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md）：<c>prerequisite</c> 里
    /// <c>quest.*</c> 引用（<c>is_active</c>/<c>is_completed</c>/<c>is_available</c>/
    /// <c>is_objectives_complete</c>/<c>objective_progress</c>，见
    /// <see cref="QuestExprSchemaEntries.RegisterInto"/>）引用的任务 id 若不存在于
    /// <c>quest.def</c>——<c>quest_prerequisite_unknown</c>；<c>prerequisite</c> 引用图（"任务 →
    /// 前置任务"有向边）成环（含自环）——<c>quest_prerequisite_cycle</c>，算法与
    /// <c>DialogContentValidationRule.story_tree_cycle</c> 同款三色标记 DFS，只是图节点是
    /// <c>quest.def</c> 记录、边由重新解析 <c>prerequisite</c> Expr 文本得到的 AST
    /// （<see cref="ExprParser"/>/<see cref="ExprReferenceNode"/>）推出，不是正则匹配文本。语法本身
    /// 不可解析已由 <c>expr_parsable</c> 结构校验报告，本规则重新解析时遇到该情形直接跳过（不在
    /// 损坏的语法树上继续构图，也不重复报告同一缺陷）——<c>quest_prerequisite_unknown</c>/
    /// <c>quest_prerequisite_cycle</c>。
    /// </item>
    /// </list>
    /// <para>
    /// <paramref name="exprSchema"/> 构造参数：<c>objectives</c>/<c>rewards</c> 两类既有业务判断不需要
    /// 它（<c>prerequisite</c>（顶层）/<c>eventFilter</c>（<c>event</c> 变体分支）语法本身是否可解析的
    /// <c>expr_parsable</c> 检查已经由 <c>DataRegistry</c> 的顶层/子结构字段登记覆盖，用的是同一份
    /// <c>exprSchema</c> 实例——<c>GameplaySchemaCatalog.RegisterQuestSchemas</c> 把 <c>FullExprSchema</c>
    /// 同时传给 <c>DataRegistryOptions.ExprSchema</c> 与本规则构造函数，不会产生解析行为分歧）；第 38
    /// 条新增的 <c>quest_prerequisite_cycle</c>/<c>quest_prerequisite_unknown</c> 检查需要重新把
    /// <c>prerequisite</c> 文本解析成 AST 才能提取 <c>quest.*</c> 引用，因此本规则起改为真正持有并使用
    /// 这份 schema（<c>_exprSchema</c>）——不传（<c>null</c>）时退回
    /// <see cref="QuestExprSchemaEntries.BuildParsingSchema"/> 构造的独立登记表（覆盖
    /// <c>quest</c>/<c>player</c>/<c>world</c>/<c>event</c> 四分组，足以正确解析
    /// <c>prerequisite</c>/<c>eventFilter</c> 里出现的全部分组前缀），保证
    /// <c>new QuestContentValidationRule()</c>（无参构造，既有测试/调用点惯例）也能正确解析、不抛
    /// <see cref="ExprParseException"/>"未登记分组"一类误报。
    /// </para>
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（组装层或本模块测试）需要显式
    /// <c>registry.RegisterValidationRule(new QuestContentValidationRule())</c>（惯例同
    /// <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class QuestContentValidationRule : IValidationRule
    {
        private readonly IExprSchema _exprSchema;

        public QuestContentValidationRule(IExprSchema? exprSchema = null)
        {
            // 见类型顶部判断记录：exprSchema 起改为供 quest_prerequisite_cycle/quest_prerequisite_unknown
            // 重新解析 prerequisite AST 用；未提供时退回独立登记表，保证无参构造仍可用。
            _exprSchema = exprSchema ?? QuestExprSchemaEntries.BuildParsingSchema();
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains(QuestSchemas.Def.Name))
            {
                yield break;
            }

            var records = view.GetAll(QuestSchemas.Def.Name).ToList();

            foreach (var record in records)
            {
                foreach (var issue in ValidateObjectives(record))
                {
                    yield return issue;
                }

                foreach (var issue in ValidateRewards(record))
                {
                    yield return issue;
                }
            }

            foreach (var issue in ValidatePrerequisiteGraph(records))
            {
                yield return issue;
            }
        }

        private static IEnumerable<ValidationIssue> ValidateObjectives(DataRecord record)
        {
            // 数组本身缺失/类型不对已由 required_field/field_type 结构校验报告，这里只处理"数组
            // 已经确认存在"之后登记表达不了的业务判断。
            if (!record.TryGetArray("objectives", out var objectives))
            {
                yield break;
            }

            // 消费方反馈第 60 条：元素数不足已由 field_item_count（WithItemCount(min: 1)）报告，
            // 这里不再重复报错；下面循环对空数组天然是空操作，不需要额外提前返回。

            for (var i = 0; i < objectives.Count; i++)
            {
                if (!(objectives[i] is JsonObject o))
                {
                    continue; // 元素不是对象已由 field_type 报告。
                }

                var fieldPrefix = $"objectives[{i}]";

                var hasType = o.TryGetValue("type", out var typeRaw) && typeRaw is JsonString typeStr;
                var type = hasType ? ((JsonString)typeRaw!).Value : null;

                if (o.TryGetValue("count", out var countRaw) && countRaw is JsonNumber countNum &&
                    countNum.TryGetInt64(out var count))
                {
                    if (count <= 0)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "objective_count_positive",
                            "objectives[].count 必须为正数", recordKey: record.Key, field: $"{fieldPrefix}.count");
                    }
                    else if (type == "explore" || type == "escort" || type == "talk")
                    {
                        if (count != 1)
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, QuestSchemas.Def.Name, "objective_count_must_be_one",
                                $"目标类型 \"{type}\" 的 count 必须恒为 1", recordKey: record.Key, field: $"{fieldPrefix}.count");
                        }
                    }
                }
                // count 缺失/非数值已由 required_field/field_type 报告，这里不重复处理。

                if (type == "kill" || type == "escort")
                {
                    if (o.TryGetValue("target_ref", out var targetRaw) && targetRaw is JsonString targetStr &&
                        Id.TryParse(targetStr.Value, out var targetId) &&
                        !string.Equals(targetId.Domain, "creature", System.StringComparison.Ordinal))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "objective_target_domain_mismatch",
                            $"目标类型 \"{type}\" 的 target_ref 域名必须是 \"creature\"，实际为 \"{targetId.Domain}\"",
                            recordKey: record.Key, field: $"{fieldPrefix}.target_ref");
                    }
                    // target_ref 缺失/格式非法已由 required_field/field_type（FieldKind.Id）报告。
                }
            }
        }

        private static IEnumerable<ValidationIssue> ValidateRewards(DataRecord record)
        {
            if (!record.TryGetObject("rewards", out var rewards))
            {
                yield break;
            }

            if (rewards.TryGetValue("xp", out var xpRaw) && xpRaw is JsonNumber xpNum && xpNum.Value < 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_xp_non_negative",
                    "rewards.xp 不能为负数", recordKey: record.Key, field: "rewards.xp");
            }

            if (rewards.TryGetValue("talent_points", out var tpRaw) && tpRaw is JsonNumber tpNum && tpNum.Value < 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_talent_points_non_negative",
                    "rewards.talent_points 不能为负数", recordKey: record.Key, field: "rewards.talent_points");
            }

            if (rewards.TryGetValue("items", out var itemsRaw) && itemsRaw is JsonArray items)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (!(items[i] is JsonObject item)) continue;
                    if (item.TryGetValue("count", out var countRaw) && countRaw is JsonNumber countNum &&
                        countNum.TryGetInt64(out var count) && count <= 0)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_item_count_positive",
                            "rewards.items[].count 必须为正数", recordKey: record.Key, field: $"rewards.items[{i}].count");
                    }
                }
            }

            if (rewards.TryGetValue("world_flags", out var flagsRaw) && flagsRaw is JsonArray flags)
            {
                for (var i = 0; i < flags.Count; i++)
                {
                    if (!(flags[i] is JsonObject flag)) continue;
                    if (!flag.TryGetValue("value", out var valueRaw) || valueRaw.Kind == JsonKind.Null)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_world_flag_value_required",
                            "rewards.world_flags[].value 缺失", recordKey: record.Key, field: $"rewards.world_flags[{i}].value");
                        continue;
                    }

                    // P2-06 根治：此前只检查"存在"，value=[] 一类不属于 ExprValueJson.Parse 联合
                    // 类型（Bool|Number|String|{"$id":...}）的形状在这里 0 error，直到
                    // RewardBundle.ParseWorldFlags → ExprValueJson.Parse 才抛 FormatException（外部
                    // 审计）。ExprValueJson.IsValid 直接复用 Parse 本身的判别逻辑，不重复维护第二份，
                    // 保证本处校验接受的形状集合与运行期解析永远一致。
                    if (!Core.Gameplay.Common.ExprValueJson.IsValid(valueRaw))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "reward_world_flag_value_shape",
                            $"rewards.world_flags[].value 不是合法的 ExprValue 形状（Bool|Number|String|{{\"$id\":...}}），实际类型：{valueRaw.Kind}",
                            recordKey: record.Key, field: $"rewards.world_flags[{i}].value");
                    }
                }
            }
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 38 条：prerequisite 前置链循环检测 + 未知任务引用
        // -----------------------------------------------------------------

        /// <summary>
        /// 对全表 <c>quest.def</c> 记录的 <c>prerequisite</c> 重新解析成 AST（复用
        /// <see cref="ExprParser"/>，不使用正则），提取其中全部 <c>quest.*</c> 引用（见
        /// <see cref="QuestExprSchemaEntries.RegisterInto"/> 登记的五个 key）的 Id 字面量参数，构成
        /// "任务 → 前置任务" 有向图：
        /// <list type="bullet">
        /// <item>引用的任务 id 不在本表已知 id 集合内——<c>quest_prerequisite_unknown</c>（阻断级，
        /// 该条边不参与后续成环检测：目标节点本就不存在，图算法无意义）。</item>
        /// <item>图中存在环（含自环——同一任务在自身 <c>prerequisite</c> 里直接引用自己）——
        /// <c>quest_prerequisite_cycle</c>（阻断级，算法同
        /// <see cref="Core.Gameplay.Dialog.DialogContentValidationRule"/> 的 <c>story_tree_cycle</c>
        /// 三色标记 DFS，只报告首个发现的环，不穷举全部环）。</item>
        /// </list>
        /// <c>prerequisite</c> 文本语法本身不可解析（<see cref="ExprParseException"/>）已由
        /// <c>expr_parsable</c> 结构校验报告，本方法遇到该情形跳过该记录（不重复报告，也不在损坏的
        /// 语法树上继续构图）。
        /// </summary>
        private IEnumerable<ValidationIssue> ValidatePrerequisiteGraph(IReadOnlyList<DataRecord> records)
        {
            var knownIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                knownIds.Add(r.Key);
            }

            // fromId -> 该任务 prerequisite 里出现的全部 quest.* 引用目标 id（已知目标才计入，见上）。
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var nodeOrder = records.Select(r => r.Key).ToList();

            foreach (var record in records)
            {
                if (!record.TryGetString("prerequisite", out var text) || string.IsNullOrEmpty(text))
                {
                    continue;
                }

                ExprNode root;
                try
                {
                    root = ExprParser.Parse(text, _exprSchema);
                }
                catch (ExprParseException)
                {
                    // 语法错误已由 expr_parsable 报告，见方法顶部判断记录。
                    continue;
                }

                var idArgs = new List<ExprLiteralNode>();
                QuestReferenceExtractor.CollectQuestIdLiteralArgs(root, idArgs);

                if (idArgs.Count == 0)
                {
                    continue;
                }

                var targets = edges.TryGetValue(record.Key, out var existing)
                    ? existing
                    : (edges[record.Key] = new List<string>());

                foreach (var idArg in idArgs)
                {
                    var targetId = idArg.Value.AsId.ToString();

                    if (!knownIds.Contains(targetId))
                    {
                        // 消费方反馈第 57 条：单节点类诊断填入该节点（本任务）自身 id，见
                        // ValidationIssue.AffectedNodeIds 判断记录。
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, QuestSchemas.Def.Name, "quest_prerequisite_unknown",
                            $"prerequisite 引用了不存在的任务 \"{targetId}\"" + PositionSuffix(idArg),
                            recordKey: record.Key, field: "prerequisite", group: null, note: null, ruleId: null,
                            affectedNodeIds: new[] { record.Key });
                        continue; // 目标节点不存在，不参与下面的成环检测。
                    }

                    targets.Add(targetId);
                }
            }

            if (TryFindCycle(nodeOrder, edges, knownIds, out var cyclePath))
            {
                // 消费方反馈第 57 条：跨节点路径类诊断按环上出现顺序填入环上全部节点 id，见
                // ValidationIssue.AffectedNodeIds 判断记录。
                yield return new ValidationIssue(
                    ValidationSeverity.Error, QuestSchemas.Def.Name, "quest_prerequisite_cycle",
                    $"任务前置链成环：{string.Join(" -> ", cyclePath)}",
                    recordKey: cyclePath[0], field: "prerequisite", group: null, note: null, ruleId: null,
                    affectedNodeIds: cyclePath);
            }
        }

        /// <summary>把 <see cref="ExprIssue"/> 同款"位置 X，长度 Y"格式附加在消息末尾（复用
        /// <see cref="ExprNode.Start"/>/<see cref="ExprNode.Length"/> 的位置能力，<c>Start</c> 为
        /// <c>-1</c>（未知，理论上不会发生——<see cref="ExprParser.Parse"/> 产出的节点恒带精确区间）
        /// 时不附加任何后缀）。</summary>
        private static string PositionSuffix(ExprNode node) =>
            node.Start >= 0 ? $"（位置 {node.Start}，长度 {node.Length}）" : string.Empty;

        /// <summary>三色标记的迭代式 DFS 成环检测，算法与
        /// <see cref="Core.Gameplay.Dialog.DialogContentValidationRule"/> 的
        /// <c>story_tree_cycle</c> 检查同款（见该类型判断记录），只是图节点是 <c>quest.def</c> 记录 id、
        /// 边来自重新解析 <c>prerequisite</c> AST 得到的 <c>quest.*</c> 引用（见
        /// <see cref="ValidatePrerequisiteGraph"/>），不是剧情树节点的 <c>branches[].next_node_id</c>。
        /// 自环（<c>edges[id]</c> 含 <c>id</c> 自身）按同一套算法自然判定为环，不需要特判。</summary>
        private static bool TryFindCycle(
            List<string> nodeOrder,
            Dictionary<string, List<string>> edges,
            HashSet<string> nodeIdSet,
            out IReadOnlyList<string> cyclePath)
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=未访问 1=访问中 2=已完成
            var path = new List<string>();

            foreach (var start in nodeOrder)
            {
                if (state.TryGetValue(start, out var s0) && s0 != 0)
                {
                    continue;
                }
                if (Visit(start, edges, nodeIdSet, state, path))
                {
                    cyclePath = path;
                    return true;
                }
            }

            cyclePath = Array.Empty<string>();
            return false;
        }

        private static bool Visit(
            string nodeId,
            Dictionary<string, List<string>> edges,
            HashSet<string> nodeIdSet,
            Dictionary<string, int> state,
            List<string> path)
        {
            state[nodeId] = 1;
            path.Add(nodeId);

            if (edges.TryGetValue(nodeId, out var targets))
            {
                foreach (var target in targets)
                {
                    if (!nodeIdSet.Contains(target))
                    {
                        continue; // 未知引用属于另一条校验项（quest_prerequisite_unknown），本方法只管成环检测。
                    }

                    if (state.TryGetValue(target, out var targetState))
                    {
                        if (targetState == 1)
                        {
                            path.Add(target);
                            return true;
                        }
                        if (targetState == 2)
                        {
                            continue;
                        }
                    }

                    if (Visit(target, edges, nodeIdSet, state, path))
                    {
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[nodeId] = 2;
            return false;
        }
    }
}

using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <c>loot.table</c> 专属校验规则（见 <c>IValidationRule</c>"模块专属校验规则的扩展点"）。
    /// <para>
    /// ADR-0019 / F1b 退役说明：此前本类整条委托 <see cref="LootTableParser.Parse"/>（运行期解析器）
    /// 把任何结构/范围问题统一转成一条 <c>loot_content</c> <see cref="ValidationIssue"/>。现在
    /// <see cref="LootSchemas.Table"/> 已把 <c>groups[].roll_mode</c>/<c>entries[].ref</c>/
    /// <c>weight_or_chance</c>/<c>condition</c>/<c>count_range</c> 登记为机器可读子结构（见该类型
    /// 注释），<c>DataRegistry</c> 的内置递归校验（<c>required_field</c>/<c>field_type</c>/
    /// <c>expr_parsable</c>）已经覆盖此前经 <c>Parse</c> 间接报出的全部纯结构坏形状——分组/条目不是
    /// 对象、字段缺失、类型不对、<c>roll_mode</c> 非法取值、<c>condition</c> 解析失败。本类不再调用
    /// <see cref="LootTableParser.Parse"/>（避免同一坏形状被两套机制各报一次），改为直接读取原始 JSON
    /// 只做登记表达不了的业务判断：
    /// </para>
    /// <list type="number">
    /// <item><description><c>ref</c> 领域段必须是 <c>item</c> 或 <c>loot</c>，且目标记录必须存在——
    /// 域名本身不是 <see cref="FieldKind.Id"/> 能表达的约束（见 <see cref="LootSchemas"/> 判断记录），
    /// 存在性检查沿用 <c>core/gameplay/spawn.SpawnContentRefRule</c> 惯例："目标表已加载才检查是否
    /// 存在，未加载视为无法判定、不报告"（保证只装配本模块单表的测试/校验场景不会因为 <c>item.template</c>
    /// 未加载而误报）。</description></item>
    /// <item><description><c>weight_or_chance</c> 的合法区间随同一分组的 <c>roll_mode</c> 变化
    /// （<c>chance_each</c>: [0,1]；<c>weighted_pick_one</c>: &gt;=0），登记层看不到"父级取值"，
    /// 是数值范围约束，登记表达不了。</description></item>
    /// <item><description><c>count_range.min&gt;=1</c> 且 <c>max&gt;=min</c>——数值范围约束。</description></item>
    /// <item><description><c>pick_count&gt;=1</c>、<c>guaranteed_min&gt;=0</c>——数值范围约束。</description></item>
    /// <item><description>嵌套 <c>loot.*</c> 引用成环（DFS，见 08 第 1.1 节"支持嵌套引用"、任务书
    /// "校验规则：嵌套引用无环（DFS）"）——跨记录判断，登记表达不了。</description></item>
    /// </list>
    /// <para>
    /// 上述五类检查对应字段/分组本身结构不合法（已被 <c>DataRegistry</c> 结构校验报过
    /// <c>required_field</c>/<c>field_type</c>）的记录一律静默跳过，不重复报告、也不因为跳过而
    /// 误报"缺失"。<see cref="LootTableParser"/> 本身不变——运行期 <see cref="LootHost"/> 仍需要它
    /// 对任意来源（含未经 <see cref="DataRegistry"/> 校验的数据）做完整解析。
    /// </para>
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，需组装层/测试显式
    /// <c>registry.RegisterValidationRule(new LootContentValidationRule())</c>（惯例同
    /// <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class LootContentValidationRule : IValidationRule
    {
        private const string Check = "loot_content";

        private const string ItemTemplateTable = "item.template";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = view.GetAll(LootSchemas.Table.Name);
            var itemTableLoaded = TableLoaded(view, ItemTemplateTable);

            // 图节点：全部已加载的 loot.table 记录（无论其内部结构是否合法，均作为 DFS 起点候选，
            // 惯例同退役前的实现——结构不合法的记录不会产生任何出边，DFS 到它就地结束，不影响其它
            // 记录的成环判断）。
            var graph = new Dictionary<Id, List<Id>>();
            foreach (var record in records)
            {
                if (record.Id.HasValue)
                {
                    graph[record.Id.Value] = new List<Id>();
                }
            }

            foreach (var record in records)
            {
                foreach (var issue in ValidateRecord(view, record, itemTableLoaded, graph))
                {
                    yield return issue;
                }
            }

            var state = new Dictionary<Id, int>(); // 0=未访问 1=在栈上 2=已完成
            foreach (var kv in graph)
            {
                if (state.TryGetValue(kv.Key, out var s) && s != 0)
                {
                    continue;
                }

                var cyclePath = new List<Id>();
                if (DetectCycle(kv.Key, graph, state, cyclePath))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                        $"嵌套 loot 引用成环：{string.Join(" -> ", cyclePath)}",
                        recordKey: kv.Key.Value, field: "groups");
                }
            }
        }

        private IEnumerable<ValidationIssue> ValidateRecord(
            IDataRegistryView view, DataRecord record, bool itemTableLoaded, Dictionary<Id, List<Id>> graph)
        {
            if (record.TryGetInt("guaranteed_min", out var guaranteedMin) && guaranteedMin < 0)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                    "guaranteed_min 不能为负数", recordKey: record.Key, field: "guaranteed_min");
            }

            if (!record.TryGetArray("groups", out var groupsArr))
            {
                yield break; // 缺失/类型不对：结构层已报告 required_field/field_type。
            }

            var outEdges = record.Id.HasValue && graph.TryGetValue(record.Id.Value, out var edges) ? edges : null;

            for (var gi = 0; gi < groupsArr.Count; gi++)
            {
                if (!(groupsArr[gi] is JsonObject groupObj))
                {
                    continue; // 结构层已报告。
                }

                string? rollMode = null;
                if (groupObj.TryGetValue("roll_mode", out var rmVal) && rmVal is JsonString rmStr)
                {
                    rollMode = rmStr.Value;
                }

                if (groupObj.TryGetValue("pick_count", out var pcVal) && pcVal is JsonNumber pcNum
                    && pcNum.TryGetInt64(out var pickCount) && pickCount < 1)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                        $"第 {gi} 个分组的 pick_count 必须 >= 1", recordKey: record.Key, field: "groups");
                }

                if (!groupObj.TryGetValue("entries", out var entriesVal) || !(entriesVal is JsonArray entriesArr))
                {
                    continue; // 结构层已报告。
                }

                for (var ei = 0; ei < entriesArr.Count; ei++)
                {
                    if (!(entriesArr[ei] is JsonObject entryObj))
                    {
                        continue; // 结构层已报告。
                    }

                    foreach (var issue in ValidateEntry(view, record, gi, ei, entryObj, rollMode, itemTableLoaded, outEdges))
                    {
                        yield return issue;
                    }
                }
            }
        }

        private IEnumerable<ValidationIssue> ValidateEntry(
            IDataRegistryView view, DataRecord record, int groupIndex, int entryIndex, JsonObject entryObj,
            string? rollMode, bool itemTableLoaded, List<Id>? outEdges)
        {
            if (entryObj.TryGetValue("ref", out var refVal) && refVal is JsonString refStr && Id.TryParse(refStr.Value, out var refId))
            {
                if (refId.Domain != "item" && refId.Domain != "loot")
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                        $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 的 ref \"{refId}\" 领域段必须是 item 或 loot",
                        recordKey: record.Key, field: "groups");
                }
                else if (refId.Domain == "item")
                {
                    if (itemTableLoaded && view.Get(ItemTemplateTable, refId) == null)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                            $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 的 ref \"{refId}\" 在表 \"{ItemTemplateTable}\" 中不存在",
                            recordKey: record.Key, field: "groups");
                    }
                }
                else // loot 域：loot.table 就是本规则正在遍历的表，必然已加载。
                {
                    outEdges?.Add(refId);

                    if (view.Get(LootSchemas.Table.Name, refId) == null)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                            $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 的 ref \"{refId}\" 在表 \"{LootSchemas.Table.Name}\" 中不存在",
                            recordKey: record.Key, field: "groups");
                    }
                }
            }
            // ref 缺失/格式不对：结构层已报告 required_field/field_type，这里不重复。

            if (rollMode != null
                && entryObj.TryGetValue("weight_or_chance", out var wocVal) && wocVal is JsonNumber wocNum)
            {
                if (rollMode == "chance_each" && (wocNum.Value < 0 || wocNum.Value > 1))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                        $"第 {groupIndex} 个分组（chance_each）第 {entryIndex} 条 entries 的 weight_or_chance " +
                        $"必须在 [0,1] 区间，实际 {wocNum.Value}",
                        recordKey: record.Key, field: "groups");
                }
                else if (rollMode == "weighted_pick_one" && wocNum.Value < 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                        $"第 {groupIndex} 个分组（weighted_pick_one）第 {entryIndex} 条 entries 的 weight_or_chance 不能为负数",
                        recordKey: record.Key, field: "groups");
                }
            }

            if (entryObj.TryGetValue("count_range", out var crVal) && crVal is JsonObject crObj
                && crObj.TryGetValue("min", out var minVal) && minVal is JsonNumber minNum && minNum.TryGetInt64(out var minLong)
                && crObj.TryGetValue("max", out var maxVal) && maxVal is JsonNumber maxNum && maxNum.TryGetInt64(out var maxLong)
                && (minLong < 1 || maxLong < minLong))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                    $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 的 count_range 不合法" +
                    $"（要求 1 <= min <= max，实际 min={minLong}, max={maxLong}）",
                    recordKey: record.Key, field: "groups");
            }
        }

        private static bool TableLoaded(IDataRegistryView view, string table)
        {
            foreach (var t in view.Tables)
            {
                if (t == table)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>标准三色 DFS 成环检测：<paramref name="path"/> 累积成功后用于报告一条具体环路。</summary>
        private static bool DetectCycle(Id current, IReadOnlyDictionary<Id, List<Id>> graph, IDictionary<Id, int> state, List<Id> path)
        {
            state[current] = 1;
            path.Add(current);

            if (graph.TryGetValue(current, out var neighbors))
            {
                foreach (var next in neighbors)
                {
                    if (!graph.ContainsKey(next))
                    {
                        // 引用了未加载/不存在的 loot.table：不是本规则职责（04 第 5 节
                        // reference_integrity 类检查负责该值本身是否可解析；本方法上层的
                        // "ref 存在性"业务判断已经在 ValidateEntry 里单独报过），这里只是不参与
                        // 成环判断的图遍历。
                        continue;
                    }

                    if (state.TryGetValue(next, out var s) && s == 1)
                    {
                        path.Add(next);
                        return true;
                    }

                    if (!state.TryGetValue(next, out var s2) || s2 == 0)
                    {
                        if (DetectCycle(next, graph, state, path))
                        {
                            return true;
                        }
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[current] = 2;
            return false;
        }
    }
}

using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <c>loot.table</c> 记录 → <see cref="LootTableDef"/> 的唯一解析入口（见
    /// <see cref="LootSchemas"/> 类型注释"深层结构由本类解析"）。<see cref="LootHost"/>（运行期加载）
    /// 与 <see cref="LootContentValidationRule"/>（内容校验，捕获本类抛出的异常转成
    /// <see cref="ValidationIssue"/>）共用同一份解析逻辑，避免"运行期认识的结构"与"校验期认识的结构"
    /// 出现偏差（惯例同 <c>core/rules/expr_host.RulesExprSchema</c>"同一份 schema 供内容校验与运行期
    /// 共用"）。<c>condition</c> 文本默认用 <c>core/rules/expr_host.RulesExprSchema.Base</c>
    /// 解析——严格模式下 <c>Base</c> 只覆盖 <c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/
    /// <c>time</c> 五个分组（见该类型判断记录），<see cref="LootExprSchemaEntries"/> 本身不新增任何
    /// 分组/键，只是"沿用既有分组"（见该类型注释）；若内容需要引用 <c>world</c>/<c>quest</c>/
    /// <c>player</c> 分组（如 08 第 1.1 节示例"任务相关掉落只在任务激活时可能出现"），调用方（组装层
    /// 的宿主装配代码）需要经 <paramref name="exprSchema"/> 显式传入
    /// <c>RulesExprSchema.Compose(questModuleSchema, ...)</c> 一类合并后的 schema——本类不硬编码
    /// <c>Base</c>，只把它作为未显式传参时的默认值。字段/结构不合法时抛 <see cref="DataFieldException"/>，
    /// 惯例同 <see cref="DataRecord"/> 各 <c>GetXxx</c> 访问器。
    /// </summary>
    public static class LootTableParser
    {
        public static LootTableDef Parse(DataRecord record, IExprSchema? exprSchema = null)
        {
            var schema = exprSchema ?? Core.Rules.ExprHost.RulesExprSchema.Base;
            var id = record.GetId("id");
            var groupsArray = record.GetArray("groups");

            var groups = new List<LootGroup>(groupsArray.Count);
            for (var gi = 0; gi < groupsArray.Count; gi++)
            {
                if (!(groupsArray[gi] is JsonObject groupObj))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "groups",
                        $"第 {gi} 个分组不是对象");
                }

                groups.Add(ParseGroup(record, gi, groupObj, schema));
            }

            int? guaranteedMin = record.TryGetInt("guaranteed_min", out var gm) ? (int)gm : (int?)null;
            if (guaranteedMin.HasValue && guaranteedMin.Value < 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "guaranteed_min", "不能为负数");
            }

            return new LootTableDef(id, groups, guaranteedMin);
        }

        private static LootGroup ParseGroup(DataRecord record, int groupIndex, JsonObject groupObj, IExprSchema exprSchema)
        {
            if (!groupObj.TryGetValue("roll_mode", out var rollModeVal) || !(rollModeVal is JsonString rollModeStr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组缺少字符串字段 roll_mode");
            }

            LootRollMode rollMode;
            switch (rollModeStr.Value)
            {
                case "chance_each": rollMode = LootRollMode.ChanceEach; break;
                case "weighted_pick_one": rollMode = LootRollMode.WeightedPickOne; break;
                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "groups",
                        $"第 {groupIndex} 个分组的 roll_mode 值 \"{rollModeStr.Value}\" 不合法（只能是 chance_each 或 weighted_pick_one）");
            }

            if (!groupObj.TryGetValue("entries", out var entriesVal) || !(entriesVal is JsonArray entriesArr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组缺少数组字段 entries");
            }

            var entries = new List<LootEntry>(entriesArr.Count);
            for (var ei = 0; ei < entriesArr.Count; ei++)
            {
                if (!(entriesArr[ei] is JsonObject entryObj))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "groups",
                        $"第 {groupIndex} 个分组第 {ei} 条 entries 不是对象");
                }

                entries.Add(ParseEntry(record, groupIndex, ei, rollMode, entryObj, exprSchema));
            }

            int? pickCount = null;
            if (groupObj.TryGetValue("pick_count", out var pickCountVal) && pickCountVal is JsonNumber pn && pn.TryGetInt64(out var pc))
            {
                pickCount = (int)pc;
                if (pickCount.Value < 1)
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "groups",
                        $"第 {groupIndex} 个分组的 pick_count 必须 >= 1");
                }
            }

            return new LootGroup(rollMode, entries, pickCount);
        }

        private static LootEntry ParseEntry(DataRecord record, int groupIndex, int entryIndex, LootRollMode rollMode, JsonObject entryObj, IExprSchema exprSchema)
        {
            if (!entryObj.TryGetValue("ref", out var refVal) || !(refVal is JsonString refStr) || !Id.TryParse(refStr.Value, out var refId))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 缺少合法的 ref 字段");
            }

            if (refId.Domain != "item" && refId.Domain != "loot")
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 的 ref \"{refId}\" 领域段必须是 item 或 loot");
            }

            if (!entryObj.TryGetValue("weight_or_chance", out var wocVal) || !(wocVal is JsonNumber wocNum))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 缺少数值字段 weight_or_chance");
            }

            var weightOrChance = wocNum.Value;
            if (rollMode == LootRollMode.ChanceEach && (weightOrChance < 0 || weightOrChance > 1))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组（chance_each）第 {entryIndex} 条 entries 的 weight_or_chance " +
                    $"必须在 [0,1] 区间，实际 {weightOrChance}");
            }

            if (rollMode == LootRollMode.WeightedPickOne && weightOrChance < 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组（weighted_pick_one）第 {entryIndex} 条 entries 的 weight_or_chance 不能为负数");
            }

            ExprNode? condition = null;
            if (entryObj.TryGetValue("condition", out var condVal) && condVal is JsonString condStr && !string.IsNullOrEmpty(condStr.Value))
            {
                try
                {
                    condition = ExprParser.Parse(condStr.Value, exprSchema);
                }
                catch (ExprParseException ex)
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "groups",
                        $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 的 condition 解析失败：{ex.Message}");
                }
            }

            if (!entryObj.TryGetValue("count_range", out var crVal) || !(crVal is JsonObject crObj)
                || !crObj.TryGetValue("min", out var minVal) || !(minVal is JsonNumber minNum) || !minNum.TryGetInt64(out var minLong)
                || !crObj.TryGetValue("max", out var maxVal) || !(maxVal is JsonNumber maxNum) || !maxNum.TryGetInt64(out var maxLong))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 缺少合法的 count_range {{min,max}} 字段");
            }

            var min = (int)minLong;
            var max = (int)maxLong;
            if (min < 1 || max < min)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "groups",
                    $"第 {groupIndex} 个分组第 {entryIndex} 条 entries 的 count_range 不合法（要求 1 <= min <= max，实际 min={min}, max={max}）");
            }

            return new LootEntry(refId, weightOrChance, condition, min, max);
        }
    }
}

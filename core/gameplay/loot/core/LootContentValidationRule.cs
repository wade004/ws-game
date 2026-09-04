using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <c>loot.table</c> 专属校验规则（见 <c>IValidationRule</c>"模块专属校验规则的扩展点"）：结构/
    /// 取值范围校验复用 <see cref="LootTableParser"/>（解析失败即报错，见该类型注释"同一份解析逻辑"），
    /// 本类另外补上 <see cref="LootTableParser"/> 单表解析无法发现的跨表问题——嵌套 <c>loot.*</c> 引用
    /// 成环（DFS，见 08 第 1.1 节"支持嵌套引用"、任务书"校验规则：嵌套引用无环（DFS）"）。
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，需组装层/测试显式
    /// <c>registry.RegisterValidationRule(new LootContentValidationRule())</c>（惯例同
    /// <c>core/carriers/creature</c> 的 <c>CreatureContentValidationRule</c>）。
    /// </para>
    /// </summary>
    public sealed class LootContentValidationRule : IValidationRule
    {
        private const string Check = "loot_content";

        private readonly IExprSchema? _conditionSchema;

        /// <summary><paramref name="conditionSchema"/> 见
        /// <c>LootTableParser</c>/<c>LootHost</c> 判断记录：未提供时默认
        /// <c>RulesExprSchema.Base</c>，组装层需要校验引用 <c>world</c>/<c>quest</c>/<c>player</c>
        /// 分组的 <c>condition</c> 时应传入与运行期 <c>LootHost</c> 构造时同一份（或至少签名兼容的）
        /// <see cref="IExprSchema"/>，避免"校验时认为合法、运行时其实不认识"的偏差。</summary>
        public LootContentValidationRule(IExprSchema? conditionSchema = null)
        {
            _conditionSchema = conditionSchema;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = view.GetAll(LootSchemas.Table.Name);
            var parsed = new Dictionary<Id, LootTableDef>();

            foreach (var record in records)
            {
                DataFieldException? failure = null;
                try
                {
                    var def = LootTableParser.Parse(record, _conditionSchema);
                    parsed[def.Id] = def;
                }
                catch (DataFieldException ex)
                {
                    // CS1631：catch 子句体内不允许 yield return，先捕获异常引用，离开 catch 块后再产出。
                    failure = ex;
                }

                if (failure != null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, LootSchemas.Table.Name, Check, failure.Message, recordKey: record.Key);
                }
            }

            // 成环检测：只在已成功解析的子集上做 DFS——解析失败的记录已经各自报过一条 Error，
            // 不需要在这里重复诊断，且其结构本就不足以安全参与图遍历。
            var state = new Dictionary<Id, int>(); // 0=未访问 1=在栈上 2=已完成
            foreach (var kv in parsed)
            {
                if (state.TryGetValue(kv.Key, out var s) && s != 0)
                {
                    continue;
                }

                var cyclePath = new List<Id>();
                if (DetectCycle(kv.Key, parsed, state, cyclePath))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, LootSchemas.Table.Name, Check,
                        $"嵌套 loot 引用成环：{string.Join(" -> ", cyclePath)}",
                        recordKey: kv.Key.Value, field: "groups");
                }
            }
        }

        /// <summary>标准三色 DFS 成环检测：<paramref name="path"/> 累积成功后用于报告一条具体环路。</summary>
        private static bool DetectCycle(Id current, IReadOnlyDictionary<Id, LootTableDef> tables, IDictionary<Id, int> state, List<Id> path)
        {
            state[current] = 1;
            path.Add(current);

            if (tables.TryGetValue(current, out var def))
            {
                foreach (var group in def.Groups)
                {
                    foreach (var entry in group.Entries)
                    {
                        if (entry.Ref.Domain != "loot")
                        {
                            continue;
                        }

                        if (!tables.ContainsKey(entry.Ref))
                        {
                            // 引用了未加载/不存在的 loot.table：不是本规则职责（04 第 5 节
                            // reference_integrity 校验项负责；本模块字段声明为 FieldKind.Id 而非
                            // Reference，见 LootSchemas 判断记录，因此这里不重复报错，只是不参与
                            // 成环判断的图遍历）。
                            continue;
                        }

                        if (state.TryGetValue(entry.Ref, out var s) && s == 1)
                        {
                            path.Add(entry.Ref);
                            return true;
                        }

                        if (!state.TryGetValue(entry.Ref, out var s2) || s2 == 0)
                        {
                            if (DetectCycle(entry.Ref, tables, state, path))
                            {
                                return true;
                            }
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

using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// <c>stat.definition.derived_from</c> 专属的内容校验规则（分阶段落地计划 T-N1-2；ADR-0030
    /// 决策 2"派生关系必须无环"；04 第 5 节数值类校验项分级表"派生无环"）：<c>category=derived</c>
    /// 的属性，其 <c>derived_from[].stat</c> 声明的来源链不得成环（含属性以自身为来源的自环）。
    /// 写法与检查名风格照抄 <c>Core.Numbers.Archetype</c> 层的
    /// <c>ArchTalentTreeCycleValidationRule</c>（同一"收集依赖表 + DFS 三色标记"算法，只是这里的
    /// "依赖"是 <c>derived_from</c> 而不是 <c>prerequisites</c>）。调用方需要
    /// <c>registry.RegisterValidationRule(new StatDefinitionDerivationCycleValidationRule())</c>
    /// 才会生效，本模块不自动注册（同 <see cref="StatDefinitionValidationRule"/>，注册时机由宿主
    /// 统一掌控）。
    /// <para>
    /// 判断记录（与 <see cref="StatHost"/> 加载期防御的分工）：本规则是内容校验阶段的阻断项，
    /// 正常数据流程下应该在这里就被拦下，不会进入 <see cref="StatHost"/> 构造；<c>StatHost</c>
    /// 自己的 <c>BuildDerivationGraph</c> 在遇到环时仍会抛 <see cref="System.InvalidOperationException"/>
    /// 作为兜底防御（04 第 4 节"报告含错误项即视为不可进入运行时"——两道防线不是重复劳动，是
    /// "内容校验"与"运行时契约"两层各自独立生效）。
    /// </para>
    /// <para>
    /// 判断记录（引用完整性不在本规则职责内）：<c>derived_from[].stat</c> 指向不存在的属性 id
    /// 由 <see cref="FieldKind.Reference"/> 的 <c>reference_integrity</c> 检查处理（见
    /// <c>DataRegistry.ValidateReferenceField</c>，覆盖任意嵌套深度，不局限于顶层标量字段）；本规则
    /// 遇到未知来源 id 时按"不成环"处理（该来源在依赖表里查不到，DFS 直接终止该分支），不重复报错。
    /// </para>
    /// </summary>
    public sealed class StatDefinitionDerivationCycleValidationRule : IValidationRule
    {
        /// <summary>检查名（04 第 5 节数值类校验项分级表"派生无环"；设计层拍板指定的具体命名，
        /// 照 <c>ArchTalentTreeCycleValidationRule</c> 检查名风格 <c>talent_prerequisite_cycle</c>
        /// 类推为 <c>stat_definition_derivation_cycle</c>）。</summary>
        public const string CheckName = "stat_definition_derivation_cycle";

        public string RuleId => nameof(StatDefinitionDerivationCycleValidationRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            // dependsOn[id] = 该属性 derived_from 声明的来源 id 列表；只有 category=derived 且带
            // derived_from 的记录才登记非空列表——非 derived 记录本就不该有 derived_from（由
            // StatDefinitionValidationRule.CheckDerivedFromRequiresDerivedCategory 另行拦截），这里
            // 统一登记一条空列表，保证下面 DFS 查到任意已知 id 时都有默认值可读，不需要额外判空。
            var dependsOn = new Dictionary<string, List<string>>();

            foreach (var record in view.GetAll("stat.definition"))
            {
                var sources = new List<string>();

                var isDerived = record.TryGetString("category", out var category) && category == "derived";
                if (isDerived && record.TryGetArray("derived_from", out var derivedArray))
                {
                    foreach (var entryValue in derivedArray)
                    {
                        if (entryValue is JsonObject entryObj
                            && entryObj.TryGetValue("stat", out var statRaw)
                            && statRaw is JsonString statStr)
                        {
                            sources.Add(statStr.Value);
                        }
                        // 元素缺 "stat" 字段或类型不对，已由 DerivedFromEntrySchema 的
                        // required_field/field_type 报过，这里静默跳过，不重复报（同
                        // ArchTalentTreeCycleValidationRule"缺 id 字段"分支的既有口径，但那里为
                        // 缺失本身另报一条——本规则只关心"环"，形状问题一律交给字段级校验）。
                    }
                }

                dependsOn[record.Key] = sources;
            }

            var reportedCycle = false;
            foreach (var id in dependsOn.Keys)
            {
                if (reportedCycle)
                {
                    break;
                }

                if (HasCycle(id, dependsOn, new HashSet<string>(), new HashSet<string>()))
                {
                    reportedCycle = true;
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "stat.definition", CheckName,
                        $"属性 \"{id}\" 的 derived_from 派生来源链存在环（含自环）", recordKey: id, field: "derived_from");
                }
            }
        }

        /// <summary>DFS 三色标记（visiting=灰、visited=黑）判环，与
        /// <c>ArchTalentTreeCycleValidationRule.HasCycle</c> 同一算法：<paramref name="id"/> 正在
        /// 访问路径上再次被访问到即成环（自环——某属性的 <c>derived_from</c> 直接引用自身——在
        /// 第一步 <c>visiting.Contains(id)</c> 判断前就会先把 id 加入 visiting，下一层递归立刻命中，
        /// 同样按环处理，不需要为自环单独分支）。</summary>
        private static bool HasCycle(
            string id, Dictionary<string, List<string>> dependsOn, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(id))
            {
                return false;
            }

            if (visiting.Contains(id))
            {
                return true;
            }

            if (!dependsOn.TryGetValue(id, out var sources))
            {
                // 未知来源 id（引用完整性问题由 reference_integrity 另行处理，见类型顶部判断记录）。
                return false;
            }

            visiting.Add(id);
            foreach (var source in sources)
            {
                if (HasCycle(source, dependsOn, visiting, visited))
                {
                    return true;
                }
            }
            visiting.Remove(id);
            visited.Add(id);
            return false;
        }
    }
}

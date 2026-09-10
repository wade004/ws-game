using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// "外形映射存在"检查项（见 04_数据与内容管线.md 第 5 节校验器检查项清单："每个出现在
    /// display 域引用集合中的逻辑 id（技能、光环、物品、生物、物件）必须在 display.map 中有
    /// 对应行"）。本模块不预设"哪些表参与外形域引用集合"——具体是 <c>skill.def</c> 还是
    /// <c>creature.template</c> 一类内容表属于更上层模块，由调用方经构造函数
    /// <c>sources</c> 注入"要检查覆盖的表 + 该表用哪个字段做逻辑 id"。
    /// </summary>
    public sealed class DisplayMapCoverageRule : IValidationRule
    {
        private const string CheckName = "display_map_coverage";

        private readonly IReadOnlyList<(string table, string idField)> _sources;

        public DisplayMapCoverageRule(IReadOnlyList<(string table, string idField)> sources)
        {
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var covered = new HashSet<Id>();
            foreach (var mapRecord in view.GetAll(DisplaySchemas.Map.Name))
            {
                if (mapRecord.TryGetId("logical_id", out var logicalId))
                {
                    covered.Add(logicalId);
                }
            }

            for (var i = 0; i < _sources.Count; i++)
            {
                var (table, idField) = _sources[i];

                foreach (var record in view.GetAll(table))
                {
                    // 字段缺失/格式不合法属于该表自己的 required_field/field_type 检查项职责，
                    // 本规则只关心"值存在时是否被 display.map 覆盖"，不重复报错。
                    if (!record.TryGetId(idField, out var id))
                    {
                        continue;
                    }

                    if (!covered.Contains(id))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error,
                            table,
                            CheckName,
                            $"逻辑 id \"{id}\" 在 display.map 中没有对应的外形映射行（logical_id 未覆盖）",
                            recordKey: record.Key,
                            field: idField);
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// [ADR-0038](../../../../architecture/adr/0038-资源引用类别前缀唯一决定路径空间.md) 决策 6
    /// 前半："资源引用字段的类别前缀必须合法，且必须落在该字段允许的类别集合内"——通用、不知道任何
    /// 具体表名的扩展点（同 <see cref="CurveMonotonicFiniteRule"/> 判断记录"只认登记形态"）：对全部
    /// 已登记 <see cref="FieldSchema.AllowedRefCategories"/> 的 <see cref="FieldKind.Id"/>/
    /// <see cref="FieldKind.IdList"/> 字段——无论它在记录顶层还是嵌在 <see cref="FieldSchema.Fields"/>/
    /// <see cref="FieldSchema.Item"/>/<see cref="FieldSchema.Map"/> 之内——逐条记录检查：取值的类别
    /// 前缀（第一个点分段）必须是 <see cref="AssetRefConventions.KnownCategories"/> 已登记的合法前缀
    /// 之一，且必须落在该字段登记的 <see cref="FieldSchema.AllowedRefCategories"/> 集合内。
    /// <para>
    /// 判断记录（不硬编码"字段名 → 允许集合"的 if 链）：允许集合完全来自 <see cref="FieldSchema.AllowedRefCategories"/>
    /// 声明（见该属性判断记录），本规则不认识 <c>vfx.def</c>/<c>display.equip_visual</c> 等任何具体
    /// 表名/字段名——新增一个持有资源引用字段的表，只需在该表 schema 上补一次
    /// <c>WithAllowedRefCategories</c> 登记，不需要改动本规则。
    /// </para>
    /// <para>
    /// 判断记录（为何不在 <c>WithAllowedRefCategories</c> 挂载时校验合法前缀集合）：
    /// <see cref="AssetRefConventions.KnownCategories"/> 是 <c>Core.Foundation.EngineAdapter</c>
    /// 模块的公开清单，随该模块演进（未来可能新增类别前缀）；<see cref="FieldSchema"/> 属
    /// <c>Core.Foundation.DataRegistry</c>，若在挂载时耦合该清单会让字段登记的合法性依赖另一个
    /// 模块当前的具体版本——与 <see cref="Core.Foundation.DataRegistry.FieldSchema.WithSoftReference"/>
    /// 不检查目标表是否真的存在同一惯例（登记时机早于任何表加载完成），本规则改在加载期（已能访问
    /// 全部已注册信息）统一核对，且把"允许集合本身是否合法"与"具体数据取值是否合法"合并成同一次
    /// 检查——若某字段的 <c>AllowedRefCategories</c> 登记了一个不在 <see cref="AssetRefConventions.KnownCategories"/>
    /// 内的前缀，对应字段的任何取值都无法通过检查，问题会在真实数据加载时暴露，不需要额外一条
    /// "元数据门禁"检查项。
    /// </para>
    /// <para>
    /// 判断记录（转正为无条件注册）：落地初期（1.44.0 之前）本规则曾经默认不注册——
    /// <c>display.equip_visual.mesh_ref</c> 登记的允许集合是 <c>model</c>/<c>paperdoll</c>
    /// （ADR-0038 决策 4 落地后的结论），当时 <c>data/_sample/display/display.equip_visual.json</c>
    /// 仍有一行 <c>mesh_ref: "sprite.item.sample_hero_hat_test"</c> 用旧的 <c>sprite</c> 前缀，若
    /// 无条件注册会让该行报错、拖垮 <c>validate_data.py --strict</c>（合并根）门禁。数据迁移任务
    /// （见 CHANGELOG 对应条目）已把该行改为 <c>paperdoll.item.sample_hero_hat_test</c>，且全仓库
    /// 逐一核对过全部资源引用类别前缀字段的实际消费型，本规则唯一的默认关闭理由已消除——按 ADR-0038
    /// 决策 6 本意（"新增一条规则"，与仓库其它 <c>*FieldGroupRule</c> 同等地位），改为与
    /// <c>PresentationSchemaCatalog.RegisterAll</c> 内 <c>DisplayKindFieldGroupRule</c>/
    /// <c>EquipVisualModeFieldGroupRule</c> 一致的无条件注册，不再经由
    /// <c>Presentation.Assembly.ContentValidationOptions</c> 任何开关控制（该开关与
    /// <c>toolchain/validator --enable-ref-category-check</c> 命令行参数均已随本次转正一并删除）。
    /// </para>
    /// </summary>
    public sealed class RefCategoryFieldRule : IValidationRule
    {
        /// <summary>检查名（ADR-0038 决策 6 前半）。</summary>
        public const string CheckName = "field_ref_category";

        private const int MaxDepth = 32;

        public string RuleId => nameof(RefCategoryFieldRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var issues = new List<ValidationIssue>();
            var tables = view.Tables;
            for (var t = 0; t < tables.Count; t++)
            {
                var table = tables[t];
                var schema = view.GetSchema(table);
                if (schema == null || !HasRefCategoryField(schema.Fields, 0))
                {
                    continue;
                }

                foreach (var record in view.GetAll(table))
                {
                    CheckFields(table, record.Key, schema.Fields, record.Raw, path: null, depth: 0, issues);
                }
            }

            return issues;
        }

        /// <summary>同 <see cref="CurveMonotonicFiniteRule"/> 判断记录"只认登记形态"的既有惯例：提前
        /// 判断某张表是否至少含一个登记了 <see cref="FieldSchema.AllowedRefCategories"/> 的字段（含
        /// 嵌套），没有就跳过整张表，避免无意义的记录遍历。</summary>
        private static bool HasRefCategoryField(IReadOnlyList<FieldSchema>? fields, int depth)
        {
            if (fields == null || depth > MaxDepth)
            {
                return false;
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (field.AllowedRefCategories != null)
                {
                    return true;
                }
                if (field.Fields != null && HasRefCategoryField(field.Fields, depth + 1))
                {
                    return true;
                }
                if (field.Item != null && (field.Item.AllowedRefCategories != null || HasRefCategoryField(field.Item.Fields, depth + 1)))
                {
                    return true;
                }
                if (field.Map != null)
                {
                    var valueSchema = field.Map.ValueSchema;
                    if (valueSchema.AllowedRefCategories != null || HasRefCategoryField(valueSchema.Fields, depth + 1))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void CheckFields(
            string table, string recordKey, IReadOnlyList<FieldSchema> fields, JsonObject obj,
            string? path, int depth, List<ValidationIssue> issues)
        {
            if (depth > MaxDepth)
            {
                // 与 field_type 等既有子结构递归检查一致：substructure_depth 已由字段级校验报过，
                // 这里静默停止，不重复报告（同 CurveMonotonicFiniteRule 判断记录）。
                return;
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!obj.TryGetValue(field.Name, out var value) || value.Kind == JsonKind.Null)
                {
                    continue;
                }

                var fieldPath = path == null ? field.Name : path + "." + field.Name;
                CheckOneField(table, recordKey, field, fieldPath, value, depth, issues);
            }
        }

        private static void CheckOneField(
            string table, string recordKey, FieldSchema field, string fieldPath, JsonValue value,
            int depth, List<ValidationIssue> issues)
        {
            if (field.AllowedRefCategories != null)
            {
                if (field.Kind == FieldKind.IdList && value is JsonArray idListArr)
                {
                    for (var i = 0; i < idListArr.Count; i++)
                    {
                        CheckValue(table, recordKey, field.AllowedRefCategories, $"{fieldPath}[{i}]", idListArr[i], issues);
                    }
                }
                else
                {
                    CheckValue(table, recordKey, field.AllowedRefCategories, fieldPath, value, issues);
                }
            }

            // 递归：Object 的 Fields/Map，Array 的 Item——同 DataRegistry 子结构递归的既有路径记法
            // （a.b / a[下标] / a[键]）。
            if (field.Kind == FieldKind.Object && value is JsonObject childObj)
            {
                if (field.Fields != null)
                {
                    CheckFields(table, recordKey, field.Fields, childObj, fieldPath, depth + 1, issues);
                }
                else if (field.Map != null)
                {
                    var valueSchema = field.Map.ValueSchema;
                    foreach (var entry in childObj)
                    {
                        if (entry.Value.Kind == JsonKind.Null)
                        {
                            continue;
                        }
                        var mapPath = $"{fieldPath}[{entry.Key}]";
                        CheckOneField(table, recordKey, valueSchema, mapPath, entry.Value, depth + 1, issues);
                    }
                }
            }
            else if (field.Kind == FieldKind.Array && field.Item != null && value is JsonArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i].Kind == JsonKind.Null)
                    {
                        continue;
                    }
                    var itemPath = $"{fieldPath}[{i}]";
                    CheckOneField(table, recordKey, field.Item, itemPath, arr[i], depth + 1, issues);
                }
            }
        }

        private static void CheckValue(
            string table, string recordKey, IReadOnlyList<string> allowedCategories, string fieldPath,
            JsonValue value, List<ValidationIssue> issues)
        {
            if (!(value is JsonString jsonString))
            {
                // 取值不是字符串：既有 field_type 检查项已经会报错，本规则不重复报告。
                return;
            }

            var text = jsonString.Value;
            var dotIndex = text.IndexOf('.');
            var category = dotIndex < 0 ? text : text.Substring(0, dotIndex);

            if (!AssetRefConventions.KnownCategories.Contains(category))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, table, CheckName,
                    $"资源引用 \"{text}\" 的类别前缀 \"{category}\" 不合法，合法类别前缀集合：" +
                        string.Join("/", AssetRefConventions.KnownCategories),
                    recordKey: recordKey, field: fieldPath));
                return;
            }

            if (!allowedCategories.Contains(category))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, table, CheckName,
                    $"资源引用 \"{text}\" 的类别前缀 \"{category}\" 不在字段 \"{fieldPath}\" 允许的类别集合 " +
                        $"{{{string.Join("/", allowedCategories)}}} 内",
                    recordKey: recordKey, field: fieldPath));
            }
        }
    }
}

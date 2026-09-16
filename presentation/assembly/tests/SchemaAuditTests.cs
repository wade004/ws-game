using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// F3 元数据门禁（ADR-0018 决策 3、ADR-0019 决策 4）验收测试：<see cref="SchemaAudit"/> 每个
    /// 检查项各覆盖命中与放行；白名单未用警告；对真实全量登记（<see cref="SchemaAudit.EnumerateRegisteredSchemas"/>
    /// + 仓库根 <c>toolchain/schema_audit_allowlist.json</c>）跑一遍断言 0 error（这是 <c>check.ps1</c>
    /// 新增门禁步骤实际会跑的同一份检查，回归本测试即回归门禁）。
    /// </summary>
    public class SchemaAuditTests
    {
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 3; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        /// <summary>ADR-0022（04 第 3.4 节"表级归属元数据"）：本文件绝大多数用例只关心其它检查项
        /// （missing_description/composite_without_substructure/...），不是在测试 table_ownership
        /// 本身——统一给 Layer/Module 一个占位值，避免每条既有用例各自被新增的 table_ownership 检查
        /// 命中而失败；真正测试 table_ownership/field_group/time_scope_declared/idlist_reference_target
        /// 的用例见下方"ADR-0022"分组，各自单独构造不经过本 helper 的 TableSchema。</summary>
        private static TableSchema SingleFieldTable(string tableName, FieldSchema field) =>
            new TableSchema(tableName, "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                field,
            }).WithOwnership(SchemaLayer.Foundation, "test");

        [Fact]
        public void MissingDescription_ReportsError()
        {
            var table = SingleFieldTable("test.missing_desc",
                new FieldSchema("value", FieldKind.Number, required: false));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "missing_description" &&
                i.Table == "test.missing_desc" && i.FieldPath == "value");
        }

        [Fact]
        public void WithDescription_NoMissingDescriptionIssue()
        {
            var table = SingleFieldTable("test.has_desc",
                new FieldSchema("value", FieldKind.Number, required: false, description: "示例数值"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "missing_description" && i.FieldPath == "value");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void CompositeWithoutSubstructure_ObjectWithNoFieldsOrVariants_ReportsError()
        {
            var table = SingleFieldTable("test.bare_object",
                new FieldSchema("payload", FieldKind.Object, required: false, description: "占位对象"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "composite_without_substructure" &&
                i.Table == "test.bare_object" && i.FieldPath == "payload");
        }

        [Fact]
        public void CompositeWithoutSubstructure_ArrayWithNoItem_ReportsError()
        {
            var table = SingleFieldTable("test.bare_array",
                new FieldSchema("items", FieldKind.Array, required: false, description: "占位数组"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "composite_without_substructure" &&
                i.Table == "test.bare_array" && i.FieldPath == "items");
        }

        [Fact]
        public void CompositeWithoutSubstructure_ObjectWithFields_NoIssue()
        {
            var table = SingleFieldTable("test.structured_object",
                new FieldSchema("payload", FieldKind.Object, required: false, description: "结构化对象", fields: new[]
                {
                    new FieldSchema("amount", FieldKind.Number, required: true, description: "数量"),
                }));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "composite_without_substructure");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void CompositeWithoutSubstructure_ArrayWithItem_NoIssue()
        {
            var table = SingleFieldTable("test.structured_array",
                new FieldSchema("items", FieldKind.Array, required: false, description: "结构化数组",
                    item: new FieldSchema("name", FieldKind.String, required: true, description: "名字")));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "composite_without_substructure");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void CompositeWithoutSubstructure_AllowlistedEntry_SuppressesErrorAndMarksUsed()
        {
            var table = SingleFieldTable("test.allowlisted_object",
                new FieldSchema("payload", FieldKind.Object, required: false, description: "Map 型占位对象"));
            var allowlist = new SchemaAuditAllowlist(new[]
            {
                new SchemaAuditAllowlistEntry("test.allowlisted_object", "payload", "测试：Map 型对象，键动态"),
            });

            var report = SchemaAudit.Run(new[] { table }, allowlist);

            Assert.DoesNotContain(report.Issues, i => i.Check == "composite_without_substructure");
            Assert.DoesNotContain(report.Issues, i => i.Check == "allowlist_entry_unused");
        }

        [Fact]
        public void AllowlistEntry_NotMatchingAnyHit_ReportsUnusedWarning()
        {
            var table = SingleFieldTable("test.no_violation",
                new FieldSchema("value", FieldKind.Number, required: false, description: "示例数值"));
            var allowlist = new SchemaAuditAllowlist(new[]
            {
                new SchemaAuditAllowlistEntry("test.no_violation", "does_not_exist", "测试：过时的白名单条目"),
            });

            var report = SchemaAudit.Run(new[] { table }, allowlist);

            Assert.Contains(report.Issues, i =>
                i.Severity == "warning" && i.Check == "allowlist_entry_unused" &&
                i.Table == "test.no_violation" && i.FieldPath == "does_not_exist");
            // allowlist_entry_unused 是 warning，不阻断。
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void ReferenceTargetUnknown_ReferenceTableNotRegistered_ReportsError()
        {
            var table = SingleFieldTable("test.dangling_ref",
                new FieldSchema("target", FieldKind.Reference, required: false, description: "指向一个不存在的表",
                    referenceTable: "test.no_such_table"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "reference_target_unknown" &&
                i.Table == "test.dangling_ref" && i.FieldPath == "target");
        }

        [Fact]
        public void ReferenceTargetUnknown_ReferenceTableRegistered_NoIssue()
        {
            var targetTable = SingleFieldTable("test.ref_target",
                new FieldSchema("value", FieldKind.Number, required: false, description: "示例数值"));
            var sourceTable = SingleFieldTable("test.ref_source",
                new FieldSchema("target", FieldKind.Reference, required: false, description: "指向 test.ref_target",
                    referenceTable: "test.ref_target"));

            var report = SchemaAudit.Run(new[] { targetTable, sourceTable }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "reference_target_unknown");
        }

        [Fact]
        public void ReferenceTargetUnknown_ReferenceDomainNotInKnownList_ReportsError()
        {
            var table = SingleFieldTable("test.dangling_domain_ref",
                new FieldSchema("target", FieldKind.Reference, required: false, description: "指向一个不存在的域",
                    referenceDomain: "not_a_real_domain"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "reference_target_unknown" &&
                i.Table == "test.dangling_domain_ref" && i.FieldPath == "target");
        }

        [Fact]
        public void ReferenceTargetUnknown_ReferenceDomainInKnownList_NoIssue()
        {
            var table = SingleFieldTable("test.known_domain_ref",
                new FieldSchema("target", FieldKind.Reference, required: false, description: "指向 skill 域下任意已加载表",
                    referenceDomain: "skill"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "reference_target_unknown");
        }

        private static VariantSchema BuildVariants(string discriminator, IReadOnlyDictionary<string, IReadOnlyList<FieldSchema>> cases, IReadOnlyList<FieldSchema>? commonFields = null) =>
            new VariantSchema(discriminator, cases, commonFields);

        [Fact]
        public void VariantShape_EmptyCaseKey_ReportsError()
        {
            var variants = BuildVariants("kind", new Dictionary<string, IReadOnlyList<FieldSchema>>
            {
                [""] = new FieldSchema[] { new FieldSchema("amount", FieldKind.Number, required: true, description: "数量") },
            });
            var table = SingleFieldTable("test.variant_empty_key",
                new FieldSchema("shape", FieldKind.Object, required: false, description: "变体对象", variants: variants));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "variant_shape" &&
                i.Table == "test.variant_empty_key" && i.FieldPath == "shape");
        }

        [Fact]
        public void VariantShape_CaseFieldNameCollidesWithCommonFields_ReportsError()
        {
            var variants = BuildVariants("kind",
                new Dictionary<string, IReadOnlyList<FieldSchema>>
                {
                    ["circle"] = new FieldSchema[] { new FieldSchema("radius", FieldKind.Number, required: true, description: "半径") },
                },
                commonFields: new FieldSchema[] { new FieldSchema("radius", FieldKind.Number, required: false, description: "公共半径") });
            var table = SingleFieldTable("test.variant_common_collision",
                new FieldSchema("shape", FieldKind.Object, required: false, description: "变体对象", variants: variants));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "variant_shape" &&
                i.Table == "test.variant_common_collision" && i.FieldPath == "shape" &&
                i.Message.Contains("radius") && i.Message.Contains("CommonFields"));
        }

        [Fact]
        public void VariantShape_CaseFieldNameCollidesWithDiscriminator_ReportsError()
        {
            var variants = BuildVariants("kind", new Dictionary<string, IReadOnlyList<FieldSchema>>
            {
                ["circle"] = new FieldSchema[] { new FieldSchema("kind", FieldKind.Number, required: true, description: "与判别字段撞名") },
            });
            var table = SingleFieldTable("test.variant_discriminator_collision",
                new FieldSchema("shape", FieldKind.Object, required: false, description: "变体对象", variants: variants));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "variant_shape" &&
                i.Table == "test.variant_discriminator_collision" && i.FieldPath == "shape" &&
                i.Message.Contains("判别字段"));
        }

        [Fact]
        public void VariantShape_WellFormed_NoIssue()
        {
            var variants = BuildVariants("kind",
                new Dictionary<string, IReadOnlyList<FieldSchema>>
                {
                    ["circle"] = new FieldSchema[] { new FieldSchema("radius", FieldKind.Number, required: true, description: "半径") },
                    ["rect"] = new FieldSchema[] { new FieldSchema("width", FieldKind.Number, required: true, description: "宽度") },
                },
                commonFields: new FieldSchema[] { new FieldSchema("enabled", FieldKind.Bool, required: false, description: "是否启用") });
            var table = SingleFieldTable("test.variant_well_formed",
                new FieldSchema("shape", FieldKind.Object, required: false, description: "变体对象", variants: variants));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "variant_shape");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void UnschematizedTable_ReportsWarning()
        {
            var table = TableSchema.Unschematized("test.unschematized", "id");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "warning" && i.Check == "unschematized_table" && i.Table == "test.unschematized");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        // -----------------------------------------------------------------
        // ADR-0021（04 第 4 节勘误"范围约束"）：field_range_kind 自洽检查
        // -----------------------------------------------------------------

        /// <summary>Range 登记在非 Number/Int 字段上——FieldSchema.WithRange 本身不检查 Kind（见
        /// FieldRange 类型顶部判断记录），必须靠本审计项在这里拦下。</summary>
        [Fact]
        public void FieldRangeKind_RangeOnStringField_ReportsError()
        {
            var table = SingleFieldTable("test.range_on_string",
                new FieldSchema("value", FieldKind.String, required: false, description: "非数值字段")
                    .WithRange(FieldRange.Range(min: 0)));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "field_range_kind" &&
                i.Table == "test.range_on_string" && i.FieldPath == "value");
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void FieldRangeKind_RangeOnNumberField_NoIssue()
        {
            var table = SingleFieldTable("test.range_on_number",
                new FieldSchema("value", FieldKind.Number, required: false, description: "数值字段")
                    .WithRange(FieldRange.Range(min: 0, max: 1)));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "field_range_kind");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void FieldRangeKind_RangeOnIntField_NoIssue()
        {
            var table = SingleFieldTable("test.range_on_int",
                new FieldSchema("value", FieldKind.Int, required: false, description: "整数字段")
                    .WithRange(FieldRange.Range(min: 1)));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "field_range_kind");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        /// <summary>回归测试（2026-09-09 修复）：<see cref="FieldSchema.Item"/> 允许惰性 factory 让
        /// 数组元素结构复用自身（如 <c>skill.def.effects[].params.on_hit_effects</c> 复用
        /// <c>effects</c> 自身元素结构，见该类型注释）。本用例构造一个真正自引用的 <see cref="FieldSchema"/>
        /// 图（<c>node</c> 字段是 Object，唯一子字段 <c>child</c> 是同一个 <c>node</c> 对象自身），
        /// 断言 <see cref="SchemaAudit.Run"/> 不会无限递归/不会把同一个字段对象在同一路径上重复展开
        /// 报告多次——只应该报告一次 <c>missing_description</c>（<c>node</c> 字段本身缺描述），
        /// 不应该产生 <c>node.child.child.child...</c> 这样深度膨胀的重复路径。</summary>
        [Fact]
        public void SelfReferentialFieldSchema_DoesNotInfiniteLoop_AndDoesNotDuplicateReports()
        {
            // FieldSchema 的 Fields 是构造期只读列表，不能延迟自引用（不像 Item/Variants 支持
            // itemFactory/variantsFactory）；用 Array 自引用（Item 支持 itemFactory 惰性求值，
            // 与 skill.def.effects 的真实自引用形状一致，见类型注释）。
            FieldSchema selfArray = null!;
            selfArray = new FieldSchema("effects", FieldKind.Array, required: false,
                itemFactory: () => selfArray);

            var table = SingleFieldTable("test.self_referential", selfArray);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            var missingDescForEffects = report.Issues.Where(i =>
                i.Check == "missing_description" && i.Table == "test.self_referential" && i.FieldPath == "effects").ToList();
            Assert.Single(missingDescForEffects);

            // FieldCount 应该是有限、很小的数（自引用只展开一层就被环检测挡住），不应该是
            // MaxDepth 量级（若环检测失效，会是数十/上百）。
            Assert.True(report.FieldCount < 10, $"FieldCount={report.FieldCount}，疑似环检测失效导致展开过深");
        }

        // -----------------------------------------------------------------
        // ADR-0024（04 第 3.3 节"映射登记"）：field_map_kind / field_map_conflict 自洽检查 +
        // 映射值递归展开（复用 missing_description 等既有检查项）+ 映射键引用目标检查
        // -----------------------------------------------------------------

        /// <summary>Map 登记在非 Object 字段上——FieldSchema.WithMap 本身不检查 Kind（同 WithRange
        /// 判断记录），必须靠本审计项在这里拦下。</summary>
        [Fact]
        public void FieldMapKind_MapOnArrayField_ReportsError()
        {
            var table = SingleFieldTable("test.map_on_array",
                new FieldSchema("value", FieldKind.Array, required: false, description: "非 Object 字段")
                    .WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("v", FieldKind.Number, required: true, description: "值"))));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "field_map_kind" &&
                i.Table == "test.map_on_array" && i.FieldPath == "value");
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void FieldMapKind_MapOnObjectField_NoIssue()
        {
            var table = SingleFieldTable("test.map_on_object",
                new FieldSchema("base_stats", FieldKind.Object, required: false, description: "属性映射")
                    .WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("v", FieldKind.Number, required: true, description: "值"))));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "field_map_kind");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        /// <summary>Map 与 Fields 同时登记——两者本不可能经同一次构造函数调用产生（Fields 是构造期
        /// 参数、Map 是事后 WithMap 挂载），但物理上可以先构造带 Fields 的字段再 WithMap，本审计项
        /// 必须能拦下这种组合。</summary>
        [Fact]
        public void FieldMapConflict_MapAndFieldsBothSet_ReportsError()
        {
            var field = new FieldSchema("payload", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("known", FieldKind.String, required: false, description: "固定键"),
            }, description: "冲突登记").WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("v", FieldKind.Number, required: true, description: "值")));
            var table = SingleFieldTable("test.map_fields_conflict", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "field_map_conflict" &&
                i.Table == "test.map_fields_conflict" && i.FieldPath == "payload");
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void FieldMapConflict_MapAndVariantsBothSet_ReportsError()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["a"] = Array.Empty<FieldSchema>(),
            };
            var field = new FieldSchema("payload", FieldKind.Object, required: false,
                variants: new VariantSchema("kind", cases), description: "冲突登记")
                .WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("v", FieldKind.Number, required: true, description: "值")));
            var table = SingleFieldTable("test.map_variants_conflict", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "field_map_conflict" &&
                i.Table == "test.map_variants_conflict" && i.FieldPath == "payload");
            Assert.True(report.IsBlocking);
        }

        /// <summary>Map 登记后不再命中 composite_without_substructure（该检查项只在 Fields/Variants/
        /// Map 三者均未登记时报告）。</summary>
        [Fact]
        public void MapRegistered_DoesNotReportCompositeWithoutSubstructure()
        {
            var table = SingleFieldTable("test.map_no_bare_object",
                new FieldSchema("base_stats", FieldKind.Object, required: false, description: "属性映射")
                    .WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("v", FieldKind.Number, required: true, description: "值"))));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "composite_without_substructure");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        /// <summary>映射值递归展开：ValueSchema 缺描述时按既有 missing_description 检查项报告，路径
        /// 用 "[*]" 表示"任意键"（同 <c>DataRegistry.ValidateMapObject</c>/
        /// <c>SchemaFieldRangeExport</c> 的 "[key]"/"[*]" 记法惯例，静态审计没有具体数据键可用）。</summary>
        [Fact]
        public void MapValue_MissingDescription_ReportsErrorWithBracketStarPath()
        {
            var table = SingleFieldTable("test.map_value_missing_desc",
                new FieldSchema("base_stats", FieldKind.Object, required: false, description: "属性映射")
                    .WithMap(MapSchema.FreeKeyed("理由", new FieldSchema("v", FieldKind.Number, required: true))));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "missing_description" &&
                i.Table == "test.map_value_missing_desc" && i.FieldPath == "base_stats[*]");
        }

        [Fact]
        public void MapKeyReferenceTable_UnknownTable_ReportsReferenceTargetUnknown()
        {
            var table = SingleFieldTable("test.map_key_ref_unknown",
                new FieldSchema("base_stats", FieldKind.Object, required: false, description: "属性映射")
                    .WithMap(MapSchema.ReferenceKeyTable("no.such.table", new FieldSchema("v", FieldKind.Number, required: true, description: "值"))));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "reference_target_unknown" &&
                i.Table == "test.map_key_ref_unknown" && i.FieldPath == "base_stats");
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void MapKeyReferenceTable_KnownTable_NoIssue()
        {
            var targetTable = new TableSchema("test.map_key_ref_target", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            }).WithOwnership(SchemaLayer.Foundation, "test");
            var sourceTable = SingleFieldTable("test.map_key_ref_known",
                new FieldSchema("base_stats", FieldKind.Object, required: false, description: "属性映射")
                    .WithMap(MapSchema.ReferenceKeyTable("test.map_key_ref_target", new FieldSchema("v", FieldKind.Number, required: true, description: "值"))));

            var report = SchemaAudit.Run(new[] { targetTable, sourceTable }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "reference_target_unknown");
        }

        [Fact]
        public void MapKeyReferenceDomain_UnknownDomain_ReportsReferenceTargetUnknown()
        {
            var table = SingleFieldTable("test.map_key_domain_unknown",
                new FieldSchema("growth", FieldKind.Object, required: false, description: "成长映射")
                    .WithMap(MapSchema.ReferenceKeyDomain("no_such_domain", new FieldSchema("v", FieldKind.Number, required: true, description: "值"))));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "reference_target_unknown" &&
                i.Table == "test.map_key_domain_unknown" && i.FieldPath == "growth");
        }

        [Fact]
        public void MapKeyReferenceDomain_KnownDomain_NoIssue()
        {
            var table = SingleFieldTable("test.map_key_domain_known",
                new FieldSchema("growth", FieldKind.Object, required: false, description: "成长映射")
                    .WithMap(MapSchema.ReferenceKeyDomain("stat", new FieldSchema("v", FieldKind.Number, required: true, description: "值"))));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "reference_target_unknown");
        }

        // -----------------------------------------------------------------
        // ADR-0022：table_ownership / field_group（计算默认值）/ time_scope_declared /
        // idlist_reference_target / time_unit_missing 五项新增自洽检查
        // -----------------------------------------------------------------

        [Fact]
        public void TableOwnership_MissingLayerAndModule_ReportsError()
        {
            var table = new TableSchema("test.no_ownership", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            });

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "table_ownership" && i.Table == "test.no_ownership" &&
                i.Message.Contains("Layer"));
            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "table_ownership" && i.Table == "test.no_ownership" &&
                i.Message.Contains("Module"));
        }

        [Fact]
        public void TableOwnership_WithOwnership_NoIssue()
        {
            var table = new TableSchema("test.with_ownership", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "table_ownership");
        }

        [Fact]
        public void TableOwnership_DomainDiffersFromFirstSegmentWithoutException_ReportsError()
        {
            // "test.mismatched_domain" 首段是 "test"，但显式登记 Domain 为 "found"，且不在
            // 04 第 2.2 节三张命名例外清单内——应报错。
            var table = new TableSchema("test.mismatched_domain", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            }).WithOwnership(SchemaLayer.Foundation, "test").WithDomain("found");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "table_ownership" && i.Table == "test.mismatched_domain");
        }

        [Fact]
        public void TableOwnership_NamingExceptionTableWithoutExplicitDomain_ReportsError()
        {
            // camera_profile 是 04 第 2.2 节登记的单段名命名例外，必须显式 WithDomain("camera")，
            // 不能沿用默认的表名首段（"camera_profile" 本身）。
            var table = new TableSchema("camera_profile", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            }).WithOwnership(SchemaLayer.Presentation, "camera");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "table_ownership" && i.Table == "camera_profile");
        }

        [Fact]
        public void FieldGroup_DefaultsByKindAndName_NoIssue()
        {
            var table = new TableSchema("test.field_group_defaults", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("icon_id", FieldKind.String, required: false, description: "图标资源引用"),
                new FieldSchema("amount", FieldKind.Number, required: false, description: "数值"),
                new FieldSchema("other_thing", FieldKind.Reference, required: false, referenceTable: "test.field_group_defaults", description: "自引用示例"),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "field_group");
            Assert.Equal(FieldGroup.Basic, table.GetField("id")!.Group);
            Assert.Equal(FieldGroup.Presentation, table.GetField("icon_id")!.Group);
            Assert.Equal(FieldGroup.Numeric, table.GetField("amount")!.Group);
            Assert.Equal(FieldGroup.Reference, table.GetField("other_thing")!.Group);
        }

        [Fact]
        public void FieldGroup_ExplicitWithGroupOverridesDefault()
        {
            var field = new FieldSchema("value", FieldKind.Number, required: false, description: "数值")
                .WithGroup(FieldGroup.Advanced);

            Assert.Equal(FieldGroup.Advanced, field.Group);
        }

        [Fact]
        public void TimeScopeDeclared_UnitTimeWithoutTableTimeScope_ReportsError()
        {
            var table = new TableSchema("test.time_no_scope", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("duration", FieldKind.Number, required: false, description: "持续时间").WithUnit(FieldUnit.Time),
            }).WithOwnership(SchemaLayer.Rules, "test");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "time_scope_declared" && i.Table == "test.time_no_scope" && i.FieldPath == "duration");
        }

        [Fact]
        public void TimeScopeDeclared_UnitTimeWithTableTimeScope_NoIssue()
        {
            var table = new TableSchema("test.time_with_scope", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("duration", FieldKind.Number, required: false, description: "持续时间").WithUnit(FieldUnit.Time),
            }).WithOwnership(SchemaLayer.Rules, "test").WithTimeScope(TimeScope.Combat);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "time_scope_declared");
        }

        [Fact]
        public void IdListReferenceTarget_NeitherReferenceNorFreeIds_ReportsError()
        {
            var table = new TableSchema("test.idlist_missing_target", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("tags", FieldKind.IdList, required: false, description: "标签集合"),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "idlist_reference_target" &&
                i.Table == "test.idlist_missing_target" && i.FieldPath == "tags");
        }

        [Fact]
        public void IdListReferenceTarget_WithFreeIds_NoIssue()
        {
            var table = new TableSchema("test.idlist_free", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("tags", FieldKind.IdList, required: false, description: "标签集合")
                    .WithFreeIds("测试：自由标签，不指向任何表"),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "idlist_reference_target");
        }

        [Fact]
        public void IdListReferenceTarget_WithReferenceTable_NoIssue()
        {
            var targetTable = new TableSchema("test.idlist_ref_target", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            }).WithOwnership(SchemaLayer.Foundation, "test");
            var sourceTable = new TableSchema("test.idlist_ref_source", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("refs", FieldKind.IdList, required: false, referenceTable: "test.idlist_ref_target",
                    description: "指向 test.idlist_ref_target 的引用列表"),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { targetTable, sourceTable }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "idlist_reference_target");
            Assert.DoesNotContain(report.Issues, i => i.Check == "reference_target_unknown");
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 28/29 条（04 第 3.4 节勘误"IdList/Id 固定取值登记""软引用元数据"）：
        // idlist_allowed_values_conflict / soft_reference_kind 自洽检查 + idlist_reference_target
        // 接受 AllowedValues 作为"已声明取值来源"之一。
        // -----------------------------------------------------------------

        [Fact]
        public void IdListReferenceTarget_WithAllowedValues_NoIssue()
        {
            var table = new TableSchema("test.idlist_allowed", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("flags", FieldKind.IdList, required: false, description: "固定取值集合")
                    .WithAllowedValues(new[] { new Core.Foundation.Common.Id("flag.a"), new Core.Foundation.Common.Id("flag.b") }),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "idlist_reference_target");
        }

        [Fact]
        public void IdlistAllowedValuesConflict_WithFreeIds_ReportsError()
        {
            var field = new FieldSchema("flags", FieldKind.IdList, required: false, description: "冲突登记")
                .WithAllowedValues(new[] { new Core.Foundation.Common.Id("flag.a") })
                .WithFreeIds("测试：刻意同时登记两者");
            var table = SingleFieldTable("test.allowed_values_free_ids_conflict", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "idlist_allowed_values_conflict" &&
                i.Table == "test.allowed_values_free_ids_conflict" && i.FieldPath == "flags");
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void IdlistAllowedValuesConflict_WithReferenceTable_ReportsError()
        {
            var field = new FieldSchema("flags", FieldKind.IdList, required: false, referenceTable: "test.some_target",
                    description: "冲突登记")
                .WithAllowedValues(new[] { new Core.Foundation.Common.Id("flag.a") });
            var table = SingleFieldTable("test.allowed_values_reference_table_conflict", field);
            var targetTable = new TableSchema("test.some_target", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { table, targetTable }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "idlist_allowed_values_conflict" &&
                i.Table == "test.allowed_values_reference_table_conflict" && i.FieldPath == "flags");
        }

        [Fact]
        public void IdlistAllowedValuesConflict_AllowedValuesAlone_NoIssue()
        {
            var field = new FieldSchema("flags", FieldKind.IdList, required: false, description: "固定取值")
                .WithAllowedValues(new[] { new Core.Foundation.Common.Id("flag.a") });
            var table = SingleFieldTable("test.allowed_values_alone", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "idlist_allowed_values_conflict");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void SoftReferenceKind_OnStringField_ReportsError()
        {
            var field = new FieldSchema("value", FieldKind.String, required: false, description: "非 Id/IdList 字段")
                .WithSoftReference(table: "test.some_target");
            var table = SingleFieldTable("test.soft_reference_on_string", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "soft_reference_kind" &&
                i.Table == "test.soft_reference_on_string" && i.FieldPath == "value");
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void SoftReferenceKind_OnIdField_NoIssue()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false, description: "软引用")
                .WithSoftReference(table: "test.some_target");
            var table = SingleFieldTable("test.soft_reference_on_id", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "soft_reference_kind");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void SoftReferenceKind_OnIdListField_NoIssue()
        {
            var field = new FieldSchema("target_refs", FieldKind.IdList, required: false, description: "软引用列表")
                .WithFreeIds("测试占位")
                .WithSoftReference(domain: "test");
            var table = SingleFieldTable("test.soft_reference_on_idlist", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "soft_reference_kind");
        }

        // -----------------------------------------------------------------
        // id_description_reference_hint（消费方反馈第 30 条，04 第 5 节勘误）
        // -----------------------------------------------------------------

        [Fact]
        public void IdDescriptionReferenceHint_MentionsReferenceWithoutMetadata_ReportsWarning()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false,
                description: "引用 test.other_table 的目标记录");
            var table = SingleFieldTable("test.reference_hint_unregistered", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "warning" && i.Check == "id_description_reference_hint" &&
                i.Table == "test.reference_hint_unregistered" && i.FieldPath == "target_ref");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void IdDescriptionReferenceHint_MentionsPointsToWithoutMetadata_ReportsWarning()
        {
            var field = new FieldSchema("logical_id", FieldKind.IdList, required: false,
                description: "指向 test.other_table 的记录列表");
            var table = SingleFieldTable("test.points_to_hint_unregistered", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "warning" && i.Check == "id_description_reference_hint" &&
                i.Table == "test.points_to_hint_unregistered" && i.FieldPath == "logical_id");
        }

        [Fact]
        public void IdDescriptionReferenceHint_WithSoftReference_NoIssue()
        {
            var field = new FieldSchema("target_ref", FieldKind.Id, required: false,
                description: "引用 test.other_table 的目标记录")
                .WithSoftReference(table: "test.other_table");
            var table = SingleFieldTable("test.reference_hint_soft_ref", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "id_description_reference_hint");
        }

        [Fact]
        public void IdDescriptionReferenceHint_WithAllowedValues_NoIssue()
        {
            var field = new FieldSchema("target_ref", FieldKind.IdList, required: false,
                description: "引用固定取值集合的标签列表")
                .WithAllowedValues(new[] { new Id("test.tag_a"), new Id("test.tag_b") });
            var table = SingleFieldTable("test.reference_hint_allowed_values", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "id_description_reference_hint");
        }

        [Fact]
        public void IdDescriptionReferenceHint_WithFreeIds_NoIssue()
        {
            var field = new FieldSchema("target_refs", FieldKind.IdList, required: false,
                description: "引用自由声明的标签列表，不对应任何已登记表")
                .WithFreeIds("测试占位：自由标签，无目标表");
            var table = SingleFieldTable("test.reference_hint_free_ids", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "id_description_reference_hint");
        }

        [Fact]
        public void IdDescriptionReferenceHint_WithoutReferenceWording_NoIssue()
        {
            var field = new FieldSchema("tag", FieldKind.Id, required: false,
                description: "标签，非表内 id，按 Id 登记");
            var table = SingleFieldTable("test.reference_hint_no_wording", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "id_description_reference_hint");
        }

        [Fact]
        public void IdDescriptionReferenceHint_OnStringField_NoIssue()
        {
            // 本检查只覆盖 Id/IdList——String 字段（如引擎适配层解析的不透明资源标识）即使描述含
            // "引用"/"指向"也不在本检查范围内，见 04 第 3.4 节勘误"描述文本与引用元数据自洽"判断记录。
            var field = new FieldSchema("resource_ref", FieldKind.String, required: false,
                description: "指向具体引擎资源的不透明标识");
            var table = SingleFieldTable("test.reference_hint_string_field", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "id_description_reference_hint");
        }

        // -----------------------------------------------------------------
        // declared_reference_unregistered（消费方反馈第 37 条，04 第 4 节勘误）
        // -----------------------------------------------------------------

        [Fact]
        public void DeclaredReferenceUnregistered_FieldWithoutAnyReferenceMetadata_ReportsWarning()
        {
            var field = new FieldSchema("target_id", FieldKind.Id, required: true, description: "占位描述，未登记引用元数据");
            var table = SingleFieldTable("test.declared_reference_unregistered", field);
            var declarations = new[]
            {
                new ReferenceDeclaration("test.declared_reference_unregistered", "target_id", "test.some_target",
                    toDomain: null, isOptional: false, source: "TestCatalog"),
            };

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty, declarations);

            Assert.Contains(report.Issues, i =>
                i.Severity == "warning" && i.Check == "declared_reference_unregistered" &&
                i.Table == "test.declared_reference_unregistered" && i.FieldPath == "target_id");
            Assert.False(report.IsBlocking, string.Join("; ", MessagesOf(report)));
        }

        [Fact]
        public void DeclaredReferenceUnregistered_WithSoftReference_NoIssue()
        {
            var field = new FieldSchema("target_id", FieldKind.Id, required: true, description: "占位描述")
                .WithSoftReference(table: "test.some_target");
            var table = SingleFieldTable("test.declared_reference_soft_ref", field);
            var declarations = new[]
            {
                new ReferenceDeclaration("test.declared_reference_soft_ref", "target_id", "test.some_target",
                    toDomain: null, isOptional: false, source: "TestCatalog"),
            };

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty, declarations);

            Assert.DoesNotContain(report.Issues, i => i.Check == "declared_reference_unregistered");
        }

        [Fact]
        public void DeclaredReferenceUnregistered_WithAllowedValues_NoIssue()
        {
            var field = new FieldSchema("target_id", FieldKind.Id, required: true, description: "占位描述")
                .WithAllowedValues(new[] { new Id("test.some_target.a") });
            var table = SingleFieldTable("test.declared_reference_allowed_values", field);
            var declarations = new[]
            {
                new ReferenceDeclaration("test.declared_reference_allowed_values", "target_id", "test.some_target",
                    toDomain: null, isOptional: false, source: "TestCatalog"),
            };

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty, declarations);

            Assert.DoesNotContain(report.Issues, i => i.Check == "declared_reference_unregistered");
        }

        [Fact]
        public void DeclaredReferenceUnregistered_DeclaredFieldNotInSchema_NoIssue()
        {
            var field = new FieldSchema("target_id", FieldKind.Id, required: true, description: "占位描述");
            var table = SingleFieldTable("test.declared_reference_field_missing", field);
            var declarations = new[]
            {
                new ReferenceDeclaration("test.declared_reference_field_missing", "no_such_field", "test.some_target",
                    toDomain: null, isOptional: false, source: "TestCatalog"),
            };

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty, declarations);

            Assert.DoesNotContain(report.Issues, i => i.Check == "declared_reference_unregistered");
        }

        [Fact]
        public void DeclaredReferenceUnregistered_DeclaredTableOutOfAuditScope_NoIssue()
        {
            var field = new FieldSchema("target_id", FieldKind.Id, required: true, description: "占位描述");
            var table = SingleFieldTable("test.declared_reference_in_scope", field);
            var declarations = new[]
            {
                new ReferenceDeclaration("test.declared_reference_out_of_scope", "target_id", "test.some_target",
                    toDomain: null, isOptional: false, source: "TestCatalog"),
            };

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty, declarations);

            Assert.DoesNotContain(report.Issues, i => i.Check == "declared_reference_unregistered");
        }

        [Fact]
        public void DeclaredReferenceUnregistered_TwoArgRunOverload_SkipsCheckEntirely()
        {
            // 两参数 Run 转发到三参数重载、referenceDeclarations 传空集合（见该重载判断记录）——
            // 即便字段本身完全符合命中条件，不传 referenceDeclarations 时本检查天然 0 命中。
            var field = new FieldSchema("target_id", FieldKind.Id, required: true, description: "占位描述，未登记引用元数据");
            var table = SingleFieldTable("test.declared_reference_two_arg_run", field);

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "declared_reference_unregistered");
        }

        /// <summary>门禁本体：真实登记 + 仓库根白名单，断言 0 error（白名单未用条目仍允许——本测试
        /// 只关心不阻断，"白名单条目全部命中"由 <c>check.ps1</c> 门禁步骤的真实输出与本测试共同
        /// 覆盖，不在此重复断言，避免白名单每次调整都要同步改测试）。消费方反馈第 30 条：一并断言
        /// 新增的 <c>id_description_reference_hint</c> 告警级检查在当前全量登记上是 0 命中——该检查
        /// 是防遗漏用的门禁，不是"欢迎警告常驻"，命中即应立即补登记或改描述（见该检查项判断记录），
        /// 不像有些告警级检查允许长期非零（本项没有这类既有先例）。消费方反馈第 37 条：同一口径新增
        /// 断言 <c>declared_reference_unregistered</c> 也是 0 命中——真实登记的全部 3 条
        /// <c>DeclareReference</c> 声明（<c>arch.class.primary_stat</c>/<c>skill.def.target_shape_ref</c>/
        /// <c>skill.proc_def.trigger_skill</c>，见 <c>RulesSchemaCatalog.DeclareKnownReferences</c>）
        /// 对应字段均已补登 <c>SoftReferenceTable</c>（本条原始案例 <c>trigger_skill</c> 本次修复补齐，
        /// 另两条分别是 29/30 号反馈的既有修复）。</summary>
        [Fact]
        public void RealRegisteredSchemas_WithRepoAllowlist_ZeroErrors()
        {
            var schemas = SchemaAudit.EnumerateRegisteredSchemas();
            var referenceDeclarations = SchemaAudit.EnumerateReferenceDeclarations();

            var repoRoot = FindRepoRoot();
            var allowlistPath = Path.Combine(repoRoot, "toolchain", "schema_audit_allowlist.json");
            Assert.True(File.Exists(allowlistPath), $"找不到白名单文件：{allowlistPath}");
            var allowlist = SchemaAuditAllowlist.Parse(File.ReadAllText(allowlistPath));

            var report = SchemaAudit.Run(schemas, allowlist, referenceDeclarations);

            Assert.Equal(0, report.ErrorCount);
            Assert.False(report.IsBlocking, string.Join("\n", MessagesOf(report)));
            Assert.DoesNotContain(report.Issues, i => i.Check == "id_description_reference_hint");
            Assert.DoesNotContain(report.Issues, i => i.Check == "declared_reference_unregistered");
        }

        private static IEnumerable<string> MessagesOf(SchemaAuditReport report)
        {
            foreach (var issue in report.Issues)
            {
                yield return $"[{issue.Severity}] {issue.Table}/{issue.FieldPath}: {issue.Check}: {issue.Message}";
            }
        }

        // -----------------------------------------------------------------
        // 分阶段落地计划 T-N0-1：field_curve_shape（04 第 3.6 节"曲线形态登记"）
        // -----------------------------------------------------------------

        [Fact]
        public void CurveShape_BreakpointsFactory_PassesAudit()
        {
            var table = SingleFieldTable("test.curve_ok",
                CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true, description: "等级曲线"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Empty(report.Issues);
        }

        [Fact]
        public void CurveShape_SaturationFactory_PassesAudit()
        {
            var table = SingleFieldTable("test.curve_sat_ok",
                CurveSchema.SaturationField("curve", required: true, description: "饱和曲线"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Empty(report.Issues);
        }

        [Fact]
        public void CurveShape_BreakpointsOnScalarField_ReportsError()
        {
            var table = SingleFieldTable("test.curve_scalar",
                new FieldSchema("value", FieldKind.Number, required: true, description: "数值")
                    .WithCurve(CurveSchema.Breakpoints(CurveAxis.Level)));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "field_curve_shape" && i.FieldPath == "value");
        }

        [Fact]
        public void CurveShape_BreakpointsArrayWithoutXY_ReportsError()
        {
            var table = SingleFieldTable("test.curve_bad_item",
                new FieldSchema("entries", FieldKind.Array, required: true, description: "曲线",
                    item: new FieldSchema("<e>", FieldKind.Object, required: true, description: "元素", fields: new[]
                    {
                        new FieldSchema("level", FieldKind.Int, required: true, description: "等级"),
                        new FieldSchema("y", FieldKind.Number, required: true, description: "值"),
                    }))
                    .WithCurve(CurveSchema.Breakpoints(CurveAxis.Level)));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "field_curve_shape" && i.FieldPath == "entries" && i.Message.Contains("\"x\""));
        }

        [Fact]
        public void CurveShape_SaturationWithOptionalK_ReportsError()
        {
            var table = SingleFieldTable("test.curve_sat_bad",
                new FieldSchema("curve", FieldKind.Object, required: true, description: "饱和", fields: new[]
                {
                    new FieldSchema("k", FieldKind.Number, required: false, description: "系数"),
                }).WithCurve(CurveSchema.Saturation()));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.Contains(report.Issues, i =>
                i.Severity == "error" && i.Check == "field_curve_shape" && i.FieldPath == "curve" && i.Message.Contains("必填"));
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 46 条（04 第 3.4 节勘误"字段废弃元数据"）：field_deprecated_metadata 自洽检查。
        // 判断记录：本检查项的两个命中条件（IsDeprecated 为真但 DeprecatedSince 为空；ReplacedBy 非空
        // 但在同表/同级找不到）在实践中都已经被 FieldSchema.WithDeprecated 的必填校验与
        // FieldSchema/TableSchema 构造函数装配期的 ReplacedBy 存在性校验堵死——同 FieldGroup_
        // DefaultsByKindAndName_NoIssue 一样，走公开 API 构造不出能触发本检查的非法状态，故本节只有
        // "合法登记不误报"的回归测试，没有"命中"的正例测试（同 field_group 既有测试风格）。
        // -----------------------------------------------------------------

        [Fact]
        public void DeprecatedMetadata_ValidTopLevelUsage_NoIssue()
        {
            var table = new TableSchema("test.deprecated_top_level", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                new FieldSchema("old_field", FieldKind.Number, required: false, description: "旧字段")
                    .WithDeprecated("1.31.0", "new_field"),
                new FieldSchema("new_field", FieldKind.Number, required: false, description: "新字段"),
            }).WithOwnership(SchemaLayer.Foundation, "test");

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "field_deprecated_metadata");
            Assert.True(table.GetField("old_field")!.IsDeprecated);
        }

        [Fact]
        public void DeprecatedMetadata_ValidNoReplacementUsage_NoIssue()
        {
            var table = SingleFieldTable("test.deprecated_no_replacement",
                new FieldSchema("old_flag", FieldKind.Bool, required: false, description: "旧开关")
                    .WithDeprecated("1.31.0", null, note: "无替代——功能始终启用"));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "field_deprecated_metadata");
        }

        [Fact]
        public void DeprecatedMetadata_ValidNestedUsage_NoIssue()
        {
            var table = SingleFieldTable("test.deprecated_nested",
                new FieldSchema("container", FieldKind.Object, required: false, description: "容器", fields: new[]
                {
                    new FieldSchema("old_child", FieldKind.Number, required: false, description: "旧子字段")
                        .WithDeprecated("1.33.0", "new_child"),
                    new FieldSchema("new_child", FieldKind.Number, required: false, description: "新子字段"),
                }));

            var report = SchemaAudit.Run(new[] { table }, SchemaAuditAllowlist.Empty);

            Assert.DoesNotContain(report.Issues, i => i.Check == "field_deprecated_metadata");
        }

        /// <summary>
        /// 消费方反馈第 46 条："打标清单与附录 C 一致"交叉测试：解析
        /// <c>docs/升级指南/1.29.0到1.37.0-数值设计专项.md</c> 附录 C"废弃与替代 API 总表"里第一列
        /// （"旧 API / 旧字段"）用反引号写出的数据字段路径，逐一断言对应的 <see cref="FieldSchema.IsDeprecated"/>
        /// 已在真实登记（<see cref="SchemaAudit.EnumerateRegisteredSchemas"/>）里为 <c>true</c>——这样
        /// 附录 C 今后新增一行数据字段（表.字段全小写点分记法）却忘了同步补
        /// <see cref="FieldSchema.WithDeprecated"/> 时，本测试会失败，而不是像本条反馈原始案例那样
        /// 只能靠人工核对才发现漂移。
        /// <para>
        /// 判断记录（只挑"看起来是数据字段路径"的行，跳过纯 C# API 签名行）：附录 C 同一张表里混了
        /// 两类"旧 API / 旧字段"——<c>StatHostOptions.EnableRatingConversion</c>/
        /// <c>IWeaponDamageQuery.GetWeaponBaseDamage</c> 一类是 C# 方法/属性签名（首段以大写字母开头，
        /// 不受本次反馈"字段废弃元数据"覆盖，见反馈原文范围仅限 <c>FieldSchema</c>），
        /// <c>stat.definition.group</c> 一类才是本反馈要打标的数据字段路径（全小写点分）——用
        /// "首反引号 token 是否整体匹配 <c>^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$</c>"这一条规则区分两者，
        /// 不需要逐行硬编码判断。
        /// </para>
        /// <para>
        /// 判断记录（<c>rewards.xp</c> 一行的特殊形状）：这一行的锚点 <c>rewards.xp</c> 本身不带表名
        /// 前缀（真正的表名——<c>quest.def</c>/<c>encounter.def</c>/<c>achv.def</c>——写在括注里，
        /// 三张表共用同一份 <c>QuestSchemas.RewardsFields</c>，见该判断记录）。本测试不为这一种形状
        /// 单独硬编码表名，而是把同一格锚点之后其它同样满足"全小写点分"格式的反引号 token 都当作候选
        /// 表名，用 <see cref="SchemaAudit.EnumerateRegisteredSchemas"/> 的真实表名集合筛出确实存在的
        /// 那些——能通过这条筛选的只有真表名，不会误把 <c>rewards.xp</c> 自己或纯字段名token 当成表。
        /// </para>
        /// </summary>
        [Fact]
        public void AppendixC_DeprecatedDataFieldRows_MatchFieldSchemaIsDeprecated()
        {
            var repoRoot = FindRepoRoot();
            var docPath = Path.Combine(repoRoot, "docs", "升级指南", "1.29.0到1.37.0-数值设计专项.md");
            Assert.True(File.Exists(docPath), $"找不到升级指南文档：{docPath}");
            var text = File.ReadAllText(docPath);

            const string startMarker = "### 附录 C：废弃与替代 API 总表";
            const string endMarker = "### 附录 D：";
            var startIndex = text.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.True(startIndex >= 0, "找不到附录 C 小节标题，文档结构可能已变化");
            var endIndex = text.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
            Assert.True(endIndex > startIndex, "找不到附录 D 小节标题，无法界定附录 C 范围");
            var section = text.Substring(startIndex, endIndex - startIndex);

            var schemas = SchemaAudit.EnumerateRegisteredSchemas();
            var schemasByName = schemas.ToDictionary(s => s.Name, StringComparer.Ordinal);
            var registeredTableNames = new HashSet<string>(schemasByName.Keys, StringComparer.Ordinal);

            var dottedTokenRegex = new Regex(@"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$", RegexOptions.CultureInvariant);
            var bareTokenRegex = new Regex(@"^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);
            var backtickTokenRegex = new Regex(@"`([^`]+)`", RegexOptions.CultureInvariant);

            var directChecks = new List<(string table, string field, string rawRow)>();
            var nestedChecks = new List<(string table, string topField, string subField, string rawRow)>();

            var dataRowCount = 0;
            foreach (var line in section.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("|", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("|---", StringComparison.Ordinal)) continue; // 分隔行
                if (trimmed.Contains("旧 API / 旧字段")) continue; // 表头行

                var cells = trimmed.Trim('|').Split('|');
                if (cells.Length == 0) continue;
                var firstCell = cells[0];

                var tokenMatches = backtickTokenRegex.Matches(firstCell);
                if (tokenMatches.Count == 0) continue;

                var tokens = tokenMatches.Select(m => m.Groups[1].Value).ToList();
                var anchor = tokens[0];
                if (!dottedTokenRegex.IsMatch(anchor)) continue; // C# API 签名行，不是数据字段路径

                dataRowCount++;
                var anchorSegments = anchor.Split('.');

                if (anchorSegments.Length >= 3)
                {
                    // "domain.table.field" 形状：前两段是表名，其余段是字段名（本文档目前没有比这更深
                    // 的字段嵌套路径出现在附录 C 里）。同格其它裸字段名 token（无点）是同一张表的
                    // 兄弟字段。
                    var table = anchorSegments[0] + "." + anchorSegments[1];
                    var field = string.Join(".", anchorSegments.Skip(2));
                    directChecks.Add((table, field, trimmed));

                    foreach (var bare in tokens.Skip(1).Where(t => bareTokenRegex.IsMatch(t)))
                    {
                        directChecks.Add((table, bare, trimmed));
                    }
                }
                else
                {
                    // 两段锚点（如 rewards.xp）：真正的表名写在同一格的其它反引号 token 里，用"是否为
                    // 已注册的真实表名"筛出来，而不是硬编码。
                    var topField = anchorSegments[0];
                    var subField = anchorSegments[1];
                    var actualTables = tokens.Skip(1).Where(t => dottedTokenRegex.IsMatch(t) && registeredTableNames.Contains(t)).ToList();
                    Assert.True(actualTables.Count > 0, $"附录 C 行 \"{trimmed}\" 的锚点 \"{anchor}\" 不含表名前缀，且同格找不到任何已注册的表名 token 作为回退");

                    foreach (var table in actualTables)
                    {
                        nestedChecks.Add((table, topField, subField, trimmed));
                    }
                }
            }

            // 判断记录：先断言"确实解析出了预期数量的可核对数据字段行"，防止将来 dottedTokenRegex/
            // backtickTokenRegex 的书写细节变化导致本测试"什么都没解析到、自然全部通过"这种假阳性
            // （消费方反馈第 46 条设计阶段明文列出的行数：stat.definition/item.affix/prog.xp_source/
            // rewards.xp 共 4 行）。
            Assert.Equal(4, dataRowCount);
            Assert.NotEmpty(directChecks);
            Assert.NotEmpty(nestedChecks);

            foreach (var (table, field, rawRow) in directChecks)
            {
                Assert.True(schemasByName.TryGetValue(table, out var schema), $"附录 C 行 \"{rawRow}\" 引用的表 \"{table}\" 未在真实登记里找到");
                var fieldSchema = schema!.GetField(field);
                Assert.True(fieldSchema != null, $"附录 C 行 \"{rawRow}\" 引用的字段 \"{table}.{field}\" 未在真实登记里找到");
                Assert.True(fieldSchema!.IsDeprecated, $"附录 C 行 \"{rawRow}\" 标记为废弃，但 \"{table}.{field}\" 的 FieldSchema.IsDeprecated 仍为 false");
            }

            foreach (var (table, topField, subField, rawRow) in nestedChecks)
            {
                Assert.True(schemasByName.TryGetValue(table, out var schema), $"附录 C 行 \"{rawRow}\" 引用的表 \"{table}\" 未在真实登记里找到");
                var topFieldSchema = schema!.GetField(topField);
                Assert.True(topFieldSchema?.Fields != null, $"附录 C 行 \"{rawRow}\" 引用的字段 \"{table}.{topField}\" 未登记 Fields 子结构");
                var subFieldSchema = topFieldSchema!.Fields!.SingleOrDefault(f => f.Name == subField);
                Assert.True(subFieldSchema != null, $"附录 C 行 \"{rawRow}\" 引用的嵌套字段 \"{table}.{topField}.{subField}\" 未在真实登记里找到");
                Assert.True(subFieldSchema!.IsDeprecated, $"附录 C 行 \"{rawRow}\" 标记为废弃，但 \"{table}.{topField}.{subField}\" 的 FieldSchema.IsDeprecated 仍为 false");
            }
        }
    }
}

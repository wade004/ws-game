using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>门禁本体：真实登记 + 仓库根白名单，断言 0 error（白名单未用条目仍允许——本测试
        /// 只关心不阻断，"白名单条目全部命中"由 <c>check.ps1</c> 门禁步骤的真实输出与本测试共同
        /// 覆盖，不在此重复断言，避免白名单每次调整都要同步改测试）。消费方反馈第 30 条：一并断言
        /// 新增的 <c>id_description_reference_hint</c> 告警级检查在当前全量登记上是 0 命中——该检查
        /// 是防遗漏用的门禁，不是"欢迎警告常驻"，命中即应立即补登记或改描述（见该检查项判断记录），
        /// 不像有些告警级检查允许长期非零（本项没有这类既有先例）。</summary>
        [Fact]
        public void RealRegisteredSchemas_WithRepoAllowlist_ZeroErrors()
        {
            var schemas = SchemaAudit.EnumerateRegisteredSchemas();

            var repoRoot = FindRepoRoot();
            var allowlistPath = Path.Combine(repoRoot, "toolchain", "schema_audit_allowlist.json");
            Assert.True(File.Exists(allowlistPath), $"找不到白名单文件：{allowlistPath}");
            var allowlist = SchemaAuditAllowlist.Parse(File.ReadAllText(allowlistPath));

            var report = SchemaAudit.Run(schemas, allowlist);

            Assert.Equal(0, report.ErrorCount);
            Assert.False(report.IsBlocking, string.Join("\n", MessagesOf(report)));
            Assert.DoesNotContain(report.Issues, i => i.Check == "id_description_reference_hint");
        }

        private static IEnumerable<string> MessagesOf(SchemaAuditReport report)
        {
            foreach (var issue in report.Issues)
            {
                yield return $"[{issue.Severity}] {issue.Table}/{issue.FieldPath}: {issue.Check}: {issue.Message}";
            }
        }
    }
}

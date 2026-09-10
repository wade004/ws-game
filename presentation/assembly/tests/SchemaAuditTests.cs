using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private static TableSchema SingleFieldTable(string tableName, FieldSchema field) =>
            new TableSchema(tableName, "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
                field,
            });

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

        /// <summary>门禁本体：真实登记 + 仓库根白名单，断言 0 error（白名单未用条目仍允许——本测试
        /// 只关心不阻断，"白名单条目全部命中"由 <c>check.ps1</c> 门禁步骤的真实输出与本测试共同
        /// 覆盖，不在此重复断言，避免白名单每次调整都要同步改测试）。</summary>
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

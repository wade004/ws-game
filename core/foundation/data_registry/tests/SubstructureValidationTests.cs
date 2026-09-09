using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// ADR-0019 / F1a：<see cref="FieldSchema.Fields"/>/<see cref="FieldSchema.Item"/>/
    /// <see cref="FieldSchema.Variants"/> 子结构登记 + <c>DataRegistry</c> 递归校验（见 04 第 3.2
    /// 节、<c>architecture/adr/0019-复合字段子结构登记为机器可读schema.md</c>）。覆盖范围：
    /// Object.Fields 递归必填/类型、Array.Item 标量与对象、Variants 判别字段缺失/非法/命中后子
    /// 字段校验、子层 Reference/Expr 与顶层共用同一套检查名、自引用递归（正常与深度超限）、未登记
    /// 子结构行为不变（向后兼容）、<c>unknown_subfield</c> 默认不报/开启后报警告、路径字符串格式。
    /// </summary>
    public sealed class SubstructureValidationTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, int schemaVersion, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": " + schemaVersion + ", \"rows\": " + rowsJson + "}";

        // -----------------------------------------------------------------
        // Object.Fields：递归必填/类型
        // -----------------------------------------------------------------

        private static TableSchema NestedObjectSchema() => new TableSchema(
            "test.nested_object", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("info", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("name", FieldKind.String, required: true),
                    new FieldSchema("age", FieldKind.Int, required: false),
                }),
            });

        [Fact]
        public void ObjectFields_MissingRequiredSubfield_ReportsRequiredFieldWithNestedPath()
        {
            var rows = "[{\"id\": \"test.a\", \"info\": {\"age\": 3}}]";
            var source = new InMemoryDataSource().Add("test.nested_object", Envelope("test.nested_object", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(NestedObjectSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "info.name");
        }

        [Fact]
        public void ObjectFields_WrongSubfieldType_ReportsFieldTypeWithNestedPath()
        {
            var rows = "[{\"id\": \"test.a\", \"info\": {\"name\": 123}}]";
            var source = new InMemoryDataSource().Add("test.nested_object", Envelope("test.nested_object", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(NestedObjectSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "info.name");
        }

        [Fact]
        public void ObjectFields_WellFormed_Passes()
        {
            var rows = "[{\"id\": \"test.a\", \"info\": {\"name\": \"n\", \"age\": 3}}]";
            var source = new InMemoryDataSource().Add("test.nested_object", Envelope("test.nested_object", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(NestedObjectSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        /// <summary>未登记子结构（<c>info</c> 不设 <c>fields</c>）时行为与登记前完全一致：只检查
        /// "存在且是对象"，内部随便放什么都不报错（向后兼容）。</summary>
        [Fact]
        public void ObjectField_NoFieldsRegistered_OnlyChecksIsObject_BackwardCompatible()
        {
            var schema = new TableSchema("test.plain_object", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("info", FieldKind.Object, required: false),
            });

            var rows = "[{\"id\": \"test.a\", \"info\": {\"whatever\": 1, \"nested\": {\"x\": true}}}]";
            var source = new InMemoryDataSource().Add("test.plain_object", Envelope("test.plain_object", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // Array.Item：标量与对象
        // -----------------------------------------------------------------

        private static TableSchema ArrayItemScalarSchema() => new TableSchema(
            "test.arr_scalar", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("tags", FieldKind.Array, required: false,
                    item: new FieldSchema("<tag>", FieldKind.Id, required: true)),
            });

        [Fact]
        public void ArrayItem_ScalarElementBadFormat_ReportsFieldTypeWithIndexedPath()
        {
            var rows = "[{\"id\": \"test.a\", \"tags\": [\"tag.ok\", \"NOT VALID\"]}]";
            var source = new InMemoryDataSource().Add("test.arr_scalar", Envelope("test.arr_scalar", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(ArrayItemScalarSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "tags[1]");
        }

        [Fact]
        public void ArrayItem_AllScalarElementsValid_Passes()
        {
            var rows = "[{\"id\": \"test.a\", \"tags\": [\"tag.a\", \"tag.b\"]}]";
            var source = new InMemoryDataSource().Add("test.arr_scalar", Envelope("test.arr_scalar", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(ArrayItemScalarSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        private static TableSchema ArrayItemObjectSchema() => new TableSchema(
            "test.arr_object", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("entries", FieldKind.Array, required: false,
                    item: new FieldSchema("<entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("k", FieldKind.String, required: true),
                    })),
            });

        [Fact]
        public void ArrayItem_ObjectElementMissingRequiredSubfield_ReportsRequiredFieldWithIndexedNestedPath()
        {
            var rows = "[{\"id\": \"test.a\", \"entries\": [{\"k\": \"x\"}, {}]}]";
            var source = new InMemoryDataSource().Add("test.arr_object", Envelope("test.arr_object", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(ArrayItemObjectSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[1].k");
        }

        /// <summary>未登记子结构（<c>tags</c> 不设 <c>item</c>）时行为与登记前完全一致：只检查
        /// "存在且是数组"（向后兼容）。</summary>
        [Fact]
        public void ArrayField_NoItemRegistered_OnlyChecksIsArray_BackwardCompatible()
        {
            var schema = new TableSchema("test.plain_array", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("tags", FieldKind.Array, required: false),
            });

            var rows = "[{\"id\": \"test.a\", \"tags\": [1, \"x\", {\"y\": true}]}]";
            var source = new InMemoryDataSource().Add("test.plain_array", Envelope("test.plain_array", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // Variants：判别字段缺失/非法/命中后子字段校验（含 CommonFields）
        // -----------------------------------------------------------------

        private static TableSchema VariantSchemaTable() => new TableSchema(
            "test.variant_tbl", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("shape", FieldKind.Object, required: true,
                    variants: new VariantSchema(
                        "kind",
                        new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
                        {
                            ["circle"] = new[] { new FieldSchema("radius", FieldKind.Number, required: true) },
                            ["rect"] = new[]
                            {
                                new FieldSchema("w", FieldKind.Number, required: true),
                                new FieldSchema("h", FieldKind.Number, required: true),
                            },
                        },
                        commonFields: new[] { new FieldSchema("color", FieldKind.String, required: false) })),
            });

        [Fact]
        public void Variants_MissingDiscriminator_ReportsVariantDiscriminator()
        {
            var rows = "[{\"id\": \"test.a\", \"shape\": {\"radius\": 1}}]";
            var source = new InMemoryDataSource().Add("test.variant_tbl", Envelope("test.variant_tbl", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(VariantSchemaTable());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "shape.kind");
        }

        [Fact]
        public void Variants_DiscriminatorValueNotInCases_ReportsVariantDiscriminatorListingLegalValues()
        {
            var rows = "[{\"id\": \"test.a\", \"shape\": {\"kind\": \"triangle\", \"radius\": 1}}]";
            var source = new InMemoryDataSource().Add("test.variant_tbl", Envelope("test.variant_tbl", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(VariantSchemaTable());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "variant_discriminator");
            Assert.Contains("circle", issue.Message);
            Assert.Contains("rect", issue.Message);
        }

        [Fact]
        public void Variants_DiscriminatorNotString_ReportsVariantDiscriminator()
        {
            var rows = "[{\"id\": \"test.a\", \"shape\": {\"kind\": 1}}]";
            var source = new InMemoryDataSource().Add("test.variant_tbl", Envelope("test.variant_tbl", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(VariantSchemaTable());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator");
        }

        [Fact]
        public void Variants_HitCase_MissingCaseRequiredField_ReportsRequiredField()
        {
            var rows = "[{\"id\": \"test.a\", \"shape\": {\"kind\": \"rect\", \"w\": 2}}]"; // 漏填 h
            var source = new InMemoryDataSource().Add("test.variant_tbl", Envelope("test.variant_tbl", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(VariantSchemaTable());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "shape.h");
        }

        [Fact]
        public void Variants_HitCase_WithCommonFieldsAndAllRequired_Passes()
        {
            var rows = "[{\"id\": \"test.a\", \"shape\": {\"kind\": \"circle\", \"radius\": 3, \"color\": \"red\"}}]";
            var source = new InMemoryDataSource().Add("test.variant_tbl", Envelope("test.variant_tbl", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(VariantSchemaTable());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // 子层 Reference 完整性 / Expr 可解析（与顶层共用同一套实现，检查名不变）
        // -----------------------------------------------------------------

        [Fact]
        public void NestedReference_TargetMissing_ReportsReferenceIntegrityWithNestedPath()
        {
            var ownerSchema = new TableSchema("test.owner", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            });
            var holderSchema = new TableSchema("test.holder", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("link", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("owner", FieldKind.Reference, required: true, referenceTable: "test.owner"),
                }),
            });

            var source = new InMemoryDataSource()
                .Add("test.owner", Envelope("test.owner", 1, "[{\"id\": \"test.owner_real\"}]"))
                .Add("test.holder", Envelope("test.holder", 1,
                    "[{\"id\": \"test.holder_a\", \"link\": {\"owner\": \"test.owner_missing\"}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(ownerSchema);
            registry.RegisterSchema(holderSchema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "link.owner");
        }

        [Fact]
        public void NestedReference_TargetExists_Passes()
        {
            var ownerSchema = new TableSchema("test.owner", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
            });
            var holderSchema = new TableSchema("test.holder", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("link", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("owner", FieldKind.Reference, required: true, referenceTable: "test.owner"),
                }),
            });

            var source = new InMemoryDataSource()
                .Add("test.owner", Envelope("test.owner", 1, "[{\"id\": \"test.owner_real\"}]"))
                .Add("test.holder", Envelope("test.holder", 1,
                    "[{\"id\": \"test.holder_a\", \"link\": {\"owner\": \"test.owner_real\"}}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(ownerSchema);
            registry.RegisterSchema(holderSchema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void NestedExpr_Unparsable_ReportsExprParsableWithNestedPath()
        {
            var exprSchema = new ExprSchema().Register("world", "some_flag", ExprValueKind.Bool, ExprValueKind.Id);
            var schema = new TableSchema("test.holder_expr", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("rule", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("condition", FieldKind.Expr, required: true),
                }),
            });

            var source = new InMemoryDataSource().Add("test.holder_expr", Envelope("test.holder_expr", 1,
                "[{\"id\": \"test.a\", \"rule\": {\"condition\": \"(( bad\"}}]"));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { ExprSchema = exprSchema });
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Field == "rule.condition");
        }

        [Fact]
        public void NestedExpr_Parsable_Passes()
        {
            var exprSchema = new ExprSchema().Register("world", "some_flag", ExprValueKind.Bool, ExprValueKind.Id);
            var schema = new TableSchema("test.holder_expr", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("rule", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("condition", FieldKind.Expr, required: true),
                }),
            });

            var source = new InMemoryDataSource().Add("test.holder_expr", Envelope("test.holder_expr", 1,
                "[{\"id\": \"test.a\", \"rule\": {\"condition\": \"world.some_flag(item.town_key)\"}}]"));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { ExprSchema = exprSchema });
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // 自引用递归：Array → Object → Array（正常场景 + 深度超限）
        // -----------------------------------------------------------------

        /// <summary>惰性自引用：<c>node</c> 字段是 <c>Object</c>，其 <c>children</c> 子字段是
        /// <c>Array</c>，元素结构又通过 <c>itemFactory</c> 惰性指回 <c>NodeSchema</c> 自身——同一份
        /// 结构在自身内部复用，模拟 <c>projectile.params.on_hit_effects</c> 的自引用形状（见
        /// <c>SkillSchemas.EffectsItemSchema</c>）。</summary>
        private static readonly FieldSchema NodeSchema = BuildNodeSchema();

        private static FieldSchema BuildNodeSchema() => new FieldSchema(
            "<node>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("label", FieldKind.String, required: true),
                new FieldSchema("children", FieldKind.Array, required: false, itemFactory: () => NodeSchema),
            });

        private static TableSchema SelfRefTreeSchema() => new TableSchema(
            "test.self_ref_tree", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("root", FieldKind.Object, required: true, fields: new[]
                {
                    new FieldSchema("label", FieldKind.String, required: true),
                    new FieldSchema("children", FieldKind.Array, required: false, itemFactory: () => NodeSchema),
                }),
            });

        [Fact]
        public void SelfReferentialRecursion_ModerateDepth_ValidatesNormally()
        {
            // root -> children[0] -> children[0] -> children[0]（3 层，远低于上限），叶子节点缺 label。
            var rows = "[{\"id\": \"test.a\", \"root\": {\"label\": \"r\", \"children\": [" +
                       "{\"label\": \"c1\", \"children\": [{\"label\": \"c2\", \"children\": [{\"children\": []}]}]}" +
                       "]}}]";
            var source = new InMemoryDataSource().Add("test.self_ref_tree", Envelope("test.self_ref_tree", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(SelfRefTreeSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" &&
                i.Field == "root.children[0].children[0].children[0].label");
        }

        [Fact]
        public void SelfReferentialRecursion_WellFormedModerateDepth_Passes()
        {
            var rows = "[{\"id\": \"test.a\", \"root\": {\"label\": \"r\", \"children\": [" +
                       "{\"label\": \"c1\", \"children\": [{\"label\": \"c2\", \"children\": []}]}" +
                       "]}}]";
            var source = new InMemoryDataSource().Add("test.self_ref_tree", Envelope("test.self_ref_tree", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(SelfRefTreeSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void SelfReferentialRecursion_ExceedsMaxDepth_ReportsSubstructureDepthAndStops()
        {
            // 构造一条深度远超 32 的链（每层 {"label": "n", "children": [ ... ]}），逼近
            // MaxSubstructureDepth（见 DataRegistry 判断记录）。
            var jsonBuilder = new System.Text.StringBuilder();
            const int chainLength = 40;
            for (var i = 0; i < chainLength; i++)
            {
                jsonBuilder.Append("{\"label\": \"n").Append(i).Append("\", \"children\": [");
            }
            jsonBuilder.Append("{\"label\": \"leaf\", \"children\": []}");
            for (var i = 0; i < chainLength; i++)
            {
                jsonBuilder.Append("]}");
            }

            var rows = "[{\"id\": \"test.a\", \"root\": " + jsonBuilder + "}]";
            var source = new InMemoryDataSource().Add("test.self_ref_tree", Envelope("test.self_ref_tree", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(SelfRefTreeSchema());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "substructure_depth");
        }

        // -----------------------------------------------------------------
        // unknown_subfield：默认不报，开启后报警告
        // -----------------------------------------------------------------

        [Fact]
        public void UnknownSubfield_DefaultPolicy_DoesNotReport()
        {
            var rows = "[{\"id\": \"test.a\", \"info\": {\"name\": \"n\", \"nickname\": \"extra\"}}]";
            var source = new InMemoryDataSource().Add("test.nested_object", Envelope("test.nested_object", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(NestedObjectSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "unknown_subfield");
        }

        [Fact]
        public void UnknownSubfield_WarningPolicy_ReportsWarningNotBlocking()
        {
            var rows = "[{\"id\": \"test.a\", \"info\": {\"name\": \"n\", \"nickname\": \"extra\"}}]";
            var source = new InMemoryDataSource().Add("test.nested_object", Envelope("test.nested_object", 1, rows));
            var registry = new DataRegistry(source, MakeBus(),
                new DataRegistryOptions { UnknownSubfieldSeverity = UnknownSubfieldPolicy.Warning });
            registry.RegisterSchema(NestedObjectSchema());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Contains(report.Issues, i => i.Check == "unknown_subfield" &&
                i.Severity == ValidationSeverity.Warning && i.Field == "info.nickname");
        }

        // -----------------------------------------------------------------
        // 路径字符串格式（顶层字段名 / obj.sub / arr[idx] / 组合）
        // -----------------------------------------------------------------

        [Fact]
        public void PathFormat_CombinesObjectAndArrayIndexingAsExpected()
        {
            var schema = new TableSchema("test.path_format", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("groups", FieldKind.Array, required: false,
                    item: new FieldSchema("<group>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("entries", FieldKind.Array, required: false,
                            item: new FieldSchema("<entry>", FieldKind.Object, required: true, fields: new[]
                            {
                                new FieldSchema("ref", FieldKind.Id, required: true),
                            })),
                    })),
            });

            var rows = "[{\"id\": \"test.a\", \"groups\": [{\"entries\": [{}, {\"ref\": \"x.y\"}]}]}]";
            var source = new InMemoryDataSource().Add("test.path_format", Envelope("test.path_format", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(schema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "groups[0].entries[0].ref");
        }

        // -----------------------------------------------------------------
        // FieldSchema/VariantSchema 构造期非法组合（"登记自洽"）
        // -----------------------------------------------------------------

        [Fact]
        public void FieldSchema_FieldsOnNonObjectKind_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FieldSchema("x", FieldKind.String, required: false,
                fields: new[] { new FieldSchema("y", FieldKind.String, required: false) }));
        }

        [Fact]
        public void FieldSchema_ItemOnNonArrayKind_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FieldSchema("x", FieldKind.Object, required: false,
                item: new FieldSchema("y", FieldKind.String, required: false)));
        }

        [Fact]
        public void FieldSchema_VariantsOnNonObjectKind_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FieldSchema("x", FieldKind.Array, required: false,
                variants: new VariantSchema("kind", new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
                {
                    ["a"] = Array.Empty<FieldSchema>(),
                })));
        }

        [Fact]
        public void FieldSchema_FieldsAndVariantsBothSet_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FieldSchema("x", FieldKind.Object, required: false,
                fields: Array.Empty<FieldSchema>(),
                variants: new VariantSchema("kind", new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
                {
                    ["a"] = Array.Empty<FieldSchema>(),
                })));
        }

        [Fact]
        public void FieldSchema_ItemAndItemFactoryBothSet_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FieldSchema("x", FieldKind.Array, required: false,
                item: new FieldSchema("y", FieldKind.String, required: false),
                itemFactory: () => new FieldSchema("y", FieldKind.String, required: false)));
        }

        [Fact]
        public void VariantSchema_EmptyCases_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VariantSchema("kind", new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)));
        }

        [Fact]
        public void VariantSchema_EmptyDiscriminator_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new VariantSchema("", new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
                {
                    ["a"] = Array.Empty<FieldSchema>(),
                }));
        }

        /// <summary>惰性求值只求值一次并缓存：多次访问 <see cref="FieldSchema.Item"/> 返回同一个
        /// 实例（<c>itemFactory</c> 只被调用一次），呼应"首次访问时求值并缓存"。</summary>
        [Fact]
        public void FieldSchema_ItemFactory_EvaluatedOnceAndCached()
        {
            var callCount = 0;
            var target = new FieldSchema("y", FieldKind.String, required: false);
            var field = new FieldSchema("x", FieldKind.Array, required: false, itemFactory: () =>
            {
                callCount++;
                return target;
            });

            var first = field.Item;
            var second = field.Item;

            Assert.Equal(1, callCount);
            Assert.Same(target, first);
            Assert.Same(first, second);
        }

        /// <summary>既有登记（不填 fields/item/variants 三项）行为完全不变：既有构造调用方式必须
        /// 继续编译且不影响运行行为（向后兼容，见 04 第 3.2 节）。</summary>
        [Fact]
        public void FieldSchema_LegacyConstructorUsage_StillCompilesAndBehavesUnchanged()
        {
            var field = new FieldSchema("legacy", FieldKind.Object, required: true, description: "旧式登记");
            Assert.Null(field.Fields);
            Assert.Null(field.Item);
            Assert.Null(field.Variants);
        }
    }
}

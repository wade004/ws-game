using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 消费方反馈第 40 条（2026-09-13）：<see cref="SchemaMigrator"/> 三个公开静态方法的单元测试，见
    /// <c>architecture/落地计划/消费方反馈-2026-09-13-编辑器-第40条.md</c>。<see cref="DataRegistry"/>
    /// 侧"加载器接受信封级可选键 migrated_from"、演示表 <c>found.migration_sample</c> 的真实加载行为、
    /// 与"加载器结果 == MigrateEnvelope 后再加载的结果"一致性用例，见本文件末尾几个测试方法。
    /// </summary>
    public class SchemaMigratorTests
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

        private static string Envelope(string table, int schemaVersion, string rowsJson, int? migratedFrom = null) =>
            "{\"table\": \"" + table + "\", \"schema_version\": " + schemaVersion +
            (migratedFrom == null ? "" : ", \"migrated_from\": " + migratedFrom.Value) +
            ", \"rows\": " + rowsJson + "}";

        // -----------------------------------------------------------------
        // BuildChain
        // -----------------------------------------------------------------

        private static TableSchema ChainSchema(params TableMigration[] migrations) => new TableSchema(
            "test.chain", "id", currentSchemaVersion: 4, fields: Array.Empty<FieldSchema>(), migrations: migrations);

        [Fact]
        public void BuildChain_ExactlyReachesTarget_ReturnsFullChain()
        {
            MigrateDelegate step = row => row;
            var m12 = new TableMigration(1, 2, step);
            var m23 = new TableMigration(2, 3, step);
            var schema = ChainSchema(m12, m23);

            var chain = SchemaMigrator.BuildChain(schema, 1, 3);

            Assert.NotNull(chain);
            Assert.Equal(new[] { m12, m23 }, chain);
        }

        [Fact]
        public void BuildChain_MissingLink_ReturnsNull()
        {
            var schema = ChainSchema(new TableMigration(1, 2, row => row)); // 缺 2 -> 4

            var chain = SchemaMigrator.BuildChain(schema, 1, 4);

            Assert.Null(chain);
        }

        [Fact]
        public void BuildChain_StepOvershootsTarget_ReturnsNull()
        {
            // 1 -> 3 一步跨过目标版本 2：current 从 1 变成 3，while (current < toVersion) 条件
            // 不再满足退出循环，但 current(3) != toVersion(2)，判定为越过目标，返回 null。
            var schema = new TableSchema(
                "test.chain_overshoot", "id", currentSchemaVersion: 2, fields: Array.Empty<FieldSchema>(),
                migrations: new[] { new TableMigration(1, 3, row => row) });

            var chain = SchemaMigrator.BuildChain(schema, 1, 2);

            Assert.Null(chain);
        }

        [Fact]
        public void BuildChain_FromEqualsToVersion_ReturnsEmptyChain()
        {
            var schema = ChainSchema();

            var chain = SchemaMigrator.BuildChain(schema, 2, 2);

            Assert.NotNull(chain);
            Assert.Empty(chain);
        }

        [Fact]
        public void BuildChain_MultipleMigrationsWithSameFromVersion_PicksFirstDeclared()
        {
            var byFirst = new TableMigration(1, 2, row =>
            {
                var b = new JsonObjectBuilder();
                b.Add("picked", new JsonString("first"));
                return b.Build();
            });
            var bySecond = new TableMigration(1, 2, row =>
            {
                var b = new JsonObjectBuilder();
                b.Add("picked", new JsonString("second"));
                return b.Build();
            });
            var schema = ChainSchema(byFirst, bySecond);

            var chain = SchemaMigrator.BuildChain(schema, 1, 2);

            Assert.NotNull(chain);
            Assert.Single(chain);
            Assert.Same(byFirst, chain[0]);

            var migrated = SchemaMigrator.MigrateRow(chain, new JsonObjectBuilder().Build());
            Assert.Equal("first", ((JsonString)migrated["picked"]).Value);
        }

        [Fact]
        public void BuildChain_NullSchema_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SchemaMigrator.BuildChain(null!, 1, 2));
        }

        [Fact]
        public void BuildChain_FromVersionBelowOne_ThrowsArgumentOutOfRangeException()
        {
            var schema = ChainSchema();
            Assert.Throws<ArgumentOutOfRangeException>(() => SchemaMigrator.BuildChain(schema, 0, 2));
        }

        [Fact]
        public void BuildChain_ToVersionBelowFromVersion_ThrowsArgumentOutOfRangeException()
        {
            var schema = ChainSchema();
            Assert.Throws<ArgumentOutOfRangeException>(() => SchemaMigrator.BuildChain(schema, 3, 2));
        }

        // -----------------------------------------------------------------
        // MigrateRow
        // -----------------------------------------------------------------

        [Fact]
        public void MigrateRow_AppliesChainInOrder()
        {
            MigrateDelegate addA = row =>
            {
                var b = new JsonObjectBuilder();
                foreach (var kv in row) b.Add(kv.Key, kv.Value);
                b.Add("a", JsonBool.True);
                return b.Build();
            };
            MigrateDelegate addB = row =>
            {
                var b = new JsonObjectBuilder();
                foreach (var kv in row) b.Add(kv.Key, kv.Value);
                b.Add("b", JsonBool.True);
                return b.Build();
            };
            var chain = new List<TableMigration> { new TableMigration(1, 2, addA), new TableMigration(2, 3, addB) };

            var result = SchemaMigrator.MigrateRow(chain, new JsonObjectBuilder().Build());

            Assert.True(result.ContainsKey("a"));
            Assert.True(result.ContainsKey("b"));
        }

        [Fact]
        public void MigrateRow_EmptyChain_ReturnsSameRow()
        {
            var row = new JsonObjectBuilder().Add("x", new JsonNumber(1)).Build();
            var result = SchemaMigrator.MigrateRow(Array.Empty<TableMigration>(), row);
            Assert.Same(row, result);
        }

        [Fact]
        public void MigrateRow_NullChain_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SchemaMigrator.MigrateRow(null!, new JsonObjectBuilder().Build()));
        }

        [Fact]
        public void MigrateRow_NullRow_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SchemaMigrator.MigrateRow(Array.Empty<TableMigration>(), null!));
        }

        // -----------------------------------------------------------------
        // MigrateEnvelope
        // -----------------------------------------------------------------

        private static TableSchema EnvelopeSchema() => new TableSchema(
            "test.envelope_migrate", "id", currentSchemaVersion: 2,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("display_name", FieldKind.String, required: true),
            },
            migrations: new[]
            {
                new TableMigration(1, 2, row =>
                {
                    var b = new JsonObjectBuilder();
                    foreach (var kv in row)
                    {
                        b.Add(kv.Key == "label" ? "display_name" : kv.Key, kv.Value);
                    }
                    return b.Build();
                }),
            });

        [Fact]
        public void MigrateEnvelope_AlreadyCurrentVersion_ReturnsSameInstance()
        {
            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate\",\"schema_version\":2,\"rows\":[{\"id\":\"test.envelope_migrate.a\",\"display_name\":\"A\"}]}");

            var result = SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope);

            Assert.Same(envelope, result);
        }

        [Fact]
        public void MigrateEnvelope_Migrates_WritesSchemaVersionAndMigratedFrom()
        {
            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate\",\"schema_version\":1,\"rows\":[{\"id\":\"test.envelope_migrate.a\",\"label\":\"A\"}]}");

            var result = SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope);

            Assert.Equal(2, (int)((JsonNumber)result["schema_version"]).Value);
            Assert.Equal(1, (int)((JsonNumber)result["migrated_from"]).Value);
            var row = (JsonObject)((JsonArray)result["rows"])[0];
            Assert.Equal("A", ((JsonString)row["display_name"]).Value);
            Assert.False(row.ContainsKey("label"));
        }

        [Fact]
        public void MigrateEnvelope_AlreadyHasEarlierMigratedFrom_KeepsEarlierValue()
        {
            // 模拟"已经从 v1 迁移到 v2 一次"，现在信封版本落后于一个假想的 v3（用两步链验证保留
            // 更早原始版本这条规则，不因本次迁移前的 schema_version 覆盖它）。
            var threeVersionSchema = new TableSchema(
                "test.envelope_migrate_v3", "id", currentSchemaVersion: 3,
                fields: new[]
                {
                    new FieldSchema("id", FieldKind.Id, required: true),
                    new FieldSchema("display_name", FieldKind.String, required: true),
                },
                migrations: new[]
                {
                    new TableMigration(2, 3, row => row),
                });

            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate_v3\",\"schema_version\":2,\"migrated_from\":1," +
                "\"rows\":[{\"id\":\"test.envelope_migrate_v3.a\",\"display_name\":\"A\"}]}");

            var result = SchemaMigrator.MigrateEnvelope(threeVersionSchema, envelope);

            Assert.Equal(3, (int)((JsonNumber)result["schema_version"]).Value);
            // 保留更早的原始版本 1，不是本次迁移前的 2。
            Assert.Equal(1, (int)((JsonNumber)result["migrated_from"]).Value);
        }

        [Fact]
        public void MigrateEnvelope_PreservesOtherEnvelopeKeys()
        {
            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate\",\"schema_version\":1,\"extra_key\":\"kept\"," +
                "\"rows\":[{\"id\":\"test.envelope_migrate.a\",\"label\":\"A\"}]}");

            var result = SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope);

            Assert.Equal("test.envelope_migrate", ((JsonString)result["table"]).Value);
            Assert.Equal("kept", ((JsonString)result["extra_key"]).Value);
        }

        [Fact]
        public void MigrateEnvelope_DoesNotMutateInput()
        {
            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate\",\"schema_version\":1,\"rows\":[{\"id\":\"test.envelope_migrate.a\",\"label\":\"A\"}]}");
            var originalText = JsonWriter.Write(envelope);

            SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope);

            Assert.Equal(originalText, JsonWriter.Write(envelope));
            Assert.Equal(1, (int)((JsonNumber)envelope["schema_version"]).Value);
        }

        [Fact]
        public void MigrateEnvelope_NonObjectRow_ThrowsArgumentException()
        {
            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate\",\"schema_version\":1,\"rows\":[1]}");

            Assert.Throws<ArgumentException>(() => SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope));
        }

        [Fact]
        public void MigrateEnvelope_MissingMigrationLink_ThrowsInvalidOperationException()
        {
            var schema = new TableSchema(
                "test.envelope_missing_link", "id", currentSchemaVersion: 3,
                fields: new[] { new FieldSchema("id", FieldKind.Id, required: true) },
                migrations: new[] { new TableMigration(1, 2, row => row) }); // 缺 2 -> 3

            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_missing_link\",\"schema_version\":1,\"rows\":[{\"id\":\"test.envelope_missing_link.a\"}]}");

            Assert.Throws<InvalidOperationException>(() => SchemaMigrator.MigrateEnvelope(schema, envelope));
        }

        [Fact]
        public void MigrateEnvelope_SchemaVersionTooHigh_ThrowsArgumentException()
        {
            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate\",\"schema_version\":99,\"rows\":[]}");

            Assert.Throws<ArgumentException>(() => SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope));
        }

        [Fact]
        public void MigrateEnvelope_MissingSchemaVersion_ThrowsArgumentException()
        {
            var envelope = (JsonObject)JsonReader.Parse("{\"table\":\"test.envelope_migrate\",\"rows\":[]}");
            Assert.Throws<ArgumentException>(() => SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope));
        }

        [Fact]
        public void MigrateEnvelope_RowsNotArray_ThrowsArgumentException()
        {
            var envelope = (JsonObject)JsonReader.Parse(
                "{\"table\":\"test.envelope_migrate\",\"schema_version\":1,\"rows\":{}}");
            Assert.Throws<ArgumentException>(() => SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), envelope));
        }

        [Fact]
        public void MigrateEnvelope_NullArgs_ThrowArgumentNullException()
        {
            var envelope = (JsonObject)JsonReader.Parse("{\"table\":\"x\",\"schema_version\":1,\"rows\":[]}");
            Assert.Throws<ArgumentNullException>(() => SchemaMigrator.MigrateEnvelope(null!, envelope));
            Assert.Throws<ArgumentNullException>(() => SchemaMigrator.MigrateEnvelope(EnvelopeSchema(), null!));
        }

        // -----------------------------------------------------------------
        // DataRegistry 加载器对信封级 migrated_from 的接受/拒绝（消费方反馈第 40 条）
        // -----------------------------------------------------------------

        private static TableSchema MigratedFromCheckSchema() => new TableSchema(
            "test.migrated_from_check", "id", currentSchemaVersion: 2,
            fields: new[] { new FieldSchema("id", FieldKind.Id, required: true) },
            migrations: new[] { new TableMigration(1, 2, row => row) });

        [Fact]
        public void LoadAll_LegalMigratedFrom_Accepted()
        {
            var rows = "[{\"id\": \"test.migrated_from_check.a\"}]";
            var source = new InMemoryDataSource().Add(
                "test.migrated_from_check", Envelope("test.migrated_from_check", 2, rows, migratedFrom: 1));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(MigratedFromCheckSchema());

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
        }

        [Fact]
        public void LoadAll_MigratedFromEqualsSchemaVersion_ReportsEnvelopeError()
        {
            var rows = "[{\"id\": \"test.migrated_from_check.a\"}]";
            // migrated_from 须严格小于 schema_version（合法区间 [1, schema_version - 1]），
            // 2 == schema_version 非法。
            var source = new InMemoryDataSource().Add(
                "test.migrated_from_check", Envelope("test.migrated_from_check", 2, rows, migratedFrom: 2));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(MigratedFromCheckSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "envelope" && i.Severity == ValidationSeverity.Error
                && i.Message.Contains("migrated_from"));
        }

        [Fact]
        public void LoadAll_MigratedFromNotANumber_ReportsEnvelopeError()
        {
            var rows = "[{\"id\": \"test.migrated_from_check.a\"}]";
            var envelope = "{\"table\": \"test.migrated_from_check\", \"schema_version\": 2, " +
                "\"migrated_from\": \"nope\", \"rows\": " + rows + "}";
            var source = new InMemoryDataSource().Add("test.migrated_from_check", envelope);
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(MigratedFromCheckSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "envelope" && i.Severity == ValidationSeverity.Error
                && i.Message.Contains("migrated_from"));
        }

        [Fact]
        public void LoadAll_MigratedFromZero_ReportsEnvelopeError()
        {
            var rows = "[{\"id\": \"test.migrated_from_check.a\"}]";
            var source = new InMemoryDataSource().Add(
                "test.migrated_from_check", Envelope("test.migrated_from_check", 2, rows, migratedFrom: 0));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(MigratedFromCheckSchema());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "envelope" && i.Severity == ValidationSeverity.Error
                && i.Message.Contains("migrated_from"));
        }

        // -----------------------------------------------------------------
        // 演示表 found.migration_sample：v1 示例数据经 DataRegistry 真实加载后字段为 display_name
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_MigrationSampleV1Row_LoadsAsDisplayNameField()
        {
            var rows = "[{\"id\": \"found.migration_sample.alpha\", \"label\": \"阿尔法\"}]";
            var source = new InMemoryDataSource().Add(
                "found.migration_sample", Envelope("found.migration_sample", 1, rows));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(BuiltinSchemas.MigrationSample);

            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);
            var record = registry.Get("found.migration_sample", "found.migration_sample.alpha");
            Assert.NotNull(record);
            Assert.Equal("阿尔法", record!.GetString("display_name"));
            Assert.False(record.Has("label"));
        }

        // -----------------------------------------------------------------
        // 一致性：加载器内部自动迁移的结果 == SchemaMigrator.MigrateEnvelope 后再按 v2 加载的结果
        // -----------------------------------------------------------------

        [Fact]
        public void MigrateEnvelope_ThenLoad_MatchesDirectLoaderMigration()
        {
            const string v1RowsJson =
                "[{\"id\": \"found.migration_sample.alpha\", \"label\": \"阿尔法\", \"note\": \"备注\"}," +
                "{\"id\": \"found.migration_sample.beta\", \"label\": \"贝塔\"}]";

            // 路径 A：直接把 v1 信封交给 DataRegistry，加载期内部自动迁移。
            var directSource = new InMemoryDataSource().Add(
                "found.migration_sample", Envelope("found.migration_sample", 1, v1RowsJson));
            var directRegistry = new DataRegistry(directSource, MakeBus());
            directRegistry.RegisterSchema(BuiltinSchemas.MigrationSample);
            var directReport = directRegistry.LoadAll();
            Assert.Equal(0, directReport.ErrorCount);

            // 路径 B：先用 SchemaMigrator.MigrateEnvelope 把 v1 信封迁移到 v2（内容工具写回场景），
            // 再把迁移后的信封文本交给 DataRegistry 按 v2 加载（此时 schemaVersion == CurrentSchemaVersion，
            // 加载期不再触发任何迁移）。
            var v1Envelope = (JsonObject)JsonReader.Parse(Envelope("found.migration_sample", 1, v1RowsJson));
            var migratedEnvelope = SchemaMigrator.MigrateEnvelope(BuiltinSchemas.MigrationSample, v1Envelope);
            var migratedText = JsonWriter.Write(migratedEnvelope);
            Assert.Equal(2, (int)((JsonNumber)migratedEnvelope["schema_version"]).Value);

            var indirectSource = new InMemoryDataSource().Add("found.migration_sample", migratedText);
            var indirectRegistry = new DataRegistry(indirectSource, MakeBus());
            indirectRegistry.RegisterSchema(BuiltinSchemas.MigrationSample);
            var indirectReport = indirectRegistry.LoadAll();
            Assert.Equal(0, indirectReport.ErrorCount);

            foreach (var id in new[] { "found.migration_sample.alpha", "found.migration_sample.beta" })
            {
                var direct = directRegistry.Get("found.migration_sample", id);
                var indirect = indirectRegistry.Get("found.migration_sample", id);
                Assert.NotNull(direct);
                Assert.NotNull(indirect);
                Assert.Equal(direct!.GetString("display_name"), indirect!.GetString("display_name"));
                Assert.Equal(direct.Has("note"), indirect.Has("note"));
                if (direct.Has("note"))
                {
                    Assert.Equal(direct.GetString("note"), indirect.GetString("note"));
                }
            }
        }
    }
}

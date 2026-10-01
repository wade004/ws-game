using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Faction;
using Xunit;

namespace Tests.Numbers.Faction
{
    /// <summary>
    /// 阵营矩阵补测（T-M6，2026-10-01 测试覆盖第四批）：<c>fac.*</c> 两张表的 schema 覆盖
    /// （惯例同 <c>StatSchemaCoverageTests</c>）、<c>Factions</c> 顺序、<c>ParseReaction</c> 非法值、
    /// 重复显式行后者覆盖前者、数据 Reload 之后矩阵的快照语义。
    /// </summary>
    public sealed class FactionSchemaAndReloadTests
    {
        private static readonly Id A = new Id("fac.sample_a");
        private static readonly Id B = new Id("fac.sample_b");
        private static readonly Id C = new Id("fac.sample_c");

        private static IEventBus NewBus() => new EventBus(
            EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(FactionEventKeys.RelationChanged, "faction",
                    new[] { "from", "to", "oldReaction", "newReaction" }),
            }),
            new EventBusOptions { StrictCatalog = false });

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string FactionRow(string id, string defaultReaction) =>
            "{\"id\": \"" + id + "\", \"name_key\": \"l10n." + id + ".name\", \"default_reaction\": \"" + defaultReaction + "\"}";

        private static string MatrixRow(string id, string from, string to, string reaction) =>
            "{\"id\": \"" + id + "\", \"from\": \"" + from + "\", \"to\": \"" + to + "\", \"reaction\": \"" + reaction + "\"}";

        private static (DataRegistry registry, ValidationReport report) Load(string factionRows, string matrixRows)
        {
            var source = new InMemoryDataSource()
                .Add("fac.faction", Envelope("fac.faction", factionRows))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix", matrixRows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);
            return (registry, registry.LoadAll());
        }

        private const string ThreeFactions =
            "[{\"id\": \"fac.sample_a\", \"name_key\": \"l10n.a\", \"default_reaction\": \"hostile\"}," +
            " {\"id\": \"fac.sample_b\", \"name_key\": \"l10n.b\", \"default_reaction\": \"neutral\"}," +
            " {\"id\": \"fac.sample_c\", \"name_key\": \"l10n.c\", \"default_reaction\": \"friendly\"}]";

        // -----------------------------------------------------------------
        // fac.* schema 覆盖
        // -----------------------------------------------------------------

        [Fact]
        public void Schemas_WellFormedRows_LoadWithoutIssues()
        {
            var (_, report) = Load(ThreeFactions, "[" + MatrixRow("fac.reaction.a_b", "fac.sample_a", "fac.sample_b", "friendly") + "]");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Equal(0, report.ErrorCount);
        }

        [Fact]
        public void Schemas_ExposeTheDocumentedFieldSetsAndOwnership()
        {
            Assert.Equal("fac.faction", FacSchemas.Faction.Name);
            Assert.Equal("fac.reaction_matrix", FacSchemas.ReactionMatrix.Name);
            Assert.Equal("id", FacSchemas.Faction.PrimaryKey);
            Assert.Equal("id", FacSchemas.ReactionMatrix.PrimaryKey);

            var factionFields = new List<string>();
            foreach (var f in FacSchemas.Faction.Fields) factionFields.Add(f.Name);
            var matrixFields = new List<string>();
            foreach (var f in FacSchemas.ReactionMatrix.Fields) matrixFields.Add(f.Name);

            Assert.Equal(new[] { "id", "name_key", "default_reaction" }, factionFields);
            Assert.Equal(new[] { "id", "from", "to", "reaction" }, matrixFields);
        }

        [Fact]
        public void Faction_UnknownDefaultReaction_IsRejectedByEnumCheck()
        {
            var (_, report) = Load(
                "[" + FactionRow("fac.sample_a", "aggressive") + "]", "[]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Table == "fac.faction" && i.Field == "default_reaction");
        }

        [Fact]
        public void Faction_MissingRequiredFields_AreReported()
        {
            var (_, report) = Load("[{\"id\": \"fac.sample_a\"}]", "[]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Table == "fac.faction" && i.Field == "name_key");
            Assert.Contains(report.Issues, i => i.Table == "fac.faction" && i.Field == "default_reaction");
        }

        [Fact]
        public void Matrix_UnknownReactionValue_IsRejectedByEnumCheck()
        {
            var (_, report) = Load(
                ThreeFactions, "[" + MatrixRow("fac.reaction.a_b", "fac.sample_a", "fac.sample_b", "allied") + "]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Table == "fac.reaction_matrix" && i.Field == "reaction");
        }

        [Fact]
        public void Matrix_FromOrToReferencingUnknownFaction_IsReportedAsBrokenReference()
        {
            var (_, report) = Load(
                ThreeFactions,
                "[" + MatrixRow("fac.reaction.x_a", "fac.ghost", "fac.sample_a", "hostile") + "," +
                MatrixRow("fac.reaction.a_x", "fac.sample_a", "fac.ghost", "hostile") + "]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Table == "fac.reaction_matrix" && i.Field == "from");
            Assert.Contains(report.Issues, i => i.Table == "fac.reaction_matrix" && i.Field == "to");
        }

        [Fact]
        public void Matrix_MissingRequiredFields_AreReported()
        {
            var (_, report) = Load(ThreeFactions, "[{\"id\": \"fac.reaction.bad\"}]");

            Assert.True(report.IsBlocking);
            foreach (var field in new[] { "from", "to", "reaction" })
            {
                Assert.Contains(report.Issues, i => i.Table == "fac.reaction_matrix" && i.Field == field);
            }
        }

        // -----------------------------------------------------------------
        // Factions 顺序
        // -----------------------------------------------------------------

        [Fact]
        public void Factions_FollowFactionTableRowOrder_NotIdOrder()
        {
            // 行序故意与 Id 字典序相反：矩阵的 Factions 必须保持数据行序。
            var rows = "[" + FactionRow("fac.sample_c", "friendly") + "," + FactionRow("fac.sample_a", "hostile") + "," +
                       FactionRow("fac.sample_b", "neutral") + "]";
            var (registry, report) = Load(rows, "[]");
            Assert.False(report.IsBlocking);

            var matrix = new FactionMatrix(registry, NewBus());

            Assert.Equal(new[] { C, A, B }, matrix.Factions);
        }

        // -----------------------------------------------------------------
        // ParseReaction 非法值（构造期）
        // -----------------------------------------------------------------

        private static DataRegistry LoadLoose(string factionRows, string matrixRows)
        {
            // 把 reaction 枚举字段放宽为 String，让非法值"合法通过"校验，专测 FactionMatrix 自身的解析防御。
            var looseFaction = new TableSchema("fac.faction", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("default_reaction", FieldKind.String, required: true),
            });
            var looseMatrix = new TableSchema("fac.reaction_matrix", "id", 1, new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("from", FieldKind.Id, required: true),
                new FieldSchema("to", FieldKind.Id, required: true),
                new FieldSchema("reaction", FieldKind.String, required: true),
            });
            var source = new InMemoryDataSource()
                .Add("fac.faction", Envelope("fac.faction", factionRows))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix", matrixRows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(looseFaction);
            registry.RegisterSchema(looseMatrix);
            Assert.False(registry.LoadAll().IsBlocking);
            return registry;
        }

        [Theory]
        [InlineData("Hostile")]
        [InlineData("HOSTILE")]
        [InlineData("enemy")]
        [InlineData("")]
        [InlineData(" hostile")]
        public void Constructor_IllegalDefaultReaction_ThrowsArgumentException_NamingTheValue(string bad)
        {
            var registry = LoadLoose("[" + FactionRow("fac.sample_a", bad) + "]", "[]");

            var ex = Assert.Throws<ArgumentException>(() => new FactionMatrix(registry, NewBus()));

            Assert.Equal("value", ex.ParamName);
            Assert.Contains("未知反应枚举值", ex.Message);
        }

        [Fact]
        public void Constructor_IllegalExplicitReaction_ThrowsArgumentException()
        {
            var registry = LoadLoose(
                "[" + FactionRow("fac.sample_a", "hostile") + "," + FactionRow("fac.sample_b", "neutral") + "]",
                "[" + MatrixRow("fac.reaction.a_b", "fac.sample_a", "fac.sample_b", "Friendly") + "]");

            Assert.Throws<ArgumentException>(() => new FactionMatrix(registry, NewBus()));
        }

        [Fact]
        public void Constructor_AcceptsAllThreeLowercaseReactionNames()
        {
            var registry = LoadLoose(
                "[" + FactionRow("fac.sample_a", "hostile") + "," + FactionRow("fac.sample_b", "neutral") + "," +
                FactionRow("fac.sample_c", "friendly") + "]", "[]");

            var matrix = new FactionMatrix(registry, NewBus());

            Assert.Equal(Reaction.Hostile, matrix.GetReaction(A, B));
            Assert.Equal(Reaction.Neutral, matrix.GetReaction(B, A));
            Assert.Equal(Reaction.Friendly, matrix.GetReaction(C, A));
        }

        [Fact]
        public void Constructor_NullArguments_Throw()
        {
            var (registry, _) = Load(ThreeFactions, "[]");

            Assert.Throws<ArgumentNullException>(() => new FactionMatrix(null!, NewBus()));
            Assert.Throws<ArgumentNullException>(() => new FactionMatrix(registry, null!));
        }

        // -----------------------------------------------------------------
        // 重复显式行：后者覆盖前者
        // -----------------------------------------------------------------

        [Fact]
        public void DuplicateExplicitRows_ForSamePair_LaterRowWinsOverEarlier()
        {
            var (registry, report) = Load(
                ThreeFactions,
                "[" + MatrixRow("fac.reaction.first", "fac.sample_a", "fac.sample_b", "friendly") + "," +
                MatrixRow("fac.reaction.second", "fac.sample_a", "fac.sample_b", "neutral") + "]");
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var matrix = new FactionMatrix(registry, NewBus());

            Assert.Equal(Reaction.Neutral, matrix.GetReaction(A, B));
        }

        [Fact]
        public void DuplicateExplicitRows_ReversedOrder_FlipsTheWinner()
        {
            var (registry, _) = Load(
                ThreeFactions,
                "[" + MatrixRow("fac.reaction.second", "fac.sample_a", "fac.sample_b", "neutral") + "," +
                MatrixRow("fac.reaction.first", "fac.sample_a", "fac.sample_b", "friendly") + "]");

            var matrix = new FactionMatrix(registry, NewBus());

            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, B));
        }

        [Fact]
        public void ExplicitRow_DoesNotAffectTheReverseDirection()
        {
            var (registry, _) = Load(
                ThreeFactions, "[" + MatrixRow("fac.reaction.a_b", "fac.sample_a", "fac.sample_b", "friendly") + "]");

            var matrix = new FactionMatrix(registry, NewBus());

            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, B));
            Assert.Equal(Reaction.Neutral, matrix.GetReaction(B, A)); // B 的 default_reaction
        }

        [Fact]
        public void ExplicitRowBetweenSameFaction_IsIgnored_SameFactionStaysFriendly()
        {
            var (registry, _) = Load(
                ThreeFactions, "[" + MatrixRow("fac.reaction.a_a", "fac.sample_a", "fac.sample_a", "hostile") + "]");

            var matrix = new FactionMatrix(registry, NewBus());

            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, A));
        }

        // -----------------------------------------------------------------
        // 覆盖表优先级：override > explicit > default
        // -----------------------------------------------------------------

        [Fact]
        public void Precedence_RuntimeOverrideBeatsExplicitRow_AndResetFallsBackToExplicit()
        {
            var (registry, _) = Load(
                ThreeFactions, "[" + MatrixRow("fac.reaction.a_b", "fac.sample_a", "fac.sample_b", "friendly") + "]");
            var matrix = new FactionMatrix(registry, NewBus());

            matrix.SetReaction(A, B, Reaction.Neutral);
            Assert.Equal(Reaction.Neutral, matrix.GetReaction(A, B));

            matrix.ResetOverrides();
            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, B));
        }

        [Fact]
        public void SetReaction_BackToTheDataValue_PublishesEventOnlyForActualChanges()
        {
            var (registry, _) = Load(ThreeFactions, "[]");
            var bus = NewBus();
            var matrix = new FactionMatrix(registry, bus);
            var events = new List<FactionRelationChangedEvent>();
            bus.Subscribe<FactionRelationChangedEvent>(FactionEventKeys.RelationChanged, e => events.Add(e));

            matrix.SetReaction(A, B, Reaction.Friendly); // hostile → friendly
            matrix.SetReaction(A, B, Reaction.Hostile);  // friendly → hostile（回到数据值，仍是一次变化）
            matrix.SetReaction(A, B, Reaction.Hostile);  // 无变化

            Assert.Equal(2, events.Count);
            Assert.Equal(Reaction.Hostile, events[0].OldReaction);
            Assert.Equal(Reaction.Friendly, events[0].NewReaction);
            Assert.Equal(Reaction.Friendly, events[1].OldReaction);
            Assert.Equal(Reaction.Hostile, events[1].NewReaction);
        }

        // -----------------------------------------------------------------
        // Reload 路径：矩阵是构造期快照
        // -----------------------------------------------------------------

        private sealed class MutableSource : IDataSource
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>();

            public void Set(string table, string rowsJson) => _texts[table] = Envelope(table, rowsJson);

            public IReadOnlyList<DataTableSource> ListTables()
            {
                var list = new List<DataTableSource>();
                foreach (var pair in _texts)
                {
                    var table = pair.Key;
                    list.Add(new DataTableSource(table, "memory://" + table, () => _texts[table]));
                }

                return list;
            }
        }

        [Fact]
        public void RegistryReload_DoesNotChangeAnExistingMatrix_ButANewMatrixSeesTheReloadedData()
        {
            var source = new MutableSource();
            source.Set("fac.faction", ThreeFactions);
            source.Set("fac.reaction_matrix", "[" + MatrixRow("fac.reaction.a_b", "fac.sample_a", "fac.sample_b", "friendly") + "]");
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);
            Assert.False(registry.LoadAll().IsBlocking);
            var first = new FactionMatrix(registry, NewBus());
            Assert.Equal(Reaction.Friendly, first.GetReaction(A, B));

            source.Set("fac.reaction_matrix", "[" + MatrixRow("fac.reaction.a_b", "fac.sample_a", "fac.sample_b", "hostile") + "]");
            var reloadReport = registry.Reload("fac.reaction_matrix");
            Assert.False(reloadReport.IsBlocking, string.Join("; ", reloadReport.Issues));

            // 已存在的矩阵是构造期快照：不随 Reload 变化（文档化语义）。
            Assert.Equal(Reaction.Friendly, first.GetReaction(A, B));
            // 重新构造的矩阵读到新数据。
            Assert.Equal(Reaction.Hostile, new FactionMatrix(registry, NewBus()).GetReaction(A, B));
        }

        [Fact]
        public void RuntimeOverrides_AreLostOnRebuild_ButSurviveRegistryReloadOnTheOldInstance()
        {
            var source = new MutableSource();
            source.Set("fac.faction", ThreeFactions);
            source.Set("fac.reaction_matrix", "[]");
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);
            Assert.False(registry.LoadAll().IsBlocking);
            var matrix = new FactionMatrix(registry, NewBus());
            matrix.SetReaction(B, C, Reaction.Hostile);

            registry.Reload("fac.faction");

            Assert.Equal(Reaction.Hostile, matrix.GetReaction(B, C));
            Assert.Equal(Reaction.Neutral, new FactionMatrix(registry, NewBus()).GetReaction(B, C));
        }
    }
}

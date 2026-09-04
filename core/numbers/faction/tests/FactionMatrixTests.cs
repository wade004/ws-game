using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Faction;
using Xunit;

namespace Tests.Numbers.Faction
{
    public class FactionMatrixTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(FactionEventKeys.RelationChanged, "faction",
                    new[] { "from", "to", "oldReaction", "newReaction" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // 三个阵营：A 默认敌对，B 默认中立，C 默认友好；显式登记 (A,B)=friendly（打破 A 的默认敌对）。
        private const string FactionRows = @"[
            { ""id"": ""fac.sample_a"", ""name_key"": ""l10n.fac.sample_a.name"", ""default_reaction"": ""hostile"" },
            { ""id"": ""fac.sample_b"", ""name_key"": ""l10n.fac.sample_b.name"", ""default_reaction"": ""neutral"" },
            { ""id"": ""fac.sample_c"", ""name_key"": ""l10n.fac.sample_c.name"", ""default_reaction"": ""friendly"" }
        ]";

        private const string ReactionMatrixRows = @"[
            { ""id"": ""fac.reaction.a_b"", ""from"": ""fac.sample_a"", ""to"": ""fac.sample_b"", ""reaction"": ""friendly"" }
        ]";

        private static FactionMatrix MakeMatrix(out IEventBus bus)
        {
            bus = MakeBus();
            var source = new InMemoryDataSource()
                .Add("fac.faction", Envelope("fac.faction", FactionRows))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix", ReactionMatrixRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);

            return new FactionMatrix(registry, bus);
        }

        private static readonly Id A = new Id("fac.sample_a");
        private static readonly Id B = new Id("fac.sample_b");
        private static readonly Id C = new Id("fac.sample_c");

        // -----------------------------------------------------------------
        // 1. 同阵营友好
        // -----------------------------------------------------------------

        [Fact]
        public void GetReaction_SameFaction_IsFriendly()
        {
            var matrix = MakeMatrix(out _);
            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, A));
        }

        // -----------------------------------------------------------------
        // 2. 显式行
        // -----------------------------------------------------------------

        [Fact]
        public void GetReaction_ExplicitRow_OverridesDefault()
        {
            var matrix = MakeMatrix(out _);
            // A 默认 hostile，但 (A,B) 显式登记为 friendly。
            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, B));
        }

        // -----------------------------------------------------------------
        // 3. 默认反应回退
        // -----------------------------------------------------------------

        [Fact]
        public void GetReaction_NoExplicitRow_FallsBackToDefault()
        {
            var matrix = MakeMatrix(out _);
            // (A,C) 未显式登记，回退到 A 的 default_reaction = hostile。
            Assert.Equal(Reaction.Hostile, matrix.GetReaction(A, C));
            // (C,A) 未显式登记，回退到 C 的 default_reaction = friendly。
            Assert.Equal(Reaction.Friendly, matrix.GetReaction(C, A));
        }

        // -----------------------------------------------------------------
        // 4. 非对称
        // -----------------------------------------------------------------

        [Fact]
        public void GetReaction_Matrix_IsNotSymmetric()
        {
            var matrix = MakeMatrix(out _);
            // (A,B) 显式 friendly，(B,A) 未登记回退到 B 的 default_reaction = neutral。
            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, B));
            Assert.Equal(Reaction.Neutral, matrix.GetReaction(B, A));
        }

        // -----------------------------------------------------------------
        // 5. SetReaction 事件与 ResetOverrides
        // -----------------------------------------------------------------

        [Fact]
        public void SetReaction_ChangesValue_AndPublishesEvent_OnlyWhenDifferent()
        {
            var matrix = MakeMatrix(out var bus);

            FactionRelationChangedEvent? received = null;
            var callCount = 0;
            bus.Subscribe<FactionRelationChangedEvent>(FactionEventKeys.RelationChanged, e =>
            {
                received = e;
                callCount++;
            });

            // (B,C) 当前回退到 B 的 default_reaction = neutral。
            Assert.Equal(Reaction.Neutral, matrix.GetReaction(B, C));

            matrix.SetReaction(B, C, Reaction.Hostile);
            Assert.Equal(Reaction.Hostile, matrix.GetReaction(B, C));
            Assert.Equal(1, callCount);
            Assert.NotNull(received);
            Assert.Equal(Reaction.Neutral, received!.OldReaction);
            Assert.Equal(Reaction.Hostile, received.NewReaction);

            // 设成同样的值不再发事件。
            matrix.SetReaction(B, C, Reaction.Hostile);
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void ResetOverrides_RestoresDataLoadedState()
        {
            var matrix = MakeMatrix(out _);

            matrix.SetReaction(B, C, Reaction.Hostile);
            Assert.Equal(Reaction.Hostile, matrix.GetReaction(B, C));

            matrix.ResetOverrides();
            Assert.Equal(Reaction.Neutral, matrix.GetReaction(B, C));

            // 显式登记行不受 ResetOverrides 影响。
            matrix.SetReaction(A, B, Reaction.Hostile);
            Assert.Equal(Reaction.Hostile, matrix.GetReaction(A, B));
            matrix.ResetOverrides();
            Assert.Equal(Reaction.Friendly, matrix.GetReaction(A, B));
        }

        // -----------------------------------------------------------------
        // 6. IsHostile 便捷方法
        // -----------------------------------------------------------------

        [Fact]
        public void IsHostile_ReflectsGetReaction()
        {
            var matrix = MakeMatrix(out _);
            Assert.True(matrix.IsHostile(A, C)); // 回退到 A 的 default_reaction = hostile
            Assert.False(matrix.IsHostile(A, B)); // 显式 friendly
        }

        // -----------------------------------------------------------------
        // 7. 未知阵营异常
        // -----------------------------------------------------------------

        [Fact]
        public void GetReaction_UnknownFaction_Throws()
        {
            var matrix = MakeMatrix(out _);
            var unknown = new Id("fac.unknown");

            Assert.Throws<ArgumentException>(() => matrix.GetReaction(unknown, A));
            Assert.Throws<ArgumentException>(() => matrix.GetReaction(A, unknown));
            Assert.Throws<ArgumentException>(() => matrix.SetReaction(unknown, A, Reaction.Neutral));
        }

        // -----------------------------------------------------------------
        // 8. Factions 顺序
        // -----------------------------------------------------------------

        [Fact]
        public void Factions_ReturnsAllLoadedFactions()
        {
            var matrix = MakeMatrix(out _);
            Assert.Equal(3, matrix.Factions.Count);
            Assert.Contains(A, matrix.Factions);
            Assert.Contains(B, matrix.Factions);
            Assert.Contains(C, matrix.Factions);
        }
    }
}

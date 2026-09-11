using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// ADR-0028 收边验收：真实 <see cref="CarriersAssembly"/> 装配（不是手工在测试里
    /// <c>new ProjectileHost(...)</c> 再自己塞一个假 <see cref="IFactionMatrix"/>）确实把
    /// <see cref="CarriersAssembly.Projectiles"/> 的 <c>Factions</c> 属性回填成与
    /// <see cref="CarriersAssembly.Rules"/>.<c>Factions</c> 同一个实例——见
    /// <c>CarriersAssembly</c> 构造函数第 3 步之后的判断记录（<c>ProjectileHost</c>（第 2.5 步）
    /// 先于 <c>RulesAssembly</c>（第 3 步，<c>Factions</c> 在其内部构造）构造完成，两者存在"谁先
    /// 构造"的循环依赖，只能靠构造完成后的可写属性回填）。
    /// </summary>
    public sealed class ADR0028_ProjectileFactionsBackfillTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static void AddMinimalRequiredTables(InMemoryDataSource source)
        {
            // 惯例同 CarriersAssemblyTests.AddMinimalRequiredTables：两类"必须有数据"的前置条件，
            // 与本测试真正关心的"Factions 是否正确回填"无关，先垫上。
            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("stat.definition", "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");
        }

        private static CarriersAssembly BuildAssembly(InMemoryDataSource source)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            AddMinimalRequiredTables(source);
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var navigation = new StubNavigation2D();
            var rng = new RngHost(1);

            return new CarriersAssembly(bus, registry, rng, world, spatial, navigation);
        }

        [Fact]
        public void Construct_BackfillsProjectilesFactions_SameInstanceAsRulesFactions()
        {
            var assembly = BuildAssembly(new InMemoryDataSource());

            Assert.NotNull(assembly.Rules.Factions);
            Assert.Same(assembly.Rules.Factions, assembly.Projectiles.Factions);
        }

        [Fact]
        public void Construct_WithFactionData_ProjectilesFactionsReflectsRealReactionMatrix()
        {
            // 用真实 fac.faction/fac.reaction_matrix 数据（不是桩）验证回填进 Projectiles 的确实是
            // 一个可正常工作的 IFactionMatrix，不只是"非空引用"这一层。
            var source = new InMemoryDataSource()
                .Add("fac.faction", Envelope("fac.faction", @"[
                    { ""id"": ""fac.adr0028_hero"", ""name_key"": ""l10n.fac.adr0028_hero.name"", ""default_reaction"": ""neutral"" },
                    { ""id"": ""fac.adr0028_monster"", ""name_key"": ""l10n.fac.adr0028_monster.name"", ""default_reaction"": ""neutral"" }
                ]"))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix", @"[
                    { ""id"": ""fac.adr0028_reaction"", ""from"": ""fac.adr0028_hero"", ""to"": ""fac.adr0028_monster"", ""reaction"": ""hostile"" }
                ]"));

            var assembly = BuildAssembly(source);

            var hero = new Id("fac.adr0028_hero");
            var monster = new Id("fac.adr0028_monster");

            Assert.NotNull(assembly.Projectiles.Factions);
            Assert.Equal(Reaction.Hostile, assembly.Projectiles.Factions!.GetReaction(hero, monster));
            // 与 SkillHost.FindUnits 共用同一账本：运行期覆盖对两侧同步可见。
            assembly.Rules.Factions.SetReaction(hero, monster, Reaction.Friendly);
            Assert.Equal(Reaction.Friendly, assembly.Projectiles.Factions!.GetReaction(hero, monster));
        }
    }
}

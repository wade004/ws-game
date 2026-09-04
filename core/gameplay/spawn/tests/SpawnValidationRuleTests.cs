using Core.Foundation.DataRegistry;
using Core.Gameplay.Spawn;
using Xunit;

namespace Tests.Gameplay.Spawn
{
    public sealed class SpawnValidationRuleTests
    {
        private static ValidationReport Validate(Core.Foundation.Common.Json.JsonObject row, params IValidationRule[] rules)
        {
            var bus = SpawnTestSupport.NewEventBus();
            var registry = SpawnTestSupport.BuildRegistry(bus, row);
            foreach (var rule in rules)
            {
                registry.RegisterValidationRule(rule);
            }

            return registry.LoadAll();
        }

        [Fact]
        public void RespawnPolicyFieldGroupRule_TimerWithoutTimer_ReportsError()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "timer");
            var report = Validate(row, new SpawnRespawnPolicyFieldGroupRule());

            Assert.Contains(report.Issues, i => i.Check == "spawn_respawn_policy_field_group" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void RespawnPolicyFieldGroupRule_TimerWithPositiveTimer_NoIssue()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "timer", respawnTimer: 10);
            var report = Validate(row, new SpawnRespawnPolicyFieldGroupRule());

            Assert.DoesNotContain(report.Issues, i => i.Check == "spawn_respawn_policy_field_group");
        }

        [Fact]
        public void RespawnPolicyFieldGroupRule_NonTimerPolicy_IgnoresMissingTimer()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter");
            var report = Validate(row, new SpawnRespawnPolicyFieldGroupRule());

            Assert.DoesNotContain(report.Issues, i => i.Check == "spawn_respawn_policy_field_group");
        }

        [Fact]
        public void ContentRefRule_IllegalDomain_ReportsError()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "item.sample_sword", "on_map_enter");
            var report = Validate(row, new SpawnContentRefRule());

            Assert.Contains(report.Issues, i => i.Check == "spawn_content_ref" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void ContentRefRule_LegalDomainWithoutLoadedTable_NoIssue()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter");
            var report = Validate(row, new SpawnContentRefRule());

            Assert.DoesNotContain(report.Issues, i => i.Check == "spawn_content_ref");
        }

        [Fact]
        public void SummonOnlyRule_WithoutTemplateQuery_ReportsWarningOnly()
        {
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter");
            var report = Validate(row, new SpawnSummonOnlyCreatureRule(null));

            Assert.Contains(report.Issues, i => i.Check == "spawn_summon_only_creature" && i.Severity == ValidationSeverity.Warning);
            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void SummonOnlyRule_FlaggedCreature_ReportsError()
        {
            var query = new FakeCreatureTemplateQuery()
                .WithFlags(new Core.Foundation.Common.Id("creature.sample_pet"), Core.Carriers.Creature.NpcFlag.SummonOnly);
            var row = SpawnTestSupport.Row("spawn.sample_pet", "world.sample_map", "creature.sample_pet", "on_map_enter");
            var report = Validate(row, new SpawnSummonOnlyCreatureRule(query));

            Assert.Contains(report.Issues, i => i.Check == "spawn_summon_only_creature" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void SummonOnlyRule_NonSummonOnlyCreature_NoIssue()
        {
            var query = new FakeCreatureTemplateQuery()
                .WithFlags(new Core.Foundation.Common.Id("creature.sample_wolf"));
            var row = SpawnTestSupport.Row("spawn.sample_wolf", "world.sample_map", "creature.sample_wolf", "on_map_enter");
            var report = Validate(row, new SpawnSummonOnlyCreatureRule(query));

            Assert.DoesNotContain(report.Issues, i => i.Check == "spawn_summon_only_creature" && i.Severity == ValidationSeverity.Error);
        }
    }
}

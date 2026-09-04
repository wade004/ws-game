using Core.Carriers.Creature;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Creature
{
    public class CreatureValidationTests
    {
        [Fact]
        public void Validate_RejectsUnknownNpcFlag()
        {
            var bus = CreatureTestSupport.CreateBus();

            const string templateRows = "[" +
                "{\"id\": \"creature.sample_bad_flag\", \"name_key\": \"l10n.creature.sample_bad_flag.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
                "\"base_stats\": {\"stat.power\": 1}, \"faction_id\": \"fac.test_monster\", " +
                "\"npc_flags\": [\"npc_flag.unknown_flag\"], \"display_ref\": \"display.sample\"}" +
                "]";

            var source = new InMemoryDataSource()
                .Add("stat.definition", CreatureTestSupport.Envelope("stat.definition", CreatureTestSupport.StatDefinitionRows))
                .Add(CreatureSchemas.TierDefinition.Name,
                    CreatureTestSupport.Envelope(CreatureSchemas.TierDefinition.Name, CreatureTestSupport.TierDefinitionRows))
                .Add(CreatureSchemas.Template.Name,
                    CreatureTestSupport.Envelope(CreatureSchemas.Template.Name, templateRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(CreatureSchemas.TierDefinition);
            registry.RegisterSchema(CreatureSchemas.Template);
            registry.RegisterValidationRule(new CreatureContentValidationRule());

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var found = false;
            foreach (var issue in report.Issues)
            {
                if (issue.Check == "creature_content" && issue.Field == "npc_flags")
                {
                    found = true;
                }
            }
            Assert.True(found, "应报告 npc_flags 含未登记职能标志的错误");
        }

        [Fact]
        public void Validate_RejectsMissingStatGrowthRef()
        {
            var bus = CreatureTestSupport.CreateBus();

            const string templateRows = "[" +
                "{\"id\": \"creature.sample_bad_growth\", \"name_key\": \"l10n.creature.sample_bad_growth.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
                "\"base_stats\": {\"stat.power\": 1}, \"stat_growth_ref\": \"prog.does_not_exist\", " +
                "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.sample\"}" +
                "]";

            var source = new InMemoryDataSource()
                .Add("stat.definition", CreatureTestSupport.Envelope("stat.definition", CreatureTestSupport.StatDefinitionRows))
                .Add(CreatureSchemas.TierDefinition.Name,
                    CreatureTestSupport.Envelope(CreatureSchemas.TierDefinition.Name, CreatureTestSupport.TierDefinitionRows))
                .Add(CreatureSchemas.Template.Name,
                    CreatureTestSupport.Envelope(CreatureSchemas.Template.Name, templateRows));
            // 注意：不加载 prog.level_curve 表，stat_growth_ref 的引用完整性校验必然失败。

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(CreatureSchemas.TierDefinition);
            registry.RegisterSchema(CreatureSchemas.Template);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            var found = false;
            foreach (var issue in report.Issues)
            {
                if (issue.Check == "reference_integrity" && issue.Field == "stat_growth_ref")
                {
                    found = true;
                }
            }
            Assert.True(found, "应报告 stat_growth_ref 引用不存在的错误");
        }
    }
}

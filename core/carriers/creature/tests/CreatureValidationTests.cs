using System.Collections.Generic;
using Core.Carriers.Creature;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Carriers.Creature
{
    public class CreatureValidationTests
    {
        /// <summary>消费方反馈第 28 条：<c>npc_flags</c> 非法值现由 <c>CreatureSchemas.Template</c>
        /// 登记的 <c>FieldSchema.WithAllowedValues(NpcFlagIds.All)</c> 在 <c>DataRegistry.LoadAll</c>
        /// 加载期拦截（<c>field_allowed_value</c> 检查项），不再是 <c>CreatureContentValidationRule</c>
        /// 手写的 <c>creature_content</c> 检查项职责（该规则已收口，见其类型判断记录）——本用例同时
        /// 断言"只报告一次"：即使仍显式注册 <c>CreatureContentValidationRule</c>（惯例调用不变），
        /// 也不会对同一处非法取值重复报告 <c>creature_content</c>。</summary>
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

            var matches = new List<ValidationIssue>();
            foreach (var issue in report.Issues)
            {
                if (issue.Field != null && issue.Field.StartsWith("npc_flags", System.StringComparison.Ordinal))
                {
                    matches.Add(issue);
                }
            }

            Assert.Single(matches);
            Assert.Equal("field_allowed_value", matches[0].Check);
            Assert.Equal("npc_flags[0]", matches[0].Field);
            Assert.DoesNotContain(report.Issues, i => i.Check == "creature_content");
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

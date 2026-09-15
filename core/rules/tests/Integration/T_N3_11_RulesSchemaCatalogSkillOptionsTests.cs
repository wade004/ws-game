using System.Linq;
using Adapters.Stub;
using Core.Foundation.DataRegistry;
using Core.Rules.Assembly;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// T-N3-11（分阶段落地计划第 14 节 N3 任务表第十一行"MaxEffectsPerSkillRule 接
    /// SkillOptions"）：<c>RulesSchemaCatalog.RegisterL2Schemas</c>（私有方法）此前把
    /// <c>MaxEffectsPerSkillRule</c> 的上限硬编码为 8，新增
    /// <see cref="RulesSchemaCatalog.RegisterAll(IDataRegistry, SkillOptions)"/> 重载后改为读取
    /// 调用方传入的 <see cref="SkillOptions.MaxEffectsPerSkill"/>。本测试验证两件事：(1) 无参
    /// <see cref="RulesSchemaCatalog.RegisterAll(IDataRegistry)"/> 逐位不变，仍按硬编码默认值 8；
    /// (2) 新重载传入自定义 <see cref="SkillOptions.MaxEffectsPerSkill"/> 后，加载期真的按注入值
    /// 拦截，不是仅登记了字段却未接线。惯例同 <c>ArchetypeDerivationOverrideTests</c>：用一个独立
    /// 的最小 <see cref="DataRegistry"/> 夹具（不复用 <see cref="FightWorldBuilder"/> 的完整世界，
    /// 只需要 <c>skill.def</c>/<c>target.chain_def</c> 两张表）。
    /// </summary>
    public sealed class T_N3_11_RulesSchemaCatalogSkillOptionsTests
    {
        private const string TargetChainJson =
            "{\"table\":\"target.chain_def\",\"schema_version\":1,\"rows\":[" +
            "{\"id\":\"target.chain.n3_11_sample\",\"source\":\"self\"}]}";

        private static string SkillDefJson(int effectCount)
        {
            var effects = string.Join(",", Enumerable.Range(0, effectCount).Select(_ =>
                "{\"kind\":\"school_damage\",\"params\":{\"base_value\":1,\"coefficient\":0}}"));
            return "{\"table\":\"skill.def\",\"schema_version\":2,\"rows\":[" +
                "{\"id\":\"skill.n3_11_sample\",\"school\":\"school.n3_11_sample\",\"kind\":\"active\"," +
                "\"range\":0,\"cast_time\":0,\"respects_gcd\":false," +
                "\"target_shape_ref\":\"target.chain.n3_11_sample\",\"effects\":[" + effects + "]}]}";
        }

        private static ValidationReport LoadWithEffectCount(int effectCount, SkillOptions? skillOptions, bool useOverload)
        {
            var bus = FightWorldBuilder.BuildEventBus(out _);
            var source = new InMemoryDataSource()
                .Add("target.chain_def", TargetChainJson)
                .Add("skill.def", SkillDefJson(effectCount));
            var registry = new DataRegistry(source, bus, RulesSchemaCatalog.CreateOptions());
            if (useOverload)
            {
                RulesSchemaCatalog.RegisterAll(registry, skillOptions);
            }
            else
            {
                RulesSchemaCatalog.RegisterAll(registry);
            }
            return registry.LoadAll();
        }

        [Fact]
        public void RegisterAll_ParameterlessOverload_KeepsHardcodedDefaultOfEight_Regression()
        {
            Assert.False(LoadWithEffectCount(8, skillOptions: null, useOverload: false).IsBlocking);

            var over = LoadWithEffectCount(9, skillOptions: null, useOverload: false);
            Assert.True(over.IsBlocking);
            Assert.Contains(over.Issues, i => i.Check == "max_effects_per_skill");
        }

        [Fact]
        public void RegisterAll_WithNullSkillOptions_BehavesSameAsParameterlessOverload()
        {
            Assert.False(LoadWithEffectCount(8, skillOptions: null, useOverload: true).IsBlocking);

            var over = LoadWithEffectCount(9, skillOptions: null, useOverload: true);
            Assert.True(over.IsBlocking);
            Assert.Contains(over.Issues, i => i.Check == "max_effects_per_skill");
        }

        [Fact]
        public void RegisterAll_WithCustomMaxEffectsPerSkill_UsesInjectedLimitInsteadOfHardcodedEight()
        {
            var options = new SkillOptions { MaxEffectsPerSkill = 2 };

            // 2 个效果：注入上限内，通过；改动前的硬编码上限（8）会误判为通过，验证不到本任务改动。
            Assert.False(LoadWithEffectCount(2, options, useOverload: true).IsBlocking);

            // 3 个效果：超过注入上限（2）但仍在改动前的硬编码上限（8）以内——若本任务未真正接线，
            // 这一步会假通过，是本测试的关键断言。
            var over = LoadWithEffectCount(3, options, useOverload: true);
            Assert.True(over.IsBlocking);
            Assert.Contains(over.Issues, i => i.Check == "max_effects_per_skill");
        }
    }
}

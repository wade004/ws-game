using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// ADR-0024 第二批登记：<see cref="Core.Rules.Ai.AiSchemas.BehaviorProfile"/> 的 <c>transitions</c>
    /// 改用 <c>MapSchema.FreeKeyed</c>（值种类 <c>FieldKind.Expr</c>）登记，覆盖范围：子结构命中/
    /// 坏形状各一例（行为语义覆盖见 <c>AiTransitionsOverrideTests.cs</c>，不在本文件重复）。
    /// </summary>
    public sealed class AiSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        // 同 AiContentValidationRuleTests 判断记录：transitions 值登记为 FieldKind.Expr，内建
        // expr_parsable 校验需要 ExprSchema 才会真正解析（未配置时只降级为 Warning），配上与运行期
        // 同一份 Core.Rules.ExprHost.RulesExprSchema.Base。rotation_ref 是本表既有的
        // Reference(ai.rotation) 字段（与本次 transitions 登记无关，但必须一并满足才能验证
        // transitions 本身），entries[].skill_id 进一步要求 Reference(skill.def)，一并补最小夹具。
        private const string SkillDefRows =
            "[{\"id\": \"skill.cov_sample\", \"school\": \"skill.school.ai_cov\", \"kind\": \"active\"," +
            " \"range\": 0, \"cast_time\": 0, \"respects_gcd\": true," +
            " \"target_shape_ref\": \"target.ai_cov\", \"effects\": []}]";

        private const string RotationRows =
            "[{\"id\": \"ai.rotation.cov_sample\", \"entries\": [" +
            "{\"priority\": 1, \"condition\": \"self.is_alive\", \"skill_id\": \"skill.cov_sample\"}]}]";

        private static DataRegistry MakeRegistry(string profileRows)
        {
            var source = new InMemoryDataSource()
                .Add(Core.Rules.Skill.SkillSchemas.Def.Name, Envelope(Core.Rules.Skill.SkillSchemas.Def.Name, SkillDefRows))
                .Add(Core.Rules.Ai.AiSchemas.Rotation.Name, Envelope(Core.Rules.Ai.AiSchemas.Rotation.Name, RotationRows))
                .Add(Core.Rules.Ai.AiSchemas.BehaviorProfile.Name, Envelope(Core.Rules.Ai.AiSchemas.BehaviorProfile.Name, profileRows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions
            {
                ExprSchema = Core.Rules.ExprHost.RulesExprSchema.Base,
            });
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.Def);
            registry.RegisterSchema(Core.Rules.Ai.AiSchemas.Rotation);
            registry.RegisterSchema(Core.Rules.Ai.AiSchemas.BehaviorProfile);
            return registry;
        }

        [Fact]
        public void Transitions_ArbitraryKeyValidExpr_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"ai.profile.cov_sample\",\"perception_radius\":10,\"leash_range\":20," +
                "\"combat_return_policy\":\"return_to_spawn\",\"rotation_ref\":\"ai.rotation.cov_sample\"," +
                "\"transitions\":{\"idle_to_chase\":\"self.is_alive\"}}]";

            var report = MakeRegistry(rows).LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Transitions_MalformedExpr_ReportsExprParsableWithBracketPath()
        {
            var rows = "[{\"id\":\"ai.profile.cov_bad\",\"perception_radius\":10,\"leash_range\":20," +
                "\"combat_return_policy\":\"return_to_spawn\",\"rotation_ref\":\"ai.rotation.cov_sample\"," +
                "\"transitions\":{\"idle_to_chase\":\"((\"}}]";

            var report = MakeRegistry(rows).LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "expr_parsable" && i.Field == "transitions[idle_to_chase]");
        }
    }
}

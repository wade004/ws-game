using Core.Foundation.DataRegistry;
using Core.Rules.Ai;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>验收标准"校验：priority 不重复（同表内）、flee_hp_pct_threshold ∈ [0,1]、
    /// points ≥ 2"（任务书 T2-10）。</summary>
    public class AiContentValidationRuleTests
    {
        private static ValidationReport Validate(string profilesJson, string rotationsJson, string patrolsJson)
        {
            var bus = AiTestSupport.CreateBus();
            var source = new InMemoryDataSource()
                .Add(AiSchemas.BehaviorProfile.Name, AiTestSupport.Envelope(AiSchemas.BehaviorProfile.Name, profilesJson))
                .Add(AiSchemas.Rotation.Name, AiTestSupport.Envelope(AiSchemas.Rotation.Name, rotationsJson))
                .Add(AiSchemas.PatrolPath.Name, AiTestSupport.Envelope(AiSchemas.PatrolPath.Name, patrolsJson));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(AiSchemas.BehaviorProfile);
            registry.RegisterSchema(AiSchemas.Rotation);
            registry.RegisterSchema(AiSchemas.PatrolPath);
            registry.RegisterValidationRule(new AiContentValidationRule());

            return registry.LoadAll();
        }

        [Fact]
        public void DuplicatePriorityInSameRotation_IsReportedAsError()
        {
            const string rotationsJson = @"[
                { ""id"": ""ai.rotation.dup"", ""entries"": [
                    { ""priority"": 10, ""condition"": ""true"", ""skill_id"": ""skill.a"" },
                    { ""priority"": 10, ""condition"": ""true"", ""skill_id"": ""skill.b"" }
                ] }
            ]";

            var report = Validate("[]", rotationsJson, "[]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "ai_content" && i.Table == AiSchemas.Rotation.Name);
        }

        [Fact]
        public void DistinctPriorities_PassValidation()
        {
            const string rotationsJson = @"[
                { ""id"": ""ai.rotation.ok"", ""entries"": [
                    { ""priority"": 10, ""condition"": ""true"", ""skill_id"": ""skill.a"" },
                    { ""priority"": 20, ""condition"": ""true"", ""skill_id"": ""skill.b"" }
                ] }
            ]";

            var report = Validate("[]", rotationsJson, "[]");

            Assert.False(report.IsBlocking);
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        public void FleeHpPctThresholdOutOfRange_IsReportedAsError(double threshold)
        {
            var profilesJson = "[{ \"id\": \"ai.profile.bad_threshold\", \"perception_radius\": 10, \"leash_range\": 10," +
                " \"combat_return_policy\": \"stay\", \"rotation_ref\": \"ai.rotation.ok\", \"flee_hp_pct_threshold\": " +
                threshold.ToString(System.Globalization.CultureInfo.InvariantCulture) + " }]";
            const string rotationsJson = @"[
                { ""id"": ""ai.rotation.ok"", ""entries"": [
                    { ""priority"": 1, ""condition"": ""true"", ""skill_id"": ""skill.a"" }
                ] }
            ]";

            var report = Validate(profilesJson, rotationsJson, "[]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "ai_content" && i.Table == AiSchemas.BehaviorProfile.Name);
        }

        [Fact]
        public void FleeHpPctThresholdWithinRange_PassesValidation()
        {
            const string profilesJson = @"[{ ""id"": ""ai.profile.ok_threshold"", ""perception_radius"": 10, ""leash_range"": 10,
                ""combat_return_policy"": ""stay"", ""rotation_ref"": ""ai.rotation.ok"", ""flee_hp_pct_threshold"": 0.25 }]";
            const string rotationsJson = @"[
                { ""id"": ""ai.rotation.ok"", ""entries"": [
                    { ""priority"": 1, ""condition"": ""true"", ""skill_id"": ""skill.a"" }
                ] }
            ]";

            var report = Validate(profilesJson, rotationsJson, "[]");

            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void PatrolPathWithFewerThanTwoPoints_IsReportedAsError()
        {
            const string patrolsJson = @"[
                { ""id"": ""ai.path.too_short"", ""points"": [{""x"": 0, ""y"": 0}], ""mode"": ""loop"" }
            ]";

            var report = Validate("[]", "[]", patrolsJson);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "ai_content" && i.Table == AiSchemas.PatrolPath.Name);
        }

        [Fact]
        public void PatrolPathWithTwoPoints_PassesValidation()
        {
            const string patrolsJson = @"[
                { ""id"": ""ai.path.ok"", ""points"": [{""x"": 0, ""y"": 0}, {""x"": 10, ""y"": 0}], ""mode"": ""loop"" }
            ]";

            var report = Validate("[]", "[]", patrolsJson);

            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void UnparsableConditionExpr_IsReportedAsExprParsableError()
        {
            const string rotationsJson = @"[
                { ""id"": ""ai.rotation.bad_expr"", ""entries"": [
                    { ""priority"": 1, ""condition"": ""("", ""skill_id"": ""skill.a"" }
                ] }
            ]";

            var report = Validate("[]", rotationsJson, "[]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable");
        }
    }
}

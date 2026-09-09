using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.Encounter
{
    /// <summary>
    /// ADR-0019 / F1b：<c>EncounterSchemas.Def</c> 五个复合字段（<c>units</c>/<c>waves</c>/<c>phases</c>/
    /// <c>arena_rules</c>/<c>initiative_override</c>）子结构登记的覆盖一致性测试 + 子结构命中/坏形状
    /// 用例（惯例同 <c>SkillSchemaCoverageTests</c>/<c>SubstructureValidationTests</c>）。本文件区别于
    /// <c>EncounterHostTests</c>：只关心 <c>DataRegistry</c> 加载期校验结果（<see cref="ValidationReport"/>），
    /// 不驱动 <see cref="EncounterHost"/> 运行期行为。
    /// </summary>
    public sealed class EncounterSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>供本文件测试共用的最小可解析 <see cref="IExprSchema"/>：只登记
        /// <c>self.is_alive</c>/<c>combat.in_combat</c> 两个布尔取值，够用即可（惯例同
        /// <c>SubstructureValidationTests.NestedExpr_*</c>）。<c>TestSupport.MakeRegistry</c> 不设置
        /// <see cref="DataRegistryOptions.ExprSchema"/>（<c>expr_parsable</c> 只降级为 Warning），本文件
        /// 需要真正触发 Error 级 <c>expr_parsable</c> 判定，因此不复用它，自行装配。</summary>
        private static IExprSchema MakeExprSchema() =>
            new ExprSchema()
                .Register("self", "is_alive", ExprValueKind.Bool)
                .Register("combat", "in_combat", ExprValueKind.Bool);

        // -----------------------------------------------------------------
        // 变体覆盖一致性：arena_rules.bounds_shape 按 kind 分派的四种形状
        // -----------------------------------------------------------------

        [Fact]
        public void BoundsShapeVariantKeys_MatchShapeKindNamesFullSet()
        {
            var arenaField = EncounterSchemas.Def.GetField("arena_rules");
            Assert.NotNull(arenaField);
            var boundsShapeField = arenaField!.Fields!.Single(f => f.Name == "bounds_shape");
            var variants = boundsShapeField.Variants;
            Assert.NotNull(variants);

            var registered = new HashSet<string>(variants!.Cases.Keys, StringComparer.Ordinal);
            var expected = new HashSet<string>(EncounterSchemas.BoundsShapeKindValues, StringComparer.Ordinal);

            Assert.Equal(expected, registered);
            Assert.Equal("kind", variants.Discriminator);
            Assert.Equal(
                Enum.GetValues(typeof(Core.Foundation.EngineAdapter.ShapeKind)).Length,
                variants.Cases.Count);
            Assert.Same(boundsShapeField, EncounterSchemas.BoundsShapeSchema);
        }

        // -----------------------------------------------------------------
        // 装配帮助：本文件自己的 DataRegistry（需要真正生效的 ExprSchema，见上）。
        // -----------------------------------------------------------------

        private static ValidationReport LoadDefRows(string defRowsJson, bool withCreatureTemplate = false)
        {
            var bus = TestSupport.CreateBus();
            var source = new InMemoryDataSource()
                .Add(EncounterSchemas.Def.Name, Envelope(EncounterSchemas.Def.Name, defRowsJson));

            if (withCreatureTemplate)
            {
                source
                    .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, Envelope(
                        Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                        "[{\"id\": \"creature.tier.sample\", \"name_key\": \"l10n.creature.tier.sample.name\"}]"))
                    .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name, Envelope(
                        Core.Carriers.Creature.CreatureSchemas.Template.Name,
                        "[{\"id\": \"creature.sample_known\", \"name_key\": \"l10n.creature.sample_known.name\", " +
                        "\"level\": 1, \"tier\": \"creature.tier.sample\", \"base_stats\": {}, " +
                        "\"faction_id\": \"fac.sample_monster\", \"display_ref\": \"display.sample_known\"}]"));
            }

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { ExprSchema = MakeExprSchema() });
            registry.RegisterSchema(EncounterSchemas.Def);
            if (withCreatureTemplate)
            {
                registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.TierDefinition);
                registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.Template);
            }

            return registry.LoadAll();
        }

        private const string MinimalUnits = "[{\"spawn_ref\": \"spawn.sample_a\"}]";

        // -----------------------------------------------------------------
        // 子结构命中：waves[].trigger_condition / phases[].enter_condition 合法 Expr、
        // arena_rules.bounds_shape 的 rect 变体、initiative_override 合法取值。
        // -----------------------------------------------------------------

        [Fact]
        public void WavesAndPhases_ValidExprAndShape_Passes()
        {
            var rows = "[{\"id\": \"encounter.sample_ok\", \"units\": " + MinimalUnits + ", " +
                "\"waves\": [{\"trigger_condition\": \"self.is_alive\", \"spawn_refs\": [\"spawn.sample_add\"]}], " +
                "\"phases\": [{\"enter_condition\": \"combat.in_combat\", \"ai_rotation_override\": {\"creature.sample_boss\": \"ai.rotation.phase2\"}}], " +
                "\"arena_rules\": {\"bounds_shape\": {\"kind\": \"rect\", \"origin\": {\"x\": 0, \"y\": 0}, " +
                "\"half_extents\": {\"x\": 5, \"y\": 5}, \"rotation\": 0}, \"reset_if_leave\": false}, " +
                "\"initiative_override\": {\"policy\": \"action_points\", \"params\": {\"action_points_per_turn\": 3}}, " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        /// <summary><c>ai_rotation_override</c> 是 Map 型对象（判断记录：ADR-0019 首批范围外，见
        /// <c>EncounterSchemas.PhaseItemSchema</c>），任意键都不应报 <c>unknown_subfield</c>（未登记
        /// <c>Fields</c> 时维持"存在且是对象"的向后兼容行为）。</summary>
        [Fact]
        public void AiRotationOverride_ArbitraryKeys_NoUnknownSubfieldReported()
        {
            var rows = "[{\"id\": \"encounter.sample_map\", \"units\": " + MinimalUnits + ", " +
                "\"phases\": [{\"enter_condition\": \"combat.in_combat\", " +
                "\"ai_rotation_override\": {\"creature.whatever_key\": \"ai.rotation.x\", \"creature.another\": \"ai.rotation.y\"}}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "unknown_subfield");
        }

        [Fact]
        public void TemplateRef_KnownCreatureTemplate_Passes()
        {
            var rows = "[{\"id\": \"encounter.sample_known_template\", " +
                "\"units\": [{\"template_ref\": \"creature.sample_known\", \"position\": {\"x\": 0, \"y\": 0}}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows, withCreatureTemplate: true);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // 坏形状：缺必填子字段 / 变体键非法 / 嵌套 Expr 不可解析 / Reference 目标不存在。
        // -----------------------------------------------------------------

        [Fact]
        public void BoundsShape_ConeMissingAngle_ReportsRequiredFieldAtNestedPath()
        {
            var rows = "[{\"id\": \"encounter.sample_bad_cone\", \"units\": " + MinimalUnits + ", " +
                "\"arena_rules\": {\"bounds_shape\": {\"kind\": \"cone\", \"origin\": {\"x\": 0, \"y\": 0}, " +
                "\"direction\": 0, \"radius\": 5}}, " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "arena_rules.bounds_shape.angle");
        }

        [Fact]
        public void BoundsShape_UnknownKind_ReportsVariantDiscriminator()
        {
            var rows = "[{\"id\": \"encounter.sample_bad_kind\", \"units\": " + MinimalUnits + ", " +
                "\"arena_rules\": {\"bounds_shape\": {\"kind\": \"triangle\", \"origin\": {\"x\": 0, \"y\": 0}}}, " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "variant_discriminator");
            Assert.Equal("arena_rules.bounds_shape.kind", issue.Field);
        }

        /// <summary>ADR-0019 / F1b 退役验证：<c>waves[].trigger_condition</c> 不可解析时，恰好报一条
        /// <c>expr_parsable</c>（旧版 <c>EncounterContentValidationRule</c> 手写检查已删除，不会与
        /// <c>DataRegistry</c> 内置校验重复报告同一缺陷——"同一缺陷不得双报"）。</summary>
        [Fact]
        public void WaveTriggerCondition_Unparsable_ReportsExactlyOneExprParsableIssue()
        {
            var rows = "[{\"id\": \"encounter.sample_bad_wave_expr\", \"units\": " + MinimalUnits + ", " +
                "\"waves\": [{\"trigger_condition\": \"(( bad\"}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "expr_parsable");
            Assert.Equal("waves[0].trigger_condition", issue.Field);
        }

        /// <summary>同上，验证 <c>phases[].enter_condition</c> 一侧。</summary>
        [Fact]
        public void PhaseEnterCondition_Unparsable_ReportsExactlyOneExprParsableIssue()
        {
            var rows = "[{\"id\": \"encounter.sample_bad_phase_expr\", \"units\": " + MinimalUnits + ", " +
                "\"phases\": [{\"enter_condition\": \"** not valid **\"}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "expr_parsable");
            Assert.Equal("phases[0].enter_condition", issue.Field);
        }

        [Fact]
        public void WaveSpawnRefs_MalformedIdElement_ReportsFieldTypeAtIndexedPath()
        {
            var rows = "[{\"id\": \"encounter.sample_bad_spawn_ref_shape\", \"units\": " + MinimalUnits + ", " +
                "\"waves\": [{\"trigger_condition\": \"self.is_alive\", \"spawn_refs\": [\"spawn.ok\", \"NOT VALID\"]}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "waves[0].spawn_refs[1]");
        }

        [Fact]
        public void TemplateRef_UnknownCreatureTemplate_ReportsReferenceIntegrity()
        {
            var rows = "[{\"id\": \"encounter.sample_unknown_template\", " +
                "\"units\": [{\"template_ref\": \"creature.does_not_exist\", \"position\": {\"x\": 0, \"y\": 0}}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows, withCreatureTemplate: true);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "units[0].template_ref");
        }

        [Fact]
        public void InitiativeOverride_UnknownPolicy_ReportsFieldType()
        {
            var rows = "[{\"id\": \"encounter.sample_bad_policy\", \"units\": " + MinimalUnits + ", " +
                "\"initiative_override\": {\"policy\": \"not_a_real_policy\"}, " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"self.is_alive\"}]";

            var report = LoadDefRows(rows);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "initiative_override.policy");
        }
    }
}

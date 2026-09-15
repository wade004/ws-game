using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-2（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 1；
    /// 06 第 3.2 节 2026-09-14 修订段；分阶段落地计划第 14 节 N3 任务表第二行）：效果值契约
    /// <c>scaling</c> 列表 + 可选 <c>base_curve_ref</c>，与旧单字段 <c>scaling_stat</c>/
    /// <c>coefficient</c> 的兼容读取路径、1→2 迁移等价性。
    /// </summary>
    public sealed class T_N3_2_SkillScalingListAndBaseCurveTests
    {
        // -----------------------------------------------------------------
        // schema 覆盖：scaling/base_curve_ref 字段登记、skill.base_curve 表、schema_version 1→2
        // -----------------------------------------------------------------

        private static FieldSchema GetEffectParamsField(TableSchema table, string kind)
        {
            var effectsField = table.GetField("effects");
            Assert.NotNull(effectsField);
            var variants = effectsField!.Item!.Variants;
            Assert.NotNull(variants);
            var caseFields = variants!.Cases[kind];
            return caseFields.Single(f => f.Name == "params");
        }

        [Theory]
        [InlineData("school_damage")]
        [InlineData("heal")]
        public void DamageOrHealParams_ScalingAndBaseCurveRef_AreOptionalAndCorrectlyTyped(string kind)
        {
            var paramsField = GetEffectParamsField(SkillSchemas.Def, kind);

            var scaling = paramsField.Fields!.Single(f => f.Name == "scaling");
            Assert.Equal(FieldKind.Array, scaling.Kind);
            Assert.False(scaling.Required);
            var scalingItem = scaling.Item!;
            Assert.Equal(FieldKind.Object, scalingItem.Kind);
            var stat = scalingItem.Fields!.Single(f => f.Name == "stat");
            Assert.Equal(FieldKind.Reference, stat.Kind);
            Assert.True(stat.Required);
            Assert.Equal("stat.definition", stat.ReferenceTable);
            var coefficient = scalingItem.Fields!.Single(f => f.Name == "coefficient");
            Assert.Equal(FieldKind.Number, coefficient.Kind);
            Assert.True(coefficient.Required);

            var baseCurveRef = paramsField.Fields!.Single(f => f.Name == "base_curve_ref");
            Assert.Equal(FieldKind.Reference, baseCurveRef.Kind);
            Assert.False(baseCurveRef.Required);
            Assert.Equal("skill.base_curve", baseCurveRef.ReferenceTable);

            // 旧字段仍登记，读取路径未删除（硬性规则）。
            Assert.NotNull(paramsField.Fields!.SingleOrDefault(f => f.Name == "scaling_stat"));
        }

        [Theory]
        [InlineData("periodic_damage")]
        [InlineData("periodic_heal")]
        public void PeriodicParams_ScalingAndBaseCurveRef_ReuseSameFieldInstances(string kind)
        {
            var damageParamsField = GetEffectParamsField(SkillSchemas.Def, "school_damage");
            var periodicParamsField = GetEffectParamsField(SkillSchemas.AuraDef, kind);

            var damageScaling = damageParamsField.Fields!.Single(f => f.Name == "scaling");
            var periodicScaling = periodicParamsField.Fields!.Single(f => f.Name == "scaling");
            // "登记一次、多处复用"：school_damage/heal 与 periodic_damage/periodic_heal 共用同一份
            // scaling/base_curve_ref FieldSchema 实例（同 EffectsItemSchema 既有惯例）。
            Assert.Same(damageScaling, periodicScaling);

            var damageCurveRef = damageParamsField.Fields!.Single(f => f.Name == "base_curve_ref");
            var periodicCurveRef = periodicParamsField.Fields!.Single(f => f.Name == "base_curve_ref");
            Assert.Same(damageCurveRef, periodicCurveRef);
        }

        [Fact]
        public void SkillDef_And_AuraDef_SchemaVersion_BumpedTo2()
        {
            Assert.Equal(2, SkillSchemas.Def.CurrentSchemaVersion);
            Assert.Equal(2, SkillSchemas.AuraDef.CurrentSchemaVersion);
        }

        [Fact]
        public void BaseCurve_Table_RegistersIdAndBreakpointEntries()
        {
            var idField = SkillSchemas.BaseCurve.GetField("id");
            Assert.NotNull(idField);
            Assert.Equal(FieldKind.Id, idField!.Kind);
            Assert.True(idField.Required);

            var entries = SkillSchemas.BaseCurve.GetField("entries");
            Assert.NotNull(entries);
            Assert.Equal(FieldKind.Array, entries!.Kind);
            Assert.True(entries.Required);
            Assert.NotNull(entries.Curve);
            Assert.Equal(CurveShape.Breakpoints, entries.Curve!.Shape);
            Assert.Equal(CurveAxis.Level, entries.Curve.Axis);
        }

        // -----------------------------------------------------------------
        // 验收标准：多缩放属性求和（两条/三条）、含 base_curve_ref 的求和
        // -----------------------------------------------------------------

        [Fact]
        public void SchoolDamage_ScalingList_TwoStats_SumsContribution()
        {
            var world = new SkillWorldBuilder()
                .Stat("stat.n3_2_power", defaultBase: 5)
                .Stat("stat.n3_2_might", defaultBase: 3)
                .Build();
            var source = new Id("unit.n3_2_source_2");
            var target = new Id("unit.n3_2_target_2");
            world.AddUnit(source);
            world.AddUnit(target);

            var context = new EffectContext(
                source, target, new Id("skill.n3_2_bolt_2"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 0, 0,
                J.O(
                    ("base_value", J.N(10)),
                    ("scaling", J.A(
                        J.O(("stat", J.S("stat.n3_2_power")), ("coefficient", J.N(2))),
                        J.O(("stat", J.S("stat.n3_2_might")), ("coefficient", J.N(1)))))));

            world.Host.EffectSink.ApplyEffect(context);

            // 10 + 2*5 + 1*3 = 23。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(23, world.Combat.ResolveCalls[0].BaseValue);
        }

        [Fact]
        public void SchoolDamage_ScalingList_ThreeStats_SumsContribution()
        {
            var world = new SkillWorldBuilder()
                .Stat("stat.n3_2_power", defaultBase: 5)
                .Stat("stat.n3_2_might", defaultBase: 3)
                .Stat("stat.n3_2_wits", defaultBase: 2)
                .Build();
            var source = new Id("unit.n3_2_source_3");
            var target = new Id("unit.n3_2_target_3");
            world.AddUnit(source);
            world.AddUnit(target);

            var context = new EffectContext(
                source, target, new Id("skill.n3_2_bolt_3"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 0, 0,
                J.O(
                    ("base_value", J.N(1)),
                    ("scaling", J.A(
                        J.O(("stat", J.S("stat.n3_2_power")), ("coefficient", J.N(2))),
                        J.O(("stat", J.S("stat.n3_2_might")), ("coefficient", J.N(1))),
                        J.O(("stat", J.S("stat.n3_2_wits")), ("coefficient", J.N(4)))))));

            world.Host.EffectSink.ApplyEffect(context);

            // 1 + 2*5 + 1*3 + 4*2 = 22。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(22, world.Combat.ResolveCalls[0].BaseValue);
        }

        [Fact]
        public void SchoolDamage_BaseCurveRef_PlusScaling_EvaluatesAtCasterLevel()
        {
            var curve = J.O(
                ("id", J.S("skill.base_curve.n3_2_curve")),
                ("entries", J.A(
                    J.O(("x", J.N(1)), ("y", J.N(10))),
                    J.O(("x", J.N(11)), ("y", J.N(110))))));

            var world = new SkillWorldBuilder()
                .Stat("stat.n3_2_power", defaultBase: 5)
                .BaseCurve(curve)
                .Build();
            var source = new Id("unit.n3_2_source_curve");
            var target = new Id("unit.n3_2_target_curve");
            world.AddUnit(source, level: 5);
            world.AddUnit(target);

            var context = new EffectContext(
                source, target, new Id("skill.n3_2_curve_bolt"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 0, 0,
                J.O(
                    ("base_curve_ref", J.S("skill.base_curve.n3_2_curve")),
                    ("scaling", J.A(
                        J.O(("stat", J.S("stat.n3_2_power")), ("coefficient", J.N(2)))))));

            world.Host.EffectSink.ApplyEffect(context);

            // 曲线在等级 5 处线性插值：10 + (5-1)/(11-1)*(110-10) = 10 + 40 = 50；缩放 2*5 = 10；
            // 合计 60。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(60, world.Combat.ResolveCalls[0].BaseValue);
        }

        [Fact]
        public void SchoolDamage_BaseCurveRef_CasterMissing_FallsBackToLevel1()
        {
            var curve = J.O(
                ("id", J.S("skill.base_curve.n3_2_curve_missing")),
                ("entries", J.A(
                    J.O(("x", J.N(1)), ("y", J.N(10))),
                    J.O(("x", J.N(11)), ("y", J.N(110))))));

            var world = new SkillWorldBuilder().BaseCurve(curve).Build();
            var target = new Id("unit.n3_2_target_missing");
            world.AddUnit(target);
            var missingSource = new Id("unit.n3_2_missing_source"); // 未 AddUnit：模拟来源已销毁。

            var context = new EffectContext(
                missingSource, target, new Id("skill.n3_2_curve_bolt_missing"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 0, 0,
                J.O(("base_curve_ref", J.S("skill.base_curve.n3_2_curve_missing"))));

            world.Host.EffectSink.ApplyEffect(context);

            // 来源不存在时按等级 1 处理（同 Resolver.ResolveEffectiveLevel 判断记录）：曲线在 x=1
            // 处取首断点 y=10。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(10, world.Combat.ResolveCalls[0].BaseValue);
        }

        // -----------------------------------------------------------------
        // 验收标准：旧单字段数据经 1→2 迁移等价（scaling_stat + coefficient → scaling: [{stat, coefficient}]，
        // 结算值逐位一致）
        // -----------------------------------------------------------------

        [Fact]
        public void SkillDef_V1ScalingStatData_MigratesToScalingList_EquivalentValue()
        {
            var effect = J.O(
                ("kind", J.S("school_damage")),
                ("params", J.O(
                    ("base_value", J.N(4)),
                    ("coefficient", J.N(3)),
                    ("school", J.S("skill.school_sample")),
                    ("scaling_stat", J.S("stat.n3_2_v1_power")))));

            var skill = J.O(
                ("id", J.S("skill.n3_2_v1_bolt")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(30)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(true)),
                ("target_shape_ref", J.S("target.chain.n3_2_v1")),
                ("effects", J.A(effect)));

            var world = new SkillWorldBuilder()
                .Stat("stat.n3_2_v1_power", defaultBase: 6)
                .SkillDef(skill)
                .Build();
            var source = new Id("unit.n3_2_v1_source");
            var target = new Id("unit.n3_2_v1_target");
            world.AddUnit(source);
            world.AddUnit(target);

            var record = world.Registry.Get("skill.def", new Id("skill.n3_2_v1_bolt"));
            Assert.NotNull(record);
            var migratedEffect = (JsonObject)record!.GetArray("effects")[0];
            var migratedParams = (JsonObject)migratedEffect["params"];

            // 迁移确实发生：scaling 列表已生成，旧字段（scaling_stat/coefficient）原样保留
            // （硬性规则"禁止删除旧 scaling_stat 读取路径"）。
            Assert.True(migratedParams.ContainsKey("scaling"));
            Assert.True(migratedParams.ContainsKey("scaling_stat"));
            var scalingArray = (JsonArray)migratedParams["scaling"];
            Assert.Single(scalingArray);
            var scalingEntry = (JsonObject)scalingArray[0];
            Assert.Equal("stat.n3_2_v1_power", ((JsonString)scalingEntry["stat"]).Value);
            Assert.Equal(3, ((JsonNumber)scalingEntry["coefficient"]).Value);

            // 用迁移后的 params 重新走一次结算，验证与旧路径手算值逐位一致。
            var context = new EffectContext(
                source, target, new Id("skill.n3_2_v1_bolt"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 0, 0, migratedParams);

            world.Host.EffectSink.ApplyEffect(context);

            // 旧路径手算：base_value(4) + coefficient(3) × stat.n3_2_v1_power(6) = 22。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(22, world.Combat.ResolveCalls[0].BaseValue);
        }

        [Fact]
        public void AuraDef_V1PeriodicScalingStatData_MigratesToScalingList_EquivalentValue()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.n3_2_v1_dot")),
                ("duration", J.N(2)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(2)),
                            ("school", J.S("skill.school_sample")),
                            ("scaling_stat", J.S("stat.n3_2_v1_periodic_power"))))))));

            var world = new SkillWorldBuilder()
                .Stat("stat.n3_2_v1_periodic_power", defaultBase: 5)
                .AuraDef(aura)
                .Build();
            var source = new Id("unit.n3_2_v1_periodic_source");
            var target = new Id("unit.n3_2_v1_periodic_target");
            world.AddUnit(source);
            world.AddUnit(target);

            var record = world.Registry.Get("skill.aura_def", new Id("skill.aura_def.n3_2_v1_dot"));
            Assert.NotNull(record);
            var migratedEffect = (JsonObject)record!.GetArray("effects")[0];
            var migratedParams = (JsonObject)migratedEffect["params"];
            Assert.True(migratedParams.ContainsKey("scaling"));
            Assert.True(migratedParams.ContainsKey("scaling_stat"));

            world.Host.EffectSink.ApplyAura(target, new Id("skill.aura_def.n3_2_v1_dot"), source);
            world.Host.Update(2.0);

            // 旧路径手算：base_value(3) + coefficient(2) × stat.n3_2_v1_periodic_power(5) = 13
            // （同 AuraEffectTests.PeriodicDamage_WithScalingStat_ScalesBySourceStat 既有用例
            // 手算方式，验证迁移后周期路径与非周期路径共用同一条 ApplyDamageOrHeal 结算逻辑）。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(13, world.Combat.ResolveCalls[0].BaseValue);
        }
    }
}

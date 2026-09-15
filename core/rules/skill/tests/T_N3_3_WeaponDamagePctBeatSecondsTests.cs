using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-3（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 1/2/10；
    /// 06 第 3.2/3.10 节 2026-09-14 修订段；分阶段落地计划第 14 节 N3 任务表第三行）：
    /// <c>weapon_damage_pct</c> 效果原语改为"武器秒伤 × 一拍常数 × 百分比"（不再读取
    /// <see cref="IWeaponDamageQuery.GetWeaponBaseDamage"/>），一拍常数来自新表
    /// <c>skill.budget_rule</c>（本任务只登记最小骨架 <c>id</c>/<c>beat_seconds</c>，见
    /// <see cref="SkillSchemas.BudgetRule"/> 类型注释）。
    /// </summary>
    public sealed class T_N3_3_WeaponDamagePctBeatSecondsTests
    {
        /// <summary>固定返回可编程 <see cref="Dps"/> 的假实现——<see cref="GetWeaponBaseDamage"/>
        /// 故意抛异常：验收标准"禁止在效果里读武器单次伤害"要求 <c>weapon_damage_pct</c> 分支不再
        /// 调用它，本假实现把这条硬性规则坐实为一个会失败的测试，而不是只靠代码走读。</summary>
        private sealed class FakeWeaponDamageQuery : IWeaponDamageQuery
        {
            public double Dps { get; set; }

            public double GetWeaponDps(Id unitId) => Dps;

            public double GetWeaponBaseDamage(Id unitId) =>
                throw new InvalidOperationException(
                    "T-N3-3 硬性规则：weapon_damage_pct 分支禁止再读取武器单次伤害" +
                    "（GetWeaponBaseDamage）——若测试执行到这里说明 EffectDispatcher 又调用了" +
                    "本方法，回归了 RC-11/T-N2-6 之前的行为。");
        }

        private static EffectContext WeaponDamagePctContext(Id source, Id target, Id skillId, double pct) =>
            new EffectContext(
                source, target, skillId, EffectKind.WeaponDamagePct,
                new Id("skill.school_sample"), 0, 0,
                J.O(("pct", J.N(pct))));

        // -----------------------------------------------------------------
        // schema 覆盖：skill.budget_rule 最小骨架、SkillOptions.BudgetRuleId 默认值
        // -----------------------------------------------------------------

        [Fact]
        public void BudgetRule_Table_RegistersIdAndBeatSecondsFields()
        {
            var idField = SkillSchemas.BudgetRule.GetField("id");
            Assert.NotNull(idField);
            Assert.Equal(FieldKind.Id, idField!.Kind);
            Assert.True(idField.Required);

            var beatSeconds = SkillSchemas.BudgetRule.GetField("beat_seconds");
            Assert.NotNull(beatSeconds);
            Assert.Equal(FieldKind.Number, beatSeconds!.Kind);
            Assert.False(beatSeconds.Required); // 缺省 1.0，见字段描述与 SkillDefCache.TryGetBeatSeconds。
            Assert.Equal(FieldUnit.Time, beatSeconds.Unit);
            Assert.NotNull(beatSeconds.Range);
            Assert.Equal(0.0, beatSeconds.Range!.Min!.Value, 9);
            Assert.True(beatSeconds.Range.MinExclusive);
        }

        [Fact]
        public void BudgetRule_Table_OnlyHasTwoFields_RestDeferredToTN39()
        {
            // 任务书"最小骨架"约束：本任务只登记 id/beat_seconds，其余字段（施放时间当量规则、
            // 三条溢价/折价曲线、带宽、硬上限、控制类别权重、玩家档/怪物档）留给 T-N3-9。
            Assert.Equal(2, SkillSchemas.BudgetRule.Fields.Count);
        }

        [Fact]
        public void BudgetRule_Table_OwnershipAndTimeScope_Declared()
        {
            Assert.Equal(SchemaLayer.Rules, SkillSchemas.BudgetRule.Layer);
            Assert.Equal("skill", SkillSchemas.BudgetRule.Module);
            Assert.Equal("skill", SkillSchemas.BudgetRule.Domain);
            Assert.Equal(TimeScope.Combat, SkillSchemas.BudgetRule.TimeScope);
            Assert.Equal(1, SkillSchemas.BudgetRule.CurrentSchemaVersion);
        }

        [Fact]
        public void SkillOptions_BudgetRuleId_DefaultsToSkillBudgetRuleDefault()
        {
            var options = new SkillOptions();
            Assert.Equal(new Id("skill.budget_rule.default"), options.BudgetRuleId);
        }

        // -----------------------------------------------------------------
        // 验收标准：GetWeaponBaseDamage 不再被调用；GetWeaponDps × beat_seconds × pct
        // -----------------------------------------------------------------

        [Fact]
        public void WeaponDamagePct_InjectedWeaponDps_NoBudgetRule_BeatDefaultsToOne_MultipliesDpsByPct()
        {
            var weaponQuery = new FakeWeaponDamageQuery { Dps = 100 };
            var world = new SkillWorldBuilder { WeaponDamageQuery = weaponQuery }.Build();
            var source = new Id("unit.n3_3_source_1");
            var target = new Id("unit.n3_3_target_1");
            world.AddUnit(source);
            world.AddUnit(target);

            world.Host.EffectSink.ApplyEffect(
                WeaponDamagePctContext(source, target, new Id("skill.n3_3_hit_1"), pct: 0.75));

            // beat_seconds 缺省 1.0：100 × 1.0 × 0.75 = 75（与改动前 weaponBase × pct 的既有测试
            // 数值逐位一致，因为本仓库缺省一拍常数取 1，不引入未声明 skill.budget_rule 的项目的
            // 结算差异）。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(75.0, world.Combat.ResolveCalls[0].BaseValue, 9);
        }

        [Fact]
        public void WeaponDamagePct_BudgetRuleRegistered_MultipliesDpsByBeatSecondsAndPct()
        {
            var weaponQuery = new FakeWeaponDamageQuery { Dps = 100 };
            var builder = new SkillWorldBuilder { WeaponDamageQuery = weaponQuery };
            builder.Options.BudgetRuleId = new Id("skill.budget_rule.n3_3_custom");
            var world = builder
                .BudgetRule(J.O(
                    ("id", J.S("skill.budget_rule.n3_3_custom")),
                    ("beat_seconds", J.N(2.0))))
                .Build();
            var source = new Id("unit.n3_3_source_2");
            var target = new Id("unit.n3_3_target_2");
            world.AddUnit(source);
            world.AddUnit(target);

            world.Host.EffectSink.ApplyEffect(
                WeaponDamagePctContext(source, target, new Id("skill.n3_3_hit_2"), pct: 0.75));

            // 100 × 2.0 × 0.75 = 150。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(150.0, world.Combat.ResolveCalls[0].BaseValue, 9);
        }

        [Fact]
        public void WeaponDamagePct_DpsDoubles_DamageScalesProportionally()
        {
            // "秒伤曲线值变化时伤害等比变化"验收标准：同一 pct/beat，秒伤翻倍则结果翻倍。
            var lowDpsQuery = new FakeWeaponDamageQuery { Dps = 40 };
            var highDpsQuery = new FakeWeaponDamageQuery { Dps = 80 };
            var lowWorld = new SkillWorldBuilder { WeaponDamageQuery = lowDpsQuery }.Build();
            var highWorld = new SkillWorldBuilder { WeaponDamageQuery = highDpsQuery }.Build();
            var source = new Id("unit.n3_3_source_scale");
            var target = new Id("unit.n3_3_target_scale");
            lowWorld.AddUnit(source);
            lowWorld.AddUnit(target);
            highWorld.AddUnit(source);
            highWorld.AddUnit(target);

            lowWorld.Host.EffectSink.ApplyEffect(
                WeaponDamagePctContext(source, target, new Id("skill.n3_3_hit_scale"), pct: 0.5));
            highWorld.Host.EffectSink.ApplyEffect(
                WeaponDamagePctContext(source, target, new Id("skill.n3_3_hit_scale"), pct: 0.5));

            var low = lowWorld.Combat.ResolveCalls[0].BaseValue;
            var high = highWorld.Combat.ResolveCalls[0].BaseValue;
            Assert.Equal(20.0, low, 9); // 40 × 1.0 × 0.5
            Assert.Equal(40.0, high, 9); // 80 × 1.0 × 0.5
            Assert.Equal(2.0, high / low, 9);
        }

        [Fact]
        public void WeaponDamagePct_NeverCallsGetWeaponBaseDamage()
        {
            // FakeWeaponDamageQuery.GetWeaponBaseDamage 抛异常——本测试不 catch，若
            // EffectDispatcher 的 WeaponDamagePct 分支调用了它，测试直接失败于异常，坐实硬性规则
            // "禁止在效果里读武器单次伤害"。
            var weaponQuery = new FakeWeaponDamageQuery { Dps = 10 };
            var world = new SkillWorldBuilder { WeaponDamageQuery = weaponQuery }.Build();
            var source = new Id("unit.n3_3_source_noold");
            var target = new Id("unit.n3_3_target_noold");
            world.AddUnit(source);
            world.AddUnit(target);

            var result = world.Host.EffectSink.ApplyEffect(
                WeaponDamagePctContext(source, target, new Id("skill.n3_3_hit_noold"), pct: 1.0));

            Assert.False(result.Immune);
            Assert.Single(world.Combat.ResolveCalls);
        }

        [Fact]
        public void WeaponDamagePct_BudgetRuleIdPointsToMissingRecord_WarnsAndDefaultsToOne()
        {
            var weaponQuery = new FakeWeaponDamageQuery { Dps = 50 };
            var world = new SkillWorldBuilder { WeaponDamageQuery = weaponQuery }
                .BudgetRule(J.O( // 注册了表，但 id 与 SkillOptions.BudgetRuleId 缺省值不匹配。
                    ("id", J.S("skill.budget_rule.n3_3_unrelated")),
                    ("beat_seconds", J.N(5.0))))
                .Build();
            var source = new Id("unit.n3_3_source_missing");
            var target = new Id("unit.n3_3_target_missing");
            world.AddUnit(source);
            world.AddUnit(target);

            world.Host.EffectSink.ApplyEffect(
                WeaponDamagePctContext(source, target, new Id("skill.n3_3_hit_missing"), pct: 1.0));

            // 50 × 1.0（缺省，非 5.0） × 1.0 = 50；且诊断记了一条警告（不阻断结算）。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(50.0, world.Combat.ResolveCalls[0].BaseValue, 9);
            Assert.Contains(world.Diagnostics.Warnings, w => w.Contains("skill.budget_rule"));
        }
    }
}

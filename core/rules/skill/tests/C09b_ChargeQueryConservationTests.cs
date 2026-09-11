using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Common;
using Core.Rules.Skill;
using Tests.Rules.Integration;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// P2 根治验收（消费方反馈 2026-09-11"只读就绪查询影响后续充能状态"，见
    /// architecture/落地计划/消费方反馈-2026-09-11-充能查询副作用.md；命名前缀 C09b 延续
    /// <see cref="C09_SkillReadinessTests"/> 同一批消费方反馈的验收惯例，本文件专门覆盖该反馈的
    /// 后续 P2 修复——查询纯化 + 充能上限变化守恒规则）。
    /// <para>
    /// 与 <see cref="CooldownTrackerReadOnlyQueryTests"/>（裸 <see cref="CooldownTracker"/> 单元测试，
    /// 验证 <see cref="CooldownTracker.TrackedChargeKeys"/> 不变式）不同，本文件经真实 <see
    /// cref="RulesAssembly"/> 装配根 + 真实 <c>charges</c> 维度 <see cref="SpellModResolver"/> 验证
    /// 端到端行为——A/B 对照（查询 0/1/多次）快照与随后连续施法成功次数必须一致，覆盖消费方反馈"回归
    /// 验收"一节列出的全部维度：充能上限提高、充能上限降低（含降低到低于当前、降低到满充能清零恢复
    /// 窗口）、零充能、部分充能恢复中、查询期间不推进时间。
    /// </para>
    /// </summary>
    public sealed class C09b_ChargeQueryConservationTests
    {
        private static readonly Id MapId = new Id("map.c09b_conservation_test");
        private static readonly Id CasterA = new Id("unit.c09b_caster_a");
        private static readonly Id CasterB = new Id("unit.c09b_caster_b");
        private static readonly Id Faction = new Id("fac.c09b_sample");
        private static readonly Id ClassId = new Id("arch.class.c09b_sample");
        private static readonly Id StatId = new Id("stat.c09b_sample_primary");
        private static readonly Id SelfChain = new Id("target.chain.c09b_self_only");
        private static readonly Id PowerTypeId = new Id("arch.power.c09b_sample");

        // -----------------------------------------------------------------
        // JSON 夹具构造帮助方法（与 C09_SkillReadinessTests 同款惯例）。
        // -----------------------------------------------------------------

        private static JsonObject ChargeSkillDef(string id, int max, double rechargeTime) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_c09b_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("cooldown_duration", J.N(0)),
            ("target_shape_ref", J.S(SelfChain.Value)),
            ("charges", J.O(("max", J.N(max)), ("recharge_time", J.N(rechargeTime)))),
            ("effects", J.A()));

        private static JsonObject SpellMod(string id, string op, double value) => J.O(
            ("id", J.S(id)),
            ("target_dimension", J.S("charges")),
            ("op", J.S(op)),
            ("value", J.N(value)));

        private static JsonObject AuraWithMod(string id, string spellModRef) => J.O(
            ("id", J.S(id)),
            ("duration", J.N(600)),
            ("effects", J.A(
                J.O(("kind", J.S("spell_mod")), ("params", J.O(("spell_mod_ref", J.S(spellModRef))))))));

        private static string TableJson(string name, IReadOnlyList<JsonObject> rows) => JsonWriter.Write(J.O(
            ("table", J.S(name)),
            ("schema_version", J.N(1)),
            ("rows", new JsonArray(rows.Cast<JsonValue>()))));

        private static string EmptyTableJson(string name) => TableJson(name, Array.Empty<JsonObject>());

        /// <summary>装配一份真实 <see cref="RulesAssembly"/>，注册两个独立单位（<see cref="CasterA"/>/
        /// <see cref="CasterB"/>）——A/B 对照场景各自使用一个单位、共享同一份技能/光环定义与同一个
        /// <see cref="RulesAssembly"/> 实例，避免"两个进程各自装配一次"这类间接差异，只让"是否查询过"
        /// 这一个变量不同（与消费方反馈复现 probe 的独立进程 A/B 相比，本文件同实例内双主角的写法更
        /// 直接地证明差异只来自查询本身，见类型文档）。</summary>
        private static RulesAssembly BuildAssembly(
            IReadOnlyList<JsonObject> skillDefs,
            IReadOnlyList<JsonObject>? spellModDefs = null,
            IReadOnlyList<JsonObject>? auraDefs = null)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", TableJson("stat.definition", new[]
                {
                    J.O(
                        ("id", J.S(StatId.Value)),
                        ("name_key", J.S("l10n.stat.c09b_sample_primary.name")),
                        ("group", J.S("primary")),
                        ("default_base", J.N(0))),
                }))
                .Add("arch.power_type", TableJson("arch.power_type", new[]
                {
                    J.O(
                        ("id", J.S(PowerTypeId.Value)),
                        ("name_key", J.S("l10n.power.c09b_sample.name")),
                        ("max_source", J.O(("kind", J.S("fixed")), ("value", J.N(100)))),
                        ("start_full", J.B(true)),
                        ("allow_overflow", J.B(false))),
                }))
                .Add("arch.class", TableJson("arch.class", new[]
                {
                    J.O(
                        ("id", J.S(ClassId.Value)),
                        ("name_key", J.S("l10n.arch.class.c09b_sample.name")),
                        ("primary_stat", J.S(StatId.Value)),
                        ("base_stats", J.O()),
                        ("power_types", J.Ids(PowerTypeId.Value))),
                }))
                .Add("target.chain_def", TableJson("target.chain_def", new[]
                {
                    J.O(("id", J.S(SelfChain.Value)), ("source", J.S("self"))),
                }))
                .Add("combat.hit_table_config", EmptyTableJson("combat.hit_table_config"))
                .Add("combat.resist_curve", EmptyTableJson("combat.resist_curve"))
                .Add("skill.def", TableJson("skill.def", skillDefs));

            if (spellModDefs != null && spellModDefs.Count > 0)
            {
                source.Add("skill.spell_mod_def", TableJson("skill.spell_mod_def", spellModDefs));
            }

            if (auraDefs != null && auraDefs.Count > 0)
            {
                source.Add("skill.aura_def", TableJson("skill.aura_def", auraDefs));
            }

            var bus = FightWorldBuilder.BuildEventBus(out _);
            var registry = new DataRegistry(source, bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);

            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "C09b 测试夹具数据未通过校验：\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            }

            var rng = new RngHost(20260911UL);
            var world = new WorldSim(bus);
            world.AddEntity(new TestUnit(CasterA, MapId, Faction) { Position = new Vec2(0, 0) });
            world.AddEntity(new TestUnit(CasterB, MapId, Faction) { Position = new Vec2(1, 0) });

            var units = new WorldUnitAccess(world);
            var spatial = new StubSpatialQuery();

            var rules = new RulesAssembly(bus, registry, rng, units, spatial, world);
            rules.RegisterUnit(CasterA, ClassId, raceId: null, level: 1);
            rules.RegisterUnit(CasterB, ClassId, raceId: null, level: 1);
            return rules;
        }

        private static int CastUntilFailure(RulesAssembly rules, Id caster, Id skill, int max, out CastResult last)
        {
            var count = 0;
            last = CastResult.Fail(CastFailureReason.UnknownSkill);
            for (var i = 0; i < max; i++)
            {
                last = rules.Skill.CastSkill(caster, skill, Array.Empty<Id>());
                if (!last.Success)
                {
                    break;
                }

                count++;
            }

            return count;
        }

        // -----------------------------------------------------------------
        // 1) 消费方反馈原文场景：上限提高（2→3），A 先查询、B 不查询，快照与连续施法成功次数一致。
        // -----------------------------------------------------------------
        [Fact]
        public void MaxIncrease_QueryBeforeVsWithoutQuery_SnapshotAndCastCountIdentical()
        {
            var skill = new Id("skill.c09b_max_increase");
            var aura = new Id("skill.aura_def.c09b_max_increase");
            var mod = SpellMod("skill.spell_mod_def.c09b_max_increase", "flat", 1);
            var rules = BuildAssembly(
                new[] { ChargeSkillDef(skill.Value, max: 2, rechargeTime: 100) },
                spellModDefs: new[] { mod },
                auraDefs: new[] { AuraWithMod(aura.Value, "skill.spell_mod_def.c09b_max_increase") });

            // A：先查询一次（消费方反馈复现的触发点），此时上限仍是 2。
            var initialA = rules.Skill.GetSkillReadiness(CasterA, skill);
            Assert.Equal(2, initialA.MaxCharges);
            Assert.Equal(2, initialA.CurrentCharges);

            // 上限光环对两个单位同时生效（2→3）。
            rules.Skill.EffectSink.ApplyAura(CasterA, aura, CasterA);
            rules.Skill.EffectSink.ApplyAura(CasterB, aura, CasterB);

            // A 再查询一次；B 完全不查询——直接进入连续施法。
            var postAuraA = rules.Skill.GetSkillReadiness(CasterA, skill);
            var postAuraB = rules.Skill.GetSkillReadiness(CasterB, skill);

            Assert.Equal(3, postAuraA.MaxCharges);
            Assert.Equal(3, postAuraB.MaxCharges);
            // 核心断言：查没查询过不应改变当前充能快照——这正是消费方反馈复现的可观察差异所在字段。
            Assert.Equal(postAuraB.CurrentCharges, postAuraA.CurrentCharges);
            Assert.Equal(3, postAuraA.CurrentCharges);

            var castsA = CastUntilFailure(rules, CasterA, skill, 5, out var lastA);
            var castsB = CastUntilFailure(rules, CasterB, skill, 5, out var lastB);

            // 核心断言：连续施法成功次数必须一致（修复前 A=2、B=3，见消费方反馈原文表格）。
            Assert.Equal(castsB, castsA);
            Assert.Equal(3, castsA);
            Assert.Equal(CastFailureReason.NoCharges, lastA.Reason);
            Assert.Equal(CastFailureReason.NoCharges, lastB.Reason);
        }

        // -----------------------------------------------------------------
        // 2) 上限降低到低于当前：满充能后降低上限，查询是否发生过不影响夹取结果与随后施法次数。
        // -----------------------------------------------------------------
        [Fact]
        public void MaxDecrease_BelowCurrentCharges_QueryBeforeVsWithoutQuery_ClampAndCastCountIdentical()
        {
            var skill = new Id("skill.c09b_max_decrease");
            var aura = new Id("skill.aura_def.c09b_max_decrease");
            var mod = SpellMod("skill.spell_mod_def.c09b_max_decrease", "flat", -2);
            var rules = BuildAssembly(
                new[] { ChargeSkillDef(skill.Value, max: 3, rechargeTime: 100) },
                spellModDefs: new[] { mod },
                auraDefs: new[] { AuraWithMod(aura.Value, "skill.spell_mod_def.c09b_max_decrease") });

            // A：满充能状态下先查询一次（上限仍是 3）。
            var initialA = rules.Skill.GetSkillReadiness(CasterA, skill);
            Assert.Equal(3, initialA.CurrentCharges);

            // 降低上限的光环对两个单位同时生效（3→1）。
            rules.Skill.EffectSink.ApplyAura(CasterA, aura, CasterA);
            rules.Skill.EffectSink.ApplyAura(CasterB, aura, CasterB);

            var postAuraA = rules.Skill.GetSkillReadiness(CasterA, skill);
            var postAuraB = rules.Skill.GetSkillReadiness(CasterB, skill); // B 从未查询过上限降低前的状态。

            Assert.Equal(1, postAuraA.MaxCharges);
            Assert.Equal(1, postAuraB.MaxCharges);
            Assert.Equal(1, postAuraA.CurrentCharges); // 夹取到新上限。
            Assert.Equal(postAuraB.CurrentCharges, postAuraA.CurrentCharges);
            // 降低后恰好满充能（1/1）：应清零恢复窗口，即便消耗充能时确实起过一个恢复窗口。
            Assert.Equal(0.0, postAuraA.NextChargeRemaining);
            Assert.Equal(postAuraB.NextChargeRemaining, postAuraA.NextChargeRemaining);

            var castsA = CastUntilFailure(rules, CasterA, skill, 5, out _);
            var castsB = CastUntilFailure(rules, CasterB, skill, 5, out _);
            Assert.Equal(castsB, castsA);
            Assert.Equal(1, castsA);
        }

        // -----------------------------------------------------------------
        // 3) 部分充能恢复中叠加上限提高：恢复进度不受影响，当前充能数同步 +Δ，A/B 一致。
        // -----------------------------------------------------------------
        [Fact]
        public void PartialRecovery_MaxIncrease_QueryDuringRecoveryDoesNotChangeOutcome()
        {
            var skill = new Id("skill.c09b_partial_increase");
            var aura = new Id("skill.aura_def.c09b_partial_increase");
            var mod = SpellMod("skill.spell_mod_def.c09b_partial_increase", "flat", 1);
            var rules = BuildAssembly(
                new[] { ChargeSkillDef(skill.Value, max: 2, rechargeTime: 6) },
                spellModDefs: new[] { mod },
                auraDefs: new[] { AuraWithMod(aura.Value, "skill.spell_mod_def.c09b_partial_increase") });

            // 两个单位各自消耗一次，进入"部分充能恢复中"（Current=1，NextChargeRemaining=6）。
            Assert.True(rules.Skill.CastSkill(CasterA, skill, Array.Empty<Id>()).Success);
            Assert.True(rules.Skill.CastSkill(CasterB, skill, Array.Empty<Id>()).Success);

            // A 在上限提高前先查询（读取恢复中的状态）；B 不查询。
            var beforeA = rules.Skill.GetSkillReadiness(CasterA, skill);
            Assert.Equal(1, beforeA.CurrentCharges);
            Assert.Equal(6.0, beforeA.NextChargeRemaining);

            // 上限提高（2→3）对两者同时生效。
            rules.Skill.EffectSink.ApplyAura(CasterA, aura, CasterA);
            rules.Skill.EffectSink.ApplyAura(CasterB, aura, CasterB);

            var afterA = rules.Skill.GetSkillReadiness(CasterA, skill);
            var afterB = rules.Skill.GetSkillReadiness(CasterB, skill);

            Assert.Equal(3, afterA.MaxCharges);
            Assert.Equal(2, afterA.CurrentCharges); // 1 + Δ(1) = 2。
            Assert.Equal(afterB.CurrentCharges, afterA.CurrentCharges);
            Assert.Equal(6.0, afterA.NextChargeRemaining); // 恢复窗口本身不受影响，不重置。
            Assert.Equal(afterB.NextChargeRemaining, afterA.NextChargeRemaining);

            // 推进满 6 秒，两者都应恢复满充能（3），随后可连续施放 3 次直至 NoCharges。
            rules.Skill.Update(6.0);
            var castsA = CastUntilFailure(rules, CasterA, skill, 5, out _);
            var castsB = CastUntilFailure(rules, CasterB, skill, 5, out _);
            Assert.Equal(castsB, castsA);
            Assert.Equal(3, castsA);
        }

        // -----------------------------------------------------------------
        // 4) 零充能叠加上限提高：耗尽后阻塞，随后上限提高应立即多出一次可用，A/B 一致。
        // -----------------------------------------------------------------
        [Fact]
        public void ZeroCharges_MaxIncrease_QueryBeforeVsWithoutQuery_ImmediatelyUsableAndIdentical()
        {
            var skill = new Id("skill.c09b_zero_increase");
            var aura = new Id("skill.aura_def.c09b_zero_increase");
            var mod = SpellMod("skill.spell_mod_def.c09b_zero_increase", "flat", 1);
            var rules = BuildAssembly(
                new[] { ChargeSkillDef(skill.Value, max: 1, rechargeTime: 1000) },
                spellModDefs: new[] { mod },
                auraDefs: new[] { AuraWithMod(aura.Value, "skill.spell_mod_def.c09b_zero_increase") });

            // 两个单位各自耗尽唯一一次充能。
            Assert.True(rules.Skill.CastSkill(CasterA, skill, Array.Empty<Id>()).Success);
            Assert.True(rules.Skill.CastSkill(CasterB, skill, Array.Empty<Id>()).Success);

            // A 在零充能状态下先查询一次。
            var zeroA = rules.Skill.GetSkillReadiness(CasterA, skill);
            Assert.False(zeroA.IsReady);
            Assert.Equal(SkillReadinessBlockers.NoCharges, zeroA.BlockingSources);
            Assert.Equal(0, zeroA.CurrentCharges);

            var blockedA = rules.Skill.CastSkill(CasterA, skill, Array.Empty<Id>());
            var blockedB = rules.Skill.CastSkill(CasterB, skill, Array.Empty<Id>());
            Assert.False(blockedA.Success);
            Assert.False(blockedB.Success);

            // 上限提高（1→2）对两者同时生效——Δ=1 应让零充能立即变为 1 可用。
            rules.Skill.EffectSink.ApplyAura(CasterA, aura, CasterA);
            rules.Skill.EffectSink.ApplyAura(CasterB, aura, CasterB);

            var afterA = rules.Skill.GetSkillReadiness(CasterA, skill);
            var afterB = rules.Skill.GetSkillReadiness(CasterB, skill);
            Assert.True(afterA.IsReady);
            Assert.Equal(1, afterA.CurrentCharges);
            Assert.Equal(afterB.CurrentCharges, afterA.CurrentCharges);

            var castsA = CastUntilFailure(rules, CasterA, skill, 5, out var lastA);
            var castsB = CastUntilFailure(rules, CasterB, skill, 5, out var lastB);
            Assert.Equal(castsB, castsA);
            Assert.Equal(1, castsA);
            Assert.Equal(CastFailureReason.NoCharges, lastA.Reason);
            Assert.Equal(CastFailureReason.NoCharges, lastB.Reason);
        }

        // -----------------------------------------------------------------
        // 5) 查询期间不推进时间：暂停区间内反复查询（0/1/多次）不改变快照，也不影响随后施法结果；
        //    与是否发生过上限变化无关。
        // -----------------------------------------------------------------
        [Fact]
        public void RepeatedQueriesDuringPause_DoNotAdvanceTimeOrChangeSnapshot_AcrossMaxChange()
        {
            var skill = new Id("skill.c09b_pause_queries");
            var aura = new Id("skill.aura_def.c09b_pause_queries");
            var mod = SpellMod("skill.spell_mod_def.c09b_pause_queries", "flat", 1);
            var rules = BuildAssembly(
                new[] { ChargeSkillDef(skill.Value, max: 2, rechargeTime: 6) },
                spellModDefs: new[] { mod },
                auraDefs: new[] { AuraWithMod(aura.Value, "skill.spell_mod_def.c09b_pause_queries") });

            Assert.True(rules.Skill.CastSkill(CasterA, skill, Array.Empty<Id>()).Success);

            // 暂停区间：不调用 Update，反复查询（0/1/多次）应逐字段完全相同。
            var q0 = rules.Skill.GetSkillReadiness(CasterA, skill);
            for (var i = 0; i < 5; i++)
            {
                var qi = rules.Skill.GetSkillReadiness(CasterA, skill);
                Assert.Equal(q0.CurrentCharges, qi.CurrentCharges);
                Assert.Equal(q0.NextChargeRemaining, qi.NextChargeRemaining);
                Assert.Equal(q0.MaxCharges, qi.MaxCharges);
                Assert.Equal(q0.IsReady, qi.IsReady);
            }

            // 暂停区间内应用上限提高光环（不涉及时间推进，仅状态变化）：反复查询仍应逐次一致。
            rules.Skill.EffectSink.ApplyAura(CasterA, aura, CasterA);
            var afterAura0 = rules.Skill.GetSkillReadiness(CasterA, skill);
            for (var i = 0; i < 5; i++)
            {
                var qi = rules.Skill.GetSkillReadiness(CasterA, skill);
                Assert.Equal(afterAura0.CurrentCharges, qi.CurrentCharges);
                Assert.Equal(afterAura0.NextChargeRemaining, qi.NextChargeRemaining);
                Assert.Equal(afterAura0.MaxCharges, qi.MaxCharges);
            }

            Assert.Equal(3, afterAura0.MaxCharges);
            Assert.Equal(2, afterAura0.CurrentCharges); // 1 + Δ(1)。
            Assert.Equal(6.0, afterAura0.NextChargeRemaining); // 未推进时间，恢复窗口原样保留。

            // 恢复：推进满 6 秒后应能施放到底，验证暂停期间的多次查询没有产生任何隐性推进。
            rules.Skill.Update(6.0);
            var readyAfterUpdate = rules.Skill.GetSkillReadiness(CasterA, skill);
            Assert.True(readyAfterUpdate.IsReady);
            Assert.Equal(3, readyAfterUpdate.CurrentCharges);
        }
    }
}

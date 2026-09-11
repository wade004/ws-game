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
    /// 消费方反馈 2026-09-11"冷却充能与公共冷却缺少统一只读查询接口"验收（见
    /// architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md，消费方反馈原文
    /// <c>ws-game-wow/docs/框架反馈/冷却充能与公共冷却缺少统一只读查询接口.md</c>，命名前缀 C09
    /// 沿用本模块既有验收测试惯例——见 <c>C07_MultipleProcTriggersTests</c>）。
    /// <para>
    /// 与本模块其余测试（经 <see cref="SkillWorldBuilder"/> 直接构造裸 <see cref="SkillHost"/>）
    /// 不同，本文件按任务书要求经真实 <see cref="RulesAssembly"/> 装配根验证——<see
    /// cref="ISkillHost.GetSkillReadiness"/> 的生产实现挂在 <see cref="RulesAssembly.Skill"/> 上，
    /// 只有经这条真实装配路径（而不是测试专用的最小 <c>SkillHost</c> 直接构造）才能确认它与
    /// <see cref="RulesAssembly"/> 其余组件（<c>SpellModResolver</c>/<c>AuraHost</c>/<c>Factions</c>
    /// 等经生产构造顺序真正接线完毕）协同的读数是对的。<see cref="BuildAssembly"/> 只保留
    /// <see cref="RulesAssembly"/> 装配 + 本次验收关心的技能/冷却/充能/GCD 路径严格必需的最小数据
    /// 集：<c>combat.hit_table_config</c>/<c>combat.resist_curve</c>（<c>CombatDataLoader.RequireTable</c>
    /// 硬性要求两张表本身存在，即便零行）、<c>stat.definition</c> 一条记录（<c>arch.class.primary_stat</c>
    /// 外键要求）、<c>target.chain_def</c> 一条 <c>source: self</c> 记录（<see
    /// cref="BuiltinTargetStrategies.SelfStrategy"/> 不需要任何空间/阵营信息即可解析出施法者自身，
    /// 避免为了跑通目标解析而搭建战斗/阵营场景，见该策略实现判断记录），以及各用例自己的
    /// <c>skill.def</c>/<c>skill.spell_mod_def</c>/<c>skill.aura_def</c>——不搭建 <c>ai.*</c>/
    /// <c>fac.*</c> 等与冷却/充能/GCD 无关的表：<see cref="DataRegistry.LoadAllCore"/> 只加载数据源
    /// 里实际提供的表，未提供的表 <c>GetAll</c> 返回空集合，不阻碍装配（见该方法判断记录"按表名分组
    /// ……只加载数据源里实际存在的表"）。
    /// </para>
    /// <para>
    /// 六项验收场景对应 <see cref="ISkillHost.GetSkillReadiness"/>/<see cref="SkillReadiness"/> 判断
    /// 记录逐条覆盖：零充能（<see cref="ZeroCharges_BlocksReadiness_MatchesSubsequentCastFailure"/>）、
    /// 部分充能恢复中（<see cref="PartialChargeRecovery_CurrentChargesAndNextChargeRemaining_BothCorrect"/>）、
    /// 仅公共冷却阻塞（<see cref="OnlyGlobalCooldownBlocks_SkillItselfReady_MatchesGcdActiveCastFailure"/>）、
    /// 冷却修饰后 <c>EffectiveCooldownDuration</c> 与剩余一致（<see
    /// cref="ModifiedCooldown_EffectiveDuration_MatchesRemainingRightAfterCast"/>）、暂停（未推进时间）
    /// 与恢复（推进时间）后快照正确（<see
    /// cref="PausedInterval_RepeatedQueries_YieldIdenticalSnapshot_ThenResumeReflectsElapsedTime"/>）、
    /// 分类冷却（<see cref="CategoryCooldown_BlocksSiblingSkill_EvenThoughItsOwnCooldownIsZero"/>）——
    /// 每项都额外断言"查询本身只读"（同一状态下反复查询/<c>GetCooldown</c> 结果不变）与"快照与随后
    /// 一次 <c>CastSkill</c> 的裁决一致"两条贯穿全部用例的验收标准。
    /// </para>
    /// </summary>
    public sealed class C09_SkillReadinessTests
    {
        private static readonly Id MapId = new Id("map.c09_readiness_test");
        private static readonly Id Caster = new Id("unit.c09_caster");
        private static readonly Id Faction = new Id("fac.c09_sample");
        private static readonly Id ClassId = new Id("arch.class.c09_sample");
        private static readonly Id StatId = new Id("stat.c09_sample_primary");
        private static readonly Id SelfChain = new Id("target.chain.c09_self_only");
        private static readonly Id PowerTypeId = new Id("arch.power.c09_sample");

        // -----------------------------------------------------------------
        // JSON 夹具构造帮助方法
        // -----------------------------------------------------------------

        private static JsonObject SkillDef(
            string id,
            double cooldownDuration = 0,
            string? cooldownCategory = null,
            (int max, double rechargeTime)? charges = null,
            bool respectsGcd = false)
        {
            var fields = new List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_c09_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(respectsGcd)),
                ("cooldown_duration", J.N(cooldownDuration)),
                ("target_shape_ref", J.S(SelfChain.Value)),
                ("effects", J.A()),
            };

            if (cooldownCategory != null)
            {
                fields.Add(("cooldown_category", J.S(cooldownCategory)));
            }

            if (charges.HasValue)
            {
                fields.Add(("charges", J.O(
                    ("max", J.N(charges.Value.max)),
                    ("recharge_time", J.N(charges.Value.rechargeTime)))));
            }

            return J.O(fields.ToArray());
        }

        private static JsonObject SpellMod(string id, string dimension, string op, double value) => J.O(
            ("id", J.S(id)),
            ("target_dimension", J.S(dimension)),
            ("op", J.S(op)),
            ("value", J.N(value)));

        private static JsonObject AuraWithMod(string id, string spellModRef) => J.O(
            ("id", J.S(id)),
            ("duration", J.N(60)),
            ("effects", J.A(
                J.O(("kind", J.S("spell_mod")), ("params", J.O(("spell_mod_ref", J.S(spellModRef))))))));

        private static string TableJson(string name, IReadOnlyList<JsonObject> rows) => JsonWriter.Write(J.O(
            ("table", J.S(name)),
            ("schema_version", J.N(1)),
            ("rows", new JsonArray(rows.Cast<JsonValue>()))));

        private static string EmptyTableJson(string name) => TableJson(name, Array.Empty<JsonObject>());

        /// <summary>装配一份真实的 <see cref="RulesAssembly"/>（判断记录见类型注释）。</summary>
        private static RulesAssembly BuildAssembly(
            IReadOnlyList<JsonObject> skillDefs,
            IReadOnlyList<JsonObject>? spellModDefs = null,
            IReadOnlyList<JsonObject>? auraDefs = null,
            SkillOptions? skillOptions = null)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", TableJson("stat.definition", new[]
                {
                    J.O(
                        ("id", J.S(StatId.Value)),
                        ("name_key", J.S("l10n.stat.c09_sample_primary.name")),
                        ("group", J.S("primary")),
                        ("default_base", J.N(0))),
                }))
                .Add("arch.power_type", TableJson("arch.power_type", new[]
                {
                    J.O(
                        ("id", J.S(PowerTypeId.Value)),
                        ("name_key", J.S("l10n.power.c09_sample.name")),
                        ("max_source", J.O(("kind", J.S("fixed")), ("value", J.N(100)))),
                        ("start_full", J.B(true)),
                        ("allow_overflow", J.B(false))),
                }))
                .Add("arch.class", TableJson("arch.class", new[]
                {
                    J.O(
                        ("id", J.S(ClassId.Value)),
                        ("name_key", J.S("l10n.arch.class.c09_sample.name")),
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
                    "C09 测试夹具数据未通过校验：\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            }

            var rng = new RngHost(20260911UL);
            var world = new WorldSim(bus);
            world.AddEntity(new TestUnit(Caster, MapId, Faction) { Position = new Vec2(0, 0) });

            var units = new WorldUnitAccess(world);
            var spatial = new StubSpatialQuery();

            var rules = new RulesAssembly(bus, registry, rng, units, spatial, world, skillOptions: skillOptions);
            rules.RegisterUnit(Caster, ClassId, raceId: null, level: 1);
            return rules;
        }

        /// <summary>字段级比对：<see cref="SkillReadiness"/> 全部属性只读，用于验证"暂停"（未推进
        /// 时间）期间反复查询得到逐字段完全相同的快照——查询本身只读，不推进时间、不修改任何状态
        /// （见 <see cref="ISkillHost.GetSkillReadiness"/> 判断记录）。</summary>
        private static void AssertSameSnapshot(SkillReadiness a, SkillReadiness b)
        {
            Assert.Equal(a.SkillId, b.SkillId);
            Assert.Equal(a.IsReady, b.IsReady);
            Assert.Equal(a.BlockingSources, b.BlockingSources);
            Assert.Equal(a.SkillCooldownRemaining, b.SkillCooldownRemaining);
            Assert.Equal(a.CategoryCooldownRemaining?.CategoryId, b.CategoryCooldownRemaining?.CategoryId);
            Assert.Equal(a.CategoryCooldownRemaining?.Remaining, b.CategoryCooldownRemaining?.Remaining);
            Assert.Equal(a.GlobalCooldownRemaining, b.GlobalCooldownRemaining);
            Assert.Equal(a.MaxCharges, b.MaxCharges);
            Assert.Equal(a.CurrentCharges, b.CurrentCharges);
            Assert.Equal(a.NextChargeRemaining, b.NextChargeRemaining);
            Assert.Equal(a.EffectiveCooldownDuration, b.EffectiveCooldownDuration);
        }

        // -----------------------------------------------------------------
        // 1) 零充能：充能耗尽 → NoCharges，与随后 CastSkill 的 NoCharges 裁决一致
        // -----------------------------------------------------------------
        [Fact]
        public void ZeroCharges_BlocksReadiness_MatchesSubsequentCastFailure()
        {
            var skillId = new Id("skill.c09_zero_charges");
            var rules = BuildAssembly(new[] { SkillDef(skillId.Value, charges: (1, 100)) });

            Assert.True(rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>()).Success);

            var readiness = rules.Skill.GetSkillReadiness(Caster, skillId);
            Assert.False(readiness.IsReady);
            Assert.Equal(SkillReadinessBlockers.NoCharges, readiness.BlockingSources);
            Assert.Equal(0, readiness.CurrentCharges);
            Assert.Equal(1, readiness.MaxCharges);
            Assert.Equal(100.0, readiness.NextChargeRemaining);
            Assert.Equal(100.0, readiness.EffectiveCooldownDuration);
            Assert.Null(readiness.SkillCooldownRemaining);
            Assert.Null(readiness.CategoryCooldownRemaining);

            // 查询本身只读：再次查询、以及 GetCooldown 的结果都不变。
            var readinessAgain = rules.Skill.GetSkillReadiness(Caster, skillId);
            AssertSameSnapshot(readiness, readinessAgain);
            Assert.Equal(100.0, rules.Skill.GetCooldown(Caster, skillId));

            var castAfter = rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>());
            Assert.False(castAfter.Success);
            Assert.Equal(CastFailureReason.NoCharges, castAfter.Reason);
        }

        // -----------------------------------------------------------------
        // 2) 部分充能恢复中：CurrentCharges 与 NextChargeRemaining 同时正确，仍就绪（充能数 > 0）
        // -----------------------------------------------------------------
        [Fact]
        public void PartialChargeRecovery_CurrentChargesAndNextChargeRemaining_BothCorrect()
        {
            var skillId = new Id("skill.c09_partial_charges");
            var rules = BuildAssembly(new[] { SkillDef(skillId.Value, charges: (2, 6)) });

            Assert.True(rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>()).Success);

            var readiness = rules.Skill.GetSkillReadiness(Caster, skillId);
            Assert.True(readiness.IsReady); // 还剩 1 充能，不受 NoCharges 阻塞。
            Assert.Equal(SkillReadinessBlockers.None, readiness.BlockingSources);
            Assert.Equal(1, readiness.CurrentCharges);
            Assert.Equal(2, readiness.MaxCharges);
            Assert.Equal(6.0, readiness.NextChargeRemaining);
            Assert.Equal(6.0, readiness.EffectiveCooldownDuration);

            rules.Skill.Update(2.0);
            var afterTwoSeconds = rules.Skill.GetSkillReadiness(Caster, skillId);
            Assert.True(afterTwoSeconds.IsReady);
            Assert.Equal(1, afterTwoSeconds.CurrentCharges); // 还没恢复满，仍是 1。
            Assert.Equal(4.0, afterTwoSeconds.NextChargeRemaining);

            // 查询本身只读：重复查询不变。
            AssertSameSnapshot(afterTwoSeconds, rules.Skill.GetSkillReadiness(Caster, skillId));

            // 随后一次 CastSkill 的裁决一致：仍有 1 充能可用，应当成功。
            Assert.True(rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>()).Success);
        }

        // -----------------------------------------------------------------
        // 3) 仅公共冷却阻塞：技能自身冷却/充能就绪，唯独 GCD 生效
        // -----------------------------------------------------------------
        [Fact]
        public void OnlyGlobalCooldownBlocks_SkillItselfReady_MatchesGcdActiveCastFailure()
        {
            var gcdTrigger = new Id("skill.c09_gcd_trigger");
            var underTest = new Id("skill.c09_gcd_blocked_only");
            var options = new SkillOptions { GcdEnabled = true };
            var rules = BuildAssembly(new[]
            {
                SkillDef(gcdTrigger.Value, respectsGcd: true),
                SkillDef(underTest.Value, respectsGcd: true),
            }, skillOptions: options);

            Assert.True(rules.Skill.CastSkill(Caster, gcdTrigger, Array.Empty<Id>()).Success);
            Assert.False(rules.Skill.IsCasting(Caster)); // cast_time=0，瞬间结算完毕，只是 GCD 生效。

            var readiness = rules.Skill.GetSkillReadiness(Caster, underTest);
            Assert.False(readiness.IsReady);
            Assert.Equal(SkillReadinessBlockers.GlobalCooldown, readiness.BlockingSources);
            Assert.Equal(0.0, readiness.SkillCooldownRemaining); // 自身从未施放过，无冷却。
            Assert.Null(readiness.CategoryCooldownRemaining);
            Assert.True(readiness.GlobalCooldownRemaining > 0);
            Assert.Equal(0.0, readiness.EffectiveCooldownDuration); // cooldown_duration 缺省 0。

            // 查询本身只读：重复查询不变，GetCooldown（不区分冷却/GCD）此时仍是 0——GetCooldown 的
            // 既有口径本就不含 GCD（06 第 7 节"某技能……距下次可用的剩余时间"只覆盖冷却/充能），
            // GetSkillReadiness 补的正是这条 GetCooldown 看不到的 GCD 维度。
            AssertSameSnapshot(readiness, rules.Skill.GetSkillReadiness(Caster, underTest));
            Assert.Equal(0.0, rules.Skill.GetCooldown(Caster, underTest));

            var castAfter = rules.Skill.CastSkill(Caster, underTest, Array.Empty<Id>());
            Assert.False(castAfter.Success);
            Assert.Equal(CastFailureReason.GcdActive, castAfter.Reason);
        }

        // -----------------------------------------------------------------
        // 4) 冷却修饰后 EffectiveCooldownDuration 与刚施放完毕的剩余一致
        // -----------------------------------------------------------------
        [Fact]
        public void ModifiedCooldown_EffectiveDuration_MatchesRemainingRightAfterCast()
        {
            var skillId = new Id("skill.c09_cooldown_mod");
            var auraId = new Id("skill.aura_def.c09_cooldown_flat");
            var spellMod = SpellMod("skill.spell_mod_def.c09_cooldown_flat", "cooldown", "flat", -4);
            var aura = AuraWithMod(auraId.Value, "skill.spell_mod_def.c09_cooldown_flat");
            var rules = BuildAssembly(
                new[] { SkillDef(skillId.Value, cooldownDuration: 10) },
                spellModDefs: new[] { spellMod },
                auraDefs: new[] { aura });

            rules.Skill.EffectSink.ApplyAura(Caster, auraId, Caster);

            // 施放前：EffectiveCooldownDuration 已经反映修饰（10-4=6），即便技能还没进入冷却——
            // 该字段只读取"若此刻施放会进入的完整周期"，不依赖冷却账本已有状态。
            var beforeCast = rules.Skill.GetSkillReadiness(Caster, skillId);
            Assert.True(beforeCast.IsReady);
            Assert.Equal(6.0, beforeCast.EffectiveCooldownDuration);
            Assert.Equal(0.0, beforeCast.SkillCooldownRemaining);

            Assert.True(rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>()).Success);

            var afterCast = rules.Skill.GetSkillReadiness(Caster, skillId);
            Assert.False(afterCast.IsReady);
            Assert.Equal(SkillReadinessBlockers.SkillCooldown, afterCast.BlockingSources);
            Assert.Equal(6.0, afterCast.EffectiveCooldownDuration);
            Assert.Equal(6.0, afterCast.SkillCooldownRemaining); // 刚施放完毕：剩余 == 完整修饰后周期。
            Assert.Equal(afterCast.EffectiveCooldownDuration, afterCast.SkillCooldownRemaining);

            AssertSameSnapshot(afterCast, rules.Skill.GetSkillReadiness(Caster, skillId));

            var castAfter = rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>());
            Assert.False(castAfter.Success);
            Assert.Equal(CastFailureReason.OnCooldown, castAfter.Reason);
        }

        // -----------------------------------------------------------------
        // 5) 暂停与恢复后快照不变：未推进时间期间反复查询逐字段相同；推进时间后正确反映剩余变化
        // -----------------------------------------------------------------
        [Fact]
        public void PausedInterval_RepeatedQueries_YieldIdenticalSnapshot_ThenResumeReflectsElapsedTime()
        {
            var skillId = new Id("skill.c09_pause_resume");
            var rules = BuildAssembly(new[] { SkillDef(skillId.Value, cooldownDuration: 8) });

            Assert.True(rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>()).Success);

            // "暂停"：本段完全不调用 Update，多次查询应逐字段完全相同（只读，不推进时间）。
            var paused1 = rules.Skill.GetSkillReadiness(Caster, skillId);
            var paused2 = rules.Skill.GetSkillReadiness(Caster, skillId);
            var paused3 = rules.Skill.GetSkillReadiness(Caster, skillId);
            AssertSameSnapshot(paused1, paused2);
            AssertSameSnapshot(paused2, paused3);
            Assert.Equal(8.0, paused1.SkillCooldownRemaining);
            Assert.Equal(8.0, paused1.EffectiveCooldownDuration);
            Assert.False(paused1.IsReady);

            // "恢复"：推进 3 秒，快照应正确反映流逝的时间；EffectiveCooldownDuration（完整周期）不变。
            rules.Skill.Update(3.0);
            var resumed = rules.Skill.GetSkillReadiness(Caster, skillId);
            Assert.Equal(5.0, resumed.SkillCooldownRemaining);
            Assert.Equal(8.0, resumed.EffectiveCooldownDuration);
            Assert.False(resumed.IsReady);

            // 再次"暂停"查询：结果与 resumed 完全相同（查询本身不会让状态继续推进）。
            AssertSameSnapshot(resumed, rules.Skill.GetSkillReadiness(Caster, skillId));

            // 推进满剩余 5 秒：应转为就绪，与随后 CastSkill 成功一致。
            rules.Skill.Update(5.0);
            var ready = rules.Skill.GetSkillReadiness(Caster, skillId);
            Assert.True(ready.IsReady);
            Assert.Equal(SkillReadinessBlockers.None, ready.BlockingSources);
            Assert.Equal(0.0, ready.SkillCooldownRemaining);

            Assert.True(rules.Skill.CastSkill(Caster, skillId, Array.Empty<Id>()).Success);
        }

        // -----------------------------------------------------------------
        // 6) 分类冷却：同分类另一技能自身从未施放，仍被分类冷却阻塞
        // -----------------------------------------------------------------
        [Fact]
        public void CategoryCooldown_BlocksSiblingSkill_EvenThoughItsOwnCooldownIsZero()
        {
            const string categoryIdText = "skill.cooldown_category.c09_sample";
            var categoryId = new Id(categoryIdText);
            var skillA = new Id("skill.c09_category_a");
            var skillB = new Id("skill.c09_category_b");
            var rules = BuildAssembly(new[]
            {
                SkillDef(skillA.Value, cooldownDuration: 10, cooldownCategory: categoryIdText),
                SkillDef(skillB.Value, cooldownDuration: 3, cooldownCategory: categoryIdText),
            });

            Assert.True(rules.Skill.CastSkill(Caster, skillA, Array.Empty<Id>()).Success);

            var readinessB = rules.Skill.GetSkillReadiness(Caster, skillB);
            Assert.False(readinessB.IsReady);
            Assert.Equal(SkillReadinessBlockers.CategoryCooldown, readinessB.BlockingSources);
            Assert.Equal(0.0, readinessB.SkillCooldownRemaining); // skillB 自身从未施放。
            Assert.NotNull(readinessB.CategoryCooldownRemaining);
            Assert.Equal(categoryId, readinessB.CategoryCooldownRemaining!.Value.CategoryId);
            Assert.Equal(10.0, readinessB.CategoryCooldownRemaining!.Value.Remaining); // 随 skillA 的时长写入。
            Assert.Equal(3.0, readinessB.EffectiveCooldownDuration); // skillB 自身修饰后周期，与分类剩余无关。

            AssertSameSnapshot(readinessB, rules.Skill.GetSkillReadiness(Caster, skillB));

            var castB = rules.Skill.CastSkill(Caster, skillB, Array.Empty<Id>());
            Assert.False(castB.Success);
            Assert.Equal(CastFailureReason.OnCooldown, castB.Reason);

            // skillA 自身查询：技能冷却与分类冷却两个来源同时命中（都是刚才这次施放写入的同一时长）。
            var readinessA = rules.Skill.GetSkillReadiness(Caster, skillA);
            Assert.Equal(SkillReadinessBlockers.SkillCooldown | SkillReadinessBlockers.CategoryCooldown, readinessA.BlockingSources);
            Assert.Equal(10.0, readinessA.SkillCooldownRemaining);
            Assert.Equal(10.0, readinessA.CategoryCooldownRemaining!.Value.Remaining);
        }
    }
}

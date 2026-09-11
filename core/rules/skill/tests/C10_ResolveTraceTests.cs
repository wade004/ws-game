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
using Core.Rules.Combat;
using Core.Rules.Common;
using Tests.Rules.Integration;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈 2026-09-11"编辑器第 31 条：结算中间步骤经真实施法路径不可观测"验收（见
    /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第31条.md"方案 1"，命名前缀 C10 沿用本模块
    /// 既有验收测试惯例——见 <see cref="C07_MultipleProcTriggersTests"/>/<see cref="C09_SkillReadinessTests"/>）。
    /// <para>
    /// 与 <c>core/rules/combat/tests/C10_ResolveTraceTests.cs</c>（<see
    /// cref="Core.Rules.Combat.Resolver.Resolve"/> 三条返回路径的穷举单元验收，Fake
    /// <c>IUnitAccess</c>/<c>IAuraQuery</c>）不同，本文件按任务书要求经真实 <see
    /// cref="RulesAssembly"/> 装配根 + 真实 <see cref="Core.Rules.Skill.SkillHost.CastSkill"/>
    /// 端到端验证——覆盖任务书列出的三类落地路径各一例：瞬发技能伤害（<c>skill.c10_strike</c>）、
    /// 光环周期伤害（<c>skill.c10_dot_cast</c> 施加 <c>skill.aura_def.c10_dot</c> 后
    /// <c>periodic_damage</c> tick）、Proc 触发的嵌套施法（<c>skill.aura_def.c10_proc</c> 挂载的
    /// <c>proc_trigger</c> 在 <c>combat.damage_dealt</c> 触发后经 <c>CastPipeline.TriggerCast</c>
    /// 再次结算），以及治疗路径（<c>skill.c10_heal</c>，自疗）；并验证"未设置时行为逐字段不变"
    /// "回调抛异常不影响结算与事件"两条跨路径不变量。
    /// </para>
    /// <para>
    /// <see cref="BuildAssembly"/> 搭建两个互相敌对的单位（<see cref="Caster"/>/<see
    /// cref="Target"/>），命中表（<c>combat.hit_table.default</c>，与 <see
    /// cref="CombatOptions.HitTableConfigId"/> 默认值同名，因此无需在 <see cref="CombatOptions"/>
    /// 上覆盖）全部分支禁用——结算不含任何随机波动，<c>FinalAmount</c> 恒等于 <c>base_value</c>，
    /// 断言不需要预先探测 <c>RngHost</c> 输出序列（同 <see cref="FightWorldBuilder"/> 判断记录）。
    /// </para>
    /// </summary>
    public sealed class C10_ResolveTraceTests
    {
        private static readonly Id MapId = new Id("map.c10_resolve_trace_test");
        private static readonly Id Caster = new Id("unit.c10_caster");
        private static readonly Id Target = new Id("unit.c10_target");
        private static readonly Id FactionCaster = new Id("fac.c10_caster");
        private static readonly Id FactionTarget = new Id("fac.c10_target");
        private static readonly Id ClassId = new Id("arch.class.c10_sample");
        private static readonly Id StatId = new Id("stat.c10_sample_primary");
        private static readonly Id SelfChain = new Id("target.chain.c10_self");
        private static readonly Id EnemyChain = new Id("target.chain.c10_nearest_enemy");

        private static readonly Id StrikeSkill = new Id("skill.c10_strike");
        private static readonly Id HealSkill = new Id("skill.c10_heal");
        private static readonly Id DotCastSkill = new Id("skill.c10_dot_cast");
        private static readonly Id ProcResponseSkill = new Id("skill.c10_proc_response");
        private static readonly Id DotAuraId = new Id("skill.aura_def.c10_dot");
        private static readonly Id ProcAuraId = new Id("skill.aura_def.c10_proc");
        private static readonly Id ProcDefId = new Id("skill.proc_def.c10");

        private const double StrikeBaseValue = 50;
        private const double HealBaseValue = 40;
        private const double DotTickBaseValue = 10;
        private const double ProcResponseBaseValue = 5;
        private const double TargetMaxHp = 100000;

        // -----------------------------------------------------------------
        // 世界装配：真实 RulesAssembly + 两个互相敌对的单位
        // -----------------------------------------------------------------

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public RulesAssembly Rules = null!;
            public List<IEvent> Events = null!;

            public IEnumerable<T> Of<T>() where T : IEvent => Events.OfType<T>();
        }

        private static JsonObject SkillDef(string id, string school, string kind, string targetShapeRef, JsonValue effects) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S(school)),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(targetShapeRef)),
                ("effects", effects));

        private static string TableJson(string name, IReadOnlyList<JsonObject> rows) => JsonWriter.Write(J.O(
            ("table", J.S(name)),
            ("schema_version", J.N(1)),
            ("rows", new JsonArray(rows.Cast<JsonValue>()))));

        private static string EmptyTableJson(string name) => TableJson(name, Array.Empty<JsonObject>());

        /// <summary>装配一份真实的 <see cref="RulesAssembly"/>（判断记录见类型注释）；
        /// <paramref name="combatOptions"/> 供各用例注入 <see cref="CombatOptions.ResolveTrace"/>。</summary>
        private static Fixture BuildAssembly(CombatOptions? combatOptions = null)
        {
            var strikeSkill = SkillDef(StrikeSkill.Value, "school.physical", "active", EnemyChain.Value,
                J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(StrikeBaseValue)), ("coefficient", J.N(0)))))));

            var healSkill = SkillDef(HealSkill.Value, "school.physical", "active", SelfChain.Value,
                J.A(J.O(("kind", J.S("heal")), ("params", J.O(("base_value", J.N(HealBaseValue)), ("coefficient", J.N(0)))))));

            var dotCastSkill = SkillDef(DotCastSkill.Value, "school.physical", "active", EnemyChain.Value,
                J.A(J.O(("kind", J.S("apply_aura")), ("params", J.O(("aura_def", J.S(DotAuraId.Value)))))));

            var procResponseSkill = SkillDef(ProcResponseSkill.Value, "school.physical", "active", EnemyChain.Value,
                J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(ProcResponseBaseValue)), ("coefficient", J.N(0)))))));

            var dotAura = J.O(
                ("id", J.S(DotAuraId.Value)),
                ("duration", J.N(10)),
                ("max_stacks", J.N(1)),
                ("effects", J.A(J.O(
                    ("kind", J.S("periodic_damage")),
                    ("params", J.O(
                        ("interval", J.N(1.0)),
                        ("base_value", J.N(DotTickBaseValue)),
                        ("coefficient", J.N(0)),
                        ("school", J.S("school.physical"))))))));

            var procAura = J.O(
                ("id", J.S(ProcAuraId.Value)),
                ("duration", J.N(600)),
                ("effects", J.A(J.O(
                    ("kind", J.S("proc_trigger")),
                    ("params", J.O(("proc_ref", J.S(ProcDefId.Value))))))));

            // internal_cooldown 刻意取一个远大于单次测试时间跨度的值：proc 响应技能自身也会产生
            // combat.damage_dealt 事件，若无 ICD，DispatchPending 同一批次里会被自己的落地事件
            // 再次触发，无限递归（虽然最终会被 SkillOptions.MaxTriggerDepth/
            // EventBusOptions.MaxDispatchPasses 兜底截断，但会污染本测试要断言的"恰好触发一次"）。
            var procDef = J.O(
                ("id", J.S(ProcDefId.Value)),
                ("trigger_event", J.S("combat.damage_dealt")),
                ("trigger_skill", J.S(ProcResponseSkill.Value)),
                ("proc_chance", J.N(1.0)),
                ("internal_cooldown", J.N(600)));

            var statDefinitionJson = TableJson("stat.definition", new[]
            {
                J.O(
                    ("id", J.S(StatId.Value)),
                    ("name_key", J.S("l10n.stat.c10_sample_primary.name")),
                    ("group", J.S("primary")),
                    ("default_base", J.N(0))),
            });

            var powerTypeJson = TableJson("arch.power_type", new[]
            {
                J.O(
                    ("id", J.S(WellKnownPowers.Health.Value)),
                    ("name_key", J.S("l10n.power.health.name")),
                    ("max_source", J.O(("kind", J.S("fixed")), ("value", J.N(TargetMaxHp)))),
                    ("start_full", J.B(true)),
                    ("allow_overflow", J.B(false))),
            });

            var classJson = TableJson("arch.class", new[]
            {
                J.O(
                    ("id", J.S(ClassId.Value)),
                    ("name_key", J.S("l10n.arch.class.c10_sample.name")),
                    ("primary_stat", J.S(StatId.Value)),
                    ("base_stats", J.O()),
                    ("power_types", J.Ids(WellKnownPowers.Health.Value))),
            });

            var factionJson = TableJson("fac.faction", new[]
            {
                J.O(("id", J.S(FactionCaster.Value)), ("name_key", J.S("l10n.fac.c10_caster.name")), ("default_reaction", J.S("friendly"))),
                J.O(("id", J.S(FactionTarget.Value)), ("name_key", J.S("l10n.fac.c10_target.name")), ("default_reaction", J.S("neutral"))),
            });

            var reactionMatrixJson = TableJson("fac.reaction_matrix", new[]
            {
                J.O(("id", J.S("fac.reaction_matrix.c10_caster_to_target")), ("from", J.S(FactionCaster.Value)), ("to", J.S(FactionTarget.Value)), ("reaction", J.S("hostile"))),
                J.O(("id", J.S("fac.reaction_matrix.c10_target_to_caster")), ("from", J.S(FactionTarget.Value)), ("to", J.S(FactionCaster.Value)), ("reaction", J.S("hostile"))),
            });

            // 命中表全部分支禁用：结算不含任何随机波动，FinalAmount 恒等于 base_value（同
            // FightWorldBuilder.HitTableJson 判断记录）。表 id 直接复用 CombatOptions.HitTableConfigId
            // 的默认值 "combat.hit_table.default"，各用例无需显式覆盖 CombatOptions。
            var hitTableJson = TableJson("combat.hit_table_config", new[]
            {
                J.O(
                    ("id", J.S("combat.hit_table.default")),
                    ("miss", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("dodge", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("parry", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("glancing_blow", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("block", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("crit", J.O(("enabled", J.B(false)), ("base", J.N(0)))),
                    ("crit_multiplier_base", J.N(2.0))),
            });

            var targetChainJson = TableJson("target.chain_def", new[]
            {
                J.O(("id", J.S(SelfChain.Value)), ("source", J.S("self"))),
                J.O(
                    ("id", J.S(EnemyChain.Value)),
                    ("source", J.S("nearest_in_shape")),
                    ("shape", J.O(("kind", J.S("circle")), ("radius", J.N(50)))),
                    ("filters", J.A(J.S("relation:hostile"), J.S("alive"))),
                    ("sort_by", J.O(("key", J.S("distance")), ("direction", J.S("asc")))),
                    ("max_targets", J.N(1))),
            });

            var source = new InMemoryDataSource()
                .Add("stat.definition", statDefinitionJson)
                .Add("arch.power_type", powerTypeJson)
                .Add("arch.class", classJson)
                .Add("fac.faction", factionJson)
                .Add("fac.reaction_matrix", reactionMatrixJson)
                .Add("combat.hit_table_config", hitTableJson)
                .Add("combat.resist_curve", EmptyTableJson("combat.resist_curve"))
                .Add("target.chain_def", targetChainJson)
                .Add("skill.def", TableJson("skill.def", new[] { strikeSkill, healSkill, dotCastSkill, procResponseSkill }))
                .Add("skill.aura_def", TableJson("skill.aura_def", new[] { dotAura, procAura }))
                .Add("skill.proc_def", TableJson("skill.proc_def", new[] { procDef }));

            var bus = FightWorldBuilder.BuildEventBus(out _);
            var events = new List<IEvent>();
            foreach (var key in EventKeys.All)
            {
                bus.Subscribe(key, e => events.Add(e));
            }

            var registry = new DataRegistry(source, bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);

            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "C10 测试夹具数据未通过校验：\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            }

            var rng = new RngHost(20260911UL);
            var world = new WorldSim(bus);
            world.AddEntity(new TestUnit(Caster, MapId, FactionCaster) { Position = new Vec2(0, 0) });
            world.AddEntity(new TestUnit(Target, MapId, FactionTarget) { Position = new Vec2(1, 0) });

            var units = new WorldUnitAccess(world);
            var spatial = new StubSpatialQuery();
            spatial.Register(Caster, new Vec2(0, 0), 0.1);
            spatial.Register(Target, new Vec2(1, 0), 0.1);

            var rules = new RulesAssembly(bus, registry, rng, units, spatial, world, combatOptions: combatOptions);
            rules.RegisterUnit(Caster, ClassId, raceId: null, level: 1);
            rules.RegisterUnit(Target, ClassId, raceId: null, level: 1);

            return new Fixture { Bus = bus, Rules = rules, Events = events };
        }

        private static List<(EffectContext Context, ResolveResult Result)> RecordingCallback(
            out Action<EffectContext, ResolveResult> callback)
        {
            var calls = new List<(EffectContext, ResolveResult)>();
            callback = (ctx, result) => calls.Add((ctx, result));
            return calls;
        }

        // -----------------------------------------------------------------
        // 1) 瞬发技能伤害：CastSkill 触发一次结算，回调收到一次，Steps 非空，与落地事件一致
        // -----------------------------------------------------------------

        [Fact]
        public void InstantStrike_ResolveTraceInvokedOnce_MatchesDamageDealtEvent()
        {
            var calls = RecordingCallback(out var callback);
            var fx = BuildAssembly(new CombatOptions { ResolveTrace = callback });

            Assert.True(fx.Rules.Skill.CastSkill(Caster, StrikeSkill, Array.Empty<Id>()).Success);
            fx.Bus.DispatchPending();

            var call = Assert.Single(calls);
            Assert.Equal(Caster, call.Context.SourceId);
            Assert.Equal(Target, call.Context.TargetId);
            Assert.NotNull(call.Result.Steps);
            Assert.NotEmpty(call.Result.Steps!);
            Assert.Equal(StrikeBaseValue, call.Result.FinalAmount);

            var dealt = Assert.Single(fx.Of<CombatDamageDealtEvent>());
            Assert.Equal(call.Result.FinalAmount, dealt.Amount);
        }

        // -----------------------------------------------------------------
        // 2) 光环周期伤害：施加光环本身不经过 Resolver（apply_aura 不落 CombatOptions.ResolveTrace），
        //    随后的周期 tick 才经过——回调收到一次，与本次 tick 的落地量一致
        // -----------------------------------------------------------------

        [Fact]
        public void PeriodicAuraDamageTick_ResolveTraceInvokedOnce_MatchesTickLandedAmount()
        {
            var calls = RecordingCallback(out var callback);
            var fx = BuildAssembly(new CombatOptions { ResolveTrace = callback });

            Assert.True(fx.Rules.Skill.CastSkill(Caster, DotCastSkill, Array.Empty<Id>()).Success);
            fx.Bus.DispatchPending();
            Assert.Empty(calls); // apply_aura 本身不经过 Resolver。

            fx.Rules.Skill.Update(1.0); // 推进到第一次 periodic tick（interval=1.0）。
            fx.Bus.DispatchPending();

            var call = Assert.Single(calls);
            Assert.True(call.Context.IsPeriodic);
            Assert.Equal(Caster, call.Context.SourceId); // 光环来源仍是施加者。
            Assert.Equal(Target, call.Context.TargetId);
            Assert.NotNull(call.Result.Steps);
            Assert.NotEmpty(call.Result.Steps!);
            Assert.Equal(DotTickBaseValue, call.Result.FinalAmount);

            var dealt = Assert.Single(fx.Of<CombatDamageDealtEvent>());
            Assert.Equal(call.Result.FinalAmount, dealt.Amount);
        }

        // -----------------------------------------------------------------
        // 3) Proc 触发的嵌套施法：一次 CastSkill 引出两次真实结算（原技能 + Proc 触发的响应技能），
        //    回调各收到一次，互不覆盖
        // -----------------------------------------------------------------

        [Fact]
        public void ProcTriggeredNestedCast_ResolveTraceInvokedOncePerRealResolution()
        {
            var calls = RecordingCallback(out var callback);
            var fx = BuildAssembly(new CombatOptions { ResolveTrace = callback });

            fx.Rules.Skill.EffectSink.ApplyAura(Caster, ProcAuraId, Caster);
            fx.Bus.DispatchPending();
            Assert.Empty(calls); // 施加 proc 光环本身不产生任何结算。

            Assert.True(fx.Rules.Skill.CastSkill(Caster, StrikeSkill, Array.Empty<Id>()).Success);
            fx.Bus.DispatchPending();

            Assert.Equal(2, calls.Count); // 原始 strike 一次 + proc 触发的 proc_response 一次。
            foreach (var call in calls)
            {
                Assert.NotNull(call.Result.Steps);
                Assert.NotEmpty(call.Result.Steps!);
            }

            var strikeCall = calls.Single(c => c.Result.FinalAmount == StrikeBaseValue);
            var procCall = calls.Single(c => c.Result.FinalAmount == ProcResponseBaseValue);
            Assert.NotSame(strikeCall.Result, procCall.Result);

            var dealtEvents = fx.Of<CombatDamageDealtEvent>().ToList();
            Assert.Equal(2, dealtEvents.Count);
            Assert.Contains(dealtEvents, e => e.Amount == StrikeBaseValue);
            Assert.Contains(dealtEvents, e => e.Amount == ProcResponseBaseValue);

            Assert.Single(fx.Of<ProcTriggeredEvent>());
        }

        // -----------------------------------------------------------------
        // 4) 治疗路径：自疗一次，回调收到一次，IsHeal=true，与落地事件一致
        // -----------------------------------------------------------------

        [Fact]
        public void SelfHeal_ResolveTraceInvokedOnce_MatchesHealDoneEvent()
        {
            var calls = RecordingCallback(out var callback);
            var fx = BuildAssembly(new CombatOptions { ResolveTrace = callback });
            fx.Rules.Powers.ModifyPower(Caster, WellKnownPowers.Health, -500, sourceId: new Id("system.c10_setup"));

            Assert.True(fx.Rules.Skill.CastSkill(Caster, HealSkill, Array.Empty<Id>()).Success);
            fx.Bus.DispatchPending();

            var call = Assert.Single(calls);
            Assert.True(call.Result.IsHeal);
            Assert.Equal(HealBaseValue, call.Result.FinalAmount);

            var healed = Assert.Single(fx.Of<CombatHealDoneEvent>());
            Assert.Equal(call.Result.FinalAmount, healed.Amount);
        }

        // -----------------------------------------------------------------
        // 5) 未设置时零开销：与设置了（无副作用）回调的对照场景逐字段/逐事件完全一致
        // -----------------------------------------------------------------

        [Fact]
        public void ResolveTrace_Unset_EventSequenceAndLandedAmount_IdenticalToBaseline()
        {
            var baseline = BuildAssembly(combatOptions: null);
            Assert.True(baseline.Rules.Skill.CastSkill(Caster, StrikeSkill, Array.Empty<Id>()).Success);
            baseline.Bus.DispatchPending();

            var withNoOpCallback = BuildAssembly(new CombatOptions { ResolveTrace = (ctx, result) => { /* 观测但不改变任何状态 */ } });
            Assert.True(withNoOpCallback.Rules.Skill.CastSkill(Caster, StrikeSkill, Array.Empty<Id>()).Success);
            withNoOpCallback.Bus.DispatchPending();

            var baselineDealt = Assert.Single(baseline.Of<CombatDamageDealtEvent>());
            var withCallbackDealt = Assert.Single(withNoOpCallback.Of<CombatDamageDealtEvent>());
            Assert.Equal(baselineDealt.Amount, withCallbackDealt.Amount);
            Assert.Equal(baselineDealt.IsCrit, withCallbackDealt.IsCrit);
            Assert.Equal(baselineDealt.HitResult, withCallbackDealt.HitResult);

            Assert.Equal(
                baseline.Rules.Powers.GetPower(Target, WellKnownPowers.Health),
                withNoOpCallback.Rules.Powers.GetPower(Target, WellKnownPowers.Health));

            Assert.Equal(baseline.Events.Count, withNoOpCallback.Events.Count);
            for (var i = 0; i < baseline.Events.Count; i++)
            {
                Assert.Equal(baseline.Events[i].GetType(), withNoOpCallback.Events[i].GetType());
            }
        }

        // -----------------------------------------------------------------
        // 6) 回调抛异常：不中断结算、不阻断落地事件
        // -----------------------------------------------------------------

        [Fact]
        public void ResolveTrace_CallbackThrows_DoesNotInterruptResolutionOrEvent()
        {
            var fx = BuildAssembly(new CombatOptions
            {
                ResolveTrace = (ctx, result) => throw new InvalidOperationException("boom（仅测试用异常，不代表真实诊断代码缺陷）"),
            });

            var castResult = fx.Rules.Skill.CastSkill(Caster, StrikeSkill, Array.Empty<Id>());
            Assert.True(castResult.Success);
            fx.Bus.DispatchPending();

            var dealt = Assert.Single(fx.Of<CombatDamageDealtEvent>());
            Assert.Equal(StrikeBaseValue, dealt.Amount);
            Assert.Equal(TargetMaxHp - StrikeBaseValue, fx.Rules.Powers.GetPower(Target, WellKnownPowers.Health));
        }
    }
}

using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-7（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 4/5；
    /// 06 第 3.3/3.8 节 2026-09-14 修订段；分阶段落地计划第 14 节 N3 任务表第七行）：
    /// <list type="bullet">
    /// <item>周期效果来源缺失冻结——<see cref="Core.Rules.Skill.EffectDispatcher"/> 的
    /// <c>_lastPeriodicEffectValue</c> 缓存（见该字段判断记录），验证"来源仍注册时每跳随属性动态
    /// 重算""来源注销后每跳复用最后一次来源仍在时算出的值，不再重算、不退化为 0"两条语义，覆盖
    /// <c>scaling</c> 列表与旧 <c>scaling_stat</c> 两条路径。本组直接构造
    /// <see cref="EffectContext"/> 经 <c>IEffectSink.ApplyEffect</c> 调用，绕开
    /// <see cref="Core.Rules.Skill.AuraHost"/> 的 tick 循环，只验证 <c>EffectDispatcher</c> 这一层的
    /// 冻结/动态重算契约本身（惯例同 <see cref="T_N3_2_SkillScalingListAndBaseCurveTests"/>）。</item>
    /// <item>瘟疫刷新比例——<see cref="Core.Rules.Skill.AuraHost"/>.<c>ComputeRefreshedRemaining</c>
    /// 按 <see cref="SkillOptions.PlagueRefreshRatio"/> 混合剩余时长与定义持续时间，比例 0（退化为
    /// 现有"直接刷新"行为）与 0.3（契约默认值）各两组，用"精确的到期边界"（<c>Update</c> 推进到
    /// 恰好越过/未越过手算出的新剩余时长）反推实际生效的新持续时间，惯例同
    /// <see cref="AuraStackingTests"/>.<c>RunOverflowScenario</c>。</item>
    /// </list>
    /// </summary>
    public sealed class T_N3_7_PeriodicFreezeAndPlagueRefreshTests
    {
        // -----------------------------------------------------------------
        // 冻结：来源缺失冻结最后一次算出的每跳值
        // -----------------------------------------------------------------

        private static readonly Id Source = new Id("unit.n37_source");
        private static readonly Id Target = new Id("unit.n37_target");
        private static readonly Id AuraInstance = new Id("skill.aura_inst.n37_fixed");
        private static readonly Id School = new Id("skill.school_sample");
        private static readonly Id Skill = new Id("skill.n37_periodic_bolt");

        [Fact]
        public void PeriodicFreeze_ScalingList_SourceUnregistered_ReusesLastLiveValue_NotZero_NotRecomputed()
        {
            var world = new SkillWorldBuilder().Stat("stat.n37_power", defaultBase: 10).Build();
            world.AddUnit(Source);
            world.AddUnit(Target);

            var liveContext = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School, 0, 0,
                J.O(
                    ("base_value", J.N(5)),
                    ("scaling", J.A(J.O(("stat", J.S("stat.n37_power")), ("coefficient", J.N(2)))))),
                auraInstanceId: AuraInstance, isPeriodic: true);

            world.Host.EffectSink.ApplyEffect(liveContext);
            // 5 + 2*10 = 25，来源仍在，正常动态求值——本条同时充当下面"冻结"分支的活值来源。
            Assert.Equal(25, world.Combat.ResolveCalls[0].BaseValue);

            // 真实场景里来源缺失后 AuraHost.FirePeriodic 仍会重建一次 EffectContext（同一
            // AuraInstanceId/Kind/School，见 EffectDispatcher._lastPeriodicEffectValue 缓存键），
            // 这里直接注销 StatHost 注册模拟"来源已被 Despawn"（同 C02 判断记录的判定条件
            // IStatHost.IsRegistered）。故意把 base_value/coefficient 改写成明显不同的数字（若被
            // 冻结分支忽略缓存、退回重算，会得到一个可辨识的错误值 999，而不是碰巧算对）。
            world.Stats.UnregisterUnit(Source);

            var afterDespawnContext = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School, 0, 0,
                J.O(
                    ("base_value", J.N(999)),
                    ("scaling", J.A(J.O(("stat", J.S("stat.n37_power")), ("coefficient", J.N(999)))))),
                auraInstanceId: AuraInstance, isPeriodic: true);

            world.Host.EffectSink.ApplyEffect(afterDespawnContext);

            Assert.Equal(2, world.Combat.ResolveCalls.Count);
            // 冻结：复用来源仍在时算出的 25，既不是新 params 算出的 999+999*10=10989，也不是旧契约
            // "缩放贡献按 0 处理"会得到的 999（只剩新 params 的 base_value）。
            Assert.Equal(25, world.Combat.ResolveCalls[1].BaseValue);
        }

        [Fact]
        public void PeriodicFreeze_LegacyScalingStat_SourceUnregistered_ReusesLastLiveValue()
        {
            // 覆盖硬性规则"禁止删除旧 scaling_stat 读取路径"——冻结缓存对两条路径（scaling 列表/
            // scaling_stat）一视同仁，legacy 路径同样要能冻结，不能只有新路径生效。
            var world = new SkillWorldBuilder().Stat("stat.n37_power", defaultBase: 4).Build();
            world.AddUnit(Source);
            world.AddUnit(Target);

            var liveContext = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School,
                baseValue: 3, coefficient: 5,
                J.O(("scaling_stat", J.S("stat.n37_power"))),
                auraInstanceId: AuraInstance, isPeriodic: true);

            world.Host.EffectSink.ApplyEffect(liveContext);
            // 3 + 5*4 = 23。
            Assert.Equal(23, world.Combat.ResolveCalls[0].BaseValue);

            world.Stats.UnregisterUnit(Source);

            var afterDespawnContext = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School,
                baseValue: 777, coefficient: 777,
                J.O(("scaling_stat", J.S("stat.n37_power"))),
                auraInstanceId: AuraInstance, isPeriodic: true);

            world.Host.EffectSink.ApplyEffect(afterDespawnContext);

            Assert.Equal(23, world.Combat.ResolveCalls[1].BaseValue);
        }

        [Fact]
        public void PeriodicDynamic_SourceStillRegistered_ControlGroup_RecomputesEveryTick_NeverFreezes()
        {
            // 对照组：来源全程仍注册，即使 AuraInstanceId/Kind/School 与前两个用例的键完全相同的
            // 结构、缓存也会被写入，但因为来源一直存在，冻结分支的 "!sourceRegistered" 前提永远不
            // 成立——每一跳都必须按当时的属性值重新算，属性变了就跟着变，绝不会退回第一跳的缓存值。
            var world = new SkillWorldBuilder().Stat("stat.n37_power", defaultBase: 10).Build();
            world.AddUnit(Source);
            world.AddUnit(Target);

            var context1 = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School, 0, 0,
                J.O(
                    ("base_value", J.N(5)),
                    ("scaling", J.A(J.O(("stat", J.S("stat.n37_power")), ("coefficient", J.N(2)))))),
                auraInstanceId: AuraInstance, isPeriodic: true);
            world.Host.EffectSink.ApplyEffect(context1);
            Assert.Equal(25, world.Combat.ResolveCalls[0].BaseValue); // 5 + 2*10

            // 属性在两跳之间变化（如装备/增益导致），来源仍注册。
            world.Stats.SetBase(Source, new Id("stat.n37_power"), 20);

            var context2 = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School, 0, 0,
                J.O(
                    ("base_value", J.N(5)),
                    ("scaling", J.A(J.O(("stat", J.S("stat.n37_power")), ("coefficient", J.N(2)))))),
                auraInstanceId: AuraInstance, isPeriodic: true);
            world.Host.EffectSink.ApplyEffect(context2);

            // 5 + 2*20 = 45——随属性动态变化，不是冻结在第一跳的 25。
            Assert.Equal(45, world.Combat.ResolveCalls[1].BaseValue);
        }

        // -----------------------------------------------------------------
        // 瘟疫刷新：SkillOptions.PlagueRefreshRatio
        // -----------------------------------------------------------------

        private static readonly Id RefreshTarget = new Id("unit.n37_refresh_target");
        private static readonly Id RefreshSource = new Id("unit.n37_refresh_source");
        private static readonly Id RefreshAuraDef = new Id("skill.aura_def.n37_refresh_sample");

        private static JsonObject RefreshableAura(double duration) => J.O(
            ("id", J.S(RefreshAuraDef.Value)),
            ("duration", J.N(duration)),
            ("max_stacks", J.N(2)),
            ("effects", J.A(
                J.O(("kind", J.S("mod_stat")),
                    ("params", J.O(("stat", J.S("stat.n37_refresh_power")), ("op", J.S("flat")), ("value", J.N(1))))))));

        [Fact]
        public void PlagueRefresh_RatioZero_EarlyReapply_ResetsToDefinitionDuration_IgnoringLargeRemaining()
        {
            // 定义持续时间 10，比例 0：刷新时手算 新持续时间 = 10 + min(剩余时长, 10*0) = 10 + 0 = 10，
            // 与"剩余时长"具体多少完全无关——本组在剩余时长还很充裕（9）时刷新。
            var builder = new SkillWorldBuilder().AuraDef(RefreshableAura(10)).Stat("stat.n37_refresh_power");
            builder.Options.PlagueRefreshRatio = 0;
            var world = builder.Build();
            world.AddUnit(RefreshTarget);

            var sink = world.Host.EffectSink;
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource);
            world.Host.Update(1.0); // 剩余 9
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource); // 刷新：手算新剩余 = 10

            world.Host.Update(9.9);
            Assert.True(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "手算新剩余 10，推进 9.9 应仍存活");

            world.Host.Update(0.2);
            Assert.False(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "累计推进 10.1 > 手算新剩余 10，应已到期");
        }

        [Fact]
        public void PlagueRefresh_RatioZero_LateReapply_StillResetsToDefinitionDuration_IgnoringSmallRemaining()
        {
            // 同上，但在剩余时长很少（0.5）时刷新——比例 0 下结果应与上一组完全一致（新剩余恒为
            // 定义持续时间 10），证明"忽略剩余时长"这一点与刷新发生的时机无关。
            var builder = new SkillWorldBuilder().AuraDef(RefreshableAura(10)).Stat("stat.n37_refresh_power");
            builder.Options.PlagueRefreshRatio = 0;
            var world = builder.Build();
            world.AddUnit(RefreshTarget);

            var sink = world.Host.EffectSink;
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource);
            world.Host.Update(9.5); // 剩余 0.5
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource); // 刷新：手算新剩余 = 10

            world.Host.Update(9.9);
            Assert.True(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "手算新剩余 10，推进 9.9 应仍存活");

            world.Host.Update(0.2);
            Assert.False(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "累计推进 10.1 > 手算新剩余 10，应已到期");
        }

        [Fact]
        public void PlagueRefresh_RatioPoint3_RemainingAboveCap_RetainsCappedAtRatioTimesDuration()
        {
            // 定义持续时间 10，比例 0.3，上限 = 10*0.3 = 3。剩余时长 9 时刷新（9 > 3，min 取上限）：
            // 手算新剩余 = 10 + min(9, 3) = 13。
            var builder = new SkillWorldBuilder().AuraDef(RefreshableAura(10)).Stat("stat.n37_refresh_power");
            builder.Options.PlagueRefreshRatio = 0.3;
            var world = builder.Build();
            world.AddUnit(RefreshTarget);

            var sink = world.Host.EffectSink;
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource);
            world.Host.Update(1.0); // 剩余 9
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource); // 刷新：手算新剩余 = 13

            world.Host.Update(12.9);
            Assert.True(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "手算新剩余 13，推进 12.9 应仍存活");

            world.Host.Update(0.2);
            Assert.False(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "累计推进 13.1 > 手算新剩余 13，应已到期");
        }

        [Fact]
        public void PlagueRefresh_RatioPoint3_RemainingBelowCap_RetainsRemainingItself()
        {
            // 同上曲线，但剩余时长 2 时刷新（2 < 上限 3，min 取剩余时长本身）：
            // 手算新剩余 = 10 + min(2, 3) = 12——与上一组的 13 不同，证明 min() 的两个分支都生效。
            var builder = new SkillWorldBuilder().AuraDef(RefreshableAura(10)).Stat("stat.n37_refresh_power");
            builder.Options.PlagueRefreshRatio = 0.3;
            var world = builder.Build();
            world.AddUnit(RefreshTarget);

            var sink = world.Host.EffectSink;
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource);
            world.Host.Update(8.0); // 剩余 2
            sink.ApplyAura(RefreshTarget, RefreshAuraDef, RefreshSource); // 刷新：手算新剩余 = 12

            world.Host.Update(11.9);
            Assert.True(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "手算新剩余 12，推进 11.9 应仍存活");

            world.Host.Update(0.2);
            Assert.False(world.Host.AuraQuery.HasAura(RefreshTarget, RefreshAuraDef), "累计推进 12.1 > 手算新剩余 12，应已到期");
        }
    }
}

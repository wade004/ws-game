using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;
using static Tests.Rules.Skill.TimelineHarness;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 武器节奏（ADR-0176，<c>skill.def.timeline.weapon_paced</c>）：动作式普攻的整段时长随主手武器挥击间隔
    /// （<c>weapon_profile.speed</c>）÷ 基准间隔（<see cref="SkillOptions.WeaponPaceReferenceSeconds"/>）缩放。
    /// 每个规则一条复现（修复前红）、一条不变量；期望 tick 一律由分相毫秒、武器速度与固定步长经 <see cref="TimelineHarness.Ticks"/> 算出，不写死裸数。
    /// </summary>
    public sealed class WeaponPacedTimelineTests
    {
        private const double S = 200;
        private const double A = 100;
        private const double R = 300;
        private static readonly Id AttackInput = new Id("input.attack");

        /// <summary>可设置挥击间隔的武器查询替身（null = 没有武器）。</summary>
        private sealed class WeaponStub : IWeaponDamageQuery
        {
            public double? Speed;

            public double GetWeaponBaseDamage(Id unitId) => 0.0;

            public double GetWeaponDps(Id unitId) => 0.0;

            public double? GetWeaponAttackIntervalSeconds(Id unitId) => Speed;

            public Id? GetWeaponSchool(Id unitId) => null;
        }

        private static JsonObject Seg(string id, string? next, bool paced, double startup = S, double active = A, double recovery = R)
        {
            var timeline = new List<(string, JsonValue)>
            {
                ("startup_ms", J.N(startup)), ("active_ms", J.N(active)), ("recovery_ms", J.N(recovery)),
                ("markers", new JsonArray(new[] { Hit(startup) })),
            };
            if (next != null)
            {
                // 连招窗口从后摇起点（判定相结束）开到动作结束：与起步包轻击三连同一形状。
                timeline.Add(("combo", ComboBlock(next, startup + active, startup + active + recovery)));
            }

            if (paced) timeline.Add(("weapon_paced", J.B(true)));
            return J.O(
                ("id", J.S(id)), ("school", J.S("skill.school_sample")), ("kind", J.S("active")), ("range", J.N(0)),
                ("cast_time", J.N((startup + active + recovery) / 1000.0)), ("respects_gcd", J.B(true)),
                ("cooldown_duration", J.N(0)), ("target_shape_ref", J.S(Chain.Value)),
                ("timeline", J.O(timeline.ToArray())),
                ("effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(7)), ("coefficient", J.N(0))))))));
        }

        private static TimelineHarness Make(WeaponStub? weapon, bool paced = true, double? reference = null, string? preset = null)
        {
            var skills = new[]
            {
                Seg("skill.sample_a", "skill.sample_b", paced),
                Seg("skill.sample_b", "skill.sample_c", paced),
                Seg("skill.sample_c", null, paced),
            };
            var h = Create(skills, b =>
            {
                b.Options.GcdEnabled = false;
                if (weapon != null) b.WeaponDamageQuery = weapon;
                if (reference.HasValue) b.Options.WeaponPaceReferenceSeconds = reference.Value;
            }, preset);
            h.Binding.Map(AttackInput.Value, "skill.sample_a");
            return h;
        }

        private static int Total(double k) => Ticks(S * k) + Ticks(A * k) + Ticks(R * k);

        private static ActionStartedEvent OnlyStart(TimelineHarness h) => h.Of<ActionStartedEvent>().Last().Event;

        // ------------------------------------------------------------------ 1. 整段时长随武器速度缩放

        [Theory]
        [InlineData(0.5)]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(2.5)]
        public void PacedAction_PhasesAndTotal_ScaleWithWeaponSpeedOverReference(double speed)
        {
            var h = Make(new WeaponStub { Speed = speed });
            var c = h.CastInTick("skill.sample_a");
            Assert.Equal(Total(speed), OnlyStart(h).DurationTicks);

            // 相位切换 tick 与前摇/判定相的缩放时长一致（标记与窗口起点跟随同一系数）。
            h.TickN(Total(speed) + 2);
            var phases = h.Of<ActionPhaseChangedEvent>().ToList();
            Assert.Equal(c, phases.First(p => p.Event.Phase == ActionPhase.Startup).Tick);
            Assert.Equal(c + Ticks(S * speed), phases.First(p => p.Event.Phase == ActionPhase.Active).Tick);
            Assert.Equal(c + Ticks(S * speed) + Ticks(A * speed), phases.First(p => p.Event.Phase == ActionPhase.Recovery).Tick);
            Assert.Equal(c + Total(speed), h.Of<ActionFinishedEvent>().Single().Tick);
        }

        [Fact]
        public void PacedAction_AnimationRates_ReflectTheStretch()
        {
            // 动画播放速率 = 作者毫秒 ÷ 实际毫秒：武器越慢，表现层的剪辑越慢，与判定时间线不脱节。
            const double speed = 2.0;
            var h = Make(new WeaponStub { Speed = speed });
            h.CastInTick("skill.sample_a");
            var started = OnlyStart(h);
            Assert.Equal(S / (Ticks(S * speed) * Step * 1000.0), started.StartupRate, 6);
            Assert.Equal(R / (Ticks(R * speed) * Step * 1000.0), started.RecoveryRate, 6);
            Assert.True(started.StartupRate < 1.0, "更慢的武器把动画放慢");
        }

        // ------------------------------------------------------------------ 2. 不声明 / 没武器 = 逐位旧行为（数据开关）

        [Theory]
        [InlineData(0.5)]
        [InlineData(2.5)]
        public void UnflaggedAction_IgnoresWeaponSpeed_BitIdenticalToBefore(double speed)
        {
            var withWeapon = Make(new WeaponStub { Speed = speed }, paced: false);
            withWeapon.CastInTick("skill.sample_a");
            withWeapon.TickN(Total(1.0) + 2);

            var withoutQuery = Make(null, paced: false);
            withoutQuery.CastInTick("skill.sample_a");
            withoutQuery.TickN(Total(1.0) + 2);

            Assert.Equal(Total(1.0), OnlyStart(withWeapon).DurationTicks);
            Assert.Equal(withoutQuery.Fingerprint(), withWeapon.Fingerprint());
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void PacedAction_WithoutUsableWeaponSpeed_KeepsAuthoredTiming(double? speed)
        {
            var h = Make(new WeaponStub { Speed = speed });
            h.CastInTick("skill.sample_a");
            Assert.Equal(Total(1.0), OnlyStart(h).DurationTicks);

            var noQuery = Make(null);
            noQuery.CastInTick("skill.sample_a");
            Assert.Equal(Total(1.0), OnlyStart(noQuery).DurationTicks);
        }

        // ------------------------------------------------------------------ 3. 基准间隔选项

        [Theory]
        [InlineData(1.5, 1.5, 1.0)]
        [InlineData(3.0, 1.5, 2.0)]
        [InlineData(0.75, 1.5, 0.5)]
        public void ReferenceSeconds_RebasesTheScale(double speed, double reference, double expectedFactor)
        {
            var h = Make(new WeaponStub { Speed = speed }, reference: reference);
            h.CastInTick("skill.sample_a");
            Assert.Equal(Total(expectedFactor), OnlyStart(h).DurationTicks);
        }

        [Fact]
        public void ReferenceSeconds_NonPositive_DisablesTheScale()
        {
            var h = Make(new WeaponStub { Speed = 2.0 }, reference: 0.0);
            h.CastInTick("skill.sample_a");
            Assert.Equal(Total(1.0), OnlyStart(h).DurationTicks);
        }

        // ------------------------------------------------------------------ 4. 快照：换装只影响下一个动作

        [Fact]
        public void WeaponSwapMidAction_OnlyAffectsTheNextAction()
        {
            var weapon = new WeaponStub { Speed = 1.0 };
            var h = Make(weapon);
            var c = h.CastInTick("skill.sample_a");
            h.TickN(3);
            weapon.Speed = 3.0; // 动作进行中换了一把慢武器
            h.TickN(Total(1.0));
            Assert.Equal(c + Total(1.0), h.Of<ActionFinishedEvent>().Single().Tick);

            h.CastInTick("skill.sample_a");
            Assert.Equal(Total(3.0), OnlyStart(h).DurationTicks);
        }

        // ------------------------------------------------------------------ 5. 与手感档案分相倍率相乘

        [Fact]
        public void PacedAction_ComposesWithFeelPhaseScale()
        {
            const double speed = 1.5;
            var h = Make(new WeaponStub { Speed = speed }, preset: "feel.preset.arpg_responsive");
            var judging = h.Feel!.ResolveJudging(Actor);
            double Ps(string field) => judging.TryGetNumber(field, out var v) ? v : 1.0;
            h.CastInTick("skill.sample_a");
            var expected = Ticks(S * Ps(FeelFieldNames.PhaseScaleStartup) * speed)
                + Ticks(A * Ps(FeelFieldNames.PhaseScaleActive) * speed)
                + Ticks(R * Ps(FeelFieldNames.PhaseScaleRecovery) * speed);
            Assert.Equal(expected, OnlyStart(h).DurationTicks);
        }

        // ------------------------------------------------------------------ 6. 节奏不变量：以远高于攻速的频率连按

        /// <summary>
        /// 每个 tick 都按一次攻击键（缓冲有效期 <paramref name="bufferTicks"/>），空闲时按缓冲出口的规则起手第一段，动作进行中由时间线在连招窗口里拉取；
        /// 返回窗口内全部动作开始事件 (tick, 技能, 连招序号, 动作总 tick)。
        /// </summary>
        private static List<(int Tick, Id Skill, int Combo, int Duration)> Spam(TimelineHarness h, int ticks, int bufferTicks)
        {
            for (var i = 0; i < ticks; i++)
            {
                h.Tick(() =>
                {
                    h.Input.Push(Actor, AttackInput, ActionClass.Attack, validTicks: bufferTicks, priority: 30);
                    if (h.Query.Current(Actor) == null && h.Input.TryConsume(Actor, null, out _))
                    {
                        Assert.True(h.World.Host.CastSkill(Actor, new Id("skill.sample_a"), Array.Empty<Id>()).Success);
                    }
                });
            }

            return h.Of<ActionStartedEvent>().Select(e => (e.Tick, e.Event.SkillId, e.Event.ComboIndex, e.Event.DurationTicks)).ToList();
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void Spam_StartsNeverComeFasterThanTheScaledLinkPoint_AndCountIsBoundedByData(double speed)
        {
            var h = Make(new WeaponStub { Speed = speed });
            var window = Ticks(8000);
            var starts = Spam(h, window, bufferTicks: Ticks(180));

            // 规则：连招接续点 = 判定相结束（后摇起点，缩放后）；非连招的新起手要等上一动作自然结束。由数据算出每对相邻起手的最小间隔。
            var linkGap = Ticks(S * speed) + Ticks(A * speed);
            for (var i = 1; i < starts.Count; i++)
            {
                var gap = starts[i].Tick - starts[i - 1].Tick;
                var minGap = starts[i].Combo > 0 ? linkGap : starts[i - 1].Duration;
                Assert.True(gap >= minGap, $"第 {i} 次起手距上一次 {gap} tick，小于规则下限 {minGap}（speed={speed}）");
            }

            // 数量上限由数据算出：一个三连周期 = 前两段到接续点 + 最后一段整段；周期内三次起手。
            var cycle = 2 * linkGap + Total(speed);
            var upper = (window / cycle + 1) * 3;
            Assert.True(starts.Count <= upper, $"{window} tick 内起手 {starts.Count} 次，超过由数据算出的上限 {upper}");
            Assert.True(starts.Count >= (window / cycle - 1) * 3, "按住不放时也不应该少于一个周期的整数倍减一（输入缓冲持续喂入）");
        }

        [Fact]
        public void Spam_RateScalesInverselyWithWeaponSpeed()
        {
            var window = Ticks(8000);
            int Count(double speed)
            {
                var h = Make(new WeaponStub { Speed = speed });
                return Spam(h, window, bufferTicks: Ticks(180)).Count;
            }

            var fast = Count(1.0);
            var slow = Count(2.0);
            // 速度减半（间隔翻倍）→ 起手次数约减半；量化到 tick 与三连周期，允许一个周期（3 次）的误差。
            Assert.True(Math.Abs(fast - 2 * slow) <= 3, $"间隔 1.0 起手 {fast} 次，间隔 2.0 起手 {slow} 次，比例应约为 2:1");
            Assert.True(slow < fast);
        }

        [Fact]
        public void Combo_PressInsideTheScaledWindow_LinksToTheNextSegment()
        {
            const double speed = 2.0;
            var h = Make(new WeaponStub { Speed = speed });
            var c = h.CastInTick("skill.sample_a");
            var linkAt = Ticks(S * speed) + Ticks(A * speed);

            // 接续点（缩放后的后摇起点）前一 tick 按下：窗口还没开，不接续；到点的当 tick 接续下一段，连招序号 +1。
            while (h.TickIndex - c < linkAt - 2) h.Tick();
            h.Tick(() => h.Input.Push(Actor, AttackInput, ActionClass.Attack, validTicks: 5));
            Assert.Single(h.Of<ActionStartedEvent>());
            h.Tick();
            var second = h.Of<ActionStartedEvent>().Last();
            Assert.Equal(new Id("skill.sample_b"), second.Event.SkillId);
            Assert.Equal(1, second.Event.ComboIndex);
            Assert.Equal(c + linkAt, second.Tick);
        }

        [Fact]
        public void Combo_AfterTheActionEnds_ContinuesWithinResetWindow_AndRestartsBeyondIt()
        {
            const double speed = 2.0;
            const string preset = "feel.preset.arpg_responsive";
            var h = Make(new WeaponStub { Speed = speed }, preset: preset);
            var resetMs = h.Feel!.ResolveJudging(Actor).GetNumber(FeelFieldNames.ComboResetMs);
            Assert.True(resetMs > 0, "测试前提：档案声明了连招链保留时长");
            var resetTicks = Ticks(resetMs);

            // 窗口外 1：动作自然结束后在 combo_reset_ms 之内再起手第一段技能 -> 链接续到下一段。
            h.CastInTick("skill.sample_a");
            while (h.Query.Current(Actor) != null) h.Tick();
            h.TickN(resetTicks - 2);
            h.CastInTick("skill.sample_a");
            Assert.Equal(new Id("skill.sample_b"), OnlyStart(h).SkillId);
            Assert.Equal(1, OnlyStart(h).ComboIndex);

            // 窗口外 2：超过 combo_reset_ms -> 从第一段重来。
            while (h.Query.Current(Actor) != null) h.Tick();
            h.TickN(resetTicks + 2);
            h.CastInTick("skill.sample_a");
            Assert.Equal(new Id("skill.sample_a"), OnlyStart(h).SkillId);
            Assert.Equal(0, OnlyStart(h).ComboIndex);
        }
    }
}

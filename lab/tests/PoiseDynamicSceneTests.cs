using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 动态韧性脚本（M4-L，<c>feel_poise_dynamic</c>）的运行期验收：韧性池轨迹由"数据里的容量/伤害/回复延迟/速率 × 命中 tick"按规则在用例里
    /// 推演，不写死裸数。复现（五击的逐击扣减与反应）加不变量（池子恒在 [0, 容量]、扣减记账恒自洽、静态韧性脚本不出现 poise 组）。
    /// </summary>
    public sealed class PoiseDynamicSceneTests
    {
        private const string Script = "feel_poise_dynamic";
        private const string Action = "2d_action";
        private const string Targeted = "2d_targeted";

        private static double Number(JsonObject row, string field)
        {
            foreach (var w in (JsonArray)row["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return ((JsonNumber)o["value"]).Value;
                }
            }

            throw new InvalidOperationException($"{((JsonString)row["id"]).Value} 没有写 {field}");
        }

        private sealed class Rule
        {
            public double Max;
            public double Damage;
            public int DelayTicks;
            public double PerTick;
            public double Power;
        }

        private static Rule Rules()
        {
            var creature = FeelRules.Row(Path.Combine("data", "_lab_action", "creature", "creature.template.json"), "creature.lab_a_resilient");
            var max = ((JsonNumber)((JsonObject)creature["base_stats"])["stat.poise"]).Value;
            var chip = FeelRules.Row(Path.Combine("data", "_lab_action", "feel", "feel.action.json"), "feel.action.lab_a_poise_chip");
            var character = FeelRules.Row(Path.Combine("data", "_lab_action", "feel", "feel.character.json"), "feel.character.lab_a_resilient");
            return new Rule
            {
                Max = max,
                Damage = Number(chip, "poise_damage"),
                DelayTicks = FeelRules.T(Number(character, "poise_recover_delay_ms")),
                PerTick = Number(character, "poise_recover_per_s") * FeelRules.StepSeconds,
                Power = FeelRules.ForCell(Action).N("stagger_power"),
            };
        }

        /// <summary>从命中 tick 序列按规则推演每次命中的（扣前, 扣后, 是否破韧）与回满 tick 列表。</summary>
        private static (List<(int Tick, double Before, double After, bool Broken)> Hits, List<int> Refills) Simulate(Rule rule, IReadOnlyList<int> hitTicks)
        {
            var hits = new List<(int, double, double, bool)>();
            var refills = new List<int>();
            var lost = 0.0;
            var lastHit = -1;
            foreach (var tick in hitTicks)
            {
                if (lastHit >= 0)
                {
                    // 两次命中之间（含本 tick 开头）的回复：延迟耗尽后每 tick 回复 PerTick，回满即发回满事件并清零。
                    var elapsed = tick - lastHit;
                    var recovering = Math.Max(0, elapsed - rule.DelayTicks);
                    if (lost > 0.0 && recovering > 0)
                    {
                        var needed = (int)Math.Ceiling(lost / rule.PerTick - 1e-9);
                        if (recovering >= needed)
                        {
                            refills.Add(lastHit + rule.DelayTicks + needed);
                            lost = 0.0;
                        }
                        else
                        {
                            lost -= recovering * rule.PerTick;
                        }
                    }
                }

                var before = Math.Max(0.0, rule.Max - lost);
                var after = Math.Max(0.0, before - rule.Damage);
                hits.Add((tick, before, after, before > 0.0 && after <= 0.0));
                lost = rule.Max - after;
                lastHit = tick;
            }

            // 最后一次命中之后，到脚本结束前如果回满也有一条回满事件。
            if (lost > 0.0)
            {
                refills.Add(lastHit + rule.DelayTicks + (int)Math.Ceiling(lost / rule.PerTick - 1e-9));
            }

            return (hits, refills);
        }

        private static List<int> HitTicks(FeelFp fp) => fp.Items("spatialhit.confirm_ticks").Select(FeelFp.TickOf).ToList();

        [Fact]
        public void DynamicPoise_TrajectoryFollowsTheRules_AndEachHitIsShelteredOrBrokenAccordingly()
        {
            var rule = Rules();
            var fp = FeelFp.Of(Script, Action);
            var hitTicks = HitTicks(fp);
            Assert.Equal(5, hitTicks.Count);
            var (expected, expectedRefills) = Simulate(rule, hitTicks);

            // 前提：每击伤害不超过容量的一半，且强度不高于任何"会被挡住"的击前韧性——否则下面"是否被挡"的推演不成立。
            Assert.True(rule.Damage * 2 <= rule.Max);
            Assert.True(rule.Power <= rule.Damage);

            var changes = fp.Items("poise.changes");
            Assert.Equal(expected.Count, changes.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                var e = expected[i];
                var want = FormattableString.Invariant($"{e.Tick}:stake_resilient:{Fmt(e.Before)}>{Fmt(e.After)}{(e.Broken ? "!" : string.Empty)}");
                Assert.Equal(want, changes[i]);
            }

            Assert.Equal(expected.Count(h => h.Broken), (int)fp.Num("poise.breaks"));

            // 受击反应：击后池子仍 > 0 且强度不高于击前韧性 → Flinch（被挡住）；否则完整反应。
            var reactions = fp.Items("reaction.reactions").Select(r => r.Split(':')[2]).ToList();
            var full = FullReaction();
            for (var i = 0; i < expected.Count; i++)
            {
                var sheltered = expected[i].After > 0.0 && rule.Power <= expected[i].Before;
                Assert.Equal(sheltered ? "Flinch" : full, reactions[i]);
            }

            // 回满事件：只在之前有损失、现在回满的 tick 发一次（脚本时长内的那些）。
            var recoveries = fp.Items("poise.recoveries").Select(FeelFp.TickOf).ToList();
            var duration = LabTestSupport.Script(Script).Meta.DurationTicks;
            Assert.Equal(expectedRefills.Where(t => t < duration).OrderBy(t => t).ToList(), recoveries);
        }

        [Fact]
        public void DynamicPoise_PoolStaysInsideItsBoundsAndTheAccountingIsConsistent_OnEveryActionCell()
        {
            foreach (var cell in new[] { "2d_action", "2_5d_action", "3d_action" })
            {
                var fp = FeelFp.Of(Script, cell);
                Assert.Equal(0.0, fp.Num("poise.out_of_range"));
                Assert.Equal(0.0, fp.Num("poise.accounting_mismatch"));
                Assert.Equal(5.0, fp.Num("poise.hits"));
            }
        }

        [Fact]
        public void DynamicPoise_TheGroupAppearsOnlyWhereHitsDeclarePoiseDamage_StaticPoiseScriptsHaveNone()
        {
            // 目标选择式格子没有时间线，削韧击的 feel_ref 随时间线一起没了：没有韧性伤害，没有 poise 组。
            Assert.False(FeelFp.Of(Script, Targeted).Has("poise"));
            // 静态韧性脚本（韧性属性 + 强度比较）从不出现动态韧性组。
            Assert.False(FeelFp.Of("feel_stake_poise", Action).Has("poise"));
            Assert.False(FeelFp.Of("feel_combo3", Action).Has("poise"));
        }

        private static string FullReaction()
        {
            // 完整反应 = 冲击等级映射（脚本里的击打都是 medium → Stagger，生产缺省映射表）。
            return new Core.Rules.Combat.HitFeelOptions().ImpactReactions[FeelRules.ForCell(Action).S("impact_class")].ToString();
        }

        private static string Fmt(double value) => Math.Round(value, 9).ToString("R", CultureInfo.InvariantCulture);
    }
}

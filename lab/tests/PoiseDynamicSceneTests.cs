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

        // ------------------------------------------------------------------ M4-W3：动态韧性的三项可选扩展（脚本 + 数据根 poise_ext）

        private static readonly string[] ActionCells = { "2d_action", "2_5d_action", "3d_action" };

        private static JsonObject ExtCharacter(string id) =>
            FeelRules.Row(Path.Combine("lab", "fixtures", "data", "poise_ext", "feel", "feel.character.json"), id);

        private static double CharacterNumber(string id, string field) => Number(ExtCharacter(id), field);

        private static string CharacterText(string id, string field)
        {
            foreach (var w in (JsonArray)ExtCharacter(id)["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return ((JsonString)o["value"]).Value;
                }
            }

            throw new InvalidOperationException($"{id} 没有写 {field}");
        }

        [Fact]
        public void PoiseRecoverModeOutOfCombat_InCombatTheDelayAndRegenAreSuspended_WhereDelayModeWouldAlreadyHaveRecovered()
        {
            const string script = "feel_poise_ooc";
            var rule = Rules();
            Assert.Equal("out_of_combat", CharacterText("feel.character.lab_poise_ooc", "poise_recover_mode"));
            var delayTicks = FeelRules.T(CharacterNumber("feel.character.lab_poise_ooc", "poise_recover_delay_ms"));
            var perTick = CharacterNumber("feel.character.lab_poise_ooc", "poise_recover_per_s") * FeelRules.StepSeconds;
            foreach (var cell in ActionCells)
            {
                var fp = FeelFp.Of(script, cell);
                var hitTicks = HitTicks(fp);
                Assert.Equal(2, hitTicks.Count);

                // 前提（场景有区分度）：两击的间隔足以让 delay 模式的池子回满——否则"战斗中没回复"证明不了什么。
                var refillTicks = delayTicks + (int)Math.Ceiling(rule.Damage / perTick - 1e-9);
                Assert.True(hitTicks[1] - hitTicks[0] > refillTicks, $"{cell}：间隔 {hitTicks[1] - hitTicks[0]} 不足以让 delay 模式回满（{refillTicks}）");

                // 战斗状态一直持续（玩家是存活的敌对仇恨来源、在交战范围内）：第 2 击落在第 1 击之后的池子上，没有任何回复，池子被打空。
                var first = Math.Max(0.0, rule.Max - rule.Damage);
                var second = Math.Max(0.0, first - rule.Damage);
                var changes = fp.Items("poise.changes");
                Assert.Equal(
                    new[]
                    {
                        FormattableString.Invariant($"{hitTicks[0]}:stake_ooc:{Fmt(rule.Max)}>{Fmt(first)}"),
                        FormattableString.Invariant($"{hitTicks[1]}:stake_ooc:{Fmt(first)}>{Fmt(second)}{(first > 0.0 && second <= 0.0 ? "!" : string.Empty)}"),
                    },
                    changes);
                Assert.Empty(fp.Items("poise.recoveries"));
                Assert.Equal(0.0, fp.Num("poise.accounting_mismatch"));
                Assert.Equal(0.0, fp.Num("poise.out_of_range"));
            }

            // 对照（不变量）：同样容量/伤害/延迟/速率的 delay 模式木桩（feel_poise_dynamic 的 stake_resilient）在同样量级的间隔下确实回满过。
            Assert.NotEmpty(FeelFp.Of("feel_poise_dynamic", "2d_action").Items("poise.recoveries"));
        }

        [Fact]
        public void PoiseBreakReset_RefillsOneResetIntervalAfterTheBreak_AndAnEmptyPoolHitDoesNotPostponeIt()
        {
            const string script = "feel_poise_break_reset";
            var rule = Rules();
            var resetTicks = FeelRules.T(CharacterNumber("feel.character.lab_poise_reset", "poise_break_reset_ms"));
            foreach (var cell in ActionCells)
            {
                var fp = FeelFp.Of(script, cell);
                var hitTicks = HitTicks(fp);
                Assert.Equal(4, hitTicks.Count);
                var changes = fp.Items("poise.changes");

                // 第 2 击破韧（2>0）起算；第 3 击落在已空池子上；回满恰在破韧后 resetTicks 个 tick（不被第 3 击顺延）；第 4 击在回满之后。
                var first = Math.Max(0.0, rule.Max - rule.Damage);
                var second = Math.Max(0.0, first - rule.Damage);
                Assert.True(first > 0.0 && second <= 0.0, "前提：第 2 击破韧");
                Assert.Equal(FormattableString.Invariant($"{hitTicks[1]}:stake_reset:{Fmt(first)}>{Fmt(second)}!"), changes[1]);
                Assert.Equal(FormattableString.Invariant($"{hitTicks[2]}:stake_reset:{Fmt(second)}>{Fmt(second)}"), changes[2]);
                var refillTick = hitTicks[1] + resetTicks;
                Assert.True(hitTicks[2] < refillTick && refillTick < hitTicks[3], "前提：第 3 击在回满之前、第 4 击在回满之后");
                Assert.Equal(new[] { refillTick }, fp.Items("poise.recoveries").Select(FeelFp.TickOf).ToArray());

                // 回满之后第 4 击落在满池子上、被挡成 Flinch；没有速率回复声明，若没有定时回满池子会一直是空的。
                Assert.Equal(FormattableString.Invariant($"{hitTicks[3]}:stake_reset:{Fmt(rule.Max)}>{Fmt(first)}"), changes[3]);
                Assert.Equal(1.0, fp.Num("poise.breaks"));
                Assert.Equal("Flinch:2;Stagger:2", fp.Text("reaction.reaction_counts"));
                Assert.Equal(0.0, fp.Num("poise.accounting_mismatch"));
                Assert.Equal(0.0, fp.Num("poise.out_of_range"));
            }
        }

        [Fact]
        public void PoiseDamageImpactScale_EachHitCostsTheDeclaredDamageTimesTheImpactMultiplier_AndDefaultStaysUnscaled()
        {
            const string script = "feel_poise_impact_scale";
            var rule = Rules();
            var meta = LabTestSupport.Script(script).Meta;
            foreach (var cell in ActionCells)
            {
                var impact = FeelRules.ForCell(cell).S("impact_class");
                var multiplier = meta.PoiseImpactScale.Single(p => p.Key == impact).Value;
                Assert.NotEqual(1.0, multiplier);
                var effective = rule.Damage * multiplier;

                var fp = FeelFp.Of(script, cell);
                var hitTicks = HitTicks(fp);
                var expected = new List<string>();
                var pool = rule.Max;
                var breaks = 0;
                foreach (var tick in hitTicks)
                {
                    var after = Math.Max(0.0, pool - effective);
                    var broken = pool > 0.0 && after <= 0.0;
                    breaks += broken ? 1 : 0;
                    expected.Add(FormattableString.Invariant($"{tick}:stake_resilient:{Fmt(pool)}>{Fmt(after)}{(broken ? "!" : string.Empty)}"));
                    pool = after;
                }

                Assert.Equal(expected, fp.Items("poise.changes"));
                Assert.Equal((double)breaks, fp.Num("poise.breaks"));
                Assert.Equal(0.0, fp.Num("poise.accounting_mismatch"));
                Assert.Equal(0.0, fp.Num("poise.out_of_range"));
                // 与不缩放的对照：不缩放时破韧所需击数是 容量 / 声明伤害，缩放后更多。
                Assert.True(Math.Ceiling(rule.Max / effective) > Math.Ceiling(rule.Max / rule.Damage));
            }

            // 缺省（脚本不声明缩放表）不缩放：既有动态韧性脚本不声明 poiseImpactScale。
            Assert.Empty(LabTestSupport.Script("feel_poise_dynamic").Meta.PoiseImpactScale);
        }

        private static string FullReaction()
        {
            // 完整反应 = 冲击等级映射（脚本里的击打都是 medium → Stagger，生产缺省映射表）。
            return new Core.Rules.Combat.HitFeelOptions().ImpactReactions[FeelRules.ForCell(Action).S("impact_class")].ToString();
        }

        private static string Fmt(double value) => Math.Round(value, 9).ToString("R", CultureInfo.InvariantCulture);
    }
}

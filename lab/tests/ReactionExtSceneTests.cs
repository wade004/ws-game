using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Rules.Combat;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 受击反应扩展脚本（M5-S2b，ADR-0145）的运行期验收：倒地/起身阶段时长、击退距离、起身无敌窗口、硬直保护期、格挡与弹反的命中结局，
    /// 全部由"数据根 <c>lab/fixtures/data/reaction</c> 里的毫秒/倍率 × 预设 × 标定 × 受击裁决规则"在用例里推出，不写死裸数。
    /// 复现（逐击的结局与阶段事件 tick）加不变量（阶段事件配对、无敌窗口无泄漏、保护期内的命中只剩 flinch）。
    /// </summary>
    public sealed class ReactionExtSceneTests
    {
        private static readonly string[] ActionCells = { "2d_action", "2_5d_action", "3d_action" };

        private const string Knockdown = "feel_react_knockdown";
        private const string Grace = "feel_react_grace";
        private const string Guard = "feel_react_guard";

        // ------------------------------------------------------------------ 数据规则读数

        private static double Write(string file, string id, string field)
        {
            var row = FeelRules.Row(Path.Combine("lab", "fixtures", "data", "reaction", file), id);
            foreach (var w in (JsonArray)row["writes"])
            {
                var o = (JsonObject)w;
                if (((JsonString)o["field"]).Value == field)
                {
                    return ((JsonNumber)o["value"]).Value;
                }
            }

            throw new InvalidOperationException($"{id} 没有写 {field}");
        }

        /// <summary>举盾技能时间线里 <c>guard_end</c> 标记的毫秒位置。</summary>
        private static double GuardEndMs()
        {
            var row = FeelRules.Row(Path.Combine("lab", "fixtures", "data", "reaction", "skill", "skill.def.json"), "skill.lab_react_guard");
            foreach (var m in (JsonArray)((JsonObject)row["timeline"])["markers"])
            {
                var o = (JsonObject)m;
                if (((JsonString)o["name"]).Value == "guard_end")
                {
                    return ((JsonNumber)o["at_ms"]).Value;
                }
            }

            throw new InvalidOperationException("举盾技能没有 guard_end 标记");
        }

        private static double Character(string id, string field) => Write(Path.Combine("feel", "feel.character.json"), id, field);

        private static double Action(string id, string field) => Write(Path.Combine("feel", "feel.action.json"), id, field);

        private static double ReferenceHeight(string cell)
        {
            var preset = LabTestSupport.Runner.Dataset.Catalog.GetScenario(cell).DefaultPreset;
            var id = "feel.calibration.lab_" + preset.Substring("feel.preset.".Length);
            var row = FeelRules.Row(Path.Combine("data", "_lab_action", "feel", "feel.calibration.json"), id);
            return ((JsonNumber)row["reference_height"]).Value;
        }

        private sealed class Phases
        {
            public int Stun;
            public int Down;
            public int Getup;
            public int Invuln;
            public int Grace;
            public int Total => Stun + Down + Getup;
        }

        /// <summary>某个木桩角色手感行的三段时长（硬直段含重击的硬直倍率）。</summary>
        private static Phases PhasesOf(string characterId, string actionId)
        {
            var scale = Action(actionId, "hit_stun_scale");
            var p = new Phases
            {
                Stun = FeelRules.T(Character(characterId, "hit_stun_ms") * scale),
                Down = FeelRules.T(Character(characterId, "downed_ms")),
                Getup = FeelRules.T(Character(characterId, "getup_ms")),
            };
            try
            {
                p.Invuln = Math.Min(p.Getup, FeelRules.T(Character(characterId, "getup_invuln_ms")));
            }
            catch (InvalidOperationException)
            {
                p.Invuln = 0;
            }

            try
            {
                p.Grace = FeelRules.T(Character(characterId, "stagger_grace_ms"));
            }
            catch (InvalidOperationException)
            {
                p.Grace = 0;
            }

            return p;
        }

        private static string[] Parts(string item) => item.Split(':');

        private static int Int(string s) => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        private static double Dbl(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ 倒地与起身

        [Fact]
        public void KnockdownPhases_AreSegmentedByTheDataMilliseconds_AndTheEventsArePaired()
        {
            var p = PhasesOf("feel.character.lab_react_downable", "feel.action.lab_react_massive");
            Assert.True(p.Stun > 0 && p.Down > 0 && p.Getup > 0 && p.Invuln > 0 && p.Invuln < p.Getup);
            foreach (var cell in ActionCells)
            {
                var fp = FeelFp.Of(Knockdown, cell);
                var downs = fp.Items("reactionext.knockdowns").Select(Parts).ToList();
                var ups = fp.Items("reactionext.getups").Select(Parts).ToList();
                var finishes = fp.Items("reactionext.getup_finishes").Select(Parts).ToList();
                Assert.Equal(2, downs.Count);
                Assert.Equal(2, ups.Count);
                Assert.Equal(2, finishes.Count);

                // 事件里带的是数据决定的倒地段/起身段/无敌 tick 数；实测时长（事件间隔）与之相等（本脚本没有顿帧打断倒地/起身）。
                for (var i = 0; i < 2; i++)
                {
                    Assert.Equal(p.Down, Int(downs[i][2]));
                    Assert.Equal(p.Getup, Int(downs[i][3]));
                    Assert.Equal(p.Getup, Int(ups[i][2]));
                    Assert.Equal(p.Invuln, Int(ups[i][3]));
                    Assert.Equal(p.Down, Int(ups[i][0]) - Int(downs[i][0]));
                    Assert.Equal(p.Getup, Int(finishes[i][0]) - Int(ups[i][0]));
                }

                Assert.Equal(new[] { p.Down, p.Down }, fp.Items("reactionext.down_ticks").Select(Int).ToArray());
                Assert.Equal(new[] { p.Getup, p.Getup }, fp.Items("reactionext.getup_ticks").Select(Int).ToArray());
                Assert.Equal(0.0, fp.Num("reactionext.phase_unpaired"));

                // 反应落地事件的总时长 = 硬直 + 倒地 + 起身；击倒事件落在"硬直开始 + 硬直段"（硬直从目标顿帧结束后起算）。
                var reactions = fp.Items("reaction.reactions").Select(Parts).Where(r => r[2] == "Knockdown").ToList();
                Assert.Equal(2, reactions.Count);
                var frozen = Int(fp.Text("hitstop.frozen_ticks_targets").Split('=')[1]) / reactions.Count;
                for (var i = 0; i < 2; i++)
                {
                    Assert.Equal(p.Total, Int(reactions[i][3]));
                    Assert.Equal(p.Stun + frozen + 1, Int(downs[i][0]) - Int(reactions[i][0]));
                }
            }
        }

        [Fact]
        public void GetupInvulnerability_AvoidsExactlyTheHitInsideTheWindow_AndNothingLeaks()
        {
            var p = PhasesOf("feel.character.lab_react_downable", "feel.action.lab_react_massive");
            foreach (var cell in ActionCells)
            {
                var fp = FeelFp.Of(Knockdown, cell);
                var ups = fp.Items("reactionext.getups").Select(Parts).ToList();
                var hits = fp.Items("reactionext.guard_results").Select(Parts).ToList();
                Assert.Equal(3, hits.Count);

                // 第 2 击落在第 1 次起身的无敌窗口 [起身开始, 起身开始 + 无敌 tick 数) 里：Invulnerable、不扣血；其余两击是普通命中。
                var gs = Int(ups[0][0]);
                var second = Int(hits[1][0]);
                Assert.True(second >= gs && second < gs + p.Invuln, $"{cell}：第 2 击 tick {second} 不在无敌窗口 [{gs}, {gs + p.Invuln})");
                Assert.Equal("Invulnerable", hits[1][2]);
                Assert.Equal(0.0, Dbl(hits[1][3]));
                Assert.Equal("Hit", hits[0][2]);
                Assert.Equal("Hit", hits[2][2]);
                Assert.True(Dbl(hits[0][3]) > 0.0);
                Assert.Equal(Dbl(hits[0][3]), Dbl(hits[2][3]), 9);
                Assert.Equal(1.0, fp.Num("reactionext.invuln_hits"));
                Assert.Equal(0.0, fp.Num("reactionext.invuln_leaks"));
                Assert.True(Int(hits[2][0]) >= Int(fp.Items("reactionext.getup_finishes").Select(Parts).First()[0]), "第 3 击在第 1 次起身结束之后");
            }
        }

        // ------------------------------------------------------------------ 击退距离

        [Fact]
        public void KnockbackDistance_FollowsTheActionRowTimesImpactMultiplierTimesBodyHeight_AndZeroMeansNoPush()
        {
            var multiplier = new HitFeelOptions().KnockbackImpactMultipliers["massive"];
            foreach (var cell in ActionCells)
            {
                var expected = Action("feel.action.lab_react_massive", "knockback_distance") * multiplier * ReferenceHeight(cell);
                var peaks = FeelFp.Of(Knockdown, cell).Items("reactionext.knockback_peaks").Select(Parts).ToList();
                Assert.Equal(2, peaks.Count);
                foreach (var peak in peaks)
                {
                    Assert.Equal(expected, Dbl(peak[2]), 6);
                }

                // 对照：不击退的重击（距离 0）时每次落地反应的位移都是 0。
                Assert.Equal(0.0, Action("feel.action.lab_react_still", "knockback_distance"));
                foreach (var peak in FeelFp.Of(Grace, cell).Items("reactionext.knockback_peaks").Select(Parts))
                {
                    Assert.Equal(0.0, Dbl(peak[2]), 9);
                }
            }
        }

        // ------------------------------------------------------------------ 硬直保护期

        [Fact]
        public void StaggerGrace_TheGracedStakeCannotBeChained_TheFreeControlIs()
        {
            var graced = PhasesOf("feel.character.lab_react_graced", "feel.action.lab_react_still");
            var free = PhasesOf("feel.character.lab_react_free", "feel.action.lab_react_still");
            Assert.Equal(graced.Total, free.Total); // 两只木桩的硬直/倒地/起身时长相同，唯一的差别是保护期
            Assert.True(graced.Grace > 0 && free.Grace == 0);

            foreach (var cell in ActionCells)
            {
                var fp = FeelFp.Of(Grace, cell);
                var reactions = fp.Items("reaction.reactions").Select(Parts).ToList();
                var hitTicks = fp.Items("spatialhit.confirm_ticks").Select(FeelFp.TickOf).Distinct().ToList();

                // 带保护期的木桩：任意两次完整倒地的间隔 ≥ 硬直 + 倒地 + 起身 + 保护期；保护期内的每一击都只剩 flinch。
                var gracedReactions = reactions.Where(r => r[1] == "stake_graced").ToList();
                var knockdownTicks = gracedReactions.Where(r => r[2] == "Knockdown").Select(r => Int(r[0])).ToList();
                Assert.True(knockdownTicks.Count >= 2, "前提：保护期过后确实还能再被击倒");
                for (var i = 1; i < knockdownTicks.Count; i++)
                {
                    Assert.True(knockdownTicks[i] - knockdownTicks[i - 1] >= graced.Total + graced.Grace,
                        $"{cell}：两次击倒间隔 {knockdownTicks[i] - knockdownTicks[i - 1]} 小于 {graced.Total + graced.Grace}");
                }

                foreach (var r in gracedReactions.Where(r => r[2] == "Flinch"))
                {
                    var lastKnockdown = knockdownTicks.Last(t => t < Int(r[0]));
                    Assert.True(Int(r[0]) - lastKnockdown < graced.Total + graced.Grace, "flinch 只出现在保护期内");
                    Assert.Equal(0, Int(r[3]));
                }

                // 保护期里的击打没有一击漏成完整反应：每一击要么是 knockdown（间隔够长），要么是 flinch。
                Assert.Equal(hitTicks.Count, gracedReactions.Count);
                Assert.Equal(gracedReactions.Count, knockdownTicks.Count + gracedReactions.Count(r => r[2] == "Flinch"));
                Assert.Contains("Flinch", gracedReactions.Select(r => r[2]));

                // 不变量：被锁住的硬直 tick 数——带保护期的木桩 = 完整倒地次数 × 总时长（段与段互不叠加）；不带的更长。
                var staggered = fp.Text("reaction.staggered_ticks").Split(';').Select(x => x.Split('=')).ToDictionary(x => x[0], x => Int(x[1]));
                Assert.Equal(knockdownTicks.Count * graced.Total, staggered["stake_graced"]);
                Assert.True(staggered["stake_free"] > staggered["stake_graced"]);

                // 对照：不带保护期的木桩每一击都是完整击倒，每次都在前一次没走完时刷新（被连击锁住）。
                var freeReactions = reactions.Where(r => r[1] == "stake_free").ToList();
                Assert.Equal(hitTicks.Count, freeReactions.Count);
                Assert.All(freeReactions, r => Assert.Equal("Knockdown", r[2]));
                Assert.Equal(freeReactions.Count - 1, (int)Dbl(fp.Text("reactionext.chained").Split('=')[1]));
                Assert.Equal(1.0, fp.Num("reactionext.chained_targets"));
                Assert.Equal(0.0, fp.Num("reactionext.phase_unpaired"));
            }
        }

        // ------------------------------------------------------------------ 格挡与弹反

        [Fact]
        public void GuardAndParry_FollowTheWindowAndTheDamageScale_AndBounceTheAttacker()
        {
            const string guarder = "feel.character.lab_react_guarder";
            var parryTicks = FeelRules.T(Character(guarder, "guard_parry_window_ms"));
            var scale = Character(guarder, "guard_damage_scale");
            var guardEndTick = FeelRules.T(GuardEndMs());
            foreach (var cell in ActionCells)
            {
                var fp = FeelFp.Of(Guard, cell);
                var hits = fp.Items("reactionext.guard_results").Select(Parts).ToList();
                Assert.Equal(3, hits.Count);

                // 弹反窗口 = 格挡开始（脚本第 0 tick 举盾）后 guard_parry_window_ms；第 1 击在窗口内、第 2 击在窗口后、第 3 击在格挡窗口结束之后。
                Assert.True(Int(hits[0][0]) <= parryTicks + 1 && Int(hits[1][0]) > parryTicks + 1 && Int(hits[2][0]) > guardEndTick);
                Assert.Equal(new[] { "Parry", "Block", "Hit" }, hits.Select(h => h[2]).ToArray());

                // 伤害：弹反 0；格挡 = 同一击的普通伤害 × 格挡伤害倍率；格挡结束后与普通命中相同。
                var plain = Dbl(hits[2][3]);
                Assert.True(plain > 0.0);
                Assert.Equal(0.0, Dbl(hits[0][3]));
                Assert.Equal(plain * scale, Dbl(hits[1][3]), 9);

                // 攻击方（玩家）被弹开：弹反与格挡各一次 stagger_light，时长 = 玩家档案的基础硬直；普通命中不弹。
                var bounce = FeelRules.ForCell(cell).Ticks("hit_stun_ms");
                var reactions = fp.Items("reaction.reactions").Select(Parts).ToList();
                var playerHits = reactions.Where(r => r[1] == "player").ToList();
                Assert.Equal(2, playerHits.Count);
                Assert.All(playerHits, r => { Assert.Equal("StaggerLight", r[2]); Assert.Equal(bounce, Int(r[3])); });
                Assert.Equal(new[] { Int(hits[0][0]), Int(hits[1][0]) }, playerHits.Select(r => Int(r[0])).ToArray());

                // 防御方：弹反不起反应；被格挡的反应封顶 flinch；格挡结束后的普通命中受角色反应上限同样封顶 flinch。
                var guarderReactions = reactions.Where(r => r[1] == "guarder").Select(r => r[2]).ToArray();
                Assert.Equal(new[] { "Flinch", "Flinch" }, guarderReactions);
                Assert.Equal("Block:1;Hit:1;Parry:1", fp.Text("reaction.hit_results"));
            }
        }

        // ------------------------------------------------------------------ 条件组只在声明了新字段的脚本里出现

        [Fact]
        public void ReactionExtGroup_AppearsOnlyWhereTheNewMechanismsFire()
        {
            foreach (var script in new[] { Knockdown, Grace, Guard })
            {
                Assert.True(FeelFp.Of(script, "2d_action").Has("reactionext"), script);
            }

            // 目标选择式格子没有时间线（手感行与格挡窗口随时间线一起没了）：没有新机制，没有该组。
            Assert.False(FeelFp.Of(Knockdown, "2d_targeted").Has("reactionext"));
            // 既有脚本（击退、静态/动态韧性、三连击）从不出现该组，基线逐字不变。
            foreach (var script in new[] { "feel_combo3", "feel_stake_poise", "feel_poise_dynamic", "feel_elite_armor", "space.launch" })
            {
                var cell = script.StartsWith("space.", StringComparison.Ordinal) ? "volume_action" : "2d_action";
                Assert.False(FeelFp.Of(script, cell).Has("reactionext"), script);
            }
        }
    }
}

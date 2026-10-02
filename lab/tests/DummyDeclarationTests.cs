using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Combat;
using Core.Rules.Common;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 靶子数据的两项声明（手感设计 06 第 2 节、第 10 节勘误 4）：
    /// <list type="bullet">
    /// <item>可破坏障碍：靶子条目声明 <c>block_half_extent</c>（<c>kind = breakable</c> 缺省 0.5）即动态阻挡，任何场景都生效——
    /// 出场时追加占位矩形、被打死后经导航接口批量替换去掉；不声明的靶子不挡路（缺省不变）。</item>
    /// <item>不死木桩可选韧性：靶子条目声明 <c>poise</c>，写进韧性属性，受击裁决读它；不声明等价韧性为 0。</item>
    /// </list>
    /// 期望值全部由数据字段与受击裁决规则在用例里算出，用例里没有裸数。
    /// </summary>
    public sealed class DummyDeclarationTests
    {
        private const string BaseSetId = "lab.dummy_set.lab_standard";
        private const string ActionSetId = "lab.dummy_set.lab_action";
        private const string Attack = "input.action.attack";
        private const string Move = "input.action.move";

        // ---------- 辅助 ----------

        /// <summary>从脚本实际运行的数据集里取靶子集条目（手感场景的靶子集在脚本额外数据根里，不在基础数据集里）。</summary>
        private static LabDummy Entry(LabRunner runner, string setId, string name, InputScript? script = null) =>
            (script == null ? runner.Dataset : runner.DatasetFor(script)).Catalog.GetDummySet(new Id(setId)).Entries.First(e => e.Name == name);

        /// <summary>数据来源（框架根、实验室根、脚本额外根）一律套同一个表文本改写，改写只活在内存里。</summary>
        private static LabRunner RunnerWithRewrite(Func<string, string, string?> rewrite)
        {
            var root = LabTestSupport.RepoRoot();
            var sources = new List<IDataSource>
            {
                new TableOverlayDataSource(LabDataSources.FromDirectory(Path.Combine(root, "data", "_framework")), rewrite),
                new TableOverlayDataSource(LabDataSources.FromDirectory(Path.Combine(root, "data", "_lab")), rewrite),
            };
            return new LabRunner(
                LabDataset.Load(sources), null,
                rel => new TableOverlayDataSource(LabDataSources.FromDirectory(Path.Combine(root, rel)), rewrite));
        }

        /// <summary>从起点朝 +x 走 <paramref name="walkTicks"/> 个 tick 后停；之后按给定 tick 各挥一刀；最后再朝 +x 走到结束。</summary>
        private static InputScript WalkSwingWalk(string id, string group, Vec2 start, int walkTicks, IReadOnlyList<int> swings, int walkAgain, int duration)
        {
            var events = new List<ScriptEvent>
            {
                new ScriptEvent(0, Move, ScriptEventKind.Axis, new Vec2(1, 0)),
                new ScriptEvent(walkTicks, Move, ScriptEventKind.Axis, Vec2.Zero),
            };
            foreach (var s in swings)
            {
                events.Add(new ScriptEvent(s, Attack, ScriptEventKind.Press));
                events.Add(new ScriptEvent(s + 1, Attack, ScriptEventKind.Release));
            }

            if (walkAgain >= 0)
            {
                events.Add(new ScriptEvent(walkAgain, Move, ScriptEventKind.Axis, new Vec2(1, 0)));
            }

            var script = LabTestSupport.Build(id, duration, events, group);
            script.Meta.PlayerStart = start;
            return script;
        }

        private static double EndX(LabRecording r) => r.Ticks[r.Ticks.Count - 1].Position.X;

        // ---------- 可破坏障碍：基础靶子集里的真表达 ----------

        [Fact]
        public void BaseSetBreakable_DeclaresBlocking_AndBlocksTheWalkUntilBroken()
        {
            var runner = LabTestSupport.Runner;
            var breakable = Entry(runner, BaseSetId, "breakable");
            Assert.Equal(0.5, breakable.DeclaredBlockHalfExtent); // 数据里显式声明，不是靠 kind 缺省
            var half = breakable.BlockHalfExtent!.Value;
            var leftEdge = breakable.Position.X - half;

            // 玩家从障碍西侧 4 个单位处朝 +x 走，被挡住后挥刀打碎（每刀间隔留够冷却），再朝 +x 走过去。
            var swings = new List<int>();
            for (var i = 0; i < 8; i++)
            {
                swings.Add(70 + i * 30);
            }

            var script = WalkSwingWalk(
                "breakable_base_walk", "breakable", new Vec2(breakable.Position.X - 4, breakable.Position.Y), 60, swings, 330, 420);
            var recording = runner.Record(script, "2d_targeted");

            var died = recording.Events.Where(e => e.Kind == "died" && e.Target == "breakable").ToList();
            Assert.Single(died);
            var killTick = died[0].Tick;

            // 击碎之前玩家从没进过障碍矩形（被挡在左缘外）；击碎之后走过了障碍中心。
            for (var t = 0; t < killTick; t++)
            {
                Assert.True(recording.Ticks[t].Position.X <= leftEdge + 1e-9, $"tick {t} 玩家 x={recording.Ticks[t].Position.X} 进入了障碍（左缘 {leftEdge}）");
            }

            Assert.True(EndX(recording) > breakable.Position.X, $"击碎后玩家应走过障碍，终点 x={EndX(recording)}");

            // 阻挡变更恰好一次，落在击杀 tick；剩余阻挡数 = 地形阻挡数（障碍的占位矩形已移除）；
            // 阻挡版本 = 三次整批替换（地形、地形加障碍、移除障碍）。
            var changes = recording.Events.Where(e => e.Kind == "blocking_changed").ToList();
            Assert.Single(changes);
            Assert.Equal(killTick, changes[0].Tick);
            var arena = runner.Dataset.Catalog.GetArena(runner.Dataset.Catalog.GetScenario("2d_targeted").ArenaId);
            Assert.Equal(arena.Blocks.Count, (int)changes[0].Amount);
            Assert.Equal("v3", changes[0].Detail);

            // 打碎所需命中数 = 障碍生命 / 单次伤害（生命来自创建物模板的耐力）。
            var hp = ((Core.Foundation.Common.Json.JsonNumber)((Core.Foundation.Common.Json.JsonObject)FeelRules.Row(
                Path.Combine("data", "_lab", "creature", "creature.template.json"), "creature.lab_breakable")["base_stats"])["stat.stamina"]).Value;
            var damages = recording.Events.Where(e => e.Kind == "damage" && e.Target == "breakable").ToList();
            var dealt = 0.0;
            var hitsToKill = 0;
            foreach (var d in damages)
            {
                dealt += d.Amount;
                hitsToKill++;
                if (dealt >= hp)
                {
                    break;
                }
            }

            Assert.True(dealt >= hp);
            Assert.Equal(killTick, damages[hitsToKill - 1].Tick);
        }

        [Fact]
        public void BreakableDefaultExtent_AppliesWhenNotDeclared_AndDeclaredValueMovesTheStopPointByTheSameAmount()
        {
            // 重写数据：把障碍的声明去掉（kind = breakable 缺省 0.5）与改成 1.0，停步位置随矩形左缘等量移动。
            Func<string, string, string?> Declared(double? half) => (table, text) =>
                table == "lab.dummy_set" ? text.Replace(", \"block_half_extent\": 0.5", half.HasValue ? ", \"block_half_extent\": " + half.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty) : null;

            double StopX(double? half)
            {
                var runner = RunnerWithRewrite(Declared(half));
                var entry = Entry(runner, BaseSetId, "breakable");
                var script = WalkSwingWalk("breakable_stop", "breakable", new Vec2(entry.Position.X - 4, entry.Position.Y), 80, Array.Empty<int>(), -1, 100);
                var recording = runner.Record(script, "2d_targeted");
                return EndX(recording);
            }

            var byDefault = StopX(null);
            var half05 = StopX(0.5);
            var half10 = StopX(1.0);
            Assert.Equal(byDefault, half05, 9); // 缺省 = 0.5（旧行为保留）
            Assert.Equal(half05 - 0.5, half10, 3); // 阻挡左缘西移 0.5，停步位置同样西移 0.5
        }

        [Fact]
        public void NonBreakableDummy_WithoutDeclaration_NeverBlocks_AndNeverChangesBlocking()
        {
            // 不声明就不挡路：木桩（kind = stake）原样可以穿过，也没有任何阻挡变更。
            var runner = LabTestSupport.Runner;
            var stake = Entry(runner, BaseSetId, "stake");
            Assert.Null(stake.DeclaredBlockHalfExtent);
            Assert.Null(stake.BlockHalfExtent);
            var script = WalkSwingWalk("stake_walk_through", "stake", new Vec2(stake.Position.X - 2, stake.Position.Y), 100, Array.Empty<int>(), -1, 110);
            var recording = runner.Record(script, "2d_targeted");
            Assert.True(EndX(recording) > stake.Position.X, "木桩没有声明阻挡，玩家应穿过它");
            Assert.DoesNotContain(recording.Events, e => e.Kind == "blocking_changed");
        }

        [Fact]
        public void AnyKindThatDeclaresBlocking_BlocksForGood_UnlessItDies()
        {
            // 不死木桩声明阻挡：永远挡着（没有死亡，也就没有阻挡变更）。
            var runner = RunnerWithRewrite((table, text) =>
                table == "lab.dummy_set" ? text.Replace("\"group\": \"stake\" }", "\"group\": \"stake\", \"block_half_extent\": 0.5 }") : null);
            var stake = Entry(runner, BaseSetId, "stake");
            Assert.Equal(0.5, stake.BlockHalfExtent);
            var script = WalkSwingWalk("stake_blocks", "stake", new Vec2(stake.Position.X - 4, stake.Position.Y), 100, Array.Empty<int>(), -1, 110);
            var recording = runner.Record(script, "2d_targeted");
            Assert.True(EndX(recording) <= stake.Position.X - stake.BlockHalfExtent!.Value + 1e-9, $"玩家不应进入木桩的阻挡矩形，终点 x={EndX(recording)}");
            Assert.DoesNotContain(recording.Events, e => e.Kind == "blocking_changed");
        }

        // ---------- 不死木桩可选韧性 ----------

        private static readonly string[] PoiseCells = { "2d_action", "2_5d_action", "3d_action" };

        private static string Rewrite(double poise, string table, string text) =>
            text.Replace("\"poise\": 1", "\"poise\": " + poise.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>三段连招每一击的（硬直强度，冲击等级）：前两段取预设缺省，终结击取覆盖行（同 feel_combo3 的数据口径）。</summary>
        private static List<(double Power, string Impact)> ComboHits()
        {
            var p = FeelRules.ForCell("2d_action");
            var finisher = "feel.action.lab_a_finisher";
            return new List<(double, string)>
            {
                (p.N("stagger_power"), p.S("impact_class")),
                (p.N("stagger_power"), p.S("impact_class")),
                (FeelRules.OverrideNumber("feel.action", finisher, "stagger_power")!.Value, FeelRules.OverrideText("feel.action", finisher, "impact_class")!),
            };
        }

        private static List<string> Reactions(LabRecording recording) =>
            recording.Feel!.Events.Where(e => e.Kind == "reaction").Select(e => e.Detail).ToList();

        [Fact]
        public void DeclaredPoise_TurnsEveryHitWithPowerNotAbovePoiseIntoFlinch_OtherwiseImpactMapping()
        {
            var expectedMap = new HitFeelOptions().ImpactReactions;
            var hits = ComboHits();
            foreach (var poise in new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 3.0 })
            {
                var runner = RunnerWithRewrite((table, text) => table == "lab.dummy_set" ? Rewrite(poise, table, text) : null);
                var script = LabTestSupport.Script("feel_stake_poise");
                Assert.Equal(poise, Entry(runner, ActionSetId, "stake_tough", script).Poise);

                // 动作式格子（预设装配了受击裁决）：每一击反应 = 强度不高于韧性 ? Flinch : 冲击等级映射（本靶子没有 reaction_cap）。
                var recording = runner.Record(script, "2d_action");
                var expected = hits.Select(h => h.Power <= poise ? HitReaction.Flinch.ToString() : expectedMap[h.Impact].ToString()).ToList();
                Assert.Equal(expected, Reactions(recording));
            }
        }

        [Fact]
        public void UndeclaredPoise_IsTheSameAsPoiseZero_AndEqualsTheImpactMapping()
        {
            // 不声明 = 韧性 0：反应序列等于"全按冲击等级映射"，也与声明 poise 0 的韧性木桩逐击一致（缺省不变）。
            var expectedMap = new HitFeelOptions().ImpactReactions;
            var combo = LabTestSupport.Script("feel_combo3");
            Assert.Null(Entry(LabTestSupport.Runner, ActionSetId, "stake", combo).Poise);
            var plain = LabTestSupport.Runner.Record(combo, "2d_action");
            Assert.Equal(ComboHits().Select(h => expectedMap[h.Impact].ToString()).ToList(), Reactions(plain));

            var declaredZero = RunnerWithRewrite((table, text) => table == "lab.dummy_set" ? Rewrite(0.0, table, text) : null);
            var zero = declaredZero.Record(LabTestSupport.Script("feel_stake_poise"), "2d_action");
            Assert.Equal(Reactions(plain), Reactions(zero));
        }

        [Fact]
        public void DeclaredPoise_DoesNotAffectCellsWithoutAJudgingProfile_AndStakeStillNeverDies()
        {
            // 目标选择式格子（经典预设，受击裁决整体不装）不受韧性影响：每击反应都是 None；木桩仍吃伤害不死。
            var recording = LabTestSupport.Runner.Record(LabTestSupport.Script("feel_stake_poise"), "2d_targeted");
            Assert.All(Reactions(recording), r => Assert.Equal(HitReaction.None.ToString(), r));
            Assert.Empty(recording.Events.Where(e => e.Kind == "died"));
            Assert.Equal(3, recording.Events.Count(e => e.Kind == "damage"));
        }

        [Fact]
        public void DeclaredPoise_WithoutTheStatDefinition_FailsWithAClearMessage()
        {
            // 数据集里没有 stat.poise 的定义（没叠加声明它的数据根）时声明 poise，给出明确诊断而不是静默忽略。
            var runner = RunnerWithRewrite((table, text) =>
                table == "lab.dummy_set" ? text.Replace("\"group\": \"stake\" }", "\"group\": \"stake\", \"poise\": 1 }") : null);
            var script = LabTestSupport.Build("poise_no_stat", 10, new List<ScriptEvent>(), "stake");
            var ex = Assert.Throws<LabFormatException>(() => runner.Record(script, "2d_targeted"));
            Assert.Contains("stat.poise", ex.Message);
        }
    }
}

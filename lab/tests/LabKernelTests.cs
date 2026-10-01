using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 实验室内核验收（06 第 6 节第 4/5 层的第一批机器证据）：输入脚本往返、输入脚本双跑逐字节一致、
    /// 三个帧率上限下逻辑组一致、平面各格子（2D/2.5D/3D × 两种结算模式）逻辑组逐字节一致、
    /// 标准脚本 × 六格全部与入库基线一致、故意改速度基线比较失败且不留改动、度量组可扩展、格子可运行状态显式化。
    /// </summary>
    public sealed class LabKernelTests
    {
        private static readonly MetricClass[] Deterministic = { MetricClass.Logic, MetricClass.Presentation };

        // ---------- 脚本格式 ----------

        [Fact]
        public void Script_RoundTripsThroughCanonicalJson()
        {
            foreach (var script in LabTestSupport.StandardScripts())
            {
                var text = script.ToJson();
                var again = InputScript.Parse(text).ToJson();
                Assert.Equal(text, again);
                Assert.Equal(1, script.Meta.ScriptVersion);
                // 换装脚本（格式版本 2）按动作时间线的 1/60 秒换算，固定 60；其余标准脚本仍是 50。
                Assert.Equal(script.Meta.Scene == "equip" ? 60 : 50, script.Meta.TickRate);
            }
        }

        [Fact]
        public void StandardScriptSet_HasTheTenScripts()
        {
            var ids = new List<string>();
            foreach (var s in LabTestSupport.StandardScripts())
            {
                ids.Add(s.Meta.ScriptId);
            }

            Assert.Equal(
                new[]
                {
                    "attack_then_stop", "attack_while_moving", "diagonal", "equip_cycle", "group_hit", "move_small_axis",
                    "move_tap", "pillar_loop", "reverse_180", "wall",
                },
                ids);
        }

        [Fact]
        public void ReplayImporter_ConvertsRealTimestampsToTicks()
        {
            var meta = new ScriptMeta { ScriptId = "replay_demo", DurationTicks = 30 };
            var script = ScriptReplayImporter.FromTimedInputs(meta, new[]
            {
                new ScriptReplayImporter.TimedInput(0.000, "input.action.move", ScriptEventKind.Axis, new Vec2(1, 0)),
                new ScriptReplayImporter.TimedInput(0.105, "input.action.attack", ScriptEventKind.Press),
                new ScriptReplayImporter.TimedInput(0.399, "input.action.attack", ScriptEventKind.Release),
            });

            Assert.Equal(new[] { 0, 5, 19 }, new[] { script.Events[0].Tick, script.Events[1].Tick, script.Events[2].Tick });
            Assert.Equal(0.105, script.Events[1].RealTimestamp);
            Assert.Equal(script.ToJson(), InputScript.Parse(script.ToJson()).ToJson());
        }

        [Fact]
        public void Script_RejectsUnknownKindAndNewerFormat()
        {
            var bad = "{\"meta\":{\"scriptId\":\"x\",\"scriptVersion\":1,\"tickRate\":50,\"frameRateCap\":60,\"durationTicks\":5},"
                + "\"events\":[{\"tick\":0,\"action\":\"a\",\"kind\":\"tap\"}]}";
            Assert.Throws<LabFormatException>(() => InputScript.Parse(bad));

            var newer = "{\"formatVersion\":99,\"meta\":{},\"events\":[]}";
            Assert.Throws<LabFormatException>(() => InputScript.Parse(newer));
        }

        // ---------- 双跑逐字节一致 ----------

        [Fact]
        public void DoubleRun_IsByteIdentical_ForEveryStandardScript()
        {
            var runner = LabTestSupport.Runner;
            foreach (var script in LabTestSupport.StandardScripts())
            {
                var a = runner.Run(script, "2_5d_action");
                var b = runner.Run(script, "2_5d_action");

                // 键 + 逻辑组 + 表现组（模拟时间决定的，可复现）逐字节一致；实时类度量（墙钟、分配）不参与字节比较。
                Assert.Equal(LabJson.Write(a.Key), LabJson.Write(b.Key));
                Assert.Equal(a.Project(runner.Registry, Deterministic), b.Project(runner.Registry, Deterministic));
            }
        }

        [Fact]
        public void DoubleRun_RecordingTimelinesAreIdentical()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("attack_while_moving");
            var a = RecordingWriter.ToJson(runner.Record(script, "3d_targeted"));
            var b = RecordingWriter.ToJson(runner.Record(script, "3d_targeted"));

            // 完整记录里含真实时间采样（逐帧毫秒与分配），去掉该段后其余（逻辑时间线 + 表现时间线）逐字节相同。
            Assert.Equal(StripRealTime(a), StripRealTime(b));
        }

        private static string StripRealTime(string recordingJson)
        {
            var cut = recordingJson.IndexOf("\"realTime\"", StringComparison.Ordinal);
            Assert.True(cut > 0, "记录里应有 realTime 段");
            return recordingJson.Substring(0, cut);
        }

        // ---------- 三个帧率上限 ----------

        [Theory]
        [InlineData("move_tap")]
        [InlineData("wall")]
        [InlineData("attack_while_moving")]
        [InlineData("group_hit")]
        public void LogicGroups_AreIdentical_At30_60_120FrameRateCaps(string scriptId)
        {
            var runner = LabTestSupport.Runner;
            var baseScript = LabTestSupport.Script(scriptId);
            var logic = new List<string>();
            var frames = new List<double>();
            foreach (var cap in new[] { 30, 60, 120 })
            {
                var fp = runner.Run(LabTestSupport.WithFrameRate(baseScript, cap), "2d_targeted");
                logic.Add(fp.Project(runner.Registry, MetricClass.Logic));
                frames.Add(((JsonNumber)((JsonObject)fp.Groups["performance"])["frames"]).Value);
            }

            Assert.Equal(logic[0], logic[1]);
            Assert.Equal(logic[0], logic[2]);

            // 证明三个帧率上限确实跑出了不同数量的表现帧（不是三次其实是同一个帧率）。
            Assert.True(frames[0] < frames[1] && frames[1] < frames[2], $"帧数应随帧率上限递增：{string.Join(",", frames)}");
        }

        // ---------- 跨格子不变量 ----------

        [Fact]
        public void PlanarCells_ShareIdenticalLogicFingerprints_ForSameScriptAndPreset()
        {
            var runner = LabTestSupport.Runner;
            foreach (var script in LabTestSupport.StandardScripts())
            {
                // 同为目标选择式、同一预设：2D / 2.5D / 3D 三个平面格子逻辑组逐字节一致（06 第 1.1 节不变量 1）。
                var reference = runner.Run(script, "2d_targeted").Project(runner.Registry, MetricClass.Logic);
                foreach (var cell in new[] { "2_5d_targeted", "3d_targeted" })
                {
                    Assert.Equal(reference, runner.Run(script, cell).Project(runner.Registry, MetricClass.Logic));
                }
            }
        }

        [Fact]
        public void ActionCells_BehaveLikeTargetedCells_ForScriptsWithoutFeelAssembly()
        {
            var runner = LabTestSupport.Runner;
            foreach (var script in LabTestSupport.StandardScripts())
            {
                var targeted = runner.Run(script, "2d_targeted").Project(runner.Registry, MetricClass.Logic);
                var action = runner.Run(script, "2d_action").Project(runner.Registry, MetricClass.Logic);
                Assert.Equal(targeted, action);
            }
        }

        [Fact]
        public void PresentationGroups_DifferAcrossCellsOnlyWhereProjectionMatters()
        {
            // 朝向表达只影响呈现：连续朝向格子不写方向档位，量化格子写 8 档——指纹里表现组允许按格子不同，
            // 但"逻辑组相同、表现组位置轨迹相同"（呈现差异只在朝向字段，不在位置）。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("diagonal");
            var quantized = runner.Record(script, "2d_targeted");
            var continuous = runner.Record(script, "3d_targeted");
            Assert.Equal(quantized.Frames.Count, continuous.Frames.Count);
            for (var i = 0; i < quantized.Frames.Count; i++)
            {
                Assert.Equal(quantized.Frames[i].ViewPosition, continuous.Frames[i].ViewPosition);
            }

            Assert.Contains(quantized.Frames, f => f.ViewDirectionCount == 8);
            Assert.All(continuous.Frames, f => Assert.Equal(0, f.ViewDirectionCount));
        }

        // ---------- 基线 ----------

        [Fact]
        public void AllStandardScripts_OnAllSixCells_MatchCommittedBaselines()
        {
            // 旧标准脚本（十个）的基线与手感场景脚本的基线分开把关（后者见 FeelSceneTests）；这里的口径与扩展前一字不差。
            var results = new List<CellResult>();
            foreach (var script in LabTestSupport.StandardScripts())
            {
                results.AddRange(LabSuite.Check(LabTestSupport.Runner, LabTestSupport.FixturesDir, script.Meta.ScriptId));
            }

            Assert.Equal(10 * 6, results.Count);
            var failures = new StringBuilder();
            foreach (var r in results)
            {
                if (r.Status != CellStatus.Pass)
                {
                    failures.Append(r.Script).Append(" @ ").Append(r.Cell).Append(' ').Append(r.Status).Append('\n');
                    if (r.Diff != null)
                    {
                        failures.Append(r.Diff.Format());
                    }
                }
            }

            Assert.True(failures.Length == 0, "基线比较失败：\n" + failures);
        }

        [Fact]
        public void ChangingMoveSpeed_MakesBaselineComparisonFail_WithReadableDiff_AndLeavesNothingBehind()
        {
            var baselinePath = LabFixtures.BaselinePath(LabTestSupport.FixturesDir, "move_tap");
            var before = File.ReadAllBytes(baselinePath);

            var changed = false;
            var tweaked = LabTestSupport.CreateRunner((table, text) =>
            {
                if (table == "stat.definition" && text.Contains("\"default_base\": 5"))
                {
                    changed = true;
                    return text.Replace("\"default_base\": 5", "\"default_base\": 6");
                }

                return null;
            });

            var results = LabSuite.Check(tweaked, LabTestSupport.FixturesDir, onlyScript: "move_tap", onlyCell: "2d_targeted");
            Assert.True(changed, "内存改写应当真的命中 stat.move_speed 的 default_base（否则本用例是空转）");

            var result = Assert.Single(results);
            Assert.Equal(CellStatus.Diff, result.Status);
            var report = result.Diff!.Format();

            // 读得懂：指出哪个度量、基线值、实际值、差值。5 -> 6 单位/秒，点按 5 个 tick（0.1 秒步长）：路径 0.5 -> 0.6。
            Assert.Contains("movement.path_length", report);
            Assert.Contains("基线 0.5 -> 实际 0.6", report);
            Assert.Contains("movement.speed_steady", report);
            Assert.Contains("基线 5 -> 实际 6", report);
            Assert.Contains("(差 +0.1)", report);

            // 键里的数据集哈希变化只是警告，真正的判定看度量差异。
            Assert.Contains(result.Diff.Warnings, w => w.Contains("key.dataset"));

            // 不留任何改动：改写只活在内存里，磁盘上的基线与数据都没动；用未改动的数据再比一次回到通过。
            Assert.Equal(before, File.ReadAllBytes(baselinePath));
            var clean = LabSuite.Check(LabTestSupport.Runner, LabTestSupport.FixturesDir, onlyScript: "move_tap", onlyCell: "2d_targeted");
            Assert.Equal(CellStatus.Pass, Assert.Single(clean).Status);
        }

        [Fact]
        public void Comparer_RejectsBaselineOfAnotherScriptVersionOrCell()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("move_tap");
            var fp = runner.Run(script, "2d_targeted");

            var other = runner.Run(script, "3d_targeted");
            var diff = FingerprintComparer.Compare(other, fp, runner.Registry);
            Assert.False(diff.Ok);
            Assert.Contains(diff.Entries, e => e.Name == "key.cell");
        }

        [Fact]
        public void Comparer_RealTimeMetrics_AreCeilingChecksNotByteComparisons()
        {
            var runner = LabTestSupport.Runner;
            var fp = runner.Run(LabTestSupport.Script("move_tap"), "2d_targeted");

            // 基线里的帧耗时被改成极小值：实际值只要不超过 max(基线 x 倍率, 下限 50 毫秒) 就通过（实时类度量不做字节比较）。
            var lowBaseline = Fingerprint.Parse(System.Text.RegularExpressions.Regex.Replace(
                fp.ToJson(), "\"frame_ms_p95\": [0-9.eE+-]+", "\"frame_ms_p95\": 0.000001"));
            Assert.DoesNotContain(
                FingerprintComparer.Compare(lowBaseline, fp, runner.Registry).Entries, e => e.Name == "performance.frame_ms_p95");

            // 实际值远超上限（比如某次改动让每帧慢了几个数量级）必须报差异。
            var slowActual = Fingerprint.Parse(System.Text.RegularExpressions.Regex.Replace(
                fp.ToJson(), "\"frame_ms_p95\": [0-9.eE+-]+", "\"frame_ms_p95\": 5000"));
            var slow = FingerprintComparer.Compare(fp, slowActual, runner.Registry);
            Assert.Contains(slow.Entries, e => e.Name == "performance.frame_ms_p95");
        }

        // ---------- 度量组可扩展 ----------

        private sealed class EchoGroup : IMetricGroup
        {
            private static readonly MetricSpec[] SpecList =
            {
                MetricSpec.Exact("ticks_seen", MetricClass.Logic, "recording 里的 tick 数（扩展示例）"),
            };

            public string Name => "echo";

            public IReadOnlyList<MetricSpec> Specs => SpecList;

            public void Compute(LabRecording recording, MetricSink sink) => sink.Add("ticks_seen", recording.Ticks.Count);
        }

        [Fact]
        public void MetricRegistry_AcceptsNewGroups_WithoutTouchingRunnerOrComparer()
        {
            var registry = MetricRegistry.CreateDefault().Register(new EchoGroup());
            var extended = new LabRunner(LabTestSupport.Runner.Dataset, registry);
            var script = LabTestSupport.Script("move_tap");

            var fp = extended.Run(script, "2d_targeted");
            Assert.True(fp.Groups.ContainsKey("echo"));
            Assert.Contains("\"echo\"", fp.Project(registry, MetricClass.Logic));

            // 旧基线里没有新组：只给警告，不让既有基线失败；新组一旦进基线就参与比较。
            var oldBaseline = LabTestSupport.Runner.Run(script, "2d_targeted");
            var diff = FingerprintComparer.Compare(oldBaseline, fp, registry);
            Assert.True(diff.Ok, diff.Format());
            Assert.Contains(diff.Warnings, w => w.Contains("echo"));

            // 基线里有、注册表里已没有的组：必须报差异（组被悄悄删掉不能静默通过）。
            var reverse = FingerprintComparer.Compare(fp, oldBaseline, LabTestSupport.Runner.Registry);
            Assert.Contains(reverse.Entries, e => e.Kind == "removed_group" && e.Name == "echo");

            Assert.Throws<ArgumentException>(() => registry.Register(new EchoGroup()));
        }

        // ---------- 格子可运行状态 ----------

        [Fact]
        public void SixCells_AllRunnable_OnHeadlessStubHost()
        {
            var cells = LabTestSupport.Runner.Dataset.Catalog.Scenarios();
            Assert.Equal(6, cells.Count);
            foreach (var cell in cells)
            {
                Assert.True(cell.CheckRunnable(LabHost.AvailableCapabilities).Runnable, cell.Cell);
                Assert.Equal("plane", cell.Space);
            }
        }

        [Fact]
        public void ReservedSpaceAndMissingCapability_AreReportedAsNotRunnable_NotSilentlySkipped()
        {
            var tweaked = LabTestSupport.CreateRunner((table, text) =>
            {
                if (table != "lab.scenario")
                {
                    return null;
                }

                // 第一个格子改成预留的体积空间；第二个格子声明缺失的适配层能力。
                var first = text.IndexOf("\"space\": \"plane\"", StringComparison.Ordinal);
                var rewritten = text.Substring(0, first) + "\"space\": \"volume\"" + text.Substring(first + "\"space\": \"plane\"".Length);
                var marker = "\"hit_shape\": \"shape_2d\",";
                var second = rewritten.IndexOf(marker, rewritten.IndexOf("lab.scenario.2d_action", StringComparison.Ordinal), StringComparison.Ordinal);
                return rewritten.Substring(0, second) + marker + " \"required_capabilities\": [\"supportsVolumeSweep\"],"
                    + rewritten.Substring(second + marker.Length);
            });

            var cells = tweaked.Dataset.Catalog.Scenarios();
            var volume = cells[0].CheckRunnable(LabHost.AvailableCapabilities);
            Assert.False(volume.Runnable);
            Assert.Contains("volume", volume.ToString());
            var missing = cells[1].CheckRunnable(LabHost.AvailableCapabilities);
            Assert.False(missing.Runnable);
            Assert.Contains("supportsVolumeSweep", missing.ToString());

            var script = LabTestSupport.Script("move_tap");
            Assert.Throws<LabCellNotRunnableException>(() => tweaked.Run(script, "2d_targeted"));

            // 套件里也显式标成"不可运行"，不静默跳过、不当作通过。
            var results = LabSuite.Check(tweaked, LabTestSupport.FixturesDir, onlyScript: "move_tap");
            Assert.Equal(2, results.FindAll(r => r.Status == CellStatus.NotRunnable).Count);
            Assert.Equal(4, results.FindAll(r => r.Status == CellStatus.Pass).Count);
        }

        // ---------- 靶子 ----------

        [Fact]
        public void AllDummyKinds_SpawnAndRunWithoutError()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Build(
                "all_dummies", 60, new List<ScriptEvent>(),
                "stake", "swarm", "mob", "elite", "patrol", "breakable");
            var recording = runner.Record(script, "2d_targeted");

            // 1 木桩 + 7 群体 + 1 普通怪 + 1 精英 + 1 巡逻靶 + 1 可破坏障碍。
            Assert.Equal(12, recording.Dummies.Count);
            Assert.Equal(60, recording.Ticks.Count);
        }

        [Fact]
        public void UndyingStake_TakesEveryHitAndNeverDies_EvenWithTinyHealthPool()
        {
            // 内存里把木桩与巡逻靶的耐力从 100000 改成 30：若没有"生命下限"，12 次 20 点伤害早已击杀；
            // 木桩模板用 power_floors 把生命下限钉在 1，所以仍应只吃伤害、不死。
            var tiny = LabTestSupport.CreateRunner((table, text) =>
                table == "creature.template" ? text.Replace("\"stat.stamina\": 100000", "\"stat.stamina\": 30") : null);
            var events = new List<ScriptEvent>();
            for (var i = 0; i < 12; i++)
            {
                events.Add(new ScriptEvent(5 + i * 25, "input.action.attack", ScriptEventKind.Press));
                events.Add(new ScriptEvent(6 + i * 25, "input.action.attack", ScriptEventKind.Release));
            }

            var script = LabTestSupport.Build("stake_soak", 320, events, "stake");
            script.Meta.PlayerStart = new Vec2(4, 0);
            var recording = tiny.Record(script, "2d_targeted");

            Assert.Equal(12, recording.Events.FindAll(e => e.Kind == "damage").Count);
            Assert.Empty(recording.Events.FindAll(e => e.Kind == "died"));
        }

        [Fact]
        public void Swarm_IsFullyKilledBySweepAndEveryTargetIsHitOncePerInstance()
        {
            var runner = LabTestSupport.Runner;
            var fp = runner.Run(LabTestSupport.Script("group_hit"), "2d_targeted");
            var attack = (JsonObject)fp.Groups["attack"];
            Assert.Equal(7.0, ((JsonNumber)attack["kills"]).Value);
            Assert.Equal(0.0, ((JsonNumber)attack["dedupe_violations"]).Value);
            Assert.Equal(7.0, ((JsonNumber)attack["hits_per_instance_max"]).Value);
        }
    }
}

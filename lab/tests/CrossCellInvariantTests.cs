using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 跨格子不变量（ADR-0122 决定 4）的验收，加上手感场景新机制（脚本格式版本 3、timeline 剥离变体、手感装配开关）的单元验收。
    /// 不变量的运行入口有两个：<c>dotnet run --project toolchain/feellab -- invariants</c>（命令行，逐条打印、末行
    /// <c>RESULT invariants total= pass= fail=</c>）和本类的 <see cref="AllInvariants_HoldForEveryScript"/>（随 <c>Tests.Lab</c> 进门禁）。
    /// </summary>
    public sealed class CrossCellInvariantTests
    {
        [Fact]
        public void AllInvariants_HoldForEveryScript()
        {
            var scripts = LabTestSupport.AllScripts();
            var results = LabInvariants.Check(LabTestSupport.Runner, scripts);

            var failures = new StringBuilder();
            foreach (var r in results)
            {
                if (!r.Ok)
                {
                    failures.Append(r).Append('\n');
                }
            }

            Assert.True(failures.Length == 0, "跨格子不变量不成立：\n" + failures);

            // 条数：平面组合每脚本 2 种结算 × 2 次比较；动作式剥离每脚本 3 个组合；手感装配透明性每个手感脚本 3 个组合。
            var feelCount = scripts.Count(s => s.Meta.Feel);
            Assert.Equal(scripts.Count * 4, results.Count(r => r.Invariant == LabInvariants.PlanarCombos));
            Assert.Equal(scripts.Count * 3, results.Count(r => r.Invariant == LabInvariants.ActionStrippedEqualsTargeted));
            Assert.Equal(feelCount * 3, results.Count(r => r.Invariant == LabInvariants.FeelAssemblyIsTransparentUnderClassic));
        }

        [Fact]
        public void ActionStrippedInvariant_IsNotVacuous_EachHalfOfTheVariantAloneBreaksEquality()
        {
            // 动作式 = 目标选择式 + timeline + 手感预设：两样缺一不可，只换其中一样都不等于目标选择式格子。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_melee");
            var targeted = runner.Run(script, "2d_targeted");
            var classic = runner.Dataset.Catalog.GetScenario("2d_targeted").DefaultPreset;

            var unchanged = runner.Run(script, "2d_action");
            var stripOnly = runner.Run(script, "2d_action", new LabRunVariant { StripTimelines = true });
            var presetOnly = runner.Run(script, "2d_action", new LabRunVariant { StripTimelines = false, PresetId = classic });
            var both = runner.Run(script, "2d_action", new LabRunVariant { StripTimelines = true, PresetId = classic });

            Assert.NotEmpty(LabInvariants.DiffLogic(runner.Registry, targeted, unchanged, null));
            Assert.NotEmpty(LabInvariants.DiffLogic(runner.Registry, targeted, stripOnly, null));
            Assert.NotEmpty(LabInvariants.DiffLogic(runner.Registry, targeted, presetOnly, null));
            Assert.Empty(LabInvariants.DiffLogic(runner.Registry, targeted, both, null));
        }

        [Fact]
        public void DiffLogic_ReportsTamperedMetric_AndIgnoresPresentationAndRealTimeMetrics()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_melee");
            var a = runner.Run(script, "2d_action");
            var tampered = Fingerprint.Parse(a.ToJson().Replace("\"started_ticks_player\": 2", "\"started_ticks_player\": 3"));
            var diffs = LabInvariants.DiffLogic(runner.Registry, a, tampered, null);
            Assert.Single(diffs);
            Assert.StartsWith("hitstop.started_ticks_player", diffs[0], StringComparison.Ordinal);

            // 另一个格子的表现组与实时组不同，但逻辑组相同时不报差异（2D 与 3D 的动作式格子）。
            var other = runner.Run(script, "3d_action");
            Assert.Empty(LabInvariants.DiffLogic(runner.Registry, a, other, null));
        }

        [Fact]
        public void FeelAssemblyTransparency_OnlyComparesWorldOutcomeMetrics()
        {
            // 旧路径与手感路径的提交时序不同（旧路径按钮边沿直接提交施放意图，手感路径经输入缓冲与动作层），
            // 所以 casts_submitted 之类时序度量按构造不同，不进透明性比较；世界结局度量（命中、伤害、移动）必须一致。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_melee");
            var on = runner.Run(script, "2d_targeted");
            var off = runner.Run(script, "2d_targeted", new LabRunVariant { FeelOff = true });
            Assert.False(Has(off, "actiontl"), "手感装配关闭时不应有手感度量组");
            Assert.True(Has(on, "actiontl"));
            Assert.NotEmpty(LabInvariants.DiffLogic(runner.Registry, on, off, null));
            Assert.Single(LabInvariants.Check(runner, new[] { script }).Where(r => r.Invariant == LabInvariants.FeelAssemblyIsTransparentUnderClassic && r.Subject.StartsWith("2d_targeted", StringComparison.Ordinal)));
        }

        private static bool Has(Fingerprint fp, string group) => fp.Groups.ContainsKey(group);

        // ---------- timeline 剥离 ----------

        [Fact]
        public void StripTimelines_RewritesOnlySkillDefRows_DropsTimelineAndZeroesCastTime()
        {
            var source = LabDataSources.FromDirectory(System.IO.Path.Combine(LabTestSupport.RepoRoot(), "data", "_lab_action"));
            string? text = null;
            foreach (var table in source.ListTables())
            {
                if (table.TableName == "skill.def")
                {
                    text = table.ReadText();
                }
            }

            Assert.NotNull(text);
            Assert.Contains("\"timeline\"", text);
            Assert.Null(LabDataSources.StripTimelines("feel.action", text!));

            var stripped = LabDataSources.StripTimelines("skill.def", text!)!;
            Assert.DoesNotContain("\"timeline\"", stripped);
            var original = LabJson.ParseObject(text!, "原表");
            var rewritten = LabJson.ParseObject(stripped, "改写表");
            var oldRows = (JsonArray)original["rows"];
            var newRows = (JsonArray)rewritten["rows"];
            Assert.Equal(oldRows.Count, newRows.Count);
            for (var i = 0; i < oldRows.Count; i++)
            {
                var o = (JsonObject)oldRows[i];
                var n = (JsonObject)newRows[i];
                Assert.Equal(((JsonString)o["id"]).Value, ((JsonString)n["id"]).Value);
                Assert.Equal(0.0, ((JsonNumber)n["cast_time"]).Value);
                // 效果与目标形状原样保留：剥掉的只是时间线块与读条时长。
                Assert.Equal(LabJson.Write(o["effects"]), LabJson.Write(n["effects"]));
                Assert.Equal(LabJson.Write(o["target_shape_ref"]), LabJson.Write(n["target_shape_ref"]));
            }
        }

        [Fact]
        public void StripVariant_ChangesDatasetOnlyForFeelScripts_AndOnlyOnTargetedCellsByDefault()
        {
            var runner = LabTestSupport.Runner;
            var feel = LabTestSupport.Script("feel_melee");
            var targeted = runner.Dataset.Catalog.GetScenario("2d_targeted");
            var action = runner.Dataset.Catalog.GetScenario("2d_action");

            var keep = runner.DatasetFor(feel, action);
            var strip = runner.DatasetFor(feel, targeted);
            Assert.NotEqual(keep.Hash, strip.Hash);
            Assert.Equal(strip.Hash, runner.DatasetFor(feel, action, new LabRunVariant { StripTimelines = true }).Hash);
            Assert.Equal(keep.Hash, runner.DatasetFor(feel, targeted, new LabRunVariant { StripTimelines = false }).Hash);

            // 旧标准脚本没有额外数据根，数据集就是基础数据集，与格子/变体无关（既有基线的数据集哈希不变）。
            foreach (var legacy in LabTestSupport.StandardScripts().Where(s => s.Meta.ExtraDataRoots.Count == 0))
            {
                Assert.Same(runner.Dataset, runner.DatasetFor(legacy, targeted));
                Assert.Same(runner.Dataset, runner.DatasetFor(legacy, action, new LabRunVariant { StripTimelines = true }));
            }
        }

        // ---------- 脚本格式版本 3 ----------

        [Fact]
        public void FeelFormat_IsWrittenOnlyWhenUsed_AndCastEventsCarryTheirActor()
        {
            foreach (var legacy in LabTestSupport.StandardScripts())
            {
                var json = legacy.ToJson();
                Assert.DoesNotContain("\"feel\"", json);
                Assert.DoesNotContain("skillSlots", json);
                Assert.DoesNotContain("dummySet", json);
                Assert.True(legacy.EffectiveFormatVersion < InputScript.FeelFormatVersion);
            }

            var elite = LabTestSupport.Script("feel_elite_armor");
            Assert.Contains("\"kind\": \"cast\"", elite.ToJson());
            Assert.Contains("\"actor\": \"elite\"", elite.ToJson());
            Assert.Equal(InputScript.FeelFormatVersion, elite.EffectiveFormatVersion);
            var cast = elite.Events.First(e => e.Kind == ScriptEventKind.Cast);
            Assert.Equal("elite", cast.Actor);
            Assert.Equal(cast.Actor, InputScript.Parse(elite.ToJson()).Events.First(e => e.Kind == ScriptEventKind.Cast).Actor);
        }

        [Fact]
        public void CastEvent_ForAnActorThatDidNotSpawn_FailsLoudly()
        {
            var script = InputScript.Parse(LabTestSupport.Script("feel_elite_armor").ToJson());
            script.Meta.DummyGroups.Clear();
            Assert.Throws<LabFormatException>(() => LabTestSupport.Runner.Record(script, "2d_action"));
        }

        [Fact]
        public void FeelOffVariant_RunsTheScriptOnTheLegacyPath_WithoutFeelRecording()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_melee");
            var on = runner.Record(script, "2d_targeted");
            var off = runner.Record(script, "2d_targeted", new LabRunVariant { FeelOff = true });
            Assert.NotNull(on.Feel);
            Assert.Null(off.Feel);
            // 旧路径提交了施放意图（按钮边沿直接施放），手感路径没有；两边命中结局一致。
            Assert.NotEmpty(off.Intents);
            Assert.Empty(on.Intents);
            Assert.Equal(
                on.Events.Count(e => e.Kind == "damage"),
                off.Events.Count(e => e.Kind == "damage"));
        }
    }
}

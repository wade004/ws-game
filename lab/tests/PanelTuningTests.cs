using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 实验室调参面板的视图模型（ADR-0150，手感设计 06 第 3.4 节、第 4 节）：字段全覆盖与来源链、改值落成覆盖事件并可无头逐字节重放、
    /// A/B 槽位、恢复基准、保存为预设与写回数据表的差异（期望值由数据与规则算出，不写裸数；写回的补丁重新解析必须还原目标值）。
    /// </summary>
    public sealed class PanelTuningTests
    {
        private const string Cell = "2d_action";
        private const double Frame = 1.0 / 60.0;

        /// <summary>试玩宿主缺省的数据根（同 <c>LabPlayground</c>）。</summary>
        internal static readonly string[] PlaygroundRoots =
        {
            "data/_feel", "data/_feel_templates", "data/_lab_action", "lab/fixtures/data/feel_templates",
        };

        private sealed class NoopExtension : LabHostExtension
        {
        }

        internal static void Frames(LabSession session, int n)
        {
            for (var i = 0; i < n; i++)
            {
                session.Advance(Frame);
            }
        }

        internal static (LabRunner Runner, LabSession Session, TuningPanel Panel) Open(
            LabRunner? runner = null, string id = "panel_tuning_test", IEnumerable<KeyValuePair<string, string>>? skillSlots = null)
        {
            runner ??= LabTestSupport.Runner;
            var script = LabLive.CreateScript(id, 60, 60, PlaygroundRoots, skillSlots: skillSlots);
            var session = runner.StartLive(script, Cell, null, new NoopExtension());
            Frames(session, 3);
            var panel = new TuningPanel(session, runner.DatasetFor(script).Sources, LabTestSupport.RepoRoot());
            return (runner, session, panel);
        }

        private static string Logic(LabRunner runner, InputScript script, LabRecording recording) =>
            runner.FingerprintOf(script, Cell, recording).Project(runner.Registry, MetricClass.Logic);

        private static string TempDir(string name)
        {
            var dir = Path.Combine(Path.GetTempPath(), "feel_panel_tests", name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---------------------------------------------------------------- 行：字段覆盖与来源链

        [Fact]
        public void Rows_CoverEveryRegisteredField_InTheSevenGroups_WithUnitRangeStatusAndProvenance()
        {
            var (_, session, panel) = Open();
            var rows = panel.AllRows();
            Assert.Equal(panel.Fields.Count, rows.Count);
            Assert.Equal(7, panel.Groups.Count);
            foreach (var group in panel.Groups)
            {
                Assert.NotEmpty(panel.Rows(group));
            }

            var names = new HashSet<string>(rows.Select(r => r.Name), StringComparer.Ordinal);
            for (var i = 0; i < panel.Fields.Count; i++)
            {
                Assert.Contains(panel.Fields[i].Name, names);
            }

            var resolver = session.Context!.World.Gameplay.Feel!.Feel.Resolver;
            var resolved = resolver.Resolve(session.Context.PlayerId);
            foreach (var row in rows)
            {
                var def = row.Def;
                Assert.Equal(def.Status == FeelFieldStatus.Planned, row.Planned);
                if (row.Planned)
                {
                    Assert.False(string.IsNullOrEmpty(row.StatusNote));
                }

                if (def.IsNumeric || def.Kind == FeelFieldKind.Enum)
                {
                    Assert.NotEmpty(row.RangeText);
                }

                Assert.Equal(resolved.GetRaw(def.Name), row.Raw);
                if (def.HasTicks && !row.Raw.IsNone)
                {
                    Assert.Equal(resolved.GetTicks(def.Name), row.Ticks);
                }

                // 来源链：每个有值的字段，最后一条覆盖操作的来源就是面板显示的来源；限幅/取整条目（层号 0）不算来源。
                if (!row.Raw.IsNone)
                {
                    Assert.NotEmpty(row.Provenance);
                    Assert.True(row.SourceLayer >= 1 && row.SourceLayer <= 8, row.Name + " 的来源层 " + row.SourceLayer);
                    Assert.NotEmpty(row.SourceId);
                    var last = row.Provenance.Last(e => e.Layer != 0);
                    Assert.Equal(last.SourceId, row.SourceId);
                    Assert.Equal(last.Layer, row.SourceLayer);
                    Assert.Equal(row.Raw, row.Provenance[row.Provenance.Count - 1].ValueAfter);
                }
            }

            // 缺省没有任何覆盖：没有一行被标覆盖，也没有一行来自第 8 层。
            Assert.DoesNotContain(rows, r => r.Overridden);
            Assert.DoesNotContain(rows, r => r.SourceLayer == 8);
        }

        [Fact]
        public void Row_SourceChain_ShowsTheBasePresetThenTheDebugOverride_AndTheTickConversionFollowsTheData()
        {
            var (_, session, panel) = Open();
            var field = FeelFieldNames.AttackerHitstopMs;
            var before = panel.Row(field);
            Assert.Equal(1, before.SourceLayer);
            Assert.StartsWith("feel.preset.", before.SourceId, StringComparison.Ordinal);

            panel.SetValue(field, FeelValue.Of(222.0));
            Assert.True(panel.Row(field).Pending, "事件已注入但还没经过固定步");
            Frames(session, 2);
            var row = panel.Row(field);
            Assert.False(row.Pending);
            Assert.True(row.Overridden);
            Assert.Equal(8, row.SourceLayer);
            Assert.StartsWith("debug:", row.SourceId, StringComparison.Ordinal);
            Assert.Equal(222.0, row.Raw.AsNumber(), 6);
            Assert.Equal(FeelCalibration.MillisecondsToTicks(222.0, session.StepSeconds), row.Ticks);
            // 来源链保留了底下的数据层：第 1 层预设的写入在 debug 之前。
            Assert.True(row.Provenance.Count >= 2);
            Assert.Equal(1, row.Provenance[0].Layer);
            Assert.Equal(before.SourceId, row.Provenance[0].SourceId);
        }

        // ---------------------------------------------------------------- 改值 → 覆盖事件 → 无头逐字节重放

        [Fact]
        public void EditingEveryKindOfField_EmitsOverrideEvents_AndTheRecordedScriptReplaysHeadlessWithByteIdenticalLogic()
        {
            var (runner, session, panel) = Open();
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(session, 4);
            panel.SetValue(FeelFieldNames.AttackerHitstopMs, FeelValue.Of(150.0));        // 数值
            panel.SetValue("action_turn_lock", FeelValue.Of(false));                      // 布尔
            panel.SetValue("reverse_policy", FeelValue.Of("through_zero"));               // 枚举
            panel.SetValue(FeelFieldNames.ImpactProfileRef, FeelValue.Of("feedback.impact_profile.lab_default")); // 引用
            panel.SetValue("turn_rate_deg_s", FeelValue.Of(1.25), FeelOp.Multiply);       // 相对运算
            panel.SetScope(session.Context!.Dummies.First().Key);
            panel.SetValue(FeelFieldNames.TargetHitstopMs, FeelValue.Of(90.0));           // 靶子作用域
            panel.SetScope(TuningPanel.GlobalScope);
            Frames(session, 3);
            session.Inject(new ScriptEvent(0, "input.action.lab_a_attack", ScriptEventKind.Press));
            Frames(session, 2);
            session.Inject(new ScriptEvent(0, "input.action.lab_a_attack", ScriptEventKind.Release));
            Frames(session, 60);
            var live = session.Finish();

            var overrides = session.Script.Events.Where(e => e.Kind == ScriptEventKind.Override).ToList();
            Assert.Equal(6, overrides.Count);
            Assert.Equal(1, overrides.Count(e => e.Text == "through_zero"));
            Assert.Equal(1, overrides.Count(e => e.Text == "feedback.impact_profile.lab_default"));
            Assert.Equal(
                new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, session.Context!.Dummies.First().Key },
                overrides.Select(e => e.Actor).ToArray());

            // 序列化往返逐字节稳定，再无头重放：逻辑组与现场逐字节一致。
            var json = session.Script.ToJson();
            var replayScript = InputScript.Parse(json);
            Assert.Equal(json, replayScript.ToJson());
            var replay = runner.Record(replayScript, Cell);
            Assert.Equal(Logic(runner, session.Script, live), Logic(runner, replayScript, replay));
            Assert.Contains(live.Events, e => e.Kind == "damage" && e.Source == "player");
        }

        [Fact]
        public void InvalidEdits_AreRejectedWithAReason_AndInjectNothing()
        {
            var (_, session, panel) = Open();
            var count = session.Script.Events.Count;
            Assert.Throws<LabFormatException>(() => panel.SetValue("no_such_field", FeelValue.Of(1.0)));
            Assert.Throws<LabFormatException>(() => panel.SetValue(FeelFieldNames.AttackerHitstopMs, FeelValue.Of(1.0e9)));
            Assert.Throws<LabFormatException>(() => panel.SetValue("reverse_policy", FeelValue.Of("not_a_policy")));
            Assert.Throws<LabFormatException>(() => panel.SetValue("action_turn_lock", FeelValue.Of(1.0)));
            Assert.Throws<LabFormatException>(() => panel.SetValue("action_turn_lock", FeelValue.Of(true), FeelOp.Multiply));
            Assert.Throws<LabFormatException>(() => panel.SetScope("ghost"));
            Assert.Contains("超出范围", panel.Validate(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(1.0e9)));
            Assert.Equal(count, session.Script.Events.Count);
            Assert.Empty(panel.ActiveWrites);
        }

        [Fact]
        public void ClearOneField_RestoresItsSourceValue_AndRestoreBaselineEmptiesTheOverrideLayer()
        {
            var (_, session, panel) = Open();
            var a = FeelFieldNames.AttackerHitstopMs;
            var b = FeelFieldNames.TargetHitstopMs;
            var baseA = panel.Row(a).Raw;
            var baseB = panel.Row(b).Raw;
            panel.SetValue(a, FeelValue.Of(111.0));
            panel.SetValue(b, FeelValue.Of(77.0));
            panel.SetValue(TuningPanel.PlayerScope, "turn_rate_deg_s", FeelValue.Of(333.0));
            Frames(session, 2);
            Assert.Equal(111.0, panel.Row(a).Raw.AsNumber(), 6);
            Assert.Equal(333.0, panel.Row("turn_rate_deg_s").Raw.AsNumber(), 6);

            panel.Clear(a);
            Frames(session, 2);
            Assert.Equal(baseA, panel.Row(a).Raw);
            Assert.Equal(77.0, panel.Row(b).Raw.AsNumber(), 6);
            Assert.Equal(333.0, panel.Row("turn_rate_deg_s").Raw.AsNumber(), 6);

            panel.RestoreBaseline();
            Frames(session, 2);
            Assert.Equal(baseB, panel.Row(b).Raw);
            Assert.Empty(panel.ActiveWrites);
            Assert.DoesNotContain(panel.AllRows(), r => r.Overridden || r.SourceLayer == 8);
        }

        [Fact]
        public void HitStopSwitch_IsTwoGlobalOverrides_PerSlot_AndChangesTheHitStopTick()
        {
            var (_, session, panel) = Open();
            Assert.True(panel.HitStopOn);
            panel.SetHitStop(false);
            Frames(session, 2);
            Assert.False(panel.HitStopOn);
            Assert.Equal(0, panel.Row(FeelFieldNames.AttackerHitstopMs).Ticks);
            panel.SetHitStop(true);
            Frames(session, 2);
            Assert.True(panel.HitStopOn);
            Assert.Equal(
                FeelCalibration.MillisecondsToTicks(FeelRules.Preset(panel.ActivePreset).N(FeelFieldNames.AttackerHitstopMs), session.StepSeconds),
                panel.Row(FeelFieldNames.AttackerHitstopMs).Ticks);
        }

        // ---------------------------------------------------------------- A/B 槽位

        [Fact]
        public void AbSlots_EachKeepTheirOwnOverridesAndPreset_SwitchingIsRecordedAndReplays()
        {
            var (runner, session, panel) = Open();
            var field = FeelFieldNames.AttackerHitstopMs;
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(session, 4);
            var baseline = panel.Row(field).Raw;

            panel.SetValue(field, FeelValue.Of(200.0));            // 槽位 A
            Frames(session, 2);
            panel.SwitchSlot();                                    // → B：没有覆盖
            Frames(session, 2);
            Assert.Equal('B', panel.ActiveSlot);
            Assert.Equal(baseline, panel.Row(field).Raw);
            panel.SetPreset("feel.preset.rpg_classic");            // B 自己的预设与覆盖
            panel.SetValue(field, FeelValue.Of(10.0));
            Frames(session, 2);
            Assert.Equal(10.0, panel.Row(field).Raw.AsNumber(), 6);
            Assert.Equal("feel.preset.rpg_classic", panel.PresetOf('B'));
            Assert.NotEqual(panel.PresetOf('A'), panel.PresetOf('B'));

            panel.SwitchSlot();                                    // → A：预设与覆盖都回来
            Frames(session, 2);
            Assert.Equal('A', panel.ActiveSlot);
            Assert.Equal(200.0, panel.Row(field).Raw.AsNumber(), 6);
            Assert.Equal(panel.PresetOf('A'), session.Context!.World.Gameplay.Feel!.Feel.Resolver.Calibration.BasePresetId);
            Assert.Equal(2, panel.SlotSwitches);

            panel.SwitchSlot();                                    // → B 再来一次，仍是它自己的
            Frames(session, 2);
            Assert.Equal(10.0, panel.Row(field).Raw.AsNumber(), 6);

            // 槽位覆盖组可导出成覆盖存储（A/B 各一组）。
            var store = panel.ExportSlots();
            Assert.Equal(200.0, store.Find("A")!.Writes.Single().Value.AsNumber(), 6);
            Assert.Equal(10.0, store.Find("B")!.Writes.Single().Value.AsNumber(), 6);

            // 切换写进了录制：脚本里有预设事件与清空覆盖事件，逐字节重放一致。
            Assert.Contains(session.Script.Events, e => e.Kind == ScriptEventKind.Preset && e.Action == "feel.preset.rpg_classic");
            Assert.Contains(session.Script.Events, e => e.Kind == ScriptEventKind.ClearOverrides && e.Action.Length == 0);
            var live = session.Finish();
            var replayScript = InputScript.Parse(session.Script.ToJson());
            Assert.Equal(Logic(runner, session.Script, live), Logic(runner, replayScript, runner.Record(replayScript, Cell)));
        }

        // ---------------------------------------------------------------- 保存为预设

        [Fact]
        public void SaveAsPreset_WritesAValidPresetRowFile_ThatReproducesTheOverridesWhenLoaded()
        {
            var (_, session, panel) = Open();
            panel.SetValue(FeelFieldNames.AttackerHitstopMs, FeelValue.Of(123.0));
            panel.SetValue("reverse_policy", FeelValue.Of("through_zero"));
            panel.SetValue("turn_rate_deg_s", FeelValue.Of(2.0), FeelOp.Multiply);
            panel.SetValue("stop_distance", FeelValue.Of(0.25), FeelOp.Add);
            panel.SetScope("player");
            panel.SetValue("walk_speed_ratio", FeelValue.Of(0.4));
            var baseValues = session.Context!.World.Gameplay.Feel!.Feel.Profiles.EffectivePresetValues(panel.ActivePreset, out _);
            var fields = panel.Fields;
            var dir = TempDir("save_preset");
            var saved = panel.SaveAsPreset("panel_test", dir);
            Assert.Equal("feel.preset.panel_test", saved.RowId);
            Assert.Equal(5, saved.WrittenFields);
            Assert.Empty(saved.Skipped);
            Assert.True(File.Exists(saved.Path));

            // 文件是一个合法数据根：装进脚本的额外数据根后能选到这个预设，取值 = 覆盖作用在基础预设上的结果。
            var roots = PlaygroundRoots.Concat(new[] { dir }).ToArray();
            var script = LabLive.CreateScript("panel_saved_preset", 60, 60, roots);
            var runner = LabTestSupport.Runner;
            var fresh = runner.StartLive(script, Cell, null, new NoopExtension());
            Frames(fresh, 2);
            fresh.Inject(new ScriptEvent(0, saved.RowId, ScriptEventKind.Preset));
            Frames(fresh, 2);
            var resolved = fresh.Context!.World.Gameplay.Feel!.Feel.Resolver.Resolve(fresh.Context.PlayerId);
            double Base(string f) => baseValues[fields.IndexOf(f)].AsNumber();
            Assert.Equal(123.0, resolved.GetRaw(FeelFieldNames.AttackerHitstopMs).AsNumber(), 6);
            Assert.Equal("through_zero", resolved.GetRaw("reverse_policy").AsText());
            Assert.Equal(Base("turn_rate_deg_s") * 2.0, resolved.GetRaw("turn_rate_deg_s").AsNumber(), 6);
            Assert.Equal(Base("stop_distance") + 0.25, resolved.GetRaw("stop_distance").AsNumber(), 6);
            Assert.Equal(0.4, resolved.GetRaw("walk_speed_ratio").AsNumber(), 6);
            // 没覆盖的字段与基础预设一致。
            Assert.Equal(Base("buffer_ms"), resolved.GetRaw("buffer_ms").AsNumber(), 6);
            Assert.Equal("feel.preset.panel_test", resolved.GetProvenance(FeelFieldNames.AttackerHitstopMs)[0].SourceId);

            // 作用于靶子的覆盖不写进预设，而是列在 Skipped 里。
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(session, 3);
            panel.SetScope(session.Context!.Dummies.First().Key);
            panel.SetValue(FeelFieldNames.TargetHitstopMs, FeelValue.Of(80.0));
            var again = panel.SaveAsPreset("panel_test2", dir);
            Assert.Equal(5, again.WrittenFields);
            Assert.Equal(Path.Combine(dir, "feel", "feel.preset.json"), again.Path);
            Assert.Single(again.Skipped);
            Assert.Throws<ArgumentException>(() => panel.SaveAsPreset("Bad Name", dir));
        }

        // ---------------------------------------------------------------- 写回数据表

        /// <summary>读仓库里数据文件的原文（与面板读的同一份）。</summary>
        private static string Disk(string relative) => File.ReadAllText(Path.Combine(LabTestSupport.RepoRoot(), relative));

        /// <summary>数据集（基础根与脚本额外根）都叠上同一个内存改写（不动磁盘）。</summary>
        private static LabRunner OverlayRunner(Func<string, string, string?> rewrite)
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

        /// <summary>把写回计划的补丁文本叠到数据来源上（内存改写，不动磁盘），按"原文相等"匹配要替换的那一份文件。</summary>
        private static LabRunner PatchedRunner(WriteBackPlan plan)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in plan.Files)
            {
                map[Disk(file)] = plan.PatchedText(file);
            }

            return OverlayRunner((_, text) => map.TryGetValue(text, out var patched) ? patched : null);
        }

        [Fact]
        public void WriteBack_OfASetFromThePresetRow_ChangesOnlyThatValue_AndReResolvingTheDiffedTableGivesTheTarget()
        {
            var (_, session, panel) = Open();
            var field = FeelFieldNames.AttackerHitstopMs;
            var presetId = panel.ActivePreset;
            panel.SetValue(field, FeelValue.Of(177.0));
            Frames(session, 2);
            var plan = panel.BuildWriteBack();
            Assert.Empty(plan.Manual);
            var edit = Assert.Single(plan.Edits);
            Assert.Equal(presetId, edit.RowId);
            Assert.Equal(FeelRules.Preset(presetId).N(field), double.Parse(edit.OldValueText, System.Globalization.CultureInfo.InvariantCulture), 6);
            Assert.Equal("177", edit.NewValueText);

            // 差异只有一行删除一行新增，且改的是那一行。
            var diff = plan.Diff();
            var removed = diff.Split('\n').Where(l => l.StartsWith("-", StringComparison.Ordinal) && !l.StartsWith("---", StringComparison.Ordinal)).ToList();
            var added = diff.Split('\n').Where(l => l.StartsWith("+", StringComparison.Ordinal) && !l.StartsWith("+++", StringComparison.Ordinal)).ToList();
            Assert.Single(removed);
            Assert.Single(added);
            Assert.Contains("\"" + field + "\"", removed[0]);
            Assert.Contains("177", added[0]);
            Assert.Contains("--- a/data/_feel/feel/feel.preset.json", diff);

            // 重新解析：用补丁后的表换掉原表，同一字段在没有任何覆盖时就是目标值。
            var runner = PatchedRunner(plan);
            var (_, fresh, freshPanel) = Open(runner, "panel_writeback_resolve");
            Frames(fresh, 2);
            Assert.Equal(177.0, freshPanel.Row(field).Raw.AsNumber(), 6);
            Assert.Equal(presetId, freshPanel.Row(field).SourceId);
            Assert.Empty(freshPanel.ActiveWrites);

            // 写文件只落到指定的本地路径，不改仓库数据。
            var before = Disk("data/_feel/feel/feel.preset.json");
            var path = Path.Combine(TempDir("writeback"), "wb.diff");
            panel.WriteBack(path);
            Assert.Equal(diff, File.ReadAllText(path));
            Assert.Equal(before, Disk("data/_feel/feel/feel.preset.json"));
        }

        [Fact]
        public void WriteBack_OfAWeaponRowSet_AndOfADummyCharacterRowSet_PatchTheirOwnWritesEntries()
        {
            var (_, session, panel) = Open();
            // 武器行：装备后，武器行写入在第 8 层（宿主把装备行当调试覆盖叠）；这里验证来源是"数据层"的字段：角色行（靶子）。
            session.Inject(new ScriptEvent(0, "stake_resilient", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(session, 4);
            var label = session.Context!.Dummies.First().Key;
            panel.SetScope(label);
            panel.SetValue("poise_recover_per_s", FeelValue.Of(9.0));
            Frames(session, 2);
            Assert.Equal("feel.character.lab_a_resilient", panel.Row("poise_recover_per_s").Provenance.Where(e => e.Layer != 8).Last().SourceId);
            var plan = panel.BuildWriteBack();
            Assert.Empty(plan.Manual);
            var edit = Assert.Single(plan.Edits);
            Assert.Equal("feel.character.lab_a_resilient", edit.RowId);
            Assert.Equal("data/_lab_action/feel/feel.character.json", edit.File);
            Assert.Equal("9", edit.NewValueText);

            var runner = PatchedRunner(plan);
            var (_, fresh, freshPanel) = Open(runner, "panel_writeback_character");
            fresh.Inject(new ScriptEvent(0, "stake_resilient", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(fresh, 4);
            freshPanel.SetScope(fresh.Context!.Dummies.First().Key);
            Assert.Equal(9.0, freshPanel.Row("poise_recover_per_s").Raw.AsNumber(), 6);
        }

        [Fact]
        public void WriteBack_OfAMultiplyFromAnArchetypeRow_SolvesTheFactor_AndTheTableReResolvesToTheTarget()
        {
            // 内存改写：给木桩的生物模板挂上重体型原型（第 2 层），其 hit_stun_ms 是 multiply 来源。
            Func<string, string, string?> attach = (table, text) =>
                table == "creature.template" && text.Contains("\"id\": \"creature.lab_a_resilient\",")
                    ? text.Replace("\"id\": \"creature.lab_a_resilient\",", "\"id\": \"creature.lab_a_resilient\",\n      \"feel_archetype_ref\": \"feel.archetype.heavy\",")
                    : null;
            var runner = OverlayRunner(attach);
            var (_, session, panel) = Open(runner, "panel_writeback_multiply");
            session.Inject(new ScriptEvent(0, "stake_resilient", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(session, 4);
            panel.SetScope(session.Context!.Dummies.First().Key);
            var field = "hit_stun_ms";
            var target = 400.0;
            panel.SetValue(field, FeelValue.Of(target));
            Frames(session, 2);
            var chain = panel.Row(field).Provenance.Where(e => e.Layer != 8).ToList();
            var writer = chain.Last();
            Assert.Equal(2, writer.Layer);
            Assert.Equal("multiply", writer.Op);
            var plan = panel.BuildWriteBack();
            Assert.Empty(plan.Manual);
            var edit = Assert.Single(plan.Edits);
            Assert.Equal("feel.archetype.heavy", edit.RowId);
            var factor = double.Parse(edit.NewValueText, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(target / writer.ValueBefore.AsNumber(), factor, 6);

            // 补丁后的原型表叠上去，再重新解析 = 目标值。
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in plan.Files)
            {
                map[Disk(file)] = plan.PatchedText(file);
            }

            Func<string, string, string?> both = (table, text) => map.TryGetValue(text, out var p) ? p : attach(table, text);
            var patchedRunner = OverlayRunner(both);
            var (_, fresh, freshPanel) = Open(patchedRunner, "panel_writeback_multiply_check");
            fresh.Inject(new ScriptEvent(0, "stake_resilient", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(fresh, 4);
            freshPanel.SetScope(fresh.Context!.Dummies.First().Key);
            Assert.Equal(target, freshPanel.Row(field).Raw.AsNumber(), 6);
        }

        [Fact]
        public void WriteBack_ListsWhatItCannotDo_InsteadOfDroppingIt()
        {
            var (_, session, panel) = Open();
            // 同一字段在全局与玩家两个作用域都有覆盖：只写回一个，另一个列入人工项。
            panel.SetValue(TuningPanel.GlobalScope, FeelFieldNames.AttackerHitstopMs, FeelValue.Of(90.0));
            panel.SetValue(TuningPanel.PlayerScope, FeelFieldNames.AttackerHitstopMs, FeelValue.Of(95.0));
            Frames(session, 2);
            var plan = panel.BuildWriteBack();
            Assert.Single(plan.Edits);
            Assert.Single(plan.Manual);
            Assert.Contains("多个作用域", plan.Manual[0]);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 交互式试玩的内核侧（ADR-0141）：实时会话（<see cref="LabSession"/>）的人手输入落成脚本，经无头宿主逐 tick 重放，逻辑组逐字节一致；
    /// 新增的宿主级事件（出靶子、清场、切预设、换装/体型档案）每个有一条复现用例。期望值由规则或另一次运行算出，不写死裸数。
    /// </summary>
    public sealed class InteractiveSessionTests
    {
        private const string Cell = "2d_action";
        private const string Attack = "input.action.lab_a_attack";
        private const string Skill = "input.action.lab_a_skill";
        private const string Move = "input.action.move";
        private const double Frame = 1.0 / 60.0;

        private sealed class NoopExtension : LabHostExtension
        {
            public readonly List<string> Spawned = new List<string>();
            public int Cleared;

            public override void OnDummySpawned(string label, Id entityId) => Spawned.Add(label);

            public override void OnDummiesCleared() => Cleared++;
        }

        private static void Frames(LabSession session, int n)
        {
            for (var i = 0; i < n; i++)
            {
                session.Advance(Frame);
            }
        }

        private static void Tap(LabSession session, string action, int holdFrames = 2)
        {
            session.Inject(new ScriptEvent(0, action, ScriptEventKind.Press));
            Frames(session, holdFrames);
            session.Inject(new ScriptEvent(0, action, ScriptEventKind.Release));
        }

        /// <summary>一局"人手"会话：移动、出靶子、连按攻击、技能、切预设、换武器与体型、再出一只、清场。</summary>
        private static (LabSession Session, LabRecording Recording, NoopExtension Ext) PlaySession()
        {
            var runner = LabTestSupport.Runner;
            var script = LabLive.CreateScript("live_session_test");
            var ext = new NoopExtension();
            var session = runner.StartLive(script, Cell, null, ext);
            Frames(session, 3);
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            session.Inject(new ScriptEvent(0, "mob", ScriptEventKind.Spawn, new Vec2(2.0, 1.0)));
            Frames(session, 5);
            Tap(session, Attack);
            Frames(session, 14);
            Tap(session, Attack);
            Frames(session, 14);
            Tap(session, Attack);
            Frames(session, 40);
            session.Inject(new ScriptEvent(0, Move, ScriptEventKind.Axis, new Vec2(0.0, 0.6)));
            Frames(session, 20);
            session.Inject(new ScriptEvent(0, Move, ScriptEventKind.Axis, Vec2.Zero));
            Frames(session, 5);
            session.Inject(new ScriptEvent(0, "feel.preset.rpg_classic", ScriptEventKind.Preset));
            session.Inject(new ScriptEvent(0, "feel.weapon.greatsword", ScriptEventKind.Loadout));
            session.Inject(new ScriptEvent(0, "feel.archetype.heavy", ScriptEventKind.Loadout));
            Tap(session, Skill);
            Frames(session, 50);
            session.Inject(new ScriptEvent(0, string.Empty, ScriptEventKind.Loadout));
            session.Inject(new ScriptEvent(0, "time_scale", ScriptEventKind.Marker, new Vec2(0.5, 0.0)));
            session.Inject(new ScriptEvent(
                0, Core.Foundation.Feel.FeelFieldNames.AttackerHitstopMs, ScriptEventKind.Override, new Vec2(200.0, 0.0), null, "player"));
            Tap(session, Attack);
            Frames(session, 30);
            session.Inject(new ScriptEvent(0, "clear_overrides", ScriptEventKind.ClearOverrides));
            session.Inject(new ScriptEvent(0, "clear", ScriptEventKind.ClearDummies));
            Frames(session, 3);
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(2.5, 0.0)));
            Frames(session, 10);
            var recording = session.Finish();
            return (session, recording, ext);
        }

        [Fact]
        public void LiveSession_RecordedScript_ReplaysHeadless_WithByteIdenticalLogicGroups()
        {
            var runner = LabTestSupport.Runner;
            var (session, live, _) = PlaySession();

            // 会话日志就是脚本事件清单：每个事件都盖了注入当时的 tick 戳，按注入先后（tick 单调不减）。
            var events = session.Script.Events;
            Assert.NotEmpty(events);
            for (var i = 1; i < events.Count; i++)
            {
                Assert.True(events[i].Tick >= events[i - 1].Tick);
            }

            Assert.Equal(InputScript.InteractiveFormatVersion, session.Script.EffectiveFormatVersion);
            Assert.Equal(session.Tick, session.Script.Meta.DurationTicks);

            // 序列化往返后再无头重放（文件里读回来的脚本，不是内存里的同一对象）。
            var replayScript = InputScript.Parse(session.Script.ToJson());
            var replay = runner.Record(replayScript, Cell);
            var liveFingerprint = runner.FingerprintOf(session.Script, Cell, live);
            var replayFingerprint = runner.FingerprintOf(replayScript, Cell, replay);
            Assert.Equal(
                liveFingerprint.Project(runner.Registry, MetricClass.Logic),
                replayFingerprint.Project(runner.Registry, MetricClass.Logic));

            // 不是空转：这一局真的打到了靶子（用同一份记录里的事件数证明，不写死数字）。
            Assert.Contains(live.Events, e => e.Kind == "damage" && e.Source == "player");
            Assert.Equal(live.Events.Count, replay.Events.Count);
            Assert.Equal(live.Ticks.Count, replay.Ticks.Count);
        }

        [Fact]
        public void SpawnEvent_PlacesDummyAtGivenPosition_WithAutoLabelAndHostCallback()
        {
            var script = LabLive.CreateScript("live_spawn_test");
            var ext = new NoopExtension();
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, ext);
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(3.0, 1.0)));
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(3.0, -1.0)));
            Frames(session, 3);

            var ctx = session.Context!;
            Assert.Equal(new[] { "stake@1", "stake@2" }, ctx.Dummies.Select(d => d.Key).ToArray());
            Assert.Equal(ctx.Dummies.Select(d => d.Key).ToArray(), ext.Spawned.ToArray());
            var first = ctx.World.World.GetEntity(ctx.Dummies[0].Value)!;
            Assert.Equal(3.0, first.Position.X, 6);
            Assert.Equal(1.0, first.Position.Y, 6);
            Assert.All(session.Script.Events, e => Assert.Equal(0, e.Tick));
            session.Finish();
        }

        [Fact]
        public void ClearDummies_RemovesEveryDummy_AndAllowsRespawn()
        {
            var script = LabLive.CreateScript("live_clear_test");
            var ext = new NoopExtension();
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, ext);
            session.Inject(new ScriptEvent(0, "breakable", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            session.Inject(new ScriptEvent(0, "mob", ScriptEventKind.Spawn, new Vec2(2.0, 2.0)));
            Frames(session, 3);
            var ctx = session.Context!;
            Assert.Equal(2, ctx.Dummies.Count);
            var ids = ctx.Dummies.Select(d => d.Value).ToArray();

            session.Inject(new ScriptEvent(0, "clear", ScriptEventKind.ClearDummies));
            Frames(session, 3);
            Assert.Empty(ctx.Dummies);
            Assert.Equal(1, ext.Cleared);
            foreach (var id in ids)
            {
                Assert.Null(ctx.World.World.GetEntity(id));
            }

            session.Inject(new ScriptEvent(0, "mob", ScriptEventKind.Spawn, new Vec2(2.0, 2.0)));
            Frames(session, 2);
            Assert.Single(ctx.Dummies);
            session.Finish();
        }

        [Fact]
        public void PresetEvent_SwitchesBasePreset_AndChangesTheResolvedHitStop()
        {
            var script = LabLive.CreateScript("live_preset_test");
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, new NoopExtension());
            Frames(session, 2);
            var feel = session.Context!.World.Gameplay.Feel!.Feel;
            var player = session.Context.PlayerId;
            double HitStopMs()
            {
                feel.Resolver.Invalidate(player, "test");
                return feel.Resolver.Resolve(player).GetAbsolute(Core.Foundation.Feel.FeelFieldNames.AttackerHitstopMs).AsNumber();
            }

            var before = HitStopMs();
            session.Inject(new ScriptEvent(0, "feel.preset.rpg_classic", ScriptEventKind.Preset));
            Frames(session, 2);
            var after = HitStopMs();
            Assert.Equal("feel.preset.rpg_classic", feel.Resolver.Calibration.BasePresetId);

            // 期望值：直接按另一个预设行的 attacker_hitstop_ms 算出来（同一份档案、同一个解析器的另一份标定）。
            var expectedPreset = feel.Profiles.GetPreset("feel.preset.rpg_classic")!;
            var write = expectedPreset.Values.First(w => w.Field == Core.Foundation.Feel.FeelFieldNames.AttackerHitstopMs);
            Assert.Equal(write.Value.AsNumber(), after, 6);
            Assert.NotEqual(before, after);
            session.Finish();
        }

        [Fact]
        public void LoadoutEvent_AppliesWeaponAndArchetypeWrites_AndEmptyClearsThem()
        {
            var script = LabLive.CreateScript("live_loadout_test");
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, new NoopExtension());
            Frames(session, 2);
            var feel = session.Context!.World.Gameplay.Feel!.Feel;
            var player = session.Context.PlayerId;
            double Get(string field)
            {
                feel.Resolver.Invalidate(player, "test");
                return feel.Resolver.Resolve(player).GetAbsolute(field).AsNumber();
            }

            var hitStop = Core.Foundation.Feel.FeelFieldNames.AttackerHitstopMs;
            var baseline = Get(hitStop);
            session.Inject(new ScriptEvent(0, "feel.weapon.greatsword", ScriptEventKind.Loadout));
            Frames(session, 2);
            var greatsword = feel.Profiles.Weapons.First(w => w.Id == "feel.weapon.greatsword");
            var expected = greatsword.Writes.First(w => w.Field == hitStop).Value.AsNumber();
            Assert.Equal(expected, Get(hitStop), 6);
            Assert.NotEqual(baseline, Get(hitStop));

            session.Inject(new ScriptEvent(0, string.Empty, ScriptEventKind.Loadout));
            Frames(session, 2);
            Assert.Equal(baseline, Get(hitStop), 6);
            session.Finish();
        }

        [Fact]
        public void NewEventKinds_RoundTripThroughJson_AndOnlyBumpTheFormatWhenUsed()
        {
            var plain = new InputScript(new ScriptMeta { ScriptId = "x", DurationTicks = 5 }, new List<ScriptEvent>
            {
                new ScriptEvent(1, Attack, ScriptEventKind.Press),
            });
            Assert.Equal(InputScript.FormatVersion, plain.EffectiveFormatVersion);

            var script = new InputScript(new ScriptMeta { ScriptId = "x", DurationTicks = 5 }, new List<ScriptEvent>
            {
                new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(1.5, -2.0), null, "stake@1"),
                new ScriptEvent(1, "clear", ScriptEventKind.ClearDummies),
                new ScriptEvent(2, "feel.preset.rpg_classic", ScriptEventKind.Preset),
                new ScriptEvent(3, "feel.weapon.sword_1h", ScriptEventKind.Loadout),
            });
            Assert.Equal(InputScript.InteractiveFormatVersion, script.EffectiveFormatVersion);
            var again = InputScript.Parse(script.ToJson());
            Assert.Equal(script.ToJson(), again.ToJson());
            Assert.Equal(new Vec2(1.5, -2.0), again.Events[0].Value);
        }

        [Fact]
        public void LiveSession_RejectsInjectionOnScriptedRuns_AndRunToEndOnLiveRuns()
        {
            var script = LabLive.CreateScript("live_guard_test");
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, new NoopExtension());
            Assert.Throws<InvalidOperationException>(() => session.RunToEnd());
            session.Finish();
            Assert.Throws<InvalidOperationException>(() => session.Advance(Frame));

            var scripted = LabTestSupport.Script("feel_combo3");
            var scriptedSession = LabHost.Start(
                LabTestSupport.Runner.DatasetFor(scripted).HostOptions, LabTestSupport.Runner.ResolveScenario(scripted, Cell), scripted,
                LabTestSupport.Runner.DatasetFor(scripted).Catalog, null, null, false);
            Assert.Throws<InvalidOperationException>(() => scriptedSession.Inject(new ScriptEvent(0, Attack, ScriptEventKind.Press)));
            scriptedSession.RunToEnd();
        }

        [Fact]
        public void OverrideEvents_SetAndClearPlayerFieldOverrides_AndMarkersAreInvisibleToLogic()
        {
            var script = LabLive.CreateScript("live_override_test");
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, new NoopExtension());
            Frames(session, 2);
            var feel = session.Context!.World.Gameplay.Feel!.Feel;
            var player = session.Context.PlayerId;
            var field = Core.Foundation.Feel.FeelFieldNames.AttackerHitstopMs;
            double Get()
            {
                feel.Resolver.Invalidate(player, "test");
                return feel.Resolver.Resolve(player).GetAbsolute(field).AsNumber();
            }

            var baseline = Get();
            session.Inject(new ScriptEvent(0, field, ScriptEventKind.Override, new Vec2(222.0, 0.0), null, "player"));
            Frames(session, 2);
            Assert.Equal(222.0, Get(), 6);
            session.Inject(new ScriptEvent(0, "feel.weapon.greatsword", ScriptEventKind.Loadout));
            Frames(session, 2);
            Assert.Equal(222.0, Get(), 6); // 覆盖写在装备行之后，仍然生效（整体重算）
            session.Inject(new ScriptEvent(0, "x", ScriptEventKind.ClearOverrides));
            Frames(session, 2);
            Assert.NotEqual(222.0, Get(), 6);
            session.Finish();

            // 呈现标记不进逻辑：同一段输入，带不带 time_scale/pause 标记，逻辑组逐字节一致。
            string Logic(bool withMarkers)
            {
                var s = LabLive.CreateScript("live_marker_test");
                var run = LabTestSupport.Runner.StartLive(s, Cell, null, new NoopExtension());
                run.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
                Frames(run, 4);
                if (withMarkers)
                {
                    run.Inject(new ScriptEvent(0, "time_scale", ScriptEventKind.Marker, new Vec2(0.25, 0.0)));
                    run.Inject(new ScriptEvent(0, "pause", ScriptEventKind.Marker, new Vec2(1.0, 0.0)));
                }

                Tap(run, Attack);
                Frames(run, 40);
                var recording = run.Finish();
                return LabTestSupport.Runner.FingerprintOf(run.Script, Cell, recording)
                    .Project(LabTestSupport.Runner.Registry, MetricClass.Logic);
            }

            Assert.Equal(Logic(false), Logic(true));
        }

        [Fact]
        public void PresetSegments_SplitTheRunAtEachSwitch_AndPrefixReplayEqualsTheSessionStoppedThere()
        {
            var (session, _, _) = PlaySession();
            var segments = LabLive.PresetSegments(session.Script, "feel.preset.arpg_responsive");
            Assert.Equal(2, segments.Count);
            Assert.Equal("feel.preset.arpg_responsive", segments[0].Preset);
            Assert.Equal("feel.preset.rpg_classic", segments[1].Preset);
            Assert.Equal(segments[0].EndTick, segments[1].StartTick);
            Assert.Equal(session.Script.Meta.DurationTicks, segments[1].EndTick);

            var runner = LabTestSupport.Runner;
            var prefix = LabLive.PrefixScript(session.Script, segments[0].EndTick);
            var full = runner.Record(InputScript.Parse(session.Script.ToJson()), Cell);
            var cut = runner.Record(prefix, Cell);
            Assert.Equal(segments[0].EndTick, cut.Ticks.Count);
            for (var i = 0; i < cut.Ticks.Count; i++)
            {
                Assert.Equal(full.Ticks[i].Position, cut.Ticks[i].Position);
                Assert.Equal(full.Ticks[i].MovementState, cut.Ticks[i].MovementState);
            }

            Assert.True(full.Events.Count(e => e.Tick < segments[0].EndTick) == cut.Events.Count);
        }
    }
}

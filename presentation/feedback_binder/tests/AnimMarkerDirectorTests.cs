using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;
using static Tests.Presentation.FeedbackBinder.ImpactTestKit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// 动画表现标记消费方（ADR-0148，手感设计/04 第 5 节、07 第 1、3 节）：脚步（标记为唯一来源、按材质与档位选音）、拖尾/残影
    /// （trail_start/trail_end 驱动、锚点/世界两种挂接）、fx 挂点、impact 标记放行推迟的闪白（含超时）。
    /// 手感取自真实 <see cref="FeelResolver"/>，期望值由字段值与规则算出。
    /// </summary>
    public sealed class AnimMarkerDirectorTests
    {
        private static readonly Id Unit = Player;
        private static readonly Id TrailVfx = new Id("vfx.kit_trail");

        private sealed class Source : IAnimMarkerSource
        {
            public event Action<Id, string>? AnimMarkerReached;

            public void Raise(Id entity, string marker) => AnimMarkerReached?.Invoke(entity, marker);

            public bool HasSubscribers => AnimMarkerReached != null;
        }

        private sealed class Rig
        {
            public Source Source = new Source();
            public RecordingFeedbackSink Sink = new RecordingFeedbackSink();
            public PresentationDiagnosticsRecorder Diagnostics = new PresentationDiagnosticsRecorder();
            public AnimMarkerDirector Director = null!;
            public string? Material;
            public List<(Id Entity, bool On)> Afterimages = new List<(Id, bool)>();
        }

        private static Rig Make(
            int footstepTier = 3, bool trail = false, bool afterimage = false, bool withTrailRef = true,
            VfxAttachMode? vfxMode = VfxAttachMode.Anchor, string? material = null, bool withFeel = true)
        {
            var writes = new List<FeelWrite>();
            if (trail) writes.Add(new FeelWrite(FeelFieldNames.TrailEnabled, FeelOp.Set, FeelValue.Of(true)));
            if (afterimage) writes.Add(new FeelWrite(FeelFieldNames.AfterimageEnabled, FeelOp.Set, FeelValue.Of(true)));
            if (withTrailRef) writes.Add(Set(FeelFieldNames.TrailRef, TrailVfx.Value));
            var kit = new ImpactTestKit()
                .WithWeapon(Unit, "feel.weapon.kit_marker", writes.ToArray())
                .WithCharacter(Unit, "feel.character.kit_marker", Set(FeelFieldNames.SfxFootstepTier, footstepTier)); // 脚步档以角色为主
            var rig = new Rig { Material = material };
            var diagnostics = rig.Diagnostics;
            var options = new AnimMarkerOptions
            {
                FeelSource = withFeel ? new PresentingImpactFeelSource(kit.Build()) : null,
                SfxLayers = new SfxLayerIndex(new[]
                {
                    FeelSfx("sfx.step_t3_generic", SfxFeelLayer.Footstep, 3),
                    FeelSfx("sfx.step_t3_grass", SfxFeelLayer.Footstep, 3, "grass"),
                    FeelSfx("sfx.step_t1_generic", SfxFeelLayer.Footstep, 1),
                }, diagnostics),
                PositionResolver = _ => new Vec2(4, 5),
                MaterialResolver = _ => rig.Material,
                VfxAttachModeOf = id => id.Equals(TrailVfx) ? vfxMode : null,
                OnAfterimage = (entity, on) => rig.Afterimages.Add((entity, on)),
            };
            rig.Director = new AnimMarkerDirector(rig.Source, rig.Sink, options, diagnostics);
            return rig;
        }

        // ------------------------------------------------------------------ 脚步

        [Fact]
        public void Footstep_PlaysTheSfxRowOfTheFootstepTier_AtTheUnitPosition()
        {
            var rig = Make(footstepTier: 3);
            rig.Source.Raise(Unit, AnimMarkerNames.Footstep);

            var call = Assert.Single(rig.Sink.PlaySfxCalls);
            Assert.Equal(new Id("sfx.step_t3_generic"), call.SfxId);
            Assert.Equal(new Vec2(4, 5), call.At);
            Assert.Equal(1, rig.Director.FootstepCount);
        }

        [Fact]
        public void Footstep_PicksTheRowByGroundMaterial_AndFallsBackToTheGenericRow()
        {
            var rig = Make(footstepTier: 3, material: "grass");
            rig.Source.Raise(Unit, AnimMarkerNames.Footstep);
            Assert.Equal(new Id("sfx.step_t3_grass"), rig.Sink.PlaySfxCalls[0].SfxId);

            rig.Material = "stone"; // 该档没有 stone 行 → 通用行
            rig.Source.Raise(Unit, AnimMarkerNames.Footstep);
            Assert.Equal(new Id("sfx.step_t3_generic"), rig.Sink.PlaySfxCalls[1].SfxId);

            rig.Material = null; // 地图没有声明区域 → 通用
            rig.Source.Raise(Unit, AnimMarkerNames.Footstep);
            Assert.Equal(new Id("sfx.step_t3_generic"), rig.Sink.PlaySfxCalls[2].SfxId);
        }

        [Fact]
        public void Footstep_TierZero_IsOff_AndOtherUnitsUseTheirOwnTier()
        {
            var off = Make(footstepTier: 0);
            off.Source.Raise(Unit, AnimMarkerNames.Footstep);
            Assert.Empty(off.Sink.PlaySfxCalls);

            // 另一个单位没有被覆盖：按框架预设里的脚步档（不是本单位的 3 档）选音。
            var other = Make(footstepTier: 3);
            other.Source.Raise(new Id("unit.someone_else"), AnimMarkerNames.Footstep);
            Assert.DoesNotContain(other.Sink.PlaySfxCalls, c => c.SfxId.Equals(new Id("sfx.step_t3_generic")));
        }

        // ------------------------------------------------------------------ 拖尾 / 残影

        [Fact]
        public void Trail_AnchorMode_StartsOnTrailStart_FollowsTheOwnerAnchor_StopsOnTrailEnd()
        {
            var rig = Make(trail: true, vfxMode: VfxAttachMode.Anchor);

            rig.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            var start = Assert.Single(rig.Sink.PlayVfxCalls);
            Assert.Equal(TrailVfx, start.VfxId);
            Assert.Equal(FeedbackAttachTarget.Source, start.Attach.Target);
            Assert.Equal(Unit, start.Attach.EntityId);
            Assert.NotNull(start.Attach.AnchorId);
            Assert.Equal(1, rig.Director.ActiveTrailCount);

            rig.Source.Raise(Unit, AnimMarkerNames.TrailEnd);
            var stop = Assert.Single(rig.Sink.StopVfxCalls);
            Assert.Equal(TrailVfx, stop.VfxId);
            Assert.Equal(start.Attach.AnchorId, stop.Attach.AnchorId); // 停的就是启的那一个
            Assert.Equal(0, rig.Director.ActiveTrailCount);
        }

        [Fact]
        public void Trail_WorldMode_IsAttachedToTheEntityWithoutAnchor()
        {
            var rig = Make(trail: true, vfxMode: VfxAttachMode.World);
            rig.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            Assert.Null(rig.Sink.PlayVfxCalls[0].Attach.AnchorId);
        }

        [Fact]
        public void Trail_WithoutTheFeelSwitch_PlaysNothing_AndAnUnknownVfxDefIsReportedOnce()
        {
            var off = Make(trail: false);
            off.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            Assert.Empty(off.Sink.PlayVfxCalls);

            var unknown = Make(trail: true, vfxMode: null);
            unknown.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            unknown.Source.Raise(Unit, AnimMarkerNames.TrailEnd);
            unknown.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            Assert.Empty(unknown.Sink.PlayVfxCalls);
            Assert.Single(unknown.Diagnostics.Warnings, w => w.Contains("vfx.def"));

            var noRef = Make(trail: true, withTrailRef: false);
            noRef.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            Assert.Empty(noRef.Sink.PlayVfxCalls);
            Assert.Single(noRef.Diagnostics.Warnings, w => w.Contains("trail_ref"));
        }

        [Fact]
        public void Afterimage_FollowsTheTrailMarkers_WhenTheFeelSwitchIsOn()
        {
            var rig = Make(trail: false, afterimage: true, withTrailRef: false);
            rig.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            rig.Source.Raise(Unit, AnimMarkerNames.TrailEnd);

            Assert.Equal(new[] { (Unit, true), (Unit, false) }, rig.Afterimages);
            Assert.Empty(rig.Sink.PlayVfxCalls);
        }

        [Fact]
        public void Trail_ARestartWithoutAnEnd_StopsTheRunningOneFirst_AndForgetStopsItOnUnitDestroy()
        {
            var rig = Make(trail: true, afterimage: true);
            rig.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            rig.Source.Raise(Unit, AnimMarkerNames.TrailStart);
            Assert.Equal(2, rig.Sink.PlayVfxCalls.Count);
            Assert.Single(rig.Sink.StopVfxCalls);

            rig.Director.Forget(Unit);
            Assert.Equal(2, rig.Sink.StopVfxCalls.Count);
            Assert.Equal(0, rig.Director.ActiveTrailCount);
            Assert.False(rig.Afterimages.Last().On);
        }

        // ------------------------------------------------------------------ fx 挂点

        [Fact]
        public void FxMarker_PlaysTheNamedVfxOnTheUnit()
        {
            var rig = Make();
            rig.Source.Raise(Unit, "fx:vfx.kit_spark");

            var call = Assert.Single(rig.Sink.PlayVfxCalls);
            Assert.Equal(new Id("vfx.kit_spark"), call.VfxId);
            Assert.Equal(Unit, call.Attach.EntityId);
        }

        // ------------------------------------------------------------------ impact：放行推迟项

        [Fact]
        public void ImpactMarker_ReleasesTheDeferredFlashOfThatEntityOnly_AndTimeoutReleasesTheRest()
        {
            var rig = Make();
            var order = new List<string>();
            rig.Director.DeferUntilMarker(Unit, "impact", () => order.Add("unit"), 0.25);
            rig.Director.DeferUntilMarker(new Id("unit.other"), "impact", () => order.Add("other"), 0.25);
            Assert.Equal(2, rig.Director.PendingGateCount);

            rig.Source.Raise(Unit, AnimMarkerNames.Impact);
            Assert.Equal(new[] { "unit" }, order);
            Assert.Equal(1, rig.Director.PendingGateCount);

            // 超时：没有标记也照常闪（剪辑没有该标记时不丢闪白）。
            rig.Director.Update(0.1);
            Assert.Equal(new[] { "unit" }, order);
            rig.Director.Update(0.2);
            Assert.Equal(new[] { "unit", "other" }, order);
            Assert.Equal(0, rig.Director.PendingGateCount);
        }

        [Fact]
        public void Gate_NonPositiveTimeout_RunsImmediately_AndForgetDropsPendingWithoutRunning()
        {
            var rig = Make();
            var ran = 0;
            rig.Director.DeferUntilMarker(Unit, "impact", () => ran++, 0.0);
            Assert.Equal(1, ran);

            rig.Director.DeferUntilMarker(Unit, "impact", () => ran++, 1.0);
            rig.Director.Forget(Unit);
            rig.Director.Update(2.0);
            Assert.Equal(1, ran);
        }

        // ------------------------------------------------------------------ 生命周期与隔离

        [Fact]
        public void Dispose_Unsubscribes_AndAFaultyCallbackDoesNotBreakOtherMarkers()
        {
            var rig = Make(trail: true, afterimage: true);
            rig.Director.Dispose();
            Assert.False(rig.Source.HasSubscribers);
            rig.Source.Raise(Unit, AnimMarkerNames.Footstep);
            Assert.Empty(rig.Sink.PlaySfxCalls);

            var faulty = new Source();
            var sink = new RecordingFeedbackSink();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var kit = new ImpactTestKit()
                .WithWeapon(Unit, "feel.weapon.kit_marker", new FeelWrite(FeelFieldNames.AfterimageEnabled, FeelOp.Set, FeelValue.Of(true)))
                .WithCharacter(Unit, "feel.character.kit_marker", Set(FeelFieldNames.SfxFootstepTier, 3));
            var director = new AnimMarkerDirector(faulty, sink, new AnimMarkerOptions
            {
                FeelSource = new PresentingImpactFeelSource(kit.Build()),
                SfxLayers = new SfxLayerIndex(new[] { FeelSfx("sfx.step_t3_generic", SfxFeelLayer.Footstep, 3) }, diagnostics),
                OnAfterimage = (_, _) => throw new InvalidOperationException("boom"),
            }, diagnostics);

            faulty.Raise(Unit, AnimMarkerNames.TrailStart); // 回调抛异常：只记诊断，不外抛
            faulty.Raise(Unit, AnimMarkerNames.Footstep);   // 之后的标记照常工作
            Assert.Single(sink.PlaySfxCalls);
            Assert.Contains(diagnostics.Warnings, w => w.Contains("trail_start"));
            director.Dispose();
        }
    }
}

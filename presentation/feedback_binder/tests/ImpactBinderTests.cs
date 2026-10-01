using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.FeedbackBinder.Schema;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;
using static Tests.Presentation.FeedbackBinder.ImpactTestKit;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// 反馈包经 <c>FeedbackBinder</c> 的端到端冒烟（事件 → 规则 <c>play_impact</c> → 流水线 → sink），以及数据形状：
    /// <c>feedback.impact_profile</c> schema/解析、<c>play_impact</c> 规则解析、呈现型手感与判定型手感隔离的不变量。
    /// </summary>
    public sealed class ImpactBinderTests
    {
        private static readonly Id ProfileId = new Id("feedback.impact_profile.kit_sword");
        private static readonly Id Enemy = new Id("unit.kit_enemy_1");

        private static (FeedbackBinderCore Binder, IEventBus Bus, ImpactRecordingSink Sink, ImpactPipeline Pipeline)
            MakeBinder(double cap = 0.03, double gain = 0.02)
        {
            var kit = new ImpactTestKit()
                .WithWeapon(Player, "feel.weapon.kit_sword",
                    Set(FeelFieldNames.CameraImpulseGain, gain), Set(FeelFieldNames.SfxImpactTier, 3),
                    Set(FeelFieldNames.SfxWhiffTier, 2), Set(FeelFieldNames.SfxMaterial, "metal_light"),
                    Set(FeelFieldNames.ImpactProfileRef, ProfileId.Value))
                .WithCharacter(Player, "feel.character.kit_player", Set(FeelFieldNames.CameraShakeCap, cap));
            var resolver = kit.Build();
            var profile = new ImpactProfile(ProfileId, new[]
            {
                Variant("medium", ImpactOutcome.Hit, camera: new ImpactCameraSpec(1.0, null, 120),
                    flash: new ImpactFlashSpec(new Id("camera_profile.flash_hit"), FeedbackAttachTarget.Target),
                    sfx: new[] { new ImpactSfxSpec(SfxFeelLayer.Impact, null) }),
            });
            var options = new ImpactOptions
            {
                FeelSource = new PresentingImpactFeelSource(resolver),
                ProfileResolver = id => id.Equals(ProfileId) ? profile : null,
                SfxLayers = new SfxLayerIndex(new[]
                {
                    FeelSfx("sfx.impact_t3_metal_light", SfxFeelLayer.Impact, 3, "metal_light"),
                    FeelSfx("sfx.whiff_t2_generic", SfxFeelLayer.Whiff, 2),
                }),
                CameraOwnerResolver = () => Player,
                StepSeconds = StepSeconds,
            };
            var pipeline = new ImpactPipeline(options);
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new ImpactRecordingSink();
            var rules = new[]
            {
                new FeedbackRule(new Id("feedback.impact_default"), RulesEventKeys.CombatHitConfirmed, null,
                    new FeedbackAction[] { new PlayImpactAction(null) }),
            };
            var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink,
                null, null, null, null, null, null, null, null, pipeline);
            return (binder, bus, sink, pipeline);
        }

        private static CombatHitConfirmedEvent HitEvent(Id target, double ratio = 1.0, Id? castInstanceId = null) =>
            new CombatHitConfirmedEvent(
                new Id("attack.kit_1"), 0, Player, target, null, HitResult.Hit, 10, ratio, false, false,
                new Vec2(1, 1), new Vec2(0, 1), new Vec2(1, 0), "medium", 0, 0, HitReaction.Flinch, castInstanceId);

        [Fact]
        public void HitConfirmed_ThroughDefaultRule_PlaysSfxFlashAndOneMergedCameraCueAtTickEnd()
        {
            var (binder, bus, sink, _) = MakeBinder();

            // 同一 tick 五个目标被命中；tick 结束前什么都还没播（合并窗口是 tick）。
            for (var i = 1; i <= 5; i++)
            {
                bus.PublishImmediate(HitEvent(new Id("unit.kit_enemy_" + i)));
            }
            Assert.Empty(sink.Cues);
            Assert.Empty(sink.Sfx);

            bus.PublishImmediate(new SimTickFinishedEvent(42));

            Assert.Equal(5, sink.Flashes.Count);
            Assert.Equal(4, sink.Sfx.Count); // 缺省 MaxImpactsPerTick = 4：第 5 个命中静默
            Assert.All(sink.Sfx, s => Assert.Equal(new Id("sfx.impact_t3_metal_light"), s.SfxId));
            var cue = Assert.Single(sink.Cues);
            Assert.Equal(Math.Min(0.02 * 1.0, 0.03), cue.Magnitude, 12);
            Assert.Equal(5, cue.HitCount);
            binder.Dispose();
        }

        [Fact]
        public void WithoutTickFinished_UpdateStillFlushesPendingImpacts()
        {
            var (binder, bus, sink, _) = MakeBinder();

            bus.PublishImmediate(HitEvent(Enemy));
            Assert.Empty(sink.Cues);

            binder.Update(1.0 / 60.0);

            Assert.Single(sink.Cues);
            binder.Dispose();
        }

        [Fact]
        public void ActiveWindowWithoutHits_ThroughBus_PlaysWhiffSfx_ThenHitsSuppressIt()
        {
            var (binder, bus, sink, _) = MakeBinder();

            bus.PublishImmediate(new ActionMarkerEvent(Player, new Id("cast.kit_1"), "active_start"));
            bus.PublishImmediate(new ActionMarkerEvent(Player, new Id("cast.kit_1"), "active_end"));
            bus.PublishImmediate(new SimTickFinishedEvent(1));
            Assert.Equal(new Id("sfx.whiff_t2_generic"), Assert.Single(sink.Sfx).SfxId);

            sink.Sfx.Clear();
            bus.PublishImmediate(new ActionMarkerEvent(Player, new Id("cast.kit_2"), "active_start"));
            bus.PublishImmediate(HitEvent(Enemy));
            bus.PublishImmediate(new ActionMarkerEvent(Player, new Id("cast.kit_2"), "active_end"));
            bus.PublishImmediate(new SimTickFinishedEvent(2));
            Assert.DoesNotContain(sink.Sfx, s => s.SfxId.Equals(new Id("sfx.whiff_t2_generic")));
            binder.Dispose();
        }

        [Fact]
        public void WhiffWindow_ThroughBus_IsPairedByTheCastInstanceIdOfHitConfirmed()
        {
            var (binder, bus, sink, _) = MakeBinder();
            var cast1 = new Id("cast.kit_1");
            var cast2 = new Id("cast.kit_2");

            // 动作 2 的窗口里只有动作 1（上一段）的迟到命中：动作 2 挥空。
            bus.PublishImmediate(new ActionMarkerEvent(Player, cast2, "active_start"));
            bus.PublishImmediate(HitEvent(Enemy, castInstanceId: cast1));
            bus.PublishImmediate(new ActionMarkerEvent(Player, cast2, "active_end"));
            bus.PublishImmediate(new SimTickFinishedEvent(1));
            Assert.Contains(sink.Sfx, s => s.SfxId.Equals(new Id("sfx.whiff_t2_generic")));

            // 命中带着本动作实例 id：不挥空。
            sink.Sfx.Clear();
            bus.PublishImmediate(new ActionMarkerEvent(Player, cast2, "active_start"));
            bus.PublishImmediate(HitEvent(Enemy, castInstanceId: cast2));
            bus.PublishImmediate(new ActionMarkerEvent(Player, cast2, "active_end"));
            bus.PublishImmediate(new SimTickFinishedEvent(2));
            Assert.DoesNotContain(sink.Sfx, s => s.SfxId.Equals(new Id("sfx.whiff_t2_generic")));
            binder.Dispose();
        }

        [Fact]
        public void HitstopEvents_ReachSinkAsFreezeAndRelease()
        {
            var (binder, bus, sink, pipeline) = MakeBinder();

            bus.PublishImmediate(new FeelHitstopStartedEvent(new[] { Enemy }, 5, new Id("attack.kit_1")));
            bus.PublishImmediate(new SimTickFinishedEvent(1));
            var (units, ticks, _) = Assert.Single(sink.Freezes);
            Assert.Equal(new[] { Enemy }, units);
            Assert.Equal(5, ticks);
            Assert.True(pipeline.Freezes.IsFrozen(Enemy));

            bus.PublishImmediate(new FeelHitstopEndedEvent(new[] { Enemy }));
            bus.PublishImmediate(new SimTickFinishedEvent(6));
            Assert.Single(sink.Releases);
            Assert.False(pipeline.Freezes.IsFrozen(Enemy));
            binder.Dispose();
        }

        [Fact]
        public void PlayImpactRule_WithoutPipeline_RecordsDiagnosticAndSkips_NoException()
        {
            var bus = FeedbackBinderTestSupport.CreateBus();
            var sink = new ImpactRecordingSink();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var rules = new[]
            {
                new FeedbackRule(new Id("feedback.impact_default"), RulesEventKeys.CombatHitConfirmed, null,
                    new FeedbackAction[] { new PlayImpactAction(null) }),
            };
            using var binder = new FeedbackBinderCore(
                bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, diagnostics: diagnostics);

            bus.PublishImmediate(HitEvent(Enemy));

            Assert.Contains(diagnostics.Warnings, w => w.Contains("play_impact"));
            Assert.Empty(sink.Sfx);
        }

        // ------------------------------------------------------------------ 数据形状

        private static string Envelope(string table, string rows) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";

        private const string ProfileRow =
            "{\"id\":\"feedback.impact_profile.kit_sword\",\"variants\":[" +
            "{\"class\":\"medium\",\"outcome\":\"hit\"," +
            "\"flash\":{\"profile_id\":\"camera_profile.flash_hit\",\"target\":\"target\"}," +
            "\"vfx\":{\"vfx_id\":\"vfx.kit_hit\",\"attach\":\"contact\",\"orient\":\"contact_normal\"," +
            "\"scale_by_ratio\":[{\"x\":0,\"y\":0.5},{\"x\":1,\"y\":1.5}]}," +
            "\"sfx\":[{\"layer\":\"impact\"},{\"layer\":\"sweetener\",\"tier\":2}]," +
            "\"camera\":{\"impulse_gain\":1.5,\"shake_profile\":\"feedback.shake.kit\",\"decay_ms\":90}," +
            "\"floating_text\":{\"style_id\":\"feedback.style.normal\"}," +
            "\"trail\":{\"start\":\"hit\",\"end\":\"active_end\"}," +
            "\"freeze_layers\":{\"particles\":true}," +
            "\"intensity\":{\"ratio_curve\":[{\"x\":0,\"y\":1},{\"x\":1,\"y\":2}],\"crit_multiplier\":1.5,\"kill_multiplier\":2}}," +
            "{\"class\":\"medium\",\"outcome\":\"avoided\",\"sfx\":[{\"layer\":\"whiff\"}]}]}";

        [Fact]
        public void ImpactProfileTable_LoadsAndParsesEveryDeclaredField()
        {
            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.impact_profile"] = "[" + ProfileRow + "]",
            });
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var profile = ImpactProfile.FromRecord(registry.GetAll(FeedbackSchemas.ImpactProfile.Name).Single());

            Assert.Equal(2, profile.Variants.Count);
            var hit = profile.Select("medium", ImpactOutcome.Hit)!;
            Assert.Equal(new Id("camera_profile.flash_hit"), hit.Flash!.ProfileId);
            Assert.Equal(ImpactVfxAttach.Contact, hit.Vfx!.Attach);
            Assert.Equal(ImpactVfxOrient.ContactNormal, hit.Vfx.Orient);
            Assert.Equal(1.0, hit.Vfx.ScaleByRatio!.Evaluate(0.5), 12);
            Assert.Equal(new[] { SfxFeelLayer.Impact, SfxFeelLayer.Sweetener }, hit.Sfx.Select(s => s.Layer).ToArray());
            Assert.Equal(2, hit.Sfx[1].Tier);
            Assert.Null(hit.Sfx[0].Tier);
            Assert.Equal(1.5, hit.Camera!.ImpulseGain);
            Assert.Equal(90.0, hit.Camera.DecayMs);
            Assert.Equal(new Id("feedback.shake.kit"), hit.Camera.ShakeProfile);
            Assert.Equal(new Id("feedback.style.normal"), hit.FloatingTextStyle);
            Assert.True(hit.FreezeLayers.Particles);
            Assert.False(hit.FreezeLayers.Trail);
            Assert.Equal(1.5, hit.Intensity.CritMultiplier);
            Assert.Equal(2.0, hit.Intensity.KillMultiplier);
            Assert.Equal(1.5, hit.Intensity.RatioFactor(0.5), 12);
        }

        [Fact]
        public void ImpactProfileTable_UnknownEnumAndDuplicateVariant_AreRejected()
        {
            var badEnum = ProfileRow.Replace("\"outcome\":\"hit\"", "\"outcome\":\"smash\"");
            var (_, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.impact_profile"] = "[" + badEnum + "]",
            });
            Assert.True(report.IsBlocking);

            // 登记层表达不了 (class, outcome) 复合键唯一，由 FromRecord 抛 DataFieldException。
            var dup = ProfileRow.Replace("\"outcome\":\"avoided\"", "\"outcome\":\"hit\"");
            var (registry, dupReport) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.impact_profile"] = "[" + dup + "]",
            });
            Assert.False(dupReport.IsBlocking, string.Join("; ", dupReport.Issues));
            Assert.Throws<DataFieldException>(() => ImpactProfile.FromRecord(registry.GetAll(FeedbackSchemas.ImpactProfile.Name).Single()));
        }

        [Fact]
        public void PlayImpactBinding_ParsesFromRecord_WithAndWithoutExplicitProfile()
        {
            var rows = "[{\"id\":\"feedback.imp_a\",\"event\":\"combat.hit_confirmed\",\"actions\":[{\"kind\":\"play_impact\",\"params\":{}}]}," +
                "{\"id\":\"feedback.imp_b\",\"event\":\"combat.damage_dealt\",\"actions\":[{\"kind\":\"play_impact\",\"params\":{\"profile_id\":\"feedback.impact_profile.kit_sword\"}}]}]";
            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.impact_profile"] = "[" + ProfileRow + "]",
                ["feedback.binding"] = rows,
            });
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var parsed = registry.GetAll(FeedbackSchemas.Binding.Name)
                .Select(r => FeedbackRule.FromRecord(r, global::Core.Rules.ExprHost.RulesExprSchema.Base))
                .OrderBy(r => r.Id.Value, StringComparer.Ordinal).ToList();

            Assert.Null(Assert.IsType<PlayImpactAction>(Assert.Single(parsed[0].Actions)).ProfileId);
            Assert.Equal(ProfileId, Assert.IsType<PlayImpactAction>(Assert.Single(parsed[1].Actions)).ProfileId);
        }

        // ------------------------------------------------------------------ 不变量：呈现型手感不改判定型手感

        [Fact]
        public void PresentingProfiles_NeverChangeJudgingFeel_AndJudgingFieldsAreUnreadableFromThePresentingView()
        {
            var neutral = new ImpactTestKit().Build();
            var loud = new ImpactTestKit()
                .WithWeapon(Player, "feel.weapon.kit_loud",
                    Set(FeelFieldNames.CameraImpulseGain, 0.15), Set(FeelFieldNames.SfxImpactTier, 5),
                    Set(FeelFieldNames.SfxMaterial, "stone_heavy"), Set(FeelFieldNames.ImpactVfxScale, 3.0),
                    Set(FeelFieldNames.ImpactProfileRef, ProfileId.Value))
                .WithCharacter(Player, "feel.character.kit_loud",
                    Set(FeelFieldNames.CameraFollowLagMs, 400), Set(FeelFieldNames.CameraLookAhead, 3),
                    Set(FeelFieldNames.CameraDeadZoneWidth, 2), Set(FeelFieldNames.CameraDampingXMs, 300),
                    Set(FeelFieldNames.CameraCombatZoomDelta, 1.5), Set(FeelFieldNames.CameraShakeCap, 0.4))
                .Build();

            var judgingBefore = Snapshot(loud.ResolveJudging(Player));
            Assert.Equal(Snapshot(neutral.ResolveJudging(Player)), judgingBefore);

            // 跑一遍完整的反馈流水线与镜头档案读取，再核对判定型视图一字未动。
            var pipeline = new ImpactPipeline(new ImpactOptions
            {
                FeelSource = new PresentingImpactFeelSource(loud),
                ProfileResolver = _ => new ImpactProfile(ProfileId, new[]
                {
                    Variant("medium", ImpactOutcome.Hit, camera: new ImpactCameraSpec(1.0, null, 100),
                        sfx: new[] { new ImpactSfxSpec(SfxFeelLayer.Impact, null) }),
                }),
                CameraOwnerResolver = () => Player,
            });
            pipeline.Offer(Hit(Player, Enemy), null);
            Assert.NotNull(pipeline.Flush()!.Camera);
            _ = global::Presentation.Camera.CameraFeelProfile.FromPresenting(loud.ResolvePresenting(Player));

            Assert.Equal(judgingBefore, Snapshot(loud.ResolveJudging(Player)));
            Assert.Equal(loud.GetVersion(Player), loud.Resolve(Player).Version);

            // 呈现型视图读判定型字段抛异常（表现层在类型上读不到判定型手感）。
            var presenting = loud.ResolvePresenting(Player);
            Assert.Throws<FeelHalfViolationException>(() => presenting.GetNumber(FeelFieldNames.AttackerHitstopMs));
            Assert.Throws<FeelHalfViolationException>(() => presenting.GetText(FeelFieldNames.ImpactClass));
        }

        private static string Snapshot(JudgingFeelView view)
        {
            var parts = new List<string>();
            foreach (var name in view.Names)
            {
                var v = view.GetAbsolute(name);
                parts.Add(name + "=" + v + (view.HasTicks(name) ? "/" + view.GetTicks(name) : string.Empty));
            }
            return string.Join("|", parts);
        }
    }
}

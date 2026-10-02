using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;
using static Tests.Presentation.FeedbackBinder.ImpactTestKit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// 打击反馈包流水线运行时冒烟（手感设计/07 第 7 节）：真实 <see cref="FeelResolver"/> 解析的呈现型手感 +
    /// <see cref="ImpactPipeline"/>，期望值全部由写入的字段值与规则公式算出。
    /// </summary>
    public sealed class ImpactPipelineTests
    {
        private const double Gain = 0.02;      // camera_impulse_gain（武器，画面高度比例）
        private const double DefaultCap = 0.03; // camera_shake_cap（角色，画面高度比例）
        private static readonly Id ProfileId = new Id("feedback.impact_profile.kit_sword");
        private static readonly Id Weapon = new Id("unit.weapon_unused");
        private static readonly Id Enemy1 = new Id("unit.kit_enemy_1");

        private sealed class Rig
        {
            public ImpactPipeline Pipeline = null!;
            public ImpactOptions Options = null!;
            public PresentationDiagnosticsRecorder Diagnostics = null!;
            public FeelResolver Resolver = null!;
        }

        private static Rig Make(
            ImpactProfile profile, double gain = Gain, double cap = DefaultCap, double minIntervalMs = 0,
            IEnumerable<SfxDef>? sfx = null, Action<ImpactOptions>? tweak = null, string? userSetting = null,
            int impactTier = 3, string material = "metal_light")
        {
            var weaponWrites = new List<FeelWrite>
            {
                Set(FeelFieldNames.CameraImpulseGain, gain),
                Set(FeelFieldNames.SfxImpactTier, impactTier),
                Set(FeelFieldNames.SfxWhiffTier, 2),
                Set(FeelFieldNames.SfxMaterial, material),
                Set(FeelFieldNames.ImpactProfileRef, ProfileId.Value),
                Set(FeelFieldNames.ImpactVfxScale, 1.5),
            };
            var characterWrites = new List<FeelWrite>
            {
                Set(FeelFieldNames.CameraShakeCap, cap),
                Set(FeelFieldNames.CameraImpulseMinIntervalMs, minIntervalMs),
            };
            if (userSetting != null)
            {
                characterWrites.Add(Set(FeelFieldNames.CameraUserIntensitySetting, userSetting));
            }

            var kit = new ImpactTestKit()
                .WithWeapon(Player, "feel.weapon.kit_sword", weaponWrites.ToArray())
                .WithCharacter(Player, "feel.character.kit_player", characterWrites.ToArray());
            var resolver = kit.Build();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var sfxRows = sfx ?? new[]
            {
                FeelSfx("sfx.impact_t3_metal_light", SfxFeelLayer.Impact, 3, "metal_light"),
                FeelSfx("sfx.impact_t3_generic", SfxFeelLayer.Impact, 3),
                FeelSfx("sfx.impact_t2_generic", SfxFeelLayer.Impact, 2),
                FeelSfx("sfx.whiff_t2_generic", SfxFeelLayer.Whiff, 2),
                FeelSfx("sfx.whiff_t1_generic", SfxFeelLayer.Whiff, 1),
            };
            var options = new ImpactOptions
            {
                FeelSource = new PresentingImpactFeelSource(resolver),
                ProfileResolver = id => id.Equals(ProfileId) ? profile : null,
                SfxLayers = new SfxLayerIndex(sfxRows, diagnostics),
                CameraOwnerResolver = () => Player,
                StepSeconds = StepSeconds,
            };
            tweak?.Invoke(options);
            return new Rig { Pipeline = new ImpactPipeline(options, diagnostics), Options = options, Diagnostics = diagnostics, Resolver = resolver };
        }

        private static ImpactProfile SwordProfile(
            ImpactIntensity? intensity = null, double cameraGain = 1.0, double decayMs = 120.0, Id? shake = null,
            ImpactFreezeLayers? freeze = null, params ImpactVariant[] extra)
        {
            var variants = new List<ImpactVariant>
            {
                Variant("medium", ImpactOutcome.Hit, camera: new ImpactCameraSpec(cameraGain, shake, decayMs), intensity: intensity,
                    flash: new ImpactFlashSpec(new Id("camera_profile.flash_hit"), FeedbackAttachTarget.Target),
                    vfx: new ImpactVfxSpec(new Id("vfx.kit_hit"), ImpactVfxAttach.Contact, ImpactVfxOrient.WorldDirection, null),
                    freeze: freeze,
                    sfx: new[] { new ImpactSfxSpec(SfxFeelLayer.Impact, null) }),
            };
            variants.AddRange(extra);
            return new ImpactProfile(ProfileId, variants);
        }

        // ------------------------------------------------------------------ 一次命中 → 震屏幅度/时长 tick/音效层 id

        [Fact]
        public void OneHit_GivesShakeAmplitudeDurationTicksAndSfxLayerId()
        {
            // 对照：中性武器（冲击增益 0）下同一个反馈包、同一次命中，镜头冲击幅度为 0 → 没有镜头提示。
            var neutral = Make(SwordProfile(), gain: 0.0);
            neutral.Pipeline.Offer(Hit(Player, Enemy1), null);
            var before = neutral.Pipeline.Flush()!;
            Assert.Null(before.Camera);

            // 非缺省档案：同一次命中。
            var rig = Make(SwordProfile(decayMs: 120.0));
            Assert.True(rig.Pipeline.Offer(Hit(Player, Enemy1, contact: new Vec2(3, 4)), null));
            var batch = rig.Pipeline.Flush()!;

            var cue = Assert.IsType<ImpactCameraCue>(batch.Camera);
            Assert.Equal(Math.Min(Gain * 1.0, DefaultCap), cue.Magnitude, 12);
            Assert.Equal(FeelCalibration.MillisecondsToTicks(120.0, StepSeconds), cue.DurationTicks);
            Assert.Equal(7, cue.DurationTicks); // 120ms / 16.667ms = 7.2 → 7

            var plan = Assert.Single(batch.Plans);
            var sfx = Assert.Single(plan.Sfx);
            Assert.Equal(SfxFeelLayer.Impact, sfx.Layer);
            Assert.Equal(new Id("sfx.impact_t3_metal_light"), sfx.SfxId);
            Assert.False(sfx.FellBack);

            // 特效：接触点挂接，大小倍率 = 手感特效倍率 1.5 × 曲线因子 1 × 结局因子 1。
            Assert.Equal(new Id("vfx.kit_hit"), plan.VfxId);
            Assert.Equal(new Vec2(3, 4), plan.VfxAttach.WorldPosition);
            Assert.Equal(1.5, plan.VfxParameters!["scale"], 12);
            Assert.Equal(Math.Atan2(0, 1), plan.VfxParameters["orient_rad"], 12);
            Assert.Equal(Enemy1, plan.FlashEntity);
        }

        // ------------------------------------------------------------------ 群体：同 tick 合并 = min(max, cap)

        [Theory]
        [InlineData(0.03)]
        [InlineData(0.012)]
        public void FiveTargetsInOneTick_MergeToSingleImpulse_MinOfMaxAndCap(double cap)
        {
            var identity = new PiecewiseCurve(new[] { new CurvePoint(0, 0), new CurvePoint(1, 1) });
            var rig = Make(SwordProfile(intensity: new ImpactIntensity(identity, 1.0, 1.0)), cap: cap);

            var ratios = new[] { 0.2, 0.4, 0.6, 0.8, 1.0 };
            for (var i = 0; i < ratios.Length; i++)
            {
                rig.Pipeline.Offer(Hit(Player, new Id("unit.kit_enemy_" + (i + 1)), ratio: ratios[i], direction: new Vec2(1, 0)), null);
            }
            var batch = rig.Pipeline.Flush()!;

            Assert.Equal(5, batch.Plans.Count);
            var cue = Assert.IsType<ImpactCameraCue>(batch.Camera);
            var expectedMax = Gain * ratios.Max();
            Assert.Equal(expectedMax, cue.UncappedMagnitude, 12);
            Assert.Equal(Math.Min(expectedMax, cap), cue.Magnitude, 12);
            Assert.Equal(5, cue.HitCount);
            Assert.Equal(new Vec2(1, 0), cue.Direction);
            // 恰好一次：再出批没有任何内容。
            Assert.Null(rig.Pipeline.Flush());
        }

        [Fact]
        public void MergedDirection_IsMagnitudeWeightedSum()
        {
            var identity = new PiecewiseCurve(new[] { new CurvePoint(0, 0), new CurvePoint(1, 1) });
            var rig = Make(SwordProfile(intensity: new ImpactIntensity(identity, 1.0, 1.0)), cap: 0.2);
            rig.Pipeline.Offer(Hit(Player, Enemy1, ratio: 1.0, direction: new Vec2(1, 0)), null);
            rig.Pipeline.Offer(Hit(Player, new Id("unit.kit_enemy_2"), ratio: 1.0, direction: new Vec2(-1, 0)), null);
            rig.Pipeline.Offer(Hit(Player, new Id("unit.kit_enemy_3"), ratio: 0.5, direction: new Vec2(0, 1)), null);

            var cue = rig.Pipeline.Flush()!.Camera!;

            // 加权和 = (1·(1,0) + 1·(-1,0) + 0.5·(0,1)) × gain → 单位化后是 (0,1)。
            Assert.Equal(0.0, cue.Direction.X, 12);
            Assert.Equal(1.0, cue.Direction.Y, 12);
        }

        // ------------------------------------------------------------------ 音效：缺材质层回落、限数、不报错

        [Fact]
        public void MissingMaterialLayer_FallsBackToSameTierGenericRow()
        {
            var rows = new[]
            {
                FeelSfx("sfx.impact_t3_generic", SfxFeelLayer.Impact, 3),
                FeelSfx("sfx.impact_t2_metal_light", SfxFeelLayer.Impact, 2, "metal_light"),
            };
            var rig = Make(SwordProfile(), sfx: rows);
            rig.Pipeline.Offer(Hit(Player, Enemy1), null);

            var sfx = Assert.Single(Assert.Single(rig.Pipeline.Flush()!.Plans).Sfx);

            // 请求 (impact, 档 3, metal_light)：档 3 没有材质行 → 同档 generic 行（先降材质、后降档）。
            Assert.Equal(new Id("sfx.impact_t3_generic"), sfx.SfxId);
            Assert.True(sfx.FellBack);
            Assert.Equal(3, sfx.Tier);
            Assert.Equal(SfxLayerIndex.GenericMaterial, sfx.Material);
        }

        [Fact]
        public void MissingTierAndMaterial_FallsBackDownTiers_AndMissingEverything_IsSilentNotAnError()
        {
            var lower = Make(SwordProfile(), sfx: new[] { FeelSfx("sfx.impact_t1_generic", SfxFeelLayer.Impact, 1) });
            lower.Pipeline.Offer(Hit(Player, Enemy1), null);
            Assert.Equal(new Id("sfx.impact_t1_generic"), Assert.Single(Assert.Single(lower.Pipeline.Flush()!.Plans).Sfx).SfxId);

            var none = Make(SwordProfile(), sfx: Array.Empty<SfxDef>());
            none.Pipeline.Offer(Hit(Player, Enemy1), null);
            var plan = Assert.Single(none.Pipeline.Flush()!.Plans);
            Assert.Empty(plan.Sfx);
            Assert.Contains(none.Diagnostics.Warnings, w => w.Contains("没有登记"));
        }

        [Fact]
        public void SfxPerTick_CappedByMaxImpactsPerTick_ButOtherFeedbackStillPlaysForEveryTarget()
        {
            var rig = Make(SwordProfile(), tweak: o => o.MaxImpactsPerTick = 2);
            for (var i = 0; i < 5; i++)
            {
                rig.Pipeline.Offer(Hit(Player, new Id("unit.kit_enemy_" + (i + 1))), null);
            }

            var plans = rig.Pipeline.Flush()!.Plans;

            Assert.Equal(5, plans.Count);
            Assert.Equal(2, plans.Count(p => p.Sfx.Count > 0));
            Assert.Equal(5, plans.Count(p => p.FlashEntity.HasValue));
            Assert.Equal(5, plans.Count(p => p.VfxId.HasValue));
        }

        [Fact]
        public void VfxPerTick_CapApplies_WhenConfigured()
        {
            var rig = Make(SwordProfile(), tweak: o => o.MaxVfxPerTick = 3);
            for (var i = 0; i < 5; i++)
            {
                rig.Pipeline.Offer(Hit(Player, new Id("unit.kit_enemy_" + (i + 1))), null);
            }
            Assert.Equal(3, rig.Pipeline.Flush()!.Plans.Count(p => p.VfxId.HasValue));
        }

        // ------------------------------------------------------------------ 幅度缩放公式

        [Fact]
        public void IntensityScaling_FollowsRatioCurveAndCritKillMultipliers()
        {
            var curve = new PiecewiseCurve(new[] { new CurvePoint(0, 0.5), new CurvePoint(1, 1.5) });
            var intensity = new ImpactIntensity(curve, critMultiplier: 2.0, killMultiplier: 3.0);
            var killVariant = Variant("medium", ImpactOutcome.Kill, camera: new ImpactCameraSpec(1.0, null, 120), intensity: intensity);
            var rig = Make(SwordProfile(extra: killVariant), cap: 0.5);

            rig.Pipeline.Offer(Hit(Player, Enemy1, ratio: 0.5, crit: true, kill: true), null);
            var cue = rig.Pipeline.Flush()!.Camera!;

            // 公式：gain × 变体增益 × curve(0.5) × crit × kill = 0.02 × 1 × 1.0 × 2 × 3。
            Assert.Equal(Gain * 1.0 * curve.Evaluate(0.5) * 2.0 * 3.0, cue.Magnitude, 12);
        }

        // ------------------------------------------------------------------ 变体选择与回落

        [Fact]
        public void VariantSelection_FallsBackOutcomeToHit_ThenClassToMedium()
        {
            var profile = SwordProfile();
            // 暴击/击杀缺项 → hit；heavy 缺项 → medium。
            Assert.Same(profile.Variants[0], profile.Select("medium", ImpactOutcome.Crit));
            Assert.Same(profile.Variants[0], profile.Select("heavy", ImpactOutcome.Kill));
            // 回避与挥空不回落到 hit。
            Assert.Null(profile.Select("medium", ImpactOutcome.Avoided));
            Assert.Null(profile.Select("heavy", ImpactOutcome.Whiff));
        }

        [Fact]
        public void AvoidedOutcome_PlaysNeitherSuccessfulHitFeedbackNorImpactLayer()
        {
            // 没有 avoided 变体：整次不播反馈包，也不回落到成功命中的完整反馈。
            var noAvoided = Make(SwordProfile());
            Assert.False(noAvoided.Pipeline.Offer(Hit(Player, Enemy1, result: HitResult.Dodge, amount: 0), null));
            Assert.Null(noAvoided.Pipeline.Flush());

            // 数据里即使给 avoided 变体误写了 impact 层，流水线也强制忽略。
            var avoided = Variant("medium", ImpactOutcome.Avoided,
                sfx: new[] { new ImpactSfxSpec(SfxFeelLayer.Impact, null), new ImpactSfxSpec(SfxFeelLayer.Whiff, null) });
            var rig = Make(SwordProfile(extra: avoided));
            Assert.True(rig.Pipeline.Offer(Hit(Player, Enemy1, result: HitResult.Dodge, amount: 0), null));
            var plan = Assert.Single(rig.Pipeline.Flush()!.Plans);

            Assert.Equal(ImpactOutcome.Avoided, plan.Outcome);
            Assert.DoesNotContain(plan.Sfx, s => s.Layer == SfxFeelLayer.Impact);
            Assert.Equal(new Id("sfx.whiff_t2_generic"), Assert.Single(plan.Sfx).SfxId);
        }

        // ------------------------------------------------------------------ 限频、玩家强度

        [Fact]
        public void ImpulseMinInterval_DropsSecondImpulseInsideTheInterval()
        {
            var rig = Make(SwordProfile(), minIntervalMs: 80);

            rig.Pipeline.SetTick(10);
            rig.Pipeline.Offer(Hit(Player, Enemy1), null);
            Assert.NotNull(rig.Pipeline.Flush()!.Camera);

            rig.Pipeline.SetTick(11); // 16.7ms 后：在 80ms 间隔内
            rig.Pipeline.Offer(Hit(Player, Enemy1), null);
            var second = rig.Pipeline.Flush()!;
            Assert.Null(second.Camera);
            Assert.True(second.CameraDroppedByInterval);
            Assert.Single(second.Plans); // 其它表现照常

            rig.Pipeline.SetTick(16); // 100ms 后：间隔已过
            rig.Pipeline.Offer(Hit(Player, Enemy1), null);
            Assert.NotNull(rig.Pipeline.Flush()!.Camera);
        }

        [Fact]
        public void UserIntensity_ZeroDisablesImpulseAndShake_OtherFeedbackUnaffected()
        {
            var shake = new Id("feedback.shake.kit");
            var setting = "settings.camera_intensity";
            var scale = 1.0;
            var rig = Make(SwordProfile(shake: shake), userSetting: setting, tweak: o => o.UserIntensity = key => key == setting ? scale : 1.0);

            rig.Pipeline.Offer(Hit(Player, Enemy1), null);
            var full = rig.Pipeline.Flush()!.Camera!;
            Assert.Equal(Gain, full.Magnitude, 12);
            Assert.Equal(shake, full.ShakeProfileId);

            scale = 0.5;
            rig.Pipeline.SetTick(100);
            rig.Pipeline.Offer(Hit(Player, Enemy1), null);
            Assert.Equal(Gain * 0.5, rig.Pipeline.Flush()!.Camera!.Magnitude, 12);

            scale = 0.0;
            rig.Pipeline.SetTick(200);
            rig.Pipeline.Offer(Hit(Player, Enemy1), null);
            var off = rig.Pipeline.Flush()!;
            Assert.Null(off.Camera);
            var plan = Assert.Single(off.Plans);
            Assert.True(plan.FlashEntity.HasValue);
            Assert.NotEmpty(plan.Sfx);
        }

        [Fact]
        public void DistanceAttenuation_CurveScalesImpulseByDistanceInBodyHeights()
        {
            var attenuation = new PiecewiseCurve(new[] { new CurvePoint(0, 1.0), new CurvePoint(10, 0.0) });
            var kit = new ImpactTestKit()
                .WithWeapon(Player, "feel.weapon.kit_sword",
                    Set(FeelFieldNames.CameraImpulseGain, Gain), Set(FeelFieldNames.ImpactProfileRef, ProfileId.Value))
                .WithCharacter(Player, "feel.character.kit_player",
                    Set(FeelFieldNames.CameraShakeCap, 0.2), Set(FeelFieldNames.CameraDistanceAttenuation, "curve.kit_falloff"));
            var resolver = kit.Build();
            var options = new ImpactOptions
            {
                FeelSource = new PresentingImpactFeelSource(resolver),
                ProfileResolver = _ => SwordProfile(),
                CameraOwnerResolver = () => Player,
                PositionResolver = id => id.Equals(Player) ? new Vec2(0, 0) : new Vec2(10, 0),
                CurveResolver = name => name == "curve.kit_falloff" ? attenuation : null,
                ReferenceHeight = 2.0,
                StepSeconds = StepSeconds,
            };
            var pipeline = new ImpactPipeline(options);

            pipeline.Offer(Hit(Player, Enemy1, contact: new Vec2(10, 0)), null);
            var cue = pipeline.Flush()!.Camera!;

            // 距离 10 世界单位 / 参考身高 2 = 5 个身高 → 曲线 (0→1, 10→0) 在 5 处为 0.5。
            Assert.Equal(Gain * attenuation.Evaluate(5.0), cue.Magnitude, 12);
        }

        /// <summary>
        /// 复现用例（手感落地 M3-B）：参考身高取实时来源——来源值从 2 变为 5 后，同一次命中的"距离（身高倍数）"从 10/2 = 5 变为 10/5 = 2，
        /// 衰减曲线上的取值从 0.5 变为 0.8，镜头冲击幅度随之变化；不设来源时只用 <c>ReferenceHeight</c>（与此前逐位一致）。
        /// </summary>
        [Fact]
        public void DistanceAttenuation_ReferenceHeightSource_IsReadLiveAndOverridesTheFixedValue()
        {
            var attenuation = new PiecewiseCurve(new[] { new CurvePoint(0, 1.0), new CurvePoint(10, 0.0) });
            var reference = 2.0;
            double Magnitude(Func<double>? source)
            {
                var kit = new ImpactTestKit()
                    .WithWeapon(Player, "feel.weapon.kit_sword",
                        Set(FeelFieldNames.CameraImpulseGain, Gain), Set(FeelFieldNames.ImpactProfileRef, ProfileId.Value))
                    .WithCharacter(Player, "feel.character.kit_player",
                        Set(FeelFieldNames.CameraShakeCap, 0.2), Set(FeelFieldNames.CameraDistanceAttenuation, "curve.kit_falloff"));
                var options = new ImpactOptions
                {
                    FeelSource = new PresentingImpactFeelSource(kit.Build()),
                    ProfileResolver = _ => SwordProfile(),
                    CameraOwnerResolver = () => Player,
                    PositionResolver = id => id.Equals(Player) ? new Vec2(0, 0) : new Vec2(10, 0),
                    CurveResolver = name => name == "curve.kit_falloff" ? attenuation : null,
                    ReferenceHeight = 99.0,
                    ReferenceHeightSource = source,
                    StepSeconds = StepSeconds,
                };
                var pipeline = new ImpactPipeline(options);
                pipeline.Offer(Hit(Player, Enemy1, contact: new Vec2(10, 0)), null);
                return pipeline.Flush()!.Camera!.Magnitude;
            }

            Assert.Equal(Gain * attenuation.Evaluate(10.0 / 2.0), Magnitude(() => reference), 12);
            reference = 5.0;
            Assert.Equal(Gain * attenuation.Evaluate(10.0 / 5.0), Magnitude(() => reference), 12);
            Assert.NotEqual(Magnitude(() => 2.0), Magnitude(() => 5.0));

            // 不变量：不设来源时只看固定值（99 个世界单位的身高下距离 10 ≈ 0.1 个身高）。
            Assert.Equal(Gain * attenuation.Evaluate(10.0 / 99.0), Magnitude(null), 12);
        }

        // ------------------------------------------------------------------ 挥空

        [Fact]
        public void ActiveWindowWithZeroHits_PlaysWhiffLayer_ButWithAHitDoesNot()
        {
            var rig = Make(SwordProfile());

            rig.Pipeline.OnActionMarker(Player, "active_start");
            rig.Pipeline.OnActionMarker(Player, "active_end");
            var whiff = Assert.Single(rig.Pipeline.Flush()!.Plans);
            Assert.Equal(ImpactOutcome.Whiff, whiff.Outcome);
            Assert.Equal(new Id("sfx.whiff_t2_generic"), Assert.Single(whiff.Sfx).SfxId);
            Assert.DoesNotContain(whiff.Sfx, s => s.Layer == SfxFeelLayer.Impact);

            // 有命中（哪怕被闪避）→ 不是挥空。
            rig.Pipeline.OnActionMarker(Player, "active_start");
            rig.Pipeline.ObserveHit(Hit(Player, Enemy1, result: HitResult.Dodge, amount: 0));
            rig.Pipeline.OnActionMarker(Player, "active_end");
            Assert.Null(rig.Pipeline.Flush());
        }

        // 挥空窗口按 (行动者, 动作实例) 配对（combat.hit_confirmed.castInstanceId，手感设计/03 第 2.4 节）。

        [Fact]
        public void WhiffWindows_ArePairedByCastInstance_AHitOfAnotherCastDoesNotCancelTheWhiff()
        {
            var rig = Make(SwordProfile());
            var castA = new Id("cast.kit_a");
            var castB = new Id("cast.kit_b");

            rig.Pipeline.OnActionMarker(Player, "active_start", castA);
            rig.Pipeline.OnActionMarker(Player, "active_start", castB);
            // 命中属于动作 B：动作 A 的窗口里没有任何命中。
            rig.Pipeline.ObserveHit(Hit(Player, Enemy1, castInstance: castB));
            rig.Pipeline.OnActionMarker(Player, "active_end", castB);
            Assert.Null(rig.Pipeline.Flush()); // B 命中过，不挥空。
            rig.Pipeline.OnActionMarker(Player, "active_end", castA);
            var whiff = Assert.Single(rig.Pipeline.Flush()!.Plans);
            Assert.Equal(ImpactOutcome.Whiff, whiff.Outcome);
        }

        [Fact]
        public void WhiffWindows_ALateHitFromThePreviousCast_DoesNotCountForTheNextCastsWindow()
        {
            var rig = Make(SwordProfile());
            var first = new Id("cast.kit_first");
            var second = new Id("cast.kit_second");
            rig.Pipeline.OnActionMarker(Player, "active_start", first);
            rig.Pipeline.OnActionMarker(Player, "active_end", first);
            Assert.Equal(ImpactOutcome.Whiff, Assert.Single(rig.Pipeline.Flush()!.Plans).Outcome); // 第一段窗口里没有命中：挥空。
            rig.Pipeline.OnActionMarker(Player, "active_start", second);
            rig.Pipeline.ObserveHit(Hit(Player, Enemy1, castInstance: first)); // 上一段的迟到命中。
            rig.Pipeline.OnActionMarker(Player, "active_end", second);
            var whiff = Assert.Single(rig.Pipeline.Flush()!.Plans);
            Assert.Equal(ImpactOutcome.Whiff, whiff.Outcome);
        }

        [Fact]
        public void WhiffWindows_AHitWithoutACastInstance_CountsForEveryOpenWindowOfThatActor()
        {
            var rig = Make(SwordProfile());
            var cast = new Id("cast.kit_legacy");
            rig.Pipeline.OnActionMarker(Player, "active_start", cast);
            rig.Pipeline.ObserveHit(Hit(Player, Enemy1)); // instant 路径的命中没有动作实例 id。
            rig.Pipeline.OnActionMarker(Player, "active_end", cast);
            Assert.Null(rig.Pipeline.Flush());

            // 另一个行动者的命中不串窗口。
            rig.Pipeline.OnActionMarker(Player, "active_start", cast);
            rig.Pipeline.ObserveHit(Hit(Enemy1, Player));
            rig.Pipeline.OnActionMarker(Player, "active_end", cast);
            Assert.Equal(ImpactOutcome.Whiff, Assert.Single(rig.Pipeline.Flush()!.Plans).Outcome);
        }

        [Fact]
        public void Whiff_CanBeDisabledByOption()
        {
            var rig = Make(SwordProfile(), tweak: o => o.WhiffFeedback = false);
            rig.Pipeline.OnActionMarker(Player, "active_start");
            rig.Pipeline.OnActionMarker(Player, "active_end");
            Assert.Null(rig.Pipeline.Flush());
        }

        // 不带攻击的动作（闪避、位移、纯增益）没有"打空"；投射物动作的挥空要等投射物的结局（手感设计/07 第 6 节、03 第 2.5 节）。

        [Fact]
        public void Whiff_ANonAttackAction_NeverOpensAWindow_EvenThroughItsActiveWindow()
        {
            var rig = Make(SwordProfile());
            var dodge = new Id("cast.kit_dodge");
            rig.Pipeline.OnActionStarted(Player, dodge, isAttack: false);
            rig.Pipeline.OnActionMarker(Player, "active_start", dodge);
            rig.Pipeline.OnActionMarker(Player, "active_end", dodge);
            rig.Pipeline.OnActionPhase(Player, ActionPhase.Active, dodge);
            rig.Pipeline.OnActionPhase(Player, ActionPhase.Recovery, dodge);
            Assert.Null(rig.Pipeline.Flush());

            // 同一行动者随后的带攻击动作照常挥空。
            var slash = new Id("cast.kit_slash");
            rig.Pipeline.OnActionStarted(Player, slash, isAttack: true);
            rig.Pipeline.OnActionMarker(Player, "active_start", slash);
            rig.Pipeline.OnActionMarker(Player, "active_end", slash);
            Assert.Equal(ImpactOutcome.Whiff, Assert.Single(rig.Pipeline.Flush()!.Plans).Outcome);
        }

        [Fact]
        public void Whiff_AnActionThatNeverAnnouncedItsStart_IsTreatedAsAnAttack_AsBefore()
        {
            var rig = Make(SwordProfile());
            var legacy = new Id("cast.kit_legacy_start");
            rig.Pipeline.OnActionMarker(Player, "active_start", legacy);
            rig.Pipeline.OnActionMarker(Player, "active_end", legacy);
            Assert.Equal(ImpactOutcome.Whiff, Assert.Single(rig.Pipeline.Flush()!.Plans).Outcome);
        }

        [Fact]
        public void Whiff_AProjectileInFlight_DefersTheDecisionToItsEnd_AMissWhiffsThen()
        {
            var rig = Make(SwordProfile());
            var bolt = new Id("cast.kit_bolt");
            rig.Pipeline.OnActionStarted(Player, bolt, isAttack: true);
            rig.Pipeline.OnActionMarker(Player, "active_start", bolt);
            rig.Pipeline.OnProjectileLaunched(Player, bolt);
            rig.Pipeline.OnActionMarker(Player, "active_end", bolt);
            Assert.Null(rig.Pipeline.Flush()); // 判定相结束了，但弹还在飞：先不挥空。

            rig.Pipeline.OnProjectileEnded(Player, bolt, cleared: false);
            Assert.Equal(ImpactOutcome.Whiff, Assert.Single(rig.Pipeline.Flush()!.Plans).Outcome);
        }

        [Fact]
        public void Whiff_AProjectileThatHitsBeforeItsEnd_MeansNoWhiffAtAll_EvenIfTheHitLandsAfterTheActiveWindow()
        {
            var rig = Make(SwordProfile());
            var bolt = new Id("cast.kit_bolt_hit");
            rig.Pipeline.OnActionMarker(Player, "active_start", bolt);
            rig.Pipeline.OnProjectileLaunched(Player, bolt);
            rig.Pipeline.OnActionMarker(Player, "active_end", bolt);
            rig.Pipeline.ObserveHit(Hit(Player, Enemy1, castInstance: bolt)); // 迟到命中：窗口还留着，计数。
            rig.Pipeline.OnProjectileEnded(Player, bolt, cleared: false);
            var batch = rig.Pipeline.Flush();
            Assert.True(batch == null || batch.Plans.All(plan => plan.Outcome != ImpactOutcome.Whiff));
        }

        [Fact]
        public void Whiff_WithSeveralProjectiles_WaitsForTheLastOne_AndAnyHitCancelsTheWhiff()
        {
            var rig = Make(SwordProfile());
            var volley = new Id("cast.kit_volley");
            rig.Pipeline.OnActionMarker(Player, "active_start", volley);
            rig.Pipeline.OnProjectileLaunched(Player, volley);
            rig.Pipeline.OnProjectileLaunched(Player, volley);
            rig.Pipeline.OnActionMarker(Player, "active_end", volley);

            rig.Pipeline.OnProjectileEnded(Player, volley, cleared: false);
            Assert.Null(rig.Pipeline.Flush()); // 还剩一发。
            rig.Pipeline.OnProjectileEnded(Player, volley, cleared: false);
            Assert.Equal(ImpactOutcome.Whiff, Assert.Single(rig.Pipeline.Flush()!.Plans).Outcome);

            var second = new Id("cast.kit_volley_2");
            rig.Pipeline.OnActionMarker(Player, "active_start", second);
            rig.Pipeline.OnProjectileLaunched(Player, second);
            rig.Pipeline.OnProjectileLaunched(Player, second);
            rig.Pipeline.OnActionMarker(Player, "active_end", second);
            rig.Pipeline.ObserveHit(Hit(Player, Enemy1, castInstance: second)); // 其中一发命中了。
            rig.Pipeline.OnProjectileEnded(Player, second, cleared: false);
            rig.Pipeline.OnProjectileEnded(Player, second, cleared: false);
            var batch = rig.Pipeline.Flush();
            Assert.True(batch == null || batch.Plans.All(plan => plan.Outcome != ImpactOutcome.Whiff));
        }

        [Fact]
        public void Whiff_AProjectileEndingBeforeTheActiveWindowCloses_LetsTheWindowCloseNormally()
        {
            var rig = Make(SwordProfile());
            var bolt = new Id("cast.kit_bolt_early");
            rig.Pipeline.OnActionMarker(Player, "active_start", bolt);
            rig.Pipeline.OnProjectileLaunched(Player, bolt);
            rig.Pipeline.OnProjectileEnded(Player, bolt, cleared: false); // 判定相还没结束，弹就没了（极近距离撞墙）。
            Assert.Null(rig.Pipeline.Flush());
            rig.Pipeline.OnActionMarker(Player, "active_end", bolt);
            Assert.Equal(ImpactOutcome.Whiff, Assert.Single(rig.Pipeline.Flush()!.Plans).Outcome);
        }

        [Fact]
        public void Whiff_AClearedProjectile_AbandonsTheWaitWithoutAWhiff()
        {
            var rig = Make(SwordProfile());
            var bolt = new Id("cast.kit_bolt_cleared");
            rig.Pipeline.OnActionMarker(Player, "active_start", bolt);
            rig.Pipeline.OnProjectileLaunched(Player, bolt);
            rig.Pipeline.OnActionMarker(Player, "active_end", bolt);
            rig.Pipeline.OnProjectileEnded(Player, bolt, cleared: true); // 清场/换图：不是"打空"。
            Assert.Null(rig.Pipeline.Flush());
            // 之后迟来的结局通知（重复）被忽略，不会补发挥空。
            rig.Pipeline.OnProjectileEnded(Player, bolt, cleared: false);
            Assert.Null(rig.Pipeline.Flush());
        }

        // ------------------------------------------------------------------ 顿帧表现

        [Fact]
        public void HitstopEvents_FreezeOnlyTheNamedUnits_ParticlesPerFreezeLayers_AndReleaseOnEnd()
        {
            var rig = Make(SwordProfile(freeze: new ImpactFreezeLayers(particles: true, trail: false)));
            var attackId = new Id("attack.kit_1");
            rig.Pipeline.Offer(Hit(Player, Enemy1, attackInstance: attackId), null);
            rig.Pipeline.OnHitstopStarted(new[] { Player, Enemy1 }, 4, attackId);

            var batch = rig.Pipeline.Flush()!;

            var op = Assert.Single(batch.Hitstops);
            Assert.True(op.IsStart);
            Assert.Equal(4, op.Ticks);
            Assert.True(op.Layers.Skeleton);
            Assert.True(op.Layers.Particles);
            Assert.False(op.Layers.Trail);
            Assert.True(rig.Pipeline.Freezes.IsFrozen(Player));
            Assert.True(rig.Pipeline.Freezes.IsFrozen(Enemy1));
            Assert.False(rig.Pipeline.Freezes.IsFrozen(new Id("unit.kit_bystander")));

            rig.Pipeline.OnHitstopEnded(new[] { Enemy1 });
            rig.Pipeline.Flush();
            Assert.True(rig.Pipeline.Freezes.IsFrozen(Player));
            Assert.False(rig.Pipeline.Freezes.IsFrozen(Enemy1));
        }

        [Fact]
        public void HitstopArrivingInLaterBatch_UsesFreezeLayersOfTheEarlierFlushedHit()
        {
            // 手感落地 M3-C 复现：生产顺序是命中先出批、顿帧（tick 末由判定型宿主排队发出）到达得更晚；此前只看同批计划，层声明恒为缺省。
            var rig = Make(SwordProfile(freeze: new ImpactFreezeLayers(particles: true, trail: true)));
            var attackId = new Id("attack.kit_lag");
            rig.Pipeline.Offer(Hit(Player, Enemy1, attackInstance: attackId), null);
            var first = rig.Pipeline.Flush()!;
            Assert.Empty(first.Hitstops); // 命中这一批里还没有顿帧

            rig.Pipeline.OnHitstopStarted(new[] { Player, Enemy1 }, 4, attackId);
            var op = Assert.Single(rig.Pipeline.Flush()!.Hitstops);

            Assert.True(op.Layers.Particles, "顿帧应取到先前已出批的命中所属反馈包的 freeze_layers.particles");
            Assert.True(op.Layers.Trail);
            Assert.True(rig.Pipeline.Freezes.TryGetLayers(Enemy1, out var registered));
            Assert.Equal(op.Layers, registered);
        }

        [Fact]
        public void HitstopFarAfterTheHit_DoesNotInheritStaleFreezeLayers()
        {
            // 不变量：只保留最近几次出批的命中——很久以前的反馈包不能套到无关的顿帧上。
            var rig = Make(SwordProfile(freeze: new ImpactFreezeLayers(particles: true, trail: false)));
            var attackId = new Id("attack.kit_stale");
            rig.Pipeline.Offer(Hit(Player, Enemy1, attackInstance: attackId), null);
            rig.Pipeline.Flush();

            var other = new Id("unit.kit_other");
            for (var i = 0; i < 4; i++)
            {
                rig.Pipeline.OnHitstopEnded(new[] { other }); // 不带命中的出批：只推进出批代数
                rig.Pipeline.Flush();
            }

            rig.Pipeline.OnHitstopStarted(new[] { Player, Enemy1 }, 4, attackId);
            var op = Assert.Single(rig.Pipeline.Flush()!.Hitstops);

            Assert.Equal(ImpactFreezeLayers.Default, op.Layers);
        }

        [Fact]
        public void HitstopWithoutMatchingImpact_DefaultsToSkeletonOnly()
        {
            var rig = Make(SwordProfile());
            rig.Pipeline.OnHitstopStarted(new[] { Enemy1 }, 3, new Id("attack.kit_orphan"));

            var op = Assert.Single(rig.Pipeline.Flush()!.Hitstops);

            Assert.Equal(ImpactFreezeLayers.Default, op.Layers);
            Assert.True(rig.Pipeline.Freezes.TryGetLayers(Enemy1, out var layers));
            Assert.False(layers.Particles);
        }

        // ------------------------------------------------------------------ 取不到反馈包 / 手感

        [Fact]
        public void NoProfileRef_OrUnknownProfile_PlaysNothingAndNeverThrows()
        {
            var unknown = Make(SwordProfile(), tweak: o => o.ProfileResolver = _ => null);
            Assert.False(unknown.Pipeline.Offer(Hit(Player, Enemy1), null));
            Assert.Contains(unknown.Diagnostics.Warnings, w => w.Contains("不存在"));

            // 受击方没有武器/档案引用，攻击方也没有 → 无反馈包。
            var kit = new ImpactTestKit();
            var pipeline = new ImpactPipeline(new ImpactOptions { FeelSource = new PresentingImpactFeelSource(kit.Build()) });
            Assert.False(pipeline.Offer(Hit(Player, Enemy1), null));
        }

        [Fact]
        public void ExplicitProfileId_WorksWithoutAnyFeelSource()
        {
            var options = new ImpactOptions { ProfileResolver = id => id.Equals(ProfileId) ? SwordProfile() : null };
            var pipeline = new ImpactPipeline(options);

            Assert.True(pipeline.Offer(Hit(Player, Enemy1), ProfileId));
            var plan = Assert.Single(pipeline.Flush()!.Plans);

            // 无手感：冲击增益取 0（没有镜头提示）、音效档取 0（静音）、特效倍率 1，但闪白照播。
            Assert.Null(pipeline.Flush());
            Assert.True(plan.FlashEntity.HasValue);
        }
    }
}

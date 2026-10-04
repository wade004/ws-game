using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Rules.Common;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 一次命中（或回避）的反馈输入（手感设计/07 第 1 节"由 <c>combat.hit_confirmed</c> 驱动，两种结算模式共用"）。
    /// 可从三种事件构造（<see cref="TryFromEvent"/>）：<c>combat.hit_confirmed</c>（完整信息，框架缺省规则）、
    /// <c>combat.damage_dealt</c> 与 <c>combat.attack_avoided</c>（<c>instant</c> 结算模式既有事件，信息较少，
    /// 缺项取中性值：<c>impactClass = medium</c>、<c>amountRatio = 0</c>、<c>isKill = false</c>、无接触点/方向）。
    /// </summary>
    public sealed class ImpactHit
    {
        public Id SourceId { get; }

        public Id TargetId { get; }

        public Id? SkillId { get; }

        public Id? AttackInstanceId { get; }

        public HitResult HitResult { get; }

        public ImpactOutcome Outcome { get; }

        /// <summary>冲击等级（判定型字段的运行结果，只能来自事件，表现层不得读判定型手感字段）。</summary>
        public string ImpactClass { get; }

        public double Amount { get; }

        public double AmountRatio { get; }

        public bool IsCrit { get; }

        public bool IsKill { get; }

        public Vec2? ContactPoint { get; }

        public Vec2 ContactNormal { get; }

        public Vec2 WorldDirection { get; }

        /// <summary>
        /// 发起本次命中的动作/施法实例 id（<c>combat.hit_confirmed.castInstanceId</c>，时间线空间命中填入；instant 命中与
        /// <c>combat.damage_dealt</c>/<c>combat.attack_avoided</c> 没有）。挥空窗口据此按动作实例配对。
        /// </summary>
        public Id? CastInstanceId { get; }

        public ImpactHit(
            Id sourceId, Id targetId, Id? skillId, Id? attackInstanceId, HitResult hitResult, string impactClass,
            double amount, double amountRatio, bool isCrit, bool isKill,
            Vec2? contactPoint, Vec2 contactNormal, Vec2 worldDirection)
            : this(
                sourceId, targetId, skillId, attackInstanceId, hitResult, impactClass, amount, amountRatio, isCrit, isKill,
                contactPoint, contactNormal, worldDirection, null)
        {
        }

        public ImpactHit(
            Id sourceId, Id targetId, Id? skillId, Id? attackInstanceId, HitResult hitResult, string impactClass,
            double amount, double amountRatio, bool isCrit, bool isKill,
            Vec2? contactPoint, Vec2 contactNormal, Vec2 worldDirection, Id? castInstanceId)
        {
            CastInstanceId = castInstanceId;
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            AttackInstanceId = attackInstanceId;
            HitResult = hitResult;
            if (impactClass == null) throw new ArgumentNullException(nameof(impactClass));
            ImpactClass = impactClass.Length == 0 ? ImpactProfile.FallbackClass : impactClass;
            Amount = amount;
            AmountRatio = amountRatio;
            IsCrit = isCrit;
            IsKill = isKill;
            ContactPoint = contactPoint;
            ContactNormal = contactNormal;
            WorldDirection = worldDirection;
            Outcome = ClassifyOutcome(hitResult, isCrit, isKill);
        }

        /// <summary>
        /// 回避类结局：Miss/Dodge/Parry（<c>combat.attack_avoided</c> 的三个终止分支）与 Immune/Invulnerable（没有伤害落地）。
        /// Block/GlancingBlow 有伤害落地，按命中类处理。判断记录：07 提到"格挡火花"属回避类反馈，但 Block 在结算里是
        /// 减伤后的命中（<c>combat.damage_dealt</c> 照发），归命中；需要格挡专属反馈的内容用 <c>hitResult</c> 条件的反馈绑定表达。
        /// </summary>
        public static bool IsAvoidedResult(HitResult result) =>
            result == HitResult.Miss || result == HitResult.Dodge || result == HitResult.Parry
            || result == HitResult.Immune || result == HitResult.Invulnerable;

        public static ImpactOutcome ClassifyOutcome(HitResult result, bool isCrit, bool isKill)
        {
            if (IsAvoidedResult(result)) return ImpactOutcome.Avoided;
            if (isKill) return ImpactOutcome.Kill;
            if (isCrit) return ImpactOutcome.Crit;
            return ImpactOutcome.Hit;
        }

        /// <summary>从支持的三种事件构造；其它事件返回 false。</summary>
        public static bool TryFromEvent(IEvent evt, out ImpactHit hit)
        {
            switch (evt)
            {
                case CombatHitConfirmedEvent confirmed:
                    hit = new ImpactHit(
                        confirmed.SourceId, confirmed.TargetId, confirmed.SkillId, confirmed.AttackInstanceId, confirmed.HitResult,
                        confirmed.ImpactClass, confirmed.Amount, confirmed.AmountRatio, confirmed.IsCrit, confirmed.IsKill,
                        confirmed.ContactPoint, confirmed.ContactNormal, confirmed.WorldDirection, confirmed.CastInstanceId);
                    return true;

                case CombatDamageDealtEvent dealt:
                    hit = new ImpactHit(
                        dealt.SourceId, dealt.TargetId, dealt.SkillId, dealt.AttackInstanceId, dealt.HitResult,
                        ImpactProfile.FallbackClass, dealt.Amount, 0.0, dealt.IsCrit, false,
                        null, Vec2.Zero, Vec2.Zero);
                    return true;

                case CombatAttackAvoidedEvent avoided:
                    hit = new ImpactHit(
                        avoided.SourceId, avoided.TargetId, avoided.SkillId, avoided.AttackInstanceId, avoided.HitResult,
                        ImpactProfile.FallbackClass, 0.0, 0.0, false, false,
                        null, Vec2.Zero, Vec2.Zero);
                    return true;

                default:
                    hit = null!;
                    return false;
            }
        }
    }

    /// <summary>
    /// 一个单位解析后的呈现型手感里，反馈包流水线用到的字段快照（特效组、音频组与镜头组的冲击相关字段）。
    /// 只由 <see cref="PresentingImpactFeelSource"/> 经 <see cref="PresentingFeelView"/> 读出，类型上读不到判定型字段。
    /// </summary>
    public sealed class ImpactFeel
    {
        private readonly int[] _tiers;

        /// <summary>反馈包引用（<c>impact_profile_ref</c>）；null 表示无反馈包。</summary>
        public Id? ProfileRef { get; }

        /// <summary>命中特效强度倍率（<c>impact_vfx_scale</c>）。</summary>
        public double VfxScale { get; }

        public string Material { get; }

        /// <summary>同时发声上限（<c>sfx_max_concurrent</c>）。</summary>
        public int MaxConcurrent { get; }

        /// <summary>镜头冲击基准幅度（画面高度比例）。</summary>
        public double CameraImpulseGain { get; }

        public double CameraImpulseMinIntervalMs { get; }

        public double CameraShakeCap { get; }

        public string CameraDistanceAttenuation { get; }

        public string? CameraUserIntensitySetting { get; }

        /// <summary>拖尾开关（<c>trail_enabled</c>）：为真时动画 <c>trail_start/trail_end</c> 标记驱动 <see cref="TrailRef"/> 拖尾特效（ADR-0148）。</summary>
        public bool TrailEnabled { get; }

        /// <summary>残影开关（<c>afterimage_enabled</c>）：为真时 <c>trail_start/trail_end</c> 标记同时开关该单位的残影。</summary>
        public bool AfterimageEnabled { get; }

        /// <summary>拖尾特效定义（<c>trail_ref</c>，<c>vfx.def</c> 行 id）；null 表示没有。</summary>
        public Id? TrailRef { get; }

        public ImpactFeel(
            Id? profileRef, double vfxScale, IReadOnlyList<int> layerTiers, string material, int maxConcurrent,
            double cameraImpulseGain, double cameraImpulseMinIntervalMs, double cameraShakeCap,
            string cameraDistanceAttenuation, string? cameraUserIntensitySetting)
            : this(
                profileRef, vfxScale, layerTiers, material, maxConcurrent, cameraImpulseGain, cameraImpulseMinIntervalMs, cameraShakeCap,
                cameraDistanceAttenuation, cameraUserIntensitySetting, false, false, null)
        {
        }

        /// <summary>带拖尾/残影字段的构造重载（ADR-0148；旧构造转调本重载，三项取关闭）。</summary>
        public ImpactFeel(
            Id? profileRef, double vfxScale, IReadOnlyList<int> layerTiers, string material, int maxConcurrent,
            double cameraImpulseGain, double cameraImpulseMinIntervalMs, double cameraShakeCap,
            string cameraDistanceAttenuation, string? cameraUserIntensitySetting,
            bool trailEnabled, bool afterimageEnabled, Id? trailRef)
        {
            TrailEnabled = trailEnabled;
            AfterimageEnabled = afterimageEnabled;
            TrailRef = trailRef;
            if (layerTiers == null) throw new ArgumentNullException(nameof(layerTiers));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (cameraDistanceAttenuation == null) throw new ArgumentNullException(nameof(cameraDistanceAttenuation));
            ProfileRef = profileRef;
            VfxScale = vfxScale;
            _tiers = new int[SfxFeelLayers.Names.Length];
            for (var i = 0; i < _tiers.Length && i < layerTiers.Count; i++)
            {
                _tiers[i] = layerTiers[i];
            }
            Material = string.IsNullOrEmpty(material) ? SfxLayerIndex.GenericMaterial : material;
            MaxConcurrent = maxConcurrent;
            CameraImpulseGain = cameraImpulseGain;
            CameraImpulseMinIntervalMs = cameraImpulseMinIntervalMs;
            CameraShakeCap = cameraShakeCap;
            CameraDistanceAttenuation = cameraDistanceAttenuation;
            CameraUserIntensitySetting = cameraUserIntensitySetting;
        }

        /// <summary>层对应的手感表强度档（语音层没有对应字段，恒为 0——只能由变体显式给档）。</summary>
        public int TierOf(SfxFeelLayer layer) => _tiers[(int)layer];

        /// <summary>无手感来源时的中性值：无反馈包、特效倍率 1、全部层档 0、增益 0。</summary>
        public static ImpactFeel Neutral { get; } =
            new ImpactFeel(null, 1.0, new int[SfxFeelLayers.Names.Length], SfxLayerIndex.GenericMaterial, int.MaxValue, 0, 0, 0, Presentation.Camera.DistanceAttenuation.None, null);
    }

    /// <summary>反馈包流水线读手感的窄口（实现见 <c>Presentation.FeedbackBinder.Core.PresentingImpactFeelSource</c>）。</summary>
    public interface IImpactFeelSource
    {
        /// <summary>取单位的反馈用手感；解析不到（单位不存在等）返回 null，调用方按中性处理。</summary>
        ImpactFeel? Get(Id unitId);
    }

    /// <summary>反馈包流水线的可选项（全部有缺省，缺省行为见各属性）。</summary>
    public sealed class ImpactOptions
    {
        /// <summary>手感来源；null 表示没有手感（<c>from_feel</c> 取不到反馈包，显式 <c>profile_id</c> 仍可用）。</summary>
        public IImpactFeelSource? FeelSource { get; set; }

        /// <summary>反馈包解析：id → <see cref="ImpactProfile"/>；null 或返回 null 记诊断并跳过。</summary>
        public Func<Id, ImpactProfile?>? ProfileResolver { get; set; }

        /// <summary>手感音效层索引；null 时音效层不发声（记一条诊断，不抛异常）。</summary>
        public SfxLayerIndex? SfxLayers { get; set; }

        /// <summary>同一 tick 内最多多少次命中播音效（手感设计/07 第 3 节 <c>max_impacts_per_tick</c>）；&lt;= 0 不限。缺省 4。</summary>
        public int MaxImpactsPerTick { get; set; } = 4;

        /// <summary>同一 tick 内最多多少次命中播特效（<c>max_vfx_per_tick</c>）；&lt;= 0 不限（缺省，07 第 4 节"缺省不限"）。</summary>
        public int MaxVfxPerTick { get; set; }

        /// <summary>镜头拥有者（跟随目标）；镜头冲击上限、最小间隔、距离衰减、玩家强度设置取其手感，缺省取攻击方手感。</summary>
        public Func<Id?>? CameraOwnerResolver { get; set; }

        /// <summary>实体世界位置（距离衰减用）；null 或返回 null 时衰减按 1。</summary>
        public Func<Id, Vec2?>? PositionResolver { get; set; }

        /// <summary>玩家强度设置：设置项引用 → 0..1 倍率（0 = 关闭镜头冲击与震屏）；null 时恒为 1。</summary>
        public Func<string, double>? UserIntensity { get; set; }

        /// <summary>曲线 id → 曲线（距离衰减曲线引用 <c>custom</c> 一类非 <c>linear</c> 值）；找不到按 1 并记诊断。</summary>
        public Func<string, PiecewiseCurve?>? CurveResolver { get; set; }

        /// <summary>固定步长（秒），毫秒到 tick 的换算与 tick 时钟用；缺省 1/60。</summary>
        public double StepSeconds { get; set; } = 1.0 / 60.0;

        /// <summary>参考身高（世界单位），距离衰减曲线横轴"身高倍数"换算用；缺省 1。</summary>
        public double ReferenceHeight { get; set; } = 1.0;

        /// <summary>
        /// 参考身高的实时来源（手感落地 M3-B）：非空时每次换算都读它，优先于 <see cref="ReferenceHeight"/>——装配根接手感标定，标定行热加载后换算随之更新，
        /// 不必重建流水线。缺省 null：只用 <see cref="ReferenceHeight"/>（与此前逐位一致）。
        /// </summary>
        public Func<double>? ReferenceHeightSource { get; set; }

        /// <summary>是否启用挥空反馈（判定相结束零命中播 whiff）；缺省 true。</summary>
        public bool WhiffFeedback { get; set; } = true;
    }

    /// <summary>流水线给出的一个音效播放决定（已映射到具体 sfx 行）。</summary>
    public sealed class ImpactSfxPlay
    {
        public SfxFeelLayer Layer { get; }

        public int Tier { get; }

        public string Material { get; }

        public Id SfxId { get; }

        /// <summary>是否经过回落（精确 (层, 档, 材质) 行不存在）。</summary>
        public bool FellBack { get; }

        public ImpactSfxPlay(SfxFeelLayer layer, int tier, string material, Id sfxId, bool fellBack)
        {
            Layer = layer;
            Tier = tier;
            Material = material ?? throw new ArgumentNullException(nameof(material));
            SfxId = sfxId;
            FellBack = fellBack;
        }
    }

    /// <summary>流水线对一次命中（或挥空）的逐目标表现计划（不含镜头：镜头按 tick 合并，见 <see cref="ImpactBatch"/>）。</summary>
    public sealed class ImpactPlan
    {
        /// <summary>反馈包 id；挥空的内置缺省计划（反馈包没有登记挥空变体）为 null。</summary>
        public Id? ProfileId { get; }

        public ImpactVariant Variant { get; }

        public ImpactOutcome Outcome { get; }

        /// <summary>来源命中；挥空计划为 null。</summary>
        public ImpactHit? Hit { get; }

        public Id SourceId { get; }

        public Id? TargetId { get; }

        public Id? FlashEntity { get; set; }

        public Id? FlashProfile { get; set; }

        /// <summary>闪白同步方式（变体 <c>flash.sync</c>）：<see cref="ImpactFlashSync.ImpactMarker"/> 时由落地方把闪白推迟到受击方剪辑的 <c>impact</c> 标记。</summary>
        public ImpactFlashSync FlashSync { get; set; }

        public Id? VfxId { get; set; }

        public FeedbackAttachSpec VfxAttach { get; set; }

        /// <summary>特效参数：<c>scale</c>（大小倍率）与可选 <c>orient_rad</c>（朝向弧度）。</summary>
        public IReadOnlyDictionary<string, double>? VfxParameters { get; set; }

        public List<ImpactSfxPlay> Sfx { get; } = new List<ImpactSfxPlay>();

        public Vec2? SfxPosition { get; set; }

        public Id? FloatingTextStyle { get; set; }

        public Id? FloatingTextEntity { get; set; }

        public double FloatingTextAmount { get; set; }

        public ImpactPlan(Id? profileId, ImpactVariant variant, ImpactOutcome outcome, ImpactHit? hit, Id sourceId, Id? targetId)
        {
            ProfileId = profileId;
            Variant = variant ?? throw new ArgumentNullException(nameof(variant));
            Outcome = outcome;
            Hit = hit;
            SourceId = sourceId;
            TargetId = targetId;
        }
    }

    /// <summary>
    /// 同一 tick 合并后的镜头提示（手感设计/07 第 4 节）：方向取幅度加权和，幅度取最大再按上限截断
    /// （<c>Magnitude = min(max, cap)</c>）；<see cref="ShakeProfileId"/> 是最强命中声明的震屏档（走既有震屏通道）。
    /// </summary>
    public sealed class ImpactCameraCue
    {
        /// <summary>单位方向；零向量表示无方向（各向同性）。</summary>
        public Vec2 Direction { get; }

        /// <summary>合并并截断后的幅度（画面高度比例）。</summary>
        public double Magnitude { get; }

        /// <summary>合并前各命中幅度的最大值（未截断，供观察截断是否生效）。</summary>
        public double UncappedMagnitude { get; }

        public double DecayMs { get; }

        /// <summary>衰减时长折算的 tick 数（<see cref="FeelCalibration.MillisecondsToTicks"/>）。</summary>
        public int DurationTicks { get; }

        public Id? ShakeProfileId { get; }

        /// <summary>参与合并的命中数。</summary>
        public int HitCount { get; }

        /// <summary>同批缩放脉冲幅度（比例，取各命中最大；0 = 无）。幅度经镜头冲击同一限频，衰减时长同 <see cref="DecayMs"/>。</summary>
        public double ZoomPunch { get; }

        public ImpactCameraCue(
            Vec2 direction, double magnitude, double uncappedMagnitude, double decayMs, int durationTicks, Id? shakeProfileId, int hitCount)
            : this(direction, magnitude, uncappedMagnitude, decayMs, durationTicks, shakeProfileId, hitCount, 0.0)
        {
        }

        public ImpactCameraCue(
            Vec2 direction, double magnitude, double uncappedMagnitude, double decayMs, int durationTicks, Id? shakeProfileId, int hitCount,
            double zoomPunch)
        {
            ZoomPunch = zoomPunch;
            Direction = direction;
            Magnitude = magnitude;
            UncappedMagnitude = uncappedMagnitude;
            DecayMs = decayMs;
            DurationTicks = durationTicks;
            ShakeProfileId = shakeProfileId;
            HitCount = hitCount;
        }
    }

    /// <summary>同一 tick 合并后的手柄震动提示（变体 <c>rumble</c>，ADR-0148）：强度取各命中最大并夹在 [0, 1]，时长取该最强命中的声明。</summary>
    public sealed class ImpactRumbleCue
    {
        public double Strength { get; }

        public double DurationMs { get; }

        public ImpactRumbleCue(double strength, double durationMs)
        {
            Strength = strength;
            DurationMs = durationMs;
        }
    }

    /// <summary>顿帧表现操作（<c>feel.hitstop_started/ended</c> 的呈现侧落地，手感设计/07 第 5 节）。</summary>
    public sealed class ImpactHitstopOp
    {
        public bool IsStart { get; }

        public IReadOnlyList<Id> UnitIds { get; }

        public int Ticks { get; }

        public ImpactFreezeLayers Layers { get; }

        public ImpactHitstopOp(bool isStart, IReadOnlyList<Id> unitIds, int ticks, ImpactFreezeLayers layers)
        {
            IsStart = isStart;
            UnitIds = unitIds ?? throw new ArgumentNullException(nameof(unitIds));
            Ticks = ticks;
            Layers = layers;
        }
    }

    /// <summary>一次 <c>Flush</c> 的产出：逐目标计划、合并后的镜头提示（可能为 null）、顿帧操作（按到达顺序）。</summary>
    public sealed class ImpactBatch
    {
        public IReadOnlyList<ImpactPlan> Plans { get; }

        public ImpactCameraCue? Camera { get; }

        /// <summary>镜头冲击因 <c>impulse_min_interval_ms</c> 被丢弃（<see cref="Camera"/> 为 null 且本批确有镜头内容时为真）。</summary>
        public bool CameraDroppedByInterval { get; }

        public IReadOnlyList<ImpactHitstopOp> Hitstops { get; }

        /// <summary>本批手柄震动（同批取最强一条）；null = 无。</summary>
        public ImpactRumbleCue? Rumble { get; }

        public ImpactBatch(
            IReadOnlyList<ImpactPlan> plans, ImpactCameraCue? camera, bool cameraDroppedByInterval, IReadOnlyList<ImpactHitstopOp> hitstops)
            : this(plans, camera, cameraDroppedByInterval, hitstops, null)
        {
        }

        public ImpactBatch(
            IReadOnlyList<ImpactPlan> plans, ImpactCameraCue? camera, bool cameraDroppedByInterval, IReadOnlyList<ImpactHitstopOp> hitstops,
            ImpactRumbleCue? rumble)
        {
            Rumble = rumble;
            Plans = plans ?? throw new ArgumentNullException(nameof(plans));
            Camera = camera;
            CameraDroppedByInterval = cameraDroppedByInterval;
            Hitstops = hitstops ?? throw new ArgumentNullException(nameof(hitstops));
        }
    }
}

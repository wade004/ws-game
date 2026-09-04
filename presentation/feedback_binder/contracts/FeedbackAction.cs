using System;
using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>六种反馈动作固定枚举（见 09_表现层.md 第 6.1 节 <c>FeedbackAction</c> 判别联合，
    /// 拍板决策：六项为固定集合，新增动作类型走审批流程，同 06 EffectKind 惯例）。</summary>
    public enum FeedbackActionKind
    {
        FloatingText,
        PlayVfx,
        PlaySfx,
        Freeze,
        ShakeCamera,
        Flash,
    }

    /// <summary>
    /// <c>feedback.binding.actions[]</c> 一项的判别联合基类（见 09 第 6.1 节）：每种
    /// <see cref="FeedbackActionKind"/> 对应一个 sealed 子类，构造函数强制该动作的必填参数——
    /// "action kind 合法""params 必填"两项校验（04 第 5 节校验器检查项清单同类项）由 C# 类型系统
    /// 在构造期天然保证，不需要额外的运行期"参数完整性"校验（与 <c>Core.Rules.Common.EffectRef</c>
    /// 用 <c>JsonObject Params</c> 弱类型透传的做法不同——本模块的六种动作形状是文档拍板的固定
    /// 有限集，强类型能表达全部必填/可选关系，选择强类型以便编译期发现漏填字段）。
    /// </summary>
    public abstract class FeedbackAction
    {
        public FeedbackActionKind Kind { get; }

        protected FeedbackAction(FeedbackActionKind kind)
        {
            Kind = kind;
        }
    }

    /// <summary>飘字（09 §6.1 <c>FloatingText(styleId, textSource)</c>）。</summary>
    public sealed class FloatingTextAction : FeedbackAction
    {
        public Id StyleId { get; }

        public TextSource TextSource { get; }

        public FloatingTextAction(Id styleId, TextSource textSource) : base(FeedbackActionKind.FloatingText)
        {
            StyleId = styleId;
            TextSource = textSource;
        }
    }

    /// <summary>播放特效（09 §6.1 <c>PlayVfx(vfxId, attach)</c>；本模块拍板扩展
    /// <c>vfx_id?|from_display</c> 二选一与可选 <c>anchor_id</c>，见 09 第 5.6 节
    /// <c>from_display: skill</c> 的使用场景"命中特效交给 DisplayInfo 动态决定"）。</summary>
    public sealed class PlayVfxAction : FeedbackAction
    {
        /// <summary>与 <see cref="FromDisplay"/> 二选一，至少一个非空（构造期校验）。</summary>
        public Id? VfxId { get; }

        public FromDisplaySource? FromDisplay { get; }

        public FeedbackAttachTarget Attach { get; }

        /// <summary>挂接到 <see cref="Attach"/> 实体的具体锚点（sprite 型）/挂点（model 型）；
        /// 省略时由 <see cref="Presentation.FeedbackBinder.Core.CompositeFeedbackSink"/> 退化为该
        /// 实体的世界位置播放（见 <see cref="Presentation.VfxSfx.Contracts.VfxAttachMode.World"/>）。</summary>
        public Id? AnchorId { get; }

        public PlayVfxAction(Id? vfxId, FromDisplaySource? fromDisplay, FeedbackAttachTarget attach, Id? anchorId)
            : base(FeedbackActionKind.PlayVfx)
        {
            if (vfxId == null && fromDisplay == null)
            {
                throw new ArgumentException("play_vfx 动作必须提供 vfx_id 或 from_display 之一");
            }
            if (vfxId != null && fromDisplay != null)
            {
                throw new ArgumentException("play_vfx 动作的 vfx_id 与 from_display 至多提供一个");
            }

            VfxId = vfxId;
            FromDisplay = fromDisplay;
            Attach = attach;
            AnchorId = anchorId;
        }
    }

    /// <summary>播放音效（09 §6.1 <c>PlaySfx(sfxId)</c>；本模块拍板同 <see cref="PlayVfxAction"/> 扩展
    /// <c>sfx_id?|from_display</c> 二选一）。</summary>
    public sealed class PlaySfxAction : FeedbackAction
    {
        public Id? SfxId { get; }

        public FromDisplaySource? FromDisplay { get; }

        public PlaySfxAction(Id? sfxId, FromDisplaySource? fromDisplay) : base(FeedbackActionKind.PlaySfx)
        {
            if (sfxId == null && fromDisplay == null)
            {
                throw new ArgumentException("play_sfx 动作必须提供 sfx_id 或 from_display 之一");
            }
            if (sfxId != null && fromDisplay != null)
            {
                throw new ArgumentException("play_sfx 动作的 sfx_id 与 from_display 至多提供一个");
            }

            SfxId = sfxId;
            FromDisplay = fromDisplay;
        }
    }

    /// <summary>顿帧（09 §6.1 <c>Freeze(durationMs)</c>）。</summary>
    public sealed class FreezeAction : FeedbackAction
    {
        public double DurationMs { get; }

        public FreezeAction(double durationMs) : base(FeedbackActionKind.Freeze)
        {
            if (durationMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationMs), "durationMs 不能为负");
            }
            DurationMs = durationMs;
        }
    }

    /// <summary>震屏（09 §6.1 <c>ShakeCamera(profileId)</c>）。</summary>
    public sealed class ShakeCameraAction : FeedbackAction
    {
        public Id ProfileId { get; }

        public ShakeCameraAction(Id profileId) : base(FeedbackActionKind.ShakeCamera)
        {
            ProfileId = profileId;
        }
    }

    /// <summary>闪白（09 §6.1 <c>Flash(profileId, target: source|target)</c>；无 world 选项，见
    /// <see cref="FeedbackAttachTarget"/> 类型注释）。</summary>
    public sealed class FlashAction : FeedbackAction
    {
        public Id ProfileId { get; }

        public FeedbackAttachTarget Target { get; }

        public FlashAction(Id profileId, FeedbackAttachTarget target) : base(FeedbackActionKind.Flash)
        {
            if (target == FeedbackAttachTarget.World)
            {
                throw new ArgumentException("flash 动作的 target 只能是 source 或 target，见 09 第 6.1 节", nameof(target));
            }

            ProfileId = profileId;
            Target = target;
        }
    }
}

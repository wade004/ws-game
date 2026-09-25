using System;
using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>反馈动作固定枚举（见 09_表现层.md 第 6.1 节 <c>FeedbackAction</c> 判别联合，
    /// 拍板决策：固定集合，新增动作类型走审批流程，同 06 EffectKind 惯例）。原六项之后，
    /// ADR-0075 经审批新增 <see cref="StopVfx"/> 第七项。</summary>
    public enum FeedbackActionKind
    {
        FloatingText,
        PlayVfx,
        PlaySfx,
        Freeze,
        ShakeCamera,
        Flash,

        /// <summary>ADR-0075 新增第七项：停止一次此前由 <see cref="PlayVfx"/> 播放的特效，见
        /// <see cref="StopVfxAction"/>。</summary>
        StopVfx,

        /// <summary>ADR-0089 新增第八项：停止一次此前由 <see cref="PlaySfx"/>（<c>attach</c> 非
        /// world 且对应 <c>sfx.def.loop=true</c>）播放的循环音效，见 <see cref="StopSfxAction"/>。</summary>
        StopSfx,
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

    /// <summary>停止特效（ADR-0075 新增：09 §6.1 <c>FeedbackAction</c> 判别联合第七项
    /// <c>StopVfx(vfxId, attach)</c>）。字段形状与 <see cref="PlayVfxAction"/> 对齐——同样支持
    /// <c>vfx_id?|from_display</c> 二选一与 <c>attach</c>——但不带 <c>anchor_id</c>：本动作不播放
    /// 任何东西，不需要解析挂接位置，只需要 (vfxId, 附着实体) 这一对键去定位由 <c>play_vfx</c> 播放
    /// 的在播实例并停止（见 <c>Presentation.FeedbackBinder.Core.CompositeFeedbackSink.StopVfx</c>
    /// 判断记录），因此 <see cref="Attach"/> 只能是 <see cref="FeedbackAttachTarget.Source"/>/
    /// <see cref="FeedbackAttachTarget.Target"/>，不支持 <see cref="FeedbackAttachTarget.World"/>
    /// ——world 附着没有具体实体可作为键，同 <see cref="FlashAction"/> 排除 world 的构造期校验
    /// 手法一致。</summary>
    public sealed class StopVfxAction : FeedbackAction
    {
        /// <summary>与 <see cref="FromDisplay"/> 二选一，至少一个非空（构造期校验）。</summary>
        public Id? VfxId { get; }

        public FromDisplaySource? FromDisplay { get; }

        public FeedbackAttachTarget Attach { get; }

        public StopVfxAction(Id? vfxId, FromDisplaySource? fromDisplay, FeedbackAttachTarget attach)
            : base(FeedbackActionKind.StopVfx)
        {
            if (vfxId == null && fromDisplay == null)
            {
                throw new ArgumentException("stop_vfx 动作必须提供 vfx_id 或 from_display 之一");
            }
            if (vfxId != null && fromDisplay != null)
            {
                throw new ArgumentException("stop_vfx 动作的 vfx_id 与 from_display 至多提供一个");
            }
            if (attach == FeedbackAttachTarget.World)
            {
                throw new ArgumentException(
                    "stop_vfx 动作的 attach 只能是 source 或 target——按 (vfx_id, 附着实体) 定位在播实例，world 没有实体可作为键，见 ADR-0075",
                    nameof(attach));
            }

            VfxId = vfxId;
            FromDisplay = fromDisplay;
            Attach = attach;
        }
    }

    /// <summary>播放音效（09 §6.1 <c>PlaySfx(sfxId)</c>；本模块拍板同 <see cref="PlayVfxAction"/> 扩展
    /// <c>sfx_id?|from_display</c> 二选一）。ADR-0089 新增可选 <see cref="Attach"/>：循环音效
    /// （<c>sfx.def.loop=true</c>）挂接到 <c>source</c>/<c>target</c> 实体后才能被同一对
    /// (sfx_id, 附着实体) 键的 <see cref="StopSfxAction"/> 定位停止，同 <see cref="PlayVfxAction.Attach"/>
    /// 惯例；默认 <see cref="FeedbackAttachTarget.World"/>——与本字段新增前的既有行为逐字一致（不
    /// 跟踪、不建键，见 <see cref="Presentation.FeedbackBinder.Core.CompositeFeedbackSink.PlaySfx(Id, Vec2?, FeedbackAttachSpec)"/>
    /// 判断记录）。</summary>
    public sealed class PlaySfxAction : FeedbackAction
    {
        public Id? SfxId { get; }

        public FromDisplaySource? FromDisplay { get; }

        public FeedbackAttachTarget Attach { get; }

        /// <summary>ABI 兼容 façade：不带 <see cref="Attach"/> 的旧构造签名，物理 IL 签名与新增该
        /// 字段之前完全一致，转调新增重载并固定传 <see cref="FeedbackAttachTarget.World"/>（新增
        /// 字段前的唯一行为）。</summary>
        public PlaySfxAction(Id? sfxId, FromDisplaySource? fromDisplay)
            : this(sfxId, fromDisplay, FeedbackAttachTarget.World)
        {
        }

        public PlaySfxAction(Id? sfxId, FromDisplaySource? fromDisplay, FeedbackAttachTarget attach)
            : base(FeedbackActionKind.PlaySfx)
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
            Attach = attach;
        }
    }

    /// <summary>停止音效（ADR-0089 新增：09 §6.1 <c>FeedbackAction</c> 判别联合第八项
    /// <c>StopSfx(sfxId, attach)</c>）。字段形状与 <see cref="StopVfxAction"/> 对齐——
    /// <c>sfx_id?|from_display</c> 二选一 + <c>attach</c>，且同样不接受
    /// <see cref="FeedbackAttachTarget.World"/>：本动作按 (sfxId, attach 解析出的附着实体) 定位由
    /// <see cref="PlaySfxAction"/>（循环音效）播放的在播实例并停止，world 没有实体可作为键。</summary>
    public sealed class StopSfxAction : FeedbackAction
    {
        public Id? SfxId { get; }

        public FromDisplaySource? FromDisplay { get; }

        public FeedbackAttachTarget Attach { get; }

        public StopSfxAction(Id? sfxId, FromDisplaySource? fromDisplay, FeedbackAttachTarget attach)
            : base(FeedbackActionKind.StopSfx)
        {
            if (sfxId == null && fromDisplay == null)
            {
                throw new ArgumentException("stop_sfx 动作必须提供 sfx_id 或 from_display 之一");
            }
            if (sfxId != null && fromDisplay != null)
            {
                throw new ArgumentException("stop_sfx 动作的 sfx_id 与 from_display 至多提供一个");
            }
            if (attach == FeedbackAttachTarget.World)
            {
                throw new ArgumentException(
                    "stop_sfx 动作的 attach 只能是 source 或 target——按 (sfx_id, 附着实体) 定位在播实例，world 没有实体可作为键，见 ADR-0089",
                    nameof(attach));
            }

            SfxId = sfxId;
            FromDisplay = fromDisplay;
            Attach = attach;
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

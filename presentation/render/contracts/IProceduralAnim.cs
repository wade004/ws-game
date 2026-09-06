using System;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>Move 原语参数：<paramref name="Offset"/> 是相对当前 <c>syncPose</c> 基准位置的偏移量，
    /// <paramref name="DurationSeconds"/> 是到达该偏移所需时长（见 09 第 4.1 节原语清单"move｜平移
    /// （跟随逻辑位置插值，非独立触发）"——本原语不是"每帧跟随逻辑位置"那条通路（那条已经由
    /// <see cref="Presentation.Common.IView.SyncPose"/> 承担），而是叠加在其上的一次性脚本化位移
    /// 效果（如冲刺/突进一类的表现性位移），见 <see cref="ProceduralAnimSequencer"/> 类型注释判断
    /// 记录）。</summary>
    public readonly struct MoveParams
    {
        public Vec2 Offset { get; }
        public double DurationSeconds { get; }

        public MoveParams(Vec2 offset, double durationSeconds)
        {
            Offset = offset;
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>Rotate 原语参数：从 0 缓动到 <paramref name="DeltaRadians"/> 并保持（用于倾倒/翻滚一类
    /// 需要"转到某个角度并停在那里"的效果；死亡/眩晕倒地更贴合语义的原语是 <see cref="ToppleParams"/>，
    /// 见 09 第 4.1 节原语清单）。</summary>
    public readonly struct RotateParams
    {
        public double DeltaRadians { get; }
        public double DurationSeconds { get; }

        public RotateParams(double deltaRadians, double durationSeconds)
        {
            DeltaRadians = deltaRadians;
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>Scale 原语参数："缩放（用于命中缩放反馈、召唤特效等）"（09 第 4.1 节）——按"1 →
    /// <paramref name="PunchScale"/> → 1"的对称冲击曲线（前半程时长内放大，后半程收回），
    /// <paramref name="DurationSeconds"/> 是往返总时长。</summary>
    public readonly struct ScaleParams
    {
        public double PunchScale { get; }
        public double DurationSeconds { get; }

        public ScaleParams(double punchScale, double durationSeconds)
        {
            PunchScale = punchScale;
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>Flash 原语参数："闪白（受击/无敌帧反馈）"（09 第 4.1 节）——强度从
    /// <paramref name="Intensity"/> 线性衰减到 0，供消费方经 <c>IRenderer2D.SetShaderParam</c> 一类
    /// 通道应用（具体着色器参数名由消费方决定，本类型不预设，见 09 第 4.1 节"参数留白由具体引擎适配
    /// 层解释"）。</summary>
    public readonly struct FlashParams
    {
        public double Intensity { get; }
        public double DurationSeconds { get; }

        public FlashParams(double intensity, double durationSeconds)
        {
            Intensity = intensity;
            DurationSeconds = durationSeconds;
        }

        /// <summary>无对应 <c>feedback.flash_profile</c> 登记表时的默认取值（09 未定义该表，见
        /// <c>presentation/feedback_binder/README.md</c> 契约缺口记录）。</summary>
        public static readonly FlashParams Default = new FlashParams(intensity: 1.0, durationSeconds: 0.15);
    }

    /// <summary>Trail 原语参数："拖尾（快速位移的视觉延迟）"（09 第 4.1 节）——强度从 1 线性衰减到 0。</summary>
    public readonly struct TrailParams
    {
        public double DurationSeconds { get; }

        public TrailParams(double durationSeconds)
        {
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>Stagger 原语参数："后仰（受击打断施法的短促位移反馈）"（09 第 4.1 节）——按"0 →
    /// <paramref name="Offset"/> → 0"的对称回弹曲线，语义同 <see cref="ScaleParams"/> 的冲击曲线。</summary>
    public readonly struct StaggerParams
    {
        public Vec2 Offset { get; }
        public double DurationSeconds { get; }

        public StaggerParams(Vec2 offset, double durationSeconds)
        {
            Offset = offset;
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>Topple 原语参数："倾倒（死亡/眩晕倒地）"（09 第 4.1 节）——从 0 缓动到
    /// <paramref name="TargetRotationRadians"/> 并保持（不回弹，倒地后停留）。</summary>
    public readonly struct ToppleParams
    {
        public double TargetRotationRadians { get; }
        public double DurationSeconds { get; }

        public ToppleParams(double targetRotationRadians, double durationSeconds)
        {
            TargetRotationRadians = targetRotationRadians;
            DurationSeconds = durationSeconds;
        }

        /// <summary>默认倒地角度：90 度（见类型注释"倒地"直觉）。</summary>
        public static ToppleParams Default(double durationSeconds) => new ToppleParams(Math.PI / 2.0, durationSeconds);
    }

    /// <summary>Fade 原语参数："淡出（消失、隐身切换）"（09 第 4.1 节）——透明度从 1 缓动到
    /// <paramref name="TargetAlpha"/> 并保持。</summary>
    public readonly struct FadeParams
    {
        public double TargetAlpha { get; }
        public double DurationSeconds { get; }

        public FadeParams(double targetAlpha, double durationSeconds)
        {
            TargetAlpha = targetAlpha;
            DurationSeconds = durationSeconds;
        }
    }

    /// <summary>
    /// 程序动画原语清单（见 09_表现层.md 第 4.1 节"程序动画原语清单（拍板，命名固定，两种外形类型
    /// 均适用，参数留白由具体引擎适配层解释）"）：move/rotate/scale/flash/trail/stagger/topple/fade
    /// 八项，均为固定集合，新增原语走 [12_扩展与变更流程.md](../../architecture/12_扩展与变更流程.md)
    /// 审批。<see cref="ICharacterRig.ProceduralAnim"/> 是本接口的消费入口。
    /// <para>
    /// 每个方法立即"触发"一次该原语的一次播放（09 第 4.1 节表述为"提供……动画能力，供反馈绑定与动画
    /// 状态机调用"，即每次调用是一次显式触发，不是持续状态）；<paramref name="onSample"/> 在
    /// <see cref="ProceduralAnimSequencer.Update"/> 推进时按各原语自己的曲线（见各 Params 类型注释）
    /// 反复回调当前数值，供消费方（通常是 <see cref="ICharacterRig"/> 实现）据此调用
    /// <c>IRenderer2D.SetTransform</c>/<c>SetShaderParam</c> 落地到具体引擎；<paramref name="onComplete"/>
    /// 在该次播放自然结束或被同类型新触发替换时调用一次（见 <see cref="ProceduralAnimSequencer"/>
    /// "叠加/互斥规则"判断记录）。
    /// </para>
    /// </summary>
    public interface IProceduralAnim
    {
        void Move(MoveParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null);

        void Rotate(RotateParams parameters, Action<double>? onSample = null, Action? onComplete = null);

        void Scale(ScaleParams parameters, Action<double>? onSample = null, Action? onComplete = null);

        void Flash(FlashParams parameters, Action<double>? onSample = null, Action? onComplete = null);

        void Trail(TrailParams parameters, Action<double>? onSample = null, Action? onComplete = null);

        void Stagger(StaggerParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null);

        void Topple(ToppleParams parameters, Action<double>? onSample = null, Action? onComplete = null);

        void Fade(FadeParams parameters, Action<double>? onSample = null, Action? onComplete = null);
    }
}

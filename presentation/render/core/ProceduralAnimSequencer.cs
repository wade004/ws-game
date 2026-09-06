using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="IProceduralAnim"/> 的引擎无关默认实现（见 09_表现层.md 第 4.1 节程序动画原语清单）：
    /// 纯粹的时间推进 + 曲线求值，从不调用任何 <c>IRenderer2D</c>/<c>IRenderer3D</c> 接口（P4，绘制
    /// 落地完全交给 <see cref="IProceduralAnim"/> 各方法的 <c>onSample</c>/<c>onComplete</c> 回调，
    /// 通常由 <see cref="ICharacterRig"/> 实现注入）。<see cref="Update"/> 由调用方（
    /// <see cref="ICharacterRig"/> 或更上层的表现帧驱动代码）每帧调用一次推进全部在播放的原语实例。
    /// <para>
    /// 判断记录（叠加/互斥规则，09 原文未给出，本类型拍板并记录）：八个原语两两之间互相独立叠加（例如
    /// 一次 <see cref="Flash"/> 与一次 <see cref="Scale"/> 可以同时播放，互不影响），但同一个原语类型
    /// 的新触发会立即替换掉尚在播放的旧实例——旧实例的 <c>onComplete</c> 立即以"被替换"的方式调用一次
    /// （不是静默丢弃，避免调用方逻辑因为等待一个永远不会来的完成回调而悬挂），随后新实例从 0 进度
    /// 开始播放。不做"跨类型互斥"（如 <see cref="Move"/> 与 <see cref="Stagger"/> 都影响位置时不自动
    /// 合并）——09 也未要求这一层合成，由消费方（<see cref="ICharacterRig"/> 实现）自行决定如何合成
    /// 多个偏移量（例如简单相加），本类型只负责"每个原语各自的时间线"。
    /// </para>
    /// <para>
    /// 判断记录（曲线，09 未拍板具体缓动曲线，"建议做成可调项"——本类型先给出最简单的线性默认值，
    /// 不引入额外的缓动库依赖）：<see cref="Move"/>/<see cref="Rotate"/>/<see cref="Topple"/>/
    /// <see cref="Fade"/> 四个"到达并保持"型原语用线性插值 0→目标值，超过 <c>DurationSeconds</c> 后
    /// 保持在目标值直到被替换/新原语覆盖同一实体（本类型不做"归零"，归零是消费方的职责，如
    /// <see cref="AnimStateMachine"/> 回到运动态后通常会重新走一次到 0 的另一次触发）；
    /// <see cref="Scale"/>/<see cref="Stagger"/> 两个"冲击并回弹"型原语用对称三角波（前半程线性升到峰
    /// 值、后半程线性回落到 0/1）；<see cref="Flash"/>/<see cref="Trail"/> 两个"强度衰减"型原语用线性
    /// 衰减到 0。
    /// </para>
    /// </summary>
    public sealed class ProceduralAnimSequencer : IProceduralAnim
    {
        private abstract class Instance
        {
            public double Elapsed;
            public double Duration;
            public abstract void Sample(double progress);
            public abstract void Complete();
        }

        // 八个原语各自独立的"当前播放实例"槽位（见类型注释"同类型互相替换"判断记录）。
        private Instance? _move;
        private Instance? _rotate;
        private Instance? _scale;
        private Instance? _flash;
        private Instance? _trail;
        private Instance? _stagger;
        private Instance? _topple;
        private Instance? _fade;

        public void Move(MoveParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null) =>
            Start(ref _move, parameters.DurationSeconds, onComplete, progress =>
                onSample?.Invoke(parameters.Offset * ClampLinear(progress)));

        public void Rotate(RotateParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            Start(ref _rotate, parameters.DurationSeconds, onComplete, progress =>
                onSample?.Invoke(parameters.DeltaRadians * ClampLinear(progress)));

        public void Scale(ScaleParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            Start(ref _scale, parameters.DurationSeconds, onComplete, progress =>
            {
                var t = TriangleWave(progress);
                onSample?.Invoke(1.0 + (parameters.PunchScale - 1.0) * t);
            });

        public void Flash(FlashParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            Start(ref _flash, parameters.DurationSeconds, onComplete, progress =>
                onSample?.Invoke(parameters.Intensity * (1.0 - ClampLinear(progress))));

        public void Trail(TrailParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            Start(ref _trail, parameters.DurationSeconds, onComplete, progress =>
                onSample?.Invoke(1.0 - ClampLinear(progress)));

        public void Stagger(StaggerParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null) =>
            Start(ref _stagger, parameters.DurationSeconds, onComplete, progress =>
            {
                var t = TriangleWave(progress);
                onSample?.Invoke(parameters.Offset * t);
            });

        public void Topple(ToppleParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            Start(ref _topple, parameters.DurationSeconds, onComplete, progress =>
                onSample?.Invoke(parameters.TargetRotationRadians * ClampLinear(progress)));

        public void Fade(FadeParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            Start(ref _fade, parameters.DurationSeconds, onComplete, progress =>
                onSample?.Invoke(1.0 + (parameters.TargetAlpha - 1.0) * ClampLinear(progress)));

        /// <summary>推进全部在播放的原语实例一帧；到达各自 <c>DurationSeconds</c> 的实例在本次调用
        /// 内触发一次 <c>onComplete</c> 后清空槽位（"到达并保持"型原语在清空前已经把 <c>onSample</c>
        /// 定格在目标值那一次采样，见类型注释）。</summary>
        public void Update(double dt)
        {
            Advance(ref _move, dt);
            Advance(ref _rotate, dt);
            Advance(ref _scale, dt);
            Advance(ref _flash, dt);
            Advance(ref _trail, dt);
            Advance(ref _stagger, dt);
            Advance(ref _topple, dt);
            Advance(ref _fade, dt);
        }

        /// <summary>清空全部在播放的原语实例，不触发任何回调（用于 View 销毁/回收时的静默清理，避免
        /// 悬挂的回调持有已销毁对象的引用）。</summary>
        public void Reset()
        {
            _move = _rotate = _scale = _flash = _trail = _stagger = _topple = _fade = null;
        }

        private static void Start(ref Instance? slot, double durationSeconds, Action? onComplete, Action<double> sample)
        {
            if (durationSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "原语时长必须为正数");
            }

            // 同类型替换：旧实例（若有）立即以"被替换"方式触发一次 onComplete，见类型注释判断记录。
            slot?.Complete();

            slot = new SimpleInstance(durationSeconds, sample, onComplete);
            slot.Sample(0.0);
        }

        private static void Advance(ref Instance? slot, double dt)
        {
            if (slot == null)
            {
                return;
            }

            slot.Elapsed += dt;
            var progress = slot.Elapsed / slot.Duration;

            if (progress >= 1.0)
            {
                slot.Sample(1.0);
                var finished = slot;
                slot = null;
                finished.Complete();
                return;
            }

            slot.Sample(progress);
        }

        private static double ClampLinear(double progress) => progress < 0 ? 0 : progress > 1 ? 1 : progress;

        /// <summary>对称三角波：0→1 区间内先线性升到峰值（前半程末端 = 1.0），再线性降回 0（见类型
        /// 注释"冲击并回弹"型原语判断记录）。</summary>
        private static double TriangleWave(double progress)
        {
            var p = ClampLinear(progress);
            return p <= 0.5 ? p * 2.0 : (1.0 - p) * 2.0;
        }

        private sealed class SimpleInstance : Instance
        {
            private readonly Action<double> _sample;
            private readonly Action? _onComplete;

            public SimpleInstance(double duration, Action<double> sample, Action? onComplete)
            {
                Duration = duration;
                _sample = sample;
                _onComplete = onComplete;
            }

            public override void Sample(double progress) => _sample(progress);
            public override void Complete() => _onComplete?.Invoke();
        }
    }
}

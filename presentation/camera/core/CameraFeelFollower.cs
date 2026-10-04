using System;
using Core.Foundation.Common;

namespace Presentation.Camera
{
    /// <summary>
    /// 镜头手感跟随计算（手感设计/07 第 2 节）：按档案把"跟随目标当前位置"换算成"镜头关注点"，再交给既有
    /// <see cref="Core.Foundation.EngineAdapter.ICamera.Follow"/>。纯数学、无引擎依赖、逐帧确定（同输入同输出）。
    /// <para>
    /// 每帧步骤（<paramref name="dt"/> 为本帧秒数，<c>a(τ) = 1 - exp(-dt/τ)</c> 是一阶滞后系数，<c>τ = 0</c> 时 <c>a = 1</c>）：
    /// </para>
    /// <list type="number">
    /// <item><description>速度 = (目标位置 - 上一帧目标位置) / dt；速度超过阈值时前瞻期望偏移 = 速度单位方向 × <c>LookAhead</c>，否则为零。</description></item>
    /// <item><description>前瞻偏移按 <c>LookAheadLagMs</c> 一阶滞后追期望偏移（方向反转时偏移穿过零平滑过渡，不甩动）。</description></item>
    /// <item><description>期望关注点 = 目标 + 前瞻偏移。</description></item>
    /// <item><description>死区（世界距离的宽、高，以当前关注点为中心的矩形）：期望点落在矩形内，关注点这一轴不动；落在外则目标轴上的期望值取"期望点沿该轴被拽回到矩形边上"的位置。</description></item>
    /// <item><description>分轴阻尼：<c>focus.x += (期望x - focus.x) × a(EffectiveDampingXMs)</c>，y 轴同理。阻尼是跟随滞后的唯一权威：分轴阻尼大于 0 取它，否则取（已弃用的）<c>FollowLagMs</c>，两者都为 0 即该轴不阻尼。</description></item>
    /// </list>
    /// 首帧（或 <see cref="Reset"/> 之后）直接把关注点放到目标上，不产生从原点滑入的瞬态。<c>ICamera.Follow</c> 的平滑参数不再随 <c>FollowLagMs</c> 变化
    /// （见 <see cref="SmoothingFor"/>），避免两级滞后串联。
    /// </summary>
    public sealed class CameraFeelFollower
    {
        /// <summary>低于此速度（世界距离/秒）视为静止，前瞻期望偏移为零（避免数值噪声决定方向）。</summary>
        public const double MinLookAheadSpeed = 1e-6;

        private bool _initialized;
        private Vec2 _focus;
        private Vec2 _previousTarget;
        private Vec2 _aheadOffset;

        /// <summary>当前关注点（未初始化时为零向量）。</summary>
        public Vec2 Focus => _focus;

        /// <summary>当前前瞻偏移。</summary>
        public Vec2 AheadOffset => _aheadOffset;

        public bool IsInitialized => _initialized;

        /// <summary>丢弃全部状态（换跟随目标、切镜头档案、场景切换时调用），下一帧重新对齐到目标。</summary>
        public void Reset()
        {
            _initialized = false;
            _focus = Vec2.Zero;
            _previousTarget = Vec2.Zero;
            _aheadOffset = Vec2.Zero;
        }

        /// <summary>
        /// <c>ICamera.Follow</c> 的平滑参数：恒取调用方给的缺省（<see cref="CameraProfile.FollowLerp"/>）。ADR-0148 起 <c>follow_lag_ms</c>
        /// 不再换算成这一级平滑——阻尼是跟随滞后的唯一权威（见 <see cref="CameraFeelProfile.EffectiveDampingXMs"/>），此前两级串联、总滞后约为两者之和。
        /// 方法保留以兼容既有调用。
        /// </summary>
        public static double SmoothingFor(CameraFeelProfile feel, double fallback) => fallback;

        /// <summary>推进一帧，返回本帧应交给 <c>ICamera.Follow</c> 的关注点。<paramref name="dt"/> 非正时不推进状态，返回当前关注点。</summary>
        public Vec2 Step(Vec2 target, double dt, CameraFeelProfile feel)
        {
            if (feel == null) throw new ArgumentNullException(nameof(feel));

            if (!_initialized)
            {
                _initialized = true;
                _focus = target;
                _previousTarget = target;
                _aheadOffset = Vec2.Zero;
                return _focus;
            }

            if (!(dt > 0))
            {
                return _focus;
            }

            var velocity = new Vec2((target.X - _previousTarget.X) / dt, (target.Y - _previousTarget.Y) / dt);
            _previousTarget = target;

            var desiredAhead = Vec2.Zero;
            if (feel.LookAhead > 0)
            {
                var speed = Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
                if (speed > MinLookAheadSpeed)
                {
                    desiredAhead = new Vec2(velocity.X / speed * feel.LookAhead, velocity.Y / speed * feel.LookAhead);
                }
            }

            var aheadA = Alpha(dt, feel.LookAheadLagMs);
            _aheadOffset = new Vec2(
                _aheadOffset.X + (desiredAhead.X - _aheadOffset.X) * aheadA,
                _aheadOffset.Y + (desiredAhead.Y - _aheadOffset.Y) * aheadA);

            var desiredX = target.X + _aheadOffset.X;
            var desiredY = target.Y + _aheadOffset.Y;

            var goalX = DeadZoneGoal(_focus.X, desiredX, feel.DeadZoneWidth * 0.5);
            var goalY = DeadZoneGoal(_focus.Y, desiredY, feel.DeadZoneHeight * 0.5);

            _focus = new Vec2(
                _focus.X + (goalX - _focus.X) * Alpha(dt, feel.EffectiveDampingXMs),
                _focus.Y + (goalY - _focus.Y) * Alpha(dt, feel.EffectiveDampingYMs));
            return _focus;
        }

        private static double DeadZoneGoal(double focus, double desired, double halfSize)
        {
            var d = desired - focus;
            if (Math.Abs(d) <= halfSize)
            {
                return focus;
            }

            return d > 0 ? desired - halfSize : desired + halfSize;
        }

        /// <summary>一阶滞后系数 <c>1 - exp(-dt/τ)</c>（<paramref name="tauMs"/> 毫秒；0 即即时，系数为 1）。</summary>
        public static double Alpha(double dt, double tauMs)
        {
            if (!(tauMs > 0))
            {
                return 1.0;
            }

            return 1.0 - Math.Exp(-dt / (tauMs / 1000.0));
        }
    }
}

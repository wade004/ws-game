using System;
using Core.Foundation.Common;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 一次施法的"瞄点"（手感设计/01 第 2.4 节，手感落地 M4-G）：施法请求自己携带的目标——单位目标（<see cref="TargetId"/>）或地面落点（<see cref="Point"/>）——
    /// 以及这次施法的射程（<see cref="Range"/>，0 = 没有射程限制/未知）。宽限条件的求值以它为准：条件里的 <c>target</c> 分组与框架内置的
    /// <c>event.aim_*</c> 上下文变量绑定到它，没有瞄点时才回落到求值器自己的缺省目标（自动攻击的当前目标）。
    /// <para>
    /// 判断记录（身份）：<see cref="SameAim"/> 比较目标、落点与射程三项——历史（"最近一次为真的 tick"）属于"这个瞄点"：换了目标、换了落点、换了技能（射程不同）
    /// 都是另一个瞄点，依赖瞄点的条件历史随之作废（<see cref="GraceTracker.NoteAim"/>）。
    /// </para>
    /// </summary>
    public readonly struct GraceAim : IEquatable<GraceAim>
    {
        /// <summary>没有瞄点（缺省值）：求值器按自己的缺省目标求值，与引入瞄点之前逐位一致。</summary>
        public static GraceAim None => default;

        /// <summary>单位目标；落点瞄点与"没有瞄点"为 null。</summary>
        public Id? TargetId { get; }

        /// <summary>地面落点；单位目标瞄点与"没有瞄点"为 null。</summary>
        public Vec2? Point { get; }

        /// <summary>这次施法的射程；0 表示没有射程限制或射程未知（此时"在射程内"恒为真）。</summary>
        public double Range { get; }

        private GraceAim(Id? targetId, Vec2? point, double range)
        {
            TargetId = targetId;
            Point = point;
            Range = range > 0 ? range : 0;
        }

        /// <summary>以单位目标为瞄点。</summary>
        public static GraceAim OfTarget(Id targetId, double range = 0) => new GraceAim(targetId, null, range);

        /// <summary>以地面落点为瞄点。</summary>
        public static GraceAim OfPoint(Vec2 point, double range = 0) => new GraceAim(null, point, range);

        /// <summary>是否没有瞄点（既无目标也无落点）。</summary>
        public bool IsNone => !TargetId.HasValue && !Point.HasValue;

        /// <summary>两个瞄点是否是同一个（目标、落点、射程三项都相同）。</summary>
        public bool SameAim(GraceAim other) =>
            Nullable.Equals(TargetId, other.TargetId) && Nullable.Equals(Point, other.Point) && Range == other.Range;

        public bool Equals(GraceAim other) => SameAim(other);

        public override bool Equals(object? obj) => obj is GraceAim other && SameAim(other);

        public override int GetHashCode() => HashCode.Combine(TargetId, Point, Range);
    }

    /// <summary>
    /// 宽限追踪接收施法瞄点的可选接口（手感落地 M4-G）：施法管线在带宽限条件的请求里把请求自己携带的目标/落点交给它，游戏也可以每 tick 把准星落点
    /// 交进来（框架没有"准星"概念，屏幕到世界的换算属于引擎侧）。<see cref="GraceTracker"/> 实现它；探测写法 <c>grace as IGraceAimSink</c>，
    /// 探测不到视为该宽限查询对象不接收瞄点，行为与引入瞄点之前一致。
    /// </summary>
    public interface IGraceAimSink
    {
        /// <summary>
        /// 记下行动者当前的施法瞄点并立即按它重新求值依赖瞄点的条件。瞄点保持一个宽限窗口长度（<c>grace_ms</c> 换算的 tick 数，至少 1），之后没有新的记录就作废；
        /// 传 <see cref="GraceAim.None"/> 等同 <see cref="ClearAim"/>。
        /// </summary>
        void NoteAim(Id actorId, GraceAim aim);

        /// <summary>撤销行动者当前的施法瞄点（依赖瞄点的条件历史一并作废）。</summary>
        void ClearAim(Id actorId);
    }
}

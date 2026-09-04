using System;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 一次施法请求的结果（见 06 第 7 节 <c>SkillHost.castSkill</c> 返回值）。不可变值类型：
    /// 只能经 <see cref="Ok"/>/<see cref="Fail"/> 两个工厂方法构造，禁止外部直接拼出"成功但带失败原因"
    /// 一类不自洽的组合。
    /// </summary>
    public readonly struct CastResult : IEquatable<CastResult>
    {
        public bool Success { get; }

        public CastFailureReason Reason { get; }

        /// <summary>成功时施放实例 id（供读条/引导期间的打断、法术队列等后续操作引用）；失败时为 null。</summary>
        public Id? CastInstanceId { get; }

        private CastResult(bool success, CastFailureReason reason, Id? castInstanceId)
        {
            Success = success;
            Reason = reason;
            CastInstanceId = castInstanceId;
        }

        /// <summary>构造一个成功结果，<see cref="Reason"/> 恒为 <see cref="CastFailureReason.None"/>。</summary>
        public static CastResult Ok(Id castInstanceId) => new CastResult(true, CastFailureReason.None, castInstanceId);

        /// <summary>构造一个失败结果；<paramref name="reason"/> 不允许是 <see cref="CastFailureReason.None"/>
        /// （"失败但无原因"不自洽），传入时抛 <see cref="ArgumentException"/>。</summary>
        public static CastResult Fail(CastFailureReason reason)
        {
            if (reason == CastFailureReason.None)
            {
                throw new ArgumentException("失败结果必须携带非 None 的失败原因", nameof(reason));
            }

            return new CastResult(false, reason, null);
        }

        public bool Equals(CastResult other) =>
            Success == other.Success && Reason == other.Reason && Nullable.Equals(CastInstanceId, other.CastInstanceId);

        public override bool Equals(object? obj) => obj is CastResult other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Success.GetHashCode();
                hash = (hash * 397) ^ (int)Reason;
                hash = (hash * 397) ^ (CastInstanceId?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public static bool operator ==(CastResult left, CastResult right) => left.Equals(right);

        public static bool operator !=(CastResult left, CastResult right) => !left.Equals(right);
    }
}

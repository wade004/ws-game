using System;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 一个已施加的光环实例的引用句柄（见 06 第 7 节 <c>EffectSink.applyAura</c> 返回值
    /// <c>AuraInstanceRef</c>）。不透明句柄：只携带实例 id，供后续 <see cref="IEffectSink.RemoveAura"/>
    /// 引用同一实例，不暴露光环内部状态（层数、剩余时长等属于 <see cref="IAuraQuery"/> 的职责）。
    /// </summary>
    public readonly struct AuraInstanceRef : IEquatable<AuraInstanceRef>
    {
        public Id AuraInstanceId { get; }

        public AuraInstanceRef(Id auraInstanceId)
        {
            AuraInstanceId = auraInstanceId;
        }

        public bool Equals(AuraInstanceRef other) => AuraInstanceId.Equals(other.AuraInstanceId);

        public override bool Equals(object? obj) => obj is AuraInstanceRef other && Equals(other);

        public override int GetHashCode() => AuraInstanceId.GetHashCode();

        public override string ToString() => AuraInstanceId.ToString();

        public static bool operator ==(AuraInstanceRef left, AuraInstanceRef right) => left.Equals(right);

        public static bool operator !=(AuraInstanceRef left, AuraInstanceRef right) => !left.Equals(right);
    }
}

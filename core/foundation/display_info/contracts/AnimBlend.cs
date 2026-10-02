using System;
using System.Globalization;
using Core.Foundation.Common;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 手感落地 M4-D（手感设计/04 第 10 节）：<c>display.anim_set.blends</c> 里"从某键切到某键"的切入混合时长声明
    /// （<c>{from, to, blend_ms}</c>）。<see cref="FromKey"/>/<see cref="ToKey"/> 是同一姿势集 <c>clips</c> 里的剪辑键
    /// （不是资源引用——键到资源的换算在 <see cref="AnimSetDef.TryGetBlendSeconds"/> 里按合并后的 <c>clips</c> 做，所以体量组行
    /// 继承主集的 <c>blends</c> 时自然换算成各自组内的资源引用）。
    /// </summary>
    public readonly struct AnimBlendPair : IEquatable<AnimBlendPair>
    {
        public string FromKey { get; }

        public string ToKey { get; }

        /// <summary>混合时长，毫秒（&gt;= 0；0 = 硬切）。</summary>
        public double BlendMs { get; }

        public AnimBlendPair(string fromKey, string toKey, double blendMs)
        {
            FromKey = fromKey ?? throw new ArgumentNullException(nameof(fromKey));
            ToKey = toKey ?? throw new ArgumentNullException(nameof(toKey));
            BlendMs = blendMs;
        }

        public bool Equals(AnimBlendPair other) =>
            string.Equals(FromKey, other.FromKey, StringComparison.Ordinal)
            && string.Equals(ToKey, other.ToKey, StringComparison.Ordinal)
            && BlendMs.Equals(other.BlendMs);

        public override bool Equals(object? obj) => obj is AnimBlendPair other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = StringComparer.Ordinal.GetHashCode(FromKey ?? string.Empty);
                h = (h * 397) ^ StringComparer.Ordinal.GetHashCode(ToKey ?? string.Empty);
                return (h * 397) ^ BlendMs.GetHashCode();
            }
        }

        public override string ToString() => FromKey + "->" + ToKey + "@" + BlendMs.ToString("0.###", CultureInfo.InvariantCulture) + "ms";
    }

    /// <summary>
    /// 手感落地 M4-D：切换动画剪辑时的交叉淡入时长来源（model 型骨骼动画）。<see cref="AnimSetDef"/> 实现它：优先级
    /// 每对键 &gt; 目标剪辑逐键 <see cref="AnimClipDef.BlendMs"/> &gt; 没有声明（返回 false，调用方用自己的默认）。
    /// 引擎适配层把解析好的姿势集挂给 <c>ModelCharacterRig.AnimBlendSource</c>，缺省不挂 = 此前的固定默认混合时长。
    /// </summary>
    public interface IAnimBlendSource
    {
        /// <summary>从剪辑 <paramref name="fromClip"/>（null = 此前没有播放过剪辑）切到 <paramref name="toClip"/> 的交叉淡入时长（秒）；
        /// 返回 false 表示数据没有声明（<paramref name="seconds"/> 无意义），调用方用默认值。</summary>
        bool TryGetBlendSeconds(Id? fromClip, Id toClip, out double seconds);
    }
}

using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 引擎无关的序列帧剪辑元数据（见 09_表现层.md 第 4.5 节"序列帧动画预留接口（sprite 型专用）"，
    /// 09 勘误："sprite 型序列帧剪辑字段"——04 并未给序列帧资源定义独立的数据表字段（04 第 7.1.1 节
    /// <c>display.anim_set</c> 只服务 <c>model</c> 型骨骼动画剪辑，见该节字段表"仅 model 型使用"），
    /// 本类型是纯运行期值对象，由具体游戏的表现层装配代码构造后注入 <see cref="FrameAnimPlayer"/>
    /// 的剪辑目录，不依赖任何 <c>DataRegistry</c> 表结构——04/09 均未拍板对应数据表 schema 之前，
    /// 这是本模块能给出的最小可用形状（同 <see cref="IFrameAnimPlayer"/> 类型注释"预留接口，不在本
    /// 架构展开实现"的一贯克制）。
    /// </summary>
    public sealed class FrameAnimClip
    {
        public Id ClipId { get; }

        /// <summary>总帧数，必须为正。</summary>
        public int FrameCount { get; }

        /// <summary>播放帧率（帧/秒），必须为正。</summary>
        public double FrameRate { get; }

        /// <summary>关键帧标记：标记名 → 帧下标（<c>[0, FrameCount)</c>），见 09 第 4.3 节
        /// <c>anim_keyframe_driven</c> 策略"动画剪辑内标记一个'命中关键帧'……sprite 型经序列帧剪辑的
        /// 等效关键帧标记触发"。<see cref="HitFrameMarker"/> 是本模块固定的命中帧标记名，具体游戏可以
        /// 额外登记其它标记名（如脚步声、换手等），<see cref="IFrameAnimPlayer.OnAnimEvent"/> 对全部
        /// 标记一视同仁地转发，不只识别命中帧。</summary>
        public IReadOnlyDictionary<string, int> Keyframes { get; }

        /// <summary>命中帧标记名（见 <see cref="Keyframes"/> 注释、09 第 4.3 节）。</summary>
        public const string HitFrameMarker = "hit_frame";

        public FrameAnimClip(Id clipId, int frameCount, double frameRate, IReadOnlyDictionary<string, int>? keyframes = null)
        {
            if (frameCount <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(frameCount), "frameCount 必须为正数");
            }
            if (frameRate <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(frameRate), "frameRate 必须为正数");
            }

            ClipId = clipId;
            FrameCount = frameCount;
            FrameRate = frameRate;
            Keyframes = keyframes ?? EmptyKeyframes;
        }

        private static readonly IReadOnlyDictionary<string, int> EmptyKeyframes = new Dictionary<string, int>();
    }
}

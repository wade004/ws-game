using System;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 动画时间标记到达的可选能力接口（手感设计/04 第 5 节表现类标记：<c>footstep</c>、<c>trail_start</c>/<c>trail_end</c>、
    /// <c>fx:&lt;id&gt;</c>、<c>impact</c>，ADR-0148）。同 <see cref="IHitFrameEmitter"/> 的做法独立成可选接口：不支持的
    /// <see cref="ICharacterRig"/> 实现不需要改动。<see cref="SpriteCharacterRig"/>/<see cref="ModelCharacterRig"/> 恒触发（与
    /// <see cref="RenderOptions.HitFrameSync"/> 无关），没有订阅者时零开销。
    /// </summary>
    public interface IAnimMarkerEmitter
    {
        /// <summary>
        /// 当前播放的剪辑推进到某个已登记标记：参数是实体 id 与标准化后的标记名（见 <see cref="AnimMarkerNames"/>：
        /// 重复同名标记的内部序号后缀已去掉；model 型的 <c>anim_event.</c> 前缀已去掉，带参标记的点分段已还原为冒号，
        /// 如 <c>fx:smoke</c>）。
        /// </summary>
        event Action<Id, string>? AnimMarker;
    }
}

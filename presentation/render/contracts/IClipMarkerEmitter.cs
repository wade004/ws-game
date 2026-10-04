using System;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 剪辑标记对外发布（手感设计/04 第 5 节，ADR-0147）：剪辑内的表现类标记（<c>footstep</c>、<c>trail_start</c>、<c>fx</c>、<c>hit_frame</c> 等，
    /// 序列帧型来自 <see cref="FrameAnimClip.Keyframes"/>、骨骼型来自 <c>display.anim_set</c> 剪辑的 <c>events</c>）播放到达时，
    /// 经本事件向外发布 <c>(entityId, 标记名)</c>。
    /// <para>
    /// 判断记录：①<b>本接口只保证"发出去"</b>——框架不替消费方决定标记要触发什么；特效/打击反馈一类的消费由反馈绑定与音画（04 第 5 节、07）接线，
    /// 脚步声音效等其它消费方订阅本事件即可。②与判定无关：表现类标记不进事件总线的 <c>action.marker</c>（那条只承载判定类标记，01 第 3.3 节），
    /// 本事件纯呈现、不得反向影响判定。③标记名原样转发（不过滤、不改写）；<c>model</c> 型的完成回调事件（内部装配用）不算标记，不经本事件。
    /// ④<c>model</c> 型的标记由引擎适配层的 <c>IRenderer3D.OnAnimEvent</c> 通道送来，事件 id 为 <c>anim_event.&lt;标记名&gt;</c>，去前缀即标记名。
    /// 与 <see cref="IHitFrameEmitter"/> 同做法：可选接口，不支持的 <see cref="ICharacterRig"/> 实现不必新增成员；消费方按 <c>rig is IClipMarkerEmitter</c> 探测。
    /// </para>
    /// </summary>
    public interface IClipMarkerEmitter
    {
        /// <summary>剪辑标记到达：<c>(entityId, 标记名)</c>。</summary>
        event Action<Id, string>? ClipMarkerReached;
    }
}

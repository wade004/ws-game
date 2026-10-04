using System;
using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 动画时间标记的聚合来源（手感设计/04 第 5 节表现类标记，ADR-0148）：按实体转发各 rig 的 <see cref="Presentation.Render.IAnimMarkerEmitter.AnimMarker"/>。
    /// 框架自带实现是 <c>CharacterRigHitFrameSource</c>（它本就登记了引擎侧创建的全部 rig）；标记名已标准化
    /// （<c>footstep</c>、<c>trail_start</c>、<c>trail_end</c>、<c>fx:&lt;id&gt;</c>、<c>impact</c>、<c>hit_frame</c> 等）。
    /// </summary>
    public interface IAnimMarkerSource
    {
        /// <summary>某实体当前剪辑推进到一个标记（实体 id，标记名）。</summary>
        event Action<Id, string>? AnimMarkerReached;
    }

    /// <summary>
    /// 把一件事推迟到某个标记再做的小口（用于 <c>flash.sync = impact_marker</c>）：实体在 <paramref name="timeoutSeconds"/> 内到达
    /// <paramref name="marker"/> 就执行 <paramref name="action"/>，超时也执行（照常闪，不丢）。
    /// </summary>
    public interface IAnimMarkerGate
    {
        void DeferUntilMarker(Id entityId, string marker, Action action, double timeoutSeconds);
    }
}

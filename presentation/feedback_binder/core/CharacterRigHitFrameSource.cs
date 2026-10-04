using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary><see cref="IHitFrameSource"/> 的默认实现（ADR-0017 决策 d）：内部持有一张
    /// "实体 id → 当前登记的 rig"表，按实体订阅/退订各 rig 的 <see cref="IHitFrameEmitter.HitFrameReached"/>，
    /// 转发为按实体 id 广播的聚合事件。
    /// <para>
    /// PJ130-04 勘误：命中帧不再是 <see cref="ICharacterRig"/> 的强制成员（见 <see cref="IHitFrameEmitter"/>
    /// 类型注释"修订记录"）——本类型按 <c>rig is IHitFrameEmitter</c> 探测，未实现该可选接口的
    /// <see cref="ICharacterRig"/>（含继续实现旧接口形状的外部实现）仍然登记成功（<see cref="HasRig"/>
    /// 照常返回 true——一个 rig 确实已经登记，只是它不参与命中帧同步），只是不会订阅到任何事件、也永远
    /// 不会转发——与改动前"HitFrameSync 未设为 AnimKeyframeDriven 时 rig 从不触发"是同一种可观察结果，
    /// 消费方（<see cref="Presentation.FeedbackBinder.Core.HitFrameSyncPolicy"/>）不需要区分这两种"不
    /// 触发"的具体原因。
    /// </para>
    /// </summary>
    public sealed class CharacterRigHitFrameSource : IHitFrameSource, IAnimMarkerSource
    {
        private sealed class Entry
        {
            public readonly IHitFrameEmitter? Emitter;
            public readonly Action<Id> Handler;
            public readonly IAnimMarkerEmitter? MarkerEmitter;
            public readonly Action<Id, string> MarkerHandler;

            public Entry(IHitFrameEmitter? emitter, Action<Id> handler, IAnimMarkerEmitter? markerEmitter, Action<Id, string> markerHandler)
            {
                Emitter = emitter;
                Handler = handler;
                MarkerEmitter = markerEmitter;
                MarkerHandler = markerHandler;
            }
        }

        private readonly Dictionary<Id, Entry> _entries = new Dictionary<Id, Entry>();

        public event Action<Id>? HitFrameReached;

        /// <summary>登记过的 rig 的动画标记到达（<see cref="IAnimMarkerSource"/>，ADR-0148）：<c>footstep</c>、<c>trail_start/trail_end</c>、
        /// <c>fx:&lt;id&gt;</c>、<c>impact</c> 等，由 <c>AnimMarkerDirector</c> 消费。</summary>
        public event Action<Id, string>? AnimMarkerReached;

        public void RegisterRig(Id entityId, ICharacterRig rig)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));

            UnregisterRig(entityId);

            void Handler(Id raisedFor) => HitFrameReached?.Invoke(raisedFor);

            void MarkerHandler(Id raisedFor, string marker) => AnimMarkerReached?.Invoke(raisedFor, marker);

            var emitter = rig as IHitFrameEmitter;
            if (emitter != null)
            {
                emitter.HitFrameReached += Handler;
            }
            var markerEmitter = rig as IAnimMarkerEmitter;
            if (markerEmitter != null)
            {
                markerEmitter.AnimMarker += MarkerHandler;
            }
            _entries[entityId] = new Entry(emitter, Handler, markerEmitter, MarkerHandler);
        }

        public void UnregisterRig(Id entityId)
        {
            if (_entries.TryGetValue(entityId, out var entry))
            {
                if (entry.Emitter != null)
                {
                    entry.Emitter.HitFrameReached -= entry.Handler;
                }
                if (entry.MarkerEmitter != null)
                {
                    entry.MarkerEmitter.AnimMarker -= entry.MarkerHandler;
                }
                _entries.Remove(entityId);
            }
        }

        public bool HasRig(Id entityId) => _entries.ContainsKey(entityId);
    }
}

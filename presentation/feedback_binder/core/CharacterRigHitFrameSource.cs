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
    public sealed class CharacterRigHitFrameSource : IHitFrameSource
    {
        private sealed class Entry
        {
            public readonly IHitFrameEmitter? Emitter;
            public readonly Action<Id> Handler;

            public Entry(IHitFrameEmitter? emitter, Action<Id> handler)
            {
                Emitter = emitter;
                Handler = handler;
            }
        }

        private readonly Dictionary<Id, Entry> _entries = new Dictionary<Id, Entry>();

        public event Action<Id>? HitFrameReached;

        public void RegisterRig(Id entityId, ICharacterRig rig)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));

            UnregisterRig(entityId);

            void Handler(Id raisedFor) => HitFrameReached?.Invoke(raisedFor);

            var emitter = rig as IHitFrameEmitter;
            if (emitter != null)
            {
                emitter.HitFrameReached += Handler;
            }
            _entries[entityId] = new Entry(emitter, Handler);
        }

        public void UnregisterRig(Id entityId)
        {
            if (_entries.TryGetValue(entityId, out var entry))
            {
                if (entry.Emitter != null)
                {
                    entry.Emitter.HitFrameReached -= entry.Handler;
                }
                _entries.Remove(entityId);
            }
        }

        public bool HasRig(Id entityId) => _entries.ContainsKey(entityId);
    }
}

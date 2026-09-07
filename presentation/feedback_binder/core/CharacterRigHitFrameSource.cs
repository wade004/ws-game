using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary><see cref="IHitFrameSource"/> 的默认实现（ADR-0017 决策 d）：内部持有一张
    /// "实体 id → 当前登记的 rig"表，按实体订阅/退订各 rig 的 <see cref="ICharacterRig.HitFrameReached"/>，
    /// 转发为按实体 id 广播的聚合事件。</summary>
    public sealed class CharacterRigHitFrameSource : IHitFrameSource
    {
        private sealed class Entry
        {
            public readonly ICharacterRig Rig;
            public readonly Action<Id> Handler;

            public Entry(ICharacterRig rig, Action<Id> handler)
            {
                Rig = rig;
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
            rig.HitFrameReached += Handler;
            _entries[entityId] = new Entry(rig, Handler);
        }

        public void UnregisterRig(Id entityId)
        {
            if (_entries.TryGetValue(entityId, out var entry))
            {
                entry.Rig.HitFrameReached -= entry.Handler;
                _entries.Remove(entityId);
            }
        }

        public bool HasRig(Id entityId) => _entries.ContainsKey(entityId);
    }
}

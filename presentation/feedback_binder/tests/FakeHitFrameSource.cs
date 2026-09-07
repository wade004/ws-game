using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>测试用最小 <see cref="IHitFrameSource"/> 假实现：<see cref="RegisterRig"/>/
    /// <see cref="UnregisterRig"/> 只记账"该实体是否已登记"，不真正订阅任何 <see cref="ICharacterRig"/>；
    /// <see cref="Fire"/> 供测试代码直接模拟一次命中帧广播，不必真的构造/驱动一个 rig。</summary>
    internal sealed class FakeHitFrameSource : IHitFrameSource
    {
        private readonly HashSet<Id> _registered = new HashSet<Id>();

        public event Action<Id>? HitFrameReached;

        public void RegisterRig(Id entityId, ICharacterRig rig) => _registered.Add(entityId);

        public void UnregisterRig(Id entityId) => _registered.Remove(entityId);

        public bool HasRig(Id entityId) => _registered.Contains(entityId);

        /// <summary>测试用：模拟实体 <paramref name="entityId"/> 到达一次命中帧。</summary>
        public void Fire(Id entityId) => HitFrameReached?.Invoke(entityId);
    }
}

using System;
using Core.Foundation.Common;
using Presentation.Common;

namespace Presentation.Camera
{
    /// <summary>把 <see cref="ISimSnapshot"/> 包一层成 <see cref="ICameraFollowTarget"/>（见接口
    /// 判断记录）：不插值，直接读当前位置——<c>ICamera.Follow</c> 自带 <c>smoothing</c> 平滑系数，
    /// 不强求逐帧插值一致。</summary>
    public sealed class SimSnapshotFollowTarget : ICameraFollowTarget
    {
        private readonly ISimSnapshot _snapshot;

        public SimSnapshotFollowTarget(ISimSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public Vec2 GetPosition(Id entityId, double alpha) => _snapshot.GetPosition(entityId);
    }
}

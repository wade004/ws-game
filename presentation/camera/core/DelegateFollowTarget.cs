using System;
using Core.Foundation.Common;

namespace Presentation.Camera
{
    /// <summary>把任意 <c>(Id entityId, double alpha) => Vec2</c> 方法组包一层成
    /// <see cref="ICameraFollowTarget"/>（见接口判断记录），典型用法是包一层
    /// <c>Presentation.ViewBinding.ViewBinder.GetInterpolatedPosition</c>，让 <see cref="CameraHost"/>
    /// 在不直接引用 <c>presentation/view_binding</c> 的前提下也能用插值位置跟随。</summary>
    public sealed class DelegateFollowTarget : ICameraFollowTarget
    {
        private readonly Func<Id, double, Vec2> _resolver;

        public DelegateFollowTarget(Func<Id, double, Vec2> resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public Vec2 GetPosition(Id entityId, double alpha) => _resolver(entityId, alpha);
    }
}

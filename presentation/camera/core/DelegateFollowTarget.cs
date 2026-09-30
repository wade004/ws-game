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

        /// <summary>ADR-0121 第 6 条（D6）：包一层委托的"目标缺失"语义——被包装的解析函数对不存在的实体按惯例抛异常
        /// （如 <c>ViewBinder.GetInterpolatedPosition</c> 对未绑定实体抛 <see cref="InvalidOperationException"/>），
        /// 这里把解析函数抛出的任何异常都视为"目标当前不可取"，返回 false 而不向上传播。</summary>
        public bool TryGetPosition(Id entityId, double alpha, out Vec2 position)
        {
            try
            {
                position = _resolver(entityId, alpha);
                return true;
            }
            catch (Exception)
            {
                position = default;
                return false;
            }
        }
    }
}

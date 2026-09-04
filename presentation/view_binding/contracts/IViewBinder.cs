using Core.Foundation.Common;

namespace Presentation.ViewBinding
{
    /// <summary>
    /// 视图绑定契约（见 01_分层与依赖.md L5 模块表 <c>view_binding</c> 行"契约接口名：ViewBinder"、
    /// 03_运行时骨架.md 第 9 节 <c>ViewBinder</c> 签名）：逻辑对象与表现视图的创建、同步、销毁协议。
    /// <see cref="OnEntityCreated"/>/<see cref="OnEntityDestroyed"/> 通常由 <c>ViewBinder</c> 内部
    /// 订阅 <c>entity.created</c>/<c>entity.destroyed</c> 事件后自动调用（见 03 第 5 节），本接口
    /// 同时把它们暴露为公开方法，供测试或需要手动驱动的调用方直接调用。
    /// </summary>
    public interface IViewBinder
    {
        /// <summary>为新实体创建并绑定 View（见 03 第 5 节"创建"）。<paramref name="kind"/> 是
        /// <c>Entity.Kind</c> 自由字符串，经 <c>Presentation.Common.EntityKindMapping</c> 映射为
        /// <c>ViewKind</c>；映射不出已知分类时跳过创建（记诊断，不抛异常）。</summary>
        void OnEntityCreated(Id entityId, string kind, Id displayId);

        /// <summary>销毁对应 View 并从绑定表移除（见 03 第 5 节"销毁"）。未绑定的 id 静默忽略。</summary>
        void OnEntityDestroyed(Id entityId);

        /// <summary>按 <c>prev + (curr - prev) × alpha</c> 插值出实体的渲染位置（见 03 第 3.1 节、
        /// 第 9 节）。未绑定的实体抛 <see cref="System.InvalidOperationException"/>。</summary>
        Vec2 GetInterpolatedPosition(Id entityId, double alpha);
    }
}

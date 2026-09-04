using Core.Foundation.Common;

namespace Presentation.Common
{
    /// <summary>
    /// View 工厂（见 09_表现层.md 第 2 节 <c>ViewFactory.createView(kind, displayId): View</c>）。
    /// 由引擎侧实现（具体 View 的渲染节点结构完全由引擎适配层实现细节决定，本接口不约束）；
    /// <see cref="Presentation.ViewBinding.IViewBinder"/> 是本接口的唯一调用方。
    /// </summary>
    public interface IViewFactory
    {
        /// <summary><paramref name="displayId"/> 用于查询 <c>IDisplayInfoRegistry</c> 取得表现资源
        /// 引用（见 03 第 5 节"用于查询外形的逻辑 id"）；<paramref name="entityId"/> 是本 View 应
        /// <c>Bind</c> 的实体 id。</summary>
        IView CreateView(ViewKind kind, Id displayId, Id entityId);
    }
}

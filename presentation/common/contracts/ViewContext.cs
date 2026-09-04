using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;

namespace Presentation.Common
{
    /// <summary>
    /// 引擎侧传给 <see cref="IViewFactory"/> 的绘制能力集合（拍板：不新造一个
    /// <c>IRenderSurface</c> 抽象——View 实现直接持有 <see cref="IRenderer2D"/>/
    /// <see cref="IRenderer3D"/>，本类型只是把创建 View 时需要用到的几个 L-1/L0 只读依赖打包
    /// 传递，避免 <see cref="IViewFactory.CreateView"/> 的签名随需要的依赖增减而反复变动）。
    /// <see cref="Renderer3D"/> 可为空——只使用 <c>sprite</c> 型外形的游戏可以不提供
    /// <see cref="Core.Foundation.EngineAdapter.IRenderer3D"/> 实现（见 02 第 1.12 节"条件必需"）。
    /// </summary>
    public sealed class ViewContext
    {
        public IRenderer2D Renderer2D { get; }

        public IRenderer3D? Renderer3D { get; }

        public ICamera Camera { get; }

        public IDisplayInfoRegistry DisplayInfo { get; }

        public ViewContext(IRenderer2D renderer2D, IRenderer3D? renderer3D, ICamera camera, IDisplayInfoRegistry displayInfo)
        {
            Renderer2D = renderer2D ?? throw new System.ArgumentNullException(nameof(renderer2D));
            Renderer3D = renderer3D;
            Camera = camera ?? throw new System.ArgumentNullException(nameof(camera));
            DisplayInfo = displayInfo ?? throw new System.ArgumentNullException(nameof(displayInfo));
        }
    }
}

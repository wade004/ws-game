// StubEngine：一次性构造全部 13 个桩适配层实现，供测试注入使用。
// 用法：var engine = new StubEngine(); 然后把 engine.Clock、engine.FileSystem 等分别注入
// 需要对应接口的被测对象。每个属性都是具体的 Stub* 类型（而非接口类型），方便测试直接调用
// 各桩类型上额外暴露的测试专用方法（如 StubClock.Advance、StubInput.Press）。
namespace Adapters.Stub
{
    /// <summary>一次性构造全部桩适配层实现的集合，供测试整体注入；每个属性是具体 Stub* 类型，可直接调用其测试专用方法。</summary>
    public sealed class StubEngine
    {
        /// <summary>窗口桩。</summary>
        public StubWindow Window { get; } = new StubWindow();
        /// <summary>时钟桩（可手动推进时间）。</summary>
        public StubClock Clock { get; } = new StubClock();
        /// <summary>二维渲染桩。</summary>
        public StubRenderer2D Renderer2D { get; } = new StubRenderer2D();
        /// <summary>音频桩。</summary>
        public StubAudio Audio { get; } = new StubAudio();
        /// <summary>输入桩（可模拟按键）。</summary>
        public StubInput Input { get; } = new StubInput();
        /// <summary>内存文件系统桩。</summary>
        public StubFileSystem FileSystem { get; } = new StubFileSystem();
        /// <summary>资源加载桩。</summary>
        public StubResourceLoader ResourceLoader { get; } = new StubResourceLoader();
        /// <summary>二维导航桩。</summary>
        public StubNavigation2D Navigation2D { get; } = new StubNavigation2D();
        /// <summary>空间查询桩。</summary>
        public StubSpatialQuery SpatialQuery { get; } = new StubSpatialQuery();
        /// <summary>界面表面桩。</summary>
        public StubUISurface UISurface { get; } = new StubUISurface();
        /// <summary>平台能力桩。</summary>
        public StubPlatform Platform { get; } = new StubPlatform();
        /// <summary>三维渲染桩。</summary>
        public StubRenderer3D Renderer3D { get; } = new StubRenderer3D();
        /// <summary>镜头桩。</summary>
        public StubCamera Camera { get; } = new StubCamera();
    }
}

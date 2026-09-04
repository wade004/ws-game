// StubEngine：一次性构造全部 13 个桩适配层实现，供测试注入使用。
// 用法：var engine = new StubEngine(); 然后把 engine.Clock、engine.FileSystem 等分别注入
// 需要对应接口的被测对象。每个属性都是具体的 Stub* 类型（而非接口类型），方便测试直接调用
// 各桩类型上额外暴露的测试专用方法（如 StubClock.Advance、StubInput.Press）。
namespace Adapters.Stub
{
    public sealed class StubEngine
    {
        public StubWindow Window { get; } = new StubWindow();
        public StubClock Clock { get; } = new StubClock();
        public StubRenderer2D Renderer2D { get; } = new StubRenderer2D();
        public StubAudio Audio { get; } = new StubAudio();
        public StubInput Input { get; } = new StubInput();
        public StubFileSystem FileSystem { get; } = new StubFileSystem();
        public StubResourceLoader ResourceLoader { get; } = new StubResourceLoader();
        public StubNavigation2D Navigation2D { get; } = new StubNavigation2D();
        public StubSpatialQuery SpatialQuery { get; } = new StubSpatialQuery();
        public StubUISurface UISurface { get; } = new StubUISurface();
        public StubPlatform Platform { get; } = new StubPlatform();
        public StubRenderer3D Renderer3D { get; } = new StubRenderer3D();
        public StubCamera Camera { get; } = new StubCamera();
    }
}

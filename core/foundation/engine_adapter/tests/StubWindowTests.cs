using Adapters.Stub;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// W2 收边补齐（A1 审计第 7 节，测试完备性缺口）：<see cref="Core.Foundation.EngineAdapter.IWindow"/>
    /// 的 <c>Destroy()</c> 此前只在 <c>WindowScenarios.cs</c> 覆盖 Create/SetResolution/
    /// SetFullscreen/OnCloseRequested 四个场景，没有一条把 <c>Destroy</c> 本身作为断言主语的场景，
    /// <c>StubWindow</c> 已实现但零测试调用。惯例同 <c>StubSpatialQueryTests.cs</c>：直接构造
    /// <c>StubWindow</c>，不依赖 <c>adapters/conformance</c> 场景框架。
    /// </summary>
    public sealed class StubWindowTests
    {
        [Fact]
        public void Destroy_AfterCreate_ClearsCreatedFlag()
        {
            var window = new StubWindow();
            window.Create("Sample", 1280, 720);
            Assert.True(window.Created);

            window.Destroy();

            Assert.False(window.Created);
        }

        [Fact]
        public void Destroy_DoesNotClearOtherState_OnlyCreatedFlag()
        {
            // Destroy 只清 Created 标志（StubWindow 是内存桩，见其注释"全部状态只是内存字段"），
            // 不清除 Title/Width/Height/Fullscreen——真实窗口销毁后这些字段本就无意义，但桩不
            // 假装重置它们，验证这一具体行为，避免调用方误以为 Destroy 后能读到"重置后的默认值"。
            var window = new StubWindow();
            window.Create("Sample", 1280, 720);
            window.SetFullscreen(true);

            window.Destroy();

            Assert.Equal("Sample", window.Title);
            Assert.Equal(1280, window.Width);
            Assert.Equal(720, window.Height);
            Assert.True(window.Fullscreen);
        }

        [Fact]
        public void Destroy_WithoutCreate_IsNoOp_AndCreatedStaysFalse()
        {
            var window = new StubWindow();

            window.Destroy();

            Assert.False(window.Created);
        }
    }
}

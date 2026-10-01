using Core.Numbers.PowerSet;
using Xunit;

namespace Tests.Numbers.PowerSet
{
    /// <summary>
    /// <see cref="InMemoryPowerDiagnostics"/> 记录容器的直接用例（T-L3 numbers 半，2026-10-01 测试覆盖第四批）：
    /// 初始为空、按调用顺序累积、重复消息不去重、返回实时视图；<see cref="PowerTickHandler"/> 缺省使用它。
    /// </summary>
    public sealed class InMemoryPowerDiagnosticsTests
    {
        [Fact]
        public void Warn_AccumulatesInOrder_AndKeepsDuplicates()
        {
            var diagnostics = new InMemoryPowerDiagnostics();
            Assert.Empty(diagnostics.Warnings);

            diagnostics.Warn("a");
            diagnostics.Warn("b");
            diagnostics.Warn("a");

            Assert.Equal(new[] { "a", "b", "a" }, diagnostics.Warnings);
        }

        [Fact]
        public void Warnings_IsALiveView_NotASnapshot()
        {
            var diagnostics = new InMemoryPowerDiagnostics();
            var view = diagnostics.Warnings;

            diagnostics.Warn("late");

            Assert.Single(view);
        }

        [Fact]
        public void PowerTickHandler_DefaultDiagnostics_IsInMemory()
        {
            var bus = PowerTestSupport.CreateBus();
            var host = new PowerHost(new System.Collections.Generic.List<PowerTypeDefinition>(), bus);

            var handler = new PowerTickHandler(host);

            Assert.IsType<InMemoryPowerDiagnostics>(handler.Diagnostics);
        }
    }
}

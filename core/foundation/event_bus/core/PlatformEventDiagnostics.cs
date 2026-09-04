using System;
using Core.Foundation.EngineAdapter;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 可选：把 <see cref="Error"/> 级诊断转发到 <c>IPlatform.ReportCrash</c>（引擎适配层
    /// L-1 接口，见 engine_adapter/contracts/IPlatform.cs：其文档注释明确写着"供 L0 等其它层
    /// 在捕获到不可恢复错误时调用"）。<c>IPlatform</c> 目前没有区分级别的通用日志方法，只有
    /// 面向不可恢复错误的 <c>ReportCrash</c>，因此 <see cref="Warn"/> 没有对应的上报通道——
    /// 本适配器里 <see cref="Warn"/> 是空操作（不是丢弃警告；需要保留警告的调用方应另外
    /// 同时使用一份 <see cref="InMemoryEventDiagnostics"/>）。
    ///
    /// 本类型是可选项：<see cref="EventBus"/> 的默认诊断实现是
    /// <see cref="InMemoryEventDiagnostics"/>，不依赖本类型，也就不依赖 <c>IPlatform</c>；
    /// 只有显式选择用本类型构造 <see cref="EventBus"/> 时才会引入这一依赖。
    /// </summary>
    public sealed class PlatformEventDiagnostics : IEventDiagnostics
    {
        private const string CrashContext = "Core.Foundation.EventBus";

        private readonly IPlatform _platform;

        public PlatformEventDiagnostics(IPlatform platform)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public void Warn(string message)
        {
            // IPlatform 无通用日志方法，警告级别没有可用的上报通道，此处刻意不做任何事。
        }

        public void Error(string message, Exception? exception)
        {
            var details = exception == null ? message : $"{message}\n{exception}";
            _platform.ReportCrash(CrashContext, details);
        }
    }
}

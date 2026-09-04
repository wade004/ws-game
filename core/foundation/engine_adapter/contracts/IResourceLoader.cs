using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>资源种类：图片、音频、字体、数据表。</summary>
    public enum ResourceKind
    {
        Image,
        Audio,
        Font,
        DataTable
    }

    /// <summary>加载完成或失败时触发一次，success 为 false 表示加载失败。</summary>
    public delegate void LoadCallback(Id resourceId, bool success);

    /// <summary>
    /// 图片、音频、字体、数据表、异步加载（见 02_引擎适配层.md 第 1.7 节）。必需接口。
    /// 加载过程不得阻塞主线程；数据表资源经此加载后交给 L0 的 DataRegistry 解析，
    /// 图片/音频/字体资源加载后交给 IRenderer2D/IAudio/IUISurface 使用。
    /// 线程约定：回调可能在非主线程触发，调用方须自行处理回到主线程的切换，或由适配层实现
    /// 保证回调总在主线程的下一次 IClock.onFrame 之前排队执行（见 02 第 2 节第 2 条）。
    /// </summary>
    public interface IResourceLoader
    {
        void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback);

        bool IsLoaded(Id resourceId);

        /// <summary>用于加载画面展示百分比，允许粗粒度估算，取值范围 [0, 1]。</summary>
        double GetLoadProgress(Id resourceId);

        void Unload(Id resourceId);
    }
}

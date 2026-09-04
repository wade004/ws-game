using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 窗口生命周期、分辨率、全屏（见 02_引擎适配层.md 第 1.1 节）。必需接口。
    /// setResolution 与 setFullscreen 允许有一帧以上延迟生效，但必须在下一次
    /// IClock.onFrame 之前完成状态切换；不得阻塞主线程超过一帧（性能约定，由具体引擎实现保证）。
    /// </summary>
    public interface IWindow
    {
        void Create(string title, int width, int height);

        void Destroy();

        void SetResolution(int width, int height);

        void SetFullscreen(bool enabled);

        bool IsFocused();

        /// <summary>
        /// 用户请求关闭窗口（点击关闭按钮、Alt+F4 等）时回调，由上层决定是否弹出
        /// "未保存进度"确认后再真正调用 Destroy。
        /// </summary>
        void OnCloseRequested(Callback callback);
    }
}

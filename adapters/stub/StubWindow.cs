// StubWindow：IWindow 的最小可用桩实现。
// 用途：让不依赖真实窗口系统的测试可以驱动"窗口创建/销毁/分辨率/全屏/关闭请求"这条流程。
// 与真实实现的差异：不创建任何操作系统窗口，全部状态只是内存字段；IsFocused 默认恒为
// true，可用 SetFocusedForTest 覆盖；RequestCloseForTest 是测试专用方法（不属于 IWindow
// 接口），用于模拟"用户点击了关闭按钮"从而触发已注册的 OnCloseRequested 回调。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubWindow : IWindow
    {
        private readonly List<Callback> _closeCallbacks = new List<Callback>();
        private bool _focused = true;

        public bool Created { get; private set; }
        public string Title { get; private set; } = string.Empty;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Fullscreen { get; private set; }

        public void Create(string title, int width, int height)
        {
            Title = title;
            Width = width;
            Height = height;
            Created = true;
        }

        public void Destroy()
        {
            Created = false;
        }

        public void SetResolution(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public void SetFullscreen(bool enabled)
        {
            Fullscreen = enabled;
        }

        public bool IsFocused() => _focused;

        public void OnCloseRequested(Callback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _closeCallbacks.Add(callback);
        }

        /// <summary>测试用：设置 IsFocused() 的返回值。</summary>
        public void SetFocusedForTest(bool focused) => _focused = focused;

        /// <summary>测试用：模拟用户请求关闭窗口，触发全部已注册的 OnCloseRequested 回调。</summary>
        public void RequestCloseForTest()
        {
            foreach (var callback in _closeCallbacks)
            {
                callback();
            }
        }
    }
}

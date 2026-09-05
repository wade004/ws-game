#nullable enable
// UnityWindow：IWindow 的 Unity 引擎实现。
//
// 判断记录：Unity 播放器窗口在进程启动时已由 Player Settings 创建，运行期没有公开 API
// 可以重新创建操作系统窗口本体，也没有跨平台公开 API 可以运行期修改标题栏文字（原生标题栏
// 文字变更需要平台特定的原生插件，超出本阶段范围）；因此 Create/Destroy 只做"记录状态 +
// 尽力而为地调用引擎对应能力"：Create 记录 title/width/height 并应用分辨率；Destroy 在真实
// 播放器构建中调用 Application.Quit()，在编辑器 Play Mode 下 Application.Quit() 不生效
// （引擎行为如此），改为记录状态供测试断言。
//
// OnCloseRequested 语义（"用户请求关闭窗口时回调，由上层决定是否弹出确认后再调用 Destroy"）
// 用 Application.wantsToQuit 承接：该回调返回 false 表示"阻止本次退出"，交由上层决定何时
// 真正调用 Destroy()（进而触发真正的 Application.Quit()）。用 _quitConfirmed 标志避免
// Destroy -> Application.Quit() -> wantsToQuit 的递归触发。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityWindow : IWindow
    {
        private readonly List<Callback> _closeCallbacks = new List<Callback>();
        private bool _quitConfirmed;
        private bool _wantsToQuitHooked;

        public bool Created { get; private set; }
        public string Title { get; private set; } = string.Empty;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Fullscreen { get; private set; }

        public void Create(string title, int width, int height)
        {
            Title = title ?? string.Empty;
            Width = width;
            Height = height;
            Created = true;

            Screen.SetResolution(width, height, Screen.fullScreenMode);

            if (!_wantsToQuitHooked)
            {
                _wantsToQuitHooked = true;
                Application.wantsToQuit += HandleWantsToQuit;
            }
        }

        public void Destroy()
        {
            Created = false;
            _quitConfirmed = true;
            Application.Quit();
        }

        public void SetResolution(int width, int height)
        {
            Width = width;
            Height = height;
            Screen.SetResolution(width, height, Screen.fullScreenMode);
        }

        public void SetFullscreen(bool enabled)
        {
            Fullscreen = enabled;
            Screen.fullScreenMode = enabled ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        public bool IsFocused() => Application.isFocused;

        public void OnCloseRequested(Callback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _closeCallbacks.Add(callback);
        }

        private bool HandleWantsToQuit()
        {
            if (_quitConfirmed)
            {
                return true;
            }

            for (var i = 0; i < _closeCallbacks.Count; i++)
            {
                _closeCallbacks[i]();
            }

            // 阻止本次默认退出，交由上层在收到回调后自行决定何时调用 Destroy() 真正退出。
            return false;
        }

        /// <summary>测试用：绕过 Application.wantsToQuit（批处理/编辑器环境下该事件未必真实触发），
        /// 直接模拟一次"用户请求关闭窗口"，触发全部已注册回调。</summary>
        public void RequestCloseForTest()
        {
            for (var i = 0; i < _closeCallbacks.Count; i++)
            {
                _closeCallbacks[i]();
            }
        }
    }
}

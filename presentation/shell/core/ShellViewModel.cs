using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;

namespace Presentation.Shell
{
    /// <summary>
    /// Shell 视图模型（见 01_分层与依赖.md L5 模块表 <c>shell</c> 行、任务书"ShellViewModel（当前
    /// 页面、菜单项文本键、槽位摘要、加载进度）"）。纯数据 + 刷新逻辑，不含任何绘制；具体菜单项
    /// 内容来自构造期注入的 <see cref="ShellMenuDefinition"/>（见 <c>schema/ShellMenuSchema.cs</c>，
    /// 游戏层数据决定，01 模块表 <c>shell</c> 行策略配置项"菜单结构（游戏层可扩展）"）。
    /// </summary>
    public sealed class ShellViewModel : IDisposable
    {
        private readonly IShellHost _shell;
        private readonly ISaveSystem _saveSystem;
        private readonly ShellMenuDefinition _menu;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public ShellPage CurrentPage { get; private set; }

        public IReadOnlyList<ShellMenuEntry> MenuEntries => _menu.Entries;

        public IReadOnlyList<SaveSlotInfo> SlotSummaries { get; private set; } = Array.Empty<SaveSlotInfo>();

        public double LoadingProgress { get; private set; }

        public bool IsLoading { get; private set; }

        public ShellViewModel(IShellHost shell, ISaveSystem saveSystem, IEventBus eventBus, ShellMenuDefinition menu)
        {
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _saveSystem = saveSystem ?? throw new ArgumentNullException(nameof(saveSystem));
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));

            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            _subscriptions.Add(eventBus.Subscribe(AppEventKeys.StateChanged, OnRelevantEvent));
            _subscriptions.Add(eventBus.Subscribe(SceneRouterEventKeys.LoadStarted, OnRelevantEvent));
            _subscriptions.Add(eventBus.Subscribe(SceneRouterEventKeys.LoadFinished, OnRelevantEvent));
            _subscriptions.Add(eventBus.Subscribe(SaveEventKeys.SaveCompleted, OnRelevantEvent));
            _subscriptions.Add(eventBus.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            CurrentPage = _shell.Page;
            IsLoading = _shell.IsLoading;
            LoadingProgress = _shell.LoadingProgress;
            SlotSummaries = _saveSystem.ListSlots();
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}

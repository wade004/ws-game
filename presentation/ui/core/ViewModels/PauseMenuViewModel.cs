using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Presentation.Ui
{
    /// <summary>一条暂停菜单选项：选项 id + 显示文本键（具体 UI 文案经 <c>IL10nHost.Text</c>
    /// 解析，本视图模型不解析，只透传 key，惯例同 <c>ShellMenuEntry</c>）。</summary>
    public readonly struct PauseMenuOption
    {
        public Id OptionId { get; }

        public Id TextKey { get; }

        public PauseMenuOption(Id optionId, Id textKey)
        {
            OptionId = optionId;
            TextKey = textKey;
        }
    }

    /// <summary>
    /// 暂停菜单视图模型（见 03_运行时骨架.md 第 2 节 <c>Pause</c> 主状态、09 第 7.1 节 UI 组成
    /// "菜单"）。选项内容（继续/存档/设置/返回主菜单等）由构造期注入，本模块不硬编码具体菜单
    /// 结构（呼应 01_分层与依赖.md L5 模块表 <c>shell</c> 行策略配置项"菜单结构（游戏层可扩展）"，
    /// 本视图模型只是通用容器，不依赖 <c>presentation/shell</c> 的 <c>ShellMenuDefinition</c>——
    /// 二者结构相似是巧合，不是耦合：Shell 的菜单驱动 MainMenu 之外的整个应用外壳，暂停菜单是
    /// InWorld 内的一个 UI 面板，两者生命周期不同，不应互相依赖，见 09 第 9 节"Shell 与游戏内 HUD
    /// 共用同一套 UI 框架与数据绑定方式"——共用的是框架，不是具体菜单数据类型）。
    /// </summary>
    public sealed class PauseMenuViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IAppStateHost _appState;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public AppState CurrentState { get; private set; }

        public bool IsPaused => CurrentState == AppState.Pause;

        public IReadOnlyList<PauseMenuOption> Options { get; }

        public PauseMenuViewModel(IUiDataSource dataSource, IAppStateHost appState, IReadOnlyList<PauseMenuOption> options)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _appState = appState ?? throw new ArgumentNullException(nameof(appState));
            Options = options ?? throw new ArgumentNullException(nameof(options));

            _subscriptions.Add(_dataSource.Subscribe(AppEventKeys.StateChanged, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            CurrentState = _appState.GetState();
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

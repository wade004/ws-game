using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Xunit;

namespace Tests.Foundation.AppLifecycle
{
    public class AppStateHostTests
    {
        // 到达某主状态所需的最短合法路径（从 Boot 出发）。
        private static readonly Dictionary<AppState, AppState[]> PathToState = new Dictionary<AppState, AppState[]>
        {
            [AppState.Boot] = new AppState[0],
            [AppState.MainMenu] = new[] { AppState.MainMenu },
            [AppState.Loading] = new[] { AppState.MainMenu, AppState.Loading },
            [AppState.InWorld] = new[] { AppState.MainMenu, AppState.Loading, AppState.InWorld },
            [AppState.Pause] = new[] { AppState.MainMenu, AppState.Loading, AppState.InWorld, AppState.Pause },
        };

        private static Core.Foundation.AppLifecycle.AppStateHost CreateHostAt(AppState from)
        {
            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus());
            foreach (var step in PathToState[from])
            {
                Assert.True(host.RequestTransition(step));
            }

            Assert.Equal(from, host.GetState());
            return host;
        }

        // 初始状态为 Boot
        [Fact]
        public void InitialState_IsBoot()
        {
            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus());

            Assert.Equal(AppState.Boot, host.GetState());
        }

        // 1~8. 03 第 2 节状态机表全部 8 条合法主状态转移逐条通过
        [Theory]
        [InlineData(AppState.Boot, AppState.MainMenu)]
        [InlineData(AppState.MainMenu, AppState.Loading)]
        [InlineData(AppState.Loading, AppState.InWorld)]
        [InlineData(AppState.InWorld, AppState.Pause)]
        [InlineData(AppState.InWorld, AppState.MainMenu)]
        [InlineData(AppState.InWorld, AppState.Loading)]
        [InlineData(AppState.Pause, AppState.InWorld)]
        [InlineData(AppState.Pause, AppState.MainMenu)]
        public void RequestTransition_LegalTransition_Succeeds(AppState from, AppState to)
        {
            var host = CreateHostAt(from);

            var result = host.RequestTransition(to);

            Assert.True(result);
            Assert.Equal(to, host.GetState());
        }

        // 9~14. 非法转移返回 false 且状态不变
        [Theory]
        [InlineData(AppState.Boot, AppState.InWorld)]
        [InlineData(AppState.Loading, AppState.Pause)]
        [InlineData(AppState.Pause, AppState.Loading)]
        [InlineData(AppState.MainMenu, AppState.InWorld)]
        [InlineData(AppState.InWorld, AppState.Boot)]
        [InlineData(AppState.Loading, AppState.MainMenu)]
        public void RequestTransition_IllegalTransition_ReturnsFalseAndStateUnchanged(AppState from, AppState to)
        {
            var host = CreateHostAt(from);

            var result = host.RequestTransition(to);

            Assert.False(result);
            Assert.Equal(from, host.GetState());
        }

        // 15. 事件先于 OnStateChanged 回调送达，且字段正确
        [Fact]
        public void RequestTransition_PublishesEventBeforeCallback()
        {
            var bus = AppLifecycleTestSupport.CreateBus();
            var order = new List<string>();
            AppStateChangedEvent? received = null;
            bus.Subscribe<AppStateChangedEvent>(AppEventKeys.StateChanged, evt =>
            {
                received = evt;
                order.Add("event");
            });

            var host = new Core.Foundation.AppLifecycle.AppStateHost(bus);
            host.OnStateChanged((oldState, newState) => order.Add("callback"));

            var result = host.RequestTransition(AppState.MainMenu);

            Assert.True(result);
            Assert.Equal(new[] { "event", "callback" }, order);
            Assert.NotNull(received);
            Assert.Equal(AppState.Boot, received!.OldState);
            Assert.Equal(AppState.MainMenu, received.NewState);
        }

        // 16. OnStateChanged 取消订阅后不再收到通知
        [Fact]
        public void OnStateChanged_Unsubscribe_StopsReceiving()
        {
            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus());
            var count = 0;
            var handle = host.OnStateChanged((oldState, newState) => count++);
            handle.Dispose();

            host.RequestTransition(AppState.MainMenu);

            Assert.Equal(0, count);
        }

        // 17. 进入 InWorld 自动置 Explore
        [Fact]
        public void EnteringInWorld_AutoSetsExplore()
        {
            var host = CreateHostAt(AppState.InWorld);

            Assert.Equal(SubStateId.Explore, host.CurrentSubState);
            Assert.Single(host.SubStateStack);
        }

        // 18. Explore→Combat→(Push MenuOverlay)→Pop 回 Combat→Pop 回 Explore
        [Fact]
        public void PushPop_MenuOverlayOnCombat_RoundTripsCorrectly()
        {
            var host = CreateHostAt(AppState.InWorld);

            Assert.True(host.PushSubState(InWorldSubState.Combat));
            Assert.Equal(SubStateId.Combat, host.CurrentSubState);

            Assert.True(host.PushSubState(InWorldSubState.MenuOverlay));
            Assert.Equal(SubStateId.MenuOverlay, host.CurrentSubState);

            Assert.True(host.PopSubState());
            Assert.Equal(SubStateId.Combat, host.CurrentSubState);

            Assert.True(host.PopSubState());
            Assert.Equal(SubStateId.Explore, host.CurrentSubState);
        }

        // 19. Explore 不可 Pop（栈底）
        [Fact]
        public void PopSubState_AtExplore_ReturnsFalse()
        {
            var host = CreateHostAt(AppState.InWorld);

            var result = host.PopSubState();

            Assert.False(result);
            Assert.Equal(SubStateId.Explore, host.CurrentSubState);
        }

        // 20. 非 InWorld 下 Push 返回 false
        [Fact]
        public void PushSubState_NotInWorld_ReturnsFalse()
        {
            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus());

            var result = host.PushSubState(InWorldSubState.Combat);

            Assert.False(result);
            Assert.Null(host.CurrentSubState);
        }

        // 21. 非 InWorld 下 Pop 返回 false
        [Fact]
        public void PopSubState_NotInWorld_ReturnsFalse()
        {
            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus());

            Assert.False(host.PopSubState());
        }

        // 22. 离开 InWorld 清空子状态栈
        [Fact]
        public void LeavingInWorld_ClearsSubStateStack()
        {
            var host = CreateHostAt(AppState.InWorld);
            Assert.True(host.PushSubState(InWorldSubState.Combat));

            Assert.True(host.RequestTransition(AppState.Pause));

            Assert.Null(host.CurrentSubState);
            Assert.Empty(host.SubStateStack);
        }

        // 23. 再次回到 InWorld 重新置 Explore（不残留旧栈内容）
        [Fact]
        public void ReenteringInWorld_ResetsToExplore()
        {
            var host = CreateHostAt(AppState.InWorld);
            Assert.True(host.PushSubState(InWorldSubState.Combat));
            Assert.True(host.RequestTransition(AppState.Pause));

            Assert.True(host.RequestTransition(AppState.InWorld));

            Assert.Equal(SubStateId.Explore, host.CurrentSubState);
            Assert.Single(host.SubStateStack);
        }

        // 24. 自定义子状态与扩展转移生效
        [Fact]
        public void CustomSubState_WithExtendedTransition_Works()
        {
            var config = AppStateMachineConfig.Default();
            var puzzle = config.AddCustomSubState("puzzle");
            config.AllowSubTransition(SubStateId.Explore, puzzle);
            config.AllowSubTransition(puzzle, SubStateId.Explore);

            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus(), config);
            Assert.True(host.RequestTransition(AppState.MainMenu));
            Assert.True(host.RequestTransition(AppState.Loading));
            Assert.True(host.RequestTransition(AppState.InWorld));

            Assert.True(host.PushSubState(puzzle));
            Assert.Equal(puzzle, host.CurrentSubState);

            Assert.True(host.PopSubState());
            Assert.Equal(SubStateId.Explore, host.CurrentSubState);
        }

        // 25. 游戏层扩展主状态转移表后生效
        [Fact]
        public void ExtendedMainTransition_AllowedAfterAllowTransition()
        {
            var config = AppStateMachineConfig.Default();
            config.AllowTransition(AppState.Boot, AppState.InWorld);

            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus(), config);

            Assert.True(host.RequestTransition(AppState.InWorld));
        }

        // 26. RequestExit 只在 MainMenu 成立
        [Fact]
        public void RequestExit_SucceedsOnlyInMainMenu()
        {
            var host = new Core.Foundation.AppLifecycle.AppStateHost(AppLifecycleTestSupport.CreateBus());

            Assert.False(host.RequestExit());
            Assert.False(host.IsExitRequested);

            Assert.True(host.RequestTransition(AppState.MainMenu));
            Assert.True(host.RequestExit());
            Assert.True(host.IsExitRequested);
        }

        // 27. RequestExit 在 InWorld 下返回 false
        [Fact]
        public void RequestExit_InWorld_ReturnsFalse()
        {
            var host = CreateHostAt(AppState.InWorld);

            Assert.False(host.RequestExit());
            Assert.False(host.IsExitRequested);
        }

        // 28. OnSubStateChanged 收到新旧子状态
        [Fact]
        public void OnSubStateChanged_ReceivesOldAndNew()
        {
            var host = CreateHostAt(AppState.InWorld);
            SubStateId? oldReceived = null;
            SubStateId? newReceived = null;
            host.OnSubStateChanged((oldSub, newSub) =>
            {
                oldReceived = oldSub;
                newReceived = newSub;
            });

            host.PushSubState(InWorldSubState.Combat);

            Assert.Equal(SubStateId.Explore, oldReceived);
            Assert.Equal(SubStateId.Combat, newReceived);
        }
    }
}

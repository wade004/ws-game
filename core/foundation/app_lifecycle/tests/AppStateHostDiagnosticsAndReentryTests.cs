using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Xunit;

namespace Tests.Foundation.AppLifecycle
{
    /// <summary>
    /// <see cref="AppStateHost"/> 的诊断断言与回调边界（T-M4，2026-10-01 测试覆盖第四批）：
    /// 被拒转移 / <c>PushSubState</c> 非法 / <c>PopSubState</c> 栈底 / <c>RequestExit</c> 的 Warn 诊断内容，
    /// 回调内重入 <c>RequestTransition</c>、回调抛异常后的宿主状态。
    /// 回调异常与重入（收口遗留修复 A7）：订阅回调抛异常与 EventBus 同口径——隔离、记 Error 诊断、
    /// 其余回调照常；回调内重入迁移时状态立即落定，但通知按发生顺序送达（嵌套迁移的通知排在外层通知
    /// 对全部订阅者派发完之后）。
    /// </summary>
    public sealed class AppStateHostDiagnosticsAndReentryTests
    {
        private static (AppStateHost host, InMemoryAppLifecycleDiagnostics diagnostics) Build(AppState at = AppState.Boot)
        {
            var diagnostics = new InMemoryAppLifecycleDiagnostics();
            var host = new AppStateHost(AppLifecycleTestSupport.CreateBus(), null, diagnostics);
            var path = new Dictionary<AppState, AppState[]>
            {
                [AppState.Boot] = new AppState[0],
                [AppState.MainMenu] = new[] { AppState.MainMenu },
                [AppState.Loading] = new[] { AppState.MainMenu, AppState.Loading },
                [AppState.InWorld] = new[] { AppState.MainMenu, AppState.Loading, AppState.InWorld },
                [AppState.Pause] = new[] { AppState.MainMenu, AppState.Loading, AppState.InWorld, AppState.Pause },
            };
            foreach (var step in path[at])
            {
                Assert.True(host.RequestTransition(step));
            }

            return (host, diagnostics);
        }

        // -----------------------------------------------------------------
        // 构造 / 诊断出口
        // -----------------------------------------------------------------

        [Fact]
        public void Constructor_NullBus_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AppStateHost(null!));
        }

        [Fact]
        public void Diagnostics_IsTheInjectedInstance_OrADefaultInMemoryOne()
        {
            var (host, diagnostics) = Build();
            Assert.Same(diagnostics, host.Diagnostics);

            var defaulted = new AppStateHost(AppLifecycleTestSupport.CreateBus());
            Assert.IsType<InMemoryAppLifecycleDiagnostics>(defaulted.Diagnostics);
            Assert.False(defaulted.RequestTransition(AppState.InWorld));
            Assert.Single(((InMemoryAppLifecycleDiagnostics)defaulted.Diagnostics).Warnings);
        }

        [Fact]
        public void LegalOperations_ProduceNoWarnings()
        {
            var (host, diagnostics) = Build(AppState.InWorld);

            Assert.True(host.PushSubState(InWorldSubState.Combat));
            Assert.True(host.PopSubState());
            Assert.True(host.RequestTransition(AppState.Pause));

            Assert.Empty(diagnostics.Warnings);
            Assert.Empty(diagnostics.Errors);
        }

        // -----------------------------------------------------------------
        // Warn 诊断内容
        // -----------------------------------------------------------------

        [Fact]
        public void RejectedMainTransition_WarnsWithFromAndTo_AndDoesNotNotify()
        {
            var (host, diagnostics) = Build(AppState.MainMenu);
            var notified = 0;
            host.OnStateChanged((o, n) => notified++);

            Assert.False(host.RequestTransition(AppState.InWorld));

            Assert.Equal(AppState.MainMenu, host.GetState());
            Assert.Equal(0, notified);
            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains(AppState.MainMenu.ToString(), warning);
            Assert.Contains(AppState.InWorld.ToString(), warning);
        }

        [Fact]
        public void RejectedSelfTransition_IsAlsoWarned()
        {
            var (host, diagnostics) = Build(AppState.MainMenu);

            Assert.False(host.RequestTransition(AppState.MainMenu));

            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void PushSubState_OutsideInWorld_WarnsWithCurrentMainState()
        {
            var (host, diagnostics) = Build(AppState.Pause);

            Assert.False(host.PushSubState(InWorldSubState.Combat));

            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains("PushSubState", warning);
            Assert.Contains(AppState.Pause.ToString(), warning);
        }

        [Fact]
        public void PushSubState_IllegalSubTransition_WarnsWithBothSubStates_AndLeavesStackUnchanged()
        {
            var (host, diagnostics) = Build(AppState.InWorld);
            Assert.True(host.PushSubState(InWorldSubState.Dialog));

            // 默认配置不放行 Dialog → MenuOverlay。
            Assert.False(host.PushSubState(InWorldSubState.MenuOverlay));

            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains(SubStateId.Dialog.Name, warning);
            Assert.Contains(SubStateId.MenuOverlay.Name, warning);
            Assert.Equal(new[] { SubStateId.Explore, SubStateId.Dialog }, host.SubStateStack);
        }

        [Fact]
        public void PushSubState_RejectedAndAccepted_NotifyOnlyForAccepted()
        {
            var (host, _) = Build(AppState.InWorld);
            var changes = new List<(SubStateId? Old, SubStateId? New)>();
            host.OnSubStateChanged((o, n) => changes.Add((o, n)));

            Assert.False(host.PushSubState(SubStateId.Explore)); // Explore → Explore 未登记
            Assert.True(host.PushSubState(InWorldSubState.Combat));

            Assert.Equal(new (SubStateId?, SubStateId?)[] { (SubStateId.Explore, SubStateId.Combat) }, changes);
        }

        [Fact]
        public void PopSubState_OutsideInWorld_WarnsWithCurrentMainState()
        {
            var (host, diagnostics) = Build(AppState.MainMenu);

            Assert.False(host.PopSubState());

            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains("PopSubState", warning);
            Assert.Contains(AppState.MainMenu.ToString(), warning);
        }

        [Fact]
        public void PopSubState_AtStackBottom_WarnsAndKeepsExplore_WithoutNotifying()
        {
            var (host, diagnostics) = Build(AppState.InWorld);
            var notified = 0;
            host.OnSubStateChanged((o, n) => notified++);

            Assert.False(host.PopSubState());

            Assert.Equal(0, notified);
            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains("PopSubState", warning);
            Assert.Equal(new[] { SubStateId.Explore }, host.SubStateStack);
        }

        [Fact]
        public void RequestExit_OutsideMainMenu_WarnsAndDoesNotSetFlag()
        {
            var (host, diagnostics) = Build(AppState.InWorld);

            Assert.False(host.RequestExit());

            Assert.False(host.IsExitRequested);
            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains("RequestExit", warning);
            Assert.Contains(AppState.InWorld.ToString(), warning);
        }

        [Fact]
        public void Subscribe_NullCallbacks_Throw()
        {
            var (host, _) = Build();

            Assert.Throws<ArgumentNullException>(() => host.OnStateChanged(null!));
            Assert.Throws<ArgumentNullException>(() => host.OnSubStateChanged(null!));
        }

        [Fact]
        public void SubStateChangedUnsubscribe_StopsReceiving_AndHandleIsIdempotent()
        {
            var (host, _) = Build(AppState.InWorld);
            var count = 0;
            var handle = host.OnSubStateChanged((o, n) => count++);

            Assert.True(host.PushSubState(InWorldSubState.Combat));
            handle.Dispose();
            handle.Dispose();
            Assert.True(host.PopSubState());

            Assert.Equal(1, count);
        }

        // -----------------------------------------------------------------
        // 回调内重入 RequestTransition（特征化）
        // -----------------------------------------------------------------

        [Fact]
        public void Reentry_TransitionRequestedInsideStateChangedCallback_IsAppliedImmediately()
        {
            var (host, diagnostics) = Build(AppState.Boot);
            var seen = new List<(AppState Old, AppState New)>();
            var reentered = false;

            host.OnStateChanged((o, n) =>
            {
                seen.Add((o, n));
                if (!reentered && n == AppState.MainMenu)
                {
                    reentered = true;
                    Assert.True(host.RequestTransition(AppState.Loading));
                }
            });

            Assert.True(host.RequestTransition(AppState.MainMenu));

            // 嵌套转移立即生效：最终状态是 Loading，外层与内层都被通知，内层先于外层余下的订阅者。
            Assert.Equal(AppState.Loading, host.GetState());
            Assert.Equal(new[]
            {
                (AppState.Boot, AppState.MainMenu),
                (AppState.MainMenu, AppState.Loading),
            }, seen);
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void Reentry_LaterSubscriberOfOuterTransition_ObservesTransitionsInOccurrenceOrder()
        {
            // 收口遗留修复 A7：订阅者 B 排在触发重入的订阅者 A 之后，按发生顺序先收到外层的 Boot→MainMenu，
            // 再收到嵌套的 MainMenu→Loading（此前顺序倒置）。
            var (host, _) = Build(AppState.Boot);
            var seenByB = new List<(AppState Old, AppState New)>();
            var reentered = false;

            host.OnStateChanged((o, n) =>
            {
                if (!reentered)
                {
                    reentered = true;
                    host.RequestTransition(AppState.Loading);
                }
            });
            host.OnStateChanged((o, n) => seenByB.Add((o, n)));

            Assert.True(host.RequestTransition(AppState.MainMenu));

            Assert.Equal(AppState.Loading, host.GetState());
            Assert.Equal(new[]
            {
                (AppState.Boot, AppState.MainMenu),
                (AppState.MainMenu, AppState.Loading),
            }, seenByB);
        }

        [Fact]
        public void Reentry_SubStateChangeInsideSubStateCallback_LaterSubscriberObservesOccurrenceOrder()
        {
            var (host, _) = Build(AppState.InWorld);
            var seenByB = new List<(SubStateId? Old, SubStateId? New)>();
            var reentered = false;

            host.OnSubStateChanged((o, n) =>
            {
                if (!reentered)
                {
                    reentered = true;
                    Assert.True(host.PopSubState());
                }
            });
            host.OnSubStateChanged((o, n) => seenByB.Add((o, n)));

            Assert.True(host.PushSubState(InWorldSubState.Combat));

            Assert.Equal(SubStateId.Explore, host.CurrentSubState);
            Assert.Equal(new (SubStateId?, SubStateId?)[]
            {
                (SubStateId.Explore, SubStateId.Combat),
                (SubStateId.Combat, SubStateId.Explore),
            }, seenByB);
        }

        [Fact]
        public void Reentry_IllegalTransitionInsideCallback_IsRejectedAndWarned_OuterStillCompletes()
        {
            var (host, diagnostics) = Build(AppState.Boot);
            var callbackResult = true;
            host.OnStateChanged((o, n) => callbackResult = host.RequestTransition(AppState.Pause));

            Assert.True(host.RequestTransition(AppState.MainMenu));

            Assert.False(callbackResult);
            Assert.Equal(AppState.MainMenu, host.GetState());
            Assert.Single(diagnostics.Warnings);
        }

        // -----------------------------------------------------------------
        // 回调抛异常（收口遗留修复 A7：与 EventBus 同口径——隔离、记 Error、其余回调照常）
        // -----------------------------------------------------------------

        [Fact]
        public void StateChangedCallbackThrows_IsIsolated_RecordedAsError_LaterSubscribersStillNotified()
        {
            var (host, diagnostics) = Build(AppState.Boot);
            var laterCalled = false;
            host.OnStateChanged((o, n) => throw new InvalidOperationException("boom"));
            host.OnStateChanged((o, n) => laterCalled = true);

            Assert.True(host.RequestTransition(AppState.MainMenu));

            Assert.Equal(AppState.MainMenu, host.GetState());
            Assert.True(laterCalled);
            var error = Assert.Single(diagnostics.Errors);
            Assert.Contains("boom", error.Message);
            Assert.IsType<InvalidOperationException>(error.Exception);
        }

        [Fact]
        public void StateChangedCallbackThrows_HostRemainsUsable_AndThrowingSubscriberCanBeRemoved()
        {
            var (host, diagnostics) = Build(AppState.Boot);
            var handle = host.OnStateChanged((o, n) => throw new InvalidOperationException("boom"));

            Assert.True(host.RequestTransition(AppState.MainMenu));
            handle.Dispose();

            Assert.True(host.RequestTransition(AppState.Loading));
            Assert.Equal(AppState.Loading, host.GetState());
            Assert.Single(diagnostics.Errors); // 订阅者移除后不再产生新的 Error。
        }

        [Fact]
        public void SubStateChangedCallbackThrows_OnPush_IsolatedAndRecorded_StackUpdated()
        {
            var (host, diagnostics) = Build(AppState.InWorld);
            var laterCalled = false;
            host.OnSubStateChanged((o, n) => throw new InvalidOperationException("boom"));
            host.OnSubStateChanged((o, n) => laterCalled = true);

            Assert.True(host.PushSubState(InWorldSubState.Combat));

            Assert.Equal(SubStateId.Combat, host.CurrentSubState);
            Assert.Equal(2, host.SubStateStack.Count);
            Assert.True(laterCalled);
            Assert.Single(diagnostics.Errors);
        }

        [Fact]
        public void SubStateChangedCallbackThrows_OnPop_IsolatedAndRecorded_StackUpdated()
        {
            var (host, diagnostics) = Build(AppState.InWorld);
            Assert.True(host.PushSubState(InWorldSubState.Combat));
            host.OnSubStateChanged((o, n) => throw new InvalidOperationException("boom"));

            Assert.True(host.PopSubState());

            Assert.Equal(SubStateId.Explore, host.CurrentSubState);
            Assert.Single(diagnostics.Errors);
        }

        [Fact]
        public void CallbackThrowsInsideNestedTransition_DoesNotBlockQueuedNotifications()
        {
            var (host, diagnostics) = Build(AppState.Boot);
            var seen = new List<(AppState Old, AppState New)>();
            var reentered = false;
            host.OnStateChanged((o, n) =>
            {
                if (!reentered)
                {
                    reentered = true;
                    host.RequestTransition(AppState.Loading);
                    throw new InvalidOperationException("boom after nested");
                }
            });
            host.OnStateChanged((o, n) => seen.Add((o, n)));

            Assert.True(host.RequestTransition(AppState.MainMenu));

            Assert.Equal(new[]
            {
                (AppState.Boot, AppState.MainMenu),
                (AppState.MainMenu, AppState.Loading),
            }, seen);
            Assert.Single(diagnostics.Errors);
        }

        [Fact]
        public void BusSubscriberThrows_IsIsolatedByBus_TransitionStillCompletesAndCallbacksStillRun()
        {
            // 总线订阅者的异常由 event_bus 隔离并记 Error（与上面"OnStateChanged 回调异常直接传播"形成对照）。
            var catalog = Core.Foundation.EventBus.EventCatalog.FromDefinitions(new[]
            {
                new Core.Foundation.EventBus.EventDefinition(AppEventKeys.StateChanged, "app", new[] { "oldState", "newState" }),
            });
            var busDiagnostics = new Core.Foundation.EventBus.InMemoryEventDiagnostics();
            var bus = new Core.Foundation.EventBus.EventBus(catalog, null, busDiagnostics);
            bus.Subscribe<AppStateChangedEvent>(AppEventKeys.StateChanged,
                _ => throw new InvalidOperationException("boom"));
            var host = new AppStateHost(bus);
            var callbackCalled = false;
            host.OnStateChanged((o, n) => callbackCalled = true);

            Assert.True(host.RequestTransition(AppState.MainMenu));

            Assert.Equal(AppState.MainMenu, host.GetState());
            Assert.True(callbackCalled);
            Assert.Single(busDiagnostics.Errors);
        }
    }
}

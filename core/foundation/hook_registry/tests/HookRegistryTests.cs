using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Xunit;

namespace Tests.Foundation.HookRegistry
{
    public class HookRegistryTests
    {
        private static readonly Id TestHook = new Id("found.hook.test_point");
        private static readonly Id OtherHook = new Id("found.hook.other_point");

        private static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(HookEventKeys.HookInvoked, "hook", new[] { "hookId", "callbackCount" }),
            });

            return new EventBus(catalog);
        }

        // 1. 声明后注册并调用：按 order 升序执行
        [Fact]
        public void Invoke_CallsCallbacksInOrderAscending()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareHookPoint(TestHook, "none");
            var calls = new List<string>();

            registry.Register(TestHook, args => calls.Add("second"), 10);
            registry.Register(TestHook, args => calls.Add("first"), 0);
            registry.Register(TestHook, args => calls.Add("third"), 20);

            registry.Invoke(TestHook, HookArgs.Empty);

            Assert.Equal(new[] { "first", "second", "third" }, calls);
        }

        // 2. 同 order 按注册先后执行
        [Fact]
        public void Invoke_SameOrder_CallsInRegistrationOrder()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareHookPoint(TestHook, "none");
            var calls = new List<string>();

            registry.Register(TestHook, args => calls.Add("a"), 5);
            registry.Register(TestHook, args => calls.Add("b"), 5);
            registry.Register(TestHook, args => calls.Add("c"), 5);

            registry.Invoke(TestHook, HookArgs.Empty);

            Assert.Equal(new[] { "a", "b", "c" }, calls);
        }

        // 3. 未声明挂载点时 Register 抛异常
        [Fact]
        public void Register_UndeclaredHookPoint_Throws()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();

            Assert.Throws<InvalidOperationException>(() => registry.Register(TestHook, args => { }, 0));
        }

        // 4. 未声明挂载点时 Invoke 抛异常
        [Fact]
        public void Invoke_UndeclaredHookPoint_Throws()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();

            Assert.Throws<InvalidOperationException>(() => registry.Invoke(TestHook, HookArgs.Empty));
        }

        // 5. AllowMultiple=false 时第二次注册抛异常
        [Fact]
        public void Register_AllowMultipleFalse_SecondRegistration_Throws()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareHookPoint(new HookPointDefinition(TestHook, "none", allowMultiple: false));

            registry.Register(TestHook, args => { }, 0);

            Assert.Throws<InvalidOperationException>(() => registry.Register(TestHook, args => { }, 0));
        }

        // 6. 声明重复挂载点抛异常
        [Fact]
        public void DeclareHookPoint_Duplicate_Throws()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareHookPoint(TestHook, "none");

            Assert.Throws<InvalidOperationException>(() => registry.DeclareHookPoint(TestHook, "none"));
        }

        // 7. 取消订阅：Dispose 后不再被调用，CallbackCount 减少
        [Fact]
        public void Unregister_ViaDispose_StopsBeingCalled()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareHookPoint(TestHook, "none");
            var calls = 0;

            var handle = registry.Register(TestHook, args => calls++, 0);
            Assert.Equal(1, registry.CallbackCount(TestHook));

            handle.Dispose();
            Assert.Equal(0, registry.CallbackCount(TestHook));

            registry.Invoke(TestHook, HookArgs.Empty);
            Assert.Equal(0, calls);
        }

        // 8. 回调异常被隔离：其余回调继续执行，异常记入诊断，不向调用方抛出
        [Fact]
        public void Invoke_CallbackThrows_IsolatedAndRecordedInDiagnostics()
        {
            var diagnostics = new InMemoryHookDiagnostics();
            var registry = new Core.Foundation.HookRegistry.HookRegistry(diagnostics: diagnostics);
            registry.DeclareHookPoint(TestHook, "none");
            var secondCalled = false;

            registry.Register(TestHook, args => throw new InvalidOperationException("boom"), 0);
            registry.Register(TestHook, args => secondCalled = true, 1);

            var exception = Record.Exception(() => registry.Invoke(TestHook, HookArgs.Empty));

            Assert.Null(exception);
            Assert.True(secondCalled);
            Assert.Single(diagnostics.Errors);
        }

        // 9. 参数传递：回调经 HookArgs.Get<T> 取到调用方传入的值
        [Fact]
        public void Invoke_PassesArgsToCallback()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareHookPoint(TestHook, "sceneId: Id");
            string? received = null;

            registry.Register(TestHook, args => received = args.Get<string>("sceneId"), 0);

            var args = new HookArgs(new Dictionary<string, object?> { ["sceneId"] = "world.map.town" });
            registry.Invoke(TestHook, args);

            Assert.Equal("world.map.town", received);
        }

        // 10. EmitInvokedEvent=false（默认）时不发事件
        [Fact]
        public void Invoke_EmitInvokedEventDisabled_DoesNotPublish()
        {
            var bus = CreateBus();
            var published = new List<Id>();
            bus.Subscribe(HookEventKeys.HookInvoked, evt => published.Add(evt.Key));

            var registry = new Core.Foundation.HookRegistry.HookRegistry(bus);
            registry.DeclareHookPoint(TestHook, "none");
            registry.Invoke(TestHook, HookArgs.Empty);

            Assert.Empty(published);
        }

        // 11. EmitInvokedEvent=true 时每次 Invoke 后发出 hook.invoked，字段正确
        [Fact]
        public void Invoke_EmitInvokedEventEnabled_PublishesWithCallbackCount()
        {
            var bus = CreateBus();
            HookInvokedEvent? received = null;
            bus.Subscribe<HookInvokedEvent>(HookEventKeys.HookInvoked, evt => received = evt);

            var registry = new Core.Foundation.HookRegistry.HookRegistry(bus, new HookRegistryOptions { EmitInvokedEvent = true });
            registry.DeclareHookPoint(TestHook, "none");
            registry.Register(TestHook, args => { }, 0);
            registry.Register(TestHook, args => { }, 1);

            registry.Invoke(TestHook, HookArgs.Empty);

            Assert.NotNull(received);
            Assert.Equal(TestHook, received!.HookId);
            Assert.Equal(2, received.CallbackCount);
        }

        // 12. EmitInvokedEvent=true 但未传 bus 时构造期抛异常
        [Fact]
        public void Constructor_EmitInvokedEventEnabledWithoutBus_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new Core.Foundation.HookRegistry.HookRegistry(options: new HookRegistryOptions { EmitInvokedEvent = true }));
        }

        // 13. DeclareFromDefinitions 批量声明；重复 id 抛异常
        [Fact]
        public void DeclareFromDefinitions_DuplicateId_Throws()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            var definitions = new[]
            {
                new HookPointDefinition(TestHook, "none"),
                new HookPointDefinition(OtherHook, "none"),
                new HookPointDefinition(TestHook, "none"),
            };

            Assert.Throws<InvalidOperationException>(() => registry.DeclareFromDefinitions(definitions));
        }

        // 14. DeclareFromDefinitions 正常路径：全部挂载点可用
        [Fact]
        public void DeclareFromDefinitions_RegistersAllHookPoints()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareFromDefinitions(new[]
            {
                new HookPointDefinition(TestHook, "none"),
                new HookPointDefinition(OtherHook, "none"),
            });

            Assert.Equal(2, registry.HookPoints.Count);
            Assert.Equal(0, registry.CallbackCount(TestHook));
            Assert.Equal(0, registry.CallbackCount(OtherHook));
        }

        // 15. 未声明挂载点的 CallbackCount 返回 0，不抛异常
        [Fact]
        public void CallbackCount_UndeclaredHook_ReturnsZero()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();

            Assert.Equal(0, registry.CallbackCount(TestHook));
        }

        // 16. Invoke 无回调时空操作，不抛异常
        [Fact]
        public void Invoke_NoCallbacks_NoOp()
        {
            var registry = new Core.Foundation.HookRegistry.HookRegistry();
            registry.DeclareHookPoint(TestHook, "none");

            var exception = Record.Exception(() => registry.Invoke(TestHook, HookArgs.Empty));

            Assert.Null(exception);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// P4 备忘 8（样板游戏 A 反馈）：输入上下文栈。模态界面打开时压入一层上下文，栈顶放行表之外的动作视为未激活
    /// （按钮不激活、不触发、不产生边沿；轴输出零），弹出后恢复；弹出时仍按住的键不补"按下"边沿。红：此前接口上没有该入口。
    /// </summary>
    public sealed class InputContextStackTests
    {
        private const string Move = "input.action.move";
        private const string Attack = "input.action.attack";
        private const string Confirm = "input.action.confirm";
        private static readonly Id Modal = new Id("input.context.modal");
        private static readonly Id Inner = new Id("input.context.inner");

        private sealed class RecordingSink : IInputEdgeSink
        {
            public readonly List<string> Edges = new List<string>();

            public void OnButtonEdge(string actionName, bool isDown) => Edges.Add(actionName + (isDown ? "+" : "-"));
        }

        private static (InputMapHost Host, IEventBus Bus, StubInput Input, List<string> Triggered) Build()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(InputMapEventKeys.ActionTriggered, "input", new[] { "actionName" }),
                new EventDefinition(InputMapEventKeys.RebindConflict, "input", new[] { "actionName", "binding" }),
            });
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
            var host = new InputMapHost(bus);
            host.DeclareActionSet(new Id("input.set.test"), new[]
            {
                new ActionDefinition(new Id(Move), ActionKind.Axis2D, new[] { "composite2d:key:w|key:s|key:a|key:d" }),
                new ActionDefinition(new Id(Attack), ActionKind.Button, new[] { "key:space" }),
                new ActionDefinition(new Id(Confirm), ActionKind.Button, new[] { "key:enter" }),
            });
            var triggered = new List<string>();
            bus.Subscribe<InputActionTriggeredEvent>(InputMapEventKeys.ActionTriggered, e => triggered.Add(e.ActionName));
            return (host, bus, new StubInput(), triggered);
        }

        [Fact]
        public void BlockedActions_AreInactive_NoTrigger_AxisZero_AllowedStillWork()
        {
            var (host, bus, input, triggered) = Build();
            host.PushInputContext(Modal, new[] { Confirm });
            Assert.Equal(Modal, host.ActiveInputContext);

            input.Press("space");
            input.Press("d");
            input.Press("enter");
            host.Update(input);
            bus.DispatchPending();

            Assert.False(host.IsActionActive(Attack));
            Assert.Equal(0.0, host.GetActionAxis(Move).Length);
            Assert.True(host.IsActionActive(Confirm));
            Assert.Equal(new[] { Confirm }, triggered);
        }

        [Fact]
        public void PopContext_RestoresActions_HeldKeysBecomeActive_WithoutNewTrigger()
        {
            var (host, bus, input, triggered) = Build();
            host.PushInputContext(Modal, new string[0]);
            input.Press("space");
            input.Press("d");
            host.Update(input);
            bus.DispatchPending();
            Assert.Empty(triggered);

            Assert.True(host.PopInputContext(Modal));
            Assert.Null(host.ActiveInputContext);
            host.Update(input);
            bus.DispatchPending();

            Assert.True(host.IsActionActive(Attack));
            Assert.True(host.GetActionAxis(Move).X > 0);
            Assert.Empty(triggered);

            // 之后新按下仍照常触发
            input.Release("space");
            host.Update(input);
            input.Press("space");
            host.Update(input);
            bus.DispatchPending();
            Assert.Equal(new[] { Attack }, triggered);
        }

        [Fact]
        public void Stack_TopContextIsExclusive_PopOutOfOrderIsAllowed()
        {
            var (host, bus, input, _) = Build();
            host.PushInputContext(Modal, new[] { Confirm });
            host.PushInputContext(Inner, new[] { Attack });
            Assert.Equal(Inner, host.ActiveInputContext);

            input.Press("space");
            input.Press("enter");
            host.Update(input);
            bus.DispatchPending();
            Assert.True(host.IsActionActive(Attack));
            Assert.False(host.IsActionActive(Confirm));

            Assert.True(host.PopInputContext(Modal));
            Assert.Equal(Inner, host.ActiveInputContext);
            Assert.True(host.PopInputContext(Inner));
            Assert.False(host.PopInputContext(Inner));
            Assert.Null(host.ActiveInputContext);
        }

        [Fact]
        public void PushSameId_ReplacesAllowList_WithoutChangingDepth()
        {
            var (host, bus, input, _) = Build();
            host.PushInputContext(Modal, new[] { Confirm });
            host.PushInputContext(Modal, new[] { Attack });

            input.Press("space");
            input.Press("enter");
            host.Update(input);
            bus.DispatchPending();

            Assert.True(host.IsActionActive(Attack));
            Assert.False(host.IsActionActive(Confirm));
            Assert.True(host.PopInputContext(Modal));
            Assert.Null(host.ActiveInputContext);
        }

        [Fact]
        public void EdgeSink_PushWhileHeld_EmitsRelease_PopWhileHeld_EmitsNoPress()
        {
            var (host, bus, input, _) = Build();
            var sink = new RecordingSink();
            host.SetEdgeSink(sink);
            input.Press("space");
            host.Update(input);
            Assert.Equal(new[] { Attack + "+" }, sink.Edges);

            host.PushInputContext(Modal, new string[0]);
            host.Update(input);
            Assert.Equal(new[] { Attack + "+", Attack + "-" }, sink.Edges);

            host.PopInputContext(Modal);
            host.Update(input);
            Assert.Equal(new[] { Attack + "+", Attack + "-" }, sink.Edges);
            Assert.True(host.IsActionActive(Attack));

            input.Release("space");
            host.Update(input);
            Assert.Equal(Attack + "-", sink.Edges.Last());
        }

        [Fact]
        public void UnknownActionInAllowList_IsIgnored_WithDiagnostic()
        {
            var (host, _, input, _) = Build();
            host.PushInputContext(Modal, new[] { "input.action.nope" });

            Assert.NotEmpty(((InMemoryInputMapDiagnostics)host.Diagnostics).Warnings);
            input.Press("enter");
            host.Update(input);
            Assert.False(host.IsActionActive(Confirm));
        }

        [Fact]
        public void NoContext_BehaviourUnchanged()
        {
            var (host, bus, input, triggered) = Build();
            input.Press("space");
            host.Update(input);
            bus.DispatchPending();

            Assert.True(host.IsActionActive(Attack));
            Assert.Equal(new[] { Attack }, triggered);
        }
    }
}

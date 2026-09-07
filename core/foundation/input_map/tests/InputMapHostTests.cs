using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Foundation.InputMap
{
    public class InputMapHostTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(InputMapEventKeys.ActionTriggered, "input", new[] { "actionName" }),
                new EventDefinition(InputMapEventKeys.RebindConflict, "input", new[] { "actionName", "binding" }),
            });
            return new EventBus(catalog);
        }

        private static ActionDefinition Button(string id, string binding, string group = "default") =>
            new ActionDefinition(new Id(id), ActionKind.Button, new[] { binding }, group);

        // -----------------------------------------------------------------
        // 1. DeclareActionSet
        // -----------------------------------------------------------------

        [Fact]
        public void DeclareActionSet_DuplicateActionSetId_Throws()
        {
            var host = new InputMapHost(MakeBus());
            var setId = new Id("input.set.gameplay");
            host.DeclareActionSet(setId, new[] { Button("input.action.confirm", "key:enter") });

            Assert.Throws<InvalidOperationException>(() =>
                host.DeclareActionSet(setId, new[] { Button("input.action.cancel", "key:escape") }));
        }

        [Fact]
        public void DeclareActionSet_DuplicateActionNameAcrossSets_Throws()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.a"), new[] { Button("input.action.confirm", "key:enter") });

            Assert.Throws<InvalidOperationException>(() =>
                host.DeclareActionSet(new Id("input.set.b"), new[] { Button("input.action.confirm", "key:space") }));
        }

        // -----------------------------------------------------------------
        // 2. 按钮：按下沿事件恰好一次 + IsActionActive
        // -----------------------------------------------------------------

        [Fact]
        public void Update_ButtonPressAndRelease_TogglesActiveAndFiresTriggeredExactlyOncePerPress()
        {
            var bus = MakeBus();
            var host = new InputMapHost(bus);
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.confirm", "key:enter") });
            var stub = new StubInput();
            var triggerCount = 0;
            bus.Subscribe<InputActionTriggeredEvent>(InputMapEventKeys.ActionTriggered, e =>
            {
                if (e.ActionName == "input.action.confirm") triggerCount++;
            });

            host.Update(stub);
            bus.DispatchPending();
            Assert.False(host.IsActionActive("input.action.confirm"));
            Assert.Equal(0, triggerCount);

            stub.Press("enter");
            host.Update(stub);
            bus.DispatchPending();
            Assert.True(host.IsActionActive("input.action.confirm"));
            Assert.Equal(1, triggerCount);

            // 持续按住，不应重复触发
            host.Update(stub);
            bus.DispatchPending();
            Assert.Equal(1, triggerCount);

            stub.Release("enter");
            host.Update(stub);
            bus.DispatchPending();
            Assert.False(host.IsActionActive("input.action.confirm"));
            Assert.Equal(1, triggerCount);

            stub.Press("enter");
            host.Update(stub);
            bus.DispatchPending();
            Assert.Equal(2, triggerCount);
        }

        /// <summary>FND-08 收口回归（外部审核 code-review.md，验证复现 validation-boundaries.md
        /// FND08）：同一批 <c>PollEvents()</c> 内同一个键先 <c>KeyDown</c> 后 <c>KeyUp</c>（点击类
        /// 交互在一次 <see cref="InputMapHost.Update"/> 调用内就完成按下与释放，例如轮询间隔较大或
        /// 玩家操作很快时很常见）此前会被"批次末持有集合净效果"判定为"没有变化"，
        /// <see cref="InputActionTriggeredEvent"/> 完全不触发；本用例验证同帧 down+up 仍会触发
        /// 恰好一次，随后正确回到未按住状态，且不影响跨帧的正常按下/持续按住/释放行为。</summary>
        [Fact]
        public void Update_KeyDownAndUpInSameBatch_StillFiresTriggeredOnce_AndEndsNotActive()
        {
            var bus = MakeBus();
            var host = new InputMapHost(bus);
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.q", "key:q") });
            var stub = new StubInput();
            var triggerCount = 0;
            bus.Subscribe<InputActionTriggeredEvent>(InputMapEventKeys.ActionTriggered, e =>
            {
                if (e.ActionName == "input.action.q") triggerCount++;
            });

            // 同一批：down 紧接着 up，两个事件在同一次 PollEvents() 里一起交给 Update。
            stub.Press("q");
            stub.Release("q");
            host.Update(stub);
            bus.DispatchPending();

            Assert.Equal(1, triggerCount); // 按下边沿仍应被计入一次，不因同帧内又释放而丢失。
            Assert.False(host.IsActionActive("input.action.q")); // 批次末净效果是"未按住"，如实反映。

            // 跨帧行为不受影响：下一批没有任何事件，不应凭空再触发一次。
            host.Update(stub);
            bus.DispatchPending();
            Assert.Equal(1, triggerCount);

            // 正常的跨帧 down → held → up 仍然只在真正按下的那一帧触发一次。
            stub.Press("q");
            host.Update(stub);
            bus.DispatchPending();
            Assert.Equal(2, triggerCount);
            Assert.True(host.IsActionActive("input.action.q"));

            host.Update(stub); // 持续按住，不应重复触发。
            bus.DispatchPending();
            Assert.Equal(2, triggerCount);

            stub.Release("q");
            host.Update(stub);
            bus.DispatchPending();
            Assert.Equal(2, triggerCount);
            Assert.False(host.IsActionActive("input.action.q"));
        }

        [Fact]
        public void Update_MouseAndPadButtonBindings_ActivateAction()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                new ActionDefinition(new Id("input.action.interact"), ActionKind.Button, new[] { "mouse:left", "pad:x" }),
            });
            var stub = new StubInput();

            stub.PressMouseButton("left");
            host.Update(stub);
            Assert.True(host.IsActionActive("input.action.interact"));

            stub.ReleaseMouseButton("left");
            host.Update(stub);
            Assert.False(host.IsActionActive("input.action.interact"));

            stub.PressGamepadButton(0, "x");
            host.Update(stub);
            Assert.True(host.IsActionActive("input.action.interact"));
        }

        // -----------------------------------------------------------------
        // 3. composite2d 四向 + 对角归一化
        // -----------------------------------------------------------------

        [Fact]
        public void Update_Composite2D_FourDirections_ProduceUnitVectors()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                new ActionDefinition(new Id("input.action.move"), ActionKind.Axis2D,
                    new[] { "composite2d:key:w|key:s|key:a|key:d" }),
            });
            var stub = new StubInput();

            stub.Press("d");
            host.Update(stub);
            Assert.Equal(new Vec2(1, 0), host.GetActionAxis("input.action.move"));

            stub.Release("d");
            stub.Press("a");
            host.Update(stub);
            Assert.Equal(new Vec2(-1, 0), host.GetActionAxis("input.action.move"));

            stub.Release("a");
            stub.Press("w");
            host.Update(stub);
            Assert.Equal(new Vec2(0, 1), host.GetActionAxis("input.action.move"));

            stub.Release("w");
            stub.Press("s");
            host.Update(stub);
            Assert.Equal(new Vec2(0, -1), host.GetActionAxis("input.action.move"));
        }

        [Fact]
        public void Update_Composite2D_DiagonalPress_NormalizedToUnitLength()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                new ActionDefinition(new Id("input.action.move"), ActionKind.Axis2D,
                    new[] { "composite2d:key:w|key:s|key:a|key:d" }),
            });
            var stub = new StubInput();

            stub.Press("w");
            stub.Press("d");
            host.Update(stub);

            var axis = host.GetActionAxis("input.action.move");
            Assert.True(Math.Abs(axis.Length - 1.0) < 1e-9);
            Assert.True(axis.X > 0 && axis.Y > 0);
            Assert.True(Math.Abs(axis.X - axis.Y) < 1e-9);
        }

        [Fact]
        public void Update_Composite2D_NoInput_ReturnsZero()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                new ActionDefinition(new Id("input.action.move"), ActionKind.Axis2D,
                    new[] { "composite2d:key:w|key:s|key:a|key:d" }),
            });
            var stub = new StubInput();

            host.Update(stub);
            Assert.Equal(Vec2.Zero, host.GetActionAxis("input.action.move"));
        }

        // -----------------------------------------------------------------
        // 4. pad_axis / pad_stick 透传
        // -----------------------------------------------------------------

        [Fact]
        public void Update_PadAxis1D_PassesThroughGamepadAxisValue()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                new ActionDefinition(new Id("input.action.camera_adjust"), ActionKind.Axis1D, new[] { "pad_axis:zoom" }),
            });
            var stub = new StubInput();
            stub.SetAxis(0, "zoom", 0.75);

            host.Update(stub);

            var axis = host.GetActionAxis("input.action.camera_adjust");
            Assert.Equal(0.75, axis.X, 9);
            Assert.Equal(0, axis.Y, 9);
        }

        [Fact]
        public void Update_PadStick2D_PassesThroughXYAxisValues()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                new ActionDefinition(new Id("input.action.look"), ActionKind.Axis2D, new[] { "pad_stick:left" }),
            });
            var stub = new StubInput();
            stub.SetAxis(0, "leftx", 0.5);
            stub.SetAxis(0, "lefty", -0.25);

            host.Update(stub);

            Assert.Equal(new Vec2(0.5, -0.25), host.GetActionAxis("input.action.look"));
        }

        // -----------------------------------------------------------------
        // 5. IsActionActive / GetActionAxis 用错类型
        // -----------------------------------------------------------------

        [Fact]
        public void IsActionActive_OnAxisAction_Throws()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                new ActionDefinition(new Id("input.action.camera_adjust"), ActionKind.Axis1D, new[] { "pad_axis:zoom" }),
            });

            Assert.Throws<InvalidOperationException>(() => host.IsActionActive("input.action.camera_adjust"));
        }

        [Fact]
        public void GetActionAxis_OnButtonAction_Throws()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.confirm", "key:enter") });

            Assert.Throws<InvalidOperationException>(() => host.GetActionAxis("input.action.confirm"));
        }

        // -----------------------------------------------------------------
        // 6. Rebind 成功 / 冲突（同组 / 跨组）+ 事件
        // -----------------------------------------------------------------

        [Fact]
        public void Rebind_NoConflict_ReplacesBindingAndReturnsTrue()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.confirm", "key:enter") });

            var result = host.Rebind("input.action.confirm", "key:space");

            Assert.True(result);
            Assert.Equal(new[] { "key:space" }, host.GetBindings("input.action.confirm"));
        }

        [Fact]
        public void Rebind_SameGroupConflict_ReturnsFalseAndPublishesConflictEvent()
        {
            var bus = MakeBus();
            var host = new InputMapHost(bus);
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                Button("input.action.confirm", "key:enter", "default"),
                Button("input.action.cancel", "key:escape", "default"),
            });
            InputRebindConflictEvent? received = null;
            bus.Subscribe<InputRebindConflictEvent>(InputMapEventKeys.RebindConflict, e => received = e);

            var result = host.Rebind("input.action.cancel", "key:enter");

            Assert.False(result);
            Assert.Equal(new[] { "key:escape" }, host.GetBindings("input.action.cancel")); // 未被替换
            Assert.NotNull(received);
            Assert.Equal("input.action.cancel", received!.ActionName);
            Assert.Equal("key:enter", received.Binding);
        }

        [Fact]
        public void Rebind_CrossGroup_SameBindingAllowed_NoConflict()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                Button("input.action.confirm", "key:enter", "gameplay"),
                Button("input.action.menu_confirm", "key:space", "menu"),
            });

            var result = host.Rebind("input.action.menu_confirm", "key:enter");

            Assert.True(result);
            Assert.Equal(new[] { "key:enter" }, host.GetBindings("input.action.menu_confirm"));
        }

        [Fact]
        public void Rebind_InvalidBindingFormat_ThrowsArgumentException()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.confirm", "key:enter") });

            Assert.Throws<ArgumentException>(() => host.Rebind("input.action.confirm", "not-a-binding"));
        }

        [Fact]
        public void Rebind_UnknownActionName_Throws()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.confirm", "key:enter") });

            Assert.Throws<InvalidOperationException>(() => host.Rebind("input.action.ghost", "key:space"));
        }

        // -----------------------------------------------------------------
        // 7. GetConflicts
        // -----------------------------------------------------------------

        [Fact]
        public void GetConflicts_ReturnsAllActionsUsingBindingAcrossGroups()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                Button("input.action.confirm", "key:enter", "gameplay"),
                Button("input.action.menu_confirm", "key:space", "menu"),
            });
            host.Rebind("input.action.menu_confirm", "key:enter");

            var conflicts = host.GetConflicts("key:enter");

            Assert.Equal(2, conflicts.Count);
            Assert.Contains("input.action.confirm", conflicts);
            Assert.Contains("input.action.menu_confirm", conflicts);
        }

        // -----------------------------------------------------------------
        // 8. ExportBindings / ImportBindings 往返 + ResetBindings
        // -----------------------------------------------------------------

        [Fact]
        public void ExportBindings_OnlyIncludesChangedActions()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[]
            {
                Button("input.action.confirm", "key:enter"),
                Button("input.action.cancel", "key:escape"),
            });

            host.Rebind("input.action.confirm", "key:space");
            var export = host.ExportBindings();

            Assert.True(export.ContainsKey("input.action.confirm"));
            Assert.False(export.ContainsKey("input.action.cancel"));
        }

        [Fact]
        public void ExportImport_RoundTrip_RestoresChangedBindingOnFreshHost()
        {
            var host1 = new InputMapHost(MakeBus());
            var actions = new[]
            {
                Button("input.action.confirm", "key:enter"),
                Button("input.action.cancel", "key:escape"),
            };
            host1.DeclareActionSet(new Id("input.set.gameplay"), actions);
            host1.Rebind("input.action.confirm", "key:space");
            var exported = host1.ExportBindings();

            var host2 = new InputMapHost(MakeBus());
            host2.DeclareActionSet(new Id("input.set.gameplay"), actions);
            host2.ImportBindings(exported);

            Assert.Equal(new[] { "key:space" }, host2.GetBindings("input.action.confirm"));
            Assert.Equal(new[] { "key:escape" }, host2.GetBindings("input.action.cancel")); // 未导出，保持默认
        }

        [Fact]
        public void ImportBindings_UnknownAction_Throws()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.confirm", "key:enter") });

            var builder = new JsonObjectBuilder();
            builder.Add("input.action.ghost", new JsonArray(new JsonValue[] { new JsonString("key:space") }));
            var bad = builder.Build();

            Assert.Throws<InvalidOperationException>(() => host.ImportBindings(bad));
        }

        [Fact]
        public void ResetBindings_RestoresDefaultAndClearsDirtyFlag()
        {
            var host = new InputMapHost(MakeBus());
            host.DeclareActionSet(new Id("input.set.gameplay"), new[] { Button("input.action.confirm", "key:enter") });
            host.Rebind("input.action.confirm", "key:space");

            host.ResetBindings("input.action.confirm");

            Assert.Equal(new[] { "key:enter" }, host.GetBindings("input.action.confirm"));
            Assert.False(host.ExportBindings().ContainsKey("input.action.confirm"));
        }

        // -----------------------------------------------------------------
        // 9. FromRecord：从内存数据表加载示例动作集 7 条
        // -----------------------------------------------------------------

        private const string SampleInputActionRows = @"[
            { ""key"": ""input.action.move"", ""kind"": ""axis2d"", ""default_bindings"": [""composite2d:key:w|key:s|key:a|key:d""], ""rebind_group"": ""default"" },
            { ""key"": ""input.action.confirm"", ""kind"": ""button"", ""default_bindings"": [""key:enter"", ""pad:a""], ""rebind_group"": ""default"" },
            { ""key"": ""input.action.cancel"", ""kind"": ""button"", ""default_bindings"": [""key:escape"", ""pad:b""], ""rebind_group"": ""default"" },
            { ""key"": ""input.action.interact"", ""kind"": ""button"", ""default_bindings"": [""key:e"", ""pad:x""], ""rebind_group"": ""default"" },
            { ""key"": ""input.action.open_menu"", ""kind"": ""button"", ""default_bindings"": [""key:tab"", ""pad:y""], ""rebind_group"": ""default"" },
            { ""key"": ""input.action.pause"", ""kind"": ""button"", ""default_bindings"": [""key:p"", ""pad:start""], ""rebind_group"": ""default"" },
            { ""key"": ""input.action.camera_adjust"", ""kind"": ""axis1d"", ""default_bindings"": [""pad_axis:zoom""], ""rebind_group"": ""default"" }
        ]";

        [Fact]
        public void FromRecord_LoadsSevenSampleActionsFromInMemoryTable()
        {
            var envelope = "{\"table\": \"found.input_action\", \"schema_version\": 1, \"rows\": " + SampleInputActionRows + "}";
            var source = new InMemoryDataSource().Add("found.input_action", envelope);
            var bus = new EventBus(EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            }));
            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(InputActionSchema.Table);

            var report = registry.LoadAll();
            Assert.Equal(0, report.ErrorCount);

            var records = registry.GetAll("found.input_action");
            Assert.Equal(7, records.Count);

            var definitions = new List<ActionDefinition>();
            foreach (var record in records)
            {
                definitions.Add(ActionDefinition.FromRecord(record));
            }

            var move = definitions.Find(d => d.ActionId.Value == "input.action.move");
            Assert.NotNull(move);
            Assert.Equal(ActionKind.Axis2D, move!.Kind);
            Assert.Equal("composite2d:key:w|key:s|key:a|key:d", move.DefaultBindings[0]);

            var confirm = definitions.Find(d => d.ActionId.Value == "input.action.confirm");
            Assert.NotNull(confirm);
            Assert.Equal(ActionKind.Button, confirm!.Kind);
            Assert.Equal(2, confirm.DefaultBindings.Count);
        }
    }
}

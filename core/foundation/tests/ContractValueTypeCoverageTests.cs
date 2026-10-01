using System.Collections.Generic;
using System.Globalization;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Foundation
{
    /// <summary>
    /// 契约值类型 / 选项类的直接用例（T-L8 foundation 半，2026-10-01 测试覆盖第四批）：
    /// <c>Rect</c>、<c>SaveResult</c>/<c>SaveSlotInfo</c>、<c>InputMapOptions.GamepadIndex</c>、
    /// （<c>SceneRouter</c> 失败后重载见 scene_router/tests，<c>ProgressionOptions</c> 各字段在 Tests.Numbers。）
    /// </summary>
    public sealed class ContractValueTypeCoverageTests
    {
        // -----------------------------------------------------------------
        // Rect
        // -----------------------------------------------------------------

        [Fact]
        public void Rect_ExposesMinMax_AndEqualityIsComponentWise()
        {
            var a = new Rect(new Vec2(0, 1), new Vec2(2, 3));
            var b = new Rect(new Vec2(0, 1), new Vec2(2, 3));

            Assert.Equal(new Vec2(0, 1), a.Min);
            Assert.Equal(new Vec2(2, 3), a.Max);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Rect_DifferentMinOrMax_IsNotEqual()
        {
            var a = new Rect(new Vec2(0, 1), new Vec2(2, 3));

            Assert.NotEqual(a, new Rect(new Vec2(0, 0), new Vec2(2, 3)));
            Assert.NotEqual(a, new Rect(new Vec2(0, 1), new Vec2(2, 4)));
            Assert.False(a.Equals("rect"));
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void Rect_HashCode_DistinguishesSwappedCorners()
        {
            var a = new Rect(new Vec2(0, 1), new Vec2(2, 3));
            var swapped = new Rect(new Vec2(2, 3), new Vec2(0, 1));

            Assert.NotEqual(a, swapped);
            Assert.NotEqual(a.GetHashCode(), swapped.GetHashCode());
        }

        [Fact]
        public void Rect_DoesNotNormalizeCorners_MinMayExceedMax()
        {
            // 构造不做规范化 / 校验：调用方负责传入 Min <= Max（特征化，值类型保持"纯数据"语义）。
            var inverted = new Rect(new Vec2(5, 5), new Vec2(1, 1));

            Assert.Equal(new Vec2(5, 5), inverted.Min);
            Assert.Equal(new Vec2(1, 1), inverted.Max);
        }

        [Fact]
        public void Rect_ToString_IsCultureInvariant()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var text = new Rect(new Vec2(0.5, 1.5), new Vec2(2.5, 3.5)).ToString();

                Assert.Equal("[(0.5, 1.5) .. (2.5, 3.5)]", text);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        // -----------------------------------------------------------------
        // SaveResult / SaveSlotInfo
        // -----------------------------------------------------------------

        [Fact]
        public void SaveResult_Ok_HasNoReasonOrMessage()
        {
            var ok = SaveResult.Ok();

            Assert.True(ok.Success);
            Assert.Null(ok.Reason);
            Assert.Null(ok.Message);
        }

        [Theory]
        [InlineData(SaveFailureReason.WriteFailed)]
        [InlineData(SaveFailureReason.SlotLimitReached)]
        [InlineData(SaveFailureReason.PersistableThrew)]
        public void SaveResult_Fail_CarriesReasonAndMessage(SaveFailureReason reason)
        {
            var fail = SaveResult.Fail(reason, "详情");

            Assert.False(fail.Success);
            Assert.Equal(reason, fail.Reason);
            Assert.Equal("详情", fail.Message);
        }

        [Fact]
        public void SaveResult_FailureReasonEnum_HasExactlyTheDocumentedMembersInOrder()
        {
            Assert.Equal(
                new[] { SaveFailureReason.WriteFailed, SaveFailureReason.SlotLimitReached, SaveFailureReason.PersistableThrew },
                (SaveFailureReason[])System.Enum.GetValues(typeof(SaveFailureReason)));
        }

        [Fact]
        public void SaveResult_OkAndFail_ReturnIndependentInstances()
        {
            Assert.NotSame(SaveResult.Ok(), SaveResult.Ok());
        }

        [Fact]
        public void SaveSlotInfo_CarriesSlotIdMetaAndPath_Verbatim()
        {
            var slot = new Id("save.slot_1");
            var meta = new SaveMeta(1, slot, "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", 90, null, new Id("game.sample"), null);

            var info = new SaveSlotInfo(slot, meta, "saves/slot_1.json");

            Assert.Equal(slot, info.SlotId);
            Assert.Same(meta, info.Meta);
            Assert.Equal("saves/slot_1.json", info.Path);
        }

        [Fact]
        public void SaveMeta_NullStringsAndSummary_NormalizeToEmpty_OptionalsStayNullable()
        {
            var meta = new SaveMeta(2, new Id("save.slot_2"), null!, null!, null, null, new Id("game.sample"), null);

            Assert.Equal(string.Empty, meta.CreatedAt);
            Assert.Equal(string.Empty, meta.UpdatedAt);
            Assert.NotNull(meta.DisplaySummary);
            Assert.Empty(meta.DisplaySummary);
            Assert.Null(meta.PlayTimeSeconds);
            Assert.Null(meta.DifficultyId);
        }

        // -----------------------------------------------------------------
        // InputMapOptions.GamepadIndex
        // -----------------------------------------------------------------

        private static IEventBus InputBus() => new EventBus(EventCatalog.FromDefinitions(new[]
        {
            new EventDefinition(InputMapEventKeys.ActionTriggered, "input", new[] { "actionName" }),
            new EventDefinition(InputMapEventKeys.RebindConflict, "input", new[] { "actionName", "binding" }),
        }));

        [Fact]
        public void InputMapOptions_GamepadIndex_DefaultsToZero()
        {
            Assert.Equal(0, new InputMapOptions().GamepadIndex);
        }

        [Fact]
        public void GamepadIndex_One_IgnoresButtonEventsFromPadZero_AndAcceptsPadOne()
        {
            var host = new InputMapHost(InputBus(), new InputMapOptions { GamepadIndex = 1 });
            host.DeclareActionSet(new Id("input.set.g"), new[]
            {
                new ActionDefinition(new Id("input.action.jump"), ActionKind.Button, new[] { "pad:a" }),
            });
            var stub = new StubInput();

            stub.PressGamepadButton(0, "a");
            host.Update(stub);
            Assert.False(host.IsActionActive("input.action.jump"));

            stub.PressGamepadButton(1, "a");
            host.Update(stub);
            Assert.True(host.IsActionActive("input.action.jump"));
        }

        [Fact]
        public void GamepadIndex_One_ReleaseFromOtherPad_DoesNotClearHeldButton()
        {
            var host = new InputMapHost(InputBus(), new InputMapOptions { GamepadIndex = 1 });
            host.DeclareActionSet(new Id("input.set.g"), new[]
            {
                new ActionDefinition(new Id("input.action.jump"), ActionKind.Button, new[] { "pad:a" }),
            });
            var stub = new StubInput();
            stub.PressGamepadButton(1, "a");
            host.Update(stub);

            stub.ReleaseGamepadButton(0, "a");
            host.Update(stub);
            Assert.True(host.IsActionActive("input.action.jump"));

            stub.ReleaseGamepadButton(1, "a");
            host.Update(stub);
            Assert.False(host.IsActionActive("input.action.jump"));
        }

        [Fact]
        public void GamepadIndex_One_AxisReadsComeFromPadOneOnly()
        {
            var host = new InputMapHost(InputBus(), new InputMapOptions { GamepadIndex = 1 });
            host.DeclareActionSet(new Id("input.set.g"), new[]
            {
                new ActionDefinition(new Id("input.action.zoom"), ActionKind.Axis1D, new[] { "pad_axis:zoom" }),
                new ActionDefinition(new Id("input.action.look"), ActionKind.Axis2D, new[] { "pad_stick:left" }),
            });
            var stub = new StubInput();
            stub.SetAxis(0, "zoom", 0.9);
            stub.SetAxis(1, "zoom", 0.25);
            stub.SetAxis(0, "leftx", 1.0);
            stub.SetAxis(1, "leftx", -0.5);
            stub.SetAxis(1, "lefty", 0.75);

            host.Update(stub);

            Assert.Equal(0.25, host.GetActionAxis("input.action.zoom").X, 9);
            var look = host.GetActionAxis("input.action.look");
            Assert.Equal(-0.5, look.X, 9);
            Assert.Equal(0.75, look.Y, 9);
        }

        [Fact]
        public void GamepadIndex_NoPadHeldOnConfiguredIndex_PadButtonStaysInactive()
        {
            var host = new InputMapHost(InputBus(), new InputMapOptions { GamepadIndex = 3 });
            host.DeclareActionSet(new Id("input.set.g"), new[]
            {
                new ActionDefinition(new Id("input.action.jump"), ActionKind.Button, new[] { "pad:a" }),
            });
            var stub = new StubInput();
            stub.PressGamepadButton(0, "a");
            stub.PressGamepadButton(1, "a");
            stub.PressGamepadButton(2, "a");

            host.Update(stub);

            Assert.False(host.IsActionActive("input.action.jump"));
        }
    }
}

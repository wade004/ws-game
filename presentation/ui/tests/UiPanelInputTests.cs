using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// P4 备忘 5/8（界面侧）：面板热键由输入绑定数据声明（声明了就用数据的绑定、没声明沿用写死键、可全部关闭）；
    /// 模态面板打开期间向输入映射压入输入上下文，挡掉移动与战斗、放行界面动作。
    /// </summary>
    public sealed class UiPanelInputTests
    {
        private static (InputMapHost Host, StubInput Input) Build(params ActionDefinition[] actions)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var host = new InputMapHost(bus);
            host.DeclareActionSet(new Id("input.set.ui_test"), actions);
            return (host, new StubInput());
        }

        private static ActionDefinition Button(string name, string binding) =>
            new ActionDefinition(new Id(name), ActionKind.Button, new[] { binding });

        [Fact]
        public void ActionName_IsSnakeCasePerPanel()
        {
            Assert.Equal("input.action.ui_toggle_inventory", UiPanelHotkeys.ActionName(UiPanel.Inventory));
            Assert.Equal("input.action.ui_toggle_quest_log", UiPanelHotkeys.ActionName(UiPanel.QuestLog));
            Assert.Equal("input.action.ui_toggle_character_stats", UiPanelHotkeys.ActionName(UiPanel.CharacterStats));
        }

        [Fact]
        public void NoDeclaredActions_LegacyKeysStillToggle_AndCanBeSwitchedOff()
        {
            var (host, _) = Build();
            var hotkeys = new UiPanelHotkeys(host);
            var toggled = new List<UiPanel>();

            hotkeys.Poll(p => p == UiPanel.Inventory, toggled.Add);
            Assert.Equal(new[] { UiPanel.Inventory }, toggled);

            hotkeys.LegacyEnabled = false;
            toggled.Clear();
            hotkeys.Poll(p => true, toggled.Add);
            Assert.Empty(toggled);
        }

        [Fact]
        public void DeclaredAction_UsesItsBinding_OnPressEdge_AndLegacyKeyNoLongerToggles()
        {
            var (host, input) = Build(Button(UiPanelHotkeys.ActionName(UiPanel.Inventory), "key:b"));
            var hotkeys = new UiPanelHotkeys(host);
            var toggled = new List<UiPanel>();

            // 写死键（I）对已声明动作的面板不再生效。
            hotkeys.Poll(p => p == UiPanel.Inventory, toggled.Add);
            Assert.DoesNotContain(UiPanel.Inventory, toggled);

            input.Press("b");
            host.Update(input);
            hotkeys.Poll(p => false, toggled.Add);
            Assert.Equal(new[] { UiPanel.Inventory }, toggled);

            // 按住不重复翻转；松开再按才翻一次。
            host.Update(input);
            hotkeys.Poll(p => false, toggled.Add);
            Assert.Single(toggled);
            input.Release("b");
            host.Update(input);
            hotkeys.Poll(p => false, toggled.Add);
            input.Press("b");
            host.Update(input);
            hotkeys.Poll(p => false, toggled.Add);
            Assert.Equal(2, toggled.Count);
        }

        [Fact]
        public void DeclaredAction_StillWorks_WhenLegacyDisabled_AndOtherPanelsGoQuiet()
        {
            var (host, input) = Build(Button(UiPanelHotkeys.ActionName(UiPanel.QuestLog), "key:q"));
            var hotkeys = new UiPanelHotkeys(host, legacyEnabled: false);
            var toggled = new List<UiPanel>();

            input.Press("q");
            host.Update(input);
            hotkeys.Poll(p => true, toggled.Add);

            Assert.Equal(new[] { UiPanel.QuestLog }, toggled);
        }

        [Fact]
        public void ModalContext_PushedWhileAnyModalOpen_BlocksMoveAndAttack_AllowsDeclaredUiActions()
        {
            var (host, input) = Build(
                Button("input.action.attack", "key:space"),
                Button("input.action.confirm", "key:enter"),
                Button(UiPanelHotkeys.ActionName(UiPanel.Inventory), "key:b"));
            var modal = new UiModalInputContext(host);

            modal.Sync(anyModalOpen: true);
            Assert.Equal(UiModalInputContext.ContextId, host.ActiveInputContext);
            input.Press("space");
            input.Press("enter");
            input.Press("b");
            host.Update(input);
            Assert.False(host.IsActionActive("input.action.attack"));
            Assert.True(host.IsActionActive("input.action.confirm"));
            Assert.True(host.IsActionActive(UiPanelHotkeys.ActionName(UiPanel.Inventory)));

            modal.Sync(anyModalOpen: false);
            Assert.Null(host.ActiveInputContext);
            host.Update(input);
            Assert.True(host.IsActionActive("input.action.attack"));
        }

        [Fact]
        public void ModalContext_Disabled_NeverPushes()
        {
            var (host, _) = Build(Button("input.action.attack", "key:space"));
            var modal = new UiModalInputContext(host) { Enabled = false };

            modal.Sync(anyModalOpen: true);

            Assert.Null(host.ActiveInputContext);
            Assert.False(modal.IsPushed);
        }

        [Fact]
        public void ModalContext_SyncIsIdempotent_PerState()
        {
            var (host, _) = Build(Button("input.action.confirm", "key:enter"));
            var modal = new UiModalInputContext(host);

            modal.Sync(true);
            modal.Sync(true);
            modal.Sync(false);
            modal.Sync(false);

            Assert.Null(host.ActiveInputContext);
        }
    }
}

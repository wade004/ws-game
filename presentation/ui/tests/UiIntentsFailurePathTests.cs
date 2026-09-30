using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Gameplay.Economy;
using Presentation.Ui;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// T-M31（ADR-0125）：<see cref="UiIntents"/> 的失败路径。既有 <c>UiIntentsTests</c> 全部使用总是成功的 Fake 宿主，
    /// 这里改用真实宿主（<c>EconomyHost</c>/<c>SkillBindingHost</c>/<c>AppStateHost</c>）观察"宿主拒绝时"
    /// 意图层的行为：失败结果原样透传（不吞、不改写）、宿主异常原样向上传播、
    /// 带 panelId 的重载在宿主调用之前就已发出 <c>ui.action_invoked</c>（即失败的点击同样可被观测）。
    /// </summary>
    public sealed class UiIntentsFailurePathTests
    {
        private static readonly Id Coin = new Id("econ.currency.sample_coin");
        private static readonly Id Shop = new Id("econ.vendor.sample_shop");
        private static readonly Id Sword = new Id("item.sample_sword");
        private static readonly Id Panel = new Id("ui_layout_definition.sample_shop");

        private sealed class Rig
        {
            public UiWorldFixture World = null!;
            public EconomyHost Economy = null!;
            public AppStateHost AppState = null!;
            public Core.Carriers.Unit.SkillBindingHost SkillBindings = null!;
            public UiIntents Intents = null!;
            public List<(Id Panel, string Action)> Invoked = new List<(Id, string)>();
        }

        private static Rig NewRig(Func<Id, Id, bool>? knownSkill = null, IAudioLayerVolumeHost? audio = null)
        {
            var world = new UiWorldFixture();
            var economy = ShopViewModelTests.BuildEconomy(world);
            var appState = new AppStateHost(TestSupport.BuildEventBus());
            var skillBindings = new Core.Carriers.Unit.SkillBindingHost(
                TestSupport.BuildEventBus(), (unit, skill) => knownSkill?.Invoke(unit, skill) ?? true);

            var rig = new Rig { World = world, Economy = economy, AppState = appState, SkillBindings = skillBindings };
            world.EventBus.Subscribe<UiActionInvokedEvent>(UiEventKeys.ActionInvoked, e => rig.Invoked.Add((e.PanelId, e.ActionName)));
            rig.Intents = new UiIntents(
                world.PlayerId, new RecordingWorldSim(), new FakeEquipmentHost(), new FakeQuestHost(), new FakeDialogHost(),
                economy, new FakeInputMapHost(),
                new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") }),
                appState, audio ?? new FakeAudioLayerVolumeHost(), skillBindings,
                turnScheduler: null, eventBus: world.EventBus);
            return rig;
        }

        // ---- 经济：失败结果原样透传 ----

        [Fact]
        public void Buy_HostRejects_ReturnsFailureReasonUnchanged_AndBalanceAndStockUntouched()
        {
            var rig = NewRig();
            var balanceBefore = rig.Economy.GetBalance(rig.World.PlayerId, Coin);
            var stockBefore = rig.Economy.GetStock(Shop, Sword);

            var notSold = rig.Intents.Buy(Shop, new Id("item.sample_shield"), 1);
            var unknownVendor = rig.Intents.Buy(new Id("econ.vendor.sample_ghost"), Sword, 1);
            var tooMany = rig.Intents.Buy(Shop, Sword, stockBefore!.Value + 1);

            Assert.Equal(PurchaseFailureReason.ItemNotSold, notSold.Reason);
            Assert.Equal(PurchaseFailureReason.UnknownVendor, unknownVendor.Reason);
            Assert.Equal(PurchaseFailureReason.InsufficientStock, tooMany.Reason);
            Assert.All(new[] { notSold, unknownVendor, tooMany }, r => Assert.False(r.Success));
            Assert.Equal(balanceBefore, rig.Economy.GetBalance(rig.World.PlayerId, Coin));
            Assert.Equal(stockBefore, rig.Economy.GetStock(Shop, Sword));
        }

        [Fact]
        public void Buy_InsufficientFunds_ReturnsInsufficientFunds()
        {
            var rig = NewRig();
            rig.Economy.SetBalance(rig.World.PlayerId, Coin, 0);

            var result = rig.Intents.Buy(Shop, Sword, 1);

            Assert.False(result.Success);
            Assert.Equal(PurchaseFailureReason.InsufficientFunds, result.Reason);
        }

        [Fact]
        public void Sell_ItemNotOwned_ReturnsNotOwned_AndUnknownVendorReturnsUnknownVendor()
        {
            var rig = NewRig();

            var notOwned = rig.Intents.Sell(Shop, new Id("item.instance_404"), 1);
            var unknownVendor = rig.Intents.Sell(new Id("econ.vendor.sample_ghost"), new Id("item.instance_404"), 1);

            Assert.False(notOwned.Success);
            Assert.Equal(SellFailureReason.NotOwned, notOwned.Reason);
            Assert.Equal(SellFailureReason.UnknownVendor, unknownVendor.Reason);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Buy_NonPositiveCount_HostException_PropagatesThroughIntent(int count)
        {
            var rig = NewRig();

            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Intents.Buy(Shop, Sword, count));
        }

        /// <summary>带 panelId 的重载在调用宿主之前发事件（事件表示"点击发生了"，不是"操作成功了"）：
        /// 宿主拒绝/抛异常时，事件仍已发出——钉住此现行为，避免未来误改为"成功才发"。</summary>
        [Fact]
        public void PanelOverloads_PublishActionInvoked_EvenWhenHostRejectsOrThrows()
        {
            var rig = NewRig();

            var failed = rig.Intents.Buy(Panel, Shop, new Id("item.sample_shield"), 1);
            Assert.ThrowsAny<ArgumentException>(() => rig.Intents.Buy(Panel, Shop, Sword, 0));
            var sold = rig.Intents.Sell(Panel, Shop, new Id("item.instance_404"), 1);

            Assert.False(failed.Success);
            Assert.False(sold.Success);
            Assert.Equal(new[] { (Panel, "buy"), (Panel, "buy"), (Panel, "sell") }, rig.Invoked);
        }

        // ---- 技能栏 ----

        [Fact]
        public void BindActionBarSlot_UnknownSkill_ReturnsFalse_AndLeavesSlotUnbound()
        {
            var rig = NewRig(knownSkill: (_, __) => false);

            var ok = rig.Intents.BindActionBarSlot(0, new Id("skill.fireball"));

            Assert.False(ok);
            Assert.False(rig.SkillBindings.GetBindings(rig.World.PlayerId).ContainsKey(ActionBarViewModel.SlotKey(0)));
        }

        [Fact]
        public void BindActionBarSlot_PanelOverload_PublishesEvent_EvenWhenBindingIsRejected()
        {
            var rig = NewRig(knownSkill: (_, __) => false);

            var ok = rig.Intents.BindActionBarSlot(Panel, 0, new Id("skill.fireball"));

            Assert.False(ok);
            Assert.Equal(new[] { (Panel, "bind_action_bar_slot") }, rig.Invoked);
        }

        [Fact]
        public void UnbindActionBarSlot_AlreadyEmptySlot_IsIdempotentSuccess()
        {
            var rig = NewRig();

            Assert.True(rig.Intents.UnbindActionBarSlot(5));
            Assert.True(rig.Intents.UnbindActionBarSlot(5));
            Assert.Empty(rig.SkillBindings.GetBindings(rig.World.PlayerId));
        }

        // ---- 应用状态 ----

        [Fact]
        public void PauseAndResume_OutsideInWorld_ReturnFalse_AndStateUnchanged()
        {
            var rig = NewRig();
            rig.AppState.RequestTransition(AppState.MainMenu);

            Assert.False(rig.Intents.Pause());
            Assert.False(rig.Intents.Resume());
            Assert.Equal(AppState.MainMenu, rig.AppState.GetState());
        }

        [Fact]
        public void OpenMenu_OutsideInWorld_ReturnsFalse_AndCloseMenuWithoutOpenMenuReturnsFalse()
        {
            var rig = NewRig();
            rig.AppState.RequestTransition(AppState.MainMenu);
            Assert.False(rig.Intents.OpenMenu());

            rig.AppState.RequestTransition(AppState.Loading);
            rig.AppState.RequestTransition(AppState.InWorld);
            var subBefore = rig.AppState.CurrentSubState;

            Assert.False(rig.Intents.CloseMenu()); // 栈底 Explore 不可弹出
            Assert.Equal(subBefore, rig.AppState.CurrentSubState);
        }

        // ---- 音量：宿主异常原样传播 ----

        private sealed class ThrowingAudioHost : IAudioLayerVolumeHost
        {
            public IReadOnlyList<string> Layers { get; } = new[] { "sfx" };

            public double GetVolume(string layer) => 1.0;

            public void SetVolume(string layer, double volume) =>
                throw new ArgumentException("未知音量层", nameof(layer));
        }

        [Fact]
        public void SetLayerVolume_HostThrows_ExceptionPropagates_NotSwallowedByIntent()
        {
            var rig = NewRig(audio: new ThrowingAudioHost());

            var ex = Assert.Throws<ArgumentException>(() => rig.Intents.SetLayerVolume("nope", 0.5));

            Assert.Equal("layer", ex.ParamName);
        }

        [Fact]
        public void SetLayerVolume_PanelOverload_PublishesEventBeforeHostThrows()
        {
            var rig = NewRig(audio: new ThrowingAudioHost());

            Assert.Throws<ArgumentException>(() => rig.Intents.SetLayerVolume(Panel, "nope", 0.5));

            Assert.Equal(new[] { (Panel, "set_layer_volume") }, rig.Invoked);
        }
    }
}

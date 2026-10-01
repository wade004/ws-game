using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.InputMap;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// 测试覆盖剩余项 T-M37：<see cref="UiIntents"/> 余下缺口——全部 panelId 重载的 <c>actionName</c> 字面量、
    /// <c>Equip</c> 失败、<c>Rebind</c> 冲突返回 false（真实 <c>InputMapHost</c>）、<c>AcceptQuest/TurnInQuest</c>
    /// 返回 false、<c>Unequip</c> 空槽、未接入事件总线时 panelId 重载静默降级。
    /// </summary>
    public sealed class UiIntentsCoverageTests
    {
        private static readonly Id Panel = new Id("ui_layout_definition.sample_panel");
        private static readonly Id Item = new Id("item.instance_1");
        private static readonly Id Slot = new Id("slot.main_hand");
        private static readonly Id Quest = new Id("quest.sample_q");
        private static readonly Id Vendor = new Id("econ.vendor.sample_shop");
        private static readonly Id Locale = new Id("l10n.en_us");

        private sealed class Rig
        {
            public UiWorldFixture World = null!;
            public RecordingWorldSim WorldSim = null!;
            public FakeEquipmentHost Equipment = null!;
            public FakeQuestHost QuestHost = null!;
            public FakeDialogHost Dialog = null!;
            public FakeEconomyHost Economy = null!;
            public InputMapHost InputMap = null!;
            public FakeL10nHost L10n = null!;
            public FakeAudioLayerVolumeHost Audio = null!;
            public AppStateHost AppState = null!;
            public UiIntents Intents = null!;
            public readonly List<(Id Panel, string Action)> Invoked = new List<(Id, string)>();
            public readonly List<(string Action, string Binding)> Conflicts = new List<(string, string)>();
        }

        /// <summary>真实 <see cref="InputMapHost"/> 里声明同一重绑定组的两个动作：move_up=key:w、move_down=key:s。</summary>
        private static Rig NewRig(bool withEventBus = true)
        {
            var world = new UiWorldFixture();
            var inputMap = new InputMapHost(world.EventBus);
            inputMap.DeclareActionSet(new Id("input.set.sample"), new[]
            {
                new ActionDefinition(new Id("input.action.move_up"), ActionKind.Button, new[] { "key:w" }),
                new ActionDefinition(new Id("input.action.move_down"), ActionKind.Button, new[] { "key:s" }),
            });

            var rig = new Rig
            {
                World = world,
                WorldSim = new RecordingWorldSim(),
                Equipment = new FakeEquipmentHost(),
                QuestHost = new FakeQuestHost(),
                Dialog = new FakeDialogHost(),
                Economy = new FakeEconomyHost(),
                InputMap = inputMap,
                L10n = new FakeL10nHost(Locale, new[] { Locale }),
                Audio = new FakeAudioLayerVolumeHost(),
                AppState = new AppStateHost(TestSupport.BuildEventBus()),
            };
            var skillBindings = new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true);
            world.EventBus.Subscribe<UiActionInvokedEvent>(UiEventKeys.ActionInvoked, e => rig.Invoked.Add((e.PanelId, e.ActionName)));
            world.EventBus.Subscribe<InputRebindConflictEvent>(InputMapEventKeys.RebindConflict, e => rig.Conflicts.Add((e.ActionName, e.Binding)));

            rig.Intents = withEventBus
                ? new UiIntents(world.PlayerId, rig.WorldSim, rig.Equipment, rig.QuestHost, rig.Dialog, rig.Economy,
                    inputMap, rig.L10n, rig.AppState, rig.Audio, skillBindings, turnScheduler: null, eventBus: world.EventBus)
                : new UiIntents(world.PlayerId, rig.WorldSim, rig.Equipment, rig.QuestHost, rig.Dialog, rig.Economy,
                    inputMap, rig.L10n, rig.AppState, rig.Audio, skillBindings);
            return rig;
        }

        /// <summary>全部带 panelId 的重载 -> 框架自持的固定 actionName 词汇（见 UiIntents.PublishActionInvoked 注释）。</summary>
        private static IReadOnlyList<(string ExpectedAction, Action<UiIntents> Invoke)> PanelOverloadTable() =>
            new (string, Action<UiIntents>)[]
            {
                ("use_item", i => i.UseItem(Panel, Item)),
                ("equip", i => i.Equip(Panel, Item, Slot)),
                ("unequip", i => i.Unequip(Panel, Slot)),
                ("cast_skill", i => i.CastSkill(Panel, new Id("skill.fireball"), null)),
                ("accept_quest", i => i.AcceptQuest(Panel, Quest)),
                ("turn_in_quest", i => i.TurnInQuest(Panel, Quest)),
                ("abandon_quest", i => i.AbandonQuest(Panel, Quest)),
                ("choose_dialog_option", i => i.ChooseDialogOption(Panel, 0)),
                ("buy", i => i.Buy(Panel, Vendor, new Id("item.sample_sword"), 1)),
                ("sell", i => i.Sell(Panel, Vendor, Item, 1)),
                ("rebind", i => i.Rebind(Panel, "input.action.move_up", "key:up")),
                ("set_locale", i => i.SetLocale(Panel, Locale)),
                ("set_layer_volume", i => i.SetLayerVolume(Panel, "music", 0.5)),
                ("bind_action_bar_slot", i => i.BindActionBarSlot(Panel, 0, new Id("skill.fireball"))),
                ("unbind_action_bar_slot", i => i.UnbindActionBarSlot(Panel, 0)),
                ("end_turn", i => i.EndTurn(Panel)),
                ("pause", i => i.Pause(Panel)),
                ("resume", i => i.Resume(Panel)),
            };

        // ------------------------------------------------------------------ 全部 panelId 重载的 actionName 字面量

        [Fact]
        public void EveryPanelOverload_PublishesExactlyOneEvent_WithItsFixedActionNameAndTheGivenPanel()
        {
            foreach (var (expectedAction, invoke) in PanelOverloadTable())
            {
                var rig = NewRig();

                invoke(rig.Intents);

                var published = Assert.Single(rig.Invoked);
                Assert.Equal(Panel, published.Panel);
                Assert.Equal(expectedAction, published.Action);
            }
        }

        [Fact]
        public void PanelOverloadActionNames_AreDistinct_AndSnakeCaseVocabulary()
        {
            var names = PanelOverloadTable().Select(t => t.ExpectedAction).ToArray();

            Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
            Assert.All(names, n => Assert.Matches("^[a-z]+(_[a-z]+)*$", n));
        }

        [Fact]
        public void PanelOverloadCount_MatchesTheNumberOfPublicPanelIdOverloads_SoANewOverloadCannotSkipTheTable()
        {
            // 反射守卫：UiIntents 上第一个形参为 Id 且名为 panelId 的公开实例方法数 == 表项数。
            var reflected = typeof(UiIntents).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => m.GetParameters().Length > 0 && m.GetParameters()[0].Name == "panelId")
                .ToArray();

            Assert.Equal(PanelOverloadTable().Count, reflected.Length);
        }

        [Fact]
        public void PanelOverloads_WithoutEventBus_DoNotThrow_AndStillReachTheirHosts()
        {
            var rig = NewRig(withEventBus: false);

            foreach (var (_, invoke) in PanelOverloadTable())
            {
                var thrown = Record.Exception(() => invoke(rig.Intents));
                Assert.Null(thrown);
            }

            Assert.Empty(rig.Invoked); // 未接入总线：一个事件也没有。
            // 宿主侧副作用照常发生：use_item/cast 两条意图已提交、装备/任务/音量调用已到达。
            Assert.Equal(new[] { "use_item", "cast" }, rig.WorldSim.SubmittedIntents.Select(i => i.Kind).ToArray());
            Assert.Single(rig.Equipment.EquipCalls);
            Assert.Single(rig.Equipment.UnequipCalls);
            Assert.Contains(Quest, rig.QuestHost.AcceptedQuests);
            Assert.Equal(("music", 0.5), Assert.Single(rig.Audio.SetCalls));
        }

        [Fact]
        public void Constructor_NullEventBus_ThrowsArgumentNullException()
        {
            var rig = NewRig();
            var skillBindings = new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true);

            var ex = Assert.Throws<ArgumentNullException>(() => new UiIntents(
                rig.World.PlayerId, rig.WorldSim, rig.Equipment, rig.QuestHost, rig.Dialog, rig.Economy,
                rig.InputMap, rig.L10n, rig.AppState, rig.Audio, skillBindings, turnScheduler: null, eventBus: null!));

            Assert.Equal("eventBus", ex.ParamName);
        }

        // ------------------------------------------------------------------ Equip 失败

        [Theory]
        [InlineData(EquipFailureReason.UnknownItem)]
        [InlineData(EquipFailureReason.SlotMismatch)]
        [InlineData(EquipFailureReason.SlotOccupied)]
        [InlineData(EquipFailureReason.RequirementNotMet)]
        [InlineData(EquipFailureReason.NotInInventory)]
        public void Equip_HostRejects_ReturnsTheFailureVerbatim_AndSlotStaysEmpty(EquipFailureReason reason)
        {
            var rig = NewRig();
            rig.Equipment.FailEquipWith = reason;

            var result = rig.Intents.Equip(Item, Slot);

            Assert.False(result.Success);
            Assert.Equal(reason, result.Reason);
            Assert.Null(result.Replaced);
            Assert.Equal((rig.World.PlayerId, Item, Slot), Assert.Single(rig.Equipment.EquipCalls)); // 以玩家身份、原样参数转发
            Assert.Null(rig.Equipment.GetEquipped(rig.World.PlayerId, Slot));
        }

        [Fact]
        public void Equip_PanelOverload_RejectedStillPublishesEvent_AndReturnsSameFailureAsPlainOverload()
        {
            var rig = NewRig();
            rig.Equipment.FailEquipWith = EquipFailureReason.RequirementNotMet;

            var viaPanel = rig.Intents.Equip(Panel, Item, Slot);
            var plain = rig.Intents.Equip(Item, Slot);

            Assert.Equal(plain, viaPanel);
            Assert.Equal(new[] { (Panel, "equip") }, rig.Invoked);
        }

        // ------------------------------------------------------------------ Unequip 空槽

        [Fact]
        public void Unequip_EmptySlot_ReturnsNull_AndHostIsStillAskedForThePlayersSlot()
        {
            var rig = NewRig();

            var result = rig.Intents.Unequip(Slot);

            Assert.Null(result);
            Assert.Equal((rig.World.PlayerId, Slot), Assert.Single(rig.Equipment.UnequipCalls));
        }

        [Fact]
        public void Unequip_AfterEquip_ReturnsTheItem_ThenSecondUnequipOfTheSameSlotReturnsNull()
        {
            var rig = NewRig();
            Assert.True(rig.Intents.Equip(Item, Slot).Success);

            var first = rig.Intents.Unequip(Panel, Slot);
            var second = rig.Intents.Unequip(Panel, Slot);

            Assert.Equal(Item, first!.Value.InstanceId);
            Assert.Null(second);
            Assert.Equal(new[] { (Panel, "unequip"), (Panel, "unequip") }, rig.Invoked); // 空槽也算一次点击
        }

        // ------------------------------------------------------------------ Rebind 冲突（真实 InputMapHost）

        [Fact]
        public void Rebind_ConflictsWithAnotherActionInTheSameGroup_ReturnsFalse_BindingsUnchanged_ConflictEventPublished()
        {
            var rig = NewRig();
            var upBefore = rig.InputMap.GetBindings("input.action.move_up").ToArray();
            var downBefore = rig.InputMap.GetBindings("input.action.move_down").ToArray();

            // move_up 想抢 move_down 当前的绑定。
            var ok = rig.Intents.Rebind("input.action.move_up", downBefore[0]);

            Assert.False(ok);
            Assert.Equal(upBefore, rig.InputMap.GetBindings("input.action.move_up").ToArray());
            Assert.Equal(downBefore, rig.InputMap.GetBindings("input.action.move_down").ToArray());
            Assert.Equal(("input.action.move_up", downBefore[0]), Assert.Single(rig.Conflicts));
        }

        [Fact]
        public void Rebind_NoConflict_ReturnsTrue_AndReplacesTheBinding()
        {
            var rig = NewRig();
            const string free = "key:up";
            Assert.Empty(rig.InputMap.GetConflicts(free)); // 前置：规则上确实没人占用，用例才有区分度。

            var ok = rig.Intents.Rebind("input.action.move_up", free);

            Assert.True(ok);
            Assert.Equal(new[] { free }, rig.InputMap.GetBindings("input.action.move_up").ToArray());
            Assert.Empty(rig.Conflicts);
        }

        [Fact]
        public void Rebind_PanelOverload_PublishesEvenWhenConflicting_AndReturnsSameResultAsPlain()
        {
            var rig = NewRig();
            var downBinding = rig.InputMap.GetBindings("input.action.move_down")[0];

            var ok = rig.Intents.Rebind(Panel, "input.action.move_up", downBinding);

            Assert.False(ok);
            Assert.Equal(new[] { (Panel, "rebind") }, rig.Invoked);
        }

        [Fact]
        public void Rebind_UnknownAction_HostExceptionPropagates()
        {
            var rig = NewRig();

            Assert.ThrowsAny<Exception>(() => rig.Intents.Rebind("input.action.no_such_action", "key:up"));
        }

        // ------------------------------------------------------------------ Accept / TurnIn 返回 false

        [Fact]
        public void AcceptQuest_HostRefuses_ReturnsFalse_AndStateUnchanged()
        {
            var rig = NewRig();
            rig.QuestHost.RefuseAccept = true;

            var plain = rig.Intents.AcceptQuest(Quest);
            var viaPanel = rig.Intents.AcceptQuest(Panel, Quest);

            Assert.False(plain);
            Assert.False(viaPanel);
            Assert.Empty(rig.QuestHost.AcceptedQuests);
            Assert.Equal(Core.Gameplay.Quest.QuestState.Unavailable, rig.QuestHost.GetState(rig.World.PlayerId, Quest));
            Assert.Equal(new[] { (Panel, "accept_quest") }, rig.Invoked);
        }

        [Fact]
        public void TurnInQuest_HostRefuses_ReturnsFalse_AndNothingIsTurnedIn()
        {
            var rig = NewRig();
            rig.QuestHost.RefuseTurnIn = true;

            var plain = rig.Intents.TurnInQuest(Quest);
            var viaPanel = rig.Intents.TurnInQuest(Panel, Quest);

            Assert.False(plain);
            Assert.False(viaPanel);
            Assert.Empty(rig.QuestHost.TurnedInQuests);
            Assert.Equal(new[] { (Panel, "turn_in_quest") }, rig.Invoked);
        }

        [Fact]
        public void AbandonQuest_NotActive_ReturnsFalse_ThenTrueOnceAcceptedAndNotAgain()
        {
            var rig = NewRig();

            Assert.False(rig.Intents.AbandonQuest(Quest)); // 从未接取
            Assert.True(rig.Intents.AcceptQuest(Quest));
            Assert.True(rig.Intents.AbandonQuest(Panel, Quest));
            Assert.False(rig.Intents.AbandonQuest(Panel, Quest)); // 已放弃，不可重复放弃

            Assert.Equal(new[] { (Panel, "abandon_quest"), (Panel, "abandon_quest") }, rig.Invoked);
        }

        [Fact]
        public void EndTurn_WithoutTurnScheduler_ReturnsFalse_AndPanelOverloadStillPublishes()
        {
            var rig = NewRig();

            Assert.False(rig.Intents.EndTurn());
            Assert.False(rig.Intents.EndTurn(Panel));

            Assert.Equal(new[] { (Panel, "end_turn") }, rig.Invoked);
        }
    }
}

using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Xunit;
using Presentation.Ui;

namespace Tests.PresentationUi
{
    public class UiIntentsTests
    {
        private static UiIntents Build(
            out RecordingWorldSim worldSim,
            out FakeEquipmentHost equipment,
            out FakeQuestHost quest,
            out FakeDialogHost dialog,
            out FakeEconomyHost economy,
            out FakeInputMapHost inputMap,
            out FakeL10nHost l10n,
            out AppStateHost appState,
            out Id playerId) =>
            Build(out worldSim, out equipment, out quest, out dialog, out economy, out inputMap, out l10n,
                out appState, out playerId, out _);

        private static UiIntents Build(
            out RecordingWorldSim worldSim,
            out FakeEquipmentHost equipment,
            out FakeQuestHost quest,
            out FakeDialogHost dialog,
            out FakeEconomyHost economy,
            out FakeInputMapHost inputMap,
            out FakeL10nHost l10n,
            out AppStateHost appState,
            out Id playerId,
            out Core.Carriers.Unit.SkillBindingHost skillBindings)
        {
            playerId = new Id("unit.hero");
            worldSim = new RecordingWorldSim();
            equipment = new FakeEquipmentHost();
            quest = new FakeQuestHost();
            dialog = new FakeDialogHost();
            economy = new FakeEconomyHost();
            inputMap = new FakeInputMapHost();
            l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us"), new Id("l10n.zh_cn") });
            appState = new AppStateHost(TestSupport.BuildEventBus());
            var audioVolume = new FakeAudioLayerVolumeHost();
            skillBindings = new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true);

            return new UiIntents(playerId, worldSim, equipment, quest, dialog, economy, inputMap, l10n, appState,
                audioVolume, skillBindings);
        }

        [Fact]
        public void UseItem_submits_use_item_intent()
        {
            var intents = Build(out var worldSim, out _, out _, out _, out _, out _, out _, out _, out var playerId);
            var instanceId = new Id("item.instance_0");

            intents.UseItem(instanceId);

            var submitted = Assert.Single(worldSim.SubmittedIntents);
            Assert.Equal(playerId, submitted.ActorId);
            Assert.Equal("use_item", submitted.Kind);
            Assert.Equal(instanceId.Value, ((Core.Foundation.Common.Json.JsonString)submitted.Args["instance_id"]).Value);
        }

        [Fact]
        public void CastSkill_submits_cast_intent_with_optional_target()
        {
            var intents = Build(out var worldSim, out _, out _, out _, out _, out _, out _, out _, out _);
            var skillId = new Id("skill.fireball");
            var targetId = new Id("unit.inst_2");

            intents.CastSkill(skillId, targetId);

            var submitted = Assert.Single(worldSim.SubmittedIntents);
            Assert.Equal("cast", submitted.Kind);
            Assert.True(submitted.Args.ContainsKey("target_id"));
        }

        [Fact]
        public void Move_submits_move_intent_with_direction()
        {
            var intents = Build(out var worldSim, out _, out _, out _, out _, out _, out _, out _, out _);

            intents.Move(new Core.Foundation.Common.Vec2(1, 0));

            var submitted = Assert.Single(worldSim.SubmittedIntents);
            Assert.Equal("move", submitted.Kind);
        }

        [Fact]
        public void Equip_calls_equipment_host_directly_not_via_submit_intent()
        {
            var intents = Build(out var worldSim, out var equipment, out _, out _, out _, out _, out _, out _, out var playerId);
            var instanceId = new Id("item.instance_0");
            var slot = new Id("equip.main_hand");

            var result = intents.Equip(instanceId, slot);

            Assert.True(result.Success);
            Assert.Single(equipment.EquipCalls);
            Assert.Equal((playerId, instanceId, slot), equipment.EquipCalls[0]);
            Assert.Empty(worldSim.SubmittedIntents);
        }

        [Fact]
        public void AcceptQuest_and_TurnInQuest_delegate_to_quest_host()
        {
            var intents = Build(out _, out _, out var quest, out _, out _, out _, out _, out _, out _);
            var questId = new Id("quest.find_the_missing_child");

            Assert.True(intents.AcceptQuest(questId));
            Assert.Contains(questId, quest.AcceptedQuests);

            Assert.True(intents.TurnInQuest(questId));
            Assert.Contains(questId, quest.TurnedInQuests);
        }

        [Fact]
        public void ChooseDialogOption_delegates_to_dialog_host()
        {
            var intents = Build(out _, out _, out _, out var dialog, out _, out _, out _, out _, out _);

            Assert.True(intents.ChooseDialogOption(2));
            Assert.Contains(2, dialog.ChosenIndices);
        }

        [Fact]
        public void Buy_and_Sell_delegate_to_economy_host()
        {
            var intents = Build(out _, out _, out _, out _, out var economy, out _, out _, out _, out _);
            var vendorId = new Id("econ.vendor.blacksmith");
            var itemId = new Id("item.iron_sword");
            var instanceId = new Id("item.instance_0");

            intents.Buy(vendorId, itemId, 1);
            intents.Sell(vendorId, instanceId, 1);

            Assert.Single(economy.BuyCalls);
            Assert.Single(economy.SellCalls);
        }

        [Fact]
        public void Rebind_delegates_to_input_map_host()
        {
            var intents = Build(out _, out _, out _, out _, out _, out var inputMap, out _, out _, out _);

            Assert.True(intents.Rebind("input.action.move", "key:w"));
            Assert.Single(inputMap.RebindCalls);
        }

        [Fact]
        public void SetLocale_delegates_to_l10n_host()
        {
            var intents = Build(out _, out _, out _, out _, out _, out _, out var l10n, out _, out _);

            intents.SetLocale(new Id("l10n.zh_cn"));

            Assert.Equal(new Id("l10n.zh_cn"), l10n.GetLocale());
        }

        [Fact]
        public void SetLayerVolume_forwards_to_audio_layer_volume_host()
        {
            var playerId = new Id("unit.hero");
            var worldSim = new RecordingWorldSim();
            var equipment = new FakeEquipmentHost();
            var quest = new FakeQuestHost();
            var dialog = new FakeDialogHost();
            var economy = new FakeEconomyHost();
            var inputMap = new FakeInputMapHost();
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            var appState = new AppStateHost(TestSupport.BuildEventBus());
            var audioVolume = new FakeAudioLayerVolumeHost();
            var skillBindings = new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true);

            var intents = new UiIntents(playerId, worldSim, equipment, quest, dialog, economy, inputMap, l10n, appState,
                audioVolume, skillBindings);

            intents.SetLayerVolume("sfx", 0.5);

            Assert.Single(audioVolume.SetCalls);
            Assert.Equal("sfx", audioVolume.SetCalls[0].Layer);
            Assert.Equal(0.5, audioVolume.SetCalls[0].Volume);
        }

        [Fact]
        public void BindActionBarSlot_bindsKnownSkill_ViaSkillBindingHost()
        {
            var intents = Build(out _, out _, out _, out _, out _, out _, out _, out _, out var playerId,
                out var skillBindings);

            var ok = intents.BindActionBarSlot(0, new Id("skill.fireball"));

            Assert.True(ok);
            Assert.Equal(new Id("skill.fireball"), skillBindings.GetBindings(playerId)["slot_0"]);
        }

        [Fact]
        public void UnbindActionBarSlot_clearsBinding()
        {
            var intents = Build(out _, out _, out _, out _, out _, out _, out _, out _, out var playerId,
                out var skillBindings);
            intents.BindActionBarSlot(1, new Id("skill.fireball"));

            var ok = intents.UnbindActionBarSlot(1);

            Assert.True(ok);
            Assert.False(skillBindings.GetBindings(playerId).ContainsKey("slot_1"));
        }

        [Fact]
        public void EndTurn_delegates_to_turn_scheduler_when_it_is_the_players_turn()
        {
            var playerId = new Id("unit.hero");
            var aiId = new Id("unit.beast");
            var worldSim = new RecordingWorldSim();
            var equipment = new FakeEquipmentHost();
            var quest = new FakeQuestHost();
            var dialog = new FakeDialogHost();
            var economy = new FakeEconomyHost();
            var inputMap = new FakeInputMapHost();
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            var appState = new AppStateHost(TestSupport.BuildEventBus());
            var audioVolume = new FakeAudioLayerVolumeHost();
            var skillBindings = new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true);
            var bus = TestSupport.BuildEventBus();
            var scheduler = new Core.Foundation.SimLoop.TurnScheduler(
                worldSim, initiativeStatProvider: _ => 0.0, isPlayerActor: id => id.Equals(playerId), bus);
            scheduler.BeginCombat(new[] { playerId, aiId });

            var intents = new UiIntents(playerId, worldSim, equipment, quest, dialog, economy, inputMap, l10n, appState,
                audioVolume, skillBindings, turnScheduler: scheduler);

            Assert.Equal(playerId, scheduler.GetCurrentActor());
            var ok = intents.EndTurn();

            Assert.True(ok);
            Assert.Equal(aiId, scheduler.GetCurrentActor());
        }

        [Fact]
        public void EndTurn_returns_false_when_no_turn_scheduler_is_configured()
        {
            var intents = Build(out _, out _, out _, out _, out _, out _, out _, out _, out _);

            Assert.False(intents.EndTurn());
        }

        [Fact]
        public void EndTurn_returns_false_when_it_is_not_the_players_turn()
        {
            var playerId = new Id("unit.hero");
            var aiId = new Id("unit.beast");
            var worldSim = new RecordingWorldSim();
            var equipment = new FakeEquipmentHost();
            var quest = new FakeQuestHost();
            var dialog = new FakeDialogHost();
            var economy = new FakeEconomyHost();
            var inputMap = new FakeInputMapHost();
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            var appState = new AppStateHost(TestSupport.BuildEventBus());
            var audioVolume = new FakeAudioLayerVolumeHost();
            var skillBindings = new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true);
            var bus = TestSupport.BuildEventBus();
            // aiId 排在先攻顺序第一位（isPlayerActor 只认 playerId），当前行动者因此是 aiId。
            var scheduler = new Core.Foundation.SimLoop.TurnScheduler(
                worldSim, initiativeStatProvider: _ => 0.0, isPlayerActor: id => id.Equals(playerId), bus);
            scheduler.BeginCombat(new[] { aiId, playerId });

            var intents = new UiIntents(playerId, worldSim, equipment, quest, dialog, economy, inputMap, l10n, appState,
                audioVolume, skillBindings, turnScheduler: scheduler);

            Assert.Equal(aiId, scheduler.GetCurrentActor());
            var ok = intents.EndTurn();

            Assert.False(ok);
            Assert.Equal(aiId, scheduler.GetCurrentActor());
        }

        [Fact]
        public void Pause_resume_and_menu_intents_transition_app_state()
        {
            var intents = Build(out _, out _, out _, out _, out _, out _, out _, out var appState, out _);
            appState.RequestTransition(AppState.MainMenu);
            appState.RequestTransition(AppState.Loading);
            appState.RequestTransition(AppState.InWorld);

            Assert.True(intents.OpenMenu());
            Assert.Equal(SubStateId.MenuOverlay, appState.CurrentSubState);

            Assert.True(intents.CloseMenu());
            Assert.Equal(SubStateId.Explore, appState.CurrentSubState);

            Assert.True(intents.Pause());
            Assert.Equal(AppState.Pause, appState.GetState());

            Assert.True(intents.Resume());
            Assert.Equal(AppState.InWorld, appState.GetState());
        }
    }
}

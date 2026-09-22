using System;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Gameplay.Dialog;
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

        /// <summary>GP-PRES-02 收口回归：断言 <see cref="UiIntents.Move"/> 提交的参数键就是
        /// <c>dx</c>/<c>dy</c>（<c>Core.Carriers.Unit.MovementTickHandler.TryReadDirection</c>
        /// 实际读取的键）——不只是断言 <c>Kind</c>，此前的测试恰恰因为只断言了 <c>Kind</c> 才没能
        /// 发现字段名错误（<c>dir_x</c>/<c>dir_y</c> 与消费端不一致，见
        /// <c>architecture/落地计划/audit-20260907/gameplay-presentation.md</c> GP-PRES-02）。</summary>
        [Fact]
        public void Move_submits_dx_dy_argument_keys_matching_MovementTickHandler_contract()
        {
            var intents = Build(out var worldSim, out _, out _, out _, out _, out _, out _, out _, out _);

            intents.Move(new Core.Foundation.Common.Vec2(3, 4));

            var submitted = Assert.Single(worldSim.SubmittedIntents);
            Assert.True(submitted.Args.TryGetValue("dx", out var dxValue));
            Assert.True(submitted.Args.TryGetValue("dy", out var dyValue));
            Assert.Equal(3, ((Core.Foundation.Common.Json.JsonNumber)dxValue).Value);
            Assert.Equal(4, ((Core.Foundation.Common.Json.JsonNumber)dyValue).Value);
            Assert.False(submitted.Args.ContainsKey("dir_x"));
            Assert.False(submitted.Args.ContainsKey("dir_y"));
        }

        /// <summary>GP-PRES-02 收口的端到端证据：<see cref="UiIntents.Move"/> 提交的意图真的经
        /// <c>Core.Carriers.Unit.MovementTickHandler</c> 让单位移动了——不是只断言参数键存在，而是
        /// 跑一次真实的 <c>WorldSim.Tick</c>，断言位置按预期位移。此前的 bug（<c>dir_x</c>/<c>dir_y</c>）
        /// 会让 <c>TryReadDirection</c> 读不到任何方向，<c>ApplyIntent</c> 落入"缺少 target/direction"
        /// 诊断分支直接忽略，位置恒不变——本用例正是覆盖这条此前完全没有测试触达的路径。</summary>
        [Fact]
        public void Move_EndToEnd_ThroughMovementTickHandler_DisplacesUnitPosition()
        {
            var playerId = new Id("unit.hero");
            var mapId = new Id("map.ui_intents_test");
            var factionId = new Id("fac.player");
            var archetypeId = new Id("arch.class.sample");
            const double moveSpeed = 10.0;

            var bus = new Core.Foundation.EventBus.EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(System.Array.Empty<Core.Foundation.EventBus.EventDefinition>()),
                new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });

            var world = new Core.Foundation.SimLoop.WorldSim(bus);
            var player = new Core.Carriers.Unit.PlayerUnit(playerId, mapId, factionId, archetypeId)
            {
                Position = Core.Foundation.Common.Vec2.Zero,
            };
            world.AddEntity(player);

            var stats = new MoveSpeedOnlyStatHost(moveSpeed);
            var auras = new NoControlFlagsAuraQuery();
            var movementHost = new Core.Carriers.Unit.MovementHost(world);
            var handler = new Core.Carriers.Unit.MovementTickHandler(
                new Core.Carriers.Unit.WorldUnitAccess(world), stats, auras, movementHost, bus);
            world.RegisterPhaseHandler(Core.Foundation.SimLoop.TickPhase.MovementAndNavigation, handler);

            // UiIntents.Move 本身只依赖 IWorldSim.SubmitIntent（构造函数第二参）——直接把真实
            // WorldSim 传进去，让"UI 提交意图"与"移动处理器消费意图"共用同一个 WorldSim 实例；
            // 其余依赖（equipment/quest/dialog/...）本用例不会触达 Move 之外的任何方法，用与
            // Build() 同一套测试假实现即可。
            var intents = new UiIntents(
                playerId, world, new FakeEquipmentHost(), new FakeQuestHost(), new FakeDialogHost(),
                new FakeEconomyHost(), new FakeInputMapHost(),
                new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") }),
                new AppStateHost(TestSupport.BuildEventBus()), new FakeAudioLayerVolumeHost(),
                new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true));

            intents.Move(new Core.Foundation.Common.Vec2(1, 0)); // 单位向量，向 +X 移动。

            world.Tick(Core.Foundation.SimLoop.SimStep.Continuous(0.5)); // 0.5s * 10 units/s = 5.0

            var position = new Core.Carriers.Unit.WorldUnitAccess(world).GetPosition(playerId);
            Assert.Equal(5.0, position.X, 3);
            Assert.Equal(0.0, position.Y, 3);
        }

        /// <summary>最小 <c>IStatHost</c> 假实现：<c>MovementTickHandler</c> 只调用
        /// <see cref="GetStat"/>（见其源码），其余成员本用例不会触达，抛异常以便一旦被意外调用能
        /// 立即暴露（不是静默返回错误值）。</summary>
        private sealed class MoveSpeedOnlyStatHost : Core.Numbers.StatBlock.IStatHost
        {
            private readonly double _moveSpeed;
            public MoveSpeedOnlyStatHost(double moveSpeed) => _moveSpeed = moveSpeed;

            public double GetStat(Id unitId, Id stat) => _moveSpeed;

            public void RegisterUnit(Id unitId) => throw new System.NotSupportedException();
            public void UnregisterUnit(Id unitId) => throw new System.NotSupportedException();
            public bool IsRegistered(Id unitId) => throw new System.NotSupportedException();
            public void SetBase(Id unitId, Id stat, double value) => throw new System.NotSupportedException();
            public double GetBase(Id unitId, Id stat) => throw new System.NotSupportedException();
            public void AddModifier(Id unitId, Core.Numbers.StatBlock.StatModifier modifier) => throw new System.NotSupportedException();
            public void RemoveModifiersBySource(Id unitId, Id sourceId) => throw new System.NotSupportedException();
            public System.Collections.Generic.IReadOnlyList<Core.Numbers.StatBlock.StatModifier> GetModifiers(Id unitId, Id stat) => throw new System.NotSupportedException();
        }

        /// <summary>最小 <c>IAuraQuery</c> 假实现：<c>MovementTickHandler</c> 只调用
        /// <see cref="GetControlFlags"/>（判断是否被禁锢，见其源码），本用例的单位从不带任何光环，
        /// 恒返回 <c>ControlFlags.None</c>。</summary>
        private sealed class NoControlFlagsAuraQuery : Core.Rules.Common.IAuraQuery
        {
            public Core.Rules.Common.ControlFlags GetControlFlags(Id unitId) => Core.Rules.Common.ControlFlags.None;

            public bool HasAura(Id unitId, Id auraDefId) => false;
            public int GetStacks(Id unitId, Id auraDefId) => 0;
            public bool IsImmune(Id unitId, Id school, Core.Rules.Common.EffectKind kind) => false;
            public double ConsumeAbsorb(Id unitId, Id school, double amount) => 0;
            public System.Collections.Generic.IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => System.Array.Empty<Id>();
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

        /// <summary>消费方反馈（游戏接入方第十五批）修复回归：gossip 会话（<see cref="IDialogHost.GetGossipView"/>
        /// 非空、<see cref="IDialogHost.GetStoryView"/> 为空）里 <see cref="UiIntents.ChooseDialogOption"/>
        /// 仍转发 <see cref="IDialogHost.ChooseOption"/>，不受本次"按会话类型分派"改动影响。</summary>
        [Fact]
        public void ChooseDialogOption_GossipSession_DelegatesToChooseOption()
        {
            var intents = Build(out _, out _, out _, out var dialog, out _, out _, out _, out _, out _);
            dialog.GossipViewToReturn = new GossipView(new Id("dialog.gossip_menu.ui_intents_test"), Array.Empty<(int, Id)>());

            Assert.True(intents.ChooseDialogOption(2));

            Assert.Contains(2, dialog.ChosenIndices);
            Assert.Empty(dialog.AdvancedBranchIndices);
        }

        /// <summary>消费方反馈（游戏接入方第十五批，阻塞，框架缺陷）根治：剧情会话
        /// （<see cref="IDialogHost.GetStoryView"/> 非空）里 <see cref="UiIntents.ChooseDialogOption"/>
        /// 此前恒转发 <see cref="IDialogHost.ChooseOption"/>——<see cref="IDialogHost.StartStory"/>
        /// 已把会话的 gossip 菜单态清空，<see cref="IDialogHost.ChooseOption"/> 因此恒拒绝，剧情节点
        /// 经原生对白面板恒不推进；本用例断言修复后改为转发
        /// <see cref="IDialogHost.AdvanceStory"/>。</summary>
        [Fact]
        public void ChooseDialogOption_StorySession_DelegatesToAdvanceStory()
        {
            var intents = Build(out _, out _, out _, out var dialog, out _, out _, out _, out _, out _);
            dialog.StoryViewToReturn = new StoryView(
                new Id("dialog.story_tree.ui_intents_test"), new Id("dialog.story_tree.ui_intents_test.n1"),
                new Id("l10n.ui_intents_test.n1"), null, Array.Empty<(int, Id)>());

            Assert.True(intents.ChooseDialogOption(1));

            Assert.Contains(1, dialog.AdvancedBranchIndices);
            Assert.Empty(dialog.ChosenIndices);
        }

        /// <summary>两种会话在 <see cref="Core.Gameplay.Dialog.DialogHost"/> 的会话模型里互斥
        /// （<c>StartStory</c>/<c>OpenGossip</c> 各自清空对方状态，见 <see cref="UiIntents.ChooseDialogOption"/>
        /// 判断记录），但仍显式覆盖"两者都非空"这一防御性分支：按与唯一消费方 <c>DialogPanel.RefreshUi</c>
        /// 相同的优先级（先剧情后闲聊）分派到 <see cref="IDialogHost.AdvanceStory"/>，不是无定义行为。</summary>
        [Fact]
        public void ChooseDialogOption_BothSessionViewsNonNull_PrefersStory_MatchingDialogPanelPriority()
        {
            var intents = Build(out _, out _, out _, out var dialog, out _, out _, out _, out _, out _);
            dialog.StoryViewToReturn = new StoryView(
                new Id("dialog.story_tree.ui_intents_test"), new Id("dialog.story_tree.ui_intents_test.n1"),
                new Id("l10n.ui_intents_test.n1"), null, Array.Empty<(int, Id)>());
            dialog.GossipViewToReturn = new GossipView(new Id("dialog.gossip_menu.ui_intents_test"), Array.Empty<(int, Id)>());

            Assert.True(intents.ChooseDialogOption(0));

            Assert.Contains(0, dialog.AdvancedBranchIndices);
            Assert.Empty(dialog.ChosenIndices);
        }

        /// <summary>两种会话视图都为空（未打开任何对话）时静默返回 false，不抛异常——与本类型其它
        /// 意图方法"被拒绝时静默返回失败"一贯风格一致。</summary>
        [Fact]
        public void ChooseDialogOption_NoActiveSession_ReturnsFalse()
        {
            var intents = Build(out _, out _, out _, out var dialog, out _, out _, out _, out _, out _);

            Assert.False(intents.ChooseDialogOption(0));

            Assert.Empty(dialog.ChosenIndices);
            Assert.Empty(dialog.AdvancedBranchIndices);
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

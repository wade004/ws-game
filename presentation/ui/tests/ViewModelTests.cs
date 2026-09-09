using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;
using Presentation.Ui;
using Xunit;
using FoundationSaveSystem = Core.Foundation.SaveSystem;
using TurnScheduler = Core.Foundation.SimLoop.TurnScheduler;

namespace Tests.PresentationUi
{
    /// <summary>十个视图模型各至少一条 <c>Refresh</c> 断言（见任务书验收要求）。每条测试都验证
    /// "构造/手动 Refresh 之后，公开属性反映底层宿主当前状态"，覆盖铁律 P1（只读查询）与 P2
    /// （订阅刷新）在视图模型层面的落实。</summary>
    public class ViewModelTests
    {
        private static readonly Id Health = new Id("arch.power.health");
        private static readonly Id Strength = new Id("stat.strength");

        [Fact]
        public void HudViewModel_refreshes_level_and_power_bars_from_hosts()
        {
            var world = new UiWorldFixture();
            world.PowerHost.RegisterUnit(world.PlayerId, new[] { Health });
            world.PowerHost.SetForTest(world.PlayerId, Health, 55, 100);
            world.Progression.SetForTest(world.PlayerId, 3, 10, 200);

            using var vm = new HudViewModel(world.DataSource, world.PlayerId, new[] { Health });

            Assert.Equal(3, vm.Level);
            Assert.Equal(55, vm.PowerBars[Health].Current);
            Assert.Equal(100, vm.PowerBars[Health].Max);
            Assert.False(vm.HasTarget);

            world.CurrentTarget = world.TargetId;
            world.PowerHost.RegisterUnit(world.TargetId, new[] { Health });
            world.PowerHost.SetForTest(world.TargetId, Health, 20, 40);
            vm.Refresh();

            Assert.True(vm.HasTarget);
            Assert.Equal(20, vm.TargetPowerBars[Health].Current);
        }

        /// <summary>UI-111-01 回归（见 InventoryViewModel 同名用例判断记录）：save.loaded 应在不手动
        /// 调用 Refresh 的情况下让等级/资源条快照跟上宿主最新值。</summary>
        [Fact]
        public void UI111_01_HudViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh()
        {
            var world = new UiWorldFixture();
            world.PowerHost.RegisterUnit(world.PlayerId, new[] { Health });
            world.PowerHost.SetForTest(world.PlayerId, Health, 55, 100);
            world.Progression.SetForTest(world.PlayerId, 3, 10, 200);

            using var vm = new HudViewModel(world.DataSource, world.PlayerId, new[] { Health });
            Assert.Equal(3, vm.Level);
            Assert.Equal(55, vm.PowerBars[Health].Current);

            world.Progression.SetForTest(world.PlayerId, 7, 0, 500);
            world.PowerHost.SetForTest(world.PlayerId, Health, 80, 100);
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.ui111_01")));

            Assert.Equal(7, vm.Level);
            Assert.Equal(80, vm.PowerBars[Health].Current);
        }

        /// <summary>技术债 17 收口：未传入 <c>turnScheduler</c>（既有调用方的既有用法）时，回合相关
        /// 四个新属性应保持"未启用回合制"的退化默认值——覆盖既有调用方/既有测试行为完全不变这条
        /// 判断记录。</summary>
        [Fact]
        public void HudViewModel_turn_state_defaults_when_no_scheduler_configured()
        {
            var world = new UiWorldFixture();
            world.Progression.SetForTest(world.PlayerId, 1, 0, 100);

            using var vm = new HudViewModel(world.DataSource, world.PlayerId, new[] { Health });

            Assert.False(vm.IsTurnBased);
            Assert.Null(vm.CurrentActorId);
            Assert.Equal(0, vm.RoundIndex);
            Assert.False(vm.CanEndTurn);
        }

        /// <summary>技术债 17 收口：装配了 <c>turnScheduler</c>/<c>appState</c>/
        /// <c>awaitingInputSubState</c> 时，<see cref="HudViewModel.CurrentActorId"/>/
        /// <see cref="HudViewModel.RoundIndex"/> 应随 <c>sim.turn_started</c> 一类事件自动刷新，
        /// <see cref="HudViewModel.CanEndTurn"/> 应随 <see cref="IAppStateHost.OnSubStateChanged"/>
        /// 自动刷新。同时覆盖该类型判断记录里指出的时序缺口本身：<c>sim.awaiting_input</c> 触发的
        /// 刷新严格早于子状态真正压栈，此时 <see cref="HudViewModel.CanEndTurn"/> 应仍为
        /// <c>false</c>；只有子状态真正压栈（<see cref="IAppStateHost.PushSubState"/>）之后才应变为
        /// <c>true</c>——这正是本类型选择直接订阅 <see cref="IAppStateHost.OnSubStateChanged"/>
        /// （而不只是 <c>sim.awaiting_input</c>/<c>app.state_changed</c>）的理由。</summary>
        [Fact]
        public void HudViewModel_refreshes_turn_state_from_scheduler_and_app_state()
        {
            var world = new UiWorldFixture();
            world.Progression.SetForTest(world.PlayerId, 1, 0, 100);
            var aiId = new Id("unit.beast");

            var appConfig = AppStateMachineConfig.Default();
            var awaitingInput = appConfig.AddCustomSubState("AwaitingInput");
            appConfig.AllowSubTransition(SubStateId.Explore, awaitingInput);
            appConfig.AllowSubTransition(awaitingInput, SubStateId.Explore);
            var appState = new AppStateHost(world.EventBus, appConfig);
            appState.RequestTransition(AppState.MainMenu);
            appState.RequestTransition(AppState.Loading);
            appState.RequestTransition(AppState.InWorld);

            var worldSim = new RecordingWorldSim();
            var scheduler = new TurnScheduler(
                worldSim, initiativeStatProvider: _ => 0.0, isPlayerActor: id => id.Equals(world.PlayerId), world.EventBus);

            using var vm = new HudViewModel(
                world.DataSource, world.PlayerId, new[] { Health },
                turnScheduler: scheduler, appState: appState, awaitingInputSubState: awaitingInput);

            Assert.True(vm.IsTurnBased);
            Assert.Null(vm.CurrentActorId);
            Assert.Equal(0, vm.RoundIndex);
            Assert.False(vm.CanEndTurn);

            // BeginCombat 发出 sim.turn_started，HudViewModel 的订阅应据此自动刷新（不需要手动
            // 调用 Refresh()）。
            scheduler.BeginCombat(new[] { world.PlayerId, aiId });
            Assert.Equal(world.PlayerId, vm.CurrentActorId);
            Assert.Equal(0, vm.RoundIndex);

            // 轮到玩家且没有待处理意图：NextStep 发出 sim.awaiting_input，但此刻 GameplayAssembly
            // 尚未（本测试直接模拟其后续动作）把 awaiting_input 压入应用状态子状态栈——CanEndTurn
            // 此时应仍为 false（判断记录里指出的时序缺口）。
            var step = scheduler.NextStep();
            Assert.Null(step);
            Assert.False(vm.CanEndTurn);

            // 子状态真正压栈后，OnSubStateChanged 订阅应立即触发 Refresh，CanEndTurn 变为 true。
            Assert.True(appState.PushSubState(awaitingInput));
            Assert.True(vm.CanEndTurn);

            Assert.True(appState.PopSubState());
            Assert.False(vm.CanEndTurn);
        }

        /// <summary>GP-PRES-09 收口回归（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
        /// <see cref="HudViewModel.TurnOrder"/> 应反映 <c>TurnScheduler.GetOrder()</c> 的完整队列
        /// （不只是当前行动者），<see cref="HudViewModel.ActionPointsRemaining"/> 应反映当前行动者
        /// 的剩余行动点——<c>fixed_order</c> 策略下账本同样无条件维护（见
        /// <c>TurnScheduler.TryConsumeActionPoints</c> 判断记录），本用例覆盖该策略。</summary>
        [Fact]
        public void HudViewModel_TurnOrderAndActionPoints_ReflectScheduler_FixedOrderPolicy()
        {
            var world = new UiWorldFixture();
            world.Progression.SetForTest(world.PlayerId, 1, 0, 100);
            var aiId = new Id("unit.beast");

            var worldSim = new RecordingWorldSim();
            var scheduler = new TurnScheduler(
                worldSim, initiativeStatProvider: _ => 0.0, isPlayerActor: id => id.Equals(world.PlayerId), world.EventBus);
            scheduler.Configure(Core.Foundation.SimLoop.InitiativePolicy.FixedOrder, new Dictionary<string, object> { ["action_points_per_turn"] = 3.0 });

            using var vm = new HudViewModel(world.DataSource, world.PlayerId, new[] { Health }, turnScheduler: scheduler);

            // 未开战：空队列、0 行动点（CurrentActorId 为 null）。
            Assert.Empty(vm.TurnOrder);
            Assert.Equal(0.0, vm.ActionPointsRemaining);

            scheduler.BeginCombat(new[] { world.PlayerId, aiId });

            Assert.Equal(new[] { world.PlayerId, aiId }, vm.TurnOrder);
            Assert.Equal(world.PlayerId, vm.CurrentActorId);
            Assert.Equal(3.0, vm.ActionPointsRemaining);

            // 消耗 1 点，HudViewModel 未自动感知（TryConsumeActionPoints 不发事件，见其判断记录），
            // 但手动 Refresh 之后应读到最新剩余量——证明确实转发的是实时账本，不是构造期快照。
            Assert.True(scheduler.TryConsumeActionPoints(world.PlayerId, 1.0));
            vm.Refresh();
            Assert.Equal(2.0, vm.ActionPointsRemaining);
        }

        /// <summary>同上一条用例，覆盖 <c>action_points</c> 先攻策略——09 §7.1"行动点显示"文档字面
        /// 场景，额外验证耗尽行动点后 <c>NotifyStepConsumed</c> 推进到下一行动者时
        /// <see cref="HudViewModel.ActionPointsRemaining"/> 切到新行动者自己的剩余量。</summary>
        [Fact]
        public void HudViewModel_TurnOrderAndActionPoints_ReflectScheduler_ActionPointsPolicy()
        {
            var world = new UiWorldFixture();
            world.Progression.SetForTest(world.PlayerId, 1, 0, 100);
            var aiId = new Id("unit.beast");

            var worldSim = new RecordingWorldSim();
            var scheduler = new TurnScheduler(
                worldSim, initiativeStatProvider: _ => 0.0, isPlayerActor: id => id.Equals(world.PlayerId), world.EventBus);
            scheduler.Configure(Core.Foundation.SimLoop.InitiativePolicy.ActionPoints, new Dictionary<string, object> { ["action_points_per_turn"] = 1.0 });

            using var vm = new HudViewModel(world.DataSource, world.PlayerId, new[] { Health }, turnScheduler: scheduler);

            scheduler.BeginCombat(new[] { world.PlayerId, aiId });
            Assert.Equal(new[] { world.PlayerId, aiId }, vm.TurnOrder);
            Assert.Equal(1.0, vm.ActionPointsRemaining);

            // NotifyStepConsumed 在 action_points 策略下耗尽当前行动者的行动点即推进到下一行动者
            // （sim.turn_ended/sim.turn_started 会触发 HudViewModel 自动 Refresh，不需要手动调用）。
            scheduler.NotifyStepConsumed(world.PlayerId);
            Assert.Equal(aiId, vm.CurrentActorId);
            Assert.Equal(1.0, vm.ActionPointsRemaining); // 新行动者的满额行动点，不是玩家耗尽后的 0。
        }

        [Fact]
        public void ActionBarViewModel_reads_slot_bindings_and_cooldowns()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 2.0);
            world.SkillBindings.Bind(world.PlayerId, "slot_0", fireball);

            using var vm = new ActionBarViewModel(world.DataSource, world.PlayerId, 2, world.SkillBindings);

            Assert.Equal(2, vm.SlotCount);
            Assert.Equal(fireball, vm.Slots[0].SkillId);
            Assert.Equal(2.0, vm.Slots[0].Cooldown);
            Assert.False(vm.Slots[0].Available);
            Assert.Null(vm.Slots[1].SkillId);
            Assert.False(vm.Slots[1].Available);
        }

        /// <summary>UI-111-01 回归（见 InventoryViewModel 同名用例判断记录）：save.loaded 应在不手动
        /// 调用 Refresh 的情况下让冷却快照跟上宿主最新值。</summary>
        [Fact]
        public void UI111_01_ActionBarViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBindings.Bind(world.PlayerId, "slot_0", fireball);
            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 5.0);

            using var vm = new ActionBarViewModel(world.DataSource, world.PlayerId, 1, world.SkillBindings);
            Assert.Equal(5.0, vm.Slots[0].Cooldown);

            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 0.0);
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.ui111_01")));

            Assert.Equal(0.0, vm.Slots[0].Cooldown);
            Assert.True(vm.Slots[0].Available);
        }

        [Fact]
        public void InventoryViewModel_reads_slots_and_equipped_items()
        {
            var world = new UiWorldFixture();
            var templateId = new Id("item.iron_sword");
            var instanceId = world.Inventory.AddItemForTest(world.PlayerId, templateId, 1);
            var slot = new Id("equip.main_hand");
            world.Equipment.Equip(world.PlayerId, instanceId, slot);

            using var vm = new InventoryViewModel(world.DataSource, new[] { slot });

            Assert.Single(vm.Slots);
            Assert.Equal(templateId, vm.Slots[0].TemplateId);
            Assert.Equal(instanceId, vm.EquippedSlots[slot]);
        }

        /// <summary>UI-111-01 复现与根治回归
        /// （architecture/落地计划/audit-6739f50-20260909/AUDIT_REPORT.md、
        /// core/logs/followup-core-probe.log INVENTORY-VM-SAME-MAP-LOAD 小节）：同图读档场景下，
        /// SaveSystem.Load 的抑制作用域会连带压住 item.added/item.removed/item.equipped/
        /// item.unequipped 四个业务事件本身的派发（这正是本用例只直接改写 Fake 宿主、不发那四个
        /// 事件来模拟的"读档期间"），只有在该作用域外正常派发的 save.loaded 才能让视图模型感知到
        /// 存档已经切到 B。断言"不手动调用 Refresh，VM 的 Slots/EquippedSlots 也应等于 B"——修复前
        /// VM 会永久停留在 A 快照（bug 签名同源日志 BUG-SIGNATURE-CURRENT 小节）。</summary>
        [Fact]
        public void UI111_01_InventoryViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh()
        {
            var world = new UiWorldFixture();
            var templateA = new Id("item.followup_a");
            var instanceA = world.Inventory.AddItemForTest(world.PlayerId, templateA, 1);
            var slot = new Id("equip.main_hand");
            world.Equipment.Equip(world.PlayerId, instanceA, slot);

            using var vm = new InventoryViewModel(world.DataSource, new[] { slot });
            Assert.Single(vm.Slots);
            Assert.Equal(templateA, vm.Slots[0].TemplateId);
            Assert.Equal(instanceA, vm.EquippedSlots[slot]);

            // 模拟"同图 RestoreFromSlot(B)"：直接改写背包/装备宿主到 B 状态（Fake 宿主本身不发事件，
            // 等价于 SaveSystem.Load 在抑制作用域内恢复段——不经过四个业务事件），只发 save.loaded。
            var templateB1 = new Id("item.followup_level2");
            var templateB2 = new Id("item.followup_level2_alt");
            var instanceB1 = world.Inventory.AddItemForTest(world.PlayerId, templateB1, 1);
            var instanceB2 = world.Inventory.AddItemForTest(world.PlayerId, templateB2, 1);
            world.Equipment.Equip(world.PlayerId, instanceB2, slot);

            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.ui111_01_b")));

            Assert.Equal(3, vm.Slots.Count); // A 的 1 条 + B 新增的 2 条（本 Fake 宿主不做"清空重灌"，
                                              // 只验证订阅确实触发了 Refresh、读到了宿主当前全集）。
            Assert.Contains(vm.Slots, s => s.TemplateId.Equals(templateB1));
            Assert.Contains(vm.Slots, s => s.TemplateId.Equals(templateB2));
            Assert.Equal(instanceB2, vm.EquippedSlots[slot]); // 装备已切到 B 的最新装备实例。
        }

        /// <summary>UI-111-01 回归：重复收到 save.loaded（如迁移紧接读档两次派发相关事件）不应重复
        /// 订阅——构造函数只调用一次 Subscribe，这里断言连续两次 save.loaded 都能正确触发 Refresh
        /// （而不是第二次开始失效或抛异常），且 Dispose 只需释放一次即可完全取消订阅。</summary>
        [Fact]
        public void UI111_01_InventoryViewModel_RepeatedSaveLoaded_RefreshesEachTime_NoDuplicateSubscription()
        {
            var world = new UiWorldFixture();
            var slot = new Id("equip.main_hand");
            using var vm = new InventoryViewModel(world.DataSource, new[] { slot });
            Assert.Empty(vm.Slots);

            world.Inventory.AddItemForTest(world.PlayerId, new Id("item.first"), 1);
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.slot_1")));
            Assert.Single(vm.Slots);

            world.Inventory.AddItemForTest(world.PlayerId, new Id("item.second"), 1);
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.slot_2")));
            Assert.Equal(2, vm.Slots.Count);

            vm.Dispose();
            world.Inventory.AddItemForTest(world.PlayerId, new Id("item.third"), 1);
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.slot_3")));
            Assert.Equal(2, vm.Slots.Count); // Dispose 之后不再订阅，停留在最后一次 Refresh 的快照。
        }

        [Fact]
        public void QuestLogViewModel_reads_log_from_quest_host()
        {
            var world = new UiWorldFixture();
            var questId = new Id("quest.find_the_missing_child");
            world.Quest.SeedQuestForTest(questId, QuestState.Active, new[] { 1 });

            using var vm = new QuestLogViewModel(world.DataSource, world.Quest, world.PlayerId);

            Assert.Contains(vm.Log, p => p.QuestId.Equals(questId) && p.State == QuestState.Active);
        }

        /// <summary>UI-111-01 回归（见 InventoryViewModel 同名用例判断记录）：save.loaded 应在不手动
        /// 调用 Refresh 的情况下让任务日志跟上宿主最新状态。</summary>
        [Fact]
        public void UI111_01_QuestLogViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh()
        {
            var world = new UiWorldFixture();
            var questId = new Id("quest.ui111_01");
            using var vm = new QuestLogViewModel(world.DataSource, world.Quest, world.PlayerId);
            Assert.Empty(vm.Log);

            world.Quest.SeedQuestForTest(questId, QuestState.Active, new[] { 1 });
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.ui111_01")));

            Assert.Contains(vm.Log, p => p.QuestId.Equals(questId) && p.State == QuestState.Active);
        }

        [Fact]
        public void DialogViewModel_reads_story_view()
        {
            var world = new UiWorldFixture();
            var dialog = new FakeDialogHost
            {
                StoryViewToReturn = new StoryView(
                    new Id("dialog.tree.intro"), new Id("dialog.node.start"), new Id("l10n.dialog.intro"), null,
                    new List<(int, Id)>())
            };

            using var vm = new DialogViewModel(world.DataSource, dialog, world.PlayerId);

            Assert.True(vm.IsOpen);
            Assert.Equal(new Id("dialog.tree.intro"), vm.Story!.TreeId);
        }

        /// <summary>UI-111-01 回归（见 InventoryViewModel 同名用例判断记录）：save.loaded 应在不手动
        /// 调用 Refresh、也不依赖对话框自身三个事件的情况下让 Story/Gossip 视图跟上宿主最新值。</summary>
        [Fact]
        public void UI111_01_DialogViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh()
        {
            var world = new UiWorldFixture();
            var dialog = new FakeDialogHost
            {
                StoryViewToReturn = new StoryView(
                    new Id("dialog.tree.intro"), new Id("dialog.node.start"), new Id("l10n.a"), null,
                    new List<(int, Id)>())
            };
            using var vm = new DialogViewModel(world.DataSource, dialog, world.PlayerId);
            Assert.Equal(new Id("dialog.node.start"), vm.Story!.NodeId);

            dialog.StoryViewToReturn = new StoryView(
                new Id("dialog.tree.intro"), new Id("dialog.node.after_load"), new Id("l10n.b"), null,
                new List<(int, Id)>());
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.ui111_01")));

            Assert.Equal(new Id("dialog.node.after_load"), vm.Story!.NodeId);
        }

        /// <summary>GP-05 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// 对话框开着的时候，任务被推进/物品增减/世界标志翻转都应该让视图模型主动刷新——不需要
        /// 调用方手动调用 <see cref="DialogViewModel.Refresh"/>，也不需要等对话框自身的三个事件
        /// （StoryNodeEntered/GossipOpened/Ended）。用 <see cref="FakeDialogHost.StoryViewToReturn"/>
        /// 在每次事件之间换一个新值，断言视图模型的 <see cref="DialogViewModel.Story"/> 紧跟着变化。</summary>
        [Fact]
        public void DialogViewModel_refreshes_on_quest_item_and_worldflag_events()
        {
            var world = new UiWorldFixture();
            var dialog = new FakeDialogHost
            {
                StoryViewToReturn = new StoryView(
                    new Id("dialog.tree.intro"), new Id("dialog.node.start"), new Id("l10n.a"), null,
                    new List<(int, Id)>())
            };
            using var vm = new DialogViewModel(world.DataSource, dialog, world.PlayerId);
            Assert.Equal(new Id("dialog.node.start"), vm.Story!.NodeId);

            dialog.StoryViewToReturn = new StoryView(
                new Id("dialog.tree.intro"), new Id("dialog.node.after_quest"), new Id("l10n.b"), null, new List<(int, Id)>());
            world.EventBus.PublishImmediate(new QuestAcceptedEvent(world.PlayerId, new Id("quest.sample")));
            Assert.Equal(new Id("dialog.node.after_quest"), vm.Story!.NodeId);

            dialog.StoryViewToReturn = new StoryView(
                new Id("dialog.tree.intro"), new Id("dialog.node.after_item"), new Id("l10n.c"), null, new List<(int, Id)>());
            world.EventBus.PublishImmediate(new ItemAddedEvent(world.PlayerId, new Id("item.instance_1"), new Id("item.sample"), 1));
            Assert.Equal(new Id("dialog.node.after_item"), vm.Story!.NodeId);

            dialog.StoryViewToReturn = new StoryView(
                new Id("dialog.tree.intro"), new Id("dialog.node.after_flag"), new Id("l10n.d"), null, new List<(int, Id)>());
            world.EventBus.PublishImmediate(new WorldFlagChangedEvent(
                new Id("flag.sample"), ExprValue.OfBool(false), ExprValue.OfBool(true), world.PlayerId));
            Assert.Equal(new Id("dialog.node.after_flag"), vm.Story!.NodeId);
        }

        [Fact]
        public void SkillBookViewModel_reads_known_skills_and_cooldowns()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.LearnForTest(world.PlayerId, fireball);
            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 0);

            using var vm = new SkillBookViewModel(world.DataSource, world.SkillBook, world.PlayerId);

            var entry = Assert.Single(vm.Entries);
            Assert.Equal(fireball, entry.SkillId);
            Assert.True(entry.Ready);
        }

        /// <summary>UI-111-01 回归（见 InventoryViewModel 同名用例判断记录）：save.loaded 应在不手动
        /// 调用 Refresh 的情况下让已知技能清单跟上宿主最新值。</summary>
        [Fact]
        public void UI111_01_SkillBookViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh()
        {
            var world = new UiWorldFixture();
            using var vm = new SkillBookViewModel(world.DataSource, world.SkillBook, world.PlayerId);
            Assert.Empty(vm.Entries);

            var iceLance = new Id("skill.ice_lance");
            world.SkillBook.LearnForTest(world.PlayerId, iceLance);
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.ui111_01")));

            Assert.Contains(vm.Entries, e => e.SkillId.Equals(iceLance));
        }

        [Fact]
        public void CharacterStatsViewModel_reads_stat_values_and_localized_names()
        {
            var world = new UiWorldFixture();
            world.StatHost.SetBase(world.PlayerId, Strength, 15);
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            var nameKey = new Id("l10n.stat.strength.name");
            l10n.SetTextForTest(nameKey, "Strength");

            using var vm = new CharacterStatsViewModel(world.DataSource, l10n, world.PlayerId, new[] { (Strength, nameKey) });

            var entry = Assert.Single(vm.Entries);
            Assert.Equal(15, entry.Value);
            Assert.Equal("Strength", entry.DisplayName);
        }

        /// <summary>UI-111-01 回归（见 InventoryViewModel 同名用例判断记录）：save.loaded 应在不手动
        /// 调用 Refresh 的情况下让属性快照跟上宿主最新值。</summary>
        [Fact]
        public void UI111_01_CharacterStatsViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh()
        {
            var world = new UiWorldFixture();
            world.StatHost.SetBase(world.PlayerId, Strength, 15);
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            var nameKey = new Id("l10n.stat.strength.name");
            l10n.SetTextForTest(nameKey, "Strength");

            using var vm = new CharacterStatsViewModel(world.DataSource, l10n, world.PlayerId, new[] { (Strength, nameKey) });
            Assert.Equal(15, vm.Entries[0].Value);

            world.StatHost.SetBase(world.PlayerId, Strength, 42);
            world.EventBus.PublishImmediate(new FoundationSaveSystem.SaveLoadedEvent(new Id("save.ui111_01")));

            Assert.Equal(42, vm.Entries[0].Value);
        }

        [Fact]
        public void SettingsViewModel_reads_locale_bindings_and_conflicts()
        {
            var world = new UiWorldFixture();
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us"), new Id("l10n.zh_cn") });
            var inputMap = new FakeInputMapHost();
            inputMap.SetBindingsForTest("input.action.move", "key:w");
            inputMap.SetBindingsForTest("input.action.jump", "key:w");

            var audioVolume = new FakeAudioLayerVolumeHost(new[] { "sfx" });
            audioVolume.SetVolume("sfx", 0.7);
            using var vm = new SettingsViewModel(
                world.DataSource, l10n, inputMap, new[] { "input.action.move", "input.action.jump" }, audioVolume);

            Assert.Equal(new Id("l10n.en_us"), vm.Locale);
            Assert.Equal(0.7, vm.LayerVolumes["sfx"]);
            var moveRow = vm.Bindings[0];
            Assert.Equal("input.action.move", moveRow.ActionName);
            Assert.Contains("input.action.jump", moveRow.Conflicts[0]);
        }

        [Fact]
        public void SaveSlotsViewModel_reads_slots_from_save_system()
        {
            var fileSystem = new StubFileSystem();
            var saveSystem = new FoundationSaveSystem.SaveSystem(
                fileSystem, new FoundationSaveSystem.SaveSystemOptions(new Id("game.demo")));
            saveSystem.Save(new FoundationSaveSystem.SaveRequest(new Id("save.slot_1"), "2026-09-05T00:00:00Z"));

            var world = new UiWorldFixture();
            using var vm = new SaveSlotsViewModel(world.DataSource, saveSystem);

            Assert.Contains(vm.Slots, s => s.SlotId.Equals(new Id("save.slot_1")));
        }

        [Fact]
        public void PauseMenuViewModel_reflects_app_state()
        {
            var world = new UiWorldFixture();
            var appState = new AppStateHost(world.EventBus);
            appState.RequestTransition(AppState.MainMenu);
            appState.RequestTransition(AppState.Loading);
            appState.RequestTransition(AppState.InWorld);

            var options = new[] { new PauseMenuOption(new Id("shell.action.resume"), new Id("l10n.pause.resume")) };
            using var vm = new PauseMenuViewModel(world.DataSource, appState, options);

            Assert.Equal(AppState.InWorld, vm.CurrentState);
            Assert.False(vm.IsPaused);
            Assert.Single(vm.Options);

            appState.RequestTransition(AppState.Pause);
            vm.Refresh();
            Assert.True(vm.IsPaused);
        }
    }
}

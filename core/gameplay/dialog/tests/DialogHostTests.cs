using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>单个测试用例的最小装配。</summary>
    internal sealed class Harness
    {
        public readonly IEventBus Bus;
        public readonly IAppStateHost AppState;
        public readonly IHookRegistry Hooks = TestSupport.NewHookRegistry();
        public readonly FakeQuestHost QuestHost = new FakeQuestHost();
        public readonly Core.Gameplay.WorldState.IWorldState WorldState;
        public readonly FakeSkillHost SkillHost = new FakeSkillHost();
        public readonly List<IEvent> Published = new List<IEvent>();

        public Id? VendorOpenedFor;
        public Id? TeleportedTo;
        public bool SaveRequested;
        public Id? EncounterStarted;

        public readonly DialogHost Host;

        public Harness(
            IEnumerable<GossipMenuDefinition> menus,
            IEnumerable<StoryTreeDefinition> trees,
            Func<string, IReadOnlyList<ExprValue>, ExprValue>? playerGroup = null)
        {
            Bus = TestSupport.NewEventBus();
            AppState = TestSupport.NewAppStateHostInWorld(Bus);
            WorldState = TestSupport.NewWorldState(Bus);

            foreach (var key in new[]
                     {
                         DialogEventKeys.GossipOpened, DialogEventKeys.GossipActionExecuted,
                         DialogEventKeys.StoryNodeEntered, DialogEventKeys.Ended,
                     })
            {
                Bus.Subscribe(key, evt => Published.Add(evt));
            }

            Host = new DialogHost(
                menus, trees, Bus, new TestExprHostFactory(playerGroup), AppState, QuestHost, Hooks, WorldState, SkillHost,
                vendorOpenRequested: (unit, npc) => VendorOpenedFor = npc,
                teleportRequested: (unit, target) => TeleportedTo = target,
                saveRequested: unit => SaveRequested = true,
                encounterStartRequested: encRef => EncounterStarted = encRef);
        }

        public IReadOnlyList<T> PublishedOf<T>() where T : IEvent => Published.OfType<T>().ToArray();
    }

    public class DialogHostTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id Npc = TestSupport.Npc;
        private static readonly IExprSchema Schema = QuestExprSchemaEntries.BuildParsingSchema();

        private static GossipActionDef Action(DialogActionKind kind, Id? @ref = null, JsonObject? @params = null) =>
            new GossipActionDef(kind, @ref, @params);

        // ---------------------------------------------------------------
        // OpenGossip / visible_if 过滤
        // ---------------------------------------------------------------

        [Fact]
        public void OpenGossip_FiltersOptionsByVisibleIf()
        {
            var visibleIf = ExprParser.Parse("player.level >= 5", Schema);
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_low"), null, new[] { Action(DialogActionKind.Save) }),
                new GossipOption(new Id("l10n.opt_high"), visibleIf, new[] { Action(DialogActionKind.Save) }),
            });
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>(),
                playerGroup: (key, args) => key == "level" ? ExprValue.OfInt(1) : ExprValue.OfBool(false));

            var view = h.Host.OpenGossip(Player, Npc, menu.Id);

            var only = Assert.Single(view.Options);
            Assert.Equal(0, only.Index);
            Assert.Equal(new Id("l10n.opt_low"), only.TextKey);
        }

        [Fact]
        public void OpenGossip_PushesDialogSubStateAndFiresGossipOpenedEvent()
        {
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_a"), null, new[] { Action(DialogActionKind.Save) }),
            });
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());

            h.Host.OpenGossip(Player, Npc, menu.Id);

            Assert.True(h.AppState.CurrentSubState.HasValue);
            Assert.Equal(SubStateId.Dialog, h.AppState.CurrentSubState!.Value);
            var evt = Assert.Single(h.PublishedOf<GossipOpenedEvent>());
            Assert.Equal(menu.Id, evt.MenuId);
            Assert.Equal(Npc, evt.NpcId);
        }

        // ---------------------------------------------------------------
        // GP-05：ChooseOption/AdvanceStory 执行前重验 VisibleIf/Condition
        // ---------------------------------------------------------------

        /// <summary>GP-05 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// 打开菜单时选项可见（level>=5 成立），点击之前世界状态变化导致条件不再成立——旧实现
        /// <c>ChooseOption</c> 只按原始 index 执行，不重验，会直接把 Save 动作跑掉；修复后必须拒绝
        /// 执行、不产生任何副作用。</summary>
        [Fact]
        public void ChooseOption_RevalidatesVisibleIf_RejectsAndNoSideEffect_WhenNoLongerVisible()
        {
            var visibleIf = ExprParser.Parse("player.level >= 5", Schema);
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_high"), visibleIf, new[] { Action(DialogActionKind.Save) }),
            });
            var level = 5;
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>(),
                playerGroup: (key, args) => key == "level" ? ExprValue.OfInt(level) : ExprValue.OfBool(false));

            var view = h.Host.OpenGossip(Player, Npc, menu.Id);
            Assert.Single(view.Options); // 打开时可见

            level = 1; // 世界状态变化：条件不再成立

            var result = h.Host.ChooseOption(Player, 0);

            Assert.False(result);
            Assert.False(h.SaveRequested); // 没有执行 Save 动作
            Assert.Empty(h.PublishedOf<GossipActionExecutedEvent>());
        }

        /// <summary>初始就隐藏（level 从未达标）的选项即使被直接按 index 调用也必须拒绝——不依赖
        /// "曾经显示过"，覆盖"客户端缓存了过期/伪造 index"的场景。</summary>
        [Fact]
        public void ChooseOption_InitiallyHiddenOption_RejectsWhenCalledDirectlyByIndex()
        {
            var visibleIf = ExprParser.Parse("player.level >= 5", Schema);
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_low"), null, new[] { Action(DialogActionKind.Save) }),
                new GossipOption(new Id("l10n.opt_high"), visibleIf, new[] { Action(DialogActionKind.Save) }),
            });
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>(),
                playerGroup: (key, args) => key == "level" ? ExprValue.OfInt(1) : ExprValue.OfBool(false));
            h.Host.OpenGossip(Player, Npc, menu.Id);

            var result = h.Host.ChooseOption(Player, 1); // 直接用隐藏选项的原始 index

            Assert.False(result);
            Assert.False(h.SaveRequested);
        }

        /// <summary>条件仍然成立时必须正常执行——确认重验不会误伤真正可见的选项。</summary>
        [Fact]
        public void ChooseOption_RevalidatesVisibleIf_ExecutesNormally_WhenStillVisible()
        {
            var visibleIf = ExprParser.Parse("player.level >= 5", Schema);
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_high"), visibleIf, new[] { Action(DialogActionKind.Save) }),
            });
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>(),
                playerGroup: (key, args) => key == "level" ? ExprValue.OfInt(5) : ExprValue.OfBool(false));
            h.Host.OpenGossip(Player, Npc, menu.Id);

            var result = h.Host.ChooseOption(Player, 0);

            Assert.True(result);
            Assert.True(h.SaveRequested);
        }

        /// <summary>AdvanceStory 侧同一件事的复现：进入节点时分支可见，推进之前条件失效——必须拒绝，
        /// 不切换到下一节点、不发 StoryNodeEnteredEvent。</summary>
        [Fact]
        public void AdvanceStory_RevalidatesCondition_RejectsAndDoesNotTransition_WhenNoLongerVisible()
        {
            var condition = ExprParser.Parse("player.level >= 5", Schema);
            var treeId = new Id("dialog.sample_tree");
            var node2Id = new Id("dialog.node_2");
            var level = 5;
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.branch_gated"), condition, node2Id),
                }, null),
                new StoryNodeDefinition(node2Id, new Id("l10n.node2"), null, Array.Empty<StoryBranchDef>(), null),
            });
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree },
                playerGroup: (key, args) => key == "level" ? ExprValue.OfInt(level) : ExprValue.OfBool(false));
            h.Host.StartStory(Player, treeId);
            Assert.Single(h.Host.GetStoryView(Player)!.VisibleBranches); // 进入时可见

            level = 1; // 世界状态变化：条件不再成立

            var result = h.Host.AdvanceStory(Player, 0);

            Assert.False(result);
            Assert.Equal(new Id("dialog.node_1"), h.Host.GetStoryView(Player)!.NodeId); // 未推进
            Assert.Single(h.PublishedOf<StoryNodeEnteredEvent>()); // 只有 StartStory 那一次
        }

        // ---------------------------------------------------------------
        // 十种动作
        // ---------------------------------------------------------------

        [Fact]
        public void ChooseOption_Vendor_InvokesVendorCallback()
        {
            var menu = SingleActionMenu(Action(DialogActionKind.Vendor));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            Assert.True(h.Host.ChooseOption(Player, 0));

            Assert.Equal(Npc, h.VendorOpenedFor);
        }

        [Fact]
        public void ChooseOption_QuestAccept_CallsQuestHostAccept()
        {
            var questId = new Id("quest.sample_deliver_letter");
            var menu = SingleActionMenu(Action(DialogActionKind.QuestAccept, questId));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            var call = Assert.Single(h.QuestHost.AcceptCalls);
            Assert.Equal(Player, call.UnitId);
            Assert.Equal(questId, call.QuestId);
        }

        [Fact]
        public void ChooseOption_QuestTurnIn_CallsQuestHostTurnIn()
        {
            var questId = new Id("quest.sample_deliver_letter");
            var menu = SingleActionMenu(Action(DialogActionKind.QuestTurnIn, questId));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            var call = Assert.Single(h.QuestHost.TurnInCalls);
            Assert.Equal(questId, call.QuestId);
        }

        [Fact]
        public void ChooseOption_Teleport_InvokesTeleportCallback()
        {
            var target = new Id("world.spawn.town_square");
            var menu = SingleActionMenu(Action(DialogActionKind.Teleport, target));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            Assert.Equal(target, h.TeleportedTo);
        }

        [Fact]
        public void ChooseOption_Save_InvokesSaveCallback()
        {
            var menu = SingleActionMenu(Action(DialogActionKind.Save));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            Assert.True(h.SaveRequested);
        }

        [Fact]
        public void ChooseOption_SetFlag_WritesWorldState()
        {
            var flagKey = new Id("world.bridge.repaired");
            var paramsObj = new JsonObjectBuilder().Add("value", JsonBool.True).Build();
            var menu = SingleActionMenu(Action(DialogActionKind.SetFlag, flagKey, paramsObj));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            Assert.True(h.WorldState.Has(flagKey));
            Assert.True(h.WorldState.Get(flagKey).AsBool);
        }

        [Fact]
        public void ChooseOption_StartEncounter_InvokesEncounterStartCallback()
        {
            var encRef = new Id("encounter.sample_ambush");
            var menu = SingleActionMenu(Action(DialogActionKind.StartEncounter, encRef));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            Assert.Equal(encRef, h.EncounterStarted);
        }

        [Fact]
        public void ChooseOption_CastSkill_CallsSkillHostCastSkillWithNpcAsCaster()
        {
            var skillId = new Id("skill.teach_fireball");
            var menu = SingleActionMenu(Action(DialogActionKind.CastSkill, skillId));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            var call = Assert.Single(h.SkillHost.CastCalls);
            Assert.Equal(Npc, call.CasterId);
            Assert.Equal(skillId, call.SkillId);
            Assert.Equal(Player, Assert.Single(call.Targets));
        }

        [Fact]
        public void ChooseOption_StartStory_TransitionsIntoStoryWithoutDoublePushingSubState()
        {
            var treeId = new Id("dialog.sample_tree");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, Array.Empty<StoryBranchDef>(), null),
            });
            var menu = SingleActionMenu(Action(DialogActionKind.StartStory, treeId));
            var h = new Harness(new[] { menu }, new[] { tree });
            h.Host.OpenGossip(Player, Npc, menu.Id);
            Assert.Equal(1, h.AppState.SubStateStack.Count(s => s.Equals(SubStateId.Dialog)));

            h.Host.ChooseOption(Player, 0);

            // 仍然只有一层 Dialog 子状态（未因 gossip→story 链路重复 Push）。
            Assert.Equal(1, h.AppState.SubStateStack.Count(s => s.Equals(SubStateId.Dialog)));
            var storyEvt = Assert.Single(h.PublishedOf<StoryNodeEnteredEvent>());
            Assert.Equal(treeId, storyEvt.TreeId);
            Assert.NotNull(h.Host.GetStoryView(Player));
        }

        [Fact]
        public void ChooseOption_Script_InvokesHookRegistry()
        {
            var hookId = new Id("found.hook.sample_dialog_script");
            var invoked = false;
            Id? unitArg = null;
            var menu = SingleActionMenu(Action(DialogActionKind.Script, hookId));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Hooks.DeclareHookPoint(hookId, "(unitId, npcId)");
            h.Hooks.Register(hookId, args =>
            {
                invoked = true;
                unitArg = args.Get<Id>("unitId");
            }, 0);
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            Assert.True(invoked);
            Assert.Equal(Player, unitArg);
        }

        [Fact]
        public void ChooseOption_FiresGossipActionExecutedEventPerAction()
        {
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_a"), null, new[] { Action(DialogActionKind.Save), Action(DialogActionKind.Vendor) }),
            });
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.ChooseOption(Player, 0);

            Assert.Equal(2, h.PublishedOf<GossipActionExecutedEvent>().Count);
        }

        // ---------------------------------------------------------------
        // Close / 子状态
        // ---------------------------------------------------------------

        [Fact]
        public void Close_PopsSubStateAndFiresDialogEndedEvent()
        {
            var menu = SingleActionMenu(Action(DialogActionKind.Save));
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            Assert.True(h.Host.Close(Player));

            Assert.True(h.AppState.CurrentSubState.HasValue);
            Assert.Equal(SubStateId.Explore, h.AppState.CurrentSubState!.Value);
            Assert.Single(h.PublishedOf<DialogEndedEvent>());
        }

        [Fact]
        public void Close_WithoutOpenSession_ReturnsFalse()
        {
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), Array.Empty<StoryTreeDefinition>());

            Assert.False(h.Host.Close(Player));
        }

        // ---------------------------------------------------------------
        // 剧情树：进入/分支条件/结束
        // ---------------------------------------------------------------

        [Fact]
        public void StartStory_EntersFirstNodeAndInvokesPerformanceHook()
        {
            var hookId = new Id("found.hook.sample_cutscene");
            var invoked = false;
            var treeId = new Id("dialog.sample_tree");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, Array.Empty<StoryBranchDef>(), hookId),
            });
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree });
            h.Hooks.DeclareHookPoint(hookId, "()");
            h.Hooks.Register(hookId, _ => invoked = true, 0);

            Assert.True(h.Host.StartStory(Player, treeId));

            Assert.True(invoked);
            Assert.True(h.AppState.CurrentSubState.HasValue);
            Assert.Equal(SubStateId.Dialog, h.AppState.CurrentSubState!.Value);
            var evt = Assert.Single(h.PublishedOf<StoryNodeEnteredEvent>());
            Assert.Equal(new Id("dialog.node_1"), evt.NodeId);
        }

        [Fact]
        public void GetStoryView_FiltersBranchesByCondition()
        {
            var condition = ExprParser.Parse("player.level >= 5", Schema);
            var treeId = new Id("dialog.sample_tree");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.branch_always"), null, null),
                    new StoryBranchDef(new Id("l10n.branch_gated"), condition, null),
                }, null),
            });
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree },
                playerGroup: (key, args) => key == "level" ? ExprValue.OfInt(1) : ExprValue.OfBool(false));
            h.Host.StartStory(Player, treeId);

            var view = h.Host.GetStoryView(Player);

            Assert.NotNull(view);
            var only = Assert.Single(view!.VisibleBranches);
            Assert.Equal(0, only.Index);
        }

        // ---------------------------------------------------------------
        // GetGossipView（对称于 GetStoryView 的契约缺口补齐）
        // ---------------------------------------------------------------

        [Fact]
        public void GetGossipView_ReturnsNull_WhenNoSessionOpen()
        {
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), Array.Empty<StoryTreeDefinition>());

            Assert.Null(h.Host.GetGossipView(Player));
        }

        [Fact]
        public void GetGossipView_AfterOpenGossip_ReflectsSameOptionsAsOpenGossipReturnValue()
        {
            var visibleIf = ExprParser.Parse("player.level >= 5", Schema);
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_low"), null, new[] { Action(DialogActionKind.Save) }),
                new GossipOption(new Id("l10n.opt_high"), visibleIf, new[] { Action(DialogActionKind.Save) }),
            });
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>(),
                playerGroup: (key, args) => key == "level" ? ExprValue.OfInt(1) : ExprValue.OfBool(false));

            h.Host.OpenGossip(Player, Npc, menu.Id);
            var view = h.Host.GetGossipView(Player);

            Assert.NotNull(view);
            Assert.Equal(menu.Id, view!.MenuId);
            var only = Assert.Single(view.Options);
            Assert.Equal(0, only.Index);
            Assert.Equal(new Id("l10n.opt_low"), only.TextKey);
        }

        [Fact]
        public void GetGossipView_ReturnsNull_WhenSessionMovedIntoStory()
        {
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_a"), null, new[] { Action(DialogActionKind.StartStory, new Id("dialog.sample_tree")) }),
            });
            var treeId = new Id("dialog.sample_tree");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, Array.Empty<StoryBranchDef>(), null),
            });
            var h = new Harness(new[] { menu }, new[] { tree });

            h.Host.OpenGossip(Player, Npc, menu.Id);
            h.Host.ChooseOption(Player, 0);

            Assert.Null(h.Host.GetGossipView(Player));
            Assert.NotNull(h.Host.GetStoryView(Player));
        }

        [Fact]
        public void GetGossipView_ReturnsNull_AfterClose()
        {
            var menu = new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_a"), null, new[] { Action(DialogActionKind.Save) }),
            });
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            Assert.True(h.Host.Close(Player));

            Assert.Null(h.Host.GetGossipView(Player));
        }

        [Fact]
        public void AdvanceStory_ToNextNode_FiresStoryNodeEnteredEvent()
        {
            var treeId = new Id("dialog.sample_tree");
            var node2Id = new Id("dialog.node_2");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.continue"), null, node2Id),
                }, null),
                new StoryNodeDefinition(node2Id, new Id("l10n.node2"), null, Array.Empty<StoryBranchDef>(), null),
            });
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree });
            h.Host.StartStory(Player, treeId);

            Assert.True(h.Host.AdvanceStory(Player, 0));

            Assert.Equal(2, h.PublishedOf<StoryNodeEnteredEvent>().Count);
            Assert.Equal(node2Id, h.Host.GetStoryView(Player)!.NodeId);
        }

        [Fact]
        public void AdvanceStory_ToTerminalBranch_ClosesDialogAndFiresEnded()
        {
            var treeId = new Id("dialog.sample_tree");
            var tree = new StoryTreeDefinition(treeId, new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.node1"), null, new[]
                {
                    new StoryBranchDef(new Id("l10n.end"), null, null),
                }, null),
            });
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree });
            h.Host.StartStory(Player, treeId);

            Assert.True(h.Host.AdvanceStory(Player, 0));

            Assert.True(h.AppState.CurrentSubState.HasValue);
            Assert.Equal(SubStateId.Explore, h.AppState.CurrentSubState!.Value);
            Assert.Single(h.PublishedOf<DialogEndedEvent>());
            Assert.Null(h.Host.GetStoryView(Player));
        }

        // ---------------------------------------------------------------
        // 树成环校验（DFS）
        // ---------------------------------------------------------------

        [Fact]
        public void StoryTreeDefinition_HasCycle_DetectsCycleBetweenNodes()
        {
            var nodeA = new Id("dialog.node_a");
            var nodeB = new Id("dialog.node_b");
            var tree = new StoryTreeDefinition(new Id("dialog.cyclic_tree"), new[]
            {
                new StoryNodeDefinition(nodeA, new Id("l10n.a"), null, new[] { new StoryBranchDef(new Id("l10n.to_b"), null, nodeB) }, null),
                new StoryNodeDefinition(nodeB, new Id("l10n.b"), null, new[] { new StoryBranchDef(new Id("l10n.to_a"), null, nodeA) }, null),
            });

            Assert.True(tree.HasCycle(out var cyclePath));
            Assert.NotEmpty(cyclePath);
        }

        [Fact]
        public void StoryTreeDefinition_Acyclic_ReportsNoCycle()
        {
            var tree = new StoryTreeDefinition(new Id("dialog.linear_tree"), new[]
            {
                new StoryNodeDefinition(new Id("dialog.node_1"), new Id("l10n.n1"), null,
                    new[] { new StoryBranchDef(new Id("l10n.next"), null, new Id("dialog.node_2")) }, null),
                new StoryNodeDefinition(new Id("dialog.node_2"), new Id("l10n.n2"), null, Array.Empty<StoryBranchDef>(), null),
            });

            Assert.False(tree.HasCycle(out _));
        }

        // ---------------------------------------------------------------
        // 帮助方法
        // ---------------------------------------------------------------

        private static GossipMenuDefinition SingleActionMenu(GossipActionDef action) =>
            new GossipMenuDefinition(new Id("dialog.sample_menu"), new[]
            {
                new GossipOption(new Id("l10n.opt_a"), null, new[] { action }),
            });
    }
}

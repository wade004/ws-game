using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;
using Core.Rules.Common;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// <see cref="IDialogHost"/> 的默认实现（见 08 第 3 节 Dialog、第 9 节汇总表、03 第 2 节
    /// Dialog 子状态表）。
    /// <para>
    /// 子状态判断记录：gossip 与 story 共用同一个"对话会话"概念——见类型内 <see cref="_sessions"/>：
    /// 一个单位同一时刻至多一个打开的对话会话（gossip 菜单或剧情树二选一，可从 gossip 经
    /// <c>start_story</c> 动作转为剧情而不离开会话，见 <see cref="StartStory"/>）；只在"当前没有
    /// 会话 → 打开会话"时 <see cref="IAppStateHost.PushSubState"/>，只在"会话结束"时
    /// <see cref="IAppStateHost.PopSubState"/>，避免 gossip→story 链路重复 Push/需要重复 Pop 才能
    /// 真正退出 Dialog 子状态。
    /// </para>
    /// </summary>
    public sealed class DialogHost : IDialogHost
    {
        private readonly Dictionary<Id, GossipMenuDefinition> _gossipMenus = new Dictionary<Id, GossipMenuDefinition>();
        private readonly Dictionary<Id, StoryTreeDefinition> _storyTrees = new Dictionary<Id, StoryTreeDefinition>();
        private readonly Dictionary<Id, Session> _sessions = new Dictionary<Id, Session>();

        private readonly IEventBus _eventBus;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IAppStateHost _appStateHost;
        private readonly IQuestHost _questHost;
        private readonly IHookRegistry _hookRegistry;
        private readonly IWorldState _worldState;
        private readonly ISkillHost _skillHost;
        private readonly VendorOpenRequestedCallback? _vendorOpenRequested;
        private readonly TeleportRequestedCallback? _teleportRequested;
        private readonly SaveRequestedCallback? _saveRequested;
        private readonly EncounterStartRequestedCallback? _encounterStartRequested;
        private readonly IExprDiagnostics _exprDiagnostics;
        private readonly IDialogDiagnostics _diagnostics;

        public DialogHost(
            IEnumerable<GossipMenuDefinition> gossipMenus,
            IEnumerable<StoryTreeDefinition> storyTrees,
            IEventBus eventBus,
            IExprHostFactory exprHostFactory,
            IAppStateHost appStateHost,
            IQuestHost questHost,
            IHookRegistry hookRegistry,
            IWorldState worldState,
            ISkillHost skillHost,
            VendorOpenRequestedCallback? vendorOpenRequested = null,
            TeleportRequestedCallback? teleportRequested = null,
            SaveRequestedCallback? saveRequested = null,
            EncounterStartRequestedCallback? encounterStartRequested = null,
            IExprDiagnostics? exprDiagnostics = null,
            IDialogDiagnostics? diagnostics = null)
        {
            if (gossipMenus == null) throw new ArgumentNullException(nameof(gossipMenus));
            if (storyTrees == null) throw new ArgumentNullException(nameof(storyTrees));
            foreach (var menu in gossipMenus) _gossipMenus[menu.Id] = menu;
            foreach (var tree in storyTrees) _storyTrees[tree.Id] = tree;

            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _appStateHost = appStateHost ?? throw new ArgumentNullException(nameof(appStateHost));
            _questHost = questHost ?? throw new ArgumentNullException(nameof(questHost));
            _hookRegistry = hookRegistry ?? throw new ArgumentNullException(nameof(hookRegistry));
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _skillHost = skillHost ?? throw new ArgumentNullException(nameof(skillHost));
            _vendorOpenRequested = vendorOpenRequested;
            _teleportRequested = teleportRequested;
            _saveRequested = saveRequested;
            _encounterStartRequested = encounterStartRequested;
            _exprDiagnostics = exprDiagnostics ?? new ExprDiagnosticsRecorder();
            _diagnostics = diagnostics ?? new InMemoryDialogDiagnostics();
        }

        public GossipView OpenGossip(Id unitId, Id npcId, Id menuId)
        {
            var menu = RequireMenu(menuId);
            var session = GetOrOpenSession(unitId);
            session.GossipMenuId = menuId;
            session.NpcId = npcId;
            session.StoryTreeId = null;
            session.StoryNodeId = null;

            var host = _exprHostFactory.CreateFor(unitId, npcId, null);
            var options = new List<(int, Id)>();
            for (var i = 0; i < menu.Options.Count; i++)
            {
                var option = menu.Options[i];
                if (option.VisibleIf == null || ExprEvaluator.EvaluateBool(option.VisibleIf, host, _exprDiagnostics))
                {
                    options.Add((i, option.TextKey));
                }
            }

            Publish(new GossipOpenedEvent(unitId, npcId, menuId));
            return new GossipView(menuId, options);
        }

        public bool ChooseOption(Id unitId, int index)
        {
            if (!_sessions.TryGetValue(unitId, out var session) || !session.GossipMenuId.HasValue || !session.NpcId.HasValue)
            {
                return false;
            }

            var menu = RequireMenu(session.GossipMenuId.Value);
            if (index < 0 || index >= menu.Options.Count)
            {
                return false;
            }

            var menuId = session.GossipMenuId.Value;
            var npcId = session.NpcId.Value;
            var option = menu.Options[index];

            foreach (var action in option.Actions)
            {
                ExecuteAction(unitId, npcId, menuId, action);
            }
            return true;
        }

        public bool Close(Id unitId)
        {
            if (!_sessions.Remove(unitId))
            {
                return false;
            }

            PopDialogSubState(unitId);
            Publish(new DialogEndedEvent(unitId));
            return true;
        }

        public bool StartStory(Id unitId, Id treeId)
        {
            var tree = RequireTree(treeId);
            var session = GetOrOpenSession(unitId);
            // 判断记录：不清空 session.NpcId——若本次 StartStory 是由某个 gossip 动作触发（同一会话
            // 内从菜单转入剧情），NpcId 仍保留供 GetStoryView 的 Expr 上下文（target 分组）使用；
            // 若是直接开场（无 gossip 前置），NpcId 本就是 null，不影响。
            session.GossipMenuId = null;
            session.StoryTreeId = treeId;

            EnterStoryNode(unitId, session, tree.FirstNode);
            return true;
        }

        public StoryView? GetStoryView(Id unitId)
        {
            if (!TryGetStorySession(unitId, out var tree, out var session, out var node))
            {
                return null;
            }

            var host = _exprHostFactory.CreateFor(unitId, node.SpeakerRef, null);
            var branches = new List<(int, Id)>();
            for (var i = 0; i < node.Branches.Count; i++)
            {
                var branch = node.Branches[i];
                if (branch.Condition == null || ExprEvaluator.EvaluateBool(branch.Condition, host, _exprDiagnostics))
                {
                    branches.Add((i, branch.TextKey));
                }
            }

            return new StoryView(tree.Id, node.Id, node.TextKey, node.SpeakerRef, branches);
        }

        public bool AdvanceStory(Id unitId, int branchIndex)
        {
            if (!TryGetStorySession(unitId, out var tree, out var session, out var node))
            {
                return false;
            }
            if (branchIndex < 0 || branchIndex >= node.Branches.Count)
            {
                return false;
            }

            var branch = node.Branches[branchIndex];
            if (!branch.NextNodeId.HasValue)
            {
                return Close(unitId);
            }

            var nextNode = tree.RequireNode(branch.NextNodeId.Value);
            EnterStoryNode(unitId, session, nextNode);
            return true;
        }

        // -------------------------------------------------------------
        // 内部帮助方法
        // -------------------------------------------------------------

        private GossipMenuDefinition RequireMenu(Id menuId)
        {
            if (!_gossipMenus.TryGetValue(menuId, out var menu))
            {
                throw new ArgumentException($"未登记的 dialog.gossip_menu id：\"{menuId}\"", nameof(menuId));
            }
            return menu;
        }

        private StoryTreeDefinition RequireTree(Id treeId)
        {
            if (!_storyTrees.TryGetValue(treeId, out var tree))
            {
                throw new ArgumentException($"未登记的 dialog.story_tree id：\"{treeId}\"", nameof(treeId));
            }
            return tree;
        }

        private Session GetOrOpenSession(Id unitId)
        {
            if (_sessions.TryGetValue(unitId, out var existing))
            {
                return existing;
            }

            if (!_appStateHost.PushSubState(SubStateId.Dialog))
            {
                _diagnostics.Warn($"DialogHost：PushSubState(Dialog) 失败（unitId={unitId}），继续以逻辑会话状态运行");
            }

            var session = new Session();
            _sessions[unitId] = session;
            return session;
        }

        private void PopDialogSubState(Id unitId)
        {
            if (_appStateHost.CurrentSubState.HasValue && _appStateHost.CurrentSubState.Value.Equals(SubStateId.Dialog))
            {
                if (!_appStateHost.PopSubState())
                {
                    _diagnostics.Warn($"DialogHost：PopSubState 失败（unitId={unitId}）");
                }
            }
            else
            {
                _diagnostics.Warn($"DialogHost.Close({unitId})：当前子状态不是 Dialog，跳过 PopSubState（见任务书判断记录）");
            }
        }

        private bool TryGetStorySession(Id unitId, out StoryTreeDefinition tree, out Session session, out StoryNodeDefinition node)
        {
            tree = null!;
            session = null!;
            node = null!;

            if (!_sessions.TryGetValue(unitId, out var s) || !s.StoryTreeId.HasValue || !s.StoryNodeId.HasValue)
            {
                return false;
            }

            var t = RequireTree(s.StoryTreeId.Value);
            if (!t.TryGetNode(s.StoryNodeId.Value, out var n))
            {
                return false;
            }

            tree = t;
            session = s;
            node = n;
            return true;
        }

        private void EnterStoryNode(Id unitId, Session session, StoryNodeDefinition node)
        {
            session.StoryNodeId = node.Id;
            if (node.PerformanceHookRef.HasValue)
            {
                _hookRegistry.Invoke(node.PerformanceHookRef.Value, HookArgs.Empty);
            }
            Publish(new StoryNodeEnteredEvent(unitId, session.StoryTreeId!.Value, node.Id));
        }

        private void ExecuteAction(Id unitId, Id npcId, Id menuId, GossipActionDef action)
        {
            switch (action.Kind)
            {
                case DialogActionKind.Vendor:
                    if (_vendorOpenRequested != null) _vendorOpenRequested(unitId, npcId);
                    else _diagnostics.Warn($"DialogHost：未注入 VendorOpenRequestedCallback，跳过 vendor 动作（menuId={menuId}）");
                    break;

                case DialogActionKind.QuestAccept:
                    _questHost.Accept(unitId, action.Ref!.Value);
                    break;

                case DialogActionKind.QuestTurnIn:
                    _questHost.TurnIn(unitId, action.Ref!.Value);
                    break;

                case DialogActionKind.Teleport:
                    if (_teleportRequested != null) _teleportRequested(unitId, action.Ref!.Value);
                    else _diagnostics.Warn($"DialogHost：未注入 TeleportRequestedCallback，跳过 teleport 动作（menuId={menuId}）");
                    break;

                case DialogActionKind.Save:
                    if (_saveRequested != null) _saveRequested(unitId);
                    else _diagnostics.Warn($"DialogHost：未注入 SaveRequestedCallback，跳过 save 动作（menuId={menuId}）");
                    break;

                case DialogActionKind.SetFlag:
                {
                    var value = action.Params != null && action.Params.TryGetValue("value", out var raw)
                        ? Core.Gameplay.Common.ExprValueJson.Parse(raw)
                        : ExprValue.OfBool(true);
                    _worldState.Set(action.Ref!.Value, value, menuId);
                    break;
                }

                case DialogActionKind.StartEncounter:
                    if (_encounterStartRequested != null) _encounterStartRequested(action.Ref!.Value);
                    else _diagnostics.Warn($"DialogHost：未注入 EncounterStartRequestedCallback，跳过 start_encounter 动作（menuId={menuId}）");
                    break;

                case DialogActionKind.CastSkill:
                    _skillHost.CastSkill(npcId, action.Ref!.Value, new[] { unitId });
                    break;

                case DialogActionKind.StartStory:
                    StartStory(unitId, action.Ref!.Value);
                    break;

                case DialogActionKind.Script:
                    _hookRegistry.Invoke(action.Ref!.Value, new HookArgs(new Dictionary<string, object?>
                    {
                        ["unitId"] = unitId,
                        ["npcId"] = npcId,
                    }));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "未知 gossip 动作类型");
            }

            var actionId = action.Ref ?? new Id("dialog.action." + DialogActionKinds.ToWireString(action.Kind));
            Publish(new GossipActionExecutedEvent(unitId, menuId, actionId));
        }

        private void Publish(IEvent evt) => _eventBus.PublishImmediate(evt);

        private sealed class Session
        {
            public Id? GossipMenuId;
            public Id? NpcId;
            public Id? StoryTreeId;
            public Id? StoryNodeId;
        }
    }
}

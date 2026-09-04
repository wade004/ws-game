using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Tests.Gameplay.Dialog
{
    /// <summary>最小 <see cref="IQuestHost"/> 假实现：只记录 Accept/TurnIn 调用，供
    /// <c>quest_accept</c>/<c>quest_turn_in</c> 动作断言使用。</summary>
    internal sealed class FakeQuestHost : IQuestHost
    {
        public readonly List<(Id UnitId, Id QuestId)> AcceptCalls = new List<(Id, Id)>();
        public readonly List<(Id UnitId, Id QuestId)> TurnInCalls = new List<(Id, Id)>();

        public QuestState GetState(Id unitId, Id questId) => QuestState.Available;

        public bool Accept(Id unitId, Id questId)
        {
            AcceptCalls.Add((unitId, questId));
            return true;
        }

        public bool UpdateProgress(Id unitId, Id questId, int objectiveIndex, int delta) => false;

        public bool TurnIn(Id unitId, Id questId)
        {
            TurnInCalls.Add((unitId, questId));
            return true;
        }

        public bool Fail(Id unitId, Id questId, string reason) => false;
        public IReadOnlyList<QuestProgress> GetLog(Id unitId) => System.Array.Empty<QuestProgress>();
        public IReadOnlyList<(Id QuestId, int ObjectiveIndex, Id TargetRef)> GetActiveObjectives(Id unitId) =>
            System.Array.Empty<(Id, int, Id)>();
        public void Update(Id unitId) { }
    }

    /// <summary>最小 <see cref="ISkillHost"/> 假实现：只记录 <see cref="CastSkill"/> 调用（供
    /// <c>cast_skill</c> 动作断言使用），其余成员本测试用不到，抛异常以便误用时能立刻发现。</summary>
    internal sealed class FakeSkillHost : ISkillHost
    {
        public readonly List<(Id CasterId, Id SkillId, IReadOnlyList<Id> Targets)> CastCalls =
            new List<(Id, Id, IReadOnlyList<Id>)>();

        public Vec2 GetPosition(Id unitId) => Vec2.Zero;
        public IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter) => System.Array.Empty<Id>();
        public void ApplyStatMod(Id sourceId, Id unitId, Id stat, StatModifierOp op, double value) { }

        public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            CastCalls.Add((casterId, skillId, targets));
            return CastResult.Ok(new Id("skill.cast_instance_test"));
        }

        public double GetCooldown(Id unitId, Id skillId) => 0;
        public bool IsCasting(Id unitId) => false;
        public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration) { }
    }

    /// <summary>只支持测试用到的 <c>self</c>/<c>target</c> 少量 key 的最小 <see cref="IExprHost"/>
    /// （gossip <c>visible_if</c>/story <c>condition</c> 主要用 <c>player</c> 分组，见
    /// <c>DialogHostTests</c>）。</summary>
    internal sealed class TestExprHost : IExprHost
    {
        private readonly System.Func<string, IReadOnlyList<ExprValue>, ExprValue>? _custom;

        public TestExprHost(System.Func<string, IReadOnlyList<ExprValue>, ExprValue>? custom = null)
        {
            _custom = custom;
        }

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
        {
            if (group == "player" && _custom != null)
            {
                return _custom(key, args);
            }
            return ExprValue.OfBool(false);
        }
    }

    internal sealed class TestExprHostFactory : IExprHostFactory
    {
        private readonly System.Func<string, IReadOnlyList<ExprValue>, ExprValue>? _playerGroup;

        public TestExprHostFactory(System.Func<string, IReadOnlyList<ExprValue>, ExprValue>? playerGroup = null)
        {
            _playerGroup = playerGroup;
        }

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new TestExprHost(_playerGroup);
    }

    internal static class TestSupport
    {
        public static readonly Id Player = new Id("unit.player_hero");
        public static readonly Id Npc = new Id("unit.npc_greeter");

        public static IEventBus NewEventBus()
        {
            var definitions = new List<EventDefinition>
            {
                new EventDefinition(DialogEventKeys.GossipOpened, "dialog", new[] { "unitId", "npcId", "menuId" }),
                new EventDefinition(DialogEventKeys.GossipActionExecuted, "dialog", new[] { "unitId", "menuId", "actionId" }),
                new EventDefinition(DialogEventKeys.StoryNodeEntered, "dialog", new[] { "unitId", "treeId", "nodeId" }),
                new EventDefinition(DialogEventKeys.Ended, "dialog", new[] { "unitId" }),
                new EventDefinition(AppEventKeys.StateChanged, "app", new[] { "oldState", "newState" }),
                new EventDefinition(WorldStateEventKeys.FlagChanged, "world", new[] { "flagKey", "oldValue", "newValue", "writerId" }),
            };
            return new EventBus(EventCatalog.FromDefinitions(definitions));
        }

        public static IWorldState NewWorldState(IEventBus bus) => new Core.Gameplay.WorldState.WorldState(bus);

        /// <summary>构造一个已经处于 <c>InWorld</c>/<c>Explore</c> 的 <see cref="IAppStateHost"/>
        /// （Boot→MainMenu→Loading→InWorld，默认配置见 <see cref="AppStateMachineConfig.Default"/>），
        /// 供 <see cref="DialogHost"/> 的 Push/PopSubState 用例复用。</summary>
        public static IAppStateHost NewAppStateHostInWorld(IEventBus bus)
        {
            var host = new AppStateHost(bus);
            host.RequestTransition(AppState.MainMenu);
            host.RequestTransition(AppState.Loading);
            host.RequestTransition(AppState.InWorld);
            return host;
        }

        public static IHookRegistry NewHookRegistry() => new HookRegistry();
    }
}

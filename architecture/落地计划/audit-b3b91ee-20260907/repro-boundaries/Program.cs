using System;
using System.Collections.Generic;
using System.IO;
using Adapters.Stub;
using Adapter.Unity.Presentation;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.InputMap;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Presentation.Render;
using Tests.Foundation.SceneRouter;

internal static class Program
{
    private static readonly List<string> Results = new List<string>();

    private static void Main()
    {
        Run("FND02 SceneRouter stale callback", ReproSceneRouterStaleCallback);
        Run("FND03 DataRegistry blocked state", ReproDataRegistryBlockedState);
        Run("FND08 InputMapHost same-batch edge", ReproInputMapSameBatchEdge);
        Run("GP02 animation completion/death wiring", ReproAnimationWiring);

        Console.WriteLine(string.Join(Environment.NewLine, Results));
    }

    private static void Run(string name, Func<string> repro)
    {
        try
        {
            Results.Add(repro());
        }
        catch (Exception ex)
        {
            Results.Add(name + " | actual=EXCEPTION " + ex.GetType().Name + ": " + ex.Message + " | NOT_REPRODUCED");
        }
    }

    private static string ReproSceneRouterStaleCallback()
    {
        var rows = "[" + SceneRouterTestSupport.TownSquareRow + "," +
            SceneRouterTestSupport.ForestPathRow + "]";
        var harness = new SceneRouterTestSupport.Harness(rows, deferCallbacks: true);
        harness.App.RequestTransition(AppState.MainMenu);
        harness.Router.LoadScene(new Id("world.town_square"));

        // 关键时序：Town 的 scene 先失败，Town 的 nav 回调仍留在 StubResourceLoader 待完成队列。
        harness.Loader.FailPending(new Id("scene.town_square"));
        harness.Router.Update();
        var afterTownFailure = harness.Router.State + "/" + harness.App.GetState();

        harness.Router.LoadScene(new Id("world.forest_path"));
        // 这是旧 Town 请求的唯一一次回调，发生在 Forest 已开始加载之后。
        harness.Loader.FailPending(new Id("nav.town_square"));
        var beforeStaleUpdate = harness.Router.State + "/" + harness.App.GetState();
        harness.Router.Update();
        var afterStaleUpdate = harness.Router.State + "/" + harness.App.GetState();

        // 对照：没有迟到回调时，Forest 的两个资源完成后可以正常进入 InWorld。
        var control = new SceneRouterTestSupport.Harness(rows, deferCallbacks: true);
        control.App.RequestTransition(AppState.MainMenu);
        control.Router.LoadScene(new Id("world.forest_path"));
        control.Loader.CompletePending(new Id("scene.forest_path"));
        control.Loader.CompletePending(new Id("nav.forest_path"));
        control.Router.Update();

        var reproduced = afterTownFailure == "Idle/MainMenu"
            && beforeStaleUpdate == "Loading/Loading"
            && afterStaleUpdate == "Idle/MainMenu"
            && control.Router.State == SceneRouterState.Idle
            && control.Router.GetCurrentScene() == new Id("world.forest_path");
        return "FND02 SceneRouter stale callback" +
            " | expected=old nav.town_square callback is ignored; after replay Router remains Loading/Loading until Forest resources complete" +
            " | actual=afterTownFailure=" + afterTownFailure +
            ", beforeStaleUpdate=" + beforeStaleUpdate +
            ", afterStaleUpdate=" + afterStaleUpdate +
            ", forestControl=" + control.Router.State + "/" + control.App.GetState() + "/" + control.Router.GetCurrentScene() +
            " | " + (reproduced ? "REPRODUCED" : "NOT_REPRODUCED");
    }

    private static string ReproDataRegistryBlockedState()
    {
        var source = new InMemoryDataSource().Add("world.map", "{");
        var registry = new Core.Foundation.DataRegistry.DataRegistry(
            source, SceneRouterTestSupport.CreateBus());
        registry.RegisterSchema(WorldMapSchema.Table);

        var initial = registry.LoadAll();
        var initialReadBlocked = ThrowsInvalidOperation(() => registry.GetAll("world.map"));
        var afterValidate = registry.Validate();
        var laterReadBlocked = ThrowsInvalidOperation(() => registry.GetAll("world.map"));
        var reproduced = initial.IsBlocking && initialReadBlocked && !afterValidate.IsBlocking && !laterReadBlocked;

        return "FND03 DataRegistry blocked state" +
            " | expected=bad JSON keeps registry blocked until a successful data load/reload" +
            " | actual=initial(IsBlocking=" + initial.IsBlocking + ", errors=" + initial.ErrorCount +
            "), initialReadBlocked=" + initialReadBlocked +
            ", Validate(IsBlocking=" + afterValidate.IsBlocking + ", errors=" + afterValidate.ErrorCount +
            "), laterReadBlocked=" + laterReadBlocked +
            " | " + (reproduced ? "REPRODUCED" : "NOT_REPRODUCED");
    }

    private static string ReproInputMapSameBatchEdge()
    {
        var bus = SceneRouterTestSupport.CreateBus();
        var host = new InputMapHost(bus);
        var action = new ActionDefinition(new Id("input.action.q"), ActionKind.Button, new[] { "key:Q" });
        host.DeclareActionSet(new Id("input.set.audit"), new[] { action });
        var input = new StubInput();
        var triggers = 0;
        bus.Subscribe<InputActionTriggeredEvent>(InputMapEventKeys.ActionTriggered, evt =>
        {
            if (evt.ActionName == "input.action.q") triggers++;
        });

        input.Press("Q");
        input.Release("Q");
        host.Update(input);
        bus.DispatchPending();
        var sameBatch = "active=" + host.IsActionActive("input.action.q") + ", actionCount=" + triggers;

        input.Press("Q");
        host.Update(input);
        bus.DispatchPending();
        var splitDown = "active=" + host.IsActionActive("input.action.q") + ", actionCount=" + triggers;
        input.Release("Q");
        host.Update(input);
        bus.DispatchPending();
        var splitUp = "active=" + host.IsActionActive("input.action.q") + ", actionCount=" + triggers;

        var reproduced = sameBatch == "active=False, actionCount=0"
            && splitDown == "active=True, actionCount=1"
            && splitUp == "active=False, actionCount=1";
        return "FND08 InputMapHost same-batch edge" +
            " | expected=Q down+up in one PollEvents batch still emits one triggered action" +
            " | actual=sameBatch(" + sameBatch + "), splitDown(" + splitDown + "), splitUp(" + splitUp + ")" +
            " | " + (reproduced ? "REPRODUCED" : "NOT_REPRODUCED");
    }

    private static string ReproAnimationWiring()
    {
        var bus = SceneRouterTestSupport.CreateBus();
        var machine = new AnimStateMachine(bus);
        var entity = new Id("unit.audit.hero");
        var clips = new Dictionary<Id, FrameAnimClip>
        {
            [new Id("anim.idle")] = new FrameAnimClip(new Id("anim.idle"), 1, 10),
            [new Id("anim.move")] = new FrameAnimClip(new Id("anim.move"), 1, 10),
            [new Id("anim.attack")] = new FrameAnimClip(new Id("anim.attack"), 2, 10),
            [new Id("anim.hit")] = new FrameAnimClip(new Id("anim.hit"), 2, 10),
            [new Id("anim.death")] = new FrameAnimClip(new Id("anim.death"), 2, 10),
        };
        var player = new FrameAnimPlayer(clips);
        var clipByState = new Dictionary<string, Id>(StringComparer.Ordinal)
        {
            ["idle"] = new Id("anim.idle"),
            ["move"] = new Id("anim.move"),
            ["attack"] = new Id("anim.attack"),
            ["hit"] = new Id("anim.hit"),
            ["death"] = new Id("anim.death"),
        };
        using var resolver = new AnimClipResolver(machine, _ => clipByState,
            (_, clipId, loop, speed) => player.Play(clipId, loop, speed));

        bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entity, "Idle", "Walk"));
        machine.RequestOverride(entity, AnimState.Hit);
        var clipAtHit = player.CurrentClipId;
        player.Update(1.0);
        var playerAfterHit = player.CurrentClipId?.Value ?? "<none>";
        var stateAfterHitComplete = machine.GetState(entity);

        bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entity, "Walk", "Run"));
        machine.RequestOverride(entity, AnimState.Attack);
        var stateAfterMoveAndAttack = machine.GetState(entity);

        machine.RequestOverride(entity, AnimState.Death);
        player.Update(1.0);
        var stateAfterDeathComplete = machine.GetState(entity);
        bus.PublishImmediate(new Core.Rules.Common.UnitRespawnedEvent(
            entity, Core.Rules.Common.RespawnPolicy.RespawnPoint));
        bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entity, "Idle", "Walk"));
        machine.RequestOverride(entity, AnimState.Attack);
        var stateAfterRespawnAndAttack = machine.GetState(entity);
        var stateBeforeManualForget = machine.GetState(entity);
        machine.Forget(entity);
        var stateAfterManualForget = machine.GetState(entity);

        var reproduced = clipAtHit == new Id("anim.hit")
            && playerAfterHit == "<none>"
            && stateAfterHitComplete == AnimState.Hit
            && stateAfterMoveAndAttack == AnimState.Hit
            && stateAfterDeathComplete == AnimState.Death
            && stateAfterRespawnAndAttack == AnimState.Death
            && stateBeforeManualForget == AnimState.Death
            && stateAfterManualForget == AnimState.Idle;
        return "GP02 animation completion/death wiring" +
            " | expected=resolver wires player completion to NotifyTransientStateFinished and death/respawn/view teardown to Forget or reset" +
            " | actual=clipAtHit=" + clipAtHit + ", playerAfterHit=" + playerAfterHit +
            ", stateAfterHitComplete=" + stateAfterHitComplete +
            ", stateAfterMoveAndAttack=" + stateAfterMoveAndAttack +
            ", stateAfterDeathComplete=" + stateAfterDeathComplete +
            ", stateAfterRespawnAndAttack=" + stateAfterRespawnAndAttack +
            ", stateBeforeManualForget=" + stateBeforeManualForget +
            ", stateAfterManualForget=" + stateAfterManualForget +
            " | component-chain-only (no UnityViewFactory/Unity runtime) | " + (reproduced ? "REPRODUCED" : "NOT_REPRODUCED");
    }

    private static bool ThrowsInvalidOperation(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

}

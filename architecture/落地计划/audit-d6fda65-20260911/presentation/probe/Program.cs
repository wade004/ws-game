using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Carriers.Unit;
using Core.Rules.Common;
using Presentation.Common;
using Presentation.ViewBinding;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Tests.PresentationViewBinding;

static class Program
{
    static EventBus Bus() => new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),new EventBusOptions{StrictCatalog=false});
    static void Main()
    {
        var bus=Bus();
        var sfxId=new Id("sfx.missing_audit");
        var sfx=new SfxPlayer(new StubAudio(),new RngHost(7),new Dictionary<Id,SfxDef>{[sfxId]=new SfxDef(sfxId,"combat",0,null,new Id("audio.missing_audit"))},new SfxOptions{FirstLoadTimeoutSeconds=0},resourceLoader:new StubResourceLoader());
        var vfx=new VfxPlayer(new StubRenderer2D(),new StubCamera(),new Dictionary<Id,VfxDef>());
        var sink=new CompositeFeedbackSink(vfx,sfx,(_,_,_)=>{},_=>{},_=>{},(_,_)=>{});
        using var feedback=new FeedbackBinder(bus,new ExprFactory(),new[]{new FeedbackRule(new Id("feedback.audit"),RulesEventKeys.CombatDamageDealt,null,new FeedbackAction[]{new PlaySfxAction(sfxId,null)})},sink);
        var pacing=new WaitForPlaybackPacingPolicy(()=>feedback.HasPendingPlayback);
        bus.Subscribe(EventKeys.PresentationPlaybackFinished,_=>pacing.OnPlaybackFinished());
        var evt=new CombatDamageDealtEvent(new Id("unit.attacker"),new Id("unit.target"),new Id("skill.school.physical"),1,false,HitResult.Hit);
        bus.PublishImmediate(evt); feedback.Update(1); Console.WriteLine($"SFX first_failed_load_pending={sfx.PendingPlayCount}");
        bus.PublishImmediate(evt); feedback.Update(1); pacing.BeginStep();
        for(int i=0;i<600;i++){feedback.Update(0.016);vfx.Update(0.016);}
        Console.WriteLine($"SFX after_second_missing_resource_plus_600_host_equivalent_frames pending={sfx.PendingPlayCount};pacing_finished={pacing.IsPlaybackFinished};expected_pending=0;expected_pacing_finished=True");
        sfx.Update(0.016);
        Console.WriteLine($"SFX control_after_explicit_sfx_update pending={sfx.PendingPlayCount};pacing_finished={pacing.IsPlaybackFinished}");

        var world=new WorldSim(bus);
        var player=new PlayerUnit(new Id("unit.player"),new Id("world.audit"),new Id("fac.player"),new Id("arch.class.audit")){Position=new Vec2(3,4)};
        var factory=new FakeViewFactory();
        using var binder=new ViewBinder(bus,factory,new WorldSimSnapshot(world),new FakeDisplayInfoRegistry());
        world.AddEntity(player); world.Tick(SimStep.Continuous(0.02));
        var save=new SaveSystem(new StubFileSystem(),new SaveSystemOptions(new Id("game.audit")),bus);
        save.RegisterPersistable(UnitPersistable.CurrentPosition(player));
        var slot=new Id("slot.audit");
        Console.WriteLine($"VIEW save_success={save.Save(new SaveRequest(slot,"audit")).Success}");
        player.Position=new Vec2(30,40); world.Tick(SimStep.Continuous(0.02)); world.Tick(SimStep.Continuous(0.02));
        var loaded=save.Load(slot); binder.SyncAll(1);
        var view=factory.CreatedByEntityId[player.EntityId];
        Console.WriteLine($"VIEW status={loaded.Status};logical={player.Position};rendered={view.SyncCalls[^1].Pos};expected_rendered=(3,4);without_postload_sim_tick=True");
        world.Tick(SimStep.Continuous(0.02)); binder.SyncAll(1);
        Console.WriteLine($"VIEW control_after_sim_tick rendered={view.SyncCalls[^1].Pos}");
    }
    sealed class ExprFactory:IExprHostFactory
    {
        public IExprHost CreateFor(Id selfId,Id? targetId,IEvent? evt)=>new Host();
        sealed class Host:IExprHost {public ExprValue Query(string group,string key,IReadOnlyList<ExprValue> args)=>default;}
    }
}

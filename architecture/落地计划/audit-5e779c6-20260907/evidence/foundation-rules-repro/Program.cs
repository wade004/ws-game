using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Core.Numbers.Progression;
using Core.Rules.Skill;
using Core.Rules.Common;
using Tests.Rules.Skill;

var caster = new Id("unit.caster");
var skillId = new Id("skill.charge");
var def = new SkillDef(skillId, new Id("skill.school"),false,0,Array.Empty<Id>(),0,0,Array.Empty<(Id,double)>(),null,0,1,0,false,new Id("target.chain"),Array.Empty<EffectRef>(),InterruptFlags.None);
var cd = new CooldownTracker();
Console.WriteLine($"ZERO_RECHARGE initial={cd.GetCharges(caster,def)}");
cd.StartCooldown(caster,def);
cd.AdvanceCharges(caster,def,100);
Console.WriteLine($"ZERO_RECHARGE after100s={cd.GetCharges(caster,def)},ready={cd.IsSkillReady(caster,def)},cooldown={cd.GetCooldown(caster,def)}");

var auraId=new Id("skill.aura.short");
var world = new SkillWorldBuilder().AuraDef(J.O(("id",J.S(auraId.Value)),("duration",J.N(0.5)),("effects",J.A(J.O(("kind",J.S("periodic_damage")),("params",J.O(("interval",J.N(1)),("school",J.S("skill.school")),("base_value",J.N(1))))))))).Build();
world.AddUnit(caster);
world.Host.EffectSink.ApplyAura(caster,auraId,caster);
world.Host.Update(1);
Console.WriteLine($"EXPIRED_PERIODIC calls={world.Combat.ResolveCalls.Count}");

string Envelope(string table,string rows)=>"{\"table\":\""+table+"\",\"schema_version\":1,\"rows\":"+rows+"}";
var bus = new EventBus(EventCatalog.FromDefinitions(new[]{
 new EventDefinition(DataRegistryEventKeys.LoadCompleted,"data",Array.Empty<string>()), new EventDefinition(DataRegistryEventKeys.ValidationFailed,"data",Array.Empty<string>()),
 new EventDefinition(StatBlockEventKeys.StatChanged,"stat",Array.Empty<string>()),new EventDefinition(ProgressionEventKeys.LevelUp,"progression",Array.Empty<string>()),new EventDefinition(ProgressionEventKeys.XpGained,"progression",Array.Empty<string>())}));
var source = new InMemoryDataSource().Add("stat.definition",Envelope("stat.definition","[{\"id\":\"stat.rating\",\"name_key\":\"l10n.rating\",\"group\":\"primary\",\"default_base\":100,\"is_rating\":true,\"rating_conversion_ref\":\"stat.curve\"}]"))
.Add("stat.rating_conversion",Envelope("stat.rating_conversion","[{\"id\":\"stat.curve\",\"entries\":[{\"level\":1,\"points_per_percent\":10},{\"level\":2,\"points_per_percent\":5}]}]"))
.Add("prog.level_curve",Envelope("prog.level_curve","[{\"id\":\"prog.curve\",\"max_level\":2,\"entries\":[{\"level\":1,\"xp_to_next\":10,\"growth\":{}},{\"level\":2,\"xp_to_next\":0,\"growth\":{}}]}]"))
.Add("prog.xp_source",Envelope("prog.xp_source","[]"));
var registry = new DataRegistry(source,bus);registry.RegisterSchema(StatSchemas.Definition);registry.RegisterSchema(StatSchemas.RatingConversion);registry.RegisterSchema(ProgSchemas.LevelCurve);registry.RegisterSchema(ProgSchemas.XpSource); registry.LoadAll();
ProgressionHost prog = null!;
var stats=new StatHost(registry,bus,new StatHostOptions{EnableRatingConversion=true,LevelLookup=id=>prog.GetLevel(id)});
prog=new ProgressionHost(registry,bus,(u,s,o,v,src)=>stats.AddModifier(u,new StatModifier(s,StatModifierOp.Flat,v,src)),(u,src)=>stats.RemoveModifiersBySource(u,src));
bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp,e=>stats.RecomputeRatingStats(e.UnitId));
stats.RegisterUnit(caster);prog.RegisterUnit(caster,new Id("prog.curve"));
Console.WriteLine($"RESTORE_RATING before={stats.GetStat(caster,new Id("stat.rating"))}");
prog.RestoreState(caster,new Id("prog.curve"),2,0);bus.DispatchPending();
Console.WriteLine($"RESTORE_RATING level={prog.GetLevel(caster)},cached={stats.GetStat(caster,new Id("stat.rating"))}");
stats.RecomputeRatingStats(caster);
Console.WriteLine($"RESTORE_RATING recomputed={stats.GetStat(caster,new Id("stat.rating"))}");

foreach(var variant in new[]{"zero_recharge","missing_kind","bad_cost"}) {
 var extra = variant=="zero_recharge" ? ",\"charges\":{\"max\":1,\"recharge_time\":0}" : variant=="bad_cost" ? ",\"cost\":[42]" : "";
 var effects = variant=="missing_kind" ? "[{}]" : "[]";
 var skillRows="[{\"id\":\"skill.probe\",\"school\":\"skill.school\",\"kind\":\"active\",\"range\":0,\"cast_time\":0,\"respects_gcd\":false,\"target_shape_ref\":\"target.self\",\"effects\":"+effects+extra+"}]";
 var probeSource=new InMemoryDataSource().Add("skill.def",Envelope("skill.def",skillRows)).Add("target.chain_def",Envelope("target.chain_def","[{\"id\":\"target.self\",\"source\":\"self\"}]"));
 var probeRegistry=new DataRegistry(probeSource,bus,Core.Rules.Assembly.RulesSchemaCatalog.CreateOptions());
 Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll(probeRegistry);
 var report=probeRegistry.LoadAll();
 Console.WriteLine($"NESTED_VALIDATION {variant}: errors={report.ErrorCount},warnings={report.WarningCount},blocking={report.IsBlocking}");
 try {new SkillDefCache(probeRegistry).GetSkillDef(new Id("skill.probe")); Console.WriteLine($"NESTED_PARSE {variant}: ok");}
 catch(Exception ex) {Console.WriteLine($"NESTED_PARSE {variant}: {ex.GetType().Name}: {ex.Message}");}
}
var hybridBus=new EventBus(EventCatalog.FromDefinitions(EventKeys.All.Select(k=>new EventDefinition(k,k.Domain,Array.Empty<string>()))),new EventBusOptions{StrictCatalog=false});
var hybridSource=new InMemoryDataSource().Add("found.time_model",Envelope("found.time_model","[{\"id\":\"found.explore\",\"scope\":\"exploration\",\"mode\":\"continuous\"},{\"id\":\"found.combat\",\"scope\":\"combat\",\"mode\":\"discrete\",\"seconds_per_turn\":5,\"initiative_policy\":\"fixed_order\",\"movement_budget_rule\":\"distance\"}]"));
var hybridRegistry=new DataRegistry(hybridSource,hybridBus);Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll(hybridRegistry); hybridRegistry.LoadAll();
var hybridFixture=new SkillWorldBuilder().SkillDef(J.O(("id",J.S("skill.ten")),("school",J.S("skill.school")),("kind",J.S("active")),("range",J.N(0)),("cast_time",J.N(0)),("respects_gcd",J.B(false)),("cooldown_duration",J.N(10)),("target_shape_ref",J.S("target.self")),("effects",J.A())))
.AuraDef(J.O(("id",J.S("skill.aura.ten")),("duration",J.N(10)),("effects",J.A()))).Build(); hybridFixture.AddUnit(caster);hybridFixture.Targets.SetChain(new Id("target.self"),caster);
var hybridSkill=new SkillHost(hybridFixture.Registry,hybridBus,hybridFixture.Units,hybridFixture.Stats,hybridFixture.Powers,hybridFixture.Rng,hybridFixture.Combat,hybridFixture.Targets,hybridFixture.Exprs,null);
var hybridWorld=new Core.Foundation.SimLoop.WorldSim(hybridBus); var hybridClock=new Core.Foundation.SimLoop.SimClockHost(hybridWorld);
var hybridApp=new Core.Foundation.AppLifecycle.AppStateHost(hybridBus); hybridApp.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);hybridApp.RequestTransition(Core.Foundation.AppLifecycle.AppState.Loading);hybridApp.RequestTransition(Core.Foundation.AppLifecycle.AppState.InWorld);
var hybridScheduler=new Core.Foundation.SimLoop.TurnScheduler(hybridWorld,_=>0,_=>true,hybridBus);
var hybridSwitch=new Core.Gameplay.Assembly.TimeModelSwitch(hybridScheduler,hybridClock,hybridApp,hybridWorld,hybridFixture.Units,new Adapters.Stub.StubSpatialQuery(),hybridBus,hybridRegistry,participantsResolver:_=>new[]{caster});
var generalTimer=hybridWorld.Timers.Create(10);
hybridSkill.CastSkill(caster,new Id("skill.ten"),Array.Empty<Id>());hybridSkill.EffectSink.ApplyAura(caster,new Id("skill.aura.ten"),caster);
hybridBus.PublishImmediate(new CombatEnteredEvent(caster));
Console.WriteLine($"HYBRID switched={hybridSwitch.CurrentMode},simtimer={hybridWorld.Timers.Remaining(generalTimer)},skillcooldown={hybridSkill.GetCooldown(caster,new Id("skill.ten"))}");
hybridSkill.AdvanceRoundTimers(1);hybridSkill.AdvanceRoundTimers(1);
Console.WriteLine($"HYBRID after2rounds skillcooldown={hybridSkill.GetCooldown(caster,new Id("skill.ten"))},auraActive={hybridSkill.AuraQuery.HasAura(caster,new Id("skill.aura.ten"))}");
var normalTickWorld=new SkillWorldBuilder().AuraDef(J.O(("id",J.S("skill.aura.short_tick")),("duration",J.N(0.25)),("effects",J.A(J.O(("kind",J.S("periodic_damage")),("params",J.O(("interval",J.N(0.3)),("school",J.S("skill.school")),("base_value",J.N(1))))))))).Build();normalTickWorld.AddUnit(caster);normalTickWorld.Host.EffectSink.ApplyAura(caster,new Id("skill.aura.short_tick"),caster);for(int i=0;i<3;i++)normalTickWorld.Host.Update(0.1);
Console.WriteLine($"EXPIRED_PERIODIC_FIXED dt=0.1,duration=0.25,interval=0.3,calls={normalTickWorld.Combat.ResolveCalls.Count}");

using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Carriers.Unit;
using Core.Gameplay.Common;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Core.Rules.Skill;
class Program {
 static string Env(string name,string rows)=>"{\"table\":\""+name+"\",\"schema_version\":1,\"rows\":"+rows+"}";
 static EventBus Bus()=> new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),new EventBusOptions{StrictCatalog=false});
 static void Main(){ SetAura(); FailedRewardEvents(); }
 static void SetAura(){
  var raw=(InMemoryDataSource)typeof(Tests.Carriers.Item.EquipmentReplaceHandleTests).GetMethod("BuildDataSource",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
  var source=new InMemoryDataSource();
  foreach(var t in raw.ListTables()) {var text=t.ReadText(); if(t.TableName=="item.template") text=text.Replace("\"grants\": {\"auras\": [\"skill.aura_def.c08_shared_trinket_aura\"]}","\"set_id\": \"item.set.test\"");source.Add(t.TableName,text);}
  source.Add("item.set",Env("item.set","[{\"id\":\"item.set.test\",\"name_key\":\"l10n.set.test\",\"pieces\":[\"item.sample_c08_trinket_a\",\"item.sample_c08_trinket_b\"],\"bonuses\":[{\"count\":1,\"aura_ref\":\"skill.aura_def.c08_shared_trinket_aura\"},{\"count\":2,\"aura_ref\":\"skill.aura_def.c08_shared_trinket_aura\"}]}]"));
  var bus=Bus();var registry=new DataRegistry(source,bus,new DataRegistryOptions{FailOnUnknownTable=false}); CarriersSchemaCatalog.RegisterAll(registry);var load=registry.LoadAll();Console.WriteLine("SET_DATA blocking="+load.IsBlocking+" "+string.Join(";",load.Issues));
  var world=new WorldSim(bus);var a=new CarriersAssembly(bus,registry,new RngHost(1),world,new StubSpatialQuery(),skillOptions:new SkillOptions{StackOverflowPolicy=StackOverflowPolicy.Replace});
  var p=a.Creatures.Spawn(new Id("creature.sample_c08_player"),new Id("map.c08_test"),Vec2.Zero,0);var tid=new Id("item.sample_c08_trinket_a");var slot=new Id("item.slot.c08_trinket_a");var aura=new Id("skill.aura_def.c08_shared_trinket_aura");
  a.Inventory.AddItem(p,tid,1);a.Equipment.Equip(p,a.Inventory.ListItems(p)[0].InstanceId,slot);
  var b=new Id("item.sample_c08_trinket_b");var slotb=new Id("item.slot.c08_trinket_b");a.Inventory.AddItem(p,b,1);a.Equipment.Equip(p,a.Inventory.ListItems(p)[0].InstanceId,slotb);
  a.Equipment.Unequip(p,slotb);
  Console.WriteLine("SET_REPLACE after_unequip aura="+a.Rules.Skill.AuraQuery.HasAura(p,aura)+" stat="+a.Rules.Stats.GetStat(p,new Id("stat.power")));
 }
 static void FailedRewardEvents(){
  var bus=Bus();var src=new InMemoryDataSource(); src.Add("item.template",Env("item.template","[{\"id\":\"item.a\",\"slot\":\"item.slot.bag\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.a\",\"stack_size\":10,\"name_key\":\"l10n.a\"},{\"id\":\"item.b\",\"slot\":\"item.slot.bag\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.b\",\"stack_size\":10,\"name_key\":\"l10n.b\"}]"));
  var registry=new DataRegistry(src,bus,new DataRegistryOptions{FailOnUnknownTable=false});registry.RegisterSchema(new TableSchema("item.template","id",1,new[]{new FieldSchema("id",FieldKind.Id,true)}));var report=registry.LoadAll();Console.WriteLine("REWARD_DATA "+string.Join(";",report.Issues));var inv=new InventoryHost(registry,bus,new InventoryOptions{MaxSlots=1,FullPolicy=InventoryFullPolicy.Reject});var p=new Id("player.one");var item=new Id("item.a");inv.AddItem(p,item,5);bus.DispatchPending();
  var reward=new RewardDispatcher(inventory:inv);var world=new WorldSim(bus); world.AddEntity(new PlayerUnit(p,new Id("map.a"),new Id("fac.a"),new Id("arch.a")));var qid=new Id("quest.consume");var q=new QuestHost(new[]{new QuestDefinition(qid,new[]{new QuestObjective(QuestObjectiveType.Collect,item,1,true)},QuestStartMethod.NpcGossip,QuestTurnInMethod.NpcGossip,QuestRepeatable.None)},bus,new NoExpr(),reward,inv,new WorldUnitAccess(world));q.Accept(p,qid);var bundle=new RewardBundle(new[]{new ItemStack(item,1),new ItemStack(new Id("item.b"),1)},0,Array.Empty<(Id,long)>(),Array.Empty<Id>(),Array.Empty<(Id,ExprValue)>(),0);
  var ok=reward.Grant(p,bundle,new Id("reward.test"));Console.WriteLine("REWARD_FAIL before_dispatch success="+ok+" inventory="+inv.CountOf(p,item));bus.DispatchPending();Console.WriteLine("REWARD_FAIL after_dispatch inventory="+inv.CountOf(p,item)+" quest="+q.GetState(p,qid)+" progress="+q.GetLog(p)[0].ObjectiveCounts[0]);
 }
 sealed class NoExpr:IExprHostFactory{public IExprHost CreateFor(Id s,Id? t,IEvent? e)=>throw new Exception("unexpected expr");}
}
namespace Xunit{ public sealed class FactAttribute:Attribute{} public static class Assert{public static void False(bool x,string? m=null){if(x)throw new Exception(m);}public static void True(bool x,string? m=null){if(!x)throw new Exception(m);}public static void NotNull(object? x){if(x==null)throw new Exception();}public static void Equal<T>(T a,T b){if(!EqualityComparer<T>.Default.Equals(a,b))throw new Exception();}}}




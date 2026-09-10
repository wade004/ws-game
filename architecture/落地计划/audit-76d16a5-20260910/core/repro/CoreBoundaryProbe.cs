using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Gameplay.Common;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Numbers.StatBlock;
using Tests.Gameplay.Quest;
using Tests.Rules.Skill;

sealed class MutableSource : IDataSource
{
    readonly Dictionary<string,string> values = new(StringComparer.Ordinal);
    public MutableSource Add(string t,string j){values[t]=j;return this;}
    public void Replace(string t,string j)=>values[t]=j;
    public IReadOnlyList<DataTableSource> ListTables(){var r=new List<DataTableSource>();foreach(var x in values)r.Add(new DataTableSource(x.Key,"memory://"+x.Key,()=>values[x.Key]));return r;}
}

sealed class ProbeInventory : IInventoryHost
{
    readonly Dictionary<Id,List<ItemInstance>> bags=new(); int seq;
    List<ItemInstance> Bag(Id u){if(!bags.TryGetValue(u,out var b))bags[u]=b=new();return b;}
    public bool AddItem(Id u,Id t,int n){Bag(u).Add(new ItemInstance(new Id("item.probe_"+(seq++)),t,n));return n>0;}
    public bool RemoveItem(Id u,Id i,int n){var b=Bag(u);for(int k=0;k<b.Count;k++)if(b[k].InstanceId==i){if(n>b[k].Count)return false;if(n==b[k].Count)b.RemoveAt(k);else b[k]=new ItemInstance(i,b[k].TemplateId,b[k].Count-n);return true;}return false;}
    public IReadOnlyList<ItemInstance> ListItems(Id u)=>Bag(u).ToArray();
    public int CountOf(Id u,Id t){var n=0;foreach(var x in Bag(u))if(x.TemplateId==t)n+=x.Count;return n;}
    public ItemInstance? FindInstance(Id u,Id i){foreach(var x in Bag(u))if(x.InstanceId==i)return x;return null;}
}
sealed class ProbeExprFactory:IExprHostFactory
{ public IExprHost CreateFor(Id s,Id? t,IEvent? e)=>new Host(); sealed class Host:IExprHost{public ExprValue Query(string g,string k,IReadOnlyList<ExprValue> a)=>ExprValue.OfInt(0);} }

static class CoreBoundaryProbe
{
    static string E(string t,string rows)=>"{\"table\":\""+t+"\",\"schema_version\":1,\"rows\":"+rows+"}";
    static IEventBus Bus()=>new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),new EventBusOptions{StrictCatalog=false});
    static string Issues(ValidationReport r)=>string.Join(" | ",r.Issues);
    static void Main(){Console.WriteLine("CURRENT-CORE-BOUNDARY-PROBE head=76d16a5 version=1.14.0"); ExprValueInvalidId(); QuestObjectiveReload(); StatQueryOrder(); EconomyNoneToTimer();}
    static void ExprValueInvalidId()
    {
        var value=JsonReader.Parse("{\"$id\":\"BAD\"}"); bool? valid=null; string parse="none";
        try{valid=ExprValueJson.IsValid(value);parse="returned";}catch(Exception ex){parse="throws:"+ex.GetType().Name;}
        Console.WriteLine("EXPR-ID;is_valid="+(valid?.ToString()??"<none>")+";is_valid_call="+parse);
        try{ExprValueJson.Parse(value);Console.WriteLine("parse=returned");}catch(Exception ex){Console.WriteLine("parse=throws:"+ex.GetType().Name);}
        Console.WriteLine("ORACLE=IsValid contract says no throw and malformed Id must be false; Parse may throw ArgumentException for invalid Id. Validator must report this before runtime consumers.");
    }
    static void QuestObjectiveReload()
    {
        var q=new Id("quest.audit_objectives"); var c=new Id("creature.audit_wolf");
        var oldDef=new QuestDefinition(q,new[]{new QuestObjective(QuestObjectiveType.Kill,c,5)},QuestStartMethod.NpcGossip,QuestTurnInMethod.NpcGossip,QuestRepeatable.None);
        var bus=TestSupport.NewEventBus(); var h=new QuestHost(new[]{oldDef},bus,new ProbeExprFactory(),new FakeRewardDispatcher(),new ProbeInventory(),new Tests.Rules.Skill.FakeUnitAccess()); var u=new Id("unit.audit_quest"); h.Accept(u,q); h.UpdateProgress(u,q,0,1);
        h.Reload(new[]{new QuestDefinition(q,new[]{new QuestObjective(QuestObjectiveType.Kill,c,5),new QuestObjective(QuestObjectiveType.Kill,c,1)},QuestStartMethod.NpcGossip,QuestTurnInMethod.NpcGossip,QuestRepeatable.None)});
        try{var ok=h.UpdateProgress(u,q,1,1);Console.WriteLine("QUEST-OBJECTIVE-RELOAD;result="+ok+";exception=<none>");}
        catch(Exception ex){Console.WriteLine("QUEST-OBJECTIVE-RELOAD;exception="+ex.GetType().Name+";message="+ex.Message);}
        Console.WriteLine("ORACLE=Reload must preserve active progress without indexing a stale ObjectiveCounts array; changed objective cardinality must migrate safely or reject atomically.");
    }
    static void StatQueryOrder()
    {
        var s=new MutableSource().Add("stat.definition",E("stat.definition","[{\"id\":\"stat.audit\",\"name_key\":\"l10n.stat.audit\",\"group\":\"primary\",\"default_base\":0}]"));
        var bus=Bus();var r=new DataRegistry(s,bus,new DataRegistryOptions());r.RegisterSchema(StatSchemas.Definition);var first=r.LoadAll();
        var host=new StatHost(r,bus);var a=new Id("unit.audit_stat_a");var b=new Id("unit.audit_stat_b");host.RegisterUnit(a);host.RegisterUnit(b);var stat=new Id("stat.audit");var beforeA=host.GetStat(a,stat);
        s.Replace("stat.definition",E("stat.definition","[{\"id\":\"stat.audit\",\"name_key\":\"l10n.stat.audit\",\"group\":\"primary\",\"default_base\":77}]"));var rr=r.Reload("stat.definition");bus.PublishImmediate(new DataLoadCompletedEvent(r.Tables.Count,1,rr.ErrorCount,rr.WarningCount));var afterA=host.GetStat(a,stat);var afterB=host.GetStat(b,stat);
        Console.WriteLine($"STAT-QUERY-ORDER;load_blocking={first.IsBlocking};reload_blocking={rr.IsBlocking};beforeA={beforeA};afterA={afterA};afterB={afterB};baseA={host.GetBase(a,stat)};baseB={host.GetBase(b,stat)}");
        Console.WriteLine("ORACLE=CHANGELOG current definition reload promises next access sees the new definition; two unmodified registered units must not differ solely because one was queried before reload. Explicit SetBase runtime state is a separate preserved state.");
    }
    static string Currency()=>"[{\"id\":\"econ.currency.audit\",\"name_key\":\"l10n.currency.audit\",\"cap\":1000,\"display_ref\":\"display.audit\"}]";
    static string Vendor(string policy)=>"[{\"id\":\"econ.vendor.audit\",\"name_key\":\"l10n.vendor.audit\",\"sell_items\":[{\"item_id\":\"item.audit\",\"price_currency_id\":\"econ.currency.audit\",\"price_amount\":1,\"stock_limit\":2"+(policy=="timer"?",\"restock_policy\":\"timer\",\"restock_timer\":1":"")+"}]}]";    static void EconomyNoneToTimer()
    {
        var s=new MutableSource().Add("econ.currency",E("econ.currency",Currency())).Add("econ.vendor",E("econ.vendor",Vendor("none")));
        var bus=Bus(); var r=new DataRegistry(s,bus,new DataRegistryOptions()); r.RegisterSchema(EconomySchemas.Currency); r.RegisterSchema(EconomySchemas.Vendor); r.RegisterValidationRule(new EconomyContentValidationRule());
        var load=r.LoadAll(); var host=new EconomyHost(r,bus,new ProbeInventory(),new ProbeExprFactory()); var v=new Id("econ.vendor.audit"); var i=new Id("item.audit"); host.SetStock(v,i,0);
        s.Replace("econ.vendor",E("econ.vendor",Vendor("timer"))); var rr=r.Reload("econ.vendor"); bus.PublishImmediate(new DataLoadCompletedEvent(r.Tables.Count,2,rr.ErrorCount,rr.WarningCount));
        var before=host.GetStock(v,i); var beforeTimer=host.GetStockTimerRemaining(v,i); host.Update(2); var after=host.GetStock(v,i); var afterTimer=host.GetStockTimerRemaining(v,i);
        var fresh=new EconomyHost(r,bus,new ProbeInventory(),new ProbeExprFactory()); fresh.SetStock(v,i,0); var freshBefore=fresh.GetStock(v,i); fresh.Update(2); var freshAfter=fresh.GetStock(v,i);
        Console.WriteLine($"ECON-NONE-TO-TIMER;load_blocking={load.IsBlocking};reload_blocking={rr.IsBlocking};before_update={before};after_update={after};timer_before={beforeTimer};timer_after={afterTimer};fresh_before={freshBefore};fresh_after={freshAfter}");
        Console.WriteLine("ORACLE=when a retained stock entry adopts timer policy, Update after the timer interval must restock to the current stock limit; null timer state must be initialized on definition transition. A fresh host is the current-definition control.");
    }
}


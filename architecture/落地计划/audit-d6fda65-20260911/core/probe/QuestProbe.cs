using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Gameplay.Quest;
using Tests.Gameplay.Quest;
static class QuestProbe {
public static void Run(){
var id=new Id("quest.audit");var player=TestSupport.Player;
QuestDefinition Def(int n)=>new QuestDefinition(id,Enumerable.Range(0,n).Select(i=>new QuestObjective(QuestObjectiveType.Kill,new Id("creature.wolf_"+i),5)).ToArray(),QuestStartMethod.NpcGossip,QuestTurnInMethod.NpcGossip,QuestRepeatable.None);
var bus=TestSupport.NewEventBus();var units=new FakeUnitAccess();var q=new QuestHost(new[]{Def(1)},bus,new Tests.Rules.Skill.FakeExprHostFactory(),new FakeRewardDispatcher(),new FakeInventoryHost(),units);
q.Accept(player,id);q.UpdateProgress(player,id,0,2);q.Reload(Array.Empty<QuestDefinition>());
try {q.GetActiveObjectives(player);Console.WriteLine("QUEST removed: no exception");}catch(Exception ex){Console.WriteLine("QUEST removed GetActiveObjectives="+ex.GetType().Name+" expected=no exception for unit-only query");}
q.Reload(new[]{Def(2)});
try {q.UpdateProgress(player,id,1,1);Console.WriteLine("QUEST restored shape: no exception");}catch(Exception ex){Console.WriteLine("QUEST restored UpdateProgress="+ex.GetType().Name+" expected=no exception, second objective valid");}
}
}

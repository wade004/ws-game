using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Tests.Rules.Skill;
class Program {
static void Main() { QuestProbe.Run();
foreach(var flushBeforeUpdate in new[]{true,false}) {
var skill=J.O(("id",J.S("skill.audit")),("school",J.S("skill.school_audit")),("kind",J.S("active")),("range",J.N(0)),("cast_time",J.N(1)),("respects_gcd",J.B(false)),("target_shape_ref",J.S("target.chain.audit")),("effects",J.A()));
var b=new SkillWorldBuilder().SkillDef(skill); b.Options.QueueWindow=2; var w=b.Build(); var caster=new Id("unit.audit"); w.AddUnit(caster); w.Targets.SetChain(new Id("target.chain.audit"),caster);
var a=w.Host.CastSkill(caster,new Id("skill.audit"),Array.Empty<Id>()); var q=w.Host.CastSkill(caster,new Id("skill.audit"),Array.Empty<Id>()); w.Flush();
w.Units.SetAlive(caster,false); w.Bus.Enqueue(new UnitDiedEvent(caster,null)); if(flushBeforeUpdate) w.Flush(); w.Host.Update(1); w.Flush();
var interrupted=w.Of<SkillCastInterruptedEvent>().Count(x=>x.CastInstanceId==a.CastInstanceId); var failed=w.Of<SkillCastFailedEvent>().Count(x=>x.CastInstanceId==q.CastInstanceId);
Console.WriteLine($"death_dispatch_before_update={flushBeforeUpdate} active={a.CastInstanceId} queued={q.CastInstanceId} interrupted={interrupted} queueFailed={failed} expected=1,1 casting={w.Host.IsCasting(caster)}");
}
}
}


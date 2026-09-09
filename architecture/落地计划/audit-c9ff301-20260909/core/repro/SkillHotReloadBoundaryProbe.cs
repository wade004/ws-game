using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Skill;
using Tests.Rules.Skill;

static class SkillHotReloadBoundaryProbe
{
    static readonly Id Caster = new Id("unit.skill.audit.caster");
    static readonly Id Target = new Id("unit.skill.audit.target");
    static readonly Id Skill = new Id("skill.audit.hot_reload");
    static JsonObject Def(double cooldown, double damage) => J.O(
        ("id", J.S(Skill.Value)), ("school", J.S("skill.school.audit")), ("kind", J.S("active")),
        ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(false)),
        ("cooldown_duration", J.N(cooldown)), ("target_shape_ref", J.S("target.chain.audit")),
        ("effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(damage)), ("coefficient", J.N(0))))))));
    static string Envelope(JsonObject row) => JsonWriter.Write(J.O(("table", J.S("skill.def")), ("schema_version", J.N(1)), ("rows", J.A(row))));
    static SkillWorld Build(JsonObject def) => new SkillWorldBuilder().SkillDef(def).Build();
    static void AddTargets(SkillWorld world) { world.AddUnit(Caster); world.AddUnit(Target); world.Targets.SetChain(new Id("target.chain.audit"), Target); }
    static double LastDamage(SkillWorld world) => world.Combat.ResolveCalls.Last().BaseValue;
    static void Main()
    {
        var world = Build(Def(0, 7)); AddTargets(world);
        var beforeRegistry = world.Registry.Get("skill.def", Skill)!;
        var hostCooldownBefore = world.Host.GetCooldown(Caster, Skill);
        var first = world.Host.CastSkill(Caster, Skill, Array.Empty<Id>()); world.Flush();
        var damageBefore = LastDamage(world);
        world.Source.Replace("skill.def", Envelope(Def(5, 99)));
        var reload = world.Registry.Reload("skill.def");
        var completedCount = world.Registry.Tables.Sum(table => world.Registry.GetAll(table).Count);
        world.Bus.PublishImmediate(new DataLoadCompletedEvent(world.Registry.Tables.Count, completedCount, reload.ErrorCount, reload.WarningCount));
        var afterRaw = world.Registry.Get("skill.def", Skill)!.Raw;
        var registryCooldownAfter = ((JsonNumber)afterRaw["cooldown_duration"]).Value;
        var effects = (JsonArray)afterRaw["effects"]; var effect = (JsonObject)effects[0]; var parameters = (JsonObject)effect["params"]; var registryDamageAfter = ((JsonNumber)parameters["base_value"]).Value;
        var hostCooldownBeforeSecond = world.Host.GetCooldown(Caster, Skill);
        var second = world.Host.CastSkill(Caster, Skill, Array.Empty<Id>()); world.Flush();
        var damageAfter = LastDamage(world); var hostCooldownAfterSecond = world.Host.GetCooldown(Caster, Skill);
        var fresh = Build(Def(5, 99)); AddTargets(fresh);
        var freshCast = fresh.Host.CastSkill(Caster, Skill, Array.Empty<Id>()); fresh.Flush();
        var freshDamage = LastDamage(fresh); var freshCooldown = fresh.Host.GetCooldown(Caster, Skill);
        Console.WriteLine($"initial_registry_cooldown={((JsonNumber)beforeRegistry.Raw["cooldown_duration"]).Value};host_cooldown_before={hostCooldownBefore};first_cast_success={first.Success};damage_before={damageBefore}");
        Console.WriteLine($"reload_blocking={reload.IsBlocking};reload_error_count={reload.ErrorCount};registry_cooldown_after={registryCooldownAfter};registry_damage_after={registryDamageAfter};host_cooldown_before_second_cast={hostCooldownBeforeSecond};second_cast_success={second.Success};damage_after={damageAfter};host_cooldown_after_second_cast={hostCooldownAfterSecond}");
        Console.WriteLine($"fresh_host_expected_cast_success={freshCast.Success};fresh_host_expected_damage={freshDamage};fresh_host_expected_cooldown_after_cast={freshCooldown}");
        Console.WriteLine("ORACLE-CURRENT=after a successful reload and DataLoadCompleted, an existing SkillHost must either invalidate its SkillDefCache or explicitly document stale behavior; same action compared with a fresh host is the independent oracle. Existing host should use damage=99 and cooldown=5, matching the fresh host.");
        Console.WriteLine("INTERPRETATION-CURRENT=registry exposes the new cooldown=5 and damage=99, while the already constructed host sends the old EffectContext.BaseValue=7 to FakeCombatHost and leaves cooldown=0 after the second cast (the damage labels are BaseValue observations, not HP application). A fresh real SkillHost uses damage=99 and cooldown=5. This is a cache invalidation boundary; process exit 0 is not semantic proof.");
    }
}

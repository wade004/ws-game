using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// P2-05 根治回归测试（外部审计 audit-c9ff301-20260909，改造自
    /// architecture/落地计划/audit-c9ff301-20260909/core/repro/SkillHotReloadBoundaryProbe.cs）：
    /// 开发期 DataHotReload 契约要求"成功 reload 并收到 DataLoadCompletedEvent 之后，既有 resident
    /// SkillHost 的下一次 cast 能看到新定义"，与一个用新数据全新构造的 fresh host 行为一致。此前
    /// <see cref="SkillDefCache"/> 懒解析后永久常驻、从不失效，resident host 在 reload 之后继续沿用
    /// 旧的 cooldown/effect base_value，直到进程重建全新 host 才会看到新值。
    /// </summary>
    public sealed class P2_05_SkillDefCacheHotReloadTests
    {
        /// <summary>最小可写 <see cref="IDataSource"/>：登记时记一份文本，<see cref="Replace"/> 就地
        /// 覆写同一张表（同 <c>InMemoryDataSource.Add</c> 语义近似，但支持覆写而不是重复追加同名表，
        /// 后者会被 <see cref="DataRegistry.Reload"/> 当成"多根合并"处理，不是本测试想验证的单根
        /// 替换场景）。</summary>
        private sealed class MutableSource : IDataSource
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);

            public MutableSource Add(string table, string text) { _texts[table] = text; return this; }

            public void Replace(string table, string text) => _texts[table] = text;

            public IReadOnlyList<DataTableSource> ListTables()
            {
                var result = new List<DataTableSource>();
                foreach (var pair in _texts)
                {
                    var table = pair.Key;
                    result.Add(new DataTableSource(table, "memory://" + table, () => _texts[table]));
                }
                return result;
            }
        }

        private static readonly Id Caster = new Id("unit.p2_05.caster");
        private static readonly Id Target = new Id("unit.p2_05.target");
        private static readonly Id SkillId = new Id("skill.p2_05.hot_reload");
        private static readonly Id ChainId = new Id("target.chain.p2_05");

        private static JsonObject Def(double cooldown, double baseValue) => J.O(
            ("id", J.S(SkillId.Value)), ("school", J.S("skill.school.p2_05")), ("kind", J.S("active")),
            ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(false)),
            ("cooldown_duration", J.N(cooldown)), ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A(J.O(
                ("kind", J.S("school_damage")),
                ("params", J.O(("base_value", J.N(baseValue)), ("coefficient", J.N(0))))))));

        private static string Envelope(JsonObject row) =>
            JsonWriter.Write(J.O(("table", J.S("skill.def")), ("schema_version", J.N(1)), ("rows", J.A(row))));

        private sealed class Harness
        {
            public MutableSource Source = default!;
            public IDataRegistry Registry = default!;
            public IEventBus Bus = default!;
            public FakeUnitAccess Units = default!;
            public FakeCombatHost Combat = default!;
            public FakeTargetHost Targets = default!;
            public SkillHost Host = default!;

            public double LastDamage() => Combat.ResolveCalls.Last().BaseValue;

            /// <summary>同生产 <c>games/_template/Runtime/DataHotReload</c> 的"Reload 成功后自行经
            /// 事件总线补发一次 DataLoadCompletedEvent"惯例（见该类型判断记录——
            /// <see cref="DataRegistry.Reload"/> 本身不发这个事件，只有 <see cref="DataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>
            /// 会发）；record 数取全部已加载表的记录数之和（见 core-findings.md P2-05 判断记录）。</summary>
            public void ReloadTableAndPublishDataLoadCompleted(string table, JsonObject newDef)
            {
                Source.Replace(table, Envelope(newDef));
                var reload = Registry.Reload(table);
                Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));

                var recordCount = Registry.Tables.Sum(t => Registry.GetAll(t).Count);
                Bus.PublishImmediate(new DataLoadCompletedEvent(Registry.Tables.Count, recordCount, reload.ErrorCount, reload.WarningCount));
            }
        }

        private static string EmptyTableEnvelope(string table) =>
            JsonWriter.Write(J.O(("table", J.S(table)), ("schema_version", J.N(1)), ("rows", J.A())));

        private static Harness Build(JsonObject def)
        {
            var source = new MutableSource()
                .Add("skill.def", Envelope(def))
                .Add("stat.definition", EmptyTableEnvelope("stat.definition"));
            var bus = SkillWorldBuilder.CreateBus();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(SkillSchemas.AuraDef);
            registry.RegisterSchema(SkillSchemas.ProcDef);
            registry.RegisterSchema(SkillSchemas.SpellModDef);
            registry.RegisterSchema(SkillSchemas.Book);
            registry.RegisterSchema(StatSchemas.Definition);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var stats = new StatHost(registry, bus);
            var powers = new PowerHost(Array.Empty<PowerTypeDefinition>(), bus);
            var rng = new RngHost(1);
            var units = new FakeUnitAccess();
            var combat = new FakeCombatHost();
            var targets = new FakeTargetHost();
            var exprs = new FakeExprHostFactory();
            var diagnostics = new InMemorySkillDiagnostics();

            var host = new SkillHost(
                registry, bus, units, stats, powers, rng, combat, targets, exprs, spatialQuery: null,
                diagnostics: diagnostics);

            units.Add(Caster);
            units.Add(Target);
            targets.SetChain(ChainId, Target);

            return new Harness
            {
                Source = source, Registry = registry, Bus = bus,
                Units = units, Combat = combat, Targets = targets, Host = host,
            };
        }

        [Fact]
        public void P2_05_ResidentHost_SeesReloadedCooldownAndEffectValue_AfterDataLoadCompleted()
        {
            var world = Build(Def(cooldown: 0, baseValue: 7));

            var first = world.Host.CastSkill(Caster, SkillId, Array.Empty<Id>());
            Assert.True(first.Success);
            Assert.Equal(7, world.LastDamage());

            world.ReloadTableAndPublishDataLoadCompleted("skill.def", Def(cooldown: 5, baseValue: 99));

            // resident host 的第二次 cast 必须看到新 cooldown（施法后 cooldown 应为 5，而不是仍是 0）
            // 与新 effect base_value（99，而不是旧的 7）。
            var second = world.Host.CastSkill(Caster, SkillId, Array.Empty<Id>());
            Assert.True(second.Success);
            Assert.Equal(99, world.LastDamage());
            Assert.Equal(5, world.Host.GetCooldown(Caster, SkillId));

            // 独立 oracle：全新构造的 fresh host（同一份已重载的 registry）应得到同样的结果。
            var freshUnits = new FakeUnitAccess();
            var freshCombat = new FakeCombatHost();
            var freshTargets = new FakeTargetHost();
            var freshBus = SkillWorldBuilder.CreateBus();
            var freshStats = new StatHost(world.Registry, freshBus);
            var freshPowers = new PowerHost(Array.Empty<PowerTypeDefinition>(), freshBus);
            var freshRng = new RngHost(1);
            var freshHost = new SkillHost(
                world.Registry, freshBus, freshUnits, freshStats, freshPowers, freshRng, freshCombat, freshTargets,
                new FakeExprHostFactory(), spatialQuery: null, diagnostics: new InMemorySkillDiagnostics());
            freshUnits.Add(Caster);
            freshUnits.Add(Target);
            freshTargets.SetChain(ChainId, Target);

            var freshCast = freshHost.CastSkill(Caster, SkillId, Array.Empty<Id>());
            Assert.True(freshCast.Success);
            Assert.Equal(99, freshCombat.ResolveCalls.Last().BaseValue);
            Assert.Equal(5, freshHost.GetCooldown(Caster, SkillId));
        }

        [Fact]
        public void P2_05_SkillDefCache_InvalidateAll_ClearsAllFiveTables()
        {
            var world = Build(Def(cooldown: 0, baseValue: 7));
            var cache = new SkillDefCache(world.Registry);
            Assert.True(cache.TryGetSkillDef(SkillId, out _));

            world.Source.Replace("skill.def", Envelope(Def(cooldown: 5, baseValue: 99)));
            world.Registry.Reload("skill.def");

            // 未失效前仍返回旧值（懒解析后常驻，见 SkillDefCache 类型判断记录）。
            Assert.True(cache.TryGetSkillDef(SkillId, out var stale));
            Assert.Equal(0, stale.CooldownDuration);

            cache.InvalidateAll();

            Assert.True(cache.TryGetSkillDef(SkillId, out var fresh));
            Assert.Equal(5, fresh.CooldownDuration);
        }
    }
}

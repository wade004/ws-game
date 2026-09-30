using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;
using Core.Rules.Skill;
using Tests.Rules.Culture;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 测试覆盖梳理 T-H13：<c>AuraHost.Update</c> 对折算后 <c>interval &lt;= 0</c> 的周期效果记诊断时，
    /// 把 interval 数值拼进告警文本——这段文本必须与当前文化无关（de-DE 下默认插值会写成 "-0,5"）。
    /// 姊妹文件 <see cref="AuraHostIntervalDiagnosticTests"/> 覆盖不变文化下的诊断行为本身。
    /// </summary>
    public sealed class AuraHostCultureInvarianceTests
    {
        private static readonly Id AuraDefId = new Id("skill.aura_def.culture_bypass");
        private static readonly Id TargetId = new Id("test.target");
        private static readonly Id SourceId = new Id("test.source");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>同 <see cref="AuraHostIntervalDiagnosticTests"/> 的装配：用 Unschematized 表绕过 field_range，
        /// 让非法 interval 进入 AuraHost 的防御性诊断分支。</summary>
        private static List<string> CollectIntervalWarnings(double interval)
        {
            var auraRow = "[{\"id\": \"" + AuraDefId.Value + "\", \"effects\": [" +
                "{\"kind\": \"periodic_damage\", \"params\": {\"interval\": " +
                interval.ToString(CultureInfo.InvariantCulture) +
                ", \"school\": \"skill.school_culture\"}}]}]";

            var source = new InMemoryDataSource()
                .Add("skill.aura_def", Envelope("skill.aura_def", auraRow))
                .Add("stat.definition", Envelope("stat.definition", "[]"));

            var bus = SkillWorldBuilder.CreateBus();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(TableSchema.Unschematized("skill.aura_def", "id"));
            registry.RegisterSchema(StatSchemas.Definition);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var stats = new StatHost(registry, bus);
            stats.RegisterUnit(TargetId);
            var defs = new SkillDefCache(registry);
            var diagnostics = new InMemorySkillDiagnostics();
            var auraHost = new AuraHost(defs, stats, bus, new SkillOptions(), diagnostics);

            auraHost.ApplyAura(TargetId, AuraDefId, SourceId, durationOverride: null);
            auraHost.Update(1.0);

            return new List<string>(diagnostics.Warnings);
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void IntervalWarningText_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            foreach (var interval in new[] { -0.5, -1234.5, 0.0 })
            {
                var baseline = CultureScope.Run("", () => CollectIntervalWarnings(interval));
                var actual = CultureScope.Run(culture, () => CollectIntervalWarnings(interval));

                Assert.Equal(baseline, actual);

                // 规则：告警文本里的 interval 数值是折算后值的不变文化 "G" 格式（timeFactor 为 1 时即 interval 本身）
                Assert.Contains(baseline, w => w.Contains("interval=" + interval.ToString(CultureInfo.InvariantCulture) + " <= 0"));
            }
        }
    }
}

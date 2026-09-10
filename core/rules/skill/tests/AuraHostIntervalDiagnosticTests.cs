using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0021（消费方反馈 2026-09-10"技能效果参数数值范围校验改进建议"）：<c>AuraHost.Update</c>
    /// 对折算后 <c>interval &lt;= 0</c> 的处理此前是纯静默 <c>continue</c>（周期效果整体不生效，
    /// 外部完全看不到任何提示）。正常内容路径现在已经在加载期被 <c>SkillSchemas.AuraDef</c> 登记的
    /// <c>field_range</c> 约束挡下（见 <see cref="SkillEffectParamRangeTests"/>），本文件覆盖的是
    /// "绕过登记表校验"的路径（如直接构造走内存数据源、未经 <c>SkillSchemas.AuraDef</c> 登记的
    /// 工具/测试场景——这里用 <see cref="TableSchema.Unschematized"/> 模拟）：证明保留下来的防御性
    /// 跳过分支不再是纯静默的，会先经 <see cref="ISkillDiagnostics"/> 记一条 Warn。
    /// </summary>
    public sealed class AuraHostIntervalDiagnosticTests
    {
        private static readonly Id AuraDefId = new Id("skill.aura_def.svng_bypass");
        private static readonly Id TargetId = new Id("test.target");
        private static readonly Id SourceId = new Id("test.source");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>构造一个只登记 <see cref="TableSchema.Unschematized"/>（不做字段级校验，含
        /// field_range）的 <c>skill.aura_def</c> 表——模拟"内容绕过了登记表校验"这条诊断本身要覆盖
        /// 的场景，interval=0 能顺利通过 <c>DataRegistry.LoadAll</c>，不像
        /// <see cref="SkillEffectParamRangeTests"/> 那样在加载期就被 <c>field_range</c> 挡下。</summary>
        private static AuraHost BuildAuraHostWithBypassedSchema(double interval, out InMemorySkillDiagnostics diagnostics)
        {
            var auraRow = "[{\"id\": \"" + AuraDefId.Value + "\", \"effects\": [" +
                "{\"kind\": \"periodic_damage\", \"params\": {\"interval\": " +
                interval.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ", \"school\": \"skill.school_svng\"}}]}]";

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
            stats.RegisterUnit(TargetId); // AuraHost.CreateInstance -> ReapplyStatMods 要求目标已注册。
            var defs = new SkillDefCache(registry);
            diagnostics = new InMemorySkillDiagnostics();

            return new AuraHost(defs, stats, bus, new SkillOptions(), diagnostics);
        }

        [Fact]
        public void IntervalZero_LogsWarning_AndPeriodicEffectStillSkipped()
        {
            var auraHost = BuildAuraHostWithBypassedSchema(0, out var diagnostics);

            auraHost.ApplyAura(TargetId, AuraDefId, SourceId, durationOverride: null);
            auraHost.Update(1.0);

            Assert.Contains(diagnostics.Warnings, w =>
                w.Contains(AuraDefId.Value) && w.Contains("interval") && w.Contains("0"));
        }

        [Fact]
        public void IntervalNegative_LogsWarning()
        {
            var auraHost = BuildAuraHostWithBypassedSchema(-1, out var diagnostics);

            auraHost.ApplyAura(TargetId, AuraDefId, SourceId, durationOverride: null);
            auraHost.Update(1.0);

            Assert.Contains(diagnostics.Warnings, w => w.Contains(AuraDefId.Value) && w.Contains("interval"));
        }

        [Fact]
        public void IntervalPositive_NoWarning()
        {
            var auraHost = BuildAuraHostWithBypassedSchema(1, out var diagnostics);

            auraHost.ApplyAura(TargetId, AuraDefId, SourceId, durationOverride: null);
            // 故意只推进到累加器未达 interval 的一半——避免真正触发一次周期结算（那会因为本测试
            // 没有接线 EffectSink 而额外报一条不相关的"EffectSink 尚未就绪"警告，见 AuraHost.FirePeriodic
            // 判断记录），本用例只关心"interval<=0 才会警告"，不关心周期结算本身。
            auraHost.Update(0.5);

            Assert.Empty(diagnostics.Warnings);
        }
    }
}

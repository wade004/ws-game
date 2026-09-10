using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;
using VfxSfxSchemas = Presentation.VfxSfx.Schema.VfxSfxSchemas;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>
    /// ADR-0024 第二批登记：<see cref="VfxSfxSchemas.WeaponStyle"/> 的
    /// <c>cast_anim_override</c>/<c>impact_vfx_override</c> 改用 <c>MapSchema.FreeKeyed</c>
    /// （值种类 <c>FieldKind.Id</c>）登记，覆盖范围：子结构命中/坏形状各一例（<c>WeaponStyleDef.
    /// FromRecord</c> 自身防御代码路径的覆盖见 <c>WeaponStyleResolverTests.cs</c>，不在本文件重复）。
    /// </summary>
    public sealed class VfxSfxSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void CastAnimAndImpactVfxOverride_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"display.weapon_style.cov_sample\",\"auto_attack_anim\":\"anim.cov_swing\"," +
                "\"cast_anim_override\":{\"skill.cov_fireball\":\"anim.cov_cast\"}," +
                "\"impact_vfx_override\":{\"skill.cov_cleave\":\"vfx.cov_hit\"}}]";

            var source = new InMemoryDataSource().Add(
                VfxSfxSchemas.WeaponStyle.Name,
                Envelope(VfxSfxSchemas.WeaponStyle.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(VfxSfxSchemas.WeaponStyle);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void CastAnimOverride_ValueNotValidId_ReportsFieldTypeWithBracketPath()
        {
            var rows = "[{\"id\":\"display.weapon_style.cov_bad\",\"auto_attack_anim\":\"anim.cov_swing\"," +
                "\"cast_anim_override\":{\"skill.cov_fireball\":\"not a valid id\"}}]";

            var source = new InMemoryDataSource().Add(
                VfxSfxSchemas.WeaponStyle.Name,
                Envelope(VfxSfxSchemas.WeaponStyle.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(VfxSfxSchemas.WeaponStyle);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_type" && i.Field == "cast_anim_override[skill.cov_fireball]");
        }

        [Fact]
        public void ImpactVfxOverride_ArbitraryKey_NoUnknownSubfieldReported()
        {
            var rows = "[{\"id\":\"display.weapon_style.cov_map\",\"auto_attack_anim\":\"anim.cov_swing\"," +
                "\"impact_vfx_override\":{\"skill.cov_a\":\"vfx.cov_a\",\"skill.cov_b\":\"vfx.cov_b\"}}]";

            var source = new InMemoryDataSource().Add(
                VfxSfxSchemas.WeaponStyle.Name,
                Envelope(VfxSfxSchemas.WeaponStyle.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(VfxSfxSchemas.WeaponStyle);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "unknown_subfield");
        }
    }
}

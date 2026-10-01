using System;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary><see cref="FeelWeaponCatalog"/>：从 <c>feel.weapon</c> 行读出换装链需要的非分层字段（族、普攻时间线引用、参考节奏）。</summary>
    public class FeelWeaponCatalogTests
    {
        private const string Rows =
            "[" +
            "{\"id\": \"feel.weapon.cat_sword\", \"family\": \"1h\", \"auto_attack_timeline_ref\": \"skill.cat.attack_sword\"," +
            " \"timeline_reference\": {\"startup_ms\": 120, \"active_ms\": 80, \"recovery_ms\": 200}}," +
            "{\"id\": \"feel.weapon.cat_plain\", \"family\": \"2h\"}" +
            "]";

        private static string WeaponTable() => "{\"table\":\"feel.weapon\",\"schema_version\":1,\"rows\":" + Rows + "}";

        [Fact]
        public void Catalog_ReadsFamilyTimelineRefAndReference_FromTheRows()
        {
            var (registry, report) = Load(new[] { ("feel.weapon", WeaponTable()) });
            // 只有"没有标定行"这一条（与本类无关）：目录读的是已加载行，不依赖装配。
            Assert.All(report.Issues, i => Assert.Equal(FeelChecks.CalibrationMissing, i.Check));

            var catalog = new FeelWeaponCatalog(registry);

            Assert.Equal(new[] { "feel.weapon.cat_plain", "feel.weapon.cat_sword" }, catalog.Refs);
            Assert.True(catalog.TryGet("feel.weapon.cat_sword", out var sword));
            Assert.Equal("1h", sword.Family);
            Assert.Equal(new Id("skill.cat.attack_sword"), sword.AutoAttackTimelineRef);
            Assert.Equal(120, sword.TimelineReference!.StartupMs);
            Assert.Equal(80, sword.TimelineReference.ActiveMs);
            Assert.Equal(200, sword.TimelineReference.RecoveryMs);

            Assert.True(catalog.TryGet("feel.weapon.cat_plain", out var plain));
            Assert.Equal("2h", plain.Family);
            Assert.Null(plain.AutoAttackTimelineRef);
            Assert.Null(plain.TimelineReference);
        }

        [Fact]
        public void Catalog_UnknownOrNullRef_IsNotFound_AndMissingTableIsEmpty()
        {
            var (registry, _) = Load(new[] { ("feel.weapon", WeaponTable()) });
            var catalog = new FeelWeaponCatalog(registry);
            Assert.False(catalog.TryGet("feel.weapon.nope", out _));
            Assert.False(catalog.TryGet(null, out _));

            var (emptyRegistry, _) = Load(Array.Empty<(string, string)>());
            Assert.Empty(new FeelWeaponCatalog(emptyRegistry).Refs);
        }
    }
}

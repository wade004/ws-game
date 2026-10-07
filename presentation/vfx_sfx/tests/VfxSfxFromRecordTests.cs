using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Schema;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>验证 <see cref="VfxDef.FromRecord"/>/<see cref="SfxDef.FromRecord"/>/
    /// <see cref="WeaponStyleDef.FromRecord"/> 能从经 <see cref="Core.Foundation.DataRegistry.DataRegistry"/>
    /// 加载的记录正确解析（呼应 09/04 "数据即内容"：本模块的运行期类型必须能从真实数据表构造，
    /// 不只是测试里手写的 C# 构造函数）。</summary>
    public class VfxSfxFromRecordTests
    {
        [Fact]
        public void VfxDef_FromRecord_ParsesAllFields()
        {
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["vfx.def"] = "[" + VfxSfxTestSupport.FireImpactVfxRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("vfx.def", "vfx.fire_impact")!;
            var def = VfxDef.FromRecord(record);

            Assert.Equal(new Id("vfx.fire_impact"), def.Id);
            Assert.Equal("impact", def.Category);
            Assert.Equal(VfxAttachMode.World, def.AttachMode);
            Assert.Equal(1.5, def.Lifetime);
            Assert.Equal(new Id("res.vfx.fire_impact"), def.ResourceRef);

            // ADR-0074：未声明 blend_mode 时默认 Alpha，与改动前逐字一致的既有行为。
            Assert.Equal(Core.Foundation.EngineAdapter.VfxBlendMode.Alpha, def.BlendMode);
        }

        [Fact]
        public void VfxDef_FromRecord_ParsesUpright_DefaultsToFalse()
        {
            const string row = @"
            {
              ""id"": ""vfx.upright_spark"",
              ""category"": ""impact"",
              ""attach_mode"": ""world"",
              ""resource_ref"": ""res.vfx.upright_spark"",
              ""upright"": true
            }";
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["vfx.def"] = "[" + row + "," + VfxSfxTestSupport.FireImpactVfxRow + "]",
            });
            Assert.False(report.IsBlocking);

            Assert.True(VfxDef.FromRecord(registry.Get("vfx.def", "vfx.upright_spark")!).Upright);
            Assert.False(VfxDef.FromRecord(registry.Get("vfx.def", "vfx.fire_impact")!).Upright);
        }

        [Fact]
        public void VfxDef_FromRecord_ParsesSortOrder_DefaultsToZero()
        {
            const string row = @"
            {
              ""id"": ""vfx.ground_warn"",
              ""category"": ""telegraph"",
              ""attach_mode"": ""world"",
              ""resource_ref"": ""res.vfx.ground_warn"",
              ""sort_order"": 3
            }";
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["vfx.def"] = "[" + row + "," + VfxSfxTestSupport.FireImpactVfxRow + "]",
            });
            Assert.False(report.IsBlocking);

            Assert.Equal(3, VfxDef.FromRecord(registry.Get("vfx.def", "vfx.ground_warn")!).SortOrder);
            Assert.Equal(0, VfxDef.FromRecord(registry.Get("vfx.def", "vfx.fire_impact")!).SortOrder);
        }

        [Fact]
        public void VfxDef_FromRecord_ParsesExplicitAdditiveBlendMode()
        {
            const string row = @"
            {
              ""id"": ""vfx.spark_additive"",
              ""category"": ""impact"",
              ""attach_mode"": ""world"",
              ""resource_ref"": ""res.vfx.spark_additive"",
              ""blend_mode"": ""additive""
            }";

            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["vfx.def"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("vfx.def", "vfx.spark_additive")!;
            var def = VfxDef.FromRecord(record);

            Assert.Equal(Core.Foundation.EngineAdapter.VfxBlendMode.Additive, def.BlendMode);
        }

        [Fact]
        public void SfxDef_FromRecord_ParsesVariantsAndPriority()
        {
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["sfx.def"] = "[" + VfxSfxTestSupport.SwordHitSfxRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("sfx.def", "sfx.sword_hit")!;
            var def = SfxDef.FromRecord(record);

            Assert.Equal("combat", def.Layer);
            Assert.Equal(5, def.Priority);
            Assert.NotNull(def.Variants);
            Assert.Equal(2, def.Variants!.Count);
            Assert.Equal(new Id("res.sfx.sword_hit_1"), def.ResourceRef);

            // ADR-0089：未声明 loop 时默认 false，与本字段新增前的既有行为逐字一致。
            Assert.False(def.Loop);
        }

        [Fact]
        public void SfxDef_FromRecord_ParsesExplicitLoopTrue()
        {
            const string row = @"
            {
              ""id"": ""sfx.buff_hum"",
              ""layer"": ""combat"",
              ""resource_ref"": ""res.sfx.buff_hum"",
              ""loop"": true
            }";

            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["sfx.def"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("sfx.def", "sfx.buff_hum")!;
            var def = SfxDef.FromRecord(record);

            Assert.True(def.Loop);
        }

        [Fact]
        public void WeaponStyleDef_FromRecord_ParsesOverrideMaps()
        {
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.weapon_style"] = "[" + VfxSfxTestSupport.GreatswordWeaponStyleRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.weapon_style", "display.weapon_style.greatsword")!;
            var def = WeaponStyleDef.FromRecord(record);

            Assert.Equal(new Id("anim.greatsword.auto_attack"), def.AutoAttackAnim);
            Assert.Equal(new Id("vfx.greatsword_swing"), def.SwingVfx);
            Assert.Equal(new Id("vfx.cleave_impact"), def.ImpactVfxOverride[new Id("skill.cleave")]);
            Assert.Equal(new Id("anim.greatsword.cleave"), def.CastAnimOverride[new Id("skill.cleave")]);
        }

        // ------------------------------------------------------------------
        // T-M14（ADR-0125）：错误路径。直接用 DataRecord 构造函数喂绕过校验的坏行。
        // ------------------------------------------------------------------

        private static DataRecord Raw(TableSchema table, string key, string json) =>
            new DataRecord(table, key, null, (JsonObject)JsonReader.Parse(json));

        private const string GoodVfx =
            "\"id\":\"vfx.t\",\"category\":\"combat\",\"attach_mode\":\"world\",\"resource_ref\":\"res.vfx_t\"";

        [Theory]
        [InlineData("{\"category\":\"combat\",\"attach_mode\":\"world\",\"resource_ref\":\"res.a\"}", "id")]
        [InlineData("{\"id\":\"vfx.t\",\"attach_mode\":\"world\",\"resource_ref\":\"res.a\"}", "category")]
        [InlineData("{\"id\":\"vfx.t\",\"category\":\"combat\",\"resource_ref\":\"res.a\"}", "attach_mode")]
        [InlineData("{\"id\":\"vfx.t\",\"category\":\"combat\",\"attach_mode\":\"world\"}", "resource_ref")]
        [InlineData("{\"id\":3,\"category\":\"combat\",\"attach_mode\":\"world\",\"resource_ref\":\"res.a\"}", "id")]
        [InlineData("{\"id\":\"vfx.t\",\"category\":7,\"attach_mode\":\"world\",\"resource_ref\":\"res.a\"}", "category")]
        [InlineData("{\"id\":\"vfx.t\",\"category\":\"combat\",\"attach_mode\":false,\"resource_ref\":\"res.a\"}", "attach_mode")]
        [InlineData("{\"id\":\"vfx.t\",\"category\":\"combat\",\"attach_mode\":\"world\",\"resource_ref\":\"Bad Ref\"}", "resource_ref")]
        public void VfxDef_FromRecord_MissingOrWrongTypeRequiredField_ThrowsDataFieldException(string rowJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => VfxDef.FromRecord(Raw(VfxSfxSchemas.Vfx, "vfx.t", rowJson)));

            Assert.Equal(expectedField, ex.Field);
            Assert.Equal("vfx.def", ex.Table);
        }

        [Theory]
        [InlineData("attach_mode", "teleport")]
        [InlineData("attach_mode", "WORLD")]
        [InlineData("attach_mode", "")]
        [InlineData("blend_mode", "multiply")]
        [InlineData("blend_mode", "ADDITIVE")]
        public void VfxDef_FromRecord_UnknownEnumValue_ThrowsDataFieldException_NamingTheField(string field, string value)
        {
            var row = field == "attach_mode"
                ? "{\"id\":\"vfx.t\",\"category\":\"combat\",\"attach_mode\":\"" + value + "\",\"resource_ref\":\"res.a\"}"
                : "{" + GoodVfx + ",\"blend_mode\":\"" + value + "\"}";

            var ex = Assert.Throws<DataFieldException>(() => VfxDef.FromRecord(Raw(VfxSfxSchemas.Vfx, "vfx.t", row)));

            Assert.Equal(field, ex.Field);
            Assert.Contains("未知的 " + field + " 取值", ex.Message);
        }

        [Theory]
        [InlineData("{\"layer\":\"combat\",\"resource_ref\":\"res.a\"}", "id")]
        [InlineData("{\"id\":\"sfx.t\",\"resource_ref\":\"res.a\"}", "layer")]
        [InlineData("{\"id\":\"sfx.t\",\"layer\":\"combat\"}", "resource_ref")]
        [InlineData("{\"id\":true,\"layer\":\"combat\",\"resource_ref\":\"res.a\"}", "id")]
        [InlineData("{\"id\":\"sfx.t\",\"layer\":5,\"resource_ref\":\"res.a\"}", "layer")]
        [InlineData("{\"id\":\"sfx.t\",\"layer\":\"combat\",\"resource_ref\":\"Bad Ref\"}", "resource_ref")]
        public void SfxDef_FromRecord_MissingOrWrongTypeRequiredField_ThrowsDataFieldException(string rowJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => SfxDef.FromRecord(Raw(VfxSfxSchemas.Sfx, "sfx.t", rowJson)));

            Assert.Equal(expectedField, ex.Field);
            Assert.Equal("sfx.def", ex.Table);
        }

        [Theory]
        [InlineData("{\"auto_attack_anim\":\"anim.a\"}", "id")]
        [InlineData("{\"id\":\"display.weapon_style.t\"}", "auto_attack_anim")]
        [InlineData("{\"id\":\"display.weapon_style.t\",\"auto_attack_anim\":12}", "auto_attack_anim")]
        [InlineData("{\"id\":\"display.weapon_style.t\",\"auto_attack_anim\":\"Bad Anim\"}", "auto_attack_anim")]
        public void WeaponStyleDef_FromRecord_MissingOrWrongTypeRequiredField_ThrowsDataFieldException(string rowJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => WeaponStyleDef.FromRecord(Raw(VfxSfxSchemas.WeaponStyle, "display.weapon_style.t", rowJson)));

            Assert.Equal(expectedField, ex.Field);
            Assert.Equal("display.weapon_style", ex.Table);
        }

        [Theory]
        [InlineData("cast_anim_override", "{\"Bad Key\":\"anim.a\"}", "键 \"Bad Key\" 不是合法 Id")]
        [InlineData("cast_anim_override", "{\"skill.a\":\"Bad Value\"}", "值不是合法 Id 字符串")]
        [InlineData("impact_vfx_override", "{\"skill.a\":42}", "值不是合法 Id 字符串")]
        [InlineData("impact_vfx_override", "{\"skill\":\"vfx.a\"}", "键 \"skill\" 不是合法 Id")]
        public void WeaponStyleDef_FromRecord_BadOverrideMapEntry_ThrowsDataFieldException_NamingTheMapField(
            string mapField, string mapJson, string expectedMessagePart)
        {
            var row = "{\"id\":\"display.weapon_style.t\",\"auto_attack_anim\":\"anim.a\",\"" + mapField + "\":" + mapJson + "}";

            var ex = Assert.Throws<DataFieldException>(() =>
                WeaponStyleDef.FromRecord(Raw(VfxSfxSchemas.WeaponStyle, "display.weapon_style.t", row)));

            Assert.Equal(mapField, ex.Field);
            Assert.Contains(expectedMessagePart, ex.Message);
        }

        [Fact]
        public void VfxSfxDefs_Constructors_NullStringArguments_Throw()
        {
            Assert.Equal("category", Assert.Throws<System.ArgumentNullException>(
                () => new VfxDef(new Id("vfx.t"), null!, VfxAttachMode.World, null, new Id("res.a"))).ParamName);
            Assert.Equal("layer", Assert.Throws<System.ArgumentNullException>(
                () => new SfxDef(new Id("sfx.t"), null!, null, null, new Id("res.a"))).ParamName);
        }
    }
}

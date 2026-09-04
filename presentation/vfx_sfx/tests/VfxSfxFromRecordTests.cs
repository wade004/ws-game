using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.VfxSfx.Contracts;
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
    }
}

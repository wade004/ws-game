using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class DisplayInfoResolverTests
    {
        [Fact]
        public void ResolveVfxAndSfx_FromDisplayMap_ReturnsRegisteredIds()
        {
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + VfxSfxTestSupport.GreyWolfDisplayRow + "]",
            });
            Assert.False(report.IsBlocking);

            var displayInfoRegistry = new DisplayInfoRegistry(registry, VfxSfxTestSupport.CreateBus());
            var resolver = new DisplayInfoResolver(displayInfoRegistry);

            Assert.Equal(new Id("vfx.fire_impact"), resolver.ResolveVfx(new Id("creature.grey_wolf")));
            Assert.Equal(new Id("sfx.sword_hit"), resolver.ResolveSfx(new Id("creature.grey_wolf")));
        }

        [Fact]
        public void Resolve_ReturnsNull_WhenLogicalIdNotRegistered()
        {
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + VfxSfxTestSupport.GreyWolfDisplayRow + "]",
            });
            Assert.False(report.IsBlocking);

            var resolver = new DisplayInfoResolver(new DisplayInfoRegistry(registry, VfxSfxTestSupport.CreateBus()));

            Assert.Null(resolver.ResolveVfx(new Id("creature.unregistered")));
            Assert.Null(resolver.ResolveSfx(new Id("creature.unregistered")));
        }

        [Fact]
        public void Resolve_ReturnsNull_ForNonDefaultSlot()
        {
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + VfxSfxTestSupport.GreyWolfDisplayRow + "]",
            });
            Assert.False(report.IsBlocking);

            var resolver = new DisplayInfoResolver(new DisplayInfoRegistry(registry, VfxSfxTestSupport.CreateBus()));

            Assert.Null(resolver.ResolveVfx(new Id("creature.grey_wolf"), "cast_start"));
        }
    }
}

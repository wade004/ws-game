using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Xunit;
using Presentation.Render;
using DisplayShadowMode = Core.Foundation.DisplayInfo.ShadowMode;
using EngineShadowMode = Core.Foundation.EngineAdapter.ShadowMode;

namespace Tests.PresentationRender
{
    /// <summary>
    /// T-L16（测试覆盖剩余项第四批，render 部分）：<see cref="RenderOptions"/> 默认值、
    /// <see cref="RenderLayers"/> 常量、<see cref="ShadowSpec"/> 全映射、<see cref="SpriteLayerPlacement"/>
    /// 取值。这些都是对外约定（<c>const</c> 会被内联进消费方程序集，改值等于 ABI 破坏），用固定用例钉住。
    /// </summary>
    public class RenderDefaultsTests
    {
        // ---------------- RenderOptions ----------------

        [Fact]
        public void RenderOptions_Defaults_AreTheDocumentedOnes()
        {
            var options = new RenderOptions();

            Assert.Equal(8, options.DirectionCount);
            Assert.Equal(32.0, options.PixelsPerUnit);
            Assert.Null(options.DirectionIndexRemap);
            Assert.Equal(HitFrameSyncStrategy.LogicDriven, options.HitFrameSync);
            Assert.False(options.MirrorFacingY);
            Assert.Equal(0.0, options.FacingAngleOffsetRadians);
        }

        [Fact]
        public void RenderOptions_EveryProperty_IsSettable_AndIndependent()
        {
            var remap = new[] { 2, 1, 0 };
            var options = new RenderOptions
            {
                DirectionCount = 16,
                PixelsPerUnit = 64.0,
                DirectionIndexRemap = remap,
                HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven,
                MirrorFacingY = true,
                FacingAngleOffsetRadians = Math.PI,
            };

            Assert.Equal(16, options.DirectionCount);
            Assert.Equal(64.0, options.PixelsPerUnit);
            Assert.Same(remap, options.DirectionIndexRemap);
            Assert.Equal(HitFrameSyncStrategy.AnimKeyframeDriven, options.HitFrameSync);
            Assert.True(options.MirrorFacingY);
            Assert.Equal(Math.PI, options.FacingAngleOffsetRadians);
            // 另一个实例不受影响（默认值不是共享可变状态）
            Assert.Equal(8, new RenderOptions().DirectionCount);
        }

        [Fact]
        public void HitFrameSyncStrategy_HasExactlyTheTwoDocumentedStrategies_LogicDrivenIsDefaultValue()
        {
            var names = Enum.GetNames(typeof(HitFrameSyncStrategy));

            Assert.Equal(new[] { "LogicDriven", "AnimKeyframeDriven" }, names);
            Assert.Equal(HitFrameSyncStrategy.LogicDriven, default(HitFrameSyncStrategy));
        }

        // ---------------- RenderLayers ----------------

        [Fact]
        public void RenderLayers_Constants_ArePinned_AndOrderedBackToFront()
        {
            Assert.Equal(0, RenderLayers.Ground);
            Assert.Equal(1, RenderLayers.Decoration);
            Assert.Equal(2, RenderLayers.Units);
            Assert.Equal(3, RenderLayers.Foreground);
            Assert.Equal(4, RenderLayers.Vfx);
            Assert.Equal(5, RenderLayers.Ui);

            var ordered = new[]
            {
                RenderLayers.Ground, RenderLayers.Decoration, RenderLayers.Units,
                RenderLayers.Foreground, RenderLayers.Vfx, RenderLayers.Ui,
            };
            for (var i = 1; i < ordered.Length; i++)
            {
                Assert.True(ordered[i - 1] < ordered[i], "层序必须严格自底向上递增");
            }
        }

        // ---------------- ShadowSpec ----------------

        [Fact]
        public void ShadowSpec_ResolveAnchor_IsIdentityOnLogicalPosition()
        {
            var pos = new Vec2(3.5, -7.25);

            Assert.Equal(pos, ShadowSpec.ResolveAnchor(pos));
            Assert.Equal(Vec2.Zero, ShadowSpec.ResolveAnchor(Vec2.Zero));
        }

        [Fact]
        public void ShadowSpec_ToEngineShadowMode_MapsEveryDisplayModeToTheSameNamedEngineMode()
        {
            foreach (DisplayShadowMode mode in Enum.GetValues(typeof(DisplayShadowMode)))
            {
                var expected = (EngineShadowMode)Enum.Parse(typeof(EngineShadowMode), mode.ToString());

                Assert.Equal(expected, ShadowSpec.ToEngineShadowMode(mode));
            }
        }

        [Fact]
        public void ShadowSpec_EngineAndDisplayModeSets_HaveTheSameNames_SoTheMappingIsTotal()
        {
            var display = new List<string>(Enum.GetNames(typeof(DisplayShadowMode)));
            var engine = new List<string>(Enum.GetNames(typeof(EngineShadowMode)));
            display.Sort(StringComparer.Ordinal);
            engine.Sort(StringComparer.Ordinal);

            Assert.Equal(display, engine);
        }

        [Fact]
        public void ShadowSpec_ToEngineShadowMode_UndefinedValue_ThrowsArgumentOutOfRange()
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ShadowSpec.ToEngineShadowMode((DisplayShadowMode)int.MaxValue));

            Assert.Equal("mode", ex.ParamName);
        }

        // ---------------- SpriteLayerPlacement ----------------

        [Fact]
        public void SpriteLayerPlacement_ExposesConstructorArguments()
        {
            var slot = new Id("dir.front");

            var placement = new SpriteLayerPlacement("hand_main", slot, flipX: true);

            Assert.Equal("hand_main", placement.LayerName);
            Assert.Equal(slot, placement.DirectionSlotId);
            Assert.True(placement.FlipX);
        }

        [Fact]
        public void SpriteLayerPlacement_Default_HasNullNameDefaultSlotAndNoFlip()
        {
            var placement = default(SpriteLayerPlacement);

            Assert.Null(placement.LayerName);
            Assert.Equal(default(Id), placement.DirectionSlotId);
            Assert.False(placement.FlipX);
        }
    }
}

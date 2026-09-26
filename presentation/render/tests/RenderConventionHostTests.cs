using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Common;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.PresentationRender
{
    public class RenderConventionHostTests
    {
        private readonly RenderConventionHost _host = new RenderConventionHost();

        [Fact]
        public void ComputeSortY_AddsSortOffsetToLogicalY()
        {
            var sortY = _host.ComputeSortY(new Vec2(10, 20), 1.5);

            Assert.Equal(21.5, sortY);
        }

        [Fact]
        public void TieBreakComparer_EqualSortY_OrdersByIdAscending()
        {
            var a = (Id: new Id("unit.a"), SortY: 5.0);
            var b = (Id: new Id("unit.b"), SortY: 5.0);

            Assert.True(_host.TieBreakComparer.Compare(a, b) < 0);
            Assert.True(_host.TieBreakComparer.Compare(b, a) > 0);
        }

        [Fact]
        public void TieBreakComparer_DifferentSortY_OrdersBySortYRegardlessOfId()
        {
            var lower = (Id: new Id("unit.z"), SortY: 1.0);
            var higher = (Id: new Id("unit.a"), SortY: 2.0);

            Assert.True(_host.TieBreakComparer.Compare(lower, higher) < 0);
        }

        [Fact]
        public void ResolveDirectionSlot_CanonicalSlotWithNoDefaultMirror_ReturnsCanonicalSlot_NoFlip()
        {
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // 90 度 -> index 2 -> "dir.front"（面朝观察者，无镜像）

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.False(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_LSlot_NoMirrorPairRegistered_FallsBackToDefault14MirrorTable()
        {
            var sprite = new SpriteInfo("sprite.x", 8); // 未登记 mirror_pairs
            var direction = Direction.FromQuantized(0.0, 8); // index 0 -> "dir.side_l"（14 默认镜像自 side_r）

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.SideR, slotId);
            Assert.True(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_ExplicitMirrorPair_OverridesDefault14MirrorTable()
        {
            // 内容侧显式登记的 mirror_pairs 优先于 14 的默认镜像表回退（09 第 3.2 节镜像规则表）。
            var mirrorPairs = new[] { new MirrorPair(DirectionSlots.SideL, DirectionSlots.Front, true) };
            var sprite = new SpriteInfo("sprite.x", 8, mirrorPairs);
            var direction = Direction.FromQuantized(0.0, 8); // index 0 -> "dir.side_l"

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.True(flipX);
        }

        [Fact]
        public void ComposeSpriteLayers_PreservesOrder_AndAppliesSameSlotToAllLayers()
        {
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // index 2 -> "dir.front"
            var layers = new List<string> { "body", "chest_armor", "weapon" };

            var placements = _host.ComposeSpriteLayers(layers, sprite, direction);

            Assert.Equal(3, placements.Count);
            Assert.Equal("body", placements[0].LayerName);
            Assert.Equal("chest_armor", placements[1].LayerName);
            Assert.Equal("weapon", placements[2].LayerName);
            Assert.All(placements, p => Assert.Equal(DirectionSlots.Front, p.DirectionSlotId));
        }

        [Fact]
        public void HeightOffsetToPixels_MultipliesByPixelsPerUnit()
        {
            Assert.Equal(64.0, _host.HeightOffsetToPixels(2.0, 32.0));
        }

        [Fact]
        public void ShadowSpec_ResolveAnchor_ReturnsLogicalPositionUnchanged()
        {
            var pos = new Vec2(3, 4);

            Assert.Equal(pos, ShadowSpec.ResolveAnchor(pos));
        }

        [Theory]
        [InlineData(ShadowMode.None, Core.Foundation.EngineAdapter.ShadowMode.None)]
        [InlineData(ShadowMode.Blob, Core.Foundation.EngineAdapter.ShadowMode.Blob)]
        [InlineData(ShadowMode.Projected, Core.Foundation.EngineAdapter.ShadowMode.Projected)]
        public void ShadowSpec_ToEngineShadowMode_MapsEachValue(ShadowMode dataMode, Core.Foundation.EngineAdapter.ShadowMode expected)
        {
            Assert.Equal(expected, ShadowSpec.ToEngineShadowMode(dataMode));
        }

        // -----------------------------------------------------------------
        // 缺口 8（方向索引重映射策略）。
        // -----------------------------------------------------------------

        [Fact]
        public void ResolveDirectionSlot_WithDirectionIndexRemap_UsesRemappedIndex()
        {
            // 恒等映射下 index 2 -> "dir.front"；重映射把 2 指向 4（"dir.side_r"），验证 ResolveDirectionSlot
            // 换算规范档位前先经重映射表这一步真的生效。
            var remap = new[] { 0, 1, 4, 3, 2, 5, 6, 7 };
            var host = new RenderConventionHost(new RenderOptions { DirectionIndexRemap = remap });
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // index 2

            var (slotId, flipX) = host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.SideR, slotId);
            Assert.False(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_DirectionIndexRemap_LengthMismatch_FallsBackToIdentity()
        {
            // 重映射表长度（4）与当前方向档位数（8）不一致时按恒等映射处理，不抛异常。
            var remap = new[] { 0, 1, 2, 3 };
            var host = new RenderConventionHost(new RenderOptions { DirectionIndexRemap = remap });
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // index 2 -> "dir.front"

            var (slotId, flipX) = host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.False(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_NoDirectionIndexRemap_DefaultsToIdentity()
        {
            var host = new RenderConventionHost();
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8);

            var (slotId, flipX) = host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.False(flipX);
        }

        // -----------------------------------------------------------------
        // ADR-0094：朝向约定口味项与档位数无关。
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyFacingConvention_DefaultOptions_ReturnsRawRadiansUnchanged()
        {
            var host = new RenderConventionHost();

            Assert.Equal(1.2345, host.ApplyFacingConvention(1.2345));
        }

        [Fact]
        public void ApplyFacingConvention_MirrorThenOffset_AppliesInThatOrder()
        {
            // 决策 1："先镜像（角度取负）、再加偏移"——镜像 2.0 得 -2.0，再加偏移 1.0 得 -1.0，
            // 不是先加偏移再镜像（那样会得到 -3.0）。
            var host = new RenderConventionHost(new RenderOptions { MirrorFacingY = true, FacingAngleOffsetRadians = 1.0 });

            Assert.Equal(-1.0, host.ApplyFacingConvention(2.0), 10);
        }

        [Fact]
        public void ApplyFacingConvention_PiOffset_FixesBothFourAndEightDirectionEntities_UnlikeLengthLockedRemap()
        {
            // 消费方第三十九批阻塞项事实：引擎适配层把逻辑 (x,y) 原样写成引擎 (x,y)（+Y=屏幕上方），与
            // DirectionSlots 假定的"+Y 朝观察者"（05 第 3.1 节）差 180°。消费方此前用
            // DirectionIndexRemap=[2,3,0,1]（长度 4）修好了 4 向单位——第一段断言复现这一点仍然成立；
            // 但该表长度锁定在 4，配到 8 向精灵集时因长度不匹配（4 != 8）静默退恒等，8 向单位仍然朝向
            // 错误（第二段断言，ADR-0094 之前唯一能做的事）。第三/四段断言：改用与档位数无关的角度
            // 口味项 FacingAngleOffsetRadians = π，同一份配置对 4 向、8 向同时正确生效——ADR-0094 的
            // ApplyFacingConvention 在此之前不存在，是本次新增的能力。
            var rawFacing = Math.PI / 2; // 90 度：4/8 向量化表 index 均落在 "dir.front"。
            var sprite8 = new SpriteInfo("sprite.x", 8);
            var sprite4 = new SpriteInfo("sprite.x", 4);

            var hostWithLengthLockedRemap = new RenderConventionHost(new RenderOptions { DirectionIndexRemap = new[] { 2, 3, 0, 1 } });

            var (slot4WithRemap, _) = hostWithLengthLockedRemap.ResolveDirectionSlot(Direction.FromQuantized(rawFacing, 4), sprite4);
            Assert.Equal(DirectionSlots.Back, slot4WithRemap);

            var (slot8WithRemap, _) = hostWithLengthLockedRemap.ResolveDirectionSlot(Direction.FromQuantized(rawFacing, 8), sprite8);
            Assert.Equal(DirectionSlots.Front, slot8WithRemap); // 消费方阻塞项：8 向单位仍然是错的。

            var hostWithOffset = new RenderConventionHost(new RenderOptions { FacingAngleOffsetRadians = Math.PI });

            var transformed = hostWithOffset.ApplyFacingConvention(rawFacing);
            var (slot4WithOffset, _) = hostWithOffset.ResolveDirectionSlot(Direction.FromQuantized(transformed, 4), sprite4);
            Assert.Equal(DirectionSlots.Back, slot4WithOffset);

            var (slot8WithOffset, _) = hostWithOffset.ResolveDirectionSlot(Direction.FromQuantized(transformed, 8), sprite8);
            Assert.Equal(DirectionSlots.Back, slot8WithOffset);
        }

        [Fact]
        public void ApplyFacingConvention_DefaultOptions_MatchesLegacyResolutionAcrossAllBucketsFourEightSixteenDirections()
        {
            // 不变量：默认口味项（MirrorFacingY=false, FacingAngleOffsetRadians=0）下，经
            // ApplyFacingConvention 转换再量化 与 直接用原始角度量化（旧路径）的 ResolveDirectionSlot
            // 结果必须逐一相同——覆盖 4/8/16 向全部桶，证明新增的转换步骤不改变默认行为。
            var host = new RenderConventionHost();
            foreach (var directionCount in new[] { 4, 8, 16 })
            {
                var sprite = new SpriteInfo("sprite.x", directionCount);
                for (var index = 0; index < directionCount; index++)
                {
                    var rawAngle = index * (2 * Math.PI / directionCount);
                    var legacyDirection = Direction.FromQuantized(rawAngle, directionCount);
                    var newDirection = Direction.FromQuantized(host.ApplyFacingConvention(rawAngle), directionCount);

                    Assert.Equal(host.ResolveDirectionSlot(legacyDirection, sprite), host.ResolveDirectionSlot(newDirection, sprite));
                }
            }
        }

        [Fact]
        public void ResolveDirectionSlot_DirectionIndexRemap_LengthMismatch_WarnsExactlyOnceAcrossRepeatedCalls()
        {
            // 决策 3：长度不匹配按 (方向档位数, 表长度) 去重只记一次 Warn，即便同一实体逐帧 SyncPose
            // 反复触发同一组合的解析。未注入诊断出口的既有用例（本文件其余用例、既有生产调用点）不受
            // 影响——仍然静默，见 ResolveDirectionSlot_DirectionIndexRemap_LengthMismatch_FallsBackToIdentity。
            var remap = new[] { 0, 1, 2, 3 }; // 长度 4，档位数 8，不匹配。
            var diagnostics = new PresentationDiagnosticsRecorder();
            var host = new RenderConventionHost(new RenderOptions { DirectionIndexRemap = remap }, diagnostics);
            var sprite = new SpriteInfo("sprite.x", 8);

            for (var i = 0; i < 5; i++)
            {
                host.ResolveDirectionSlot(Direction.FromQuantized(Math.PI / 2, 8), sprite);
            }

            Assert.Single(diagnostics.Warnings);
        }
    }
}

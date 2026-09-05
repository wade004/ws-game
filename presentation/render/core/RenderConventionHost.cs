using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="IRenderConventionHost"/> 的默认实现（见接口注释、09 第 3.1～3.4 节）。除构造期注入的
    /// 只读 <see cref="RenderOptions"/>（缺口 8：<see cref="RenderOptions.DirectionIndexRemap"/>）外无
    /// 其它内部状态，全部方法都是纯函数，可安全作为单例复用。
    /// </summary>
    public sealed class RenderConventionHost : IRenderConventionHost
    {
        private readonly RenderOptions _options;

        public RenderConventionHost(RenderOptions? options = null)
        {
            _options = options ?? new RenderOptions();
        }

        public double ComputeSortY(Vec2 logicalPos, double sortOffset) => logicalPos.Y + sortOffset;

        public IComparer<(Id Id, double SortY)> TieBreakComparer { get; } = new SortYThenIdComparer();

        /// <summary>
        /// 判断记录（P4-2 恢复，见模块 README"索引→档位对应表"一节、
        /// <see cref="Presentation.Common.DirectionSlots"/> 类型注释）：量化索引先经
        /// <see cref="DirectionSlots.FromQuantized"/> 换算成 14 第 2.1 节命名的规范档位 id
        /// （<c>front</c>/<c>front_side_r</c>/…）；先查 <c>display.map.mirror_pairs</c>（内容侧显式
        /// 登记优先，见 09 第 3.2 节镜像规则表），没有登记时再按
        /// <see cref="DirectionSlots.MirrorSourceOf"/> 给出的 14 默认镜像表回退（<c>_l</c> 档位镜像
        /// 自同族 <c>_r</c> 档位）；两处都没有命中（<c>front</c>/<c>back</c>/<c>_r</c> 档位本身）时
        /// 直接使用规范档位、不翻转。
        /// <para>
        /// 缺口 8：换算规范档位之前先经 <see cref="RenderOptions.DirectionIndexRemap"/> 重映射一次量化
        /// 索引（长度须等于 <paramref name="direction"/>.<see cref="Direction.DirectionCount"/>，否则
        /// 忽略、按恒等映射处理，见该字段注释）——供镜头朝向与 05 第 3.1 节默认约定不一致的具体游戏在
        /// 接入阶段配置，不改 <see cref="DirectionSlots"/>/<c>DirectionQuantizer</c> 这两处固定原语。
        /// </para>
        /// </summary>
        public (Id SlotId, bool FlipX) ResolveDirectionSlot(Direction direction, SpriteInfo spriteInfo)
        {
            if (spriteInfo == null)
            {
                throw new System.ArgumentNullException(nameof(spriteInfo));
            }

            var remap = _options.DirectionIndexRemap;
            var index = remap != null && remap.Count == direction.DirectionCount ? remap[direction.Index] : direction.Index;
            var canonical = DirectionSlots.FromQuantized(index, direction.DirectionCount);

            for (var i = 0; i < spriteInfo.MirrorPairs.Count; i++)
            {
                var pair = spriteInfo.MirrorPairs[i];
                if (pair.DirectionSlot.Equals(canonical))
                {
                    return (pair.MirrorOf, pair.FlipX);
                }
            }

            var defaultMirror = DirectionSlots.MirrorSourceOf(canonical);
            if (defaultMirror.HasValue)
            {
                return (defaultMirror.Value.MirrorOf, defaultMirror.Value.FlipX);
            }

            return (canonical, false);
        }

        public IReadOnlyList<SpriteLayerPlacement> ComposeSpriteLayers(
            IReadOnlyList<string> layerNamesInOrder, SpriteInfo spriteInfo, Direction direction)
        {
            if (layerNamesInOrder == null)
            {
                throw new System.ArgumentNullException(nameof(layerNamesInOrder));
            }

            var (slotId, flipX) = ResolveDirectionSlot(direction, spriteInfo);

            var result = new List<SpriteLayerPlacement>(layerNamesInOrder.Count);
            for (var i = 0; i < layerNamesInOrder.Count; i++)
            {
                result.Add(new SpriteLayerPlacement(layerNamesInOrder[i], slotId, flipX));
            }

            return result;
        }

        public double HeightOffsetToPixels(double height, double pixelsPerUnit) => height * pixelsPerUnit;

        private sealed class SortYThenIdComparer : IComparer<(Id Id, double SortY)>
        {
            public int Compare((Id Id, double SortY) x, (Id Id, double SortY) y)
            {
                var cmp = x.SortY.CompareTo(y.SortY);
                return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
            }
        }
    }
}

using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="IRenderConventionHost"/> 的默认实现（见接口注释、09 第 3.1～3.4 节）。无内部状态，
    /// 全部方法都是纯函数，可安全作为单例复用。
    /// </summary>
    public sealed class RenderConventionHost : IRenderConventionHost
    {
        /// <summary>4 档方向槽位命名（东/北/西/南，逆时针，见类型注释判断记录）。</summary>
        private static readonly string[] Compass4 = { "e", "n", "w", "s" };

        /// <summary>8 档方向槽位命名（逆时针，index 0 = 角度 0 = +X 轴，与
        /// <c>Core.Carriers.Unit.DirectionQuantizer</c> 的量化桶编号约定一致）。</summary>
        private static readonly string[] Compass8 = { "e", "ne", "n", "nw", "w", "sw", "s", "se" };

        public double ComputeSortY(Vec2 logicalPos, double sortOffset) => logicalPos.Y + sortOffset;

        public IComparer<(Id Id, double SortY)> TieBreakComparer { get; } = new SortYThenIdComparer();

        public (Id SlotId, bool FlipX) ResolveDirectionSlot(Direction direction, SpriteInfo spriteInfo)
        {
            if (spriteInfo == null)
            {
                throw new System.ArgumentNullException(nameof(spriteInfo));
            }

            var canonical = CanonicalDirectionSlotId(direction.Index, direction.DirectionCount);

            for (var i = 0; i < spriteInfo.MirrorPairs.Count; i++)
            {
                var pair = spriteInfo.MirrorPairs[i];
                if (pair.DirectionSlot.Equals(canonical))
                {
                    return (pair.MirrorOf, pair.FlipX);
                }
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

        /// <summary>
        /// 量化方向索引 → 规范方向槽位 id 的命名约定。
        /// <para>
        /// 判断记录（契约缺口，见模块 README）：09 第 3.2 节只给出方向槽位 id 的"举例"
        /// （<c>front</c>/<c>front_side</c>/<c>side</c>/<c>back_side</c>/<c>back</c>），04 的
        /// <c>display.map.mirror_pairs</c> 测试夹具用的是 <c>dir.se</c>/<c>dir.sw</c> 这类罗盘缩写，
        /// 两处均未规定"量化索引 0 对应哪个具体方向"这一约定——这天然是"逻辑坐标系的 +X/+Y 朝向
        /// 与游戏镜头朝向如何对应"的游戏层/口味问题，架构文档不可能在不知道具体游戏镜头摆放方式的
        /// 前提下预先拍板。本方法选择一个确定、可测试、与
        /// <c>Core.Carriers.Unit.DirectionQuantizer</c>（index 0 = 角度 0 = +X 轴，按角度递增方向即
        /// 逆时针编号）直接对应的罗盘命名：4 档 <c>dir.e/n/w/s</c>，8 档
        /// <c>dir.e/ne/n/nw/w/sw/s/se</c>，16 档退化为 <c>dir.slot_&lt;index&gt;</c>（16 档没有
        /// 简洁通用的英文罗盘缩写，不强行发明）。这只是一个可随时替换的默认命名约定，集中在本方法
        /// 一处——具体游戏的镜头朝向如果与"角度 0 = 东"不一致，只需要在游戏层提供一个方向索引重映射
        /// （不改动本模块任何签名），或者游戏内容作者按本约定反向命名 <c>mirror_pairs</c> 的
        /// <c>direction_slot</c> 取值。
        /// </para>
        /// </summary>
        public static Id CanonicalDirectionSlotId(int index, int directionCount)
        {
            string label;
            if (directionCount == 4 && index >= 0 && index < 4)
            {
                label = Compass4[index];
            }
            else if (directionCount == 8 && index >= 0 && index < 8)
            {
                label = Compass8[index];
            }
            else
            {
                label = $"slot_{index}";
            }

            return new Id($"dir.{label}");
        }

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

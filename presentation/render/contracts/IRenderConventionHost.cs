using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 混合渲染约定的落地（见 01 L5 模块表 <c>render</c> 行"契约接口名：RenderConventionHost"、
    /// 09 第 3.1～3.4 节）：统一 <c>sortY</c> 排序、方向量化档位到方向槽位的镜像回退解析、纸娃娃层
    /// 合成顺序、影子锚点、高度像素换算。
    /// </summary>
    public interface IRenderConventionHost
    {
        /// <summary>统一排序键 = 逻辑位置的纵轴分量 + 排序偏移（<c>display.map.sort_offset</c>，
        /// 见 09 第 3.1 节）。</summary>
        double ComputeSortY(Vec2 logicalPos, double sortOffset);

        /// <summary>排序键相同时的平局比较器：按对象稳定 id 排序，避免同帧抖动闪烁
        /// （见 09 第 3.1 节"排序键相同时的平局规则（建议）"）。</summary>
        IComparer<(Id Id, double SortY)> TieBreakComparer { get; }

        /// <summary>把量化后的 <see cref="Direction"/> 解析为具体应绘制的方向槽位 id 与是否翻转：
        /// 若该方向槽位在 <paramref name="spriteInfo"/> 的 <c>MirrorPairs</c> 中被声明为"由另一槽位
        /// 镜像得到"，返回 <c>(MirrorOf, FlipX)</c>；否则该槽位有自己的美术，返回
        /// <c>(该槽位本身, false)</c>（见 09 第 3.2 节镜像规则表）。</summary>
        (Id SlotId, bool FlipX) ResolveDirectionSlot(Direction direction, SpriteInfo spriteInfo);

        /// <summary>给定当前应绘制的纸娃娃层名顺序（z 序 = 列表顺序，见 09 第 3.3.1 节）与朝向，
        /// 产出每层对应的方向槽位解析结果（见 <see cref="SpriteLayerPlacement"/> 类型注释——具体
        /// 引擎可消费的资源 Id 拼接不在本方法职责内，属契约缺口）。</summary>
        IReadOnlyList<SpriteLayerPlacement> ComposeSpriteLayers(
            IReadOnlyList<string> layerNamesInOrder, SpriteInfo spriteInfo, Direction direction);

        /// <summary>高度偏移换算为像素纵向偏移（见 09 第 3.4 节）。</summary>
        double HeightOffsetToPixels(double height, double pixelsPerUnit);
    }
}

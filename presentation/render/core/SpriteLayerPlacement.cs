using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="IRenderConventionHost.ComposeSpriteLayers"/> 产出的单条纸娃娃层放置信息（见 09
    /// 第 3.3.1 节"层内 z 序 = 列表顺序"）。
    /// <para>
    /// 判断记录（契约缺口，见模块 README）：<c>DisplayInfo.Sprite.PaperdollLayers</c> 的类型是
    /// <c>IReadOnlyList&lt;string&gt;</c>（纸娃娃分层引用名，如 <c>"body"</c>/<c>"chest_armor"</c>），
    /// 而 <c>IRenderer2D.SetLayers</c> 需要的是 <c>IReadOnlyList&lt;Id&gt;</c>；两者之间"层名 +
    /// 已解析方向档位 → 引擎可消费的具体资源 Id"的组装规则，架构文档未给出（04/09 均未规定
    /// 拼接格式）。本类型只把"排序 + 方向镜像回退"这一段本模块能确定的逻辑产出为结构化结果
    /// （层名 + 已解析方向槽位 id + 是否翻转），具体资源 Id 的拼接留给消费方（<see cref="SpriteViewBase"/>
    /// 或具体游戏的引擎适配实现）决定，避免本模块替架构文档"发明"一个未拍板的资源命名格式。
    /// </para>
    /// </summary>
    public readonly struct SpriteLayerPlacement
    {
        /// <summary>纸娃娃层名（来自 <c>DisplayInfo.Sprite.PaperdollLayers</c> 或装备项自身的同名
        /// 字段），z 序即调用方传入列表的顺序。</summary>
        public string LayerName { get; }

        /// <summary>本层在当前朝向下应使用的方向槽位 id（已按 <c>mirror_pairs</c> 做过镜像回退）。</summary>
        public Id DirectionSlotId { get; }

        /// <summary>该槽位是否需要水平翻转（镜像回退的产物）。</summary>
        public bool FlipX { get; }

        public SpriteLayerPlacement(string layerName, Id directionSlotId, bool flipX)
        {
            LayerName = layerName;
            DirectionSlotId = directionSlotId;
            FlipX = flipX;
        }
    }
}

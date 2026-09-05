using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="IRenderConventionHost.ComposeSpriteLayers"/> 产出的单条纸娃娃层放置信息（见 09
    /// 第 3.3.1 节"层内 z 序 = 列表顺序"）。
    /// <para>
    /// 判断记录（P4-2 补充）：<c>DisplayInfo.Sprite.PaperdollLayers</c> 的类型是
    /// <c>IReadOnlyList&lt;string&gt;</c>（纸娃娃分层引用名，如 <c>"body"</c>/<c>"chest_armor"</c>），
    /// 而 <c>IRenderer2D.SetLayers</c> 需要的是 <c>IReadOnlyList&lt;Id&gt;</c>；本类型只把"排序 +
    /// 方向镜像回退"这一段本模块能确定的逻辑产出为结构化结果（层名 + 已解析方向槽位 id + 是否
    /// 翻转），具体资源 Id 的拼接交给消费方 <see cref="SpriteViewBase.ResolveLayerResourceId"/>
    /// 决定——该方法现已按 14_资产规格书模板.md 第 1.2 节命名模板实现默认拼接规则（见其类型注释判断
    /// 记录），不再是未拍板的占位；本类型继续保持"不直接产出资源 Id"，因为 14 §1.2 的命名模板需要
    /// <c>DisplayInfo.Sprite.SpriteSetId</c>（本类型不持有），拼接职责天然属于持有完整 DisplayInfo
    /// 的 <see cref="SpriteViewBase"/>。
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

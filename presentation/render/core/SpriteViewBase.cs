using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Presentation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 引擎无关的 <c>sprite</c> 型 <see cref="IView"/> 骨架（见 09 第 4.1 节 CharacterRig 职责表
    /// "sprite 型持有当前应绘制的纸娃娃层集合……随装备/外形变化事件更新"）：持有
    /// <see cref="SpriteHandle"/>（经 <see cref="IRenderer2D.CreateSpriteInstance"/>）、
    /// <see cref="SyncPose"/> 驱动 <see cref="IRenderer2D.SetTransform"/>、<see cref="Destroy"/>
    /// 驱动 <see cref="IRenderer2D.DestroySpriteInstance"/>。
    /// <para>
    /// 判断记录：<see cref="OnEvent"/> 按任务书拍板"留虚方法给反馈层"——本类型默认空实现，不在
    /// P4-1 范围内推测"收到 <c>item.equipped</c> 应该怎么改纸娃娃层"这类需要额外查"已装备物品的
    /// DisplayInfo"（L3 <c>item</c> 模块尚未提供的查询能力）的策略；本类型只提供
    /// <see cref="SetPaperdollLayers"/> 这个底层原语（排序 + 方向镜像回退 + 资源 Id 解析 +
    /// <c>IRenderer2D.SetLayers</c>），具体"收到什么事件时该传什么层名列表"由具体游戏的 View 子类
    /// （重写 <see cref="OnEvent"/>）或后续 <c>presentation/feedback_binder</c> 决定。
    /// </para>
    /// <para>
    /// 契约缺口——高度偏移无专用参数（见 <c>presentation/common/README.md</c>"契约缺口"一节）：
    /// <see cref="IRenderer2D.SetTransform"/> 没有高度参数（不同于 <c>IRenderer3D.SetPlacement</c>
    /// 显式携带 <c>height</c>），本类型把换算出的像素高度经既有的
    /// <see cref="IRenderer2D.SetShaderParam"/> 通道（参数名 <see cref="HeightOffsetShaderParam"/>）
    /// 传给引擎侧实现，作为一个可随时替换的工作绕；建议 02 文档评估是否给
    /// <see cref="IRenderer2D.SetTransform"/> 补一个高度/像素纵向偏移参数。
    /// </para>
    /// <para>
    /// 契约缺口——<c>sprite_set_id</c>/纸娃娃层名的字符串到 <see cref="Id"/> 转换：
    /// <see cref="SpriteInfo.SpriteSetId"/> 类型是 <see cref="string"/>，<see cref="Core.Foundation.EngineAdapter.IRenderer2D.CreateSpriteInstance"/>
    /// 需要 <see cref="Id"/>；本类型假定内容已按现有测试夹具的惯例把它写成合法的 <c>Id</c> 格式
    /// （如 <c>"sprite.creature.wolf_grey"</c>），用 <see cref="Id.Parse"/> 直接转换，格式不合法时
    /// 在构造期抛出清晰的 <see cref="ArgumentException"/> 而不是静默失败；纸娃娃层资源 Id 的解析见
    /// <see cref="ResolveLayerResourceId"/> 的判断记录。
    /// </para>
    /// </summary>
    public abstract class SpriteViewBase : IView
    {
        /// <summary>高度偏移换算出的像素值经 <see cref="IRenderer2D.SetShaderParam"/> 传递时使用的
        /// 参数名（见类型注释"契约缺口"）。</summary>
        public const string HeightOffsetShaderParam = "height_offset_px";

        protected IRenderer2D Renderer { get; }

        protected IRenderConventionHost Conventions { get; }

        protected DisplayInfo DisplayInfo { get; }

        protected RenderOptions Options { get; }

        protected SpriteHandle Handle { get; }

        public Id EntityId { get; private set; }

        public bool IsAlive { get; private set; }

        private bool _destroyed;

        protected SpriteViewBase(
            IRenderer2D renderer,
            IRenderConventionHost conventions,
            DisplayInfo displayInfo,
            RenderOptions? options = null)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
            DisplayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));

            if (DisplayInfo.Kind != DisplayKind.Sprite || DisplayInfo.Sprite == null)
            {
                throw new ArgumentException(
                    $"SpriteViewBase 只支持 kind=sprite 的 DisplayInfo，实际 kind={DisplayInfo.Kind}",
                    nameof(displayInfo));
            }

            Options = options ?? new RenderOptions();

            var spriteSetId = Id.Parse(DisplayInfo.Sprite.SpriteSetId);
            Handle = Renderer.CreateSpriteInstance(spriteSetId);
        }

        public virtual void Bind(Id entityId)
        {
            EntityId = entityId;
            IsAlive = true;
        }

        /// <summary>默认空实现，留给具体游戏的 View 子类或后续反馈层重写（见类型注释判断记录）。</summary>
        public virtual void OnEvent(IEvent evt)
        {
        }

        public virtual void SyncPose(Vec2 pos, Direction facing, double height)
        {
            EnsureAlive();

            var sortY = Conventions.ComputeSortY(pos, DisplayInfo.SortOffset);
            var (_, flipX) = Conventions.ResolveDirectionSlot(facing, DisplayInfo.Sprite!);

            Renderer.SetTransform(Handle, pos, sortY, RenderLayers.Units, 0.0, DisplayInfo.Scale, flipX);

            var heightPixels = Conventions.HeightOffsetToPixels(height, Options.PixelsPerUnit);
            Renderer.SetShaderParam(Handle, HeightOffsetShaderParam, heightPixels);
        }

        /// <summary>用给定层名顺序与当前朝向重新合成纸娃娃层并应用到 <see cref="IRenderer2D.SetLayers"/>
        /// （见 09 第 3.3.1 节"层内 z 序 = 列表顺序"）。由具体游戏的 <see cref="OnEvent"/> 重写在收到
        /// <c>item.equipped</c>/<c>item.unequipped</c> 等装备变化事件时调用。</summary>
        protected void SetPaperdollLayers(IReadOnlyList<string> layerNamesInOrder, Direction currentFacing)
        {
            EnsureAlive();

            var placements = Conventions.ComposeSpriteLayers(layerNamesInOrder, DisplayInfo.Sprite!, currentFacing);
            var resourceIds = new List<Id>(placements.Count);
            for (var i = 0; i < placements.Count; i++)
            {
                resourceIds.Add(ResolveLayerResourceId(placements[i]));
            }

            Renderer.SetLayers(Handle, resourceIds);
        }

        /// <summary>把一条已解析方向槽位的纸娃娃层放置信息转换成 <see cref="IRenderer2D.SetLayers"/>
        /// 可消费的资源 <see cref="Id"/>（见 <see cref="SpriteLayerPlacement"/> 类型注释的契约缺口）。
        /// 默认约定：<c>"layer.&lt;layerName&gt;"</c>——只是一个可随时被具体游戏覆盖的占位命名，
        /// 不代表架构拍板的资源命名格式。</summary>
        protected virtual Id ResolveLayerResourceId(SpriteLayerPlacement placement) =>
            new Id($"layer.{placement.LayerName}");

        public virtual void Destroy()
        {
            if (_destroyed)
            {
                return;
            }

            Renderer.DestroySpriteInstance(Handle);
            _destroyed = true;
            IsAlive = false;
        }

        private void EnsureAlive()
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException("View 尚未 Bind 或已 Destroy，不能调用本方法");
            }
        }
    }
}

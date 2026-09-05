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
        protected IRenderer2D Renderer { get; }

        protected IRenderConventionHost Conventions { get; }

        protected DisplayInfo DisplayInfo { get; }

        protected RenderOptions Options { get; }

        protected SpriteHandle Handle { get; }

        public Id EntityId { get; private set; }

        public bool IsAlive { get; private set; }

        private bool _destroyed;

        private readonly Presentation.Common.ResourceReferenceTracker? _resourceTracker;

        /// <summary><paramref name="resourceLoader"/> 可选（同 <c>VfxPlayer</c>/<c>SfxPlayer</c>
        /// 判断记录）：注入时构造期对 <c>sprite_set_id</c>、<see cref="SetPaperdollLayers"/> 期间对
        /// 每个新解析出的纸娃娃层资源 id，均以 <see cref="ResourceKind.Image"/> 触发一次
        /// <see cref="IResourceLoader.LoadAsync"/>（ADR-0016 决策 6："谁首次引用谁加载"）。</summary>
        protected SpriteViewBase(
            IRenderer2D renderer,
            IRenderConventionHost conventions,
            DisplayInfo displayInfo,
            RenderOptions? options = null,
            IResourceLoader? resourceLoader = null)
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
            _resourceTracker = resourceLoader != null ? new Presentation.Common.ResourceReferenceTracker(resourceLoader) : null;

            var spriteSetId = Id.Parse(DisplayInfo.Sprite.SpriteSetId);
            _resourceTracker?.EnsureLoading(spriteSetId, ResourceKind.Image);
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

            var heightPixels = Conventions.HeightOffsetToPixels(height, Options.PixelsPerUnit);
            Renderer.SetTransform(Handle, pos, heightPixels, sortY, RenderLayers.Units, 0.0, DisplayInfo.Scale, flipX);
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
                var resourceId = ResolveLayerResourceId(placements[i]);
                _resourceTracker?.EnsureLoading(resourceId, ResourceKind.Image);
                resourceIds.Add(resourceId);
            }

            Renderer.SetLayers(Handle, resourceIds);
        }

        /// <summary>
        /// 把一条已解析方向槽位的纸娃娃层放置信息转换成 <see cref="IRenderer2D.SetLayers"/> 可消费的
        /// 资源 <see cref="Id"/>（见 <see cref="SpriteLayerPlacement"/> 类型注释的契约缺口）。
        /// <para>
        /// 判断记录（P4-2 恢复，改用 14_资产规格书模板.md 第 1.2 节命名模板，取代 P4-1 的占位
        /// <c>"layer.&lt;layerName&gt;"</c>）：14 第 1.2 节给出的是"文件名"模板
        /// <c>&lt;资源引用id去掉类别前缀，点号换下划线&gt;[__&lt;方向档位id&gt;][__&lt;层id&gt;]…</c>
        /// （原文示例 <c>sprite_set_id: sprite.creature.wolf_grey</c> 在 <c>front</c> 档位、<c>body</c>
        /// 层下的文件名为 <c>creature_wolf_grey__front__body</c>），不是运行期资源 <see cref="Id"/>
        /// 的格式规定；但 04/09/14 均未给出"层名 + 已解析方向档位 → 引擎可消费的资源 Id"的独立映射
        /// 规则（<see cref="SpriteLayerPlacement"/> 类型注释同一契约缺口），本方法选择让资源 Id 的
        /// "名字"部分直接复用 14 §1.2 的文件名模板（同一套命名，减少"文件在磁盘上叫什么"与"运行期
        /// 用什么 Id 去引用它"两处各自发明命名的必要），只在外面包一层 <c>"layer."</c>
        /// 域前缀满足 <see cref="Id"/> 的格式要求（同 <see cref="Presentation.Common.DirectionSlots"/>
        /// 类型注释"Id 前缀判断记录"的同一原因）。方向档位段经
        /// <see cref="Presentation.Common.DirectionSlots.StripPrefix"/> 还原成 14 的裸档位名（不带
        /// 本模块内部使用的 <c>"dir."</c> 前缀），<c>flipX</c> 不编码进资源 Id——它是整个精灵实例的
        /// 水平翻转（经 <see cref="SyncPose"/> 传给 <see cref="IRenderer2D.SetTransform"/>），不是
        /// "换一张镜像图"，同一份 <c>_r</c> 原图配合 <c>flipX</c> 足够表达镜像档位，不需要为 <c>_l</c>
        /// 档位单独生成资源 Id（<see cref="SpriteLayerPlacement.DirectionSlotId"/> 本就已经是
        /// <see cref="IRenderConventionHost.ResolveDirectionSlot"/> 镜像回退后的 <c>_r</c> 档位，见
        /// 该接口方法签名注释）。<c>protected virtual</c>，具体游戏按自己的资源命名规则覆盖。
        /// </para>
        /// </summary>
        protected virtual Id ResolveLayerResourceId(SpriteLayerPlacement placement)
        {
            var spriteSetName = StripCategoryPrefix(DisplayInfo.Sprite!.SpriteSetId);
            var directionSlotName = Presentation.Common.DirectionSlots.StripPrefix(placement.DirectionSlotId);
            return new Id($"layer.{spriteSetName}__{directionSlotName}__{placement.LayerName}");
        }

        /// <summary>去掉资源引用 id 的"类别前缀"（第一个点分段，如 <c>sprite.creature.wolf_grey</c>
        /// 的 <c>sprite</c>），剩余部分把点号换成下划线（见 14 第 1.2 节命名模板
        /// "&lt;资源引用id去掉类别前缀，点号换下划线&gt;"）。</summary>
        private static string StripCategoryPrefix(string resourceRefId)
        {
            var dotIndex = resourceRefId.IndexOf('.');
            var withoutCategory = dotIndex < 0 ? resourceRefId : resourceRefId.Substring(dotIndex + 1);
            return withoutCategory.Replace('.', '_');
        }

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

using System;
using System.Collections.Generic;
using Core.Carriers.Common;
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
    /// 判断记录（缺口 10 恢复，取代 P4-1"留虚方法给反馈层"的搁置）：<see cref="OnEvent"/> 现默认处理
    /// <c>item.equipped</c>/<c>item.unequipped</c>（见 <c>found.event_catalog</c> 对应行、07 第 1.4
    /// 节）——按 <c>display.equip_visual</c>（04 第 7.1.2 节）重新解析该槽位的纸娃娃层资源并调用
    /// <see cref="IRenderer2D.SetLayers"/>；未在构造期注入 <c>equipVisualByItemInstanceId</c>，或该表
    /// 查不到对应行时保持不变（不抛异常，不改动当前层集合）。字段解析/查找规则、"槽位→纸娃娃层名"
    /// 换算判断记录见 <see cref="OnEvent"/>/<see cref="EquipVisualDef"/> 类型注释。具体游戏的 View 子类
    /// 仍可重写 <see cref="OnEvent"/> 替换/扩展本默认行为（如接入真正的"物品实例 id → item.template"
    /// 只读查询，见 <see cref="OnEvent"/> 判断记录）。
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
    public abstract class SpriteViewBase : IView, IHasCharacterRig, IEquipmentVisualResettable
    {
        protected IRenderer2D Renderer { get; }

        protected IRenderConventionHost Conventions { get; }

        protected DisplayInfo DisplayInfo { get; }

        protected RenderOptions Options { get; }

        protected SpriteHandle Handle { get; }

        public Id EntityId { get; private set; }

        public bool IsAlive { get; private set; }

        /// <summary>缺口（09 §4.1 CharacterRig 归并任务）：层/槽位管理、锚点查询、动画状态机驱动、
        /// 程序动画原语四项职责归并到独立的 <see cref="SpriteCharacterRig"/>，本类型持有并委托（见
        /// <see cref="SetPaperdollLayers"/>/<see cref="RebuildEquippedLayers"/> 判断记录、
        /// <see cref="SpriteCharacterRig"/> 类型注释"层管理与 SpriteViewBase 既有逻辑的分工"）。经
        /// <see cref="IHasCharacterRig"/> 能力接口对外暴露，供反馈层/帧驱动代码取用（同
        /// <c>IModelHandleProvider</c> 一贯的能力接口惯例）。</summary>
        public ICharacterRig Rig => _rig;

        private readonly SpriteCharacterRig _rig;

        private bool _destroyed;

        private readonly Presentation.Common.ResourceReferenceTracker? _resourceTracker;

        /// <summary>缺口 10：<c>item.template</c> 物品实例 id → <see cref="EquipVisualDef"/> 的查询表
        /// （见 <see cref="OnEvent"/> 判断记录"为何按物品实例 id 而不是 item_id 索引"）。未注入
        /// （null）时 <see cref="OnEvent"/> 对装备事件保持默认空处理，等价于 P4-1 行为。</summary>
        private readonly IReadOnlyDictionary<Id, EquipVisualDef>? _equipVisuals;

        /// <summary>当前各装备槽位覆盖的纸娃娃层：槽位 id → (层名, 该层资源 Id)。装备/卸下事件增删本表
        /// 后重新调用 <see cref="RebuildEquippedLayers"/> 合成完整层列表。</summary>
        private readonly Dictionary<Id, (string LayerName, Id ResourceId)> _equipOverridesBySlot =
            new Dictionary<Id, (string, Id)>();

        /// <summary><see cref="SyncPose"/> 最近一次收到的朝向，供 <see cref="RebuildEquippedLayers"/>
        /// 重算未被装备覆盖的层的方向档位资源 Id（装备事件与 SyncPose 异步到达，不能假设装备事件自带
        /// 朝向）。构造期以 <see cref="Direction.FromQuantized"/> 0 弧度、<c>DisplayInfo.Sprite.DirectionCount</c>
        /// 档位初始化，首次 SyncPose 前触发的装备事件按该默认朝向合成，不阻断装配。</summary>
        private Direction _lastFacing;

        /// <summary><paramref name="resourceLoader"/> 可选（同 <c>VfxPlayer</c>/<c>SfxPlayer</c>
        /// 判断记录）：注入时构造期对 <c>sprite_set_id</c>、<see cref="SetPaperdollLayers"/> 期间对
        /// 每个新解析出的纸娃娃层资源 id，均以 <see cref="ResourceKind.Image"/> 触发一次
        /// <see cref="IResourceLoader.LoadAsync"/>（ADR-0016 决策 6："谁首次引用谁加载"）。
        /// <paramref name="equipVisualByItemInstanceId"/> 可选（缺口 10，见 <see cref="OnEvent"/>
        /// 判断记录）。
        /// <para>
        /// 判断记录（W3b 判断记录 2 收口，<paramref name="frameAnimPlayer"/>）：构造期已经拿得到具体
        /// <see cref="IFrameAnimPlayer"/> 实现（如引擎无关测试用内存实现）的调用方可以直接经本参数
        /// 注入，随 <see cref="Options"/> 一并转交内部 <see cref="SpriteCharacterRig"/>，使
        /// <see cref="ICharacterRig.PlayClip"/>/命中帧同步不再是结构性 no-op（此前本构造函数固定省略
        /// 这两项去构造 <see cref="SpriteCharacterRig"/>，见该类型历史版本）；构造期还拿不到具体实现
        /// （常见于引擎侧实现是"挂在精灵根节点上的组件"，根节点要等本构造函数跑完才能被定位到，见
        /// <see cref="SpriteCharacterRig.AttachFrameAnimPlayer"/> 判断记录）的调用方改用
        /// <see cref="AttachFrameAnimPlayer"/> 在拿到之后补接线，两条路径共用同一套接线逻辑。</para>
        /// </summary>
        protected SpriteViewBase(
            IRenderer2D renderer,
            IRenderConventionHost conventions,
            DisplayInfo displayInfo,
            RenderOptions? options = null,
            IResourceLoader? resourceLoader = null,
            IReadOnlyDictionary<Id, EquipVisualDef>? equipVisualByItemInstanceId = null,
            IFrameAnimPlayer? frameAnimPlayer = null)
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
            _equipVisuals = equipVisualByItemInstanceId;
            _lastFacing = Direction.FromQuantized(0.0, DisplayInfo.Sprite.DirectionCount);

            var spriteSetId = Id.Parse(DisplayInfo.Sprite.SpriteSetId);
            _resourceTracker?.EnsureLoading(spriteSetId, ResourceKind.Image);
            Handle = Renderer.CreateSpriteInstance(spriteSetId);

            // GP-PRES-05 收口（09 第 3.4 节"每个可见单位默认携带一个地面投影影子"）：此前
            // DisplayInfo.Shadow 只在数据/转换层（ShadowSpec）存在，从未被任何实际 View 消费——
            // 构造时按 DisplayInfo.Shadow 设置一次，后续该值不会随生命周期变化（DisplayInfo 本身
            // 不可变），不需要在 SyncPose 里重复设置。
            Renderer.SetShadow(Handle, ShadowSpec.ToEngineShadowMode(DisplayInfo.Shadow));

            _rig = new SpriteCharacterRig(default, Renderer, Handle, Conventions, DisplayInfo, _resourceTracker, frameAnimPlayer, Options);
        }

        public virtual void Bind(Id entityId)
        {
            EntityId = entityId;
            _rig.Bind(entityId);
            IsAlive = true;
        }

        /// <summary>见构造函数 <c>frameAnimPlayer</c> 参数判断记录：转发到
        /// <see cref="SpriteCharacterRig.AttachFrameAnimPlayer"/>，供构造期还拿不到具体
        /// <see cref="IFrameAnimPlayer"/> 实现的具体游戏/引擎侧 View 子类在拿到之后补接线。</summary>
        public void AttachFrameAnimPlayer(IFrameAnimPlayer player) => _rig.AttachFrameAnimPlayer(player);

        /// <summary>
        /// 缺口 10 默认处理 <c>item.equipped</c>/<c>item.unequipped</c>（见类型注释）；其余事件类型
        /// 空实现，留给具体游戏的 View 子类或后续反馈层重写。
        /// <para>
        /// 判断记录（为何 <see cref="_equipVisuals"/> 按"物品实例 id"索引，而不是 04 第 7.1.2 节
        /// <c>display.equip_visual.item_id</c> 指向的 <c>item.template</c> id）：<see cref="ItemEquippedEvent"/>/
        /// <see cref="ItemUnequippedEvent"/> 只携带 <c>itemInstanceId</c>，"实例 id → 模板 id"是 L3
        /// <c>item</c> 模块的只读查询（<c>IInventoryQuery</c> 一类），本类型属 L5 <c>render</c>，构造期
        /// 只接受表现层/数据层查询表，不持有任何 L3 宿主引用（09 第 1 节铁律 P1"只读逻辑层查询"的落实
        /// 方式是"由装配根按需注入已解析好的表"，不是"View 自己去查逻辑层"）；因此把"实例 id → 模板 id
        /// → equip_visual 行"这一步解析的责任交给注入方（通常是具体游戏的表现层装配代码，在拿到
        /// <c>item.equipped</c> 通知时自行解析后维护/刷新这份按实例 id 索引的表，再传给
        /// <c>SpriteViewBase</c> 的构造参数或未来的"替换查询表"扩展点），本类型只消费已解析好的结果。
        /// </para>
        /// <para>
        /// 判断记录（槽位→纸娃娃层名换算）：<c>EquipVisualDef.SlotId</c> 取最后一个点分段作为纸娃娃层名
        /// （与 <c>anchor_points</c>/<c>paperdoll_layers</c> 的裸层名同一惯例，如 <c>slot.hand_main</c> →
        /// <c>hand_main</c>）；该层名不要求预先出现在 <c>DisplayInfo.Sprite.PaperdollLayers</c>
        /// 默认集合里——装备可以给角色新增一层默认不绘制的部位（如武器）。
        /// </para>
        /// <para>
        /// 判断记录（<c>mesh_ref</c> 不经方向档位换算）：见 <see cref="EquipVisualDef.MeshRef"/> 字段
        /// 注释——04 未给该字段定义按方向拆分的子结构，本类型把它当作该层的唯一资源直接使用，是已知
        /// 简化（该层因此不随朝向切换镜像素材，只整体跟随精灵实例翻转，同 <see cref="SyncPose"/> 的
        /// <c>flipX</c>）。
        /// </para>
        /// </summary>
        public virtual void OnEvent(IEvent evt)
        {
            if (_equipVisuals == null)
            {
                return;
            }

            switch (evt)
            {
                case ItemEquippedEvent equipped when equipped.UnitId.Equals(EntityId):
                    HandleItemEquipped(equipped.Slot, equipped.ItemInstanceId);
                    break;

                case ItemUnequippedEvent unequipped when unequipped.UnitId.Equals(EntityId):
                    HandleItemUnequipped(unequipped.Slot);
                    break;
            }
        }

        private void HandleItemEquipped(Id slot, Id itemInstanceId)
        {
            if (!_equipVisuals!.TryGetValue(itemInstanceId, out var def) || def.Mode != EquipVisualMode.SlotMesh
                || def.SlotId == null || def.MeshRef == null)
            {
                // 无对应 equip_visual 行（或该行不是 slot_mesh 模式）：按判断记录保持不变。
                return;
            }

            var layerName = LayerNameFromSlotId(def.SlotId.Value);
            _equipOverridesBySlot[slot] = (layerName, def.MeshRef.Value);
            RebuildEquippedLayers();
        }

        private void HandleItemUnequipped(Id slot)
        {
            if (_equipOverridesBySlot.Remove(slot))
            {
                RebuildEquippedLayers();
            }
        }

        /// <summary>见 <see cref="IEquipmentVisualResettable"/> 接口注释（P2-08 根治）：清空
        /// <see cref="_equipOverridesBySlot"/> 全表（不依赖逐槽位反查是否命中），再按
        /// <paramref name="equipped"/> 逐条重建——与 <see cref="HandleItemEquipped"/> 同一套
        /// "查不到对应行/非 slot_mesh 模式时保持不变（跳过该条）"规则，只是批量执行；最后统一调用
        /// 一次 <see cref="RebuildEquippedLayers"/>，不为每条已装备物品各重建一次层列表。</summary>
        public void ResetEquipmentVisuals(IReadOnlyList<EquippedItemRef> equipped)
        {
            _equipOverridesBySlot.Clear();

            if (_equipVisuals != null && equipped != null)
            {
                for (var i = 0; i < equipped.Count; i++)
                {
                    var item = equipped[i];
                    if (_equipVisuals.TryGetValue(item.ItemInstanceId, out var def)
                        && def.Mode == EquipVisualMode.SlotMesh && def.SlotId != null && def.MeshRef != null)
                    {
                        _equipOverridesBySlot[item.Slot] = (LayerNameFromSlotId(def.SlotId.Value), def.MeshRef.Value);
                    }
                }
            }

            RebuildEquippedLayers();
        }

        private static string LayerNameFromSlotId(Id slotId)
        {
            var value = slotId.Value;
            var dotIndex = value.LastIndexOf('.');
            return dotIndex < 0 ? value : value.Substring(dotIndex + 1);
        }

        /// <summary>用 <see cref="DisplayInfo.Sprite"/>.<c>PaperdollLayers</c> 的默认层名顺序为基底，
        /// 逐层用 <see cref="_equipOverridesBySlot"/> 里当前生效的装备覆盖资源 Id 替换（未被任何槽位
        /// 覆盖的层沿用 <see cref="ResolveLayerResourceId"/> 的常规方向档位换算）；装备覆盖引入的、不在
        /// 默认层名集合里的新层追加在末尾（见 <see cref="OnEvent"/> 判断记录"装备可以新增一层"），按
        /// <see cref="_equipOverridesBySlot"/> 的槽位 <see cref="Id"/> 升序排列以保证结果确定。</summary>
        private void RebuildEquippedLayers()
        {
            var overridesByLayerName = new Dictionary<string, Id>(StringComparer.Ordinal);
            foreach (var kv in _equipOverridesBySlot)
            {
                overridesByLayerName[kv.Value.LayerName] = kv.Value.ResourceId;
            }

            var placements = Conventions.ComposeSpriteLayers(DisplayInfo.Sprite!.PaperdollLayers, DisplayInfo.Sprite!, _lastFacing);
            var resourceIds = new List<Id>(placements.Count);
            var coveredLayerNames = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < placements.Count; i++)
            {
                var layerName = placements[i].LayerName;
                coveredLayerNames.Add(layerName);

                Id resourceId;
                if (overridesByLayerName.TryGetValue(layerName, out var overrideId))
                {
                    resourceId = overrideId;
                }
                else
                {
                    resourceId = ResolveLayerResourceId(placements[i]);
                }

                _resourceTracker?.EnsureLoading(resourceId, ResourceKind.Image);
                resourceIds.Add(resourceId);
            }

            var extraLayerNames = new List<string>();
            foreach (var layerName in overridesByLayerName.Keys)
            {
                if (!coveredLayerNames.Contains(layerName))
                {
                    extraLayerNames.Add(layerName);
                }
            }
            extraLayerNames.Sort(StringComparer.Ordinal);

            for (var i = 0; i < extraLayerNames.Count; i++)
            {
                var resourceId = overridesByLayerName[extraLayerNames[i]];
                _resourceTracker?.EnsureLoading(resourceId, ResourceKind.Image);
                resourceIds.Add(resourceId);
            }

            _rig.ApplyLayers(resourceIds);
        }

        public virtual void SyncPose(Vec2 pos, Direction facing, double height)
        {
            EnsureAlive();

            _lastFacing = facing;

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

            _rig.ComposeAndApplyLayers(layerNamesInOrder, currentFacing, ResolveLayerResourceId);
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

        /// <summary>消费方反馈第 32 条（ADR-0025）：此前本方法独立实现"去掉类别前缀、点号换下划线"
        /// 规则（见 14 第 1.2 节命名模板"&lt;资源引用id去掉类别前缀，点号换下划线&gt;"），与
        /// <c>UnityResourceLoader</c> 的同名私有方法字节级相同但各自维护；现转发到
        /// <see cref="AssetRefConventions.StripCategoryPrefix"/>，全仓唯一实现见该类型。</summary>
        private static string StripCategoryPrefix(string resourceRefId) =>
            AssetRefConventions.StripCategoryPrefix(resourceRefId);

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

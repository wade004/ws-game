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

        /// <summary>当前各装备槽位覆盖的纸娃娃层：槽位 id → (层名, 装备层资源集引用)。
        /// <c>EquipLayerSetRef</c> 是 <see cref="EquipVisualDef.MeshRef"/> 原值（ADR-0071 决策
        /// 1：sprite 型下不再是"该层最终资源 Id"，而是与身体层 <c>SpriteSetId</c> 同一位置的"资源集
        /// 引用"），装备/卸下事件增删本表后重新调用 <see cref="RebuildEquippedLayers"/>——命中覆盖的层
        /// 改经 <see cref="ResolveEquipLayerResourceId"/> 按当前朝向换算，不再是本表存什么就直接用
        /// 什么。</summary>
        private readonly Dictionary<Id, (string LayerName, Id EquipLayerSetRef)> _equipOverridesBySlot =
            new Dictionary<Id, (string, Id)>();

        /// <summary><see cref="SyncPose"/> 最近一次收到的朝向，供 <see cref="RebuildEquippedLayers"/>
        /// 重算未被装备覆盖的层的方向档位资源 Id（装备事件与 SyncPose 异步到达，不能假设装备事件自带
        /// 朝向）。构造期以 <see cref="Direction.FromQuantized"/> 0 弧度、<c>DisplayInfo.Sprite.DirectionCount</c>
        /// 档位初始化，首次 SyncPose 前触发的装备事件按该默认朝向合成，不阻断装配。</summary>
        private Direction _lastFacing;

        /// <summary>ADR-0093 决策 3：<see cref="SyncPose"/> 最近一次触发 <see cref="OnDirectionSlotChanged"/>
        /// 时对应的 <see cref="IRenderConventionHost.ResolveDirectionSlot"/> 结果（已经过 remap/mirror
        /// 的裸档位 Id，如 <c>"dir.front"</c>），供逐次 <see cref="SyncPose"/> 判断"这一次解析出的档位
        /// 是否与上一次不同"——只有真的不同才触发钩子，同档位内逐帧调用是空操作（不逐帧探测，见
        /// <see cref="OnDirectionSlotChanged"/> 判断记录）。构造期按 <see cref="_lastFacing"/> 同一份
        /// 默认朝向预先算出初始值，避免首次 <see cref="SyncPose"/> 恰好与挂接期默认朝向相同时也触发一次
        /// 多余的钩子调用。</summary>
        private Id _lastDirectionSlotId;

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
            _lastDirectionSlotId = Conventions.ResolveDirectionSlot(_lastFacing, DisplayInfo.Sprite).SlotId;

            var spriteSetId = Id.Parse(DisplayInfo.Sprite.SpriteSetId);
            _resourceTracker?.EnsureLoading(spriteSetId, ResourceKind.Image);
            Handle = Renderer.CreateSpriteInstance(spriteSetId);

            // GP-PRES-05 收口（09 第 3.4 节"每个可见单位默认携带一个地面投影影子"）：此前
            // DisplayInfo.Shadow 只在数据/转换层（ShadowSpec）存在，从未被任何实际 View 消费——
            // 构造时按 DisplayInfo.Shadow 设置一次，后续该值不会随生命周期变化（DisplayInfo 本身
            // 不可变），不需要在 SyncPose 里重复设置。
            Renderer.SetShadow(Handle, ShadowSpec.ToEngineShadowMode(DisplayInfo.Shadow));

            _rig = new SpriteCharacterRig(default, Renderer, Handle, Conventions, DisplayInfo, _resourceTracker, frameAnimPlayer, Options);

            // ADR-0099 决策 2 收口（已知限制 1 根治，见 OnLayersComposed 判断记录）：订阅 SpriteCharacterRig
            // 唯一的渲染器写入出口，使"方向槽位变化/装备变化"（本类型自己调用 _rig.ApplyLayers）与
            // "首次引用的纸娃娃层资源异步加载完成后的迟到回填"（_rig.HandleResourceLoadCompleted 内部
            // 调用 _rig.ApplyLayers，本类型不直接参与）两条路径共用同一个通知出口，不需要分别接线；
            // OnLayersComposed 是 protected virtual，此处以方法组订阅，调用时按运行期实际类型走虚派发
            // （构造期订阅、调用发生在构造完成之后，具体子类的重写在那时已经就绪，同 09"虚方法在构造期
            // 订阅、真正触发在构造完成后"惯例）。
            _rig.LayersApplied += OnLayersComposed;
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
        /// 判断记录（ADR-0071 决策 1，取代此前"<c>mesh_ref</c> 不经方向档位换算"的已知简化）：
        /// <see cref="EquipVisualDef.MeshRef"/> sprite 型下现与身体层 <c>SpriteSetId</c> 同一位置——
        /// "该装备层的资源集引用"，不是最终资源 Id。<see cref="HandleItemEquipped"/>/
        /// <see cref="ResetEquipmentVisuals"/> 只把它原样存进 <see cref="_equipOverridesBySlot"/>，
        /// 真正的资源 Id 换算延后到 <see cref="RebuildEquippedLayers"/> 按当前朝向调用
        /// <see cref="ResolveEquipLayerResourceId"/>——与未被覆盖的层经 <see cref="ResolveLayerResourceId"/>
        /// 换算同一套方向档位规则（见该方法判断记录），装备层因此与身体层一样随朝向切换素材，见
        /// <see cref="EquipVisualDef.MeshRef"/> 字段注释。
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
        /// 追加装备覆盖引入的、不在默认层名集合里的新层（见 <see cref="OnEvent"/> 判断记录"装备可以
        /// 新增一层"，按 <see cref="_equipOverridesBySlot"/> 的槽位 <see cref="Id"/> 升序排列以保证
        /// 结果确定），对这一份合并后的完整层名列表统一调一次 <see cref="IRenderConventionHost.ComposeSpriteLayers"/>
        /// 按当前朝向解析方向槽位——装备新增的层与身体默认层共用同一次方向解析，不另起一套（同
        /// <see cref="ResolveEquipLayerResourceId"/> 判断记录"两条路径永远同一套方向档位规则"）。
        /// 逐层再按是否命中 <see cref="_equipOverridesBySlot"/> 覆盖分派到 <see cref="ResolveEquipLayerResourceId"/>
        /// 或 <see cref="ResolveLayerResourceId"/>（ADR-0071 决策 1：命中覆盖的层不再直接采用
        /// <see cref="EquipVisualDef.MeshRef"/> 原值，而是把它当"装备层资源集引用"经方向换算）。
        /// <para>
        /// 判断记录（协调者反馈根治，2026-09-23：朝向变化掉装）：本方法的"基础层名 + 装备新增额外层
        /// 合并 -&gt; 逐层按是否命中覆盖分派解析"这套逻辑已抽成 <see cref="ComposeAndApplyEquipAwareLayers"/>
        /// （接受"基础层名顺序 + 朝向"两个参数），本方法与 <see cref="SetPaperdollLayers"/> 共用同一份
        /// 实现——此前只有本方法（装备/卸下事件触发）经这套装备感知逻辑，<see cref="SetPaperdollLayers"/>
        /// （具体游戏 View 子类在朝向变化时调用，见该方法判断记录）走的是另一条完全不感知
        /// <see cref="_equipOverridesBySlot"/> 的路径，导致已装备的层朝向一变就被身体层资源顶掉、
        /// 装备新增的额外层更是被整个漏掉（<see cref="SetPaperdollLayers"/> 的调用方只传默认层名，
        /// 从不包含额外层名）。两个方法现在共用同一套合成逻辑，行为不再分叉。</para>
        /// </summary>
        private void RebuildEquippedLayers() => ComposeAndApplyEquipAwareLayers(DisplayInfo.Sprite!.PaperdollLayers, _lastFacing);

        /// <summary>
        /// 见 <see cref="RebuildEquippedLayers"/>/<see cref="SetPaperdollLayers"/> 判断记录：两者共用的
        /// "装备感知层合成"核心逻辑——用 <paramref name="baseLayerNamesInOrder"/> 为基底，追加
        /// <see cref="_equipOverridesBySlot"/> 引入的、不在这份基底集合里的新层（按槽位 Id 升序排列
        /// 保证结果确定），对合并后的完整层名列表统一调一次
        /// <see cref="IRenderConventionHost.ComposeSpriteLayers"/> 按 <paramref name="facing"/> 解析
        /// 方向槽位，逐层按是否命中覆盖分派到 <see cref="ResolveEquipLayerResourceId"/> 或
        /// <see cref="ResolveLayerResourceId"/>。没有任何装备覆盖时（<c>_equipOverridesBySlot</c> 为空）
        /// 结果与"直接对 <paramref name="baseLayerNamesInOrder"/> 调用
        /// <see cref="ResolveLayerResourceId"/>"逐字节一致——<see cref="SetPaperdollLayers"/> 改走本方法
        /// 后，未装备任何物品的实体朝向变化行为不受影响。</summary>
        private void ComposeAndApplyEquipAwareLayers(IReadOnlyList<string> baseLayerNamesInOrder, Direction facing)
        {
            var overridesByLayerName = new Dictionary<string, Id>(StringComparer.Ordinal);
            foreach (var kv in _equipOverridesBySlot)
            {
                overridesByLayerName[kv.Value.LayerName] = kv.Value.EquipLayerSetRef;
            }

            var coveredLayerNames = new HashSet<string>(baseLayerNamesInOrder, StringComparer.Ordinal);

            var extraLayerNames = new List<string>();
            foreach (var layerName in overridesByLayerName.Keys)
            {
                if (!coveredLayerNames.Contains(layerName))
                {
                    extraLayerNames.Add(layerName);
                }
            }
            extraLayerNames.Sort(StringComparer.Ordinal);

            var allLayerNames = new List<string>(baseLayerNamesInOrder.Count + extraLayerNames.Count);
            allLayerNames.AddRange(baseLayerNamesInOrder);
            allLayerNames.AddRange(extraLayerNames);

            var placements = Conventions.ComposeSpriteLayers(allLayerNames, DisplayInfo.Sprite!, facing);
            var resourceIds = new List<Id>(placements.Count);

            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                var resourceId = overridesByLayerName.TryGetValue(placement.LayerName, out var equipLayerSetRef)
                    ? ResolveEquipLayerResourceId(placement, equipLayerSetRef)
                    : ResolveLayerResourceId(placement);

                // 见 SpriteCharacterRig 类型注释"资源加载完成后回填已渲染层"判断记录：把
                // HandleResourceLoadCompleted 作为 onComplete 传入，与 SpriteCharacterRig.
                // ComposeAndApplyLayers 内部同一份回调绑定到同一个 rig 实例，装备驱动的层重建路径
                // 与朴素层合成路径共用同一套"加载完成后重新应用当前完整层列表"机制。
                _resourceTracker?.EnsureLoading(resourceId, ResourceKind.Image, _rig.HandleResourceLoadCompleted);
                resourceIds.Add(resourceId);
            }

            // 判断记录：不在这里直接调用 OnLayersComposed——_rig.ApplyLayers 内部的 LayersApplied 事件
            // （构造期已订阅到 OnLayersComposed，见构造函数判断记录）会在写入渲染器之后自动触发一次，
            // 这里再调一次会导致同一次合成触发两次通知。
            _rig.ApplyLayers(resourceIds);
        }

        /// <summary>
        /// ADR-0099 决策 2（消费方反馈第四十四批）：<see cref="_rig"/>.<see cref="SpriteCharacterRig.ApplyLayers"/>
        /// 是本类型对渲染器写入纸娃娃层的唯一出口，写入之后经 <see cref="SpriteCharacterRig.LayersApplied"/>
        /// 事件（构造函数已订阅到本方法，见该处判断记录）触发一次——覆盖 <see cref="RebuildEquippedLayers"/>
        /// （装备变化）、<see cref="SetPaperdollLayers"/>（方向变化，<c>UnitySpriteView.SyncPose</c> 在
        /// 方向槽位真的变化时调用，见 ADR-0099 决策 1）与 <see cref="SpriteCharacterRig.HandleResourceLoadCompleted"/>
        /// （首次引用的纸娃娃层资源异步加载完成后的迟到回填）三条路径，是"任何一次写入渲染器的重合成"
        /// 唯一的落点——冷加载回填此前遗漏本钩子（ADR-0099 定稿时的已知限制 1），因为它由
        /// <see cref="SpriteCharacterRig"/> 内部直接调用 <see cref="SpriteCharacterRig.ApplyLayers"/>，
        /// 不经过本类型代码；现改为在 <see cref="SpriteCharacterRig.ApplyLayers"/> 内部统一触发通知，
        /// 不要求调用方各自记得转发，两条既有路径与这一条冷加载路径此后天然待遇一致，不需要再分别接线。
        /// <para>
        /// 判断记录（为什么需要这个钩子）：<see cref="SpriteCharacterRig.ApplyLayers"/> 对每一层无条件
        /// 覆盖 <c>SpriteRenderer.sprite</c> 为本次解析出的静态资源，会把 ADR-0072 挂接的纸娃娃层逐层
        /// 走路/攻击等动画（<c>UnityViewFactory.TryAttachPerLayerAnimation</c> 挂在
        /// <see cref="IFrameAnimPlayer.OnFrameChanged"/> 上按帧号写回的当前动画帧）覆盖回静态图，
        /// 要等下一次 <c>OnFrameChanged</c> 触发（12fps 剪辑在 60Hz 下最多 5 帧空档）才会被重新写回，
        /// 期间出现短暂的"贴图跳回静态图再跳回动画帧"的可见闪烁。本方法默认空实现（ABI 加法，
        /// <c>protected virtual</c>，不改 <see cref="SetPaperdollLayers"/> 既有签名）：本类型不知道
        /// "方向相关的逐层动画"具体如何组织（那是引擎适配层 <c>UnityViewFactory</c> 所在层的职责，同
        /// <see cref="OnDirectionSlotChanged"/> 判断记录"只负责事实通知，不负责具体探测/回填逻辑"）；
        /// 具体游戏/引擎适配层的 View 子类（如 <c>Adapter.Unity.Presentation.UnitySpriteView</c>）重写
        /// 本方法把 <paramref name="layerResourceIds"/> 对应的这次重合成事实转发出去（该类型转发为
        /// <c>LayersComposed</c> 事件），供订阅方按"当前正在播放的逐层动画剪辑 + 播放器当前帧号"立即
        /// 把命中逐层动画的层重新 <c>SetLayerSprite</c> 回当前帧——不推进时间轴、不重置播放进度，只是
        /// 把这一次被覆盖的静态图立即纠正回来，不必等下一次 <c>OnFrameChanged</c>。
        /// </para>
        /// </summary>
        protected virtual void OnLayersComposed(IReadOnlyList<Id> layerResourceIds)
        {
        }

        public virtual void SyncPose(Vec2 pos, Direction facing, double height)
        {
            EnsureAlive();

            _lastFacing = facing;

            var sortY = Conventions.ComputeSortY(pos, DisplayInfo.SortOffset);
            var (slotId, flipX) = Conventions.ResolveDirectionSlot(facing, DisplayInfo.Sprite!);

            if (!slotId.Equals(_lastDirectionSlotId))
            {
                _lastDirectionSlotId = slotId;
                OnDirectionSlotChanged(slotId);
            }

            var heightPixels = Conventions.HeightOffsetToPixels(height, Options.PixelsPerUnit);
            Renderer.SetTransform(Handle, pos, heightPixels, sortY, RenderLayers.Units, 0.0, DisplayInfo.Scale, flipX);
        }

        /// <summary>
        /// ADR-0093 决策 3/4：<see cref="SyncPose"/> 每次调用都会经 <see cref="IRenderConventionHost.ResolveDirectionSlot"/>
        /// 重新解析当前朝向对应的方向槽位（已经过 <c>DirectionIndexRemap</c>/<c>mirror_pairs</c> 处理的
        /// 裸档位 Id，如 <c>"dir.front"</c>）；只有这次解析结果与上一次不同时才会调用本方法一次——同一
        /// 方向档位内逐帧 <see cref="SyncPose"/>（移动但朝向不变、原地静止）不会重复触发，满足"只在档位
        /// 变化时探测，不逐帧探测"（见 ADR-0093 决策 3）。
        /// <para>
        /// 默认空实现（ABI 加法，<c>protected virtual</c>，不改 <see cref="SyncPose"/> 既有签名）：本类型
        /// 自身不知道"方向相关的动画剪辑"具体如何组织（那是引擎适配层 <c>TryAttachPerLayerAnimation</c>
        /// 所在层的职责，见该方法类型注释），只负责"档位变了"这一事实的检测与通知。具体游戏/引擎适配层
        /// 的 View 子类（如 <c>Adapter.Unity.Presentation.UnitySpriteView</c>）重写本方法，把
        /// <paramref name="newSlotId"/> 转发给自己持有的动画播放/探测机制，重新解析该方向下应播放的
        /// 剪辑集（逐层/整身），并保留当前剪辑的播放进度（同一状态内换方向不从头播，见 ADR-0093 决策 2）。
        /// </para>
        /// </summary>
        protected virtual void OnDirectionSlotChanged(Id newSlotId)
        {
        }

        /// <summary>用给定层名顺序与当前朝向重新合成纸娃娃层并应用到 <see cref="IRenderer2D.SetLayers"/>
        /// （见 09 第 3.3.1 节"层内 z 序 = 列表顺序"）。由具体游戏的 <see cref="OnEvent"/> 重写在收到
        /// <c>item.equipped</c>/<c>item.unequipped</c> 等装备变化事件时调用；引擎侧 View 子类（如
        /// <c>Adapter.Unity.Presentation.UnitySpriteView.SyncPose</c>）在朝向变化时也会调用本方法
        /// 重新合成层——见下方判断记录，本方法签名/可见性不变（ABI 只加法）。
        /// <para>
        /// 判断记录（协调者反馈根治，2026-09-23：朝向变化掉装）：本方法此前直接调
        /// <c>_rig.ComposeAndApplyLayers(layerNamesInOrder, currentFacing, ResolveLayerResourceId)</c>，
        /// 完全不看 <see cref="_equipOverridesBySlot"/>——朝向变化触发本方法时，已装备槽位命中的层会
        /// 被身体层解析（<see cref="ResolveLayerResourceId"/>）顶掉，装备新增的额外层（不在
        /// <paramref name="layerNamesInOrder"/> 里的层名）更是整个消失，直到下一次装备/卸下事件重新
        /// 调用 <see cref="RebuildEquippedLayers"/> 才恢复——玩家表现为"一转身装备就消失"。改为与
        /// <see cref="RebuildEquippedLayers"/> 共用 <see cref="ComposeAndApplyEquipAwareLayers"/>：没有
        /// 任何装备覆盖时结果与旧实现逐字节一致（见该方法判断记录），有装备覆盖时命中的层改走
        /// <see cref="ResolveEquipLayerResourceId"/>、装备新增的额外层同样会被合并进最终层列表，与
        /// 朝向变化是否触发本方法这条路径无关。
        /// </para>
        /// </summary>
        protected void SetPaperdollLayers(IReadOnlyList<string> layerNamesInOrder, Direction currentFacing)
        {
            EnsureAlive();

            ComposeAndApplyEquipAwareLayers(layerNamesInOrder, currentFacing);
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
            return ComposeLayerResourceId(spriteSetName, placement);
        }

        /// <summary>
        /// ADR-0071 决策 1 新增：装备覆盖层的资源 Id 换算——与 <see cref="ResolveLayerResourceId"/>
        /// 完全同一套方向档位/命名规则（两者共用 <see cref="ComposeLayerResourceId"/>），唯一区别是
        /// "资源集名字"的来源不是 <see cref="DisplayInfo.Sprite"/>.<c>SpriteSetId</c>，而是
        /// <paramref name="equipLayerSetRef"/>（<see cref="_equipOverridesBySlot"/> 里存的、来自
        /// <see cref="EquipVisualDef.MeshRef"/> 的装备层资源集引用，见该字段注释）。<c>protected
        /// virtual</c>、不改 <see cref="ResolveLayerResourceId"/> 既有签名（ABI 只新增，见 AGENTS.md
        /// 第 3 节）：具体游戏若要自定义装备层的资源命名规则，覆盖本方法即可，不影响身体层既有覆盖点。
        /// </summary>
        protected virtual Id ResolveEquipLayerResourceId(SpriteLayerPlacement placement, Id equipLayerSetRef)
        {
            var layerSetName = StripCategoryPrefix(equipLayerSetRef.Value);
            return ComposeLayerResourceId(layerSetName, placement);
        }

        /// <summary>见 <see cref="ResolveLayerResourceId"/>/<see cref="ResolveEquipLayerResourceId"/>
        /// 判断记录：两者共用的资源 Id 拼接公式（14 第 1.2 节命名模板），只是"资源集名字"来源不同
        /// （身体层用 <c>SpriteSetId</c>，装备层用 <c>EquipVisualDef.MeshRef</c>），确保两条路径
        /// 永远是同一套方向档位规则，不会出现"身体层换了命名算法、装备层没跟着换"的分叉。</summary>
        private static Id ComposeLayerResourceId(string layerSetName, SpriteLayerPlacement placement)
        {
            var directionSlotName = Presentation.Common.DirectionSlots.StripPrefix(placement.DirectionSlotId);
            return new Id($"layer.{layerSetName}__{directionSlotName}__{placement.LayerName}");
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

            // 见 SpriteCharacterRig 类型注释"资源加载完成后回填已渲染层"判断记录：先标记 rig
            // 已销毁，再销毁渲染实例——之后到达的迟到 HandleResourceLoadCompleted 回调会直接跳过，
            // 不会在 Handle 已失效之后再调用 IRenderer2D.SetLayers 触碰它。
            _rig.MarkDestroyed();
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

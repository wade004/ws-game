#nullable enable
// UnityViewFactory：Presentation.Common.IViewFactory 的 Unity 引擎实现，presentation/view_binding
// 的 ViewBinder 是唯一调用方（见该接口注释）。
//
// 判断记录（View 生命周期跟踪，弥补 ViewBinder/CameraHost 不支持退订的已知缺口）：
// presentation/assembly/README.md"判断记录 4"如实记录了 ViewBinder 构造期直接 bus.Subscribe，
// 没有对外暴露退订句柄，PresentationAssembly.Dispose() 因此无法代为退订/销毁 ViewBinder 已创建的
// 全部 View。若不处理，场景重进（新建一整套 World/GameplayAssembly/PresentationAssembly）会让
// 上一次的 View（其 Sprite 实例挂在 DontDestroyOnLoad 的 UnityEngineHost.Renderer2D 根下，不会
// 随场景卸载自动销毁）持续累积。本类型不改 presentation/（该模块补退订属于契约变更，需要走 12
// 第 5 节流程），改为在"引擎侧"补一个不属于 IViewFactory 契约本身的协作方法
// DestroyAllCreatedViews——本工厂记住自己创建过的每一个 View，供
// GameFoundationBootstrap.OnDestroy 主动逐一调用 IView.Destroy() 清理，与
// UnityResourceLoader.TryGetSprite 之类"引擎实现之间的内部协作方法，不算契约违反"同一惯例。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using UnityEngine;

namespace Adapter.Unity.Presentation
{
    /// <summary>没有可用 sprite 型 DisplayInfo 时的退化 View（见 <see cref="UnityViewFactory.CreateView"/>
    /// 判断记录）：不渲染任何东西，但仍满足 <see cref="IView"/> 契约，不阻断 <c>ViewBinder</c> 的绑定表
    /// 维护与后续事件转发查找。</summary>
    internal sealed class NullView : IView
    {
        public Id EntityId { get; private set; }
        public bool IsAlive { get; private set; }

        public void Bind(Id entityId)
        {
            EntityId = entityId;
            IsAlive = true;
        }

        public void OnEvent(IEvent evt)
        {
        }

        public void SyncPose(Vec2 pos, Direction facing, double height)
        {
        }

        public void Destroy() => IsAlive = false;
    }

    public sealed class UnityViewFactory : IViewFactory
    {
        /// <summary>动画状态机驱动到剪辑的六个状态名，见 <see cref="AnimClipResolver.StateKey"/>；
        /// 逐一登记默认剪辑（真实 <c>display.anim_set</c> 或单帧退化），使
        /// <see cref="AnimClipResolver"/> 对任意一次状态切换都能查到表项，不遗漏。</summary>
        private static readonly string[] DefaultAnimStateKeys = { "idle", "move", "attack", "cast", "hit", "death" };

        private readonly IRenderer2D _renderer2D;
        private readonly IRenderConventionHost _conventions;
        private readonly IDisplayInfoRegistry _displayInfo;
        private readonly IResourceLoader _resourceLoader;
        private readonly IEventBus? _bus;
        private readonly IDataRegistryView? _dataRegistry;

        /// <summary>W6-B 新增：model 型外形的三维渲染实现，可选（默认 null，纯 sprite 型游戏不需要
        /// 提供，见 02 第 1.12 节"条件必需"）——未提供时遇到 kind=model 的 DisplayInfo 退化为
        /// <see cref="NullView"/>（同"没有可用 sprite 型 DisplayInfo"分支同一套宽容处理，见
        /// <see cref="CreateView"/> 判断记录）。</summary>
        private readonly IRenderer3D? _renderer3D;

        /// <summary>W6-B 新增（ADR-0017 决策 d）：命中帧同步的按实体 rig 注册表，可选——未注入时
        /// View 创建/销毁不做任何登记（同本类型一贯"未装配的能力静默跳过"惯例），装配方需要
        /// <c>RenderOptions.HitFrameSync == AnimKeyframeDriven</c> 时应传入同一个实例并把它也接给
        /// <c>Presentation.FeedbackBinder.Core.FeedbackBinder</c> 的 <c>hitFrameSource</c> 构造参数
        /// （装配层职责，见包 README"命中帧同步接线步骤"）。</summary>
        private readonly IHitFrameSource? _hitFrameSource;

        /// <summary>W6-B 新增（ADR-0017 决策 e）：实体 -> 武器风格引用查询，可选——非空时
        /// <see cref="AnimClipResolver"/> 的 Attack/Cast 状态额外尝试武器风格覆盖（见该类型判断
        /// 记录）。</summary>
        private readonly IWeaponStyleSource? _weaponStyleSource;

        /// <summary>W6 收口新增（ADR-0017 决策 d 遗留缺口收口）：构造 <c>UnitySpriteView</c>/
        /// <c>UnityModelView</c> 时透传的 <see cref="RenderOptions"/>，决定 <see cref="SpriteCharacterRig"/>/
        /// <see cref="ModelCharacterRig"/> 的 <c>HitFrameSync</c> 策略——此前本类型两处 View 构造调用
        /// （<see cref="CreateView"/>）都省略了这个可选参数，即便装配方把 <c>AnimKeyframeDriven</c>
        /// 配到别处（如 <see cref="Presentation.Assembly.PresentationAssemblyOptions.RenderOptions"/>），
        /// rig 构造期实际拿到的仍是 <c>options: null</c>（默认 <c>LogicDriven</c>），<c>HitFrameReached</c>
        /// 事件永远不会被订阅/触发（见两个 Rig 类型构造函数判断记录）——是比 <c>PresentationAssembly</c>
        /// 未暴露 <c>hitFrameSource</c> 构造参数更深一层、此前未被发现的接线缺口，与该缺口同属"命中帧
        /// 同步端到端未打通"这同一个问题，一并收口。装配方需要把同一个 <see cref="RenderOptions"/>
        /// 实例分别传给本参数与 <c>PresentationAssemblyOptions.RenderOptions</c>（见包 README"命中帧
        /// 同步接线步骤"），保证两处看到同一个 <c>HitFrameSync</c> 取值；不传时（默认 null）行为与
        /// 改动前完全一致。</summary>
        private readonly RenderOptions? _renderOptions;

        /// <summary>PR130-07 根治新增：默认 factory 的 equipVisual 映射入口（doc-code-matrix 此前记录
        /// 的能力边界"UnityViewFactory 构造函数没有 equipVisual 参数……默认 factory 仍缺入口"）——
        /// 物品实例 id -&gt; <see cref="EquipVisualDef"/>，直接透传给 <see cref="UnityModelView"/> 构造
        /// 函数的同名参数（见该类型判断记录）。可选，默认 null 时 <see cref="UnityModelView.OnEvent"/>
        /// 对装备变化事件保持默认空处理，行为与改动前完全一致；装配方通常传入
        /// <see cref="EquipmentVisualSource.VisualByItemInstanceId"/>（见该类型判断记录）。</summary>
        private readonly IReadOnlyDictionary<Id, EquipVisualDef>? _equipVisuals;

        private readonly List<IView> _created = new List<IView>();
        private readonly HashSet<string> _warnedMissingDisplay = new HashSet<string>();
        private readonly HashSet<string> _warnedAnimDegraded = new HashSet<string>();

        /// <summary>W6-B 新增：已经处理过关键帧事件注册的 model 型剪辑资源引用去重集合（见
        /// <see cref="RegisterModelClipEvents"/> 判断记录），避免同一份 <c>AnimationClip</c> 资产被
        /// 多个共享同一 <c>display.anim_set</c> 的实体重复设置 <c>events</c>。</summary>
        private readonly HashSet<Id> _registeredModelClipEvents = new HashSet<Id>();

        // 外部审核阻塞项 3 收口（见 architecture/落地计划/audit-20260907/followup-2026-09-07.md
        // "外部审核阻塞项处理"一节）：默认动画接线状态——entityId -> 已挂接的播放器/该实体的剪辑表，
        // 供下方懒构造的单例 AnimClipResolver 的 playClip/defaultClipsForEntity 两个委托按 entityId
        // 路由（见该类型构造函数参数注释）。AnimStateMachine/AnimClipResolver 是全局单例（跨全部
        // 实体共享同一份状态机与解析器，而不是每个 View 各建一份）——AnimStateMachine 本身就是按
        // entityId 分别记账的全局状态表（见该类型注释），重复构造多份只会让多份状态机各自收到同一批
        // 事件、重复计算，没有任何好处。
        private readonly Dictionary<Id, UnityFrameAnimPlayer> _animPlayersByEntity = new Dictionary<Id, UnityFrameAnimPlayer>();
        private readonly Dictionary<Id, IReadOnlyDictionary<string, Id>> _animClipsByEntity = new Dictionary<Id, IReadOnlyDictionary<string, Id>>();

        /// <summary>W6-B 新增：model 型实体的默认动画路由表——同 <see cref="_animPlayersByEntity"/>
        /// 姊妹表，供 <see cref="EnsureAnimClipResolver"/> 的 <c>playClip</c> 委托在查不到
        /// <see cref="UnityFrameAnimPlayer"/>（sprite 专属）时改走该实体的
        /// <see cref="Presentation.Render.ModelCharacterRig.PlayClip"/>。</summary>
        private readonly Dictionary<Id, UnityModelView> _modelViewsByEntity = new Dictionary<Id, UnityModelView>();

        private AnimStateMachine? _animStateMachine;

        // GP-06 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：冷启动时
        // display.anim_set 声明了 resource_ref、但该资源尚未加载进 UnityResourceLoader 缓存
        // （TryGetEffect 未命中）——此前直接登记单帧 fallback 并永久停在那儿，从不 LoadAsync、
        // 加载完成后也不重新登记，纸娃娃永久退化成白点。改法见 RequestAnimClipUpgrade：先登记单帧
        // fallback 保证 Rig.PlayClip 立即可执行，同时发起真正加载，完成后把同一个 clipId 重新登记为
        // 真实多帧剪辑（RegisterClipFromEffect 对同一 clipId 直接覆盖，见 UnityFrameAnimPlayer.
        // RegisterClip 判断记录）。
        // _pendingAnimResourceLoads：已经调用过 LoadAsync 的资源 id 去重集合（同 VfxPlayer.
        // _pendingResourceLoads/SfxPlayer._pendingResourceLoads 同款惯例），避免多个实体共享同一份
        // anim_set 时重复发起加载。
        // _pendingAnimClipWaiters：resourceRef -> 等待这份资源加载完成后需要重新登记的
        // (player, clipId) 列表——同一份资源可能被多个实体的多个状态引用（例如同一个 anim_set 的
        // idle 剪辑），加载完成时需要通知全部等待方，不止最早发起加载的那一个。
        private readonly HashSet<Id> _pendingAnimResourceLoads = new HashSet<Id>();
        // W6-B 新增（ADR-0017 决策 c）：等待元组追加 Events——events 属于具体某一条 AnimClipDef
        // （某个状态对某个 resourceRef 的引用），不是 resourceRef 本身的固有属性（理论上不同状态可能
        // 引用同一份 resource_ref 但声明不同的 events，虽然占位内容不会这样做），因此按等待方各自
        // 携带自己的 events，而不是按 resourceRef 缓存一份"代表性"events。
        private readonly Dictionary<Id, List<(UnityFrameAnimPlayer Player, Id ClipId, IReadOnlyList<Core.Foundation.DisplayInfo.AnimClipEventSpec> Events)>> _pendingAnimClipWaiters =
            new Dictionary<Id, List<(UnityFrameAnimPlayer, Id, IReadOnlyList<Core.Foundation.DisplayInfo.AnimClipEventSpec>)>>();
        private AnimClipResolver? _animClipResolver;

        // PR130-03 根治：AnimClipResolver 解析出的 clipId 不只来自 RegisterDefaultClips 登记的六个
        // 默认状态——武器风格覆盖（WeaponStyleDef.AutoAttackAnim/CastAnimOverride，见该类型注释）与
        // 未来任何"按技能/装备覆盖默认剪辑"的查表结果都可能是一个从未登记进 UnityFrameAnimPlayer 的
        // 全新 clipId，FrameAnimPlayer.Play 对未登记的 clipId 直接抛 ArgumentException（见其类型
        // 判断记录），此前 playClip 委托对此毫无防御。改法与 RegisterDefaultClips/RequestAnimClipUpgrade
        // 同一套"先登记单帧占位保证立即可用，同时发起真正加载，完成后原地升级"机制（见
        // EnsureSpriteClipRegistered/RequestWeaponClipUpgrade），只是不再局限于六个固定状态键，改为
        // 任意 clipId——_pendingWeaponClipResourceLoads 去重同一 clipId 只发起一次加载（含"此后永远
        // 不再移除，加载失败不重试"，同 _pendingAnimResourceLoads 一贯惯例），
        // _pendingWeaponClipWaiters 登记同一 clipId 被多个实体的播放器共同引用时的全部等待方（同
        // _pendingAnimClipWaiters 一贯惯例，避免只升级最早发起加载的那一个播放器）。
        private readonly HashSet<Id> _pendingWeaponClipResourceLoads = new HashSet<Id>();
        private readonly Dictionary<Id, List<UnityFrameAnimPlayer>> _pendingWeaponClipWaiters = new Dictionary<Id, List<UnityFrameAnimPlayer>>();

        private static Sprite? _fallbackFrame;

        /// <summary>
        /// <paramref name="bus"/>/<paramref name="dataRegistry"/> 均可选（外部审核阻塞项 3 收口新增，
        /// 见类型顶部 <see cref="_animPlayersByEntity"/> 判断记录）：默认动画接线只在两者都提供时才
        /// 生效——未提供任一个（如既有 PlayMode 测试直接 <c>new UnityViewFactory(renderer, conv,
        /// displayInfo, loader)</c> 四参构造、不关心动画）保持此前"不接线"的行为，不抛异常、不产生
        /// 任何副作用，与本类型一贯"未装配的能力静默跳过"的既有惯例一致。生产装配（
        /// <c>GameFoundationBootstrap</c>/<c>FrameworkResidentHost</c>/<c>games/_template.GameBootstrap</c>）
        /// 三处一律传入两者，使 <c>Rig.PlayClip</c> 默认可用，三处调用方不需要各自再手工接一遍
        /// <see cref="UnityFrameAnimPlayer"/>/<see cref="AnimClipResolver"/>。
        /// W6-B 新增三个可选参数（<paramref name="renderer3D"/>/<paramref name="hitFrameSource"/>/
        /// <paramref name="weaponStyleSource"/>）：均遵循本类型既有"未装配的能力静默跳过"惯例，均默认
        /// null 时行为与改动前完全一致（纯 sprite 型装配不需要改任何调用点）。
        /// </summary>
        public UnityViewFactory(
            IRenderer2D renderer2D,
            IRenderConventionHost conventions,
            IDisplayInfoRegistry displayInfo,
            IResourceLoader resourceLoader,
            IEventBus? bus = null,
            IDataRegistryView? dataRegistry = null,
            IRenderer3D? renderer3D = null,
            IHitFrameSource? hitFrameSource = null,
            IWeaponStyleSource? weaponStyleSource = null,
            RenderOptions? renderOptions = null,
            IReadOnlyDictionary<Id, EquipVisualDef>? equipVisualByItemInstanceId = null)
        {
            _renderer2D = renderer2D ?? throw new ArgumentNullException(nameof(renderer2D));
            _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
            _bus = bus;
            _dataRegistry = dataRegistry;
            _renderer3D = renderer3D;
            _hitFrameSource = hitFrameSource;
            _weaponStyleSource = weaponStyleSource;
            _renderOptions = renderOptions;
            _equipVisuals = equipVisualByItemInstanceId;

            if (_bus != null)
            {
                // GP-02 根治：复活/销毁两个事件各自清理默认动画的一部分记账，见两个处理方法各自
                // 判断记录——二者不能合并成一个处理方法，清理范围不同（销毁清空播放器引用，复活不）。
                _bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, OnEntityDestroyedForAnim);
                _bus.Subscribe<UnitRespawnedEvent>(RulesEventKeys.UnitRespawned, OnUnitRespawnedForAnim);
            }
        }

        /// <summary>GP-02 根治：实体真正从世界移除——这个 entityId 之后可能被完全不同的新实体复用
        /// （<c>WorldSim</c> 惯例，见 <c>GameplayAssembly.LeaveMap</c> 一类判断记录"实体 id 不保证不
        /// 复用"），本工厂给旧实体挂接的 <see cref="UnityFrameAnimPlayer"/> 引用、剪辑表、
        /// <see cref="AnimStateMachine"/> 记账都必须一并清空——组件本身随 GameObject 销毁自动失效，
        /// 但字典里持有的引用不会自动消失，留着就是悬空引用（同 <see cref="DestroyAllCreatedViews"/>
        /// 判断记录），新实体复用同一个 entityId 时会读到已销毁旧组件、或继承旧实体的终态锁。</summary>
        private void OnEntityDestroyedForAnim(EntityDestroyedEvent evt)
        {
            _animStateMachine?.Forget(evt.EntityId);
            _animPlayersByEntity.Remove(evt.EntityId);
            _animClipsByEntity.Remove(evt.EntityId);
            _modelViewsByEntity.Remove(evt.EntityId);

            // W6-B 新增：命中帧同步注册表同一套"随实体销毁清理"惯例（见 IHitFrameSource.UnregisterRig
            // 契约注释"未登记过时 no-op"，对从未注册过 hit frame 的实体调用同样安全）。
            _hitFrameSource?.UnregisterRig(evt.EntityId);
        }

        /// <summary>GP-02 根治：复活时同一个 View/播放器组件通常原地复用（不是"销毁重建"），只清空
        /// <see cref="AnimStateMachine"/> 对该实体的状态记账——死亡是优先级最高的终态锁（见该类型
        /// 注释），不清空的话复活后仍然停留在 Death，后续任何状态切换（Move/Attack/Cast）都会被
        /// "终态不接受回落"规则拒绝，复活的角色会卡在死亡姿势且再也动不了。不移除
        /// <see cref="_animPlayersByEntity"/>/<see cref="_animClipsByEntity"/>：播放器组件与剪辑表
        /// 依然有效，清空它们反而会让 <see cref="AnimClipResolver"/> 此后找不到播放器，彻底哑掉该
        /// 实体的动画（同 <see cref="OnEntityDestroyedForAnim"/> 判断记录"清理范围不同"）。</summary>
        private void OnUnitRespawnedForAnim(UnitRespawnedEvent evt)
        {
            _animStateMachine?.Forget(evt.UnitId);
        }

        /// <summary>本工厂迄今创建过的全部 View，只读快照（诊断/测试用）。</summary>
        public IReadOnlyList<IView> CreatedViews => _created;

        /// <summary>诊断/测试用：<paramref name="resourceRef"/> 是否已经发起过一次
        /// <see cref="RequestAnimClipUpgrade"/> 加载尝试（不代表加载结果是成功还是失败，见
        /// <see cref="_pendingAnimResourceLoads"/> 判断记录"此后永远不再移除"）。供跨用例共享同一个
        /// <see cref="UnityViewFactory"/> 单例（DontDestroyOnLoad，见 PlayModeIsolation.cs 判断记录）
        /// 的 PlayMode 测试判断"这次是不是第一次触发某个默认动画资源的冷加载"，从而只在真正会产生
        /// 新诊断日志的那一次用例里注册 <c>LogAssert.Expect</c>，不需要（也无法）在测试代码里猜测
        /// 跨用例执行顺序。</summary>
        public bool HasAttemptedAnimResourceLoad(Id resourceRef) => _pendingAnimResourceLoads.Contains(resourceRef);

        /// <summary>C12 测试用（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：本工厂
        /// 内部持有的全局单例 <see cref="AnimStateMachine"/>，供 PlayMode 测试直接查询某实体是否仍
        /// 停留在 <see cref="AnimStateMachine.IsTerminal"/>（死亡终态锁）——验证 <c>death.reload_save</c>
        /// 同图读档成功后确实清理了这个终态锁（<see cref="OnUnitRespawnedForAnim"/> 已经能正确处理
        /// <see cref="UnitRespawnedEvent"/>，只是此前 <c>DeathPolicyHost</c> 的 <c>reload_save</c>
        /// 成功分支从不发这个事件，见该问题判断记录）。<c>internal</c>——只供
        /// <c>Runtime/AssemblyInfo.cs</c> 的 <c>InternalsVisibleTo</c> 对 <c>Tests.Runtime</c>/
        /// <c>Tests.Editor</c> 可见，不是公开契约的一部分。</summary>
        internal AnimStateMachine? AnimStateMachineForTests => _animStateMachine;

        /// <summary>W6-B 收口：<paramref name="displayId"/> 解析到 kind=model 的 DisplayInfo 时
        /// （见 <see cref="DisplayKind"/>），若装配方提供了 <see cref="_renderer3D"/> 走真实三维渲染
        /// 分支，否则与"完全没有可用 DisplayInfo"同一套退化处理——本类型不假设任何 model 型外形都必须
        /// 有 <see cref="IRenderer3D"/>，02 第 1.12 节"条件必需"的落实方式正是"未提供时该外形静默不
        /// 可见，不阻断装配"。</summary>
        public IView CreateView(ViewKind kind, Id displayId, Id entityId)
        {
            var info = _displayInfo.Lookup(displayId);
            IView view;

            if (info != null && info.Kind == DisplayKind.Model && _renderer3D != null)
            {
                var modelView = new UnityModelView(entityId, _renderer3D, _conventions, info, _renderOptions, _equipVisuals);
                view = modelView;

                if (info.Category == DisplayCategory.Creature)
                {
                    AttachDefaultModelAnimation(modelView, info, entityId);
                }

                _hitFrameSource?.RegisterRig(entityId, modelView.Rig);
            }
            else if (info == null || info.Kind != DisplayKind.Sprite)
            {
                if (_warnedMissingDisplay.Add(displayId.Value))
                {
                    var reason = info != null && info.Kind == DisplayKind.Model
                        ? "kind=model 但本工厂未装配 IRenderer3D"
                        : "没有 kind=sprite 的 DisplayInfo";
                    Debug.LogWarning($"[UnityViewFactory] displayId \"{displayId}\" {reason}，退化为空视图（不渲染）：kind={kind}");
                }
                view = new NullView();
            }
            else
            {
                var spriteView = new UnitySpriteView(_renderer2D, _conventions, info, _resourceLoader, _renderOptions);
                view = spriteView;

                // 外部审核阻塞项 3 收口：只给"生物"（玩家/NPC/怪物——唯一会真正经
                // AnimStateMachine 收到 idle/move/attack/cast/hit/death 状态切换的分类，见该类型
                // 事件->状态映射判断记录）挂默认动画；item/gobj/projectile 一类不会触发任何状态
                // 切换的静态外形不需要这份接线（挂了也是纯开销，见 AttachDefaultAnimation 调用点
                // 判断记录）。
                if (info.Category == DisplayCategory.Creature)
                {
                    AttachDefaultAnimation(spriteView, info, entityId);
                }

                // W6-B 新增：sprite 路线也登记进命中帧同步注册表（见 IHitFrameSource 类型注释、
                // ADR-0017 决策 d）——与 model 路线同一套接线，不管外形类型，只要挂了 CharacterRig
                // 就登记。
                _hitFrameSource?.RegisterRig(entityId, spriteView.Rig);
            }

            _created.Add(view);
            return view;
        }

        /// <summary>
        /// 外部审核阻塞项 3 收口（09 §4 动画层——组件已实现但默认生产入口未接线）：给
        /// <paramref name="view"/> 默认挂一个 <see cref="UnityFrameAnimPlayer"/>（六个状态各注册一条
        /// 剪辑）+ 全局单例 <see cref="AnimClipResolver"/>（懒构造，见 <see cref="EnsureAnimClipResolver"/>），
        /// 使 <see cref="SpriteViewBase.Rig"/> 的 <c>PlayClip</c> 默认可执行、且随
        /// <c>AnimStateMachine</c> 状态切换自动播放——不再要求
        /// <c>FrameworkResidentHost</c>/<c>GameFoundationBootstrap</c>/<c>games/_template.GameBootstrap</c>
        /// 三处各自手工接线（此前只有 <c>Tests/Runtime/AnimationLayerTests.cs</c> 验证过这几个组件
        /// 本身能正确工作，没有任何生产代码路径真正调用过 <c>AttachFrameAnimPlayer</c>）。
        /// </summary>
        private void AttachDefaultAnimation(UnitySpriteView view, Core.Foundation.DisplayInfo.DisplayInfo info, Id entityId)
        {
            if (_bus == null)
            {
                // 未注入 IEventBus：见构造函数判断记录，静默跳过，不影响 View 本身创建成功。
                return;
            }

            // 判断记录：GetSpriteRoot/GetLayersRoot 是 UnityRenderer2D 的具体类型方法，不属于
            // IRenderer2D 契约本身（同类还有 UnityResourceLoader.TryGetSprite 一类"引擎实现之间的
            // 内部协作方法，不算契约违反"，见本文件类型顶部判断记录）；_renderer2D 字段按契约只声明为
            // IRenderer2D，这里用 is 模式防御性向下转型（同 GameplayAssembly 多处
            // "Carriers.Units is WorldUnitAccess" 的一贯写法），生产装配传入的恒为 UnityRenderer2D，
            // 不是该具体类型时（测试替身/未来实现变化）静默跳过，不抛异常。
            if (!(_renderer2D is Adapter.Unity.EngineAdapter.UnityRenderer2D concreteRenderer))
            {
                return;
            }

            // U04 根治（第五轮外部审核 audit-5e779c6-20260907/AUDIT_REPORT.md）：此前把
            // UnityFrameAnimPlayer 直接挂在 GetSpriteRoot 返回的根物体自身上——该组件自带的
            // SpriteRenderer 因此既不是 LayersRoot 的子物体（不受 SetTransform 的 height 偏移平移），
            // 也不在 LayerRenderers 集合里（ApplyColor 遍历不到，flash/fade 不生效），只有根物体本身
            // 的位置/旋转/缩放仍由 SetTransform 正常处理。改为挂在 GetLayersRoot 返回的 LayersRoot
            // 子物体下，天然随 height 偏移一起平移；并经 RegisterAnimRootRenderer 登记，使其额外参与
            // 颜色（flash/fade）与 flipX 遍历——与纸娃娃层同一份变换和反馈管线，影子仍按原判断记录
            // 独立处理。
            var layersRoot = concreteRenderer.GetLayersRoot(view.EngineHandle);
            if (layersRoot == null)
            {
                // 防御性判断：真实 UnityRenderer2D 在 UnitySpriteView 构造完成后必定已经建好精灵根
                // 节点/LayersRoot 子物体（见 AnimationLayerTests 一贯做法——构造后立即调用
                // GetSpriteRoot 不为 null），这里只是防御未来实现变化，不抛异常。
                return;
            }

            var animRootGo = new GameObject("AnimRoot");
            animRootGo.transform.SetParent(layersRoot, worldPositionStays: false);

            var player = animRootGo.AddComponent<UnityFrameAnimPlayer>();
            concreteRenderer.RegisterAnimRootRenderer(view.EngineHandle, player.SpriteRenderer);
            var clips = RegisterDefaultClips(player, info);
            view.AttachFrameAnimPlayer(player);

            _animPlayersByEntity[entityId] = player;
            _animClipsByEntity[entityId] = clips;

            EnsureAnimClipResolver();

            // GP-02 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：此前默认
            // 工厂只接了 StateChanged -> Play 这一半（见 AnimClipResolver），播放完成后从不回头通知
            // AnimStateMachine——Hit/Attack/Cast 这类瞬态状态优先级锁只能靠
            // NotifyTransientStateFinished 解除（见该方法判断记录"只有 finishedState 与当前状态一致
            // 才生效"），不调用就永久锁死，播放完 Hit 后角色再也进不了 Move/Attack/Cast。这里把
            // IFrameAnimPlayer.OnComplete（每次非循环剪辑播放结束触发一次，见该接口注释）接回
            // AnimStateMachine：以"回调触发那一刻状态机记录的当前状态"作为 finishedState——
            // AttachDefaultAnimation 在 EnsureAnimClipResolver 之后才订阅，_animStateMachine 此时必已
            // 构造完成，不会是 null。
            var stateMachine = _animStateMachine!;
            player.OnComplete(() =>
            {
                stateMachine.NotifyTransientStateFinished(entityId, stateMachine.GetState(entityId));
            });
        }

        /// <summary>
        /// 六个状态各登记一条剪辑，返回登记结果供 <see cref="AnimClipResolver"/> 的
        /// <c>defaultClipsForEntity</c> 查表使用。
        /// <para>
        /// 判断记录（<c>display.anim_set</c> 行 id 解析约定——按最后一段匹配，惯例同
        /// <c>Core.Gameplay.Assembly.TeleportTargetResolver.TryFindByLastSegment</c>"点位 id 最后一段
        /// 匹配"）：<see cref="AnimClipResolver"/> 类型顶部判断记录明确指出"具体某个实体该用哪张
        /// <c>display.anim_set</c> 表，属于具体游戏的组装层代码该决定的事，本模块不硬编码"——但
        /// <see cref="UnityViewFactory"/> 恰恰是"没有具体游戏参与、必须给出一个开箱即用默认值"的
        /// 生产入口，只能给一个尽量不容易误配的默认约定：取 <paramref name="info"/>.<see
        /// cref="Core.Foundation.DisplayInfo.DisplayInfo.Id"/>（<c>display.map</c> 行自身 id，如
        /// <c>"display.map.sample_hero"</c>）的最后一段（<c>"sample_hero"</c>），查找 id 为
        /// <c>"display.anim_set." + 最后一段</c>（<c>"display.anim_set.sample_hero"</c>）的
        /// <c>display.anim_set</c> 行——与示例数据 <c>data/_sample/display/display.anim_set.json</c>
        /// 的 <c>display.anim_set.sample_hero</c> 行天然对上（"占位英雄"，见任务书原文）。查不到该行、
        /// 或行内某个状态没有声明剪辑、或声明了但 <c>resource_ref</c> 加载失败（当前占位资源集没有
        /// 真正的角色序列帧资源，见 <see cref="AnimClipResolver"/> 类型顶部判断记录），均逐状态退化为
        /// "单帧剪辑"（复用同一张 1x1 占位帧，见
        /// <see cref="FallbackFrame"/>）——不抛异常，只在每个 (displayId, 状态) 组合首次退化时记一条
        /// 诊断（<see cref="_warnedAnimDegraded"/> 去重，避免同一实体反复创建/销毁刷屏）。
        /// </para>
        /// </summary>
        private IReadOnlyDictionary<string, Id> RegisterDefaultClips(UnityFrameAnimPlayer player, Core.Foundation.DisplayInfo.DisplayInfo info)
        {
            var animSet = TryResolveAnimSet(info.Id);
            var result = new Dictionary<string, Id>(StringComparer.Ordinal);

            for (var i = 0; i < DefaultAnimStateKeys.Length; i++)
            {
                var stateKey = DefaultAnimStateKeys[i];
                var clipId = new Id($"anim.default.{info.Id.Value}.{stateKey}");
                Core.Foundation.DisplayInfo.AnimClipDef? clipDef = null;
                var hasDeclaredResource = animSet != null && animSet.Clips.TryGetValue(stateKey, out clipDef);
                var resourceRef = hasDeclaredResource ? clipDef!.ResourceRef : default;
                var events = hasDeclaredResource ? clipDef!.Events : Array.Empty<Core.Foundation.DisplayInfo.AnimClipEventSpec>();
                var unityLoader = _resourceLoader as Adapter.Unity.EngineAdapter.UnityResourceLoader;

                if (hasDeclaredResource && unityLoader != null && unityLoader.TryGetEffect(resourceRef, out var effect))
                {
                    // 资源已经在缓存里（非首次引用，或恰好是同步加载器）：直接登记真实多帧剪辑，
                    // ADR-0017 决策 c：把 events 换算成帧索引关键帧（见 ComputeKeyframes 判断记录）
                    // 一并注册。
                    player.RegisterClipFromEffect(clipId, effect, ComputeKeyframes(events, effect.Frames.Length));
                }
                else if (hasDeclaredResource && unityLoader != null)
                {
                    // GP-06 根治：声明了 resource_ref，只是这次是"冷启动"——尚未加载进缓存，不是
                    // "压根没配"。先登记单帧占位保证立即可用，同时发起真正加载，完成后原地升级成
                    // 真实多帧剪辑（见 RequestAnimClipUpgrade 判断记录），不再永久停留在单帧退化。
                    player.RegisterSingleFrameClip(clipId, FallbackFrame);
                    RequestAnimClipUpgrade(unityLoader, resourceRef, player, clipId, stateKey, events);
                }
                else
                {
                    // 真的没有声明这个状态的动画（display.anim_set 查不到该行，或行内该状态字段缺失）
                    // ——不是"还没加载完"，是数据里压根没配，维持原有单帧退化 + 诊断，不发起加载。
                    if (_warnedAnimDegraded.Add(info.Id.Value + "." + stateKey))
                    {
                        Debug.LogWarning(
                            $"[UnityViewFactory] displayId \"{info.Id}\" 状态 \"{stateKey}\" 没有可用的 display.anim_set 序列帧剪辑，" +
                            "退化为单帧剪辑（Rig.PlayClip 仍可执行，只是视觉上不切换贴图）");
                    }
                    player.RegisterSingleFrameClip(clipId, FallbackFrame);
                }

                result[stateKey] = clipId;
            }

            return result;
        }

        /// <summary>
        /// GP-06 根治：<paramref name="resourceRef"/> 已声明但尚未加载完成时，登记
        /// (<paramref name="player"/>, <paramref name="clipId"/>) 为该资源的等待方，并按需发起一次
        /// <see cref="IResourceLoader.LoadAsync"/>（同一资源被多个实体/状态共同引用时只发起一次，见
        /// <see cref="_pendingAnimResourceLoads"/> 判断记录）。加载完成后把全部等待方一次性升级为
        /// 真实多帧剪辑；<paramref name="player"/> 若在加载完成前已被销毁（Unity 对象销毁后与
        /// <c>null</c> 比较为真，见 Unity 官方"伪 null"惯例），跳过它，不抛异常。
        /// </summary>
        private void RequestAnimClipUpgrade(
            Adapter.Unity.EngineAdapter.UnityResourceLoader unityLoader, Id resourceRef,
            UnityFrameAnimPlayer player, Id clipId, string stateKey,
            IReadOnlyList<Core.Foundation.DisplayInfo.AnimClipEventSpec> events)
        {
            if (!_pendingAnimClipWaiters.TryGetValue(resourceRef, out var waiters))
            {
                waiters = new List<(UnityFrameAnimPlayer, Id, IReadOnlyList<Core.Foundation.DisplayInfo.AnimClipEventSpec>)>();
                _pendingAnimClipWaiters[resourceRef] = waiters;
            }
            waiters.Add((player, clipId, events));

            if (!_pendingAnimResourceLoads.Add(resourceRef))
            {
                // 已经有别的实体/状态先一步发起了这份资源的加载，本次只需要排进等待列表，不重复
                // LoadAsync（同一资源重复请求加载没有意义，且部分引擎实现可能因此重复触发磁盘 IO）。
                return;
            }

            unityLoader.LoadAsync(resourceRef, ResourceKind.Effect, (loadedResourceId, success) =>
            {
                // 判断记录：不从 _pendingAnimResourceLoads 移除——同 VfxPlayer._pendingResourceLoads/
                // SfxPlayer._pendingResourceLoads 既有惯例"此后永远不再移除，含加载失败的情形，
                // 失败不重试"（GP-06 验收"后续不重复加载"）。若这里移除，同一个确定加载失败的资源
                // 会在每次有新实体/新场景重新引用它时又重新发起一次 LoadAsync（重复磁盘 IO + 重复
                // 警告日志），对一个已确定失败的资源没有任何意义。
                if (!_pendingAnimClipWaiters.TryGetValue(loadedResourceId, out var pendingWaiters))
                {
                    return;
                }
                _pendingAnimClipWaiters.Remove(loadedResourceId);

                if (!success || !unityLoader.TryGetEffect(loadedResourceId, out var loadedEffect))
                {
                    Debug.LogWarning(
                        $"[UnityViewFactory] 状态 \"{stateKey}\" 引用的动画资源 \"{loadedResourceId}\" 加载失败，" +
                        "继续使用单帧占位剪辑（不重试）");
                    return;
                }

                for (var i = 0; i < pendingWaiters.Count; i++)
                {
                    var (waitingPlayer, waitingClipId, waitingEvents) = pendingWaiters[i];
                    if (waitingPlayer == null)
                    {
                        // 加载完成前 View 已被销毁（切图/实体销毁）：Unity 对象销毁后与 null 比较为
                        // 真，跳过即可，不需要也不应该再对一个已销毁的组件重新登记剪辑。
                        continue;
                    }
                    waitingPlayer.RegisterClipFromEffect(waitingClipId, loadedEffect, ComputeKeyframes(waitingEvents, loadedEffect.Frames.Length));
                }
            });
        }

        /// <summary>PR130-03 根治：保证 <paramref name="clipId"/> 在 <paramref name="player"/> 上已经
        /// 登记，供 <see cref="EnsureAnimClipResolver"/> 的 <c>playClip</c> 委托在调用
        /// <see cref="UnityFrameAnimPlayer.Play"/> 之前调用——<paramref name="clipId"/> 可能来自
        /// <see cref="Presentation.VfxSfx.Contracts.WeaponStyleDef.AutoAttackAnim"/>/
        /// <see cref="Presentation.VfxSfx.Contracts.WeaponStyleDef.CastAnimOverride"/>，从未随
        /// <see cref="RegisterDefaultClips"/> 的六个默认状态一起登记过。已登记（<see cref="UnityFrameAnimPlayer.HasClip"/>）
        /// 时直接返回；未登记时按 <see cref="RegisterDefaultClips"/> 同一套优先级尝试解析：资源已在
        /// <see cref="Adapter.Unity.EngineAdapter.UnityResourceLoader"/> 缓存里（<c>TryGetEffect</c>
        /// 命中）直接登记真实多帧剪辑；未命中时先登记单帧占位剪辑保证立即可用并记一次诊断，同时发起
        /// 一次真正的异步加载（<see cref="RequestWeaponClipUpgrade"/>），完成后原地升级——与
        /// <see cref="RequestAnimClipUpgrade"/> 是同一套机制在"任意 clipId"而不是"六个固定状态键"上
        /// 的推广，见类型顶部 <see cref="_pendingWeaponClipResourceLoads"/> 判断记录。</summary>
        private void EnsureSpriteClipRegistered(UnityFrameAnimPlayer player, Id clipId)
        {
            if (player.HasClip(clipId))
            {
                return;
            }

            var unityLoader = _resourceLoader as Adapter.Unity.EngineAdapter.UnityResourceLoader;
            if (unityLoader != null && unityLoader.TryGetEffect(clipId, out var effect))
            {
                player.RegisterClipFromEffect(clipId, effect);
                return;
            }

            if (_warnedAnimDegraded.Add("weapon_clip." + clipId.Value))
            {
                Debug.LogWarning(
                    $"[UnityViewFactory] 剪辑 \"{clipId}\"（武器风格/技能覆盖解析得到）尚未登记，" +
                    "退化为单帧剪辑呈现，同时发起异步加载，加载完成后原地升级为真实多帧剪辑（Rig.PlayClip 仍可执行）");
            }
            player.RegisterSingleFrameClip(clipId, FallbackFrame);

            if (unityLoader != null)
            {
                RequestWeaponClipUpgrade(unityLoader, clipId, player);
            }
        }

        /// <summary>见 <see cref="EnsureSpriteClipRegistered"/> 判断记录：按 <paramref name="clipId"/>
        /// 去重发起一次 <see cref="IResourceLoader.LoadAsync"/>（同一 clipId 被多个实体的播放器共同
        /// 引用时只发起一次），完成后把全部登记等待方一次性升级为真实多帧剪辑；某个
        /// <paramref name="player"/> 在加载完成前已被销毁（Unity 对象销毁后与 <c>null</c> 比较为真）
        /// 时跳过它，不抛异常。</summary>
        private void RequestWeaponClipUpgrade(Adapter.Unity.EngineAdapter.UnityResourceLoader unityLoader, Id clipId, UnityFrameAnimPlayer player)
        {
            if (!_pendingWeaponClipWaiters.TryGetValue(clipId, out var waiters))
            {
                waiters = new List<UnityFrameAnimPlayer>();
                _pendingWeaponClipWaiters[clipId] = waiters;
            }
            waiters.Add(player);

            if (!_pendingWeaponClipResourceLoads.Add(clipId))
            {
                return;
            }

            unityLoader.LoadAsync(clipId, ResourceKind.Effect, (loadedId, success) =>
            {
                if (!_pendingWeaponClipWaiters.TryGetValue(loadedId, out var pendingWaiters))
                {
                    return;
                }
                _pendingWeaponClipWaiters.Remove(loadedId);

                if (!success || !unityLoader.TryGetEffect(loadedId, out var loadedEffect))
                {
                    Debug.LogWarning(
                        $"[UnityViewFactory] 武器风格/技能覆盖剪辑 \"{loadedId}\" 加载失败，继续使用单帧占位剪辑（不重试）");
                    return;
                }

                for (var i = 0; i < pendingWaiters.Count; i++)
                {
                    var waitingPlayer = pendingWaiters[i];
                    if (waitingPlayer == null)
                    {
                        continue;
                    }
                    waitingPlayer.RegisterClipFromEffect(loadedId, loadedEffect);
                }
            });
        }

        /// <summary>ADR-0017 决策 c：把 <c>display.anim_set.clips[*].events</c>（时间轴百分比 + 裸
        /// 事件名）换算成 <see cref="UnityFrameAnimPlayer.RegisterClip"/> 系列方法要求的"事件名 -> 帧
        /// 索引"关键帧表（见 <see cref="Presentation.Render.FrameAnimClip.Keyframes"/>）：
        /// <c>frameIndex = round(time_pct × (frameCount − 1))</c>，四舍五入后夹在
        /// <c>[0, frameCount − 1]</c> 区间（<paramref name="frameCount"/> 为 0 时返回空表，避免除零/
        /// 负索引）。<paramref name="events"/> 为空或 null 时返回 null（<see cref="UnityFrameAnimPlayer.RegisterClipFromEffect"/>
        /// 的 <c>keyframes</c> 参数本就是可选的，传 null 与"没有任何关键帧"语义一致，不需要额外分配一个
        /// 空字典）。</summary>
        private static IReadOnlyDictionary<string, int>? ComputeKeyframes(
            IReadOnlyList<Core.Foundation.DisplayInfo.AnimClipEventSpec>? events, int frameCount)
        {
            if (events == null || events.Count == 0 || frameCount <= 0)
            {
                return null;
            }

            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < events.Count; i++)
            {
                var frameIndex = (int)Math.Round(events[i].TimePct * (frameCount - 1), MidpointRounding.AwayFromZero);
                frameIndex = Math.Max(0, Math.Min(frameCount - 1, frameIndex));
                result[events[i].Name] = frameIndex;
            }
            return result;
        }

        /// <summary>解析 <paramref name="displayMapId"/>（<c>display.map</c> 行自身 id）对应的
        /// <c>display.anim_set</c> 行（sprite 型没有专属字段直接引用，见 <see cref="AnimClipResolver"/>
        /// 类型顶部判断记录"约定 id"）。W6-B 收口：不再经本文件自造的 <c>AnimSetRecordParser</c>
        /// 中转，直接消费 <see cref="Core.Foundation.DisplayInfo.AnimSetDef.FromRecord"/>（W6-A 新增，
        /// 同时解析 <c>resource_ref</c> 与 <c>events</c>，取代此前"显式跳过 events"的临时简化）。</summary>
        private Core.Foundation.DisplayInfo.AnimSetDef? TryResolveAnimSet(Id displayMapId)
        {
            if (_dataRegistry == null)
            {
                return null;
            }

            var lastDot = displayMapId.Value.LastIndexOf('.');
            var lastSegment = lastDot < 0 ? displayMapId.Value : displayMapId.Value.Substring(lastDot + 1);
            var animSetId = new Id("display.anim_set." + lastSegment);

            var record = _dataRegistry.Get("display.anim_set", animSetId);
            return record == null ? null : Core.Foundation.DisplayInfo.AnimSetDef.FromRecord(record);
        }

        /// <summary>全局单例 <see cref="AnimClipResolver"/>：首次挂接默认动画时懒构造，此后全部实体
        /// 共用同一份（见类型顶部 <see cref="_animPlayersByEntity"/> 字段判断记录）。</summary>
        private void EnsureAnimClipResolver()
        {
            if (_animClipResolver != null)
            {
                return;
            }

            _animStateMachine = new AnimStateMachine(_bus!);
            _animClipResolver = new AnimClipResolver(
                _animStateMachine,
                defaultClipsForEntity: entityId => _animClipsByEntity.TryGetValue(entityId, out var clips) ? clips : null,
                playClip: (entityId, clipId, loop, speed) =>
                {
                    if (_animPlayersByEntity.TryGetValue(entityId, out var player))
                    {
                        // PR130-03 根治：clipId 可能是武器风格/技能覆盖解析出的、从未登记过的剪辑，
                        // 见 EnsureSpriteClipRegistered 判断记录——FrameAnimPlayer.Play 对未登记的
                        // clipId 会抛异常，本调用点必须先保证已登记。
                        EnsureSpriteClipRegistered(player, clipId);
                        player.Play(clipId, loop, speed);
                        return;
                    }

                    // W6-B 新增：model 型实体没有 UnityFrameAnimPlayer，改直接转发到该实体持有的
                    // ModelCharacterRig.PlayClip（经 IRenderer3D.PlayAnim 落地，见该类型判断记录）。
                    // model 路线不需要类似 EnsureSpriteClipRegistered 的预登记步骤——
                    // UnityRenderer3D.PlayAnim 按 Animator 状态名现查现用（AnimatorHasState），查不到
                    // 时静默退回 legacy Animation 兜底或直接 no-op，不会抛异常（见该方法判断记录）。
                    if (_modelViewsByEntity.TryGetValue(entityId, out var modelView))
                    {
                        modelView.Rig.PlayClip(clipId, loop, speed);
                    }
                },
                weaponStyleSource: _weaponStyleSource,
                weaponStyles: ResolveWeaponStyleCatalog());
        }

        /// <summary>W6-B 新增：懒解析一次 <c>display.weapon_style</c> 全表（见 <see cref="AnimClipResolver"/>
        /// 构造参数 <c>weaponStyles</c> 判断记录——与 <c>Presentation.Assembly.PresentationAssembly</c>
        /// 内部同名解析各自独立一份，二者都是只读投影，语义一致，不产生状态不一致，同
        /// <c>viewFactoryDisplayInfo</c> 既有判断记录同一惯例）；<see cref="_dataRegistry"/> 未注入或
        /// 数据集里没有该表时返回 null（<see cref="AnimClipResolver"/> 对 null 目录的处理是"跳过武器
        /// 风格覆盖，退回默认剪辑表"，见其构造函数注释）。只解析一次并缓存，武器风格表在装配期之后
        /// 不会变化（同 <c>display.map</c>/<c>display.anim_set</c> 一贯的"内容表只读、装配期加载一次"
        /// 惯例）。</summary>
        private IReadOnlyDictionary<Id, WeaponStyleDef>? ResolveWeaponStyleCatalog()
        {
            if (_dataRegistry == null)
            {
                return null;
            }

            if (_weaponStyleCatalog != null)
            {
                return _weaponStyleCatalog;
            }

            var records = _dataRegistry.GetAll("display.weapon_style");
            var catalog = new Dictionary<Id, WeaponStyleDef>();
            for (var i = 0; i < records.Count; i++)
            {
                var def = WeaponStyleDef.FromRecord(records[i]);
                catalog[def.Id] = def;
            }
            _weaponStyleCatalog = catalog;
            return _weaponStyleCatalog;
        }

        private IReadOnlyDictionary<Id, WeaponStyleDef>? _weaponStyleCatalog;

        /// <summary>单帧退化剪辑复用的占位帧：1x1 白色像素合成的 <see cref="Sprite"/>，跨全部实体
        /// 共享同一份（不需要每个实体各自持有一份视觉上完全等价的纹理）。<see cref="UnityFrameAnimPlayer"/>
        /// 播放时写入的是它自己内部持有的 <see cref="UnityEngine.SpriteRenderer"/>（与
        /// <see cref="UnitySpriteView"/> 管理的纸娃娃层渲染器是两套独立通道，见
        /// <c>Tests/Runtime/AnimationLayerTests.cs</c> 判断记录"两套渲染通道尚未融合"），本占位帧
        /// 只需要满足"确实存在一张贴图，<see cref="IFrameAnimPlayer.CurrentClipId"/>/播放状态可观察"
        /// 这一验收要求，不追求任何视觉效果。</summary>
        private static Sprite FallbackFrame
        {
            get
            {
                if (_fallbackFrame == null)
                {
                    var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    texture.SetPixel(0, 0, Color.white);
                    texture.Apply();
                    _fallbackFrame = Sprite.Create(texture, new UnityEngine.Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
                }
                return _fallbackFrame;
            }
        }

        /// <summary>销毁本工厂创建过的全部仍存活的 View（见类型注释判断记录），并清空跟踪列表。
        /// 供 <c>GameFoundationBootstrap.OnDestroy</c>/场景重进清理调用，不属于 <see cref="IViewFactory"/>
        /// 契约本身。</summary>
        public void DestroyAllCreatedViews()
        {
            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i].IsAlive)
                {
                    _created[i].Destroy();
                }
            }
            _created.Clear();

            // 外部审核阻塞项 3 收口：View 全部销毁的同时，本工厂给这些实体挂接的动画播放器引用与
            // 剪辑表也一并清空——UnityFrameAnimPlayer 组件本身随精灵根节点被销毁（挂在同一
            // GameObject 上，见 AttachDefaultAnimation），这里只是清掉本工厂自己持有的字典引用，
            // 避免下一局游戏（同一 UnityViewFactory 实例复用）里 entityId 复用时读到已销毁组件的
            // 悬空引用。AnimStateMachine/AnimClipResolver 不重建——它们不持有任何具体 View/引擎对象
            // 引用，装配一次即可跨局复用。
            _animPlayersByEntity.Clear();
            _animClipsByEntity.Clear();
            _modelViewsByEntity.Clear();
        }

        // --------------------------------------------------------------
        // W6-B 新增：model 型外形默认动画接线（与 sprite 路线 AttachDefaultAnimation/RegisterDefaultClips
        // 同一套职责划分，见两者判断记录）。
        // --------------------------------------------------------------

        /// <summary>与 <see cref="AttachDefaultAnimation"/> 同一职责的 model 型版本：登记默认剪辑表
        /// （见 <see cref="RegisterDefaultModelClips"/>）、把该实体登记进 <see cref="_modelViewsByEntity"/>
        /// 供 <see cref="EnsureAnimClipResolver"/> 的 <c>playClip</c> 委托路由、懒构造全局单例
        /// <see cref="AnimClipResolver"/>（与 sprite 路线共用同一个实例——<see cref="AnimStateMachine"/>
        /// 本就是跨实体共享的全局状态表，见 sprite 路线同名判断记录，不需要为 model 型另建一份）。
        /// <para>
        /// 判断记录（H5b 根治，游戏侧复核发现 1"model 路线没有完成回调"，取代此前"不接 OnComplete
        /// 回调解 Attack/Hit 终态锁"的已知简化）：sprite 路线的 <see cref="UnityFrameAnimPlayer.OnComplete"/>
        /// 是"序列帧播放器自己知道一条非循环剪辑何时播完"；model 路线现在也有对等的完成信号——
        /// <see cref="IRenderer3D.OnAnimEvent"/> 通道会在具体实现（<see cref="Adapter.Unity.EngineAdapter.UnityRenderer3D"/>）
        /// 侦测到一次 <c>PlayAnim(loop: false)</c> 自然播放完成时，以 <see cref="ModelCharacterRig.AnimFinishedEventId"/>
        /// 为 <c>eventId</c> 触发一次（见该常量判断记录——这是 <see cref="IRenderer3D"/> 追加的契约
        /// 义务，不是可选能力）。本方法因此直接订阅 <see cref="_renderer3D"/>.<see cref="IRenderer3D.OnAnimEvent"/>
        /// （不经 <see cref="ModelCharacterRig"/> 中转——该类型已经把 <c>HitFrameReached</c> 一类事件
        /// 收敛进 <see cref="ICharacterRig"/> 契约，但"完成回调"只是装配层内部接线细节，不需要放大成
        /// 契约成员，同 sprite 路线 <c>player.OnComplete(...)</c> 直接在本类型接线、不经
        /// <see cref="SpriteCharacterRig"/> 中转的一贯做法），接回
        /// <see cref="AnimStateMachine.NotifyTransientStateFinished"/>，与 sprite 路线接的是同一个
        /// 全局单例状态机、同一套"以回调触发那一刻状态机记录的当前状态"作为 <c>finishedState</c> 的
        /// 判断记录（见 <see cref="AttachDefaultAnimation"/> 对应段落）。命中帧
        /// （<see cref="ModelCharacterRig.HitFrameReached"/>）不受影响——命中帧与完成事件是
        /// <see cref="IRenderer3D.OnAnimEvent"/> 同一通道上两个不同的 <c>eventId</c>，互不干扰。
        /// </para>
        /// </summary>
        private void AttachDefaultModelAnimation(UnityModelView view, Core.Foundation.DisplayInfo.DisplayInfo info, Id entityId)
        {
            if (_bus == null)
            {
                return;
            }

            var clips = RegisterDefaultModelClips(info);
            _modelViewsByEntity[entityId] = view;
            _animClipsByEntity[entityId] = clips;

            EnsureAnimClipResolver();

            // 见本方法判断记录：_renderer3D 在走到这里之前必然非空（CreateView 只在
            // info.Kind == DisplayKind.Model && _renderer3D != null 这一分支才会构造 UnityModelView
            // 并调用本方法，见该方法判断记录）；_animStateMachine 在 EnsureAnimClipResolver 之后必已
            // 构造完成，同 AttachDefaultAnimation 同款判断记录，不会是 null。
            var stateMachine = _animStateMachine!;
            _renderer3D!.OnAnimEvent(view.EngineHandle, (handle, eventId) =>
            {
                if (eventId == ModelCharacterRig.AnimFinishedEventId)
                {
                    stateMachine.NotifyTransientStateFinished(entityId, stateMachine.GetState(entityId));
                }
            });
        }

        /// <summary>解析 <c>DisplayInfo.Model.AnimSetRef</c> 指向的 <c>display.anim_set</c> 行（model
        /// 型专属字段，直接可用——不像 sprite 路线需要"按 display.map 行 id 最后一段猜测约定 id"，见
        /// <see cref="TryResolveAnimSet"/> 判断记录；model 型 <c>ModelInfo.AnimSetRef</c> 本就是
        /// 显式声明的引用，不需要猜测），返回"剪辑名 -> resource_ref"表供
        /// <see cref="AnimClipResolver"/> 查表，并顺带触发每条剪辑的关键帧事件注册（见
        /// <see cref="RegisterModelClipEvents"/>）。数据集没有该 <c>_dataRegistry</c>、查不到该行，或
        /// 该行没有声明 <c>clips</c> 字段时返回空表——不抛异常，同表现层一贯宽容策略；查不到某个具体
        /// 状态时 <see cref="AnimClipResolver"/> 自然跳过那一次状态切换的播放，不特殊处理。</summary>
        private IReadOnlyDictionary<string, Id> RegisterDefaultModelClips(Core.Foundation.DisplayInfo.DisplayInfo info)
        {
            var result = new Dictionary<string, Id>(StringComparer.Ordinal);
            if (_dataRegistry == null || info.Model == null)
            {
                return result;
            }

            var record = _dataRegistry.Get("display.anim_set", info.Model.AnimSetRef);
            if (record == null)
            {
                return result;
            }

            var animSet = Core.Foundation.DisplayInfo.AnimSetDef.FromRecord(record);
            foreach (var kv in animSet.Clips)
            {
                result[kv.Key] = kv.Value.ResourceRef;
                RegisterModelClipEvents(kv.Value);
            }
            return result;
        }

        /// <summary>ADR-0017 决策 c 落地（model 型一侧）：把 <paramref name="clipDef"/>.<c>Events</c>
        /// （<c>display.anim_set.clips[*].events</c> 数据）数据驱动地写回该剪辑对应的 Unity
        /// <see cref="AnimationClip"/> 资产的 <see cref="AnimationClip.events"/>（运行期可写属性，不是
        /// <c>UnityEditor.AnimationUtility</c> 编辑器专属 API），命中帧一律用
        /// <see cref="Adapter.Unity.EngineAdapter.UnityRenderer3D.AnimEventFunctionName"/> 函数名 +
        /// 裸事件名 String Parameter（见该类型判断记录，<c>"hit_frame"</c> 经
        /// <see cref="Adapter.Unity.EngineAdapter.UnityRenderer3D.RaiseAnimEvent"/> 换算后与
        /// <see cref="Presentation.Render.ModelCharacterRig.HitFrameEventId"/> 逐字相等）。
        /// <para>
        /// 判断记录（为什么直接改资产对象的运行期内存状态就能让 Animator 生效）：
        /// <see cref="Resources.Load{T}(string)"/> 对同一路径返回的是 Unity 内部资产缓存的同一个对象
        /// 实例——本方法与 <see cref="Adapter.Unity.EngineAdapter.UnityRenderer3D"/> 播放该剪辑时
        /// Animator 内部引用的是同一个 <see cref="AnimationClip"/> 对象，因此这里设置的 <c>events</c>
        /// 会在下一次该状态被播放时生效，不需要额外的"通知 Animator 重新加载"步骤；本类型因此只需要在
        /// 该剪辑第一次被某个实体引用时设置一次（<see cref="_registeredModelClipEvents"/> 去重），
        /// 之后同一份资产被其它实体复用时事件已经生效，不需要重复设置。占位内容（见
        /// Editor/GeneratePlaceholderModelAssets.cs）额外在美术资产里预先烘焙了同一个事件作为
        /// 双重覆盖，即便某个具体游戏后续替换掉这条数据驱动注册路径，占位内容仍然自带可用的命中帧
        /// 事件（见该脚本判断记录）。
        /// </para>
        /// </summary>
        private void RegisterModelClipEvents(Core.Foundation.DisplayInfo.AnimClipDef clipDef)
        {
            if (clipDef.Events.Count == 0 || !_registeredModelClipEvents.Add(clipDef.ResourceRef))
            {
                return;
            }

            var clip = Resources.Load<AnimationClip>(
                Adapter.Unity.EngineAdapter.UnityResourceLoader.ResolveAnimClipResourcesPath(clipDef.ResourceRef));
            if (clip == null)
            {
                return;
            }

            var events = new AnimationEvent[clipDef.Events.Count];
            for (var i = 0; i < clipDef.Events.Count; i++)
            {
                events[i] = new AnimationEvent
                {
                    time = (float)(clipDef.Events[i].TimePct * clip.length),
                    functionName = Adapter.Unity.EngineAdapter.UnityRenderer3D.AnimEventFunctionName,
                    stringParameter = clipDef.Events[i].Name,
                };
            }
            clip.events = events;
        }
    }
}

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
using Presentation.Common;
using Presentation.Render;
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
        private readonly List<IView> _created = new List<IView>();
        private readonly HashSet<string> _warnedMissingDisplay = new HashSet<string>();
        private readonly HashSet<string> _warnedAnimDegraded = new HashSet<string>();

        // 外部审核阻塞项 3 收口（见 architecture/落地计划/audit-20260907/followup-2026-09-07.md
        // "外部审核阻塞项处理"一节）：默认动画接线状态——entityId -> 已挂接的播放器/该实体的剪辑表，
        // 供下方懒构造的单例 AnimClipResolver 的 playClip/defaultClipsForEntity 两个委托按 entityId
        // 路由（见该类型构造函数参数注释）。AnimStateMachine/AnimClipResolver 是全局单例（跨全部
        // 实体共享同一份状态机与解析器，而不是每个 View 各建一份）——AnimStateMachine 本身就是按
        // entityId 分别记账的全局状态表（见该类型注释），重复构造多份只会让多份状态机各自收到同一批
        // 事件、重复计算，没有任何好处。
        private readonly Dictionary<Id, UnityFrameAnimPlayer> _animPlayersByEntity = new Dictionary<Id, UnityFrameAnimPlayer>();
        private readonly Dictionary<Id, IReadOnlyDictionary<string, Id>> _animClipsByEntity = new Dictionary<Id, IReadOnlyDictionary<string, Id>>();
        private AnimStateMachine? _animStateMachine;
        private AnimClipResolver? _animClipResolver;

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
        /// </summary>
        public UnityViewFactory(
            IRenderer2D renderer2D,
            IRenderConventionHost conventions,
            IDisplayInfoRegistry displayInfo,
            IResourceLoader resourceLoader,
            IEventBus? bus = null,
            IDataRegistryView? dataRegistry = null)
        {
            _renderer2D = renderer2D ?? throw new ArgumentNullException(nameof(renderer2D));
            _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
            _bus = bus;
            _dataRegistry = dataRegistry;
        }

        /// <summary>本工厂迄今创建过的全部 View，只读快照（诊断/测试用）。</summary>
        public IReadOnlyList<IView> CreatedViews => _created;

        public IView CreateView(ViewKind kind, Id displayId, Id entityId)
        {
            var info = _displayInfo.Lookup(displayId);
            IView view;
            if (info == null || info.Kind != DisplayKind.Sprite)
            {
                if (_warnedMissingDisplay.Add(displayId.Value))
                {
                    Debug.LogWarning($"[UnityViewFactory] displayId \"{displayId}\" 没有 kind=sprite 的 DisplayInfo，退化为空视图（不渲染）：kind={kind}");
                }
                view = new NullView();
            }
            else
            {
                var spriteView = new UnitySpriteView(_renderer2D, _conventions, info, _resourceLoader);
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

            // 判断记录：GetSpriteRoot 是 UnityRenderer2D 的具体类型方法，不属于 IRenderer2D 契约本身
            // （同类还有 UnityResourceLoader.TryGetSprite 一类"引擎实现之间的内部协作方法，不算契约
            // 违反"，见本文件类型顶部判断记录）；_renderer2D 字段按契约只声明为 IRenderer2D，这里用
            // is 模式防御性向下转型（同 GameplayAssembly 多处 "Carriers.Units is WorldUnitAccess"
            // 的一贯写法），生产装配传入的恒为 UnityRenderer2D，不是该具体类型时（测试替身/未来
            // 实现变化）静默跳过，不抛异常。
            if (!(_renderer2D is Adapter.Unity.EngineAdapter.UnityRenderer2D concreteRenderer))
            {
                return;
            }

            var root = concreteRenderer.GetSpriteRoot(view.EngineHandle);
            if (root == null)
            {
                // 防御性判断：真实 UnityRenderer2D 在 UnitySpriteView 构造完成后必定已经建好精灵根
                // 节点（见 AnimationLayerTests 一贯做法——构造后立即调用 GetSpriteRoot 不为
                // null），这里只是防御未来实现变化，不抛异常。
                return;
            }

            var player = root.AddComponent<UnityFrameAnimPlayer>();
            var clips = RegisterDefaultClips(player, info);
            view.AttachFrameAnimPlayer(player);

            _animPlayersByEntity[entityId] = player;
            _animClipsByEntity[entityId] = clips;

            EnsureAnimClipResolver();
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
        /// 真正的角色序列帧资源，见 <see cref="AnimClipResolver"/> 与 <see cref="AnimSetRecordParser"/>
        /// 类型顶部判断记录），均逐状态退化为"单帧剪辑"（复用同一张 1x1 占位帧，见
        /// <see cref="FallbackFrame"/>）——不抛异常，只在每个 (displayId, 状态) 组合首次退化时记一条
        /// 诊断（<see cref="_warnedAnimDegraded"/> 去重，避免同一实体反复创建/销毁刷屏）。
        /// </para>
        /// </summary>
        private IReadOnlyDictionary<string, Id> RegisterDefaultClips(UnityFrameAnimPlayer player, Core.Foundation.DisplayInfo.DisplayInfo info)
        {
            var animSetClips = TryResolveAnimSetClips(info.Id);
            var result = new Dictionary<string, Id>(StringComparer.Ordinal);

            for (var i = 0; i < DefaultAnimStateKeys.Length; i++)
            {
                var stateKey = DefaultAnimStateKeys[i];
                var clipId = new Id($"anim.default.{info.Id.Value}.{stateKey}");

                if (animSetClips != null && animSetClips.TryGetValue(stateKey, out var resourceRef) &&
                    _resourceLoader is Adapter.Unity.EngineAdapter.UnityResourceLoader unityLoader &&
                    unityLoader.TryGetEffect(resourceRef, out var effect))
                {
                    player.RegisterClipFromEffect(clipId, effect);
                }
                else
                {
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

        private IReadOnlyDictionary<string, Id>? TryResolveAnimSetClips(Id displayMapId)
        {
            if (_dataRegistry == null)
            {
                return null;
            }

            var lastDot = displayMapId.Value.LastIndexOf('.');
            var lastSegment = lastDot < 0 ? displayMapId.Value : displayMapId.Value.Substring(lastDot + 1);
            var animSetId = new Id("display.anim_set." + lastSegment);

            var record = _dataRegistry.Get("display.anim_set", animSetId);
            return record == null ? null : AnimSetRecordParser.ParseClips(record);
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
                        player.Play(clipId, loop, speed);
                    }
                });
        }

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
        }
    }
}

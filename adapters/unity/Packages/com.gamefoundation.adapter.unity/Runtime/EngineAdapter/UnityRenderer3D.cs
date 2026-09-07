#nullable enable
// UnityRenderer3D：IRenderer3D 的 Unity 引擎实现（W6-B 收口，取代 W3b～H5 阶段"整体声明降级"的
// 占位实现——ADR-0017 决策 b 收紧后，model 型外形的默认路线是框架职责，本引擎适配层现在提供真实
// 三维渲染实现，见 architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md）。
//
// 判断记录（三维放置的坐标换算，PR130-01 根治，取代此前 (X, height, Y) 的独立约定）：本类型此前
// 把逻辑 (planePos.X, height, planePos.Y) 直接写成 Unity 世界 (X, height, Z)——把 planePos.Y（"地面
// 平面"逻辑坐标）映射到 Unity 世界 Z 轴（相机固定沿该轴取景，深度轴上的位移不改变屏幕投影结果，见
// UnityCamera.cs 类型顶部判断记录"相机保持正交投影、镜头朝向固定沿 Z 轴看向 XY 平面"），height 映射
// 到 Unity 世界 Y 轴。这与 sprite/相机共用的既有平面约定不是同一套：UnityRenderer2D.SetTransform 把
// planePos 直接映射到 Unity (X, Y)、height 经 PixelsPerUnit 换算后只平移一个子物体（不改变
// sortY/根物体位置，见该方法判断记录）；UnityCamera.WorldToScreen 据此以 (planePos.X, planePos.Y +
// height, 0) 求屏幕投影。旧约定下 model 与 sprite 分别落在两个不同的世界平面：model 沿 planePos.Y
// 变化的位移落在相机取景轴上（对屏幕投影没有可观察影响），height 变化反而落在屏幕纵轴上（本该由
// planePos.Y 决定的屏幕位置被 height 顶替）——同一份 planePos/height 数值，sprite 与 model 呈现出的
// 屏幕位置完全对不上，地图点击反投影（经 UnityCamera.ScreenToWorld，固定与 Z=0 平面求交）命中的也
// 不是视觉上看到的 model。
//
// 本类型现改为与 UnityRenderer2D/UnityCamera 完全同一套换算（"模型本体在该平面上用旋转/朝向表达"，
// 不再借用第三根世界坐标轴表达深度）：
//   锚点 Root（ModelInstance.Root，供 SetPlacement 的 planePos/facing/scale 与影子挂接使用）
//     世界坐标 = (planePos.X, planePos.Y, 0)——与 UnityRenderer2D.SetTransform 的 Root
//     localPosition 逐字同一套换算，Z 固定 0（同 UnityCamera"地面平面固定为世界 Z = 0"判断记录）。
//   朝向：Y 轴欧拉角（度）= -facing（弧度）× (180/π)，换算理由不变（见下方"朝向换算"判断记录）——
//     facing 绕 Unity Y 轴旋转，Y 轴本身在新约定下正是"屏幕纵轴/深度共用轴"，旋转轴与位移轴重合时
//     该轴上的坐标分量不受旋转影响（数学上 (0, y, 0) 绕 Y 轴旋转后仍是 (0, y, 0)），因此下面的
//     height 子物体偏移不会被朝向旋转扰动。
//   高度 VisualRoot（ModelInstance.VisualRoot，Root 的子物体，承载实际实例化出的可见内容——真实
//     预制体或占位模型，见"资源缺失降级"判断记录）：localPosition = (0, height, 0)，与
//     UnityRenderer2D.SetTransform 把 height 只平移 LayersRoot 子物体（不改变 Root 位置/sortY）
//     同一套结构；Root 的 localScale 统一缩放全部子物体（含本偏移），与 sprite 路线"Root.localScale
//     一并缩放整个精灵实例，包括影子"同一惯例（见 UnityRenderer2D.SetShadow 判断记录），因此非
//     单位缩放下 height 的世界位移量随 scale 等比例变化——这与 sprite 路线是同一种已知简化（04/09
//     均未把 ICamera.WorldToScreen 的 height 参数定义为"随 scale 缩放"，该接口方法本身也不接受
//     scale 参数，属于两条路线共同持有的既有简化，不是本次改动新引入的差异）。
//   sortY 不参与任何实际渲染调用，语义不变（见 ModelInstance.LastSortY 字段注释）。
// 这一改法使 model 与 sprite 对同一份 planePos/height/facing 落在同一个 Unity 世界平面上：
// ICamera.WorldToScreen(planePos, height) 与该模型实际渲染位置经 Camera.WorldToScreenPoint 得到的
// 屏幕坐标严格相等（scale = 1 时），地图点击反投影命中的正是视觉上看到的那个 model。
//
// 判断记录（PR130-08 根治，随上一条一并解决）：09 第 3.4 节"影子贴地、不随 height 位移"——旧实现
// 把 BlobShadow 挂在 ModelInstance.Root 下、height 又直接写进 Root 的 Y 轴，二者耦合导致影子随角色
// 一起被 height 抬离地面。改法本身不需要额外处理：BlobShadow 依旧挂在 Root 下（不是 VisualRoot），
// 但 height 现在只写入 VisualRoot 的局部偏移，Root 本身只由 planePos 决定——影子因此随 Root 一起
// 锚定在地面逻辑坐标，天然不随 height 位移，与 UnityRenderer2D.SetShadow 影子挂在 Root（不是
// LayersRoot）而不受 height 影响同一套结构。
//
// 判断记录（PR130-05 根治，资源缺失降级——取代此前"资源缺失就是配置错误，应该尽早暴露"的立场）：
// ADR-0017 决策 1 明确"首次引用者 loadAsync、renderer 只消费已加载/占位资源、不隐式加载、不抛"。
// CreateModelInstance 契约本身仍是同步的（09/02 均未把它列为异步 API），无法阻塞等待
// IResourceLoader.LoadAsync 走完 Tick 排队；本类型因此按以下顺序解析实际要实例化的可见内容
// （见 TryResolvePrefab/CreatePlaceholderVisualInstance/RequestModelLoadAndSwap）：
//   1. 经 UnityResourceLoader.TryGetModelPrefab 查已加载缓存（调用方已经先经
//      IResourceLoader.LoadAsync(modelId, ResourceKind.Model, ...) 预热过，或本类型自己此前已经
//      发起过加载并完成）；
//   2. 未命中时退回同步 Resources.Load（与 UnityResourceLoader.FinishModelLoad 内部调用同一个 API、
//      同一条路径约定，只是不经过 LoadCallback 排队——这一步不算"隐式加载资源系统之外的东西"，只是
//      直接读取已经存在于 Resources 目录下的资产，Resources.Load 本身在主线程调用总是同步完成）；
//   3. 两条路径都找不到（预制体确实不存在于 Resources/GameFoundation/models/ 下）时：不再抛异常
//      中断 View 创建——记一次诊断（按 modelId 去重，不刷屏），落地一个占位可见内容（优先复用内置
//      占位模型 "model.placeholder_biped"；连它都取不到时兜底一个不依赖任何 Resources 资产的内建
//      几何体，保证任何环境下都不会中断），同时经 IResourceLoader.LoadAsync 发起一次真正的异步加载
//      （同一 modelId 被多个实例共同引用时只发起一次，见 _pendingModelLoadRequested 判断记录），
//      加载成功后把全部等待中的实例原地替换为真实内容（同一句柄不变，见 AttachVisual/
//      RequestModelLoadAndSwap 判断记录），失败则保持占位、记一次诊断、不重试——与
//      UnityViewFactory.RequestAnimClipUpgrade/_pendingAnimResourceLoads 同一套既有惯例。
//
// 判断记录（PlayAnim：Animator CrossFadeInFixedTime 优先，Animation 组件兜底）：状态名＝clipId 去掉
// 类别前缀后的末段（与 UnityViewFactory 默认剪辑登记同一套"resource_ref 末段＝可播放的具体名字"
// 惯例，见该类型判断记录）；先查 Animator（若预制体带 Animator 组件）在任一层是否存在同名状态
// （Animator.HasState 逐层探测，找不到时不调用 CrossFadeInFixedTime——该方法对不存在的状态名只是
// 静默不生效并不总保证不产生 Console 警告，本类型选择显式探测后再决定要不要调）；不存在时兜底走
// UnityEngine.Animation（legacy）组件——经 UnityResourceLoader.ResolveAnimClipResourcesPath 约定
// 路径 Resources.Load&lt;AnimationClip&gt; 取剪辑，AddClip+Play。选择"Animator 优先、Animation
// 兜底"而不是反过来：本迭代的占位模型统一走 Animator（Editor/GeneratePlaceholderModelAssets.cs
// 生成的 AnimatorController，见该脚本），兜底路径只覆盖"游戏层提供了没有对应 Animator 状态的模型
// /剪辑"这一更少见的场景，不要求每个模型都必须挂 AnimatorController。
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;
using UnityEngine.Rendering;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityRenderer3D : IRenderer3D
    {
        /// <summary>W6-B 新增：Unity AnimationEvent 统一约定的接收方法名（见
        /// <see cref="ModelAnimEventRelay.OnAnimEvent"/>）——占位内容与具体游戏内容制作 AnimationClip
        /// 时，事件的 Function 字段一律填本字符串，事件名本身经 String Parameter 传递（不是把每个
        /// 事件名各自建一个 Unity 方法），使任意事件名都能统一转发到
        /// <see cref="AnimEventCallback"/>，不需要为每个新事件名改引擎适配层代码。</summary>
        public const string AnimEventFunctionName = "OnAnimEvent";

        /// <summary>W6-B 新增：<see cref="AnimEventCallback"/> 的 <c>eventId</c> 参数要求点分 <see cref="Id"/>
        /// 格式（见 <see cref="IRenderer3D.OnAnimEvent"/> 契约注释），而 Unity AnimationEvent 的
        /// String Parameter 承载的是裸事件名（如 <c>"hit_frame"</c>，见
        /// <see cref="Core.Foundation.DisplayInfo.AnimClipEventSpec.Name"/>）；本类型统一给裸事件名
        /// 加上 <c>"anim_event."</c> 域前缀完成换算——<c>"hit_frame"</c> 换算结果
        /// <c>"anim_event.hit_frame"</c> 与 <see cref="Presentation.Render.ModelCharacterRig.HitFrameEventId"/>
        /// 逐字相等，命中帧事件因此自动对齐，不需要为它单独特判。</summary>
        public const string AnimEventDomainPrefix = "anim_event.";

        /// <summary>H5b 根治新增：<see cref="ModelCharacterRig.AnimFinishedEventId"/> 换算成裸事件名
        /// （去掉 <see cref="AnimEventDomainPrefix"/> 域前缀）——<see cref="RaiseAnimEvent"/> 统一接受
        /// 裸事件名再加前缀，本类型内部驱动"完成"事件复用同一条通路，不另开一条直发 <see cref="Id"/>
        /// 的旁路。</summary>
        private const string AnimFinishedBareEventName = "finished";

        /// <summary>PR130-05 新增：资源缺失时优先复用的内置占位模型 id（占位内容由
        /// <c>Editor/GeneratePlaceholderModelAssets.cs</c> 一次性生成并提交，见该脚本与包 README
        /// "资源路径约定"一节）。</summary>
        private static readonly Id BuiltinPlaceholderModelId = new Id("model.placeholder_biped");

        /// <summary>H5b 根治新增：单个模型实例驱动 <see cref="PlayAnim"/> 的方式——供 <see cref="Tick"/>
        /// 判断该按哪条路径检测"非循环剪辑自然播放完成"（见 <see cref="ModelCharacterRig.AnimFinishedEventId"/>
        /// 判断记录）。</summary>
        private enum AnimDriveMode
        {
            None,
            Animator,
            Legacy,
        }

        private sealed class ModelInstance
        {
            /// <summary>锚点根节点：只承载 <see cref="SetPlacement"/> 的 planePos/facing/scale 与
            /// <see cref="BlobShadow"/>，不承载 height 偏移——见类型顶部"三维放置的坐标换算"判断
            /// 记录，与 <see cref="UnityRenderer2D"/> 的 <c>SpriteInstance.Root</c> 同一职责划分。</summary>
            public GameObject Root = null!;

            /// <summary>Root 下的可见内容子物体：承载 height 偏移，实际实例化出来的预制体（真实内容
            /// 或占位模型，见类型顶部"资源缺失降级"判断记录）挂在这里——与
            /// <see cref="UnityRenderer2D"/> 的 <c>SpriteInstance.LayersRoot</c> 同一职责划分。资源
            /// 加载完成后原地替换（<see cref="AttachVisual"/>）时整体销毁重建，<see cref="Root"/> 与
            /// <see cref="BlobShadow"/> 不受影响。</summary>
            public Transform VisualRoot = null!;

            public Animator? Animator;
            public UnityEngine.Animation? LegacyAnimation;
            public readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
            public readonly List<AnimEventCallback> AnimEventCallbacks = new List<AnimEventCallback>();
            public GameObject? BlobShadow;
            public ShadowMode Shadow = ShadowMode.None;

            // 见类型顶部"三维放置的坐标换算"判断记录：sortY 只存不用，保留字段只为诊断/未来扩展。
            public double LastSortY;

            /// <summary>最近一次 <see cref="SetPlacement"/> 写入的 height，供 <see cref="AttachVisual"/>
            /// 在资源加载完成原地替换时把新视觉内容摆到与替换前一致的高度偏移（PR130-05）。</summary>
            public double HeightOffset;

            // H5b 根治新增（游戏侧复核发现 1）：当前一次 PlayAnim 的驱动方式/目标状态或剪辑名/是否
            // 循环/是否已经通知过完成——供 Tick() 逐实例检测"非循环剪辑自然播放完成"，见该方法判断
            // 记录。FinishNotified 初始为 true（尚未播放过任何剪辑，没有"未完成的播放"需要检测）。
            public AnimDriveMode Drive = AnimDriveMode.None;
            public string? CurrentStateName;
            public string? CurrentClipName;
            public bool CurrentClipLoop = true;
            public bool FinishNotified = true;

            /// <summary>PR130-05 新增：最近一次 <see cref="PlayAnim"/> 的完整调用参数——资源加载完成
            /// 原地替换视觉内容后，<see cref="AttachVisual"/> 据此对新内容重放同一条命令，保持替换前后
            /// 视觉连续（不追求逐帧进度对齐，只保证"新内容也在播正确的剪辑"，同 09 第 1 节表现层一贯
            /// 宽容策略）。null 表示尚未调用过 <see cref="PlayAnim"/>。</summary>
            public (Id ClipId, bool Loop, double Speed, double BlendSeconds)? LastPlayAnimCall;

            /// <summary>PR130-05 新增：已登记的槽位网格覆盖（<see cref="SetSlotMesh"/>），原地替换视觉
            /// 内容后据此逐条重放。</summary>
            public readonly Dictionary<Id, Id?> SlotMeshes = new Dictionary<Id, Id?>();

            /// <summary>PR130-05 新增：已登记的材质参数（<see cref="SetMaterialParam"/>），原地替换
            /// 视觉内容后据此逐条重放。</summary>
            public readonly Dictionary<string, double> MaterialParams = new Dictionary<string, double>(StringComparer.Ordinal);

            /// <summary>PR130-05 新增：本实例当前是否正在展示占位内容（尚未被真实资源原地替换）——
            /// 供测试/诊断查询，不属于 <see cref="IRenderer3D"/> 契约本身。</summary>
            public bool IsPlaceholder;
        }

        private readonly Transform _root;
        private readonly UnityResourceLoader _resourceLoader;
        private readonly Dictionary<int, ModelInstance> _instances = new Dictionary<int, ModelInstance>();
        private int _nextHandle = 1;

        /// <summary>W6-B 新增：Animation（legacy）兜底路径按剪辑资源引用 id 缓存已加载的
        /// <see cref="AnimationClip"/>，避免同一剪辑被多个模型实例反复 <c>Resources.Load</c>。</summary>
        private readonly Dictionary<Id, AnimationClip> _legacyClipCache = new Dictionary<Id, AnimationClip>();

        /// <summary>PR130-05 新增：已经记过一次"找不到模型资源"诊断的 modelId 去重集合（按
        /// <see cref="Id.Value"/> 字符串去重，同 <c>UnityViewFactory._warnedMissingDisplay</c> 一贯
        /// 惯例），避免同一个缺失资源被多个实例反复引用时刷屏。</summary>
        private readonly HashSet<string> _missingModelWarned = new HashSet<string>();

        /// <summary>PR130-05 新增：已经发起过一次 <see cref="IResourceLoader.LoadAsync"/> 的缺失
        /// modelId 去重集合——同一资源被多个实例共同引用时只发起一次加载，同
        /// <c>UnityViewFactory._pendingAnimResourceLoads</c> 一贯惯例（含"此后永远不再移除，加载
        /// 失败也不重试"）。</summary>
        private readonly HashSet<Id> _pendingModelLoadRequested = new HashSet<Id>();

        /// <summary>PR130-05 新增：modelId -&gt; 正在等待该资源加载完成后原地替换的实例句柄值列表；
        /// 加载完成时对列表中仍存活的实例逐一调用 <see cref="AttachVisual"/> 替换，已销毁的实例
        /// （<see cref="_instances"/> 已不含该句柄）直接跳过，同
        /// <c>UnityViewFactory._pendingAnimClipWaiters</c> 一贯惯例。</summary>
        private readonly Dictionary<Id, List<int>> _pendingModelSwapWaiters = new Dictionary<Id, List<int>>();

        public UnityRenderer3D(Transform root, UnityResourceLoader resourceLoader)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        }

        public ModelHandle CreateModelInstance(Id modelId)
        {
            var handle = _nextHandle++;

            var anchor = new GameObject($"Model_{handle}_{modelId.Value}");
            anchor.transform.SetParent(_root, worldPositionStays: false);

            var instance = new ModelInstance { Root = anchor };
            _instances[handle] = instance;

            if (TryResolvePrefab(modelId, out var prefab))
            {
                AttachVisual(instance, handle, UnityEngine.Object.Instantiate(prefab));
                return new ModelHandle(handle);
            }

            // 见类型顶部"资源缺失降级"判断记录：不抛异常，落地占位内容并发起真正的异步加载。
            if (_missingModelWarned.Add(modelId.Value))
            {
                var path = UnityResourceLoader.ResolveModelResourcesPath(modelId);
                Debug.LogWarning(
                    $"[UnityRenderer3D] 找不到模型资源 \"{modelId}\"（约定路径 Resources/{path}）：" +
                    "先使用占位模型呈现并发起异步加载，加载完成后原地替换为真实内容；请确认该预制体已放在" +
                    " adapters/unity 包的 Assets/Resources/GameFoundation/models/ 目录下。");
            }

            AttachVisual(instance, handle, CreatePlaceholderVisualInstance());
            instance.IsPlaceholder = true;

            if (!_pendingModelSwapWaiters.TryGetValue(modelId, out var waiters))
            {
                waiters = new List<int>();
                _pendingModelSwapWaiters[modelId] = waiters;
            }
            waiters.Add(handle);

            RequestModelLoadAndSwap(modelId);

            return new ModelHandle(handle);
        }

        /// <summary>见类型顶部"资源缺失降级"判断记录第 1/2 步：先查已加载缓存，未命中时退回同步
        /// <c>Resources.Load</c>。</summary>
        private bool TryResolvePrefab(Id modelId, out GameObject prefab)
        {
            if (_resourceLoader.TryGetModelPrefab(modelId, out prefab!))
            {
                return true;
            }

            var path = UnityResourceLoader.ResolveModelResourcesPath(modelId);
            prefab = Resources.Load<GameObject>(path);
            return prefab != null;
        }

        /// <summary>见类型顶部"资源缺失降级"判断记录第 3 步：优先复用内置占位模型
        /// <see cref="BuiltinPlaceholderModelId"/>，连它都取不到（隔离测试工程/尚未同步占位资产的
        /// 极端场景）时兜底一个不依赖任何 Resources 资产的内建几何体，保证任何环境下都不中断。</summary>
        private GameObject CreatePlaceholderVisualInstance()
        {
            if (TryResolvePrefab(BuiltinPlaceholderModelId, out var placeholderPrefab))
            {
                return UnityEngine.Object.Instantiate(placeholderPrefab);
            }

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "BuiltinPlaceholderCapsule";
            return capsule;
        }

        /// <summary>见类型顶部"资源缺失降级"判断记录第 3 步：按 modelId 去重发起一次
        /// <see cref="IResourceLoader.LoadAsync"/>，完成后把 <see cref="_pendingModelSwapWaiters"/>
        /// 里登记的全部仍存活实例原地替换为真实内容；失败则记一次诊断并保持占位，不重试。</summary>
        private void RequestModelLoadAndSwap(Id modelId)
        {
            if (!_pendingModelLoadRequested.Add(modelId))
            {
                return;
            }

            _resourceLoader.LoadAsync(modelId, ResourceKind.Model, (loadedId, success) =>
            {
                if (!_pendingModelSwapWaiters.TryGetValue(loadedId, out var waitingHandles))
                {
                    return;
                }
                _pendingModelSwapWaiters.Remove(loadedId);

                if (!success || !_resourceLoader.TryGetModelPrefab(loadedId, out var realPrefab))
                {
                    Debug.LogWarning(
                        $"[UnityRenderer3D] 模型资源 \"{loadedId}\" 异步加载失败，继续使用占位模型（不重试）。");
                    return;
                }

                for (var i = 0; i < waitingHandles.Count; i++)
                {
                    if (!_instances.TryGetValue(waitingHandles[i], out var instance))
                    {
                        // 加载完成前该实例已被销毁（切图/实体销毁），跳过即可，同
                        // UnityViewFactory.RequestAnimClipUpgrade 一贯惯例。
                        continue;
                    }

                    AttachVisual(instance, waitingHandles[i], UnityEngine.Object.Instantiate(realPrefab));
                    instance.IsPlaceholder = false;
                }
            });
        }

        /// <summary>把 <paramref name="visualInstance"/>（已经 <c>Instantiate</c>/<c>CreatePrimitive</c>
        /// 出来的一份实例，不是预制体模板）挂到 <paramref name="instance"/>.<see cref="ModelInstance.Root"/>
        /// 下成为新的 <see cref="ModelInstance.VisualRoot"/>：首次创建时直接挂接；PR130-05 原地替换时
        /// 先销毁旧内容，再重新挂接并重放已登记的 <see cref="ModelInstance.SlotMeshes"/>/
        /// <see cref="ModelInstance.MaterialParams"/>/<see cref="ModelInstance.LastPlayAnimCall"/>，
        /// 保持替换前后已生效的呈现状态与视觉连续（见类型顶部"资源缺失降级"判断记录）。</summary>
        private void AttachVisual(ModelInstance instance, int handle, GameObject visualInstance)
        {
            if (instance.VisualRoot != null)
            {
                UnityEngine.Object.Destroy(instance.VisualRoot.gameObject);
            }

            visualInstance.name = "Visual";
            visualInstance.transform.SetParent(instance.Root.transform, worldPositionStays: false);
            // 见类型顶部"三维放置的坐标换算"判断记录：height 只写在 VisualRoot 的局部 Y。
            visualInstance.transform.localPosition = new Vector3(0f, (float)instance.HeightOffset, 0f);
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;

            instance.VisualRoot = visualInstance.transform;
            instance.Animator = visualInstance.GetComponentInChildren<Animator>();
            instance.LegacyAnimation = null; // 见 PlayAnim 判断记录：legacy 组件按需懒创建于新内容上。

            // AnimationEvent 的 SendMessage 目标是"持有 Animator 组件的那个 GameObject 自身"（Unity
            // 既有行为，不搜索父子层级），中继组件必须挂在同一个 GameObject 上，见类型顶部
            // ModelAnimEventRelay 类型注释。
            if (instance.Animator != null)
            {
                var relay = instance.Animator.gameObject.AddComponent<ModelAnimEventRelay>();
                relay.Bind(this, handle);
            }

            foreach (var kv in instance.SlotMeshes)
            {
                ApplySlotMesh(instance, kv.Key, kv.Value);
            }
            foreach (var kv in instance.MaterialParams)
            {
                ApplyMaterialParam(instance, kv.Key, kv.Value);
            }
            if (instance.LastPlayAnimCall.HasValue)
            {
                var call = instance.LastPlayAnimCall.Value;
                PlayAnimOnInstance(instance, call.ClipId, call.Loop, call.Speed, call.BlendSeconds);
            }
        }

        public void DestroyModelInstance(ModelHandle handle)
        {
            var instance = EnsureAlive(handle);
            UnityEngine.Object.Destroy(instance.Root);
            _instances.Remove(handle.Value);
        }

        public void SetPlacement(ModelHandle handle, Vec2 planePos, double height, double facing, double scale, double sortY)
        {
            var instance = EnsureAlive(handle);

            // 见类型顶部"三维放置的坐标换算"判断记录。
            instance.Root.transform.localPosition = new Vector3((float)planePos.X, (float)planePos.Y, 0f);
            instance.Root.transform.localRotation = Quaternion.Euler(0f, (float)(-facing * Mathf.Rad2Deg), 0f);
            instance.Root.transform.localScale = new Vector3((float)scale, (float)scale, (float)scale);
            instance.LastSortY = sortY;
            instance.HeightOffset = height;

            if (instance.VisualRoot != null)
            {
                var local = instance.VisualRoot.localPosition;
                instance.VisualRoot.localPosition = new Vector3(local.x, (float)height, local.z);
            }
        }

        /// <summary>
        /// 判断记录（H5b 根治，游戏侧复核发现 2"同状态重入不重播"model 一侧）：<c>Animator.CrossFadeInFixedTime</c>
        /// 用于"进入一个此前不是当前状态的目标状态"（含从别的状态切入，享受混合过渡）；但
        /// <see cref="AnimStateMachine.StateRetriggered"/>（见其类型判断记录）驱动的是"目标状态与
        /// Animator 当前正在播放的状态是同一个"这一特殊情形（连续两次普攻，第二次在第一次动画播完前
        /// 到达）——继续调用 CrossFadeInFixedTime 混合到"自己当前所在的状态"在不同 Unity 版本上的
        /// 行为不总是可靠地重启 <c>normalizedTime</c>（部分实现会把它优化成无操作，因为目标状态已经是
        /// 当前状态），达不到"重播一遍完整剪辑"的要求。本方法据此按"这次要播的状态是否与实例当前正在
        /// 驱动的状态相同"分两支：不同（含首次播放）走原有的 <c>CrossFadeInFixedTime</c> 混合过渡；
        /// 相同则改用 <c>Animator.Play(stateName, layer: -1, normalizedTime: 0f)</c>——该重载显式传入
        /// <c>normalizedTime</c> 时是有文档保证的硬切（不管目标状态是不是已经是当前状态，都会立即把
        /// 播放头拨回指定的归一化时间点），牺牲这一次重播的混合过渡，换取"确定性地从头重新播放"这一
        /// 更重要的正确性要求（连击类玩法的美术诉求本就是"每一下都要看到完整的挥击"，不是"丝滑但可能
        /// 播不全"）。
        /// </summary>
        public void PlayAnim(ModelHandle handle, Id clipId, bool loop, double speed, double blendSeconds)
        {
            var instance = EnsureAlive(handle);
            instance.LastPlayAnimCall = (clipId, loop, speed, blendSeconds); // PR130-05：供原地替换后重放。
            PlayAnimOnInstance(instance, clipId, loop, speed, blendSeconds);
        }

        /// <summary>见 <see cref="PlayAnim"/> 判断记录——抽成不更新 <see cref="ModelInstance.LastPlayAnimCall"/>
        /// 的内部版本，供 <see cref="AttachVisual"/> 在原地替换视觉内容后重放最近一次命令时复用，避免
        /// 重放本身又把自己重新记成"最近一次命令"（值不变，语义上也不应该算一次新命令）。</summary>
        private void PlayAnimOnInstance(ModelInstance instance, Id clipId, bool loop, double speed, double blendSeconds)
        {
            var stateName = BareName(clipId);

            if (instance.Animator != null && AnimatorHasState(instance.Animator, stateName))
            {
                instance.Animator.speed = (float)speed;

                var isRetrigger = instance.Drive == AnimDriveMode.Animator && instance.CurrentStateName == stateName;
                if (isRetrigger)
                {
                    instance.Animator.Play(stateName, -1, 0f);
                }
                else
                {
                    instance.Animator.CrossFadeInFixedTime(stateName, (float)Math.Max(blendSeconds, 0.0));
                }

                instance.Drive = AnimDriveMode.Animator;
                instance.CurrentStateName = stateName;
                instance.CurrentClipName = null;
                instance.CurrentClipLoop = loop;
                // 见 Tick() 判断记录：FinishNotified 恒随每次 PlayAnim 调用重置——循环剪辑直接标记
                // "已通知"（Tick 因此永不对它检测），非循环剪辑（含本次重播）标记"未通知"，交给 Tick
                // 检测这一次播放的自然完成。
                instance.FinishNotified = loop;
                return;
            }

            // 见类型顶部判断记录：Animator 没有对应状态时兜底走 Animation（legacy）组件。
            var clip = ResolveLegacyClip(clipId);
            if (clip == null)
            {
                return;
            }

            var animation = instance.LegacyAnimation;
            if (animation == null)
            {
                animation = instance.VisualRoot.gameObject.AddComponent<UnityEngine.Animation>();
                instance.LegacyAnimation = animation;

                var relay = animation.gameObject.GetComponent<ModelAnimEventRelay>();
                if (relay == null)
                {
                    relay = animation.gameObject.AddComponent<ModelAnimEventRelay>();
                    relay.Bind(this, HandleValueOf(instance));
                }
            }

            clip.legacy = true;
            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            if (animation.GetClip(clip.name) == null)
            {
                animation.AddClip(clip, clip.name);
            }

            // 判断记录：UnityEngine.Animation.Play 对"再次播放同一个已在播放的剪辑"的既有行为就是从头
            // 重新播放（内部按 PlayMode.StopSameLayer 停掉同层旧播放状态后重新开始），不需要像 Animator
            // 分支那样额外分两支处理重播——legacy 路径的"重播语义"天然正确。
            animation.Play(clip.name);
            var state = animation[clip.name];
            if (state != null)
            {
                state.speed = (float)speed;
            }

            instance.Drive = AnimDriveMode.Legacy;
            instance.CurrentStateName = null;
            instance.CurrentClipName = clip.name;
            instance.CurrentClipLoop = loop;
            instance.FinishNotified = loop;
        }

        /// <summary>
        /// H5b 根治新增（游戏侧复核发现 1"model 路线没有完成回调"）：由 <c>Adapter.Unity.EngineAdapter.UnityEngineHost.Update</c>
        /// 每帧驱动（同 <see cref="UnityResourceLoader.Tick"/>/<c>UnityCamera.Tick</c> 一贯的"引擎适配层
        /// 实现自己不驱动自己，由宿主组合根统一每帧调用"惯例，见该类型判断记录）：逐实例检测"当前这
        /// 一次 <see cref="PlayAnim"/>（<c>loop: false</c>）播放的剪辑是否已经自然播放完成"，完成时
        /// 经既有 <see cref="RaiseAnimEvent"/> 通路发出一次 <see cref="ModelCharacterRig.AnimFinishedEventId"/>
        /// （<see cref="AnimFinishedBareEventName"/> 经 <see cref="AnimEventDomainPrefix"/> 换算，逐字
        /// 等于该常量）。<see cref="ModelInstance.FinishNotified"/> 保证同一次播放只通知一次（非循环
        /// 剪辑自然播完后 Animator/Animation 都会继续停留在"已完成"状态，若不去重，下一帧的 Tick 会
        /// 反复重新检测到同一个"已完成"信号，反复发出事件）；循环剪辑（<c>CurrentClipLoop == true</c>）
        /// 从不检测——<see cref="PlayAnim"/> 已经把这类实例的 <c>FinishNotified</c> 直接置 true，本方法
        /// 因此天然跳过它们，不需要在这里重复判断 loop 标志。
        /// </summary>
        public void Tick()
        {
            foreach (var kv in _instances)
            {
                var instance = kv.Value;
                if (instance.FinishNotified)
                {
                    continue;
                }

                bool finished;
                switch (instance.Drive)
                {
                    case AnimDriveMode.Animator:
                        finished = instance.Animator != null
                            && IsAnimatorStateFinished(instance.Animator, instance.CurrentStateName);
                        break;
                    case AnimDriveMode.Legacy:
                        finished = instance.LegacyAnimation != null && instance.CurrentClipName != null
                            && !instance.LegacyAnimation.IsPlaying(instance.CurrentClipName);
                        break;
                    default:
                        finished = false;
                        break;
                }

                if (!finished)
                {
                    continue;
                }

                instance.FinishNotified = true;
                RaiseAnimEvent(kv.Key, AnimFinishedBareEventName);
            }
        }

        /// <summary>见 <see cref="Tick"/> 判断记录：<paramref name="stateName"/> 对应的层已经不在
        /// 过渡中（<c>IsInTransition</c> 为假——过渡中的 <c>normalizedTime</c> 含义是过渡本身的进度，
        /// 不是目标状态剪辑的播放进度，不能用来判定剪辑是否播完）且该层当前状态的
        /// <c>shortNameHash</c> 精确等于目标状态、<c>normalizedTime &gt;= 1</c>（该状态的
        /// <c>AnimatorState</c>"Loop Time"应当与传给 <see cref="PlayAnim"/> 的 <c>loop</c> 参数保持
        /// 一致——占位内容按此约定烘焙，见 <c>Adapter.Unity.Editor.GeneratePlaceholderModelAssets</c>；
        /// 具体游戏若不遵守这一约定，非循环 <c>loop: false</c> 但 Animator 状态本身"Loop Time"开着的
        /// 剪辑，<c>normalizedTime</c> 会持续增长永不停留在 1 附近，本方法仍能在恰好越过 1 的那一帧
        /// 检测到"完成"，之后剪辑继续循环播放不受影响——不是本方法需要规避的错误场景）。</summary>
        private static bool IsAnimatorStateFinished(Animator animator, string? stateName)
        {
            if (stateName == null)
            {
                return false;
            }

            var hash = Animator.StringToHash(stateName);
            for (var layer = 0; layer < animator.layerCount; layer++)
            {
                if (animator.IsInTransition(layer))
                {
                    continue;
                }

                var info = animator.GetCurrentAnimatorStateInfo(layer);
                if (info.shortNameHash == hash && info.normalizedTime >= 1f)
                {
                    return true;
                }
            }
            return false;
        }

        public void SetAnimSpeed(ModelHandle handle, double speed)
        {
            var instance = EnsureAlive(handle);
            if (instance.Animator != null)
            {
                instance.Animator.speed = (float)speed;
            }

            if (instance.LegacyAnimation != null)
            {
                foreach (UnityEngine.AnimationState state in instance.LegacyAnimation)
                {
                    state.speed = (float)speed;
                }
            }
        }

        public SubscriptionHandle OnAnimEvent(ModelHandle handle, AnimEventCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var instance = EnsureAlive(handle);

            instance.AnimEventCallbacks.Add(callback);
            return new SubscriptionHandle(() =>
            {
                if (_instances.TryGetValue(handle.Value, out var stillAlive))
                {
                    stillAlive.AnimEventCallbacks.Remove(callback);
                }
            });
        }

        /// <summary>由 <see cref="ModelAnimEventRelay.OnAnimEvent"/> 调用（AnimationEvent 的
        /// SendMessage 落点，见该类型注释）：把裸事件名换算成 <see cref="Id"/>（见类型顶部"AnimEventDomainPrefix"
        /// 判断记录）后通知本次实例登记的全部回调。</summary>
        internal void RaiseAnimEvent(int handleValue, string eventName)
        {
            if (!_instances.TryGetValue(handleValue, out var instance) || string.IsNullOrEmpty(eventName))
            {
                return;
            }

            var eventId = new Id(AnimEventDomainPrefix + eventName);
            var handle = new ModelHandle(handleValue);

            // 快照后再遍历：回调内部可能触发 OnAnimEvent/退订，直接遍历原列表会在枚举期间修改集合。
            var snapshot = instance.AnimEventCallbacks.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i](handle, eventId);
            }
        }

        /// <summary>W6-B 新增：按子对象名查找 <see cref="SkinnedMeshRenderer"/>/<see cref="MeshFilter"/>
        /// 替换网格（<paramref name="meshId"/> 为 null 时卸下——同 <see cref="IRenderer3D.SetSlotMesh"/>
        /// 契约语义）；查不到 <paramref name="slotId"/> 对应的子对象（占位内容/游戏预制体未按约定命名，
        /// 见包 README"资源路径约定"）时静默跳过，不抛异常——槽位换装属于表现层"缺表现资源不阻断游戏"
        /// 的一贯宽容范围（同 <see cref="UnityRenderer2D"/> 资源缺失时的整体宽容立场，与
        /// <see cref="CreateModelInstance"/> 此前"模型本体缺失时严格抛异常"的立场不同——PR130-05 已把
        /// 后者也改为宽容降级，二者现在是同一套宽容立场的两个具体落地）。网格资源经与模型预制体同一套
        /// <see cref="UnityResourceLoader.ResolveModelResourcesPath"/> 约定路径
        /// <c>Resources.Load&lt;Mesh&gt;</c> 取用（判断记录：04/09/14 均未给"网格资源"单独定义路径
        /// 规则，本类型选择复用模型预制体那一套"去类别前缀、点号换下划线"约定与同一个子目录，不额外新增
        /// 子目录——网格与模型本就是同一大类"三维几何资产"，没有必要用不同目录管理两次同一条命名规则）。
        /// PR130-05：登记进 <see cref="ModelInstance.SlotMeshes"/>，供 <see cref="AttachVisual"/> 在
        /// 原地替换视觉内容后重放。</summary>
        public void SetSlotMesh(ModelHandle handle, Id slotId, Id? meshId)
        {
            var instance = EnsureAlive(handle);
            instance.SlotMeshes[slotId] = meshId;
            ApplySlotMesh(instance, slotId, meshId);
        }

        private static void ApplySlotMesh(ModelInstance instance, Id slotId, Id? meshId)
        {
            var slotTransform = FindDeep(instance.VisualRoot, slotId.Value);
            if (slotTransform == null)
            {
                return;
            }

            var skinned = slotTransform.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null)
            {
                skinned.sharedMesh = meshId.HasValue ? ResolveMesh(meshId.Value) : null;
                return;
            }

            var filter = slotTransform.GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = meshId.HasValue ? ResolveMesh(meshId.Value) : null;
            }
        }

        /// <summary>W6-B 新增：按子对象名查找挂点 <see cref="Transform"/>，把
        /// <paramref name="child"/>（另一个已创建的模型实例）挂接为其子物体（局部位置/旋转清零，
        /// 对齐挂点原点）。查不到 <paramref name="socketId"/> 对应的子对象时静默跳过，理由同
        /// <see cref="SetSlotMesh"/>。</summary>
        public void AttachToSocket(ModelHandle handle, Id socketId, ModelHandle child)
        {
            var instance = EnsureAlive(handle);
            var childInstance = EnsureAlive(child);

            var socketTransform = FindDeep(instance.VisualRoot, socketId.Value);
            if (socketTransform == null)
            {
                return;
            }

            childInstance.Root.transform.SetParent(socketTransform, worldPositionStays: false);
            childInstance.Root.transform.localPosition = Vector3.zero;
            childInstance.Root.transform.localRotation = Quaternion.identity;
        }

        /// <summary>把子实例摘回本渲染器的根节点下（不销毁，见 <see cref="IRenderer3D.Detach"/>
        /// 契约注释"只摘不销毁"——销毁由调用方另行调用 <see cref="DestroyModelInstance"/>，同
        /// <see cref="Presentation.Render.ModelCharacterRig.ClearSocket"/> 判断记录"Detach 之后
        /// 紧接着 DestroyModelInstance"）。</summary>
        public void Detach(ModelHandle child)
        {
            var childInstance = EnsureAlive(child);
            childInstance.Root.transform.SetParent(_root, worldPositionStays: true);
        }

        /// <summary>W6-B 新增：经 <see cref="MaterialPropertyBlock"/> 把命名参数广播给实例可见内容下
        /// 全部 <see cref="Renderer"/>（见 <see cref="IRenderer3D.SetMaterialParam"/> 契约注释"参数
        /// 含义由 DisplayInfo 映射决定，本接口不解释参数语义"）——与 <see cref="UnityRenderer2D.SetShaderParam"/>
        /// 对未知参数名的通用兜底分支同一套机制，保证 <see cref="Presentation.Render.ModelCharacterRig"/>
        /// 固定使用的三个参数名（<c>flash_intensity</c>/<c>trail_intensity</c>/<c>fade_alpha</c>）与
        /// sprite 型走同一套命名，便于游戏侧编写通用着色器同时支持两种外形类型。遍历范围限定在
        /// <see cref="ModelInstance.VisualRoot"/>（不是 <see cref="ModelInstance.Root"/>），避免误把
        /// <see cref="ModelInstance.BlobShadow"/> 自己的 <see cref="Renderer"/> 也带上材质参数——影子
        /// 的呈现完全由 <see cref="SetShadow"/> 独立管理。PR130-05：登记进
        /// <see cref="ModelInstance.MaterialParams"/>，供 <see cref="AttachVisual"/> 原地替换视觉内容
        /// 后重放。</summary>
        public void SetMaterialParam(ModelHandle handle, string paramName, double value)
        {
            var instance = EnsureAlive(handle);
            instance.MaterialParams[paramName] = value;
            ApplyMaterialParam(instance, paramName, value);
        }

        private static void ApplyMaterialParam(ModelInstance instance, string paramName, double value)
        {
            var renderers = instance.VisualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(instance.PropertyBlock);
                instance.PropertyBlock.SetFloat(paramName, (float)value);
                renderers[i].SetPropertyBlock(instance.PropertyBlock);
            }
        }

        /// <summary>见 <see cref="ShadowMode"/> 与任务书判断记录——<see cref="ShadowMode.None"/>
        /// 关闭全部渲染器的投影阴影并移除贴地占位影子；<see cref="ShadowMode.Blob"/> 关闭真实投影阴影、
        /// 改用一个贴地占位影子子物体（同 <see cref="UnityRenderer2D.SetShadow"/> 的 Blob 占位精神，
        /// 只是 3D 场景下用一个压扁的 Quad 而不是 SpriteRenderer）；<see cref="ShadowMode.Projected"/>
        /// 打开全部渲染器的真实投影阴影（<see cref="ShadowCastingMode.On"/>）——与 sprite 路线不同，
        /// model 型有真正的三维几何体，可以直接使用 Unity 内建的实时阴影管线，不需要像
        /// <see cref="UnityRenderer2D.SetShadow"/> 那样把 Projected 降级为 Blob。
        /// <para>
        /// PR130-08 根治：<see cref="ModelInstance.BlobShadow"/> 挂在 <see cref="ModelInstance.Root"/>
        /// 下（不是 <see cref="ModelInstance.VisualRoot"/>）——height 只写入 VisualRoot 的局部偏移
        /// （见类型顶部"三维放置的坐标换算"判断记录），影子因此天然锚定在地面逻辑坐标，不随 height
        /// 位移，与 09 第 3.4 节"影子贴地、不随高度位移"一致，也与
        /// <see cref="UnityRenderer2D.SetShadow"/> 影子挂在 Root（不是 LayersRoot）同一套结构。真实
        /// 投影阴影的开关（<c>shadowCastingMode</c>）遍历范围限定在 VisualRoot，理由同
        /// <see cref="SetMaterialParam"/>——不误把影子自己的 Renderer 也算进"可见内容"。
        /// </para>
        /// </summary>
        public void SetShadow(ModelHandle handle, ShadowMode mode)
        {
            var instance = EnsureAlive(handle);
            instance.Shadow = mode;

            var renderers = instance.VisualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            var castMode = mode == ShadowMode.Projected ? ShadowCastingMode.On : ShadowCastingMode.Off;
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = castMode;
            }

            if (mode != ShadowMode.Blob)
            {
                if (instance.BlobShadow != null)
                {
                    UnityEngine.Object.Destroy(instance.BlobShadow);
                    instance.BlobShadow = null;
                }
                return;
            }

            if (instance.BlobShadow == null)
            {
                var blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.Destroy(blob.GetComponent<Collider>());
                blob.name = "BlobShadow";
                blob.transform.SetParent(instance.Root.transform, worldPositionStays: false);
                blob.transform.localPosition = new Vector3(0f, 0.01f, 0f);
                blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                blob.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

                var blobRenderer = blob.GetComponent<Renderer>();
                blobRenderer.shadowCastingMode = ShadowCastingMode.Off;
                blobRenderer.receiveShadows = false;
                var material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 0.5f) };
                blobRenderer.material = material;

                instance.BlobShadow = blob;
            }
        }

        /// <summary>W6-B 新增：供测试/同属引擎适配层的协作代码取回模型实例的锚点根
        /// <see cref="GameObject"/>（不属于 <see cref="IRenderer3D"/> 契约本身，同
        /// <see cref="UnityRenderer2D.GetSpriteRoot"/> 一贯的"引擎实现之间的内部协作方法，不算契约
        /// 违反"惯例）——不含 height 偏移（见类型顶部"三维放置的坐标换算"判断记录），需要含 height
        /// 的可见内容位置请用 <see cref="GetModelVisualRoot"/>。查不到（已销毁/未知句柄）时返回
        /// null。</summary>
        public GameObject? GetModelRoot(ModelHandle handle) =>
            _instances.TryGetValue(handle.Value, out var instance) ? instance.Root : null;

        /// <summary>PR130-01 新增：供测试取回模型实例的可见内容子物体（<see cref="ModelInstance.VisualRoot"/>，
        /// 含 height 偏移），惯例同 <see cref="GetModelRoot"/>——与
        /// <see cref="UnityRenderer2D.GetLayersRoot"/> 是同一职责的 model 型对应方法。查不到时返回
        /// null。</summary>
        public Transform? GetModelVisualRoot(ModelHandle handle) =>
            _instances.TryGetValue(handle.Value, out var instance) ? instance.VisualRoot : null;

        /// <summary>PR130-05 新增：供测试查询该实例当前是否仍在展示占位内容（尚未被真实资源原地
        /// 替换），不属于 <see cref="IRenderer3D"/> 契约本身。查不到（已销毁/未知句柄）时返回
        /// false。</summary>
        public bool IsShowingPlaceholder(ModelHandle handle) =>
            _instances.TryGetValue(handle.Value, out var instance) && instance.IsPlaceholder;

        /// <summary>W6-B 新增：供测试断言 <see cref="Animator"/> 当前是否正处于名为
        /// <paramref name="stateName"/> 的状态（任一层），不属于 <see cref="IRenderer3D"/> 契约本身，
        /// 惯例同 <see cref="GetModelRoot"/>。</summary>
        public bool IsPlayingState(ModelHandle handle, string stateName)
        {
            var instance = EnsureAlive(handle);
            if (instance.Animator == null)
            {
                return false;
            }

            var hash = Animator.StringToHash(stateName);
            for (var layer = 0; layer < instance.Animator.layerCount; layer++)
            {
                if (instance.Animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == hash)
                {
                    return true;
                }
            }
            return false;
        }

        // --------------------------------------------------------------

        private static bool AnimatorHasState(Animator animator, string stateName)
        {
            var hash = Animator.StringToHash(stateName);
            var controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                return false;
            }

            var layerCount = animator.layerCount;
            for (var layer = 0; layer < layerCount; layer++)
            {
                if (animator.HasState(layer, hash))
                {
                    return true;
                }
            }
            return false;
        }

        private AnimationClip? ResolveLegacyClip(Id clipId)
        {
            if (_legacyClipCache.TryGetValue(clipId, out var cached))
            {
                return cached;
            }

            var clip = Resources.Load<AnimationClip>(UnityResourceLoader.ResolveAnimClipResourcesPath(clipId));
            if (clip != null)
            {
                _legacyClipCache[clipId] = clip;
            }
            return clip;
        }

        private static Mesh? ResolveMesh(Id meshId) => Resources.Load<Mesh>(UnityResourceLoader.ResolveModelResourcesPath(meshId));

        /// <summary>递归按精确名字（含域前缀，如 <c>"socket.main_hand"</c>/<c>"slot.head"</c>）查找子
        /// 物体——占位内容与本模块生成脚本（<see cref="Adapter.Unity.Editor.GeneratePlaceholderModelAssets"/>）
        /// 约定子对象名逐字等于挂点/槽位 <see cref="Id"/> 的 <c>Value</c>（见包 README"资源路径约定"
        /// 一节），比骨骼真实命名规则更宽容，具体游戏可以按自己的骨骼命名习惯重新实现一份
        /// <see cref="IRenderer3D"/>（02 第 4 节"迁移引擎的步骤清单"）。</summary>
        private static Transform? FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static string BareName(Id id)
        {
            var value = id.Value;
            var dotIndex = value.LastIndexOf('.');
            return dotIndex < 0 ? value : value.Substring(dotIndex + 1);
        }

        private ModelInstance EnsureAlive(ModelHandle handle)
        {
            if (!_instances.TryGetValue(handle.Value, out var instance))
            {
                throw new InvalidOperationException($"模型句柄 {handle.Value} 已销毁或不存在");
            }
            return instance;
        }

        /// <summary>反查某个 <see cref="ModelInstance"/> 当前登记的句柄值——只在 legacy Animation 兜底
        /// 路径首次挂接 <see cref="ModelAnimEventRelay"/> 时用到（该分支没有随手带上 handle 值，见
        /// <see cref="PlayAnimOnInstance"/>），实例数量通常很小（同屏活跃角色数量级），线性查找足够。</summary>
        private int HandleValueOf(ModelInstance instance)
        {
            foreach (var kv in _instances)
            {
                if (ReferenceEquals(kv.Value, instance))
                {
                    return kv.Key;
                }
            }
            throw new InvalidOperationException("内部一致性错误：ModelInstance 未登记在 _instances 中");
        }
    }

    /// <summary>W6-B 新增：Unity AnimationEvent 的 SendMessage 中继组件——挂在持有
    /// <see cref="Animator"/>/<see cref="UnityEngine.Animation"/> 组件的那个 GameObject 上（见
    /// <see cref="UnityRenderer3D.CreateModelInstance"/>/<see cref="UnityRenderer3D.PlayAnim"/>
    /// 判断记录），把 <see cref="OnAnimEvent"/>（函数名约定见
    /// <see cref="UnityRenderer3D.AnimEventFunctionName"/>）转发回所属 <see cref="UnityRenderer3D"/>。
    /// <c>internal</c>——不是 <see cref="IRenderer3D"/> 契约的一部分，纯粹是本引擎实现的内部协作
    /// 组件，同 <see cref="Adapter.Unity.Presentation.UnityFrameAnimPlayer"/> 一贯惯例。</summary>
    internal sealed class ModelAnimEventRelay : MonoBehaviour
    {
        private UnityRenderer3D? _owner;
        private int _handleValue;

        internal void Bind(UnityRenderer3D owner, int handleValue)
        {
            _owner = owner;
            _handleValue = handleValue;
        }

        /// <summary>由 Unity 动画事件系统经 SendMessage 调用（函数名固定为
        /// <see cref="UnityRenderer3D.AnimEventFunctionName"/>，String Parameter＝裸事件名，见
        /// <see cref="UnityRenderer3D.RaiseAnimEvent"/>）。</summary>
        public void OnAnimEvent(string eventName) => _owner?.RaiseAnimEvent(_handleValue, eventName);
    }
}

#nullable enable
// UnityRenderer3D：IRenderer3D 的 Unity 引擎实现（W6-B 收口，取代 W3b～H5 阶段"整体声明降级"的
// 占位实现——ADR-0017 决策 b 收紧后，model 型外形的默认路线是框架职责，本引擎适配层现在提供真实
// 三维渲染实现，见 architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md）。
//
// 判断记录（模型实例创建：经 UnityResourceLoader 缓存优先，找不到时回退同步直接
// Resources.Load）：CreateModelInstance 契约本身是同步的（09/02 均未把它列为异步 API），不能像
// LoadAsync 那样排队等下一次 Tick；本类型因此优先查 UnityResourceLoader.TryGetModelPrefab（若调用方
// 已经先经 IResourceLoader.LoadAsync(modelId, ResourceKind.Model, ...) 预热过缓存，见 ADR-0016
// 决策 6"谁首次引用谁加载"），未命中时直接同步调用 Resources.Load&lt;GameObject&gt;（与
// UnityResourceLoader.FinishModelLoad 内部调用的是同一个 API、同一条路径约定，只是不经过
// LoadCallback 那一层排队——Resources.Load 本身在主线程调用总是同步完成，两条路径殊途同归）；
// 两条路径都找不到（预制体确实不存在于 Resources/GameFoundation/models/ 下）时抛
// InvalidOperationException，异常消息带上解析出的具体路径——这是本类型与 UnityRenderer2D.ResolveSprite
// 的刻意差异（后者找不到精灵资源时静默退化为洋红色占位方块并继续运行，见该方法判断记录"资源解析与
// 占位"）：sprite 型外形允许缺资源仍可运行（09 第 1 节表现层"缺表现资源不阻断游戏"一贯宽容策略），
// 但 model 型没有对应的"占位模型"这种轻量退化手段（临时拼一个立方体 GameObject 冒充角色模型，
// 视觉上的误导性远大于一块醒目的洋红色方块，且会让"到底有没有真正接上三维模型"这件事变得难以在
// 运行期分辨），因此本类型选择"资源缺失就是配置错误，应该尽早暴露"这一更严格的立场，与任务书
// "找不到资源时抛带路径的明确异常"一致。
//
// 判断记录（三维放置的坐标换算：无既有 3D 世界坐标约定可循，本类型拍板一套）：09/14 均只给出
// sprite 型的像素/排序换算公式（UnityRenderer2D.SetTransform），从未定义 model 型三维放置该如何
// 把逻辑层的 (Vec2 planePos, height, facing) 换算成 Unity 世界坐标/旋转——这是一处此前从未落地过
// 的契约缺口，本类型据此拍板并如实记录：
//   世界坐标 = (planePos.X, height, planePos.Y)——planePos 是"地面平面"坐标，Unity 侧选择用
//     水平的 X/Z 平面盛放它（Unity 约定"Y 轴朝上"），height 直接作为世界 Y 轴坐标（不像 2D 的
//     height 需要经 PixelsPerUnit 换算成像素位移——3D 场景本就以"世界单位＝美术资产的建模单位"
//     为基准，不存在"像素"这个中间量，见 IRenderConventionHost.HeightOffsetToPixels 类型注释
//     "供高度偏移换算像素纵向偏移"——那是 sprite 型专属换算，本类型不复用）。
//   Y 轴欧拉角（度）= -facing（弧度）× (180/π)——facing 取 05 第 3.1 节"index 0＝角度 0（+X 轴），
//     按角度递增方向（逆时针）编号"这一数学惯例（逆时针，从 +X 轴量起）；Unity 的 Transform.eulerAngles.y
//     是"从上往下看顺时针为正"的左手系惯例，两者手性相反，取负号做一次性换算。这只保证"facing 的
//     数值变化单调对应模型朝向的旋转方向"，不对"模型美术资产的正前方到底建模在哪个局部轴"做任何
//     假设——后者是具体游戏美术资产的建模约定，不属于引擎适配层能够替游戏拍板的范围（同
//     UnityRenderer2D 排序换算"具体数值不在本架构拍板"的一贯立场）。
//   sortY 不参与任何实际渲染调用——3D 管线原生经深度缓冲区决定遮挡关系，不需要 IRenderer2D 那样
//     手工换算 sortingOrder；本方法仍然接受该参数（与 IRenderer2D.SetTransform 同一套放置签名，
//     09 类型注释"sortY 与 IRenderer2D 共享同一排序空间"），只是单纯存下来不使用，保留参数是为了
//     ModelCharacterRig.SyncPlacement 与 sprite 型调用方一份完全对称的调用形状，不需要为 model 型
//     另开一套精简签名。
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
            public GameObject Root = null!;
            public Animator? Animator;
            public UnityEngine.Animation? LegacyAnimation;
            public readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
            public readonly List<AnimEventCallback> AnimEventCallbacks = new List<AnimEventCallback>();
            public GameObject? BlobShadow;
            public ShadowMode Shadow = ShadowMode.None;

            // 见类型顶部"三维放置的坐标换算"判断记录：sortY 只存不用，保留字段只为诊断/未来扩展。
            public double LastSortY;

            // H5b 根治新增（游戏侧复核发现 1）：当前一次 PlayAnim 的驱动方式/目标状态或剪辑名/是否
            // 循环/是否已经通知过完成——供 Tick() 逐实例检测"非循环剪辑自然播放完成"，见该方法判断
            // 记录。FinishNotified 初始为 true（尚未播放过任何剪辑，没有"未完成的播放"需要检测）。
            public AnimDriveMode Drive = AnimDriveMode.None;
            public string? CurrentStateName;
            public string? CurrentClipName;
            public bool CurrentClipLoop = true;
            public bool FinishNotified = true;
        }

        private readonly Transform _root;
        private readonly UnityResourceLoader _resourceLoader;
        private readonly Dictionary<int, ModelInstance> _instances = new Dictionary<int, ModelInstance>();
        private int _nextHandle = 1;

        /// <summary>W6-B 新增：Animation（legacy）兜底路径按剪辑资源引用 id 缓存已加载的
        /// <see cref="AnimationClip"/>，避免同一剪辑被多个模型实例反复 <c>Resources.Load</c>。</summary>
        private readonly Dictionary<Id, AnimationClip> _legacyClipCache = new Dictionary<Id, AnimationClip>();

        public UnityRenderer3D(Transform root, UnityResourceLoader resourceLoader)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        }

        public ModelHandle CreateModelInstance(Id modelId)
        {
            GameObject prefab;
            if (!_resourceLoader.TryGetModelPrefab(modelId, out prefab!))
            {
                // 见类型顶部判断记录：未经 LoadAsync 预热缓存时，回退为同步直接加载，找不到则抛出
                // 带路径的明确异常。
                var path = UnityResourceLoader.ResolveModelResourcesPath(modelId);
                prefab = Resources.Load<GameObject>(path);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"[UnityRenderer3D] 找不到模型资源 \"{modelId}\"（约定路径 Resources/{path}）：请确认该预制体已放在" +
                        " adapters/unity 包的 Assets/Resources/GameFoundation/models/ 目录下。");
                }
            }

            var handle = _nextHandle++;
            var instanceRoot = UnityEngine.Object.Instantiate(prefab, _root);
            instanceRoot.name = $"Model_{handle}_{modelId.Value}";

            var instance = new ModelInstance
            {
                Root = instanceRoot,
                Animator = instanceRoot.GetComponentInChildren<Animator>(),
            };

            // AnimationEvent 的 SendMessage 目标是"持有 Animator/Animation 组件的那个 GameObject
            // 自身"（Unity 既有行为，不搜索父子层级），因此中继组件必须挂在同一个 GameObject 上；
            // 两种驱动方式（Animator/Animation）都可能存在，各自可能挂在预制体内部不同的子物体上，
            // 分别按需补挂一份中继（挂两份也不冲突——事件只会从真正在播放的那一套驱动方式触发）。
            if (instance.Animator != null)
            {
                var relay = instance.Animator.gameObject.AddComponent<ModelAnimEventRelay>();
                relay.Bind(this, handle);
            }

            _instances[handle] = instance;
            return new ModelHandle(handle);
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
            instance.Root.transform.localPosition = new Vector3((float)planePos.X, (float)height, (float)planePos.Y);
            instance.Root.transform.localRotation = Quaternion.Euler(0f, (float)(-facing * Mathf.Rad2Deg), 0f);
            instance.Root.transform.localScale = new Vector3((float)scale, (float)scale, (float)scale);
            instance.LastSortY = sortY;
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
                animation = instance.Root.AddComponent<UnityEngine.Animation>();
                instance.LegacyAnimation = animation;

                var relay = animation.gameObject.GetComponent<ModelAnimEventRelay>();
                if (relay == null)
                {
                    relay = animation.gameObject.AddComponent<ModelAnimEventRelay>();
                    relay.Bind(this, handle.Value);
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
        /// <see cref="CreateModelInstance"/> 找不到"模型本体"时严格抛异常的立场不同——模型本体缺失是
        /// 无法呈现任何东西的阻断性配置错误，槽位换装缺失只是少画一件装备，二者严重程度不对等）。
        /// 网格资源经与模型预制体同一套 <see cref="UnityResourceLoader.ResolveModelResourcesPath"/>
        /// 约定路径 <c>Resources.Load&lt;Mesh&gt;</c> 取用（判断记录：04/09/14 均未给"网格资源"单独
        /// 定义路径规则，本类型选择复用模型预制体那一套"去类别前缀、点号换下划线"约定与同一个子目录，
        /// 不额外新增子目录——网格与模型本就是同一大类"三维几何资产"，没有必要用不同目录管理两次同一
        /// 条命名规则）。</summary>
        public void SetSlotMesh(ModelHandle handle, Id slotId, Id? meshId)
        {
            var instance = EnsureAlive(handle);
            var slotTransform = FindDeep(instance.Root.transform, slotId.Value);
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

            var socketTransform = FindDeep(instance.Root.transform, socketId.Value);
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

        /// <summary>W6-B 新增：经 <see cref="MaterialPropertyBlock"/> 把命名参数广播给实例下全部
        /// <see cref="Renderer"/>（见 <see cref="IRenderer3D.SetMaterialParam"/> 契约注释"参数含义由
        /// DisplayInfo 映射决定，本接口不解释参数语义"）——与 <see cref="UnityRenderer2D.SetShaderParam"/>
        /// 对未知参数名的通用兜底分支同一套机制，保证 <see cref="Presentation.Render.ModelCharacterRig"/>
        /// 固定使用的三个参数名（<c>flash_intensity</c>/<c>trail_intensity</c>/<c>fade_alpha</c>）与
        /// sprite 型走同一套命名，便于游戏侧编写通用着色器同时支持两种外形类型。</summary>
        public void SetMaterialParam(ModelHandle handle, string paramName, double value)
        {
            var instance = EnsureAlive(handle);
            var renderers = instance.Root.GetComponentsInChildren<Renderer>(includeInactive: true);
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
        /// <see cref="UnityRenderer2D.SetShadow"/> 那样把 Projected 降级为 Blob。</summary>
        public void SetShadow(ModelHandle handle, ShadowMode mode)
        {
            var instance = EnsureAlive(handle);
            instance.Shadow = mode;

            var renderers = instance.Root.GetComponentsInChildren<Renderer>(includeInactive: true);
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

        /// <summary>W6-B 新增：供测试/同属引擎适配层的协作代码取回模型实例的根 <see cref="GameObject"/>
        /// （不属于 <see cref="IRenderer3D"/> 契约本身，同 <see cref="UnityRenderer2D.GetSpriteRoot"/>
        /// 一贯的"引擎实现之间的内部协作方法"惯例）。查不到（已销毁/未知句柄）时返回 null。</summary>
        public GameObject? GetModelRoot(ModelHandle handle) =>
            _instances.TryGetValue(handle.Value, out var instance) ? instance.Root : null;

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

        private Mesh? ResolveMesh(Id meshId) => Resources.Load<Mesh>(UnityResourceLoader.ResolveModelResourcesPath(meshId));

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

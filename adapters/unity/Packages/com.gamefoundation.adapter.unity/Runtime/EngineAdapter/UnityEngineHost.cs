#nullable enable
// UnityEngineHost：13 个引擎适配层接口 Unity 实现的组合根。
//
// 职责：在 Awake 里一次性构造全部 13 个 Unity* 实现（与 adapters/stub/StubEngine 的角色完全
// 对应，区别是 StubEngine 是纯 C# 对象，本类型必须是 MonoBehaviour 才能拿到 Update/FixedUpdate/
// OnApplicationQuit 生命周期去驱动需要每帧推进的几个实现（Clock/Input/Camera/Audio/
// ResourceLoader 的主线程完成队列））；DontDestroyOnLoad 保证跨场景存活；Ensure() 提供静态
// 获取入口，供测试与后续引导代码使用（测试可以在 EditMode/PlayMode 里调用
// UnityEngineHost.Ensure() 取得一整套可用的引擎适配层实现，不需要重复关心构造顺序与依赖关系）。
//
// 驱动分工（对应任务书"在 Update/FixedUpdate/OnApplicationQuit 里驱动 UnityClock 与输入采样"）：
//   Update      -> Clock.TickFrame（帧回调、GetDeltaSeconds）、Input 的鼠标移动检测、
//                  Camera.Tick（跟随/震屏）、Audio.Tick（音乐淡入淡出）、
//                  ResourceLoader.Tick（把后台线程读完的资源在主线程完成解码并回调）。
//   FixedUpdate -> Clock.TickFixedStep（固定步长模拟节拍，见 UnityClock.cs 顶部判断记录）。
//   OnApplicationQuit -> UnityInput.Dispose（释放 InputActionMap/设备事件订阅）、
//                  把 Application.logMessageReceived 采集到的未处理异常在退出前落盘（见
//                  HandleUnityLogMessage）。
using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityEngineHost : MonoBehaviour
    {
        private static UnityEngineHost? _instance;

        public UnityWindow Window { get; private set; } = null!;
        public UnityClock Clock { get; private set; } = null!;
        public UnityRenderer2D Renderer2D { get; private set; } = null!;
        public UnityAudio Audio { get; private set; } = null!;
        public UnityInput Input { get; private set; } = null!;
        public UnityFileSystem FileSystem { get; private set; } = null!;
        public UnityResourceLoader ResourceLoader { get; private set; } = null!;
        public UnityNavigation2D Navigation2D { get; private set; } = null!;
        public UnitySpatialQuery SpatialQuery { get; private set; } = null!;
        public UnityUISurface UISurface { get; private set; } = null!;
        public UnityPlatform Platform { get; private set; } = null!;
        public UnityRenderer3D Renderer3D { get; private set; } = null!;
        public UnityCamera Camera { get; private set; } = null!;

        /// <summary>获取（必要时创建）全局唯一的宿主实例。测试与游戏引导代码统一走这个入口，
        /// 不直接 new UnityEngineHost（MonoBehaviour 不允许 new，也不应该有第二份实例）。</summary>
        public static UnityEngineHost Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var existing = FindFirstObjectByType<UnityEngineHost>();
            if (existing != null)
            {
                _instance = existing;
                return _instance;
            }

            var go = new GameObject("GameFoundation.EngineHost");
            return go.AddComponent<UnityEngineHost>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            var cameraGo = new GameObject("GameFoundation.Camera");
            cameraGo.transform.SetParent(transform, worldPositionStays: false);
            var cameraComponent = cameraGo.AddComponent<UnityEngine.Camera>();

            Window = new UnityWindow();
            Clock = new UnityClock();
            FileSystem = new UnityFileSystem();
            ResourceLoader = new UnityResourceLoader();
            Renderer2D = new UnityRenderer2D(transform, ResourceLoader);
            Audio = new UnityAudio(transform, ResourceLoader);
            Input = new UnityInput();
            Navigation2D = new UnityNavigation2D();
            SpatialQuery = new UnitySpatialQuery();
            UISurface = new UnityUISurface(transform);
            Platform = new UnityPlatform(FileSystem);
            // W6-B 收口：UnityRenderer3D 不再整体声明降级，构造依赖同 Renderer2D 一致的
            // (Transform root, UnityResourceLoader resourceLoader) 两参（见该类型顶部判断记录）。
            Renderer3D = new UnityRenderer3D(transform, ResourceLoader);
            Camera = new UnityCamera(cameraComponent);

            SpatialQuery.SetLineOfSightBlocker((from, to) => Navigation2D.Raycast(DefaultMapId, from, to) != null);

            Application.logMessageReceived += HandleUnityLogMessage;

            // 判断记录（引擎侧收口任务，根治"There are 2 event systems in the scene"）：本类型是
            // DontDestroyOnLoad 单例，UISurface 的构造（进而其内部按需创建的兜底
            // "GameFoundation.EventSystem"，见 UnityUISurface.cs 判断记录）只在本 Awake 执行的
            // 那一次决定；而 Assets/Editor/GreyBoxSceneBuilder.cs/ShellSceneBuilder.cs/
            // games/_template/Editor/GameSceneBuilder.cs 生成的场景各自烘焙了一份场景自己的
            // EventSystem，随场景加载/卸载各自创建/销毁——一旦本类型早年（第一次 Ensure() 时）
            // 判定"当时还没有"而建了兜底的那份，它会 DontDestroyOnLoad 持续存活，之后每次任意
            // 场景加载出自己烘焙的那份，两者同时存在，UGUI 记"There are 2 event systems"警告
            // （UnityUISurface.cs 的判断记录只能修正"第一次判定"本身的时序竞争，修不了"兜底份额外
            // 持续存活、后续每次加载都会撞上"这个结构性问题）。改为订阅 SceneManager.sceneLoaded，
            // 每次任意场景加载完成后做一次"全局至多一个 EventSystem"收敛：优先保留场景自己烘焙的
            // 那份（名字不是 "GameFoundation.EventSystem"），销毁多余的（含本类型早年建的兜底份）。
            SceneManager.sceneLoaded += (_, __) => DeduplicateEventSystems();
        }

        private static void DeduplicateEventSystems()
        {
            var systems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
            if (systems.Length <= 1)
            {
                return;
            }

            UnityEngine.EventSystems.EventSystem? toKeep = null;
            for (var i = 0; i < systems.Length; i++)
            {
                if (systems[i].gameObject.name != "GameFoundation.EventSystem")
                {
                    toKeep = systems[i];
                    break;
                }
            }
            toKeep ??= systems[0];

            for (var i = 0; i < systems.Length; i++)
            {
                if (systems[i] != toKeep)
                {
                    Destroy(systems[i].gameObject);
                }
            }
        }

        private void Update()
        {
            var deltaSeconds = (double)Time.unscaledDeltaTime;
            Clock.TickFrame(deltaSeconds);
            Camera.Tick(deltaSeconds);
            Audio.Tick(deltaSeconds);
            ResourceLoader.Tick();
            // H5b 根治新增（游戏侧复核发现 1）：见 UnityRenderer3D.Tick 判断记录——逐 model 实例检测
            // 非循环剪辑自然播放完成，与其余几个 *.Tick() 同一惯例，每帧调用一次。
            Renderer3D.Tick();
        }

        private void FixedUpdate()
        {
            Clock.TickFixedStep(Time.fixedDeltaTime);
        }

        private void OnApplicationQuit()
        {
            Input.Dispose();
            Application.logMessageReceived -= HandleUnityLogMessage;
        }

        private void HandleUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                Platform.ReportCrash($"UnityLog:{type}", condition + "\n" + stackTrace);
            }
        }

        /// <summary>ISpatialQuery.HasLineOfSight 默认接到 Navigation2D 的遮挡判定用的占位地图
        /// id（ISpatialQuery 契约本身"mapId 由当前场景隐含"，02 第 1.9 节），本框架阶段暂未提供
        /// "当前场景 mapId"的统一来源，先固定用一个框架内保留 id；游戏层接入具体场景管理后应
        /// 替换本行为（记录为契约缺口之外的框架内部占位选择，不属于任务要求汇报的契约缺口清单，
        /// 但供后续接入时参考）。</summary>
        private static readonly Id DefaultMapId = new Id("map.default");
    }
}

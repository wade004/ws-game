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
            Renderer3D = new UnityRenderer3D();
            Camera = new UnityCamera(cameraComponent);

            SpatialQuery.SetLineOfSightBlocker((from, to) => Navigation2D.Raycast(DefaultMapId, from, to) != null);

            Application.logMessageReceived += HandleUnityLogMessage;
        }

        private void Update()
        {
            var deltaSeconds = (double)Time.unscaledDeltaTime;
            Clock.TickFrame(deltaSeconds);
            Camera.Tick(deltaSeconds);
            Audio.Tick(deltaSeconds);
            ResourceLoader.Tick();
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

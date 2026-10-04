// 让两个测试程序集可以访问 Adapter.Unity 内部方法（例如 UnityClock.TickFrame/TickFixedStep、
// UnityResourceLoader.Tick、UnityAudio.Tick、UnityCamera.Tick 等——这些方法按设计只应由
// UnityEngineHost 驱动，不属于对外公开 API，但测试需要在没有完整宿主 MonoBehaviour 生命周期的
// 情况下手动驱动它们）。
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Editor")]
[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Runtime")]
// 实验室引擎宿主（可选组件，见 Runtime/LabHost）按模拟时间驱动资源加载器、音频、镜头与模型，仍需要它们的内部 Tick 入口
// （按设计只应由 UnityEngineHost 驱动）；帧动画播放器与特效序列播放器已改走公开的 Step() 加可注入时间源（M4-W4），不再依赖内部入口。
[assembly: InternalsVisibleTo("Adapter.Unity.LabHost")]
// 参考皮肤包 PlayMode 用例（Tests/Runtime/LabHost/ReferenceSkinPlayModeTests）要换资源内容根并手动驱动自己的加载器，需要 RootDirOverrideForTests 与 Tick。
[assembly: InternalsVisibleTo("Adapter.Unity.Tests.LabHost")]

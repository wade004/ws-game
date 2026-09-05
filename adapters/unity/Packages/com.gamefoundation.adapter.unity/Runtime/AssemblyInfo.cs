// 让两个测试程序集可以访问 Adapter.Unity 内部方法（例如 UnityClock.TickFrame/TickFixedStep、
// UnityResourceLoader.Tick、UnityAudio.Tick、UnityCamera.Tick 等——这些方法按设计只应由
// UnityEngineHost 驱动，不属于对外公开 API，但测试需要在没有完整宿主 MonoBehaviour 生命周期的
// 情况下手动驱动它们）。
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Editor")]
[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Runtime")]

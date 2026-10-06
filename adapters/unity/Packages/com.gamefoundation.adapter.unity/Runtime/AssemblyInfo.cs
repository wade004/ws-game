// 让两个测试程序集可以访问 Adapter.Unity 内部方法（例如 UnityClock.TickFrame/TickFixedStep、
// UnityResourceLoader.Tick、UnityAudio.Tick、UnityCamera.Tick 等——这些方法按设计只应由
// UnityEngineHost 驱动，不属于对外公开 API，但测试需要在没有完整宿主 MonoBehaviour 生命周期的
// 情况下手动驱动它们）。
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Editor")]
[assembly: InternalsVisibleTo("Adapter.Unity.Tests.Runtime")]
// 判断记录（ADR-0160，运行时包不认识任何开发期可选包）：这里不再点名实验室程序集。实验室引擎宿主按模拟时间驱动资源加载器、音频与镜头所需的
// Tick 入口已改成公开成员（UnityResourceLoader.Tick、UnityAudio.Tick、UnityCamera.Tick），宿主走公开接口，不靠内部可见性；
// 门禁断言运行时包的 InternalsVisibleTo 不得点名实验室或演示程序集。
// 工作台工程里的真实美术回归（Assets/RealAssetTests，参考皮肤包的 PlayMode 用例，随框架仓库但不进任何发布包）要换资源内容根，
// 需要测试接缝 UnityResourceLoader.RootDirOverrideForTests；它是测试程序集，不是实验室程序集。
[assembly: InternalsVisibleTo("Framework.RealAssetTests")]

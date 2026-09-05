#nullable enable
// PlayModeTestBase：Adapter.Unity.Tests.Runtime 命名空间下全部测试类共用的隔离基类
// （收边任务 3，见 PlayModeIsolation.cs 顶部判断记录）。
//
// 判断记录（用"每个测试类共用的基类"而不是只用 GlobalPlayModeTestSetup 的 OneTimeSetUp/
// OneTimeTearDown 覆盖全部隔离职责）：NUnit 的 [SetUpFixture].OneTimeSetUp/OneTimeTearDown
// 只在整个命名空间的全部用例跑完前后各执行一次（见 GlobalPlayModeTestSetup.cs），不提供"每一条
// 用例前后都执行一次"的装配级钩子——这正是任务书要求的"每条用例结束后统一清理"的语义，只能靠
// 每个测试类自己的 [UnitySetUp]/[UnityTearDown] 达成。让每个测试类各自重复实现一遍不是本任务的
// 目标（"收敛此前散落的规避代码"），因此提供本基类，测试类改为 `: PlayModeTestBase`
// 即可获得统一的清理与"用例开始前断言干净"，不需要各自重复实现。
//
// NUnit 的 SetUp/TearDown（含 UnitySetUp/UnityTearDown）发现机制按方法特性反射整个类型继承链，
// 基类与派生类各自声明的同名特性方法都会被调用（不依赖 C# virtual/override 机制）——因此测试类
// 若还需要额外的、真正属于自己职责的 SetUp/TearDown（如 SharedBootstrapDiscreteTests.cs 销毁自己
// 持有的 GameObject 字段），可以正常再声明一份自己的 [UnitySetUp]/[UnityTearDown] 方法，两者都会
// 执行，不冲突、不需要显式调用 base.xxx()。
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public abstract class PlayModeTestBase
    {
        [UnitySetUp]
        public IEnumerator BaseSetUp()
        {
            PlayModeIsolation.AssertCleanBeforeTest();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator BaseTearDown()
        {
            yield return PlayModeIsolation.TearDownAfterTest();
        }
    }
}

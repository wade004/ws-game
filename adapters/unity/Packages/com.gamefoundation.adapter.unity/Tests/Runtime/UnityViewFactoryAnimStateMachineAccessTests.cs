#nullable enable
// UnityViewFactoryAnimStateMachineAccessTests：ADR-0070 决定 2 收口验收（消费方反馈第十七批
// "AnimStateMachine.RequestOverride 扩展点实际不可达"）——此前 UnityViewFactory 只以
// internal AnimStateMachineForTests 暴露内部持有的 AnimStateMachine 实例，生产代码（游戏侧程序集，
// 不在 InternalsVisibleTo 名单内）拿不到实例，RequestOverride 这个"供具体游戏主动调用"的扩展点因此
// 实际不可达。本文件验证新增的公开只读属性 UnityViewFactory.AnimStateMachine：
// 1. 反射断言其 getter 确实是 public（测试与被测类型同程序集，看不出 internal/public 的区别，
//    必须用反射证明可见性，任务书原句）。
// 2. 功能用例：经该公开入口拿到实例调 RequestOverride 后状态确实变化。
//
// 判断记录（不经完整 FrameworkResidentHost/ShellRoot 装配，直接构造 UnityViewFactory 本体）：同
// UnityViewFactoryDefaultAnimationTests.cs 顶部判断记录——本文件验证的是 UnityViewFactory 这一个
// 单元的公开访问面，不需要完整游戏世界装配；复用同目录 AnimationLayerTests.cs 的
// DisplayInfoTestSupportForAnimTests.CreateSpriteInfo()（Category=Creature，触发
// EnsureAnimClipResolver 懒构造 AnimStateMachine）与 UnityViewFactoryDefaultAnimationTests.cs 的
// FakeDisplayInfoRegistryForAnim，均为 internal，同一程序集内可见。
using System.Collections.Generic;
using System.Reflection;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Render;
using UnityEngine;

using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityViewFactoryAnimStateMachineAccessTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("UnityViewFactoryAnimStateMachineAccessTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_rootGo);
        }

        private static (IEventBus Bus, DisplayInfo Info, Id EntityId, FakeDisplayInfoRegistryForAnim DisplayInfo) BuildFixture()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, System.Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var info = DisplayInfoTestSupportForAnimTests.CreateSpriteInfo();
            var displayInfo = new FakeDisplayInfoRegistryForAnim();
            displayInfo.Add(info);

            return (bus, info, new Id("unit.anim_access_test_entity"), displayInfo);
        }

        /// <summary>反射断言：<see cref="UnityViewFactory.AnimStateMachine"/> 的 getter 必须是
        /// public——测试与被测类型同程序集，直接调用属性看不出 internal/public 的区别（同程序集内
        /// internal 成员一样能编译通过），只有反射查询访问修饰符才能真正证明"生产代码（游戏侧程序集，
        /// 不在 InternalsVisibleTo 名单内）拿得到"。</summary>
        [Test]
        public void AnimStateMachineProperty_GetterIsPublic()
        {
            var property = typeof(UnityViewFactory).GetProperty(
                nameof(UnityViewFactory.AnimStateMachine), BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property,
                "UnityViewFactory 应当有一个名为 AnimStateMachine 的 public 实例属性");
            var getter = property!.GetGetMethod(nonPublic: false);
            Assert.IsNotNull(getter,
                "AnimStateMachine 属性的 getter 必须是 public（GetGetMethod(nonPublic: false) 不应为 null）");
        }

        /// <summary>功能用例：经公开属性拿到的实例与内部字段是同一个（不是每次新建一份代理），且
        /// 能正常调用 <see cref="AnimStateMachine.RequestOverride"/> 驱动状态变化——证明这个扩展点
        /// 经生产可达入口确实可用，不只是反射能看到一个 public 签名。</summary>
        [Test]
        public void AnimStateMachineProperty_ReturnsUsableInstance_RequestOverrideChangesState()
        {
            var (bus, info, entityId, displayInfo) = BuildFixture();
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfo, _resourceLoader, bus: bus, dataRegistry: null);

            // 未挂接过任何默认动画（EnsureAnimClipResolver 尚未被触发）之前，公开属性与既有 internal
            // 测试出口应一致为 null——本属性只是把已有的懒构造实例转发出去，不改变懒构造时机本身。
            Assert.IsNull(factory.AnimStateMachine);

            // CreateView（Category=Creature 的 sprite 分类）触发 AttachDefaultAnimation →
            // EnsureAnimClipResolver，懒构造出真正的 AnimStateMachine 实例。
            factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);

            var machine = factory.AnimStateMachine;
            Assert.IsNotNull(machine, "CreateView 之后公开属性应能拿到懒构造出的 AnimStateMachine 实例");
            Assert.AreSame(machine, factory.AnimStateMachineForTests,
                "公开属性与既有 internal 测试出口应指向同一个实例，不是各自持有一份");

            Assert.AreEqual(AnimState.Idle, machine!.GetState(entityId));

            machine.RequestOverride(entityId, AnimState.Jump);

            Assert.AreEqual(AnimState.Jump, machine.GetState(entityId),
                "经公开属性拿到的实例调用 RequestOverride 后状态应确实变化");
        }
    }
}

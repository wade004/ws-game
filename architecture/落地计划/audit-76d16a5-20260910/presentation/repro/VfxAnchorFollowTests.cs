#nullable enable
// VfxAnchorFollowTests：第十四轮审核（audit-c9ff301-20260909）"VFX anchor/socket 未持续跟随"根治的
// PlayMode 验收——presentation/vfx_sfx/tests/VfxPlayerFollowTests.cs 已用测试专用桩验证
// VfxPlayer.UpdateFollowTargets 的调度逻辑（dotnet test，引擎无关）；本用例改用真实
// Adapter.Unity.EngineAdapter.UnityRenderer2D 验证 IParticleRepositioner 实现本身——EmitParticle
// 产生的确实是一个真实 Unity Transform（序列帧播放器/ParticleSystem 各自的 GameObject），
// SetParticlePosition 确实原地移动了它，不是空实现或只更新内部记账。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.VfxSfx.Contracts;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class VfxAnchorFollowTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("VfxAnchorFollowRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        [UnityTest]
        public IEnumerator SetParticlePosition_MovesTheRealTransformCreatedByEmitParticle()
        {
            // 未加载到具体特效资源时 EmitParticle 回退播放内建通用粒子（见类型判断记录），同样是
            // 一个真实 ParticleSystem GameObject，足够验证 SetParticlePosition 本身。
            var handle = _renderer.EmitParticle(
                new Id("vfx.follow_probe"), new Vec2(0, 0), new Dictionary<string, double>());
            yield return null;

            Assert.AreEqual(1, _rootGo.transform.childCount, "EmitParticle 应在 root 下创建一个真实子物体");
            var particleTransform = _rootGo.transform.GetChild(0);
            Assert.AreEqual(new Vector3(0f, 0f, 0f), particleTransform.position);

            // IParticleRepositioner 是可选能力接口——VfxPlayer 用 (_renderer2D as IParticleRepositioner)
            // 探测；本用例直接转型调用，验证接口在真实引擎适配层上确实是这个具体实现（不是别的类型
            // 恰好同名方法）。
            Assert.IsInstanceOf<IParticleRepositioner>(_renderer);
            var repositioner = (IParticleRepositioner)_renderer;
            repositioner.SetParticlePosition(handle, new Vec2(5, 7));
            yield return null;

            Assert.AreEqual(new Vector3(5f, 7f, 0f), particleTransform.position,
                "SetParticlePosition 应原地移动 EmitParticle 创建的同一个真实 Transform（跟随的落地机制）");

            _renderer.StopParticle(handle);
        }

        [UnityTest]
        public IEnumerator SetParticlePosition_AfterStopParticle_IsSilentlyIgnored()
        {
            var handle = _renderer.EmitParticle(
                new Id("vfx.follow_probe_stopped"), new Vec2(1, 1), new Dictionary<string, double>());
            yield return null;

            _renderer.StopParticle(handle);
            yield return null;

            var repositioner = (IParticleRepositioner)_renderer;
            // 见 IParticleRepositioner.SetParticlePosition 接口注释"防御性契约要求"：句柄已不存在时
            // 静默忽略，不抛异常——正常调用方（VfxPlayer）会在 Stop 时同步摘除跟随记账，不会再传入
            // 已停止的句柄，这里验证的是防御性兜底本身，不是预期路径。
            Assert.DoesNotThrow(() => repositioner.SetParticlePosition(handle, new Vec2(9, 9)));
        }
    }
}

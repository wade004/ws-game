#nullable enable
// Renderer2DScenarios：IRenderer2D 契约一致性场景（见 02_引擎适配层.md 第 1.3 节 / ADR-0016 决策 2）。
using System;
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class Renderer2DScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IRenderer2D>> All = new[]
        {
            new ConformanceScenario<IRenderer2D>("创建实例后SetLayers与SetTransform不抛异常", CreateInstance_SetLayersAndTransform_DoesNotThrow),
            new ConformanceScenario<IRenderer2D>("销毁句柄后再使用抛InvalidOperationException", DestroyedHandle_ReuseThrows),
            new ConformanceScenario<IRenderer2D>("粒子_停止未知句柄抛异常_停止已发射句柄不抛", Particle_StopUnknownThrows_StopEmittedSucceeds),
        };

        private static readonly Id SpriteSetId = new Id("sprite.conformance_placeholder");
        private static readonly Id LayerId = new Id("layer.conformance_placeholder__front__body");
        private static readonly Id EffectId = new Id("vfx.conformance_placeholder");

        private static IEnumerator CreateInstance_SetLayersAndTransform_DoesNotThrow(IRenderer2D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            var handle = renderer.CreateSpriteInstance(SpriteSetId);
            assert.DoesNotThrow(() => renderer.SetLayers(handle, new[] { LayerId }), "SetLayers 对存活句柄不应抛异常");
            assert.DoesNotThrow(
                () => renderer.SetTransform(handle, new Vec2(1, 2), height: 10, sortY: 2, layer: 0, rotation: 0, scale: 1, flipX: false),
                "SetTransform 对存活句柄不应抛异常");
            assert.DoesNotThrow(() => renderer.SetShaderParam(handle, "flash_intensity", 0.5), "SetShaderParam 对存活句柄不应抛异常");
            renderer.DestroySpriteInstance(handle);
            yield break;
        }

        private static IEnumerator DestroyedHandle_ReuseThrows(IRenderer2D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            var handle = renderer.CreateSpriteInstance(SpriteSetId);
            renderer.DestroySpriteInstance(handle);

            assert.Throws<InvalidOperationException>(
                () => renderer.SetTransform(handle, Vec2.Zero, 0, 0, 0, 0, 1, false),
                "销毁后的精灵句柄调用 SetTransform 应抛 InvalidOperationException");
            assert.Throws<InvalidOperationException>(
                () => renderer.SetLayers(handle, Array.Empty<Id>()),
                "销毁后的精灵句柄调用 SetLayers 应抛 InvalidOperationException");
            assert.Throws<InvalidOperationException>(
                () => renderer.DestroySpriteInstance(handle),
                "重复销毁同一个精灵句柄应抛 InvalidOperationException");
            yield break;
        }

        private static IEnumerator Particle_StopUnknownThrows_StopEmittedSucceeds(IRenderer2D renderer, IConformanceAssert assert, ConformanceContext ctx)
        {
            var unknown = new ParticleHandle(int.MaxValue - 1);
            assert.Throws<InvalidOperationException>(
                () => renderer.StopParticle(unknown),
                "StopParticle 对未发射/未知的粒子句柄应抛 InvalidOperationException");

            var emitted = renderer.EmitParticle(EffectId, Vec2.Zero, new Dictionary<string, double>());
            assert.DoesNotThrow(() => renderer.StopParticle(emitted), "StopParticle 对刚发射的粒子句柄不应抛异常");
            yield break;
        }
    }
}

// FreezeCapableStubRenderer2D：测试专用——手感落地 M3-C 的粒子冻结用例需要一个同时实现 IRenderer2D 与
// Presentation.VfxSfx.Contracts.IParticleFreezer 的引擎适配层替身（理由同 FollowCapableStubRenderer2D：adapters/stub 对
// presentation 层零依赖，不给 StubRenderer2D 加表现层专属的可选能力接口），在测试项目内组合一个 StubRenderer2D 逐个转发，
// 只在 StopParticle/SetParticlePaused 处附加"逐句柄暂停状态 + 暂停/恢复调用次数"的记录。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;

namespace Tests.Presentation.VfxSfx
{
    public sealed class FreezeCapableStubRenderer2D : IRenderer2D, IParticleFreezer
    {
        private readonly StubRenderer2D _inner = new StubRenderer2D();

        /// <summary>仍存活的粒子句柄 → 当前是否暂停。</summary>
        public readonly Dictionary<int, bool> Paused = new Dictionary<int, bool>();

        /// <summary>句柄 → 特效 id（EmitParticle 时记录），供按特效找句柄。</summary>
        public readonly Dictionary<int, Id> EffectOf = new Dictionary<int, Id>();

        public int PauseCallCount { get; private set; }

        public int ResumeCallCount { get; private set; }

        public bool IsPaused(ParticleHandle handle) => Paused.TryGetValue(handle.Value, out var p) && p;

        public bool IsAlive(ParticleHandle handle) => _inner.IsParticleAlive(handle);

        public SpriteHandle CreateSpriteInstance(Id spriteSetId) => _inner.CreateSpriteInstance(spriteSetId);

        public void SetLayers(SpriteHandle handle, IReadOnlyList<Id> layers) => _inner.SetLayers(handle, layers);

        public void SetTransform(SpriteHandle handle, Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX) =>
            _inner.SetTransform(handle, position, height, sortY, layer, rotation, scale, flipX);

        public void SetShaderParam(SpriteHandle handle, string paramName, double value) => _inner.SetShaderParam(handle, paramName, value);

        public void SetShadow(SpriteHandle handle, ShadowMode mode) => _inner.SetShadow(handle, mode);

        public void DestroySpriteInstance(SpriteHandle handle) => _inner.DestroySpriteInstance(handle);

        public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters)
        {
            var handle = _inner.EmitParticle(effectId, position, parameters);
            Paused[handle.Value] = false;
            EffectOf[handle.Value] = effectId;
            return handle;
        }

        public void StopParticle(ParticleHandle handle)
        {
            _inner.StopParticle(handle);
            Paused.Remove(handle.Value);
        }

        public void SetParticlePaused(ParticleHandle handle, bool paused)
        {
            if (!Paused.ContainsKey(handle.Value))
            {
                return; // 已停止的句柄静默忽略（契约）。
            }

            if (paused)
            {
                PauseCallCount++;
            }
            else
            {
                ResumeCallCount++;
            }

            Paused[handle.Value] = paused;
        }
    }
}

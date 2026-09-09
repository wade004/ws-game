// FollowCapableStubRenderer2D：测试专用——VFX 持续跟随根治（见 VfxPlayerFollowTests.cs）需要一个
// 同时实现 IRenderer2D 与 Presentation.VfxSfx.Contracts.IParticleRepositioner 的引擎适配层来验证
// VfxPlayer 探测到重定位能力后确实会调用 SetParticlePosition；Adapters.Stub.StubRenderer2D 是
// sealed 且只实现 IRenderer2D（该项目不依赖 presentation/，见 adapters/stub/Adapters.Stub.csproj
// 判断记录——不给它加这个presentation 专属的可选能力接口，保持 adapters/stub 对 presentation 层零
// 依赖），因此本类型在测试项目内部组合（不是继承）一个 StubRenderer2D 实例，逐个成员转发，只在
// EmitParticle/SetParticlePosition 两处附加"记录当前坐标"这一额外行为。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;

namespace Tests.Presentation.VfxSfx
{
    public sealed class FollowCapableStubRenderer2D : IRenderer2D, IParticleRepositioner
    {
        private readonly StubRenderer2D _inner = new StubRenderer2D();

        /// <summary>每个仍存活的粒子句柄当前的坐标——EmitParticle 首次写入，
        /// SetParticlePosition（VfxPlayer 的跟随刷新调用）每帧覆盖，供测试断言"是否真的随目标移动"。</summary>
        public readonly Dictionary<int, Vec2> ParticlePositions = new Dictionary<int, Vec2>();

        /// <summary>SetParticlePosition 被调用的次数，供测试断言"未装配跟随能力时是否完全没有被调用"
        /// 一类反向用例（本类型恒实现该接口，用不到这个反向场景，这里只是留一个诊断计数，同本仓库
        /// 一贯"Xxx CallCount"惯例，供后续用例扩展）。</summary>
        public int SetParticlePositionCallCount { get; private set; }

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
            ParticlePositions[handle.Value] = position;
            return handle;
        }

        public void StopParticle(ParticleHandle handle)
        {
            _inner.StopParticle(handle);
            ParticlePositions.Remove(handle.Value);
        }

        public void SetParticlePosition(ParticleHandle handle, Vec2 position)
        {
            SetParticlePositionCallCount++;
            ParticlePositions[handle.Value] = position;
        }
    }
}

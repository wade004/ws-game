// StubRenderer2D：IRenderer2D 的最小可用桩实现——只记录调用，不做任何真实绘制。
// 用途：测试可以断言"精灵实例被创建了、SetTransform 传了什么参数"等，而不启动任何渲染管线。
// 与真实实现的差异：句柄按创建顺序自增分配（从 1 开始，0 保留不用）；不产生任何像素输出；
// 销毁后的句柄再次使用（SetLayers/SetTransform/SetShaderParam/DestroySpriteInstance）会抛
// InvalidOperationException，帮助测试及早发现"用了已销毁实例"的逻辑错误。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubRenderer2D : IRenderer2D
    {
        public readonly struct TransformRecord
        {
            public readonly Vec2 Position;
            public readonly double Height;
            public readonly double SortY;
            public readonly int Layer;
            public readonly double Rotation;
            public readonly double Scale;
            public readonly bool FlipX;

            public TransformRecord(Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX)
            {
                Position = position;
                Height = height;
                SortY = sortY;
                Layer = layer;
                Rotation = rotation;
                Scale = scale;
                FlipX = flipX;
            }
        }

        private int _nextSpriteHandle = 1;
        private int _nextParticleHandle = 1;
        private readonly HashSet<int> _aliveSprites = new HashSet<int>();
        private readonly HashSet<int> _aliveParticles = new HashSet<int>();

        public readonly Dictionary<int, Id> CreatedSpriteSets = new Dictionary<int, Id>();
        public readonly Dictionary<int, IReadOnlyList<Id>> Layers = new Dictionary<int, IReadOnlyList<Id>>();
        public readonly Dictionary<int, TransformRecord> Transforms = new Dictionary<int, TransformRecord>();
        public readonly Dictionary<int, Dictionary<string, double>> ShaderParams = new Dictionary<int, Dictionary<string, double>>();
        public readonly Dictionary<int, (Id EffectId, Vec2 Position)> EmittedParticles = new Dictionary<int, (Id, Vec2)>();

        /// <summary>GP-PRES-05 收口新增：记录每个句柄最近一次 <see cref="SetShadow"/> 的模式，供
        /// 测试断言。</summary>
        public readonly Dictionary<int, ShadowMode> Shadows = new Dictionary<int, ShadowMode>();

        public SpriteHandle CreateSpriteInstance(Id spriteSetId)
        {
            var handle = new SpriteHandle(_nextSpriteHandle++);
            _aliveSprites.Add(handle.Value);
            CreatedSpriteSets[handle.Value] = spriteSetId;
            return handle;
        }

        public void SetLayers(SpriteHandle handle, IReadOnlyList<Id> layers)
        {
            EnsureSpriteAlive(handle);
            Layers[handle.Value] = layers;
        }

        public void SetTransform(SpriteHandle handle, Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX)
        {
            EnsureSpriteAlive(handle);
            Transforms[handle.Value] = new TransformRecord(position, height, sortY, layer, rotation, scale, flipX);
        }

        public void SetShaderParam(SpriteHandle handle, string paramName, double value)
        {
            EnsureSpriteAlive(handle);
            if (!ShaderParams.TryGetValue(handle.Value, out var parameters))
            {
                parameters = new Dictionary<string, double>(StringComparer.Ordinal);
                ShaderParams[handle.Value] = parameters;
            }

            parameters[paramName] = value;
        }

        public void SetShadow(SpriteHandle handle, ShadowMode mode)
        {
            EnsureSpriteAlive(handle);
            Shadows[handle.Value] = mode;
        }

        public void DestroySpriteInstance(SpriteHandle handle)
        {
            EnsureSpriteAlive(handle);
            _aliveSprites.Remove(handle.Value);
        }

        public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters)
        {
            var handle = new ParticleHandle(_nextParticleHandle++);
            _aliveParticles.Add(handle.Value);
            EmittedParticles[handle.Value] = (effectId, position);
            return handle;
        }

        public void StopParticle(ParticleHandle handle)
        {
            if (!_aliveParticles.Contains(handle.Value))
            {
                throw new InvalidOperationException($"粒子句柄 {handle.Value} 已销毁或不存在");
            }

            _aliveParticles.Remove(handle.Value);
        }

        private void EnsureSpriteAlive(SpriteHandle handle)
        {
            if (!_aliveSprites.Contains(handle.Value))
            {
                throw new InvalidOperationException($"精灵句柄 {handle.Value} 已销毁或不存在");
            }
        }
    }
}

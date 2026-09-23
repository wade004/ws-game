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

        /// <summary>诊断记录 diag-isolation.md 配套新增：<see cref="SetLayers"/> 每次调用的
        /// (句柄, 层 resourceId 列表) 按调用顺序追加（含对同一句柄的重复调用）——<see cref="Layers"/>
        /// 只保留"最新一次"的值，测试如果要断言"资源加载完成后是否真的补触发了第二次 SetLayers"
        /// （而不仅仅是最终结果是否正确），需要看调用次数本身，本字段专供这类断言使用。</summary>
        public readonly List<(int Handle, IReadOnlyList<Id> Layers)> SetLayersCalls = new List<(int, IReadOnlyList<Id>)>();
        public readonly Dictionary<int, TransformRecord> Transforms = new Dictionary<int, TransformRecord>();
        public readonly Dictionary<int, Dictionary<string, double>> ShaderParams = new Dictionary<int, Dictionary<string, double>>();
        public readonly Dictionary<int, (Id EffectId, Vec2 Position)> EmittedParticles = new Dictionary<int, (Id, Vec2)>();

        /// <summary>ADR-0074 新增：每个粒子句柄发射时实际传入的混合模式，供测试断言
        /// <c>VfxDef.BlendMode</c> 是否被真正透传到 <see cref="IRenderer2D"/> 这一层，不依赖接口默认
        /// 实现（见 <see cref="EmitParticle(Id, Vec2, IReadOnlyDictionary{string, double}, VfxBlendMode)"/>）。</summary>
        public readonly Dictionary<int, VfxBlendMode> ParticleBlendModes = new Dictionary<int, VfxBlendMode>();

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
            SetLayersCalls.Add((handle.Value, layers));
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

        public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters) =>
            EmitParticle(effectId, position, parameters, VfxBlendMode.Alpha);

        /// <summary>ADR-0074 新增重载：显式实现（不依赖 <see cref="IRenderer2D"/> 的默认转发），记下
        /// <paramref name="blendMode"/> 供测试断言（见 <see cref="ParticleBlendModes"/>）。三参旧重载
        /// 改为转发到本方法并固定传 <see cref="VfxBlendMode.Alpha"/>，签名本身不变。</summary>
        public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters, VfxBlendMode blendMode)
        {
            var handle = new ParticleHandle(_nextParticleHandle++);
            _aliveParticles.Add(handle.Value);
            EmittedParticles[handle.Value] = (effectId, position);
            ParticleBlendModes[handle.Value] = blendMode;
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

        /// <summary>测试专用只读查询：句柄当前是否仍存活（未销毁/未停止），供断言
        /// <c>stop_vfx</c>/自然超时一类回收路径确实生效（见 ADR-0075）。</summary>
        public bool IsParticleAlive(ParticleHandle handle) => _aliveParticles.Contains(handle.Value);

        private void EnsureSpriteAlive(SpriteHandle handle)
        {
            if (!_aliveSprites.Contains(handle.Value))
            {
                throw new InvalidOperationException($"精灵句柄 {handle.Value} 已销毁或不存在");
            }
        }

        // ADR-0080：地图分层图建/销测试替身。与本类型其余成员同一惯例——显式实现（不依赖
        // IRenderer2D 的默认转发），记录调用供测试断言；新增 SetMapLayerAvailable 让测试模拟
        // "某地图的某一层图片实际不存在"（例如可选层 decal 缺失），不依赖真实文件系统。

        /// <summary>一次 CreateMapLayerInstance 调用的完整参数记录，按调用顺序追加（含返回无效句柄
        /// 的尝试），供测试断言"确实调用过、但因为该层不存在没有建出实例"这类否定断言。</summary>
        public readonly struct MapLayerCreateRecord
        {
            public readonly Id MapId;
            public readonly MapLayerKind Layer;
            public readonly Rect WorldBounds;
            public readonly int RenderLayer;
            public readonly MapLayerHandle Handle;

            public MapLayerCreateRecord(Id mapId, MapLayerKind layer, Rect worldBounds, int renderLayer, MapLayerHandle handle)
            {
                MapId = mapId;
                Layer = layer;
                WorldBounds = worldBounds;
                RenderLayer = renderLayer;
                Handle = handle;
            }
        }

        private int _nextMapLayerHandle = 1;
        private readonly HashSet<int> _aliveMapLayers = new HashSet<int>();

        /// <summary>测试专用：默认全部三层均"可用"（返回有效句柄）；测试可用
        /// <see cref="SetMapLayerAvailable"/> 显式标记某地图的某一层不可用，模拟该层图片文件不存在
        /// （decal 可选层缺失场景）。</summary>
        private readonly HashSet<(Id MapId, MapLayerKind Layer)> _unavailableMapLayers =
            new HashSet<(Id, MapLayerKind)>();

        public readonly List<MapLayerCreateRecord> MapLayerCreateCalls = new List<MapLayerCreateRecord>();
        public readonly List<MapLayerHandle> MapLayerDestroyCalls = new List<MapLayerHandle>();

        /// <summary>测试用：标记 <paramref name="mapId"/> 的 <paramref name="layer"/> 层图片不存在——
        /// 后续 <see cref="CreateMapLayerInstance"/> 对该 (地图, 层) 组合返回无效句柄，不计入存活
        /// 集合。默认（不调用本方法）全部层均视为存在。</summary>
        public void SetMapLayerAvailable(Id mapId, MapLayerKind layer, bool available)
        {
            if (available)
            {
                _unavailableMapLayers.Remove((mapId, layer));
            }
            else
            {
                _unavailableMapLayers.Add((mapId, layer));
            }
        }

        public MapLayerHandle CreateMapLayerInstance(Id mapId, MapLayerKind layer, Rect worldBounds, int renderLayer)
        {
            if (_unavailableMapLayers.Contains((mapId, layer)))
            {
                MapLayerCreateCalls.Add(new MapLayerCreateRecord(mapId, layer, worldBounds, renderLayer, default));
                return default;
            }

            var handle = new MapLayerHandle(_nextMapLayerHandle++);
            _aliveMapLayers.Add(handle.Value);
            MapLayerCreateCalls.Add(new MapLayerCreateRecord(mapId, layer, worldBounds, renderLayer, handle));
            return handle;
        }

        public void DestroyMapLayerInstance(MapLayerHandle handle)
        {
            MapLayerDestroyCalls.Add(handle);
            if (!handle.IsValid)
            {
                return;
            }
            _aliveMapLayers.Remove(handle.Value);
        }

        /// <summary>测试专用只读查询：当前存活（已建未销毁）的地图分层图实例数。</summary>
        public int AliveMapLayerCount => _aliveMapLayers.Count;
    }
}

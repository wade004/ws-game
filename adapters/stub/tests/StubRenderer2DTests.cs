using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.StubAdapters
{
    public class StubRenderer2DTests
    {
        private static readonly Id SpriteSet = new Id("spr.probe");
        private static readonly IReadOnlyDictionary<string, double> NoParams = new Dictionary<string, double>();

        // ---- 精灵实例 ----

        [Fact]
        public void CreateSpriteInstance_HandlesAreDistinctIncreasingAndNeverZero_RecordingSpriteSet()
        {
            var r = new StubRenderer2D();
            var a = r.CreateSpriteInstance(SpriteSet);
            var b = r.CreateSpriteInstance(new Id("spr.other"));

            Assert.True(a.Value > 0, "0 保留不用");
            Assert.True(b.Value > a.Value);
            Assert.Equal(SpriteSet, r.CreatedSpriteSets[a.Value]);
            Assert.Equal(new Id("spr.other"), r.CreatedSpriteSets[b.Value]);
        }

        [Fact]
        public void SetLayers_LayersKeepsLatest_ButSetLayersCallsKeepsEveryCallInOrder()
        {
            var r = new StubRenderer2D();
            var h = r.CreateSpriteInstance(SpriteSet);
            var first = new[] { new Id("lay.a") };
            var second = new[] { new Id("lay.a"), new Id("lay.b") };

            r.SetLayers(h, first);
            r.SetLayers(h, second);
            r.SetLayers(h, second);   // 相同内容再调一次也要计一次调用（供“是否补触发”断言）

            Assert.Equal(second, r.Layers[h.Value]);
            Assert.Equal(3, r.SetLayersCalls.Count);
            Assert.All(r.SetLayersCalls, c => Assert.Equal(h.Value, c.Handle));
            Assert.Equal(first, r.SetLayersCalls[0].Layers);
            Assert.Equal(second, r.SetLayersCalls[1].Layers);
        }

        [Fact]
        public void SetTransform_RecordsEveryField_PerHandle_LatestWins()
        {
            var r = new StubRenderer2D();
            var a = r.CreateSpriteInstance(SpriteSet);
            var b = r.CreateSpriteInstance(SpriteSet);

            r.SetTransform(a, new Vec2(1, 2), 0.5, 2.0, 3, 90.0, 1.5, flipX: true);
            r.SetTransform(b, new Vec2(9, 9), 0, 0, 0, 0, 1, flipX: false);
            r.SetTransform(a, new Vec2(4, 5), 0.25, 5.0, 1, 45.0, 2.0, flipX: false);

            var rec = r.Transforms[a.Value];
            Assert.Equal(new Vec2(4, 5), rec.Position);
            Assert.Equal(0.25, rec.Height);
            Assert.Equal(5.0, rec.SortY);
            Assert.Equal(1, rec.Layer);
            Assert.Equal(45.0, rec.Rotation);
            Assert.Equal(2.0, rec.Scale);
            Assert.False(rec.FlipX);
            Assert.Equal(new Vec2(9, 9), r.Transforms[b.Value].Position);
        }

        [Fact]
        public void SetShaderParam_AccumulatesPerHandleByName_LatestValuePerNameWins()
        {
            var r = new StubRenderer2D();
            var a = r.CreateSpriteInstance(SpriteSet);
            var b = r.CreateSpriteInstance(SpriteSet);

            r.SetShaderParam(a, "flash", 0.2);
            r.SetShaderParam(a, "tint", 1.0);
            r.SetShaderParam(a, "flash", 0.9);
            r.SetShaderParam(b, "flash", 0.1);

            Assert.Equal(0.9, r.ShaderParams[a.Value]["flash"]);
            Assert.Equal(1.0, r.ShaderParams[a.Value]["tint"]);
            Assert.Equal(2, r.ShaderParams[a.Value].Count);
            Assert.Equal(0.1, r.ShaderParams[b.Value]["flash"]);
            // 名称区分大小写（Ordinal）。
            r.SetShaderParam(a, "Flash", 5.0);
            Assert.Equal(0.9, r.ShaderParams[a.Value]["flash"]);
        }

        [Fact]
        public void SetShadow_And_SetSortIdentity_RecordLatestPerHandle()
        {
            var r = new StubRenderer2D();
            var h = r.CreateSpriteInstance(SpriteSet);

            r.SetShadow(h, ShadowMode.Blob);
            r.SetShadow(h, ShadowMode.None);
            r.SetSortIdentity(h, new Id("unit.one"));
            r.SetSortIdentity(h, new Id("unit.two"));

            Assert.Equal(ShadowMode.None, r.Shadows[h.Value]);
            Assert.Equal(new Id("unit.two"), r.SortIdentities[h.Value]);
        }

        [Fact]
        public void CompareDrawOrder_StubHasNoReadbackAndKeepsInterfaceDefaultZero()
        {
            var r = new StubRenderer2D();
            var a = r.CreateSpriteInstance(SpriteSet);
            var b = r.CreateSpriteInstance(SpriteSet);
            r.SetSortIdentity(a, new Id("unit.a"));
            r.SetSortIdentity(b, new Id("unit.b"));

            IRenderer2D asInterface = r;
            Assert.Equal(0, asInterface.CompareDrawOrder(a, b));
        }

        public static IEnumerable<object[]> OperationsOnHandle()
        {
            yield return new object[] { "SetLayers", (Action<StubRenderer2D, SpriteHandle>)((r, h) => r.SetLayers(h, new[] { new Id("lay.a") })) };
            yield return new object[] { "SetTransform", (Action<StubRenderer2D, SpriteHandle>)((r, h) => r.SetTransform(h, Vec2.Zero, 0, 0, 0, 0, 1, false)) };
            yield return new object[] { "SetShaderParam", (Action<StubRenderer2D, SpriteHandle>)((r, h) => r.SetShaderParam(h, "p", 1)) };
            yield return new object[] { "SetShadow", (Action<StubRenderer2D, SpriteHandle>)((r, h) => r.SetShadow(h, ShadowMode.Blob)) };
            yield return new object[] { "SetSortIdentity", (Action<StubRenderer2D, SpriteHandle>)((r, h) => r.SetSortIdentity(h, new Id("unit.x"))) };
            yield return new object[] { "DestroySpriteInstance", (Action<StubRenderer2D, SpriteHandle>)((r, h) => r.DestroySpriteInstance(h)) };
        }

        [Theory]
        [MemberData(nameof(OperationsOnHandle))]
        public void EveryHandleOperation_AfterDestroy_ThrowsInvalidOperation_ForcingEarlyDetectionOfUseAfterDestroy(
            string operation, Action<StubRenderer2D, SpriteHandle> op)
        {
            var r = new StubRenderer2D();
            var h = r.CreateSpriteInstance(SpriteSet);
            r.DestroySpriteInstance(h);

            var ex = Assert.Throws<InvalidOperationException>(() => op(r, h));
            Assert.Contains(h.Value.ToString(), ex.Message);
            Assert.False(string.IsNullOrEmpty(operation));
        }

        [Theory]
        [MemberData(nameof(OperationsOnHandle))]
        public void EveryHandleOperation_OnNeverCreatedHandle_ThrowsInvalidOperation(
            string operation, Action<StubRenderer2D, SpriteHandle> op)
        {
            var r = new StubRenderer2D();
            Assert.Throws<InvalidOperationException>(() => op(r, new SpriteHandle(int.MaxValue)));
            Assert.False(string.IsNullOrEmpty(operation));
        }

        [Fact]
        public void DestroySpriteInstance_OnlyAffectsThatHandle()
        {
            var r = new StubRenderer2D();
            var a = r.CreateSpriteInstance(SpriteSet);
            var b = r.CreateSpriteInstance(SpriteSet);
            r.DestroySpriteInstance(a);

            var ex = Record.Exception(() => r.SetTransform(b, Vec2.Zero, 0, 0, 0, 0, 1, false));
            Assert.Null(ex);
        }

        // ---- 粒子 ----

        [Fact]
        public void EmitParticle_ThreeArgOverload_RecordsAlphaBlend_FourArgRecordsGivenMode()
        {
            var r = new StubRenderer2D();
            var plain = r.EmitParticle(new Id("fx.hit"), new Vec2(1, 1), NoParams);
            var additive = r.EmitParticle(new Id("fx.glow"), new Vec2(2, 2), NoParams, VfxBlendMode.Additive);

            Assert.True(plain.Value > 0);
            Assert.True(additive.Value > plain.Value);
            Assert.Equal(VfxBlendMode.Alpha, r.ParticleBlendModes[plain.Value]);
            Assert.Equal(VfxBlendMode.Additive, r.ParticleBlendModes[additive.Value]);
            Assert.Equal((new Id("fx.glow"), new Vec2(2, 2)), r.EmittedParticles[additive.Value]);
        }

        [Fact]
        public void StopParticle_MakesItDead_SecondStopAndUnknownHandleThrow()
        {
            var r = new StubRenderer2D();
            var p = r.EmitParticle(new Id("fx.hit"), Vec2.Zero, NoParams);
            Assert.True(r.IsParticleAlive(p));

            r.StopParticle(p);
            Assert.False(r.IsParticleAlive(p));
            Assert.Throws<InvalidOperationException>(() => r.StopParticle(p));
            Assert.Throws<InvalidOperationException>(() => r.StopParticle(new ParticleHandle(int.MaxValue)));
            Assert.False(r.IsParticleAlive(new ParticleHandle(int.MaxValue)));
        }

        [Fact]
        public void ParticleAndSpriteHandleSpaces_AreIndependent()
        {
            var r = new StubRenderer2D();
            var sprite = r.CreateSpriteInstance(SpriteSet);
            var particle = r.EmitParticle(new Id("fx.hit"), Vec2.Zero, NoParams);

            // 同为序号 1，但粒子句柄不能被当作精灵句柄使用，反之亦然。
            Assert.Equal(sprite.Value, particle.Value);
            r.StopParticle(particle);
            Assert.Null(Record.Exception(() => r.SetShadow(sprite, ShadowMode.Blob)));
        }

        // ---- 地图分层 ----

        [Fact]
        public void MapLayers_DefaultAvailable_ValidHandlesIncreasing_CountTracksAliveOnes()
        {
            var r = new StubRenderer2D();
            var map = new Id("map.probe");
            var bounds = new Rect(new Vec2(0, 0), new Vec2(10, 10));

            var ground = r.CreateMapLayerInstance(map, MapLayerKind.Ground, bounds, 0);
            var overlay = r.CreateMapLayerInstance(map, MapLayerKind.Overlay, bounds, 5);

            Assert.True(ground.IsValid);
            Assert.True(overlay.IsValid);
            Assert.True(overlay.Value > ground.Value);
            Assert.Equal(2, r.AliveMapLayerCount);

            r.DestroyMapLayerInstance(ground);
            Assert.Equal(1, r.AliveMapLayerCount);
            Assert.Equal(new[] { ground }, r.MapLayerDestroyCalls);
        }

        [Fact]
        public void MapLayers_UnavailableLayer_ReturnsInvalidHandle_ButTheAttemptIsStillRecorded()
        {
            var r = new StubRenderer2D();
            var map = new Id("map.probe");
            var bounds = new Rect(new Vec2(0, 0), new Vec2(1, 1));
            r.SetMapLayerAvailable(map, MapLayerKind.Decal, available: false);

            var decal = r.CreateMapLayerInstance(map, MapLayerKind.Decal, bounds, 2);
            var ground = r.CreateMapLayerInstance(map, MapLayerKind.Ground, bounds, 0);

            Assert.False(decal.IsValid);
            Assert.True(ground.IsValid);
            Assert.Equal(1, r.AliveMapLayerCount);
            Assert.Equal(2, r.MapLayerCreateCalls.Count);
            var attempt = r.MapLayerCreateCalls[0];
            Assert.Equal(MapLayerKind.Decal, attempt.Layer);
            Assert.False(attempt.Handle.IsValid);
            Assert.Equal(bounds, attempt.WorldBounds);
            Assert.Equal(2, attempt.RenderLayer);
            // 无效句柄不占用序号：第一个有效句柄仍是最小序号。
            Assert.Equal(1, ground.Value);
        }

        [Fact]
        public void MapLayers_AvailabilityIsPerMapAndPerLayer_AndCanBeRestored()
        {
            var r = new StubRenderer2D();
            var mapA = new Id("map.a");
            var mapB = new Id("map.b");
            var bounds = new Rect(Vec2.Zero, new Vec2(1, 1));
            r.SetMapLayerAvailable(mapA, MapLayerKind.Decal, false);

            Assert.False(r.CreateMapLayerInstance(mapA, MapLayerKind.Decal, bounds, 0).IsValid);
            Assert.True(r.CreateMapLayerInstance(mapB, MapLayerKind.Decal, bounds, 0).IsValid);
            Assert.True(r.CreateMapLayerInstance(mapA, MapLayerKind.Ground, bounds, 0).IsValid);

            r.SetMapLayerAvailable(mapA, MapLayerKind.Decal, true);
            Assert.True(r.CreateMapLayerInstance(mapA, MapLayerKind.Decal, bounds, 0).IsValid);
        }

        [Fact]
        public void DestroyMapLayerInstance_InvalidOrRepeatedHandle_IsRecordedButDoesNotChangeAliveCount()
        {
            var r = new StubRenderer2D();
            var bounds = new Rect(Vec2.Zero, new Vec2(1, 1));
            var h = r.CreateMapLayerInstance(new Id("map.a"), MapLayerKind.Ground, bounds, 0);

            r.DestroyMapLayerInstance(default);
            Assert.Equal(1, r.AliveMapLayerCount);

            r.DestroyMapLayerInstance(h);
            r.DestroyMapLayerInstance(h);
            Assert.Equal(0, r.AliveMapLayerCount);
            Assert.Equal(3, r.MapLayerDestroyCalls.Count);
        }

        [Fact]
        public void MapLayerCreateCalls_PreserveCallOrderAcrossLayersAndMaps()
        {
            var r = new StubRenderer2D();
            var bounds = new Rect(Vec2.Zero, new Vec2(1, 1));
            var calls = new[]
            {
                (new Id("map.a"), MapLayerKind.Ground),
                (new Id("map.a"), MapLayerKind.Overlay),
                (new Id("map.b"), MapLayerKind.Ground),
            };
            foreach (var (map, kind) in calls)
            {
                r.CreateMapLayerInstance(map, kind, bounds, 0);
            }

            Assert.Equal(calls, r.MapLayerCreateCalls.Select(c => (c.MapId, c.Layer)).ToArray());
        }
    }
}

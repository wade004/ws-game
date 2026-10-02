// ParticleFreezeFallbackRenderer2DTests：手感落地 M4-W3——没有 IParticleFreezer 的适配层的通用兜底（装饰器：停止并按原参数重发 / 宿主给的时间缩放口）。
// 复现用例：既有 StubRenderer2D 不实现 IParticleFreezer，顿帧里画面上的粒子数不变（1 → 1，粒子继续播放）；包一层之后冻结期间画面上的粒子 1 → 0，解冻后 0 → 1（按原特效、
// 原位置、原混合模式重发）。不变量：虚拟句柄在暂停/恢复前后不变、幂等、暂停期间位置更新只记录、时间缩放口优先且不消失不重播、停止/未知句柄的处理、
// 已实现 IParticleFreezer 的适配层不叠第二层、可选的 IParticleRepositioner 探测结果与不包装时一致。期望值由规则算出（暂停期间推进的时间 vs 剩余 lifetime），不写死裸数。
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class ParticleFreezeFallbackRenderer2DTests
    {
        private static readonly Id AuraVfx = new Id("vfx.fallback_aura");
        private static readonly Id GlowVfx = new Id("vfx.fallback_glow");
        private static readonly Id TimedVfx = new Id("vfx.fallback_timed");
        private static readonly Id HitUnit = new Id("unit.fallback_hit");
        private static readonly Id Bystander = new Id("unit.fallback_bystander");
        private static readonly Id Anchor = new Id("anchor.chest");

        private const double TimedLifetime = 1.0;

        private static Dictionary<Id, VfxDef> Catalog() => new Dictionary<Id, VfxDef>
        {
            [AuraVfx] = new VfxDef(AuraVfx, "buff", VfxAttachMode.Anchor, lifetime: null, new Id("res.fallback_aura")),
            [GlowVfx] = new VfxDef(GlowVfx, "buff", VfxAttachMode.Anchor, lifetime: null, new Id("res.fallback_glow"), blendMode: VfxBlendMode.Additive),
            [TimedVfx] = new VfxDef(TimedVfx, "dot", VfxAttachMode.Anchor, TimedLifetime, new Id("res.fallback_timed")),
        };

        private static VfxPlayer NewPlayer(IRenderer2D renderer) =>
            new VfxPlayer(
                renderer, new StubCamera(), Catalog(),
                anchorResolver: (e, a) => new Vec2(1, 1),
                entityPositionResolver: e => new Vec2(2, 2));

        private static ParticleHandle Spawn(VfxPlayer player, Id vfx, Id owner)
        {
            var handle = player.Spawn(vfx, VfxAttach.Anchor(owner, Anchor), null);
            Assert.NotNull(handle);
            return handle!.Value;
        }

        private static int AliveCount(StubRenderer2D stub) => stub.EmittedParticles.Keys.Count(k => stub.IsParticleAlive(new ParticleHandle(k)));

        /// <summary>带位置读回的适配层替身：实现 IParticleRepositioner（组合一个 StubRenderer2D 逐个转发），记录每个真实句柄的最近位置。</summary>
        private sealed class RepositionableStubRenderer2D : IRenderer2D, IParticleRepositioner
        {
            public readonly StubRenderer2D Inner = new StubRenderer2D();
            public readonly Dictionary<int, Vec2> Positions = new Dictionary<int, Vec2>();

            public SpriteHandle CreateSpriteInstance(Id spriteSetId) => Inner.CreateSpriteInstance(spriteSetId);

            public void SetLayers(SpriteHandle handle, IReadOnlyList<Id> layers) => Inner.SetLayers(handle, layers);

            public void SetTransform(SpriteHandle handle, Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX) =>
                Inner.SetTransform(handle, position, height, sortY, layer, rotation, scale, flipX);

            public void SetShaderParam(SpriteHandle handle, string paramName, double value) => Inner.SetShaderParam(handle, paramName, value);

            public void SetShadow(SpriteHandle handle, ShadowMode mode) => Inner.SetShadow(handle, mode);

            public void DestroySpriteInstance(SpriteHandle handle) => Inner.DestroySpriteInstance(handle);

            public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters)
            {
                var handle = Inner.EmitParticle(effectId, position, parameters);
                Positions[handle.Value] = position;
                return handle;
            }

            public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters, VfxBlendMode blendMode)
            {
                var handle = Inner.EmitParticle(effectId, position, parameters, blendMode);
                Positions[handle.Value] = position;
                return handle;
            }

            public void StopParticle(ParticleHandle handle) => Inner.StopParticle(handle);

            public void SetParticlePosition(ParticleHandle handle, Vec2 position)
            {
                if (Inner.IsParticleAlive(handle)) Positions[handle.Value] = position;
            }
        }

        // ---- 复现 ----

        /// <summary>
        /// 复现用例：不包装时，既有 <c>StubRenderer2D</c>（没有 <c>IParticleFreezer</c>）上冻结单位的特效对画面毫无影响——冻结期间画面上粒子数 1 → 1（继续播放）；
        /// 包一层兜底后冻结期间 1 → 0（视觉暂停），解冻后 0 → 1（按原特效、位置、混合模式重发），虚拟句柄不变、随后正常停止回收。
        /// </summary>
        [Fact]
        public void Freeze_OnAnAdapterWithoutAFreezer_HidesTheParticleUntilRelease_AndReemitsWithTheOriginalArguments()
        {
            var plain = new StubRenderer2D();
            var plainPlayer = NewPlayer(plain);
            Spawn(plainPlayer, GlowVfx, HitUnit);
            plainPlayer.SetOwnerFrozen(HitUnit, true);
            Assert.Equal(1, AliveCount(plain)); // 不包装：冻结对画面没有任何作用

            var inner = new StubRenderer2D();
            var renderer = ParticleFreezeFallbackRenderer2D.Wrap(inner);
            Assert.IsAssignableFrom<IParticleFreezer>(renderer);
            var player = NewPlayer(renderer);
            var handle = Spawn(player, GlowVfx, HitUnit);
            var before = inner.EmittedParticles.Single();
            Assert.Equal(1, AliveCount(inner));

            player.SetOwnerFrozen(HitUnit, true);
            Assert.Equal(0, AliveCount(inner));

            player.SetOwnerFrozen(HitUnit, false);
            Assert.Equal(1, AliveCount(inner));
            var again = inner.EmittedParticles.Where(kv => inner.IsParticleAlive(new ParticleHandle(kv.Key))).Single();
            Assert.NotEqual(before.Key, again.Key); // 真实句柄换了（重新发射）
            Assert.Equal(before.Value.EffectId, again.Value.EffectId);
            Assert.Equal(before.Value.Position, again.Value.Position);
            Assert.Equal(VfxBlendMode.Additive, inner.ParticleBlendModes[again.Key]);

            player.Stop(handle); // 虚拟句柄对 VfxPlayer 始终有效
            Assert.Equal(0, AliveCount(inner));
            Assert.Equal(0, ((ParticleFreezeFallbackRenderer2D)renderer).TrackedCount);
        }

        /// <summary>
        /// 不变量：旁观单位的特效不暂停；冻结期间存活计时仍停住、解冻后从冻结点继续（暂停原语与逻辑侧停表互不依赖）：
        /// 冻结前走了 b 秒，冻结期间过去 10 倍 lifetime，解冻后在剩余时长内仍在、走完后被回收。
        /// </summary>
        [Fact]
        public void Freeze_OnlyAffectsTheFrozenOwner_AndTheLifetimeStillResumesFromTheFreezePoint()
        {
            var inner = new StubRenderer2D();
            var player = NewPlayer(ParticleFreezeFallbackRenderer2D.Wrap(inner));
            var onHit = Spawn(player, TimedVfx, HitUnit);
            Spawn(player, AuraVfx, Bystander);
            Assert.Equal(2, AliveCount(inner));

            const double beforeFreeze = 0.4;
            player.Update(beforeFreeze);
            player.SetOwnerFrozen(HitUnit, true);
            Assert.Equal(1, AliveCount(inner)); // 只有旁观者的还在画面上
            player.Update(10 * TimedLifetime);
            Assert.Equal(1, AliveCount(inner));

            player.SetOwnerFrozen(HitUnit, false);
            Assert.Equal(2, AliveCount(inner));
            var remaining = TimedLifetime - beforeFreeze;
            player.Update(remaining - 0.05);
            Assert.True(AliveCount(inner) == 2, "解冻后从冻结点继续倒计时");
            player.Update(0.1);
            Assert.True(AliveCount(inner) == 1, "剩余 lifetime 走完后被回收，旁观者的持续特效仍在");
            Assert.NotEqual(0, onHit.Value);
        }

        // ---- 不变量 ----

        [Fact]
        public void PauseAndResume_AreIdempotent_NoRepeatedStopOrEmit()
        {
            var inner = new StubRenderer2D();
            var decorator = new ParticleFreezeFallbackRenderer2D(inner);
            var handle = decorator.EmitParticle(new Id("res.x"), new Vec2(3, 4), new Dictionary<string, double> { ["k"] = 1 });
            Assert.Single(inner.EmittedParticles);

            decorator.SetParticlePaused(handle, true);
            decorator.SetParticlePaused(handle, true);
            Assert.True(decorator.IsPaused(handle));
            Assert.Equal(0, AliveCount(inner));

            decorator.SetParticlePaused(handle, false);
            decorator.SetParticlePaused(handle, false);
            Assert.False(decorator.IsPaused(handle));
            Assert.Equal(2, inner.EmittedParticles.Count); // 一次暂停-恢复只重发一次
            Assert.Equal(1, AliveCount(inner));
        }

        [Fact]
        public void StopWhilePaused_DoesNotTouchTheInner_AndLaterCallsOnTheStoppedHandleAreIgnored()
        {
            var inner = new StubRenderer2D();
            var decorator = new ParticleFreezeFallbackRenderer2D(inner);
            var handle = decorator.EmitParticle(new Id("res.x"), Vec2.Zero, new Dictionary<string, double>());
            decorator.SetParticlePaused(handle, true);

            decorator.StopParticle(handle);
            Assert.Equal(0, decorator.TrackedCount);
            decorator.SetParticlePaused(handle, false); // 已停止：静默忽略，不重发
            Assert.Single(inner.EmittedParticles);
            decorator.StopParticle(handle); // 重复停止：不认识的句柄静默忽略（不转发——虚拟句柄与真实句柄数值可能重合，转发会误停别的粒子）
            var other = decorator.EmitParticle(new Id("res.y"), Vec2.Zero, new Dictionary<string, double>());
            decorator.StopParticle(handle);
            Assert.Equal(1, AliveCount(inner)); // other 没被误停
            Assert.True(decorator.TrackedCount == 1 && !decorator.IsPaused(other));
        }

        [Fact]
        public void TimeScaleHook_IsPreferred_TheParticleStaysAndNothingIsReemitted()
        {
            var inner = new StubRenderer2D();
            var calls = new List<(int Real, double Scale)>();
            var decorator = new ParticleFreezeFallbackRenderer2D(
                inner, new ParticleFreezeFallbackOptions { SetTimeScale = (h, s) => calls.Add((h.Value, s)) });
            var handle = decorator.EmitParticle(new Id("res.x"), Vec2.Zero, new Dictionary<string, double>());
            var real = inner.EmittedParticles.Keys.Single();

            decorator.SetParticlePaused(handle, true);
            Assert.Equal(1, AliveCount(inner)); // 不消失
            decorator.SetParticlePaused(handle, false);

            Assert.Equal(new[] { (real, 0.0), (real, 1.0) }, calls);
            Assert.Single(inner.EmittedParticles); // 不重播
        }

        [Fact]
        public void Wrap_ReturnsAnAdapterThatAlreadyFreezesAsIs_AndOtherwiseMirrorsTheOptionalRepositioner()
        {
            var capable = new FreezeCapableStubRenderer2D();
            Assert.Same(capable, ParticleFreezeFallbackRenderer2D.Wrap(capable));

            var plain = ParticleFreezeFallbackRenderer2D.Wrap(new StubRenderer2D());
            Assert.IsAssignableFrom<IParticleFreezer>(plain);
            Assert.False(plain is IParticleRepositioner, "被包装者没有位置能力：不对外声称有，VfxPlayer 的跟随探测与不包装时一致");

            var repositionable = ParticleFreezeFallbackRenderer2D.Wrap(new RepositionableStubRenderer2D());
            Assert.IsAssignableFrom<IParticleFreezer>(repositionable);
            Assert.IsAssignableFrom<IParticleRepositioner>(repositionable);
        }

        /// <summary>
        /// 不变量（位置透传）：未暂停时位置更新原样交给真实句柄；暂停期间只记录、不碰被停止的真实句柄，恢复时按最新位置重发（暂停期间位置从 p0 变到 p1，重发点 = p1）。
        /// </summary>
        [Fact]
        public void PositionUpdates_PassThroughWhenLive_AndAreRecordedWhilePaused()
        {
            var inner = new RepositionableStubRenderer2D();
            var renderer = ParticleFreezeFallbackRenderer2D.Wrap(inner);
            var repositioner = (IParticleRepositioner)renderer;
            var freezer = (IParticleFreezer)renderer;
            var p0 = new Vec2(1, 1);
            var p1 = new Vec2(5, 6);
            var p2 = new Vec2(7, 8);
            var handle = renderer.EmitParticle(new Id("res.x"), p0, new Dictionary<string, double>());
            var real0 = inner.Inner.EmittedParticles.Keys.Single();

            repositioner.SetParticlePosition(handle, p1);
            Assert.Equal(p1, inner.Positions[real0]);

            freezer.SetParticlePaused(handle, true);
            repositioner.SetParticlePosition(handle, p2); // 暂停期间只记录
            Assert.Equal(p1, inner.Positions[real0]);

            freezer.SetParticlePaused(handle, false);
            var real1 = inner.Inner.EmittedParticles.Keys.Single(k => k != real0);
            Assert.Equal(p2, inner.Positions[real1]);
        }

        [Fact]
        public void EverythingElse_PassesThroughToTheWrappedRenderer()
        {
            var inner = new StubRenderer2D();
            var renderer = ParticleFreezeFallbackRenderer2D.Wrap(inner);
            var sprite = renderer.CreateSpriteInstance(new Id("spriteset.x"));
            renderer.SetTransform(sprite, new Vec2(2, 3), 0.5, 3, 1, 0, 1, false);
            Assert.Equal(new Id("spriteset.x"), inner.CreatedSpriteSets[sprite.Value]);
            Assert.Equal(new Vec2(2, 3), inner.Transforms[sprite.Value].Position);
            renderer.SetSortIdentity(sprite, new Id("unit.sorted"));
            Assert.Equal(new Id("unit.sorted"), inner.SortIdentities[sprite.Value]);
            renderer.DestroySpriteInstance(sprite);
            Assert.Throws<System.InvalidOperationException>(() => renderer.SetTransform(sprite, Vec2.Zero, 0, 0, 0, 0, 1, false));
        }
    }
}

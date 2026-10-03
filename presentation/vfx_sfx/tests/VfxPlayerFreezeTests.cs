// VfxPlayerFreezeTests：手感落地 M3-C——局部顿帧期间，属于被冻结单位的粒子/特效暂停、结束恢复，其它单位的不受影响
// （手感设计/07 第 5 节、IVfxFreezable 判断记录）。复现用例（被冻结单位的特效暂停、旁观单位的不暂停）与不变量用例（幂等、lifetime
// 倒计时随暂停停住、world 挂接永不冻、冷加载补发与热路径同一出口、没有暂停能力的引擎适配层视觉上不暂停但逻辑侧仍停表，手感落地 M4-G）各自成组；
// 期望值全部由规则算出（暂停期间推进的时间 vs 剩余 lifetime），不写死裸数。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class VfxPlayerFreezeTests
    {
        private static readonly Id AuraVfx = new Id("vfx.freeze_aura");
        private static readonly Id TimedVfx = new Id("vfx.freeze_timed");
        private static readonly Id FlashVfx = new Id("vfx.freeze_flash");
        private static readonly Id SocketVfx = new Id("vfx.freeze_socket");
        private static readonly Id AuraRes = new Id("res.freeze_aura");
        private static readonly Id Anchor = new Id("anchor.chest");
        private static readonly Id HitUnit = new Id("unit.freeze_hit");
        private static readonly Id Bystander = new Id("unit.freeze_bystander");

        private const double TimedLifetime = 1.0;

        private static Dictionary<Id, VfxDef> Catalog() => new Dictionary<Id, VfxDef>
        {
            [AuraVfx] = new VfxDef(AuraVfx, "buff", VfxAttachMode.Anchor, lifetime: null, AuraRes),
            [TimedVfx] = new VfxDef(TimedVfx, "dot", VfxAttachMode.Anchor, TimedLifetime, new Id("res.freeze_timed")),
            [FlashVfx] = new VfxDef(FlashVfx, "impact", VfxAttachMode.World, lifetime: null, new Id("res.freeze_flash")),
            [SocketVfx] = new VfxDef(SocketVfx, "weapon", VfxAttachMode.Socket, lifetime: null, new Id("res.freeze_socket")),
        };

        private static VfxPlayer NewPlayer(IRenderer2D renderer, StubResourceLoader? loader = null, StubRenderer3D? r3 = null, ModelHandle? host = null) =>
            new VfxPlayer(
                renderer, new StubCamera(), Catalog(),
                anchorResolver: (e, a) => new Vec2(1, 1),
                entityPositionResolver: e => new Vec2(2, 2),
                resourceLoader: loader,
                renderer3D: r3,
                modelHandleResolver: host.HasValue ? (e => e.Equals(HitUnit) ? host : (ModelHandle?)null) : (ModelHandleResolver?)null);

        private static ParticleHandle SpawnAnchor(VfxPlayer player, Id vfx, Id owner)
        {
            var handle = player.Spawn(vfx, VfxAttach.Anchor(owner, Anchor), null);
            Assert.NotNull(handle);
            return handle!.Value;
        }

        // ---- 复现 ----

        [Fact]
        public void OwnerFrozen_PausesOnlyThatOwnersParticles_ResumesOnRelease()
        {
            var renderer = new FreezeCapableStubRenderer2D();
            var player = NewPlayer(renderer);
            var onHit = SpawnAnchor(player, AuraVfx, HitUnit);
            var onBystander = SpawnAnchor(player, AuraVfx, Bystander);

            Assert.False(renderer.IsPaused(onHit));

            ((IVfxFreezable)player).SetOwnerFrozen(HitUnit, true);

            Assert.True(renderer.IsPaused(onHit), "被冻结单位名下的特效应暂停");
            Assert.False(renderer.IsPaused(onBystander), "旁观单位的特效不受影响");
            Assert.True(player.IsOwnerFrozen(HitUnit));
            Assert.False(player.IsOwnerFrozen(Bystander));

            player.SetOwnerFrozen(HitUnit, false);

            Assert.False(renderer.IsPaused(onHit), "解冻后恢复");
            Assert.Equal(1, renderer.PauseCallCount);
            Assert.Equal(1, renderer.ResumeCallCount);
        }

        [Fact]
        public void OwnerFrozen_HitFlashAttachedToWorld_NeverPaused()
        {
            // 命中闪光这类以接触点/世界坐标播放的一次性特效没有宿主单位，不随顿帧暂停（IVfxFreezable 判断记录）。
            var renderer = new FreezeCapableStubRenderer2D();
            var player = NewPlayer(renderer);
            var flash = player.Spawn(FlashVfx, VfxAttach.World(new Vec2(3, 3)), null)!.Value;
            var aura = SpawnAnchor(player, AuraVfx, HitUnit);

            player.SetOwnerFrozen(HitUnit, true);

            Assert.False(renderer.IsPaused(flash));
            Assert.True(renderer.IsPaused(aura), "同一宿主单位的持续特效（挂接实体）照冻");
        }

        // ---- 不变量 ----

        [Fact]
        public void Freeze_IsIdempotent_NoCounting()
        {
            var renderer = new FreezeCapableStubRenderer2D();
            var player = NewPlayer(renderer);
            var handle = SpawnAnchor(player, AuraVfx, HitUnit);

            player.SetOwnerFrozen(HitUnit, true);
            player.SetOwnerFrozen(HitUnit, true); // 同一单位冻结中再被命中：只延长，不重复发起
            Assert.Equal(1, renderer.PauseCallCount);

            player.SetOwnerFrozen(HitUnit, false); // 一次解冻即恢复（没有"冻结计数"要配平）
            Assert.False(renderer.IsPaused(handle));
            player.SetOwnerFrozen(HitUnit, false);
            Assert.Equal(1, renderer.ResumeCallCount);
        }

        [Fact]
        public void Freeze_StopsLifetimeCountdown_ResumesFromFreezePoint()
        {
            var renderer = new FreezeCapableStubRenderer2D();
            var player = NewPlayer(renderer);
            var handle = SpawnAnchor(player, TimedVfx, HitUnit);

            const double beforeFreeze = 0.4;
            player.Update(beforeFreeze);
            player.SetOwnerFrozen(HitUnit, true);
            player.Update(10 * TimedLifetime); // 冻结期间无论过去多久都不能到期回收
            Assert.True(renderer.IsAlive(handle), "冻结期间 lifetime 倒计时应停住，否则解冻时恢复的是一个已消失的特效");

            player.SetOwnerFrozen(HitUnit, false);
            var remaining = TimedLifetime - beforeFreeze;
            player.Update(remaining - 0.05);
            Assert.True(renderer.IsAlive(handle), "解冻后从冻结点继续倒计时");
            player.Update(0.1);
            Assert.False(renderer.IsAlive(handle), "剩余 lifetime 走完后正常回收");
        }

        [Fact]
        public void SpawnWhileOwnerFrozen_StartsPaused_HotPath()
        {
            var renderer = new FreezeCapableStubRenderer2D();
            var player = NewPlayer(renderer);
            player.SetOwnerFrozen(HitUnit, true);

            var handle = SpawnAnchor(player, AuraVfx, HitUnit);

            Assert.True(renderer.IsPaused(handle), "冻结期间新播放的该单位特效从暂停状态起播");
            player.SetOwnerFrozen(HitUnit, false);
            Assert.False(renderer.IsPaused(handle));
        }

        [Fact]
        public void SpawnWhileOwnerFrozen_ColdLoad_StartsPausedWhenLoadCompletes_SameExitAsHotPath()
        {
            // 冷加载补发与热路径同一出口：资源没加载完时排队，冻结期间加载完成补发出来的粒子同样从暂停起播。
            var renderer = new FreezeCapableStubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var player = NewPlayer(renderer, loader);

            var placeholder = SpawnAnchor(player, AuraVfx, HitUnit);
            Assert.Equal(1, player.PendingSpawnCount);
            Assert.Empty(renderer.Paused);

            player.SetOwnerFrozen(HitUnit, true);
            loader.CompletePending(AuraRes);

            var real = Assert.Single(renderer.Paused);
            Assert.True(real.Value, "冻结期间冷加载补发的粒子应从暂停起播");

            player.SetOwnerFrozen(HitUnit, false);
            Assert.False(renderer.Paused[real.Key]);
            player.Stop(placeholder); // 占位句柄仍能停到补发出来的粒子，且不残留宿主登记
            Assert.False(renderer.IsAlive(new ParticleHandle(real.Key)));
        }

        [Fact]
        public void StoppedParticle_LeavesNoOwnerRegistration_LaterFreezeDoesNotTouchIt()
        {
            var renderer = new FreezeCapableStubRenderer2D();
            var player = NewPlayer(renderer);
            var handle = SpawnAnchor(player, AuraVfx, HitUnit);
            player.Stop(handle);

            player.SetOwnerFrozen(HitUnit, true);

            Assert.Equal(0, renderer.PauseCallCount);
        }

        [Fact]
        public void Socket3DModel_FrozenByAnimSpeedZero_RestoredToOne()
        {
            var renderer2D = new FreezeCapableStubRenderer2D();
            var r3 = new StubRenderer3D();
            var host = r3.CreateModelInstance(new Id("model.freeze_host"));
            var player = NewPlayer(renderer2D, r3: r3, host: host);

            var handle = player.Spawn(SocketVfx, VfxAttach.Socket(HitUnit, new Id("socket.hand")), null)!.Value;
            Assert.Single(r3.Attachments);
            var childValue = System.Linq.Enumerable.Single(r3.Attachments.Keys);

            player.SetOwnerFrozen(HitUnit, true);
            Assert.Equal(0.0, r3.AnimSpeeds[childValue]);

            player.SetOwnerFrozen(HitUnit, false);
            Assert.Equal(1.0, r3.AnimSpeeds[childValue]);
            Assert.Equal(0, renderer2D.PauseCallCount); // socket 真挂接走 3D 速率，不经 2D 粒子暂停能力
            player.Stop(handle);
        }

        // ---- 手感落地 M4-G：没有粒子暂停能力的适配层，逻辑侧仍停表 ----

        /// <summary>
        /// 复现用例：既有 <c>StubRenderer2D</c> 不实现 <see cref="IParticleFreezer"/>。M3-C 时这类适配层上冻结既不暂停也不停表（lifetime 照常到期，被冻结单位的特效在顿帧里走完存活被回收：
        /// 冻结期间存活 true → false）；M4-G 起存活计时随冻结停住，无论适配层是否能暂停：冻结期间无论过去多久都存活，解冻后从冻结点继续倒计时。视觉上无法暂停（适配层没有原语）不抛异常。
        /// </summary>
        [Fact]
        public void RendererWithoutPauseCapability_StillStopsTheLifetimeClock_AndResumesFromTheFreezePoint()
        {
            var renderer = new StubRenderer2D();
            Assert.False((object)renderer is IParticleFreezer);
            var player = NewPlayer(renderer);
            var handle = SpawnAnchor(player, TimedVfx, HitUnit);

            const double beforeFreeze = 0.4;
            player.Update(beforeFreeze);
            player.SetOwnerFrozen(HitUnit, true);
            player.Update(10 * TimedLifetime);
            Assert.True(renderer.IsParticleAlive(handle), "冻结期间存活计时应停住，与适配层能否暂停无关");

            player.SetOwnerFrozen(HitUnit, false);
            var remaining = TimedLifetime - beforeFreeze;
            player.Update(remaining - 0.05);
            Assert.True(renderer.IsParticleAlive(handle), "解冻后从冻结点继续倒计时");
            player.Update(0.1);
            Assert.False(renderer.IsParticleAlive(handle), "剩余存活走完后正常回收");
        }

        /// <summary>
        /// 不变量：没有粒子暂停能力的适配层上，没被冻结的特效（旁观单位、world 挂接的命中闪光）存活计时照常——冻结只影响被冻结单位名下的特效；
        /// 从未冻结过（反馈包 <c>freeze_layers.particles</c> 缺省为假，装配根不调用冻结）时与此前逐位一致：lifetime 走完即回收。
        /// </summary>
        [Fact]
        public void RendererWithoutPauseCapability_NeverFrozen_OrBystanders_KeepExpiringOnSchedule()
        {
            var renderer = new StubRenderer2D();
            var player = NewPlayer(renderer);
            var onHit = SpawnAnchor(player, TimedVfx, HitUnit);
            var onBystander = SpawnAnchor(player, TimedVfx, Bystander);

            player.SetOwnerFrozen(HitUnit, true);
            player.Update(TimedLifetime + 0.1);

            Assert.True(renderer.IsParticleAlive(onHit), "被冻结单位名下的特效停表");
            Assert.False(renderer.IsParticleAlive(onBystander), "旁观单位的特效照常到期回收");

            var neverFrozen = new StubRenderer2D();
            var other = NewPlayer(neverFrozen);
            var handle = SpawnAnchor(other, TimedVfx, HitUnit);
            other.Update(TimedLifetime - 0.05);
            Assert.True(neverFrozen.IsParticleAlive(handle));
            other.Update(0.1);
            Assert.False(neverFrozen.IsParticleAlive(handle));
        }

        /// <summary>不变量：没有粒子暂停能力的适配层上，冻结期间新播放的该单位特效（热路径）同样从停表状态起播——存活计时不流逝，解冻后才开始倒计时。</summary>
        [Fact]
        public void RendererWithoutPauseCapability_SpawnWhileOwnerFrozen_StartsHeld()
        {
            var renderer = new StubRenderer2D();
            var player = NewPlayer(renderer);
            player.SetOwnerFrozen(HitUnit, true);
            var handle = SpawnAnchor(player, TimedVfx, HitUnit);

            player.Update(10 * TimedLifetime);
            Assert.True(renderer.IsParticleAlive(handle));

            player.SetOwnerFrozen(HitUnit, false);
            player.Update(TimedLifetime + 0.01);
            Assert.False(renderer.IsParticleAlive(handle));
        }
    }
}

// CompositeFeedbackSinkAttachTests：T-M30（测试覆盖剩余项第四批）——CompositeFeedbackSink.PlayVfx 的
// attach 换算分支直接用例。装配级用例已覆盖转发委托；这里用一个记录型 IVfxPlayer 直接断言：
//   * 带 anchor_id 的实体挂接 → VfxAttach.Anchor(entity, anchor)（即便注入了实体位置解析器也不查位置）；
//   * 无 anchor_id → 经实体位置解析器退化为 VfxAttach.World(实体位置)；
//   * 无 anchor_id 且查不到位置（含未注入解析器）→ 告警并跳过播放，且不登记 stop 用的句柄键；
//   * world 挂接缺世界坐标 → 告警并跳过；
//   * 登记规则：只在"有实体且 Spawn 返回句柄"时登记；同一 (vfx, 实体) 再播覆盖旧记录，StopVfx 只停最近一次。
using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Rng;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    public class CompositeFeedbackSinkAttachTests
    {
        private static readonly Id Vfx = new Id("vfx.edge_glow");
        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Anchor = new Id("anchor.hand_main");

        /// <summary>记录 Spawn / Stop 调用；Spawn 返回的句柄按发放顺序记入 Issued（可配置为返回 null）。</summary>
        private sealed class RecordingVfxPlayer : IVfxPlayer
        {
            public readonly List<(Id VfxId, VfxAttach At)> Spawns = new List<(Id, VfxAttach)>();
            public readonly List<ParticleHandle> Stops = new List<ParticleHandle>();
            public readonly List<ParticleHandle> Issued = new List<ParticleHandle>();
            public bool ReturnNullHandle;
            private int _next = 100;

            public ParticleHandle? Spawn(Id vfxId, VfxAttach at, IReadOnlyDictionary<string, double>? parameters)
            {
                Spawns.Add((vfxId, at));
                if (ReturnNullHandle)
                {
                    return null;
                }

                var handle = new ParticleHandle(_next++);
                Issued.Add(handle);
                return handle;
            }

            public void Stop(ParticleHandle handle) => Stops.Add(handle);
            public void Update(double dt) { }
            public int PendingSpawnCount => 0;
            public event Action? PendingSpawnCountChanged { add { } remove { } }
        }

        private static CompositeFeedbackSink Build(
            RecordingVfxPlayer vfx, PresentationDiagnosticsRecorder diag, EntityPositionResolver? resolver = null)
        {
            var sfx = new SfxPlayer(new StubAudio(), new RngHost(1), new Dictionary<Id, SfxDef>());
            return new CompositeFeedbackSink(
                vfx, sfx,
                onFloatingText: (_, __, ___) => { },
                onFreeze: _ => { },
                onShakeCamera: _ => { },
                onFlash: (_, __) => { },
                entityPositionResolver: resolver,
                diagnostics: diag);
        }

        [Fact]
        public void PlayVfx_EntityWithAnchor_SpawnsAnchorAttach_WithoutConsultingPositionResolver()
        {
            var vfx = new RecordingVfxPlayer();
            var diag = new PresentationDiagnosticsRecorder();
            var resolverCalls = 0;
            var sink = Build(vfx, diag, id => { resolverCalls++; return Vec2.Zero; });

            sink.PlayVfx(Vfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, Anchor));

            var spawn = Assert.Single(vfx.Spawns);
            Assert.Equal(Vfx, spawn.VfxId);
            Assert.Equal(VfxAttach.Anchor(Hero, Anchor), spawn.At);
            Assert.Equal(0, resolverCalls);
            Assert.Empty(diag.Warnings);
        }

        [Fact]
        public void PlayVfx_EntityWithoutAnchor_FallsBackToWorldAtResolvedEntityPosition()
        {
            var vfx = new RecordingVfxPlayer();
            var diag = new PresentationDiagnosticsRecorder();
            var heroPos = new Vec2(3, 4);
            var sink = Build(vfx, diag, id => id.Equals(Hero) ? heroPos : (Vec2?)null);

            sink.PlayVfx(Vfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Target, Hero, null));

            var spawn = Assert.Single(vfx.Spawns);
            Assert.Equal(VfxAttach.World(heroPos), spawn.At);
            Assert.Empty(diag.Warnings);
        }

        [Fact]
        public void PlayVfx_EntityWithoutAnchor_PositionUnresolvable_WarnsSkipsAndRegistersNothing()
        {
            var vfx = new RecordingVfxPlayer();
            var diag = new PresentationDiagnosticsRecorder();
            var sink = Build(vfx, diag, id => (Vec2?)null);
            var spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, null);

            sink.PlayVfx(Vfx, spec);

            Assert.Empty(vfx.Spawns);
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains(Vfx.Value, warning);
            Assert.Contains(Hero.Value, warning);

            // 没有登记句柄键：之后的 StopVfx 是静默空操作。
            sink.StopVfx(Vfx, spec);
            Assert.Empty(vfx.Stops);
        }

        [Fact]
        public void PlayVfx_EntityWithoutAnchor_NoResolverInjected_BehavesAsUnresolvable()
        {
            var vfx = new RecordingVfxPlayer();
            var diag = new PresentationDiagnosticsRecorder();
            var sink = Build(vfx, diag, resolver: null);

            sink.PlayVfx(Vfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, null));

            Assert.Empty(vfx.Spawns);
            Assert.Single(diag.Warnings);
        }

        [Fact]
        public void PlayVfx_WorldAttach_UsesGivenPosition_AndRegistersNoKey()
        {
            var vfx = new RecordingVfxPlayer();
            var diag = new PresentationDiagnosticsRecorder();
            var sink = Build(vfx, diag);
            var at = new Vec2(-2, 9);

            sink.PlayVfx(Vfx, FeedbackAttachSpec.ForWorld(at));

            Assert.Equal(VfxAttach.World(at), Assert.Single(vfx.Spawns).At);
            // world 没有实体可键：StopVfx 对任何实体都是空操作。
            sink.StopVfx(Vfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, null));
            Assert.Empty(vfx.Stops);
        }

        [Fact]
        public void PlayVfx_WorldAttachWithoutPosition_WarnsAndSkips()
        {
            var vfx = new RecordingVfxPlayer();
            var diag = new PresentationDiagnosticsRecorder();
            var sink = Build(vfx, diag);

            sink.PlayVfx(Vfx, new FeedbackAttachSpec(FeedbackAttachTarget.World, null, null, null));

            Assert.Empty(vfx.Spawns);
            Assert.Contains(Vfx.Value, Assert.Single(diag.Warnings));
        }

        [Fact]
        public void StopVfx_AfterAnchorAttachedPlay_StopsTheSpawnedHandle_OnceOnly()
        {
            var vfx = new RecordingVfxPlayer();
            var sink = Build(vfx, new PresentationDiagnosticsRecorder());
            var spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, Anchor);

            sink.PlayVfx(Vfx, spec);
            sink.StopVfx(Vfx, spec);
            sink.StopVfx(Vfx, spec); // 键已摘除：第二次是空操作。

            Assert.Single(vfx.Stops);
        }

        [Fact]
        public void PlayVfx_SpawnReturnsNull_RegistersNothing()
        {
            var vfx = new RecordingVfxPlayer { ReturnNullHandle = true };
            var sink = Build(vfx, new PresentationDiagnosticsRecorder());
            var spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, Anchor);

            sink.PlayVfx(Vfx, spec);
            sink.StopVfx(Vfx, spec);

            Assert.Single(vfx.Spawns);
            Assert.Empty(vfx.Stops);
        }

        [Fact]
        public void PlayVfx_SameVfxAndEntityReplayed_StopVfxStopsOnlyTheLatestHandle()
        {
            var vfx = new RecordingVfxPlayer();
            var sink = Build(vfx, new PresentationDiagnosticsRecorder());
            var spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, Anchor);

            sink.PlayVfx(Vfx, spec);
            sink.PlayVfx(Vfx, spec);
            Assert.Equal(2, vfx.Spawns.Count);

            sink.StopVfx(Vfx, spec);

            // 规则（_activeVfxByKey 判断记录）：新句柄覆盖旧记录，只停最近一次。
            Assert.Equal(new[] { vfx.Issued[1] }, vfx.Stops);
        }

        [Fact]
        public void StopVfx_KeyedByVfxAndEntity_OtherEntityOrVfxIsUntouched()
        {
            var vfx = new RecordingVfxPlayer();
            var sink = Build(vfx, new PresentationDiagnosticsRecorder());
            var other = new Id("unit.other");
            var otherVfx = new Id("vfx.other_glow");
            sink.PlayVfx(Vfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, Anchor));
            sink.PlayVfx(Vfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, other, Anchor));
            sink.PlayVfx(otherVfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, Anchor));

            sink.StopVfx(Vfx, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, Hero, null));

            // 只停 (Vfx, Hero) 对应的第一个句柄。
            Assert.Equal(new[] { vfx.Issued[0] }, vfx.Stops);
        }
    }
}

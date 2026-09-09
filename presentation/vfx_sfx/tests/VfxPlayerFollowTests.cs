// VfxPlayerFollowTests：第十四轮审核"VFX anchor/socket 未持续跟随（静态差距）"根治验收——见
// architecture/落地计划/audit-c9ff301-20260909/presentation/scope-review.md"通用机制与游戏策略的
// 归责"一节静态发现、VfxPlayer.cs UpdateFollowTargets/IParticleRepositioner 判断记录。
// 用 FollowCapableStubRenderer2D（同时实现 IRenderer2D 与 IParticleRepositioner）验证：
//   1. attach_mode=anchor 的活动实例，锚点解析结果随后续 Update 变化时，粒子坐标跟着刷新；
//   2. attach_mode=socket 未提供 IRenderer3D/ModelHandleResolver（降级为 world）时，同样按实体
//      位置持续刷新（"退而求其次跟随实体本身"，见 FollowKind.EntityPosition 判断记录）；
//   3. 锚点/实体位置都查不到（视为实体已销毁）时特效结束（Stop），不留悬空静止实例；
//   4. 未装配 IParticleRepositioner（如既有 StubRenderer2D）时行为与改动前完全一致——
//      Update 不抛异常，坐标保持生成时那一次，不产生任何调用。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class VfxPlayerFollowTests
    {
        private static readonly Id AnchorVfx = new Id("vfx.follow_anchor_glow");
        private static readonly Id SocketVfx = new Id("vfx.follow_socket_flame");

        private static Dictionary<Id, VfxDef> BuildCatalog() => new Dictionary<Id, VfxDef>
        {
            [AnchorVfx] = new VfxDef(AnchorVfx, "buff", VfxAttachMode.Anchor, lifetime: null, new Id("res.glow")),
            [SocketVfx] = new VfxDef(SocketVfx, "weapon", VfxAttachMode.Socket, lifetime: null, new Id("res.flame")),
        };

        [Fact]
        public void Update_AnchorMode_RepositionsParticleAsAnchorMoves()
        {
            var renderer = new FollowCapableStubRenderer2D();
            var entity = new Id("unit.hero");
            var anchor = new Id("anchor.hand_main");
            var anchorPos = new Vec2(0, 0);

            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                anchorResolver: (e, a) => e.Equals(entity) && a.Equals(anchor) ? anchorPos : (Vec2?)null);

            var handle = player.Spawn(AnchorVfx, VfxAttach.Anchor(entity, anchor), null);
            Assert.NotNull(handle);
            Assert.Equal(new Vec2(0, 0), renderer.ParticlePositions[handle!.Value.Value]);

            // 实体移动：锚点解析结果随之改变——真实场景下这是 ViewBinder.GetAnchorWorldPosition 每帧
            // 随实体位置/朝向重新计算的结果，这里直接改闭包捕获的局部变量模拟同样的效果。
            anchorPos = new Vec2(5, 7);
            player.Update(0.016);

            Assert.Equal(new Vec2(5, 7), renderer.ParticlePositions[handle.Value.Value]);
            Assert.True(renderer.SetParticlePositionCallCount >= 1);
        }

        [Fact]
        public void Update_SocketModeDowngradedToWorld_FollowsEntityPosition()
        {
            var renderer = new FollowCapableStubRenderer2D();
            var entity = new Id("unit.hero");
            var socket = new Id("socket.main_hand");
            var entityPos = new Vec2(1, 1);

            // 不注入 renderer3D/modelHandleResolver：TrySpawnAttachedToSocket 必然不命中，
            // 走 ResolveSocketDowngradedToWorld 降级路径（同既有 VfxPlayerTests 用例惯例）。
            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                entityPositionResolver: e => e.Equals(entity) ? entityPos : (Vec2?)null);

            var handle = player.Spawn(SocketVfx, VfxAttach.Socket(entity, socket), null);
            Assert.NotNull(handle);
            Assert.Equal(new Vec2(1, 1), renderer.ParticlePositions[handle!.Value.Value]);

            entityPos = new Vec2(9, 2);
            player.Update(0.016);

            Assert.Equal(new Vec2(9, 2), renderer.ParticlePositions[handle.Value.Value]);
        }

        [Fact]
        public void Update_AnchorAndEntityBothUnresolvable_StopsTheParticle()
        {
            var renderer = new FollowCapableStubRenderer2D();
            var entity = new Id("unit.hero");
            var anchor = new Id("anchor.hand_main");
            var alive = true;

            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                anchorResolver: (e, a) => alive && e.Equals(entity) && a.Equals(anchor) ? new Vec2(0, 0) : (Vec2?)null,
                entityPositionResolver: e => alive && e.Equals(entity) ? new Vec2(0, 0) : (Vec2?)null);

            var handle = player.Spawn(AnchorVfx, VfxAttach.Anchor(entity, anchor), null);
            Assert.NotNull(handle);

            // 实体销毁：锚点解析器与实体位置兜底解析器均查不到（同 ResolveAnchor 首次解析"两者都查不
            // 到时跳过播放"同一套语义，这里是持续跟随场景下的对应处理——见 UpdateFollowTargets 判断
            // 记录"策略：实体销毁时特效结束"）。
            alive = false;
            player.Update(0.016);

            Assert.False(renderer.ParticlePositions.ContainsKey(handle!.Value.Value));
        }

        [Fact]
        public void Update_WithoutParticleRepositioner_KeepsSpawnPositionAndDoesNotThrow()
        {
            // StubRenderer2D（既有默认桩）不实现 IParticleRepositioner——改动前既有行为：生成后静止，
            // Update 正常运行不抛异常，不产生任何"重定位"副作用。
            var renderer = new StubRenderer2D();
            var entity = new Id("unit.hero");
            var anchor = new Id("anchor.hand_main");
            var anchorPos = new Vec2(0, 0);

            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                anchorResolver: (e, a) => e.Equals(entity) && a.Equals(anchor) ? anchorPos : (Vec2?)null);

            var handle = player.Spawn(AnchorVfx, VfxAttach.Anchor(entity, anchor), null);
            Assert.NotNull(handle);

            anchorPos = new Vec2(99, 99);
            player.Update(0.016);

            var (_, pos) = renderer.EmittedParticles[handle!.Value.Value];
            Assert.Equal(new Vec2(0, 0), pos);
        }
    }
}

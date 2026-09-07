using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary><see cref="ModelCharacterRig"/> 真实实现用例（ADR-0017 决策 b/c）：用
    /// <see cref="StubRenderer3D"/> 断言放置/材质参数/槽位换装/挂点挂接/命中帧事件是否按预期落到
    /// <see cref="IRenderer3D"/>。</summary>
    public class ModelCharacterRigTests
    {
        private static DisplayInfo ModelDisplayInfo(IReadOnlyList<Id>? sockets = null, IReadOnlyList<Id>? slots = null) =>
            new DisplayInfo(
                id: new Id("display.hero_model"),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.hero_model"),
                kind: DisplayKind.Model,
                iconId: null,
                vfxId: null,
                sfxId: null,
                scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.Blob,
                sortOffset: 0.0,
                weaponStyleRef: null,
                sprite: null,
                model: new ModelInfo(
                    modelRef: new Id("model.hero"),
                    animSetRef: new Id("display.anim_set.hero"),
                    sockets: sockets,
                    slots: slots));

        private static (StubRenderer3D Renderer, ModelHandle Handle, ModelCharacterRig Rig) NewRig(
            RenderOptions? options = null, IReadOnlyList<Id>? sockets = null, IReadOnlyList<Id>? slots = null)
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.hero"));
            var rig = new ModelCharacterRig(new Id("unit.hero_1"), renderer, handle, ModelDisplayInfo(sockets, slots), options);
            return (renderer, handle, rig);
        }

        [Fact]
        public void Constructor_RejectsSpriteDisplayInfo()
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.hero"));
            var spriteInfo = new DisplayInfo(
                new Id("display.hero_sprite"), DisplayCategory.Creature, new Id("creature.hero_sprite"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                sprite: new SpriteInfo("sprite.hero", 8, null, null, null), model: null);

            Assert.Throws<ArgumentException>(() => new ModelCharacterRig(new Id("unit.hero_1"), renderer, handle, spriteInfo));
        }

        [Fact]
        public void SetAnimState_RecordsCurrentState()
        {
            var (_, _, rig) = NewRig();

            rig.SetAnimState(AnimState.Cast);

            Assert.Equal(AnimState.Cast, rig.CurrentAnimState);
        }

        [Fact]
        public void PlayClip_ForwardsToRendererPlayAnim()
        {
            var (renderer, handle, rig) = NewRig();

            rig.PlayClip(new Id("anim.sample_attack"), loop: true, speed: 1.5);

            var playback = renderer.CurrentAnims[handle.Value];
            Assert.Equal(new Id("anim.sample_attack"), playback.ClipId);
            Assert.True(playback.Loop);
            Assert.Equal(1.5, playback.Speed);
        }

        [Fact]
        public void TryGetModelHandle_ReturnsConstructorHandle()
        {
            var (_, handle, rig) = NewRig();

            Assert.Equal(handle, rig.TryGetModelHandle());
        }

        // ------------------------------------------------------------------
        // ResolveAnchorLocalOffset
        // ------------------------------------------------------------------

        [Fact]
        public void ResolveAnchorLocalOffset_DeclaredSocket_ReturnsZero()
        {
            var (_, _, rig) = NewRig(sockets: new[] { new Id("socket.hand_main") });

            var offset = rig.ResolveAnchorLocalOffset(new Id("socket.hand_main"), Direction.Continuous(0.0));

            Assert.Equal(Vec2.Zero, offset);
        }

        [Fact]
        public void ResolveAnchorLocalOffset_UndeclaredSocket_ReturnsNull()
        {
            var (_, _, rig) = NewRig(sockets: new[] { new Id("socket.hand_main") });

            var offset = rig.ResolveAnchorLocalOffset(new Id("socket.hand_off"), Direction.Continuous(0.0));

            Assert.Null(offset);
        }

        // ------------------------------------------------------------------
        // ComposeAndApplyLayers / ApplyLayers：no-op，不抛异常
        // ------------------------------------------------------------------

        [Fact]
        public void ComposeAndApplyLayers_IsNoOp_DoesNotThrow()
        {
            var (_, _, rig) = NewRig();

            var ex = Record.Exception(() =>
                rig.ComposeAndApplyLayers(Array.Empty<string>(), Direction.Continuous(0.0), _ => new Id("layer.x")));

            Assert.Null(ex);
        }

        [Fact]
        public void ApplyLayers_IsNoOp_DoesNotThrow()
        {
            var (_, _, rig) = NewRig();

            var ex = Record.Exception(() => rig.ApplyLayers(Array.Empty<Id>()));

            Assert.Null(ex);
        }

        // ------------------------------------------------------------------
        // ApplyEquipVisual
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyEquipVisual_SlotMesh_CallsSetSlotMesh()
        {
            var (renderer, handle, rig) = NewRig();
            var def = new EquipVisualDef(
                new Id("display.equip_visual.sword"), new Id("item.sword"), EquipVisualMode.SlotMesh,
                slotId: new Id("slot.weapon_main"), meshRef: new Id("mesh.sword"), socketId: null, modelRef: null);

            rig.ApplyEquipVisual(def);

            Assert.Equal(new Id("mesh.sword"), renderer.SlotMeshes[handle.Value][new Id("slot.weapon_main")]);
        }

        [Fact]
        public void ApplyEquipVisual_SlotMesh_MissingSlotId_IsNoOp()
        {
            var (renderer, handle, rig) = NewRig();
            var def = new EquipVisualDef(
                new Id("display.equip_visual.sword"), new Id("item.sword"), EquipVisualMode.SlotMesh,
                slotId: null, meshRef: new Id("mesh.sword"), socketId: null, modelRef: null);

            rig.ApplyEquipVisual(def);

            Assert.False(renderer.SlotMeshes.ContainsKey(handle.Value));
        }

        [Fact]
        public void ApplyEquipVisual_SocketAttach_CreatesAndAttachesChildModel()
        {
            var (renderer, handle, rig) = NewRig();
            var def = new EquipVisualDef(
                new Id("display.equip_visual.torch"), new Id("item.torch"), EquipVisualMode.SocketAttach,
                slotId: null, meshRef: null, socketId: new Id("socket.hand_off"), modelRef: new Id("model.torch"));

            rig.ApplyEquipVisual(def);

            Assert.Single(renderer.Attachments);
            var attachment = System.Linq.Enumerable.Single(renderer.Attachments);
            Assert.Equal(new Id("socket.hand_off"), attachment.Value.SocketId);
            Assert.Equal(handle.Value, attachment.Value.ChildHandle);
            Assert.Equal(new Id("model.torch"), renderer.CreatedModels[attachment.Key]);
        }

        [Fact]
        public void ApplyEquipVisual_SocketAttach_ReplacingExistingAttachment_DetachesOldChild()
        {
            var (renderer, handle, rig) = NewRig();
            var firstDef = new EquipVisualDef(
                new Id("display.equip_visual.torch"), new Id("item.torch"), EquipVisualMode.SocketAttach,
                null, null, new Id("socket.hand_off"), new Id("model.torch"));
            var secondDef = new EquipVisualDef(
                new Id("display.equip_visual.lantern"), new Id("item.lantern"), EquipVisualMode.SocketAttach,
                null, null, new Id("socket.hand_off"), new Id("model.lantern"));

            rig.ApplyEquipVisual(firstDef);
            var firstChildHandle = System.Linq.Enumerable.Single(renderer.Attachments).Key;
            rig.ApplyEquipVisual(secondDef);

            Assert.Single(renderer.Attachments);
            var attachment = System.Linq.Enumerable.Single(renderer.Attachments);
            Assert.Equal(new Id("model.lantern"), renderer.CreatedModels[attachment.Key]);
            Assert.False(renderer.Attachments.ContainsKey(firstChildHandle));
        }

        [Fact]
        public void ClearSlot_SetsNullMesh()
        {
            var (renderer, handle, rig) = NewRig();

            rig.ClearSlot(new Id("slot.weapon_main"));

            Assert.Null(renderer.SlotMeshes[handle.Value][new Id("slot.weapon_main")]);
        }

        // ------------------------------------------------------------------
        // 八原语 → SetPlacement / SetMaterialParam
        // ------------------------------------------------------------------

        [Fact]
        public void SyncPlacement_BeforeAnyPrimitive_AppliesBaseValuesVerbatim()
        {
            var (renderer, handle, rig) = NewRig();

            rig.SyncPlacement(new Vec2(1, 2), height: 0.5, facing: 0.1, scale: 1.0, sortY: 2.0);

            var placement = renderer.Placements[handle.Value];
            Assert.Equal(new Vec2(1, 2), placement.PlanePos);
            Assert.Equal(0.5, placement.Height);
            Assert.Equal(0.1, placement.Facing);
            Assert.Equal(1.0, placement.Scale);
            Assert.Equal(2.0, placement.SortY);
        }

        [Fact]
        public void Move_ComposesOntoBasePlanePosition()
        {
            var (renderer, handle, rig) = NewRig();
            rig.SyncPlacement(new Vec2(10, 10), height: 0, facing: 0, scale: 1.0, sortY: 10);

            rig.ProceduralAnim.Move(new MoveParams(new Vec2(3, 0), 1.0));
            rig.Update(0.5); // 半程：progress=0.5，线性插值 → offset = (1.5, 0)

            var placement = renderer.Placements[handle.Value];
            Assert.Equal(new Vec2(11.5, 10), placement.PlanePos);
        }

        [Fact]
        public void Move_And_Stagger_CombineAdditively()
        {
            var (renderer, handle, rig) = NewRig();
            rig.SyncPlacement(Vec2.Zero, height: 0, facing: 0, scale: 1.0, sortY: 0);

            rig.ProceduralAnim.Move(new MoveParams(new Vec2(4, 0), 1.0));
            rig.ProceduralAnim.Stagger(new StaggerParams(new Vec2(0, 4), 1.0));
            rig.Update(1.0); // 到达：move 采样到 (4,0)；stagger 三角波在 progress=1 时回落到 0

            var placement = renderer.Placements[handle.Value];
            Assert.Equal(new Vec2(4, 0), placement.PlanePos);
        }

        [Fact]
        public void Scale_MultipliesBaseScale()
        {
            var (renderer, handle, rig) = NewRig();
            rig.SyncPlacement(Vec2.Zero, height: 0, facing: 0, scale: 2.0, sortY: 0);

            rig.ProceduralAnim.Scale(new ScaleParams(1.5, 1.0));
            rig.Update(0.5); // 三角波 progress=0.5 → 峰值 1.0 → multiplier = 1 + (1.5-1)*1 = 1.5

            var placement = renderer.Placements[handle.Value];
            Assert.Equal(3.0, placement.Scale, 6);
        }

        [Fact]
        public void Rotate_AddsToBaseFacing()
        {
            var (renderer, handle, rig) = NewRig();
            rig.SyncPlacement(Vec2.Zero, height: 0, facing: 1.0, scale: 1.0, sortY: 0);

            rig.ProceduralAnim.Rotate(new RotateParams(Math.PI, 1.0));
            rig.Update(1.0);

            var placement = renderer.Placements[handle.Value];
            Assert.Equal(1.0 + Math.PI, placement.Facing, 6);
        }

        [Fact]
        public void Flash_SetsFlashIntensityMaterialParam()
        {
            var (renderer, handle, rig) = NewRig();

            rig.ProceduralAnim.Flash(FlashParams.Default);

            Assert.True(renderer.MaterialParams[handle.Value].ContainsKey("flash_intensity"));
        }

        [Fact]
        public void Fade_SetsFadeAlphaMaterialParam()
        {
            var (renderer, handle, rig) = NewRig();

            rig.ProceduralAnim.Fade(new FadeParams(0.0, 0.5));

            Assert.True(renderer.MaterialParams[handle.Value].ContainsKey("fade_alpha"));
        }

        [Fact]
        public void Trail_SetsTrailIntensityMaterialParam()
        {
            var (renderer, handle, rig) = NewRig();

            rig.ProceduralAnim.Trail(new TrailParams(0.5));

            Assert.True(renderer.MaterialParams[handle.Value].ContainsKey("trail_intensity"));
        }

        [Fact]
        public void ProceduralAnim_BeforeSyncPlacement_DoesNotCallSetPlacement()
        {
            var (renderer, handle, rig) = NewRig();

            rig.ProceduralAnim.Move(new MoveParams(new Vec2(1, 1), 1.0));
            rig.Update(0.5);

            Assert.False(renderer.Placements.ContainsKey(handle.Value));
        }

        // ------------------------------------------------------------------
        // 命中帧同步（AnimKeyframeDriven）
        // ------------------------------------------------------------------

        [Fact]
        public void AnimKeyframeDriven_HitFrameEventId_RaisesHitFrameReached()
        {
            var options = new RenderOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            var (renderer, handle, rig) = NewRig(options);
            Id? raisedFor = null;
            rig.HitFrameReached += id => raisedFor = id;

            renderer.FireAnimEventForTest(handle, ModelCharacterRig.HitFrameEventId);

            Assert.Equal(new Id("unit.hero_1"), raisedFor);
        }

        [Fact]
        public void AnimKeyframeDriven_OtherEventId_DoesNotRaiseHitFrameReached()
        {
            var options = new RenderOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            var (renderer, handle, rig) = NewRig(options);
            var raised = false;
            rig.HitFrameReached += _ => raised = true;

            renderer.FireAnimEventForTest(handle, new Id("anim_event.footstep"));

            Assert.False(raised);
        }

        [Fact]
        public void LogicDriven_Default_NeverRaisesHitFrameReached()
        {
            var (renderer, handle, rig) = NewRig();
            var raised = false;
            rig.HitFrameReached += _ => raised = true;

            renderer.FireAnimEventForTest(handle, ModelCharacterRig.HitFrameEventId);

            Assert.False(raised);
        }

        [Fact]
        public void Dispose_DetachesTrackedSocketChildren()
        {
            var (renderer, handle, rig) = NewRig();
            var def = new EquipVisualDef(
                new Id("display.equip_visual.torch"), new Id("item.torch"), EquipVisualMode.SocketAttach,
                null, null, new Id("socket.hand_off"), new Id("model.torch"));
            rig.ApplyEquipVisual(def);

            rig.Dispose();

            Assert.Empty(renderer.Attachments);
        }
    }
}

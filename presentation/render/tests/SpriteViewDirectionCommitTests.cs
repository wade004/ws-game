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
    /// <summary>
    /// 可关闭"方向准备闸门"的最小 <see cref="SpriteViewBase"/> 子类（T-M27）：重写
    /// <see cref="PrepareDirection"/> 返回 <see cref="Ready"/>，并按调用顺序记录三个方向钩子，
    /// 用来直接验证 ADR-0112 的"期望方向 / 已显示方向"提交机制。
    /// </summary>
    internal sealed class GatedSpriteView : SpriteViewBase
    {
        public bool Ready = true;

        public readonly List<(Direction Facing, Id SlotId, bool FlipX)> PrepareCalls = new List<(Direction, Id, bool)>();

        /// <summary>钩子触发顺序记录："slot:&lt;id&gt;" 表示 OnDirectionSlotChanged，"commit:&lt;id&gt;:&lt;flip&gt;" 表示 OnDirectionCommitted。</summary>
        public readonly List<string> HookLog = new List<string>();

        public GatedSpriteView(
            IRenderer2D renderer, IRenderConventionHost conventions, DisplayInfo displayInfo,
            RenderOptions? options = null, IResourceLoader? resourceLoader = null)
            : base(renderer, conventions, displayInfo, options, resourceLoader)
        {
        }

        protected override bool PrepareDirection(Direction facing, Id slotId, bool flipX)
        {
            PrepareCalls.Add((facing, slotId, flipX));
            return Ready;
        }

        protected override void OnDirectionSlotChanged(Id newSlotId) => HookLog.Add("slot:" + newSlotId.Value);

        protected override void OnDirectionCommitted(Direction facing, Id slotId, bool flipX) =>
            HookLog.Add("commit:" + slotId.Value + ":" + (flipX ? "flip" : "noflip"));
    }

    /// <summary>
    /// T-M27（测试覆盖剩余项第四批）：<see cref="SpriteViewBase"/> 的方向提交机制（ADR-0112）——
    /// <c>PrepareDirection</c> 否决、<c>CommitDirection</c> 的三种触发、期望/已显示方向查询、
    /// <c>GetDistinctDirectionSlots</c>、<c>EnsureLayerImage</c> 三态。
    /// 槽位 / 镜像期望值一律经 <see cref="RenderConventionHost.ResolveDirectionSlot"/> 由规则算出，不写死档位名。
    /// </summary>
    public class SpriteViewDirectionCommitTests
    {
        private static readonly Id Hero = new Id("unit.dir_hero");
        private static readonly RenderConventionHost Conventions = new RenderConventionHost();

        private static DisplayInfo MakeInfo(int directionCount = 8) =>
            new DisplayInfo(
                new Id("display.dir_hero"), DisplayCategory.Creature, new Id("creature.dir_hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.dir_hero", directionCount), null);

        private static Direction DirAt(int index, int count = 8) =>
            new Direction(2.0 * Math.PI * index / count, index, count);

        private static (Id Slot, bool Flip) Resolve(Direction d, DisplayInfo info) =>
            Conventions.ResolveDirectionSlot(d, info.Sprite!);

        private static (GatedSpriteView View, StubRenderer2D Renderer, DisplayInfo Info) Build(
            IResourceLoader? loader = null, int directionCount = 8)
        {
            var renderer = new StubRenderer2D();
            var info = MakeInfo(directionCount);
            var view = new GatedSpriteView(renderer, Conventions, info, resourceLoader: loader);
            view.Bind(Hero);
            return (view, renderer, info);
        }

        /// <summary>找到两个量化索引：解析到不同槽位（用于"槽位变化"场景）。</summary>
        private static (int A, int B) TwoIndicesWithDifferentSlots(DisplayInfo info)
        {
            var count = info.Sprite!.DirectionCount;
            var a = 0;
            for (var b = 1; b < count; b++)
            {
                if (!Resolve(DirAt(a, count), info).Slot.Equals(Resolve(DirAt(b, count), info).Slot))
                {
                    return (a, b);
                }
            }
            throw new InvalidOperationException("未找到不同槽位的两个索引");
        }

        /// <summary>找到两个量化索引：同一槽位、镜像标志不同（镜像对）。</summary>
        private static (int A, int B) MirrorPairIndices(DisplayInfo info)
        {
            var count = info.Sprite!.DirectionCount;
            for (var a = 0; a < count; a++)
            {
                for (var b = a + 1; b < count; b++)
                {
                    var ra = Resolve(DirAt(a, count), info);
                    var rb = Resolve(DirAt(b, count), info);
                    if (ra.Slot.Equals(rb.Slot) && ra.Flip != rb.Flip)
                    {
                        return (a, b);
                    }
                }
            }
            throw new InvalidOperationException("未找到镜像对");
        }

        // ---------------- 初始状态 ----------------

        [Fact]
        public void BeforeAnySyncPose_DesiredEqualsDisplayed_NothingCommitted_NoPendingSwitch()
        {
            var (view, _, info) = Build();
            var initial = Resolve(Direction.FromQuantized(0.0, info.Sprite!.DirectionCount), info);

            Assert.False(view.HasDisplayedDirection);
            Assert.False(view.HasPendingDirectionSwitch);
            Assert.Equal(initial, view.DesiredDirection);
            Assert.Equal(initial, view.DisplayedDirection);
            Assert.Equal(Direction.FromQuantized(0.0, info.Sprite.DirectionCount), view.DisplayedFacing);
            Assert.Empty(view.HookLog);
            Assert.Empty(view.PrepareCalls);
        }

        // ---------------- 首次显示 ----------------

        [Fact]
        public void FirstSyncPose_CommitsImmediately_EvenWhenGateIsClosed_WithoutAskingPrepare()
        {
            var (view, _, info) = Build();
            view.Ready = false;
            var (a, b) = TwoIndicesWithDifferentSlots(info);
            var initialSlot = Resolve(Direction.FromQuantized(0.0, 8), info).Slot;
            var facing = DirAt(b);
            var expected = Resolve(facing, info);

            view.SyncPose(Vec2.Zero, facing, 0.0);

            Assert.Empty(view.PrepareCalls); // 首次显示不等待
            Assert.True(view.HasDisplayedDirection);
            Assert.False(view.HasPendingDirectionSwitch);
            Assert.Equal(expected, view.DisplayedDirection);
            Assert.Equal(expected, view.DesiredDirection);
            Assert.Equal(facing, view.DisplayedFacing);
            // 钩子顺序：槽位相对构造期默认值变了才有 slot 钩子，commit 钩子恒有，且 slot 在前。
            var expectedLog = new List<string>();
            if (!expected.Slot.Equals(initialSlot)) expectedLog.Add("slot:" + expected.Slot.Value);
            expectedLog.Add("commit:" + expected.Slot.Value + ":" + (expected.Flip ? "flip" : "noflip"));
            Assert.Equal(expectedLog, view.HookLog);
            _ = a;
        }

        // ---------------- 槽位变化 + 闸门 ----------------

        [Fact]
        public void SlotChange_WithGateClosed_KeepsDisplayedDirection_RecordsDesired_AndAsksEveryFrame()
        {
            var (view, renderer, info) = Build();
            var (a, b) = TwoIndicesWithDifferentSlots(info);
            view.SyncPose(Vec2.Zero, DirAt(a), 0.0); // 首次显示：提交 a
            var displayed = view.DisplayedDirection;
            view.HookLog.Clear();
            view.Ready = false;
            var wanted = Resolve(DirAt(b), info);

            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);
            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);
            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);

            Assert.Equal(displayed, view.DisplayedDirection);   // 显示停在旧方向
            Assert.Equal(wanted, view.DesiredDirection);        // 期望已更新
            Assert.True(view.HasPendingDirectionSwitch);
            Assert.Equal(DirAt(a), view.DisplayedFacing);
            Assert.Empty(view.HookLog);                          // 未提交：两个钩子都不触发
            Assert.Equal(3, view.PrepareCalls.Count);            // 每帧都再问一次
            Assert.All(view.PrepareCalls, c =>
            {
                Assert.Equal(wanted.Slot, c.SlotId);
                Assert.Equal(wanted.Flip, c.FlipX);
                Assert.Equal(DirAt(b), c.Facing);
            });
            // 变换里的镜像标志用已显示方向的，不是期望的。
            var handle = new List<int>(renderer.Transforms.Keys)[0];
            Assert.Equal(displayed.FlipX, renderer.Transforms[handle].FlipX);
        }

        [Fact]
        public void SlotChange_GateOpens_NextSyncPoseCommits_HooksInOrder_SlotBeforeCommit()
        {
            var (view, renderer, info) = Build();
            var (a, b) = TwoIndicesWithDifferentSlots(info);
            view.SyncPose(Vec2.Zero, DirAt(a), 0.0);
            view.Ready = false;
            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);
            view.HookLog.Clear();
            var wanted = Resolve(DirAt(b), info);

            view.Ready = true;
            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);

            Assert.False(view.HasPendingDirectionSwitch);
            Assert.Equal(wanted, view.DisplayedDirection);
            Assert.Equal(DirAt(b), view.DisplayedFacing);
            Assert.Equal(new[]
            {
                "slot:" + wanted.Slot.Value,
                "commit:" + wanted.Slot.Value + ":" + (wanted.Flip ? "flip" : "noflip"),
            }, view.HookLog);
            var handle = new List<int>(renderer.Transforms.Keys)[0];
            Assert.Equal(wanted.Flip, renderer.Transforms[handle].FlipX);
        }

        [Fact]
        public void SlotChange_GateOpenFromTheStart_CommitsSameFrame_ExactlyOnePrepareCall()
        {
            var (view, _, info) = Build();
            var (a, b) = TwoIndicesWithDifferentSlots(info);
            view.SyncPose(Vec2.Zero, DirAt(a), 0.0);
            view.PrepareCalls.Clear();

            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);

            Assert.Single(view.PrepareCalls);
            Assert.False(view.HasPendingDirectionSwitch);
            Assert.Equal(Resolve(DirAt(b), info), view.DisplayedDirection);
        }

        [Fact]
        public void DesiredChangesAgainWhilePending_LatestDesiredIsAsked_DisplayStaysOnLastCommit()
        {
            var (view, _, info) = Build();
            var count = info.Sprite!.DirectionCount;
            // 取三个互不同槽位的索引
            var indices = new List<int>();
            var seen = new HashSet<Id>();
            for (var i = 0; i < count && indices.Count < 3; i++)
            {
                if (seen.Add(Resolve(DirAt(i), info).Slot)) indices.Add(i);
            }
            Assert.Equal(3, indices.Count);
            view.SyncPose(Vec2.Zero, DirAt(indices[0]), 0.0);
            var displayed = view.DisplayedDirection;
            view.Ready = false;

            view.SyncPose(Vec2.Zero, DirAt(indices[1]), 0.0);
            view.SyncPose(Vec2.Zero, DirAt(indices[2]), 0.0);

            Assert.Equal(displayed, view.DisplayedDirection);
            Assert.Equal(Resolve(DirAt(indices[2]), info), view.DesiredDirection);
            Assert.Equal(Resolve(DirAt(indices[2]), info).Slot, view.PrepareCalls[view.PrepareCalls.Count - 1].SlotId);
            Assert.True(view.HasPendingDirectionSwitch);
        }

        [Fact]
        public void DesiredReturnsToDisplayedSlot_CancelsPendingSwitch_WithoutAskingOrHooks()
        {
            var (view, _, info) = Build();
            var (a, b) = TwoIndicesWithDifferentSlots(info);
            view.SyncPose(Vec2.Zero, DirAt(a), 0.0);
            view.Ready = false;
            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);
            Assert.True(view.HasPendingDirectionSwitch);
            view.PrepareCalls.Clear();
            view.HookLog.Clear();

            view.SyncPose(Vec2.Zero, DirAt(a), 0.0);

            Assert.False(view.HasPendingDirectionSwitch);
            Assert.Empty(view.PrepareCalls);
            Assert.Empty(view.HookLog);
            Assert.Equal(view.DesiredDirection, view.DisplayedDirection);
        }

        // ---------------- 仅镜像变化 ----------------

        [Fact]
        public void MirrorOnlyChange_CommitsSameFrame_WithoutPrepare_WithoutSlotHook_ButWithCommitHook()
        {
            var (view, renderer, info) = Build();
            var (a, b) = MirrorPairIndices(info);
            view.SyncPose(Vec2.Zero, DirAt(a), 0.0);
            view.Ready = false; // 闸门关闭也不应影响镜像变化
            view.PrepareCalls.Clear();
            view.HookLog.Clear();
            var rb = Resolve(DirAt(b), info);

            view.SyncPose(Vec2.Zero, DirAt(b), 0.0);

            Assert.Empty(view.PrepareCalls);
            Assert.False(view.HasPendingDirectionSwitch);
            Assert.Equal(rb, view.DisplayedDirection);
            Assert.Equal(DirAt(b), view.DisplayedFacing);
            Assert.Equal(new[] { "commit:" + rb.Slot.Value + ":" + (rb.Flip ? "flip" : "noflip") }, view.HookLog);
            var handle = new List<int>(renderer.Transforms.Keys)[0];
            Assert.Equal(rb.Flip, renderer.Transforms[handle].FlipX);
        }

        [Fact]
        public void SameDirectionRepeated_DoesNotCommitAgain_NoHooks_NoPrepare()
        {
            var (view, _, info) = Build();
            view.SyncPose(Vec2.Zero, DirAt(2), 0.0);
            view.PrepareCalls.Clear();
            view.HookLog.Clear();

            view.SyncPose(new Vec2(5, 5), DirAt(2), 1.0);
            view.SyncPose(new Vec2(6, 5), DirAt(2), 1.0);

            Assert.Empty(view.PrepareCalls);
            Assert.Empty(view.HookLog);
        }

        // ---------------- GetDistinctDirectionSlots ----------------

        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        public void GetDistinctDirectionSlots_OneEntryPerDistinctSlot_AscendingByIndex_RepresentativeResolvesToIt(int directionCount)
        {
            var (view, _, info) = Build(directionCount: directionCount);

            var slots = view.GetDistinctDirectionSlots();

            // 期望：对全部量化索引暴力解析，按首次出现顺序去重。
            var expectedSlots = new List<Id>();
            var expectedFirstIndex = new List<int>();
            for (var i = 0; i < directionCount; i++)
            {
                var slot = Resolve(DirAt(i, directionCount), info).Slot;
                if (!expectedSlots.Contains(slot))
                {
                    expectedSlots.Add(slot);
                    expectedFirstIndex.Add(i);
                }
            }

            Assert.Equal(expectedSlots.Count, slots.Count);
            for (var k = 0; k < slots.Count; k++)
            {
                Assert.Equal(expectedSlots[k], slots[k].SlotId);
                Assert.Equal(expectedFirstIndex[k], slots[k].Facing.Index);
                Assert.Equal(directionCount, slots[k].Facing.DirectionCount);
                Assert.Equal(2.0 * Math.PI * expectedFirstIndex[k] / directionCount, slots[k].Facing.RawRadians, 12);
                Assert.Equal(slots[k].SlotId, Resolve(slots[k].Facing, info).Slot);
            }
            Assert.True(slots.Count <= directionCount);
            Assert.True(slots.Count < directionCount, "镜像对去重后必须少于总档位数（默认镜像表至少有一对）");
        }

        // ---------------- EnsureLayerImage 三态 ----------------

        private static readonly Id Layer = new Id("layer.dir_hero__front__body");

        [Fact]
        public void EnsureLayerImage_WithoutLoader_IsAlwaysLoaded()
        {
            var (view, _, _) = Build(loader: null);

            Assert.Equal(SpriteViewBase.LayerImageState.Loaded, view.EnsureLayerImage(Layer));
            Assert.Equal(SpriteViewBase.LayerImageState.Loaded, view.EnsureLayerImage(Layer, allowRequest: false));
        }

        [Fact]
        public void EnsureLayerImage_Cold_PendingThenLoaded_RequestIssuedExactlyOnce()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            loader.Register(Layer);
            var (view, _, _) = Build(loader);
            var before = loader.LoadRequests.FindAll(r => r.ResourceId.Equals(Layer)).Count;

            Assert.Equal(SpriteViewBase.LayerImageState.Pending, view.EnsureLayerImage(Layer));
            Assert.Equal(SpriteViewBase.LayerImageState.Pending, view.EnsureLayerImage(Layer)); // 重复调用不再请求
            Assert.Equal(before + 1, loader.LoadRequests.FindAll(r => r.ResourceId.Equals(Layer)).Count);
            Assert.True(loader.HasPending(Layer));

            loader.CompletePending(Layer);

            Assert.Equal(SpriteViewBase.LayerImageState.Loaded, view.EnsureLayerImage(Layer));
            Assert.Equal(before + 1, loader.LoadRequests.FindAll(r => r.ResourceId.Equals(Layer)).Count);
        }

        [Fact]
        public void EnsureLayerImage_LoadFails_BecomesMissing_AndStaysMissingWithoutRetry()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var (view, _, _) = Build(loader);
            Assert.Equal(SpriteViewBase.LayerImageState.Pending, view.EnsureLayerImage(Layer));
            var requestsBefore = loader.LoadRequests.Count;

            loader.FailPending(Layer);

            Assert.Equal(SpriteViewBase.LayerImageState.Missing, view.EnsureLayerImage(Layer));
            Assert.Equal(SpriteViewBase.LayerImageState.Missing, view.EnsureLayerImage(Layer, allowRequest: false));
            Assert.Equal(requestsBefore, loader.LoadRequests.Count);
        }

        [Fact]
        public void EnsureLayerImage_SynchronousLoader_ReturnsConclusionInSameCall()
        {
            var loader = new StubResourceLoader(); // 同步回调
            loader.Register(Layer);
            var missing = new Id("layer.dir_hero__front__nowhere");
            var (view, _, _) = Build(loader);

            Assert.Equal(SpriteViewBase.LayerImageState.Loaded, view.EnsureLayerImage(Layer));
            Assert.Equal(SpriteViewBase.LayerImageState.Missing, view.EnsureLayerImage(missing)); // 未登记 -> 同步失败
        }

        [Fact]
        public void EnsureLayerImage_AlreadyLoadedElsewhere_IsLoadedWithoutRequest()
        {
            var loader = new StubResourceLoader();
            loader.Register(Layer);
            loader.LoadAsync(Layer, ResourceKind.Image, (_, __) => { }); // 别处先加载完成
            var (view, _, _) = Build(loader);
            var before = loader.LoadRequests.Count;

            Assert.Equal(SpriteViewBase.LayerImageState.Loaded, view.EnsureLayerImage(Layer));

            Assert.Equal(before, loader.LoadRequests.Count);
        }

        [Fact]
        public void EnsureLayerImage_AllowRequestFalse_OnlyQueries_NeverIssuesARequest()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var (view, _, _) = Build(loader);
            var before = loader.LoadRequests.Count;

            Assert.Equal(SpriteViewBase.LayerImageState.Pending, view.EnsureLayerImage(Layer, allowRequest: false));

            Assert.Equal(before, loader.LoadRequests.Count);
            Assert.False(loader.HasPending(Layer));
        }

        [Fact]
        public void EnsureLayerImage_AfterDestroy_WithoutConclusion_ReportsLoadedAndDoesNotRequest()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var (view, _, _) = Build(loader);
            view.Destroy();
            var before = loader.LoadRequests.Count;

            Assert.Equal(SpriteViewBase.LayerImageState.Loaded, view.EnsureLayerImage(Layer)); // 无从等待

            Assert.Equal(before, loader.LoadRequests.Count);
        }
    }
}

using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// T-M26（测试覆盖剩余项第四批）：<see cref="EquipmentVisualSource"/> 对 ItemAdded / ItemEquipped /
    /// ItemUnequipped 的订阅直接用例、<c>ReplayEquippedForUnit</c>、<c>Dispose</c>；以及
    /// <see cref="SpriteViewBase.ResetEquipmentVisuals"/> 的空列表 / 未知模板 / null 边界。
    /// </summary>
    public class EquipmentVisualSourceTests
    {
        private static readonly Id Unit = new Id("unit.equip_hero");
        private static readonly Id MainHand = new Id("slot.hand_main");
        private static readonly Id SwordTemplate = new Id("item.template.sword");
        private static readonly Id AxeTemplate = new Id("item.template.axe");
        private static readonly Id SwordA = new Id("item.instance.sword_a");
        private static readonly Id SwordB = new Id("item.instance.sword_b");

        private static EquipVisualDef Visual(string name, Id templateId, string mesh) =>
            new EquipVisualDef(
                new Id("display.equip_visual." + name), templateId, EquipVisualMode.SlotMesh,
                slotId: MainHand, meshRef: new Id("paperdoll.item." + mesh), socketId: null, modelRef: null);

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public EquipmentVisualSource Source = null!;
            public Dictionary<Id, EquipVisualDef> Catalog = null!;
            public EquipVisualDef SwordVisual = null!;
            public EquipVisualDef AxeVisual = null!;
            public List<Id> ResolverCalls = new List<Id>();
            public IReadOnlyList<EquippedItemRef>? ResolverResult;
        }

        private static Fixture Build(bool withResolver = false)
        {
            var fx = new Fixture
            {
                Bus = NewBus(),
                SwordVisual = Visual("sword", SwordTemplate, "sword"),
                AxeVisual = Visual("axe", AxeTemplate, "axe"),
            };
            fx.Catalog = new Dictionary<Id, EquipVisualDef>
            {
                [SwordTemplate] = fx.SwordVisual,
                [AxeTemplate] = fx.AxeVisual,
            };
            fx.Source = withResolver
                ? new EquipmentVisualSource(fx.Bus, fx.Catalog, unitId =>
                {
                    fx.ResolverCalls.Add(unitId);
                    return fx.ResolverResult!;
                })
                : new EquipmentVisualSource(fx.Bus, fx.Catalog);
            return fx;
        }

        private static void Added(Fixture fx, Id instance, Id template) =>
            fx.Bus.PublishImmediate(new ItemAddedEvent(Unit, instance, template, 1));

        private static void Equipped(Fixture fx, Id instance) =>
            fx.Bus.PublishImmediate(new ItemEquippedEvent(Unit, instance, MainHand));

        private static void Unequipped(Fixture fx, Id instance) =>
            fx.Bus.PublishImmediate(new ItemUnequippedEvent(Unit, MainHand, instance));

        // ---------------- 订阅：ItemAdded -> ItemEquipped ----------------

        [Fact]
        public void Equipped_AfterAdded_RegistersCatalogVisualForThatInstance()
        {
            var fx = Build();

            Added(fx, SwordA, SwordTemplate);
            Equipped(fx, SwordA);

            Assert.Same(fx.SwordVisual, Assert.Single(fx.Source.VisualByItemInstanceId).Value);
            Assert.True(fx.Source.VisualByItemInstanceId.ContainsKey(SwordA));
        }

        [Fact]
        public void Added_AloneNeverRegistersAVisual()
        {
            var fx = Build();

            Added(fx, SwordA, SwordTemplate);

            Assert.Empty(fx.Source.VisualByItemInstanceId);
        }

        [Fact]
        public void Equipped_WithoutPriorAdded_DoesNothing()
        {
            var fx = Build();

            Equipped(fx, SwordA);

            Assert.Empty(fx.Source.VisualByItemInstanceId);
        }

        [Fact]
        public void Equipped_TemplateNotInCatalog_DoesNothing()
        {
            var fx = Build();
            var unknownTemplate = new Id("item.template.unlisted");

            Added(fx, SwordA, unknownTemplate);
            Equipped(fx, SwordA);

            Assert.Empty(fx.Source.VisualByItemInstanceId);
        }

        [Fact]
        public void Added_WithMultipleStackRemovals_EveryInstanceIsKnownForLaterEquip()
        {
            var fx = Build();
            var removals = new List<(Id InstanceId, int Count)> { (SwordA, 1), (SwordB, 2) };

            fx.Bus.PublishImmediate(new ItemAddedEvent(Unit, SwordA, SwordTemplate, 3, removals));
            Equipped(fx, SwordA);
            Equipped(fx, SwordB);

            Assert.Equal(removals.Count, fx.Source.VisualByItemInstanceId.Count);
            Assert.Same(fx.SwordVisual, fx.Source.VisualByItemInstanceId[SwordA]);
            Assert.Same(fx.SwordVisual, fx.Source.VisualByItemInstanceId[SwordB]);
        }

        [Fact]
        public void Added_DefaultRemovalsCoverTheEventsOwnInstance()
        {
            var fx = Build();
            var evt = new ItemAddedEvent(Unit, SwordA, SwordTemplate, 1); // 单堆叠构造：Removals = [(instance, count)]

            fx.Bus.PublishImmediate(evt);
            Equipped(fx, SwordA);

            Assert.Equal(evt.ItemInstanceId, SwordA);
            Assert.True(fx.Source.VisualByItemInstanceId.ContainsKey(SwordA));
        }

        [Fact]
        public void Added_LaterAddedWithDifferentTemplateForSameInstance_OverwritesTemplateKnowledge()
        {
            var fx = Build();

            Added(fx, SwordA, SwordTemplate);
            Added(fx, SwordA, AxeTemplate);
            Equipped(fx, SwordA);

            Assert.Same(fx.AxeVisual, fx.Source.VisualByItemInstanceId[SwordA]);
        }

        [Fact]
        public void TwoInstancesOfDifferentTemplates_GetTheirOwnVisuals()
        {
            var fx = Build();
            Added(fx, SwordA, SwordTemplate);
            Added(fx, SwordB, AxeTemplate);

            Equipped(fx, SwordA);
            Equipped(fx, SwordB);

            Assert.Same(fx.SwordVisual, fx.Source.VisualByItemInstanceId[SwordA]);
            Assert.Same(fx.AxeVisual, fx.Source.VisualByItemInstanceId[SwordB]);
        }

        [Fact]
        public void Equipped_Twice_IsIdempotent()
        {
            var fx = Build();
            Added(fx, SwordA, SwordTemplate);

            Equipped(fx, SwordA);
            Equipped(fx, SwordA);

            Assert.Single(fx.Source.VisualByItemInstanceId);
        }

        // ---------------- 订阅：ItemUnequipped ----------------

        [Fact]
        public void Unequipped_RemovesTheVisual_OtherInstancesRemain()
        {
            var fx = Build();
            Added(fx, SwordA, SwordTemplate);
            Added(fx, SwordB, AxeTemplate);
            Equipped(fx, SwordA);
            Equipped(fx, SwordB);

            Unequipped(fx, SwordA);

            Assert.False(fx.Source.VisualByItemInstanceId.ContainsKey(SwordA));
            Assert.True(fx.Source.VisualByItemInstanceId.ContainsKey(SwordB));
        }

        [Fact]
        public void Unequipped_UnknownInstance_IsNoOp()
        {
            var fx = Build();

            Unequipped(fx, SwordA);

            Assert.Empty(fx.Source.VisualByItemInstanceId);
        }

        [Fact]
        public void Reequip_AfterUnequip_RegistersTheVisualAgain_TemplateKnowledgeSurvivesUnequip()
        {
            var fx = Build();
            Added(fx, SwordA, SwordTemplate);
            Equipped(fx, SwordA);
            Unequipped(fx, SwordA);

            Equipped(fx, SwordA); // 不需要再次 ItemAdded

            Assert.Same(fx.SwordVisual, fx.Source.VisualByItemInstanceId[SwordA]);
        }

        [Fact]
        public void VisualByItemInstanceId_IsALiveViewOfTheSameDictionary()
        {
            var fx = Build();
            var view = fx.Source.VisualByItemInstanceId;
            Added(fx, SwordA, SwordTemplate);

            Equipped(fx, SwordA);

            Assert.Same(view, fx.Source.VisualByItemInstanceId);
            Assert.True(view.ContainsKey(SwordA)); // 先取到的引用随事件更新（SpriteViewBase 构造期就持有它）
        }

        // ---------------- Dispose ----------------

        [Fact]
        public void Dispose_UnsubscribesAll_LaterEventsAreIgnored()
        {
            var fx = Build();
            Added(fx, SwordA, SwordTemplate);
            Equipped(fx, SwordA);

            fx.Source.Dispose();
            Unequipped(fx, SwordA);                       // 退订后不再移除
            Added(fx, SwordB, SwordTemplate);
            Equipped(fx, SwordB);                         // 退订后不再登记

            Assert.True(fx.Source.VisualByItemInstanceId.ContainsKey(SwordA));
            Assert.False(fx.Source.VisualByItemInstanceId.ContainsKey(SwordB));
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var fx = Build();

            fx.Source.Dispose();
            fx.Source.Dispose();
        }

        // ---------------- 构造守卫 ----------------

        [Fact]
        public void Constructor_NullBusOrCatalog_Throws()
        {
            var catalog = new Dictionary<Id, EquipVisualDef>();
            Assert.Throws<ArgumentNullException>(() => new EquipmentVisualSource(null!, catalog));
            Assert.Throws<ArgumentNullException>(() => new EquipmentVisualSource(NewBus(), null!));
        }

        // ---------------- ReplayEquippedForUnit ----------------

        [Fact]
        public void Replay_WithoutResolver_ReturnsEmpty_AndRegistersNothing()
        {
            var fx = Build(withResolver: false);

            var result = fx.Source.ReplayEquippedForUnit(Unit);

            Assert.Empty(result);
            Assert.Empty(fx.Source.VisualByItemInstanceId);
        }

        [Fact]
        public void Replay_ResolverReturnsNullOrEmpty_ReturnsEmpty_ButStillCalledWithTheUnit()
        {
            var fx = Build(withResolver: true);

            fx.ResolverResult = null;
            Assert.Empty(fx.Source.ReplayEquippedForUnit(Unit));
            fx.ResolverResult = Array.Empty<EquippedItemRef>();
            Assert.Empty(fx.Source.ReplayEquippedForUnit(Unit));

            Assert.Equal(new[] { Unit, Unit }, fx.ResolverCalls);
            Assert.Empty(fx.Source.VisualByItemInstanceId);
        }

        [Fact]
        public void Replay_KnownTemplates_RegisterVisuals_AndReturnTheResolverListUnchanged()
        {
            var fx = Build(withResolver: true);
            var list = new[]
            {
                new EquippedItemRef(MainHand, SwordA, SwordTemplate),
                new EquippedItemRef(new Id("slot.hand_off"), SwordB, AxeTemplate),
            };
            fx.ResolverResult = list;

            var result = fx.Source.ReplayEquippedForUnit(Unit);

            Assert.Same(list, result);
            Assert.Same(fx.SwordVisual, fx.Source.VisualByItemInstanceId[SwordA]);
            Assert.Same(fx.AxeVisual, fx.Source.VisualByItemInstanceId[SwordB]);
        }

        [Fact]
        public void Replay_UnknownTemplate_IsSkippedForVisual_ButStillReturned()
        {
            var fx = Build(withResolver: true);
            var unknown = new EquippedItemRef(MainHand, SwordA, new Id("item.template.unlisted"));
            fx.ResolverResult = new[] { unknown };

            var result = fx.Source.ReplayEquippedForUnit(Unit);

            Assert.Equal(unknown.ItemInstanceId, Assert.Single(result).ItemInstanceId);
            Assert.Empty(fx.Source.VisualByItemInstanceId);
        }

        [Fact]
        public void Replay_TeachesTemplateKnowledge_SoLaterEquippedEventWorksWithoutAdded()
        {
            var fx = Build(withResolver: true);
            fx.ResolverResult = new[] { new EquippedItemRef(MainHand, SwordA, SwordTemplate) };
            fx.Source.ReplayEquippedForUnit(Unit);
            Unequipped(fx, SwordA);
            Assert.False(fx.Source.VisualByItemInstanceId.ContainsKey(SwordA));

            Equipped(fx, SwordA); // 没有 ItemAdded，但 Replay 已记下模板

            Assert.Same(fx.SwordVisual, fx.Source.VisualByItemInstanceId[SwordA]);
        }

        [Fact]
        public void Replay_Twice_SameSnapshot_IsStable_AndChangedSnapshotOverwritesTemplate()
        {
            var fx = Build(withResolver: true);
            fx.ResolverResult = new[] { new EquippedItemRef(MainHand, SwordA, SwordTemplate) };
            fx.Source.ReplayEquippedForUnit(Unit);
            fx.Source.ReplayEquippedForUnit(Unit);
            Assert.Same(fx.SwordVisual, fx.Source.VisualByItemInstanceId[SwordA]);

            fx.ResolverResult = new[] { new EquippedItemRef(MainHand, SwordA, AxeTemplate) };
            fx.Source.ReplayEquippedForUnit(Unit);

            Assert.Same(fx.AxeVisual, fx.Source.VisualByItemInstanceId[SwordA]);
        }

        // ---------------- SpriteViewBase.ResetEquipmentVisuals 边界 ----------------

        private static DisplayInfo MakeInfoWithLayers() =>
            new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, new Id("creature.hero"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.hero", 8, mirrorPairs: null, paperdollLayers: new[] { "body", "hand_main" }), null);

        private static (TestSpriteView View, StubRenderer2D Renderer, int Handle, Dictionary<Id, EquipVisualDef> Visuals) BuildView()
        {
            var renderer = new StubRenderer2D();
            var visuals = new Dictionary<Id, EquipVisualDef>
            {
                [SwordA] = Visual("sword", SwordTemplate, "sword"),
            };
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeInfoWithLayers(), equipVisualByItemInstanceId: visuals);
            view.Bind(Unit);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI / 2, 8), 0.0);
            return (view, renderer, new List<int>(renderer.CreatedSpriteSets.Keys)[0], visuals);
        }

        [Fact]
        public void ResetEquipmentVisuals_EmptyList_ClearsOverrides_AndRestoresDefaultLayers()
        {
            var (view, renderer, handle, _) = BuildView();
            view.ResetEquipmentVisuals(new[] { new EquippedItemRef(MainHand, SwordA, SwordTemplate) });
            var equippedLayer = renderer.Layers[handle][1];

            view.ResetEquipmentVisuals(Array.Empty<EquippedItemRef>());

            var layers = renderer.Layers[handle];
            Assert.Equal(2, layers.Count);
            Assert.NotEqual(equippedLayer, layers[1]);
            Assert.Equal(new Id("layer.creature_hero__front__hand_main"), layers[1]); // 默认（未装备）层资源
        }

        [Fact]
        public void ResetEquipmentVisuals_UnknownItemInstance_IsSkipped_LayersStayDefault()
        {
            var (view, renderer, handle, _) = BuildView();

            view.ResetEquipmentVisuals(new[] { new EquippedItemRef(MainHand, new Id("item.instance.unknown"), SwordTemplate) });

            Assert.Equal(new Id("layer.creature_hero__front__hand_main"), renderer.Layers[handle][1]);
        }

        [Fact]
        public void ResetEquipmentVisuals_NullList_ActsLikeEmpty_NoThrow()
        {
            var (view, renderer, handle, _) = BuildView();
            view.ResetEquipmentVisuals(new[] { new EquippedItemRef(MainHand, SwordA, SwordTemplate) });

            view.ResetEquipmentVisuals(null!);

            Assert.Equal(new Id("layer.creature_hero__front__hand_main"), renderer.Layers[handle][1]);
        }

        [Fact]
        public void ResetEquipmentVisuals_ThenSameListAgain_IsStable()
        {
            var (view, renderer, handle, _) = BuildView();
            var list = new[] { new EquippedItemRef(MainHand, SwordA, SwordTemplate) };

            view.ResetEquipmentVisuals(list);
            var first = renderer.Layers[handle][1];
            view.ResetEquipmentVisuals(list);

            Assert.Equal(first, renderer.Layers[handle][1]);
            Assert.Equal(new Id("layer.item_sword__front__hand_main"), first);
        }

        [Fact]
        public void ResetEquipmentVisuals_ViewWithoutEquipVisualTable_IgnoresListAndKeepsDefaults()
        {
            var renderer = new StubRenderer2D();
            var view = new TestSpriteView(renderer, new RenderConventionHost(), MakeInfoWithLayers(), equipVisualByItemInstanceId: null);
            view.Bind(Unit);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI / 2, 8), 0.0);
            var handle = new List<int>(renderer.CreatedSpriteSets.Keys)[0];

            view.ResetEquipmentVisuals(new[] { new EquippedItemRef(MainHand, SwordA, SwordTemplate) });

            Assert.Equal(new Id("layer.creature_hero__front__hand_main"), renderer.Layers[handle][1]);
        }
    }
}

using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SaveSystem;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 换装链的规则侧（手感设计/08 第 1 节）：<see cref="EquipmentFeelProvider"/>（主手/副手武器引用）、
    /// <see cref="EquipmentFeelChain"/>（装备事件 → 失效 → <c>feel.weapon_changed</c>）、<see cref="WeaponActionBinding"/>
    /// （主手武器 → 普攻技能）。两个武器槽 + 一个胸甲槽的最小夹具；解析器用只记录失效的假实现，版本号语义同真实解析器
    /// （失效后下一次读取才递增）——真实解析器的端到端见实验室 equip_cycle 脚本。
    /// </summary>
    public sealed class EquipmentFeelChainTests
    {
        private static readonly Id Player = new Id("player.fc_hero");
        private static readonly Id MainSlot = new Id("item.slot.fc_main_hand");
        private static readonly Id OffSlot = new Id("item.slot.fc_off_hand");
        private static readonly Id ChestSlot = new Id("item.slot.fc_chest");

        private const string SlotJson =
            "[" +
            "{\"id\": \"item.slot.fc_main_hand\", \"name_key\": \"l10n.s1\", \"is_weapon\": true}," +
            "{\"id\": \"item.slot.fc_off_hand\", \"name_key\": \"l10n.s2\", \"is_weapon\": true}," +
            "{\"id\": \"item.slot.fc_chest\", \"name_key\": \"l10n.s3\"}" +
            "]";

        private const string QualityJson = "[{\"id\": \"item.quality.fc_common\", \"name_key\": \"l10n.q1\"}]";

        private const string StatDefJson =
            "[{\"id\": \"stat.fc_strength\", \"name_key\": \"l10n.stat.fc_strength\", \"group\": \"primary\"}]";

        private static string Item(string id, string slot, string? feelRef, bool weapon)
        {
            var profile = weapon
                ? ", \"weapon_profile\": {\"damage_min\": 1, \"damage_max\": 2, \"speed\": 1.5, \"weapon_school\": \"skill.school.physical\"}"
                : string.Empty;
            var feel = feelRef != null ? ", \"feel_weapon_ref\": \"" + feelRef + "\"" : string.Empty;
            return "{\"id\": \"" + id + "\", \"slot\": \"" + slot + "\", \"quality\": \"item.quality.fc_common\"," +
                   " \"item_level\": 1, \"display_ref\": \"display.item." + id + "\", \"stack_size\": 1, \"name_key\": \"l10n." + id + "\"," +
                   " \"stats\": [{\"stat\": \"stat.fc_strength\", \"op\": \"flat\", \"value\": 1}]" + profile + feel + "}";
        }

        private static readonly string TemplateJson =
            "[" +
            Item("item.fc_sword", "item.slot.fc_main_hand", "feel.weapon.fc_sword", true) + "," +
            Item("item.fc_axe", "item.slot.fc_main_hand", "feel.weapon.fc_axe", true) + "," +
            Item("item.fc_dagger", "item.slot.fc_off_hand", "feel.weapon.fc_dagger", true) + "," +
            Item("item.fc_plain_club", "item.slot.fc_main_hand", null, true) + "," +
            Item("item.fc_chest", "item.slot.fc_chest", null, false) +
            "]";

        private static string WeaponRow(string id, string family, string? timeline)
        {
            var auto = timeline != null ? ", \"auto_attack_timeline_ref\": \"" + timeline + "\"" : string.Empty;
            return "{\"id\": \"" + id + "\", \"family\": \"" + family + "\"" + auto + "}";
        }

        private static readonly string WeaponJson =
            "[" +
            WeaponRow("feel.weapon.fc_sword", "1h", "skill.fc.attack_sword") + "," +
            WeaponRow("feel.weapon.fc_axe", "2h", "skill.fc.attack_axe") + "," +
            WeaponRow("feel.weapon.fc_dagger", "1h", null) +
            "]";

        /// <summary>只记录失效的解析器：失效后下一次 <see cref="GetVersion"/> 才递增版本（同真实解析器）。</summary>
        private sealed class RecordingResolver : IFeelResolver
        {
            private readonly HashSet<Id> _dirty = new HashSet<Id>();
            private readonly Dictionary<Id, int> _version = new Dictionary<Id, int>();

            public List<string> Invalidations { get; } = new List<string>();

            public int InvalidateAllCount { get; private set; }

            public FeelFieldSet Fields => throw new NotSupportedException();

            public FeelCalibration Calibration => throw new NotSupportedException();

            public void Invalidate(Id unitId, string reason)
            {
                Invalidations.Add(unitId.Value + ":" + reason);
                _dirty.Add(unitId);
            }

            public void InvalidateAll(string reason)
            {
                InvalidateAllCount++;
                foreach (var key in new List<Id>(_version.Keys))
                {
                    _dirty.Add(key);
                }
            }

            public int GetVersion(Id unitId)
            {
                _version.TryGetValue(unitId, out var v);
                var dirty = _dirty.Remove(unitId);
                if (v == 0 || dirty)
                {
                    v++;
                    _version[unitId] = v;
                }

                return v;
            }

            public ResolvedFeel Resolve(Id unitId) => throw new NotSupportedException();

            public JudgingFeelView ResolveJudging(Id unitId) => throw new NotSupportedException();

            public PresentingFeelView ResolvePresenting(Id unitId) => throw new NotSupportedException();

            public IReadOnlyList<FeelProvenanceEntry> GetProvenance(Id unitId, string field) => throw new NotSupportedException();

            public ResolvedFeel BeginAction(Id unitId, Id castInstanceId, string? actionFeelRef) => throw new NotSupportedException();

            public ResolvedFeel? GetSnapshot(Id castInstanceId) => throw new NotSupportedException();

            public void EndAction(Id castInstanceId) => throw new NotSupportedException();
        }

        private sealed class Fixture
        {
            public IDataRegistryView Registry = null!;
            public IEventBus Bus = null!;
            public InventoryHost Inventory = null!;
            public EquipmentHost Equipment = null!;
            public EquipmentFeelProvider Provider = null!;
            public FeelWeaponCatalog Catalog = null!;
            public RecordingResolver Resolver = new RecordingResolver();
            public List<FeelWeaponChangedEvent> Changes = new List<FeelWeaponChangedEvent>();
            public EquipmentFeelChain? Chain;
        }

        private static Fixture Build(bool withChain = true)
        {
            var registry = TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityJson));
                source.Add("item.template", TestSupport.Table("item.template", TemplateJson));
                source.Add("stat.definition", TestSupport.Table("stat.definition", StatDefJson));
                source.Add("feel.weapon", TestSupport.Table("feel.weapon", WeaponJson));
            });

            var bus = TestSupport.CreateBus();
            var inventory = new InventoryHost(registry, bus);
            var statHost = new StatHost(registry, bus);
            var unitAccess = new FakeUnitAccess().Add(Player, 10);
            statHost.RegisterUnit(Player);
            var equipment = new EquipmentHost(
                registry, bus, inventory, statHost, new FakeEffectSink(), new RecordingSkillGranter().Grant, unitAccess);

            var f = new Fixture
            {
                Registry = registry,
                Bus = bus,
                Inventory = inventory,
                Equipment = equipment,
                Provider = new EquipmentFeelProvider(equipment, registry),
                Catalog = new FeelWeaponCatalog(registry),
            };
            bus.Subscribe<FeelWeaponChangedEvent>(RulesEventKeys.FeelWeaponChanged, e => f.Changes.Add(e));
            if (withChain)
            {
                f.Chain = new EquipmentFeelChain(bus, f.Provider, f.Catalog, f.Resolver, () => new[] { Player });
            }

            return f;
        }

        private static void Wear(Fixture f, string template, Id slot)
        {
            f.Inventory.AddItem(Player, new Id(template), 1, null, null);
            var items = f.Inventory.ListItems(Player);
            var result = f.Equipment.Equip(Player, items[items.Count - 1].InstanceId, slot);
            Assert.True(result.Success, result.Reason.ToString());
            f.Bus.DispatchPending();
        }

        // ---------- 提供者 ----------

        [Fact]
        public void Provider_MainIsFirstWeaponSlotByOrdinal_OffhandIsSecond_ArmorIgnored()
        {
            var f = Build(withChain: false);
            Assert.Null(f.Provider.GetMainWeaponRef(Player));
            Assert.Null(f.Provider.GetOffhandWeaponRef(Player));

            Wear(f, "item.fc_chest", ChestSlot);
            Assert.Null(f.Provider.GetMainWeaponRef(Player));

            Wear(f, "item.fc_sword", MainSlot);
            Assert.Equal("feel.weapon.fc_sword", f.Provider.GetMainWeaponRef(Player));
            Assert.Null(f.Provider.GetOffhandWeaponRef(Player));

            Wear(f, "item.fc_dagger", OffSlot);
            Assert.Equal("feel.weapon.fc_sword", f.Provider.GetMainWeaponRef(Player));
            Assert.Equal("feel.weapon.fc_dagger", f.Provider.GetOffhandWeaponRef(Player));
        }

        [Fact]
        public void Provider_WeaponWithoutFeelRef_IsTreatedAsNoFeelWeapon()
        {
            var f = Build(withChain: false);
            Wear(f, "item.fc_plain_club", MainSlot);
            Assert.Null(f.Provider.GetMainWeaponRef(Player));
        }

        // ---------- 换装链 ----------

        [Fact]
        public void Chain_WeaponSwap_InvalidatesOnceAndPublishesChangedEvent_InTheSameDispatch()
        {
            var f = Build();
            Wear(f, "item.fc_sword", MainSlot);

            // Wear 里只调用了一次 DispatchPending：feel.weapon_changed 是订阅者在派发中新入队的，必须在同一次派发里送达。
            Assert.Single(f.Changes);
            var first = f.Changes[0];
            Assert.Equal(Player, first.UnitId);
            Assert.Null(first.PreviousMainRef);
            Assert.Equal("feel.weapon.fc_sword", first.MainRef);
            Assert.Null(first.PreviousFamily);
            Assert.Equal("1h", first.Family);
            Assert.Single(f.Resolver.Invalidations);
            Assert.Equal(f.Resolver.GetVersion(Player), first.FeelVersion);

            // 换另一把主手武器：武器族 1h -> 2h，版本在事件里是重算后的新值，比上一次大。
            Wear(f, "item.fc_axe", MainSlot);
            Assert.Equal(2, f.Changes.Count);
            Assert.Equal("feel.weapon.fc_sword", f.Changes[1].PreviousMainRef);
            Assert.Equal("1h", f.Changes[1].PreviousFamily);
            Assert.Equal("2h", f.Changes[1].Family);
            Assert.True(f.Changes[1].FeelVersion > first.FeelVersion);
            Assert.Equal("2h", f.Chain!.GetFamily(Player));
        }

        [Fact]
        public void Chain_ArmorOnlyChange_DoesNotInvalidateOrPublish()
        {
            var f = Build();
            Wear(f, "item.fc_sword", MainSlot);
            var invalidations = f.Resolver.Invalidations.Count;
            var events = f.Changes.Count;

            Wear(f, "item.fc_chest", ChestSlot);

            Assert.Equal(invalidations, f.Resolver.Invalidations.Count);
            Assert.Equal(events, f.Changes.Count);
        }

        [Fact]
        public void Chain_FirstArmorOnlyEquipOfUnseenUnit_PublishesNothing()
        {
            var f = Build();
            Wear(f, "item.fc_chest", ChestSlot);
            Assert.Empty(f.Changes);
            Assert.Empty(f.Resolver.Invalidations);
        }

        [Fact]
        public void Chain_UnequipMainWeapon_PublishesBareHandedEvent()
        {
            var f = Build();
            Wear(f, "item.fc_axe", MainSlot);
            f.Changes.Clear();

            Assert.NotNull(f.Equipment.Unequip(Player, MainSlot));
            f.Bus.DispatchPending();

            Assert.Single(f.Changes);
            Assert.Equal("feel.weapon.fc_axe", f.Changes[0].PreviousMainRef);
            Assert.Null(f.Changes[0].MainRef);
            Assert.Equal("2h", f.Changes[0].PreviousFamily);
            Assert.Null(f.Changes[0].Family);
            Assert.Null(f.Chain!.GetFamily(Player));
        }

        [Fact]
        public void Chain_OffhandOnlyChange_StillInvalidates_FamilyUnchanged()
        {
            var f = Build();
            Wear(f, "item.fc_sword", MainSlot);
            f.Changes.Clear();

            Wear(f, "item.fc_dagger", OffSlot);

            Assert.Single(f.Changes);
            Assert.Equal("feel.weapon.fc_dagger", f.Changes[0].OffhandRef);
            Assert.Equal("1h", f.Changes[0].Family);
            Assert.Equal("1h", f.Changes[0].PreviousFamily);
        }

        // ---------- 读档冷路径 ----------

        [Fact]
        public void Chain_SaveLoaded_ReconcilesSuppressedEquipChanges_LikeTheHotPath()
        {
            var f = Build();
            Wear(f, "item.fc_sword", MainSlot);
            f.Changes.Clear();
            f.Resolver.Invalidations.Clear();

            // 读档重放期间装备事件被抑制（SaveSystem.Load 的做法）：装备变了，链收不到 item.equipped。
            using (f.Bus.SuppressDispatch())
            {
                Wear(f, "item.fc_axe", MainSlot);
            }

            Assert.Empty(f.Changes);

            f.Bus.Enqueue(new SaveLoadedEvent(new Id("save.slot_fc")));
            f.Bus.DispatchPending();

            Assert.Equal(1, f.Resolver.InvalidateAllCount);
            Assert.Single(f.Changes);
            Assert.Equal("feel.weapon.fc_sword", f.Changes[0].PreviousMainRef);
            Assert.Equal("feel.weapon.fc_axe", f.Changes[0].MainRef);
            Assert.Equal("2h", f.Changes[0].Family);
        }

        [Fact]
        public void Chain_SaveLoaded_CoversKnownUnitsNeverSeenBefore()
        {
            // 链创建之前单位已有装备（预置/读档）：knownUnits 让 save.loaded 对账覆盖它们，首次对账发出"空手 -> 武器"。
            var f = Build(withChain: false);
            Wear(f, "item.fc_sword", MainSlot);
            Assert.Empty(f.Changes);

            f.Chain = new EquipmentFeelChain(f.Bus, f.Provider, f.Catalog, f.Resolver, () => new[] { Player });
            f.Bus.Enqueue(new SaveLoadedEvent(new Id("save.slot_fc")));
            f.Bus.DispatchPending();

            Assert.Single(f.Changes);
            Assert.Null(f.Changes[0].PreviousMainRef);
            Assert.Equal("feel.weapon.fc_sword", f.Changes[0].MainRef);
        }

        [Fact]
        public void Chain_SaveLoaded_WithNoWeaponChange_PublishesNothing()
        {
            var f = Build();
            Wear(f, "item.fc_sword", MainSlot);
            f.Changes.Clear();

            f.Bus.Enqueue(new SaveLoadedEvent(new Id("save.slot_fc")));
            f.Bus.DispatchPending();

            Assert.Empty(f.Changes);
        }

        [Fact]
        public void Chain_Dispose_StopsReactingToEquipEvents()
        {
            var f = Build();
            f.Chain!.Dispose();
            Wear(f, "item.fc_sword", MainSlot);
            Assert.Empty(f.Changes);
        }

        // ---------- 武器普攻映射 ----------

        [Fact]
        public void WeaponActionBinding_ResolvesMainWeaponAutoAttack_ElseUnarmed_ElseNone()
        {
            var f = Build(withChain: false);
            var unarmed = new Id("skill.fc.attack_unarmed");
            var binding = new WeaponActionBinding(f.Provider, f.Catalog, unarmed);

            Assert.True(binding.TryResolveAttackSkill(Player, out var skill));
            Assert.Equal(unarmed, skill);

            Wear(f, "item.fc_sword", MainSlot);
            Assert.True(binding.TryResolveAttackSkill(Player, out skill));
            Assert.Equal(new Id("skill.fc.attack_sword"), skill);

            // 换装后下一次映射立刻用新武器（不依赖换装链）。
            Wear(f, "item.fc_axe", MainSlot);
            Assert.True(binding.TryResolveAttackSkill(Player, out skill));
            Assert.Equal(new Id("skill.fc.attack_axe"), skill);

            // 武器行没声明普攻时间线：回落空手普攻。
            Assert.NotNull(f.Equipment.Unequip(Player, MainSlot));
            Wear(f, "item.fc_dagger", OffSlot);
            Assert.True(binding.TryResolveAttackSkill(Player, out skill));
            Assert.Equal(unarmed, skill);

            var noFallback = new WeaponActionBinding(f.Provider, f.Catalog);
            Assert.False(noFallback.TryResolveAttackSkill(Player, out _));
        }

        private static BufferedIntent Intent(string action, ActionClass cls) =>
            new BufferedIntent(new Id(action), cls, 0, 10, 0, null, BufferHoldState.Tap, 0, false);

        [Fact]
        public void WeaponActionBinding_OnlyMapsAttackClass()
        {
            var f = Build(withChain: false);
            Wear(f, "item.fc_sword", MainSlot);
            var binding = new WeaponActionBinding(f.Provider, f.Catalog);

            var attack = Intent("input.action.attack", ActionClass.Attack);
            var dodge = Intent("input.action.dodge", ActionClass.Dodge);

            Assert.True(binding.TryResolveSkill(Player, attack, out var skill));
            Assert.Equal(new Id("skill.fc.attack_sword"), skill);
            Assert.False(binding.TryResolveSkill(Player, dodge, out _));
        }
    }
}

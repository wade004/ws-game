using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Foundation.SimLoop;
using Core.Gameplay.Dialog;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Presentation.Ui;

namespace Tests.PresentationUi
{
    /// <summary>
    /// 本模块测试共用的最小 Fake 集合（惯例同仓库其它模块，例如
    /// <c>core/gameplay/quest/tests/TestSupport.cs</c>）：每个 Fake 只覆盖测试实际用到的行为，
    /// 不追求还原真实宿主的全部业务规则（如聚合公式、库存上限、限量库存等），这些属于对应模块
    /// 自己的测试范围。
    /// </summary>
    internal static class TestSupport
    {
        public static IEventBus BuildEventBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
    }

    internal sealed class FakeStatHost : IStatHost
    {
        private readonly HashSet<Id> _registered = new HashSet<Id>();
        private readonly Dictionary<(Id, Id), double> _values = new Dictionary<(Id, Id), double>();

        public void RegisterUnit(Id unitId) => _registered.Add(unitId);

        public void UnregisterUnit(Id unitId) => _registered.Remove(unitId);

        public bool IsRegistered(Id unitId) => _registered.Contains(unitId);

        public void SetBase(Id unitId, Id stat, double value) => _values[(unitId, stat)] = value;

        public double GetBase(Id unitId, Id stat) => _values.TryGetValue((unitId, stat), out var v) ? v : 0;

        public double GetStat(Id unitId, Id stat) => GetBase(unitId, stat);

        public void AddModifier(Id unitId, StatModifier modifier)
        {
        }

        public void RemoveModifiersBySource(Id unitId, Id sourceId)
        {
        }

        public IReadOnlyList<StatModifier> GetModifiers(Id unitId, Id stat) => Array.Empty<StatModifier>();
    }

    internal sealed class FakePowerHost : IPowerHost
    {
        private readonly Dictionary<Id, Dictionary<Id, (double Current, double Max)>> _units =
            new Dictionary<Id, Dictionary<Id, (double, double)>>();

        public void RegisterUnit(Id unitId, IReadOnlyList<Id> powerTypes)
        {
            var dict = new Dictionary<Id, (double, double)>();
            foreach (var pt in powerTypes)
            {
                dict[pt] = (0, 0);
            }
            _units[unitId] = dict;
        }

        public void UnregisterUnit(Id unitId) => _units.Remove(unitId);

        public bool HasPower(Id unitId, Id powerType) => _units.TryGetValue(unitId, out var d) && d.ContainsKey(powerType);

        public double GetPower(Id unitId, Id powerType) => _units[unitId][powerType].Current;

        public double GetPowerMax(Id unitId, Id powerType) => _units[unitId][powerType].Max;

        public void ModifyPower(Id unitId, Id powerType, double delta, Id sourceId)
        {
            var d = _units[unitId];
            var (cur, max) = d[powerType];
            d[powerType] = (cur + delta, max);
        }

        public void SetInCombat(Id unitId, bool inCombat)
        {
        }

        public void Advance(Id unitId, double timeUnits)
        {
        }

        public void AdvanceAll(double timeUnits)
        {
        }

        public void RecomputeMax(Id unitId)
        {
        }

        public void SetForTest(Id unitId, Id powerType, double current, double max)
        {
            if (!_units.TryGetValue(unitId, out var d))
            {
                d = new Dictionary<Id, (double, double)>();
                _units[unitId] = d;
            }
            d[powerType] = (current, max);
        }
    }

    internal sealed class FakeProgressionHost : IProgressionHost
    {
        private readonly Dictionary<Id, (int Level, long Xp, long XpToNext)> _units =
            new Dictionary<Id, (int, long, long)>();

        public void RegisterUnit(Id unitId, Id curveId, int startLevel = 1) => _units[unitId] = (startLevel, 0, 1000);

        public int GetLevel(Id unitId) => _units[unitId].Level;

        public long GetXp(Id unitId) => _units[unitId].Xp;

        public long GetXpToNext(Id unitId) => _units[unitId].XpToNext;

        public void AddXp(Id unitId, Id sourceId, long amount)
        {
            var v = _units[unitId];
            _units[unitId] = (v.Level, v.Xp + amount, v.XpToNext);
        }

        public void GrantFromSource(Id unitId, Id xpSourceId, double multiplier = 1)
        {
        }

        public void SetForTest(Id unitId, int level, long xp, long xpToNext) => _units[unitId] = (level, xp, xpToNext);
    }

    internal sealed class FakeInventoryHost : IInventoryHost
    {
        private readonly Dictionary<Id, List<ItemInstance>> _items = new Dictionary<Id, List<ItemInstance>>();
        private int _seq;

        public Id AddItemForTest(Id unitId, Id templateId, int count)
        {
            AddItem(unitId, templateId, count);
            var list = _items[unitId];
            return list[list.Count - 1].InstanceId;
        }

        public bool AddItem(Id unitId, Id templateId, int count)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                list = new List<ItemInstance>();
                _items[unitId] = list;
            }
            var instanceId = new Id("item.instance_" + _seq++);
            list.Add(new ItemInstance(instanceId, templateId, count));
            return true;
        }

        public bool RemoveItem(Id unitId, Id instanceId, int count)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                return false;
            }
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].InstanceId.Equals(instanceId))
                {
                    var newCount = list[i].Count - count;
                    if (newCount < 0) return false;
                    if (newCount == 0) list.RemoveAt(i); else list[i] = new ItemInstance(instanceId, list[i].TemplateId, newCount);
                    return true;
                }
            }
            return false;
        }

        public IReadOnlyList<ItemInstance> ListItems(Id unitId) =>
            _items.TryGetValue(unitId, out var list) ? list : (IReadOnlyList<ItemInstance>)Array.Empty<ItemInstance>();

        public int CountOf(Id unitId, Id templateId)
        {
            var total = 0;
            foreach (var item in ListItems(unitId))
            {
                if (item.TemplateId.Equals(templateId)) total += item.Count;
            }
            return total;
        }

        public ItemInstance? FindInstance(Id unitId, Id instanceId)
        {
            foreach (var item in ListItems(unitId))
            {
                if (item.InstanceId.Equals(instanceId)) return item;
            }
            return null;
        }
    }

    internal sealed class FakeEquipmentHost : IEquipmentHost
    {
        private readonly Dictionary<(Id UnitId, Id Slot), ItemInstanceRef> _equipped =
            new Dictionary<(Id, Id), ItemInstanceRef>();

        public readonly List<(Id UnitId, Id InstanceId, Id Slot)> EquipCalls = new List<(Id, Id, Id)>();
        public readonly List<(Id UnitId, Id Slot)> UnequipCalls = new List<(Id, Id)>();

        public EquipResult Equip(Id unitId, Id instanceId, Id slot)
        {
            EquipCalls.Add((unitId, instanceId, slot));
            var replaced = _equipped.TryGetValue((unitId, slot), out var old) ? (ItemInstanceRef?)old : null;
            _equipped[(unitId, slot)] = new ItemInstanceRef(instanceId);
            return EquipResult.Ok(replaced);
        }

        public ItemInstanceRef? Unequip(Id unitId, Id slot)
        {
            UnequipCalls.Add((unitId, slot));
            if (_equipped.TryGetValue((unitId, slot), out var v))
            {
                _equipped.Remove((unitId, slot));
                return v;
            }
            return null;
        }

        public ItemInstanceRef? GetEquipped(Id unitId, Id slot) =>
            _equipped.TryGetValue((unitId, slot), out var v) ? (ItemInstanceRef?)v : null;

        public IReadOnlyDictionary<Id, ItemInstanceRef> GetAllEquipped(Id unitId)
        {
            var result = new Dictionary<Id, ItemInstanceRef>();
            foreach (var kv in _equipped)
            {
                if (kv.Key.UnitId.Equals(unitId)) result[kv.Key.Slot] = kv.Value;
            }
            return result;
        }
    }

    internal sealed class FakeQuestHost : IQuestHost
    {
        private readonly Dictionary<Id, QuestState> _states = new Dictionary<Id, QuestState>();
        private readonly Dictionary<Id, List<int>> _objectiveCounts = new Dictionary<Id, List<int>>();

        public readonly HashSet<Id> AcceptedQuests = new HashSet<Id>();
        public readonly HashSet<Id> TurnedInQuests = new HashSet<Id>();

        public QuestState GetState(Id unitId, Id questId) => _states.TryGetValue(questId, out var s) ? s : QuestState.Unavailable;

        public bool Accept(Id unitId, Id questId)
        {
            _states[questId] = QuestState.Active;
            if (!_objectiveCounts.ContainsKey(questId)) _objectiveCounts[questId] = new List<int> { 0 };
            AcceptedQuests.Add(questId);
            return true;
        }

        public bool UpdateProgress(Id unitId, Id questId, int objectiveIndex, int delta)
        {
            if (!_objectiveCounts.TryGetValue(questId, out var list) || objectiveIndex < 0 || objectiveIndex >= list.Count)
            {
                return false;
            }
            list[objectiveIndex] += delta;
            return true;
        }

        public bool TurnIn(Id unitId, Id questId)
        {
            _states[questId] = QuestState.TurnedIn;
            TurnedInQuests.Add(questId);
            return true;
        }

        public bool Fail(Id unitId, Id questId, string reason)
        {
            _states[questId] = QuestState.Failed;
            return true;
        }

        public IReadOnlyList<QuestProgress> GetLog(Id unitId)
        {
            var result = new List<QuestProgress>();
            foreach (var kv in _states)
            {
                var counts = _objectiveCounts.TryGetValue(kv.Key, out var l) ? (IReadOnlyList<int>)l : Array.Empty<int>();
                result.Add(new QuestProgress(kv.Key, kv.Value, counts, 0, null));
            }
            return result;
        }

        public IReadOnlyList<(Id QuestId, int ObjectiveIndex, Id TargetRef)> GetActiveObjectives(Id unitId) =>
            Array.Empty<(Id, int, Id)>();

        public void Update(Id unitId)
        {
        }

        public void SeedQuestForTest(Id questId, QuestState state, IReadOnlyList<int> counts)
        {
            _states[questId] = state;
            _objectiveCounts[questId] = new List<int>(counts);
        }
    }

    internal sealed class FakeDialogHost : IDialogHost
    {
        public StoryView? StoryViewToReturn;
        public readonly List<int> ChosenIndices = new List<int>();

        public GossipView OpenGossip(Id unitId, Id npcId, Id menuId) => new GossipView(menuId, Array.Empty<(int, Id)>());

        public bool ChooseOption(Id unitId, int index)
        {
            ChosenIndices.Add(index);
            return true;
        }

        public bool Close(Id unitId) => true;

        public bool StartStory(Id unitId, Id treeId) => true;

        public StoryView? GetStoryView(Id unitId) => StoryViewToReturn;

        public bool AdvanceStory(Id unitId, int branchIndex) => true;
    }

    internal sealed class FakeEconomyHost : IEconomyHost
    {
        private readonly Dictionary<(Id UnitId, Id CurrencyId), long> _balances = new Dictionary<(Id, Id), long>();
        public readonly List<(Id VendorId, Id ItemId, int Count)> BuyCalls = new List<(Id, Id, int)>();
        public readonly List<(Id VendorId, Id InstanceId, int Count)> SellCalls = new List<(Id, Id, int)>();

        public void RegisterUnit(Id unitId)
        {
        }

        public long GetBalance(Id unitId, Id currencyId) => _balances.TryGetValue((unitId, currencyId), out var v) ? v : 0;

        public bool Add(Id unitId, Id currencyId, long amount, Id sourceId)
        {
            _balances[(unitId, currencyId)] = GetBalance(unitId, currencyId) + amount;
            return true;
        }

        public bool TryPay(Id unitId, Id currencyId, long amount)
        {
            var bal = GetBalance(unitId, currencyId);
            if (bal < amount) return false;
            _balances[(unitId, currencyId)] = bal - amount;
            return true;
        }

        public PurchaseResult Buy(Id unitId, Id vendorId, Id itemId, int count)
        {
            BuyCalls.Add((vendorId, itemId, count));
            return PurchaseResult.Ok(0);
        }

        public SellResult Sell(Id unitId, Id vendorId, Id itemInstanceId, int count)
        {
            SellCalls.Add((vendorId, itemInstanceId, count));
            return SellResult.Ok(0);
        }

        public void OnMapEnter(Id mapId)
        {
        }

        public void Update(double dt)
        {
        }

        public int? GetStock(Id vendorId, Id itemId) => null;

        public void SetBalanceForTest(Id unitId, Id currencyId, long value) => _balances[(unitId, currencyId)] = value;
    }

    internal sealed class FakeInputMapHost : IInputMapHost
    {
        private readonly Dictionary<string, List<string>> _bindings = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public readonly List<(string Action, string Binding)> RebindCalls = new List<(string, string)>();

        public void DeclareActionSet(Id actionSetId, IReadOnlyList<ActionDefinition> actions)
        {
            foreach (var a in actions)
            {
                _bindings[a.ActionId.Value] = new List<string>(a.DefaultBindings);
            }
        }

        public bool Rebind(string actionName, string newBinding)
        {
            RebindCalls.Add((actionName, newBinding));
            _bindings[actionName] = new List<string> { newBinding };
            return true;
        }

        public IReadOnlyList<string> GetConflicts(string binding)
        {
            var result = new List<string>();
            foreach (var kv in _bindings)
            {
                if (kv.Value.Contains(binding)) result.Add(kv.Key);
            }
            return result;
        }

        public bool IsActionActive(string actionName) => false;

        public Vec2 GetActionAxis(string actionName) => Vec2.Zero;

        public void Update(IInput input)
        {
        }

        public void ResetBindings(string actionName)
        {
        }

        public JsonObject ExportBindings()
        {
            var builder = new JsonObjectBuilder();
            foreach (var kv in _bindings)
            {
                var arr = new List<JsonValue>();
                foreach (var b in kv.Value) arr.Add(new JsonString(b));
                builder.Add(kv.Key, new JsonArray(arr));
            }
            return builder.Build();
        }

        public void ImportBindings(JsonObject bindings)
        {
            foreach (var entry in bindings)
            {
                if (entry.Value is JsonArray arr)
                {
                    var list = new List<string>();
                    foreach (var v in arr)
                    {
                        if (v is JsonString s) list.Add(s.Value);
                    }
                    _bindings[entry.Key] = list;
                }
            }
        }

        public IReadOnlyList<string> GetBindings(string actionName) =>
            _bindings.TryGetValue(actionName, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>();

        public void SetBindingsForTest(string actionName, params string[] bindings) => _bindings[actionName] = new List<string>(bindings);
    }

    internal sealed class FakeL10nHost : IL10nHost
    {
        private readonly List<Id> _supported;
        private readonly Dictionary<Id, string> _texts = new Dictionary<Id, string>();
        private Id _locale;

        public FakeL10nHost(Id defaultLocale, IEnumerable<Id> supportedLocales)
        {
            DefaultLocale = defaultLocale;
            _locale = defaultLocale;
            _supported = new List<Id>(supportedLocales);
        }

        public Id DefaultLocale { get; }

        public IReadOnlyList<Id> SupportedLocales => _supported;

        public string Text(Id key, IReadOnlyDictionary<string, string>? vars = null) =>
            _texts.TryGetValue(key, out var t) ? t : key.Value;

        public void SetLocale(Id locale)
        {
            if (!_supported.Contains(locale))
            {
                throw new ArgumentException("unsupported locale");
            }
            _locale = locale;
        }

        public Id GetLocale() => _locale;

        public bool HasText(Id key) => _texts.ContainsKey(key);

        public void SetTextForTest(Id key, string text) => _texts[key] = text;
    }

    internal sealed class FakeSkillBookQuery : ISkillBookQuery
    {
        private readonly Dictionary<Id, List<Id>> _known = new Dictionary<Id, List<Id>>();
        private readonly Dictionary<(Id UnitId, Id SkillId), double> _cooldowns = new Dictionary<(Id, Id), double>();

        public IReadOnlyList<Id> GetKnownSkills(Id unitId) =>
            _known.TryGetValue(unitId, out var l) ? l : (IReadOnlyList<Id>)Array.Empty<Id>();

        public double GetCooldown(Id unitId, Id skillId) => _cooldowns.TryGetValue((unitId, skillId), out var v) ? v : 0;

        public void LearnForTest(Id unitId, Id skillId)
        {
            if (!_known.TryGetValue(unitId, out var list))
            {
                list = new List<Id>();
                _known[unitId] = list;
            }
            list.Add(skillId);
        }

        public void SetCooldownForTest(Id unitId, Id skillId, double value) => _cooldowns[(unitId, skillId)] = value;
    }

    /// <summary>
    /// 组装一份完整的 <see cref="UiDataSource"/>（含三个 <see cref="IUiPathProvider"/>）与它背后
    /// 全部 Fake 宿主，供 <c>UiDataSourceTests</c>/<c>ViewModelTests</c> 共用，避免每条测试各自
    /// 重复一遍构造样板。
    /// </summary>
    internal sealed class UiWorldFixture
    {
        public readonly Id PlayerId = new Id("unit.hero");
        public readonly Id TargetId = new Id("unit.inst_2");

        public readonly IEventBus EventBus = TestSupport.BuildEventBus();
        public readonly FakeStatHost StatHost = new FakeStatHost();
        public readonly FakePowerHost PowerHost = new FakePowerHost();
        public readonly FakeProgressionHost Progression = new FakeProgressionHost();
        public readonly FakeInventoryHost Inventory = new FakeInventoryHost();
        public readonly FakeEquipmentHost Equipment = new FakeEquipmentHost();
        public readonly FakeQuestHost Quest = new FakeQuestHost();
        public readonly FakeEconomyHost Economy = new FakeEconomyHost();
        public readonly FakeSkillBookQuery SkillBook = new FakeSkillBookQuery();
        public readonly InMemoryUiDiagnostics Diagnostics = new InMemoryUiDiagnostics();

        public Id? CurrentTarget;

        public readonly UiDataSource DataSource;

        public UiWorldFixture()
        {
            StatHost.RegisterUnit(PlayerId);
            StatHost.RegisterUnit(TargetId);

            var playerProvider = new PlayerPathProvider(
                PlayerId, StatHost, PowerHost, Progression, Inventory, Equipment, Quest, Economy, SkillBook);
            var targetProvider = new TargetPathProvider(() => CurrentTarget, StatHost, PowerHost);
            var unitProvider = new UnitPathProvider(StatHost, PowerHost);

            DataSource = new UiDataSource(EventBus, new IUiPathProvider[] { playerProvider, targetProvider, unitProvider }, Diagnostics);
        }
    }

    /// <summary>记录型 <see cref="IWorldSim"/>：只实现 <see cref="UiIntents"/> 实际用到的
    /// <see cref="IWorldSim.SubmitIntent"/>，其余成员按接口最小可编译要求给出无副作用实现，
    /// 惯例同任务书"记录型 IWorldSim"要求。</summary>
    internal sealed class RecordingWorldSim : IWorldSim
    {
        public readonly List<Intent> SubmittedIntents = new List<Intent>();

        public void Tick(SimStep step)
        {
        }

        public Entity? GetEntity(Id id) => null;

        public IReadOnlyList<Entity> QueryEntities(EntityFilter filter) => Array.Empty<Entity>();

        public void MarkForDestruction(Id id)
        {
        }

        public void AddEntity(Entity entity)
        {
        }

        public Id AllocateEntityId(string kind) => new Id($"{kind}.inst_1");

        public void RegisterPhaseHandler(TickPhase phase, ITickPhaseHandler handler)
        {
        }

        public void ClearAll()
        {
        }

        public ISimTimers Timers => throw new NotSupportedException("测试未用到 Timers");

        public int EntityCount => 0;

        public void SubmitIntent(Intent intent) => SubmittedIntents.Add(intent);

        public IReadOnlyList<Intent> CurrentIntents => SubmittedIntents;

        public void AppendCurrentIntent(Intent intent) => SubmittedIntents.Add(intent);
    }
}

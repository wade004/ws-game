using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Foundation.SimLoop;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.Dialog;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Core.Rules.Skill;
using Presentation.Ui;
using Presentation.VfxSfx.Contracts;

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

        // ApplyGrowthToCurrentLevel 不在此覆盖：IProgressionHost 的默认接口方法（空操作）已够用，
        // 本假实现不建模真实的曲线成长聚合（_units 只记 Level/Xp/XpToNext，不持有曲线数据），
        // 呈现层测试目前也没有依赖成长修正是否写入，见 IProgressionHost.ApplyGrowthToCurrentLevel
        // 判断记录。

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

        /// <summary>
        /// ADR-0063 测试支持：真实 <c>EquipmentHost.Equip</c> 的模板 id 来自装备时查一次背包实例
        /// （见该类型判断记录），本 Fake 没有背包概念，<see cref="IEquipmentHost.Equip"/> 签名本身
        /// 也只有 <c>instanceId</c>——由调用方（测试）经本字典显式登记"这个实例 id 对应哪个模板 id"，
        /// <see cref="GetEquippedTemplateId"/>/<see cref="GetAllEquippedIdentities"/> 据此查询；未登记
        /// 的实例视为"模板未知"，同真实实现"查不到就返回 null"的降级口径（这里对应"测试没有配置，
        /// 不代表生产实现会查不到"）。
        /// </summary>
        public readonly Dictionary<Id, Id> TemplatesByInstance = new Dictionary<Id, Id>();

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

        public Id? GetEquippedTemplateId(Id unitId, Id slot) =>
            _equipped.TryGetValue((unitId, slot), out var v) && TemplatesByInstance.TryGetValue(v.InstanceId, out var templateId)
                ? templateId
                : (Id?)null;

        public IReadOnlyDictionary<Id, EquippedItemIdentity> GetAllEquippedIdentities(Id unitId)
        {
            var result = new Dictionary<Id, EquippedItemIdentity>();
            foreach (var kv in _equipped)
            {
                if (!kv.Key.UnitId.Equals(unitId)) continue;
                if (TemplatesByInstance.TryGetValue(kv.Value.InstanceId, out var templateId))
                {
                    result[kv.Key.Slot] = new EquippedItemIdentity(kv.Value.InstanceId, templateId);
                }
            }
            return result;
        }
    }

    internal sealed class FakeQuestHost : IQuestHost
    {
        private readonly Dictionary<Id, QuestState> _states = new Dictionary<Id, QuestState>();
        private readonly Dictionary<Id, List<int>> _objectiveCounts = new Dictionary<Id, List<int>>();

        /// <summary>消费方反馈第 2 条根治：供 <c>QuestLogViewModel.GetObjectiveRequiredCounts</c>
        /// 转发测试用——单独一份字典而不是复用 <see cref="_objectiveCounts"/>，因为真实
        /// <c>QuestHost</c> 里"需求数"来自任务定义、"当前计数"来自运行期进度，两者是不同数据源，
        /// 本 Fake 按同一形状拆开更贴近真实契约（也便于测试"需求数缺失但当前计数存在"这一边界）。</summary>
        private readonly Dictionary<Id, List<int>> _requiredCounts = new Dictionary<Id, List<int>>();

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

        public bool TurnIn(Id unitId, Id questId) => TurnIn(unitId, questId, out _);

        public bool TurnIn(Id unitId, Id questId, out QuestTurnInFailure failure)
        {
            _states[questId] = QuestState.TurnedIn;
            TurnedInQuests.Add(questId);
            failure = QuestTurnInFailure.None;
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

        /// <summary>见 <see cref="_requiredCounts"/> 判断记录：单独按需为某条任务设置需求数量，
        /// 不调用则 <see cref="GetObjectiveRequiredCounts"/> 走默认接口成员返回空列表（同真实
        /// <c>QuestHost</c> 对未登记定义的降级行为一致）。</summary>
        public void SeedRequiredCountsForTest(Id questId, IReadOnlyList<int> counts)
        {
            _requiredCounts[questId] = new List<int>(counts);
        }

        public IReadOnlyList<int> GetObjectiveRequiredCounts(Id questId) =>
            _requiredCounts.TryGetValue(questId, out var l) ? (IReadOnlyList<int>)l : Array.Empty<int>();

        /// <summary>消费方反馈第六批（阻塞）根治：供 <c>title_key</c> 路径子查询/
        /// <c>QuestLogViewModel.GetQuestTitleKey</c> 转发测试用——同 <see cref="_requiredCounts"/>
        /// 判断记录惯例，单独一份字典，不设置则走接口默认实现（恒返回 <c>null</c>）。</summary>
        private readonly Dictionary<Id, Id> _titleKeys = new Dictionary<Id, Id>();

        /// <summary>同上，供 <c>objective_description_key[i]</c> 路径子查询/
        /// <c>QuestLogViewModel.GetObjectiveDescriptionKey</c> 转发测试用，键为 (questId, objectiveIndex)。</summary>
        private readonly Dictionary<(Id QuestId, int ObjectiveIndex), Id> _objectiveDescriptionKeys =
            new Dictionary<(Id, int), Id>();

        public void SeedTitleKeyForTest(Id questId, Id titleKey) => _titleKeys[questId] = titleKey;

        public void SeedObjectiveDescriptionKeyForTest(Id questId, int objectiveIndex, Id descriptionKey) =>
            _objectiveDescriptionKeys[(questId, objectiveIndex)] = descriptionKey;

        public Id? GetQuestTitleKey(Id questId) =>
            _titleKeys.TryGetValue(questId, out var key) ? key : (Id?)null;

        public Id? GetObjectiveDescriptionKey(Id questId, int objectiveIndex) =>
            _objectiveDescriptionKeys.TryGetValue((questId, objectiveIndex), out var key) ? key : (Id?)null;
    }

    internal sealed class FakeDialogHost : IDialogHost
    {
        public StoryView? StoryViewToReturn;

        /// <summary>UiIntents.ChooseDialogOption 按会话类型分派（消费方反馈第十五批修复）判断记录：
        /// 该方法先查 <see cref="GetStoryView"/> 再查 <see cref="GetGossipView"/> 决定转发到
        /// <see cref="AdvanceStory"/> 还是 <see cref="ChooseOption"/>，本字段供
        /// <c>UiIntentsTests</c> 摆出"当前是 gossip 会话"这一状态——默认 null（未打开任何 gossip
        /// 会话），既有只依赖 <see cref="StoryViewToReturn"/> 的调用方（<c>ViewModelTests</c>）行为
        /// 不变。</summary>
        public GossipView? GossipViewToReturn;

        public readonly List<int> ChosenIndices = new List<int>();
        public readonly List<int> AdvancedBranchIndices = new List<int>();

        public GossipView OpenGossip(Id unitId, Id npcId, Id menuId) => new GossipView(menuId, Array.Empty<(int, Id)>());

        public GossipView? GetGossipView(Id unitId) => GossipViewToReturn;

        public bool ChooseOption(Id unitId, int index)
        {
            ChosenIndices.Add(index);
            return true;
        }

        public bool Close(Id unitId) => true;

        public bool StartStory(Id unitId, Id treeId) => true;

        public StoryView? GetStoryView(Id unitId) => StoryViewToReturn;

        public bool AdvanceStory(Id unitId, int branchIndex)
        {
            AdvancedBranchIndices.Add(branchIndex);
            return true;
        }
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

        public bool SetBalance(Id unitId, Id currencyId, long amount)
        {
            _balances[(unitId, currencyId)] = amount;
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

        /// <summary>消费方反馈第 3 条（2026-09-20，ADR-0048）：按技能 id（不分单位）登记
        /// <c>skill.def.name_key</c>，供 <c>GetNameKey</c> 转发——同真实 <c>SkillHost.GetSkillNameKey</c>
        /// 语义（技能名称不分单位，是技能定义本身的属性）。未登记的技能走接口默认成员返回 <c>null</c>，
        /// 不需要本 Fake 覆盖。</summary>
        private readonly Dictionary<Id, Id> _nameKeys = new Dictionary<Id, Id>();

        /// <summary>消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：按 (unitId, skillId) 登记一个
        /// 显式 <see cref="SkillReadiness"/>，供 <see cref="GetSkillReadiness"/> 原样返回——测试据此
        /// 精确控制 <c>EffectiveCooldownDuration</c>/充能/<c>BlockingSources</c> 组合。未登记的组合
        /// 落到 <see cref="GetSkillReadiness"/> 自己的降级分支（见该方法判断记录），供诊断类用例
        /// 不需要额外开关即可模拟"取不到完整就绪数据"。</summary>
        private readonly Dictionary<(Id UnitId, Id SkillId), SkillReadiness> _readiness =
            new Dictionary<(Id, Id), SkillReadiness>();

        /// <summary>消费方反馈第 1 条（2026-09-21，ADR-0056）：按单位登记"当前读条的技能 id"，供
        /// <see cref="GetCastingSkillId"/> 转发；<see cref="_castingRemaining"/>/<see
        /// cref="_castingTotal"/> 三者分开存、分开缺省（而不是打包成一个元组一起有无），是为了能在
        /// <see cref="SetCastingForTest"/> 里单独制造"技能 id 已登记但剩余/总时长未登记"这一组合——
        /// 供 <c>UnitSubQueries.ResolveCastingTiming</c>"确认在读条但取不到数据"诊断分支的测试用例
        /// 复现该分支（真实 <c>SkillHost</c> 三者同源，不会出现这种组合，但适配层实现可能不完整）。</summary>
        private readonly Dictionary<Id, Id> _castingSkill = new Dictionary<Id, Id>();
        private readonly Dictionary<Id, double> _castingRemaining = new Dictionary<Id, double>();
        private readonly Dictionary<Id, double> _castingTotal = new Dictionary<Id, double>();

        public IReadOnlyList<Id> GetKnownSkills(Id unitId) =>
            _known.TryGetValue(unitId, out var l) ? l : (IReadOnlyList<Id>)Array.Empty<Id>();

        public double GetCooldown(Id unitId, Id skillId) => _cooldowns.TryGetValue((unitId, skillId), out var v) ? v : 0;

        public Id? GetNameKey(Id skillId) => _nameKeys.TryGetValue(skillId, out var v) ? v : (Id?)null;

        /// <summary>消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：登记过就原样返回；未登记时按
        /// <see cref="ISkillBookQuery.GetSkillReadiness"/> 默认接口成员同一套降级算法就地计算（只看
        /// <see cref="GetCooldown"/>，其余明细字段与 <c>EffectiveCooldownDuration</c> 均为
        /// <c>null</c>）——不是重新发明一套算法，是刻意与接口默认成员保持逐字一致，供
        /// <c>ActionBarViewModel</c> 诊断用例（未显式 <see cref="SetReadinessForTest"/>）直接命中
        /// "取不到完整就绪数据"这一分支。</summary>
        public SkillReadiness GetSkillReadiness(Id unitId, Id skillId)
        {
            if (_readiness.TryGetValue((unitId, skillId), out var explicitReadiness))
            {
                return explicitReadiness;
            }

            var remaining = GetCooldown(unitId, skillId);
            var blocking = remaining > 0 ? SkillReadinessBlockers.SkillCooldown : SkillReadinessBlockers.None;
            return new SkillReadiness(
                skillId, remaining <= 0, blocking,
                skillCooldownRemaining: null, categoryCooldownRemaining: null, globalCooldownRemaining: null,
                maxCharges: null, currentCharges: null, nextChargeRemaining: null, effectiveCooldownDuration: null);
        }

        public Id? GetCastingSkillId(Id unitId) => _castingSkill.TryGetValue(unitId, out var v) ? v : (Id?)null;

        public double? GetCastingRemaining(Id unitId) => _castingRemaining.TryGetValue(unitId, out var v) ? v : (double?)null;

        public double? GetCastingTotal(Id unitId) => _castingTotal.TryGetValue(unitId, out var v) ? v : (double?)null;

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

        public void SetNameKeyForTest(Id skillId, Id nameKey) => _nameKeys[skillId] = nameKey;

        public void SetReadinessForTest(Id unitId, Id skillId, SkillReadiness readiness) =>
            _readiness[(unitId, skillId)] = readiness;

        /// <summary>消费方反馈第 1 条测试用：分别设置/清空三项读条数据，<c>null</c> 表示"清空"
        /// （不是"设为 0"）——三个参数各自独立缺省，供 <see cref="_castingSkill"/> 判断记录描述的
        /// 诊断分支复现用例传入"技能 id 有值、remaining/total 缺省"的组合。</summary>
        public void SetCastingForTest(Id unitId, Id? skillId, double? remaining, double? total)
        {
            if (skillId.HasValue) _castingSkill[unitId] = skillId.Value; else _castingSkill.Remove(unitId);
            if (remaining.HasValue) _castingRemaining[unitId] = remaining.Value; else _castingRemaining.Remove(unitId);
            if (total.HasValue) _castingTotal[unitId] = total.Value; else _castingTotal.Remove(unitId);
        }
    }

    /// <summary>
    /// 消费方反馈第三批第 2 条（2026-09-21，ADR-0057）："真实触发一次技能进入冷却"验收用例的载体：
    /// 不使用假造的 <see cref="SkillReadiness"/> 快照，而是持有生产用的真实
    /// <see cref="Core.Rules.Skill.CooldownTracker"/>（<c>core/rules/skill</c> 模块导出的公开类型，
    /// <c>Core.Rules.Skill.SkillHost</c> 内部用的同一个协作对象）+ 一个真实 <see
    /// cref="Core.Rules.Skill.SkillDef"/>，<see cref="StartCooldownForTest"/>/<see
    /// cref="UpdateForTest"/> 直接调用该真实类型的 <c>StartCooldown</c>/<c>Update</c>/
    /// <c>AdvanceCharges</c>（与 <c>CastPipeline</c> 步骤 9 成功施法后调用的方法完全相同），
    /// <see cref="GetSkillReadiness"/> 按 <c>Core.Rules.Skill.SkillHost.GetSkillReadiness</c>
    /// 非充能/充能两个分支同一套公式重新计算（略去 SpellMod/公共冷却——本类不需要它们，构造
    /// <c>SkillHost</c> 完整依赖链超出本模块测试边界，见 <c>ISkillBookQuery</c> 类型判断记录"本模块
    /// 不修改 core/rules，不必搭建 SkillHost 完整构造依赖链"）。
    /// </summary>
    internal sealed class RealCooldownSkillBookQuery : ISkillBookQuery
    {
        private readonly Core.Rules.Skill.CooldownTracker _cooldowns = new Core.Rules.Skill.CooldownTracker();
        private readonly Core.Rules.Skill.SkillDef _def;

        public RealCooldownSkillBookQuery(Core.Rules.Skill.SkillDef def)
        {
            _def = def;
        }

        public IReadOnlyList<Id> GetKnownSkills(Id unitId) => Array.Empty<Id>();

        public Id? GetNameKey(Id skillId) => null;

        public double GetCooldown(Id unitId, Id skillId) => _cooldowns.GetCooldown(unitId, _def);

        /// <summary>真实触发：与生产 <c>CastPipeline</c> 步骤 9 成功施法后调用的方法完全相同。</summary>
        public void StartCooldownForTest(Id unitId) => _cooldowns.StartCooldown(unitId, _def);

        /// <summary>推进冷却/充能恢复计时——与生产 <c>SkillHost.Update</c> 每帧调用的方法完全相同。</summary>
        public void UpdateForTest(Id unitId, double dt)
        {
            _cooldowns.Update(dt);
            _cooldowns.AdvanceCharges(unitId, _def, dt);
        }

        public SkillReadiness GetSkillReadiness(Id unitId, Id skillId)
        {
            if (_def.HasCharges)
            {
                var current = _cooldowns.GetCharges(unitId, _def);
                var max = _cooldowns.GetEffectiveChargesMax(unitId, _def);
                var nextChargeRemaining = _cooldowns.GetChargeRechargeRemaining(unitId, _def);
                var effective = _cooldowns.GetEffectiveRechargeTimeScaled(unitId, _def);
                var blocking = current > 0 ? SkillReadinessBlockers.None : SkillReadinessBlockers.NoCharges;
                return new SkillReadiness(
                    skillId, isReady: current > 0, blockingSources: blocking,
                    skillCooldownRemaining: null, categoryCooldownRemaining: null, globalCooldownRemaining: 0,
                    maxCharges: max, currentCharges: current, nextChargeRemaining: nextChargeRemaining,
                    effectiveCooldownDuration: effective);
            }

            var skillRemaining = _cooldowns.GetSkillCooldownRemaining(unitId, _def.Id);
            var blocking2 = skillRemaining > 0 ? SkillReadinessBlockers.SkillCooldown : SkillReadinessBlockers.None;
            var effectiveDuration = _def.CooldownDuration * _cooldowns.CurrentTimeFactor;
            return new SkillReadiness(
                skillId, isReady: skillRemaining <= 0, blockingSources: blocking2,
                skillCooldownRemaining: skillRemaining, categoryCooldownRemaining: null, globalCooldownRemaining: 0,
                maxCharges: null, currentCharges: null, nextChargeRemaining: null,
                effectiveCooldownDuration: effectiveDuration);
        }
    }

    /// <summary>消费方反馈第 4 条（2026-09-21，ADR-0056）测试用假实现：按单位登记一份完整的
    /// <see cref="AuraSnapshot"/> 列表（不经 <see cref="IAuraQuery.GetActiveAuraDefs"/>/<see
    /// cref="IAuraQuery.GetStacks"/> 拼装默认接口成员那条降级路径——本 Fake 显式覆盖
    /// <see cref="GetActiveAuraSnapshots"/>，直接测试 <c>presentation/ui</c> 路径解析这一段的
    /// 插拔，光环快照本身的字段计算由 <c>core/rules/skill</c> 层的 <c>AuraSnapshotTests</c> 覆盖，
    /// 两层各自负责一段，不重复验证同一件事）。除 <see cref="GetActiveAuraSnapshots"/>/<see
    /// cref="GetActiveAuraDefs"/>/<see cref="GetStacks"/> 外的其余接口成员本模块测试未用到，均给
    /// 最小无副作用实现。</summary>
    internal sealed class FakeAuraQuery : IAuraQuery
    {
        private readonly Dictionary<Id, List<AuraSnapshot>> _snapshots = new Dictionary<Id, List<AuraSnapshot>>();

        private List<AuraSnapshot> SnapshotsOf(Id unitId) =>
            _snapshots.TryGetValue(unitId, out var list) ? list : new List<AuraSnapshot>();

        public bool HasAura(Id unitId, Id auraDefId) => GetStacks(unitId, auraDefId) > 0;

        public int GetStacks(Id unitId, Id auraDefId)
        {
            foreach (var snap in SnapshotsOf(unitId))
            {
                if (snap.AuraDefId.Equals(auraDefId)) return snap.Stacks;
            }
            return 0;
        }

        public ControlFlags GetControlFlags(Id unitId) => ControlFlags.None;

        public bool IsImmune(Id unitId, Id school, EffectKind kind) => false;

        public double ConsumeAbsorb(Id unitId, Id school, double amount) => 0;

        public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId)
        {
            var result = new List<Id>();
            foreach (var snap in SnapshotsOf(unitId)) result.Add(snap.AuraDefId);
            return result;
        }

        public IReadOnlyList<AuraSnapshot> GetActiveAuraSnapshots(Id unitId) =>
            _snapshots.TryGetValue(unitId, out var list) ? list : (IReadOnlyList<AuraSnapshot>)Array.Empty<AuraSnapshot>();

        public void SetSnapshotsForTest(Id unitId, IReadOnlyList<AuraSnapshot> snapshots) =>
            _snapshots[unitId] = new List<AuraSnapshot>(snapshots);
    }

    /// <summary>消费方反馈第 1/4 条（2026-09-21，ADR-0056）测试用最小假 <see cref="IUnitAccess"/>：
    /// 只为了满足 <see cref="TargetPathProvider"/> 七参数构造函数的非空前提（该重载同时携带
    /// <see cref="IUnitAccess"/>/<see cref="ICreatureTemplateQuery"/> 两个此前已有的可选能力），
    /// <c>target.casting</c>/<c>target.auras</c> 两条新子路径本身不读取这两个依赖（见
    /// <see cref="TargetPathProvider.Resolve"/>），因此本 Fake 不需要真正模拟单位存在性——全部
    /// 成员给最小无副作用实现即可，不影响任何测试断言。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        public IReadOnlyList<Id> AllUnits => Array.Empty<Id>();

        public bool Exists(Id unitId) => false;

        public Vec2 GetPosition(Id unitId) => Vec2.Zero;

        public void SetPosition(Id unitId, Vec2 position)
        {
        }

        public Id GetFaction(Id unitId) => new Id("unit.faction_unset");

        public int GetLevel(Id unitId) => 1;

        public double GetFacing(Id unitId) => 0;

        public bool IsAlive(Id unitId) => true;

        public void SetAlive(Id unitId, bool alive)
        {
        }

        public Id? GetTemplateId(Id unitId) => null;

        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();
    }

    /// <summary>同 <see cref="FakeUnitAccess"/> 判断记录：只为满足 <see cref="TargetPathProvider"/>
    /// 七参数构造函数的非空前提，<c>target.casting</c>/<c>target.auras</c> 不读取本依赖。</summary>
    internal sealed class FakeCreatureTemplateQuery : ICreatureTemplateQuery
    {
        public CreatureTemplate Get(Id templateId) => throw new ArgumentException($"未登记的生物模板：{templateId}");

        public bool HasFlag(Id templateId, NpcFlag flag) => false;
    }

    /// <summary>
    /// 消费方反馈第九批（阻塞，2026-09-22，ADR-0066）测试用最小 <see cref="IExprHostFactory"/> 假实现：
    /// 本模块 <c>player.area.*</c> 用例的 <c>area.trigger_def</c> 测试数据均不带 <c>condition</c>
    /// 字段，<c>AreaTriggerHost.ConditionPasses</c> 在 <c>ConditionNode == null</c> 时直接短路返回
    /// <c>true</c>、从不调用 <see cref="CreateFor"/>（见该方法源码）——这里给一个不会被真正调用的最小
    /// 占位实现，仅用于满足 <see cref="AreaTriggerHost"/> 构造函数的非空参数要求。</summary>
    internal sealed class NullExprHostFactory : IExprHostFactory
    {
        private sealed class Host : IExprHost
        {
            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) =>
                throw new NotSupportedException("测试数据不带 condition，不应调用到这里。");
        }

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host();
    }

    /// <summary>消费方反馈第九批（阻塞，ADR-0066）测试用最小 <see cref="IEffectSink"/> 假实现：本模块
    /// <c>player.area.*</c> 用例只驱动 <c>AreaTriggerHost.Evaluate</c>，从不触发普通攻击真正挥击，
    /// 全部方法不应被调用到——<see cref="Core.Rules.Combat.AutoAttackHost"/> 构造要求非空实例，仅此
    /// 而已。</summary>
    internal sealed class NullEffectSink : IEffectSink
    {
        public ResolveResult ApplyEffect(EffectContext context) =>
            throw new NotSupportedException("测试不驱动普通攻击挥击，不应调用到这里。");

        public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
            throw new NotSupportedException("测试不驱动普通攻击挥击，不应调用到这里。");

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) =>
            throw new NotSupportedException("测试不驱动普通攻击挥击，不应调用到这里。");
    }

    /// <summary>同 <see cref="NullEffectSink"/> 判断记录：<see cref="Core.Rules.Combat.AutoAttackHost"/>
    /// 构造要求的最小 <see cref="IWeaponDamageQuery"/> 占位实现，恒返回"无武器"（0 伤害），
    /// <c>player.area.*</c> 用例不关心普通攻击伤害数值。</summary>
    internal sealed class NullWeaponDamageQuery : IWeaponDamageQuery
    {
        public double GetWeaponBaseDamage(Id unitId) => 0.0;
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
        public readonly FakeAuraQuery AuraQuery = new FakeAuraQuery();
        public readonly FakeUnitAccess UnitAccess = new FakeUnitAccess();
        public readonly FakeCreatureTemplateQuery CreatureTemplates = new FakeCreatureTemplateQuery();
        public readonly InMemoryUiDiagnostics Diagnostics = new InMemoryUiDiagnostics();

        /// <summary>缺口 4：真实 <see cref="Core.Carriers.Unit.SkillBindingHost"/>，knownSkillQuery
        /// 恒返回 true（测试不关心"已知技能"校验，见该类型构造参数）。</summary>
        public readonly Core.Carriers.Unit.SkillBindingHost SkillBindings;

        public Id? CurrentTarget;

        /// <summary>消费方反馈第九批（阻塞，ADR-0066）：<paramref name="withAreaTrigger"/> 为
        /// <c>true</c> 时非空——真实 <see cref="AreaTriggerHost"/>（经 <see cref="PlayerId"/> 的
        /// <c>Evaluate</c> 驱动进入/离开），供 <c>player.area.*</c> 用例调用。</summary>
        public readonly AreaTriggerHost? AreaTrigger;

        /// <summary>同上：<paramref name="withAreaTrigger"/> 为 <c>true</c> 时非空——承载
        /// <c>area.trigger_def</c> 测试数据的真实 <see cref="IDataRegistry"/>，经
        /// <see cref="AddAreaTrigger"/> 添加行后需要调用方自行 <see cref="IDataRegistry.LoadAll"/>。</summary>
        public readonly IDataRegistry? AreaTriggerData;

        public readonly UiDataSource DataSource;

        /// <summary>消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：<paramref name="skillBookForPathProvider"/>
        /// 可选覆盖 <see cref="PlayerPathProvider"/> 背后实际使用的 <see cref="ISkillBookQuery"/>——
        /// 默认（<c>null</c>）与改动前逐字节一致，用 <see cref="SkillBook"/>（<see
        /// cref="FakeSkillBookQuery"/>）；"真实触发一次技能进入冷却"一类用例需要 <c>player.skill.
        /// &lt;id&gt;.cooldown</c> 路径与 <c>ActionBarViewModel</c> 的 <c>skillCatalog</c> 参数读到
        /// 同一个真实 <see cref="Core.Rules.Skill.CooldownTracker"/> 实例（见
        /// <see cref="RealCooldownSkillBookQuery"/>），因此需要覆盖这里，<see cref="SkillBook"/>
        /// 字段本身不受影响，仍可用于其它既有测试。
        /// <para>
        /// 消费方反馈第九批（阻塞，ADR-0066）：<paramref name="areaTriggerRows"/> 非空（含空数组）时
        /// 额外构造一份真实 <see cref="AreaTriggerHost"/> + <see cref="IDataRegistry"/>（见
        /// <see cref="AreaTrigger"/>/<see cref="AreaTriggerData"/>），把全部行一次性加载进注册表并按
        /// <see cref="DefaultMapId"/> 登记进宿主，再把宿主/注册表传给 <see cref="PlayerPathProvider"/>
        /// 十四参数重载——用真实宿主/真实数据而不是 Fake，是因为"当前区域"定义本身就是
        /// <c>GetActiveTriggerIds</c> 的进入序号排序 + <c>area.trigger_def</c> 的 <c>name_key</c>
        /// 过滤这两处真实实现的组合，Fake 化会测不到这套组合逻辑本身；一次性加载（而不是像
        /// <c>core/gameplay/area_trigger/tests</c> 那样逐条 <c>Register</c>）是因为这里额外需要一份
        /// 可供 <see cref="PlayerPathProvider"/> 按 id 反查 <c>name_key</c> 的注册表，让 <c>host.
        /// LoadForMap</c> 与该注册表读到同一份数据，与生产装配（<c>PresentationAssembly</c> 用同一个
        /// <c>registry</c> 实例喂 <c>GameplayAssembly.AreaTrigger.LoadForMap</c> 与
        /// <c>PlayerPathProvider</c>）同一处理口径。默认 <c>null</c> 时与改动前逐字节一致（不装配
        /// <c>area.*</c> 路径，查询恒为"无"）。
        /// </para>
        /// </summary>
        public UiWorldFixture(ISkillBookQuery? skillBookForPathProvider = null, IReadOnlyList<JsonObject>? areaTriggerRows = null)
        {
            SkillBindings = new Core.Carriers.Unit.SkillBindingHost(EventBus, (_, __) => true);

            StatHost.RegisterUnit(PlayerId);
            StatHost.RegisterUnit(TargetId);

            IUiPathProvider playerProvider;
            if (areaTriggerRows != null)
            {
                var source = new InMemoryDataSource();
                var root = new JsonObjectBuilder()
                    .Add("table", new JsonString(AreaTriggerSchemas.TriggerDef.Name))
                    .Add("schema_version", new JsonNumber(1))
                    .Add("rows", new JsonArray(areaTriggerRows.Cast<JsonValue>()))
                    .Build();
                source.Add(AreaTriggerSchemas.TriggerDef.Name, JsonWriter.Write(root));

                var registry = new DataRegistry(source, EventBus, new DataRegistryOptions { FailOnUnknownTable = false });
                registry.RegisterSchema(AreaTriggerSchemas.TriggerDef);
                var report = registry.LoadAll();
                if (report.IsBlocking)
                {
                    throw new InvalidOperationException(
                        "UiWorldFixture 的 area.trigger_def 测试数据未通过校验：\n" +
                        string.Join("\n", report.Issues.Select(i => i.ToString())));
                }

                AreaTriggerData = registry;

                var worldSim = new WorldSim(EventBus);
                var worldState = new WorldState(EventBus);
                AreaTrigger = new AreaTriggerHost(worldSim, worldState, EventBus, new NullExprHostFactory());
                AreaTrigger.LoadForMap(DefaultMapId, registry);

                var autoAttackHost = new Core.Rules.Combat.AutoAttackHost(
                    UnitAccess, AuraQuery, new NullEffectSink(), new NullWeaponDamageQuery(), new Id("school.physical"));
                playerProvider = new PlayerPathProvider(
                    PlayerId, StatHost, PowerHost, Progression, Inventory, Equipment, Quest, Economy,
                    skillBookForPathProvider ?? SkillBook, AuraQuery, UnitAccess,
                    autoAttackHost, AreaTrigger, AreaTriggerData);
            }
            else
            {
                playerProvider = new PlayerPathProvider(
                    PlayerId, StatHost, PowerHost, Progression, Inventory, Equipment, Quest, Economy,
                    skillBookForPathProvider ?? SkillBook, AuraQuery);
            }

            var targetProvider = new TargetPathProvider(
                () => CurrentTarget, StatHost, PowerHost, UnitAccess, CreatureTemplates, SkillBook, AuraQuery);
            var unitProvider = new UnitPathProvider(StatHost, PowerHost);

            DataSource = new UiDataSource(EventBus, new IUiPathProvider[] { playerProvider, targetProvider, unitProvider }, Diagnostics);
        }

        /// <summary>见 <see cref="AreaTrigger"/> 判断记录：本夹具全部 <c>area.trigger_def</c> 测试行
        /// 固定挂在同一张地图下，<see cref="AreaTriggerRow"/> 不需要每条用例重复传 map_id。</summary>
        public static readonly Id DefaultMapId = new Id("world.sample_map");

        /// <summary>见 <see cref="AreaTriggerData"/> 判断记录：<c>area.trigger_def</c> 测试行——圆形
        /// 范围，圆心固定 (0,0)，半径/是否有显示名可调，惯例同
        /// <c>core/gameplay/area_trigger/tests/TestSupport.AreaTriggerTestSupport.QuestExploreRow</c>。</summary>
        public static JsonObject AreaTriggerRow(string id, double radius, string? nameKey) =>
            new JsonObjectBuilder()
                .Add("id", new JsonString(id))
                .Add("map_id", new JsonString(DefaultMapId.Value))
                .Add("shape", new JsonObjectBuilder()
                    .Add("kind", new JsonString("circle"))
                    .Add("radius", new JsonNumber(radius))
                    .Add("center", new JsonObjectBuilder().Add("x", new JsonNumber(0)).Add("y", new JsonNumber(0)).Build())
                    .Build())
                .Add("trigger_type", new JsonString("quest_explore"))
                .Add("params", new JsonObjectBuilder().Build())
                .Add("name_key", nameKey == null ? (JsonValue)JsonNull.Instance : new JsonString(nameKey))
                .Build();
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

    /// <summary>缺口 12 测试用最小假 <see cref="Presentation.VfxSfx.Contracts.IAudioLayerVolumeHost"/>：
    /// 纯内存态，记录每次 <see cref="SetVolume"/> 调用供断言，不接 <c>IAudio</c>/<c>ISfxPlayer</c>/
    /// <c>ISettingsStore</c>（真实落地与持久化行为由 <c>presentation/vfx_sfx/tests</c> 覆盖）。</summary>
    internal sealed class FakeAudioLayerVolumeHost : IAudioLayerVolumeHost
    {
        private readonly Dictionary<string, double> _volumes;

        public readonly List<(string Layer, double Volume)> SetCalls = new List<(string, double)>();

        public FakeAudioLayerVolumeHost(IReadOnlyList<string>? layers = null)
        {
            Layers = layers ?? new[] { "combat", "ui", "music" };
            _volumes = new Dictionary<string, double>();
            foreach (var layer in Layers)
            {
                _volumes[layer] = 1.0;
            }
        }

        public IReadOnlyList<string> Layers { get; }

        public double GetVolume(string layer) => _volumes.TryGetValue(layer, out var v) ? v : 1.0;

        public void SetVolume(string layer, double volume)
        {
            _volumes[layer] = volume;
            SetCalls.Add((layer, volume));
        }
    }
}

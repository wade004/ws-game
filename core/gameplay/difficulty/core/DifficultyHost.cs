using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Numbers.Faction;
using Core.Rules.Common;

namespace Core.Gameplay.Difficulty
{
    /// <summary>
    /// <see cref="IDifficultyHost"/> 的默认（唯一）实现（见 08 第 5 节、第 9 节汇总表
    /// <c>Difficulty</c> 行）。构造期从 <see cref="IDataRegistryView"/> 一次性解析
    /// <c>diff.tier</c> 表，之后只读（惯例同 <c>Core.Carriers.Creature.CreatureFactory</c>）；
    /// 构造期同时订阅一次 <c>creature.spawned</c>（<see cref="CarriersEventKeys.CreatureSpawned"/>），
    /// 对此后每个新生成的（敌对，或全体，见 <see cref="DifficultyOptions.ApplyToAll"/>）单位施加
    /// 当前档位的 <c>modifier_aura_refs</c>（08 第 5.2 节"已进行中的遭遇不因难度切换而重算，只
    /// 影响后续新生成的内容"——本类型天然满足这一点：只对"订阅生效之后才发生"的
    /// <c>creature.spawned</c> 事件反应，不做任何"扫描现有单位"的操作）。
    /// 同时实现 <see cref="IPersistable"/>（自定义存档段 <see cref="SectionKeyValue"/>，见
    /// 10 第 2.1 节"meta.difficulty_id 由存档系统写；本模块提供 CurrentTier 供组装层填
    /// SaveRequest.DifficultyId"、任务书拍板"并提供段 world.difficulty 保存当前档与作用域"——
    /// 10 文档 <see cref="SaveSections"/> 未预留这一段的常量，本类型自行约定段 key 字符串，见
    /// <see cref="IPersistable.SectionKey"/> 注释"自定义段可以是任意非空字符串"）。
    /// </summary>
    public sealed class DifficultyHost : IDifficultyHost, IPersistable
    {
        /// <summary><see cref="DifficultyAppliedEvent.ScopeId"/> 在
        /// <see cref="DifficultyScope.Global"/> 下的固定哨兵值（见该事件类型判断记录）。</summary>
        public static readonly Id GlobalScopeId = new Id("diff.scope.global");

        /// <summary>本模块自定义存档段 key（见本类型顶部判断记录）。</summary>
        public const string SectionKeyValue = "world.difficulty";

        private readonly Dictionary<string, DifficultyTierDefinition> _tiers =
            new Dictionary<string, DifficultyTierDefinition>(StringComparer.Ordinal);

        private readonly IEventBus _bus;
        private readonly IEffectSink _effectSink;
        private readonly IFactionMatrix _factions;
        private readonly IUnitAccess _units;
        private readonly DifficultyOptions _options;

        public DifficultyHost(
            IDataRegistryView registry,
            IEventBus bus,
            IEffectSink effectSink,
            IFactionMatrix factions,
            IUnitAccess units,
            DifficultyOptions options)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _effectSink = effectSink ?? throw new ArgumentNullException(nameof(effectSink));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            LoadTiers(registry);

            _bus.Subscribe<CreatureSpawnedEvent>(CarriersEventKeys.CreatureSpawned, OnCreatureSpawned);
        }

        // -----------------------------------------------------------------
        // IDifficultyHost
        // -----------------------------------------------------------------

        public Id? CurrentTier { get; private set; }

        public DifficultyScope? CurrentScope { get; private set; }

        public Id? CurrentMapId { get; private set; }

        public bool Apply(Id tierId, DifficultyScope scope, Id? mapId)
        {
            var tier = RequireTier(tierId);

            if (scope == DifficultyScope.Map && !mapId.HasValue)
            {
                throw new ArgumentException("scope 为 Map 时 mapId 必填", nameof(mapId));
            }
            if (scope == DifficultyScope.Global && mapId.HasValue)
            {
                throw new ArgumentException("scope 为 Global 时 mapId 必须为 null", nameof(mapId));
            }

            if (CurrentTier.HasValue && !_options.AllowMidSwitch)
            {
                // 判断记录见 IDifficultyHost.Apply 注释："已应用过 + 不允许中途切换" 时不做任何
                // 改动、不发事件，返回 false。
                return false;
            }

            CurrentTier = tier.Id;
            CurrentScope = scope;
            CurrentMapId = scope == DifficultyScope.Map ? mapId : null;

            // 判断记录：难度选择发生在"新游戏开始/关卡入口"（08 第 5.2 节），属于非 tick 上下文的
            // 一次性动作（同 IEventBus.PublishImmediate 注释"供非 tick 上下文使用，例如应用状态机、
            // 场景路由发出的事件"惯例），不走 Enqueue + DispatchPending 的 tick 末批处理。
            var scopeId = scope == DifficultyScope.Global ? GlobalScopeId : mapId!.Value;
            _bus.PublishImmediate(new DifficultyAppliedEvent(scopeId, tier.Id));

            return true;
        }

        public double LootMultiplier => CurrentTier.HasValue ? RequireTier(CurrentTier.Value).LootMultiplier : 1.0;

        public bool AllowMidSwitch => _options.AllowMidSwitch;

        // -----------------------------------------------------------------
        // creature.spawned 订阅：对新生成单位施加修正光环
        // -----------------------------------------------------------------

        private void OnCreatureSpawned(CreatureSpawnedEvent evt)
        {
            if (!CurrentTier.HasValue)
            {
                return;
            }

            if (CurrentScope == DifficultyScope.Map)
            {
                // 判断记录：IUnitAccess.GetMapId 是集成任务补齐的契约缺口，默认实现返回 null
                // （"未知/不接入地图概念"）；Map 作用域下无法判定归属地图的单位一律视为"不在作用域
                // 内"，不施加修正光环——保守处理，避免把全局都当作命中 Map 作用域。
                var unitMapId = _units.GetMapId(evt.EntityId);
                // CurrentMapId 正常情况下随 Apply(Map, mapId) 与 CurrentScope 同时设置，理应非空；
                // 这里仍显式判空而不是 CurrentMapId!.Value——防御 Load 恢复了损坏/不一致存档数据
                // （scope=Map 但 mapId 缺失）时不让本事件处理器抛异常中断，保守按"不在作用域内"处理。
                if (!unitMapId.HasValue || !CurrentMapId.HasValue || !unitMapId.Value.Equals(CurrentMapId.Value))
                {
                    return;
                }
            }

            if (!_units.Exists(evt.EntityId))
            {
                return;
            }

            if (!_options.ApplyToAll)
            {
                var unitFaction = _units.GetFaction(evt.EntityId);
                if (!_factions.IsHostile(unitFaction, _options.PlayerFactionId))
                {
                    return;
                }
            }

            var tier = RequireTier(CurrentTier.Value);
            for (var i = 0; i < tier.ModifierAuraRefs.Count; i++)
            {
                _effectSink.ApplyAura(evt.EntityId, tier.ModifierAuraRefs[i], sourceId: tier.Id);
            }
        }

        // -----------------------------------------------------------------
        // IPersistable（存档段 world.difficulty，见 10 第 2.1 节 + 任务书拍板补录）
        // -----------------------------------------------------------------

        public string SectionKey => SectionKeyValue;

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            builder.Add("tierId", CurrentTier.HasValue ? (JsonValue)new JsonString(CurrentTier.Value.Value) : JsonNull.Instance);
            builder.Add("scope", CurrentScope.HasValue ? (JsonValue)new JsonString(CurrentScope.Value.ToString()) : JsonNull.Instance);
            builder.Add("mapId", CurrentMapId.HasValue ? (JsonValue)new JsonString(CurrentMapId.Value.Value) : JsonNull.Instance);
            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            CurrentTier = null;
            CurrentScope = null;
            CurrentMapId = null;

            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException($"world.difficulty 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            if (obj.TryGetValue("tierId", out var tierIdValue) && tierIdValue is JsonString tierIdStr)
            {
                CurrentTier = new Id(tierIdStr.Value);
            }

            if (obj.TryGetValue("scope", out var scopeValue) && scopeValue is JsonString scopeStr
                && Enum.TryParse<DifficultyScope>(scopeStr.Value, out var scope))
            {
                CurrentScope = scope;
            }

            if (obj.TryGetValue("mapId", out var mapIdValue) && mapIdValue is JsonString mapIdStr)
            {
                CurrentMapId = new Id(mapIdStr.Value);
            }

            // 读档不是一次业务 Apply（同 WorldState.Load 判断记录）：不重新对现有单位做任何扫描，
            // 也不发 DifficultyAppliedEvent——后续新生成的单位仍会经 OnCreatureSpawned 正确命中
            // 刚恢复的 CurrentTier/CurrentScope/CurrentMapId。
        }

        // -----------------------------------------------------------------
        // 数据加载 / 内部辅助
        // -----------------------------------------------------------------

        private void LoadTiers(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll(DifficultySchemas.Tier.Name))
            {
                var tier = DifficultyTierDefinition.FromRecord(record);
                _tiers[tier.Id.Value] = tier;
            }
        }

        private DifficultyTierDefinition RequireTier(Id tierId)
        {
            if (_tiers.TryGetValue(tierId.Value, out var tier))
            {
                return tier;
            }
            throw new ArgumentException($"未知的 diff.tier \"{tierId}\"", nameof(tierId));
        }
    }
}

using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// 一条 <c>encounter.def</c> 记录的强类型视图（见 08 第 4.1 节全部字段）。只做结构抽取，
    /// <c>victory_condition</c>/<c>defeat_condition</c>/<c>waves[].trigger_condition</c>/
    /// <c>phases[].enter_condition</c> 均保留原始 Expr 文本，解析放到 <c>EncounterHost</c> 构造期
    /// （惯例同 <c>AchievementCriterion</c>/<c>AchievementDefinition</c>）。
    /// </summary>
    public sealed class EncounterDefinition
    {
        public Id Id { get; }

        public IReadOnlyList<EncounterUnitSpec> Units { get; }

        public IReadOnlyList<EncounterWaveDefinition> Waves { get; }

        public IReadOnlyList<EncounterPhaseDefinition> Phases { get; }

        public EncounterArenaRules? ArenaRules { get; }

        public string VictoryConditionText { get; }

        public string DefeatConditionText { get; }

        public RewardBundle Rewards { get; }

        /// <summary>覆盖场景默认战斗时间模型（08 第 4.1 节 <c>combat_mode_override</c>），
        /// <c>"continuous"</c>/<c>"discrete"</c> 之一；本类型本身只登记读取——真正的运行时切换由
        /// `core/gameplay/assembly.GameplayAssembly` 订阅 `encounter.started` 后经
        /// `EncounterHost.TryGetModeOverride` 转交 `TimeModelSwitch.SetPendingOverride` 执行
        /// （ADR-0013 离散时间模型已接线，见 `encounter/README.md` 判断记录 6）。</summary>
        public string? CombatModeOverride { get; }

        /// <summary>覆盖默认先攻策略（08 第 4.1 节 <c>initiative_override</c>），原样保留 JSON 结构
        /// （字段本身只在 <c>combat_mode_override</c> 为 <c>discrete</c> 时才有意义）。同
        /// <see cref="CombatModeOverride"/>，本类型只登记读取，真正生效由
        /// `GameplayAssembly`/`TimeModelSwitch` 承担。</summary>
        public JsonObject? InitiativeOverride { get; }

        private EncounterDefinition(
            Id id, IReadOnlyList<EncounterUnitSpec> units, IReadOnlyList<EncounterWaveDefinition> waves,
            IReadOnlyList<EncounterPhaseDefinition> phases, EncounterArenaRules? arenaRules,
            string victoryConditionText, string defeatConditionText, RewardBundle rewards,
            string? combatModeOverride, JsonObject? initiativeOverride)
        {
            Id = id;
            Units = units;
            Waves = waves;
            Phases = phases;
            ArenaRules = arenaRules;
            VictoryConditionText = victoryConditionText;
            DefeatConditionText = defeatConditionText;
            Rewards = rewards;
            CombatModeOverride = combatModeOverride;
            InitiativeOverride = initiativeOverride;
        }

        public static EncounterDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");

            var unitsArray = record.GetArray("units");
            if (unitsArray.Count == 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "units", "至少需要一个参战单位");
            }
            var units = new List<EncounterUnitSpec>(unitsArray.Count);
            for (var i = 0; i < unitsArray.Count; i++)
            {
                if (!(unitsArray[i] is JsonObject unitJson))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, $"units[{i}]", "必须是对象");
                }
                units.Add(ParseUnit(record, unitJson, i));
            }

            var waves = new List<EncounterWaveDefinition>();
            if (record.TryGetArray("waves", out var wavesArray))
            {
                for (var i = 0; i < wavesArray.Count; i++)
                {
                    if (!(wavesArray[i] is JsonObject waveJson))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, $"waves[{i}]", "必须是对象");
                    }
                    waves.Add(ParseWave(record, waveJson, i));
                }
            }

            var phases = new List<EncounterPhaseDefinition>();
            if (record.TryGetArray("phases", out var phasesArray))
            {
                for (var i = 0; i < phasesArray.Count; i++)
                {
                    if (!(phasesArray[i] is JsonObject phaseJson))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, $"phases[{i}]", "必须是对象");
                    }
                    phases.Add(ParsePhase(record, phaseJson, i));
                }
            }

            EncounterArenaRules? arenaRules = null;
            if (record.TryGetObject("arena_rules", out var arenaObj))
            {
                if (!arenaObj.TryGetValue("bounds_shape", out var boundsRaw) || !(boundsRaw is JsonObject boundsObj))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "arena_rules.bounds_shape", "必须是对象");
                }
                var resetIfLeave = arenaObj.TryGetValue("reset_if_leave", out var resetRaw) && resetRaw is JsonBool resetBool && resetBool.Value;
                arenaRules = new EncounterArenaRules(EncounterShapeJson.Parse(boundsObj), resetIfLeave);
            }

            var victoryConditionText = record.GetString("victory_condition");
            var defeatConditionText = record.GetString("defeat_condition");

            var rewards = record.TryGetObject("rewards", out var rewardsObj) ? RewardBundle.FromRecord(rewardsObj) : RewardBundle.Empty;

            string? combatModeOverride = record.TryGetString("combat_mode_override", out var cmo) ? cmo : null;

            JsonObject? initiativeOverride = record.TryGetObject("initiative_override", out var iov) ? iov : null;

            return new EncounterDefinition(
                id, units, waves, phases, arenaRules, victoryConditionText, defeatConditionText, rewards,
                combatModeOverride, initiativeOverride);
        }

        private static EncounterUnitSpec ParseUnit(DataRecord record, JsonObject unitJson, int index)
        {
            Id? spawnRef = null;
            if (unitJson.TryGetValue("spawn_ref", out var spawnRaw) && spawnRaw is JsonString spawnStr)
            {
                if (!Id.TryParse(spawnStr.Value, out var parsedSpawn))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, $"units[{index}].spawn_ref", "必须是合法 Id 字符串");
                }
                spawnRef = parsedSpawn;
            }

            Id? templateRef = null;
            if (unitJson.TryGetValue("template_ref", out var templateRaw) && templateRaw is JsonString templateStr)
            {
                if (!Id.TryParse(templateStr.Value, out var parsedTemplate))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, $"units[{index}].template_ref", "必须是合法 Id 字符串");
                }
                templateRef = parsedTemplate;
            }

            Vec2? position = null;
            if (unitJson.TryGetValue("position", out var positionRaw) && positionRaw is JsonObject positionObj
                && positionObj.TryGetValue("x", out var xRaw) && xRaw is JsonNumber xNum
                && positionObj.TryGetValue("y", out var yRaw) && yRaw is JsonNumber yNum)
            {
                position = new Vec2(xNum.Value, yNum.Value);
            }

            try
            {
                return new EncounterUnitSpec(spawnRef, templateRef, position);
            }
            catch (ArgumentException ex)
            {
                throw new DataFieldException(record.Table.Name, record.Key, $"units[{index}]", ex.Message);
            }
        }

        private static EncounterWaveDefinition ParseWave(DataRecord record, JsonObject waveJson, int index)
        {
            if (!waveJson.TryGetValue("trigger_condition", out var triggerRaw) || !(triggerRaw is JsonString triggerStr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, $"waves[{index}].trigger_condition", "必须是字符串（Expr 文本）");
            }

            var spawnRefs = new List<Id>();
            if (waveJson.TryGetValue("spawn_refs", out var spawnRefsRaw) && spawnRefsRaw is JsonArray spawnRefsArr)
            {
                for (var i = 0; i < spawnRefsArr.Count; i++)
                {
                    if (!(spawnRefsArr[i] is JsonString s) || !Id.TryParse(s.Value, out var id))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, $"waves[{index}].spawn_refs[{i}]", "必须是合法 Id 字符串");
                    }
                    spawnRefs.Add(id);
                }
            }

            return new EncounterWaveDefinition(triggerStr.Value, spawnRefs);
        }

        private static EncounterPhaseDefinition ParsePhase(DataRecord record, JsonObject phaseJson, int index)
        {
            if (!phaseJson.TryGetValue("enter_condition", out var enterRaw) || !(enterRaw is JsonString enterStr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, $"phases[{index}].enter_condition", "必须是字符串（Expr 文本）");
            }

            var rotationOverride = new Dictionary<Id, Id>();
            if (phaseJson.TryGetValue("ai_rotation_override", out var rotationRaw) && rotationRaw is JsonObject rotationObj)
            {
                foreach (var kv in rotationObj)
                {
                    if (!Id.TryParse(kv.Key, out var keyId) || !(kv.Value is JsonString valueStr) || !Id.TryParse(valueStr.Value, out var valueId))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, $"phases[{index}].ai_rotation_override",
                            $"元素 \"{kv.Key}\" 不是合法的 Id -> Id 映射");
                    }
                    rotationOverride[keyId] = valueId;
                }
            }

            Id? onEnterHook = null;
            if (phaseJson.TryGetValue("on_enter_hook", out var hookRaw) && hookRaw is JsonString hookStr)
            {
                if (!Id.TryParse(hookStr.Value, out var parsedHook))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, $"phases[{index}].on_enter_hook", "必须是合法 Id 字符串");
                }
                onEnterHook = parsedHook;
            }

            return new EncounterPhaseDefinition(enterStr.Value, rotationOverride, onEnterHook);
        }
    }
}

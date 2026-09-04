using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Rules.ExprHost;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 把 <c>skill.*</c> 五张表的 <see cref="DataRecord"/> 解析为强类型定义并缓存（懒解析，
    /// 命中一次后常驻内存，与 <c>DataRegistry</c> 本身"加载期一次性校验、运行期只读"的分工一致——
    /// 本缓存只做结构转换，不重复校验器已经保证过的合法性）。找不到记录或字段格式非法时抛
    /// <see cref="InvalidOperationException"/>（对应"未通过校验的数据不应流入运行时"）。
    /// </summary>
    public sealed class SkillDefCache
    {
        private readonly IDataRegistryView _registry;
        private readonly Dictionary<Id, SkillDef> _skills = new Dictionary<Id, SkillDef>();
        private readonly Dictionary<Id, AuraDef> _auras = new Dictionary<Id, AuraDef>();
        private readonly Dictionary<Id, ProcDef> _procs = new Dictionary<Id, ProcDef>();
        private readonly Dictionary<Id, SpellModDefRecord> _spellMods = new Dictionary<Id, SpellModDefRecord>();
        private readonly Dictionary<Id, SkillBookDef> _books = new Dictionary<Id, SkillBookDef>();

        // 集成任务改动：解析 skill.proc_def.condition 用的 IExprSchema，默认改用集成任务提供的
        // core/rules/expr_host.RulesExprSchema（原先本模块自带的临时 PermissiveExprSchema 已被
        // 取代，见该类型所在文件顶部注释），保留可注入口子（构造参数 exprSchema）。
        private readonly IExprSchema _exprSchema;

        public SkillDefCache(IDataRegistryView registry, IExprSchema? exprSchema = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _exprSchema = exprSchema ?? RulesExprSchema.Instance;
        }

        public bool TryGetSkillDef(Id id, out SkillDef def)
        {
            if (_skills.TryGetValue(id, out def!))
            {
                return true;
            }

            var record = _registry.Get("skill.def", id);
            if (record == null)
            {
                def = null!;
                return false;
            }

            def = ParseSkillDef(record);
            _skills[id] = def;
            return true;
        }

        public SkillDef GetSkillDef(Id id)
        {
            if (TryGetSkillDef(id, out var def))
            {
                return def;
            }

            throw new InvalidOperationException($"skill.def 未找到记录：\"{id}\"");
        }

        public bool TryGetAuraDef(Id id, out AuraDef def)
        {
            if (_auras.TryGetValue(id, out def!))
            {
                return true;
            }

            var record = _registry.Get("skill.aura_def", id);
            if (record == null)
            {
                def = null!;
                return false;
            }

            def = ParseAuraDef(record);
            _auras[id] = def;
            return true;
        }

        public AuraDef GetAuraDef(Id id)
        {
            if (TryGetAuraDef(id, out var def))
            {
                return def;
            }

            throw new InvalidOperationException($"skill.aura_def 未找到记录：\"{id}\"");
        }

        public bool TryGetProcDef(Id id, out ProcDef def)
        {
            if (_procs.TryGetValue(id, out def!))
            {
                return true;
            }

            var record = _registry.Get("skill.proc_def", id);
            if (record == null)
            {
                def = null!;
                return false;
            }

            def = ParseProcDef(record);
            _procs[id] = def;
            return true;
        }

        public ProcDef GetProcDef(Id id)
        {
            if (TryGetProcDef(id, out var def))
            {
                return def;
            }

            throw new InvalidOperationException($"skill.proc_def 未找到记录：\"{id}\"");
        }

        public bool TryGetSpellModDef(Id id, out SpellModDefRecord def)
        {
            if (_spellMods.TryGetValue(id, out def!))
            {
                return true;
            }

            var record = _registry.Get("skill.spell_mod_def", id);
            if (record == null)
            {
                def = null!;
                return false;
            }

            def = ParseSpellModDef(record);
            _spellMods[id] = def;
            return true;
        }

        public SpellModDefRecord GetSpellModDef(Id id)
        {
            if (TryGetSpellModDef(id, out var def))
            {
                return def;
            }

            throw new InvalidOperationException($"skill.spell_mod_def 未找到记录：\"{id}\"");
        }

        public SkillBookDef GetBook(Id id)
        {
            if (_books.TryGetValue(id, out var def))
            {
                return def;
            }

            var record = _registry.Get("skill.book", id);
            if (record == null)
            {
                throw new InvalidOperationException($"skill.book 未找到记录：\"{id}\"");
            }

            def = ParseBook(record);
            _books[id] = def;
            return def;
        }

        // -----------------------------------------------------------------
        // 解析
        // -----------------------------------------------------------------

        private static SkillDef ParseSkillDef(DataRecord record)
        {
            var id = record.GetId("id");
            var school = record.GetId("school");
            var kind = record.GetString("kind");
            var isPassive = kind == "passive";
            var range = record.GetNumber("range");

            var tags = record.TryGetIdList("tags", out var tagList) ? tagList : Array.Empty<Id>();

            var castTime = record.GetNumber("cast_time");
            var channelTime = record.TryGetNumber("channel_time", out var ct) ? ct : 0;

            var cost = new List<(Id, double)>();
            if (record.TryGetArray("cost", out var costArray))
            {
                for (var i = 0; i < costArray.Count; i++)
                {
                    var entry = (JsonObject)costArray[i];
                    var powerType = new Id(((JsonString)entry["power_type"]).Value);
                    var amount = ((JsonNumber)entry["amount"]).Value;
                    cost.Add((powerType, amount));
                }
            }

            Id? cooldownCategory = record.TryGetId("cooldown_category", out var cc) ? cc : (Id?)null;
            var cooldownDuration = record.TryGetNumber("cooldown_duration", out var cd) ? cd : 0;

            int? chargesMax = null;
            double chargesRecharge = 0;
            if (record.TryGetObject("charges", out var chargesObj))
            {
                chargesMax = (int)((JsonNumber)chargesObj["max"]).Value;
                chargesRecharge = ((JsonNumber)chargesObj["recharge_time"]).Value;
            }

            var respectsGcd = record.GetBool("respects_gcd");
            var targetShapeRef = record.GetId("target_shape_ref");

            var effects = ParseEffectRefs(record.GetArray("effects"));

            var interruptFlags = InterruptFlags.None;
            if (record.TryGetArray("interrupt_flags", out var flagsArray))
            {
                for (var i = 0; i < flagsArray.Count; i++)
                {
                    var text = ((JsonString)flagsArray[i]).Value;
                    interruptFlags |= text switch
                    {
                        "movement" => InterruptFlags.Movement,
                        "damage_taken" => InterruptFlags.DamageTaken,
                        "control" => InterruptFlags.Control,
                        _ => InterruptFlags.None,
                    };
                }
            }

            return new SkillDef(
                id, school, isPassive, range, tags, castTime, channelTime, cost,
                cooldownCategory, cooldownDuration, chargesMax, chargesRecharge,
                respectsGcd, targetShapeRef, effects, interruptFlags);
        }

        internal static IReadOnlyList<EffectRef> ParseEffectRefs(JsonArray array)
        {
            var list = new List<EffectRef>(array.Count);
            for (var i = 0; i < array.Count; i++)
            {
                var obj = (JsonObject)array[i];
                var kind = EffectKindNames.Parse(((JsonString)obj["kind"]).Value);
                var @params = obj.TryGetValue("params", out var p) && p is JsonObject po ? po : EmptyObject();
                list.Add(new EffectRef(kind, @params));
            }

            return list;
        }

        private static AuraDef ParseAuraDef(DataRecord record)
        {
            var id = record.GetId("id");
            double? duration = record.TryGetNumber("duration", out var d) ? d : (double?)null;
            var maxStacks = record.TryGetInt("max_stacks", out var ms) ? (int)ms : 1;
            Id? stackCategory = record.TryGetId("stack_category", out var sc) ? sc : (Id?)null;
            Id? dispelType = record.TryGetId("dispel_type", out var dt) ? dt : (Id?)null;

            var rawEffects = record.GetArray("effects");
            var effects = new List<AuraEffectEntry>(rawEffects.Count);
            for (var i = 0; i < rawEffects.Count; i++)
            {
                var obj = (JsonObject)rawEffects[i];
                var kind = AuraEffectKindNames.Parse(((JsonString)obj["kind"]).Value);
                var @params = obj.TryGetValue("params", out var p) && p is JsonObject po ? po : EmptyObject();
                effects.Add(new AuraEffectEntry(kind, @params));
            }

            return new AuraDef(id, duration, maxStacks, stackCategory, dispelType, effects);
        }

        private ProcDef ParseProcDef(DataRecord record)
        {
            var id = record.GetId("id");
            var triggerEvent = record.GetId("trigger_event");

            ExprNode? condition = null;
            if (record.TryGetString("condition", out var conditionText) && !string.IsNullOrEmpty(conditionText))
            {
                condition = ExprParser.Parse(conditionText, _exprSchema);
            }

            var triggerSkill = record.GetId("trigger_skill");
            double? internalCooldown = record.TryGetNumber("internal_cooldown", out var icd) ? icd : (double?)null;
            var procChance = record.GetNumber("proc_chance");

            return new ProcDef(id, triggerEvent, condition, triggerSkill, internalCooldown, procChance);
        }

        private static SpellModDefRecord ParseSpellModDef(DataRecord record)
        {
            var id = record.GetId("id");
            var dimension = ParseDimension(record.GetString("target_dimension"));
            var op = record.GetString("op") == "flat" ? SpellModOp.Flat : SpellModOp.Pct;
            var value = record.GetNumber("value");

            var schools = Array.Empty<Id>() as IReadOnlyList<Id>;
            var tags = Array.Empty<Id>() as IReadOnlyList<Id>;
            var skillIds = Array.Empty<Id>() as IReadOnlyList<Id>;

            if (record.TryGetObject("affects", out var affectsObj))
            {
                if (affectsObj.TryGetValue("schools", out var s) && s is JsonArray sa) schools = ParseIdArray(sa);
                if (affectsObj.TryGetValue("tags", out var t) && t is JsonArray ta) tags = ParseIdArray(ta);
                if (affectsObj.TryGetValue("skill_ids", out var sk) && sk is JsonArray ska) skillIds = ParseIdArray(ska);
            }

            var affects = new SkillFilter(schools, tags, skillIds);
            return new SpellModDefRecord(id, dimension, op, value, affects);
        }

        private static IReadOnlyList<Id> ParseIdArray(JsonArray array)
        {
            var list = new List<Id>(array.Count);
            for (var i = 0; i < array.Count; i++)
            {
                list.Add(new Id(((JsonString)array[i]).Value));
            }

            return list;
        }

        private static SpellModDimension ParseDimension(string text) => text switch
        {
            "cast_time" => SpellModDimension.CastTime,
            "cost" => SpellModDimension.Cost,
            "cooldown" => SpellModDimension.Cooldown,
            "crit_chance" => SpellModDimension.CritChance,
            "effect_value" => SpellModDimension.EffectValue,
            "charges" => SpellModDimension.Charges,
            _ => throw new InvalidOperationException($"未知的 SpellMod target_dimension：\"{text}\""),
        };

        private static SkillBookDef ParseBook(DataRecord record)
        {
            var id = record.GetId("id");
            var rawEntries = record.GetArray("entries");
            var entries = new List<(int, Id)>(rawEntries.Count);
            for (var i = 0; i < rawEntries.Count; i++)
            {
                var obj = (JsonObject)rawEntries[i];
                var level = (int)((JsonNumber)obj["level"]).Value;
                var skillId = new Id(((JsonString)obj["skill_id"]).Value);
                entries.Add((level, skillId));
            }

            return new SkillBookDef(id, entries);
        }

        private static JsonObject EmptyObject() => new JsonObjectBuilder().Build();
    }
}

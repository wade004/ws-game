using System;
using System.Collections.Generic;
using System.Linq;
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

        // T-N3-2（ADR-0031 决策 1）：skill.base_curve 的已解析缓存——本表不属于"五张表"强类型定义
        // 之列（没有对应 Defs.cs 强类型），只缓存 PiecewiseCurve 本身，供 EffectDispatcher.
        // ApplyDamageOrHeal 按 base_curve_ref 取值。判断记录：不新增 Defs.cs 类型，直接缓存
        // Core.Foundation.Common.PiecewiseCurve——本表只有 entries 一个字段，没有强类型化的必要
        // （同 item.budget_curve/ItemBudgetCurve 判断记录"曲线表读出即插值载体，不需要额外包一层"，
        // 唯一差异是本表更简单、连独立包装类型都不需要）。
        private readonly Dictionary<Id, PiecewiseCurve> _baseCurves = new Dictionary<Id, PiecewiseCurve>();

        // T-N3-3（ADR-0031 决策 2/10）：skill.budget_rule.beat_seconds 的已解析缓存——同
        // _baseCurves 判断记录，本表（最小骨架，见 SkillSchemas.BudgetRule 类型注释）目前只有
        // id/beat_seconds 两个字段，不需要强类型 Defs.cs 包装，直接缓存解出的 double。
        private readonly Dictionary<Id, double> _beatSeconds = new Dictionary<Id, double>();

        // T-N3-9（ADR-0031 决策 2；06 第 3.10 节"玩家档/怪物档由反向引用决定"）：skill_id → 习得
        // 等级/档位的反查缓存，懒构建（首次调用 TryResolveBudgetAttribution 时一次性扫描
        // skill.book 与 creature.template/ai.rotation 两条来源，同 _skills 等既有缓存"命中一次后
        // 常驻内存"惯例）。两个字典任一为 null 代表"尚未构建"，构建后必为非 null（即便扫描结果为
        // 空字典）——用 null 而不是"空集合也算已构建"的哨兵，避免"表未注册/无数据"与"确实没扫描"
        // 两种状态混淆（同 IDataRegistryView.Get 对未知表返回 null 的既有区分手法）。
        private Dictionary<Id, int>? _playerBookLevels;
        private Dictionary<Id, int>? _monsterCreatureLevels;

        // 集成任务改动：解析 skill.proc_def.condition 用的 IExprSchema，默认改用集成任务提供的
        // core/rules/expr_host.RulesExprSchema.Base（原先本模块自带的临时占位 schema 已被取代，
        // 阶段 3 整理后已删除），保留可注入口子（构造参数 exprSchema）。
        private readonly IExprSchema _exprSchema;

        public SkillDefCache(IDataRegistryView registry, IExprSchema? exprSchema = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _exprSchema = exprSchema ?? RulesExprSchema.Base;
        }

        /// <summary>
        /// P2-05 根治：清空全部五张表的已解析缓存，下一次 <c>TryGet*</c>/<c>Get*</c> 重新从
        /// <see cref="_registry"/> 读取并解析（registry 侧本身在 <see cref="IDataRegistry.Reload"/>
        /// 之后已经是新数据，见该方法判断记录）。供 <see cref="SkillHost"/> 订阅
        /// <see cref="Core.Foundation.DataRegistry.DataLoadCompletedEvent"/> 后调用——本类型不直接
        /// 持有 <c>IEventBus</c>（构造签名保持不变，不强加事件总线依赖），由持有它的
        /// <see cref="SkillHost"/> 决定何时失效，本方法只负责"清空"这一件事，不关心触发时机。
        /// </summary>
        public void InvalidateAll()
        {
            _skills.Clear();
            _auras.Clear();
            _procs.Clear();
            _spellMods.Clear();
            _books.Clear();
            _baseCurves.Clear();
            _beatSeconds.Clear();
            _playerBookLevels = null;
            _monsterCreatureLevels = null;
        }

        /// <summary>
        /// T-N3-9（ADR-0031 决策 2；06 第 3.10 节"玩家档/怪物档由反向引用决定：只被
        /// <c>creature.template</c> 引用的技能按怪物档，出现在任一 <c>skill.book</c> 的按玩家档"）：
        /// 按 <paramref name="skillId"/> 反查它的技能预算档位与"技能等级"（06 第 3.10 节"技能等级
        /// 取技能书里的习得等级"）。
        /// <para>
        /// 判断记录（多处引用时取哪一个等级）：同一技能可能被多本 <c>skill.book</c> 以不同等级登记、
        /// 或被多个 <c>creature.template</c> 以不同等级引用——06/ADR-0031 均未规定取哪一个。本任务
        /// 临时判定：取全部命中里的最小等级（"最早可获得的等级"，对预算带宽判定更保守——同一技能
        /// 在低等级出现意味着期望缩放属性更低，若按此仍落在带宽内，高等级角色使用时只会更宽松）。
        /// </para>
        /// <para>
        /// 判断记录（玩家档优先于怪物档）：ADR-0031"后果"段"预算校验的怪物档/玩家档靠反向引用判定，
        /// 技能同时被两边引用时按玩家档"——本方法先查 <c>skill.book</c>，命中即返回，不再查
        /// <c>creature.template</c>。
        /// </para>
        /// <para>
        /// 判断记录（两处引用来源均找不到时返回 <see cref="SkillBudgetTier.Unattributed"/>，
        /// 而不是抛异常）：见 <see cref="SkillBudgetTier.Unattributed"/> 判断记录——这是一个契约
        /// 疑点（已上报待设计层确认），本方法不因此抛异常，调用方（<see cref="SkillBudgetAnalyzer"/>）
        /// 决定如何降级处理。
        /// </para>
        /// </summary>
        public bool TryResolveBudgetAttribution(Id skillId, out SkillBudgetTier tier, out int level)
        {
            EnsureBudgetAttributionIndex();

            if (_playerBookLevels!.TryGetValue(skillId, out var playerLevel))
            {
                tier = SkillBudgetTier.Player;
                level = playerLevel;
                return true;
            }

            if (_monsterCreatureLevels!.TryGetValue(skillId, out var monsterLevel))
            {
                tier = SkillBudgetTier.Monster;
                level = monsterLevel;
                return true;
            }

            tier = SkillBudgetTier.Unattributed;
            level = 1;
            return false;
        }

        private void EnsureBudgetAttributionIndex()
        {
            if (_playerBookLevels != null)
            {
                return;
            }

            var playerLevels = new Dictionary<Id, int>();
            if (_registry.Tables.Contains("skill.book"))
            {
                foreach (var record in _registry.GetAll("skill.book"))
                {
                    if (!record.TryGetArray("entries", out var entries))
                    {
                        continue;
                    }

                    for (var i = 0; i < entries.Count; i++)
                    {
                        if (entries[i] is not JsonObject entry) continue;
                        if (!entry.TryGetValue("skill_id", out var skillIdRaw) || skillIdRaw is not JsonString skillIdText) continue;
                        if (!entry.TryGetValue("level", out var levelRaw) || levelRaw is not JsonNumber levelNumber) continue;

                        var skillId = new Id(skillIdText.Value);
                        var level = (int)levelNumber.Value;
                        if (!playerLevels.TryGetValue(skillId, out var existing) || level < existing)
                        {
                            playerLevels[skillId] = level;
                        }
                    }
                }
            }

            var monsterLevels = new Dictionary<Id, int>();
            if (_registry.Tables.Contains("creature.template") && _registry.Tables.Contains("ai.rotation"))
            {
                var rotationSkillIds = new Dictionary<Id, List<Id>>();
                foreach (var rotation in _registry.GetAll("ai.rotation"))
                {
                    if (!rotation.TryGetArray("entries", out var entries))
                    {
                        continue;
                    }

                    var skillIds = new List<Id>();
                    for (var i = 0; i < entries.Count; i++)
                    {
                        if (entries[i] is not JsonObject entry) continue;
                        if (!entry.TryGetValue("skill_id", out var skillIdRaw) || skillIdRaw is not JsonString skillIdText) continue;
                        skillIds.Add(new Id(skillIdText.Value));
                    }

                    rotationSkillIds[new Id(rotation.Key)] = skillIds;
                }

                foreach (var creature in _registry.GetAll("creature.template"))
                {
                    if (!creature.TryGetId("ai_rotation_ref", out var rotationRef)) continue;
                    if (!rotationSkillIds.TryGetValue(rotationRef, out var skillIds)) continue;
                    if (!creature.TryGetInt("level", out var creatureLevelRaw)) continue;

                    var creatureLevel = (int)creatureLevelRaw;
                    foreach (var skillId in skillIds)
                    {
                        if (!monsterLevels.TryGetValue(skillId, out var existing) || creatureLevel < existing)
                        {
                            monsterLevels[skillId] = creatureLevel;
                        }
                    }
                }
            }

            _playerBookLevels = playerLevels;
            _monsterCreatureLevels = monsterLevels;
        }

        /// <summary>T-N3-2（ADR-0031 决策 1）：按 <paramref name="id"/> 取 <c>skill.base_curve</c>
        /// 的断点曲线；表未注册（宿主未 <c>RegisterSchema(SkillSchemas.BaseCurve)</c>，见该表类型
        /// 判断记录"契约疑点"，<see cref="IDataRegistryView.Get(string, Core.Foundation.Common.Id)"/>
        /// 对未知表原样返回 <c>null</c>，不抛异常）或记录不存在时返回 <c>false</c>——<c>base_curve_ref</c>
        /// 是可选字段，调用方（<see cref="EffectDispatcher.ApplyDamageOrHeal"/>）在取不到曲线时
        /// 退回 <c>base_value</c>，不中断结算。</summary>
        public bool TryGetBaseCurve(Id id, out PiecewiseCurve curve)
        {
            if (_baseCurves.TryGetValue(id, out curve!))
            {
                return true;
            }

            var record = _registry.Get("skill.base_curve", id);
            if (record == null)
            {
                curve = null!;
                return false;
            }

            curve = CurveSchema.ReadBreakpoints(record, "entries");
            _baseCurves[id] = curve;
            return true;
        }

        /// <summary>T-N3-3（ADR-0031 决策 2/10；06 第 3.2/3.10 节）：按 <paramref name="id"/> 取
        /// <c>skill.budget_rule.beat_seconds</c>（一拍常数）。表未注册（<see cref="SkillSchemas.BudgetRule"/>
        /// 未 <c>RegisterSchema</c>，同 <see cref="TryGetBaseCurve"/> 判断记录，<c>IDataRegistryView.Get</c>
        /// 对未知表原样返回 <c>null</c>）或记录不存在时返回 <c>false</c>——调用方
        /// （<see cref="EffectDispatcher"/> 的 <c>ResolveBeatSeconds</c>）在取不到时退回缺省
        /// 1.0，不中断结算。<c>beat_seconds</c> 字段本身是可选字段（缺省 1.0，见
        /// <see cref="SkillSchemas.BudgetRule"/> 字段描述）：记录存在但未声明该字段时，同样按 1.0
        /// 处理（不算作"记录不存在"，仍返回 <c>true</c>）。</summary>
        public bool TryGetBeatSeconds(Id id, out double beatSeconds)
        {
            if (_beatSeconds.TryGetValue(id, out beatSeconds))
            {
                return true;
            }

            var record = _registry.Get("skill.budget_rule", id);
            if (record == null)
            {
                beatSeconds = 0;
                return false;
            }

            beatSeconds = record.TryGetNumber("beat_seconds", out var value) ? value : 1.0;
            _beatSeconds[id] = beatSeconds;
            return true;
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

        // T-N3-4：改为实例方法（原为 static）——解析 use_condition 需要 _exprSchema（同
        // ParseProcDef.condition 惯例），静态方法拿不到实例字段。其余解析逻辑与改动前逐字节相同。
        private SkillDef ParseSkillDef(DataRecord record)
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
            var actionCost = record.TryGetNumber("action_cost", out var ac) ? ac : 0;
            // ADR-0027：缺省 false，见 SkillDef.AllowGroundTarget 判断记录。
            var allowGroundTarget = record.TryGetBool("ground_target", out var gt) && gt;

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

            // T-N3-4（ADR-0031 决策 9）：与 ParseProcDef.condition 同一惯例——use_condition 是
            // FieldKind.Expr 原始文本（T-N3-1 已登记 expr_parsable 校验，见 SkillSchemas.Def），运行期
            // 用同一份 _exprSchema 再解析一次成 ExprNode，供 CastPipeline 步骤 1.5 求值。
            ExprNode? useCondition = null;
            if (record.TryGetString("use_condition", out var useConditionText) && !string.IsNullOrEmpty(useConditionText))
            {
                useCondition = ExprParser.Parse(useConditionText, _exprSchema);
            }

            return new SkillDef(
                id, school, isPassive, range, tags, castTime, channelTime, cost,
                cooldownCategory, cooldownDuration, chargesMax, chargesRecharge,
                respectsGcd, targetShapeRef, effects, interruptFlags, actionCost, allowGroundTarget, useCondition);
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

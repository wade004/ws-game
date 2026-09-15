using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// <see cref="IProgressionHost"/> 的默认实现（见本模块 README）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性读取 <c>prog.level_curve</c>/<c>prog.xp_source</c>
    /// 建索引；之后只读，不重新查询 registry（与 localization 的 <c>L10nHost</c> 同一惯例）。
    /// <para>
    /// 判断记录（成长写入来源 id）：任务书原文"把该等级累计成长以来源 <c>prog.growth</c> 写入"，
    /// 因此全部单位、全部曲线共用同一个 <see cref="GrowthSourceId"/> 常量作为
    /// <see cref="StatModifierWriter"/>/<see cref="StatModifierRemover"/> 的 <c>sourceId</c> 参数
    /// ——区分不同单位靠 <c>unitId</c> 参数本身，属性宿主按 <c>(unitId, sourceId)</c> 二元组分组
    /// 叠加/移除，同一单位下 <c>prog.growth</c> 来源天然只有一份，不会与其它单位互相覆盖。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="RegisterUnit"/> 不隐式写入成长）：<see cref="RegisterUnit"/> 既用于
    /// "全新单位从 1 级开始"，也用于"读档/初始化到某个已知等级"；后一种场景下单位的属性值应当
    /// 由存档数据本身决定，本模块若在 <see cref="RegisterUnit"/> 内强行按曲线重算并覆盖成长
    /// 修正，会与存档数据产生冲突。因此只有真正经由 <see cref="AddXp"/> 触发的升级路径才会写入
    /// 成长，<see cref="RegisterUnit"/> 本身不触碰 <see cref="StatModifierWriter"/>/
    /// <see cref="StatModifierRemover"/>。
    /// </para>
    /// <para>
    /// 判断记录（满级丢弃分支不发 <see cref="XpGainedEvent"/>）：任务书"到 max_level 后经验不再
    /// 累积（丢弃并记诊断）"与"AddXp...→ 发 progression.xp_gained"两句并列；本模块选择"确实没有
    /// 经验被记入"时不发放事件——<c>xp_gained</c> 语义是"记入了多少经验"，丢弃分支记入量为 0，
    /// 发一个 <c>amount=0</c> 的事件对下游（如浮字提示）没有意义，改用诊断警告表达"发生了什么"。
    /// </para>
    /// <para>
    /// 判断记录（消费方反馈第 36 条，成长唯一来源）：出生等级 &gt; 1 的单位（<see
    /// cref="Core.Carriers.Creature.CreatureFactory.Spawn"/> 按 <c>creature.template.level</c>
    /// 生成）此前由 <c>CreatureFactory.ApplyStats</c> 自行重复解析 <c>prog.level_curve</c>、把
    /// "2 级到出生等级"的累计成长直接加进 <see cref="Core.Numbers.StatBlock.IStatHost.SetBase"/>
    /// 写的基础值里；随后该单位一旦经 <see cref="AddXp"/> 真实升级，<see cref="ApplyGrowth"/> 又会
    /// 按"2 级到新等级"整段重算并整体覆盖写入修正——"2 级到出生等级"这一段因此被基础值与修正
    /// 各计了一次（真实探针：出生等级 2、出生 strength 7，升到 3 级实测 11，应为 9）。根治为
    /// "成长统一只由修正承载"：<c>CreatureFactory.ApplyStats</c> 只再写
    /// <c>base_stats × tier.stat_multiplier</c>，出生等级 &gt; 1 时改为紧随 <see
    /// cref="RegisterUnit"/> 显式调用 <see cref="ApplyGrowthToCurrentLevel"/>——与升级、读档共用
    /// 同一份 <see cref="ApplyGrowth"/> 聚合实现，成长量因此只有"当前等级对应的整段修正"这一份，
    /// 不会再与基础值里的另一份重复。
    /// </para>
    /// </summary>
    public sealed class ProgressionHost : IProgressionHost
    {
        private static readonly Id GrowthSourceId = new Id("prog.growth");

        private sealed class LevelEntry
        {
            public long XpToNext;
            public IReadOnlyList<KeyValuePair<string, double>> Growth = Array.Empty<KeyValuePair<string, double>>();
        }

        private sealed class CurveInfo
        {
            public Id Id;
            public int MaxLevel;
            public List<LevelEntry> Entries = new List<LevelEntry>();
        }

        private sealed class XpSourceInfo
        {
            public long BaseXp;
            public double Weight;

            // T-N4-2（ADR-0033 决策 3；06 第 2.5 节）：三个新增可选字段的运行期投影——Kind/
            // BaseCurveRefId/LevelDiffRefId 均为 null 表示该来源未登记对应字段（旧数据、或
            // kind 未填），null 语义分别见 ComputeCurveBasedRawAmount/EvaluateDeltaFactor
            // 判断记录。
            public string? Kind;
            public string? BaseCurveRefId;
            public string? LevelDiffRefId;
        }

        private sealed class UnitState
        {
            public CurveInfo Curve = null!;
            public int Level;
            public long Xp;
        }

        private readonly IEventBus _bus;
        private readonly StatModifierWriter _statModifierWriter;
        private readonly StatModifierRemover _statModifierRemover;
        private readonly IProgressionDiagnostics _diagnostics;

        /// <summary>CORE-170-02 根治：见 <see cref="LevelSync"/> 判断记录——可选（未注入时 <c>null</c>，
        /// 行为与本次改动之前完全一致，多数测试用的最小假实现不需要提供），非 null 时在
        /// <see cref="RegisterUnit"/>/<see cref="AddXp"/>/<see cref="RestoreState"/> 三个等级会变化/
        /// 确立的时机末尾统一调用，同步外部实体等级字段。</summary>
        private readonly LevelSync? _levelSync;

        private readonly Dictionary<string, CurveInfo> _curves = new Dictionary<string, CurveInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, XpSourceInfo> _xpSources = new Dictionary<string, XpSourceInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, UnitState> _units = new Dictionary<string, UnitState>(StringComparer.Ordinal);

        // T-N4-2（ADR-0033 决策 2/3）：构造期口味配置，见 ProgressionOptions.MaxLevel"变更记录"——
        // 旧构造函数（不带 ProgressionOptions 参数）转发 null，本字段落到全字段缺省的实例，效果
        // 与本次改动之前完全一致。
        private readonly ProgressionOptions _options;

        // T-N4-2（ADR-0033 决策 3；04 第 3.6 节通用断点表形态）：prog.xp_base_curve 的运行期投影，
        // 键为曲线 id。见 GetEffectiveMaxLevel 判断记录（层次一致：只读一次、之后只读不查 registry，
        // 与 _curves/_xpSources 同一惯例）。
        private readonly Dictionary<string, PiecewiseCurve> _xpBaseCurves = new Dictionary<string, PiecewiseCurve>(StringComparer.Ordinal);

        // T-N4-2（ADR-0033 决策 3/5）：combat.level_diff_table 的 xp_factor 列的运行期投影，键为
        // 该表记录 id。判断记录（不引用 Core.Rules.Combat.LevelDiffTable 类型）：01_分层与依赖.md
        // 把 progression 登记为 L1、combat 登记为 L2（规则层），L1 反向依赖 L2 具体类型会倒置分层
        // ——本类型只经 Core.Foundation.DataRegistry 的通用 CurveSchema.ReadBreakpoints 直接从原始
        // DataRecord 读取 xp_factor 断点表，不经过 Core.Rules.Combat 程序集（该程序集的
        // LevelDiffTable 类型另有一套供 combat 模块自己使用的强类型视图，读的是同一张表、同一个
        // xp_factor 字段，两边各自独立解析，互不依赖；见 Tests.Numbers.csproj 不引用 Core.Rules 的
        // 既有约束，ProgSchemaCoverageTests.cs"combat.level_diff_table 归 Core.Rules"判断记录）。
        private readonly Dictionary<string, PiecewiseCurve> _levelDiffXpFactors = new Dictionary<string, PiecewiseCurve>(StringComparer.Ordinal);

        public ProgressionHost(
            IDataRegistryView registry,
            IEventBus bus,
            StatModifierWriter statModifierWriter,
            StatModifierRemover statModifierRemover,
            IProgressionDiagnostics? diagnostics = null,
            LevelSync? levelSync = null)
            : this(registry, bus, statModifierWriter, statModifierRemover, null, diagnostics, levelSync)
        {
        }

        /// <summary>
        /// T-N4-2 新增构造重载：接受 <see cref="ProgressionOptions"/>（ADR-0033 决策 2；06 第 2.5 节
        /// "策略配置项：经验来源权重、最大等级"）。<paramref name="options"/> 为 <c>null</c> 时等价于
        /// 传入全字段缺省的实例（<c>MaxLevel=0</c>），与不带本参数的旧构造函数完全同义——旧构造函数
        /// 内部即转发 <c>null</c> 到本构造函数，两者共用同一份初始化逻辑，不是"新增一条平行的初始化
        /// 路径"（ABI 门禁"公开 API 只能新增"：本次改动只新增这一个构造重载，未带 <see
        /// cref="ProgressionOptions"/> 的旧构造函数签名原样保留，见 <c>RulesAssembly</c>/各测试夹具
        /// 既有调用点核对，均只用位置参数或按类型消歧的具名参数，不会与本新重载产生重载决议歧义）。
        /// </summary>
        public ProgressionHost(
            IDataRegistryView registry,
            IEventBus bus,
            StatModifierWriter statModifierWriter,
            StatModifierRemover statModifierRemover,
            ProgressionOptions? options,
            IProgressionDiagnostics? diagnostics = null,
            LevelSync? levelSync = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _statModifierWriter = statModifierWriter ?? throw new ArgumentNullException(nameof(statModifierWriter));
            _statModifierRemover = statModifierRemover ?? throw new ArgumentNullException(nameof(statModifierRemover));
            _diagnostics = diagnostics ?? new InMemoryProgressionDiagnostics();
            _levelSync = levelSync;
            _options = options ?? new ProgressionOptions();

            foreach (var record in registry.GetAll("prog.level_curve"))
            {
                var curve = ParseCurve(record);
                _curves[curve.Id.Value] = curve;
            }

            foreach (var record in registry.GetAll("prog.xp_source"))
            {
                var id = record.GetId("id");
                var baseXp = record.GetInt("base_xp");
                var weight = record.TryGetNumber("weight", out var w) ? w : 1.0;
                var kind = record.TryGetString("kind", out var k) ? k : null;
                var baseCurveRefId = record.TryGetId("base_curve_ref", out var bcr) ? bcr.Value : null;
                var levelDiffRefId = record.TryGetId("level_diff_ref", out var ldr) ? ldr.Value : null;
                _xpSources[id.Value] = new XpSourceInfo
                {
                    BaseXp = baseXp,
                    Weight = weight,
                    Kind = kind,
                    BaseCurveRefId = baseCurveRefId,
                    LevelDiffRefId = levelDiffRefId,
                };
            }

            // T-N4-2（ADR-0033 决策 3）：prog.xp_base_curve 消费——本表由 progression 模块自己
            // 拥有（RulesSchemaCatalog 无条件注册其 schema，见 ProgSchemas.XpBaseCurve 判断记录），
            // 但数据源里没有对应文件时 registry.Tables 不会列出它（见 HasTable 判断记录）；用与
            // combat.level_diff_table 同一条"可选表"判断，未接数据时留空字典，不抛异常——多数
            // 只测曲线/满级逻辑、不测当量折算的既有测试夹具（未注册 ProgSchemas.XpBaseCurve、
            // 未提供该表数据）应继续正常构造。
            if (HasTable(registry, "prog.xp_base_curve"))
            {
                foreach (var record in registry.GetAll("prog.xp_base_curve"))
                {
                    var id = record.GetId("id");
                    _xpBaseCurves[id.Value] = CurveSchema.ReadBreakpoints(record, "entries");
                }
            }

            // T-N4-2（ADR-0033 决策 3/5）：combat.level_diff_table 的 xp_factor 列——可选表，判断
            // 记录同上（不引用 Core.Rules.Combat 具体类型）；未接数据（单机/测试夹具不装配 combat
            // 模块，或该模块存在但未配置 CombatOptions.LevelDiffTableId）时留空字典，
            // EvaluateDeltaFactor 据此退化为"等级差系数恒为 1"。
            if (HasTable(registry, "combat.level_diff_table"))
            {
                foreach (var record in registry.GetAll("combat.level_diff_table"))
                {
                    var id = record.GetId("id");
                    _levelDiffXpFactors[id.Value] = CurveSchema.ReadBreakpoints(record, "xp_factor");
                }
            }
        }

        /// <summary>判断记录：与 <c>Core.Rules.Combat.CombatDataLoader.HasTable</c> 同一惯例（该
        /// 类型 <c>internal</c>，不能跨程序集复用，见本类型字段判断记录"不引用 Core.Rules.Combat
        /// 具体类型"）——<see cref="IDataRegistryView.Tables"/> 只反映"实际从数据源加载了记录的
        /// 表"，与"是否曾经 RegisterSchema"是两回事，这正是"该表本次是否真的接了数据"这一语义。</summary>
        private static bool HasTable(IDataRegistryView registry, string table)
        {
            var tables = registry.Tables;
            for (var i = 0; i < tables.Count; i++)
            {
                if (tables[i] == table)
                {
                    return true;
                }
            }

            return false;
        }

        // -----------------------------------------------------------------
        // 加载期解析：不信任上游一定跑过 ProgLevelCurveValidationRule，
        // 这里做一次防御性重复校验（与 L10nHost 的 ValidateNoFallbackCycle 同一惯例）。
        // -----------------------------------------------------------------
        private static CurveInfo ParseCurve(DataRecord record)
        {
            var id = record.GetId("id");
            var maxLevel = (int)record.GetInt("max_level");
            var entriesJson = record.GetArray("entries");

            if (entriesJson.Count != maxLevel)
            {
                throw new ArgumentException(
                    $"曲线 \"{id}\" 的 entries 数量（{entriesJson.Count}）与 max_level（{maxLevel}）不一致" +
                    "（应已被 ProgLevelCurveValidationRule 拦截，见该规则的判断记录）");
            }

            var entries = new List<LevelEntry>(entriesJson.Count);
            for (var i = 0; i < entriesJson.Count; i++)
            {
                var expectedLevel = i + 1;
                if (!(entriesJson[i] is JsonObject entryObj))
                {
                    throw new ArgumentException($"曲线 \"{id}\" entries[{i}] 不是对象");
                }

                var level = (int)RequireInt(entryObj, "level", id, i);
                if (level != expectedLevel)
                {
                    throw new ArgumentException(
                        $"曲线 \"{id}\" entries[{i}] 的 level 应为 {expectedLevel}，实际 {level}" +
                        "（应已被 ProgLevelCurveValidationRule 拦截）");
                }

                var xpToNext = RequireInt(entryObj, "xp_to_next", id, i);

                var growth = new List<KeyValuePair<string, double>>();
                if (entryObj.TryGetValue("growth", out var growthValue) && growthValue is JsonObject growthObj)
                {
                    foreach (var kv in growthObj)
                    {
                        if (kv.Value is JsonNumber num)
                        {
                            growth.Add(new KeyValuePair<string, double>(kv.Key, num.Value));
                        }
                    }
                }

                entries.Add(new LevelEntry { XpToNext = xpToNext, Growth = growth });
            }

            return new CurveInfo { Id = id, MaxLevel = maxLevel, Entries = entries };
        }

        private static long RequireInt(JsonObject entryObj, string field, Id curveId, int index)
        {
            if (entryObj.TryGetValue(field, out var v) && v is JsonNumber n && n.TryGetInt64(out var value))
            {
                return value;
            }
            throw new ArgumentException($"曲线 \"{curveId}\" entries[{index}].{field} 缺失或不是整数");
        }

        // -----------------------------------------------------------------
        // IProgressionHost
        // -----------------------------------------------------------------

        public void RegisterUnit(Id unitId, Id curveId, int startLevel = 1)
        {
            var curve = GetCurveOrThrow(curveId);
            if (startLevel < 1 || startLevel > curve.MaxLevel)
            {
                throw new ArgumentException(
                    $"起始等级 {startLevel} 超出曲线 \"{curveId}\" 的范围 [1,{curve.MaxLevel}]", nameof(startLevel));
            }

            _units[unitId.Value] = new UnitState { Curve = curve, Level = startLevel, Xp = 0 };

            // CORE-170-02 根治：首次注册即确立本单位的权威等级，同步给外部实体字段——见
            // LevelSync 判断记录"生产装配构造 PlayerUnit 时从未写入 PlayerUnit.Level，只把等级传给
            // RulesAssembly.RegisterUnit"这一真实探针复现的根因，本行正是补上这一步同步。
            _levelSync?.Invoke(unitId, startLevel);
        }

        public int GetLevel(Id unitId) => GetUnitOrThrow(unitId).Level;

        public long GetXp(Id unitId) => GetUnitOrThrow(unitId).Xp;

        public long GetXpToNext(Id unitId)
        {
            var unit = GetUnitOrThrow(unitId);
            var effectiveMaxLevel = GetEffectiveMaxLevel(unit.Curve);
            if (unit.Level >= effectiveMaxLevel)
            {
                // T-N4-2（ADR-0033 决策 9"满级后……getXpToNext 返回零"）：显式按"有效满级"短路
                // 返回 0，不再依赖"曲线最后一条记录的 xp_to_next 恰好是 0"这条数据约定——
                // ProgressionOptions.MaxLevel 收紧的有效满级可能落在曲线中间某一级，那一级的
                // xp_to_next 通常不是 0（它对"未被收紧"的单位仍然有意义），仍必须返回 0。
                return 0;
            }
            return unit.Curve.Entries[unit.Level - 1].XpToNext;
        }

        /// <summary>T-N4-2：单位绑定曲线的"有效满级"——<see cref="ProgressionOptions.MaxLevel"/>
        /// 为 0（缺省）时等于曲线自身 <see cref="CurveInfo.MaxLevel"/>；为正值时取二者较小值（只能
        /// 收紧、不能放宽到超出曲线数据本身，见 <see cref="ProgressionOptions.MaxLevel"/> 判断
        /// 记录）。<see cref="GetXpToNext"/>/<see cref="AddXpCore"/>/<see cref="GrantXpCore"/> 共用
        /// 本方法判定"是否已满级"，确保三者口径一致。</summary>
        private int GetEffectiveMaxLevel(CurveInfo curve) =>
            _options.MaxLevel > 0 ? Math.Min(_options.MaxLevel, curve.MaxLevel) : curve.MaxLevel;

        public void AddXp(Id unitId, Id sourceId, long amount) => AddXpCore(unitId, sourceId, amount);

        /// <summary>T-N4-2：从 <see cref="AddXp"/> 拆出的核心实现，返回"实际入账值"（已满级时为 0，
        /// 否则等于 <paramref name="amount"/>）——<see cref="AddXp"/> 保持既有 <c>void</c> 签名不变
        /// （ABI 门禁不允许修改既有接口成员签名），<see cref="GrantXp"/>/<see cref="GrantFromSource"/>
        /// 改内部直接调用本方法拿到返回值（设计层裁定"内部经同一条'应用倍率 → 加经验 → 升级循环 →
        /// 事件'路径"，本方法就是那条共用路径，三个公开入口只负责算出各自的 <paramref name="amount"/>
        /// 后转发到这里，不各自重复一份满级判定/事件发布/升级循环）。</summary>
        private long AddXpCore(Id unitId, Id sourceId, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "经验增量不能为负");

            var unit = GetUnitOrThrow(unitId);
            var effectiveMaxLevel = GetEffectiveMaxLevel(unit.Curve);

            if (unit.Level >= effectiveMaxLevel)
            {
                _diagnostics.Warn(
                    $"单位 \"{unitId}\" 已达曲线 \"{unit.Curve.Id}\" 满级（{effectiveMaxLevel}），" +
                    $"丢弃经验 {amount}（来源 \"{sourceId}\"）");
                return 0;
            }

            _bus.PublishImmediate(new XpGainedEvent(unitId, sourceId, amount));
            unit.Xp += amount;

            var leveledUp = false;
            while (unit.Level < effectiveMaxLevel)
            {
                var xpToNext = unit.Curve.Entries[unit.Level - 1].XpToNext;
                if (xpToNext <= 0 || unit.Xp < xpToNext)
                {
                    break;
                }

                unit.Xp -= xpToNext;
                var oldLevel = unit.Level;
                var newLevel = oldLevel + 1;
                // N08 收边补齐（外部审计 68c9bed，P2）：先提交等级、再发布 progression.level_up——
                // PublishImmediate 是同步派发，修复前 unit.Level 在发布时仍是旧值，事件处理器内如果
                // 反查 GetLevel(unitId)（而不是只读事件自带的 NewLevel 字段）会读到旧等级，
                // RulesAssembly 一类按等级重算派生属性的消费者可能因此用旧等级计算，直到下一次别的
                // 属性写入才被动补救（见外部审计 N08）。事件本身携带的 OldLevel/NewLevel 字段值不变，
                // 只调整"字段赋值"与"发布事件"两个语句的先后顺序。
                unit.Level = newLevel;
                _bus.PublishImmediate(new LevelUpEvent(unitId, oldLevel, newLevel));
                leveledUp = true;
            }

            if (unit.Level >= effectiveMaxLevel && unit.Xp > 0)
            {
                _diagnostics.Warn(
                    $"单位 \"{unitId}\" 升至曲线 \"{unit.Curve.Id}\" 满级（{effectiveMaxLevel}）过程中，" +
                    $"残余经验 {unit.Xp} 被丢弃");
                unit.Xp = 0;
            }

            if (leveledUp)
            {
                ApplyGrowth(unitId, unit);
                // CORE-170-02 根治：升级后同步外部实体字段——LevelUpEvent 是 PublishImmediate 同步
                // 派发，本行放在事件已经发布之后，与 unit.Level 赋值先于事件发布（见上方 N08 收边
                // 补齐同一惯例）不矛盾：LevelSync 的消费者（WorldUnitAccess.SetLevel）只是把最终
                // 等级写回实体字段，不关心中途经历了几次 LevelUpEvent，写一次最终值即可，不需要在
                // while 循环内逐级调用。
                _levelSync?.Invoke(unitId, unit.Level);
            }

            return amount;
        }

        // -----------------------------------------------------------------
        // 存档（W1 收边补齐：10 第 2.2 节 player.progression 字段，见 core/numbers/progression/
        // core/ProgressionPersistable.cs 判断记录——本模块不直接实现 IPersistable，理由与
        // core/carriers/unit/core/UnitPersistable.cs 同款静态工厂模式一致：本模块可能同时管理
        // 多个单位（NPC 也可注册 Progression），但存档只关心"哪个单位是玩家"，这一决定权在调用方
        // （游戏层引导代码知道谁是玩家），本模块自己不应该假设"唯一一个已注册单位就是玩家"。
        // -----------------------------------------------------------------

        /// <summary>序列化 <paramref name="unitId"/> 当前的 Progression 状态（<c>curve_id</c>/
        /// <c>level</c>/<c>xp</c>）。<paramref name="unitId"/> 必须已经过 <see cref="RegisterUnit"/>
        /// 注册，否则抛 <see cref="ArgumentException"/>（与其它公开方法一致的前置校验，见
        /// <see cref="GetUnitOrThrow"/>）。</summary>
        public JsonValue SaveUnit(Id unitId)
        {
            var unit = GetUnitOrThrow(unitId);
            return new JsonObjectBuilder()
                .Add("curve_id", new JsonString(unit.Curve.Id.Value))
                .Add("level", new JsonNumber(unit.Level))
                // xp 是 long（见字段声明），与 CurrencyPersistable 同款判断记录：用 FromInt64 保留精确
                // 原始文本，避免超过 2^53 的 xp 值经 double 中转丢精度；level 是 int（远小于 2^53），
                // 不受影响，维持原写法。
                .Add("xp", JsonNumber.FromInt64(unit.Xp))
                .Build();
        }

        /// <summary>
        /// 读档专用状态恢复入口——与 <see cref="RegisterUnit"/> 的区别是允许指定非零
        /// <paramref name="xp"/>（<see cref="RegisterUnit"/> 恒 <c>xp=0</c>，语义是"全新单位从
        /// 起始等级开始"，不适合读档场景）。恢复状态后按 <see cref="ApplyGrowth"/> 同一逻辑重新
        /// 聚合 1..<paramref name="level"/> 的全部成长修正——见 10 第 2.5 节"属性快照默认不存……
        /// 存基础来源（装备、已知天赋等）后可在读档时重新聚合"，成长修正是该原则里的"基础来源"
        /// 之一，本模块负责在读档时重建它（<see cref="StatModifierWriter"/> 写入的修正不参与
        /// 存档，读档后必须由持有方重新写入）。<b>调用方必须确保 <paramref name="unitId"/> 已在
        /// 属性宿主（StatHost）完成注册</b>——本方法与 <see cref="ApplyGrowth"/> 一样直接调用
        /// <see cref="StatModifierWriter"/>/<see cref="StatModifierRemover"/>，未注册的单位会被
        /// 属性宿主自身的前置校验拒绝（本模块不重复做这层校验，职责边界见类型注释"不依赖具体属性
        /// 宿主实现"）。
        /// </summary>
        /// <summary>
        /// R08 收边补齐（外部审计 5e779c6，P2）：结尾发布 <see cref="ProgressionRestoredEvent"/>——
        /// 恢复等级不经过 <see cref="AddXp"/> 的正常升级路径，不会发布 <see cref="LevelUpEvent"/>
        /// （见该类型判断记录"为什么不复用 LevelUpEvent"），依赖等级的下游缓存
        /// （<c>Core.Rules.Assembly.RulesAssembly</c> 订阅本事件调用
        /// <c>StatHost.RecomputeRatingStats</c>）此前没有任何通知路径能感知"读档换了等级"，评级
        /// 换算属性的缓存会一直停留在读档前（通常是刚构造、默认等级 1）算出的值，直到该单位下一次
        /// 因为其它原因（装备变化等）真正触发 stat.changed／恰好再打一次怪连锁到
        /// progression.level_up 才被动修正（外部审计复现：读档恢复高等级后，评级换算属性仍按等级 1
        /// 的换算系数计算，直到偶然被其它事件带动重算）。
        /// </summary>
        public void RestoreState(Id unitId, Id curveId, int level, long xp)
        {
            var curve = GetCurveOrThrow(curveId);
            if (level < 1 || level > curve.MaxLevel)
            {
                throw new ArgumentException(
                    $"读档等级 {level} 超出曲线 \"{curveId}\" 的范围 [1,{curve.MaxLevel}]", nameof(level));
            }

            var unit = new UnitState { Curve = curve, Level = level, Xp = xp };
            _units[unitId.Value] = unit;
            ApplyGrowth(unitId, unit);
            _bus.PublishImmediate(new ProgressionRestoredEvent(unitId, level));
            // CORE-170-02 根治：读档恢复同样是一条等级会变化/确立的路径，同步外部实体字段——理由
            // 与 RegisterUnit/AddXp 完全一致，见 LevelSync 判断记录。
            _levelSync?.Invoke(unitId, level);
        }

        /// <summary>消费方反馈第 36 条根治：见 <see cref="IProgressionHost.ApplyGrowthToCurrentLevel"/>
        /// 契约注释——直接复用 <see cref="ApplyGrowth"/> 这同一段内部聚合逻辑，不新写公式，也不发
        /// 任何事件。</summary>
        public void ApplyGrowthToCurrentLevel(Id unitId)
        {
            var unit = GetUnitOrThrow(unitId);
            ApplyGrowth(unitId, unit);
        }

        public void GrantFromSource(Id unitId, Id xpSourceId, double multiplier = 1) =>
            GrantFromSourceCore(unitId, xpSourceId, multiplier);

        /// <summary>T-N4-2：<see cref="GrantFromSource"/> 的核心实现，返回实际入账值（<see
        /// cref="GrantFromSource"/> 保持既有 <c>void</c> 签名不变）。</summary>
        private long GrantFromSourceCore(Id unitId, Id xpSourceId, double multiplier)
        {
            var source = GetXpSourceOrThrow(xpSourceId);

            double raw;
            if (string.IsNullOrEmpty(source.BaseCurveRefId))
            {
                // 旧算法逐位保留（拍板 4"base_xp/weight 保留一个版本周期"；见 IProgressionHost.
                // GrantFromSource 判断记录"分两种算法"）：T-N4-2 之前本方法唯一的实现分支，
                // 数值/舍入方式与之前逐位相同。
                raw = source.BaseXp * source.Weight * multiplier;
            }
            else
            {
                // 判断记录（契约疑点上报，见 IProgressionHost.GrantFromSource 判断记录）：旧签名
                // 没有"来源等级"参数，按"该来源与领取者当前等级相同"处理（Δ 恒从 0 起算），
                // multiplier 直接相乘、不经 ExtraXpMultiplierProvider 钩子（钩子是 GrantXp 新路径
                // 的默认倍率来源，旧调用方已经在用自己的 multiplier 表达倍率，两者不应该叠加）。
                var receiverLevel = GetUnitOrThrow(unitId).Level;
                var context = new XpContext(sourceLevel: receiverLevel);
                raw = ComputeCurveBasedRawAmount(unitId, xpSourceId, source, context) * multiplier;
            }

            return AddXpCore(unitId, xpSourceId, RoundXp(raw));
        }

        public long GrantXp(Id unitId, Id sourceId, XpContext context) => GrantXpCore(unitId, sourceId, context);

        /// <summary>T-N4-2：<see cref="GrantXp"/> 的核心实现（见 IProgressionHost.GrantXp 判断记录
        /// 的公式小节）。</summary>
        private long GrantXpCore(Id unitId, Id sourceId, XpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var source = GetXpSourceOrThrow(sourceId);

            // 判断记录：GrantXp 对没有 base_curve_ref 的来源按"隐式 multiplier=1 的
            // GrantFromSource"处理（与该方法共用同一条旧算法，不重复一份），context 此时未被
            // 使用——旧式来源没有"按怪物/任务/区域等级折算"的概念，这是 GrantXp 的新增能力。
            var raw = string.IsNullOrEmpty(source.BaseCurveRefId)
                ? source.BaseXp * source.Weight
                : ComputeCurveBasedRawAmount(unitId, sourceId, source, context);

            return AddXpCore(unitId, sourceId, RoundXp(raw));
        }

        /// <summary>T-N4-2（ADR-0033 决策 3；06 第 2.5 节）：三种来源当量折算公式的唯一实现，
        /// <see cref="GrantXpCore"/> 与 <see cref="GrantFromSourceCore"/>（曲线分支）共用——
        /// <paramref name="source"/><c>.BaseCurveRefId</c> 必须非空（调用方职责，两个调用点均已
        /// 在调用前判断）。返回未舍入的原始值，舍入统一在 <see cref="RoundXp"/> 做（避免"曲线结果
        /// 先舍入一次、乘 multiplier 再舍入一次"的二次舍入误差）。</summary>
        private double ComputeCurveBasedRawAmount(Id unitId, Id sourceId, XpSourceInfo source, XpContext context)
        {
            if (!_xpBaseCurves.TryGetValue(source.BaseCurveRefId!, out var baseCurve))
            {
                // reference_integrity 校验应已在加载期拦下悬空引用（同 ADR-0033 决策 3 的
                // base_curve_ref 字段登记）；本分支是运行期防御性兜底，同 GetCurveOrThrow 惯例。
                throw new ArgumentException(
                    $"经验来源 \"{sourceId}\" 的 base_curve_ref \"{source.BaseCurveRefId}\" 未在 " +
                    "\"prog.xp_base_curve\" 表中找到（应已被 reference_integrity 校验拦截）",
                    nameof(source));
            }

            var baseAmount = baseCurve.Evaluate(context.SourceLevel);

            // 判断记录（kind 缺省按 kill 处理）：prog.xp_source.kind 登记为可选（T-N4-1，兼容旧
            // 数据），存在 base_curve_ref 却未填 kind 的来源没有字面契约结论——kill 分支公式最
            // 贴近"基数曲线本身就是一只同级怪的经验"这一最基础语义，取为兜底，非 kill/quest/
            // discovery 之外的任何字符串同样落到这条兜底（不应发生，schema 层已用 enum 约束，
            // 这里只是不让防御性代码抛出意外分支异常）。
            switch (source.Kind)
            {
                case "quest":
                    return (context.Equivalent ?? 1.0) * baseAmount * EvaluateDeltaFactor(unitId, source, context.SourceLevel);

                case "discovery":
                    // ADR-0033 决策 3"探索 = 一只怪当量 × 击杀基数(区域等级)"——原文公式没有等级差
                    // 项，即便该来源同时登记了 level_diff_ref 也不查（不调用 EvaluateDeltaFactor）。
                    return (context.Equivalent ?? 1.0) * baseAmount;

                case "kill":
                default:
                    // T-N4-4：ExtraXpMultiplierProvider 委托签名扩展新增 tierId 参数（见该委托
                    // 判断记录"T-N4-4 变更记录"）——context.TierId 在本分支调用时点已经就位
                    // （由 CreatureDeathXpListener 经 XpContext 传入），原样转发。
                    var multiplier = _options.ExtraXpMultiplierProvider?.Invoke(unitId, sourceId, context.TierId) ?? 1.0;
                    return baseAmount * multiplier * EvaluateDeltaFactor(unitId, source, context.SourceLevel);
            }
        }

        /// <summary>T-N4-2（ADR-0033 决策 3/5）：等级差系数——<paramref name="source"/> 未登记
        /// <c>level_diff_ref</c>，或登记了但对应记录未加载（目标表整体缺失，见构造函数
        /// <c>combat.level_diff_table</c> 判断记录）时退化为 1（不折算），不抛异常——单机/测试
        /// 夹具不装配 combat 模块时经验数值仍应可算。Δ = <paramref name="sourceLevel"/>（来源侧：
        /// 怪物/任务等级）− 领取者 <paramref name="unitId"/> 当前有效等级（<c>combat.level_diff_
        /// table</c> 字段登记原文"Δ = 目标/来源有效等级 − 攻击者/领取者有效等级"）。</summary>
        private double EvaluateDeltaFactor(Id unitId, XpSourceInfo source, int sourceLevel)
        {
            if (string.IsNullOrEmpty(source.LevelDiffRefId)) return 1.0;
            if (!_levelDiffXpFactors.TryGetValue(source.LevelDiffRefId!, out var xpFactorCurve)) return 1.0;

            var receiverLevel = GetUnitOrThrow(unitId).Level;
            var delta = sourceLevel - receiverLevel;
            return xpFactorCurve.Evaluate(delta);
        }

        /// <summary>四舍五入到 <see cref="long"/>（<see cref="MidpointRounding.AwayFromZero"/>），
        /// 负值钳为 0——与本方法引入之前 <see cref="GrantFromSource"/> 的舍入方式逐位一致。</summary>
        private static long RoundXp(double raw) => raw <= 0 ? 0L : (long)Math.Round(raw, MidpointRounding.AwayFromZero);

        private XpSourceInfo GetXpSourceOrThrow(Id xpSourceId)
        {
            if (!_xpSources.TryGetValue(xpSourceId.Value, out var source))
            {
                throw new ArgumentException($"未知经验来源 \"{xpSourceId}\"", nameof(xpSourceId));
            }
            return source;
        }

        /// <summary>T-N4-4 附带任务：显式转发（见 <see cref="IProgressionHost.HasXpSource"/> 判断
        /// 记录——本类型不落回默认值 <c>true</c>，直接按 <c>prog.xp_source</c> 构造期解析出的索引
        /// 精确判断）。</summary>
        public bool HasXpSource(Id sourceId) => _xpSources.ContainsKey(sourceId.Value);

        // -----------------------------------------------------------------
        // 内部
        // -----------------------------------------------------------------

        private void ApplyGrowth(Id unitId, UnitState unit)
        {
            var order = new List<string>();
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);

            for (var level = 2; level <= unit.Level; level++)
            {
                foreach (var kv in unit.Curve.Entries[level - 1].Growth)
                {
                    if (!totals.ContainsKey(kv.Key))
                    {
                        order.Add(kv.Key);
                        totals[kv.Key] = 0;
                    }
                    totals[kv.Key] += kv.Value;
                }
            }

            _statModifierRemover(unitId, GrowthSourceId);
            foreach (var statKey in order)
            {
                _statModifierWriter(unitId, new Id(statKey), "flat", totals[statKey], GrowthSourceId);
            }
        }

        private CurveInfo GetCurveOrThrow(Id curveId)
        {
            if (!_curves.TryGetValue(curveId.Value, out var curve))
            {
                throw new ArgumentException($"未知等级曲线 \"{curveId}\"", nameof(curveId));
            }
            return curve;
        }

        private UnitState GetUnitOrThrow(Id unitId)
        {
            if (!_units.TryGetValue(unitId.Value, out var unit))
            {
                throw new ArgumentException($"单位 \"{unitId}\" 未通过 RegisterUnit 注册", nameof(unitId));
            }
            return unit;
        }
    }
}

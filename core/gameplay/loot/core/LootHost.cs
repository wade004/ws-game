using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// <see cref="ILootHost"/> 唯一实现（见 08 第 9 节 Loot 行），同时实现 <see cref="ILootRoller"/>
    /// （L3 依赖倒置接口，见其类型注释：由 L4 实现、组装期注入给 <c>core/carriers/gobj</c>/
    /// <c>core/carriers/creature</c> 一类需要产出掉落的 L3 宿主）。
    /// <para>
    /// 判断记录 1——掉落判定的确定性：唯一随机源是 <see cref="LootOptions.RngStream"/> 指定的
    /// <c>IRngHost</c> 流（默认 <c>"loot.roll"</c>），按分组/条目的登记顺序依次消耗（<c>chance_each</c>
    /// 每条一次 <c>Next</c> + 命中时一次 <c>NextInt</c>；<c>weighted_pick_one</c> 每次抽取一次
    /// <c>Next</c> + 命中后一次 <c>NextInt</c>），保证同一 <c>IRngHost</c> 内部状态下两次独立调用产生
    /// 完全相同的结果序列（落地方案与分阶段计划.md 第 13 节验收标准 2）。
    /// </para>
    /// <para>
    /// 判断记录 2——嵌套 <c>loot.*</c> 引用的 <c>count</c> 语义：08 第 1.1 节 <c>LootEntry</c> 的
    /// <c>countRange</c> 字段统一适用于 <c>ref</c> 是 <c>item.*</c> 还是 <c>loot.*</c> 两种情况，未
    /// 说明后者该如何解释"数量"。本模块拍板：<c>ref</c> 为嵌套表时，<c>count</c>（在
    /// <c>[countMin,countMax]</c> 内抽出的具体值）表示"把该嵌套表整体再抽取 count 次"，每次独立走
    /// 一遍该嵌套表自己的分组/保底逻辑，产出的物品堆叠全部并入外层结果——不是"该嵌套表的第一次结果
    /// 重复 count 份"。
    /// </para>
    /// <para>
    /// 判断记录 3——伪随机（<see cref="LootOptions.PseudoRandom"/>）与保底（<c>guaranteed_min</c>）的
    /// 计数不共享：伪随机的"未掉落次数"按 <c>(ContextId, tableId, groupIndex, entryIndex)</c> 累积在
    /// 本实例内存中（不持久化，见 README"不负责什么"），保底计数是每次 <see cref="Roll"/> 调用内部
    /// 局部的"本次已产出条目数"，二者是两套独立机制，互不干扰——伪随机影响的是"是否命中"，保底影响
    /// 的是"命中数不够时额外补抽"。
    /// </para>
    /// </summary>
    public sealed class LootHost : ILootHost, ILootRoller
    {
        private readonly IRngHost _rng;
        private readonly IEventBus _bus;
        private readonly IWorldSim _world;
        private readonly IUnitAccess _units;
        private readonly IInventoryHost _inventory;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly Func<double> _simTimeProvider;
        private readonly LootOptions _options;
        private readonly IExprDiagnostics _diagnostics;

        private readonly IDataRegistryView _registry;
        private readonly IExprSchema? _conditionSchema;

        private readonly Dictionary<Id, LootTableDef> _tables = new Dictionary<Id, LootTableDef>();
        private readonly Dictionary<Id, DroppedLootEntity> _dropped = new Dictionary<Id, DroppedLootEntity>();
        private readonly List<Id> _order = new List<Id>();

        /// <summary>伪随机"连续未中次数"，key 见判断记录 3；只在 <see cref="LootOptions.PseudoRandom"/>
        /// 启用时读写。</summary>
        private readonly Dictionary<string, int> _missStreaks = new Dictionary<string, int>(StringComparer.Ordinal);

        public LootHost(
            IDataRegistryView registry,
            IRngHost rng,
            IEventBus bus,
            IWorldSim world,
            IUnitAccess units,
            IInventoryHost inventory,
            IExprHostFactory exprHostFactory,
            Func<double> simTimeProvider,
            LootOptions? options = null,
            IExprDiagnostics? diagnostics = null,
            IExprSchema? conditionSchema = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _simTimeProvider = simTimeProvider ?? throw new ArgumentNullException(nameof(simTimeProvider));
            _options = options ?? new LootOptions();
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();
            _conditionSchema = conditionSchema;

            // 判断记录：conditionSchema 未显式提供时默认 RulesExprSchema.Base（见
            // LootTableParser 判断记录"不硬编码 Base，只作默认值"）——组装层需要 loot 条件引用
            // world/quest/player 分组时应显式传入 RulesExprSchema.Compose(...) 的结果。
            ReloadTables();

            // P2-05 同类缓存收口（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：_tables
            // 此前只在构造期从 registry 读取一次、永久常驻，与 SkillDefCache/ArchetypeRegistry/
            // StatHost 同一类模式。_dropped/_order/_missStreaks 是运行期状态（活跃地面掉落物、
            // 伪随机连续未中计数），不派生自 _tables 内容本身，reload 不清空/不重算，只替换掉落表
            // 定义本身——已存在的地面掉落物实体不受影响（同 QuestHost.Reload 判断记录"不清空运行期
            // 状态"）。
            _bus.Subscribe<Core.Foundation.DataRegistry.DataLoadCompletedEvent>(
                Core.Foundation.DataRegistry.DataRegistryEventKeys.LoadCompleted, _ => ReloadTables());
        }

        private void ReloadTables()
        {
            _tables.Clear();
            foreach (var record in _registry.GetAll(LootSchemas.Table.Name))
            {
                var def = LootTableParser.Parse(record, _conditionSchema);
                _tables[def.Id] = def;
            }
        }

        /// <summary>当前活跃（尚未被完全拾取/过期销毁）的地面掉落物 id，按 <see cref="Drop"/> 调用
        /// 顺序排列（惯例同 <c>core/carriers/summon.SummonHost.ActiveSummonIds</c>）。</summary>
        public IReadOnlyList<Id> ActiveLootIds => _order;

        /// <summary>按 id 取回一个仍活跃的地面掉落物实体强类型引用（供 <see
        /// cref="DroppedLootPersistable.Save"/> 使用，避免重复维护第二份状态）。</summary>
        public bool TryGetDropped(Id lootInstanceId, out DroppedLootEntity entity) => _dropped.TryGetValue(lootInstanceId, out entity);

        // -----------------------------------------------------------------
        // ILootHost / ILootRoller
        // -----------------------------------------------------------------

        public IReadOnlyList<ItemStack> Roll(Id tableId, RollContext context)
        {
            // T-N2-8 判断记录：旧 Roll 改为调用 RollDetailed 再投影/合并（见该方法），不再自己走一遍
            // RollTableInto——保证两条路径消耗同一份 IRngHost 序列，不会重复抽取。旧 Roll 的随机数
            // 消耗因此也会变化（品质骰/词缀骰追加在既有"掉哪条"掷骰之后），这正是拍板 12"掉落三次
            // 掷骰那次再更新一次回放基线"的来源，允许且必须在本任务提交内更新基线（见 README"掷骰
            // 顺序"判断记录）。
            var outcomes = RollDetailed(tableId, context);
            var merged = MergeOutcomes(outcomes);
            _bus.Enqueue(new LootRolledEvent(tableId, context.ContextId, merged));
            return merged;
        }

        /// <summary><see cref="ILootRoller"/> 落地：委托到 <see cref="Roll(Id, RollContext)"/>（见
        /// <c>ILootRoller</c> 类型注释）。</summary>
        public IReadOnlyList<ItemStack> Roll(Id lootTableId, Id sourceUnitId, Id? killerId) =>
            Roll(lootTableId, new RollContext(sourceUnitId, killerId));

        /// <summary>
        /// T-N2-8（ADR-0032 决策 7；<see cref="ILootHost.RollDetailed"/> 类型注释）：带身份的掉落
        /// 抽取——与 <see cref="Roll(Id, RollContext)"/> 走同一遍 <see cref="RollTableInto"/> 树遍历、
        /// 消耗同一份 <see cref="IRngHost"/> 序列，只是不按模板合并、也不在本方法内发
        /// <see cref="LootRolledEvent"/>（事件由 <see cref="Roll(Id, RollContext)"/> 在合并之后发出，
        /// 直接调用本方法的调用方若需要"已发生一次掉落"的事件通知，应自行处理，见 README"事件"一节）。
        /// </summary>
        public IReadOnlyList<LootRollOutcome> RollDetailed(Id tableId, RollContext context)
        {
            if (!_tables.TryGetValue(tableId, out var def))
            {
                throw new ArgumentException($"未知的掉落表 \"{tableId}\"", nameof(tableId));
            }

            var exprHost = _exprHostFactory.CreateFor(context.KillerId ?? context.SourceUnitId, context.SourceUnitId, null);

            var raw = new List<LootRollOutcome>();
            RollTableInto(def, context, exprHost, raw, depth: 0);
            return raw;
        }

        /// <summary><see cref="ILootRoller"/> 落地：委托到 <see cref="RollDetailed(Id, RollContext)"/>
        /// （同 <see cref="Roll(Id, Id, Id?)"/> 判断记录）。</summary>
        public IReadOnlyList<LootRollOutcome> RollDetailed(Id lootTableId, Id sourceUnitId, Id? killerId) =>
            RollDetailed(lootTableId, new RollContext(sourceUnitId, killerId));

        // -----------------------------------------------------------------
        // 抽取核心
        // -----------------------------------------------------------------

        private void RollTableInto(LootTableDef def, RollContext context, IExprHost exprHost, List<LootRollOutcome> output, int depth)
        {
            if (depth > _options.MaxNestedDepth)
            {
                // 判断记录（LootOptions.MaxNestedDepth 注释同一处）：正常内容已被
                // LootContentValidationRule 的成环检测拦截，这里只是运行期兜底，静默停止展开。
                return;
            }

            var resultCount = 0;

            for (var gi = 0; gi < def.Groups.Count; gi++)
            {
                var group = def.Groups[gi];
                if (group.RollMode == LootRollMode.ChanceEach)
                {
                    resultCount += RollChanceEachGroup(def, gi, group, context, exprHost, depth, output);
                }
                else
                {
                    resultCount += RollWeightedGroup(group.Entries, group.PickCount ?? 1, context, exprHost, depth, output);
                }
            }

            if (def.GuaranteedMin.HasValue && resultCount < def.GuaranteedMin.Value)
            {
                var candidatePool = new List<LootEntry>();
                foreach (var group in def.Groups)
                {
                    candidatePool.AddRange(LootRollCore.FilterEligible(group.Entries, exprHost, _diagnostics));
                }

                while (resultCount < def.GuaranteedMin.Value && candidatePool.Count > 0)
                {
                    var picked = PickWeighted(candidatePool);
                    if (picked == null)
                    {
                        break;
                    }

                    candidatePool.Remove(picked);
                    var count = RollCount(picked);
                    ResolveEntryAtDepth(picked, count, context, exprHost, depth, output);
                    resultCount++;
                }
            }
        }

        private int RollChanceEachGroup(LootTableDef def, int groupIndex, LootGroup group, RollContext context, IExprHost exprHost, int depth, List<LootRollOutcome> output)
        {
            var produced = 0;
            for (var ei = 0; ei < group.Entries.Count; ei++)
            {
                var entry = group.Entries[ei];
                if (!ConditionPasses(entry, exprHost))
                {
                    continue;
                }

                var baseChance = LootRollCore.EffectiveChanceEach(entry.WeightOrChance, context.Multiplier);
                var effectiveChance = baseChance;
                string? pseudoKey = null;

                if (_options.PseudoRandom)
                {
                    pseudoKey = $"{context.ContextId}|{def.Id}|{groupIndex}|{ei}";
                    var streak = _missStreaks.TryGetValue(pseudoKey, out var s) ? s : 0;
                    effectiveChance = LootRollCore.ApplyPseudoRandomStep(baseChance, streak, _options.PseudoRandomStep);
                }

                var roll = _rng.Next(_options.RngStream);
                if (roll < effectiveChance)
                {
                    if (pseudoKey != null)
                    {
                        _missStreaks[pseudoKey] = 0;
                    }

                    var count = RollCount(entry);
                    ResolveEntryAtDepth(entry, count, context, exprHost, depth, output);
                    produced++;
                }
                else if (pseudoKey != null)
                {
                    _missStreaks[pseudoKey] = _missStreaks.TryGetValue(pseudoKey, out var s2) ? s2 + 1 : 1;
                }
            }

            return produced;
        }

        private int RollWeightedGroup(IReadOnlyList<LootEntry> entries, int pickCount, RollContext context, IExprHost exprHost, int depth, List<LootRollOutcome> output)
        {
            var pool = LootRollCore.FilterEligible(entries, exprHost, _diagnostics);

            var produced = 0;
            for (var i = 0; i < pickCount && pool.Count > 0; i++)
            {
                var picked = PickWeighted(pool);
                if (picked == null)
                {
                    break;
                }

                pool.Remove(picked);
                var count = RollCount(picked);
                ResolveEntryAtDepth(picked, count, context, exprHost, depth, output);
                produced++;
            }

            return produced;
        }

        private LootEntry? PickWeighted(List<LootEntry> pool)
        {
            var totalWeight = LootRollCore.TotalWeight(pool);
            if (totalWeight <= 0)
            {
                return null;
            }

            var thresholdFraction = _rng.Next(_options.RngStream);
            return LootRollCore.SelectByThreshold(pool, totalWeight, thresholdFraction);
        }

        private int RollCount(LootEntry entry) =>
            entry.CountMin == entry.CountMax ? entry.CountMin : _rng.NextInt(_options.RngStream, entry.CountMin, entry.CountMax);

        private bool ConditionPasses(LootEntry entry, IExprHost exprHost) =>
            LootRollCore.ConditionPasses(entry, exprHost, _diagnostics);

        /// <summary>把一条候选解析为具体产出：<paramref name="entry"/>.Ref 是 <c>item.*</c> 时按
        /// 判断记录"掷骰顺序"追加一条 <see cref="LootRollOutcome"/>（品质骰 + 词缀骰追加在本条目已经
        /// 消耗的"掉哪条"掷骰之后，见 <see cref="ResolveItemOutcome"/>）；是 <c>loot.*</c> 时按判断
        /// 记录 2 递归展开该嵌套表 <paramref name="count"/> 次（嵌套表自己的叶子 <c>item.*</c> 条目
        /// 各自独立走一遍品质骰/词缀骰，不在本层重复）。</summary>
        private void ResolveEntryAtDepth(LootEntry entry, int count, RollContext context, IExprHost exprHost, int depth, List<LootRollOutcome> output)
        {
            if (entry.Ref.Domain == "loot")
            {
                if (!_tables.TryGetValue(entry.Ref, out var nestedDef))
                {
                    // 引用了未加载的嵌套表：不是运行期应该崩溃的错误（04 第 5 节引用完整性理应已在
                    // 内容校验阶段拦截），静默跳过该次展开。
                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    RollTableInto(nestedDef, context, exprHost, output, depth + 1);
                }
            }
            else
            {
                output.Add(ResolveItemOutcome(entry, count, context));
            }
        }

        /// <summary>
        /// T-N2-8（ADR-0032 决策 7；08 第 1.1 节修订段"三次独立掷骰"；07 第 1.6 节修订段）：在"掉哪条"
        /// 掷骰（<see cref="RollChanceEachGroup"/>/<see cref="RollWeightedGroup"/> 的 <c>Next</c> +
        /// <see cref="RollCount"/> 的 <c>NextInt</c>，均不变）之后，紧接着为这一条 <c>item.*</c> 结果
        /// 追加两步：
        /// <list type="number">
        /// <item><description>品质骰：<paramref name="entry"/>.QualityWeights 非空时消耗一次
        /// <see cref="IRngHost.Next"/> 加权抽取；为空/未配置时不掷骰、直接取模板自身
        /// <c>item.template.quality</c>（不消耗随机数，见 <see cref="LootEntry.QualityWeights"/>
        /// 判断记录）。</description></item>
        /// <item><description>词缀骰：按品质定义 <c>item.quality_definition.affix_count</c>（未登记/
        /// &lt;=0 视为不掷词缀骰，同样不消耗随机数）逐个消耗一次 <see cref="IRngHost.Next"/>，从
        /// "<c>quality_pool == 本次品质骰结果</c> 且（模板 <c>affixes</c> 白名单非空时取交集）"的
        /// <c>item.affix</c> 候选池按 <c>weight</c> 加权、不放回地抽取，候选耗尽提前停止（不是
        /// 错误）。</description></item>
        /// </list>
        /// 物品等级不参与掷骰（<see cref="RollContext.SourceLevel"/> 非空时 = <c>SourceLevel +
        /// ItemLevelOffset</c> 的确定性折算，为空时取模板自身 <c>item_level</c>），因此三次独立掷骰
        /// 里只有品质与词缀两步真正消耗随机数——这样"掉哪条"的既有结果与旧基线逐位一致，只有品质骰/
        /// 词缀骰新增的随机数消耗会让"这一条之后"的序列发生变化（见 README"掷骰顺序"判断记录、拍板
        /// 12"回放基线更新"）。
        /// </summary>
        private LootRollOutcome ResolveItemOutcome(LootEntry entry, int count, RollContext context)
        {
            var templateRecord = _registry.Get(ItemTemplateTable, entry.Ref);
            if (templateRecord == null)
            {
                // 引用完整性理应已在内容校验阶段拦截（LootContentValidationRule"ref 存在性"）；运行期
                // 兜底：拿不到模板数据就无法解析品质/词缀/物品等级，退化为"未额外指定"（同
                // ILootRoller.RollDetailed 默认接口成员的投影语义）。
                return new LootRollOutcome(entry.Ref, count, null, null, null);
            }

            var templateQuality = templateRecord.GetId("quality");
            var qualityId = RollQuality(entry, templateQuality);

            var itemLevel = context.SourceLevel.HasValue
                ? context.SourceLevel.Value + context.ItemLevelOffset
                : (int?)null;

            var affixes = RollAffixes(qualityId, templateRecord);

            return new LootRollOutcome(entry.Ref, count, qualityId, affixes, itemLevel);
        }

        private const string ItemTemplateTable = "item.template";

        private const string ItemQualityDefinitionTable = "item.quality_definition";

        private const string ItemAffixTable = "item.affix";

        /// <summary>品质骰：见 <see cref="ResolveItemOutcome"/> 判断记录第 1 步。</summary>
        private Id RollQuality(LootEntry entry, Id templateQuality)
        {
            if (entry.QualityWeights == null || entry.QualityWeights.Count == 0)
            {
                return templateQuality;
            }

            var weights = new List<double>(entry.QualityWeights.Count);
            var ids = new List<Id>(entry.QualityWeights.Count);
            foreach (var kv in entry.QualityWeights)
            {
                if (kv.Value <= 0)
                {
                    continue;
                }

                ids.Add(kv.Key);
                weights.Add(kv.Value);
            }

            if (ids.Count == 0)
            {
                return templateQuality;
            }

            var totalWeight = 0.0;
            foreach (var w in weights)
            {
                totalWeight += w;
            }

            var thresholdFraction = _rng.Next(_options.RngStream);
            var idx = LootRollCore.SelectIndexByThreshold(weights, totalWeight, thresholdFraction);
            return ids[idx];
        }

        /// <summary>词缀骰：见 <see cref="ResolveItemOutcome"/> 判断记录第 2 步。候选池按
        /// <c>item.affix.Id</c>（序数字符串）排序后再抽取——不依赖 <see
        /// cref="IDataRegistryView.GetAll"/> 的既有顺序是否稳定（同 T-N1-2 判断记录"禁止把拓扑序依赖
        /// 字典枚举顺序，须稳定排序"），保证同种子重跑逐字段一致不受注册表内部实现细节影响。</summary>
        private IReadOnlyList<Id> RollAffixes(Id qualityId, DataRecord templateRecord)
        {
            var qualityDef = _registry.Get(ItemQualityDefinitionTable, qualityId);
            var affixCount = qualityDef != null && qualityDef.TryGetInt("affix_count", out var ac) ? (int)ac : 0;
            if (affixCount <= 0)
            {
                return Array.Empty<Id>();
            }

            var hasWhitelist = templateRecord.TryGetIdList("affixes", out var whitelist) && whitelist.Count > 0;

            var candidates = new List<(Id Id, double Weight)>();
            foreach (var record in _registry.GetAll(ItemAffixTable))
            {
                if (record.Id == null)
                {
                    continue;
                }

                if (!record.TryGetId("quality_pool", out var pool) || !pool.Equals(qualityId))
                {
                    continue;
                }

                if (hasWhitelist && !ContainsId(whitelist, record.Id.Value))
                {
                    continue;
                }

                var weight = record.TryGetNumber("weight", out var w) ? w : 0.0;
                if (weight <= 0.0)
                {
                    continue;
                }

                candidates.Add((record.Id.Value, weight));
            }

            if (candidates.Count == 0)
            {
                return Array.Empty<Id>();
            }

            candidates.Sort((a, b) => string.CompareOrdinal(a.Id.Value, b.Id.Value));

            var picked = new List<Id>(Math.Min(affixCount, candidates.Count));
            for (var i = 0; i < affixCount && candidates.Count > 0; i++)
            {
                var weights = new List<double>(candidates.Count);
                foreach (var c in candidates)
                {
                    weights.Add(c.Weight);
                }

                var totalWeight = 0.0;
                foreach (var w in weights)
                {
                    totalWeight += w;
                }

                if (totalWeight <= 0.0)
                {
                    break;
                }

                var thresholdFraction = _rng.Next(_options.RngStream);
                var idx = LootRollCore.SelectIndexByThreshold(weights, totalWeight, thresholdFraction);
                picked.Add(candidates[idx].Id);
                candidates.RemoveAt(idx);
            }

            return picked;
        }

        private static bool ContainsId(IReadOnlyList<Id> list, Id value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>按模板 id 合并（不看品质/词缀，惯例同旧实现——见 <see cref="Roll(Id, RollContext)"/>
        /// 判断记录"投影/合并"）。</summary>
        private static IReadOnlyList<ItemStack> MergeOutcomes(IReadOnlyList<LootRollOutcome> outcomes)
        {
            var order = new List<Id>();
            var totals = new Dictionary<Id, int>();
            foreach (var outcome in outcomes)
            {
                if (outcome.Count <= 0)
                {
                    continue;
                }

                if (!totals.ContainsKey(outcome.TemplateId))
                {
                    order.Add(outcome.TemplateId);
                    totals[outcome.TemplateId] = 0;
                }

                totals[outcome.TemplateId] += outcome.Count;
            }

            var result = new List<ItemStack>(order.Count);
            foreach (var templateId in order)
            {
                result.Add(new ItemStack(templateId, totals[templateId]));
            }

            return result;
        }

        /// <summary>T-N2-8：把一条不含身份信息的 <see cref="ItemStack"/> 解析出"未额外指定"以外的
        /// 缺省身份——用于 <see cref="Drop(Id, Vec2, IReadOnlyList{ItemStack}, Id?)"/>（旧签名，调用方
        /// 只有 <see cref="ItemStack"/> 可给）与 <see cref="DroppedLootPersistable"/> 读取旧存档（缺
        /// <c>qualityId</c>/<c>affixes</c>/<c>itemLevel</c> key）两处复用同一份"缺省=模板品质、无
        /// 词缀、物品等级=模板 item_level"解析逻辑（照 T-N2-7 的兼容读取先例，见 <see
        /// cref="LootRollOutcome"/> 判断记录）。查不到模板数据（引用完整性理应已在内容校验阶段拦截）
        /// 时退化为"未额外指定"（<c>null</c>/<c>null</c>），不抛异常。</summary>
        internal LootRollOutcome ResolveDefaultOutcome(ItemStack stack)
        {
            var templateRecord = _registry.Get(ItemTemplateTable, stack.TemplateId);
            if (templateRecord == null)
            {
                return LootRollOutcome.FromStack(stack);
            }

            var qualityId = templateRecord.TryGetId("quality", out var q) ? (Id?)q : null;
            var itemLevel = templateRecord.TryGetInt("item_level", out var lvl) ? (int?)lvl : null;
            return new LootRollOutcome(stack.TemplateId, stack.Count, qualityId, null, itemLevel);
        }

        // -----------------------------------------------------------------
        // Drop / PickUp
        // -----------------------------------------------------------------

        /// <summary>生成一个地面掉落物实体（见 08 第 1.2 节、05 第 1.6 节）。T-N2-8 判断记录：本重载
        /// （既有签名，ABI 不变）只拿得到 <see cref="ItemStack"/>（无身份信息，如调用方经旧 <see
        /// cref="Roll(Id, RollContext)"/> 拿到的结果），<see cref="DroppedLootEntity.Outcomes"/> 因此经
        /// <see cref="ResolveDefaultOutcome"/> 按模板缺省（品质=模板品质、无词缀、物品等级=模板
        /// item_level）逐条解析——与显式提供 <see cref="LootRollOutcome"/> 的新重载相比，唯一区别是
        /// "有没有额外指定"，落地的 <see cref="DroppedLootEntity.Outcomes"/> 长度总是与 <paramref
        /// name="items"/> 一致（见该属性判断记录）。</summary>
        public Id Drop(Id mapId, Vec2 position, IReadOnlyList<ItemStack> items, Id? ownerHint = null)
        {
            var stacks = items ?? Array.Empty<ItemStack>();
            var outcomes = new List<LootRollOutcome>(stacks.Count);
            foreach (var stack in stacks)
            {
                outcomes.Add(ResolveDefaultOutcome(stack));
            }

            return DropCore(mapId, position, stacks, outcomes, ownerHint);
        }

        /// <summary>
        /// T-N2-8 新增重载（ABI 硬性规则"只允许新增"，不改既有 <see cref="Drop(Id, Vec2,
        /// IReadOnlyList{ItemStack}, Id?)"/> 签名）：直接用 <see cref="RollDetailed(Id, RollContext)"/>
        /// 产出的带身份结果生成地面掉落物，<see cref="DroppedLootEntity.Outcomes"/> 原样保留调用方给出
        /// 的品质/词缀/物品等级（不经 <see cref="ResolveDefaultOutcome"/> 再解析）。<see
        /// cref="DroppedLootEntity.Items"/> 按 <paramref name="outcomes"/> 逐条投影（<see
        /// cref="LootRollOutcome.ToStack"/>），顺序与下标均与 <see
        /// cref="DroppedLootEntity.Outcomes"/> 一致，不做按模板合并（同一模板不同品质的两条各自成一条
        /// <see cref="ItemStack"/>，同 <see cref="RollDetailed(Id, RollContext)"/> 判断记录"不按模板
        /// 合并"）。
        /// </summary>
        public Id Drop(Id mapId, Vec2 position, IReadOnlyList<LootRollOutcome> outcomes, Id? ownerHint = null)
        {
            var list = outcomes ?? Array.Empty<LootRollOutcome>();
            var stacks = new List<ItemStack>(list.Count);
            foreach (var outcome in list)
            {
                stacks.Add(outcome.ToStack());
            }

            return DropCore(mapId, position, stacks, list, ownerHint);
        }

        private Id DropCore(Id mapId, Vec2 position, IReadOnlyList<ItemStack> items, IReadOnlyList<LootRollOutcome> outcomes, Id? ownerHint)
        {
            var id = _world.AllocateEntityId(EntityKinds.Loot);
            double? expireAt = _options.DefaultLifetime > 0 ? _simTimeProvider() + _options.DefaultLifetime : (double?)null;

            var entity = new DroppedLootEntity(id, mapId, items, outcomes, ownerHint, expireAt)
            {
                Position = position,
                // H4 补齐（见 DroppedLootEntity.GenericDisplayTemplateId 判断记录）：固定复用同一个
                // "地面拾取物外观"逻辑 id，让 WorldSim.AddEntity 能算出一个稳定、非空的 displayId。
                TemplateId = DroppedLootEntity.GenericDisplayTemplateId,
            };

            _world.AddEntity(entity);
            _dropped[id] = entity;
            _order.Add(id);
            return id;
        }

        /// <summary>拾取一个地面掉落物（见 08 第 1.2 节）。距离超出 <see
        /// cref="LootOptions.PickupRange"/> 或 <paramref name="lootInstanceId"/> 不存在时失败，见
        /// <see cref="LootPickupResult"/> 类型注释。</summary>
        public LootPickupResult PickUp(Id unitId, Id lootInstanceId)
        {
            if (!_dropped.TryGetValue(lootInstanceId, out var entity) || entity.Items.Count == 0)
            {
                return LootPickupResult.Fail(LootPickupFailureReason.NotFound);
            }

            var unitPos = _units.GetPosition(unitId);
            if (Vec2.Distance(unitPos, entity.Position) > _options.PickupRange)
            {
                return LootPickupResult.Fail(LootPickupFailureReason.TooFar);
            }

            return _options.FullPolicy == LootPickupPolicy.Reject
                ? PickUpReject(unitId, lootInstanceId, entity)
                : PickUpPartial(unitId, lootInstanceId, entity);
        }

        private LootPickupResult PickUpReject(Id unitId, Id lootInstanceId, DroppedLootEntity entity)
        {
            var want = new List<ItemStack>(entity.Items);
            var addedPerStack = new List<int>(want.Count);
            var fullySucceeded = true;

            // CR130-01 根治（外部审计 audit-5c444f1-20260908，与 EconomyHost.Buy 同款缺口）：本方法
            // 是"全部拿到才算数"的 Reject 策略——某一件放不下时，前面已经成功 AddItem 的堆叠需要按
            // RollbackAdd 补偿撤销；AddItem 的 item.added 与补偿的 item.removed 是两条独立入队事件，
            // 不加事务时会在同一次 DispatchPending 里先后派发，下游 consumeOnProgress 一类订阅者会把
            // 先到的 item.added 当真、立即消费玩家已有的同模板物品，后到的 item.removed 抵消不了这个
            // 副作用（同 EconomyHost.Buy 判断记录）。_inventory 实现 IBatchableInventoryHost 时把整趟
            // 拾取尝试包进一次事务，失败时 using 块结束触发 Dispose（未 Commit 即回滚）把背包状态与
            // 缓存事件一并撤销，不需要再逐项 RollbackAdd；不支持事务的宿主退回历史行为。
            var transaction = _inventory is IBatchableInventoryHost batchable ? batchable.BeginBatch() : null;
            using (transaction)
            {
                foreach (var stack in want)
                {
                    var before = _inventory.CountOf(unitId, stack.TemplateId);
                    _inventory.AddItem(unitId, stack.TemplateId, stack.Count);
                    var added = Math.Max(0, _inventory.CountOf(unitId, stack.TemplateId) - before);
                    addedPerStack.Add(added);
                    if (added < stack.Count)
                    {
                        fullySucceeded = false;
                    }
                }

                if (!fullySucceeded)
                {
                    if (transaction == null)
                    {
                        for (var i = 0; i < want.Count; i++)
                        {
                            if (addedPerStack[i] > 0)
                            {
                                RollbackAdd(unitId, want[i].TemplateId, addedPerStack[i]);
                            }
                        }
                    }
                    // 宿主支持事务时不需要手动回滚——using 块结束触发 Dispose 即整体撤销（含事件）。

                    return LootPickupResult.Fail(LootPickupFailureReason.Rejected);
                }

                transaction?.Commit();
            }

            entity.Items.Clear();
            DestroyDropped(lootInstanceId);
            _bus.Enqueue(new LootPickedUpEvent(unitId, lootInstanceId, want));
            return LootPickupResult.Ok(want);
        }

        private LootPickupResult PickUpPartial(Id unitId, Id lootInstanceId, DroppedLootEntity entity)
        {
            var want = new List<ItemStack>(entity.Items);
            var taken = new List<ItemStack>();
            var remaining = new List<ItemStack>();

            foreach (var stack in want)
            {
                var before = _inventory.CountOf(unitId, stack.TemplateId);
                _inventory.AddItem(unitId, stack.TemplateId, stack.Count);
                var added = Math.Min(stack.Count, Math.Max(0, _inventory.CountOf(unitId, stack.TemplateId) - before));

                if (added > 0)
                {
                    taken.Add(new ItemStack(stack.TemplateId, added));
                }

                var leftover = stack.Count - added;
                if (leftover > 0)
                {
                    remaining.Add(new ItemStack(stack.TemplateId, leftover));
                }
            }

            if (taken.Count == 0)
            {
                // 一件都没拿到：地面掉落物内容不变（remaining == want 的堆叠数值，只是重新分配了
                // 列表实例，逐项数值相等）。
                return LootPickupResult.Fail(LootPickupFailureReason.InventoryFull);
            }

            entity.Items.Clear();
            entity.Items.AddRange(remaining);

            if (entity.Items.Count == 0)
            {
                DestroyDropped(lootInstanceId);
            }

            _bus.Enqueue(new LootPickedUpEvent(unitId, lootInstanceId, taken));
            return LootPickupResult.Ok(taken);
        }

        private void RollbackAdd(Id unitId, Id templateId, int amount)
        {
            var remaining = amount;
            foreach (var instance in _inventory.ListItems(unitId))
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (!instance.TemplateId.Equals(templateId))
                {
                    continue;
                }

                var take = Math.Min(remaining, instance.Count);
                _inventory.RemoveItem(unitId, instance.InstanceId, take);
                remaining -= take;
            }
        }

        private void DestroyDropped(Id lootInstanceId)
        {
            _world.MarkForDestruction(lootInstanceId);
            _dropped.Remove(lootInstanceId);
            _order.Remove(lootInstanceId);
        }

        // -----------------------------------------------------------------
        // 过期（供 LootExpiryTickHandler 驱动）
        // -----------------------------------------------------------------

        /// <summary>销毁全部 <c>ExpireAt &lt;= now</c> 的地面掉落物（见 <see
        /// cref="LootExpiryTickHandler"/>）。</summary>
        public void PurgeExpired(double now)
        {
            // 先快照 id 列表：DestroyDropped 会修改 _order/_dropped，边遍历边改容易漏处理。
            var ids = new List<Id>(_order);
            foreach (var id in ids)
            {
                if (_dropped.TryGetValue(id, out var entity) && entity.ExpireAt.HasValue && entity.ExpireAt.Value <= now)
                {
                    DestroyDropped(id);
                }
            }
        }

        // -----------------------------------------------------------------
        // 存档重建（供 DroppedLootPersistable 使用）
        // -----------------------------------------------------------------

        /// <summary>把一个从存档反序列化出的实体直接接回世界与本模块的跟踪表，不重新分配 id、不发
        /// <c>loot.rolled</c>/<c>loot.picked_up</c> 事件（这不是"发生了一次新的掉落/拾取"，只是恢复
        /// 既有状态，惯例同 <c>core/carriers/item.InventoryHost.ReplaceBag</c>）。
        /// <para>
        /// 判断记录（U3 排障发现的契约缺口：同一局内"存档 -&gt; 读档"这条路径会撞上"实体 id
        /// 重复"异常）：<c>Presentation.Shell.ShellHost.LoadGame</c> 的既有实现顺序是
        /// <c>ISaveSystem.Load</c>（本方法在这一步被调用）先于 <c>ISceneRouter.LoadScene</c>
        /// （真正触发 <c>IWorldSim.ClearAll</c> 清空旧实体的地方）——也就是说本方法执行时，
        /// 存档快照里记录的地面掉落物 id，如果是"读档前那局游戏本身还没被拾取/过期就已经掉落在地上"
        /// 的同一个 <c>DroppedLootEntity</c>，此时仍然原样存在于 <see cref="IWorldSim"/> 里（还没被
        /// 清空），直接调 <c>IWorldSim.AddEntity</c> 会因为 id 已存在抛
        /// <c>InvalidOperationException("实体 id 重复")</c>（U3 实测复现：`
        /// VerticalSliceTests.FullVerticalSlice_...` 是第一条"击杀生物产生地面掉落 + 存档 -&gt;
        /// 读档"两件事同时发生的测试，此前从未有测试同时触碰过这条路径）。<see cref="IWorldSim"/>
        /// 没有暴露"立即同步移除单个实体"的入口（<see cref="IWorldSim.MarkForDestruction"/> 只是
        /// 排入下一次 Tick 阶段 8 才真正生效，本方法内联调用后立刻 <c>AddEntity</c> 仍会撞上同一个
        /// 异常），本方法退而求其次：若发现同 id 的地面掉落物已经存在于世界里，判定为"读档快照与
        /// 当前存活实体本就是同一份掉落物"，直接原地把存档内容写回这个已存在的实体对象（位置/物品/
        /// 归属提示/过期时间），不重新 <c>AddEntity</c>、不产生"重复实体"——不改动
        /// <see cref="IWorldSim"/> 契约（新增一个"立即同步移除"的公开方法会牵动其全部实现/测试替身，
        /// 超出本次最小修复范围），也不改动 <c>ShellHost.LoadGame</c> 既有的
        /// "先恢复段、后切场景"顺序（那会影响全部 <see cref="IPersistable"/> 段，改动面过大）。
        /// </para>
        /// <para>
        /// GP-PRES-01 收口（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
        /// 上一段判断记录只处理了"读档时同 id 实体仍原地存活"这一种情况，没有处理"读档 →
        /// ISceneRouter.LoadScene 触发 IWorldSim.ClearAll"这个更常见的后续步骤——本方法把恢复的
        /// 实体同时写入 _world 与本模块自己的跟踪表 _dropped/_order，但 IWorldSim.ClearAll 只清空
        /// IWorldSim 自己的实体集合，不知道也不会通知 LootHost（本模块没有订阅 entity.destroyed，
        /// 见类型顶部判断记录——不需要，本模块自己的增删入口 Drop/PickUp/DestroyDropped 已经足够
        /// 维护 _dropped）：结果是 _dropped 里的引用在 ClearAll 之后变成"游戏逻辑上仍认为存在，
        /// 但已经不在 IWorldSim 里"的孤儿状态，掉落物随场景切换静默消失。修复不改
        /// ShellHost.LoadGame/ISaveSystem.Load 的既有顺序（理由同上一段），改为在场景真正就绪之后
        /// （ScenePostLoad 钩子，晚于 ClearAll）调用 <see cref="ReattachToWorld"/> 把 _dropped 里
        /// "应该在当前地图、但当前不在 IWorldSim 里"的实体重新 AddEntity 回去——见该方法与
        /// games/_template/Runtime/GameBootstrap.HandlePostLoad（该方法已经用同一个钩子重新添加
        /// 玩家实体，本次只是给同一个钩子再加一步）。
        /// </para>
        /// </summary>
        public void RestoreDropped(DroppedLootEntity entity)
        {
            if (_world.GetEntity(entity.EntityId) is DroppedLootEntity existing)
            {
                existing.Position = entity.Position;
                existing.Items.Clear();
                existing.Items.AddRange(entity.Items);
                existing.ReplaceOutcomes(entity.Outcomes);
                existing.OwnerHint = entity.OwnerHint;
                existing.ExpireAt = entity.ExpireAt;

                _dropped[entity.EntityId] = existing;
                if (!_order.Contains(entity.EntityId))
                {
                    _order.Add(entity.EntityId);
                }
                return;
            }

            _world.AddEntity(entity);
            _dropped[entity.EntityId] = entity;
            _order.Add(entity.EntityId);
        }

        /// <summary>
        /// GP-PRES-01 收口新增：把本模块自己跟踪表（<see cref="_dropped"/>）里"属于
        /// <paramref name="mapId"/>、但当前不在 <see cref="_world"/> 里"的地面掉落物重新
        /// <c>AddEntity</c> 回世界——见 <see cref="RestoreDropped"/> 判断记录"GP-PRES-01 收口"一节。
        /// 调用方（<c>games/_template/Runtime/GameBootstrap.HandlePostLoad</c>）应在
        /// <c>ISceneRouter</c> 的 <c>ScenePostLoad</c> 钩子里、场景真正切好之后调用本方法——早于此
        /// 调用（例如 <c>ClearAll</c> 之前）没有意义（此时实体还没被清掉），晚于此调用（例如下一次
        /// 场景切换的 <c>ClearAll</c> 之后才想起来调用）会导致空档期内本应存在的掉落物从
        /// <see cref="IWorldSim"/> 视角"消失"。
        /// <para>
        /// 按 <paramref name="mapId"/> 过滤（不是把 <see cref="_dropped"/> 全部重新
        /// <c>AddEntity</c>）：<see cref="_dropped"/> 会跨地图累积（本模块从不因为"玩家离开了这张
        /// 地图"就清理该地图上尚未拾取/过期的掉落物——见类型顶部判断记录，<see cref="IWorldSim.ClearAll"/>
        /// 每次场景切换都会触发，但那只是"当前渲染/模拟的地图"清空，不代表其它地图的掉落物应该被
        /// 遗忘），只应该把属于"即将激活的这张地图"的记录重新接回 <see cref="_world"/>，否则会把
        /// 其它地图的掉落物错误地混入当前地图的 <see cref="IWorldSim"/> 实体集合。
        /// </para>
        /// <para>
        /// 幂等：已经在 <see cref="_world"/> 里的实体（<see cref="IWorldSim.GetEntity"/> 非
        /// null）会被跳过，不重复 <c>AddEntity</c>（那会触发"实体 id 重复"异常）——正常
        /// 首次进入地图（<c>HandlePostLoad</c> 在没有发生过 <c>ClearAll</c> 的"游戏刚启动、第一次
        /// 进图"场景下也会被调用一次）时 <see cref="_dropped"/> 本就是空的，本方法是安全的空操作。
        /// </para>
        /// </summary>
        public void ReattachToWorld(Id mapId)
        {
            // 快照 key 列表：AddEntity 不会修改 _dropped，但遍历期间调用其它 LootHost 方法（理论上
            // 调用方不应该在本方法执行期间重入）仍然按惯例（同 PurgeExpired）避免边遍历边改。
            var ids = new List<Id>(_order);
            foreach (var id in ids)
            {
                if (!_dropped.TryGetValue(id, out var entity))
                {
                    continue;
                }

                if (!entity.MapId.Equals(mapId))
                {
                    continue;
                }

                if (_world.GetEntity(id) == null)
                {
                    _world.AddEntity(entity);
                }
            }
        }

        /// <summary>
        /// 契约缺口的运行期规避：<c>IWorldSim.AllocateEntityId</c> 内部按 <c>kind</c> 维护一个只增
        /// 计数器，且不提供任何"设置/推进到指定值"的入口（见 <see cref="RestoreDropped"/> 判断记录同一
        /// 处、README"契约缺口"一节）。读档后若不推进该计数器，后续真正的新掉落 <see cref="Drop"/>
        /// 调用可能分配到与刚恢复的存档实体相同的 id，触发 <c>IWorldSim.AddEntity</c> 的"实体 id 重复"
        /// 异常。本方法反复调用 <c>AllocateEntityId("loot")</c>（每次调用固定 +1，不产生任何其它副
        /// 作用——不调用 <c>AddEntity</c>）把计数器推进到严格大于 <paramref name="maxRestoredSequence"/>，
        /// 供 <see cref="DroppedLootPersistable.Load"/> 在恢复完全部实体后调用一次。
        /// </summary>
        public void ReserveLootIdSequenceAtLeast(int maxRestoredSequence)
        {
            if (maxRestoredSequence <= 0)
            {
                return;
            }

            int seq;
            do
            {
                var allocated = _world.AllocateEntityId(EntityKinds.Loot);
                seq = ExtractSequence(allocated);
            } while (seq <= maxRestoredSequence);
        }

        /// <summary>
        /// 外部审核阻塞项 1 收口（见 <c>DroppedLootPersistable.Load</c> 判断记录、
        /// architecture/落地计划/audit-20260907/followup-2026-09-07.md"外部审核阻塞项处理"一节）：
        /// 读档一致性根治——把本模块当前跟踪的地面掉落物中"不在这次要恢复的 id 集合
        /// （<paramref name="keepIds"/>）里"的全部按既有 <see cref="DestroyDropped"/> 语义清掉（标记
        /// 世界待销毁 + 立即移出 <see cref="_dropped"/>/<see cref="_order"/> 跟踪表），复现的缺口是：
        /// "保存空掉落档 → 产生物品 B → 读取旧档（该旧档不含 B）→ B 仍出现"——旧实现的
        /// <c>DroppedLootPersistable.Load</c> 只管往 <see cref="_dropped"/>/<see cref="_order"/> 里
        /// 加（经 <see cref="RestoreDropped"/>），从不清理"当前存在、但这次读档的存档快照里已经不再
        /// 提及"的旧记录，读档因此只有"增补"、没有"归零重建"的语义，与 10 号文档"读档恢复到存档
        /// 时刻的完整状态"这一预期不符。
        /// <para>
        /// 判断记录（<paramref name="keepIds"/> 内的 id 不动，交给随后的 <see cref="RestoreDropped"/>
        /// 处理，而不是本方法先统一销毁再全部重新 <see cref="IWorldSim.AddEntity"/>）：本方法只清理
        /// "不会被这次读档覆盖"的陈旧记录；对于"这次读档的快照里仍然存在同一个 id"的情形（典型场景
        /// 见 <see cref="RestoreDropped"/> 判断记录"U3 排障发现的契约缺口"——同一局内存档后未清空
        /// 世界就立即读档，该实体仍原样存活在 <see cref="_world"/> 里），若本方法也把它
        /// <see cref="MarkForDestruction"/>，会把该 id 排入 <see cref="IWorldSim"/> 的待销毁队列，
        /// 而随后 <see cref="RestoreDropped"/> 发现"世界里仍有同 id 实体"会走"原地覆写"分支、不重新
        /// <see cref="IWorldSim.AddEntity"/>——那个刚被标记待销毁的实体对象会在下一次
        /// <see cref="IWorldSim.Tick"/> 的生命周期清理阶段被真正移除，即便 <see cref="_dropped"/>/
        /// <see cref="_order"/> 都认为它仍然存活，产生"逻辑上存在、下一 tick 却突然消失"的新缺口。
        /// 只清理"不在 keepIds 里"的部分，天然避开这个问题——两个方法各自负责一半，合起来才是完整
        /// 的"读档=归零重建"语义。
        /// </para>
        /// </summary>
        public void ClearDroppedExcept(IReadOnlyCollection<Id> keepIds)
        {
            var keep = keepIds as ISet<Id> ?? new HashSet<Id>(keepIds);

            // 快照 id 列表：DestroyDropped 会修改 _order/_dropped，边遍历边改容易漏处理（同
            // PurgeExpired/ReattachToWorld 一贯惯例）。
            var ids = new List<Id>(_order);
            foreach (var id in ids)
            {
                if (!keep.Contains(id))
                {
                    DestroyDropped(id);
                }
            }
        }

        internal static int ExtractSequence(Id lootEntityId)
        {
            const string prefix = "loot.inst_";
            var value = lootEntityId.Value;
            return value.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(value.Substring(prefix.Length), out var n)
                ? n
                : 0;
        }
    }
}

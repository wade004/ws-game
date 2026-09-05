using System;
using System.Collections.Generic;
using System.Linq;
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
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _simTimeProvider = simTimeProvider ?? throw new ArgumentNullException(nameof(simTimeProvider));
            _options = options ?? new LootOptions();
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();

            // 判断记录：conditionSchema 未显式提供时默认 RulesExprSchema.Base（见
            // LootTableParser 判断记录"不硬编码 Base，只作默认值"）——组装层需要 loot 条件引用
            // world/quest/player 分组时应显式传入 RulesExprSchema.Compose(...) 的结果。
            foreach (var record in registry.GetAll(LootSchemas.Table.Name))
            {
                var def = LootTableParser.Parse(record, conditionSchema);
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
            if (!_tables.TryGetValue(tableId, out var def))
            {
                throw new ArgumentException($"未知的掉落表 \"{tableId}\"", nameof(tableId));
            }

            var exprHost = _exprHostFactory.CreateFor(context.KillerId ?? context.SourceUnitId, context.SourceUnitId, null);

            var raw = new List<(Id TemplateId, int Count)>();
            RollTableInto(def, context, exprHost, raw, depth: 0);

            var merged = Merge(raw);
            _bus.Enqueue(new LootRolledEvent(tableId, context.ContextId, merged));
            return merged;
        }

        /// <summary><see cref="ILootRoller"/> 落地：委托到 <see cref="Roll(Id, RollContext)"/>（见
        /// <c>ILootRoller</c> 类型注释）。</summary>
        public IReadOnlyList<ItemStack> Roll(Id lootTableId, Id sourceUnitId, Id? killerId) =>
            Roll(lootTableId, new RollContext(sourceUnitId, killerId));

        // -----------------------------------------------------------------
        // 抽取核心
        // -----------------------------------------------------------------

        private void RollTableInto(LootTableDef def, RollContext context, IExprHost exprHost, List<(Id, int)> output, int depth)
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
                    foreach (var entry in group.Entries)
                    {
                        if (ConditionPasses(entry, exprHost))
                        {
                            candidatePool.Add(entry);
                        }
                    }
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

        private int RollChanceEachGroup(LootTableDef def, int groupIndex, LootGroup group, RollContext context, IExprHost exprHost, int depth, List<(Id, int)> output)
        {
            var produced = 0;
            for (var ei = 0; ei < group.Entries.Count; ei++)
            {
                var entry = group.Entries[ei];
                if (!ConditionPasses(entry, exprHost))
                {
                    continue;
                }

                var baseChance = Clamp01(entry.WeightOrChance * context.Multiplier);
                var effectiveChance = baseChance;
                string? pseudoKey = null;

                if (_options.PseudoRandom)
                {
                    pseudoKey = $"{context.ContextId}|{def.Id}|{groupIndex}|{ei}";
                    var streak = _missStreaks.TryGetValue(pseudoKey, out var s) ? s : 0;
                    effectiveChance = Clamp01(baseChance * (1 + streak * _options.PseudoRandomStep));
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

        private int RollWeightedGroup(IReadOnlyList<LootEntry> entries, int pickCount, RollContext context, IExprHost exprHost, int depth, List<(Id, int)> output)
        {
            var pool = new List<LootEntry>();
            foreach (var entry in entries)
            {
                if (ConditionPasses(entry, exprHost))
                {
                    pool.Add(entry);
                }
            }

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
            var totalWeight = pool.Sum(e => e.WeightOrChance);
            if (totalWeight <= 0)
            {
                return null;
            }

            var threshold = _rng.Next(_options.RngStream) * totalWeight;
            var cumulative = 0.0;
            foreach (var entry in pool)
            {
                cumulative += entry.WeightOrChance;
                if (threshold < cumulative)
                {
                    return entry;
                }
            }

            // 浮点误差兜底：理论上不可达（cumulative 最终等于 totalWeight > threshold），保留防御分支。
            return pool[pool.Count - 1];
        }

        private int RollCount(LootEntry entry) =>
            entry.CountMin == entry.CountMax ? entry.CountMin : _rng.NextInt(_options.RngStream, entry.CountMin, entry.CountMax);

        private bool ConditionPasses(LootEntry entry, IExprHost exprHost) =>
            entry.Condition == null || ExprEvaluator.EvaluateBool(entry.Condition, exprHost, _diagnostics);

        /// <summary>把一条候选解析为具体产出：<paramref name="entry"/>.Ref 是 <c>item.*</c> 时直接
        /// 追加一条 <c>(templateId, count)</c>；是 <c>loot.*</c> 时按判断记录 2 递归展开该嵌套表
        /// <paramref name="count"/> 次。</summary>
        private void ResolveEntryAtDepth(LootEntry entry, int count, RollContext context, IExprHost exprHost, int depth, List<(Id, int)> output)
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
                output.Add((entry.Ref, count));
            }
        }

        private static IReadOnlyList<ItemStack> Merge(List<(Id TemplateId, int Count)> raw)
        {
            var order = new List<Id>();
            var totals = new Dictionary<Id, int>();
            foreach (var (templateId, count) in raw)
            {
                if (count <= 0)
                {
                    continue;
                }

                if (!totals.ContainsKey(templateId))
                {
                    order.Add(templateId);
                    totals[templateId] = 0;
                }

                totals[templateId] += count;
            }

            var result = new List<ItemStack>(order.Count);
            foreach (var templateId in order)
            {
                result.Add(new ItemStack(templateId, totals[templateId]));
            }

            return result;
        }

        private static double Clamp01(double value) => value < 0 ? 0 : (value > 1 ? 1 : value);

        // -----------------------------------------------------------------
        // Drop / PickUp
        // -----------------------------------------------------------------

        /// <summary>生成一个地面掉落物实体（见 08 第 1.2 节、05 第 1.6 节）。</summary>
        public Id Drop(Id mapId, Vec2 position, IReadOnlyList<ItemStack> items, Id? ownerHint = null)
        {
            var id = _world.AllocateEntityId(EntityKinds.Loot);
            double? expireAt = _options.DefaultLifetime > 0 ? _simTimeProvider() + _options.DefaultLifetime : (double?)null;

            var entity = new DroppedLootEntity(id, mapId, items ?? Array.Empty<ItemStack>(), ownerHint, expireAt)
            {
                Position = position,
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
                for (var i = 0; i < want.Count; i++)
                {
                    if (addedPerStack[i] > 0)
                    {
                        RollbackAdd(unitId, want[i].TemplateId, addedPerStack[i]);
                    }
                }

                return LootPickupResult.Fail(LootPickupFailureReason.Rejected);
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
        /// </summary>
        public void RestoreDropped(DroppedLootEntity entity)
        {
            if (_world.GetEntity(entity.EntityId) is DroppedLootEntity existing)
            {
                existing.Position = entity.Position;
                existing.Items.Clear();
                existing.Items.AddRange(entity.Items);
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

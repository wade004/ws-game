using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SaveSystem;
using Core.Gameplay.Common;
using Core.Rules.Common;
using Core.Rules.ExprHost;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// <see cref="IAchievementHost"/> 的默认（唯一）实现（见 08 第 6、9 节）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性解析 <c>achv.def</c>，把每条 <c>criteria[].filter</c>
    /// 解析成 <see cref="ExprNode"/>（惯例同 <c>core/rules/ai</c> 的 <c>AiHost</c>：<c>exprSchema</c>
    /// 可选注入，默认 <see cref="RulesExprSchema.Base"/>），并按全部登记成就出现过的
    /// <c>observe_event</c> 去重后逐个 <see cref="IEventBus.Subscribe(Id, EventHandler)"/>，处理器
    /// 统一是 <see cref="Evaluate"/> 本身。之后只读（构造期之外不再查询 registry）。
    /// 同时实现 <see cref="IPersistable"/>（存档段 <see cref="SaveSections.PlayerAchievementState"/>，
    /// 见 10 第 2.2 节 <c>achievement_state: Map&lt;Id, AchievementProgress&gt;</c>——该字段的
    /// <c>Id</c> 维度是成就 id，隐含"这是玩家自己的进度"，本类型运行期按 (unitId, achievementId)
    /// 维护进度以支持"非玩家单位触发的 criterion"这一更一般的场景（见
    /// <see cref="AchievementCriterion"/> 判断记录），但 <see cref="Save"/>/<see cref="Load"/> 只
    /// 落盘 <see cref="AchievementOptions.PlayerUnitResolver"/> 解析出的那一个单位的进度，与 10
    /// 文档字段定义保持一致）。
    /// </summary>
    public sealed class AchievementHost : IAchievementHost, IPersistable
    {
        private sealed class CompiledCriterion
        {
            public CriterionType Type;
            public Id ObserveEvent;
            public Id? TargetRef;
            public int Count;
            public ExprNode? FilterNode;
        }

        private sealed class CompiledAchievement
        {
            public Id Id;
            public List<CompiledCriterion> Criteria = new List<CompiledCriterion>();
            public RewardBundle Rewards = RewardBundle.Empty;
        }

        private readonly SortedDictionary<string, CompiledAchievement> _achievements =
            new SortedDictionary<string, CompiledAchievement>(StringComparer.Ordinal);

        // (unitId, achievementId) -> 每条 criterion 当前累计值，数组下标对应 CompiledAchievement.Criteria 的顺序。
        private readonly Dictionary<(string UnitId, string AchievementId), int[]> _progress =
            new Dictionary<(string, string), int[]>();

        private readonly HashSet<(string UnitId, string AchievementId)> _unlocked =
            new HashSet<(string, string)>();

        /// <summary>C04 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：达成
        /// 条件已满足（<see cref="IsFullyAchieved"/> 为真）、但 <see cref="_rewardDispatcher"/>.
        /// <c>Grant</c> 失败（如背包已满，<c>InventoryFullPolicy.Reject</c>）的成就——此前实现先写
        /// <see cref="_unlocked"/> 再调用 <c>Grant</c> 且不检查返回值，发奖失败时成就已经判定解锁、
        /// 奖励却一件没发，玩家没有任何补领入口。见 <see cref="ApplyProgress"/>/<see
        /// cref="RetryPendingRewards"/> 判断记录。</summary>
        private readonly HashSet<(string UnitId, string AchievementId)> _pendingReward =
            new HashSet<(string, string)>();

        private readonly IEventBus _bus;
        private readonly IUnitAccess _units;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IRewardDispatcher _rewardDispatcher;
        private readonly AchievementOptions _options;
        private readonly IExprDiagnostics _diagnostics;

        /// <summary>
        /// 契约缺口判断记录（<c>exprSchema</c> 默认值）：并行开发中 <c>core/rules/expr_host</c> 的
        /// <c>RulesExprSchema</c> 经 ADR-0015 严格化改造后，<see cref="RulesExprSchema.Base"/> 不再
        /// 对 <c>event</c>/<c>world</c>/<c>quest</c>/<c>player</c> 四个分组做任何"未登记 key 放行"，
        /// 只登记 <c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/<c>time</c> 五个分组
        /// （见该类型判断记录）。<see cref="CriterionType.CustomEvent"/> 的 <c>filter</c> 若要引用
        /// <c>event.&lt;field&gt;</c>（08 第 6.1 节最自然的用法——"匹配条件"通常就是在读触发事件的
        /// 字段），必须由调用方经 <see cref="RulesExprSchema.Compose"/> 传入一份额外登记了具体
        /// <c>event.&lt;field&gt;</c> 签名的 <see cref="IExprSchema"/>（字段名随被观察的具体事件类型
        /// 而变，本模块无法预先穷举，不属于"本模块新增 group.key"的范畴，因此本模块不提供
        /// 类似 <c>WorldExprSchemaEntries</c> 的静态登记类）；不传时默认 <see cref="RulesExprSchema.Base"/>，
        /// filter 只能安全引用 <c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/<c>time</c>
        /// 五个分组，<c>event.&lt;field&gt;</c> 会被 <see cref="ExprParser"/> 按 Id 字面量解析，
        /// 求值时不会等于期望的事件字段值——这是并行任务（expr_host）交付边界之外的契约缺口，
        /// 本模块只能在此记录，见任务汇报"契约缺口"一节。
        /// </summary>
        public AchievementHost(
            IDataRegistryView registry,
            IEventBus bus,
            IUnitAccess units,
            IExprHostFactory exprHostFactory,
            IRewardDispatcher rewardDispatcher,
            AchievementOptions options,
            IExprSchema? exprSchema = null,
            IExprDiagnostics? diagnostics = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _rewardDispatcher = rewardDispatcher ?? throw new ArgumentNullException(nameof(rewardDispatcher));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            var schema = exprSchema ?? RulesExprSchema.Base;
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();

            var observedEvents = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in registry.GetAll(AchievementSchemas.Def.Name))
            {
                var def = AchievementDefinition.FromRecord(record);
                var compiled = new CompiledAchievement { Id = def.Id, Rewards = def.Rewards };

                foreach (var criterion in def.Criteria)
                {
                    compiled.Criteria.Add(new CompiledCriterion
                    {
                        Type = criterion.Type,
                        ObserveEvent = criterion.ObserveEvent,
                        TargetRef = criterion.TargetRef,
                        Count = criterion.Count,
                        FilterNode = criterion.FilterText != null ? ExprParser.Parse(criterion.FilterText, schema) : null,
                    });
                    observedEvents.Add(criterion.ObserveEvent.Value);
                }

                _achievements[def.Id.Value] = compiled;
            }

            foreach (var eventKeyText in observedEvents)
            {
                _bus.Subscribe(new Id(eventKeyText), Evaluate);
            }
        }

        // -----------------------------------------------------------------
        // IAchievementHost
        // -----------------------------------------------------------------

        public void Evaluate(IEvent evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            foreach (var achievement in _achievements.Values)
            {
                for (var i = 0; i < achievement.Criteria.Count; i++)
                {
                    var criterion = achievement.Criteria[i];
                    if (!criterion.ObserveEvent.Equals(evt.Key))
                    {
                        continue;
                    }

                    if (!TryMatch(criterion, evt, out var actorUnitId, out var increment))
                    {
                        continue;
                    }

                    if (criterion.FilterNode != null)
                    {
                        var host = _exprHostFactory.CreateFor(actorUnitId, null, evt);
                        if (!ExprEvaluator.EvaluateBool(criterion.FilterNode, host, _diagnostics))
                        {
                            continue;
                        }
                    }

                    ApplyProgress(achievement, i, actorUnitId, increment);
                }
            }
        }

        public IReadOnlyList<AchievementCriterionProgress> GetProgress(Id unitId, Id achievementId)
        {
            var achievement = RequireAchievement(achievementId);
            _progress.TryGetValue((unitId.Value, achievementId.Value), out var counts);

            var result = new AchievementCriterionProgress[achievement.Criteria.Count];
            for (var i = 0; i < achievement.Criteria.Count; i++)
            {
                var current = counts != null ? counts[i] : 0;
                result[i] = new AchievementCriterionProgress(current, achievement.Criteria[i].Count);
            }
            return result;
        }

        public bool IsUnlocked(Id unitId, Id achievementId)
        {
            RequireAchievement(achievementId);
            return _unlocked.Contains((unitId.Value, achievementId.Value));
        }

        /// <summary>
        /// C04 根治：重试全部处于"达成条件已满足但奖励此前未发放成功"状态（见 <see
        /// cref="_pendingReward"/>）的成就——供调用方在推断发放前置条件已恢复后（如清理背包空间）
        /// 主动调用；只对 <paramref name="unitId"/> 生效。幂等：一旦某条成就的 <see
        /// cref="IRewardDispatcher.Grant"/> 调用成功，立即从 <see cref="_pendingReward"/> 移出、并入
        /// <see cref="_unlocked"/>（<see cref="IsUnlocked"/> 从此对它返回 true），不会被下一次
        /// <see cref="RetryPendingRewards"/> 调用重复发放；仍然失败的保持 pending，可反复调用直到
        /// 成功。返回本次调用真正转为解锁状态的成就 id 列表（可能为空），供调用方按需展示"补领
        /// 成功"提示，不强制消费。
        /// </summary>
        public IReadOnlyList<Id> RetryPendingRewards(Id unitId)
        {
            var succeeded = new List<Id>();
            foreach (var achievement in _achievements.Values)
            {
                var key = (unitId.Value, achievement.Id.Value);
                if (!_pendingReward.Contains(key))
                {
                    continue;
                }

                if (!achievement.Rewards.IsEmpty && !_rewardDispatcher.Grant(unitId, achievement.Rewards, achievement.Id))
                {
                    continue;
                }

                _pendingReward.Remove(key);
                _unlocked.Add(key);
                succeeded.Add(achievement.Id);
                _bus.PublishImmediate(new AchievementUnlockedEvent(achievement.Id, unitId));
            }

            return succeeded;
        }

        // -----------------------------------------------------------------
        // 匹配规则（08 第 6.1 节六种类型 + 任务书拍板落地字段来源）
        // -----------------------------------------------------------------

        /// <summary>按 <paramref name="criterion"/> 的类型判断 <paramref name="evt"/> 是否命中，
        /// 命中时给出本次应记进度的单位（<paramref name="actorUnitId"/>）与增量
        /// （<paramref name="increment"/>）。字段一律经 <see cref="IExprReadableEvent.TryGetField"/>
        /// 按名字读取而不是转换成具体事件类型——本模块不假设 <c>quest.turned_in</c>/
        /// <c>area.trigger_entered</c> 对应的具体事件类型已经存在于本次编译（这两个事件由并行开发的
        /// quest/area_trigger 模块定义），只依赖 found.event_catalog 登记的字段名字与
        /// <see cref="IExprReadableEvent"/> 这一通用读取协议，天然与事件的具体实现类型解耦。</summary>
        private bool TryMatch(CompiledCriterion criterion, IEvent evt, out Id actorUnitId, out int increment)
        {
            actorUnitId = default;
            increment = 0;

            switch (criterion.Type)
            {
                case CriterionType.KillCount:
                {
                    // unit.died {unitId, killerId}：killerId 可能缺失（环境死亡，见 UnitDiedEvent
                    // 判断记录），此时不计入任何成就的击杀计数。
                    if (!TryGetIdField(evt, "unitId", out var diedUnitId)) return false;
                    if (!TryGetIdField(evt, "killerId", out var killerId)) return false;
                    if (!killerId.Equals(_options.PlayerUnitResolver())) return false;
                    if (!criterion.TargetRef.HasValue) return false;
                    var templateId = _units.GetTemplateId(diedUnitId);
                    if (!templateId.HasValue || !templateId.Value.Equals(criterion.TargetRef.Value)) return false;

                    actorUnitId = killerId;
                    increment = 1;
                    return true;
                }

                case CriterionType.CollectCount:
                {
                    // item.added {unitId, itemInstanceId, itemTemplateId, count}
                    if (!criterion.TargetRef.HasValue) return false;
                    if (!TryGetIdField(evt, "itemTemplateId", out var itemTemplateId)) return false;
                    if (!itemTemplateId.Equals(criterion.TargetRef.Value)) return false;
                    if (!TryGetIdField(evt, "unitId", out var unitId)) return false;
                    if (!TryGetIntField(evt, "count", out var count)) count = 1;

                    actorUnitId = unitId;
                    increment = (int)count;
                    return increment > 0;
                }

                case CriterionType.QuestComplete:
                {
                    // quest.turned_in {unitId, questId}
                    if (!criterion.TargetRef.HasValue) return false;
                    if (!TryGetIdField(evt, "questId", out var questId)) return false;
                    if (!questId.Equals(criterion.TargetRef.Value)) return false;
                    if (!TryGetIdField(evt, "unitId", out var unitId)) return false;

                    actorUnitId = unitId;
                    increment = 1;
                    return true;
                }

                case CriterionType.ReachArea:
                {
                    // area.trigger_entered {triggerId, unitId}
                    if (!criterion.TargetRef.HasValue) return false;
                    if (!TryGetIdField(evt, "triggerId", out var triggerId)) return false;
                    if (!triggerId.Equals(criterion.TargetRef.Value)) return false;
                    if (!TryGetIdField(evt, "unitId", out var unitId)) return false;

                    actorUnitId = unitId;
                    increment = 1;
                    return true;
                }

                case CriterionType.CastCount:
                {
                    // skill.cast_success {casterId, skillId, targets}
                    if (!criterion.TargetRef.HasValue) return false;
                    if (!TryGetIdField(evt, "skillId", out var skillId)) return false;
                    if (!skillId.Equals(criterion.TargetRef.Value)) return false;
                    if (!TryGetIdField(evt, "casterId", out var casterId)) return false;

                    actorUnitId = casterId;
                    increment = 1;
                    return true;
                }

                case CriterionType.CustomEvent:
                {
                    // 08 第 6.1 节"以上四类不能覆盖的情形"：基础匹配即"事件 key 相同"（已由调用方
                    // 保证），实际筛选交给 criterion.FilterNode；宿主固定绑定玩家单位（任务书
                    // "宿主 CreateFor(player, null, evt)"）。
                    actorUnitId = _options.PlayerUnitResolver();
                    increment = 1;
                    return true;
                }

                default:
                    return false;
            }
        }

        private static bool TryGetIdField(IEvent evt, string field, out Id value)
        {
            if (evt is IExprReadableEvent readable && readable.TryGetField(field, out var exprValue) && exprValue.Kind == ExprValueKind.Id)
            {
                value = exprValue.AsId;
                return true;
            }
            value = default;
            return false;
        }

        private static bool TryGetIntField(IEvent evt, string field, out long value)
        {
            if (evt is IExprReadableEvent readable && readable.TryGetField(field, out var exprValue) && exprValue.Kind == ExprValueKind.Int)
            {
                value = exprValue.AsInt;
                return true;
            }
            value = default;
            return false;
        }

        // -----------------------------------------------------------------
        // 进度累计 / 解锁
        // -----------------------------------------------------------------

        private void ApplyProgress(CompiledAchievement achievement, int criterionIndex, Id unitId, int increment)
        {
            var key = (unitId.Value, achievement.Id.Value);
            if (_unlocked.Contains(key))
            {
                // 08 第 6.1 节"解锁（一次性）"：已解锁的成就不再累计任何进度、不再重复发奖励。
                return;
            }

            if (!_progress.TryGetValue(key, out var counts))
            {
                counts = new int[achievement.Criteria.Count];
                _progress[key] = counts;
            }

            var target = achievement.Criteria[criterionIndex].Count;
            if (counts[criterionIndex] >= target)
            {
                // 该条 criterion 早已达标，同类事件继续发生时不再重复累计、不再重复发 progressed。
                return;
            }

            counts[criterionIndex] = Math.Min(counts[criterionIndex] + increment, target);
            _bus.PublishImmediate(new AchievementProgressedEvent(achievement.Id, unitId, counts[criterionIndex], target));

            // C04 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：此前先把 key
            // 写进 _unlocked、再调用 Grant 且不检查返回值——发奖失败（如背包已满）时成就已经判定
            // 解锁、奖励却一件没发，玩家没有任何补领入口。改为"发奖成功后再提交 _unlocked 终态"：
            // Grant 失败时不写 _unlocked、不发 AchievementUnlockedEvent，改记入 _pendingReward（见
            // 该字段判断记录）。与 EncounterHost.Evaluate 的"下次调用自然重试"不同——本类型没有
            // 天然的周期性重新求值入口（criterion 计数已经打满，同一 criterion 的后续事件会在本方法
            // 顶部提前 return，不会再次落到这里），因此需要一份可持久化的显式 pending 状态与
            // RetryPendingRewards 这一显式重试入口，而不是依赖"下次事件自动重来"。
            if (IsFullyAchieved(achievement, counts))
            {
                if (achievement.Rewards.IsEmpty || _rewardDispatcher.Grant(unitId, achievement.Rewards, achievement.Id))
                {
                    _unlocked.Add(key);
                    _bus.PublishImmediate(new AchievementUnlockedEvent(achievement.Id, unitId));
                }
                else
                {
                    _pendingReward.Add(key);
                }
            }
        }

        private static bool IsFullyAchieved(CompiledAchievement achievement, int[] counts)
        {
            for (var i = 0; i < achievement.Criteria.Count; i++)
            {
                if (counts[i] < achievement.Criteria[i].Count)
                {
                    return false;
                }
            }
            return true;
        }

        private CompiledAchievement RequireAchievement(Id achievementId)
        {
            if (_achievements.TryGetValue(achievementId.Value, out var achievement))
            {
                return achievement;
            }
            throw new ArgumentException($"未知的 achv.def \"{achievementId}\"", nameof(achievementId));
        }

        // -----------------------------------------------------------------
        // IPersistable（存档段 player.achievement_state，见 10 第 2.2 节）
        // -----------------------------------------------------------------

        public string SectionKey => SaveSections.PlayerAchievementState;

        public JsonValue Save()
        {
            var player = _options.PlayerUnitResolver();
            var builder = new JsonObjectBuilder();

            foreach (var achievement in _achievements.Values)
            {
                var key = (player.Value, achievement.Id.Value);
                var hasProgress = _progress.TryGetValue(key, out var counts);
                var unlocked = _unlocked.Contains(key);
                var pendingReward = _pendingReward.Contains(key);
                if (!hasProgress && !unlocked && !pendingReward)
                {
                    continue;
                }

                var currentArray = new List<JsonValue>(achievement.Criteria.Count);
                for (var i = 0; i < achievement.Criteria.Count; i++)
                {
                    currentArray.Add(new JsonNumber(hasProgress ? counts![i] : 0));
                }

                // C04 根治：pending_reward 字段随存档持久化——见 <see cref="_pendingReward"/>/
                // <see cref="RetryPendingRewards"/> 判断记录，保证读档后仍能识别出"条件已满足、
                // 奖励还没领到"的成就，供玩家/调用方之后继续重试补领，而不是这次会话结束就丢失
                // 这个待领奖标记。
                var entry = new JsonObjectBuilder()
                    .Add("unlocked", unlocked ? JsonBool.True : JsonBool.False)
                    .Add("current", new JsonArray(currentArray))
                    .Add("pending_reward", pendingReward ? JsonBool.True : JsonBool.False)
                    .Build();

                builder.Add(achievement.Id.Value, entry);
            }

            return builder.Build();
        }

        /// <summary>
        /// CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：修复前本方法开头
        /// 无条件清空该玩家既有的进度/解锁/待领奖记录，随后才校验 <paramref name="data"/> 的 JSON
        /// 形状；坏 shape（<c>data</c> 本身不是 JSON 对象，或某个成就条目 <c>kv.Value</c> 不是 JSON
        /// 对象）会在清空之后才抛 <see cref="FormatException"/>——此时该玩家的成就状态已经丢失，且
        /// 逐条目提交也不是原子的：一次 <see cref="Load"/> 调用中排在坏条目之前的成就已经写入了
        /// "本次读档的新值"，排在坏条目之后的成就完全没处理，形成"半新半旧"的中间态，与
        /// <c>EquipmentPersistable.Load</c> 曾经的同一类缺陷成因相同（见 <c>ItemPersistable.cs</c>
        /// <c>EquipmentPersistable.Load</c> 判断记录）。
        /// <para>
        /// 根治方式：遵循 <see cref="IPersistable.Load"/> 契约注释确立的"先解析校验成临时恢复计划、
        /// 再一次性提交"——先完整遍历 <paramref name="data"/> 校验全部条目的形状（不触碰
        /// <see cref="_progress"/>/<see cref="_unlocked"/>/<see cref="_pendingReward"/> 任何一个），
        /// 只有整份数据校验通过才清空该玩家既有记录并按解析结果一次性提交；校验期间遇到任何一条
        /// 坏形状，直接抛异常返回，读档前的运行期状态原样保留，不会出现半新半旧的中间态。
        /// </para>
        /// </summary>
        public void Load(JsonValue data)
        {
            var player = _options.PlayerUnitResolver();

            if (data is JsonNull)
            {
                // 整段缺失：清空到"从未记录过"的默认态。这个分支不需要先解析（没有数据可解析），
                // 直接清空即是完整语义，不存在"清到一半又失败"的风险。
                foreach (var achievement in _achievements.Values)
                {
                    var key = (player.Value, achievement.Id.Value);
                    _progress.Remove(key);
                    _unlocked.Remove(key);
                    _pendingReward.Remove(key);
                }

                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException($"player.achievement_state 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            // 第一遍：只解析校验，不提交任何状态——遇到坏形状条目直接抛异常，此时既有运行期状态
            // 完全未被触碰。
            var plan = new List<((string UnitId, string AchievementId) Key, int[] Counts, bool Unlocked, bool PendingReward)>();
            foreach (var kv in obj)
            {
                if (!_achievements.TryGetValue(kv.Key, out var achievement))
                {
                    // 存档引用了当前内容集里已不存在的成就 id（内容变更）：忽略，不阻断读档。
                    continue;
                }

                if (!(kv.Value is JsonObject entry))
                {
                    throw new FormatException($"player.achievement_state[\"{kv.Key}\"] 不是 JSON 对象");
                }

                var unlocked = entry.TryGetValue("unlocked", out var unlockedVal) && unlockedVal is JsonBool ub && ub.Value;
                var pendingReward = entry.TryGetValue("pending_reward", out var pendingVal) && pendingVal is JsonBool pb && pb.Value;

                var counts = new int[achievement.Criteria.Count];
                if (entry.TryGetValue("current", out var currentVal) && currentVal is JsonArray arr)
                {
                    for (var i = 0; i < counts.Length && i < arr.Count; i++)
                    {
                        if (arr[i] is JsonNumber n && n.TryGetInt64(out var v))
                        {
                            counts[i] = (int)v;
                        }
                    }
                }

                plan.Add(((player.Value, achievement.Id.Value), counts, unlocked, pendingReward));
            }

            // 第二遍：全部条目校验通过，一次性提交——先清空该玩家既有记录（不触碰其它单位，见本
            // 类型顶部判断记录），再按计划写入。
            foreach (var achievement in _achievements.Values)
            {
                var key = (player.Value, achievement.Id.Value);
                _progress.Remove(key);
                _unlocked.Remove(key);
                _pendingReward.Remove(key);
            }

            foreach (var entry in plan)
            {
                _progress[entry.Key] = entry.Counts;
                if (entry.Unlocked)
                {
                    _unlocked.Add(entry.Key);
                }
                else if (entry.PendingReward)
                {
                    // C04 根治：unlocked 与 pending_reward 互斥（见 Save 侧写入逻辑——只有 Grant
                    // 成功那一刻才会同时写 unlocked=true，此时不会再落入 _pendingReward），old 存档
                    // 若两者都为 true（理论上不应发生，防御性处理）以 unlocked 优先，不重复放进
                    // pending 集合。
                    _pendingReward.Add(entry.Key);
                }
            }

            // 读档不是一次业务事件（同 WorldState/DifficultyHost 判断记录）：不重发
            // achievement.progressed/unlocked，也不重新调用 IRewardDispatcher.Grant。
        }
    }
}

using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Foundation.Expr;
using Core.Foundation.SaveSystem;
using Core.Gameplay.WorldState;
using Core.Rules.Common;

namespace Core.Gameplay.Spawn
{
    /// <summary>
    /// <see cref="ISpawnHost"/> 的默认实现（见 05_对象模型与世界.md 第 5、5.1、5.2、5.3 节）。同时
    /// 实现 <see cref="IPersistable"/>（存档段 <c>"spawn_state"</c>，见 10_存档与持久化.md 第 2.3 节
    /// <c>spawn_state</c> 行、任务书拍板"记录 once 标志由 world_state 持久化，本段只存 timer 剩余与
    /// 计数"）。
    /// <para>
    /// 判断记录：<c>content_ref</c> 域名为 <c>creature</c> 时直接依赖
    /// <see cref="ICreatureFactory"/>（L3 <c>core/carriers/common</c> 契约，已在其自身注释里点名
    /// "<c>core/gameplay/spawn</c>……可复用本接口生成生物实体"）；域名为 <c>gobj</c> 时改用
    /// <see cref="SpawnOptions.GobjSpawner"/> 委托而不是直接引用
    /// <c>Core.Carriers.Gobj.GameObjectFactory</c>——该类型自身注释明确"不对外暴露为 common 契约
    /// 接口"，任务书拍板"两者二选一"正是为此：能复用既有契约的直接依赖，不能的改用委托注入，
    /// 避免新增一条 core/carriers/gobj 的编译期依赖。
    /// </para>
    /// <para>
    /// <c>condition</c> 文本解析复用与 <c>Core.Gameplay.AreaTrigger.AreaTriggerHost</c> 相同的
    /// <see cref="ConditionSchema"/>（<c>RulesExprSchema.Compose</c> + <c>WorldExprSchemaEntries</c>），
    /// 见该类型判断记录。
    /// </para>
    /// </summary>
    public sealed class SpawnHost : ISpawnHost, IPersistable
    {
        /// <summary>见 <c>Core.Gameplay.AreaTrigger.AreaTriggerHost.ConditionSchema</c> 同款判断记录。</summary>
        private static readonly IExprSchema ConditionSchema =
            Core.Rules.ExprHost.RulesExprSchema.Compose(WorldExprSchemaEntries.BuildStandalone());

        /// <summary>见 <see cref="SpawnOptions.PlayerUnitResolver"/> 判断记录："玩家可空"与
        /// <c>IExprHostFactory.CreateFor</c> 的 selfId 不可空之间的契约缺口——玩家缺席时用本占位 Id
        /// 代替，不对应任何真实单位，<c>self</c> 分组查询会按"缺失 -&gt; 默认值"处理（04 第 6.3、6.4
        /// 节），<c>world</c> 分组查询不受影响（不读 selfId）。</summary>
        private static readonly Id NoPlayerSentinel = new Id("spawn.no_player_sentinel");

        private sealed class RuntimeState
        {
            public Id? EntityId;
            public double? RespawnRemaining;
            public int SpawnCount;
        }

        private readonly IDataRegistryView _data;
        private readonly Core.Gameplay.WorldState.IWorldState _worldState;
        private readonly ICreatureFactory _creatureFactory;
        private readonly IEventBus _bus;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly SpawnOptions _options;
        private readonly ISpawnDiagnostics _diagnostics;

        private readonly Dictionary<Id, RuntimeState> _records = new Dictionary<Id, RuntimeState>();
        private readonly Dictionary<Id, Id> _entityToSpawn = new Dictionary<Id, Id>();
        private readonly HashSet<Id> _loadedMaps = new HashSet<Id>();

        public SpawnHost(
            IDataRegistryView data,
            Core.Gameplay.WorldState.IWorldState worldState,
            ICreatureFactory creatureFactory,
            IEventBus bus,
            IExprHostFactory exprHostFactory,
            SpawnOptions? options = null,
            ISpawnDiagnostics? diagnostics = null)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _creatureFactory = creatureFactory ?? throw new ArgumentNullException(nameof(creatureFactory));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _options = options ?? new SpawnOptions();
            _diagnostics = diagnostics ?? new InMemorySpawnDiagnostics();

            // 见 05 第 5.3 节判断记录（任务书拍板）："只处理 creature.despawned；死亡实体的销毁由
            // 上层/L3 决定"——gobj 侧无对应的 gobj.despawned 事件（见 GameObjectFactory.Despawn 判断
            // 记录），需要 gobj 内容的调用方自行调 NotifyDespawn。
            _bus.Subscribe<CreatureDespawnedEvent>(CarriersEventKeys.CreatureDespawned,
                evt => NotifyDespawn(evt.EntityId, evt.Reason));
        }

        // -----------------------------------------------------------------
        // ISpawnHost
        // -----------------------------------------------------------------

        public IReadOnlyList<Id> ApplyForMap(Id mapId)
        {
            _loadedMaps.Add(mapId);
            var generated = new List<Id>();

            foreach (var record in _data.GetAll(SpawnSchemas.Table.Name))
            {
                SpawnTableDef def;
                try
                {
                    def = SpawnTableDef.FromRecord(record);
                }
                catch (DataFieldException ex)
                {
                    _diagnostics.Error($"spawn.table 记录 \"{record.Key}\" 解析失败，跳过：{ex.Message}");
                    continue;
                }

                if (!def.MapId.Equals(mapId))
                {
                    continue;
                }

                var runtime = GetOrCreateRuntime(def.Id);

                if (def.RespawnPolicy == RespawnPolicy.Never)
                {
                    // 05 第 5.2 节：数据登记但不自动生成。
                    continue;
                }

                if (!ConditionPasses(def))
                {
                    continue;
                }

                switch (def.RespawnPolicy)
                {
                    case RespawnPolicy.Once:
                        if (HasOnceDone(def.Id))
                        {
                            continue;
                        }

                        var onceEntity = SpawnEntity(def, runtime);
                        if (onceEntity.HasValue)
                        {
                            MarkOnceDone(def.Id);
                            generated.Add(onceEntity.Value);
                        }

                        break;

                    case RespawnPolicy.OnMapEnter:
                        if (runtime.EntityId.HasValue)
                        {
                            continue;
                        }

                        var enterEntity = SpawnEntity(def, runtime);
                        if (enterEntity.HasValue)
                        {
                            generated.Add(enterEntity.Value);
                        }

                        break;

                    case RespawnPolicy.Timer:
                        if (runtime.EntityId.HasValue)
                        {
                            continue;
                        }

                        if (runtime.RespawnRemaining.HasValue && runtime.RespawnRemaining.Value > 0)
                        {
                            continue;
                        }

                        var timerEntity = SpawnEntity(def, runtime);
                        if (timerEntity.HasValue)
                        {
                            generated.Add(timerEntity.Value);
                        }

                        break;
                }
            }

            return generated;
        }

        public void NotifyDespawn(Id entityId, string reason)
        {
            if (!_entityToSpawn.TryGetValue(entityId, out var spawnId))
            {
                return;
            }

            _entityToSpawn.Remove(entityId);

            if (!_records.TryGetValue(spawnId, out var runtime))
            {
                return;
            }

            runtime.EntityId = null;

            var record = _data.Get(SpawnSchemas.Table.Name, spawnId);
            if (record == null)
            {
                return;
            }

            var def = SpawnTableDef.FromRecord(record);
            if (def.RespawnPolicy == RespawnPolicy.Timer)
            {
                runtime.RespawnRemaining = def.RespawnTimer ?? 0;
            }
        }

        public SpawnRecord? GetSpawnRecord(Id spawnId)
        {
            if (!_records.TryGetValue(spawnId, out var runtime))
            {
                return null;
            }

            return new SpawnRecord(spawnId, runtime.EntityId, HasOnceDone(spawnId), runtime.RespawnRemaining, runtime.SpawnCount);
        }

        public void Update(double dt)
        {
            if (dt < 0)
            {
                throw new ArgumentException("dt 不能为负数", nameof(dt));
            }

            foreach (var kv in _records)
            {
                var spawnId = kv.Key;
                var runtime = kv.Value;

                if (!runtime.RespawnRemaining.HasValue || runtime.EntityId.HasValue)
                {
                    continue;
                }

                var remaining = runtime.RespawnRemaining.Value - dt;
                if (remaining > 0)
                {
                    runtime.RespawnRemaining = remaining;
                    continue;
                }

                runtime.RespawnRemaining = 0;

                var record = _data.Get(SpawnSchemas.Table.Name, spawnId);
                if (record == null)
                {
                    continue;
                }

                SpawnTableDef def;
                try
                {
                    def = SpawnTableDef.FromRecord(record);
                }
                catch (DataFieldException ex)
                {
                    _diagnostics.Error($"spawn.table 记录 \"{spawnId}\" 解析失败，跳过重生：{ex.Message}");
                    continue;
                }

                if (_loadedMaps.Contains(def.MapId) && ConditionPasses(def))
                {
                    SpawnEntity(def, runtime);
                }

                // 地图未加载或 condition 不满足：RespawnRemaining 保持 0，下次 ApplyForMap（进图）
                // 或 Update 时会重新判定（timer 分支同样以 "RespawnRemaining <= 0 且当前无实例"
                // 作为可重生条件，见 ApplyForMap timer 分支）。
            }
        }

        public void TriggerNever(Id spawnId)
        {
            var record = _data.Get(SpawnSchemas.Table.Name, spawnId)
                ?? throw new ArgumentException($"未知的刷新点：\"{spawnId}\"", nameof(spawnId));

            // README 判断记录 6（W2 收边补齐）：此前本方法对 SpawnTableDef.FromRecord 解析失败
            // 不捕获、直接向上抛出，与 ApplyForMap/Update/SpawnNow 三者"记诊断并跳过该条，不抛
            // 异常"的一贯处理方式不一致（11 第 4 节错误处理约定：数据解析失败按诊断降级，不假设
            // 调用方能处理异常）。"未知 spawnId"（上面的 ArgumentException）与"记录存在但字段
            // 非法"是两类不同性质的失败——前者是调用方传参错误，后者是数据内容问题——统一改动
            // 只覆盖后者，不改变前者的既有契约（ISpawnHost.SpawnNow 的文档注释仍以"去掉未知 id
            // 抛异常"这一点对比二者，该对比继续成立）。
            SpawnTableDef def;
            try
            {
                def = SpawnTableDef.FromRecord(record);
            }
            catch (DataFieldException ex)
            {
                _diagnostics.Error($"TriggerNever：刷新点 \"{spawnId}\" 解析失败，已跳过：{ex.Message}");
                return;
            }

            var runtime = GetOrCreateRuntime(spawnId);

            TrySpawnGuarded(def, runtime, "TriggerNever");
        }

        public IReadOnlyList<Id> SpawnNow(Id spawnId, Id mapId)
        {
            var record = _data.Get(SpawnSchemas.Table.Name, spawnId);
            if (record == null)
            {
                _diagnostics.Error($"SpawnNow：未知的刷新点 \"{spawnId}\"");
                return Array.Empty<Id>();
            }

            SpawnTableDef def;
            try
            {
                def = SpawnTableDef.FromRecord(record);
            }
            catch (DataFieldException ex)
            {
                _diagnostics.Error($"SpawnNow：刷新点 \"{spawnId}\" 解析失败：{ex.Message}");
                return Array.Empty<Id>();
            }

            if (!def.MapId.Equals(mapId))
            {
                _diagnostics.Error(
                    $"SpawnNow：刷新点 \"{spawnId}\" 所属地图 \"{def.MapId}\" 与请求地图 \"{mapId}\" 不符，已跳过生成");
                return Array.Empty<Id>();
            }

            var runtime = GetOrCreateRuntime(spawnId);
            var entityId = TrySpawnGuarded(def, runtime, "SpawnNow");
            return entityId.HasValue ? new[] { entityId.Value } : Array.Empty<Id>();
        }

        /// <summary>见 <see cref="TriggerNever"/>/<see cref="SpawnNow"/> 共用的"已有存活实例/
        /// condition 不满足则只记一条诊断、不生成"守卫逻辑。</summary>
        private Id? TrySpawnGuarded(SpawnTableDef def, RuntimeState runtime, string callerName)
        {
            if (runtime.EntityId.HasValue)
            {
                _diagnostics.Warn($"刷新点 \"{def.Id}\" 已有存活实例，{callerName} 被忽略");
                return null;
            }

            if (!ConditionPasses(def))
            {
                _diagnostics.Warn($"刷新点 \"{def.Id}\" condition 不满足，{callerName} 被忽略");
                return null;
            }

            return SpawnEntity(def, runtime);
        }

        public void UnloadMap(Id mapId)
        {
            _loadedMaps.Remove(mapId);

            foreach (var record in _data.GetAll(SpawnSchemas.Table.Name))
            {
                SpawnTableDef def;
                try
                {
                    def = SpawnTableDef.FromRecord(record);
                }
                catch (DataFieldException)
                {
                    continue;
                }

                if (!def.MapId.Equals(mapId))
                {
                    continue;
                }

                if (_records.TryGetValue(def.Id, out var runtime) && runtime.EntityId.HasValue)
                {
                    _entityToSpawn.Remove(runtime.EntityId.Value);
                    runtime.EntityId = null;
                }
            }
        }

        // -----------------------------------------------------------------
        // IPersistable（存档段 "spawn_state"，见 10 第 2.3 节；只存 timer 剩余与计数，见类型注释）
        // -----------------------------------------------------------------

        public string SectionKey => "spawn_state";

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            foreach (var kv in _records)
            {
                var runtime = kv.Value;
                if (runtime.SpawnCount == 0 && !runtime.RespawnRemaining.HasValue)
                {
                    continue;
                }

                var entry = new JsonObjectBuilder()
                    .Add("spawn_count", new JsonNumber(runtime.SpawnCount))
                    .Add("respawn_remaining", runtime.RespawnRemaining.HasValue
                        ? (JsonValue)new JsonNumber(runtime.RespawnRemaining.Value)
                        : JsonNull.Instance)
                    .Build();

                builder.Add(kv.Key.Value, entry);
            }

            return builder.Build();
        }

        /// <summary>
        /// 判断记录（N03 根治）：同图读档时，<c>GameplayAssembly.RestoreFromSlot</c> 判定"目标地图与
        /// 当前地图相同"就不切场景——玩家实体与全部世界实体（含本类之前经 <see cref="SpawnEntity"/>
        /// 生成的怪物/物件）都从未被销毁或重建，仍然原样存活在 <see cref="Core.Gameplay.WorldState"/>
        /// 里；<see cref="Load"/> 本身也没有、也不应该有销毁/重新生成实体的能力，它只回滚
        /// <c>spawn_count</c>/<c>respawn_remaining</c> 这类簿记数字（见类型注释"本段只存 timer 剩余
        /// 与计数"）。旧实现无条件 <c>_entityToSpawn.Clear()</c>，会让"读档动作本身完全没有触碰"的
        /// 存活实体凭空失去与刷新点的关联——该实体之后死亡/销毁时 <see cref="NotifyDespawn"/> 查不到
        /// 映射、直接提前返回，刷新点从此既不知道"自己已经没有实体"也不会重新计时/重生，永久卡死。
        /// <para>
        /// 修复：Load 前先记下当前每个刷新点对应的存活实体 id（<c>previouslyAlive</c>），按快照重建
        /// <c>_records</c> 之后，把这份"仍然存活"的映射重新接回去——快照里的 <c>respawn_remaining</c>
        /// 对这些刷新点不生效（实体明明还活着，不该有倒计时），<c>spawn_count</c> 取快照值，快照没有
        /// 提到该刷新点时（存活实体是读档所回滚到的那个时间点"之后"才生成的边界情形）仍然补建一条
        /// 记录、至少计数为 1，保证映射不丢失——不属于"跨图那样整批销毁重建"的语义（本类没有销毁
        /// 实体的能力，也不持有 <c>IWorldSim</c> 引用），是"尽量还原簿记、绝不丢失当前存活实体的
        /// 追踪"这一更保守的选择，见类型顶部 <c>ISpawnHost</c> 判断记录同款风格。
        /// </para>
        /// <para>
        /// C11 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：上一段"存活中，
        /// 不该有倒计时"的判断只对"快照本身也认为这个刷新点当前存活（没有记录 <c>respawn_remaining</c>）"
        /// 的情形成立。同图读档场景下（<c>GameplayAssembly.RestoreFromSlot</c>/<c>ShellHost.LoadGame</c>
        /// 判定目标地图与当前地图相同、不切场景，见 <c>ShellHost</c> 该方法判断记录）存在另一种可能：
        /// 存档那一刻这个刷新点其实是"已死亡、倒计时到一半"（快照里 <c>respawn_remaining</c> 有值），
        /// 但存档之后、读档之前，玩家在当前这局未重载的会话里眼看着它倒计时结束、重新生成了一个新
        /// 存活实体——此时 <c>previouslyAlive</c> 命中的是这个"存档时间点之后才诞生"的新实体，旧
        /// 实现无条件按上面的规则重绑并清空倒计时，等于让"当前世界恰好有个存活实体"这一事实覆盖了
        /// 快照明确记录的"应该是死亡计时中"这一权威状态——判断记录（拍板优先级，见 08 文档同款
        /// 判断记录）：<b>快照的倒计时优先于当前世界状态</b>，因为存档/读档的语义就是"把状态恢复到
        /// 保存那一刻"，若允许"读档前会话里发生的事"泄漏进读档后的状态，读档就不再是确定性的时间
        /// 回退。因此：<paramref name="data"/> 快照对某个刷新点记录了 <c>respawn_remaining</c> 时，
        /// 直接采用快照值、不重绑 <c>previouslyAlive</c> 里那个实体——本类没有销毁实体的能力（同上
        /// 一段判断记录），那个实体会变成不再被任何刷新点追踪的孤儿（不在 <c>_entityToSpawn</c>
        /// 里，它死亡/销毁时 <see cref="NotifyDespawn"/> 找不到映射会直接提前返回，不会误伤这个
        /// 刷新点接下来按快照倒计时重新生成的新实体）——这是"允许一个不受刷新点管理的多余实体短暂
        /// 存在于世界里，直到玩家下次离开重进这张地图（<see cref="UnloadMap"/> 不认 <c>EntityId</c>
        /// 只按 <c>spawn.table</c> 记录清空，不会清理这个孤儿，但该实体本身仍然是一个合法的
        /// <c>IWorldSim</c> 实体，不会导致空引用/异常）"与"违背存档语义、丢弃玩家读档想要拿回的
        /// 那份确定状态"之间，选择前者。
        /// </para>
        /// <para>
        /// R14 根治（architecture/落地计划/audit-5e779c6-20260907）：上一段"接受孤儿实体短暂存在"的
        /// 选择低估了后果——该刷新点按快照倒计时结束后会重新生成一个新实体（<see cref="SpawnEntity"/>），
        /// 而孤儿实体本身从未被销毁，二者同时存活，同一刷新点/同一模板在世界里出现两个实体，且孤儿
        /// 实体从此彻底脱离任何刷新点追踪，不属于"短暂存在"而是永久重复。既然 <see
        /// cref="_creatureFactory"/>（生物域）与 <see cref="SpawnOptions.GobjDespawner"/>（gobj 域）
        /// 都具备移除单个实体的能力，改为在这里主动移除孤儿实体（<see cref="DespawnOrphan"/>，原因
        /// <c>"restore_reconcile"</c>），而不是放任它留在世界里——移除走 <see
        /// cref="Core.Foundation.SimLoop.IWorldSim.MarkForDestruction"/>（<c>Despawn</c> 内部调用），
        /// 是"下个 tick 生命周期清理阶段真正移除"的惯例（同任何其它 Despawn 调用），不是立即同步移除；
        /// 该孤儿从未被写回 <see cref="_entityToSpawn"/>，随后它的 <c>creature.despawned</c>/等效
        /// gobj 事件即便被派发，<see cref="NotifyDespawn"/> 也查不到映射、直接提前返回，不会碰这个
        /// 刷新点刚从快照恢复的 <c>respawn_remaining</c>。
        /// </para>
        /// </summary>
        /// <summary>
        /// CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：修复前
        /// <c>_records.Clear()</c>/<c>_entityToSpawn.Clear()</c> 在校验 <paramref name="data"/> 形状
        /// 之前就已经无条件执行——坏 shape（<paramref name="data"/> 既不是 <see cref="JsonNull"/> 也
        /// 不是 <see cref="JsonObject"/>）会在清空之后才抛 <see cref="FormatException"/>，此时全部
        /// 刷新点的运行期记录（含存活实体绑定）已经丢失，与 <c>EquipmentPersistable.Load</c>/
        /// <c>AchievementHost.Load</c> 曾经的同一类缺陷成因相同。根治方式：把形状校验前移到任何
        /// 状态变更之前——本方法其余逻辑（<paramref name="previouslyAlive"/> 快照、清空、按快照
        /// 重建、孤儿实体处理）原样保留，只是现在只有形状确认合法之后才会开始执行。</summary>
        public void Load(JsonValue data)
        {
            if (!(data is JsonNull) && !(data is JsonObject))
            {
                throw new FormatException($"spawn_state 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            var previouslyAlive = new Dictionary<Id, Id>();
            foreach (var kv in _records)
            {
                if (kv.Value.EntityId.HasValue)
                {
                    previouslyAlive[kv.Key] = kv.Value.EntityId.Value;
                }
            }

            _records.Clear();
            _entityToSpawn.Clear();

            if (data is JsonObject obj)
            {
                foreach (var kv in obj)
                {
                    var runtime = new RuntimeState();
                    if (kv.Value is JsonObject entryObj)
                    {
                        if (entryObj.TryGetValue("spawn_count", out var scv) && scv is JsonNumber scn)
                        {
                            runtime.SpawnCount = (int)scn.Value;
                        }

                        if (entryObj.TryGetValue("respawn_remaining", out var rrv) && rrv is JsonNumber rrn)
                        {
                            runtime.RespawnRemaining = rrn.Value;
                        }
                    }

                    _records[new Id(kv.Key)] = runtime;
                }
            }

            foreach (var kv in previouslyAlive)
            {
                var spawnId = kv.Key;

                // C11 根治：快照对这个刷新点记录了倒计时——快照倒计时优先于"当前世界恰好还有个存活
                // 实体"这一事实（见本方法判断记录），不重绑，让 previouslyAlive 里的这个实体变成
                // 不受刷新点追踪的孤儿；_records[spawnId] 已经是快照原样写入的值（含
                // respawn_remaining），这里不需要、也不应该再碰它。
                if (_records.TryGetValue(spawnId, out var snapshotRuntime) && snapshotRuntime.RespawnRemaining.HasValue)
                {
                    // R14 根治：这个刷新点按快照会重新计时、重新生成——kv.Value 这个"存档之后才诞生"
                    // 的孤儿实体不能留在世界里，否则倒计时结束后会与新生成的实体同模板重复（见本方法
                    // 判断记录）。
                    DespawnOrphan(spawnId, kv.Value);
                    continue;
                }

                var runtime = GetOrCreateRuntime(spawnId);
                runtime.EntityId = kv.Value;
                runtime.RespawnRemaining = null; // 存活中，不该有倒计时（快照的旧倒计时已不适用）
                if (runtime.SpawnCount == 0)
                {
                    runtime.SpawnCount = 1;
                }

                _entityToSpawn[kv.Value] = spawnId;
            }
        }

        // -----------------------------------------------------------------
        // 内部实现
        // -----------------------------------------------------------------

        private RuntimeState GetOrCreateRuntime(Id spawnId)
        {
            if (!_records.TryGetValue(spawnId, out var runtime))
            {
                runtime = new RuntimeState();
                _records[spawnId] = runtime;
            }

            return runtime;
        }

        private Id? SpawnEntity(SpawnTableDef def, RuntimeState runtime)
        {
            Id entityId;
            var domain = def.ContentRef.Domain;

            if (domain == EntityKinds.Creature)
            {
                entityId = _creatureFactory.Spawn(def.ContentRef, def.MapId, def.Position, def.Facing);
            }
            else if (domain == EntityKinds.Gobj)
            {
                if (_options.GobjSpawner == null)
                {
                    _diagnostics.Warn($"刷新点 \"{def.Id}\" 的 content_ref 是 gobj.* 但未注入 SpawnOptions.GobjSpawner，跳过生成");
                    return null;
                }

                entityId = _options.GobjSpawner(def.ContentRef, def.MapId, def.Position, def.Facing);
            }
            else
            {
                _diagnostics.Error($"刷新点 \"{def.Id}\" 的 content_ref 域名非法：\"{domain}\"（只允许 creature|gobj）");
                return null;
            }

            runtime.EntityId = entityId;
            runtime.SpawnCount++;
            runtime.RespawnRemaining = null;
            _entityToSpawn[entityId] = def.Id;

            _bus.Enqueue(new SpawnExecutedEvent(def.Id, entityId));
            return entityId;
        }

        /// <summary>R14 根治：把一个已经脱离刷新点追踪（不在 <see cref="_entityToSpawn"/> 里）的孤儿
        /// 实体从世界移除，见 <see cref="Load"/> 判断记录。<paramref name="spawnId"/> 对应的
        /// <c>spawn.table</c> 记录已不存在或解析失败时保守跳过——同 <see cref="NotifyDespawn"/>/<see
        /// cref="Update"/> 对缺失/非法记录的一贯处理，不抛异常。</summary>
        private void DespawnOrphan(Id spawnId, Id entityId)
        {
            var record = _data.Get(SpawnSchemas.Table.Name, spawnId);
            if (record == null)
            {
                return;
            }

            SpawnTableDef def;
            try
            {
                def = SpawnTableDef.FromRecord(record);
            }
            catch (DataFieldException)
            {
                return;
            }

            var domain = def.ContentRef.Domain;
            if (domain == EntityKinds.Creature)
            {
                _creatureFactory.Despawn(entityId, "restore_reconcile");
            }
            else if (domain == EntityKinds.Gobj)
            {
                if (_options.GobjDespawner == null)
                {
                    _diagnostics.Warn(
                        $"刷新点 \"{spawnId}\" 的孤儿实体 \"{entityId}\" 因未注入 SpawnOptions.GobjDespawner，读档时未能移除");
                    return;
                }

                _options.GobjDespawner(entityId);
            }
        }

        private bool ConditionPasses(SpawnTableDef def)
        {
            if (string.IsNullOrEmpty(def.ConditionText))
            {
                return true;
            }

            var node = ExprParser.Parse(def.ConditionText, ConditionSchema);
            var selfId = _options.PlayerUnitResolver() ?? NoPlayerSentinel;
            var host = _exprHostFactory.CreateFor(selfId, null, null);
            var diagnostics = new ExprDiagnosticsRecorder();
            return ExprEvaluator.EvaluateBool(node, host, diagnostics);
        }

        private bool HasOnceDone(Id spawnId) => _worldState.Has(OnceFlagKey(spawnId));

        private void MarkOnceDone(Id spawnId) => _worldState.Set(OnceFlagKey(spawnId), ExprValue.OfBool(true), _options.WriterId);

        /// <summary>见任务书拍板 <c>world.spawn.&lt;name&gt;.done</c>（惯例同
        /// <c>Core.Gameplay.AreaTrigger.AreaTriggerHost.FiredFlagKey</c>）。</summary>
        private static Id OnceFlagKey(Id spawnId)
        {
            var value = spawnId.Value;
            var dot = value.IndexOf('.');
            var name = dot < 0 ? value : value.Substring(dot + 1);
            return new Id("world.spawn." + name + ".done");
        }
    }
}

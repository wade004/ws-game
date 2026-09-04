using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
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
            var def = SpawnTableDef.FromRecord(record);
            var runtime = GetOrCreateRuntime(spawnId);

            if (runtime.EntityId.HasValue)
            {
                _diagnostics.Warn($"刷新点 \"{spawnId}\" 已有存活实例，TriggerNever 被忽略");
                return;
            }

            if (!ConditionPasses(def))
            {
                _diagnostics.Warn($"刷新点 \"{spawnId}\" condition 不满足，TriggerNever 被忽略");
                return;
            }

            SpawnEntity(def, runtime);
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

        public void Load(JsonValue data)
        {
            _records.Clear();
            _entityToSpawn.Clear();

            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException($"spawn_state 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

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

            if (domain == "creature")
            {
                entityId = _creatureFactory.Spawn(def.ContentRef, def.MapId, def.Position, def.Facing);
            }
            else if (domain == "gobj")
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

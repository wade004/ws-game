using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <see cref="ILevelHost"/> 的默认（唯一）实现（见 08 第 4.2 节）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性解析 <c>encounter.level</c>，之后只读；运行期按
    /// <c>encounter_sequence</c> 顺序推进，靠订阅 <see cref="EncounterWonEvent"/> 并按
    /// <b>实例 id</b>（不是 <c>encounter.def</c> 定义 id，见 <see cref="EncounterEventKeys"/>
    /// 判断记录）精确匹配"是不是我刚发起的那一次运行"来驱动。
    /// </summary>
    public sealed class LevelHost : ILevelHost
    {
        private sealed class LevelRunState
        {
            public Id PlayerUnitId;
            public int SequenceIndex;
            public SubscriptionHandle? WonSubscription;

            /// <summary>本次运行所属的关卡定义（GP-04 新增：<see cref="AbortForMap"/> 靠它反查
            /// <c>map_ref</c>，避免额外一次按 id 查表往返）。</summary>
            public EncounterLevelDefinition Def = null!;

            /// <summary>当前序列位置正在进行的遭遇实例 id（N13 新增：<see cref="StartLevel"/> 重开时
            /// 靠它把旧运行对应的遭遇实例一并 <see cref="IEncounterHost.Abort"/> 掉，不只是删订阅——
            /// 见 <see cref="StartLevel"/> 判断记录）。</summary>
            public Id ActiveInstanceId;
        }

        private readonly SortedDictionary<string, EncounterLevelDefinition> _levels =
            new SortedDictionary<string, EncounterLevelDefinition>(StringComparer.Ordinal);

        // 键：playerUnitId.Value——同一玩家同一时刻只有一次进行中的关卡运行（见 StartLevel 判断记录）。
        private readonly Dictionary<string, LevelRunState> _activeRuns = new Dictionary<string, LevelRunState>(StringComparer.Ordinal);

        private readonly IEncounterHost _encounterHost;
        private readonly IEventBus _bus;

        public LevelHost(IDataRegistryView registry, IEncounterHost encounterHost, IEventBus bus)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _encounterHost = encounterHost ?? throw new ArgumentNullException(nameof(encounterHost));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            foreach (var record in registry.GetAll(EncounterSchemas.Level.Name))
            {
                var def = EncounterLevelDefinition.FromRecord(record);
                _levels[def.Id.Value] = def;
            }
        }

        public void StartLevel(Id levelId, Id playerUnitId)
        {
            var def = RequireLevel(levelId);
            if (def.EncounterSequence.Count == 0)
            {
                return;
            }

            // 判断记录（N13 根治，architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
            // 同一玩家重复调用 StartLevel（如中途放弃重开）时，此前只清理上一次运行遗留的 Won
            // 订阅，没有连带终止旧运行正在进行的遭遇实例——旧实例仍然 IsActive，之后仍可能被
            // EncounterTickHandler.Evaluate 推进到胜利并发一次 EncounterWonEvent；虽然本类型的
            // Won 订阅已经清了、不会再响应它，但旧实例本身残留成一套"活跃但没人管"的运行，
            // 与新开的一套并存（两套活跃实例），且旧实例若被其它路径（如内容脚本直接查询
            // IEncounterHost）观察到会呈现"关卡还在进行"的错误状态。修复：重开前一并
            // Abort 旧实例，保证同一玩家任意时刻最多只有一套来自本类型的活跃遭遇实例。
            if (_activeRuns.TryGetValue(playerUnitId.Value, out var existing))
            {
                existing.WonSubscription?.Dispose();
                _encounterHost.Abort(existing.ActiveInstanceId);
                _activeRuns.Remove(playerUnitId.Value);
            }

            var state = new LevelRunState { PlayerUnitId = playerUnitId, SequenceIndex = 0, Def = def };
            _activeRuns[playerUnitId.Value] = state;
            StartNextEncounter(state, def);
        }

        public IReadOnlyList<Id> GetEntryDifficultyOptions(Id levelId) => RequireLevel(levelId).EntryDifficultyOptions;

        public void AbortForMap(Id mapId)
        {
            List<string>? toRemove = null;
            foreach (var kv in _activeRuns)
            {
                if (!kv.Value.Def.MapRef.Equals(mapId))
                {
                    continue;
                }

                kv.Value.WonSubscription?.Dispose();
                (toRemove ??= new List<string>()).Add(kv.Key);
            }

            if (toRemove == null)
            {
                return;
            }

            foreach (var key in toRemove)
            {
                _activeRuns.Remove(key);
            }
        }

        // -----------------------------------------------------------------
        // 内部辅助
        // -----------------------------------------------------------------

        private void StartNextEncounter(LevelRunState state, EncounterLevelDefinition def)
        {
            var encounterId = def.EncounterSequence[state.SequenceIndex];
            var instanceId = _encounterHost.Start(encounterId, def.MapRef, state.PlayerUnitId);
            state.ActiveInstanceId = instanceId;

            state.WonSubscription = _bus.Subscribe<EncounterWonEvent>(
                EncounterEventKeys.Won,
                evt => OnEncounterWon(state, def, instanceId, evt));
        }

        private void OnEncounterWon(LevelRunState state, EncounterLevelDefinition def, Id expectedInstanceId, EncounterWonEvent evt)
        {
            if (!evt.EncounterId.Equals(expectedInstanceId))
            {
                // 不是本次运行发起的那个遭遇实例（可能是另一玩家/另一次并发运行的胜利事件），
                // 与本状态机无关，忽略。
                return;
            }

            state.WonSubscription?.Dispose();
            state.SequenceIndex++;

            if (state.SequenceIndex >= def.EncounterSequence.Count)
            {
                // 关卡序列全部完成（见 ILevelHost.StartLevel 判断记录："08 未定义 level.completed
                // 一类事件"，本类型不额外发明一个）。
                _activeRuns.Remove(state.PlayerUnitId.Value);
                return;
            }

            StartNextEncounter(state, def);
        }

        private EncounterLevelDefinition RequireLevel(Id levelId)
        {
            if (_levels.TryGetValue(levelId.Value, out var def))
            {
                return def;
            }
            throw new ArgumentException($"未知的 encounter.level \"{levelId}\"", nameof(levelId));
        }
    }
}

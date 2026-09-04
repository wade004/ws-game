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

            // 判断记录：同一玩家重复调用 StartLevel（如中途放弃重开）时，先清理上一次运行遗留的
            // Won 订阅，避免旧订阅在新一轮运行期间继续存活、造成事件串扰或订阅泄漏。
            if (_activeRuns.TryGetValue(playerUnitId.Value, out var existing))
            {
                existing.WonSubscription?.Dispose();
                _activeRuns.Remove(playerUnitId.Value);
            }

            var state = new LevelRunState { PlayerUnitId = playerUnitId, SequenceIndex = 0 };
            _activeRuns[playerUnitId.Value] = state;
            StartNextEncounter(state, def);
        }

        public IReadOnlyList<Id> GetEntryDifficultyOptions(Id levelId) => RequireLevel(levelId).EntryDifficultyOptions;

        // -----------------------------------------------------------------
        // 内部辅助
        // -----------------------------------------------------------------

        private void StartNextEncounter(LevelRunState state, EncounterLevelDefinition def)
        {
            var encounterId = def.EncounterSequence[state.SequenceIndex];
            var instanceId = _encounterHost.Start(encounterId, def.MapRef, state.PlayerUnitId);

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

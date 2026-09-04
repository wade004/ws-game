using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// 一条 <c>encounter.level</c> 记录的强类型视图（见 08 第 4.2 节"关卡 = 地图 + 遭遇序列"：
    /// <c>id</c>、<c>mapRef</c>（指向 <c>world.map</c>）、<c>encounterSequence: List&lt;Id&gt;</c>
    /// （有序 <c>encounter.def</c> 引用）、<c>entryDifficultyOptions: List&lt;Id&gt;</c>（可选难度
    /// 档位））。
    /// </summary>
    public sealed class EncounterLevelDefinition
    {
        public Id Id { get; }

        /// <summary>指向 <c>world.map</c>（08 第 4.2 节）；该表不在本任务数据集范围内，登记为
        /// <see cref="Core.Foundation.DataRegistry.FieldKind.Id"/> 而非 Reference，惯例同
        /// <c>creature.template.display_ref</c> 一类判断记录（见 <see cref="EncounterSchemas"/>）。</summary>
        public Id MapRef { get; }

        public IReadOnlyList<Id> EncounterSequence { get; }

        public IReadOnlyList<Id> EntryDifficultyOptions { get; }

        private EncounterLevelDefinition(Id id, Id mapRef, IReadOnlyList<Id> encounterSequence, IReadOnlyList<Id> entryDifficultyOptions)
        {
            Id = id;
            MapRef = mapRef;
            EncounterSequence = encounterSequence;
            EntryDifficultyOptions = entryDifficultyOptions;
        }

        public static EncounterLevelDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");
            var mapRef = record.GetId("map_ref");
            var sequence = record.GetIdList("encounter_sequence");
            if (sequence.Count == 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "encounter_sequence", "至少需要一个遭遇");
            }
            var entryOptions = record.TryGetIdList("entry_difficulty_options", out var options) ? options : Array.Empty<Id>();

            return new EncounterLevelDefinition(id, mapRef, sequence, entryOptions);
        }
    }
}

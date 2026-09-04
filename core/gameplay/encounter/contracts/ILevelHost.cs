using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// 关卡编排契约（见 08 第 4.2 节"关卡 = 地图 + 遭遇序列"）：按 <c>encounter.level</c> 的
    /// <c>encounter_sequence</c> 顺序推进一串遭遇，上一遭遇 <c>encounter.won</c> 后自动
    /// <see cref="IEncounterHost.Start"/> 下一个。由 <c>core/gameplay/encounter</c> 实现。
    /// </summary>
    public interface ILevelHost
    {
        /// <summary>开始一个关卡：<see cref="IEncounterHost.Start"/> <c>encounter_sequence[0]</c>，
        /// 并订阅 <see cref="EncounterWonEvent"/> 以便上一遭遇获胜后自动推进到下一个；序列全部完成
        /// （最后一个遭遇也 <c>Won</c>）后关卡本身不再产生任何事件（08 未定义
        /// <c>level.completed</c> 一类事件，见 README 判断记录）。<paramref name="levelId"/> 未登记
        /// 时抛 <see cref="System.ArgumentException"/>；<c>encounter_sequence</c> 为空时空操作。
        /// <para>
        /// 判断记录（<c>encounter.lost</c> 不推进）：08 第 4.2 节只说明"上一遭遇 won 后自动 Start
        /// 下一个"，未规定 <c>lost</c> 时的行为——本实现不监听 <c>encounter.lost</c>，遭遇失败后
        /// 关卡停留在当前遭遇（不自动重试、不自动终止关卡），具体重试/放弃/回城流程由调用方
        /// （游戏层/UI）在收到 <c>encounter.lost</c> 后自行决定，必要时重新调用
        /// <see cref="IEncounterHost.Start"/> 重开当前遭遇。
        /// </para>
        /// </summary>
        void StartLevel(Id levelId, Id playerUnitId);

        /// <summary><c>encounter.level.entry_difficulty_options</c>，供 UI 查询可选难度档位。
        /// <paramref name="levelId"/> 未登记时抛 <see cref="System.ArgumentException"/>。</summary>
        IReadOnlyList<Id> GetEntryDifficultyOptions(Id levelId);
    }
}

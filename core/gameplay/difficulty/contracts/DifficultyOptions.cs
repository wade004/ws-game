using System;
using Core.Foundation.Common;

namespace Core.Gameplay.Difficulty
{
    /// <summary>
    /// <see cref="Core.Gameplay.Difficulty.DifficultyHost"/> 的策略配置项（见 08 第 9 节汇总表
    /// <c>Difficulty</c> 行"策略配置项：分档集合、是否允许中途切换"）。
    /// <para>
    /// 判断记录：<see cref="PlayerFactionId"/> 设为必填构造参数而非可选项——任务书原句
    /// "对新生物（阵营非玩家：经 <c>IFactionMatrix.IsHostile(unit, DifficultyOptions.PlayerFactionId)</c>
    /// 或 <c>DifficultyOptions.ApplyToAll</c>）"隐含它是修正光环施加判定的必要输入之一；若
    /// <see cref="ApplyToAll"/> 为 true 则完全不读取该字段，但类型层面仍要求调用方显式提供一个
    /// 合法 <see cref="Id"/>（不允许 <c>default(Id)</c> 悄悄流入），避免"只想用 ApplyToAll 时随手
    /// 传一个非法占位值"的隐患——构造期即校验。
    /// </para>
    /// </summary>
    public sealed class DifficultyOptions
    {
        /// <summary>玩家阵营 id，供 <see cref="Core.Numbers.Faction.IFactionMatrix.IsHostile"/>
        /// 判断新生成单位是否为敌对单位（08 第 5.2 节"对新生成的敌对单位施加修正光环"）。</summary>
        public Id PlayerFactionId { get; }

        /// <summary>true 时忽略阵营敌对判定，对全部新生成单位（含玩家阵营/中立阵营）都施加修正
        /// 光环——供"全体属性统一缩放"一类难度口味使用（08 第 5.1 节 <c>modifier_aura_refs</c>
        /// "例如统一提升生物属性"，未强制限定只对敌对生效，任务书拍板补充这一开关）。</summary>
        public bool ApplyToAll { get; set; }

        /// <summary>是否允许在已应用某一难度档位后中途切换到另一档位（08 第 5.2 节"视
        /// Level.entryDifficultyOptions 是否允许中途切换"，本类只提供开关本身，具体由哪些关卡
        /// 允许中途切换属于游戏层/关卡数据的编排职责）。默认 true。</summary>
        public bool AllowMidSwitch { get; set; } = true;

        public DifficultyOptions(Id playerFactionId, bool applyToAll = false, bool allowMidSwitch = true)
        {
            if (playerFactionId.Value == null)
            {
                throw new ArgumentException("playerFactionId 必须是合法构造的 Id（不能是 default(Id)）", nameof(playerFactionId));
            }

            PlayerFactionId = playerFactionId;
            ApplyToAll = applyToAll;
            AllowMidSwitch = allowMidSwitch;
        }
    }
}

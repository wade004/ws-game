using Core.Foundation.Common;

namespace Core.Gameplay.Difficulty
{
    /// <summary>
    /// 难度模块对外契约（见 08 第 9 节契约汇总表 <c>Difficulty</c> 行
    /// <c>DifficultyHost.apply(tierId: Id, scope): Void</c>——任务书把返回类型拍板补充为
    /// <c>Bool</c>，见 <see cref="Apply"/> 注释判断记录）。由 <c>core/gameplay/difficulty</c>
    /// 实现，供关卡入口/新游戏流程与 UI 调用。
    /// </summary>
    public interface IDifficultyHost
    {
        /// <summary>当前生效的难度档位；从未调用过 <see cref="Apply"/>（或读档前）为 null。</summary>
        Id? CurrentTier { get; }

        /// <summary>当前生效档位的作用域；<see cref="CurrentTier"/> 为 null 时同为 null。</summary>
        DifficultyScope? CurrentScope { get; }

        /// <summary><see cref="CurrentScope"/> 为 <see cref="DifficultyScope.Map"/> 时对应的地图 id；
        /// 其它情况为 null（供组装层/存档段之外的调用方查询当前作用域范围）。</summary>
        Id? CurrentMapId { get; }

        /// <summary>
        /// 应用一个难度档位（08 第 5.1/5.2 节）：记录 <see cref="CurrentTier"/>/
        /// <see cref="CurrentScope"/>/<see cref="CurrentMapId"/>，此后经 <c>creature.spawned</c>
        /// 订阅对每个新生成的敌对（或全体，见 <see cref="DifficultyOptions.ApplyToAll"/>）单位
        /// 施加 <c>modifier_aura_refs</c>；已存在单位不重算（08 第 5.2 节"已进行中的遭遇不因难度
        /// 切换而重算，只影响后续新生成的内容"）。成功后发出 <see cref="DifficultyAppliedEvent"/>。
        /// <para>
        /// 判断记录（返回 <see cref="bool"/>）：08 第 9 节契约签名原文是
        /// <c>apply(tierId: Id, scope): Void</c>，未给出失败路径；任务书把
        /// <see cref="DifficultyOptions.AllowMidSwitch"/> 列为需要落地的策略配置项，本方法用
        /// 返回值表达"是否允许切换"这一判定结果：<see cref="CurrentTier"/> 已有值（本局已应用过
        /// 某档位）且 <see cref="DifficultyOptions.AllowMidSwitch"/> 为 false 时，本方法不做任何
        /// 改动、不发事件，直接返回 false；其余情况正常应用并返回 true。未登记的
        /// <paramref name="tierId"/> 仍按 <see cref="System.ArgumentException"/> 处理（与本模块
        /// 其它"未知 id"惯例一致，不是"允许中途切换"判定覆盖的范围）。
        /// </para>
        /// </summary>
        /// <param name="tierId"><c>diff.tier</c> 引用。</param>
        /// <param name="scope">作用域：全局或单地图。</param>
        /// <param name="mapId"><paramref name="scope"/> 为 <see cref="DifficultyScope.Map"/> 时必填，
        /// 指定生效的地图；<see cref="DifficultyScope.Global"/> 时必须为 null。</param>
        bool Apply(Id tierId, DifficultyScope scope, Id? mapId);

        /// <summary>当前档位的掉落数量/概率倍率（供 08 第 1 节 Loot 模块的
        /// <c>lootMultiplierProvider</c> 策略配置项引用）；未应用任何档位时为 <c>1.0</c>
        /// （"无难度修正"的中性默认值，任务书未规定，判断记录同本类型其余"缺省值"约定）。</summary>
        double LootMultiplier { get; }

        /// <summary>是否允许中途切换难度档位（08 第 9 节汇总表策略配置项，透传
        /// <see cref="DifficultyOptions.AllowMidSwitch"/>）。</summary>
        bool AllowMidSwitch { get; }
    }
}

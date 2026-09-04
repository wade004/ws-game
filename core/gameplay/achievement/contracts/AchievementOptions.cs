using System;
using Core.Foundation.Common;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// <see cref="AchievementHost"/> 的策略配置项。
    /// <para>
    /// 判断记录（<see cref="PlayerUnitResolver"/>）：任务书原句"kill_count ← unit.died
    /// （...且 killerId==玩家单位（AchievementOptions.PlayerUnitResolver）...）"、"custom_event ←
    /// observe_event + filter Expr（宿主 CreateFor(player, null, evt)）"——两处都需要"当前玩家
    /// 单位 id"这一值，且不依赖触发事件本身（玩家单位在一局游戏内基本固定，切角色/多人场景是
    /// 后续扩展），因此声明为无参 <see cref="Func{Id}"/> 而非携带 <c>IEvent</c> 参数的委托：
    /// 由调用方（组装层）注入一个"返回当前玩家单位 id"的取值函数，本模块不关心它内部如何解析
    /// （单机固定值、还是查询某个"当前控制角色"状态）。
    /// </para>
    /// </summary>
    public sealed class AchievementOptions
    {
        public Func<Id> PlayerUnitResolver { get; }

        public AchievementOptions(Func<Id> playerUnitResolver)
        {
            PlayerUnitResolver = playerUnitResolver ?? throw new ArgumentNullException(nameof(playerUnitResolver));
        }
    }
}

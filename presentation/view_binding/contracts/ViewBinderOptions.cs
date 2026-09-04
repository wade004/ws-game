using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Presentation.ViewBinding
{
    /// <summary>
    /// <see cref="ViewBinder"/> 的策略配置项（见 01 L5 模块表 <c>view_binding</c> 行"策略配置
    /// 项：视图创建的资源加载策略"，本模块额外拍板补充"转发事件键集合"这一项——09 第 2 节
    /// "onEvent 是 View 接收战斗日志风格事件……的唯一入口"未规定具体转发哪些事件 key，任务书拍板
    /// 由 <see cref="ForwardedEventKeys"/> 显式声明，默认覆盖 unit/combat/aura/skill/item/gobj 域。
    /// </summary>
    public sealed class ViewBinderOptions
    {
        /// <summary>转发给相关 View 的事件 key 集合（见 <see cref="ViewBinder"/> 类型注释
        /// "转发规则"）。不含 <c>entity.created</c>/<c>entity.destroyed</c>（由 <c>ViewBinder</c>
        /// 内部专门处理，不走通用转发路径）、不含 <c>unit.moved</c>（位置同步改由
        /// <c>sim.tick_finished</c> 快照 + <see cref="ViewBinder.SyncAll"/> 插值处理，见类型
        /// 注释）、不含 <c>sim.*</c>（内部节奏事件）。</summary>
        public IReadOnlyList<Id> ForwardedEventKeys { get; }

        /// <summary>未在 DisplayInfo 中声明方向档位（<c>Sprite</c> 型缺失 DisplayInfo 时的兜底）
        /// 时使用的默认方向量化档位数（4/8/16 之一，见 05 第 3.2 节）。默认 8。</summary>
        public int DefaultDirectionCount { get; }

        public ViewBinderOptions(IReadOnlyList<Id>? forwardedEventKeys = null, int defaultDirectionCount = 8)
        {
            ForwardedEventKeys = forwardedEventKeys ?? DefaultForwardedEventKeys;
            DefaultDirectionCount = defaultDirectionCount;
        }

        /// <summary>默认转发键集合（见类型注释）。</summary>
        public static IReadOnlyList<Id> DefaultForwardedEventKeys { get; } = new[]
        {
            // skill 域
            RulesEventKeys.SkillCastStart,
            RulesEventKeys.SkillCastSuccess,
            RulesEventKeys.SkillCastFailed,
            RulesEventKeys.SkillCastInterrupted,

            // combat 域
            RulesEventKeys.CombatDamageDealt,
            RulesEventKeys.CombatHealDone,
            RulesEventKeys.CombatThreatChanged,
            RulesEventKeys.CombatEntered,
            RulesEventKeys.CombatLeft,
            RulesEventKeys.UnitDied,
            RulesEventKeys.UnitRespawned,

            // aura 域
            RulesEventKeys.AuraApplied,
            RulesEventKeys.AuraRemoved,
            RulesEventKeys.AuraStackChanged,
            RulesEventKeys.ProcTriggered,

            // unit 域（不含 unit.moved，见类型注释）
            CarriersEventKeys.UnitStateChanged,

            // item 域
            CarriersEventKeys.ItemAdded,
            CarriersEventKeys.ItemRemoved,
            CarriersEventKeys.ItemEquipped,
            CarriersEventKeys.ItemUnequipped,

            // gobj 域
            CarriersEventKeys.GobjInteracted,
            CarriersEventKeys.GobjStateChanged,
        };
    }
}

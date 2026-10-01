using Core.Foundation.Common;
using Core.Foundation.InputMap;

namespace Core.Rules.Common
{
    /// <summary>
    /// 动作状态只读查询（手感设计/01 第 3.7 节）：表现层与实验室只经它读动作状态；结算管线经
    /// <see cref="IsInvulnerable"/> 做无敌窗口前置检查（手感设计/03 第 2.3 节）。实现由动作时间线切片提供，
    /// 本接口只定义契约。
    /// </summary>
    public interface IActionStateQuery
    {
        /// <summary>行动者进行中的动作；无动作返回 null。</summary>
        ActionState? Current(Id unitId);

        /// <summary>该类别的取消窗口此刻是否打开（无进行中动作恒为 false）。</summary>
        bool IsCancelOpen(Id unitId, ActionClass actionClass);

        /// <summary>行动者当前是否处于无敌窗口（<c>invuln_start</c>～<c>invuln_end</c>）。</summary>
        bool IsInvulnerable(Id unitId);

        /// <summary>行动者动作时钟是否被顿帧暂停。</summary>
        bool IsActionClockPaused(Id unitId);

        /// <summary>
        /// 行动者当前是否处于霸体窗口（时间线标记 <c>armor_start</c>～<c>armor_end</c>，手感设计/03 第 4 节）。受击裁决读它：
        /// 霸体期间命中不产生反应（仍扣血，受击方顿帧视策略）。C# 默认接口成员（纯加法，既有实现无需改动，缺省恒 false）；
        /// 动作时间线的实现方覆盖它。光环类霸体不经本成员，见 <c>HitFeelOptions.SuperArmorAuraDef</c>。
        /// </summary>
        bool IsSuperArmor(Id unitId) => false;
    }
}

using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// W1 收边补齐（拍板 3 前置：死亡复活链路，见 <see cref="Core.Carriers.Unit.WorldUnitAccess.Revive"/>
    /// 判断记录）：按生命值百分比恢复某单位当前生命值的具名委托。本模块（<c>core/carriers/unit</c>）
    /// 不引用 <c>core/numbers/power_set</c> 的具体类型（<c>IPowerHost</c>），转发给注入方处理（通常
    /// 由 <c>core/carriers/assembly.CarriersAssembly</c> 用 <c>IPowerHost.GetPowerMax</c>/
    /// <c>ModifyPower</c> 实现，见该类型判断记录）——与 <c>Core.Numbers.Archetype.StatBaseWriter</c>
    /// 一类"跨层不直接引用类型、走具名委托"的既有惯例一致。
    /// <paramref name="fraction"/> 取值区间 <c>[0, 1]</c>，<c>1</c> 表示恢复满血；调用方（本委托的
    /// 实现方）负责夹取越界输入。
    /// </summary>
    public delegate void HealthFractionSetter(Id unitId, double fraction);
}

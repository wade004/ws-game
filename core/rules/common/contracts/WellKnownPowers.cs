using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 拍板：生命值本身是一种资源，不在 <see cref="IUnitAccess"/> 上单列 <c>GetHealth</c> 之类的
    /// 专用方法，而是经 L1 <c>Core.Numbers.PowerSet.IPowerHost</c> 按普通资源类型读写——
    /// <c>hp_pct = IPowerHost.GetPower(unitId, Health) / IPowerHost.GetPowerMax(unitId, Health)</c>。
    /// 本类只登记这一约定的资源类型 id 常量，格式与 <c>arch.power_type</c> 记录的 <c>id</c> 字段
    /// 一致（见 <c>core/numbers/power_set/contracts/PowerTypeDefinition.cs</c> "资源类型 id，格式
    /// arch.power.&lt;name&gt;"）；游戏层数据必须登记一条 id 为本常量值的 <c>arch.power_type</c> 记录，
    /// 否则 skill/combat 模块对生命值的一切读写都会在 <c>IPowerHost</c> 层面失败。
    /// </summary>
    public static class WellKnownPowers
    {
        /// <summary>生命值资源类型 id：<c>arch.power.health</c>。</summary>
        public static readonly Id Health = new Id("arch.power.health");
    }
}

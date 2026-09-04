using System;

namespace Core.Carriers.Common
{
    /// <summary>穿戴失败原因（见 07 第 1.3 节 <c>EquipmentHost.equip</c> 返回值 <c>EquipResult</c>，
    /// 07 原文未列出具体失败分支，本枚举是执行层按常见穿脱校验点补全的固定有限集合，惯例同
    /// <c>core/rules/common</c> 的 <c>CastFailureReason</c>）。</summary>
    public enum EquipFailureReason
    {
        None,

        /// <summary>该物品实例 id 未知（不存在于任何背包/装备栏）。</summary>
        UnknownItem,

        /// <summary>该物品的 <c>slot</c> 与请求穿戴的槽位不匹配。</summary>
        SlotMismatch,

        /// <summary>目标槽位已被占用（调用方需先 <c>unequip</c> 或由实现按策略自动置换，具体策略不
        /// 属于本契约约束范围）。</summary>
        SlotOccupied,

        /// <summary>不满足穿戴需求（等级/职业/阵营等前置条件，具体校验项由数据与实现约定）。</summary>
        RequirementNotMet,

        /// <summary>该物品实例不在此单位的背包中。</summary>
        NotInInventory,
    }

    /// <summary>
    /// 一次穿戴请求的结果（见 07 第 1.3 节 <c>EquipmentHost.equip</c>）。不可变值类型：只能经
    /// <see cref="Ok"/>/<see cref="Fail"/> 两个工厂方法构造，惯例同 <c>core/rules/common</c> 的
    /// <c>CastResult</c>，禁止外部直接拼出"成功但带失败原因"一类不自洽的组合。
    /// </summary>
    public readonly struct EquipResult : IEquatable<EquipResult>
    {
        public bool Success { get; }

        public EquipFailureReason Reason { get; }

        /// <summary>若该槽位原先已有装备且被本次穿戴置换下来，携带被置换物品的引用；否则为 null。
        /// 只在 <see cref="Success"/> 为 true 时可能非 null。</summary>
        public ItemInstanceRef? Replaced { get; }

        private EquipResult(bool success, EquipFailureReason reason, ItemInstanceRef? replaced)
        {
            Success = success;
            Reason = reason;
            Replaced = replaced;
        }

        public static EquipResult Ok(ItemInstanceRef? replaced = null) =>
            new EquipResult(true, EquipFailureReason.None, replaced);

        public static EquipResult Fail(EquipFailureReason reason)
        {
            if (reason == EquipFailureReason.None)
            {
                throw new ArgumentException("失败结果必须携带非 None 的失败原因", nameof(reason));
            }

            return new EquipResult(false, reason, null);
        }

        public bool Equals(EquipResult other) =>
            Success == other.Success && Reason == other.Reason && Nullable.Equals(Replaced, other.Replaced);

        public override bool Equals(object? obj) => obj is EquipResult other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Success.GetHashCode();
                hash = (hash * 397) ^ (int)Reason;
                hash = (hash * 397) ^ (Replaced?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public static bool operator ==(EquipResult left, EquipResult right) => left.Equals(right);

        public static bool operator !=(EquipResult left, EquipResult right) => !left.Equals(right);
    }
}

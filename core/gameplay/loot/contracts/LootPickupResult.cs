using System;
using System.Collections.Generic;
using Core.Carriers.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>一次 <see cref="LootHost.PickUp"/> 调用未能（完全）拾取的原因；<see
    /// cref="None"/> 表示成功（可能是部分成功，见 <see cref="LootPickupResult.Success"/> 判断记录）。</summary>
    public enum LootPickupFailureReason
    {
        None,

        /// <summary>该 <c>lootInstanceId</c> 不存在（未知/已被拾完销毁）。</summary>
        NotFound,

        /// <summary>单位与掉落物的距离超出 <see cref="LootOptions.PickupRange"/>。</summary>
        TooFar,

        /// <summary><see cref="LootOptions.FullPolicy"/>=<see cref="LootPickupPolicy.Reject"/> 下，
        /// 因至少一个物品堆叠放不下而整体撤回。</summary>
        Rejected,

        /// <summary><see cref="LootOptions.FullPolicy"/>=<see cref="LootPickupPolicy.Partial"/> 下，
        /// 背包已满到一件都放不下（没有任何数量成功加入）。</summary>
        InventoryFull,

        /// <summary>
        /// ADR-0062：经 <see cref="LootHost.Interact"/>（<c>interact</c> 意图原生分流）尝试拾取时，
        /// <see cref="LootOptions.PickupPermissionChecker"/> 拒绝了本次拾取——只在经 <c>interact</c>
        /// 意图分流时才可能出现（<see cref="LootHost.PickUp"/> 直接调用不经权限判定，见该方法与
        /// <see cref="LootOptions.PickupPermissionChecker"/> 判断记录"默认放行，不新造归属系统"）。
        /// </summary>
        PermissionDenied,
    }

    /// <summary>
    /// <see cref="LootHost.PickUp"/> 的返回值：任务书未给出该方法的具体返回类型，本模块按现有
    /// <c>EquipResult</c>/<c>SkillCastRequest</c> 一类"结果值类型 + 失败原因枚举"的惯例补上（见
    /// <c>core/carriers/common/contracts/EquipResult.cs</c>）。<see cref="Success"/> 语义："本次调用
    /// 是否有任何物品进入了单位背包"——<see cref="LootPickupPolicy.Partial"/> 下即使只拿到掉落物的
    /// 一部分也算成功，<see cref="ItemsTaken"/> 记录实际拿到手的部分；<see
    /// cref="LootPickupPolicy.Reject"/> 下不存在"部分成功"，<see cref="Success"/> 与"是否拿到全部"
    /// 等价。
    /// </summary>
    public readonly struct LootPickupResult
    {
        public bool Success { get; }

        public LootPickupFailureReason Reason { get; }

        public IReadOnlyList<ItemStack> ItemsTaken { get; }

        private LootPickupResult(bool success, LootPickupFailureReason reason, IReadOnlyList<ItemStack> itemsTaken)
        {
            Success = success;
            Reason = reason;
            ItemsTaken = itemsTaken;
        }

        public static LootPickupResult Ok(IReadOnlyList<ItemStack> itemsTaken) =>
            new LootPickupResult(true, LootPickupFailureReason.None, itemsTaken);

        public static LootPickupResult Fail(LootPickupFailureReason reason)
        {
            if (reason == LootPickupFailureReason.None)
            {
                throw new ArgumentException("失败结果必须携带非 None 的失败原因", nameof(reason));
            }

            return new LootPickupResult(false, reason, Array.Empty<ItemStack>());
        }
    }
}

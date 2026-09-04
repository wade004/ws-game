using System;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Carriers.Common
{
    /// <summary>判别联合的具体变体（见 07 第 3.2 节 <c>LockDef.requirement</c>）。</summary>
    public enum LockRequirementKind
    {
        /// <summary>持有指定钥匙物品（<c>item_key(itemId: Id)</c>）。</summary>
        ItemKey,

        /// <summary>指定世界标志达到期望值（<c>world_flag(flagKey: Id, expected: Value)</c>）。</summary>
        WorldFlag,

        /// <summary>某项技能相关数值达到门槛（<c>skill_check(skillTag: Id, minValue: Number)</c>）。</summary>
        SkillCheck,
    }

    /// <summary>
    /// 锁的判定条件（见 07 第 3.2 节 <c>LockDef.requirement</c> 三种变体）。判别联合：只能经三个静态
    /// 工厂方法之一构造，<see cref="Kind"/> 决定应读取哪些字段，惯例同本目录 <c>core/rules/common</c>
    /// 的 <see cref="Core.Foundation.EngineAdapter.Shape"/> 判别联合写法。
    /// </summary>
    public readonly struct LockRequirement
    {
        public LockRequirementKind Kind { get; }

        /// <summary><see cref="LockRequirementKind.ItemKey"/> 的钥匙物品模板 id。</summary>
        public Id? ItemId { get; }

        /// <summary><see cref="LockRequirementKind.WorldFlag"/> 的世界标志 key。</summary>
        public Id? FlagKey { get; }

        /// <summary><see cref="LockRequirementKind.WorldFlag"/> 的期望值（见 <see cref="IWorldFlags"/>
        /// 顶部判断记录，值类型统一按 <see cref="ExprValue"/> 承载）。</summary>
        public ExprValue? Expected { get; }

        /// <summary><see cref="LockRequirementKind.SkillCheck"/> 的技能相关数值标签。</summary>
        public Id? SkillTag { get; }

        /// <summary><see cref="LockRequirementKind.SkillCheck"/> 的最低门槛值。</summary>
        public double MinValue { get; }

        private LockRequirement(
            LockRequirementKind kind,
            Id? itemId,
            Id? flagKey,
            ExprValue? expected,
            Id? skillTag,
            double minValue)
        {
            Kind = kind;
            ItemId = itemId;
            FlagKey = flagKey;
            Expected = expected;
            SkillTag = skillTag;
            MinValue = minValue;
        }

        public static LockRequirement ItemKey(Id itemId) =>
            new LockRequirement(LockRequirementKind.ItemKey, itemId, null, null, null, 0);

        public static LockRequirement WorldFlag(Id flagKey, ExprValue expected) =>
            new LockRequirement(LockRequirementKind.WorldFlag, null, flagKey, expected, null, 0);

        public static LockRequirement SkillCheck(Id skillTag, double minValue) =>
            new LockRequirement(LockRequirementKind.SkillCheck, null, null, null, skillTag, minValue);
    }
}

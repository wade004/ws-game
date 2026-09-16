using Core.Carriers.Common;
using Core.Foundation.Common;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// 2026-09-16 深度复审 D-M1 抽取的共享内部辅助：把"击杀者 id"解析为"记账单位 id"——ADR-0033
    /// 决策 3"召唤物击杀归主人"的唯一实现，供 <see
    /// cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener"/>（经验入账，此前的唯一实现）与
    /// <see cref="Core.Gameplay.Loot.CreatureDeathLootListener"/>（货币入账，D-M1 之前完全没有做这一
    /// 步解析）共用，避免两处各自维护一份同构逻辑、日后改动只改一处（原逻辑逐字保留，仅换成静态方法，
    /// 不改变任何既有行为）。
    /// </summary>
    internal static class SummonCreditResolver
    {
        /// <summary><paramref name="killerId"/> 是某个已登记召唤物（经 <paramref name="summons"/>
        /// 查询）时改记其主人；否则原样返回 <paramref name="killerId"/> 本身（包括"是玩家本人"与
        /// "是普通生物，非玩家、非召唤物"两种情形，留给调用方按 <c>IUnitAccess.GetSourceKind</c>
        /// 继续判断）。<paramref name="summons"/> 为 <c>null</c>（未注入召唤宿主）时恒原样返回，
        /// 与 <see cref="ISummonHost.GetOwner"/> 查不到主人时的语义一致（无召唤物概念可用）。</summary>
        public static Id ResolveCreditUnit(ISummonHost? summons, Id killerId)
        {
            var owner = summons?.GetOwner(killerId);
            return owner.HasValue ? owner.Value : killerId;
        }
    }
}

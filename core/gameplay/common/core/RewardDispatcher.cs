using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Gameplay.WorldState;
using Core.Numbers.Progression;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// <see cref="IRewardDispatcher"/> 的默认实现：把 <see cref="RewardBundle"/> 的六类奖励分别
    /// 转发给各自的宿主依赖。全部依赖均为可空的可选构造参数——判断记录：任务书"任一为 null 时对应
    /// 奖励记诊断跳过"，即调用方可以只注入自己关心的子集（例如只测试物品发放时不必构造一个真实
    /// <see cref="IProgressionHost"/>），未注入的依赖对应类别的奖励被静默跳过（记一条诊断警告，
    /// 不抛异常），不影响其余类别正常发放；<see cref="RewardBundle"/> 里某类奖励本就为空（如
    /// 没有 <c>skills</c>）时，即便对应依赖也是 null，也不产生警告——只有"确实有这类奖励要发但
    /// 发不出去"才值得警告。
    /// </summary>
    public sealed class RewardDispatcher : IRewardDispatcher
    {
        private readonly IInventoryHost? _inventory;
        private readonly IProgressionHost? _progression;
        private readonly IWorldState? _worldState;
        private readonly SkillGranter? _skillGranter;
        private readonly CurrencyGranter? _currencyGranter;
        private readonly TalentPointGranter? _talentPointGranter;
        private readonly IRewardDiagnostics _diagnostics;

        public RewardDispatcher(
            IInventoryHost? inventory = null,
            IProgressionHost? progression = null,
            IWorldState? worldState = null,
            SkillGranter? skillGranter = null,
            CurrencyGranter? currencyGranter = null,
            TalentPointGranter? talentPointGranter = null,
            IRewardDiagnostics? diagnostics = null)
        {
            _inventory = inventory;
            _progression = progression;
            _worldState = worldState;
            _skillGranter = skillGranter;
            _currencyGranter = currencyGranter;
            _talentPointGranter = talentPointGranter;
            _diagnostics = diagnostics ?? new InMemoryRewardDiagnostics();
        }

        public void Grant(Id unitId, RewardBundle bundle, Id sourceId)
        {
            if (bundle == null)
            {
                throw new System.ArgumentNullException(nameof(bundle));
            }

            GrantItems(unitId, bundle);
            GrantXp(unitId, bundle, sourceId);
            GrantCurrency(unitId, bundle, sourceId);
            GrantSkills(unitId, bundle);
            GrantWorldFlags(unitId, bundle, sourceId);
            GrantTalentPoints(unitId, bundle, sourceId);
        }

        private void GrantItems(Id unitId, RewardBundle bundle)
        {
            if (bundle.Items.Count == 0)
            {
                return;
            }

            if (_inventory == null)
            {
                _diagnostics.Warn(
                    $"RewardDispatcher.Grant({unitId})：未注入 IInventoryHost，跳过 {bundle.Items.Count} 项物品奖励");
                return;
            }

            foreach (var stack in bundle.Items)
            {
                _inventory.AddItem(unitId, stack.TemplateId, stack.Count);
            }
        }

        private void GrantXp(Id unitId, RewardBundle bundle, Id sourceId)
        {
            if (bundle.Xp <= 0)
            {
                return;
            }

            if (_progression == null)
            {
                _diagnostics.Warn($"RewardDispatcher.Grant({unitId})：未注入 IProgressionHost，跳过 {bundle.Xp} 点经验奖励");
                return;
            }

            _progression.AddXp(unitId, sourceId, bundle.Xp);
        }

        private void GrantCurrency(Id unitId, RewardBundle bundle, Id sourceId)
        {
            if (bundle.Currency.Count == 0)
            {
                return;
            }

            if (_currencyGranter == null)
            {
                _diagnostics.Warn(
                    $"RewardDispatcher.Grant({unitId})：未注入 CurrencyGranter，跳过 {bundle.Currency.Count} 项货币奖励");
                return;
            }

            foreach (var (currencyId, amount) in bundle.Currency)
            {
                _currencyGranter(unitId, currencyId, amount, sourceId);
            }
        }

        private void GrantSkills(Id unitId, RewardBundle bundle)
        {
            if (bundle.Skills.Count == 0)
            {
                return;
            }

            if (_skillGranter == null)
            {
                _diagnostics.Warn(
                    $"RewardDispatcher.Grant({unitId})：未注入 SkillGranter，跳过 {bundle.Skills.Count} 项技能奖励");
                return;
            }

            foreach (var skillId in bundle.Skills)
            {
                _skillGranter(unitId, skillId, true);
            }
        }

        private void GrantWorldFlags(Id unitId, RewardBundle bundle, Id sourceId)
        {
            if (bundle.WorldFlags.Count == 0)
            {
                return;
            }

            if (_worldState == null)
            {
                _diagnostics.Warn(
                    $"RewardDispatcher.Grant({unitId})：未注入 IWorldState，跳过 {bundle.WorldFlags.Count} 项世界标志奖励");
                return;
            }

            foreach (var (flagKey, value) in bundle.WorldFlags)
            {
                _worldState.Set(flagKey, value, sourceId);
            }
        }

        private void GrantTalentPoints(Id unitId, RewardBundle bundle, Id sourceId)
        {
            if (bundle.TalentPoints <= 0)
            {
                return;
            }

            if (_talentPointGranter == null)
            {
                _diagnostics.Warn(
                    $"RewardDispatcher.Grant({unitId})：未注入 TalentPointGranter，跳过 {bundle.TalentPoints} 点天赋点奖励");
                return;
            }

            _talentPointGranter(unitId, bundle.TalentPoints, sourceId);
        }
    }
}

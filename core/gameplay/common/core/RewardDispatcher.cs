using System.Collections.Generic;
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

        public bool Grant(Id unitId, RewardBundle bundle, Id sourceId)
        {
            if (bundle == null)
            {
                throw new System.ArgumentNullException(nameof(bundle));
            }

            // N02 根治：物品奖励最先发放且原子（全部成功才继续，否则回滚已发放部分并整体中止）——
            // 见 IRewardDispatcher.Grant 判断记录，其余类别没有"容量不足"这类失败模式。
            if (!GrantItems(unitId, bundle))
            {
                return false;
            }

            GrantXp(unitId, bundle, sourceId);
            GrantCurrency(unitId, bundle, sourceId);
            GrantSkills(unitId, bundle, sourceId);
            GrantWorldFlags(unitId, bundle, sourceId);
            GrantTalentPoints(unitId, bundle, sourceId);
            return true;
        }

        /// <summary>发放 <paramref name="bundle"/> 里的物品奖励，返回是否全部发放成功。<see
        /// cref="_inventory"/>.<c>AddItem</c> 在 <c>InventoryFullPolicy.Reject</c> 下要么完整加入
        /// <c>stack.Count</c>、要么完全不产生任何变化（见 <c>InventoryHost.AddItem</c> 判断记录 2"先
        /// 算容量够不够，再决定是否落地任何变化"）；<c>InventoryFullPolicy.Partial</c> 下可能只加入
        /// 一部分仍返回成功（该策略本身的既有语义，"缩水但不失败"不属于本方法要处理的失败模式）。
        /// <para>
        /// C05 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：此前回滚按"请求
        /// 数量" <c>stack.Count</c> 精确移除——Reject 策略下请求量恒等于实际落地量，没有问题；但
        /// Partial 策略下二者可能不等（少量加入仍返回 true），某一项失败触发整批回滚时，会把之前
        /// Partial 少加的那一项按"请求量"移除，越过实际落地量、多删到该批发放之前就已经存在的同
        /// 模板堆叠。改用 <see cref="IInventoryHost.TryAddItem"/> 取得每一项的实际落地量
        /// <c>actualCount</c>，回滚按实际量而不是请求量移除，保证失败整批回滚后背包精确回到"这次
        /// <see cref="Grant"/> 调用之前"的状态，不多不少。
        /// </para>
        /// <para>
        /// R01 根治（architecture/落地计划/audit-5e779c6-20260907）：上一段的按量回滚只保证了背包
        /// "数量"精确回到发放前，但没有解决"事件"层面的问题——<c>TryAddItem</c> 成功那一项已经把
        /// <c>item.added</c> 事件排进了事件总线的待处理队列（<see
        /// cref="Core.Foundation.EventBus.IEventBus.Enqueue"/> 只入队不立即派发），回滚调用的
        /// <c>RemoveItem</c> 又补入一条独立的 <c>item.removed</c>；下游订阅者（如
        /// <c>QuestHost.HandleItemAdded</c> 的 <c>consumeOnProgress</c>）在同一次
        /// <c>DispatchPending</c> 里先收到"已加入"、当作真实发生的事件立即消费掉玩家已有的同模板
        /// 物品并推进任务进度，后到的 <c>item.removed</c> 抵消不了这个副作用——整批回滚后背包"数量"
        /// 对了，但任务系统已经误判、玩家已有物品被多扣了一份。现在若 <paramref name="_inventory"/>
        /// 实现了 <see cref="IBatchableInventoryHost"/>（<c>InventoryHost</c> 已实现），把整批发放包
        /// 进一次事务：事务期间产生的通知事件先缓存、不送入总线；失败时 <c>using</c> 块结束触发
        /// <see cref="IInventoryTransaction.Dispose"/>（未 Commit 即回滚）把背包状态与缓存事件一并
        /// 撤销，下游完全观察不到这次失败的发放发生过，不需要再逐项调用 <see cref="RemoveByTemplate"/>。
        /// 宿主不支持事务时（多数测试用的 Fake）退回上一段的历史行为，不强制所有实现跟进。
        /// </para>
        /// </summary>
        private bool GrantItems(Id unitId, RewardBundle bundle)
        {
            if (bundle.Items.Count == 0)
            {
                return true;
            }

            if (_inventory == null)
            {
                _diagnostics.Warn(
                    $"RewardDispatcher.Grant({unitId})：未注入 IInventoryHost，跳过 {bundle.Items.Count} 项物品奖励");
                return true;
            }

            var transaction = _inventory is IBatchableInventoryHost batchable ? batchable.BeginBatch() : null;
            using (transaction)
            {
                var granted = new List<(Id TemplateId, int Count)>();
                foreach (var stack in bundle.Items)
                {
                    if (_inventory.TryAddItem(unitId, stack.TemplateId, stack.Count, out var actualCount))
                    {
                        if (actualCount > 0)
                        {
                            granted.Add((stack.TemplateId, actualCount));
                        }
                        continue;
                    }

                    if (transaction == null)
                    {
                        // 历史行为：宿主不支持事务，按实际落地量（不是请求量）逐项回滚。
                        foreach (var prior in granted)
                        {
                            RemoveByTemplate(_inventory, unitId, prior.TemplateId, prior.Count);
                        }
                    }
                    // 宿主支持事务时，using 块结束触发 Dispose（未调用 Commit）即整体回滚，含事件，
                    // 不需要在这里手动移除（见本方法判断记录）。

                    _diagnostics.Warn(
                        $"RewardDispatcher.Grant({unitId})：背包已满，物品奖励 \"{stack.TemplateId}\" x{stack.Count} " +
                        "无法发放，整批奖励回滚未生效");
                    return false;
                }

                transaction?.Commit();
                return true;
            }
        }

        /// <summary>按模板 id 移除总计 <paramref name="count"/> 个物品（跨堆叠），供 <see
        /// cref="GrantItems"/> 回滚使用；惯例同 <c>QuestHost.RemoveCollectedItems</c>。</summary>
        private static void RemoveByTemplate(IInventoryHost inventory, Id unitId, Id templateId, int count)
        {
            var remaining = count;
            foreach (var item in inventory.ListItems(unitId))
            {
                if (remaining <= 0)
                {
                    break;
                }
                if (!item.TemplateId.Equals(templateId))
                {
                    continue;
                }

                var take = System.Math.Min(remaining, item.Count);
                if (inventory.RemoveItem(unitId, item.InstanceId, take))
                {
                    remaining -= take;
                }
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

        /// <summary>
        /// 接线跟进（Y1/Y2 侧 RC-05"装备授予技能缺来源计数"根治，见
        /// core/carriers/item/contracts/SkillGranter.cs：委托签名从
        /// <c>(unitId, skillId, learn)</c> 三参改为 <c>(unitId, skillId, sourceId, learn)</c> 四参，
        /// 供 <c>SkillHost.LearnSkill(Id,Id,Id)</c>/<c>ForgetSkill(Id,Id,Id)</c> 的按来源引用计数）：
        /// 本方法此前不带 <paramref name="sourceId"/>，改为透传 <see cref="Grant"/> 收到的
        /// <paramref name="sourceId"/>（例如 quest.def 的 id、encounter.def 的 id）作为技能授予的
        /// 来源标识，与 <see cref="GrantCurrency"/>/<see cref="GrantWorldFlags"/>/
        /// <see cref="GrantTalentPoints"/> 同一惯例——奖励发放视为"以此次奖励来源为准的一次授予"，
        /// 引用计数与装备/永久学习等其它来源各自独立，撤销时只影响这一来源。
        /// </summary>
        private void GrantSkills(Id unitId, RewardBundle bundle, Id sourceId)
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
                _skillGranter(unitId, skillId, sourceId, true);
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

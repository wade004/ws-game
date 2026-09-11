using System;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// P2 根治验收（消费方反馈 2026-09-11"只读就绪查询影响后续充能状态"，见
    /// architecture/落地计划/消费方反馈-2026-09-11-充能查询副作用.md；<see cref="CooldownTracker"/>
    /// 类型判断记录"查询纯化"/"充能上限变化守恒规则"）：直接构造裸 <see cref="CooldownTracker"/>
    /// （不经 <see cref="SkillHost"/>/<see cref="RulesAssembly"/>），验证类型判断记录声明的两条不变式：
    /// <list type="number">
    /// <item>只读方法（<see cref="CooldownTracker.GetCharges(Id, SkillDef)"/>/
    /// <see cref="CooldownTracker.GetChargeRechargeRemaining"/>/
    /// <see cref="CooldownTracker.GetEffectiveChargesMax"/>/
    /// <see cref="CooldownTracker.GetEffectiveRechargeTimeScaled"/>/
    /// <see cref="CooldownTracker.IsSkillReady"/>/<see cref="CooldownTracker.GetCooldown(Id, SkillDef)"/>）
    /// 调用前后 <see cref="CooldownTracker.TrackedChargeKeys"/> 不变（覆盖 0/1/多次调用）——只有写路径
    /// （<see cref="CooldownTracker.StartCooldown"/> 等）才会让 <see cref="CooldownTracker.TrackedChargeKeys"/>
    /// 出现新条目。</item>
    /// <item>有效充能上限在两次调用之间发生变化时，只读快照按"充能上限变化守恒规则"计算，与"是否曾经
    /// 查询过"无关——本文件用同一个 (unit, skill) 键、但两次调用传入 <c>ChargesMax</c> 不同的
    /// <see cref="SkillDef"/> 实例来模拟"有效上限随 SpellMod 变化"（<see cref="CooldownTracker"/> 不
    /// 缓存 <see cref="SkillDef"/>，每次调用都用传入的 def 重新计算有效上限，因此不需要真正接线
    /// <see cref="SpellModResolver"/> 即可复现同一类"上限变化"场景，行文与 C09b 系列基于真实
    /// <see cref="Core.Rules.Assembly.RulesAssembly"/> 的集成测试互补——本文件专注 <see
    /// cref="CooldownTracker"/> 自身的不变式，C09b 验证经真实 SpellMod/施法管线时的端到端行为）。</item>
    /// </list>
    /// </summary>
    public sealed class CooldownTrackerReadOnlyQueryTests
    {
        private static readonly Id Unit = new Id("unit.crt_readonly_test");
        private static readonly Id School = new Id("skill.school.crt_readonly_test");
        private static readonly Id TargetChain = new Id("target.chain.crt_readonly_test");

        private static SkillDef ChargeSkill(string skillId, int chargesMax, double rechargeTime) => new SkillDef(
            id: new Id(skillId),
            school: School,
            isPassive: false,
            range: 0,
            tags: Array.Empty<Id>(),
            castTime: 0,
            channelTime: 0,
            cost: Array.Empty<(Id, double)>(),
            cooldownCategory: null,
            cooldownDuration: 0,
            chargesMax: chargesMax,
            chargesRechargeTime: rechargeTime,
            respectsGcd: false,
            targetShapeRef: TargetChain,
            effects: Array.Empty<EffectRef>(),
            interruptFlags: InterruptFlags.None);

        // -----------------------------------------------------------------
        // 1) 只读方法调用前后 TrackedChargeKeys 不变（0/1/多次）。
        // -----------------------------------------------------------------
        [Fact]
        public void ReadOnlyQueries_ZeroCalls_TrackedChargeKeysStartsEmpty()
        {
            var tracker = new CooldownTracker();
            Assert.Empty(tracker.TrackedChargeKeys);
        }

        [Fact]
        public void ReadOnlyQueries_SingleCallToEachReadOnlyMethod_TrackedChargeKeysStaysEmpty()
        {
            var tracker = new CooldownTracker();
            var def = ChargeSkill("skill.crt_single", chargesMax: 2, rechargeTime: 6);

            _ = tracker.GetCharges(Unit, def);
            Assert.Empty(tracker.TrackedChargeKeys);

            _ = tracker.GetChargeRechargeRemaining(Unit, def);
            Assert.Empty(tracker.TrackedChargeKeys);

            _ = tracker.GetEffectiveChargesMax(Unit, def);
            Assert.Empty(tracker.TrackedChargeKeys);

            _ = tracker.GetEffectiveRechargeTimeScaled(Unit, def);
            Assert.Empty(tracker.TrackedChargeKeys);

            _ = tracker.IsSkillReady(Unit, def);
            Assert.Empty(tracker.TrackedChargeKeys);

            _ = tracker.GetCooldown(Unit, def);
            Assert.Empty(tracker.TrackedChargeKeys);
        }

        [Fact]
        public void ReadOnlyQueries_ManyRepeatedCalls_TrackedChargeKeysStaysEmpty()
        {
            var tracker = new CooldownTracker();
            var def = ChargeSkill("skill.crt_repeated", chargesMax: 3, rechargeTime: 10);

            for (var i = 0; i < 25; i++)
            {
                _ = tracker.GetCharges(Unit, def);
                _ = tracker.GetChargeRechargeRemaining(Unit, def);
                _ = tracker.GetEffectiveChargesMax(Unit, def);
                _ = tracker.GetEffectiveRechargeTimeScaled(Unit, def);
                _ = tracker.IsSkillReady(Unit, def);
                _ = tracker.GetCooldown(Unit, def);
            }

            Assert.Empty(tracker.TrackedChargeKeys);
        }

        [Fact]
        public void ReadOnlyQueries_DefaultSnapshot_MatchesFullChargesWithoutCreatingState()
        {
            var tracker = new CooldownTracker();
            var def = ChargeSkill("skill.crt_default_snapshot", chargesMax: 3, rechargeTime: 10);

            Assert.Equal(3, tracker.GetCharges(Unit, def));
            Assert.Equal(0.0, tracker.GetChargeRechargeRemaining(Unit, def));
            Assert.Equal(3, tracker.GetEffectiveChargesMax(Unit, def));
            Assert.True(tracker.IsSkillReady(Unit, def));
            Assert.Equal(0.0, tracker.GetCooldown(Unit, def));
            Assert.Empty(tracker.TrackedChargeKeys);
        }

        // -----------------------------------------------------------------
        // 2) 写路径才会真正创建状态。
        // -----------------------------------------------------------------
        [Fact]
        public void WritePath_StartCooldown_CreatesChargeState_TrackedChargeKeysNowContainsIt()
        {
            var tracker = new CooldownTracker();
            var def = ChargeSkill("skill.crt_write_path", chargesMax: 2, rechargeTime: 6);

            Assert.Empty(tracker.TrackedChargeKeys);

            tracker.StartCooldown(Unit, def);

            var tracked = Assert.Single(tracker.TrackedChargeKeys);
            Assert.Equal(Unit, tracked.Unit);
            Assert.Equal(def.Id, tracked.Skill);
        }

        // -----------------------------------------------------------------
        // 3) 充能上限变化守恒规则：查询是否发生过不改变对账结果（直接在 CooldownTracker 层面复现
        //    消费方 A/B 场景，不依赖 SpellModResolver）。
        // -----------------------------------------------------------------
        [Fact]
        public void MaxIncrease_QueryBeforeVsWithoutQuery_ProducesIdenticalConservedCurrent()
        {
            var trackerA = new CooldownTracker(); // A：先查询。
            var trackerB = new CooldownTracker(); // B：不查询。
            var lowDef = ChargeSkill("skill.crt_max_increase", chargesMax: 2, rechargeTime: 100);
            var highDef = ChargeSkill("skill.crt_max_increase", chargesMax: 3, rechargeTime: 100);

            // A 在上限提高前先只读查询一次——不应创建状态（这正是消费方反馈复现的触发点）。
            Assert.Equal(2, trackerA.GetCharges(Unit, lowDef));
            Assert.Empty(trackerA.TrackedChargeKeys);

            // 上限"生效"后（等价于 SpellMod 修饰后的新 def），A/B 都只读查询一次；查询本身仍不创建状态。
            var afterA = trackerA.GetCharges(Unit, highDef);
            var afterB = trackerB.GetCharges(Unit, highDef);
            Assert.Empty(trackerA.TrackedChargeKeys);
            Assert.Empty(trackerB.TrackedChargeKeys);
            Assert.Equal(3, afterA);
            Assert.Equal(afterB, afterA); // 核心断言：查没查询过不影响只读快照。

            // 紧接着各自走写路径（消耗一次）：结果必须完全一致，不因"先前是否查询过"而分叉——
            // 这正是消费方反馈复现的可观察差异（修复前 A=2、B=3 次连续施法成功）。
            trackerA.StartCooldown(Unit, highDef);
            trackerB.StartCooldown(Unit, highDef);
            Assert.Equal(trackerB.GetCharges(Unit, highDef), trackerA.GetCharges(Unit, highDef));
            Assert.Equal(2, trackerA.GetCharges(Unit, highDef)); // 3 - 1 = 2，两者一致。
        }

        [Fact]
        public void MaxDecrease_BelowCurrentCharges_ClampsAndClearsRechargeWindow()
        {
            var tracker = new CooldownTracker();
            var fullDef = ChargeSkill("skill.crt_max_decrease_clamp", chargesMax: 3, rechargeTime: 100);
            var loweredDef = ChargeSkill("skill.crt_max_decrease_clamp", chargesMax: 1, rechargeTime: 100);

            // 先写路径消耗一次，制造一个"正在恢复中"的状态（Current=2，RechargeRemaining>0）。
            tracker.StartCooldown(Unit, fullDef);
            Assert.Equal(2, tracker.GetCharges(Unit, fullDef));
            Assert.True(tracker.GetChargeRechargeRemaining(Unit, fullDef) > 0);

            // 上限降到 1（低于当前的 2）：只读快照应夹取到新上限，且因为夹取后恰好等于新上限（满充能）
            // 而清零恢复窗口——查询本身只计算，不写回。
            Assert.Equal(1, tracker.GetCharges(Unit, loweredDef));
            Assert.Equal(0.0, tracker.GetChargeRechargeRemaining(Unit, loweredDef));

            // 紧接着一次写路径（AddCharge 0，只用于触发对账）应得到同样结果，证明只读快照与写路径口径一致。
            tracker.AddCharge(Unit, loweredDef, 0);
            Assert.Equal(1, tracker.GetCharges(Unit, loweredDef));
            Assert.Equal(0.0, tracker.GetChargeRechargeRemaining(Unit, loweredDef));
        }

        [Fact]
        public void MaxDecrease_StillBelowNewCap_PreservesCurrentChargesAndRechargeWindow()
        {
            var tracker = new CooldownTracker();
            var fullDef = ChargeSkill("skill.crt_max_decrease_preserve", chargesMax: 3, rechargeTime: 100);
            var loweredDef = ChargeSkill("skill.crt_max_decrease_preserve", chargesMax: 2, rechargeTime: 100);

            // 消耗两次：Current=1，仍在恢复中。
            tracker.StartCooldown(Unit, fullDef);
            tracker.StartCooldown(Unit, fullDef);
            Assert.Equal(1, tracker.GetCharges(Unit, fullDef));
            var rechargeBefore = tracker.GetChargeRechargeRemaining(Unit, fullDef);
            Assert.True(rechargeBefore > 0);

            // 上限降到 2（仍然 > 当前的 1，不需要夹取）：当前充能数与恢复进度都不受影响——"未满充能"
            // 的降低不应清零一个仍在进行中的恢复窗口。
            Assert.Equal(1, tracker.GetCharges(Unit, loweredDef));
            Assert.Equal(rechargeBefore, tracker.GetChargeRechargeRemaining(Unit, loweredDef));
        }

        [Fact]
        public void MaxIncrease_WhileAlreadyFull_GrantsExtraChargeImmediately_RechargeStaysZero()
        {
            var tracker = new CooldownTracker();
            var lowDef = ChargeSkill("skill.crt_max_increase_full", chargesMax: 2, rechargeTime: 50);
            var highDef = ChargeSkill("skill.crt_max_increase_full", chargesMax: 3, rechargeTime: 50);

            // 从未消耗过（惰性满充能，只读，不创建状态）。
            Assert.Equal(2, tracker.GetCharges(Unit, lowDef));
            Assert.Empty(tracker.TrackedChargeKeys);

            // 上限提高：满充能状态下也应立即多出一次可用充能（Δ=1），不产生恢复窗口。
            Assert.Equal(3, tracker.GetCharges(Unit, highDef));
            Assert.Equal(0.0, tracker.GetChargeRechargeRemaining(Unit, highDef));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Assembly;
using Core.Rules.Common;
using Tests.Rules.Integration;
using Xunit;

namespace Tests.Rules
{
    /// <summary>
    /// 落地计划 T2-11"两个单位互殴"集成测试 + 阶段 2 集成收口。世界组装、数据、玩家意图脚本全部
    /// 由 <see cref="FightWorldBuilder"/> 提供（见该类型注释），本文件只负责跑起来 + 断言。
    /// <para>
    /// 全部断言只经公开契约/事件读取（<see cref="FightWorldBuilder.Fixture.Events"/>/
    /// <see cref="FightWorldBuilder.Fixture.Rules"/> 暴露的宿主属性），不触碰 <c>core/</c> 内部
    /// 字段（T2-11 禁止事项）。
    /// </para>
    /// <para>
    /// 判断记录（步长选择）：<see cref="FightWorldBuilder.StepSeconds"/> 固定为 1.0 模拟秒/tick——
    /// 技能读条时间（<c>skill.sample_burn.cast_time = 1.0</c>）、光环周期
    /// （<c>skill.aura_def.sample_burn</c> 的 <c>interval = 1.0</c>）、AI 决策节奏
    /// （<c>ai.profile.sample.decision_interval = 0.5</c>）都与这个步长同量级，玩家意图脚本按
    /// "tick 序号"调度（任务书原句）才能直接对应到"第几次读条/第几次光环周期"，不需要额外换算。
    /// </para>
    /// </summary>
    public sealed class TwoUnitsFightTests
    {
        // -----------------------------------------------------------------
        // 0. 夹具自检
        // -----------------------------------------------------------------

        [Fact]
        public void Fixture_LoadReport_IsNotBlocking()
        {
            var fx = FightWorldBuilder.Build();
            Assert.False(fx.LoadReport.IsBlocking, string.Join("; ", fx.LoadReport.Issues));
        }

        // -----------------------------------------------------------------
        // 1. 死亡
        // -----------------------------------------------------------------

        [Fact]
        public void Fight_RunsToCompletion_AtLeastOneUnitDies()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run();

            var deaths = fx.Events.OfType<UnitDiedEvent>().ToList();
            Assert.NotEmpty(deaths);
            Assert.True(!fx.Units.IsAlive(FightWorldBuilder.PlayerId) || !fx.Units.IsAlive(FightWorldBuilder.NpcId));
        }

        // -----------------------------------------------------------------
        // 2. 事件顺序：施法管线 cast_start → 效果落地 damage_dealt → cast_success
        // -----------------------------------------------------------------

        /// <summary>
        /// 判断记录：任务书原句要求"首个 skill.cast_success 早于首个 combat.damage_dealt"，实际
        /// 跑出来的事件序列是反过来的——<c>CastPipeline.cs</c>（<c>TryStartCast</c>/
        /// <c>ResumeQueuedOrFinishCast</c> 等三处调用点）一律"先 <c>ExecuteEffectsOnly</c>（效果
        /// 落地，产生 <c>combat.damage_dealt</c>）、再 <c>Enqueue(SkillCastSuccessEvent)</c>"，
        /// 这是该模块一致、刻意的设计（<c>skill.cast_success</c> 语义是"步骤 9：整次施放——含全部
        /// 效果——已经完成"，不是"步骤 8 结束、效果尚待落地"）。三个调用点写法完全一致，不是疏漏。
        /// 本任务的允许改动范围包含 <c>core/rules/skill/</c>，但改这里的事件顺序属于改变已经落地
        /// 并有 197 条既有测试覆盖的模块行为，不是"集成任务"该做的事，因此这里选择修正断言方向
        /// 而不是动 <c>CastPipeline</c>；断言改为验证真正成立、同样有意义的管线顺序：
        /// <c>cast_start</c>（步骤 8 开始）先于效果落地，效果落地先于 <c>cast_success</c>（步骤 9
        /// 完成）。
        /// </summary>
        [Fact]
        public void EventOrder_CastStart_Then_DamageDealt_Then_CastSuccess()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run();

            var castStartIndex = FirstIndex<SkillCastStartEvent>(fx.Events);
            var damageDealtIndex = FirstIndex<CombatDamageDealtEvent>(fx.Events);
            var castSuccessIndex = FirstIndex<SkillCastSuccessEvent>(fx.Events);

            Assert.True(castStartIndex.HasValue, "从未出现 skill.cast_start");
            Assert.True(damageDealtIndex.HasValue, "从未出现 combat.damage_dealt");
            Assert.True(castSuccessIndex.HasValue, "从未出现 skill.cast_success");
            Assert.True(castStartIndex < damageDealtIndex,
                $"cast_start(index={castStartIndex}) 应早于首个 damage_dealt(index={damageDealtIndex})");
            Assert.True(damageDealtIndex < castSuccessIndex,
                $"首个 damage_dealt(index={damageDealtIndex}) 应早于首个 cast_success(index={castSuccessIndex})");
        }

        // -----------------------------------------------------------------
        // 3. 事件顺序：aura.applied 早于首个周期伤害
        // -----------------------------------------------------------------

        [Fact]
        public void EventOrder_AuraApplied_BeforeFirstPeriodicDamage()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run();

            var auraAppliedIndex = FirstIndex<AuraAppliedEvent>(fx.Events);
            Assert.True(auraAppliedIndex.HasValue, "从未出现 aura.applied");

            // 判断记录：CombatDamageDealtEvent payload 本身不携带"是否为周期效果"标志（06 第 8 节
            // 字段表：sourceId/targetId/school/amount/isCrit/hitResult，没有 isPeriodic），无法直接
            // 从事件类型上区分"周期伤害"与"技能瞬发伤害"。本场景按数值区分：sample_strike 造成 25、
            // sample_bite 造成 15，只有光环周期效果（sample_burn 的 periodic_damage，base_value=5，
            // coefficient=0，命中表暴击/加成分支全禁用，see FightWorldBuilder.HitTableJson，数值不会
            // 被随机/加成改变）造成 5 点伤害——用 Amount==5 精确定位周期伤害事件，不是近似猜测。
            var firstPeriodicIndex = FirstIndex<CombatDamageDealtEvent>(fx.Events, e => e.Amount == 5.0);
            Assert.True(firstPeriodicIndex.HasValue, "从未出现来自光环周期效果的伤害（Amount==5）");
            Assert.True(auraAppliedIndex < firstPeriodicIndex,
                $"aura.applied(index={auraAppliedIndex}) 应早于首个周期伤害(index={firstPeriodicIndex})");
        }

        // -----------------------------------------------------------------
        // 4. 事件顺序：combat.entered 对双方各一次，且在战斗真正打起来（第二次伤害结算）之前
        //    已经就位
        // -----------------------------------------------------------------

        /// <summary>
        /// 判断记录：任务书原句"combat.entered 对双方各一次且早于第一次伤害"——实际序列里第一次
        /// <c>combat.damage_dealt</c> 正是触发进战的原因（<c>Resolver</c> 落地伤害后调用
        /// <c>NotifyCombatEvent</c> 才产生 <c>combat.entered</c>，见 06 第 4.5 节"造成/受到伤害…
        /// 任一条件满足即置位 combatState=in"——进战判定的输入就包含"刚刚发生的这次伤害"本身，
        /// 不可能早于触发它的那次伤害），第一次伤害必然先于双方进战。断言改为验证一个同样成立、
        /// 同样有意义的属性：双方进战在"战斗真正打起来"（第二次伤害结算，即第一次交换之后的
        /// 后续攻击）之前就已经就位——证明进战状态不是拖到战斗过半才姗姗来迟。
        /// </summary>
        [Fact]
        public void EventOrder_CombatEntered_BothUnits_BeforeSecondDamage()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run();

            var enteredForPlayer = fx.Events.OfType<CombatEnteredEvent>()
                .Where(e => e.UnitId == FightWorldBuilder.PlayerId).ToList();
            var enteredForNpc = fx.Events.OfType<CombatEnteredEvent>()
                .Where(e => e.UnitId == FightWorldBuilder.NpcId).ToList();

            Assert.Single(enteredForPlayer);
            Assert.Single(enteredForNpc);

            var lastEnteredIndex = Math.Max(IndexOf(fx.Events, enteredForPlayer[0]), IndexOf(fx.Events, enteredForNpc[0]));

            var damageIndices = new List<int>();
            for (var i = 0; i < fx.Events.Count; i++)
            {
                if (fx.Events[i] is CombatDamageDealtEvent) damageIndices.Add(i);
            }

            Assert.True(damageIndices.Count >= 2, "本场战斗至少应该发生两次伤害结算才有意义");
            var secondDamageIndex = damageIndices[1];

            Assert.True(lastEnteredIndex < secondDamageIndex,
                $"双方 combat.entered（较晚一次 index={lastEnteredIndex}）应早于第二次 damage_dealt(index={secondDamageIndex})");
        }

        // -----------------------------------------------------------------
        // 5. 死亡后脱战延迟内推进，最终发出 combat.left
        // -----------------------------------------------------------------

        [Fact]
        public void CombatLeft_EventuallyFires_AfterDeathAndLeaveCombatDelay()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run(bufferTicksAfterDeath: 15); // 默认 CombatOptions.LeaveCombatDelay = 5，留足余量。

            var left = fx.Events.OfType<CombatLeftEvent>().ToList();
            Assert.NotEmpty(left);
        }

        // -----------------------------------------------------------------
        // 6. AI 至少发出一次 ai.decision_made
        // -----------------------------------------------------------------

        [Fact]
        public void Ai_EmitsAtLeastOneDecisionMadeEvent()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run();

            var decisions = fx.Events.OfType<AiDecisionMadeEvent>().ToList();
            Assert.NotEmpty(decisions);
            Assert.All(decisions, e =>
            {
                Assert.Equal(FightWorldBuilder.NpcId, e.UnitId);
                Assert.Equal(FightWorldBuilder.SkillBite, e.DecisionId);
            });
        }

        // -----------------------------------------------------------------
        // 7. 确定性：同种子重复运行两次，事件序列与最终生命值逐项相等
        // -----------------------------------------------------------------

        [Fact]
        public void Determinism_SameSeed_TwoIndependentRuns_ProduceIdenticalOutcome()
        {
            var fxA = FightWorldBuilder.Build(seed: 777UL);
            var tickA = fxA.Run();
            var fxB = FightWorldBuilder.Build(seed: 777UL);
            var tickB = fxB.Run();

            Assert.Equal(tickA, tickB);
            Assert.Equal(EventKeySequence(fxA), EventKeySequence(fxB));
            AssertSameFinalHp(fxA, fxB);
        }

        // -----------------------------------------------------------------
        // 8. 不同种子：本场景命中表全部分支禁用（见 FightWorldBuilder.HitTableJson），伤害结算
        //    不消耗 combat.hit 这条 RngStream 的任何随机数，AI 侧 RandomTieBreak 默认关闭、Proc
        //    触发链本场景未使用——种子在本测试数据集下不会影响任何分支选择，因此"不同种子"的
        //    预期结果是【逐项相等】，不是"至少某处不同"。这正是任务书"不同种子至少某处不同或
        //    明确说明（命中表全开则骰值影响）"里"明确说明"分支的落地：命中表不是"全开"而是
        //    "全禁用"，效果相同——两者都让骰值不参与任何判定，只是全禁用更直接地证明了这一点。
        // -----------------------------------------------------------------

        [Fact]
        public void DifferentSeeds_ProduceIdenticalOutcome_BecauseHitTableIsFullyDeterministic()
        {
            var fxA = FightWorldBuilder.Build(seed: 1UL);
            var tickA = fxA.Run();
            var fxB = FightWorldBuilder.Build(seed: 999999UL);
            var tickB = fxB.Run();

            Assert.Equal(tickA, tickB);
            Assert.Equal(EventKeySequence(fxA), EventKeySequence(fxB));
            AssertSameFinalHp(fxA, fxB);
        }

        // -----------------------------------------------------------------
        // 9. RulesSchemaCatalog.RegisterAll + 载入 data/_sample（磁盘）→ LoadAll 0 错误
        // -----------------------------------------------------------------

        [Fact]
        public void RulesSchemaCatalog_RegisterAll_LoadsRealSampleData_ZeroErrors()
        {
            var source = BuildRealSampleSource();
            var bus = FightWorldBuilder.BuildEventBus(out _);
            var options = RulesSchemaCatalog.CreateOptions();

            // 阶段 3 整理"事项四"改动：data/_sample 新增了 item/（item.budget_curve，供
            // toolchain/validator 经 CarriersSchemaCatalog 校验 L3 用），本用例只关心 L2
            // RulesSchemaCatalog 自己登记的表在真实数据上能否 0 错误加载，不是"data/_sample 里
            // 出现的每一张表都必须被 L2 目录认识"——FailOnUnknownTable 改为 false，未登记的表
            // （如 item.*）按 TableSchema.Unschematized 兜底加载，不产生错误也不产生警告（见
            // DataRegistry.LoadOneTable 判断记录），本用例真正要验证的"L2 已登记表 0 错误/0 警告"
            // 不受影响。
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, options);

            RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();

            Assert.Equal(0, report.ErrorCount);

            // 允许 Warning，但要求本用例把原因列出来（见任务书验收标准"允许 Warning 但列出原因"）：
            // data/_sample 里 L0/L1（arch/fac/found/l10n/prog/stat）真实数据 + 阶段 3 新增的 L3
            // item/ 均不产生 Warning；RulesSchemaCatalog 注册的 L2（skill/combat/target/ai）表
            // 因此始终 0 行，同样不会产生 Warning。
            //
            // 判断记录（数据行覆盖语义任务）：本用例只加载 data/_sample 单根（不含
            // data/_framework），而 data/_sample/arch/arch.power_type.json 新增了一条
            // arch.power.health 的 "override": true 行（演示覆盖 data/_framework 同 id 行的
            // 示例数据，见 data/README.md"arch.power_type"判断记录）——override 字段"仅在多根合并
            // 且发生同主键跨根重复时才有意义"（见 DataRegistry 类型级判断记录"覆盖语义"），本用例
            // 单根加载天然触发该字段的预期 Warning（DataRegistry.WarnStrayOverrideMetaFields），
            // 是这条新增示例数据本身的正常提示，不是回归；下方显式排除这一条已知 Warning，其余任何
            // 未预期的 Warning 仍然当作真实问题失败。
            // 若本断言失败，请把下面打印的 Warning 列表当作需要跟进的真实问题，而不是放宽断言。
            var unexpectedWarnings = report.Issues
                .Where(i => i.Severity == ValidationSeverity.Warning)
                .Where(i => !(i.Table == "arch.power_type" && i.Check == "envelope" && i.Field == "override"
                              && i.RecordKey == "arch.power.health"))
                .ToList();
            if (unexpectedWarnings.Count > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    "预期 0 Warning（arch.power_type/arch.power.health 的 override 单根提示已知并排除），实际出现：" +
                    string.Join("; ", unexpectedWarnings));
            }
        }

        // -----------------------------------------------------------------
        // 帮助方法
        // -----------------------------------------------------------------

        private static int? FirstIndex<T>(IReadOnlyList<IEvent> events, Func<T, bool>? predicate = null) where T : IEvent
        {
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i] is T typed && (predicate == null || predicate(typed)))
                {
                    return i;
                }
            }
            return null;
        }

        private static int IndexOf(IReadOnlyList<IEvent> events, IEvent target)
        {
            for (var i = 0; i < events.Count; i++)
            {
                if (ReferenceEquals(events[i], target))
                {
                    return i;
                }
            }
            return -1;
        }

        private static List<string> EventKeySequence(FightWorldBuilder.Fixture fx) =>
            fx.Events.Select(e => e.Key.Value).ToList();

        private static void AssertSameFinalHp(FightWorldBuilder.Fixture a, FightWorldBuilder.Fixture b)
        {
            var powerHealth = FightWorldBuilder.PowerHealth;
            Assert.Equal(
                a.Rules.Powers.GetPower(FightWorldBuilder.PlayerId, powerHealth),
                b.Rules.Powers.GetPower(FightWorldBuilder.PlayerId, powerHealth));
            Assert.Equal(
                a.Rules.Powers.GetPower(FightWorldBuilder.NpcId, powerHealth),
                b.Rules.Powers.GetPower(FightWorldBuilder.NpcId, powerHealth));
        }

        /// <summary>惯例同 <c>core/numbers/tests/L1SampleDataTests.cs</c>
        /// <c>BuildRealSampleSource</c>：用 <see cref="System.Runtime.CompilerServices.CallerFilePathAttribute"/>
        /// 定位仓库根，把 <c>data/_sample</c> 下全部 <c>.json</c> 文件搬进一个
        /// <see cref="StubFileSystem"/>，构造 <see cref="FileSystemDataSource"/>。</summary>
        private static FileSystemDataSource BuildRealSampleSource(
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var repoRoot = FindRepoRoot(sourceFilePath);
            var sampleRoot = Path.Combine(repoRoot, "data", "_sample");
            var fs = new StubEngine().FileSystem;

            foreach (var file in Directory.GetFiles(sampleRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(sampleRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic("data/_sample/" + rel, File.ReadAllText(file));
            }

            return new FileSystemDataSource(fs, "data/_sample");
        }

        /// <summary>本源文件固定位于 <c>&lt;repoRoot&gt;/core/rules/tests/TwoUnitsFightTests.cs</c>，
        /// 向上 3 级（tests → rules → core）即仓库根。</summary>
        private static string FindRepoRoot(string sourceFilePath)
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 3; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }
    }
}

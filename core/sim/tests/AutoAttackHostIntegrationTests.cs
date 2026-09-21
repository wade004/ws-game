using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.SimLoop;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// ADR-0059（消费方反馈第三批第 5 条"普通攻击缺少框架原生执行机制"）：<see
    /// cref="Core.Rules.Combat.AutoAttackHost"/> 的装配级集成测试——全程只经框架真实公开入口（
    /// <c>HeadlessWorldBuilder.Build</c>/<c>StandardPlayerBuilder.Build</c>/
    /// <c>world.Gameplay.Carriers.Rules.AutoAttack</c>/<c>world.Clock.Advance</c>），不直接
    /// 反射/白盒调用任何内部方法，验证"普通攻击是框架原生一等执行路径"这一结论在真实装配根下成立。
    /// <para>
    /// 判断记录（确定性命中表覆盖，见 <see cref="ZeroVarianceHitTableJson"/>）：
    /// <c>core/sim/tests/data</c> 嵌入式数据集自带的 <c>combat.hit_table.default</c> 行有 5% 未命中/
    /// 5% 闪避/5% 暴击概率（见该表 json），会让"伤害精确等于秒伤×一拍常数×百分比"这一断言在个别
    /// 种子下失真。本文件经额外一层 <see cref="InMemoryDataSource"/>（放在 <c>DataSources</c> 数组
    /// 末尾，配 <c>"override": true</c>）把该行覆盖成全零概率的"恒定通常命中，不暴击不闪避不招架"
    /// 分支——覆盖语义见 <c>Core.Foundation.DataRegistry.DataRegistry</c> 判断记录"跨根同主键重复"，
    /// 写法照抄 <c>core/rules/combat/tests/CombatTestSupport.cs</c>.<c>HitTableJson</c> 的
    /// <c>combat.hit_table.default</c> 行。
    /// </para>
    /// </summary>
    public sealed class AutoAttackHostIntegrationTests
    {
        private static readonly Id DefaultQualityId = new Id("item.quality.sim_common");
        private static readonly Id AttackSkillId = new Id("skill.sim_warrior_strike");
        private static readonly Id GoldCurrencyId = new Id("econ.currency.sim_gold");

        private const string ZeroVarianceHitTableJson = @"
        {
            ""table"": ""combat.hit_table_config"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.hit_table.default"", ""override"": true,
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 }
            ]
        }";

        /// <summary>装配一份嵌入式最小仿真数据集世界（惯例同 <c>SimTestWorldFactory.BuildFromEmbeddedDataset</c>），
        /// 额外叠加确定性命中表覆盖层，随后经真实 <c>StandardPlayerBuilder.Build</c> 生成标准 1 级玩家
        /// （含真实装备的主手武器，<c>EquipmentHost.Equip</c> 落地——不是本文件手工构造）。</summary>
        private static HeadlessWorld BuildDeterministicWorld(ulong seed, bool equipStandardGear = true)
        {
            var dataSources = new List<IDataSource>(SimTestWorldFactory.BuildEmbeddedDataSources())
            {
                new InMemoryDataSource().Add("combat.hit_table_config", ZeroVarianceHitTableJson),
            };

            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = dataSources,
                Seed = seed,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                PlayerLevel = 1,
                GameId = SimTestWorldFactory.EmbeddedGameId,
                StepSeconds = SimTestWorldFactory.StepSeconds,
                FailOnUnknownTable = false,
            });

            if (equipStandardGear)
            {
                StandardPlayerBuilder.Build(world, SimTestWorldFactory.EmbeddedClassId, 1, DefaultQualityId);
            }
            return world;
        }

        private static Id SpawnWolf(HeadlessWorld world, Vec2 pos)
        {
            var creatureId = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId, pos, facing: Math.PI);
            world.Spatial.Register(creatureId, pos, 0.5);
            return creatureId;
        }

        private static void SubmitCast(HeadlessWorld world, Id casterId, Id skillId)
        {
            var args = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();
            world.World.SubmitIntent(new Intent(casterId, "cast", args));
        }

        /// <summary>验收标准 1：开启普通攻击、推进恰好一个挥击周期，目标 HP 应下降"武器秒伤 ×
        /// 一拍常数 × 100%"——与既有 <c>weapon_damage_pct</c> 结算公式完全一致，本方法全程用真实
        /// <c>EquipmentHost.GetWeaponDps</c>/<c>GetWeaponAttackIntervalSeconds</c> 现查现算，不写死
        /// 任何裸数。</summary>
        [Fact]
        public void EnableAutoAttack_AdvanceOneSwingInterval_TargetHpDropsByResolverComputedAmount()
        {
            const ulong seed = 20260921_1001UL;
            var world = BuildDeterministicWorld(seed);
            var playerId = SimTestWorldFactory.PlayerId;
            var creatureId = SpawnWolf(world, new Vec2(3, 0));

            var equipment = world.Gameplay.Carriers.Equipment;
            var interval = equipment.GetWeaponAttackIntervalSeconds(playerId);
            Assert.True(interval.HasValue && interval.Value > 0.0, "标准玩家应已装备带 weapon_profile.speed 的主手武器");
            var weaponDps = equipment.GetWeaponDps(playerId);
            Assert.True(weaponDps > 0.0, "标准玩家的主手武器秒伤应 > 0");

            // 一拍常数：本数据集未把 SkillOptions.BudgetRuleId 指向 skill.budget_rule.sim_default，
            // EffectDispatcher.ResolveBeatSeconds 查不到默认 id "skill.budget_rule.default" 时按该方法
            // 文档化缺省 1.0 处理（乘 1 不改变结果）——与既有
            // T_ReviewB_Gap2_WeaponQualityDriftBattleDamageTests 同一口径，非本文件杜撰。
            const double beatSeconds = 1.0;
            var expectedDamage = weaponDps * beatSeconds * 1.0;

            Assert.True(world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var hpBefore));

            var autoAttack = world.Gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(playerId, creatureId);
            autoAttack.SetEnabled(playerId, true);

            world.Clock.Advance(interval.Value);

            Assert.True(world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var hpAfter));
            Assert.Equal(hpBefore - expectedDamage, hpAfter, 9);
        }

        /// <summary>验收标准 2：挥击计时驱动（不是每 tick 结算一次）的精确分界——推进"少于一个周期"
        /// 的累计时长不产生任何结算，累计时长跨过周期边界的那一次推进恰好结算一次。</summary>
        [Fact]
        public void AutoAttack_EnforcesSwingTimer_NoDamageBeforeInterval_ExactlyOneHitAtInterval()
        {
            const ulong seed = 20260921_1002UL;
            var world = BuildDeterministicWorld(seed);
            var playerId = SimTestWorldFactory.PlayerId;
            var creatureId = SpawnWolf(world, new Vec2(3, 0));

            var equipment = world.Gameplay.Carriers.Equipment;
            var interval = equipment.GetWeaponAttackIntervalSeconds(playerId)!.Value;
            var weaponDps = equipment.GetWeaponDps(playerId);
            const double beatSeconds = 1.0;
            var expectedDamage = weaponDps * beatSeconds;
            var stepSeconds = world.Clock.StepSeconds;

            // 数据集前提：武器挥击周期须是世界步长的整数倍且 > 1 步，否则"少于一个周期"这一步无法
            // 落在一个严格小于 interval 又 > 0 的 StepSeconds 整数倍上——用断言而不是静默调整，
            // 保证测试前提随嵌入式数据集变化时能第一时间暴露。
            Assert.True(interval > stepSeconds, "本用例要求武器挥击周期严格大于世界步长（当前嵌入式数据集：主手武器 speed=1.5，世界 StepSeconds=0.5）");

            world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var hp0);

            var autoAttack = world.Gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(playerId, creatureId);
            autoAttack.SetEnabled(playerId, true);

            world.Clock.Advance(interval - stepSeconds);
            world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var hp1);
            Assert.Equal(hp0, hp1, 9);

            world.Clock.Advance(stepSeconds);
            world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var hp2);
            Assert.Equal(hp1 - expectedDamage, hp2, 9);
        }

        /// <summary>验收标准 3：普通攻击击杀与技能击杀落地事件同构——既有 XP/掉落监听器（
        /// <c>CreatureDeathXpListener</c>/<c>CreatureDeathLootListener</c>）对两种死法的效果必须一致，
        /// 不需要为普通攻击特判。判断记录（为什么用"技能击杀 vs 普通攻击击杀"对照而不是直接断言某个
        /// 硬编码经验值）：嵌入式数据集登记的击杀经验来源 id 是 <c>prog.xp_source.sim_kill</c>，与
        /// <c>CreatureDeathXpListener.DefaultKillXpSourceId</c>（<c>"prog.xp_source.kill"</c>）不是
        /// 同一个 id（见 <c>core/sim/core/GrowthSimulation.cs</c> 判断记录"两者不是同一个 id"），
        /// <c>HeadlessWorldOptions</c> 也未开放配置监听器 <c>KillXpSourceId</c> 的口子——两条死法在
        /// 默认装配下经验发放数额因此都恒为该表未登记时的既定"静默跳过"结果，本方法不假设具体数值，
        /// 只断言两条独立路径产生的经验增量彼此相等，这恰是"同构、无特判"的可观测定义：任何一条路径
        /// 若被特殊处理，两者的增量就会出现差异。掉落货币（<c>econ.currency.sim_gold</c>，
        /// <c>loot.table.sim_wolf_l1</c> 该条目 <c>weight_or_chance=1.0</c>/<c>count_range</c>
        /// 恒为 1）同样不假设具体数额（<c>LootHost.ResolveCurrencyOutcome</c> 实际入账数额是
        /// <c>equivalents × IEconomyHost.TryGetGoldBaseAmount(击杀者等级) × 分档倍率 × Multiplier</c>，
        /// 不是掉落条目的 <c>count_range</c> 字面值本身），只断言两条路径产出同一个非零数额。</summary>
        [Fact]
        public void AutoAttackKill_IsIsomorphicToSkillKill_XpAndLootListenersFireIdentically()
        {
            const ulong seed = 20260921_1003UL;

            var (xpDeltaSkill, goldDeltaSkill) = RunToDeathAndMeasure(seed, viaAutoAttack: false);
            var (xpDeltaAutoAttack, goldDeltaAutoAttack) = RunToDeathAndMeasure(seed, viaAutoAttack: true);

            Assert.True(goldDeltaSkill > 0, "技能击杀那条路径应至少入账一笔击杀货币（loot.table.sim_wolf_l1 该条目 weight_or_chance=1.0，恒定产出）");
            Assert.Equal(goldDeltaSkill, goldDeltaAutoAttack);
            Assert.Equal(xpDeltaSkill, xpDeltaAutoAttack);
        }

        private static (long xpDelta, long goldDelta) RunToDeathAndMeasure(ulong seed, bool viaAutoAttack)
        {
            var world = BuildDeterministicWorld(seed);
            var playerId = SimTestWorldFactory.PlayerId;
            var creatureId = SpawnWolf(world, new Vec2(3, 0));
            var equipment = world.Gameplay.Carriers.Equipment;
            var interval = equipment.GetWeaponAttackIntervalSeconds(playerId)!.Value;

            var xpBefore = world.Gameplay.Carriers.Rules.Progression.GetXp(playerId);
            var goldBefore = world.Gameplay.Economy.GetBalance(playerId, GoldCurrencyId);

            const int maxAttempts = 200;
            if (viaAutoAttack)
            {
                var autoAttack = world.Gameplay.Carriers.Rules.AutoAttack;
                autoAttack.SetTarget(playerId, creatureId);
                autoAttack.SetEnabled(playerId, true);
                for (var i = 0; i < maxAttempts && world.Gameplay.Carriers.Units.IsAlive(creatureId); i++)
                {
                    world.Clock.Advance(interval);
                }
            }
            else
            {
                world.Gameplay.Carriers.Rules.Skill.LearnFromBook(playerId, SimTestWorldFactory.EmbeddedSkillBookId, 1);
                for (var i = 0; i < maxAttempts && world.Gameplay.Carriers.Units.IsAlive(creatureId); i++)
                {
                    SubmitCast(world, playerId, AttackSkillId);
                    world.Clock.Advance(SimTestWorldFactory.StepSeconds);
                }
            }

            Assert.False(world.Gameplay.Carriers.Units.IsAlive(creatureId), "本用例要求生物在上限次数内死亡（否则下方比较不成立）");

            var xpAfter = world.Gameplay.Carriers.Rules.Progression.GetXp(playerId);
            var goldAfter = world.Gameplay.Economy.GetBalance(playerId, GoldCurrencyId);
            return (xpAfter - xpBefore, goldAfter - goldBefore);
        }

        /// <summary>验收标准 4：关闭普通攻击应立即停止后续结算（不是"下一次挥击才生效"）；目标死亡后
        /// 状态应回到"未攻击"（<see cref="AutoAttackState.NoTarget"/>，不是 <see
        /// cref="AutoAttackState.Off"/>，也不会继续停在 <see cref="AutoAttackState.Attacking"/> 对着
        /// 尸体）。</summary>
        [Fact]
        public void DisablingAutoAttack_StopsFurtherDamage_AndStateReturnsToNoTargetAfterTargetDies()
        {
            const ulong seed = 20260921_1004UL;
            var world = BuildDeterministicWorld(seed);
            var playerId = SimTestWorldFactory.PlayerId;
            var creatureId = SpawnWolf(world, new Vec2(3, 0));

            var equipment = world.Gameplay.Carriers.Equipment;
            var interval = equipment.GetWeaponAttackIntervalSeconds(playerId)!.Value;
            var weaponDps = equipment.GetWeaponDps(playerId);
            var expectedDamagePerHit = weaponDps * 1.0;

            var autoAttack = world.Gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(playerId, creatureId);
            autoAttack.SetEnabled(playerId, true);
            Assert.Equal(AutoAttackState.Attacking, autoAttack.GetState(playerId));

            world.Clock.Advance(interval);
            world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var hpAfterOneHit);

            autoAttack.SetEnabled(playerId, false);
            Assert.Equal(AutoAttackState.Off, autoAttack.GetState(playerId));

            world.Clock.Advance(interval * 5);
            world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var hpAfterDisabledWindow);
            Assert.Equal(hpAfterOneHit, hpAfterDisabledWindow, 9);

            autoAttack.SetEnabled(playerId, true);
            Assert.Equal(AutoAttackState.Attacking, autoAttack.GetState(playerId));

            const int maxAttempts = 200;
            for (var i = 0; i < maxAttempts && world.Gameplay.Carriers.Units.IsAlive(creatureId); i++)
            {
                world.Clock.Advance(interval);
            }

            Assert.False(world.Gameplay.Carriers.Units.IsAlive(creatureId), "本用例要求目标在上限次数内被打死");

            // 判断记录（为什么击杀之后还要再推进一个 tick 才断言状态）：目标死亡是在杀死它的那一次
            // Update 内部结算产生的（AutoAttackHost.Update 先挥击、伤害落地才可能致死），"目标已死亡"
            // 这一检查在 Update 顶部——要到*下一次* Update 才会观察到"目标现在死了"并清空 TargetId、
            // 回到 NoTarget（同 CastPipeline 一贯的"tick 驱动、非全知同步"惯例，见 AutoAttackHost 类型
            // 判断记录"目标消失/死亡"）。真实游戏循环每帧都在推进时钟，这个"下一 tick 才收敛"的延迟
            // 在实际运行中不可观察；测试里显式补一个小步长使其收敛，不是在掩盖缺陷。
            world.Clock.Advance(world.Clock.StepSeconds);
            Assert.Equal(AutoAttackState.NoTarget, autoAttack.GetState(playerId));
        }

        /// <summary>验收标准 5：运行时不静默降级（AGENTS.md §3）——攻击者既没有装备武器、也没有可用的
        /// 生物模板 <c>attack_interval</c> 回退时，普通攻击不发生任何结算，且经既有
        /// <see cref="ICombatDiagnostics"/> 出口告警恰好一次（同一原因持续存在期间不刷屏）。
        /// <para>
        /// 判断记录（用玩家而不是野狼生物充当"无武器无回退"的攻击者）：野狼生物模板
        /// <c>creature.sim_wolf_l1</c> 挂了 <c>ai_rotation_ref</c>/<c>ai_behavior_ref</c>——一旦生成在
        /// 玩家附近，框架既有的敌对 AI 会独立地对玩家发起技能攻击（与本类型完全无关的另一条既有战斗
        /// 路径），若拿它当攻击者会把"AI 主动技能伤害"混进"本用例本该恒为零的伤害"断言里，产生假
        /// 阳性/假阴性。玩家（<see cref="BuildDeterministicWorld"/> 的 <c>equipStandardGear: false</c>
        /// 重载——跳过 <c>StandardPlayerBuilder.Build</c>，不装备任何主手武器）没有这套 AI 接线，且
        /// 玩家本身不是 <c>Core.Carriers.Unit.CreatureUnit</c>（<see
        /// cref="Core.Carriers.Creature.CreatureAttackIntervalProvider.GetAttackIntervalSeconds"/>
        /// 对非生物单位恒返回 <c>null</c>），"无武器" + "回退查询对非生物单位恒为空" 两个条件同时成立，
        /// 且不引入任何额外的伤害来源——本用例改让玩家攻击野狼，断言野狼（目标）HP 不变。
        /// </para>
        /// </summary>
        [Fact]
        public void AutoAttack_NoWeaponAndNoIntervalFallback_WarnsExactlyOnce_NeverAttacks()
        {
            const ulong seed = 20260921_1005UL;
            var world = BuildDeterministicWorld(seed, equipStandardGear: false);
            var playerId = SimTestWorldFactory.PlayerId;
            // 判断记录（生成在很远的位置）：野狼生物模板挂了敌对 ai_rotation_ref/ai_behavior_ref，一旦
            // 感知到玩家会独立发起技能攻击——那条路径的战斗结算共用同一个 Combat.Diagnostics 出口
            // （"缺失属性按 0 处理"一类既有警告），若生物与玩家距离在其感知范围内，会把与本用例无关
            // 的告警也计入下方的告警计数断言，产生假阳性。摆在感知范围之外，彻底避免这个既有、与
            // 本次改动无关的系统产生噪声，不需要引入子串匹配这类脆弱写法。
            var creatureId = SpawnWolf(world, new Vec2(5000, 0));

            Assert.Null(world.Gameplay.Carriers.Equipment.GetWeaponAttackIntervalSeconds(playerId));

            var diagnostics = Assert.IsType<InMemoryCombatDiagnostics>(world.Gameplay.Carriers.Rules.Combat.Diagnostics);
            var warningsBefore = diagnostics.Warnings.Count;

            world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var creatureHpBefore);

            var autoAttack = world.Gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(playerId, creatureId);
            autoAttack.SetEnabled(playerId, true);

            // 推进若干个 tick（远超嵌入式数据集里任何一件武器的 speed），确认"没有周期就谈不上挥击
            // 到点"不是偶然没触发，而是持续如此。
            for (var i = 0; i < 8; i++)
            {
                world.Clock.Advance(SimTestWorldFactory.StepSeconds);
            }

            world.Gameplay.Carriers.Rules.Powers.TryGetPower(creatureId, WellKnownPowers.Health, out var creatureHpAfter);

            Assert.Equal(creatureHpBefore, creatureHpAfter, 9);
            Assert.Equal(warningsBefore + 1, diagnostics.Warnings.Count);
        }
    }
}

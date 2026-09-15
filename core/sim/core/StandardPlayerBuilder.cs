using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Ai;
using Core.Rules.Common;

namespace Core.Sim
{
    /// <summary>T-N6-3a（ADR-0035 决策 2）：一次 <see cref="StandardPlayerBuilder.Build"/> 调用的完整
    /// 结果。</summary>
    public sealed class StandardPlayer
    {
        public Id UnitId { get; }

        public Id ClassId { get; }

        public int Level { get; }

        public Id QualityId { get; }

        /// <summary><c>LearnFromBook</c> 之后玩家已学到的全部技能（永久 + 已装备授予的并集，见
        /// <see cref="Core.Rules.Skill.SkillHost.GetKnownSkills"/>）。</summary>
        public IReadOnlyList<Id> KnownSkills { get; }

        /// <summary>槽位 id → 已装备物品实例 id；未能找到该槽位+品质对应模板时该槽位缺席（见
        /// <see cref="StandardPlayerBuilder"/> 判断记录"槽位缺模板时的行为"）。</summary>
        public IReadOnlyDictionary<Id, Id> EquippedInstances { get; }

        /// <summary>各非武器装备槽的"反解向量"——按 <see cref="StandardPlayerBuilder"/> 判断记录
        /// "装备实例方案"的口径，等于该槽位承载模板的 <c>stats[]</c> 字面值加上所选词缀的预算反解值之和
        /// （与 <c>EquipmentHost.ApplyGrants</c>/<c>ApplyAffixValues</c> 实际穿戴时执行的运算逐位相同，
        /// 见该类型判断记录），逐属性求和。</summary>
        public IReadOnlyDictionary<Id, IReadOnlyDictionary<Id, double>> ExpectedEquipmentContributionBySlot { get; }

        /// <summary>该等级"标准玩家"的期望属性快照（<see cref="ExpectedStatCalculator.Compute"/>）。</summary>
        public IReadOnlyDictionary<Id, double> ExpectedStatSnapshot { get; }

        /// <summary>玩家单位当前实际属性快照（<c>StatHost.GetStat</c>，与
        /// <see cref="ExpectedStatSnapshot"/> 同一组属性 id）——与期望值的偏差不代表数据错误，见
        /// <see cref="ExpectedStatCalculator"/>/<see cref="StandardPlayerBuilder"/> 判断记录"两条独立
        /// 求值路径互不要求相等"。</summary>
        public IReadOnlyDictionary<Id, double> ActualStatSnapshot { get; }

        /// <summary><c>ActualStatSnapshot[stat] - ExpectedStatSnapshot[stat]</c>，供调用方/测试快速
        /// 查看两条路径的偏差幅度（诊断用途，不是任何断言依据）。</summary>
        public IReadOnlyDictionary<Id, double> Deviation { get; }

        /// <summary>主手武器槽的秒伤（<c>EquipmentHost.GetWeaponDps</c>，数值总纲第 4.4 节武器秒伤曲线
        /// 口径）；未装备任何武器时为 0。</summary>
        public double WeaponDps { get; }

        /// <summary>本次使用的优先级表 id（见 <see cref="StandardPlayerBuilder.Build"/> 参数判断记录）。</summary>
        public Id RotationId { get; }

        internal StandardPlayer(
            Id unitId, Id classId, int level, Id qualityId, IReadOnlyList<Id> knownSkills,
            IReadOnlyDictionary<Id, Id> equippedInstances,
            IReadOnlyDictionary<Id, IReadOnlyDictionary<Id, double>> expectedEquipmentContributionBySlot,
            IReadOnlyDictionary<Id, double> expectedStatSnapshot,
            IReadOnlyDictionary<Id, double> actualStatSnapshot,
            IReadOnlyDictionary<Id, double> deviation,
            double weaponDps,
            Id rotationId)
        {
            UnitId = unitId;
            ClassId = classId;
            Level = level;
            QualityId = qualityId;
            KnownSkills = knownSkills;
            EquippedInstances = equippedInstances;
            ExpectedEquipmentContributionBySlot = expectedEquipmentContributionBySlot;
            ExpectedStatSnapshot = expectedStatSnapshot;
            ActualStatSnapshot = actualStatSnapshot;
            Deviation = deviation;
            WeaponDps = weaponDps;
            RotationId = rotationId;
        }
    }

    /// <summary>
    /// T-N6-3a（ADR-0035 决策 2；落地改动点清单 N3"标准玩家生成器：等级 + 职业 → 经 BudgetSolver 生成
    /// 整套标准装 → SkillHost.LearnFromBook 填技能 → 挂 ai.rotation 策略"）：标准玩家生成器——给定
    /// 一个已装配的 <see cref="HeadlessWorld"/>（其 <see cref="HeadlessWorld.Player"/> 须已以
    /// <paramref name="level"/>/<paramref name="classId"/> 注册，见 <see cref="Build"/> 判断记录"① 玩家
    /// 单位以等级 L 注册"）与目标（职业、等级、期望品质），学满技能书、给全部装备位穿上按预算反解生成的
    /// "标准装"、校验优先级表能选出可施放技能，返回完整快照。
    /// <para>
    /// 判断记录（装备实例方案：模板 + 词缀，不新增实例级属性存储）：07 第 1.3/1.6 节与 <c>ItemInstance</c>
    /// 类型顶部判断记录（ADR-0032 决策 8"物品实例只存身份……不存任何算出的属性数值"）已经把"实例级
    /// 属性表达"这条路关死——<c>ItemInstance</c> 只有 <c>InstanceId</c>/<c>TemplateId</c>/<c>Count</c>/
    /// <c>Quality</c>/<c>Affixes</c> 五个身份字段，运行期由 <see cref="EquipmentHost.ApplyGrants"/> 按
    /// 模板 <c>stats[]</c>（字面值，逐条 <c>AddModifier</c>）+ <see cref="EquipmentHost.ApplyAffixValues"/>
    /// （对 <see cref="Affixes"/> 逐条经 <see cref="IBudgetSolver.Solve"/> 反解）重新算出贡献，不接受
    /// 调用方注入任意自定义数值。本类型因此选择"嵌入数据集中该槽位、该品质、物品等级 ≤ E(L) 最近一档的
    /// 模板作载体"（任务书指定路径），并让"反解向量"的定义**与 <c>EquipmentHost</c> 实际执行的运算
    /// 完全同构而不是另起一套独立公式——本类型对每个选中的词缀调用与 <c>ApplyAffixValues</c> 完全相同的
    /// <see cref="IBudgetSolver.Solve"/> 归一化/份额换算，逐条累加模板自身 <c>stats[]</c> 字面值
    /// 得到"反解向量"，再与 <c>StatHost.GetModifiers(unitId, stat)</c> 里来源为该实例 id 的全部
    /// <c>Flat</c> 修正求和比对——两者必然逐位相等（同一份输入喂给同一份公式，不是近似对齐），满足任务
    /// 书"装备贡献 == 反解向量"的验收断言，而不是"直接用模板自带属性糊弄"（模板可能压根没有
    /// <c>stats[]</c>，本类型的"反解向量"包含词缀部分，不是只看模板字面值）。
    /// </para>
    /// <para>
    /// 判断记录（本类型的"反解向量"与 <see cref="ExpectedStatCalculator"/> 的"期望属性"是两条独立求值
    /// 路径，互不要求相等）：<see cref="ExpectedStatCalculator"/> 是"标准玩家"这一简化抽象自己的期望值
    /// 曲线（对全部非武器装备槽用统一 <c>statMix</c> 反解求和，见该类型判断记录），本类型是"实际生成并
    /// 穿戴一件可玩的装备实例"这一具体操作（受限于嵌入数据集里已有哪些模板/词缀，只能取最接近的一档，
    /// 词缀池也是固定枚举而非连续可调）。两者的数值一般不相等，<see cref="StandardPlayer.Deviation"/>
    /// 只是诊断信息，不是任何断言依据——这与 04/07/ADR-0035 原文"期望值曲线用于内容平衡校验、标准玩家
    /// 生成器用于仿真驱动"两个不同用途的定位一致，契约本就没有要求两者数值相等。
    /// </para>
    /// <para>
    /// 判断记录（槽位缺模板时的行为）：嵌入数据集/游戏数据未必对每个槽位×品质组合都登记了模板（如只有
    /// common 品质的某个槽位没有 rare 版本）。本类型对此不抛异常——跳过该槽位（不装备、不计入
    /// <see cref="StandardPlayer.EquippedInstances"/>/<see cref="StandardPlayer.ExpectedEquipmentContributionBySlot"/>），
    /// 保证调用方可以在数据尚不完整的游戏层直接使用本生成器，只是产出的"标准玩家"少几件装备；调用方
    /// 如需要强制"全部槽位必有装备"的验收断言，应在游戏内容层面补齐模板数据，不是本类型的校验职责
    /// （04 第 5 节数值类校验项分级表未列出这一条）。
    /// </para>
    /// <para>
    /// 判断记录（优先级表 id 默认值：<c>arch.class.&lt;name&gt;</c> → <c>ai.rotation.&lt;name&gt;</c>
    /// 命名约定）：ADR-0035 决策 2 原文"策略为 <c>ai.rotation</c> 优先级表"未规定具体 id 怎么从职业
    /// 推出。<c>sim.scenario</c> 只登记了"哪个职业+哪个基准等级"，没有登记"用哪张优先级表"这一维度
    /// （见 <c>SimSchemas.cs</c> 对 <c>sim.scenario.player</c> 字段表）——本模块没有其它权威数据来源可
    /// 查，因此按"把 <c>arch.class.</c> 前缀替换成 <c>ai.rotation.</c>"这一朴素命名约定推断默认值
    /// （嵌入数据集 <c>arch.class.sim_warrior</c> ↔ <c>ai.rotation.sim_warrior</c> 确实遵循这一约定，
    /// 见 <c>core/sim/tests/data/ai/ai.rotation.json</c>）；调用方可显式传入 <paramref
    /// name="rotationId"/> 覆盖，游戏层若不遵循这一命名约定必须显式传参，本类型不做进一步猜测。
    /// </para>
    /// </summary>
    public static class StandardPlayerBuilder
    {
        public static readonly Id DefaultBudgetCurveId = new Id("item.budget.default");

        /// <param name="world">已装配的无头世界；<see cref="HeadlessWorld.Player"/> 须已以
        /// <paramref name="level"/>/<paramref name="classId"/> 注册（见类型判断记录"① 玩家单位以等级
        /// L 注册"——由调用方经 <see cref="HeadlessWorldOptions.PlayerLevel"/>/<see
        /// cref="HeadlessWorldOptions.PlayerClassId"/> 在装配 <paramref name="world"/> 时完成，本方法
        /// 不重复注册）；<see cref="HeadlessWorld.AnchorTable"/> 须非空（数据源须含 <c>sim.anchor</c>）。</param>
        /// <param name="classId">标准玩家职业，须与 <paramref name="world"/> 装配时的
        /// <c>PlayerClassId</c> 一致（防御性校验，见 <see cref="Build"/> 实现）。</param>
        /// <param name="level">标准玩家等级，须与 <paramref name="world"/> 装配时的 <c>PlayerLevel</c>
        /// 一致。</param>
        /// <param name="qualityId">标准玩家期望装备品质，指向 <c>item.quality_definition</c>。</param>
        /// <param name="rotationId">优先级表 id；缺省按类型判断记录"优先级表 id 默认值"命名约定推断。</param>
        /// <param name="budgetSolver">预算反解实现，缺省 <c>new BudgetSolver()</c>。</param>
        /// <param name="budgetCurveId"><c>item.budget_curve</c> 曲线 id，缺省 <see cref="DefaultBudgetCurveId"/>。</param>
        public static StandardPlayer Build(
            HeadlessWorld world,
            Id classId,
            int level,
            Id qualityId,
            Id? rotationId = null,
            IBudgetSolver? budgetSolver = null,
            Id? budgetCurveId = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.AnchorTable == null)
            {
                throw new InvalidOperationException(
                    "StandardPlayerBuilder.Build 要求 world.AnchorTable 非空——数据源须含 sim.anchor（ADR-0035 决策 2）。");
            }

            var registry = world.Registry;
            var solver = budgetSolver ?? new BudgetSolver();
            var curveId = budgetCurveId ?? DefaultBudgetCurveId;
            var playerId = world.Player.EntityId;

            var actualLevel = world.Gameplay.Carriers.Rules.Progression.GetLevel(playerId);
            if (actualLevel != level)
            {
                throw new ArgumentException(
                    $"StandardPlayerBuilder.Build：world.Player 的当前等级（{actualLevel}）与传入的 level（{level}）不一致——" +
                    "调用方须在装配 HeadlessWorld 时经 HeadlessWorldOptions.PlayerLevel 指定相同等级（见类型判断记录①）。",
                    nameof(level));
            }

            var classRecord = registry.Get("arch.class", classId)
                ?? throw new ArgumentException($"arch.class 未登记 \"{classId}\"", nameof(classId));

            // ② LearnFromBook：技能书按该等级可学的全部技能填满。
            var knownSkills = Array.Empty<Id>() as IReadOnlyList<Id>;
            if (classRecord.TryGetId("skill_book_ref", out var bookId))
            {
                world.Gameplay.Carriers.Rules.Skill.LearnFromBook(playerId, bookId, level);
                knownSkills = world.Gameplay.Carriers.Rules.Skill.GetKnownSkills(playerId);
            }

            // ③ 每个装备槽位：物品等级 = E(L)，生成并装备"标准装"。
            var anchorRow = world.AnchorTable.Get(level);
            var itemLevel = (int)Math.Round(anchorRow.ExpectedItemLevel, MidpointRounding.AwayFromZero);
            if (itemLevel < 1) itemLevel = 1;

            world.Gameplay.Carriers.Inventory.RegisterUnit(playerId);

            var equippedInstances = new Dictionary<Id, Id>();
            var expectedContributionBySlot = new Dictionary<Id, IReadOnlyDictionary<Id, double>>();
            var weaponDps = 0.0;

            var qualityRecord = registry.Get("item.quality_definition", qualityId);
            var affixCount = qualityRecord != null && qualityRecord.TryGetInt("affix_count", out var ac) ? (int)ac : 0;

            foreach (var slot in registry.GetAll("item.slot_definition"))
            {
                var isEquipment = !slot.TryGetBool("is_equipment", out var eqFlag) || eqFlag;
                if (!isEquipment)
                {
                    continue;
                }

                var slotId = slot.GetId("id");
                var carrier = FindCarrierTemplate(registry, slotId, qualityId, itemLevel);
                if (carrier == null)
                {
                    continue;
                }

                var templateId = carrier.Id!.Value;
                var carrierItemLevel = (int)carrier.GetInt("item_level");

                var chosenAffixes = new List<Id>();
                if (affixCount > 0 && carrier.TryGetIdList("affixes", out var whitelist))
                {
                    for (var i = 0; i < whitelist.Count && chosenAffixes.Count < affixCount; i++)
                    {
                        chosenAffixes.Add(whitelist[i]);
                    }
                }

                world.Gameplay.Carriers.Inventory.AddItem(playerId, templateId, 1, qualityId, chosenAffixes);
                var instanceId = FindLatestInstance(world, playerId, templateId, qualityId, chosenAffixes);

                world.Gameplay.Carriers.Equipment.Equip(playerId, instanceId, slotId);
                equippedInstances[slotId] = instanceId;

                var contribution = ComputeExpectedContribution(
                    registry, solver, curveId, carrier, carrierItemLevel, qualityId, slotId, chosenAffixes);
                expectedContributionBySlot[slotId] = contribution;
            }

            weaponDps = world.Gameplay.Carriers.Equipment.GetWeaponDps(playerId);

            // ④ 校验：RotationEvaluator 能用指定优先级表选出可施放技能。
            var resolvedRotationId = rotationId ?? InferRotationId(classId);
            var rotationEvaluator = new RotationEvaluator(
                registry, world.Gameplay.Carriers.Rules.Skill, world.Gameplay.Carriers.Rules.ExprHostFactory,
                world.Gameplay.Carriers.Rules.ExprSchema);
            if (!rotationEvaluator.HasRotation(resolvedRotationId))
            {
                throw new InvalidOperationException(
                    $"StandardPlayerBuilder.Build：ai.rotation \"{resolvedRotationId}\" 未登记（见类型判断记录" +
                    "\"优先级表 id 默认值\"，游戏层若不遵循 arch.class.<name> → ai.rotation.<name> 命名约定，" +
                    "须显式传入 rotationId）。");
            }

            // ⑤ 返回快照：期望属性、实际属性、偏差。
            var calculator = new ExpectedStatCalculator(registry, world.AnchorTable, classId, qualityId, solver, curveId);
            var expectedStats = calculator.Compute(level);
            var actualStats = new Dictionary<Id, double>();
            var deviation = new Dictionary<Id, double>();
            foreach (var kv in expectedStats)
            {
                var actual = world.Gameplay.Carriers.Rules.Stats.GetStat(playerId, kv.Key);
                actualStats[kv.Key] = actual;
                deviation[kv.Key] = actual - kv.Value;
            }

            return new StandardPlayer(
                playerId, classId, level, qualityId, knownSkills, equippedInstances,
                expectedContributionBySlot, expectedStats, actualStats, deviation, weaponDps, resolvedRotationId);
        }

        private static DataRecord? FindCarrierTemplate(IDataRegistryView registry, Id slotId, Id qualityId, int itemLevel)
        {
            DataRecord? best = null;
            DataRecord? fallbackMin = null;
            foreach (var t in registry.GetAll("item.template"))
            {
                if (!t.TryGetId("slot", out var s) || !s.Equals(slotId)) continue;
                if (!t.TryGetId("quality", out var q) || !q.Equals(qualityId)) continue;

                var tLevel = (int)t.GetInt("item_level");
                if (tLevel <= itemLevel && (best == null || tLevel > (int)best.GetInt("item_level")))
                {
                    best = t;
                }
                if (fallbackMin == null || tLevel < (int)fallbackMin.GetInt("item_level"))
                {
                    fallbackMin = t;
                }
            }

            return best ?? fallbackMin;
        }

        /// <summary>在刚 <c>AddItem</c> 之后按 (模板, 品质, 词缀集合) 找回本次新增/续填到的实例——
        /// 判断记录：<see cref="Core.Carriers.Item.InventoryHost.AddItem(Id, Id, int, Id?,
        /// IReadOnlyList{Id})"/> 不直接返回实例 id（<see cref="Core.Carriers.Item.ItemAddedEvent"/>
        /// 才携带，本方法不订阅事件、避免额外事件总线耦合），改按身份匹配从 <c>ListItems</c> 里挑一条——
        /// 本方法每次只为一个全新槽位调用一次 <c>AddItem(count:1)</c>，同一 (模板,品质,词缀) 组合在
        /// 本次调用之前不可能已存在（每个槽位对应的模板 id 各不相同，见 <see cref="FindCarrierTemplate"/>
        /// 按槽位过滤），取匹配到的最后一条（<c>ListItems</c> 顺序与背包内部列表顺序一致，最新添加的
        /// 排在最后）足够可靠，不需要引入更复杂的"添加前后差集"追踪。</summary>
        private static Id FindLatestInstance(HeadlessWorld world, Id playerId, Id templateId, Id qualityId, IReadOnlyList<Id> affixes)
        {
            var items = world.Gameplay.Carriers.Inventory.ListItems(playerId);
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (!item.TemplateId.Equals(templateId) || !item.Quality.Equals(qualityId))
                {
                    continue;
                }
                if (item.Affixes.Count != affixes.Count)
                {
                    continue;
                }
                var matches = true;
                for (var j = 0; j < affixes.Count; j++)
                {
                    if (!item.Affixes[j].Equals(affixes[j]))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return item.InstanceId;
                }
            }

            throw new InvalidOperationException(
                $"StandardPlayerBuilder：AddItem(templateId={templateId}, quality={qualityId}) 之后未能在背包中找回对应实例。");
        }

        /// <summary>与 <see cref="Core.Carriers.Item.EquipmentHost.ApplyGrants"/>/<c>ApplyAffixValues</c>
        /// 完全同构的运算（见类型判断记录"装备实例方案"）：模板自身 <c>stats[]</c> 字面值 + 每条词缀
        /// 经 <see cref="IBudgetSolver.Solve"/> 反解值（<c>shareOfBudget = budget_share × Σratio</c>、
        /// <c>statMix</c> 按 <c>ratio/Σratio</c> 归一化，逐条累加），供调用方与
        /// <c>StatHost.GetModifiers(unitId, stat)</c> 按来源过滤后求和比对。</summary>
        private static IReadOnlyDictionary<Id, double> ComputeExpectedContribution(
            IDataRegistryView registry, IBudgetSolver solver, Id budgetCurveId,
            DataRecord template, int itemLevel, Id qualityId, Id slotId, IReadOnlyList<Id> affixIds)
        {
            var result = new Dictionary<Id, double>();

            if (template.TryGetArray("stats", out var stats))
            {
                foreach (var raw in stats)
                {
                    if (raw is Core.Foundation.Common.Json.JsonObject obj &&
                        obj.TryGetValue("stat", out var statRaw) && statRaw is Core.Foundation.Common.Json.JsonString statStr &&
                        obj.TryGetValue("value", out var valueRaw) && valueRaw is Core.Foundation.Common.Json.JsonNumber valueNum)
                    {
                        var statId = new Id(statStr.Value);
                        result.TryGetValue(statId, out var existing);
                        result[statId] = existing + valueNum.Value;
                    }
                }
            }

            foreach (var affixId in affixIds)
            {
                var affixRecord = registry.Get("item.affix", affixId);
                if (affixRecord == null)
                {
                    continue;
                }

                var budgetShare = affixRecord.TryGetNumber("budget_share", out var bs) ? bs : 0.0;
                if (budgetShare <= 0.0)
                {
                    continue;
                }

                if (!affixRecord.TryGetArray("stat_mix", out var mixArray) || mixArray.Count == 0)
                {
                    continue;
                }

                var rawMix = new List<(Id Stat, double Ratio)>();
                double ratioSum = 0;
                foreach (var raw in mixArray)
                {
                    if (raw is Core.Foundation.Common.Json.JsonObject obj &&
                        obj.TryGetValue("stat", out var statRaw) && statRaw is Core.Foundation.Common.Json.JsonString statStr &&
                        obj.TryGetValue("ratio", out var ratioRaw) && ratioRaw is Core.Foundation.Common.Json.JsonNumber ratioNum)
                    {
                        rawMix.Add((new Id(statStr.Value), ratioNum.Value));
                        ratioSum += ratioNum.Value;
                    }
                }

                if (rawMix.Count == 0 || ratioSum <= 0.0)
                {
                    continue;
                }

                var normalizedMix = rawMix.Select(e => (e.Stat, e.Ratio / ratioSum)).ToList();
                var shareOfBudget = Math.Min(1.0, budgetShare * ratioSum);
                if (shareOfBudget <= 0.0)
                {
                    continue;
                }

                var solved = solver.Solve(itemLevel, qualityId, slotId, normalizedMix, budgetCurveId, shareOfBudget, registry);
                foreach (var kv in solved.Values)
                {
                    result.TryGetValue(kv.Key, out var existing);
                    result[kv.Key] = existing + kv.Value;
                }
            }

            return result;
        }

        private static Id InferRotationId(Id classId)
        {
            const string classPrefix = "arch.class.";
            var value = classId.Value;
            var name = value.StartsWith(classPrefix, StringComparison.Ordinal)
                ? value.Substring(classPrefix.Length)
                : value;
            return new Id("ai.rotation." + name);
        }
    }
}

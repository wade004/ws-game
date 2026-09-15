using System;
using System.Collections.Generic;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-4（ADR-0035 决策 3；数值总纲第 4.2 节）：<see cref="ICreatureLevelScaler"/> 的锚点表实现——
    /// 把 <c>creature.template.base_stats</c> 从模板登记等级按 <see cref="AnchorTable"/> 换算到任意
    /// 目标出生等级，供越级矩阵仿真"同一个生物模板、跨等级出生"（<c>sim.scenario.opponent.level_offsets</c>）
    /// 使用（该矩阵场景的 <c>opponent.creature_id</c> 全程只登记一个模板 id，靠本类型把它换算到
    /// <c>player.level + offset</c> 的任意等级出生，见 <c>core/sim/tests/data/sim/sim.scenario.json</c>
    /// 的 <c>sim.scenario.sim_arena_matrix</c> 一行）。
    /// <para>
    /// 判断记录（换算规则：一个"血量"槽位 + 其余全部按"伤害"槽位处理，任务书原文三分法的落地）：
    /// 任务书原文"血量 ∝ DPS(L)×TTK(L)，伤害 ∝ HP(L)÷TTD(L)，其它属性按模板等级→目标等级的同比例或
    /// 保持"——本实现按以下两条规则处理 <paramref name="baseStats"/> 里的每一个词条（不再区分出第三类
    /// "其它属性"）：
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description><b>血量槽位</b>：经 <see cref="ResolveHealthStat"/> 从 <c>arch.power_type</c> 查
    /// <see cref="Core.Rules.Common.WellKnownPowers.Health"/>（<c>arch.power.health</c>）一行的
    /// <c>max_source.stat</c>（<c>kind:"stat"</c> 时的资源上限来源属性，见该表字段），按
    /// <c>(DPS(target)×TTK(target)) ÷ (DPS(source)×TTK(source))</c> 缩放——数值总纲 4.2 节
    /// "怪物血量(L,分档) = DPS(L)×TTK(L)×分档属性倍率"公式本身算的是最终血量，这里换算的是驱动它的
    /// 基础属性（本数据集 <c>hp(L)=stamina(L)</c> 恒等式下即 <c>stat.stamina</c>），分档倍率由
    /// <see cref="CreatureFactory.ApplyStats"/> 在本方法返回值之后按既有顺序统一相乘，本方法不重复
    /// 处理。</description>
    /// </item>
    /// <item>
    /// <description><b>其余全部属性</b>（含"伤害"槽位与任务书所说的"其它属性"）：统一按
    /// <c>(HP(target)÷TTD(target)) ÷ (HP(source)÷TTD(source))</c> 缩放——数值总纲 4.2 节"怪物伤害
    /// (L,分档) = HP(L)÷TTD(L)×分档属性倍率"公式的驱动比值。选择"其它属性并入伤害槽位同一条规则"
    /// 而不是再拆出一条"保持不变"的第三分支：<see cref="ICreatureLevelScaler"/> 只拿到一份扁平的
    /// <c>StatKey→Number</c> 字典，框架层不知道、也不应该硬编码某个具体游戏"伤害由哪个基础属性驱动"
    /// （不同游戏的攻击属性命名不同——本数据集是 <c>stat.strength</c>，但这是内容决定，不是架构
    /// 决定）；"血量槽位"能被精确识别是因为它有唯一的、数据驱动的锚点（<c>arch.power_type</c> 的
    /// <c>max_source.stat</c>），而"伤害槽位"没有同等强度的通用锚点（技能可以从 <c>base_curve_ref</c>
    /// 按施法者等级直接取值、完全不经过基础属性缩放——本数据集 <c>skill.sim_creature_bite</c> 正是
    /// 如此，见 <c>core/sim/tests/data/skill/skill.base_curve.json</c>，此时本方法对
    /// <c>stat.strength</c> 的缩放结果对该技能的实际伤害输出无可观测影响）。把"其它属性"统一并入
    /// "伤害比值"缩放，是在"保持不变"（对确实由基础属性驱动伤害的游戏内容而言，等级差越大、强度
    /// 差距会被低估）与"伤害比值缩放"（对不受影响的属性至多是一次无害的数值改写）两个选项之间，
    /// 选择伤害风险更小、更符合"生物按等级变强"直觉的一个，并在此明确记录：这是一条不精确的启发式
    /// 缩放，不是任何游戏内容的精确复刻，游戏层如需要更精确的按属性分类缩放，应实现自己的
    /// <see cref="ICreatureLevelScaler"/>。</description>
    /// </item>
    /// </list>
    /// <para>
    /// 判断记录（越界夹到 <see cref="AnchorTable.MaxLevel"/>/最低 1 级）：越级矩阵允许
    /// <c>player.level + offset</c> 超出 <see cref="AnchorTable"/> 的登记范围（如满级 20 + 偏移 +5 =
    /// 25），沿用 <c>AnchorTableSkillBudgetAnchorProvider.GetAnchorDps</c> 既有先例"越界夹到
    /// MaxLevel"，下界同理夹到 1（<c>AnchorTable</c> 从 1 级起连续登记，见该类型判断记录）——不外推
    /// 曲线，只复用边界那一级的锚点值。
    /// </para>
    /// </summary>
    public sealed class AnchorCreatureLevelScaler : ICreatureLevelScaler
    {
        private readonly AnchorTable _anchors;
        private readonly Id? _healthStatId;

        /// <param name="registry">已装载的数据只读视图，供解析 <c>arch.power_type</c> 找到血量槽位对应
        /// 的基础属性。</param>
        /// <param name="anchors">已构造的 <c>sim.anchor</c> 类型化读取。</param>
        public AnchorCreatureLevelScaler(IDataRegistryView registry, AnchorTable anchors)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
            _healthStatId = ResolveHealthStat(registry);
        }

        public IReadOnlyDictionary<Id, double> ScaleBaseStats(
            CreatureTemplate template, int templateLevel, int targetLevel, IReadOnlyDictionary<Id, double> baseStats)
        {
            if (baseStats == null) throw new ArgumentNullException(nameof(baseStats));

            var sourceAnchor = _anchors.Get(ClampToTable(templateLevel));
            var targetAnchor = _anchors.Get(ClampToTable(targetLevel));

            var healthRatio = SafeRatio(
                targetAnchor.Dps * targetAnchor.TtkSeconds,
                sourceAnchor.Dps * sourceAnchor.TtkSeconds);
            var damageRatio = SafeRatio(
                targetAnchor.Hp / targetAnchor.TtdSeconds,
                sourceAnchor.Hp / sourceAnchor.TtdSeconds);

            var result = new Dictionary<Id, double>();
            foreach (var kv in baseStats)
            {
                var ratio = _healthStatId.HasValue && kv.Key.Equals(_healthStatId.Value) ? healthRatio : damageRatio;
                result[kv.Key] = kv.Value * ratio;
            }

            return result;
        }

        private int ClampToTable(int level)
        {
            if (_anchors.MaxLevel <= 0)
            {
                // AnchorCreatureLevelScaler 只应在 HeadlessWorldBuilder 探测到 sim.anchor 有行时
                // 才被装配（见该类型判断记录"数据源含 sim.anchor 才自动装配"一贯口径），本分支是
                // 防御性兜底（调用方绕过装配根、拿一张空锚点表直接构造本类型）。
                throw new InvalidOperationException("AnchorCreatureLevelScaler：AnchorTable 为空（MaxLevel=0），无法换算。");
            }

            if (level < 1) return 1;
            if (level > _anchors.MaxLevel) return _anchors.MaxLevel;
            return level;
        }

        private static double SafeRatio(double target, double source)
        {
            // source 恒 > 0（AnchorRow.Dps/Hp/TtkSeconds/TtdSeconds 均为正的时长/强度量，
            // SimAnchorValidationRule 未对此加正数约束，但嵌入数据集/框架示例数据全部为正——
            // 防御性兜底，source<=0 时不缩放，保留原值，避免除零产生 NaN/Infinity 污染下游属性）。
            if (source <= 0 || double.IsNaN(source) || double.IsInfinity(source))
            {
                return 1.0;
            }
            var ratio = target / source;
            return double.IsNaN(ratio) || double.IsInfinity(ratio) ? 1.0 : ratio;
        }

        /// <summary>从 <c>arch.power_type</c> 查 <see cref="WellKnownPowers.Health"/> 一行的
        /// <c>max_source.stat</c>（仅当 <c>max_source.kind == "stat"</c> 时有意义）。未登记该行、
        /// 或该行的 <c>max_source.kind</c> 不是 <c>"stat"</c>（如固定值 <c>"fixed"</c>）时返回
        /// <c>null</c>——本方法据此把"哪个基础属性驱动血量"这一问题完全交给数据回答，不硬编码任何
        /// 具体属性名（如 <c>stat.stamina</c>，那是本数据集的内容选择，不是架构约定）。</summary>
        private static Id? ResolveHealthStat(IDataRegistryView registry)
        {
            var record = registry.Get("arch.power_type", WellKnownPowers.Health);
            if (record == null)
            {
                return null;
            }

            if (!record.TryGetObject("max_source", out var maxSource))
            {
                return null;
            }

            if (!maxSource.TryGetValue("kind", out var kindRaw) ||
                kindRaw is not Core.Foundation.Common.Json.JsonString kindStr ||
                kindStr.Value != "stat")
            {
                return null;
            }

            if (!maxSource.TryGetValue("stat", out var statRaw) ||
                statRaw is not Core.Foundation.Common.Json.JsonString statStr)
            {
                return null;
            }

            return new Id(statStr.Value);
        }
    }
}

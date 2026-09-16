using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-2a：<c>sim.anchor</c> 一行的强类型只读视图（数值总纲第 4.1 节 HP(L)/DPS(L)/TTK(L)/
    /// TTD(L)/E(L)/T(L)/G(L)/Q(L) 八个锚点量）。由 <see cref="AnchorTable"/> 内部构造，字段与
    /// <see cref="SimSchemas.Anchor"/> 的登记顺序一一对应。
    /// </summary>
    public readonly struct AnchorRow
    {
        public int Level { get; }

        /// <summary>期望生命 HP(L)。</summary>
        public double Hp { get; }

        /// <summary>期望秒伤 DPS(L)。</summary>
        public double Dps { get; }

        /// <summary>击杀同级普通怪时长 TTK(L)，单位秒。</summary>
        public double TtkSeconds { get; }

        /// <summary>被同级普通怪击杀时长 TTD(L)，单位秒。</summary>
        public double TtdSeconds { get; }

        /// <summary>期望装备等级曲线 E(L)（07 第 1.2 节）。</summary>
        public double ExpectedItemLevel { get; }

        /// <summary>目标每级时长 T(L)，单位秒。</summary>
        public double LevelDurationSeconds { get; }

        /// <summary>期望击杀间隔 G(L)，单位秒。</summary>
        public double KillIntervalSeconds { get; }

        /// <summary>任务与探索经验占比 Q(L)，取值 [0,1]。</summary>
        public double QuestShare { get; }

        /// <summary>内容作者说明原文，可选。</summary>
        public string? Note { get; }

        internal AnchorRow(
            int level, double hp, double dps, double ttkSeconds, double ttdSeconds,
            double expectedItemLevel, double levelDurationSeconds, double killIntervalSeconds,
            double questShare, string? note)
        {
            Level = level;
            Hp = hp;
            Dps = dps;
            TtkSeconds = ttkSeconds;
            TtdSeconds = ttdSeconds;
            ExpectedItemLevel = expectedItemLevel;
            LevelDurationSeconds = levelDurationSeconds;
            KillIntervalSeconds = killIntervalSeconds;
            QuestShare = questShare;
            Note = note;
        }
    }

    /// <summary>
    /// T-N6-2a：<c>sim.anchor</c> 的类型化只读读取（ADR-0035 决策 4）——数值仿真骨架与内容工具按
    /// 等级查锚点值时，不必各自重新解析 <see cref="DataRecord"/>。
    /// <para>
    /// 判断记录（构造期一次性解析、不做惰性读取）：<c>sim.anchor</c> 全表通常只有几十行（每级一行，
    /// 满级封顶），一次性 <c>view.GetAll("sim.anchor")</c> 解析进内存字典的成本可忽略；构造期解析还
    /// 让 <see cref="TryGet"/>/<see cref="Get"/> 保持纯查表、不重复触碰 <see cref="IDataRegistryView"/>，
    /// 与 <see cref="Core.Numbers.Progression"/> 等既有模块"运行期宿主装配一次、后续只读查表"的一贯
    /// 做法一致。
    /// </para>
    /// <para>
    /// 判断记录（构造期不做 <c>SimAnchorValidationRule</c> 已覆盖的连续性/单调性校验）：调用方（
    /// <see cref="HeadlessWorldBuilder"/>）总是在 <see cref="IDataRegistry.LoadAll(IReadOnlyList{IDataSource})"/>
    /// 通过校验（<c>report.IsBlocking</c> 为 false）之后才可能走到构造 <see cref="AnchorTable"/> 这一步
    /// （见该类型判断记录"数据未通过校验时直接抛异常，不会正常返回"）——届时 <c>SimAnchorValidationRule</c>
    /// 已经保证等级连续无缺口、无重复（阻断级），本类型据此可以安全假设"按 <see cref="Level"/> 建的
    /// 字典键值一一对应、无需再次防御性检查"，不重复造一遍已经在加载期做过的校验。
    /// </para>
    /// </summary>
    public sealed class AnchorTable
    {
        private readonly Dictionary<int, AnchorRow> _byLevel;

        /// <summary>表中登记的最高等级；表为空时为 0。</summary>
        public int MaxLevel { get; }

        /// <summary>从已加载的 <paramref name="registry"/> 读取 <c>sim.anchor</c> 全部记录并解析为
        /// 类型化行。<paramref name="registry"/> 必须已通过 <see cref="IDataRegistry.LoadAll()"/>/
        /// <see cref="IDataRegistry.LoadAll(IReadOnlyList{IDataSource})"/>——本构造函数只读查询，不
        /// 触发加载。</summary>
        public AnchorTable(IDataRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _byLevel = new Dictionary<int, AnchorRow>();
            var maxLevel = 0;

            foreach (var record in registry.GetAll("sim.anchor"))
            {
                var level = (int)record.GetInt("level");
                var row = new AnchorRow(
                    level,
                    hp: record.GetNumber("hp"),
                    dps: record.GetNumber("dps"),
                    ttkSeconds: record.GetNumber("ttk_seconds"),
                    ttdSeconds: record.GetNumber("ttd_seconds"),
                    expectedItemLevel: record.GetNumber("expected_item_level"),
                    levelDurationSeconds: record.GetNumber("level_duration_seconds"),
                    killIntervalSeconds: record.GetNumber("kill_interval_seconds"),
                    questShare: record.GetNumber("quest_share"),
                    note: record.TryGetString("note", out var note) ? note : null);

                // 判断记录：同一 level 出现多条记录本应已被 SimAnchorValidationRule 判为阻断错误、
                // 调用方走不到这里；万一调用方绕过校验直接注入数据（如测试装配未接线该规则），后写
                // 者覆盖先写者，不抛异常——与本类型"不重复造一遍加载期校验"的既有判断记录一致，这里
                // 只是防止字典 Add 在这种非常规用法下抛出无关的 ArgumentException。
                _byLevel[level] = row;
                if (level > maxLevel)
                {
                    maxLevel = level;
                }
            }

            MaxLevel = maxLevel;
        }

        /// <summary>按等级查锚点行；不存在返回 <c>false</c>。</summary>
        public bool TryGet(int level, out AnchorRow row) => _byLevel.TryGetValue(level, out row);

        /// <summary>按等级取锚点行；不存在时抛 <see cref="ArgumentOutOfRangeException"/>，消息里附带
        /// 当前表的 <see cref="MaxLevel"/> 供调用方定位问题（如传入的等级超过锚点表覆盖范围）。</summary>
        public AnchorRow Get(int level)
        {
            if (!TryGet(level, out var row))
            {
                throw new ArgumentOutOfRangeException(nameof(level),
                    $"sim.anchor 未登记 level={level}（当前表 MaxLevel={MaxLevel}）");
            }

            return row;
        }
    }
}

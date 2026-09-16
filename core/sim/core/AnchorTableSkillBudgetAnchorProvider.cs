using System;
using System.Collections.Generic;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-3a（ADR-0035 决策 4 锚点表接入；06 第 405 行勘误"锚点表接入后两条预算校验规则按本节公式
    /// 默认生效，不需要额外拍板"）：<see cref="ISkillBudgetAnchorProvider"/> 的 <c>sim.anchor</c> 真实
    /// 实现——<see cref="GetAnchorDps"/> 直接查 <c>AnchorTable</c>；<see cref="GetExpectedScalingStatValue"/>
    /// 委托 <see cref="ExpectedStatCalculator"/>（带缓存，见该类型"构造期一次性解析、按等级缓存"判断
    /// 记录，本类型只是在其基础上再按 (职业,品质) 组合多缓存一份计算器实例）。
    /// <para>
    /// 判断记录（惰性持有 <see cref="IDataRegistry"/> 引用，不在构造期读取任何数据行——同
    /// <c>Presentation.Carriers.Creature.RegistryCreatureTemplateQuery</c>"只持有 registry 引用，
    /// 真正读取延迟到规则 Validate() 调用时（此时数据已加载）"先例）：<c>DataRegistry.LoadAllCore</c>
    /// 的加载顺序是"全部表装载完毕（<c>_tables = loaded</c>）后才跑 <see cref="IValidationRule.Validate"/>"
    /// （见该方法源码），<see cref="Core.Rules.Skill.SkillBudgetValidationRule"/>/<see
    /// cref="Core.Carriers.Item.ItemGrantValueExceedsShareRule"/> 的 <c>Validate</c> 因此总是在
    /// <c>sim.anchor</c> 等全部表已装载完毕之后才被调用——本类型的构造函数因此可以在
    /// <c>RegisterAll</c>（装载之前）就被安全构造并塞进两条规则的构造参数，真正触碰 <c>AnchorTable</c>/
    /// <c>sim.scenario</c>/<c>item.quality_definition</c> 等数据表的时机推迟到 <see cref="GetAnchorDps"/>/
    /// <see cref="GetExpectedScalingStatValue"/> 首次被调用（届时数据必已装载完毕），不需要"先加载一遍
    /// 拿锚点、再注册规则跑第二遍 Validate"的两遍机制（任务书"若不是……用 ContentValidationAssembly 的
    /// 两遍机制"分支未被触发，因为核实到的是前一种情形——见 <see cref="Core.Sim.HeadlessWorldBuilder"/>/
    /// <c>toolchain/validator/Program.cs</c> 接入点判断记录）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="ISkillBudgetAnchorProvider.GetExpectedScalingStatValue"/> 接口本身不带
    /// 职业/品质参数，本类型如何决定用哪一对 (职业,品质) 求值）：接口签名（阶段 N3 落地，早于本任务）
    /// 只接受 <c>(stat, level)</c>，没有给技能预算校验"这条技能属于哪个职业"的上下文——技能本就不是
    /// 职业专属内容（一把武器授予的技能、天赋树节点等都可能跨职业适用）。本类型按构造参数
    /// <paramref name="classIdOverride"/>/<paramref name="qualityIdOverride"/> 显式指定时最高优先级；
    /// 否则取 <c>sim.scenario</c> 全表里 <c>id</c> 字典序最小的一行的 <c>player.class_id</c>/
    /// <c>player.quality_id</c>（<c>sim.scenario</c> 各行本就是"一个职业+一个基准配置"的标准玩家配置，
    /// 见 <c>SimSchemas.cs</c> 判断记录 12；取字典序最小行只是为了在多行数据下有一个确定性的选择，不
    /// 代表"这一行比其它行更重要"这一额外语义）；<c>quality_id</c> 在选中的场景行里仍为空（该字段本身
    /// 是可选字段）时，回退取 <c>item.quality_definition</c> 里 <c>sort_weight</c> 最小的一行（约定
    /// "序号最小 = 最基础/常见品质"，同 07 第 1.1 节 <c>sort_weight</c> 排序惯例）。全部回退路径都取
    /// 不到时抛 <see cref="InvalidOperationException"/>，清楚指出调用方必须显式提供 <c>classId</c> 或
    /// 补一条 <c>sim.scenario</c> 数据——不静默退化为某个内置默认值（那会让"标准玩家是谁"变得含糊）。
    /// 解析结果按 (职业 id, 品质 id) 缓存，避免每次调用都重新扫描 <c>sim.scenario</c>/
    /// <c>item.quality_definition</c>。
    /// </para>
    /// </summary>
    public sealed class AnchorTableSkillBudgetAnchorProvider : ISkillBudgetAnchorProvider
    {
        private readonly Func<IDataRegistry> _registryFactory;
        private readonly Id? _classIdOverride;
        private readonly Id? _qualityIdOverride;
        private readonly IBudgetSolver _budgetSolver;
        private readonly Id? _budgetCurveId;

        private IDataRegistry? _resolvedRegistry;
        private AnchorTable? _anchorTable;
        private (Id ClassId, Id QualityId)? _resolvedStandardPlayer;
        private readonly Dictionary<(Id ClassId, Id QualityId), ExpectedStatCalculator> _calculators =
            new Dictionary<(Id, Id), ExpectedStatCalculator>();

        public AnchorTableSkillBudgetAnchorProvider(
            IDataRegistry registry,
            Id? classId = null,
            Id? qualityId = null,
            IBudgetSolver? budgetSolver = null,
            Id? budgetCurveId = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _registryFactory = () => registry;
            _classIdOverride = classId;
            _qualityIdOverride = qualityId;
            _budgetSolver = budgetSolver ?? new BudgetSolver();
            _budgetCurveId = budgetCurveId;
        }

        /// <summary>
        /// T-N6-3a：工厂重载——供 <c>toolchain/validator/Program.cs</c> 这类调用方使用，见该文件接入点
        /// 判断记录"registry 尚不存在时如何提前决定是否装配锚点提供者"。<paramref name="registryFactory"/>
        /// 只会在 <see cref="GetAnchorDps"/>/<see cref="GetExpectedScalingStatValue"/> 首次被调用（即
        /// <c>Validate()</c> 阶段，数据已装载完毕）时才调用一次并缓存其返回值——与主构造函数持有
        /// 固定 <see cref="IDataRegistry"/> 引用相比，唯一区别是"注册期（<see cref="RegisterAll"/>
        /// 调用时）允许 <see cref="IDataRegistry"/> 实例本身尚未构造出来"，本类型对外行为完全一致。
        /// </summary>
        public AnchorTableSkillBudgetAnchorProvider(
            Func<IDataRegistry> registryFactory,
            Id? classId = null,
            Id? qualityId = null,
            IBudgetSolver? budgetSolver = null,
            Id? budgetCurveId = null)
        {
            _registryFactory = registryFactory ?? throw new ArgumentNullException(nameof(registryFactory));
            _classIdOverride = classId;
            _qualityIdOverride = qualityId;
            _budgetSolver = budgetSolver ?? new BudgetSolver();
            _budgetCurveId = budgetCurveId;
        }

        /// <summary>越级时夹到 <see cref="AnchorTable.MaxLevel"/>（任务书"越界：夹到 MaxLevel"）；
        /// 同样把低于 1 的等级夹到 1（防御性对称处理，锚点表不可能登记 0/负等级行，见 <see
        /// cref="SimAnchorValidationRule"/> 判断记录"全表须从 1 起连续"）。</summary>
        public double GetAnchorDps(int level)
        {
            var table = GetAnchorTable();
            return table.Get(ClampLevel(level, table)).Dps;
        }

        public double GetExpectedScalingStatValue(Id stat, int level)
        {
            var table = GetAnchorTable();
            var clamped = ClampLevel(level, table);
            var calculator = GetCalculator();
            return calculator.Compute(clamped).TryGetValue(stat, out var value) ? value : 0.0;
        }

        private IDataRegistry Registry => _resolvedRegistry ??= _registryFactory();

        private AnchorTable GetAnchorTable() => _anchorTable ??= new AnchorTable(Registry);

        private static int ClampLevel(int level, AnchorTable table)
        {
            if (table.MaxLevel <= 0)
            {
                throw new InvalidOperationException(
                    "AnchorTableSkillBudgetAnchorProvider 已构造但 sim.anchor 表为空——调用方不应在" +
                    "数据源不含 sim.anchor 行时装配本提供者（见 HeadlessWorldBuilder/toolchain validator" +
                    "\"数据源含 sim.anchor 才装配\"接入判断记录）。");
            }

            if (level < 1) return 1;
            return level > table.MaxLevel ? table.MaxLevel : level;
        }

        /// <summary>
        /// 判断记录（2026-09-16，深度复审 E 测试覆盖缺口 5：暴露只读解析结果供诊断/测试观察）：
        /// 本次解析实际选中的标准玩家 (职业, 品质)，见类型判断记录"标准玩家 (职业,品质) 解析顺序"
        /// （显式覆盖 &gt; <c>sim.scenario</c> 字典序最小行 &gt; <c>item.quality_definition</c> 最低
        /// <c>sort_weight</c>）。首次访问即触发并缓存解析（与 <see cref="GetAnchorDps"/>/<see
        /// cref="GetExpectedScalingStatValue"/> 首次调用触发解析同一时机约定）。纯新增只读属性——
        /// 一方面供编辑器/诊断工具展示"当前锚点计算实际在用哪个 (职业,品质)"，另一方面让
        /// <c>AnchorTableSkillBudgetAnchorProviderTests</c> 可以脱离 <see cref="ExpectedStatCalculator"/>
        /// 所需的完整依赖表集合（<c>stat.definition</c>/<c>stat.weight</c>/<c>prog.level_curve</c>
        /// 等），只用 <c>item.quality_definition</c> 单表直接验证 <c>sort_weight</c> 并列时的选择
        /// 顺序（多行 <c>sort_weight</c> 相等时取先注册的一行，不是任何形式的 id 字典序，见该测试
        /// 判断记录）。
        /// </summary>
        public (Id ClassId, Id QualityId) ResolvedStandardPlayer => ResolveStandardPlayer();

        private ExpectedStatCalculator GetCalculator()
        {
            var (classId, qualityId) = ResolveStandardPlayer();
            if (!_calculators.TryGetValue((classId, qualityId), out var calc))
            {
                calc = new ExpectedStatCalculator(Registry, GetAnchorTable(), classId, qualityId, _budgetSolver, _budgetCurveId);
                _calculators[(classId, qualityId)] = calc;
            }
            return calc;
        }

        /// <summary>
        /// T-N6-3a：供 <see cref="Core.Sim.HeadlessWorldBuilder.Build"/>/<c>toolchain/validator/
        /// Program.cs</c> 两处接入点共用的"数据源是否真的提供了 sim.anchor 行"预扫描（装载之前调用，
        /// 见两处调用点判断记录"数据源含 sim.anchor 才自动装配锚点提供者"）。
        /// <para>
        /// 判断记录（为何不能只看 <see cref="IDataSource.ListTables"/> 是否列出该表名）：
        /// <c>games/_template</c>（游戏层空壳模板）已经登记了一份 <c>data/game/sim/sim.anchor.json</c>
        /// 文件（<c>{"table":"sim.anchor","schema_version":1,"rows":[]}</c>，供 <c>toolchain/
        /// validate_data.py</c>/内容工具识别该表结构，但内容为空），若只按"文件/表名是否存在"判断，
        /// <c>games/_template</c> 会被误判为"含 sim.anchor"从而装配一个内部 <see cref="AnchorTable"/>
        /// 恒为空（<c>MaxLevel == 0</c>）的提供者——一旦真被调用（<see cref="GetAnchorDps"/>）就会抛出
        /// <see cref="InvalidOperationException"/>，且与任务书"validator --json 对 games/_template 为
        /// false"的验收点矛盾。本方法因此读取每个候选 <c>sim.anchor</c> 文件的原始 JSON 文本、解析
        /// <c>rows</c> 数组长度，只有真正含至少一行时才判定"数据源含 sim.anchor"——与 <see
        /// cref="HeadlessWorldBuilder"/> 装配完成后决定是否构造 <see cref="HeadlessWorld.AnchorTable"/>
        /// 属性时"该表本次加载到的行数是否 &gt; 0"的既有判断口径完全一致，只是提前到装载之前、按原始
        /// 文本自行解析（此时还没有 <see cref="IDataRegistry"/> 实例可用）。
        /// </para>
        /// </summary>
        public static bool DataSourcesHaveAnchorRows(IReadOnlyList<IDataSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            foreach (var source in sources)
            {
                foreach (var table in source.ListTables())
                {
                    if (table.TableName != "sim.anchor")
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = table.ReadText();
                    }
                    catch
                    {
                        // 判断记录：读取失败（如文件被并发修改）不是本方法的职责，留给 DataRegistry
                        // 装载时按既有加载期错误处理报告，这里只是"预判是否需要装配提供者"，读取失败
                        // 时保守按"这个候选不算数"处理，继续看下一个。
                        continue;
                    }

                    if (!(Core.Foundation.Common.Json.JsonReader.Parse(text) is Core.Foundation.Common.Json.JsonObject envelope))
                    {
                        continue;
                    }

                    if (envelope.TryGetValue("rows", out var rowsRaw) &&
                        rowsRaw is Core.Foundation.Common.Json.JsonArray rows && rows.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private (Id ClassId, Id QualityId) ResolveStandardPlayer()
        {
            if (_resolvedStandardPlayer.HasValue)
            {
                return _resolvedStandardPlayer.Value;
            }

            Id? classId = _classIdOverride;
            Id? qualityId = _qualityIdOverride;

            if (classId == null || qualityId == null)
            {
                // 惰性构造 ScenarioCatalog 成本可忽略（sim.scenario 通常只有个位数行）：直接复用其
                // 解析逻辑，不在本类型内再手写一遍 JsonObject 解析（同 ScenarioCatalog 判断记录、
                // 避免重复实现风险）。
                if (Registry.GetAll("sim.scenario").Count > 0)
                {
                    var catalog = new ScenarioCatalog(Registry);
                    ScenarioDef? best = null;
                    foreach (var s in catalog.All)
                    {
                        if (best == null || string.CompareOrdinal(s.Id.Value, best.Id.Value) < 0)
                        {
                            best = s;
                        }
                    }

                    if (best != null)
                    {
                        classId ??= best.Player.ClassId;
                        qualityId ??= best.Player.QualityId;
                    }
                }
            }

            if (qualityId == null)
            {
                DataRecord? lowest = null;
                foreach (var q in Registry.GetAll("item.quality_definition"))
                {
                    if (lowest == null ||
                        (q.TryGetInt("sort_weight", out var sw) && lowest.TryGetInt("sort_weight", out var lowestSw) && sw < lowestSw))
                    {
                        lowest = q;
                    }
                }

                if (lowest != null)
                {
                    qualityId = lowest.Id;
                }
            }

            if (classId == null)
            {
                throw new InvalidOperationException(
                    "AnchorTableSkillBudgetAnchorProvider 无法确定标准玩家职业：未显式指定 classId，" +
                    "且 sim.scenario 表为空（无法从场景推断）。");
            }

            if (qualityId == null)
            {
                throw new InvalidOperationException(
                    "AnchorTableSkillBudgetAnchorProvider 无法确定标准玩家期望品质：未显式指定 qualityId，" +
                    "sim.scenario 未登记 player.quality_id，且 item.quality_definition 表为空。");
            }

            var resolved = (classId.Value, qualityId.Value);
            _resolvedStandardPlayer = resolved;
            return resolved;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;

namespace Presentation.Assembly
{
    /// <summary>
    /// ADR-0018 决策第 3 条"校验装配入口"：把此前只内联在 <c>toolchain/validator/Program.cs</c>
    /// 里的"调用 <see cref="PresentationSchemaCatalog"/> 汇总注册目录 + 两个可选规则的接线参数 +
    /// <see cref="DataRegistryOptions"/> 装配选项"这段逻辑抽为核心库内的单一公开入口。
    /// <see cref="ContentValidationAssembly.Run"/>/<see cref="ContentValidationAssembly.CreateRegistry"/>
    /// 是本装配步骤唯一实现——<c>toolchain/validator</c> 与编辑器基础套件（ADR-0018 决策 1/2 定义的
    /// 独立消费方项目）都只调用这两个方法，不得各自重新内联一遍同样的注册顺序，这是"编辑器里看到的
    /// 红线 = 门禁会报的错"这一验收标准的落地方式：两个消费方用同一份装配代码，天然不会出现两边
    /// 分叉的注册顺序/选项默认值。
    /// </summary>
    public sealed class ContentValidationOptions
    {
        /// <summary>校验严格级别，透传给 <see cref="DataRegistryOptions.Strictness"/>。默认
        /// <see cref="DataRegistryStrictness.WarningsAllowed"/>，与 <c>toolchain/validator</c>
        /// 此前"未传 <c>--strict</c> 时"的默认行为一致。</summary>
        public DataRegistryStrictness Strictness { get; set; } = DataRegistryStrictness.WarningsAllowed;

        /// <summary>透传给 <see cref="DataRegistryOptions.FailOnUnknownTable"/>。默认 <c>true</c>，
        /// 与 <c>toolchain/validator</c> 此前硬编码的取值一致。</summary>
        public bool FailOnUnknownTable { get; set; } = true;

        /// <summary>透传给 <see cref="Core.Carriers.Assembly.CarriersSchemaCatalog.RegisterAll"/>
        /// （经 <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll"/> 转发）。默认
        /// <c>null</c>。</summary>
        public Id? ItemBudgetCurveId { get; set; }

        /// <summary>可选规则 <c>SpawnSummonOnlyCreatureRule</c> 的接线依赖：未提供（默认 <c>null</c>）
        /// 时 <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll"/> 不注册该规则
        /// （见其判断记录，等价于"该项检查不生效"），本入口在 <see cref="ContentValidationRun.DisabledOptionalRules"/>
        /// 里如实列出，不静默跳过。</summary>
        public ICreatureTemplateQuery? CreatureTemplateQuery { get; set; }

        /// <summary>可选规则 <see cref="DisplayMapCoverageRule"/> 的接线依赖（哪些内容表参与外形域
        /// 覆盖检查 + 各表用哪个字段做逻辑 id，见该规则构造函数）：未提供（默认 <c>null</c>）时本入口
        /// 不注册该规则，同样如实列入 <see cref="ContentValidationRun.DisabledOptionalRules"/>。</summary>
        public IReadOnlyList<(string table, string idField)>? DisplayMapCoverageSources { get; set; }

        /// <summary>装配用的事件总线；未提供（默认 <c>null</c>）时本入口按
        /// <c>toolchain/validator/Program.cs</c> 此前的写法内部新建一条
        /// <c>StrictCatalog = false</c>、只登记 <c>data.load_completed</c>/<c>data.validation_failed</c>
        /// 两个事件 key 的总线（本入口是一次性装配/校验调用，不关心这两个 key 之外的事件登记）。
        /// 调用方（如编辑器）若已持有一条总线，可显式传入以复用同一份事件流。</summary>
        public IEventBus? Bus { get; set; }
    }

    /// <summary>一次 <see cref="ContentValidationAssembly.Run"/> 调用的完整结果。</summary>
    public sealed class ContentValidationRun
    {
        public ValidationReport Report { get; }

        public IDataRegistryView Registry { get; }

        public IReadOnlyList<OverrideDiagnostic> Overrides { get; }

        public int TableCount { get; }

        public int RecordCount { get; }

        /// <summary>本次因调用方未提供对应接线参数而未启用的可选规则名（取值集合见
        /// <see cref="ContentValidationAssembly.OptionalRuleNames"/>）。</summary>
        public IReadOnlyList<string> DisabledOptionalRules { get; }

        /// <summary>本次已启用的可选规则名（<see cref="ContentValidationAssembly.OptionalRuleNames"/>
        /// 减去 <see cref="DisabledOptionalRules"/>）。</summary>
        public IReadOnlyList<string> EnabledOptionalRules { get; }

        internal ContentValidationRun(
            ValidationReport report,
            IDataRegistryView registry,
            IReadOnlyList<OverrideDiagnostic> overrides,
            int tableCount,
            int recordCount,
            IReadOnlyList<string> disabledOptionalRules,
            IReadOnlyList<string> enabledOptionalRules)
        {
            Report = report;
            Registry = registry;
            Overrides = overrides;
            TableCount = tableCount;
            RecordCount = recordCount;
            DisabledOptionalRules = disabledOptionalRules;
            EnabledOptionalRules = enabledOptionalRules;
        }
    }

    /// <summary>校验装配入口本体，见类型注释判断记录。</summary>
    public static class ContentValidationAssembly
    {
        private const string SpawnSummonOnlyCreatureRuleName = "SpawnSummonOnlyCreatureRule";
        private const string DisplayMapCoverageRuleName = "DisplayMapCoverageRule";

        /// <summary>本入口承认的全部可选规则名（固定清单，见 <see cref="ContentValidationOptions"/>
        /// 两个接线参数）。调用方（如编辑器"校验设置"面板）可据此展示全量选项，而不必硬编码字符串。</summary>
        public static IReadOnlyList<string> OptionalRuleNames { get; } =
            new[] { SpawnSummonOnlyCreatureRuleName, DisplayMapCoverageRuleName };

        /// <summary>
        /// 构造并注册好全部 L0～L5 <see cref="TableSchema"/>/<see cref="IValidationRule"/>（含按
        /// <paramref name="options"/> 接线的可选规则）的 <see cref="IDataRegistry"/>，不调用
        /// <see cref="IDataRegistry.LoadAll()"/>——加载时机由调用方决定（供需要先持有 registry、
        /// 再自行选择何时/用哪些数据源加载的宿主使用，如编辑器需要在用户操作间隙重复
        /// <see cref="IDataRegistry.Reload"/> 单表）。
        /// </summary>
        /// <param name="primary">构造 <see cref="DataRegistry"/> 要求的主数据源（构造函数参数，见
        /// <see cref="DataRegistry"/> 类型注释；多根加载仍通过 <see cref="IDataRegistry.LoadAll(IReadOnlyList{IDataSource})"/>
        /// 传入完整列表，与本参数是否为该列表第一项无关）。</param>
        /// <param name="options">装配选项，必填（不像 <see cref="Run"/> 允许省略——本方法通常由已经
        /// 知道自己需要哪些可选规则的调用方直接使用）。</param>
        /// <param name="disabledOptionalRules">本次因未提供接线参数而未启用的可选规则名。</param>
        public static IDataRegistry CreateRegistry(
            IDataSource primary, ContentValidationOptions options, out IReadOnlyList<string> disabledOptionalRules)
        {
            return CreateRegistryCore(primary, options, out disabledOptionalRules, out _);
        }

        /// <summary>
        /// 一次性完成"建 registry + 注册 schema/规则 + <see cref="IDataRegistry.LoadAll(IReadOnlyList{IDataSource})"/>
        /// + 汇总结果"，是 <c>toolchain/validator</c> 与大多数一次性校验调用方（含编辑器"跑一次全量
        /// 校验"操作）的主入口。
        /// </summary>
        /// <param name="sources">数据源列表（<see cref="IDataRegistry.LoadAll(IReadOnlyList{IDataSource})"/>
        /// 的多根加载参数，见该方法判断记录"合并规则"）；不能为空——本方法用 <c>sources[0]</c>
        /// 构造 <see cref="DataRegistry"/>（构造函数要求的主数据源，其余根仍参与 <c>LoadAll</c> 合并）。</param>
        /// <param name="options">装配选项；省略（<c>null</c>）时使用全部默认值。</param>
        public static ContentValidationRun Run(IReadOnlyList<IDataSource> sources, ContentValidationOptions? options = null)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (sources.Count == 0) throw new ArgumentException("sources 不能为空：至少需要一个数据根", nameof(sources));

            var opts = options ?? new ContentValidationOptions();
            var registry = CreateRegistryCore(sources[0], opts, out var disabledOptionalRules, out var bus);

            // 判断记录：recordCount 取值惯例同 toolchain/validator/Program.cs 此前的写法——订阅
            // DataRegistry.LoadAll 内部发出的 data.load_completed 事件读 RecordCount 字段，而不是
            // 事后自己遍历 registry.Tables 逐表 GetAll(..).Count 求和：DataRegistry.LoadAll 内部对
            // 每张表的 recordCount 是在解析阶段边解析边累加的，与 report.IsBlocking 无关（阻断态下
            // 事件仍会照常发出，见 DataRegistry.LoadAll 判断记录），逐表 GetAll 累加则要求数据已经
            // 通过校验（IDataRegistryView.GetAll 契约：未通过校验时抛异常，见该接口类型注释），
            // 阻断态下无法这样求和，两条路径的语义与可用性都不同，必须复用同一条事件路径才能保证
            // 与此前 validator 输出的 recordCount 逐字节一致。
            DataLoadCompletedEvent? loadCompleted = null;
            var subscription = bus.Subscribe<DataLoadCompletedEvent>(
                DataRegistryEventKeys.LoadCompleted, e => loadCompleted = e);
            ValidationReport report;
            try
            {
                report = registry.LoadAll(sources);
            }
            finally
            {
                subscription.Dispose();
            }

            var recordCount = loadCompleted?.RecordCount ?? 0;
            var enabledOptionalRules = OptionalRuleNames.Except(disabledOptionalRules).ToList();

            return new ContentValidationRun(
                report,
                registry,
                registry.GetOverrideDiagnostics(),
                registry.Tables.Count,
                recordCount,
                disabledOptionalRules,
                enabledOptionalRules);
        }

        private static IDataRegistry CreateRegistryCore(
            IDataSource primary, ContentValidationOptions options,
            out IReadOnlyList<string> disabledOptionalRules, out IEventBus bus)
        {
            if (primary == null) throw new ArgumentNullException(nameof(primary));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = options.FailOnUnknownTable;
            registryOptions.Strictness = options.Strictness;

            bus = options.Bus ?? CreateDefaultBus();
            var registry = new DataRegistry(primary, bus, registryOptions);

            PresentationSchemaCatalog.RegisterAll(registry, options.ItemBudgetCurveId, options.CreatureTemplateQuery);

            var disabled = new List<string>();
            if (options.CreatureTemplateQuery == null)
            {
                disabled.Add(SpawnSummonOnlyCreatureRuleName);
            }

            if (options.DisplayMapCoverageSources != null)
            {
                registry.RegisterValidationRule(new DisplayMapCoverageRule(options.DisplayMapCoverageSources));
            }
            else
            {
                disabled.Add(DisplayMapCoverageRuleName);
            }

            disabledOptionalRules = disabled;
            return registry;
        }

        /// <summary>惯例同 <c>toolchain/validator/Program.cs</c> 此前的写法：一次性命令行/一次性
        /// 校验调用不关心 <c>data.load_completed</c>/<c>data.validation_failed</c> 之外的任何事件
        /// 登记，<c>StrictCatalog = false</c> 让未登记的事件 key 只记警告、不抛异常。</summary>
        private static IEventBus CreateDefaultBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }
    }
}

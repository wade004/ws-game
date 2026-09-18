using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;

namespace Toolchain.Validator
{
    /// <summary>
    /// 数据校验器阶段 2 的真实校验入口（落地方案 T2-12，阶段 3 集成收尾"事项四"升级到 L0～L4，
    /// 阶段 4 收敛 B 再升级到 L0～L5）：复用 <c>presentation/assembly</c> 的
    /// <see cref="PresentationSchemaCatalog"/> 一次性注册 L0～L5 全部
    /// <c>TableSchema</c>/<see cref="IValidationRule"/>（先 L0～L4 经
    /// <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog"/>——该方法内部再先经
    /// <see cref="Core.Carriers.Assembly.CarriersSchemaCatalog"/> 登记 L0～L3——，再补 L5 表现层
    /// 全部表：<c>vfx.def</c>/<c>sfx.def</c>/<c>display.weapon_style</c>/<c>feedback.binding</c>/
    /// <c>feedback.floating_text_style</c>/<c>camera_profile</c>/<c>ui_layout_definition</c>/
    /// <c>shell_menu_definition</c>/<c>display.map</c>/<c>display.anim_set</c>/
    /// <c>display.equip_visual</c>/<c>l10n.locale</c>/<c>l10n.text</c>，见
    /// <see cref="PresentationSchemaCatalog"/> 类型注释判断记录），对 <c>--data-root</c> 指向的磁盘
    /// 目录跑一遍 <see cref="DataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{Core.Foundation.DataRegistry.IDataSource})"/>
    /// （多根合并加载的重载，见调用处判断记录"合并规则"；本工具恒定以列表形式传入，即便只有一个
    /// <c>--data-root</c> 也归一化为单元素列表——消费方反馈 E1 根治，cref 此前未指定参数列表，与
    /// 同名的无参重载 <see cref="DataRegistry.LoadAll()"/> 产生 CS0419 歧义警告，消费方
    /// <c>Directory.Build.props</c> 若设置 <c>TreatWarningsAsErrors=true</c> 会被提升为编译错误，
    /// 见 architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E1），逐条打印校验问题。
    /// <para>
    /// 判断记录：本类是唯一的真实校验逻辑实现——<c>toolchain/validate_data.py</c> 只做骨架级
    /// 信封/表名/id 格式检查（阶段 0），不得与本类重复实现任何字段级/引用完整性/Expr 规则（落地
    /// 计划 T2-12 明文禁止"与 core 内校验逻辑重复实现两套判断"）；<c>validate_data.py</c> 在骨架
    /// 检查通过后，用子进程调用本工具完成第二道真实校验。
    /// </para>
    /// <para>
    /// 判断记录：本工具依赖 <c>adapters/stub</c> 提供的 <c>StubFileSystem</c> 只有内存实现，无法
    /// 读取真实磁盘文件，因此本工具自带 <see cref="DiskFileSystem"/>（只读，见其文件头判断记录），
    /// 不修改 <c>core/</c> 下任何已有类型。
    /// </para>
    /// <para>
    /// 判断记录（ADR-0018 决策 3，校验装配入口）：本类不再自行内联"建 EventBus + 建 DataRegistry +
    /// <see cref="PresentationSchemaCatalog.RegisterAll(IDataRegistry, Core.Foundation.Common.Id?, Core.Carriers.Creature.ICreatureTemplateQuery?)"/>
    /// + <c>LoadAll</c> + 汇总"这一整段装配逻辑——改为调用 <see cref="ContentValidationAssembly.Run"/>
    /// （核心库内单一公开入口），保证本工具与编辑器基础套件（ADR-0018 决策 1/2 的独立消费方项目）
    /// 使用同一份注册顺序与选项默认值，即"编辑器里看到的红线 = 门禁会报的错"。
    /// </para>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 校验问题消息里的中文文本（来自 core 内各校验规则）默认按控制台代码页输出会乱码——
            // Windows 控制台默认代码页通常不是 UTF-8。显式设为 UTF-8（无 BOM）保证标准输出/标准
            // 错误可读、可被下游（如 toolchain/validate_data.py 的 subprocess 管道）正确解码。
            try
            {
                Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch (IOException)
            {
                // 标准输出被重定向到某些不支持设置编码的目标时可能抛出；不影响功能，只是退化为
                // 使用调用方已设置的编码，不视为致命错误。
            }

            // 判断记录（数据目录框架/游戏分层任务，多根加载）：--data-root 由"最多一个"改为
            // "可重复传入"，收集到 dataRootArgs 列表；每个根各自构造一个 FileSystemDataSource，
            // 一起传给 DataRegistry.LoadAll(IReadOnlyList<IDataSource>)（见该方法类型级判断记录
            // "合并规则"）——同名表跨根合并、主键冲突/schema_version 不一致跨根阻断，均由该方法
            // 实现，本文件不重复实现任何判断逻辑（见类型头判断记录"唯一实现"）。只传一个
            // --data-root 时行为与改动前完全一致（单元素列表）。
            var dataRootArgs = new List<string>();
            var strict = false;
            var jsonOutput = false;
            var listTables = false;
            var schemaAudit = false;
            string? allowlistPath = null;
            IReadOnlyList<(string table, string idField)>? displayMapSources = null;
            // 消费方反馈第 42 条：默认 WarnOnMissingTranslation=true（见
            // DataRegistryOptions.WarnOnMissingTranslation 判断记录），本开关显式传入时关闭。
            var noMissingTranslationWarning = false;
            // 消费方反馈第 56 条追问：ContentValidationOptions.EnableGraphIsolationDiagnostics 的命令行
            // 开关——首个"命令行式可选规则开关"（既有两条可选规则靠 --display-map-sources 一类接线参数
            // 是否提供间接决定是否启用，本次是独立的纯布尔命令行开关，见该属性判断记录），默认不传即关闭。
            var enableGraphIsolation = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--data-root":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("参数错误：--data-root 需要一个目录参数");
                            return 2;
                        }
                        dataRootArgs.Add(args[++i]);
                        break;

                    case "--strict":
                        strict = true;
                        break;

                    case "--json":
                        jsonOutput = true;
                        break;

                    case "--list-tables":
                        listTables = true;
                        break;

                    // 消费方反馈第 42 条：关闭"非默认已登记语言缺翻译报 Warning 级 text_key_exists"
                    // 这条校验，恢复只查默认语言的旧行为（见 DataRegistryOptions.WarnOnMissingTranslation）。
                    case "--no-missing-translation-warning":
                        noMissingTranslationWarning = true;
                        break;

                    // 消费方反馈第 56 条追问：开启 QuestPrerequisiteIsolationRule/TalentTreeIsolationRule
                    // 两条默认关闭的孤立节点展示性提示规则（见 ContentValidationOptions.
                    // EnableGraphIsolationDiagnostics 判断记录）。
                    case "--enable-graph-isolation":
                        enableGraphIsolation = true;
                        break;

                    // 判断记录（F3 元数据门禁）：--schema-audit 是一种完全不同的运行模式——不需要
                    // --data-root（不加载任何实际数据，只审计代码里已登记的 TableSchema 结构本身，
                    // 见 Presentation.Assembly.SchemaAudit 类型注释），因此下面 dataRootArgs.Count==0
                    // 的必填校验对这一模式不生效，见本方法后续分支判断。
                    case "--schema-audit":
                        schemaAudit = true;
                        break;

                    case "--allowlist":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("参数错误：--allowlist 需要一个文件路径参数");
                            return 2;
                        }
                        allowlistPath = args[++i];
                        break;

                    // 消费方反馈第 34 条：本参数现在是可选覆盖——不传时 ContentValidationAssembly
                    // 默认使用 PresentationSchemaCatalog.DefaultDisplayMapCoverageSources（该规则
                    // 默认启用），传了则完整替换默认清单（不是追加）。
                    case "--display-map-sources":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("参数错误：--display-map-sources 需要一个参数，形如 \"table1:idField1,table2:idField2\"");
                            return 2;
                        }
                        if (!TryParseDisplayMapSources(args[++i], out displayMapSources, out var parseError))
                        {
                            Console.Error.WriteLine($"参数错误：--display-map-sources 格式非法：{parseError}");
                            return 2;
                        }
                        break;

                    default:
                        Console.Error.WriteLine($"参数错误：未知参数 \"{args[i]}\"");
                        return 2;
                }
            }

            if (schemaAudit)
            {
                return RunSchemaAudit(allowlistPath, jsonOutput);
            }

            if (dataRootArgs.Count == 0)
            {
                Console.Error.WriteLine(
                    "参数错误：缺少必填参数 --data-root <dir>（可重复传入以合并多个数据根）\n" +
                    "用法：dotnet run --project toolchain/validator -- --data-root <dir> [--data-root <dir2> ...] [--strict] [--json] [--list-tables] [--display-map-sources <table:idField,...>] [--no-missing-translation-warning] [--enable-graph-isolation]（省略 --display-map-sources 时默认覆盖 skill.def/skill.aura_def/item.template/creature.template/gobj.template；--no-missing-translation-warning 关闭非默认语言缺翻译的 text_key_exists Warning，见消费方反馈第 42 条；--enable-graph-isolation 开启 quest_prerequisite_node_isolated/talent_node_isolated 两条默认关闭的孤立节点展示性提示规则，见消费方反馈第 56 条追问；field_ref_category 规则自 1.44.0 起无条件注册，不再有对应命令行开关）\n" +
                    "或元数据门禁：dotnet run --project toolchain/validator -- --schema-audit [--allowlist <path>] [--json]");
                return 2;
            }

            // 相对路径相对当前工作目录解析——调用方（toolchain/validate_data.py 或用户）需要在
            // 仓库根目录下运行本工具，此时"相对路径"与"相对仓库根"是同一件事，惯例同
            // toolchain/validate_data.py"从仓库根目录运行"。绝对路径原样使用。
            var sources = new List<IDataSource>(dataRootArgs.Count);
            // 判断记录（数据根非数据表 JSON 误判修复任务）：额外保留一份具体类型的引用列表——
            // FileSystemDataSource.ListTables() 在 ContentValidationAssembly.Run 内部被调用后会
            // 填好 SkippedNonTableFiles（见该类型判断记录），本工具据此在 Run 完成后打印
            // "[skip] ..." 提示行；sources（IDataSource 列表）本身拿不到这个只有具体类型才有的属性。
            var fileSystemSources = new List<FileSystemDataSource>(dataRootArgs.Count);
            var fs = new DiskFileSystem();
            foreach (var dataRootArg in dataRootArgs)
            {
                var dataRoot = Path.IsPathRooted(dataRootArg)
                    ? dataRootArg
                    : Path.Combine(Directory.GetCurrentDirectory(), dataRootArg);
                dataRoot = Path.GetFullPath(dataRoot).Replace('\\', '/');

                if (!Directory.Exists(dataRoot))
                {
                    Console.Error.WriteLine($"参数错误：目录不存在：{dataRoot}");
                    return 2;
                }

                var fileSystemSource = new FileSystemDataSource(fs, dataRoot);
                sources.Add(fileSystemSource);
                fileSystemSources.Add(fileSystemSource);
            }

            // ADR-0018 决策 3（校验装配入口）：本工具不再自行内联"建 EventBus + 建 DataRegistry +
            // PresentationSchemaCatalog.RegisterAll + LoadAll + 汇总"这一整段装配逻辑——改为调用
            // Presentation.Assembly.ContentValidationAssembly.Run（核心库内的单一公开入口，供本工具
            // 与编辑器基础套件共用同一份装配代码，见该类型注释判断记录）。消费方反馈第 34 条：
            // --display-map-sources 现在是可选覆盖——不传（displayMapSources 为 null）时
            // ContentValidationAssembly 默认使用 PresentationSchemaCatalog.DefaultDisplayMapCoverageSources
            // （该规则默认启用，不再是"未接线即禁用"），传了则按解析结果覆盖默认清单。
            //
            // 消费方反馈第 44 条根治（2026-09-14，比照上面第 34 条 DisplayMapCoverageRule 先例）：
            // 本处判断记录此前写着"SpawnSummonOnlyCreatureRule 仍不接线——本工具运行时机（一次性
            // 命令行进程）没有真正的 ICreatureTemplateQuery 实现可用"，据此本工具从未在
            // CreatureTemplateQuery 传参——这是真实缺口：示例数据集的门禁（本命令行工具）因此从未
            // 真正跑过这条规则，`spawn.table` 引用一条 `summon_only` 生物永远不会被拦下。根治：
            // ContentValidationAssembly.CreateRegistryCore 现在未提供 CreatureTemplateQuery 时默认
            // 改用 Core.Carriers.Creature.RegistryCreatureTemplateQuery（直接从已构造的 registry
            // 现读现解析 creature.template 记录，不需要像 CreatureFactory 那样预先解析出完整索引，
            // 因此命令行一次性进程这个运行时机同样可以直接用——此前"没有真正的实现可用"的判断记录
            // 已不成立）。本工具不需要改动：validationOptions 仍不显式设置 CreatureTemplateQuery，
            // 走 ContentValidationAssembly 的新默认值即可，SpawnSummonOnlyCreatureRule 现默认启用。
            // T-N6-3a（ADR-0035 决策 4 锚点表接入）：判断记录（"registry 尚不存在时如何提前决定是否
            // 装配锚点提供者"）——ContentValidationOptions.SkillBudgetAnchorProvider 必须在
            // ContentValidationAssembly.Run 内部 PresentationSchemaCatalog.RegisterAll 调用时就确定
            // （两条预算规则的构造参数），但 registry 实例要到 Run 内部的 CreateRegistryCore 才构造
            // 出来。本工具用 AnchorTableSkillBudgetAnchorProvider 的 Func<IDataRegistry> 工厂重载 +
            // 一个延迟赋值的 registryHolder 闭包解决：工厂只在 Validate() 阶段（数据已装载完毕）才会
            // 被调用一次；registryHolder 的赋值时机借用既有的 ExtraSchemaRegistration 钩子——该钩子
            // 由 CreateRegistryCore 在 registry 构造完成之后、registry.LoadAll（真正触发 Validate）
            // 之前同步调用（见 ContentValidationAssembly.CreateRegistryCore 内调用顺序），因此在这里
            // 先把 registry 存进 registryHolder、再转发给 Core.Sim.SimSchemaCatalog.RegisterAll，
            // 能保证 registryHolder 严格早于任何一次 Validate() 调用完成赋值——不需要 presentation/
            // assembly 认识 Core.Sim 任何类型（分层边界不变，见 SimSchemaCatalog 类型判断记录"为何不
            // 并入 GameplaySchemaCatalog"）。是否装配（anchorProviderWired）按本次全部 --data-root
            // 是否含 sim.anchor 决定（同 Core.Sim.HeadlessWorldBuilder.Build 判断记录"数据源含
            // sim.anchor 才自动装配锚点提供者"同一惯例、同一判定口径：只看 IDataSource.ListTables()
            // 是否列出该表，不解析行）。
            IDataRegistry? registryHolder = null;
            // 判断记录：Core.Sim.AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows 按
            // "sim.anchor 文件实际是否含至少一行"判定，不是"文件/表名是否存在"——games/_template 登记
            // 了一份零行的 sim.anchor.json（供 validate_data.py/内容工具识别表结构），只看表名存在会
            // 把它误判成"已接入"，与本方法上方注释"对 games/_template 为 false"验收点矛盾，见该方法
            // 判断记录。
            var anchorProviderWired = Core.Sim.AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows(sources);

            var validationOptions = new ContentValidationOptions
            {
                FailOnUnknownTable = true,
                Strictness = strict ? DataRegistryStrictness.WarningsBlock : DataRegistryStrictness.WarningsAllowed,
                DisplayMapCoverageSources = displayMapSources,
                WarnOnMissingTranslation = !noMissingTranslationWarning,
                // T-N6-2a：sim.scenario/sim.anchor 仅无头仿真与内容工具读取（ADR-0035），不进
                // PresentationSchemaCatalog——本工具正是"内容工具"之一，经 ExtraSchemaRegistration
                // 钩子把这两张表接进本次校验（见 ContentValidationOptions.ExtraSchemaRegistration
                // 判断记录）；T-N6-3a 起本钩子同时承担"捕获 registry 引用给锚点提供者工厂用"这一职责
                // （见上方判断记录），两件事合并进同一个回调，不新增第二个钩子属性。
                ExtraSchemaRegistration = registry =>
                {
                    registryHolder = registry;
                    Core.Sim.SimSchemaCatalog.RegisterAll(registry);
                },
                SkillBudgetAnchorProvider = anchorProviderWired
                    ? new Core.Sim.AnchorTableSkillBudgetAnchorProvider(() => registryHolder!)
                    : null,
                EnableGraphIsolationDiagnostics = enableGraphIsolation,
            };
            var effectiveDisplayMapCoverageSources = displayMapSources ?? PresentationSchemaCatalog.DefaultDisplayMapCoverageSources;

            var run = ContentValidationAssembly.Run(sources, validationOptions);
            var report = run.Report;
            var tableCount = run.TableCount;
            var recordCount = run.RecordCount;

            // 判断记录（数据根非数据表 JSON 误判修复任务）：ContentValidationAssembly.Run 内部经
            // DataRegistry.LoadAll 调用了每个 FileSystemDataSource.ListTables()，此时
            // SkippedNonTableFiles 已经填好（见该类型判断记录"非数据表 JSON 候选判定"）。本提示
            // 始终打到标准错误（不是 Warning/Error，--strict 下也不阻断），--json 模式下同样只走
            // 标准错误——与本文件其余人类可读诊断消息的既有约定一致，不污染 --json 的单一 JSON
            // 标准输出。
            foreach (var fileSystemSource in fileSystemSources)
            {
                foreach (var skipped in fileSystemSource.SkippedNonTableFiles)
                {
                    var displayPath = string.IsNullOrEmpty(fileSystemSource.Root)
                        ? skipped
                        : $"{fileSystemSource.Root}/{skipped}";
                    Console.Error.WriteLine($"[skip] {displayPath}: 非数据表文件（不符合 <域>.<表名>.json 命名约定，或未放在对应域子目录/数据根下）");
                }
            }

            // 判断记录（数据行覆盖语义任务）：覆盖诊断（见 DataRegistry 类型级判断记录"覆盖语义"、
            // OverrideDiagnostic）不是 ValidationIssue（既非 Warning 也非 Error），report.Issues 里
            // 看不到；单独从 run.Overrides 取出打印，供人工核对"这次加载真的按预期覆盖了哪些行"
            // （如 arch.power.health 是否确实被 _sample 覆盖）。
            var overrides = run.Overrides;

            if (jsonOutput)
            {
                PrintJson(report, tableCount, recordCount, listTables ? run.Registry : null, overrides,
                    run.DisabledOptionalRules, run.EnabledOptionalRules, effectiveDisplayMapCoverageSources,
                    validationOptions.WarnOnMissingTranslation, anchorProviderWired);
            }
            else
            {
                if (listTables)
                {
                    foreach (var table in run.Registry.Tables.OrderBy(t => t, StringComparer.Ordinal))
                    {
                        var count = report.IsBlocking ? -1 : run.Registry.GetAll(table).Count;
                        Console.WriteLine(count < 0 ? $"table: {table} (? 条记录，数据未通过校验)" : $"table: {table} ({count} 条记录)");

                        var schema = run.Registry.GetSchema(table);

                        // ADR-0022 决策 5：表级归属元数据（04 第 3.4 节），文本模式下的人类可读展示。
                        if (schema != null)
                        {
                            var layerText = schema.Layer?.ToString() ?? "(未登记)";
                            var moduleText = schema.Module ?? "(未登记)";
                            Console.WriteLine($"  owner: layer={layerText} module={moduleText} domain={schema.Domain} time_scope={schema.TimeScope}");
                        }

                        // ADR-0021 决策 4："导出给内容工具"：把该表已登记的字段范围约束一并列出
                        // （供人工核对/编辑器接入前的手工检查），见 SchemaFieldRangeExport 判断记录。
                        if (schema != null)
                        {
                            foreach (var r in SchemaFieldRangeExport.Collect(schema))
                            {
                                Console.WriteLine($"  range: {r.FieldPath}: {r.Kind} {r.Range.Describe()}");
                            }
                        }
                    }
                }

                foreach (var issue in report.Issues)
                {
                    Console.WriteLine(FormatIssue(issue));
                }

                if (overrides.Count > 0)
                {
                    Console.WriteLine($"覆盖清单（{overrides.Count} 条，见 data/README.md\"多根加载与合并规则\"）：");
                    foreach (var diag in overrides.OrderBy(d => d.Table, StringComparer.Ordinal).ThenBy(d => d.RecordKey, StringComparer.Ordinal))
                    {
                        // 消费方反馈第三批第 22 条（2026-09-10，见
                        // architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 22 条）：
                        // 人类可读文本改用"根序号 + 相对路径"，比完整绝对路径更短、跨机器可比对；
                        // 绝对路径仍随 --json 一并给出（见 AppendOverrideJson）。
                        Console.WriteLine($"  [override] {diag.Table}[{diag.RecordKey}]: " +
                            $"\"根{diag.OverridingRootIndex}:{diag.OverridingRelativePath}\" 覆盖 \"根{diag.OverriddenRootIndex}:{diag.OverriddenRelativePath}\"");
                    }
                }

                Console.WriteLine($"tables {tableCount}, records {recordCount}, errors {report.ErrorCount}, warnings {report.WarningCount}, overrides {overrides.Count}");

                // ADR-0018 决策 3 新增：把 ContentValidationAssembly.Run 如实汇报的"本次未启用的
                // 可选规则清单"追加为最后一行，不静默跳过（见 ContentValidationOptions 两个可选
                // 接线参数的判断记录）；均已启用时打印 "none"。追加在既有末尾汇总行之后，不改动
                // 既有任何一行的内容，保持此前行为逐字节兼容。
                var disabledSummary = run.DisabledOptionalRules.Count == 0
                    ? "none"
                    : string.Join(", ", run.DisabledOptionalRules);
                Console.WriteLine($"optional rules disabled: {disabledSummary}");

                // 消费方反馈第 42 条：如实汇报本次是否启用了"非默认语言缺翻译报 Warning"，追加在
                // 既有 "optional rules disabled" 行之后，不改动既有任何一行的内容。
                Console.WriteLine($"missing translation warning: {(validationOptions.WarnOnMissingTranslation ? "enabled" : "disabled")}");

                // 分阶段落地计划 T-N0-6（落地清单 2.2 V3；ADR-0035 决策 5 报告要求）：追加"规则清单与
                // 命中统计"段——本次跑过的全部已注册规则（ValidationReport.Rules，按注册顺序、含命中
                // 0 条的），每行 id / 默认级别 / 是否不可提升 / 命中条数；追加在既有末尾各行之后，
                // 不改动既有任何一行，保持此前文本输出逐字节兼容。
                // 分阶段落地计划 T-N5-3：命中 04 分级表数值规则清单的行，行尾追加数值域分组/锚点依赖
                // 提示（NumericValidationRuleCatalog；非数值规则不受影响，行尾不追加任何内容）——只是
                // 在既有一行末尾追加文字，不改变既有前缀的逐字节内容。
                Console.WriteLine($"rules ({report.Rules.Count}):");
                foreach (var rule in report.Rules)
                {
                    var severity = rule.DefaultSeverity == ValidationSeverity.Error ? "error" : "warning";
                    var numericEntries = FindNumericRuleEntries(rule.RuleId);
                    var requiresAnchorText = numericEntries.Any(e => e.RequiresAnchor);
                    var numericSuffix = numericEntries.Count == 0
                        ? string.Empty
                        : $", numeric group={numericEntries[0].Group}" +
                          (requiresAnchorText
                              ? $", requires_anchor ({(anchorProviderWired ? "已接入" : "未接入")}, 当前 enabled={(anchorProviderWired ? "true" : "false")})"
                              : string.Empty);
                    Console.WriteLine($"  {rule.RuleId}: {severity}{(rule.NonEscalatable ? " (non-escalatable)" : "")}, hits {rule.HitCount}{numericSuffix}");
                }
            }

            return report.IsBlocking ? 1 : 0;
        }

        /// <summary>
        /// F3 元数据门禁（ADR-0018 决策 3、ADR-0019 决策 4）：--schema-audit 模式的完整流程——
        /// 用 <see cref="SchemaAudit.EnumerateRegisteredSchemas"/> 建一个只登记 schema、不加载任何
        /// 数据的 registry（复用 <see cref="ContentValidationAssembly"/> 同一份装配顺序），读出全部
        /// <see cref="TableSchema"/>，交给 <see cref="SchemaAudit.Run"/> 审计。<paramref name="allowlistPath"/>
        /// 省略时使用空白名单（不豁免任何 composite_without_substructure）。
        /// </summary>
        private static int RunSchemaAudit(string? allowlistPath, bool jsonOutput)
        {
            SchemaAuditAllowlist allowlist;
            if (allowlistPath == null)
            {
                allowlist = SchemaAuditAllowlist.Empty;
            }
            else
            {
                string allowlistText;
                try
                {
                    allowlistText = File.ReadAllText(allowlistPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"参数错误：读取白名单文件失败：{allowlistPath}：{ex.Message}");
                    return 2;
                }

                try
                {
                    allowlist = SchemaAuditAllowlist.Parse(allowlistText);
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine($"参数错误：白名单文件格式非法：{allowlistPath}：{ex.Message}");
                    return 2;
                }
            }

            // T-N6-2a：--schema-audit 同样要覆盖 sim.scenario/sim.anchor（见上面主校验路径同一处
            // ExtraSchemaRegistration 判断记录）——两个 Enumerate* 入口各自独立构造 registry（见其
            // 实现），必须各自传入同一份 options，否则 --schema-audit 会漏审这两张新表。
            var schemaAuditOptions = new ContentValidationOptions
            {
                ExtraSchemaRegistration = Core.Sim.SimSchemaCatalog.RegisterAll,
            };
            var schemas = SchemaAudit.EnumerateRegisteredSchemas(schemaAuditOptions);
            // 消费方反馈第 37 条：一并读出已登记的 DeclareReference 声明，供 declared_reference_unregistered
            // 检查使用（见 SchemaAudit.CheckDeclaredReferencesRegistered 判断记录）。
            var referenceDeclarations = SchemaAudit.EnumerateReferenceDeclarations(schemaAuditOptions);
            var report = SchemaAudit.Run(schemas, allowlist, referenceDeclarations);

            if (jsonOutput)
            {
                PrintSchemaAuditJson(report, schemas);
            }
            else
            {
                foreach (var issue in report.Issues)
                {
                    var loc = issue.FieldPath.Length == 0 ? issue.Table : $"{issue.Table}/{issue.FieldPath}";
                    Console.WriteLine($"[{issue.Severity}] {loc}: {issue.Check}: {issue.Message}");
                }
                Console.WriteLine($"tables {report.TableCount}, fields {report.FieldCount}, errors {report.ErrorCount}, warnings {report.WarningCount}");
            }

            return report.IsBlocking ? 1 : 0;
        }

        /// <summary>
        /// 消费方反馈第三批第 23 条（2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 23 条）：<paramref name="schemas"/>
        /// 新增输出到 <c>--json</c> 的 <c>table_names</c> 字段——本次已注册的全部表名（与
        /// <c>SchemaAudit.EnumerateRegisteredSchemas</c> 同一份快照，纯增量字段，不影响既有消费方
        /// 已在用的 <c>tables</c>/<c>fields</c>/<c>errors</c>/<c>warnings</c>/<c>blocking</c>/<c>issues</c>
        /// 字段）。供 <c>toolchain/validate_data.py</c> 的 <c>sample_table_empty</c> 告警级门禁
        /// （见该脚本 <c>check_sample_table_coverage</c> 判断记录）比对"已注册但 <c>data/_sample</c>
        /// 里一行都没有"的表——这类比对天然需要"全架构已注册哪些表"这份权威信息，只有 C# 侧持有。
        /// </summary>
        private static void PrintSchemaAuditJson(SchemaAuditReport report, IReadOnlyList<TableSchema> schemas)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"tables\":").Append(report.TableCount).Append(',');
            sb.Append("\"fields\":").Append(report.FieldCount).Append(',');
            sb.Append("\"errors\":").Append(report.ErrorCount).Append(',');
            sb.Append("\"warnings\":").Append(report.WarningCount).Append(',');
            sb.Append("\"blocking\":").Append(report.IsBlocking ? "true" : "false").Append(',');
            sb.Append("\"table_names\":[");
            for (var i = 0; i < schemas.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonEscape(schemas[i].Name)).Append('"');
            }
            sb.Append("],");
            sb.Append("\"issues\":[");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var issue = report.Issues[i];
                sb.Append('{');
                sb.Append("\"severity\":\"").Append(JsonEscape(issue.Severity)).Append("\",");
                sb.Append("\"table\":\"").Append(JsonEscape(issue.Table)).Append("\",");
                sb.Append("\"field_path\":\"").Append(JsonEscape(issue.FieldPath)).Append("\",");
                sb.Append("\"check\":\"").Append(JsonEscape(issue.Check)).Append("\",");
                sb.Append("\"message\":\"").Append(JsonEscape(issue.Message)).Append('"');
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append('}');
            Console.WriteLine(sb.ToString());
        }

        /// <summary>解析 <c>--display-map-sources</c> 的值：形如 <c>"table1:idField1,table2:idField2"</c>
        /// （逗号分隔多组，每组用一个冒号分隔表名与字段名，两侧均不能为空）。</summary>
        private static bool TryParseDisplayMapSources(
            string raw, out IReadOnlyList<(string table, string idField)>? sources, out string error)
        {
            var result = new List<(string table, string idField)>();
            var entries = raw.Split(',');
            foreach (var entry in entries)
            {
                var trimmedEntry = entry.Trim();
                if (trimmedEntry.Length == 0)
                {
                    continue;
                }

                var parts = trimmedEntry.Split(':');
                if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
                {
                    sources = null;
                    error = $"条目 \"{trimmedEntry}\" 不是 \"table:idField\" 形式";
                    return false;
                }

                result.Add((parts[0].Trim(), parts[1].Trim()));
            }

            if (result.Count == 0)
            {
                sources = null;
                error = "至少需要一组 \"table:idField\"";
                return false;
            }

            sources = result;
            error = "";
            return true;
        }

        private static string FormatIssue(ValidationIssue issue)
        {
            var severity = issue.Severity == ValidationSeverity.Error ? "error" : "warning";
            string loc;
            if (issue.RecordKey == null)
            {
                loc = issue.Table;
            }
            else if (issue.Field == null)
            {
                loc = $"{issue.Table}/{issue.RecordKey}";
            }
            else
            {
                loc = $"{issue.Table}/{issue.RecordKey}/{issue.Field}";
            }

            // T-N0-6（落地清单 2.2 V2）：可选的分组与说明原文以后缀形式追加在既有行尾，未填时行内容
            // 与此前逐字节相同。
            var line = $"[{severity}] {loc}: {issue.Check}: {issue.Message}";
            if (issue.Group != null)
            {
                line += $" [group: {issue.Group}]";
            }
            if (issue.Note != null)
            {
                line += $" [note: {issue.Note}]";
            }
            return line;
        }

        private static void PrintJson(
            ValidationReport report, int tableCount, int recordCount, IDataRegistryView? tablesForListing,
            IReadOnlyList<OverrideDiagnostic> overrides,
            IReadOnlyList<string> disabledOptionalRules, IReadOnlyList<string> enabledOptionalRules,
            IReadOnlyList<(string table, string idField)> effectiveDisplayMapCoverageSources,
            bool warnOnMissingTranslation, bool anchorProviderWired)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"tables\":").Append(tableCount).Append(',');
            sb.Append("\"records\":").Append(recordCount).Append(',');
            sb.Append("\"errors\":").Append(report.ErrorCount).Append(',');
            sb.Append("\"warnings\":").Append(report.WarningCount).Append(',');
            sb.Append("\"blocking\":").Append(report.IsBlocking ? "true" : "false").Append(',');

            if (tablesForListing != null)
            {
                sb.Append("\"tables_list\":[");
                var names = new List<string>(tablesForListing.Tables);
                names.Sort(StringComparer.Ordinal);
                for (var i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var count = report.IsBlocking ? -1 : tablesForListing.GetAll(names[i]).Count;
                    sb.Append('{').Append("\"name\":\"").Append(JsonEscape(names[i])).Append("\",\"record_count\":").Append(count).Append(',');

                    // ADR-0022 决策 5："导出给内容工具"：表级归属元数据（Layer/Module/Domain/
                    // TimeScope，04 第 3.4 节）——追加在既有 "record_count" 字段之后、"fields" 之前，
                    // 不改动任何既有字段，保持此前 --json 输出对不读取这些新键的调用方逐字节兼容。
                    var ownerSchema = tablesForListing.GetSchema(names[i]);
                    sb.Append("\"layer\":");
                    sb.Append(ownerSchema?.Layer == null ? "null" : "\"" + JsonEscape(ownerSchema.Layer.Value.ToString()) + "\"");
                    sb.Append(',');
                    sb.Append("\"module\":");
                    sb.Append(ownerSchema?.Module == null ? "null" : "\"" + JsonEscape(ownerSchema.Module) + "\"");
                    sb.Append(',');
                    sb.Append("\"domain\":");
                    sb.Append(ownerSchema == null ? "null" : "\"" + JsonEscape(ownerSchema.Domain) + "\"");
                    sb.Append(',');
                    sb.Append("\"time_scope\":\"").Append(JsonEscape((ownerSchema?.TimeScope ?? TimeScope.None).ToString())).Append("\",");
                    // 判断记录（消费方反馈 E11 根治，2026-09-10，见
                    // architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E11）：追加顶层字段的
                    // 登记顺序（TableSchema.Fields，构造时的登记顺序，见该类型注释）——
                    // toolchain/format_data.py --schema-order 靠这份顺序把示例数据记录字段重排成
                    // 与 schema 声明一致，不需要新增一个独立的验证器子命令，复用既有的
                    // --list-tables --json 组合（"选最省事且可测的"，本字段是在既有 tables_list
                    // 条目里追加的新增字段，不改动任何既有字段，--json 输出对不需要这份信息的
                    // 调用方保持逐字节兼容）。GetSchema 理论上不会返回 null（tablesForListing.Tables
                    // 枚举的都是已注册过 schema 的表名），仍防御性判空，避免这里意外抛异常。
                    var fieldSchema = tablesForListing.GetSchema(names[i]);
                    sb.Append("\"fields\":[");
                    if (fieldSchema != null)
                    {
                        for (var j = 0; j < fieldSchema.Fields.Count; j++)
                        {
                            if (j > 0) sb.Append(',');
                            sb.Append('"').Append(JsonEscape(fieldSchema.Fields[j].Name)).Append('"');
                        }
                    }
                    sb.Append(']');
                    sb.Append(',');

                    // ADR-0022 决策 5："导出给内容工具"：随每张表一并输出顶层字段的分组/单位/引用
                    // 目标元数据（04 第 3.4 节）——与上面 "fields" 名字数组同一层级范围（只覆盖顶层
                    // 字段，不递归子结构；子结构内部的 Group/Unit/引用目标暂不导出，留待后续按需
                    // 扩展，不影响本版三条决策 1～4 的验收范围）。
                    sb.Append("\"field_meta\":[");
                    if (fieldSchema != null)
                    {
                        for (var j = 0; j < fieldSchema.Fields.Count; j++)
                        {
                            if (j > 0) sb.Append(',');
                            AppendFieldMetaJson(sb, fieldSchema.Fields[j]);
                        }
                    }
                    sb.Append(']');
                    sb.Append(',');

                    // ADR-0021 决策 4："导出给内容工具"：随每张表一并输出已登记的字段范围约束
                    // （SchemaFieldRangeExport 判断记录），编辑器可据此在内容作者输入时就地校验，
                    // 不必等到一次完整的 DataRegistry.LoadAll。
                    sb.Append("\"field_ranges\":[");
                    if (fieldSchema != null)
                    {
                        var ranges = SchemaFieldRangeExport.Collect(fieldSchema);
                        for (var r = 0; r < ranges.Count; r++)
                        {
                            if (r > 0) sb.Append(',');
                            AppendFieldRangeJson(sb, ranges[r]);
                        }
                    }
                    sb.Append(']');
                    sb.Append(',');

                    // 消费方反馈第 60 条："导出给内容工具"：随每张表一并输出已登记的字段元素数量约束
                    // （SchemaFieldItemCountExport 判断记录），编辑器可据此在内容作者增删数组/IdList
                    // 元素时就地提示，不必等到一次完整的 DataRegistry.LoadAll。追加在既有 "field_ranges"
                    // 字段之后、闭合大括号之前，不改动任何既有字段。
                    sb.Append("\"field_item_counts\":[");
                    if (fieldSchema != null)
                    {
                        var itemCounts = SchemaFieldItemCountExport.Collect(fieldSchema);
                        for (var r = 0; r < itemCounts.Count; r++)
                        {
                            if (r > 0) sb.Append(',');
                            AppendFieldItemCountJson(sb, itemCounts[r]);
                        }
                    }
                    sb.Append(']');
                    sb.Append(',');

                    // 消费方反馈第 40 条："--list-tables --json 组合" 新增字段——每张表当前登记的
                    // schema_version（TableSchema.CurrentSchemaVersion）与迁移链形状（Migrations 的
                    // (from, to) 环节清单，按登记顺序，不代表已从 fromVersion 到 CurrentSchemaVersion
                    // 全部串通，链是否可用仍须调用 SchemaMigrator.BuildChain 才能确认）。追加在既有
                    // "field_ranges" 字段之后、闭合大括号之前，不改动任何既有字段，供内容工具不必靠
                    // 反射/复刻常量即可得知链形状（editor 侧此前只能自行硬编码或反射读取）。
                    sb.Append("\"schema_version\":").Append(fieldSchema?.CurrentSchemaVersion ?? 1).Append(',');
                    sb.Append("\"migrations\":[");
                    if (fieldSchema != null)
                    {
                        var migrations = fieldSchema.Migrations;
                        for (var m = 0; m < migrations.Count; m++)
                        {
                            if (m > 0) sb.Append(',');
                            sb.Append('{').Append("\"from\":").Append(migrations[m].FromVersion)
                                .Append(",\"to\":").Append(migrations[m].ToVersion).Append('}');
                        }
                    }
                    sb.Append(']');
                    sb.Append(',');

                    // 消费方反馈第 46 条收口（验收报告"必须修项"根治，1.38.0）：既有 "field_meta.
                    // deprecated" 只覆盖顶层字段（04 第 3.4 节），嵌套在 Fields/Item/Map 值/Variants
                    // 分支内的废弃字段（如 quest.def.rewards.xp、skill.def 效果参数
                    // scaling_stat/coefficient）此前 FieldSchema.IsDeprecated 已正确登记，但本导出
                    // 路径吐不出来（见 SchemaFieldDeprecationExport 类型顶部判断记录）。新增
                    // "deprecated_paths"：按 SchemaFieldDeprecationExport.Collect 递归收集本表全部
                    // （含顶层与任意深度嵌套）废弃字段，点路径记法与 "field_ranges"/SchemaAudit 的
                    // FieldPath 记法一致。追加在既有 "migrations" 字段之后、闭合大括号之前，不改动
                    // "field_meta" 既有形状，保持此前 --json 输出对不消费本键的调用方逐字节兼容。
                    sb.Append("\"deprecated_paths\":[");
                    if (fieldSchema != null)
                    {
                        var deprecatedPaths = SchemaFieldDeprecationExport.Collect(fieldSchema);
                        for (var d = 0; d < deprecatedPaths.Count; d++)
                        {
                            if (d > 0) sb.Append(',');
                            AppendDeprecatedPathJson(sb, deprecatedPaths[d]);
                        }
                    }
                    sb.Append(']');

                    sb.Append('}');
                }
                sb.Append("],");
            }

            sb.Append("\"issues\":[");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendIssueJson(sb, report.Issues[i]);
            }
            sb.Append("],");

            sb.Append("\"overrides\":[");
            for (var i = 0; i < overrides.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendOverrideJson(sb, overrides[i]);
            }
            sb.Append("],");

            // ADR-0018 决策 3 新增：如实汇报 ContentValidationAssembly.Run 返回的可选规则接线状态
            // （不静默跳过，见 ContentValidationOptions 两个可选接线参数的判断记录）。追加在既有
            // "overrides" 字段之后、闭合大括号之前，不改动任何既有字段，保持此前 --json 输出逐字节
            // 兼容。
            sb.Append("\"disabled_optional_rules\":[");
            for (var i = 0; i < disabledOptionalRules.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonEscape(disabledOptionalRules[i])).Append('"');
            }
            sb.Append("],");

            sb.Append("\"enabled_optional_rules\":[");
            for (var i = 0; i < enabledOptionalRules.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonEscape(enabledOptionalRules[i])).Append('"');
            }
            sb.Append(']');
            sb.Append(',');

            // 消费方反馈第 43 条：在既有 "disabled_optional_rules"/"enabled_optional_rules" 之后追加
            // "optional_rules"——每项 {rule, check, enabled}，rule/check 直接来自
            // ContentValidationAssembly.OptionalRules（单一来源，见该属性判断记录），enabled 按
            // disabledOptionalRules 是否含该规则名判定。消费方（编辑器问题面板）据此可以不必再自行
            // 维护一份 PascalCase 规则名 -> snake_case 检查名的映射表；不改动既有两个字段，保持此前
            // --json 输出逐字节兼容。
            sb.Append("\"optional_rules\":[");
            var optionalRuleDescriptors = ContentValidationAssembly.OptionalRules;
            for (var i = 0; i < optionalRuleDescriptors.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var descriptor = optionalRuleDescriptors[i];
                var ruleEnabled = true;
                for (var j = 0; j < disabledOptionalRules.Count; j++)
                {
                    if (disabledOptionalRules[j] == descriptor.RuleName)
                    {
                        ruleEnabled = false;
                        break;
                    }
                }

                sb.Append('{');
                sb.Append("\"rule\":\"").Append(JsonEscape(descriptor.RuleName)).Append("\",");
                sb.Append("\"check\":\"").Append(JsonEscape(descriptor.CheckName)).Append("\",");
                sb.Append("\"enabled\":").Append(ruleEnabled ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append(',');

            // 消费方反馈第 34 条：如实导出本次实际生效的 DisplayMapCoverageRule sources 清单——
            // 未传 --display-map-sources 时是 PresentationSchemaCatalog.DefaultDisplayMapCoverageSources
            // （见该属性判断记录），传了则是解析出的覆盖值；不区分这两种来源，字段名本身只承诺"本次
            // 实际用的是什么"，不承诺"是否显式传参"（后者已由 enabled_optional_rules 是否含
            // "DisplayMapCoverageRule" 间接表达）。追加在既有 "enabled_optional_rules" 字段之后、
            // 闭合大括号之前，不改动任何既有字段。
            sb.Append("\"display_map_coverage_sources\":[");
            for (var i = 0; i < effectiveDisplayMapCoverageSources.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"table\":\"").Append(JsonEscape(effectiveDisplayMapCoverageSources[i].table)).Append("\",");
                sb.Append("\"id_field\":\"").Append(JsonEscape(effectiveDisplayMapCoverageSources[i].idField)).Append('"');
                sb.Append('}');
            }
            sb.Append(']');

            // 消费方反馈第 42 条：如实导出本次是否启用了"非默认语言缺翻译报 Warning"（见
            // DataRegistryOptions.WarnOnMissingTranslation / --no-missing-translation-warning）。
            // 追加在既有 "display_map_coverage_sources" 字段之后，不改动任何既有字段。
            sb.Append(',');
            sb.Append("\"warn_on_missing_translation\":").Append(warnOnMissingTranslation ? "true" : "false");

            // 消费方反馈第 37 条："--list-tables --json 组合" 新增字段（同 "tables_list" 一样只在
            // --list-tables 传入时才有意义，见 tablesForListing 判空）：本次已登记的全部 DeclareReference
            // 声明（见 IDataRegistryView.GetReferenceDeclarations），供内容工具不必再按值弱推断
            // "这个 Id/IdList 字段到底是不是引用"（消费方反馈第 37 条 trigger_skill 案例）。追加在既有
            // "display_map_coverage_sources" 字段之后、闭合大括号之前，不改动任何既有字段。
            if (tablesForListing != null)
            {
                sb.Append(',');
                sb.Append("\"reference_declarations\":[");
                var declarations = tablesForListing.GetReferenceDeclarations();
                for (var i = 0; i < declarations.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendReferenceDeclarationJson(sb, declarations[i]);
                }
                sb.Append(']');
            }

            // 分阶段落地计划 T-N0-6（落地清单 2.2 V3；ADR-0035 决策 5 报告要求）："rules"——本次跑过的
            // 全部已注册规则（ValidationReport.Rules，按注册顺序、含命中 0 条的），每项
            // {id, severity, non_escalatable, hits}。
            // 分阶段落地计划 T-N5-3（数值规则核对表；ADR-0035 决策 5"报告为结构化产物……供仿真/编辑器
            // 消费"）：追加五个数值规则专属字段——category（命中 NumericValidationRuleCatalog 时固定
            // 为 "numeric"，否则 null，标记这条规则是否属于 04 第 5 节数值类校验项分级表管辖范围）、
            // group（04 分级表口径的数值域分组，非数值规则为 null）、check_names（本 RuleId 在分级表里
            // 对应的全部检查名——StatDefinitionValidationRule/ItemBudgetValidationRule/
            // SkillBudgetValidationRule 三个类各占两条，数组长度为 2；非数值规则为空数组）、
            // requires_anchor（是否依赖阶段 N6 才接入的 ISkillBudgetAnchorProvider，非数值规则恒
            // false）、enabled（T-N6-3a：不再恒等于 !requires_anchor——本工具现按"本次数据源是否含
            // sim.anchor"（见本文件 Main 方法接入点判断记录）动态装配真实 ISkillBudgetAnchorProvider，
            // anchorProviderWired 如实反映这一次运行是否真的接上了；requires_anchor 的三条规则
            // enabled = anchorProviderWired，非数值规则恒 true）。全部追加在既有四个字段之后、闭合
            // 大括号之前，不改动既有字段名与语义（禁止事项）。
            sb.Append(',');
            sb.Append("\"rules\":[");
            for (var i = 0; i < report.Rules.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var rule = report.Rules[i];
                var numericEntries = FindNumericRuleEntries(rule.RuleId);
                var isNumeric = numericEntries.Count > 0;
                var requiresAnchor = numericEntries.Any(e => e.RequiresAnchor);
                sb.Append('{');
                sb.Append("\"id\":\"").Append(JsonEscape(rule.RuleId)).Append("\",");
                sb.Append("\"severity\":\"").Append(rule.DefaultSeverity == ValidationSeverity.Error ? "error" : "warning").Append("\",");
                sb.Append("\"non_escalatable\":").Append(rule.NonEscalatable ? "true" : "false").Append(',');
                sb.Append("\"hits\":").Append(rule.HitCount).Append(',');
                sb.Append("\"category\":").Append(isNumeric ? "\"numeric\"" : "null").Append(',');
                sb.Append("\"group\":").Append(isNumeric ? "\"" + JsonEscape(numericEntries[0].Group) + "\"" : "null").Append(',');
                sb.Append("\"check_names\":[");
                for (var j = 0; j < numericEntries.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append('"').Append(JsonEscape(numericEntries[j].CheckName)).Append('"');
                }
                sb.Append("],");
                sb.Append("\"requires_anchor\":").Append(requiresAnchor ? "true" : "false").Append(',');
                sb.Append("\"enabled\":").Append(!requiresAnchor || anchorProviderWired ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append('}');
            Console.WriteLine(sb.ToString());
        }

        /// <summary>见 <see cref="PrintJson"/> 里 <c>reference_declarations</c> 字段的判断记录。</summary>
        private static void AppendReferenceDeclarationJson(StringBuilder sb, ReferenceDeclaration decl)
        {
            sb.Append('{');
            sb.Append("\"from_table\":\"").Append(JsonEscape(decl.FromTable)).Append("\",");
            sb.Append("\"field_path\":\"").Append(JsonEscape(decl.FieldPath)).Append("\",");
            sb.Append("\"to_table\":\"").Append(JsonEscape(decl.ToTable)).Append("\",");
            sb.Append("\"to_domain\":").Append(decl.ToDomain == null ? "null" : "\"" + JsonEscape(decl.ToDomain) + "\"").Append(',');
            sb.Append("\"is_optional\":").Append(decl.IsOptional ? "true" : "false").Append(',');
            sb.Append("\"source\":").Append(decl.Source == null ? "null" : "\"" + JsonEscape(decl.Source) + "\"");
            sb.Append('}');
        }

        private static void AppendOverrideJson(StringBuilder sb, OverrideDiagnostic diag)
        {
            // 消费方反馈第三批第 22 条：--json 两者都给——绝对路径字段保留（向后兼容既有消费方），
            // 新增根序号 + 相对路径字段。
            sb.Append('{');
            sb.Append("\"table\":\"").Append(JsonEscape(diag.Table)).Append("\",");
            sb.Append("\"record_key\":\"").Append(JsonEscape(diag.RecordKey)).Append("\",");
            sb.Append("\"overriding_location\":\"").Append(JsonEscape(diag.OverridingLocation)).Append("\",");
            sb.Append("\"overridden_location\":\"").Append(JsonEscape(diag.OverriddenLocation)).Append("\",");
            sb.Append("\"overriding_root_index\":").Append(diag.OverridingRootIndex).Append(',');
            sb.Append("\"overriding_relative_path\":\"").Append(JsonEscape(diag.OverridingRelativePath)).Append("\",");
            sb.Append("\"overridden_root_index\":").Append(diag.OverriddenRootIndex).Append(',');
            sb.Append("\"overridden_relative_path\":\"").Append(JsonEscape(diag.OverriddenRelativePath)).Append('"');
            sb.Append('}');
        }

        /// <summary>ADR-0022 决策 5："导出给内容工具"：单个顶层字段的分组/单位/IdList 引用目标元数据
        /// （04 第 3.4 节），供编辑器不必等一次完整加载即可按字段分组渲染表单、按引用目标提供自动
        /// 补全候选。</summary>
        private static void AppendFieldMetaJson(StringBuilder sb, FieldSchema field)
        {
            sb.Append('{');
            sb.Append("\"name\":\"").Append(JsonEscape(field.Name)).Append("\",");
            sb.Append("\"kind\":\"").Append(JsonEscape(field.Kind.ToString())).Append("\",");
            sb.Append("\"group\":\"").Append(JsonEscape(field.Group.ToString())).Append("\",");
            sb.Append("\"unit\":\"").Append(JsonEscape(field.Unit.ToString())).Append("\",");
            sb.Append("\"reference_table\":").Append(field.ReferenceTable == null ? "null" : "\"" + JsonEscape(field.ReferenceTable) + "\"").Append(',');
            sb.Append("\"reference_domain\":").Append(field.ReferenceDomain == null ? "null" : "\"" + JsonEscape(field.ReferenceDomain) + "\"").Append(',');
            sb.Append("\"free_ids\":").Append(field.FreeIds ? "true" : "false").Append(',');

            // 消费方反馈第 28 条（04 第 3.4 节勘误"IdList/Id 固定取值登记"）："导出给内容工具"：
            // AllowedValues 登记后随字段元信息一并导出，供编辑器渲染下拉候选/就地校验，不必等一次
            // 完整的 DataRegistry.LoadAll 才发现非法取值。未登记时为 null，保持既有输出对不消费本键的
            // 调用方逐字节兼容（新增字段追加在已有字段之后）。
            sb.Append("\"allowed_values\":");
            if (field.AllowedValues == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('[');
                for (var i = 0; i < field.AllowedValues.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(JsonEscape(field.AllowedValues[i].Value)).Append('"');
                }
                sb.Append(']');
            }
            sb.Append(',');

            // 消费方反馈第 29 条（04 第 3.4 节勘误"软引用元数据"）："导出给内容工具"：
            // SoftReferenceTable/SoftReferenceDomain 随字段元信息一并导出，供编辑器做自动补全/跳转——
            // 不参与 DataRegistry 加载期的引用完整性校验（见 FieldSchema.WithSoftReference 判断记录），
            // 纯粹是传达给内容工具的提示。
            sb.Append("\"soft_reference_table\":").Append(field.SoftReferenceTable == null ? "null" : "\"" + JsonEscape(field.SoftReferenceTable) + "\"").Append(',');
            sb.Append("\"soft_reference_domain\":").Append(field.SoftReferenceDomain == null ? "null" : "\"" + JsonEscape(field.SoftReferenceDomain) + "\"").Append(',');

            // ADR-0024 决策 5："导出给内容工具"：顶层字段登记了 Map（04 第 3.3 节"映射登记"）时，
            // 随其它字段元信息一并导出键约束（map_key）与值种类（map_value）——与 field_ranges 一样，
            // 只覆盖顶层字段，不递归导出映射值内部的子结构（那部分留给编辑器按需再查
            // --list-tables 的 field_ranges/未来扩展；本版只保证映射本身"键怎么补全/值大致是什么
            // 种类"这两条最有编辑器价值的信息）。未登记 Map 的字段两者均为 null，保持既有输出对不
            // 消费这两个新键的调用方逐字节兼容（新增字段追加在已有字段之后）。
            sb.Append("\"map_key\":");
            AppendMapKeyJson(sb, field.Map);
            sb.Append(',');
            sb.Append("\"map_value\":");
            AppendMapValueJson(sb, field.Map);
            sb.Append(',');

            // 分阶段落地计划阶段 N0 任务线 ②（T-N0-1 曲线形态登记的导出面，04 第 3.6 节）："导出给内容
            // 工具"：顶层字段登记了 FieldSchema.Curve 时导出 {shape, axis}（shape：breakpoints/saturation；
            // axis：level/item_level/value），编辑器据此渲染曲线编辑控件并标注横轴；未登记为 null，
            // 追加在既有字段之后，保持既有输出对不消费本键的调用方逐字节兼容。
            sb.Append("\"curve\":");
            if (field.Curve == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('{');
                sb.Append("\"shape\":\"").Append(field.Curve.Shape == CurveShape.Breakpoints ? "breakpoints" : "saturation").Append("\",");
                sb.Append("\"axis\":\"").Append(CurveAxisName(field.Curve.Axis)).Append('"');
                sb.Append('}');
            }
            sb.Append(',');

            // 消费方反馈第 46 条（04 第 3.4 节勘误"字段废弃元数据"）："导出给内容工具"：登记了
            // FieldSchema.WithDeprecated 时导出 {since, replaced_by, note}（未登记 replacedBy/note 时对应
            // 子键为 null，同其它可选元数据字段惯例）；未登记 IsDeprecated 时整个 "deprecated" 键为
            // null——内容工具（本反馈原始案例：编辑器侧 Editor.Core.Validation.DeprecatedFieldHints）
            // 据此可直接从 schema 回吐读取废弃提示，不需要再手工维护一份与 Description/升级指南人工
            // 核对的静态清单。追加在既有 "curve" 字段之后，不改动任何既有字段，保持此前 --json 输出对
            // 不消费本键的调用方逐字节兼容。
            sb.Append("\"deprecated\":");
            if (!field.IsDeprecated)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('{');
                sb.Append("\"since\":\"").Append(JsonEscape(field.DeprecatedSince ?? "")).Append("\",");
                sb.Append("\"replaced_by\":").Append(field.ReplacedBy == null ? "null" : "\"" + JsonEscape(field.ReplacedBy) + "\"").Append(',');
                sb.Append("\"note\":").Append(field.DeprecationNote == null ? "null" : "\"" + JsonEscape(field.DeprecationNote) + "\"");
                sb.Append('}');
            }
            sb.Append('}');
        }

        /// <summary>见 <see cref="PrintJson"/> 里 <c>deprecated_paths</c> 字段的判断记录。</summary>
        private static void AppendDeprecatedPathJson(StringBuilder sb, DeprecatedFieldInfo info)
        {
            sb.Append('{');
            sb.Append("\"path\":\"").Append(JsonEscape(info.FieldPath)).Append("\",");
            sb.Append("\"since\":\"").Append(JsonEscape(info.Since)).Append("\",");
            sb.Append("\"replaced_by\":").Append(info.ReplacedBy == null ? "null" : "\"" + JsonEscape(info.ReplacedBy) + "\"").Append(',');
            sb.Append("\"note\":").Append(info.Note == null ? "null" : "\"" + JsonEscape(info.Note) + "\"");
            sb.Append('}');
        }

        private static string CurveAxisName(CurveAxis axis)
        {
            switch (axis)
            {
                case CurveAxis.Level: return "level";
                case CurveAxis.ItemLevel: return "item_level";
                // T-N1-8（ADR-0030 决策 6）：新增等级差 Δ 横轴（combat.level_diff_table 的
                // miss_bonus/crit_suppression/xp_factor 三列），不落进 default 分支——否则会与
                // CurveAxis.Value（数值横轴）混淆，编辑器曲线编辑控件就无法区分"这是等级差、可为
                // 负"与"这是任意数值"两种语义（见 CurveAxis.LevelDiff 判断记录）。
                case CurveAxis.LevelDiff: return "level_diff";
                default: return "value";
            }
        }

        private static void AppendMapKeyJson(StringBuilder sb, MapSchema? map)
        {
            if (map == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('{');
            sb.Append("\"reference_table\":").Append(map.KeyReferenceTable == null ? "null" : "\"" + JsonEscape(map.KeyReferenceTable) + "\"").Append(',');
            sb.Append("\"reference_domain\":").Append(map.KeyReferenceDomain == null ? "null" : "\"" + JsonEscape(map.KeyReferenceDomain) + "\"").Append(',');
            sb.Append("\"free_keys\":").Append(map.FreeKeys ? "true" : "false");
            sb.Append('}');
        }

        private static void AppendMapValueJson(StringBuilder sb, MapSchema? map)
        {
            if (map == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('{');
            sb.Append("\"kind\":\"").Append(JsonEscape(map.ValueSchema.Kind.ToString())).Append('"');
            sb.Append('}');
        }

        private static void AppendFieldRangeJson(StringBuilder sb, FieldRangeInfo info)
        {
            var range = info.Range;
            sb.Append('{');
            sb.Append("\"field_path\":\"").Append(JsonEscape(info.FieldPath)).Append("\",");
            sb.Append("\"kind\":\"").Append(JsonEscape(info.Kind)).Append("\",");
            sb.Append("\"min\":").Append(range.Min.HasValue ? range.Min.Value.ToString(CultureInfo.InvariantCulture) : "null").Append(',');
            sb.Append("\"min_exclusive\":").Append(range.MinExclusive ? "true" : "false").Append(',');
            sb.Append("\"max\":").Append(range.Max.HasValue ? range.Max.Value.ToString(CultureInfo.InvariantCulture) : "null").Append(',');
            sb.Append("\"max_exclusive\":").Append(range.MaxExclusive ? "true" : "false").Append(',');
            sb.Append("\"describe\":\"").Append(JsonEscape(range.Describe())).Append('"');
            sb.Append('}');
        }

        /// <summary>见 <see cref="PrintJson"/> 里 <c>field_item_counts</c> 字段的判断记录。</summary>
        private static void AppendFieldItemCountJson(StringBuilder sb, FieldItemCountInfo info)
        {
            sb.Append('{');
            sb.Append("\"field_path\":\"").Append(JsonEscape(info.FieldPath)).Append("\",");
            sb.Append("\"kind\":\"").Append(JsonEscape(info.Kind)).Append("\",");
            sb.Append("\"min_items\":").Append(info.MinItems.HasValue ? info.MinItems.Value.ToString(CultureInfo.InvariantCulture) : "null").Append(',');
            sb.Append("\"max_items\":").Append(info.MaxItems.HasValue ? info.MaxItems.Value.ToString(CultureInfo.InvariantCulture) : "null");
            sb.Append('}');
        }

        /// <summary>分阶段落地计划 T-N5-3：按 <see cref="ValidationRuleSummary.RuleId"/> 查
        /// <see cref="ContentValidationAssembly.NumericRules"/>（见该属性判断记录，转发自
        /// <see cref="NumericValidationRuleCatalog"/>）——同一 RuleId 可能命中多条（如
        /// <c>StatDefinitionValidationRule</c> 在清单里占两条，见该类型两个 <c>Check*</c> 常量），
        /// 未命中（非数值规则）返回空列表，不返回 null（调用方一律按 <c>Count</c> 判空，不需要额外
        /// null 检查）。</summary>
        private static List<NumericValidationRuleDescriptor> FindNumericRuleEntries(string ruleId) =>
            ContentValidationAssembly.NumericRules.Where(e => e.RuleId == ruleId).ToList();

        private static void AppendIssueJson(StringBuilder sb, ValidationIssue issue)
        {
            sb.Append('{');
            sb.Append("\"severity\":\"").Append(issue.Severity == ValidationSeverity.Error ? "error" : "warning").Append("\",");
            sb.Append("\"table\":\"").Append(JsonEscape(issue.Table)).Append("\",");
            sb.Append("\"record_key\":").Append(issue.RecordKey == null ? "null" : "\"" + JsonEscape(issue.RecordKey) + "\"").Append(',');
            sb.Append("\"field\":").Append(issue.Field == null ? "null" : "\"" + JsonEscape(issue.Field) + "\"").Append(',');
            sb.Append("\"check\":\"").Append(JsonEscape(issue.Check)).Append("\",");
            sb.Append("\"message\":\"").Append(JsonEscape(issue.Message)).Append("\",");
            // T-N0-6（落地清单 2.2 V2）：追加在既有 "message" 之后，未填时为 null；既有字段名与
            // 语义一律不变（禁止事项）。
            sb.Append("\"group\":").Append(issue.Group == null ? "null" : "\"" + JsonEscape(issue.Group) + "\"").Append(',');
            sb.Append("\"note\":").Append(issue.Note == null ? "null" : "\"" + JsonEscape(issue.Note) + "\"").Append(',');
            sb.Append("\"rule_id\":").Append(issue.RuleId == null ? "null" : "\"" + JsonEscape(issue.RuleId) + "\"");
            // 消费方反馈第 57 条（2026-09-18）：图诊断（成环/不可达一类）涉及的节点 id 列表，
            // ValidationIssue.AffectedNodeIds 默认空集合——本字段空时整体省略（不是"null"），与既有
            // group/note/rule_id"未填也始终输出 null"的既有约定不同：这是设计层拍板的新字段专属口径
            // （见回复文档 消费方反馈-2026-09-18-编辑器-第54-58条.md），既有三个字段的既有约定不变。
            if (issue.AffectedNodeIds.Count > 0)
            {
                sb.Append(",\"affected_node_ids\":[");
                for (var i = 0; i < issue.AffectedNodeIds.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append('"').Append(JsonEscape(issue.AffectedNodeIds[i])).Append('"');
                }
                sb.Append(']');
            }
            sb.Append('}');
        }

        private static string JsonEscape(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

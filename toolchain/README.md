# toolchain

跨游戏的工具链目录：数据校验、构建辅助、资产导入等脚本，供本框架及各游戏仓库共用。

数据校验分两道（T2-12 落地）：`validate_data.py` 自己只做第一道骨架级通用检查（信封/表名/id
格式）；第二道真实校验（字段级必填/类型/枚举、引用完整性、文本键存在、Expr 可解析，以及
skill/combat/target/ai 等各模块的专属校验规则）复用 `core/foundation/data_registry.DataRegistry`
与 `core/rules/assembly.RulesSchemaCatalog`，由 `validate_data.py` 以子进程调用
`toolchain/validator`（一个 .NET 控制台工具）完成——两套判断逻辑不重复实现，`core` 内的校验
规则是唯一权威来源。

## 控制台编码

本目录下的命令行工具脚本（`validate_data.py`/`gen_event_constants.py`/
`gen_placeholder_assets.py`/`import_assets.py` 及其 `asset_import` 包）打印的说明/错误信息
都是中文。Windows 控制台默认代码页通常不是 UTF-8——尤其是非交互式场景（CI 运行器的管道
重定向、被其他进程捕获输出等），Python 拿不到真实控制台代码页，会退化为系统 ANSI 代码页
（例如英文版 Windows/GitHub Actions `windows-latest` 运行器上是 `cp1252`），这时 `print()`
遇到中文字符会直接抛 `UnicodeEncodeError` 崩溃，而不只是打印乱码。

统一的修法是 `toolchain/_console.py` 提供的 `ensure_utf8_stdio()`：把 `sys.stdout`/
`sys.stderr` 显式 reconfigure 成 UTF-8，不依赖运行环境的默认代码页；`reconfigure` 在极少数
不支持的重定向目标上会抛 `AttributeError`/`OSError`，静默忽略即可。本目录下全部命令行入口都
在最开始调用它（`toolchain/asset_import/common.py` 的 `setup_utf8_streams()` 也已改为委托
给同一个函数，不再各自维护一份）——新增命令行入口脚本时同样要在 `main()` 最开始调用
`ensure_utf8_stdio()`（顶层脚本用 `sys.path.insert(0, str(Path(__file__).resolve().parent))`
把 `toolchain/` 目录本身放进 `sys.path` 后 `from _console import ensure_utf8_stdio`，惯例见
`toolchain/gen_event_constants.py`；`asset_import` 包内新增模块直接
`from _console import ensure_utf8_stdio`，包入口已把 `toolchain/` 放进 `sys.path`）。

`.github/workflows/ci.yml` 额外在 job 级 `env` 设置了 `PYTHONUTF8: "1"` /
`PYTHONIOENCODING: "utf-8"` 作为双保险；这两个环境变量不能替代 `ensure_utf8_stdio()`——新脚本
忘了调用它、又在这两个环境变量不生效的场合（例如本机开发者直接双击运行、或未来某个调用方
清空了环境变量）运行，仍然会在非 UTF-8 控制台上崩溃。

## `.ps1`/`.psm1` 脚本源码编码（UTF-8 BOM）

判断记录（2026-09-08，发布前 CI 复核发现，见 `architecture/11_工程规范与测试.md` 第 8 节
提交门槛清单对应勘误行）：本机跑完 `build.ps1 -Release 1.7.0 -PublishRegistry` 并本地发布
三包后、推送前，发现远端 CI 在同一提交上失败——`toolchain/_hash.ps1` 没有 UTF-8 BOM 且含中文
注释，托管运行器的 Windows PowerShell 5.1 在文件没有 BOM 时按系统 ANSI 代码页（`cp1252`，与
上面"控制台编码"一节提到的 GitHub Actions `windows-latest` 默认代码页是同一个）读取脚本源码，
中文注释 UTF-8 字节序列里的 `0x93`/`0x94` 被当成弯引号字符，导致 `_hash.ps1:48` 报
`TerminatorExpectedAtEndOfString`（字符串终止符缺失）。本机开发环境代码页是 GBK，同样不是
UTF-8，但字节巧合下没有触发这个具体的解析错误，本机门禁没能暴露这个问题——即"本机代码页 A 下
能跑通"不能替代"目标运行代码页 B 下也能跑通"的验证，两者都不是脚本实际编码，谁踩坑纯属巧合。

根治：给 `_hash.ps1` 补上 UTF-8 BOM（内容不变，只在文件开头加 3 字节 `EF BB BF`），并把"脚本
含非 ASCII 字符必须带 BOM"升级为门禁校验——`toolchain/tests/test_powershell_scripts_ansi_safe.py`
对仓库内跟踪的每个 `.ps1`/`.psm1` 文件（`audit-*/` 下的历史审计证据脚本
除外）做两件事：(a) 含非 ASCII 字节的文件必须以 BOM 开头；(b) 用 PowerShell 语言分析器
（`Parser.ParseInput`，只做语法解析、不执行）复核脚本能否被正确解析——没有 BOM 的文件按
`cp1252` 解码文件字节模拟"目标运行代码页误读"场景，带 BOM 的文件按 PowerShell 自身
`Get-Content -Raw` 的默认读取（BOM 会被正确识别，不需要额外模拟）。该测试在补 BOM 之前对
`_hash.ps1` 确认能失败（复现出与 CI 相同的解析错误类别），补 BOM 之后转为通过，确认门禁本身
有效而非形同虚设。

## 虚拟环境

```
python -m venv toolchain/.venv
```

`toolchain/.venv/` 已在仓库根 `.gitignore` 中忽略，不会被提交。数据校验（`validate_data.py`）
本身无第三方依赖；资产导入工具（`import_assets.py`）需要 Pillow，激活虚拟环境后执行：

```
pip install -r toolchain/requirements.txt
```

`requirements.txt` 只含必需依赖（当前是 Pillow）；`rembg`（`import_assets.py --matting rembg`
用到的可选抠图后端，体积大且需要本机预先准备好 `~/.u2net/*.onnx` 权重才真正可用）拆到单独的
`requirements-optional.txt`，按需再装：

```
pip install -r toolchain/requirements-optional.txt
```

`check.ps1`/CI 一键门禁只依赖 `requirements.txt`（Pillow），不需要 `requirements-optional.txt`。

## 运行校验器

从仓库根目录运行（脚本内部按自身文件路径推导仓库根，从其他目录运行结果相同；第二道校验会用
`subprocess` 以仓库根为工作目录调用 `dotnet run --project toolchain/validator`，需要能在
`PATH` 里找到 `dotnet`）：

```
python toolchain/validate_data.py
```

首次运行第二道校验时，`dotnet run` 会自动编译 `toolchain/validator`（打印 MSBuild 输出），
之后的运行会复用已编译的产物，速度明显加快。

常用参数：

- `--data-root <path>`：默认 `data`（相对仓库根），指定要校验的数据根目录。
- `--dataset <name>`：只校验 `data/<name>/` 下的表，省略则校验 `--data-root` 下全部表（这个
  子目录同时作为两道校验共同的输入范围）。
- `--verbose`：输出更详细的检查过程信息（第一道骨架检查）。
- `--strict`：第二道校验里 Warning 也阻断（透传为 `toolchain/validator --strict`，等价于
  `DataRegistryStrictness.WarningsBlock`）。
- `--skip-dotnet`：只跑第一道骨架检查，跳过第二道（没有安装 .NET SDK 的环境可用）。

判断记录（数据根非数据表 JSON 误判修复任务，2026-09-16）：`--data-root` 指向的目录下，递归找到
的 `*.json` 文件不再一律当成数据表——游戏侧仓库常见"数据目录旁边/上层还有 `package.json` 之类
配置文件"的布局（如 UPM 包根目录），"看起来不像数据表"的文件（文件名解析不出 04 第 2.2 节"域"、
或不在对应域子目录/数据根下，`package.json`/误放的 `xxx.config.json` 皆属此类）会被跳过，
打一行 `[skip] <path>: 非数据表文件（...）` 到标准错误（不是 Warning/Error，`--strict` 下也不
阻断，不计入下方"checked N files"）。两道校验（本脚本第一道骨架检查、`toolchain/validator`
第二道真实校验，以及游戏运行期 `Core.Foundation.DataRegistry.DataRegistry.LoadAll` 本身）走同一
条判定规则的独立实现，见 `core/foundation/data_registry/core/FileSystemDataSource.cs` 类型注释、
`validate_data.py` 内 `is_data_table_candidate` 函数注释。

## 返回码约定

- `0`：两道检查全部通过，无错误。
- `1`：至少一道检查报出错误（第一道逐行打印到标准输出，汇总
  `[第一道·骨架检查] checked N files, M errors`；第二道的每条问题打印为
  `[severity] table/key/field: check: message` 格式，汇总
  `tables N, records M, errors E, warnings W`，格式定义见 `toolchain/validator/Program.cs`）。
- `2`：命令行参数错误（如指定了不存在的 `--data-root`），或找不到 `dotnet` 可执行文件且未传
  `--skip-dotnet`。

第一道脚本只做骨架级通用检查（信封三键、表名一致、id/key 格式）；04 第 5 节校验器检查项清单
中的引用完整性、枚举合法、表达式可解析、模块专属规则（效果数上限、叠加类别冲突、命中表概率
区间、抗性曲线单调性、目标链来源已注册与无环、AI 优先级唯一性等）均由第二道
`toolchain/validator` 覆盖，见该项目下方说明；两道检查项不重叠，也不重复实现同一条判断逻辑。

## `toolchain/validator`（.NET 真实校验工具）

`toolchain/validator/Validator.csproj`（`net8.0` 控制台项目，已加入 `Core.sln`）：复用
`core/foundation/data_registry.DataRegistry`（加载 + 字段级校验）与
`core/rules/assembly.RulesSchemaCatalog.RegisterAll`（一次性注册 L0～L2 全部 `TableSchema`/
`IValidationRule`/已知外键），对磁盘上的真实数据目录跑一遍完整校验。自带
`DiskFileSystem`（`toolchain/validator/DiskFileSystem.cs`）——`adapters/stub` 的
`StubFileSystem` 只有内存实现，本工具需要读真实文件，因此在工具自己的目录下补一个只读磁盘
实现（不放进 `core/`，也不修改 `adapters/stub` 任何一行）。

判断记录（P02 根治，2026-09-07，审计 `audit-7e63d66-20260907/
project-review.md` P02）：`Validator.csproj` 对核心程序集（`Core.Foundation`/`Core.Numbers`/
`Core.Rules`/`Core.Carriers`/`Core.Gameplay`/`Presentation.Common`）的引用按"`presentation/`
源码树是否存在"二选一——本仓库内（`presentation/Presentation.Common.csproj` 存在）走
`ProjectReference`，与改动前行为一致；独立发行包内（`dist/ws-game-<ver>.zip` 的
`toolchain/validator/`、UPM `com.gamefoundation.toolchain` 包内 `Tools~/validator/`，两者都不
随包分发 `presentation/`/`core/`/`adapters/stub/` 源码）改引用同目录下 `lib/` 子目录的编译产物
DLL——`lib/` 由 `build.ps1` 打分发包步骤显式补齐（源头是同一次打包已经拷进 dist 的适配层包
`Runtime/Plugins/Core/` 产物），不提交到 git、也不存在于源码仓库本身。此前无条件引用
`../../presentation/Presentation.Common.csproj` 与 `../../adapters/stub/Adapters.Stub.csproj`
（后者是历史遗留死引用，本工具代码从未使用任何 `Adapters.Stub.*` 类型），导致独立包内
`dotnet build`/`dotnet run --project toolchain/validator` 因引用路径不存在而失败（ZIP、UPM
两条独立包消费复现均命中，见审计证据）。

判断记录（ADR-0018 决策 3，校验装配入口，2026-09-09）：`Program.cs` 不再自行内联"建 EventBus +
建 `DataRegistry` + `PresentationSchemaCatalog.RegisterAll` + `LoadAll` + 汇总"这一整段装配逻辑——
改为调用核心库 `presentation/assembly/ContentValidationAssembly.cs` 提供的单一公开入口
`Presentation.Assembly.ContentValidationAssembly.Run`。本工具现在只做"命令行参数解析 → 调用该
入口 → 打印"三件事，与编辑器基础套件（ADR-0018 决策 1/2 定义的独立消费方项目，随游戏走）共用
同一份注册顺序与选项默认值，见 `data/README.md`"与校验器的关系"一节、
`presentation/assembly/README.md`。

直接运行（不经 `validate_data.py`）：

```
dotnet run --project toolchain/validator -- --data-root <dir> [--strict] [--json] [--list-tables] [--display-map-sources <table:idField,...>] [--enable-graph-isolation]
```

参数：

- `--data-root <dir>`：必填，数据根目录；相对路径按当前工作目录解析（从仓库根运行时等价于
  "相对仓库根"），绝对路径原样使用。
- `--strict`：Warning 也阻断（`DataRegistryStrictness.WarningsBlock`）。
- `--json`：输出机器可读的单行 JSON（`{tables, records, errors, warnings, blocking, issues[],
  overrides[], disabled_optional_rules[], enabled_optional_rules[], optional_rules[], rules[]}`），不输出
  人类可读文本行。分阶段落地计划 T-N0-6（落地清单 2.2 V2/V3）新增：`rules`——本次跑过的全部已注册规则
  （`ValidationReport.Rules`，按注册顺序、含命中 0 条的），每项 `{id, severity, non_escalatable, hits}`；
  `issues[]` 每项追加 `group`/`note`/`rule_id`（未填为 `null`）。文本模式对应追加末尾 `rules (N):` 段与
  问题行尾的 ` [group: …]`/` [note: …]` 后缀（未填时行内容不变）。既有字段名与语义一律不变。

  分阶段落地计划 T-N5-3（数值规则核对表；ADR-0035 决策 5）在 `rules[]` 每项之后再追加五个数值规则
  专属字段：`category`（命中 `Presentation.Assembly.NumericValidationRuleCatalog` 时固定为
  `"numeric"`，标记这条规则属于 04 第 5 节数值类校验项分级表管辖范围，否则 `null`）、`group`（该
  清单登记的数值域分组，如"属性"/"技能"/"装备"/"经验"/"经济"/"曲线"/"职业"，非数值规则为
  `null`——注意与 `issues[].group` 不是同一件事，后者是某一条具体问题的业务意图分组，如技能预算
  偏离的"已确认"/"待确认"，前者是某条规则/检查名整体归属的数值域，两者同名但层级不同）、
  `check_names`（该 `RuleId` 在分级表里对应的全部检查名数组——大多数长度为 1，
  `StatDefinitionValidationRule`/`ItemBudgetValidationRule`/`SkillBudgetValidationRule` 三个类各占
  两条检查名，长度为 2；非数值规则为空数组）、`requires_anchor`（是否依赖阶段 N6 才接入的
  `ISkillBudgetAnchorProvider`，非数值规则恒 `false`；数值规则里仅"技能预算硬上限""技能预算偏离"
  "授予价值超特效占比"三条为 `true`）、`enabled`（`!requires_anchor`——上述三条规则已注册但锚点未
  接入前不会真的产出任何问题，如实标 `false`，同 1.29.0 `optional_rules[].enabled` 口径；其余规则
  恒 `true`）。文本模式对应给命中数值规则清单的行追加 `, numeric group=<group>` 与（锚点依赖时）
  `, requires_anchor (未接入, 当前 enabled=false)` 后缀。既有字段名与语义一律不变。

  消费方反馈第 43 条新增 `optional_rules`：每项 `{rule, check, enabled}`——`rule`/
  `check` 直接取自 `ContentValidationAssembly.OptionalRules`（单一来源，规则名 ↔ 检查名不再需要
  消费方自行维护 PascalCase→snake_case 映射表），`enabled` 按该规则是否出现在
  `disabled_optional_rules` 判定；追加在既有 `enabled_optional_rules` 字段之后，不改动既有任何
  字段的取值。
- `--list-tables`：额外列出本次加载到的全部表名与记录数（文本模式下逐行打印，JSON 模式下并入
  `tables_list` 字段）。
- `--display-map-sources <table:idField,...>`（ADR-0018 决策 3 新增，可选）：接线
  `ContentValidationOptions.DisplayMapCoverageSources`，声明哪些内容表参与"外形映射存在"检查
  （`DisplayMapCoverageRule`，见 04 第 5 节检查项清单）——逗号分隔多组 `table:idField`。未传时该
  可选规则仍默认启用（消费方反馈第 34 条根治：改用
  `PresentationSchemaCatalog.DefaultDisplayMapCoverageSources`），本参数只用来覆盖默认接线源，不
  是"传了才启用"的开关。`SpawnSummonOnlyCreatureRule` 自 1.29.0 起默认接线：本工具不显式传
  `ContentValidationOptions.CreatureTemplateQuery`（一次性命令行进程没有真正的
  `ICreatureTemplateQuery` 实现可用，这一点没变），但 `ContentValidationAssembly` 未提供时改用
  `Core.Carriers.Creature.RegistryCreatureTemplateQuery`（基于已构造的 registry 现读现解析，见该
  类型判断记录），规则因此默认启用，不再是"仍不接线"。
- `--enable-graph-isolation`（消费方反馈第 56 条追问，可选，本工具首个"命令行式可选规则开关"——与
  上面 `--display-map-sources` 那种"接线参数是否提供决定是否启用"不同，本参数是独立的纯布尔开关）：
  接线 `ContentValidationOptions.EnableGraphIsolationDiagnostics`，开启 `quest_prerequisite_node_isolated`
  （`QuestPrerequisiteIsolationRule`）/`talent_node_isolated`（`TalentTreeIsolationRule`）两条默认
  **不**注册的展示性提示规则——分别提示 `quest.def`/`arch.talent_tree` 图里既无前置也未被引用的
  孤立节点（两类图里孤立节点均属合法内容形态，见两条规则类型判断记录，默认关闭、Warning 级、不可
  提升为阻断）。未传本参数时 `disabled_optional_rules` 含这两条规则名（不再"当前入口下默认恒为空"，
  与另外两条"提供依赖即启用"的可选规则不同）。
- `field_ref_category`（`RefCategoryFieldRule`，`Core.Foundation.DataRegistry`）——校验资源引用
  字段的类别前缀合法且落在该字段 schema 声明的允许集合内（`FieldSchema.WithAllowedRefCategories`）。
  ADR-0038 落地初期曾以 `--enable-ref-category-check` 命令行开关默认关闭（当时样例数据集
  `display.equip_visual.sample_hero_hat` 一行的 `mesh_ref` 还是遗留的 `sprite.*` 前缀，开启会让该
  行报错、拖垮 `validate_data.py --strict` 门禁）；数据迁移任务完成后该行已迁移到 ADR-0038 决策 4
  新增的 `paperdoll.*` 前缀，本规则随即转正为无条件注册（与 `DisplayKindFieldGroupRule`/
  `EquipVisualModeFieldGroupRule` 同等地位），命令行开关与对应选项已一并删除，不再出现在
  `disabled_optional_rules`/`enabled_optional_rules` 里。

问题逐条打印为 `[severity] table/key/field: check: message`（`key`/`field` 缺失时对应段落省略），
`severity` 取 `error`/`warning`，`check` 是 04 第 5 节固定的检查项名或各模块 `IValidationRule`
自行命名的检查项名；末尾汇总一行 `tables N, records M, errors E, warnings W, overrides O`，再追加
一行 `optional rules disabled: <规则名逗号分隔，或 none>`（ADR-0018 决策 3 新增，如实汇报本次未
接线的可选规则，不静默跳过）。返回码：`0` 未阻断；`1` 阻断（存在 Error，或 `--strict` 下存在
Warning）；`2` 命令行参数错误（缺 `--data-root`、目录不存在、`--display-map-sources` 格式非法）。

### 元数据门禁（--schema-audit，F3 新增）

与上面"数据校验"是两种不同的运行模式：不需要 `--data-root`、不加载任何实际数据，只审计**代码里
已登记的 `TableSchema`/`FieldSchema` 结构声明本身**是否完整、自洽（ADR-0018 决策 3、ADR-0019 决策
4）——核心审计逻辑在 `presentation/assembly/SchemaAudit.cs`（`Presentation.Assembly.SchemaAudit`），
与"数据校验"共用同一份 `ContentValidationAssembly` 装配顺序，保证"编辑器里看到的红线 = 门禁会报
的错"这一 ADR-0018 一以贯之的验收标准。

```
dotnet run --project toolchain/validator -- --schema-audit [--allowlist <path>] [--json]
```

- `--allowlist <path>`：白名单文件路径（本仓库固定用 `toolchain/schema_audit_allowlist.json`，见
  该文件头注释），省略时视为空白名单（不豁免任何 `composite_without_substructure` 命中）。
- `--json`：输出单行 JSON（`{tables, fields, errors, warnings, blocking, issues[]}`）。

检查项（递归进入 `Fields`/`Item`/`Variants.CommonFields`/`Variants.Cases[*]`，字段路径记法同
`SchemaAuditIssue.FieldPath`——`effects[].params.base_value`、`shape{kind=circle}.radius`）：

| Check | 级别 | 规则 |
|---|---|---|
| `missing_description` | error | 任一层级 `FieldSchema.Description` 为空/空白 |
| `composite_without_substructure` | error | `Object` 无 `Fields`/`Variants`，或 `Array` 无 `Item`，且未在白名单里豁免 |
| `allowlist_entry_unused` | warning | 白名单条目在当前登记里找不到对应的 `composite_without_substructure` 命中 |
| `reference_target_unknown` | error | `Reference` 字段的 `ReferenceTable` 不在已登记表清单；`ReferenceDomain` 不在 04 第 2.2 节域名清单 |
| `variant_shape` | error | `Variants.Cases` 键为空、某 case 字段与 `CommonFields`/判别字段同名 |
| `unschematized_table` | warning | `TableSchema.IsUnschematized` 的表 |

白名单只允许豁免 `composite_without_substructure`，不允许豁免 `missing_description`——描述缺失
必须真正补齐，不能靠白名单绕过（见 `toolchain/schema_audit_allowlist.json` 头注释、条目清单与
逐条 reason）。文本模式末尾汇总一行 `tables N, fields M, errors E, warnings W`。返回码：`0` 未
阻断（`errors == 0`）；`1` 阻断；`2` 命令行参数错误（白名单文件不存在/格式非法，含文件内容不是合法 JSON）。`check.ps1`
"元数据门禁"步骤即上面这条命令，`-Quick` 下也跑（秒级，不需要 Unity/构建产物）。

## `toolchain/simrunner`（数值仿真报告与基线比对命令行工具，T-N6-6/T-N6-7）

`toolchain/simrunner/SimRunner.csproj`（`net8.0` 控制台项目，已加入 `Core.sln`，工程惯例照抄
`toolchain/validator` 的 `lib/` 分发分支）：对给定数据根跑 `sim.scenario` 场景，产出统一信封
`Core.Sim.SimReport`（`*.report.json`），可选与既往基线 `Core.Sim.SimBaseline` 比对输出差异
（`*.diff.txt`/`*.diff.json`）或用 `--update-baseline` 覆写基线。命令用法、参数、退出码约定、
控制台摘要格式详见 `core/sim/README.md`"命令行入口"一节；基线更新的五步流程（同
`core/gameplay/tests/Replay/README.md`"如何更新基线"一节同一原理）见该文件"基线更新流程"一节，
薄封装脚本 `toolchain/sim_baseline.ps1` 提供 `-Scenario`/`-UpdateBaseline`/`-Out`/
`-ArtifactsPath` 等参数（`--project` 路径按脚本自身目录解析，两种布局——源码仓库
`toolchain/sim_baseline.ps1` 与 UPM 包 `Tools~/sim_baseline.ps1`——下都能正确找到同级
`simrunner/`，见该脚本 T-N6-7 判断记录）。本工具与 `toolchain/validator` 是并列的两个独立控制台
工程，各自维护一份不含业务逻辑的 `DiskFileSystem`（只读磁盘适配层），互不引用；`lib/` 回退分支
T-N6-7 补全为完整 7 个 DLL（此前只列 3 个，缺 4 个运行期传递依赖，独立发行包内编译能通过但
运行会抛 `FileNotFoundException`，见该 csproj 判断记录）。

T-N6-7 已接入 `check.ps1`"数值仿真基线比对"步骤（直接执行步骤 1 已构建的 `SimRunner.dll`，不
`dotnet run` 重复编译，-Quick 跳过）、随 `check.ps1 -SkipUnity` 一并纳入 CI（`.github/
workflows/ci.yml` 不需要改动）、随 `build.ps1 -Dist`/`-Release` 打包——预编译产物（`bin/`+
`lib/`）随 `toolchain/` 打进 dist 与 `com.gamefoundation.toolchain` 包 `Tools~/simrunner/`，
`Core.Sim.dll` 随 `Adapters.Stub.dll` 一起补进 `dist/<ver>/adapters/headless/` 与
`com.gamefoundation.adapter.headless` 包 `Lib~/`，`toolchain/validator/lib/`（含 UPM 包内
对应位置）补齐 `Core.Sim.dll`/`Adapters.Stub.dll` 两个此前缺失的 DLL（判断记录 10 缺口消除）。
详见 `build.ps1` 对应各节判断记录、`core/sim/README.md`"T-N6-7 判断记录"。

**`fight` 子命令（单场战斗与逐条战斗日志，NF 清扫新增，补上消费方反馈第 49 条当时"命令行没有指定对手打一场的入口"的缺口）**：

```
dotnet <SimRunner.dll> fight --framework-root <dir> --data-root <dir> [--data-root <dir2> ...] \
  --class <arch.class.id> --quality <item.quality.id> --creature <creature.id> \
  [--player-level <n>=1] [--creature-level <n>=玩家等级] [--seed <n>=1] [--max-ticks <n>] \
  [--fight-log <file>] [--max-log-entries <n>] [--json]
```

- 标准输出首行摘要 `fight outcome=… ticks=… duration_s=… player_damage=… creature_damage=… player_hit_rate=… creature_hit_rate=… log_entries=… log_truncated=…`；`--json` 时再打一份聚合结果 JSON（不含逐条日志，无穷大的 `ttd_estimate` 写 `null`）。
- `--fight-log <file>`：开启 `FightRunnerOptions.CaptureEvents`，把 `FightResult.CapturedEvents` 写成 `{schema_version:1, truncated, entries:[{tick,category,source_id,target_id,skill_or_effect_id,amount,result_tag}]}`（UTF-8 无 BOM；`category` 取 `FightLogEventCategory` 枚举名的 snake_case）；超过 `--max-log-entries` 截断并置 `truncated`。不传 `--fight-log` 时不采集（零开销，聚合结果与采集时逐字段一致）。
- 退出码：`0` 跑完（不论胜负——"玩家输了"不是命令失败）；`2` 参数错误或数据装载阻断（缺必填参数、未知生物/职业、`--max-log-entries` 缺 `--fight-log` 等），不写日志文件；`4` Ctrl+C 取消。与 `run` 子命令的 0/1/3 基线语义互不相干。
- 判断记录：①独立子命令而不是给 `run` 加参数——一场自选对手的战斗没有基线可比，塞进 `run` 会搅乱它的参数组合与退出码语义；②`--quality` 必填（标准玩家装备品质没有天然缺省），其余四个参数有自然缺省；③同参数（含种子）两次运行的标准输出与日志文件逐字节相同。测试：`toolchain/tests/test_simrunner_cli.py`（`test_fight_*`：日志里玩家/生物来源的 damage 条目之和等于摘要里的聚合伤害、两次运行逐字节一致且开不开采集聚合不变、截断、参数错误与未知生物退出 2）。

## `toolchain/feellab`（手感实验室无头宿主命令行，手感落地 M2-D 起随发布产物分发）

`toolchain/feellab/FeelLab.csproj`（`net8.0` 控制台项目，已加入 `Core.sln`）：命令（`run`/`suite`/`export-test`/`list`/
`invariants`）、参数与基线更新流程见 `lab/README.md`。分发方式与 `simrunner` 同一治理：源码树存在时 `ProjectReference`
内核 `lab/Lab.Kernel.csproj`，独立发行包里改引用 `lib/` 下 9 个预编译 DLL；`build.ps1 -Dist`/`-Release` 把预编译产物
（`bin/`+`lib/`+空 `Directory.Build.props`）补进 `dist/<ver>/toolchain/feellab/` 与 `com.gamefoundation.toolchain` 包
`Tools~/feellab/`，实验室数据集 `data/_lab`、`data/_lab_action`、占位装备集 `data/_equip` 与夹具 `lab/fixtures` 按仓库同路径
进 dist 根（dist 根即实验室根），私服包另在 `Tools~/feellab/labroot/` 放自包含的实验室根；MANIFEST 增 `[feellab]` 段。
`toolchain/consumer_smoke.ps1` 有一步在消费方工作目录里用 dist 里的预编译命令行跑 `suite` 与 `invariants`（验证"只消费发布产物
的游戏能自己跑实验室"）；`check.ps1` 的 pkg_manifest 步骤要求 toolchain 包清单里有预编译命令行与实验室根的代表文件。

## 生成事件常量（gen_event_constants.py）

读取事件词汇登记表 `data/_sample/found/found.event_catalog.json`，为每一行生成一个
`Core.Foundation.Common.Id` 强类型常量，写入
`core/foundation/event_bus/generated/EventKeys.g.cs`（生成物，不可手改；修改登记表后
重新运行本脚本，见该文件头部注释与 `core/foundation/event_bus/README.md`）。

```
python toolchain/gen_event_constants.py
```

常用参数：

- `--catalog <path>`：事件词汇登记表 JSON 路径，相对仓库根（默认
  `data/_sample/found/found.event_catalog.json`）。
- `--output <path>`：生成文件输出路径，相对仓库根（默认
  `core/foundation/event_bus/generated/EventKeys.g.cs`）。
- `--namespace <ns>`：生成类型所在命名空间（默认 `Core.Foundation.EventBus`）。
- `--check`：不写文件，只比较生成内容与 `--output` 现有文件是否一致；不一致返回码 1。
  **提交门槛**：改过 `found.event_catalog.json` 后应先跑
  `python toolchain/gen_event_constants.py`（覆盖生成文件）再提交；CI/预提交可用
  `python toolchain/gen_event_constants.py --check` 校验生成文件与登记表是否同步，
  不同步则失败。

返回码约定：`0` 成功；`1` 数据错误（信封非法、key 格式非法、重复 key、常量名冲突）或
`--check` 模式下内容不一致；`2` 命令行参数错误（如 `--catalog` 指向不存在的文件）。

## 数据表字段顺序格式化（format_data.py，消费方反馈 E11 根治）

消费方反馈 E11（2026-09-10，见 `docs/消费方反馈/消费方反馈-2026-09-10-编辑器.md` E11）：
示例数据里记录字段的书写顺序与对应 `TableSchema.Fields` 的登记顺序经常不一致，编辑器等工具按
schema 顺序渲染表单/生成 diff 时与仓库里实际的 JSON 字段顺序对不上。`toolchain/format_data.py
--schema-order` 把每条记录的字段按该表 `TableSchema.Fields` 的登记顺序重排（未登记字段保持原
相对顺序、整体追加在已登记字段之后，不丢弃）；字段顺序信息来自
`toolchain/validator --list-tables --json`（`tables_list` 条目新增的 `fields` 数组，同一次
消费方反馈改动，不新增独立子命令，见 `toolchain/validator/Program.cs` `PrintJson` 判断记录）。

```powershell
python toolchain/format_data.py --schema-order                    # 处理 data/_framework + data/_sample，就地重写
python toolchain/format_data.py --schema-order --check            # 只检查，不写文件；有差异时返回码 1（check.ps1 门禁用这一条）
python toolchain/format_data.py --schema-order --data-root data/_framework  # 只处理指定根，可重复传入
```

判断记录：是否需要重排只看"字段相对顺序是否真的变化"（逐行比较重排前后的 key 序列），不是看
整份文件重新序列化后字节是否不同——仓库里现有数据文件的空白/换行风格本来就不统一（部分文件每条
记录压缩成单行，部分文件展开成多行），按字节差异判定会把纯粹的空白规范化混进"字段顺序不对"这条
判断里。真正发生字段顺序变化而被改写的文件，会顺带统一成 2 空格缩进、每个字段各占一行的展开格式
（与 `data/README.md`"编码与格式"约定的 UTF-8 无 BOM、LF 行尾一致）。

返回码约定：`0` 成功（`--check` 模式下代表未发现需要重排的文件）；`1` `--check` 模式下发现有
文件需要重排；`2` 命令行参数错误、或调用 `toolchain/validator` 拿字段顺序失败。

## 资产导入工具（import_assets.py）

`toolchain/import_assets.py`（薄入口，实现在 `toolchain/asset_import/` 包）：把出图产物
（精灵集、图标、特效序列帧、音效、地图分层图）规范化落到 `assets/<dataset>/`，并把对应的
`display.map`/`vfx.def`/`sfx.def`/`world.map` 数据行合并写入 `data/<dataset>/`，对应
[11_工程规范与测试.md](../architecture/11_工程规范与测试.md) 第 2.1 节"资产导入工具"与
[落地方案与分阶段计划.md](../architecture/落地计划/落地方案与分阶段计划.md) 第 15 节。字段定义
以 [04_数据与内容管线.md](../architecture/04_数据与内容管线.md) 第 7.1 节（`display.map`）、
[09_表现层.md](../architecture/09_表现层.md) 第 3.2～3.4 节（方向量化机制、镜像字段结构、纸娃娃层、
锚点、影子）与 [14_资产规格书模板.md](../architecture/14_资产规格书模板.md) 第 2 节（方向档位的
权威命名与 `mirror_of` 关系表、第 1.2 节纸娃娃层资源 id 命名模板）为准；`vfx.def`/`sfx.def`
字段以 `presentation/vfx_sfx/schema/VfxSfxSchemas.cs` 登记的 schema 为准（`vfx.def`：
`id`/`category`/`attach_mode`/`lifetime`（可选）/`resource_ref`；`sfx.def`：
`id`/`layer`/`priority`（可选）/`variants`（可选 Id 列表）/`resource_ref`）。

```
python toolchain/import_assets.py <子命令> ...
```

八个子命令（各自 `--help` 查看完整参数）：

- `sprite`：精灵集源目录 -> 规范化精灵资源 + `display.map` 行。
  输入约定：`<src>/<direction_slot>/<layer>.png`（纸娃娃多层）或 `<src>/<direction_slot>.png`
  （单层）；`<src>` 目录名即 `sprite_set_name`；`<src>/icon.png`（可选）随精灵集一并登记图标。
  输出目录为 `assets/<dataset>/sprites/<category>_<sprite_set_name>/`（`sprite_set_id`
  即 `sprite.<category>.<sprite_set_name>` 去掉首段类别前缀 `sprite.` 后把剩余点号换成
  下划线），与运行时资源 id 解析规则（14 第 1.2 节命名模板、
  `adapters/unity/.../UnityResourceLoader.cs`/`SpriteViewBase.ResolveLayerResourceId`）对齐，
  不是只用 `sprite_set_name` 本身。
  方向档位只需要画"canonical"档位（14 第 2.1 节命名表：8 方向下的
  `front`/`front_side_r`/`side_r`/`back_side_r`/`back` 五个；4 方向为
  `front`/`side_r`/`back`；16 方向的具体命名 14 只给延伸规则，本工具按该规则自行扩展出一套
  具体命名，见 `toolchain/asset_import/directions.py`），其余方向档位（`front_side_l`/`side_l`/
  `back_side_l` 等，命名规则是把来源 canonical 档位名中最后一个独立的 `r` 分段替换为 `l`）在
  `--mirror auto`（默认）下自动用水平镜像回填并记入 `mirror_pairs`；`--mirror none` 时缺失档位
  不回填（打印警告，跳过）。**Id 前缀判断记录**（14 第 2.1 节末尾勘误、
  `presentation/common/contracts/DirectionSlots.cs` `IdPrefix`）：`mirror_pairs` 里
  `direction_slot`/`mirror_of` 两个字段是 `Id` 类型，写入 `display.map` 时会自动加上 `"dir."`
  前缀（如 `"dir.front_side_l"`），裸名字（不含点号）不满足 `Id` 格式；文件名、`--anchors` 参数、
  `anchors.json` 标注文件、本节其余提到的"档位名"一律仍是不带前缀的裸名字，两者按
  `toolchain/asset_import/common.py` 的 `to_direction_slot_id` 一一对应，只在拼进 `mirror_pairs`
  时转换一次，不影响文件系统路径。
  `--anchors` 指向的 JSON 文件格式为 `{"<direction_slot>": {"<anchor_name>": [x_px, y_px], ...}}`
  （像素坐标）；缺失档位用 `--anchor-default name=fx,fy`（画布比例，可重复，默认
  `root=0.5,1.0`）回填并记警告；`--trim` 会按裁剪掉的透明边偏移量平移锚点。写入
  `display.map` 的 `anchor_points` 只取"默认档位"（`front`，即 canonical 列表第一个）的锚点，
  按 `--pixels-per-unit`（未显式传入时按 32 换算，只影响这一步数值计算）换算成世界单位；每个
  方向档位的完整锚点另存一份到精灵集自己的 `anchors.json`（像素坐标 + 各档位画布尺寸），供后续
  更细粒度的挂点系统使用。**显式传入 `--pixels-per-unit`（必须 > 0）时，还会在该 `anchors.json`
  顶层写入同名 `pixels_per_unit` 声明**——这个声明现在有运行期消费方了：引擎适配层解码该精灵集
  下的图像资源时会优先读取它（未声明/非正数/文件缺失/解析失败才回退运行期全局默认值），见
  [ADR-0081](../architecture/adr/0081-精灵集自带像素密度在运行期生效.md)；不传该参数则不写这个
  键（既有精灵集重新导入零 diff，不受影响）。`--scale`/
  `--shadow` 直接写入 `display.map` 对应字段，不改变图像像素；图像的物理缩放不在本工具范围内
  （出图阶段自行控制分辨率）。
- `icon`：一批图标源图 -> 归一化尺寸（等比缩放 + 透明居中垫底，默认 64x64）落到
  `assets/<dataset>/icons/<category>/<name>.png`，打印 `icon.<category>.<name>` 清单（不写数据表，
  `icon_id` 由引用方（如 `sprite`/未来的 `display.equip_visual` 等）自行填写）。
- `vfx`：序列帧目录（按文件名排序的一组 `.png`）-> 图集 `atlas.png` + `frames.json` +
  `vfx.def` 行（`id`/`category`/`attach_mode`/`lifetime`（可选）/`resource_ref`）。
  `--attach-mode` 取值 `world`（按世界坐标播放）/`anchor`（挂接到 sprite 型锚点跟随）/
  `socket`（挂接到 model 型挂点跟随）/`screen`（按屏幕空间坐标播放），默认 `world`，与
  `presentation/vfx_sfx/contracts/VfxAttachMode.cs` 枚举一一对应。
  `frames.json` 结构 `{frame_w, frame_h, fps, frame_duration, loop, frames:
  [{index, x, y, w, h, duration}]}`，与运行时 `ResourceKind.Effect` 加载器
  （`UnityResourceLoader.TryDecodeEffect`）对齐，示例见
  `assets/_placeholder/vfx/burn/frames.json`；不再写旧版 `atlas.json`。`--fps` 决定
  `frame_duration`（`1/fps`）；`--loop` 写 `loop: true`（循环特效，如持续光环），且
  `--loop` 时若省略 `--lifetime` 则该行不写 `lifetime`（循环特效没有固有时长）；非循环时
  仍按 `帧数 / --fps` 推算（除非显式传 `--lifetime`）。
- `sfx`：一批 `.wav`（无压缩 PCM；用标准库 `wave` 读采样率/时长做基本校验，非 wav 或无法解析
  一律报错）作为同一 `sfx.def` id 下的随机变体，扁平复制到
  `assets/<dataset>/sfx/<name>_v<N>.wav`（不是子目录）。行固定写
  `resource_ref: "sfx.<name>_v0"`（第一个源文件）；只有传入 >= 2 个源文件时才写
  `variants`（含 `resource_ref` 本身在内的全部变体 Id 数组），单文件不写 `variants`。
  采样率/时长只用于本命令自身的 PCM 合法性校验并打印到日志，不再进数据表字段。
- `map`：地图分层图源目录（`ground.png`/`overlay.png` 必需，`decal.png`/`nav_hint.png` 可选，见
  14 第 9 节"场景与地图"）-> 规范化落到 `assets/<dataset>/maps/<map>/<layer>.png` + `world.map` 行
  （`id`/`scene_ref`/`nav_ref`/`spawn_points`）。`scene_ref`/`nav_ref` 固定按"类别前缀 + 地图名"
  写成 `scene.<map>`/`nav.<map>`，与 `adapters/unity` 侧 `UnityResourceLoader` 的 `Scene`/
  `NavMesh` 种类解析规则同一套引用 id 命名口径（见该包 README"资源 id → 路径规则"一节）；本工具
  不生成场景/导航资源本身（05 第 4.1 节"导航与碰撞...由引擎适配层侧在场景中手工绘制"）。
  `--spawn x,y[,facing]` 可重复传入覆盖默认出生点（省略时写一条 `<map>.spawn.default`，原点、
  朝向 0）；未识别的分层文件名（非 `ground`/`overlay`/`decal`/`nav_hint`）原样跳过并打印警告。

  **判断记录（消费方反馈第 75 条，ADR-0053）**：地图目录/各层文件的相对路径格式此前只以本节这段
  说明性文字的形式存在，`ref_conventions.py`/框架契约面 `AssetRefConventions` 均未收口——与
  sprite/icon/vfx/sfx 等类别早已收口（ADR-0025/0037/0038）不一致，编辑器等消费方只能照抄这段文字
  自行拼接。现已补齐 `ref_conventions.map_directory`/`map_ground_file`/`map_overlay_file`/
  `map_decal_file`/`map_nav_hint_file` 五个函数与框架契约面对应的五个方法（同一套输入/输出，
  两侧各自独立实现），本子命令改为调用这组函数；本节不再重复给出具体路径格式字符串，格式变化
  以后会体现为这组函数签名/行为的变化，不会再与本节文字各自漂移。跨语言一致性另有
  `toolchain/map_ref_probe`（不对外发行、不进 Core.sln 的最小消费方工程，仅供
  `toolchain/tests/test_ref_conventions.py` 经子进程调用取得真实运行期结果做比对，沿用
  `toolchain/abi_surface` 已确立的同类惯例）。
- `check`：交叉校验 `assets/<dataset>/` 与 `data/<dataset>/display|vfx|sfx|world`——
  `sprite_set_id`/`icon_id` 对应目录/文件、`vfx.def` 的 `resource_ref` 对应 `atlas.png`/
  `frames.json`、`sfx.def` 的 `resource_ref`（必查）与 `variants`（若存在，逐项查且必须包含
  `resource_ref` 本身）各自对应的扁平 `.wav` 文件是否存在、每个精灵集
  同一层跨方向档位尺寸是否一致、锚点是否落在对应方向档位画布范围内、实际落地的方向档位数是否与
  `direction_count` 一致、**`mirror_pairs` 完整性**（每个已落地但不属于 canonical 档位集合的方向
  档位必须有对应的镜像来源声明，声明的来源必须已落地）、**声明锚点缺失**（`display.map.
  anchor_points` 声明的每个锚点必须能在精灵集自己的 `anchors.json` 默认档位标注中找到，对应
  14 第 11 节"缺锚点"校验项）、`world.map` 引用的地图分层图（`map` 子命令产出）是否存在。
  `--only sprite,vfx,sfx,world,display_anim`（逗号分隔，省略则跑 `DEFAULT_DOMAINS`
  = `sprite,vfx,sfx,world,display_anim` 全部五项，见下方"新增 `display_anim` 域"）只跑选定的
  检查域，供门禁在某个数据集只有部分域已接入真实资产时缩小本次运行覆盖范围（不放宽已选中域自身的
  判断逻辑）。只打印问题清单，不写任何文件；返回码 `0`（无问题）/`1`（有问题），加不加 `--json`
  语义相同。

  **`display_anim` 域（ADR-0038 落地，已纳入默认集合）**：核对 `display.anim_set.clips.
  resource_ref`、`display.weapon_style.auto_attack_anim`/`cast_anim_override`、`display.
  equip_visual.mesh_ref` 四个字段——经 `ref_conventions.resolve_path_space` 先判断路径空间，
  类别前缀不合法报 `display_anim_ref_category_invalid`；`anim`/`model` 前缀（引擎侧逻辑路径）
  跳过存在性检查（同 ADR-0037 决策 3 理由）；`sprite_anim` 前缀检查 `atlas.png`/`frames.json`
  是否存在；`paperdoll` 前缀专属 `display.equip_visual.mesh_ref`（ADR-0071 决策 1：sprite 型
  语义变更为"装备层资源集引用"，与身体纸娃娃层同一套方向档位换算解析），改核对
  `EQUIP_LAYER_CHECK_DIRECTIONS`（`front`/`side_r`/`back`）三个方向档位各自的层文件是否存在
  （`sprites/<mesh_ref 去掉类别前缀>/<方向>/<slot_id 推导出的层名>.png`，报
  `display_anim_equip_layer_file_missing`），不再是此前的单个扁平文件；其余遗留前缀按目录/文件
  最小核对，报 `display_anim_asset_missing`。**纳入默认集合的时间点**：ADR-0038 落地初期本域曾因样例数据集
  `display.equip_visual.sample_hero_hat` 一行的 `mesh_ref` 仍是迁移前的 `sprite.item.
  sample_hero_hat` 遗留取值而暂不进默认集合（纳入会让该行报错、拖垮门禁默认跑法）；数据迁移任务
  已把该行与另外三个受影响字段的全部样例数据行迁移到正确的类别前缀并补齐占位资产，本域随即纳入
  `DEFAULT_DOMAINS`，不再需要显式 `--only display_anim` 才能核对。

  **`equip` 域（手感设计/08，ADR-0123，显式 opt-in）**：`--only equip` 才跑，不进 `DEFAULT_DOMAINS`/`ALL_DOMAINS`
  （默认集合是"每个数据集都有这些表"的域；装备域依赖框架占位装备集 `data/_equip` 与 `assets/_placeholder`）。
  数据根取 `data/_framework` + `data/_feel` + `data/<--dataset>`，资产取 `assets/_placeholder`；实现与下面的 `equip`
  子命令共用 `equip_cmd.run_equip`。与其余域不同，装备域有警告级问题（回落记录，不阻断），所以 `check` 的返回码
  现在按"有错误级问题才为 1"判定（既有域的问题全是错误级，行为不变）。
- `skin-checklist`：按界面资源契约清单（`skin_manifest.json`）与数据展开的槽位/品质输出人读的逐文件清单（Markdown，`--out` 写文件，`--json` 只输出元素数统计），交给出图与美术；出图工具与导入校验读同一份清单。
- `equip`：装备资产包 + 界面皮肤包校验，输出**装备完整性报告**（`toolchain/asset_import/equip_pack.py`、
  `skin_pack.py`，规则与检查名清单见两个文件的 docstring 与
  [手感设计/08](../architecture/手感设计/08_装备与UI资产契约.md) 第 5 节；皮肤包、图标、纸娃娃图层的全部规则读同一份机器可读契约清单
  `toolchain/asset_import/skin_manifest.json`，每条规则一个具名诊断，ADR-0149）。默认读 `data/_framework`、`data/_feel`、
  `data/_equip` 与 `assets/_placeholder`（`--data-root`/`--assets-dir`/`--anim-set`/`--direction-count`/
  `--skin-ref` 可覆盖）。错误级：图标缺失/尺寸不合规/贴边、装备缺 `display.equip_visual`、纸娃娃静态层图或必备姿势键
  逐层剪辑（ADR-0100 两级探测）缺失、`model` 型槽位/挂点命名不在 model 型 `display.map.slots/sockets` 里、武器缺
  `display.weapon_style` 或 `feel.weapon`（同 id 分表）、`preview_direction` 非声明方向档；警告级（报告写明回落目标）：
  推荐键层剪辑缺失（回落静态层图）、覆盖剪辑无逐层剪辑、`feel.weapon.family` 在姿势集里无键、`sfx_material` 在
  `sfx.def` 无 swing/impact 行（回落 `generic`）、`equip_sfx_ref`/`trail_ref` 悬空、皮肤包缺项（回落 `skin.default`）。
  第二轮（ADR-0152）新增：错误 `equip_behind_direction_invalid`（`behind_directions` 某项不是声明的方向档）、`equip_anchor_invalid`/
  `equip_anchor_out_of_bounds`（精灵集 `anchors.json` 的 `grip` 格式不对/落在层图画布之外）；警告 `equip_opaque_coverage_low`（图标或静态层
  不透明像素占比低于清单阈值，几乎看不见；阈值写在 `skin_manifest.json` 的 `icons.coverage`/`paperdoll.static_layer_coverage`）。
  ADR-0155 新增警告 `equip_layer_density_mismatch`（层精灵集带 `anchors.json` 时，其顶层 `pixels_per_unit` 与身体精灵集——`ui_layout_definition` 行 `preview_body_set`——不一致或没声明：运行期合成里该装备相对身体画得偏大或偏小，预览区看不出来）。
  错误级物品不能进入 validated 数据集：判定接口 `EquipReport.is_validated(item_id)` / `filter_validated(tables, report)`，
  命令行 `--blocked-out <文件>` 写出被阻断物品 id。报告写 `--report-dir`（默认 `bin/_check_artifacts/equip_report/`，
  已在 `.gitignore`，只留本地）的 `equip_completeness.json`/`.txt`；返回码 `0` 零错误（`--strict-warnings` 时还要零警告）
  / `1`。门禁步骤 `equip_pack_check`（占位装备集须零错误零警告）与 `validate_equip_data`。占位装备集与占位皮肤包
  的生成器见 [`std_equip_set/README.md`](std_equip_set/README.md)。
  **`check` 的姿势与剪辑检查（手感落地 M5-S4，ADR-0147，`toolchain/asset_import/clip_checks.py`，与核心侧规则同口径）**：
  `display.anim_set` 域新增 `anim_set_clip_markers_missing`（攻击类缺 `active_start/active_end/hit`、走/跑/冲刺缺 `footstep`、
  闪避类缺 `invuln_start/invuln_end`，错误；**只对按清单发布的姿势集**：声明 `pose_standard: true` 或框架级 `display.anim_set.std_*`，
  既有自由姿势集不受影响）、`anim_set_clip_duration_mismatch`（声明的 `duration_ms` 与 `sprite_anim/<名>/frames.json` 量出的总时长不一致，错误；
  没声明时以量出值为准，骨骼剪辑没有磁盘资源可量，只检查声明值）；另有一道与技能时间线的交叉检查（`timeline_clip_missing`、
  `timeline_clip_mismatch`、`timeline_clip_deviation`）：技能到剪辑的对应来自武器表现数据（`display.weapon_style.cast_anim_override`
  与普攻映射，见手感设计/04 第 8 节），`source: clip` 的技能任一偏差超过 0.5 毫秒抄写取整误差为错误、取不到对应剪辑为错误，
  `source: data` 偏差超过标定表 `marker_tolerance_ms`（取不到按 50）为警告、取不到静默跳过。
- `bake-motion`：动画剪辑的根位移采样 -> `skill.motion_curve` 行（手感设计/02 第 4 节，取代已删除的根运动驱动）。输入一个 JSON
  `{"clip": "anim.<剪辑资源引用>", "samples": [{"t_ms": 0, "distance": 0.0}, ...]}`（`distance` 是该时刻起算的累计前进距离，可由引擎侧
  导出脚本从根骨轨迹求得，不要求等间隔采样），时间归一到 [0,1]、距离按总位移归一到 [0,1] 并取累计最大值（曲线只增不减），断点数超过
  `--max-points`（缺省 32）时等间隔重采样（端点保留）；行里另记 `source_clip`/`source_distance` 仅供溯源。技能时间线的动作位移用
  `curve: custom:<行 id>` 引用，`distance` 取总位移（身高倍数）。`--id` 必填，`--dataset`（缺省 `_sample`）、`--data-root`、`--dry-run` 同其它子命令。

  **`--json`（消费方反馈第 62 条）**：stdout 只输出一个 JSON 文档（UTF-8、中文不转义），其余日志
  （逐条问题的人类可读文本、末尾汇总行）改走 stderr；不加 `--json` 时文本输出（含走 stdout 而非
  stderr）与不加本参数前逐字节一致。顶层结构：`{tool, dataset, domains: [...], ok: bool,
  counts: {error, warning}, issues: [...]}`；每条 `issues[]` 元素：`severity`（`error`/
  `warning`）、`table`（登记表名，如 `display.map`/`vfx.def`/`sfx.def`/`world.map`）、
  `record_key`（问题所在行的 `id`）、`field_path`（问题定位到的字段路径，支持数组下标如
  `"variants[0]"`，无法定位到具体字段时为 `null`——语义同 `toolchain/validator` 的
  `ValidationIssue.Field`，但键名不同：本工具是独立新契约，直接用更明确的 `field_path`，不强求
  与 validator 的历史键名 `field` 一致）、`check`（稳定 snake_case 检查名，见下）、`message`
  （与文本模式同一条消息，逐字相同）、`path`（本工具专属：期望存在但实际缺失的资源相对路径，
  格式/一致性类问题无具体路径时为 `null`，validator 不检查资源文件存在性，没有对应字段）。

  稳定检查名清单（`toolchain/asset_import/check_cmd.py` 的 `CHECK_NAMES` 常量，供 `--json`
  消费方按 `check` 字段过滤/分类；命名风格参照 `toolchain/validator` 既有检查名，如
  `world_map_point_outside_image`；本工具当前诊断与 validator 既有规则均无语义重合，未见可复用
  同名的既有检查）：`sprite_set_id_missing`、`sprite_set_id_format_invalid`、
  `sprite_set_dir_missing`、`sprite_atlas_json_missing`、`sprite_anchors_json_missing`、
  `sprite_atlas_png_missing`、`sprite_direction_count_mismatch`、
  `sprite_layer_size_inconsistent`、`sprite_frame_file_missing`、
  `sprite_mirror_pair_source_not_landed`、`sprite_mirror_pair_missing`、
  `sprite_anchor_out_of_canvas`、`sprite_declared_anchor_missing`、
  `sprite_pixels_per_unit_invalid`（ADR-0081 新增：顶层 `pixels_per_unit` 出现时必须是正数，不
  出现不报错）、`icon_id_format_invalid`、
  `icon_file_missing`、`vfx_resource_ref_missing`、`vfx_atlas_missing`、
  `vfx_frames_json_missing`、`sfx_resource_ref_missing`、`sfx_resource_file_missing`、
  `sfx_variants_missing_resource_ref`、`sfx_variant_file_missing`、`world_map_dir_missing`、
  `world_map_layer_missing`、`world_map_scene_ref_mismatch`、`world_map_nav_ref_mismatch`、
  `display_anim_ref_category_invalid`、`display_anim_sprite_anim_atlas_missing`、
  `display_anim_sprite_anim_frames_json_missing`、`display_anim_paperdoll_file_missing`
  （该检查名保留，现无实际调用方——sprite 型 `mesh_ref` 已改用下一条，见 ADR-0071）、
  `display_anim_equip_layer_file_missing`（ADR-0071 决策 1 新增）、`display_anim_asset_missing`
  （ADR-0038 落地新增，属 `display_anim` 域，见上方该域说明）。

全部子命令支持 `--dataset`（默认 `_sample`）、`--assets-root`/`--data-root`
（默认仓库 `assets/`/`data/`，可指向任意目录，测试与临时数据集用此覆盖）、`--dry-run`
（`check` 本身不写文件，无需此参数）。

**判断记录（消费方反馈第 76 条，[ADR-0054](../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)，
续 ADR-0053 同一病灶）**：`--assets-root`/`--data-root` 省略时的默认值（`<仓库根>/assets`/
`<仓库根>/data`）此前只是 `common.py` `resolve_root` 一个内部函数的实现细节，框架契约面
`AssetRefConventions` 只给出资源引用 id 之下的相对路径，从未表达"资产根目录本身在哪"这一段；
`assets/<dataset>/` 前缀由调用方自行拼接（消费方反馈第 75 条答复文档"dataset 段由谁拼接"一节）
同样从未收口。现已补齐 `ref_conventions.resolve_assets_root`/`resolve_data_root`/
`dataset_assets_directory`/`dataset_data_directory` 四个函数与框架契约面
`AssetRootConventions` 对应的四个方法（同一套输入/输出，两侧各自独立实现），`common.py` 的
`resolve_root` 改为对 `"assets"`/`"data"` 两个真实取值委托它们；跨语言一致性另有
`toolchain/asset_root_probe`（不对外发行、不进 Core.sln 的最小消费方工程，与
`toolchain/map_ref_probe` 同一惯例，仅供 `toolchain/tests/test_ref_conventions.py` 经子进程
调用取得真实运行期结果做比对）。全仓排查另发现 `toolchain/import_sample_assets.py` 独立维护
的第二处重复实现（且相对路径覆盖值的解析基准与六个子命令有细微差异：按当前工作目录而非仓库根
解析），已改为调用共享函数收敛为统一行为。

`vfx`/`sprite_anim` 目录下固定含的 `atlas.png`/`frames.json`、`sprite_set` 目录下固定含的
`atlas.png`，此前只写在各子命令模块文档字符串与 `AssetRefConventions` 方法 XML 注释的说明性
文字里，从未提升为可编译期引用的方法；`frames.json` 的结构（`frame_w`/`frame_h`/`fps`/
`frame_duration`/`loop`/`frames:[{index,x,y,w,h,duration}]`）也只有唯一一处解码实现——
`UnityResourceLoader.TryDecodeEffect`（引擎适配层内部私有方法，不对外公开）。现已补齐
`AssetRefConventions.VfxAtlasFile`/`VfxFramesFile`/`SpriteAnimAtlasFile`/
`SpriteAnimFramesFile`/`SpriteSetAtlasFile` 五个方法与 `ref_conventions.py` 对应的五个函数；
新增 `Core.Foundation.EngineAdapter.EffectFramesDocument` 收口 `frames.json` 结构化解析，
`TryDecodeEffect` 改为调用它、只做本地环境特有的收尾（按裁剪矩形从已解码图集切出 Sprite），
不再自行解析。判断记录（不新增 `sprite_set` 类的 `frames.json` 对应方法）：`sprite_set` 目录内
与 `atlas.png` 配套的索引文件是 `atlas.json`（按方向档位/层名分帧，结构与 `frames.json` 不同，
见上方 `sprite` 子命令产出说明），且只被本文件 `check` 子命令自身的存在性校验消费，不在运行时
资源加载路径上，本次不借机扩大收口范围。

**判断记录（消费方反馈第 79 条，ADR-0054"决策 1"纳入同一契约范围）**：`DatasetDataDirectory`
只有正向（数据集名 → 目录），逆向（磁盘上已探测到的数据目录 → 数据集名）没有公开出处，消费方
（内容编辑器）从"游戏仓库根 + 已探测数据目录"定位资产文件时，中间这一步只能自己切路径末段。
现已在 `ref_conventions.py` 补齐 `try_get_dataset_name(data_root, dataset_data_directory)`，与
框架契约面 `AssetRootConventions.TryGetDatasetName` 逐条对应（同一套输入/输出，两侧各自独立
实现，不互相调用），跨语言一致性经 `toolchain/asset_root_probe` 扩充第 5 个可选参数
`inverseProbeDirectory` 对照。边界口径（逐条对应消费方反馈原文"路径比较口径请框架定"）：

- 给定目录不是数据根的**直接**子目录（更深层级、不在数据根之下、或就是数据根本身）：返回
  `None`（不抛异常，与本模块既有 `try_parse_sprite_set_id`/`try_parse_icon_id` 同一惯例——
  "解析失败"是可预期的业务结果，不是异常情形）。
- 路径分隔符 `/`/`\`、结尾多余分隔符：比较前归一化（`/` 换成 `os.sep`、去掉结尾多余分隔符）；
  不经过 `pathlib.PurePath` 做父目录/末段拆分——`PurePath` 会静默折叠单独的 `.` 段，而 C# 侧
  `Path.GetDirectoryName`/`Path.GetFileName` 不会，为保证两侧对同一输入算出逐字节相同的结果，
  改为对字符串做与 C# 侧逐字对应的纯前缀/后缀截取。
- `.`/`..` 路径段：不做任何语义解析，按字面字符比较——`dataset_data_directory` 正向本身也只是
  字符串/路径拼接，不校验、不规整这类段，逆运算不借机新增正向没有的语义；真实来源（对目录做
  遍历取得的已存在路径）不会产生这类段，不影响验收口径覆盖的实际场景。
- 相对路径 vs 绝对路径：不做绝对化处理（不调用 `Path.resolve()`，会依赖当前工作目录，与"运行时
  路径不依赖运行环境状态"冲突），只按字面字符串比较，调用方须保证两个参数处于同一相对/绝对表示
  下（典型来源都经同一次 `resolve_data_root` 调用得到，天然满足）。
- 大小写：Windows 文件系统大小写不敏感，父目录段按 `str.lower()` 比较（不用 `casefold`/文化相关
  比较，保持跨环境确定性）；还原出的数据集名取自 `dataset_data_directory` 最后一段的**原样
  字符**，不做任何大小写变换——数据集名是内容标识，大小写以调用方实际观测到的磁盘目录名为准。
- 假定合法数据集名本身不含路径分隔符（与目录名惯例一致，`dataset_data_directory` 对此也未做
  任何校验）；数据集名含分隔符属于对该函数的非法用法，逆运算行为不在契约范围内。

图像处理只用 Pillow：`--matting none|rembg|colorkey:#RRGGBB`
——`colorkey` 抠图是本工具自写的容差比色（`--colorkey-tolerance`，默认 32），`rembg` 仅在本机
`~/.u2net/` 下已有权重文件时可用（本工具不会自动联网下载模型权重，未准备好权重直接报错并提示
改用 `none`/`colorkey`）。合并写入 `display.map.json`/`vfx.def.json`/`sfx.def.json`/
`world.map.json` 时：已存在同 `id` 的行整体替换，否则新增，其余行原样保留，整份文件按 `id`
重新排序后整体写出（2 空格缩进、LF、UTF-8 无 BOM，同 `data/README.md` 约定）。

**TOOL-02 收口（第四方深度审核）：id → 物理文件名/资源引用片段的编码改为"点号替换成双下划线"，
不再是"点号替换成单下划线"**——`sfx`/`vfx` 子命令去掉 domain 前缀后，原实现直接
`.replace(".", "_")`，会让"点分段"与"本就带下划线"的不同合法 id 归一到同一个物理文件名：例如
`sfx.fire.hit`（去前缀后 `fire.hit`）与合法的 `sfx.fire_hit`（去前缀后 `fire_hit`）都会得到
`fire_hit`，第二次导入会静默覆盖第一次写出的音频/图集文件，且两条 `sfx.def`/`vfx.def` 记录
最终指向同一份物理资源。现在改为点号替换成双下划线、单个下划线原样保留（`toolchain/
asset_import/common.py` 的 `flatten_id_segment`），与表现层已在用的"结构分隔用双下划线"口径
一致（14 第 1.2 节纸娃娃扁平文件名模板 `<...>__<direction_slot>__<layer_id>`）；写入
`sfx.def`/`vfx.def` 前另加运行期兜底 `check_no_resource_collision`——不同 id 的记录若仍共用
同一条物理 `resource_ref`（含 `variants`），直接报错而不是静默覆盖（同一 id 的重复导入/更新
不算碰撞）。见 `toolchain/tests/test_import_assets.py` 新增碰撞用例。

写完 `sprite`/`vfx`/`sfx`/`map` 后应跑 `python toolchain/import_assets.py check --dataset <name>`
确认资产与数据表互相对得上，再跑 `python toolchain/validate_data.py --dataset <name>` 走完整的
表级校验（04 第 5 节"外形映射存在"等检查项）。

单元测试：`python -m unittest toolchain.tests.test_import_assets -v`（标准库 `unittest`；测试数据
写在系统临时目录，不接触仓库内 `assets/`/`data/`），也可用 `python -m pytest toolchain/tests -q`。

## 框架级假人姿势集生成器（`gen_std_dummy_poses.py`，手感设计/04 第 6.2 节）

程序绘制的几何人偶姿势集（sprite 型）：序列帧 + `display.anim_set.std_dummy_biped` 数据行 + 规格文件，
自带只读自检（`--check`）与每键一格缩略拼图（`--sheet`，只留本地）。用法、参数、键清单、帧数规则、判断记录与
自检 `--check`（需要图像库，缺库时按 `-Quick` 档跳过）登记在门禁步骤 `std_dummy_poses`，与 model 型步骤并列。键清单现为 103 键（含全部可选键与八个武器族），每个攻击剪辑带 `hit_frame` 事件。范围与边界（设计决定）见 [`std_dummy_poses/README.md`](std_dummy_poses/README.md)；测试见 `tests/test_std_dummy_poses.py`。

## 框架级假人姿势集生成器 model 型（`gen_std_dummy_model_clips.py`，手感设计/04 第 6.2、10 节）

同一姿势集的骨骼剪辑版：与 sprite 版共用键清单、事件与姿势函数（引用不复制），产出规格
`assets/_placeholder/std_dummy_model_clips.json` 与 `display.anim_set.std_dummy_biped_model` 数据行；引擎侧
预制体/控制器/剪辑资产由 `adapters/unity/Assets/Editor/GenerateStdDummyModelAssets.cs` 按规格生成。自检
（`--check`，含引擎资产 YAML 核对与控制器确定性 fileID 核对）登记在门禁步骤 `std_dummy_model_clips`；另含轻/重体量组（`extends` 主集）。用法、判断记录与范围与边界（设计决定）见
[`std_dummy_model_clips/README.md`](std_dummy_model_clips/README.md)；测试见 `tests/test_std_dummy_model_clips.py`。

## 标准骨骼蒙皮预渲染（`run_prerender_skin.py`，手感设计/04 第 6.2 节、ADR-0140）

序列帧型游戏做一套蒙皮（骨名符合标准骨骼的模型，可带装备层），得到全部姿势键 × 全部方向档 × 体量组的序列帧与
`display.anim_set.<名>` 数据行。离线工具链能力，运行期契约不变，产物与假人姿势集同形；帧数、帧时长、事件时刻与假人姿势集取自同一套函数。
驱动器（`prerender_skin/`，Python）写作业、调引擎批处理渲染、组装图集与数据行、自检（键与帧数、非空、枢轴不漂移、层对齐、
画布裁切）、再过 `validate_data --strict` 与 `import_assets check`。缺骨骼、未知方向档、找不到装备层对象一律拒绝并列出清单。
同机同输入两次渲染逐字节一致；跨机器只承诺结构与像素容差。用法、渲染配置字段、蒙皮输入约定与判断记录见
[`prerender_skin/README.md`](prerender_skin/README.md)；测试见 `tests/test_prerender_skin.py` 与引擎侧 `SkinPrerenderPlayModeTests`。

## 框架级占位装备集生成器（`gen_std_equip_set.py`，手感设计/06 第 2 节、08）

单手剑/双手巨剑/匕首/弓/法杖/胸甲各一个完整装备资产包（图标 + 纸娃娃静态层图 + 逐层剪辑 + 外观/武器表现/手感/音效材质数据行
`data/_equip`）与框架占位皮肤包 `skin.default`（`assets/_placeholder/ui/skin/default/`），全部程序绘制，是 `import_assets.py equip`
的参照实现。用法、布局与判断记录见 [`std_equip_set/README.md`](std_equip_set/README.md)；测试见 `tests/test_equip_pack.py`。

## Markdown 相对链接校验（`test_markdown_relative_links.py`）

`toolchain/tests/test_markdown_relative_links.py` 扫描所有由 `git ls-files` 跟踪的 `*.md`
文件（含 `audit-*/` 各轮审计归档，2026-09-09 起不再整段排除审计目录，
理由与判断记录见该测试文件顶部 docstring），校验其中的相对文件链接确实指向存在的文件；同时识别
两类合法的"非字面路径"写法后再判定：`path.cs:123`/`path.cs:123-145` 这种源码行号引用记法（剥掉
行号后缀再判存在性），以及仍落在 `.gitignore` 覆盖范围内的一次性构建产物/日志（按设计不入库，
不算文档缺陷）。

个别审计归档引用的证据原件确认已经找不回（例如生成该轮审计的 codex 产出目录已被清理），且对应
Markdown 正文已经在链接旁标注"原件未归档"及原因时，把该链接登记进
`toolchain/tests/.linkcheck-ignore`（每行 `<md 文件仓库相对路径>::<链接原始 target 文本>`，
`#` 开头或空行忽略）显式豁免，不要用来掩盖真正的路径层级错误。随 `python -m pytest
toolchain/tests -q` 一并跑。

## `data/_sample` 的资产来源（`import_sample_assets.py`）

`data/_sample/display/display.map.json`/`vfx/vfx.def.json`/`sfx/sfx.def.json`/`world/world.map.json`
是框架仓库自身灰盒竖切/PlayMode 测试用的示例数据（见该目录顶层 `README.md`"两类目录"一节）。
这四张表引用的 `assets/_sample/` 资产由驱动脚本 `toolchain/import_sample_assets.py` 统一生成：
它把 `assets/_placeholder/` 的占位素材（外加一张 Pillow 现生成的存档点占位图）依次喂给
`import_assets.py` 的 `sprite`/`icon`/`vfx`/`sfx`/`map` 真实子命令，再把子命令产出的引用字段
（`sprite_set_id`/`icon_id`/`mirror_pairs`/`resource_ref`/`variants`/`lifetime`/`priority`）回写进
`data/_sample` 既有行——四张表每一行的 `id`/`logical_id`/`category` 全程不变。

来源 -> 产物对应关系（完整表见该脚本文件头 docstring）：

| `data/_sample` 行 | 占位素材来源 | 产物 |
| --- | --- | --- |
| `display.map.sample_hero`/`sample_beast` | `assets/_placeholder/sprites/placeholder_{hero,beast}/` 5 个 canonical 方向档位 | `assets/_sample/sprites/creature_sample_{hero,beast}/` |
| `display.map.sample_blade`/`sample_bolt`（共用精灵集） | `assets/_placeholder/icons/icon_placeholder_blade.png` | `assets/_sample/sprites/item_sample_blade/` |
| `display.map.sample_chest`/`sample_loot_pile`（共用精灵集） | `assets/_placeholder/sprites/placeholder_chest/closed.png` | `assets/_sample/sprites/gobj_sample_chest/` |
| `display.map.sample_door` | `assets/_placeholder/sprites/placeholder_door/closed.png` | `assets/_sample/sprites/gobj_sample_door/` |
| `display.map.sample_save_point` | 脚本用 Pillow 确定性生成的占位立柱图 | `assets/_sample/sprites/gobj_sample_save_point/` |
| `vfx.def.sample_cast_circle`/`sample_hit_spark`/`sample_burn` | `assets/_placeholder/vfx/{cast_circle,hit_spark,burn}/frame_*.png` | `assets/_sample/vfx/sample_*/{atlas.png,frames.json}` |
| `sfx.def.sample_hit`/`sample_cast`/`sample_ui_click` | `assets/_placeholder/sfx/*.wav` | `assets/_sample/sfx/sample_*_v<N>.wav` |
| `world.map.sample_field` | `assets/_placeholder/maps/placeholder_field/` | `assets/_sample/maps/sample_field/` |

精灵集目录名规则 `<category>_<sprite_set_name>`（如 `creature_sample_hero`）与
`sprite` 子命令本身的落地规则、运行时 `UnityResourceLoader`/`SpriteViewBase.
ResolveLayerResourceId` 的资源 id 解析规则、[14_资产规格书模板.md](../architecture/14_资产规格书模板.md)
第 1.2 节命名模板三方一致（见上面 `sprite` 子命令说明）。

`build.ps1` 第 4 节"内容数据集同步"把 `assets/_placeholder/{sprites,sfx,vfx}` 与
`assets/_sample/{sprites,sfx,vfx}` 同时同步进 Unity 工作台的
`Assets/StreamingAssets/GameFoundation/{sprites,audio,vfx}`（`Sync-ContentTree` 现支持多个
源目录，后列源目录同名文件覆盖前者）。

重新生成命令（幂等，改了 `assets/_placeholder/` 下的源素材或需要修复 `data/_sample` 引用时重跑
即可）：

```
python toolchain/import_sample_assets.py
```

脚本末尾会自动跑一次 `python toolchain/import_assets.py check --dataset _sample`（全量四域），
非 0 直接失败退出，不会把校验不过的产物留在仓库里。单元测试：
`python -m unittest toolchain.tests.test_import_sample_assets -v`（测试数据写在系统临时目录，
不接触仓库内 `assets/`/`data/`）。

## `get_framework.ps1`（游戏侧按版本号引用本框架）

与本目录其余脚本不同，`get_framework.ps1` 运行在**游戏仓库**那一侧（游戏仓库把它复制/引用过去
用），不属于本框架仓库自身的构建/门禁链路——它是框架随每次发布产物一并提供、供游戏侧调用的工具，
职责是"按版本号取得一份可信的框架发布产物"。

**依赖边界（消费方反馈 E2 根治，2026-09-10）**：`get_framework.ps1` 现已自包含——DLL 哈希校验
函数 `Get-Sha256FileHash` 原样内联在本文件内，不再 dot-source 同目录 `toolchain/_hash.ps1`，
只下载/复制这一个文件即可在游戏仓库使用（GitHub Release 附件里单独提供，见下方"下载方式"）。
`toolchain/_hash.ps1` 本身继续保留在本目录，供仓库内其它脚本（`build.ps1`、
`sync_package_content.ps1`）共用，也继续作为 Release 附件一并提供，兼容消费方现有"下载
`get_framework.ps1` + `_hash.ps1` 两个文件"的还原脚本（本脚本自身不再读取它）。两处
`Get-Sha256FileHash` 函数体逐字节一致，`toolchain/tests/test_get_framework_hash_inline_consistency.py`
静态比对回归。

**下载方式**：三种等价途径都能拿到自包含的 `get_framework.ps1`——1) GitHub Release 附件里单独
下载 `get_framework.ps1`（每次发布固定附带，见框架仓库根 `README.md`"版本与发布"一节）；
2) 随 `ws-game-<ver>.zip` 解压得到 `toolchain/get_framework.ps1`；3) 把 `toolchain` 整个目录
复制/引用过去。三种方式下脚本行为完全一致。

```powershell
powershell -File toolchain\get_framework.ps1 -Version 1.0.0 -Target packages
powershell -File toolchain\get_framework.ps1 -Version 1.0.0 -Target packages -FromLocalDist path\to\ws-game-1.0.0.zip
powershell -File toolchain\get_framework.ps1 -Version 1.0.0 -Target packages -WithSamples
```

在线路径依赖 `gh`（GitHub CLI）已登录，按 `v<Version>` 标签从框架仓库的 Release 下载
`ws-game-<Version>.zip` 与配套的 `.lock` 锁文件；离线路径 `-FromLocalDist` 直接指定本机已有的
zip（同目录需要有同名 `.lock` 文件）。两条路径都会：解压到临时目录 → 按锁文件记录的 sha256 校验
六个核心 DLL（`Core.Foundation`/`Core.Numbers`/`Core.Rules`/`Core.Carriers`/`Core.Gameplay`/
`Presentation.Common`，锁文件存在 `headless_dlls`/`validator_dlls` 字段时一并校验无头适配层
DLL/预编译 validator 的 DLL，见 ADR-0018 决策 3、消费方反馈 E1）→ 校验通过才落地到
`<Target>/ws-game-<Version>/`（旧目录整体删除重建）→ 在当前目录写入/校验 `ws-game.lock`
（游戏仓库根，与下载到的锁文件内容等价，判定规则见下方"消费方反馈 E3"）。校验失败（哈希不
一致、DLL 缺失）时不会改动 `-Target` 与 `ws-game.lock`，保持"校验通过才落地"。

**`-WithSamples`（消费方反馈 E4 根治，2026-09-10）**：主 zip 不含 `data/_sample`/`assets/_sample`
（那是框架自测用的验收数据集，不代表真实游戏内容，见 `data/README.md`），新工程想要一份现成的、
已知合法的样例数据/资源验证框架端到端可用时，传 `-WithSamples` 额外下载/校验
`ws-game-<ver>-samples.zip`（按锁文件 `samples.sha256` 字段校验，`build.ps1 -Dist`/`-Release`
同一次打包新增产出），合并落地到与主 zip 相同的 `<Target>/ws-game-<Version>/` 目录下。锁文件
缺 `samples` 字段（早于本次功能落地的旧版本）时传 `-WithSamples` 会明确报错退出，不静默忽略。

判断记录（消费方反馈第 67 条，2026-09-19，补充说明）：`-WithSamples` **只对上面这条 zip 快照
通道有意义**——只用下一节"私服"（`-FromRegistry`）通道消费框架的游戏，传 `-WithSamples` 会被
直接忽略（见本文件与脚本自身 `.PARAMETER WithSamples` 说明"仅对 zip 通道有意义，`-FromRegistry`
时忽略"），没有等价的私服侧样例数据获取方式。私服通道下想要验收样例数据集，需要额外单独跑一次
zip 通道的 `-WithSamples`（哪怕平时按包依赖方式消费框架的其余内容）；消费方入口说明见
`games/_template/README.md`"获取验收样例数据集"一节。

**锁文件 `source` 字段与本机路径（消费方反馈 E3 根治，2026-09-10）**：`ws-game.lock` 约定提交进
游戏仓库；`-FromLocalDist` 场景的 `source` 字段不再写调用方本机的绝对路径（只写 `channel` 与
相对的 `zip_file_name`），换机器重新运行不会因为本机路径不同产生一次与框架引用内容无关的锁文件
diff。判定"锁文件是否需要改写"时也不再整份 JSON 字符串比较，改为只比较 `version`/`git_commit`/
`dlls`/`headless_dlls`/`validator_dlls`/`samples` 这几个描述引用内容本身的字段，忽略 `source`
差异；带旧格式 `source.local_path` 的既有锁文件仍能被正常接受。回归测试见
`toolchain/tests/test_get_framework_lock_source_no_local_path.py`。

版本号格式、锁文件字段、`build.ps1 -Release`/`-Zip` 如何产出这两个文件，见框架仓库根
`README.md`"版本与发布"一节与 `architecture/落地计划/落地方案与分阶段计划.md` 第 3.5 节。

**路径边界判断记录（PJ150-01 根治，2026-09-08，审计
`audit-3224ca1-20260908/AUDIT_REPORT.md` PJ150-01）**：锁文件的 `version`
字段此前只在与 `-Version` 相等时才被信任；`-AllowVersionMismatch` 放行版本不一致后，脚本会把该
字段原样拼进落地目录路径并对其执行 `Remove-Item -Recurse -Force`——六个 DLL 的哈希校验不覆盖这个
元数据字段，把 `version` 构造成形如 `x/../../outside_sentinel` 的值即可让落地路径规范化后逃出
`-Target`，脚本仍以 exit 0 收尾并删除、覆盖那里已有的内容。根治两层，均已补 pytest 回归
（`toolchain/tests/test_get_framework_path_boundary.py`，随 `pytest toolchain/tests` 一并跑）：
1. 锁文件 `version` 字段与 `-Version` 参数同样严格校验语义化版本格式（`^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$`），
   不允许出现路径分隔符/`..`/空白等字符；2. 无论格式校验是否通过，落地目录规范化后必须仍是
   `-Target` 的严格子目录，任何写入/删除前完成校验（脚本内 `Test-IsStrictSubPath` 函数）。同一轮
   顺带把解压方式从无差别的 `Expand-Archive` 改为逐条目手动解压 + 边界校验，堵住 zip 内条目路径
   本身携带 `../`（zip slip）的越界口子——即便六个 DLL 的哈希都对得上，zip 里混进的其它恶意条目
   仍会在解压前被拒绝。`-Target`/`-LockPath` 两个参数本身由调用方（游戏仓库一侧的可信调用者）
   传入，不是本次攻击面的输入源，本轮未额外校验其格式；真正需要校验的是随 zip/lock 一起搬运、
   可能被篡改的数据字段（锁文件 `version`、zip 条目路径），落地路径的边界校验以调用方传入的
   `-Target` 本身作为信任根。

上面两条是 zip 快照通道；`-FromRegistry` 是并存的第二条通道（私服，见下一节），不下载任何文件，
只生成/更新游戏工程 `Packages/manifest.json` 与 `ws-game.lock`：

```powershell
powershell -File toolchain\get_framework.ps1 -Version 1.0.0 -FromRegistry
powershell -File toolchain\get_framework.ps1 -Version 1.0.0 -FromRegistry -RegistryUrl http://192.168.1.10:4873
```

## 私服（`toolchain/registry/`）与 `sync_package_content.ps1`

`toolchain/registry/` 是框架仓库自己维护的私服（Verdaccio，npm 兼容协议）运行时——本机或局域网内
起一个私有包仓库，把框架拆成三个可独立按版本号依赖的包（`com.gamefoundation.adapter.unity`/
`com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`）供游戏侧的 Unity 工程用作用域
注册表依赖，是根 README.md"版本与发布"一节"私服通道"描述的落地，与本目录 `get_framework.ps1`
默认的 zip 快照通道并存、内容一致。快速开始、配置细节、无人值守发布账号原理见
[toolchain/registry/README.md](registry/README.md)；完整设计见
`architecture/落地计划/落地方案与分阶段计划.md` 第 3.5.1 节。

`toolchain/sync_package_content.ps1` 是这条通道的配套工具，运行在**游戏仓库**那一侧（与
`get_framework.ps1` 同侧）：把游戏工程通过 UPM 私服解析到的 `com.gamefoundation.framework-data`
包内容（`Data~/`，Unity 资产管线不导入）同步到该工程自己的 `Assets/StreamingAssets/GameFoundation/`
（数据表、占位资产）、`Assets/TextMesh Pro/`（TMP 运行期资源）、`Assets/Framework/Resources/Fonts/`
（占位字体）——引擎运行时的内容根固定是工程自己的 `Application.streamingAssetsPath`，不可能直接
指向某个包在 `Library/PackageCache/` 下的解析路径，因此需要这一步同步，原理与 zip 通道下
`games/_template/README.md`"复制为新游戏：改哪几处"第 8 步同一个根因。

```powershell
powershell -File toolchain\sync_package_content.ps1 -UnityProjectPath <你的 Unity 工程根目录>
powershell -File toolchain\sync_package_content.ps1 -UnityProjectPath <工程根> -PackageName com.gamefoundation.toolchain -ResolveOnly
```

## 参数化内容同步入口（`sync_content.ps1`，ADR-0040 决策 3）

`toolchain/sync_content.ps1` 是 [ADR-0040](../architecture/adr/0040-运行期宿主命令行能力契约.md)
决策 3 新增的**通用、参数化**内容同步入口（对应消费方反馈第 69 条：无官方工具把"消费方自己
游戏的数据"同步到任意 Unity 工程）——面向任意外部工具（CI 脚本、内容作者自己的工具链、任何第
三方联调工具），不为某个具体消费方或某个内容编辑器定制专属协议。参数只认字面的
`-SourceDir`/`-TargetDir` 两个路径，不对目标目录做任何 Unity 工程/StreamingAssets 子路径拼接
假设，调用方自己决定要同步到哪一层目录：

```powershell
powershell -File toolchain\sync_content.ps1 -SourceDir <任意源内容目录> -TargetDir <任意目标目录>
powershell -File toolchain\sync_content.ps1 -SourceDir <源目录> -TargetDir <目标目录> -OverridePolicy Mirror
powershell -File toolchain\sync_content.ps1 -SourceDir <源目录> -TargetDir <目标目录> -DryRun
```

参数面：`-SourceDir`/`-TargetDir`（均必填，任意路径）、`-OverridePolicy`（`Additive` 默认，只
新增/更新、保留目标独有文件；`Mirror`，以源为准，删除目标中源里没有的文件）、`-DryRun`（只报告
将要发生的改动，不写入任何文件）。幂等：源内容不变时重复运行不产生有意义差异（按内容哈希而非
修改时间判定变化）。详见脚本头部 `.SYNOPSIS`/`.PARAMETER`/`.NOTES` 与
`toolchain/tests/test_sync_content.py`。

### 三个同步入口如何选（认知成本对照表）

仓库现在有三个"同步内容到某处"的入口，各自服务不同场景，互不取代、互不包装——`sync_content.ps1`
是通用原语，另外两个是各自场景下的专属预设（固定好了源/目标路径拼接规则与专属治理规则，换来
"不用自己填路径、还顺带做了专属自检"的便利）：

| 入口 | 运行在哪一侧 | 源 | 目标 | 专属规则 | 适用场景 |
| --- | --- | --- | --- | --- | --- |
| `sync_content.ps1` | 任意（通用原语） | 任意路径（`-SourceDir`） | 任意路径（`-TargetDir`），字面同步，不拼接子路径 | 无——只有 Additive/Mirror 两种通用覆盖策略、干跑 | 框架自身发布流程与私服通道之外的任意场景，例如某个游戏想把自己独有的内容数据同步进自己独立维护的运行期工程 |
| `sync_package_content.ps1` | 消费方游戏工程侧 | 该工程通过 UPM 私服解析到的 `com.gamefoundation.framework-data` 包内容（`Data~/`），不是任意目录 | 固定拼接到该工程的 `Assets/StreamingAssets/GameFoundation/`（另有 TMP 运行期资源、占位字体两个例外目标） | 按 `resource_layout_map.json` 做 sprites/audio/vfx 等目录改名/扁平化；维护跨次调用的同步清单，避免整树镜像误删同目录下消费者自己的文件（如 `Assets/TextMesh Pro`） | 走私服（UPM 作用域注册表）通道接入框架的游戏工程，需要把包解析结果落地成引擎运行时能读到的 StreamingAssets 内容 |
| `build.ps1 -SyncContent` | 仅框架仓库自己的工作树 | 框架仓库自己的几类内容（`data/_framework`、`data/_sample`、`games/_template/data/game`、`assets/_placeholder` 等），源路径硬编码 | 框架自己的开发自测工作台 `adapters/unity/Assets/StreamingAssets/GameFoundation/`，目标路径硬编码 | 一次调用同步好几组固定源/目标路径对；`games/_template/data/game` 同步排除 `.meta`；同步后自检"目标目录不允许残留 `.meta`"，命中直接 `exit 1` | 框架仓库自己开发/自测时把仓库内几类内容刷新进工作台 Unity 工程，不面向框架仓库之外的任何消费方 |

## ABI 探针（`abi_probe.ps1`，第十六/十七方深度审核跟进）

`toolchain/abi_probe.ps1`：发布前二进制/API 兼容性门禁（见根 `architecture/11_工程规范与测试.md`
第 7 节"发布说明不得宣称未经验证的二进制兼容"）。两道独立检查，任一不满足都判失败：

1. 手写消费方探针（`toolchain/abi_probe/`）：编译一份只见过基线版本公开签名的最小消费方，换上
   当前工作树刚构建出的正式 DLL、**不重新编译**运行——`System.MissingMethodException` 等即说明
   存在未声明的二进制破坏性变更。
2. 通用公开 API 表面差异（`toolchain/abi_surface/`，PJ114-02 根治）：用
   `System.Reflection.MetadataLoadContext` 反射基线与当前六个核心 DLL 的全部公开/受保护 API
   表面并逐行比对，不再依赖第 1 点手写消费方里人工维护的少量调用点——新增/修改的公开签名即使没人
   记得在消费方探针里补一条调用，也会被这一道检查覆盖到。放行清单见
   `toolchain/abi_surface_allowlist.txt`（默认空，只允许放行走过 ADR 流程的 MAJOR 破坏）。

基线版本号读取同目录 `abi_probe_baseline.txt`（默认 `1.12.0`，人工维护的"最后一个已知二进制兼容"
锚点，日常 MINOR/PATCH 发布不应推进它，见该脚本 `.PARAMETER BaselineVersion` 判断记录）。基线
发行包（`dist/ws-game-<基线版本>.zip`）是本机构建缓存，`.gitignore` 排除、未必存在于每台机器——
找不到时脚本默认打印警告后以**退出码 3（SKIP，不是 PASS）**收尾，`-SkipIfBaselineMissing:$false`
改为强制要求（此时基线缺失判 FAIL，退出码 1）。`-OutDir` 输出目录有边界保护（PJ114-03 根治）：
省略时用时间戳 + 随机后缀新建一个全新临时目录；显式传入且已存在时，非空直接拒绝，任何分支都不再
对已存在目录做递归删除。

已纳入 `check.ps1` 全量步骤（`-Quick` 跳过，见该脚本"2b. ABI 探针"步骤）；`check.ps1 -AbiStrict`
把"基线缺失"从可见 SKIP 升级为 FAIL（`build.ps1 -Release` 固定传此开关）。独立运行：

```powershell
powershell -File toolchain\abi_probe.ps1
powershell -File toolchain\abi_probe.ps1 -BaselineVersion 1.12.0 -SkipIfBaselineMissing:$false
```

复审整合项 1 根治（review_E.md T-N6-7 遗留缺口）：`Core.Sim.dll` 一旦真的存在于基线包里
（例如基线换成已含它的 1.36.0），`abi_surface` 反射解析其公开成员签名同样需要 `Adapters.Stub.dll`
在同一目录——此前只在"当前工作树"一侧拷贝了这份依赖，基线侧漏拷贝，探针在应该跑通的正常路径上
会直接抛 `FileNotFoundException`。现基线侧按同一 entry 后缀（`toolchain/validator/lib/
Adapters.Stub.dll`）尝试解出，找不到（旧基线本就不含 `Core.Sim.dll`）就跳过，不算错误。

`toolchain/abi_surface` 也可独立调用，覆盖任意 DLL 集合（不限于本仓库的基线/当前场景）：

```powershell
dotnet run --project toolchain\abi_surface -- dump --out surface.txt <dll1> <dll2> ...
dotnet run --project toolchain\abi_surface -- compare baseline.txt current.txt --allowlist toolchain\abi_surface_allowlist.txt --out report.txt
```

### `abi_surface` dump/compare 覆盖范围（ABI-116-01 根治，外部审计 audit-24a11fe-20260910）

此前 dump 只记方法/构造/字段/事件/属性访问器"是否可见"（public/protected/protected-internal 才
dump），不记具体是哪一档——`public -> protected` 这类可见性收窄两次 dump 输出完全相同，
`compare` 判 `breaks=0`，而旧编译消费方实际运行会抛 `System.MethodAccessException`（最小 oracle
见 `toolchain/tests/test_abi_surface_compare.py::test_public_to_protected_negative_oracle_end_to_end_via_real_dll`）。
现在每种成员与类型都单独记可见性档位，`compare` 对"收窄"判破坏、对"放宽"豁免（放宽是调用方能力
只增不减，不构成破坏，报告里单列"可见性放宽"小节，不计入 `breaks`）：

| 维度 | dump 记录位置 | compare 判定 |
|---|---|---|
| 方法/构造/事件可见性 | MEMBER 行 flags 首 token：`public`/`protected`/`protected-internal` | 收窄=破坏，放宽=豁免 |
| 字段可见性 | 同上 | 同上 |
| 属性 get/set 可见性 | 既有 `get:VIS,set:VIS`（未变） | get/set 分别判定，任一收窄即破坏 |
| 类型可见性（含嵌套类型） | TYPE 行 flags 首 token：`public`/`nested-public`/`nested-protected`/`nested-protected-internal` | 收窄=破坏，放宽=豁免 |
| 抽象/虚方法变 sealed/非虚 | MEMBER 行 flags：`virtual`/`abstract`/`sealed-override` | 变化=破坏（派生方重写会断） |
| 实例 ↔ 静态 | MEMBER/字段/事件 flags：`static` | 变化=破坏 |
| 类型种类变化（class/struct/interface/enum/delegate） | TYPE 行第 3 列 | 变化=破坏 |
| 泛型约束变化 | TYPE/MEMBER flags：`constraints:!<位置>:<variance&特殊约束&基类约束>` | 任何变化=破坏（不做放宽豁免，任何方向都当破坏处理，对齐既有接口新增 abstract 成员的治理口径） |
| 非枚举 const 字段值变化 | field 行 sig 追加 `=<内联值>` | 变化=破坏（值被内联进旧调用方 IL） |

### `abi_surface` dump/compare 覆盖范围补齐（ABI-1162-01 根治，外部审计 audit-4faab73-20260910）

此前属性签名只编码 `Name:PropertyType`，不含 `PropertyInfo.GetIndexParameters()`——public indexer
（`this[int]`）的索引参数类型变化（例如改成 `this[string]`）两次 dump 输出完全相同，`compare` 判
`breaks=0`，而旧编译消费方运行期 `System.MissingMethodException`（最小 oracle 见
`toolchain/tests/test_abi_surface_compare.py::test_indexer_parameter_type_change_negative_oracle_end_to_end_via_real_dll`）。
核对同一批容易漏判的签名维度时，另确认运算符重载/转换运算符（此前被 `IsSpecialName` 整体跳过，
完全不进 dump）、事件 remove 访问器可见性（此前只记 add 半边）两处也是真实缺口，一并补齐：

| 维度 | dump 记录位置 | compare 判定 |
|---|---|---|
| 索引器（属性）索引参数类型 | property 行 sig：`Name[<索引参数类型列表>]:PropertyType`，非索引器不追加 `[...]` | 变化=破坏（物理上是不同的 `get_Item`/`set_Item` 方法签名） |
| 运算符重载/转换运算符 | method 行（`op_Addition`/`op_Implicit`/`op_Explicit` 等按普通 method 记录，不再被 `IsSpecialName` 整体过滤） | 删除/改签名=破坏，与普通方法同一治理口径 |
| 事件 remove 访问器可见性 | MEMBER 行 flags 追加独立 token：`remove:<VIS>`（add 可见性仍是首 token，不影响既有放宽豁免逻辑） | remove 单独收窄=破坏（`obj.Event -= handler` 物理调用 `remove_Event`） |

以下两个维度核查后确认**不**编码，与既有 `ref`/`out`/`in` 不编码设计（见
`TypeNameFormatter.FormatParameters` 判断记录）同一治理逻辑：`params` 数组修饰
（`ParamArrayAttribute`）与可选参数默认值存在性/取值——两者都是 C# 编译器侧语法糖，不改变 IL
物理签名，旧编译调用方省略实参时编译器已把具体值/展开后的显式数组写死进调用方 IL，物理绑定只按
参数类型与个数匹配。曾经短暂编码过这两项，但用 `dist/ws-game-1.12.0.zip` 基线重跑当前工作树六个
DLL 时产生 5 处假破坏——框架给公开构造函数新增尾部可选参数时统一采用"新增一个更长的可选参数
重载，同时保留原始定长参数表的旧重载（旧重载参数改为不带默认值，仅作为物理兼容 shim）"模式
（`FieldSchema`/`EconomyContentValidationRule`/`LootContentValidationRule`/`SkillHost`/
`ViewBinder` 均如此），旧编译调用方物理上调用的正是这个保留的定长重载，从未因为它是否声明默认值
而受影响；按值/存在性编码后误将这一合法兼容模式判成破坏，因此撤回、改为不编码（真实反射验证见
`toolchain/tests/test_abi_surface_compare.py::test_params_and_optional_default_value_are_not_encoded_in_real_dump`）。

### `abi_surface` dump/compare 覆盖范围补齐（TOOL-118-ABI 根治，codex 第十八轮，audit-d6fda65-20260911）

此前属性行 flags 只有 `get:VIS,set:VIS[,abstract]`，完全不编码访问器的 static/virtual/final——
public 实例属性改成同名同类型 static 属性（或反过来）两次 dump 输出完全相同，`compare` 判
`breaks=0`，而旧编译消费方运行期 `System.MissingMethodException`（getter/setter 从虚方法调用
`callvirt` 变成 static 方法调用 `call`，物理绑定方式不同；最小 oracle 见
`toolchain/tests/test_abi_surface_compare.py::test_property_instance_to_static_negative_oracle_end_to_end_via_real_dll`，
复现证据见 `docs-project/abi-property/{result.json,consumer-new.log,surface-report.txt}`）。索引器
复用属性分支的同一套 flags，事件此前只记 `static`、没记 `abstract`/`virtual`/`final`，一并补齐：

| 维度 | dump 记录位置 | compare 判定 |
|---|---|---|
| 属性/索引器实例 ↔ 静态 | property 行 flags 追加 `static`（get/set 任一访问器判定，C# 不允许同一属性两个访问器 static 状态不一致） | 变化=破坏 |
| 属性/索引器 abstract/virtual/sealed-override | property 行 flags 追加，与 method 行同一套编码（`abstract`/`virtual`/`sealed-override` 三选一或都不出现） | 变化=破坏 |
| 事件 abstract/virtual/sealed-override | event 行 flags 追加，取 add/remove 访问器并集判定 | 变化=破坏 |

与此配套，`SurfaceCompareLogic.SplitPropertyIdentity`（可见性放宽豁免判定用的"身份"）此前只把
`abstract` 状态纳入身份、不含 static/virtual/sealed-override，会让"属性同时可见性放宽 + 从实例
变 static"这类复合变化被误判成纯可见性放宽而放过（假阴性）；现在身份纳入全部非可见性 token，
与 method 行 `SplitVisibility` 的"去掉可见性 token 后其余部分参与身份比较"同一口径（回归测试见
`test_property_static_change_not_masked_by_simultaneous_visibility_widening_is_breaking`）。

用新版工具对 `dist/ws-game-1.12.0.zip`、`dist/ws-game-1.13.0.zip` 两份历史基线重跑当前工作树六个
DLL：两者均 `breaks=0`（1.12.0 additions=275，1.13.0 additions=186；未发现历史真实破坏；1.13.0
场景的手写消费方探针第 1 点因 consumer 源码只锚定 `abi_probe_baseline.txt` 记录的 1.12.0 签名
集合，对 1.13.0 编译会失败，这是既有已知边界，与本次 dump/compare 覆盖范围扩充无关，不在本次
改动范围——1.13.0 只跑 dump/compare 两份基线 DLL，不跑消费方探针）。

## Unity 测试结果分诊（`unity_test_triage.py`，排查复盘 2026-09-15 落地）

背景见 `docs/复盘/排查复盘-2026-09-15-PlayMode-PRES180.md`：一次 PlayMode
全量门禁稳定失败排查耗时约 3.5 小时、约 156 万 token，其中很大一块耗在"没有工具把失败用例对应
的日志片段与首个异常抽出来，只能整段读 `playmode.log`"（十几千行）。本脚本补上这一环，输入
NUnit3 结果 XML（`playmode.xml`/`editmode.xml`）与对应 Unity 日志（`playmode.log`/
`editmode.log`），输出：

- 汇总（`total`/`passed`/`failed`/`skipped`/`inconclusive`）；
- 每个失败用例的 NUnit message/stack-trace；
- 用例在日志里的执行窗口片段（见下方"定位方式"）；
- 窗口内按行号顺序排列的疑似异常/断言行——**并显式提醒**：NUnit 结果 XML 里的
  message/stack-trace 只是该用例抛出的**最后一次**异常（`UnityLogCheckDelegatingCommand` 在
  断言异常之后仍会跑 `CheckLogs`，见复盘文档），窗口内更早出现的一条才是第一现场；
- 窗口内 Warning/Error 行摘要（按文本去重计数）。

用法：

```
python toolchain/unity_test_triage.py --xml <path/to/playmode.xml> --log <path/to/playmode.log>
python toolchain/unity_test_triage.py --xml ... --log ... --json
python toolchain/unity_test_triage.py --xml ... --log ... --max-lines 60
```

返回码：`0` 结果 XML 里没有失败用例；`1` 至少一个失败用例（诊断信息的退出码，不代表脚本本身
出错）；`2` 命令行参数错误（输入文件不存在/XML 解析失败）。

**用例窗口定位方式（四级回退，见脚本文件头判断记录）**：实测本仓库 Unity 6000.3.23f1 批处理
`-runTests` 跑出的日志**没有**任何逐用例起止标记，甚至连用例名/类名都不会被打印。定位因此按
以下顺序回退：

1. `test_first_chance`——若测试程序集里已接入 `TestFirstChanceExceptionLogger` 回调（见
   `adapters/unity` 下 `Tests/Runtime`/`Tests/Editor` 两份实现，本工具与之配套设计），日志里
   会有精确的 `[TestFirstChance] Started: <用例全名>` / `[TestFirstChance] Finished: <用例全名>
   result=<ResultState> message=<...>` 边界对，直接用这对标记切出精确窗口——**注意**：窗口内
   不会有断言异常本身的日志行（NUnit 断言失败不经过 Unity 日志系统，与该回调无关），但窗口内
   真实发生的 `LogType.Error`/`LogType.Exception` 级日志会额外打一行
   `[TestFirstChance] LogDuringTest: <用例全名>: <级别>: <消息首行>`，本工具一并识别、单独列出
   （见 `--json` 输出的 `log_during_test` 字段）；
2. `name_fallback`——退化为在日志里搜索用例全名/方法名的**首次出现位置**，按各用例找到的行号
   排序切窗口，是近似值；
3. `message_fallback`——再退化为用 NUnit message 里一段原文去日志里找可能相关的行，**不保证**
   属于该用例窗口；
4. `not_found`——以上都找不到时，如实说明，Warning/Error 摘要与异常扫描退化为整份日志范围
   （报告里会明确标注"可能混入其它用例的内容"）。

**判断（定案）：没有用例边界打点的日志，诚实退化到 `not_found`，不靠猜测补边界。** 在本仓库真实产出的历史
`playmode.log`（PRES180 事发时那次，`TestFirstChanceExceptionLogger` 尚未接入）上实测，`PRES180` 一类失败用例会一路退化到
`not_found`：这类日志里没有任何可供第三方脚本识别的用例边界信号，硬猜只会把别的用例的内容算进来、给出看似精确实则错误的
定位——所以本脚本在日志本身不带边界时就明说"定位不到，范围退化为整份日志"，这是对的行为，不是缺口。可靠的办法是从测试进程内部
主动打点，这一步已在测试程序集里补上（`TestStarted`/`TestFinished` 边界 + `LogDuringTest` 行，见下一段判断记录与复盘文档 C 部分），
此后产出的日志命中第 1 级定位；旧日志不追溯。

**判断记录（`TestFirstChanceExceptionLogger` 最初设计与实测证伪，2026-09-15）**：该回调最初
按 `AppDomain.FirstChanceException` 设计（异常刚抛出、尚未被任何代码捕获时就能拿到），但在本仓库
使用的 Unity 6000.3.23f1 Editor（Mono 脚本后端）上实测：即使是最简单的
`try { throw new InvalidOperationException(...); } catch { }` 也不会触发该事件订阅的回调——
不是实现哪里写错了，是这个 Mono 运行时压根不触发该事件（已知的 Mono 嵌入式运行时限制）。已改为
验证可用的方案：`TestStarted`/`TestFinished` 打边界，`Application.logMessageReceivedThreaded`
（标准公开 API，不依赖 Mono 对 `FirstChanceException` 的支持）捕获窗口内的 Error/Exception 级
日志——真实用 EditMode/PlayMode 各跑一条构造的失败用例验证过：边界与 `LogDuringTest` 行均按
预期出现（见 `adapters/unity` 下该文件判断记录 1 的完整记录）。

已接入 `check.ps1`："Unity EditMode 测试"/"Unity PlayMode 测试" 两步判定失败（NUnit 结果 XML
根节点 `result` 非 `Passed`）时自动调用本脚本，把报告打进门禁日志；成功分支不调用、不多打印。
找不到 `python` 或分诊脚本自身报错只告警，不改变 Unity 步骤本身的 Ok/Detail 判定（诊断辅助，
不是新的把关点）。

回归测试：`toolchain/tests/test_unity_test_triage.py`（内嵌最小 XML+log 夹具，核心用例还原
"最后异常覆盖首个断言"场景，断言分诊脚本把更早出现的 `AssertionException` 排在
`UnexpectedLogMessageException`/`Expected log did not appear` 之前）。

## Unity `.meta` 完整性检查（`check_unity_meta.py`）

背景：Unity 会为它自己导入范围内的每个文件/目录生成一个同名 `.meta`，仓库约定把 `.meta` 与
源文件一并提交；漏提交会让消费方克隆后 Unity 重新生成 GUID，破坏既有场景/预制体对这些资源的
引用。执行 agent 一律被要求不跑 Unity（`check.ps1`/`build.ps1` 的 Unity 步骤耗时且会触发全量
reimport），而 `.meta` 恰恰是 Unity 首次导入新文件时才在本机生成的产物——纯 .NET/Python 门禁
此前完全没有覆盖这一类问题，已经在 1.45.0 发版前（`games/_template/` 下 6 个新 `.cs`）与之后
（`Runtime/Diagnostics/` 下 2 个新 `.cs`）各漏提交一次，口头提醒不管用。

**判定范围（用当前仓库实测核对过，不是想当然）**：

1. `<Unity 工程>/Assets/` 整棵树。
2. `<Unity 工程>/Packages/<pkg>/`——直接摆在 `Packages/` 下、自带 `package.json` 的本地包
   （本仓库目前是 `adapters/unity/Packages/com.gamefoundation.adapter.unity/`）。
3. `<Unity 工程>/Packages/manifest.json` 里值以 `"file:"` 开头的依赖——UPM 内嵌本地包，物理
   路径可能落在仓库其它位置（本仓库目前是 `games/_template/`、`adapters/conformance/`）。

`Packages/manifest.json`、`packages-lock.json` 本身是 Package Manager 的清单文件，不是被导入
的资源，天然不在任何一个"导入根"目录*里面*，不需要额外硬编码例外——已用当前仓库实测核对：这两
个文件确实没有 `.meta`。范围之外的例子：`adapters/unity/DiagnosticsForwarding/`（普通 dotnet
测试工程，有 `.csproj`，不在 `Assets/`/`Packages/` 下）不需要 `.meta`。

两类问题：**缺失**（已跟踪文件落在导入范围内、没有对应 `.meta`）与**孤儿**（`.meta` 存在，但
对应文件/目录已不再被跟踪）。孤儿判定额外排除一种情况：目标路径整体被 `.gitignore` 排除（如
`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core/`，见该
`.gitignore` 条目上方判断记录——同步进来的构建期 DLL 不提交），但目录**自身**的 `Core.meta`
为保留稳定 GUID 仍按约定提交——这种"目录级 meta 但目录内容被忽略"不算孤儿。

**判断记录（`git check-ignore` 对不存在的目录判空，2026-09-20）**：`_is_ignored` 最初只查一次
`git check-ignore -q -- <目标路径>`（不带尾部 `/`），对 `Runtime/Plugins/Core.meta` 这个已知
应豁免的样本却判定"未被忽略"，把它误报成孤儿——原因是 `.gitignore` 里这条规则写成"仅目录"形式
（以 `/` 结尾），而 `git check-ignore` 判断一个不带尾部 `/` 的路径是否命中"仅目录"规则时要靠
stat 该路径确认它确实是目录；孤儿检查的目标路径本身多半已不在本地磁盘存在（构建产物目录、或
确已被删除的资源），stat 不到就不会命中仅目录规则。改法：`rel_path` 与 `rel_path + "/"` 两种
形式各查一次，任一命中即算被忽略，不依赖目标是否真的在磁盘上。

**判断记录（实现放在 Python 而不是内联 PowerShell）**：`check.ps1` 里紧邻的"禁用词扫描"/"工作树
文本文件无 CR"两步是纯 PowerShell 实现，但本检查需要解析 `manifest.json`（JSON）、维护"已跟踪
文件集合"与"目录前缀"这类集合运算、还要支持独立于真实仓库状态的单元测试覆盖每条判定分支——这些
在 PowerShell 5.1（不能用 `&&`/三元运算符）下明显更啰嗦，而 `toolchain/` 下已有大量同类"跨平台
静态规则检查"用 Python 实现 + `toolchain/tests` 覆盖的先例（`validate_data.py`、
`format_data.py`、`ref_conventions.py` 等）。因此实现为 `toolchain/check_unity_meta.py`，
`check.ps1` 只新增一步 `Test-NativeExitCode "python" @("toolchain/check_unity_meta.py")`
（与相邻的 `validate_data.py` 等步骤同一调用模式），放在"7.6"（紧接"工作树文本文件无 CR"之后、
"8. 版本一致性"之前）——这一段既有步骤都是纯静态检查、不依赖 `dotnet build`/Unity，`-SkipUnity`/
`-Quick` 下同样跑，符合"必须在 -SkipUnity 下也能跑"的要求。

**判断记录（目录本身也要有 .meta，二次踩坑后补齐，2026-09-20）**：第一版实现只检查文件级
缺失——`git ls-files` 从不返回目录本身的路径，只有文件路径，漏了"某个目录自己也要有 `.meta`"
这一条。真实踩坑：`Runtime/Diagnostics/` 下两个 `.cs` 补齐文件级 `.meta` 后，Unity 真实导入
还额外生成了目录级的 `Runtime/Diagnostics.meta`（本仓库同类既有样本：`.../Runtime/
Plugins.meta` 是 `Plugins` 目录自身的 meta，与 `Plugins/Core.meta` 是两层不同的东西）。补齐
方式：`_ancestor_dirs` 从每个被跟踪文件反推它在导入根内的全部祖先目录（不含导入根自身——
`Assets`、每个包/内嵌包根目录都不需要 `.meta`，已用当前仓库实测核对），汇总成
`required_dirs` 集合；缺失检查、孤儿检查（孤儿判定里"目录下还有其它被跟踪文件"这一分支原本
就是用等价的前缀匹配实现的，改用 `required_dirs` 只是复用同一份集合，逻辑不变）都基于这个
集合统一处理，不再区分文件/目录两套逻辑。隐藏目录/以 `~` 结尾的目录同文件一样整体跳过（含
其内容，见 `_is_in_import_scope`）。回归测试新增 `test_directory_level_meta_missing_is_
detected`（原样复现 Diagnostics 这次的形状：两个文件各自都有 meta、目录没有）与
`test_nested_directories_each_require_their_own_meta`（多层嵌套，每层各自要求）。

回归测试：`toolchain/tests/test_check_unity_meta.py`——在**独立的临时 git 仓库**里构造最小
Unity 工程布局，覆盖缺失（文件级/目录级/多层嵌套）/孤儿/"目录被忽略不算孤儿"/`Packages/` 根
清单文件不在范围内/范围外目录不检查/`manifest.json` `file:` 内嵌本地包被发现（含其目录级
meta）/隐藏文件与 `~` 结尾文件不要求 `.meta`/CLI 退出码共 11 个场景，外加一条对**真实仓库
当前状态**的回归（共 12 个用例，见该测试注释）。

**判断记录（落地当次实测检出的三处真实缺口，2026-09-20，最终状态）**：本检查从设计到落地的
过程中，在 `main` 上先后实测检出三处真实存在、且此前完全没有任何门禁能发现的不一致，均已修复，
落地时（`main` `29d7e5bd`）对当前仓库实跑为**零缺失、零孤儿**：

1. **文件级缺失**：`Runtime/Diagnostics/` 下 `DiagnosticsHub.cs`、
   `DiagnosticsHubComposition.cs` 两个新文件漏提交 `.meta`（本检查最初立项的直接起因）。由
   真实 Unity 导入补齐（提交 `90691cc3`）。
2. **目录级缺失**：修复上一条时 Unity 额外生成了目录级的 `Runtime/Diagnostics.meta`——第一版
   实现看不到这类问题（见上方判断记录），促成本检查补齐目录级判定能力。
3. **规则与现状脱节导致的"该跟踪却被忽略"**：`adapters/unity/Assets/Resources/
   GameFoundation/...` 下有 17 个永久提交的占位模型/动画资产（含子目录自身的
   `GameFoundation.meta` 都已跟踪），但 `.gitignore` 把父目录的 `Resources.meta` 整体忽略——
   该条忽略规则写于"`Assets/Resources/` 目录此前不存在、由 PlayMode 性能测试自动创建"之时，
   前提早已过时（目录下现有大量永久性内容），规则本身与仓库现状自相矛盾：不跟踪会让消费方
   克隆后该目录 GUID 重新生成。修复方式是**撤销那条过期的忽略规则**（`PerformanceTestRun*`
   一条运行时文件忽略保留），磁盘上早已存在的 `Resources.meta`（2026-09-06 由 Unity 生成）
   随之正常纳入跟踪（提交 `29d7e5bd`）——不是手写 meta，也不是放宽本检查。

三处缺口的共同点：**都是"纯 .NET/Python 门禁完全看不见、只有对照 Unity 实际导入范围才能发现"
的一类问题**，且第三处（规则注释与仓库现状脱节）尤其难靠人眼审查发现——`.gitignore` 里那条
规则本身读起来完全合理，只有把它跟"该目录下实际有哪些文件被跟踪"逐条对照才能看出矛盾。本检查
落地当次即照单验出全部三处，是它存在价值的直接证明，也是本次任务"给 `check.ps1` 补一个不依赖
Unity 的 meta 完整性检查"的完整验收闭环。

## `toolchain/tests` 里 PowerShell 子进程用例的确定性环境（`_ps_subprocess_env.py`，2026-09-20）

背景：`test_real_baseline_end_to_end_via_abi_probe`（`test_abi_surface_compare.py`）在部分
开发机上稳定复现失败：`abi_probe.ps1` 报 `The term 'Get-FileHash' is not recognized ...`
（`CommandNotFoundException`）。

**根因（已用最小复现锁定，非猜测）**：本仓库的开发/门禁环境普遍是从 PowerShell 7（`pwsh`，
本机为 MSIX/WindowsApps 打包安装）里拉起 Windows PowerShell 5.1（`powershell.exe`）子进程去
跑各类 `.ps1` 门禁测试。Python `subprocess.run` 对子进程做的是原始 CreateProcess，会把父进程
（pwsh 7）自己的 `PSModulePath`（其中含一条 PowerShell 7 专属的 MSIX/WindowsApps 模块目录）
原样继承给子进程；Windows PowerShell 5.1 在这个外来模块目录下做命令自动发现（`Get-FileHash`
这类内置 cmdlet 靠自动加载解析）时会内部命中一个非终止性异常，`abi_probe.ps1` 等门禁脚本按
AGENTS.md §3"保持确定性"的惯例设置了 `$ErrorActionPreference = "Stop"`，这个非终止性异常被
提升为终止性异常，导致整个命令自动发现流程中止。用四组对照的最小复现锁定了触发条件（详见提交
`toolchain/tests/_ps_subprocess_env.py` 头注释与本次改动判断记录）：只在"父进程是 pwsh 7 +
raw `subprocess.run` 继承其 `PSModulePath`（含该 MSIX 模块目录）+ 目标脚本设了
`$ErrorActionPreference = 'Stop'`"三个条件同时满足时复现；缺任一条件（如 pwsh 自己的 `&`
调用运算符起子进程、或不设 `Stop`）均不复现。

**为什么 `check.ps1` 的 ABI 探针步骤这次没有暴露这个问题**：该步骤用 `& powershell -NoProfile
... -Command ...`（pwsh 的调用运算符）起子进程，实测这条路径下子进程拿到的 `PSModulePath` 是
Windows PowerShell 5.1 的原生默认值（不含 PowerShell 7 的模块目录），因此不受影响——这正是
"同一份 `abi_probe.ps1`，`check.ps1` 跑得过、pytest 跑不过"的原因，不是脚本本身的缺陷。

**根治方向选择**（不是"哪个都能修，随便选一个"，三个候选逐一评估）：
1. **采用**：测试侧显式给 5.1 子进程一个确定、干净的 `PSModulePath`（新增
   `toolchain/tests/_ps_subprocess_env.py` 的 `clean_powershell_env()`），效果上与
   `check.ps1` 已经在用的调用方式对齐，使两者不再因为"父进程是哪个 shell"而分道扬镳。
2. 不采用"统一测试侧也改用 pwsh 起子进程"：本仓库 `toolchain/*.ps1` 的兼容目标本来就包含
   Windows PowerShell 5.1（下游游戏仓库消费方的典型环境），相关测试（ANSI 代码页解析、`-File`
   场景下的 `[bool]` 参数绑定行为等，见 `check.ps1` ABI 探针步骤判断记录）必须能在真实的 5.1
   宿主下验证，不能用 pwsh 替代验证对象。
3. 不采用"在 `abi_probe.ps1`/`get_framework.ps1` 等生产脚本内部加防御"：这些脚本的实际调用方
   （`check.ps1`/`build.ps1`）已经用 `& powershell ...` 规避了这个问题，不需要再改；
   `get_framework.ps1` 早先为兼容"下游消费方自己的运行环境不可控"这个更宽的问题已经内联了不
   依赖 `Get-FileHash` 的兜底哈希函数（`Get-Sha256FileHash`，见该脚本判断记录），但那是为
   下游消费方兜底、职责边界不同——本仓库内部的 pytest 用例完全知道自己要调用哪个宿主，不存在
   "调用方环境不可控"的理由，把测试环境问题也塞进生产脚本只会徒增生产代码分支。

**同类问题一并修**：扫描 `toolchain/tests` 下全部会启动 PowerShell 子进程的用例，在
`test_abi_probe_outdir_safety.py`、`test_abi_surface_compare.py`、
`test_build_version_writeback_lf.py`、`test_get_framework_lock_source_no_local_path.py`、
`test_get_framework_path_boundary.py`、`test_get_framework_with_samples.py`、
`test_lock_writeback_repair_parity.py`、`test_powershell_scripts_ansi_safe.py`、
`test_registry_stop_pidfile_rewrite_timestamp.py`、`test_sync_content.py` 共 10 个文件的
13 处 `subprocess.run` 调用点统一接入 `clean_powershell_env()`，不是只修最先暴露的那一个。
`clean_powershell_env()` 的 `PSModulePath` 处理只对 Windows PowerShell 5.1 目标生效（按可执行文件名判定），
pwsh 目标的 `PSModulePath` 不动——目前没有复现证据表明 pwsh 目标存在同类问题，不做未经验证的改动
（2026-10-01 起两种宿主都会剥掉全部 `GIT_*` 变量，因此该函数总是返回字典而不再返回 `None`，见
下方"测试里起 git 子进程的环境隔离"一节）。

**反向确认**：临时删掉 `test_abi_surface_compare.py` 里的 `env=clean_powershell_env(...)`
参数，并把启动 pytest 的那个 pwsh 会话的 `PSModulePath` 显式设成含 PowerShell 7 MSIX 模块
目录的污染值，`test_real_baseline_end_to_end_via_abi_probe` 稳定复现失败
（`CommandNotFoundException`）；加回该参数后，同一个污染的 `PSModulePath` 下测试稳定通过——
确认修复对症，不是巧合。

## Unity 相关门禁步骤的 Windows MAX_PATH 快速失败守卫（`_unity_path_length_guard.ps1`，2026-09-20）

背景：深层 scratchpad 工作树（`git worktree add` 到系统临时目录下，路径本身比主检出深很多）里
跑 Unity 测试会踩 Windows 260 字符 MAX_PATH——具体触发点是 `games/_template/Tests/Runtime/
GameTemplateResidentTests.cs` 的 `ResidentRunner_DatasetRootOverride_LoadsProbeTable_
FromOverrideRootOnly` 用例（见该文件类型头 2026-09-19 判断记录）：运行期把
`adapters/unity/Assets/StreamingAssets/GameFoundation/data/game` 整棵目录树（不含 `.meta`）
复制到同级一个新目录 `gf_test_dataset_root_override_<32 位十六进制 GUID>`——这个目录名比原来的
"game" 长 59 个字符，工作树根路径一旦较深，复制出来的文件绝对路径就可能超过 260。更麻烦的是
Mono/.NET 旧式路径 API 在这种情况下抛的是 `DirectoryNotFoundException` 而不是
`PathTooLongException`，症状会伪装成"目录没建出来"/数据装配失败，本仓库已经把这种伪装症状
误判成产品缺陷一次。

**根治方向（快速失败 + 自解释，不是"修复"路径过深本身）**：任务范围明确排除启用 Windows 长
路径支持、改注册表、加长路径前缀——那是改用户机器配置/引入平台特定路径形式，超出门禁脚本的
职责边界。落地为 `Test-UnityWorkingTreePathLength`（`toolchain/_unity_path_length_guard.ps1`），
`check.ps1` 在 Unity 相关四步 + 消费方演练的入口（`-SkipUnity`/`-Quick` 均不生效的那个分支）
调用，且**不经 `Invoke-CheckStep` 包裹**——`Invoke-CheckStep` 的约定是"任一步骤失败都不会中断
后续步骤"，但路径过深是环境性前提问题，一旦成立，后面几步 Unity 批处理必然全部朝着同一个根因
失败，继续跑只是白白耗掉几分钟到十几分钟，所以用会终止整个脚本的 `throw`。

**为什么独立成 `toolchain/_unity_path_length_guard.ps1` 而不是内联写在 `check.ps1` 里**：与同
目录 `_hash.ps1`/`_version_writeback.ps1` 同一模式——只定义函数、无顶层副作用，`check.ps1`
在真正调用 Unity 批处理之前 dot-source 后调用；独立成文件后
`toolchain/tests/test_unity_path_length_guard.py` 才能单独 dot-source 这一个函数测试，不需要
跑完整 `check.ps1`（后者会顺带跑一大批耗时的构建/测试步骤）。

**为什么不硬编码"最长路径是多少字符"**：函数实际扫一遍 `data/game` 下最长的相对路径，代入
覆盖目录名模板（`"gf_test_dataset_root_override_" + 32 位十六进制占位`）重新算一次——
`data/game` 下的文件将来增删（加表、改文件名）时这道校验能跟着更新，不会因为写死的数字过期
而失去保护力。落地当次实测：仓库当前 `data/game` 下最长相对路径是
`combat\combat.hit_table_config.json`/`combat\combat.level_diff_table.json`（并列，29 字符），
代入覆盖目录名模板后的模拟相对路径 156 字符；阈值取 Windows MAX_PATH 260 减 10 字符余量（覆盖
"覆盖目录名模板/GUID 格式今后如果略有变化"这类小幅度漂移，不是卡着上限走）= 250。主检出
（`D:\workespace\ws-game`，17 字符）预估最长路径 174 字符，远低于阈值；本次任务给出的深层
scratchpad 工作树根路径示例（119 字符）预估最长路径 276 字符，超阈值，正确触发。

**测试侧的编码陷阱（差点做出一个偶发失败的测试，记录下来避免下次重踩）**：`toolchain/tests/
test_unity_path_length_guard.py` 最初用 `subprocess.run(..., text=True)` 捕获守卫抛出的错误
信息、断言其中包含的中文指引文本，单独跑通过，但混进 `toolchain/tests` 全量跑时偶发
`UnicodeDecodeError: 'gbk' codec can't decode byte ... in position 9`。根因：Windows
PowerShell 5.1 下非终端/被管道重定向的 stdout/stderr 默认走系统 ANSI 代码页（本机实测是
GBK/936，与源 `.ps1` 文件的 UTF-8 编码无关），`text=True`（或显式 `encoding="utf-8"`）在多字节
序列跨读缓冲区边界处偶发解码失败或整体乱码——且这个代码页因机器区域设置而异，硬编码
`encoding="gbk"` 只在中文 Windows 上凑巧对。根治：让 PowerShell 脚本自己在真正调用守卫函数之前
显式把 `[Console]::OutputEncoding` 设成 `[System.Text.Encoding]::UTF8`，并把函数调用包一层
`try/catch`，异常信息改走 `Write-Output`（stdout，会被强制编码影响）而不是让它以未捕获异常的
形式落到原生 stderr（该流不受 `[Console]::OutputEncoding` 影响）；Python 侧固定
`encoding="utf-8"`——两端都不依赖宿主机的系统区域设置，跨代码页环境下都应确定性通过。

**测试夹具不依赖长路径支持**：用例故意构造一个较深的根路径来触发阈值，但只让函数内部"模拟"
出来的覆盖目录路径超过 260（该路径从不落盘），真正在磁盘上创建的目录/文件路径全程留有安全
余量地保持在 260 字符以内——与本次任务"不修复路径过深本身"的范围保持一致，测试也不应该依赖
宿主机是否开启了长路径支持，具体窗口推导见测试文件头注释与 `_MIN_DEEP_REPO_ROOT_LEN`/
`_MAX_SAFE_PHYSICAL_REPO_ROOT_LEN` 两个常量。

**复审发现的缺口：守卫在它要防的头号场景里恰恰失效（2026-09-20，同日修复）**

首版实现只扫 `adapters/unity/Assets/StreamingAssets/GameFoundation/data/game`，判断依据是
`.gitignore` 未生效前不会想到——该目录整个被 `.gitignore` 第 39 行忽略（`# 生成的 .meta，父
目录 Assets/StreamingAssets/ 本身除了这个子目录不再有任何其它内容` 一节），由 `build.ps1
-SyncOnly` 从 `games/_template/data/game` 同步生成。旧实现在这个目录不存在时直接 `return`
（当时的判断记录写的是"数据根缺失是别的门禁步骤该管的事，按无法评估、当作未超限处理"）。

问题：**"agent 在深层 scratchpad 里新建工作树、还没跑过任何同步就直接跑 Unity 步骤"，恰恰是本
守卫从立项起就要防的头号场景**——而新建的工作树里生成目录必然还不存在（要跑一次 `build.ps1
-SyncOnly` 才会出现）。也就是说旧实现在它最该拦截的场景里，会稳定命中"目录不存在 -> 静默
return -> 直接放行"这条分支，等于完全没有防护效果，只在"已经手工同步过一次生成目录"的工作树
里才生效——而这类工作树往往是已经跑过一轮 Unity 步骤、问题已经暴露过的工作树，防护价值最低。

根治：改为同时扫两个候选根——`games/_template/data/game`（随仓库提交的源目录，任何检出/工作
树里都在，是生成目录的来源）与 `adapters/unity/.../data/game`（生成目录，可能不存在）；两者
都存在时内容应当一致（生成目录就是从源目录同步出来的，不含 `.meta` 差异），逐个存在的根分别
求最长相对路径后取 max。这样"只有源目录存在"（新建工作树的常态）、"只有生成目录存在"（理论
上不应发生，但不假设）、"两者都在"（同步过至少一次的工作树）三种场景都能正确估算，不用猜此刻
该信哪一个。

**两个候选根都不存在时的处置选择（硬失败 vs 显式警告，按要求把理由记下来）**：这种情况理论上
只会在仓库数据目录结构本身发生变化（`games/_template/data/game` 改了位置/改了名字）而本守卫
没跟着更新时出现——不是"数据还没同步"这种正常态。评估了两个方向：

1. **硬失败（`throw`，终止整个门禁）**：优点是绝不会重蹈"静默放行"的覆辙；缺点是本守卫是一个
   "尽力估算"的启发式保护，不是不可或缺的产品行为——一旦仓库结构调整没有同步更新这两个路径，
   会让一次与本次改动完全无关的正常 Unity 工作被彻底挡住，直到有人回来修这个守卫本身，代价
   偏重。
2. **采用：显式警告后继续（不阻断门禁）**：按 AGENTS.md §3"运行时路径不静默降级；只读分析类
   入口在遇到阻断态时降级要显式标记（不能悄悄吞掉问题当作正常返回）"——本函数只读文件系统、
   不写任何东西，属于该条款覆盖的"只读分析类入口"，条款本身允许这类入口在遇到阻断态（这里是
   "找不到任何基准数据"）时选择降级，只要求降级必须**显式、醒目**，不能悄悄放行。落地为一条
   `Write-Host -ForegroundColor Yellow` 警告，明确点出两个候选根路径、说明"本次未执行路径长度
   检查"、以及"深层工作树若确实过深仍可能失败"——警告文本本身包含关键词"警告"与两个候选根的
   完整相对路径，方便在门禁输出里被人或后续自动化捕捉到，不会被误当成一次干净的 PASS。

两者的取舍标准：**本守卫的价值是"提前拦截、给出根因"，不是"绝对不允许 Unity 步骤在结构异常时
运行"**——真正决定 Unity 步骤能不能正常工作的仍然是 Unity 自身与实际数据是否存在，本守卫从未
承诺过覆盖"仓库结构本身损坏"这类更广的问题。选显式警告而不是硬失败，是不让一个范围明确、职责
单一的启发式保护，在职责之外的场景里获得"挡住整条门禁"的否决权。

**新增回归测试**（`toolchain/tests/test_unity_path_length_guard.py`，从 3 例扩到 7 例）：短
根路径放行、深根路径终止两组用例都按`generated_only`/`source_only`/`both` 三种候选根填充场景
参数化，`source_only` 正是修复前会被漏判的那一例；另新增 `test_neither_root_present_warns_
but_does_not_throw` 验证两个候选根都缺失时返回码仍是 0（不阻断）但 stdout 里能看到醒目的
"警告"字样与两个候选根路径。

## 发布产物不可变强制校验（`_dist_immutability_guard.ps1`，2026-09-20，消费方反馈第 8 条根治）

背景：仓库对外承诺"标签 + dist zip + lock 不可变发布"（见 architecture/11_工程规范与测试.md 第 7 节"构建产物与版本号约定"
一节），但此前没有任何强制手段落地这个承诺——`build.ps1`"打分发包 dist/<版本>/"一节、
"打 zip + lock"一节、`toolchain/_lock_writeback.ps1` 的 `Write-WsGameLockFile` 三处均无版本
存在性校验，会无条件覆盖同名产物。唯一的防线是 `-Release` 第 7 步的 `git tag -a`（标签已存在
会失败），但这道防线在三个产物已经被覆盖之后才执行；更严重的是 `-Dist`/`-Zip` 这两条独立于
`-Release` 的打包路径完全不经过这道防线——不校验 git 状态、不改版本号、不提交、不打标签，只要
传入一个已经发布过的版本号就会静默覆盖。复现：发布过 vX 后单独执行 `build.ps1 -Dist X -Zip`
（不用 `-Release`、不改版本号、不碰 git），三个产物被静默覆盖，同一 zip 两次打包出的哈希不同。
消费方按 `ws-game.lock` 锁定的哈希会对不上，排查时只会怀疑自己环境，而不是框架自己破坏了对外
承诺。

**判定"已发布"的依据：`git tag -l v<版本>` 是否存在，不是"dist/ 下文件/目录是否存在"**：
dist/ 整体 `.gitignore`（不进源码库），本机 dist/ 下有没有文件，只反映"最近一次在这台机器上
跑没跑过打包"，不反映"这个版本号是否已经对外发布过"——可能是上次构建失败、进程被中途杀掉
留下的半成品，也可能是一份从没打过包的干净 checkout；两种情况都不能反推"版本是否已发布"，用
文件存在性判断反而会在"半成品残留"时误判成已发布而拒绝合法的首次打包，或者在"dist/ 被手动
清空但版本确实发布过"时误判成未发布而放行覆盖，两个方向都会判错。`v<版本>` 标签由 `-Release`
第 7 步在"打包完成自检"通过之后才创建（见 `build.ps1` 该步骤判断记录：自检失败根本不会走到
打标签这一步），标签一旦存在就代表这个版本号确实产出过一份自检通过、已经对外发布的产物快照
——这是仓库里唯一一处"发布"这件事真正落定的信号，比文件系统状态更可靠。

**校验插入点覆盖三条入口，而不是只在 `-Release` 里加**：`build.ps1` 在两处共享代码块各调用
一次 `Assert-DistVersionNotAlreadyReleased`——"打分发包 dist/<版本>/"一节（`Remove-Item
-Path $DistRoot` 之前）与"打 zip + lock"一节（`Compress-Archive`/`Write-WsGameLockFile`
之前）。之所以两处就能覆盖 `-Release`/`-Dist`/`-Zip` 三条入口的任意组合：`-Release` 在自己
的前置校验一节里无条件把 `$DistRequested` 置为 `$true`，把自己转译成一次等价的 `-Dist` 请求，
落到与 `-Dist` 完全相同的共享代码块；`-Zip` 在参数校验阶段已经要求必须同传 `-Dist`/
`-Dist auto` 或 `-Release`，因此也一定会先经过"打分发包"这一步的校验。校验对象统一用调用方
传入的 `$DistDirVersion`（打包路径实际使用的版本字符串，`-DryRun` 场景下带 `-dryrun` 后缀），
不是记录进 MANIFEST/lock 内容字段的"干净"版本号 `$ResolvedDistVersion`：真实发布从不会给一个
带 `-dryrun` 后缀的字符串打标签，所以 `-Release -DryRun`、`-Dist X.Y.Z-dryrun` 两条 dry-run
路径下 `git tag -l "v<带后缀的字符串>"` 天然查不到匹配，不需要在每个调用点分别记住"这里要放行
dry-run"这条例外。

**例外通道 `-AllowOverwriteDist`，默认关闭**：重跑一次失败的发布（标签还没打成功、产物只是
半成品）是合法需求（见 AGENTS.md §5"半途状态若发布提交已产生但无标签"一节），不能把覆盖完全
锁死。开一个显式开关，传入时跳过本次校验直接放行，但必须打印醒目警告点出正在覆盖哪些文件——
不能悄悄放行，否则例外通道本身又变成一个新的"覆盖不留痕"的口子。默认关闭：这是发布不可变
承诺的强制落地，不应该在日常调用里习惯性带上这个开关绕过校验。

**独立成 `toolchain/_dist_immutability_guard.ps1` 而不是内联写在 `build.ps1` 里**：与同目录
`_hash.ps1`/`_version_writeback.ps1`/`_lock_writeback.ps1`/`_unity_path_length_guard.ps1`
同一模式——只定义函数、无顶层副作用，`toolchain/tests/test_dist_immutability_guard.py` 可以
直接 dot-source 后单独测试，不需要跑完整 `build.ps1`（后者会顺带跑一大批耗时的 dotnet
build/test 步骤）。

**dist 目录校验的位置：排在 `build.ps1` 一切写盘动作之前（缺陷修复 bugfix/test-git-env-leak_20261001）**：
此前它排在 4.1 节之后——被拦截的 `-Dist <已发布版本> -SyncContent` 仍会先 `Set-Content` 重写
`StreamingAssets/GameFoundation/{scene,nav_mesh}/*.json` 四个共享占位文件才走到校验。门禁并行两条线里 Unity 恰好读着其中之一时
（另起进程用 `FileShare.Read` 持有 `scene/sample_field.json` 即可稳定复现）`Set-Content` 抛 `GetContentWriterIOError`，被拦截的调用
连"已发布版本被拒绝"的提示都没打出来（2026-10-01 合并前全量 `test_build_zip_entry_blocked_for_already_released_version` 失败，
由 `test_git_env_isolation` 的嵌套复现多跑一遍同名用例放大）。现在校验紧跟 `$DistDirVersion` 解析之后、`$ContentOnlyMode` 判定之前，
被拦截的调用既不写共享文件也不起 `dotnet build`；`test_dist_guard_runs_before_any_shared_file_write` 以静态顺序断言钉住
（不用动态抢文件锁：抢锁会反过来打断并行门禁线里正常的 `build.ps1`）。同时 `test_git_env_isolation.AFFECTED_TESTS` 对本文件只取
四条临时仓库用例——`test_build_*_entry_blocked_*` 两条直接跑真实仓库的 `build.ps1`、不起任何临时 git 仓库，不可能泄漏，不属于该复现。

**测试覆盖**（`toolchain/tests/test_dist_immutability_guard.py`）：
1. 独立函数逻辑（合成的空 git 仓库，不涉及本仓库真实标签）：版本未发布放行、版本已发布拒绝
   （异常信息含版本号/已发布证据/拒绝原因/正确做法/例外开关名五类关键指引）、
   `-AllowOverwrite` 放行但打印警告、`-dryrun` 后缀版本不会被已发布的干净版本号误伤。
2. `build.ps1` 真实入口接线（真实调用脚本，用 `-SyncContent` 跳过耗时的 `dotnet build`/
   `test`/DLL 同步，让测试在几秒内跑到校验点）：`-Dist <已发布版本>`、
   `-Dist <已发布版本> -Zip` 均被拦截且不产生任何 dist 产物；已发布版本号动态从仓库真实标签
   （`git tag -l "v*"`）里取，不硬编码、不新建标签，本机没有符合格式的标签时跳过。
3. 静态结构校验（纯文本断言，不依赖 PowerShell 解释器）：三处真正写产物的语句
   （`Remove-Item -Path $DistRoot`、两次 `Compress-Archive`、`Write-WsGameLockFile`）在
   `build.ps1` 全文件里各只出现一次且都排在对应校验调用之后，`-Release` 前置校验一节里
   `$DistRequested = $true` 排在这些写入语句之前——用来证明 `-Release` 没有另一条绕开校验
   的独立写入路径，而不是仅凭"信任代码结构"下结论；`-Release` 本身不在本次任务允许的调用
   范围内（不能真的发版验证），只能用这种方式覆盖。

## 定向门禁（`module_map.json` / `gen_module_map.py` / `change_impact.py`，ADR-0126）

目标：日常切片不再"全量或 `-Quick` 二选一"，而是 **改动 → 影响集 → 只跑相关步骤**。设计依据见 [ADR-0126](../architecture/adr/0126-定向门禁四级影响集与切片级只跑下游一层.md)。

| 文件 | 作用 |
| --- | --- |
| `toolchain/module_map.json` | 机器可读模块表：`layers`（层依赖链与测试工程）、`tier_rules`（公开面/文档/共享面规则）、`engine`（引擎侧分类前缀）、`steps`（每个门禁步骤的触发规则）、`modules`（每个子模块：路径前缀、层、测试命名空间、引擎分类、责任人、交互例外） |
| `toolchain/gen_module_map.py` | 从目录结构重新生成 `modules`（保留手填的责任人与交互例外）；`--check` 只读自检：未登记目录、幽灵条目、重名、责任人为空、测试工程缺失、例外文件不存在。门禁步骤"模块表覆盖所有子模块目录"调用它 |
| `toolchain/change_impact.py` | 改动路径 → JSON 判定（级别、命中模块、测试工程、步骤、引擎分类、逐文件理由）。输入：`--base <基线>`（含工作树）、`--staged`、`--commit <sha>`、`--paths-from-stdin`、`--paths`；`--modules a,b` 手动指定模块；`--print-level` 只打印 T0～T3 |
| `toolchain/tests/test_change_impact.py` | 复现用例（T0/T1/T2/T3 各一）+ 不变量用例（级别单调、未知路径为 T3、T3 跑全部步骤、步骤编号与表一一对应、每个引擎侧用例类都带模块分类）+ 模块表自检 |

### 四级影响集

取各文件最高级，**表里查不到的路径一律 T3**。

- **T0**：文档（`**/*.md`、`architecture/**`、`docs/**`、`timing/**`、`.github/**`）。
- **T1**：子模块内部实现 → 编译 + 该层测试工程 + 已登记的交互例外；另见下表中登记为 T1 的路径类别。
- **T2**：契约（`contracts/`、`schema/`、`generated/`、`I*.cs`、`*Events.cs`）→ T1 + 向下游多看一层 + 公开面兼容探针 + 该模块所属分类的引擎侧用例；**层级范围**（层内共享面、层根文件、各层测试工程文件）与适配层运行时/一致性套件也记 T2。
- **T3**：真正跨层/跨工具链的共享面（生产 `*.csproj`、`**/*.sln`、`Directory.Build.props`、`data/**`、`adapters/stub` 生产代码、`check.ps1`、`build.ps1`、`toolchain/**`（`toolchain/tests/**` 与 `gate_floors.json` 除外）、`.gitattributes`）与未知路径 → 全量。

#### 路径规则（`tier_rules.path_rules`，ADR-0126 决定 9、10）

每条规则 = 路径类别 → 级别 + 额外要跑的测试工程/引擎侧分类/步骤；同一路径命中多条规则取并集、级别取最高；`except` 排除的文件（csproj 等）落回共享面 T3；规则命中先于共享面。判级依据是 csproj 的 `Compile Include`/`ProjectReference` 与 asmdef 引用。

| 路径 | 级别 | 要跑的 |
| --- | --- | --- |
| `core/*/common/**`、`core/*/assembly/**`、层根文件（`core/*/*`、`presentation/*`，csproj 除外）、`presentation/common/**`、`presentation/assembly/**` | T2（层级范围） | 本层测试工程 + 下游一层 + ABI 探针 + 本层所有模块的引擎侧分类（加 `module:shared`）+ 同步 DLL |
| `adapters/conformance/**` | T2 | `Tests.Foundation` + 分类 `module:engine_adapter` + 引擎编译 + EditMode（无 ABI、无演练、无下游） |
| `adapters/stub/tests/**` | T1 | `Tests.Foundation`（只有它编译该目录）；桩生产代码仍 T3 |
| `core/*/tests/*.csproj`、`presentation/tests/*.csproj`（各层测试工程文件） | T2（层级范围） | 同第一行，按该测试工程所在层；生产 csproj、`*.sln`、`Directory.Build.props` 仍 T3 |
| `adapters/unity/DiagnosticsForwarding/tests/*.csproj` | T2 | 只跑该测试工程（不在核心层里，无 ABI、无下游）；生产 csproj 仍 T3 |
| `toolchain/tests/**`、`toolchain/gate_floors.json` | T1 | 只跑 `toolchain_pytest`（外加秒级自检步骤；无 dotnet、无引擎侧）；改私服回归用例文件时再带它自己的 `registry_pytest`；`toolchain/` 下其它路径仍 T3 |
| `adapters/unity/Packages/*/Tests/Runtime/**` | T1 | 只跑该文件自己标注的 PlayMode 分类（无标注 → 全部 PlayMode），无 dotnet 步骤、不同步 DLL |
| `adapters/unity/Packages/*/Tests/Editor/**` | T1 | 只跑 EditMode，无 dotnet 步骤 |
| `adapters/unity/Packages/*/Runtime/Ui/**`、`.../Runtime/Shell/**` | T2 | 引擎编译 + 分类 `ui;shell;shared` + 消费方演练 + 同步 DLL |
| `adapters/unity/Packages/*/Runtime/Diagnostics/**` | T2 | 引擎编译 + 分类 `shared` + 演练 + 同步 DLL；`DiagnosticsHub.cs`、`ValidationReportFileOutlet.cs`（及 `Runtime/Presentation/PresentationDiagnosticsConsoleForwarding.cs`）另跑 `Tests.Adapters.Unity.DiagnosticsForwarding` |
| `adapters/unity/Packages/*/Runtime/` 其余（`EngineAdapter`、`Presentation`、`Bootstrap`、包根） | T2 | 引擎编译 + 全部 PlayMode + 演练 + 同步 DLL |
| `adapters/unity/Packages/*/Editor/**` | T2 | 引擎编译 + EditMode + 分类 `shared` + 演练 + 同步 DLL |
| `adapters/unity/DiagnosticsForwarding/**`（csproj 除外） | T1 | `Tests.Adapters.Unity.DiagnosticsForwarding` |
| `games/_template/**` | T1 | 模板数据校验 + 消费方演练（模板冒烟）+ `.meta` 完整性 + 包清单 |
| `assets/**` | T1 | 占位资产生成器一致性 + 样例导入幂等 + 导入检查 |
| `.githooks/**`、`.gitignore` | T1 | 门禁自检 + 钩子相关 pytest（`hooks_pytest`：提交前分级守卫） |
| `core/gameplay/tests/Perf/PerfMachineCalibration.cs` | T1 | `Tests.Gameplay` + `Tests.Sim`（后者用 Link 直接编译它） |

仍按未知路径 T3 的：`adapters/unity/Assets/**`（场景、第三方资源、编辑器脚本）、`adapters/unity/ProjectSettings/**`、`adapters/unity/Packages/` 的清单与包 `package.json`/目录 `.meta`、`adapters/headless`、`.config`、`VERSION`。

### `check.ps1` 参数

| 参数 | 含义 |
| --- | --- |
| `-Changed <基线>` | 与基线（默认 `main`）的 merge-base 至当前工作树的全部改动 |
| `-Staged` | 只看暂存区（提交前钩子用） |
| `-Modules a,b` | 手动指定模块（按 T1 处理） |
| `-DryRun` | 只打印"本次判定"块（含将传给引擎的分类过滤串），不执行任何步骤 |

不带上述参数时行为与此前完全一致。带上后：先打印"本次判定"块；被跳过的步骤标 SKIP 与"T? 未触发"；全文输出落盘到产物目录，控制台只留每步一行、汇总表、首个失败步骤的最后 30 行；`dotnet test` 按命中的测试工程逐个跑，用例数下限改为"失败 0、通过至少 1、跳过不超上限"。

### 判断记录

- **引擎分类用"且"关系的原因**：引擎测试框架的分类过滤与名称过滤同时给出时取交集，交互例外（移动阻挡用例）因此也登记为一个独立分类（`interaction:movement_stop_blocking`）标在用例类上，而不是追加类名。
- **共享测试的细分**：`core/*/assembly/tests/**`、`core/*/common/tests/**`、`presentation/assembly/tests/**`、`presentation/common/tests/**` 只改测试、不改生产代码，按 T1 处理（对应层的测试工程）。关掉它只需清空 `tier_rules.shared_tests_globs`。
- **层级范围记 T2 的原因**：层内共享面与层根文件只被本层与下游通过本层程序集使用，与"切片级只看下游一层"同一取舍，不必为它们全量；生产工程文件/解决方案/全局构建配置改的是所有工程的编译条件，所以留在 T3（规则里 `except` 守住，不变量用例对全仓库的 csproj/sln 逐个核对）；测试工程文件只影响所在层的编译与测试，按该层的层级范围记 T2（不变量用例核对：任何测试工程文件的改动所列测试工程都含它自己）。最终结论（2026-10-01）：装配层按层级范围 T2 判，切片内不为装配入口另跑全量；`AGENTS.md` §4 的全量时刻改为里程碑收口、升级框架/依赖、合并到 `main` 前后各一次，原"改了装配入口/共享数据"的场景由合并前后的全量覆盖。
- **工具链测试与用例数下限记 T1**（2026-10-01 拍板）：只改 pytest 用例或 `gate_floors.json` 时，要验证的就是 `toolchain_pytest` 本身，其余步骤（dotnet、数据校验、引擎侧）与它无关；`toolchain/` 下其它路径（脚本、模块表、判定脚本、探针工程）改动的是门禁自己，仍 T3。`registry_pytest` 的触发路径收窄到私服目录与它自己的用例文件，避免 T1 白跑。
- **引擎侧测试文件的分类取自文件自己的标注**：`Tests/Runtime/**` 下的文件，判定时读它里面的 `[Category("…")]`（`.meta` 取所属源文件）；没有标注的辅助类/程序集定义看不出被哪些用例用，保守跑全部 PlayMode；文件已不存在（提交里删除）不贡献分类。这类改动不同步核心 DLL——前提是 DLL 已与源码同步（`AGENTS.md` §4），否则测的是旧二进制。
- **运行时目录到分类的对应是人工判断**：`Runtime/Ui`、`Runtime/Shell` 给 `ui;shell;shared`，`Runtime/Diagnostics` 给 `shared`，其余目录（`EngineAdapter`、`Presentation`、`Bootstrap`、包根）被的用例面太宽，直接跑全部 PlayMode；登记漏项由里程碑全量兜底。一致性套件规则里静态登记的分类由不变量用例核对（引用 `Adapters.Conformance` 的 PlayMode 文件的分类必须被覆盖）。
- **"所列测试工程必含自己所在的测试工程"不变量扫出过一处漏登记**：`Tests.Sim.csproj` 用 Link 直接编译 `core/gameplay/tests/Perf/PerfMachineCalibration.cs`，该文件改动此前只判 `Tests.Gameplay`；已补规则。不变量用例按 csproj 的编译包含/工程引用逐文件重算期望，不读模块表，今后新增跨目录编译包含而忘了登记会在这里红。
- **基础步骤的 `needs`**：`dotnet_build`/`dotnet_test` 声明 `needs: dotnet_projects`，`abi_probe` 声明 `needs: public_layers`——判定里没有测试工程/没有层级公开面时不选，所以只改引擎侧测试、模板、资产、钩子时没有任何 dotnet 步骤；`crlf_check`、`module_map_check` 这两个秒级步骤仍随 T1 基础步骤运行。
- **步骤触发默认忽略 md 路径**：改文档不触发任何依赖代码的步骤（`trigger_md` 为真的步骤除外）。
- **数值仿真基线比对只在 T2 及以上触发**（2026-10-01 拍板）：此前 `core/**` 任何改动（含 T1 的模块内部）都会触发，约 17 秒、占 T1 总时长的大半，而 T1 不动契约、不动公开面。实现是模块表步骤字段 `min_level: 2`（低于该级别时 `triggers` 不生效）；`core/sim/tests/data/**` 与 `core/sim/tests/baseline/**` 登记在同一步骤的 `always_triggers`（不受 `min_level` 限制）——它们是基线比对自己的输入与基线文件，改了而不比对等于漏检。已知取舍：T1 改仿真所依赖的核心模块内部实现（例如改了战斗结算内部公式却没动契约）不再在切片级比对基线，由 T2 以上与里程碑全量回归兜住。
- **手动 `-Modules`** 一律按 T1，不会升级到 T2。
- **子集用例数下限**：定向模式不检查全量下限（子集不可能达到），只检查失败 0 与通过 ≥ 1。
- **回放**（把每次提交的改动清单喂给 `change_impact.py --commit`；合并提交按第一父提交的差异；"落地前"= `main` 的 `8261822c` 上的规则与判定脚本，"第一版"= 本分支 `69f41f80`，"当前"= 本分支最新规则）：

  1. **消费方反馈修复期**：`v1.80.0..v1.91.0` 区间 `main` 上的全部第一父提交，共 66 个——更接近日常开发（功能/修复/文档/发布交替）。

| 口径 | T0 | T1 | T2 | T3 | T1+T2 占全部 | T1+T2 占非 T0 |
| --- | --- | --- | --- | --- | --- | --- |
| 落地前 | 26 | 3 | 3 | 34 | 9.1%（6/66） | 15.0%（6/40） |
| 第一版（层级范围与适配层规则） | 26 | 4 | 16 | 20 | 30.3%（20/66） | 50.0%（20/40） |
| 当前（再加测试工程文件、工具链测试） | 26 | 4 | 16 | 20 | 30.3%（20/66） | 50.0%（20/40） |

  仍是 T3 的 20 次按原因分组：16 次是发布提交（改 `VERSION`、包 `package.json`、`packages-lock.json`，未登记路径，发布前后本就跑全量）；4 次含桩适配层生产代码（`adapters/stub/*.cs`，被所有测试工程引用，设计上保持 T3）。这个区间里测试工程文件与工具链测试的新规则没有改变任何提交的级别，所以后两行相同。去掉 16 次发布提交后，非 T0 的 24 次里 T1+T2 占 20 次（83.3%）。

  2. **主线最近 30 次提交**（`main` 的 `8261822c` 往前，几乎都是门禁自身的集中开发期，不代表日常开发）：

| 口径 | T0 | T1 | T2 | T3 | T1+T2 占全部 | T1+T2 占非 T0 |
| --- | --- | --- | --- | --- | --- | --- |
| 落地前 | 13 | 0 | 0 | 17 | 0% | 0% |
| 第一版 | 13 | 1 | 2 | 14 | 10.0%（3/30） | 17.6%（3/17） |
| 当前 | 13 | 3 | 4 | 10 | 23.3%（7/30） | 41.2%（7/17） |

  仍是 T3 的 10 次按原因分组：7 次改了门禁脚本或工具链自身（`check.ps1`、`build.ps1`、`toolchain/**` 里的脚本/模块表/判定脚本，其中 1 次同时有桩生产代码）；1 次改 `.gitattributes`；1 次发布提交；1 次改生产工程文件（`Presentation.Common.csproj`）。门禁与构建配置留在 T3 是设计决定（改它们影响所有步骤）。当前规则比第一版多出来的 4 次来自：测试工程文件按层级范围记 T2（2 次）、`toolchain/tests/**` 与 `gate_floors.json` 记 T1（2 次）。
- **引擎侧过滤已实测对账（2026-10-01，路径足够短的工作树 `D:\wt\ai-transformation`，做完 `AGENTS.md` 约定的 DLL 同步之后）**：`module:shared` 单独 46 例（11 个类）；`module:ui` 27 例，`module:ui;module:shared` 合跑 73 = 27 + 46（分号串是并集，各类无重叠）；`interaction:movement_stop_blocking` 单独 11 例、恰好是 `MovementStopAndBlockingPlayModeTests`，`module:unit` 单独也是 11，二者分号合跑仍是 11（有重叠时去重，不是求和）。判定块里的过滤串在深层 scratchpad 工作树里不能跑，要到主检出或短路径工作树执行。

## 门禁耗时自动记录（`_gate_timing.ps1` / `timing_report.py`，AGENTS.md §1c）

`check.ps1` 每次运行结束（通过或失败都写）把每个步骤追加到仓库根 `timing/<年月日>_<分支名去 feature/ bugfix/ 前缀>.jsonl`（main 与游离 HEAD 上改写待领目录 `timing/_pending/<年月日>_main_<时分秒>.jsonl`，见下），一行一条 JSON，字段与 §1c 完全一致：`task`、`branch`、`phase`、`step`、`start`、`end`、`seconds`、`result`、`note`。

| 字段 | 取值 |
| --- | --- |
| `phase` | 定向模式（`-Changed`/`-Staged`/`-Modules`）`定向门禁`，否则 `全量门禁` |
| `step` | 步骤稳定 `-Id`（如 `dotnet_test`、`unity_playmode`）；另有一行 `_total` 记脚本总墙钟 |
| `start`/`end` | 该步真实起止时刻（本地时间，精确到秒） |
| `seconds` | 步骤 Stopwatch 秒数（1 位小数）；被跳过的步骤为 0，`result=SKIP`，`note` 是跳过原因 |
| `task` | `-TimingTask "<一句话>"`，缺省 `check.ps1 <参数串>` |

开关：`-NoTiming` 跳过写入（`.githooks/pre-commit` 与 `build.ps1 -Release` 调用门禁时固定带，否则每次提交/发布都弄脏工作树，发布打包自检还会看到 `-dirty`）。写入失败（只读、取不到分支……）不影响门禁结论，只在汇总末尾打一行警告。

统计：`python toolchain/timing_report.py [--since 2026-10-01] [--branch <名>] [--phase 全量门禁] [--json] [--no-pending]`，按 phase、按 step 输出次数、合计、中位数、P90、最大值，只写 stdout；默认也统计 `timing/_pending/` 下还没被领走的行。

**待领目录**：在 `main`（及游离 HEAD）上运行时，耗时改写 `timing/_pending/<年月日>_main_<时分秒>.jsonl`（`.gitignore` 覆盖，不弄脏主检出）。下一条分支在跑合并前全量之前，在自己的工作树里运行 `python toolchain/claim_pending_records.py [--from <主检出>] [--dry-run]`：把主检出待领文件并入 `timing/<日期>_main.jsonl` 并删除被领走的文件，输出领走几个文件、几行；在 main 上运行会被拒绝。随后连同 `REGRESSION_LOG.md` 的合并后行一起按显式 pathspec 提交（`AGENTS.md` §1b）。

### 判断记录

- **start/end 在步骤运行器里取，不是"整次起点 + 秒数"推算**：`Invoke-CheckStep` 在步骤前后各取一次系统时间，写进结果行（`Id`/`Start`/`End` 三个新字段，汇总表仍只打印原四列）；两条并行线各自在自己的子进程里取，经结果 JSON 带回主进程（`Import-GateLineResults`，从 `check.ps1` 挪进 `_gate_step_runner.ps1`，让测试能 dot-source 整条链路）。因此并行线的步骤时间区间真的会重叠，`timing_report` 的合计是步骤秒数求和（并行下大于墙钟），墙钟看 `_total`。
- **所有步骤调用点都带 `-Id`**：被开关跳过的 `Add-SkippedStep` 此前没有 Id，现补齐；`toolchain/tests/test_gate_timing_log.py` 有静态用例卡住"新增调用点忘了 `-Id`"（忘了会在记录里退化成中文显示名，跨任务汇总对不上）。
- **phase 汇总不含 `_total` 行**：`_total` 与同一次运行里的步骤行重叠，一起加会重复计数；它只作为 `(phase, _total)` 一个 step 单独列出。P90 用线性插值，保证 中位数 ≤ P90 ≤ 最大值。
- **定向模式由干活子进程写**：父进程只透传 `-TimingTask`/`-NoTiming`，所以定向运行的 `_total` 不含父进程的判定（`change_impact.py`）与子进程启动时间。
- **Windows PowerShell 5.1 的两个坑（实测）**：`@($List[Object])` 抛 "Argument types do not match"（门禁的 `$script:Results` 就是这个类型），所以写入函数直接 `foreach`；`Sort-Object` 不保证稳定，按 start 排序改用 `[Array]::Sort` 配序数比较。
- **main 与游离 HEAD 写待领目录，不写已跟踪文件**：在 main 上跑门禁（合并后全量）若追加到已入库的 `timing/<日期>_main.jsonl`，主检出就变脏，挡住 `build.ps1 -Release`（要求干净工作树）和下一次 `git merge --ff-only`。所以 `Get-GateTimingFilePath` 在分支名为 `main` 或 `detached` 时返回 `timing/_pending/<年月日>_main_<时分秒>.jsonl`（游离 HEAD 为 `_detached_`；一次运行一个文件，文件名带时分秒），该目录在 `.gitignore` 里；判定与 `version_label.py` 同口径（`Test-GateTimingUsesPending`，测试里有两边一致性用例）。feature/bugfix/release 分支保持原行为，`-NoTiming` 语义不变。
- **领取脚本 `claim_pending_records.py` 的取舍**：在分支工作树里运行，`--from` 缺省取 `git worktree list` 里检出 main 的工作树；并入规则是"同一天的待领文件按时分秒顺序追加到 `timing/<日期>_main.jsonl`、丢弃逐字节重复行"（包括与目标文件已有行重复的行，所以"写入成功但删除失败"后重跑不会重复）；先写完目标文件再删待领文件；只删主检出 `timing/_pending/` 下未被 git 跟踪的普通文件（符号链接、已跟踪文件、文件名不合规的一律不动、打印警告）；在 main、游离 HEAD、与 `--from` 同一目录、与 `--from` 不同仓库的情形拒绝（退出码 1）；不 `git add`、不提交。领走即删除，所以重复运行第二次领 0 个文件。`REGRESSION_LOG.md` 的合并后行仍由人写（不自动化）。`timing_report.py` 默认把待领目录也算进统计（`--no-pending` 排除），领取后两处不会重复计数。
- **脚本被未接住的异常中断时也写记录**（NF 清扫新增）：`check.ps1` 顶层 `trap` 调 `Write-GateTimingOnAbort`（`_gate_timing.ps1`）——已完成的步骤行照写，`_total` 行 `result=FAIL`、`note` 以 `aborted:` 开头带异常消息，墙钟取到中断时刻；`$script:GateTimingWritten` 标记保证同一次运行只写一次（正常收尾先置位，之后再触发就什么都不追加）。测试：`test_gate_timing_log.py` 的 `test_abort_*` 把 `check.ps1` 里真实的 `trap` 块原样抽出来，在一个"跑完两步后抛未接住异常"的脚本里执行，读回 jsonl 断言步骤行、`_total` 的 `FAIL`/`aborted:`/墙钟，并断言只写一次。
- **进程被强行结束时不写记录，是物理限制，不处理**：任务管理器结束进程、`Stop-Process`、断电时脚本内任何代码都来不及运行；不另起外部看门进程去补写（那只会再造一份要维护的机制，而这种运行本来就没有可信的结论）。
- **游离 HEAD 的待领文件也并入 `<日期>_main.jsonl`**（行里的 `branch` 字段仍是 `detached`，据此可区分）：游离 HEAD 同样不能写已跟踪文件（理由同 main），没有另设一份"detached 专用文件"的必要——统计脚本按 `branch` 字段过滤即可。
- **同一天同一分支多次运行追加到同一文件，靠 `task`/`start` 区分**：文件名不带时分秒是为了让"一个分支一天一个文件"便于人看、便于入库；每行自带起止时间，不会混淆。
- **待领文件只存在于主检出本机**：它是门禁自动产生的耗时原始数据，下一条分支合并前领走入库；换机器或删了主检出目录丢的只是尚未领走的耗时行，不影响任何门禁结论，与此前"主检出里一份未跟踪文件"同等。为这点数据引入跨机同步，成本远大于价值。
- **`release/X.Y.x` 等其它分支上的记录仍写已跟踪的 `timing/<日期>_<分支>.jsonl`**：那些分支上没有"禁止直接提交"约束，也不挡发布，维持原行为最简单。

## 版本标签与分支名规范（`version_label.py`，ADR-0127）

`python toolchain/version_label.py` 按当前分支推导版本标签并打印；`--check-branch-name` 检查分支名规范（不合规退出码 1，输出以 `FAIL` 开头）。`--branch`、`--version`、`--repo-root` 可显式指定，供测试与演示。

| 当前分支 | 版本标签 |
| --- | --- |
| `feature/<名>`、`bugfix/<名>` | `<VERSION>_<名>`，例 `1.92.0_gate-timing-autolog_20261001` |
| `main` | `<VERSION>_release`（发布后 `VERSION` 升级，标签随之变化） |
| 其它（`release/X.Y.x` 等） | `<VERSION>_<分支名，/ 换成 ->`，例 `1.92.0_release-1.90.x` |
| 游离 HEAD | `<VERSION>_detached-<短提交号>` |

落点：`check.ps1` 开头与汇总末尾各打印一行"版本标签：…"，并新增门禁步骤"分支名规范"（`feature/`、`bugfix/` 前缀的分支必须形如 `<前缀>/<小写英文数字连字符>_<八位有效年月日>`，其它分支不判定）；`check.ps1` 与 `build.ps1` 把标签放进环境变量 `WsGameVersionLabel`，`Directory.Build.props` 据此设置 `InformationalVersion`（未设置时回退为 `VERSION` 内容，AssemblyVersion/FileVersion 不动）；`build.ps1 -Release` 的发布说明首行写 `<新版本>_release`（构造函数 `_release_notes.ps1`），本次发布构建的信息版本同样是它。

### 判断记录

- **标签不进 `VERSION`/包版本/发布标签/发布包名/变更日志标题/ABI 基线**：包管理器只接受语义化版本，下划线后缀会让发布失败；改成 `-xxx` 预发布后缀又会让版本排序低于正式版。用户规定的格式因此只做成"自动推导的展示标识"，合并时 `VERSION` 也不会冲突（决定理由见 ADR-0127）。
- **没有新增运行期公开接口**：仓库里没有对外暴露框架版本号的公开成员（现有"版本"成员都是存档/数据表版本），ABI 只新增原则下不为此加接口，只写程序集信息版本。
- **`IncludeSourceRevisionInInformationalVersion` 关闭**：避免 SDK 在标签后再追加提交哈希，使读出的产品版本就是标签本身。
- **分支名规范只判 `feature/`、`bugfix/`**：主干上的合并后全量门禁、发布维护分支与游离 HEAD 不应被它挡住；不合规名字的标签仍照推导，失败信息单独给出（非 ASCII 名字会同时给出转义形式，避免控制台代码页不一致时看不清）。
- **`build.ps1 -Release -DryRun` 不写发布说明文件（定案）**：发布说明与版本号提交同在非 DryRun 的第 6 步生成，DryRun 的定义就是"不碰源码、不提交、不留发布产物"；首行格式（`<版本>_release`）由 `_release_notes.ps1` 的用例直接对 `New-ReleaseNotesText` 覆盖，不需要靠 DryRun 再落一份文件来验证。
- **标签经环境变量传给构建，非 ASCII 分支名在非 UTF-8 代码页下可能被改写——不处理（定案）**：这种分支名违反 §1b"名称只用英文"，会先被"分支名规范"步骤判失败（失败信息给出转义形式），走不到发布，改写发生在一个本来就被拦下的输入上。
- **直接在命令行手工构建（不经 `check.ps1`/`build.ps1`）时信息版本是 `VERSION` 内容而非带后缀的标签（定案）**：标签是"由入口脚本按当前分支推导并经环境变量传入"的展示标识，手工构建没有这个入口环境，就不凭空造标签；`VERSION` 本身是语义化版本，作为信息版本是正确的兜底，不是缺陷。

## 包管理器子进程消失的失败现场抓取与演练工作目录隔离（`_upm_evidence.ps1`，2026-10-01）

门禁里的 Unity 批处理偶发几秒内退出，引擎日志含 `IPCStream (Upm-xxxxx): IPC stream failed to read (Not connected)` 与 `[Package Manager] Failed to resolve packages: operation cancelled.`：包管理器子进程（`UnityPackageManager.exe`）在解析途中消失（中途结束该进程可复现同一签名）。真正的触发者没查到，因为 `%LOCALAPPDATA%\Unity\Editor\upm.log` 与子进程退出码会被下一次运行覆盖。本节的做法是"下次再出现时当场留证"，不是猜根因。

- **抓取**：`_gate_line_unity.ps1` 的编译检查/EditMode/PlayMode/独立版构建/IL2CPP 构建与 `consumer_smoke.ps1` 的首次编译/场景构建器/PlayMode/独立版构建/registry 探针，在引擎退出码非零时调 `Get-UpmEvidenceDetailSuffix`：日志含签名才抓。抓到后在 `<ArtifactsPath>\upm_evidence\<时间>_<步骤>\` 存 `upm_candidate<N>.log`（`upm.log` 的每个存在的候选路径各一份）、`engine_log_excerpt.txt`（包管理器相关行 + 日志末尾 60 行）、`unity_processes.txt`（Unity 相关进程快照，用来对照"是不是别的会话误杀"）、`summary.txt`；步骤 Detail 追加 `包管理器子进程退出码=…（-1/1 疑似外部结束，101 疑似自身崩溃）；现场已存 <路径>`。演练步骤的子进程输出被 `| Out-Null` 吞掉，所以 Unity 线在演练失败时从 `consumer_smoke.log` 里把这些摘要行并进 `consumer_drill` 的 Detail。
- **判定函数可单测**：`Test-UpmIpcFailureSignature`（退出码非零且日志含签名）、`Get-UpmChildExitCodes`、`Save-UpmFailureEvidence`；用例见 `tests/test_upm_evidence.py`。
- **演练工作目录**：默认 `<TEMP>\gf_consumer_smoke_<仓库根路径 SHA-256 前 8 位>`（`Get-ConsumerSmokeDefaultWorkDir`，在 `_unity_smoke_wait_scope_guard.ps1`），主检出与各工作树互不清空对方的工程；显式 `-WorkDir` 优先。
- **注册表测试的清理**：`tests/_pid_identity.py` 按"映像名 + 启动时间"核对后才结束进程；`start_registry.ps1 -LogDir` 让测试把 verdaccio 日志写进临时目录。

### 判断记录

- **只在引擎退出码非零时抓取**：实测（消费方演练里结束 `UnityPackageManager.exe`）Unity 会重新拉起包管理器（日志 `Server process restart attempt #2`）并正常完成，此时日志里同样有 `IPC stream failed to read`，但引擎退出码为 0、步骤通过——这类不是失败，不留证。
- **退出码格式**：日志行是 ``[Package Manager] Server process stopped with exit code `4294967295` ``（数字外有反引号，按无符号 32 位打印；`Stop-Process` 结束的 -1 即 4294967295），脚本折回有符号值；包管理器可能被拉起多次，每次一行，摘要按顺序全列。
- **抓取不参与步骤判定**：只产生 Detail 后缀字符串，内部全部 try/catch，任何失败都只让摘要多一句"现场抓取部分失败"；`tests/test_upm_evidence.py` 里有静态用例保证 `$upmNote` 只出现在 Detail 里。
- **建留证目录用 `[System.IO.Directory]::CreateDirectory`**：`New-Item -ItemType Directory -Force` 在父路径是同名文件时既不报错也建不出目录（实测），会让后面的写文件连环失败。
- **超时被强杀（`TimedOut`）的路径同样抓取**（NF 清扫新增）：`consumer_smoke.ps1` 里首次编译/场景构建器/PlayMode/独立版构建/registry 探针五个 Editor 步骤的超时分支，也按同一签名调 `Get-UpmEvidenceDetailSuffix`（强杀的退出码是 -1，非零，走与普通非零退出同一口径：日志含 `IPC stream failed to read` 才留证，否则不留证、不改 Detail）。测试：`test_upm_evidence.py` 的 `test_timeout_kill_exit_code_still_captures_when_signature_present`（退出码 -1 + 含签名 → 留证目录有 `upm_candidate1.log`/`summary.txt` 且摘要含 `-1,-1`；不含签名 → 不建目录、摘要为空）与 `test_consumer_smoke_editor_timeout_branches_capture_evidence`（五个步骤的超时分支各接了留证）。`_gate_line_unity.ps1` 的 Editor 步骤没有超时机制，只有独立版冒烟有，见下一条。
- **只覆盖 Windows 上 Unity 6 的 `%LOCALAPPDATA%\Unity\Editor\upm.log`（定案）**：本工具链的 Unity 批处理入口（`check.ps1`/`consumer_smoke.ps1`）本身只支持 Windows（进程快照查 `Win32_Process`、引擎路径解析都是 Windows 的），别的平台根本走不到这里；候选路径函数 `Get-UpmLogCandidatePaths` 是列表，将来真支持别的平台时在这里加一项即可，不为没有运行环境、无法验证的路径预写代码。
- **`upm.log` 可能已被另一个 Unity 实例覆盖（定案）**：抓取发生在引擎进程退出之后，这是"事后抓"的固有性质；`unity_processes.txt` 与 `summary.txt` 里的引擎日志片段（取自本次引擎日志，不会被覆盖）就是为此留的补充对证手段，摘要里也只把 `upm.log` 当旁证。
- **只覆盖 Editor 批处理步骤，独立版冒烟（Player）不经包管理器（定案）**：Player 运行时没有包管理器进程，签名不会出现，不需要抓。
- **进程快照拿不到时在文件里写失败原因、不重试（定案）**：`Get-UnityProcessSnapshotText` 查询 `Win32_Process`，查不到只影响"是不是别的会话误杀"这条旁证；重试会拖慢失败路径，且失败原因（权限、WMI 不可用）重试通常不会变。
- **PID 复用无法在测试里可控地制造**：`test_pid_identity_kill.py` 用"同一 PID、身份不符"等价构造覆盖判定本身。

## 测试里起 git 子进程的环境隔离（`toolchain/tests/_git_env.py`，2026-10-01 事故）

**事故**：2026-10-01 19:57 在工作树里提交，预提交钩子判 T1 跑了完整 `toolchain_pytest`，之后共享的主检出 `.git/config` 被写进
`core.bare=true`、`user.email=t@example.invalid`、`user.name=t`、`commit.gpgsign=false`，所有工作树的 git 命令都报
`fatal: this operation must be run in a work tree`（用户手工修复）。

**根因（已在一次性沙箱仓库上用模拟钩子环境复现，不是猜测）**：git 运行钩子时给钩子进程注入 `GIT_DIR`（链接工作树时为
`<主检出>/.git/worktrees/<名>`）、`GIT_INDEX_FILE`、`GIT_PREFIX`，以及随 `git -c` 带下来的 `GIT_CONFIG_PARAMETERS`、
`GIT_AUTHOR_*`；钩子再起的 pytest 继承了它们。测试在临时目录里 `git init`/`config`/`add`/`commit`/`tag` 时没有剥掉这些变量，命令实际作用到
`GIT_DIR` 指的那个仓库：`git init` 在 `GIT_DIR` 已设而 `GIT_WORK_TREE` 未设时当裸库处理（写 `core.bare=true`），随后
`git config` 写进共享 `.git/config`（链接工作树的 `git config` 默认写共享文件），`git add` 写真实暂存区，`git commit`/`git tag` 写真实分支与标签。
沙箱实测被改的有：`config`、工作树暂存区、`refs/heads/<分支>`、`refs/tags/v1.0.0`、`refs/tags/v1.2.3`。

**确认会泄漏的用例（修复前，钩子环境下逐文件实测）**：

| 文件 | 用例 | 泄漏方式 |
| --- | --- | --- |
| `test_version_label.py` | 所有用到 `tmp_repo` 夹具的用例（`test_repro_cli_real_repo_main_feature_bugfix_release_detached`、`test_repro_cli_noncompliant_feature_branch_fails` 等） | `git init -b main`、`git config user.email/user.name/commit.gpgsign`、`add`、`commit`、`checkout -b` 全作用到真实仓库，直接复现事故的三个配置值与 `core.bare=true` |
| `test_check_unity_meta.py` | 全部用到 `_init_repo` 的 11 条（`test_baseline_layout_has_no_issues` 起至 `test_cli_exit_code_reflects_result`） | `git init`/`config`/`add -A` 写真实配置与暂存区 |
| `test_dist_immutability_guard.py` | `test_not_released_version_passes`、`test_released_version_blocked_with_guidance`、`test_allow_overwrite_bypasses_with_warning`、`test_dryrun_suffixed_version_not_confused_with_released_tag` | `init`、空提交、`git tag v<版本>`：真实仓库多出标签 `v1.0.0`/`v1.2.3` 与一次提交 |
| `test_gate_step_runner.py` | `test_git_grep_banned_codename_no_hit_on_clean_repo`、`..._detects_tracked_file`、`..._ignores_untracked_file` | `init`、`config`、`add -A` |
| `test_change_impact.py` | `test_check_dryrun_prints_playmode_category_filter`、`test_module_map_check_flags_uncovered_path_category_and_ghost_rule` | `init`、`config`、`add -A`、`commit` |

`test_gate_timing_log.py`、`test_release_regression_guard.py` 修复前已经自带 `git_env()`（剥 `GIT_*`），不泄漏，现统一改走共享辅助函数。

**三道防线**：① `conftest.py` 会话开始（模块导入时，早于收集）移除 `os.environ` 里全部 `GIT_*`，所有子进程（含 PowerShell 脚本里再起的 git）继承干净环境，
确有用例需要这些变量必须自己显式设置；② `_git_env.py` 的 `git_env`/`run_git`/`init_temp_repo` 是测试起 git 子进程的唯一入口——显式干净环境 +
`GIT_CONFIG_NOSYSTEM=1` + 指向空文件的 `GIT_CONFIG_GLOBAL`，身份写进临时仓库自己的配置，`init_temp_repo` 建完先核对 git 目录确实在目标目录下才写配置；
③ conftest 的不变量守卫：会话开始记真实仓库共享配置（`git rev-parse --git-common-dir` 下的 `config`）的 SHA-256 与全文，会话结束再比，不一致就让整个会话失败并打印差异（只报告不还原）。

用例见 `tests/test_git_env_isolation.py`：复现用例在模拟钩子环境（环境变量指向一次性沙箱仓库）里把上表的用例当子 pytest 跑并断言沙箱一个字节没变（参数化
"conftest + 辅助函数"与"仅辅助函数（`--noconftest`）"两支），另有朴素 git 调用用例（证明防线①独立生效）与守卫用例（改了配置会话必败、打印差异、不还原；没改不吭声）；
不变量用例覆盖"任意变量集合下 `clean_git_env` 不留 `GIT_*` 且不动其它变量"、全局/系统配置隔离、环境里带着钩子变量时辅助函数只作用于目标仓库、落点校验。

### 判断记录

- **剥掉全部 `GIT_*` 而不是一份定位变量清单**：清单要随 git 版本补全（`GIT_DIR`、`GIT_WORK_TREE`、`GIT_INDEX_FILE`、`GIT_OBJECT_DIRECTORY`、
  `GIT_ALTERNATE_OBJECT_DIRECTORIES`、`GIT_COMMON_DIR`、`GIT_PREFIX`、`GIT_CEILING_DIRECTORIES`、`GIT_NAMESPACE`、`GIT_CONFIG*`、`GIT_AUTHOR_*`、
  `GIT_COMMITTER_*`……），漏一个就是下一次事故；测试不依赖任何 `GIT_*`，整个前缀剥掉最稳。`GIT_CONFIG_PARAMETERS`（`git -c` 的载体）与 `GIT_AUTHOR_*` 也在其中，
  只剥定位变量会漏掉它们。
- **守卫只比 `config`，不比暂存区/HEAD/refs**：钩子运行期间 git 自己会合法地刷新暂存区，比暂存区会误报；共享 `config` 正常情况下整个测试会话不会变，且是这次事故的
  破坏面。暂存区/分支/标签被写属于同一根因，由防线①②挡住，不另设守卫。
- **守卫不自动还原**：还原要先弄清被改的是什么、是谁改的；自动还原可能把别人正当写入的内容一并抹掉。
- **`clean_powershell_env` 改为总是返回字典**：pwsh 目标以前返回 `None`（继承），现在要在继承的基础上剥 `GIT_*`，所以返回 `clean_git_env()`；没有调用方依赖 `None`（全仓库
  28 处调用都直接传给 `env=`）。`_ps_harness.run_ps_script` 也剥。
- **`init_temp_repo` 的落点校验是兜底，不是主防线**：万一有人绕开环境清理，`git init` 本身已经改过目标仓库的 `core.bare`，校验只拦住随后的 `config` 写入；真正的防线是
  环境清理（防线①②）。
- **守卫只比"分支/远端登记簿"之外的配置（NF 清扫新增）**：别的会话在 pytest 运行期间正当执行 `git branch --set-upstream-to`、`git push -u`、`git remote add`，只会改共享 `config` 里的 `[branch …]`/`[remote …]` 小节，原先会让整个测试会话误报失败。现在 `_git_env.py` 的 `normalize_config_bytes` 在比较前去掉这两类小节（含其下的键行）；其余小节（`core`、`user`、`commit`、`extensions`、`include`……）的任何改动照常报告，也不会因为同一次变化里夹带了登记簿写入而被放过。2026-10-01 事故污染的 `core.bare`/`user.*`/`commit.gpgsign` 都不在被忽略之列。测试：`test_git_env_isolation.py` 的 `test_invariant_config_fingerprint_ignores_only_branch_and_remote_sections`（登记簿写入不报；夹带 `core.bare`/`[user]`/`[commit]`/`[extensions]` 任一仍报，且报告里不出现被忽略的小节行）。`git worktree add` 本身不写共享配置（只在启用 `extensions.worktreeConfig` 时才会），不需要另行处理。
- **守卫只在真实仓库的 `git rev-parse --git-common-dir` 可解析时启用（定案）**：发布 zip 解出的目录不是 git 仓库，没有"真实仓库共享配置"可守，静默跳过是对的。
- **`GIT_CONFIG_GLOBAL` 需要 git 2.32 及以上（定案）**：工具链别处已经要求 git 2.31 以上（`prune_dist.ps1`/`_abi_baseline_resolve.ps1` 用 `--path-format=absolute`），再抬到 2.32 不是新增负担；更老的 git 还读得到全局配置，但全局配置只影响"不依赖它"以外的假设，防线①②对 `GIT_*` 与身份的隔离不依赖它。
- **防线只覆盖 `toolchain/tests` 下的 pytest 会话（定案）**：事故的根因是"测试里起 git 子进程继承钩子注入的 `GIT_*`"。仓库里没有任何 C# 代码起子进程（全仓 `.cs` 里没有 `Process.Start`/`ProcessStartInfo`），dotnet/Unity 测试不存在这条路径；`check.ps1`、`build.ps1` 自己调 git 本来就是要作用到真实仓库（钩子注入的 `GIT_*` 指向的正是这个仓库），隔离它们反而是错的。
- **没有 conftest 的运行方式（`--noconftest`、把单个测试文件拷到别处跑）下，被测脚本自己起的只读 git 仍会继承调用方的 `GIT_*`（定案）**：只读，不写；测试自己起的写操作仍走辅助函数，不受影响。
- **`gate_floors.json` 的 pytest `min_passed` 已随用例数增长按既有规则抬高（NF 清扫）**：四个套件的下限都按"最近一次全量实测 passed（full-20261001-33：dotnet 9056、pytest 1310、EditMode 188、PlayMode 452）× 0.9 向下取整到十位"重登记（8150 / 1170 / 160 / 400），`measured_*` 同步；该登记的取值规则有 `test_gate_floors_logic.py` 校验。

## `dist/` 瘦身与 ABI 基线回落（`prune_dist.ps1`、`_abi_baseline_resolve.ps1`，2026-10-02）

背景：`dist/` 是 `.gitignore` 排除的本机构建缓存，此前只增不减，主检出累积到约 12.4 GB（433 个条目）；并行会话在链接工作树里跑门禁时，
`abi_probe.ps1` 只看 `<工作树>\dist` 找不到 ABI 基线，有人于是把整个 `dist` 复制进每个工作树（每份约 12.5 GB）。

**入口**

- `pwsh toolchain\prune_dist.ps1 [-RepoRoot <仓库根>] [-KeepVersions <n>] [-Apply]`：不传 `-Apply` 只打印清单（分类汇总、待删合计、保留清单、未识别清单、删除清单），什么都不删；
  传 `-Apply` 才删。`-KeepVersions` 默认 2（含当前版本，不小于 1）；`-RepoRoot` 默认脚本所在仓库根，工作树里没有 `dist\`，清理主检出时指向 `D:\workespace\ws-game`。
  退出码：0 正常；1 拒绝（重解析点、`VERSION` 无效、删除失败等）。
- `build.ps1 -Release`（非 `-DryRun`）在门禁、打包、打标签与可选的私服/GitHub 发布全部成功之后，用子进程调用 `prune_dist.ps1 -Apply`（发布流水线第 9 步）。
- `abi_probe.ps1` 默认基线解析（不传 `-BaselineZip` 时）：先找 `<仓库根>\dist\ws-game-<基线版本>.zip`，没有且本目录是链接工作树时回落到主检出的 `dist\`
  （实现是 `_abi_baseline_resolve.ps1` 的 `Resolve-AbiBaselineZip`/`Get-MainWorktreeRoot`），回落命中时输出标"（来自主工作树）"。

**保留规则**（`prune_dist.ps1` 文件头为准，按序判定；只在 `<RepoRoot>\dist` 这一层、按下列形态操作：目录 `<ver>\`，文件 `ws-game-<ver>.zip`、
`ws-game-<ver>-samples.zip`、`ws-game-<ver>.lock`、`release-notes-<ver>.txt`、`release-<ver>.state.json`（发布续跑的门禁通过记录），以及它们带 `-dryrun` 后缀的形态）：

1. 保留版本 = `VERSION` 里的当前版本 + `dist/` 里按语义化版本排序紧邻其下的正式版本，合计 `-KeepVersions` 个；保留版本的 `<ver>\`、主 zip、samples zip 全留。
   "正式版本"指有非 dryrun 的 `<ver>\` 目录或 `ws-game-<ver>.zip` 的版本。
2. 所有版本的 `.lock`、`release-notes-*.txt` 与 `release-*.state.json` 一律保留（体积很小，是发布记录）。
3. 所有带 `-dryrun` 的目录与文件一律删除（含当前版本的，门禁每次会重打）。
4. 非保留版本的 `<ver>\`、主 zip、samples zip 删除，但有两类例外一并保留：ABI 基线版本（`toolchain/abi_probe_baseline.txt`，当前 1.12.0）的**主 zip**（只留 zip，不留目录与
   samples 包），以及版本号高于当前 `VERSION` 的条目。
5. 认不出形态的条目不动，输出里单列"未识别，已保留"。

**判断记录**

1. **宁少删勿多删**：保留规则在任务书基础上额外保留 ABI 基线 zip 与高于当前版本的条目。前者因为 `build.ps1 -Release` 固定带 `-AbiStrict`，基线 zip 缺失会让发布 FAIL，
   `test_real_baseline_end_to_end_via_abi_probe` 在基线缺失时 skip、而门禁的 pytest skip 上限为 0；后者覆盖发布中途残留与维护分支上 `VERSION` 低于 `main` 的场景。
2. **历史产物可重建、`.lock` 只作记录**：消费方走私服或 GitHub Release，不读本机 `dist/`；不可变发布守卫判定"已发布"靠 `git tag`，不靠 `dist/` 下有没有文件，
   所以删历史产物既不影响消费方也不会让守卫误放行。历史版本要时检出对应标签、运行 `build.ps1 -Dist <ver>` 重建 `dist/<ver>/` 目录：该版本的 `v<ver>` 标签已存在，发布不可变守卫会拒绝，所以**哪怕不加 `-Zip` 也必须带 `-AllowOverwriteDist`**（该开关只是跳过守卫并打印将被覆盖的产物清单，本身不删不改任何东西）；`-Zip` 是另一回事——它才会重写 `dist/ws-game-<ver>.zip`、`.lock`、`-samples.zip`，覆盖本机上同名的原件（ABI 基线版本的 zip 是 `abi_probe` 的输入，不要对它加 `-Zip`），只为查阅历史内容时不需要加；
   重建出的 zip 的 sha 不保证与当年 `.lock` 一致（打包时间戳、压缩实现不保证逐字节相同），所以 `.lock` 不能用来校验重建产物。
3. **安全**：`dist` 本身是重解析点（junction/symlink）、待删条目自身或其内部任何一层含重解析点，一律拒绝并整次不删任何东西；每个待删条目解析后的完整路径必须仍在 `<RepoRoot>\dist\` 之下；
   待删条目有无法枚举的内容同样拒绝。大小在删除前实测并求和，"已释放"按删除前实测字节数计。
4. **自动瘦身不影响发布结果**：发布流水线里的调用整体包在 try/catch，失败（含 `prune_dist.ps1` 非零退出码）只警告，不改变已打好标签的发布与脚本退出码（脚本末尾固定 `exit 0`）；
   用子进程 `powershell` 调用，脚本内部的 `exit 1`/未捕获异常只影响子进程。`-DryRun` 不走到这一步。
5. **基线回落**：主检出根取自 `git rev-parse --path-format=absolute --git-common-dir` 的父目录；`--git-dir` 与 `--git-common-dir` 相同说明本目录就是主检出（或普通克隆），不回落。
   求值时临时摘掉 `GIT_DIR`/`GIT_WORK_TREE`/`GIT_INDEX_FILE`/`GIT_COMMON_DIR`/`GIT_PREFIX`（预提交钩子里会继承到，使 git 的答案指向钩子的仓库），并用 `Env:` 驱动器删除而不是
   `SetEnvironmentVariable(…, $null)`（PowerShell 7 下后者把变量置成空串，空的 `GIT_DIR` 会让 git 报 not a git repository）；任何一步失败返回 `$null`，调用方按"本工作树没有基线"的既有路径处理，
   不新增失败模式。显式 `-BaselineZip` 不经过回落；两处都没有时行为不变（`-SkipIfBaselineMissing` 为 SKIP、退出码 3，`-AbiStrict` 为 FAIL）。`check.ps1` 的 ABI 步骤不判断基线是否存在，直接调探针，无需改动。
   pytest 里读真实 dist 产物的用例（`test_get_framework_path_boundary`、`test_lock_writeback_repair_parity`、`test_real_baseline_end_to_end_via_abi_probe`）改走
   `_latest_dist.resolve_dist_dir`/`locate_dist_file`，口径相同，链接工作树里不再因 `dist/` 缺失而 skip。
6. **测试**：`tests/test_prune_dist.py`（先列清单后执行、`-KeepVersions` 与基线 zip 保留、重解析点拒绝且不删任何东西）、`tests/test_abi_baseline_fallback.py`
   （真实 git 仓库 + 真实链接工作树：回落命中、本地优先、两处都没有、主检出不回落、Python 侧同口径辅助函数）。
7. **打标签失败残留的 `tag-message-<ver>.txt` 由瘦身脚本识别并清理（NF 清扫新增）**：`build.ps1 -Release` 打标签前写 `dist\tag-message-<ver>.txt`、打成功后立刻删，留下来说明打标签失败过。`prune_dist.ps1` 现在认这个形态：标签 `v<ver>` 已存在则删（重试发布会整份重写它，残留早已无用），标签不存在则按"未识别，已保留"处理（可能是眼下失败的发布现场，交人判断）。取不到 git 或不是仓库一律当作"标签不存在"，宁可多留。测试：`test_prune_dist.py::test_tag_message_leftover_deleted_only_when_tag_exists`（真实临时 git 仓库：有标签的版本集合里的残留被删、无标签的留下，不传 `-Apply` 不删）。除此之外仍认不出的条目照旧"未识别，已保留"（脚本不替人决定删陌生文件）。
8. **基线回落只对 `git worktree add` 建出的链接工作树有效（定案）**：独立克隆没有"主工作树"，也就没有可回落的 `dist`，缺基线仍按原有 SKIP/FAIL 处理；为独立克隆编一个回落来源（例如去网络下载基线 zip）会引入网络依赖和第二份"基线真相"，与"基线 zip 由本机发布产生"相悖。
9. **"当前版本"取自 `VERSION` 文件，高于当前版本的条目一律不动（定案）**：在 `VERSION` 低于 `dist/` 中某些版本的维护分支上运行时，那些更高版本的条目属于发布中途残留或 main 上的新版本，宁少删勿多删，不当作历史产物删除。

## 发布续跑（`build.ps1 -Release <版本> -Resume`、`_release_resume.ps1`，2026-10-04）

背景：1.96.1 发布三次失败——全量门禁（`-Release` 第 5 步，约 50 分钟）已通过，第 7 步打包阶段先后因 `dotnet test` 偶发红、docfx 崩溃而失败；
当时唯一的恢复路径是 `git reset --soft <发布前提交>`、手工还原五个版本文件、整条 `-Release` 重跑，全量门禁白跑一遍。

**入口**：`powershell -File build.ps1 -Release <ver> -Resume [同样的 -PublishRegistry/-Publish/-SkipManual]`。第 6 步产生发布提交之后任一阶段失败，
脚本末尾（无论 `throw` 还是 `exit`）打印这条命令；`git reset --soft` 只保留为"放弃本次发布"的回退路径。

**门禁通过记录（状态文件）**：第 5 步通过时写 `dist/release-<ver>.state.json`（被 `.gitignore` 覆盖；`prune_dist.ps1` 当发布记录保留），
字段 `schema`、`version`、`previousVersion`、`parentCommit`（门禁测过的提交）、`releaseCommit`（第 6 步写入）、`gateConclusion`（取自 check.ps1 的"门禁通过"行）、
`createdAt`/`updatedAt`（带时区偏移的 ISO 8601 字符串）、`stages`（`gate`、`commit`、`packaging`、`selfCheck`、`tag`、`registry:<包名>`×4、`push`、`githubRelease`，
各含 `done`/`at`/`detail`）。每个阶段完成时写一笔完成标记；进入第 5 步前先作废同版本的旧状态文件，门禁失败不会留着上一次尝试的凭据。

**前置校验（六项缺一不可，任一不满足即拒绝并打印原因与下一步，拒绝时不改任何东西）**：状态文件在且 `version` 等于 `<ver>`；`HEAD` 等于记录的发布提交；
发布提交的父提交等于记录的 `parentCommit`；`git diff --name-only HEAD~1 HEAD` 只含第 6 步 `git add` 的五个版本文件；工作树干净；本地 `VERSION` 等于 `<ver>`。
拒绝码：`NoState`、`StateUnreadable`、`VersionMismatch`、`NoReleaseCommit`、`HeadMismatch`、`ParentMismatch`、`CommitFiles`、`DirtyTree`、`LocalVersionMismatch`（多项同时不满足时全部列出）。
`-Resume` 不能与 `-DryRun`、`-AllowOverwriteDist` 同传，且必须同传 `-Release`。

**续跑行为**：跳过第 1～6 步，从第一个未完成的阶段起按序执行；必需阶段 = `gate`/`commit`/`packaging`/`selfCheck`/`tag` 恒必需，`registry:*` 仅传 `-PublishRegistry`，
`push`/`githubRelease` 仅传 `-Publish`。各阶段：

| 阶段 | 续跑时的幂等行为 |
| --- | --- |
| 打包 + 自检 | 成组判断（自检未完成则打包也重跑）；重跑 `dotnet build`、DLL/内容同步、打包、zip/lock，跳过 `dotnet test`；允许覆盖未打标签的 `dist/<ver>/`（沿用"发布不可变"守卫，标签已存在则拒绝） |
| 标签 | 本地已有且指向 `HEAD` 则跳过；指向别处则拒绝（不移动、不删除） |
| 私服 | 状态已记完成则跳过；否则 `npm view` 查：404 才发布；已有则比对本地 `.tgz` 与私服的 `integrity`，一致跳过、不一致拒绝（绝不覆盖）；非 404 的查询错误中止 |
| 推送 | 远端已有标签且指向 `HEAD`、远端分支在 `HEAD` 则跳过；远端标签指向别处则拒绝；其余照常 `git push origin <分支> refs/tags/<标签>` |
| GitHub Release | 不存在则 `gh release create`；已存在则附件齐全跳过、有缺只 `gh release upload` 缺的（不带 `--clobber`）、同名附件大小不符则拒绝 |

**判断记录（决定 + 理由）**

1. **状态文件放 `dist/` 不进 git**：它是"这台机器上这一次发布尝试"的运行记录，不是源码；进 git 会让发布提交之后的工作树变脏，打包自检（带 `-dirty`）直接失败；生成物不进 git 是仓库硬规则。
2. **前置校验宁严勿松**：门禁通过记录只对"门禁测过的那个提交 + 只改版本文件的发布提交"成立；`HEAD` 动过、发布提交夹带别的文件、工作树有未提交改动，都意味着要发布的东西不是门禁测过的东西，继续等于给未验证的内容盖章。
3. **不在续跑范围：第 5 步（全量门禁）或更早、以及第 6 步提交之前的失败**：此时没有门禁通过记录或没有发布提交，`-Resume` 被拒绝（`NoState`/`NoReleaseCommit`），修好问题后重跑 `-Release`（重跑全量门禁）。续跑只覆盖"发布提交之后"：打包、自检、打标签、私服发布、推送、GitHub Release。
4. **打包与自检成组**：自检失败说明打包时工作树不干净（lock/MANIFEST 记 `-dirty`），那份产物不可信，必须整段重打；状态自相矛盾（打包/自检未完成但后续阶段已标记完成）直接拒绝。
5. **续跑的打包阶段跳过 `dotnet test`，保留 `dotnet build`**：门禁已对同一棵代码树（发布提交只改版本文件）跑过完整测试，1.96.1 的失败之一正是这里的偶发红；`dotnet build` 保留是因为打包读默认输出路径下的 DLL。
6. **私服比对用 `integrity` 而不是"版本号已存在就当成功"**：`npm pack` 产物确定性（同输入同字节，与 `npm publish` 给出的 `integrity` 逐位相同），内容一致才可以安全跳过；不一致说明私服上是另一份东西，覆盖违反"发布不可变"，所以拒绝并交人。查询失败（非 404）不当作"没发布过"，否则会把网络故障误判成可以发布。
7. **阶段执行器正常路径与续跑共用一份代码**：区别只在 `-Resume` 开关（多出"先检测是否已做过"）；正常 `-Release` 的阶段顺序、输出与改动前一致，只多写状态标记。
8. **失败提示改在脚本末尾的 `try/finally` 里统一打印**（不缩进 1500 行）：`throw` 与各处 `exit 1` 都会经过它；成功完成不打印。

测试：`tests/test_release_resume.py`（库函数单元、前置校验每一种拒绝、真实 `build.ps1` 跑在 `tests/_release_skeleton.py` 搭的最小仓库骨架里：`dotnet`/`npm`/`gh` 是 PATH 上的桩，私服与 GitHub Release 是 JSON 文件，`git push` 推到本地裸仓库，绝不触及真实私服/GitHub）、
`tests/test_prune_dist.py`（状态文件被瘦身保留）。

## CI / Release 工作流与本机环境口径对齐（2026-10-02）

背景：GitHub 上 CI 工作流（`check.ps1 -SkipUnity`）至少自 2026-09-30 起每次红灯（pytest `6 failed, 5 skipped`，门禁的 skip 上限为 0；b7ac4dc3 那次多一条耗时断言共 7 failed），Release 工作流至少自 v1.85.0 起每次红灯，v1.92.0、v1.93.0 的 GitHub Release 上只有本机上传的 zip/lock/samples/两个脚本，四个 `.tgz` 一直缺。

**判断记录**

1. **Release：提交号按完整 sha 前缀比对**（`_lock_writeback.ps1` 的 `Test-WsGameCommitMatch`，`release.yml` zip 与 lock 两处共用）。MANIFEST/lock 的 `git_commit` 是发布机完整仓库的 `git rev-parse --short`（对象多了自动加长到 8 位），托管运行器浅克隆只有 7 位，`--short` 位数不是可对齐的约定。规则：记录值至少 7 位十六进制、且是当前提交完整 40 位 sha 的前缀（不分大小写）；过短、带 `-dirty` 之类后缀、空值、比完整 sha 还长一律判不一致。
2. **Release：zip 条目查找不分路径分隔符**（`Get-WsGameZipEntry`）。本机 1.92.0 包条目是 `ws-game-1.92.0/MANIFEST.txt`，1.93.0 是 `ws-game-1.93.0\MANIFEST.txt`，`Compress-Archive` 在不同 PowerShell 版本下写法不同；v1.92.0 的 Release 运行死在这里（早于上一条），"缺附件修复"的 `.tgz` 抽取处同类写法一并改。`_lock_writeback.ps1` 内既有的取条目函数早已按此口径归一。
3. **v1.85.0～v1.91.0 的 Release 运行是第三种失败**：GitHub 上当时没有对应 Release，走"全部缺失 -> 全量重建"路径，`build.ps1 -Dist` 的发布不可变守卫看到标签已存在而拒绝（`_dist_immutability_guard.ps1`）。这条路径在 tag 触发的运行里天然走不通（检出的就是标签提交本身）。**已改为"zip 缺失一律明确失败并给人工指引"，工作流不再有任何构建步骤**（`toolchain/_release_assets_plan.ps1` 的 `Get-WsGameReleaseAssetsPlan` 给出 Skip / Repair / Blocked 三选一，Blocked 的 Reason 分 `NoVerifiedAssets` 与 `MixedBatch`；`build.ps1 -SkipTests`、`dotnet tool restore`、`build.ps1 -SyncOnly -Dist -Zip` 三步随旧路径删除）。没有选"放行守卫、从标签提交原样重建"：不可变发布要求同一版本号只有一套字节，托管运行器重建的 DLL 因确定性构建仍嵌入 checkout 路径而字节、哈希必然与本机已验证（含 Unity 全量门禁）的那套不同，放行只是把"覆盖"换成"重建"的名义制造第二套字节，且要放宽发布不可变的唯一强制点（`_dist_immutability_guard.ps1`，一行未改）。代价：只推标签而本机没上传 Release 附件时不再自动出 Release，而是红灯并告知用本机 `build.ps1 -Release` 末尾打印的 `gh release create` 补传，再 `workflow_dispatch` 重跑验证。测试：`toolchain/tests/test_release_assets_plan.py`（判定函数逐例 + 全部附件子集的不变量 + 把工作流 `Check for existing release assets` 步骤的 `run:` 正文原样抽出，用桩 `gh` 在本机模拟 tag 触发运行的失败退出码与指引文案 + 静态守卫：工作流不再含构建步骤）。
4. **CI：把运行器补成与本机同口径，不放宽 skip 上限**。浅克隆没有历史与标签 -> `test_ref_conventions.py` 三条（`git show e8b48aa1/97052ac6`）失败，`test_dist_immutability_guard.py` 两条无标签 skip：`ci.yml` checkout 改 `fetch-depth: 0` + `fetch-tags: true`。`dist/` 没有成对的 zip+lock（只下了 ABI 基线 zip，基线 1.12.0 是早期布局）-> 三条真实产物用例 skip：新增三步从 GitHub Release 取最新一个 zip+lock 齐全的发布包落到 `dist/`（`actions/cache` 按版本号缓存，下载失败只留 warning，用例 skip 由 pytest 步骤的 `max_skipped=0` 如实判 FAIL）。本机复现方法：对主检出做 `git clone --depth 1 --no-local file:///<主检出>`、删掉全部标签、`dist/` 只放基线 zip，跑 `pytest -rs` 得到与 CI 完全相同的 5 个跳过 + 3 个失败；补齐历史/标签/最新 zip+lock 后 0 跳过 0 失败。
5. **CI：`test_unity_path_length_guard.py` 短根路径前提不依赖 pytest `tmp_path`**。托管运行器上 `tmp_path` 是 `C:\Users\runneradmin\AppData\Local\Temp\pytest-of-runneradmin\pytest-1\<用例名>0`，加 `\short` 共 108 字符，已超过"深根"下限 98，前提自己不成立。改为系统临时目录（不够短退到盘符根）下建 `gfpl_` 前缀短目录，长度由夹具核对，仍不够短明确失败。复现：把 `TEMP` 指到 80 字符深的目录，旧用例三条失败、新用例通过。
6. **CI：并行性用例读"并行阶段墙钟"**。`test_two_lines_run_concurrently` 原先断言外层总墙钟 < 40s + 25s 固定余量；托管运行器上固定开销（两次子进程启动、前置步骤、doc-pytest 子集）约 29s（本机 8～9s），同一次运行里并行阶段自己只用 41.5s，外层 70.8s 被顶过阈值。固定余量吸收不了环境量；改读门禁汇总表 `（并行阶段墙钟：…）` 行（check.ps1 对并行阶段单独计的实际耗时），阈值与串行预期下限不变，外层总墙钟只作诊断输出。
7. **CI 依赖 GitHub Release 上存在 zip+lock 齐全的发布包（定案）**：全新仓库或 Release 被清空时那三条真实产物用例会 skip 并让 pytest 步骤红灯——这是有意的：把"没有可对照的发布产物"静默放过，等于让依赖真实产物的守卫形同虚设（`max_skipped=0` 的本意）。补救方式是先补传发布包（见第 3 条的 `gh release create` 指引），不是放宽下限。
8. **Release 工作流的线上验证口径（验收方式，不是遗留缺口）**：工作流的比对与条目查找修复已用本机真实 1.92.0/1.93.0 的 zip 与 lock 逐项验证（`Test-WsGameCommitMatch`、`Get-WsGameZipEntry` 与桩 `gh` 模拟 tag 触发运行），两个步骤的 `run:` 正文语法已解析；GitHub 托管运行器上的真实运行只能由真实的 tag 推送或 `workflow_dispatch` 触发，开发会话不触发远端工作流（推送只由主会话按发布流程做）。因此线上验收 = 下一次发布推送标签之后查看该次 Release 运行结果：绿即验收通过；红则按其日志与第 1～3 条对照处理。

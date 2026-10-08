# ws-game

[![CI](https://github.com/wade004/ws-game/actions/workflows/ci.yml/badge.svg)](https://github.com/wade004/ws-game/actions/workflows/ci.yml)

本仓库是「游戏技术基础架构」框架仓库：技术无关、游戏无关，面向后续所有游戏。仓库本身不承载任何具体游戏的开发与内容扩展，也不出现任何具体游戏代号。

`architecture/` 是已定稿的架构文档集，是本仓库唯一的规范来源，入口见 [architecture/README.md](architecture/README.md)；技术选型、工程结构、分工与分阶段落地计划见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)。面向执行 agent 的派单规则单一来源见 [AGENTS.md](AGENTS.md)。

> 落地状态：阶段 0～5（环境骨架、L0 基础层、L1+L2 数值与规则、L3+L4 载体与玩法、Unity 适配层+表现层+UI 套件、美术管线与资产规格）均已完成，详见落地计划文档末节「落地进度记录」。3D 渲染（model 型外形）、装备外观、武器动画（`auto_attack_anim`/`cast_anim_override`）、关键帧反馈（`anim_keyframe_driven`）四项能力框架侧均**已实现**，**默认接线**由各装配根的口味配置开关控制（决策见 [ADR-0017](architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)，明细见落地计划「W6 表现能力补齐」小节）；"框架已实现"“默认接线”与"是否已有真实 Unity/消费方运行证据验证"是三件分开记录的事，不能互相替代，证据等级口径见各轮审计报告（`audit-*/AUDIT_REPORT.md`）。除上述四项外，其余能力边界按该节表格的分类口径分属当前仍开放的状态——分类口径 2026-09-09（第十四轮修订版审核责任解耦跟进，基线 `c9ff301`）由四类扩到六类：**未实现**（如孤儿检查——04 明确定位为建议、非门禁契约、不构成本轮待办、`teleport_points` 引用目标完整性校验——元素结构 `{id?, position?}` 本身已登记校验，未提供的是"带 id 的命名点是否被某处 `teleport_target_ref` 正确引用、引用的目标地图/落点是否存在"这层跨表引用完整性检查）、**延期/预留**（如 ATB——`TimeModelSchema` 已登记为合法预留枚举值，`TurnScheduler.Configure` 遇到时抛 `NotSupportedException`，是否/何时实现由后续版本排期决定，区别于"明确非目标"的无计划实现）、**已实现未默认接线**（如采集时钟、回放接入、`FeedbackRuleValidator`、`SpawnSummonOnlyCreatureRule` 的查询接入、owner/day/vendor 回调——默认未提供业务回调/固定时钟不能被误读为整个 Quest/采集能力关闭）、**游戏责任**（框架已提供机制/字段/扩展点，是否/如何进一步落地由具体游戏或宿主决定，不构成框架待办——如天赋完整点数管理、`SampleNewGameStarter` 新局重置、位移轨迹碰撞、escort 自动路线）、**明确非目标**（如 `day_cycle`、导航跨帧请求预算与空间查询完整索引化——02 第 1.8/1.9 节"性能约定"已收窄为实现方自行决定的性能边界，均已有决策记录明确不是待接的缺口）、**暂不落地（用户拍板，2026-09-09）**（编辑器工具：产品文档已完成，实现按用户拍板暂缓，ADR-0018 已将其列为框架仓库外部消费方项目，见 [docs/编辑器/README.md](docs/编辑器/README.md)）；框架提供 `TargetPoint` 字段（施法请求可携带的可空落点），地面点选到具体目标的解析/消费由上层 AI 或玩家辅助施法负责，不属于框架未实现项；已核实"至少一处生产装配根默认接入调用链"的**已实现且默认接线**能力（如上述四项、`target.chain` 形状范围目标查询、Summon follow/owner/联动、`ISkillHost.FindUnits`——已委托 `ISpatialQuery.QueryShape` 并按 `UnitFilter` 全维度过滤、生产装配默认注入 `Spatial`/`Factions`、VFX 锚点持续跟随——新增可选能力接口 `IParticleRepositioner`，Unity 参考适配层默认实现）不再列入"能力边界"表格本体，归档在该节"已修复历史项"小节。逐项源码锚点与当前完整条目数以 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)「能力边界与未默认接入能力索引」一节的表格为准（本行不重复维护具体数字，避免与该表更新脱节）。

## 顶层目录结构

```
.github/workflows/      GitHub Actions 持续集成工作流（ci.yml + release.yml，见"持续集成"一节）
.githooks/              版本化 git 钩子（pre-commit，见"提交前钩子"一节）
architecture/          架构文档集（已定稿），本仓库唯一的规范来源；00~14 号文档 + adr/（ADR 全集）+ 数值设计/ + 选型/ + 图解/ + 落地计划/（落地计划与进度记录）
core/                  L0~L4 纯逻辑类库，零引擎依赖，目标框架 .NET Standard 2.1
  foundation/            L0 基础层：event_bus、rng、expr、data_registry、sim_loop、save_system、input_map、l10n、display_info、scene_router、hook_registry、app_lifecycle
  numbers/               L1 数值层：stat_block、power_set、progression、archetype、faction
  rules/                 L2 规则层：skill、combat、targeting、ai
  carriers/              L3 载体层：item、creature、gobj、summon
  gameplay/              L4 玩法层：loot、quest、dialog、encounter、difficulty、achievement、economy、world_state、area_trigger、spawn、death（死亡复活三策略执行主体）
  sim/                   数值仿真骨架 Core.Sim（ADR-0035）：无头运行器的装配根与 simrunner 场景基线，见 core/sim/README.md
presentation/           L5 表现层的引擎无关部分（Presentation.Common：渲染/相机/UI 数据绑定、反馈绑定、VFX/SFX 播放体系、纸娃娃合成与动画状态机/程序动画原语等，均不依赖具体引擎）
adapters/
  stub/                  桩适配层（Adapters.Stub，"无头适配层"），纯 .NET 实现；供 xUnit 测试/CI 驱动，同时是框架正式交付物之一（ADR-0018 决策 3）——随构建产物分发（zip 快照 `adapters/headless/`、私服第四个包 `com.gamefoundation.adapter.headless`），供内容编辑器等无头宿主使用；`core/sim/`（数值仿真骨架，ADR-0035 决策 1）依赖它组装无头世界，`Core.Sim.dll` 同目录一并分发
  unity/                 Unity 6 LTS 工作台工程；真正的框架交付物是内嵌 UPM 包 adapters/unity/Packages/com.gamefoundation.adapter.unity/
  conformance/           引擎适配层契约一致性测试套件（同一份场景源码驱动桩实现与各引擎实现），见 adapters/conformance/README.md
games/_template/        游戏层骨架模板（本地包 com.gamefoundation.game-template），新游戏复制本目录改名接入；真实游戏代码放各自仓库
data/_framework/    框架自带的 5 张数据表（arch.power_type 与 found.* 四张），随分发包 dist/<version>/data/_framework/ 一起发给游戏，见 data/README.md
data/_sample/           框架自测/校验器自测用的示例数据表，不代表任何真实游戏内容；真实游戏数据放各自仓库的 data/<game>/
assets/_placeholder/    灰盒竖切用的通用占位资产包（精灵、特效、音效、音乐、地图分层图、字体等源素材），随版本快照一并交付
assets/_sample/         由 toolchain/import_sample_assets.py 驱动资产导入工具真实产出并提交入库的样例资产（消费 assets/_placeholder 源素材生成），供 data/_sample 的 display/vfx/sfx/world 四张表引用；改了 assets/_placeholder 源素材或需修复 data/_sample 引用时重跑该脚本幂等重新生成，见 toolchain/README.md"data/_sample 的资产来源"一节
toolchain/              校验、构建、资产导入等跨游戏 Python 工具链（validate_data.py、import_assets.py 等）；get_framework.ps1 是游戏侧按版本号引用本框架的工具（zip 通道），见"版本与发布"一节
  registry/              私服（Verdaccio 注册表）交付通道：本机/局域网内起一个私有包仓库，发布六个可发布包（游戏侧按版本号依赖其中三个，其余按需自取：无头适配层包与两个可选的手感实验室包），与 zip 通道并存，见 toolchain/registry/README.md
  sync_package_content.ps1  私服通道配套：把游戏工程解析到的 com.gamefoundation.framework-data 包内容同步到该工程的 StreamingAssets/TextMesh Pro
docs/                  非规范的工程往来与记录（规范只放 architecture/）
  升级指南/              面向游戏侧的跨版本升级指南，见"版本与发布"一节
  消费方反馈/            消费方反馈稿与框架侧答复、通知稿（按日期与批次命名）
  复盘/                  排查复盘（PlayMode 等疑难问题的根因与标准流程）
  编辑器/                内容编辑器产品文档（markdown + 离线 HTML）；编辑器代码不在本仓库：基础套件与模板在独立的编辑器项目，编辑器实例随各游戏仓库走，见 docs/编辑器/README.md
  CHANGELOG-归档-0.1.0至1.79.x.md  从根 CHANGELOG.md 拆出的 0.1.0～1.79.0 历史版本条目
dist/<version>/         build.ps1 -Dist 产出的版本快照（构建产物，.gitignore，不入库，可由源码重建）；-Release/-Zip 额外产出 ws-game-<version>.zip/.lock
VERSION                 单一版本源（纯文本版本号，如 0.2.0），两个 package.json、CHANGELOG.md、dist 快照均以此为准，见"版本与发布"一节
CHANGELOG.md             变更日志（Keep a Changelog 风格），发布时随 VERSION 一并更新（1.80.0 起；更早版本见 docs/CHANGELOG-归档-0.1.0至1.79.x.md）
REGRESSION_LOG.md       全量回归记录（每轮一行：run_id / 通过或失败 / 对应提交 / 日期）
Core.sln                六个核心类库 + 六个测试工程的 .NET 解决方案
build.ps1               DLL 同步、内容同步、版本快照打包、发布流程脚本（PowerShell 5.1 兼容）
check.ps1               一键门禁脚本：构建/测试/校验/禁用词扫描/Unity 编译与测试/独立版冒烟一次跑完并汇总（PowerShell 5.1 兼容）
```

## 构建与测试

### .NET 核心逻辑（`Core.sln`）

```powershell
dotnet build Core.sln -c Release
dotnet test Core.sln -c Release --no-build
```

### 数据校验（Python 工具链）

```powershell
python toolchain/validate_data.py
```

默认校验仓库自带的 `data/_sample/`；校验某个游戏自己的数据目录时传 `--data-root <game 仓库>/data`。分两道校验：骨架级检查（信封、主键格式）与 `toolchain/validator`（.NET，复用 `DataRegistry`）做引用完整性、枚举合法性等真实校验。

### `build.ps1`（在仓库根目录跑）

| 命令 | 效果 |
|---|---|
| `powershell -File build.ps1` | 完整流程：`dotnet build/test` → 同步六个核心 DLL 到 Unity 适配层包 `Runtime/Plugins/Core/` → 同步 `data/_sample`/`assets/_placeholder`/`assets/_sample` 到 Unity 工程 `StreamingAssets/GameFoundation/`（`sprites`/`audio`/`vfx` 三棵目标目录树同时接受 `assets/_placeholder`、`assets/_sample` 两个源目录，见 `Sync-ContentTree` 判断记录） |
| `powershell -File build.ps1 -SkipTests` | 同上，跳过 `dotnet test` |
| `powershell -File build.ps1 -SyncOnly` | 跳过 `dotnet build/test`，只做 DLL 同步 + 内容同步（要求此前至少完整 build 过一次） |
| `powershell -File build.ps1 -SyncContent` | 只做内容同步（跳过 `dotnet build/test` 与 DLL 同步）；只改了 `data/_sample`/`assets/_placeholder`/`assets/_sample`、没改任何 C# 代码时的快速路径 |
| `powershell -File build.ps1 -Dist <version>` | 额外把适配层包、`games/_template`、`toolchain`（不含 `.venv`/`__pycache__`/`registry`/`node_modules`/`bin`/`obj`，但预编译的 `toolchain/validator/bin`、`toolchain/simrunner/bin` 单独补回）、`assets/_placeholder`、`data/_framework`、`adapters/headless`（无头适配层 `Adapters.Stub.dll` + 数值仿真骨架 `Core.Sim.dll` + README，ADR-0018 决策 3、ADR-0035 决策 1/5）打成一份版本快照 `dist/<version>/`，并生成扩展后的 `MANIFEST.txt`；同时无条件额外组装私服交付通道的六个包到 `dist/<version>/packages/{六个包名}/` 并 `npm pack` 出六个 `.tgz`（两个可选的手感实验室包只作单独包，不进主 zip 与工具链包，ADR-0160）（见下方"版本与发布"一节"私服通道"） |
| `powershell -File build.ps1 -Dist auto` | 同上，但版本号不由调用方指定，改为读取仓库根 `VERSION` 文件当前内容 |
| `powershell -File build.ps1 -Dist <version> -Zip` | 在 `-Dist` 基础上额外打 `dist/ws-game-<version>.zip` + `dist/ws-game-<version>.lock`，不做任何版本号写回/提交/打标签（`.github/workflows/release.yml` 用这条路径） |
| `powershell -File build.ps1 -Release <version> [-DryRun] [-Publish] [-PublishRegistry [-RegistryUrl <url>]]` | 完整发布流程：校验（含 `REGRESSION_LOG.md` 含 Unity 全量记录） → 写回版本号 → 全量门禁（含 IL2CPP 三步） → 提交 → **候选阶段（ADR-0160）**：先把 `X-rc.N` 发到本地私服（标签 `rc`，不打标签不推送），样板仓库 `ws-game-samples` 升级到候选版本并跑它自己的全量门禁，红就在打标签前停下（样板仓库不存在时拒绝，显式 `-SkipSamplesCandidate` 才放行并记 OPT-OUT；样板位置 `-SamplesRepo` > 环境变量 `WS_GAME_SAMPLES` > 同级 `ws-game-samples`） → 打包 + zip + lock + 六个 npm 包（打包完成自检 `git_commit` 指向发布提交） → 打标签；`-PublishRegistry` 独立于 `-Publish` 控制是否额外 `npm publish` 六个包到私服；发布提交之后失败用 `-Resume` 续跑；见下方"版本与发布"一节 |

同步与打包均按文件哈希比较、只处理变化的文件；`-Dist` 打的快照不入库，可随时由源码重新生成；`-Dist` 传入的版本号必须形如 `X.Y.Z`（三段纯数字），格式非法直接报错退出。

`-Dist`/`-Dist auto`/`-Release`/`-Zip` 还会调用 `docs/manual/build.ps1` 生成 API 参考手册并放进 `dist/<version>/manual/`（见下一节）；手册生成失败即打包失败，不静默跳过。直接传 `-Dist X.Y.Z-dryrun`（只验证打包清单的形式，`check.ps1`"包清单一致性"步骤与 `toolchain/consumer_smoke.ps1` 用它）或显式传 `-SkipManual` 时跳过手册。

### API 参考手册（`docs/manual/`，ADR-0124）

javadoc 式的 API 参考：按命名空间 → 类型 → 成员逐条列出公开接口，含源码 `///` 注释里写的用途说明；各模块 `README.md`（概念文档）与 `architecture/` 下的架构文档、ADR 并入同一站点，保持原相对路径，彼此链接可点。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs\manual\build.ps1   # 生成，约 1～2 分钟，任一步失败即非零退出
dotnet docfx serve docs\manual\_site                                        # 本地浏览（直接双击 .html 缺搜索/目录脚本）
```

- **步骤**：`dotnet tool restore`（按 `.config/dotnet-tools.json` 恢复 DocFX，需能访问 NuGet 源）→ `dotnet build Core.sln -c Release`（可 `-SkipBuild` 跳过）→ `toolchain/gen_manual_toc.py`（按目录层级生成概念文档与架构文档的导航目录）→ `docfx`。
- **产物**：`docs/manual/_site/`（静态站点）；随版本快照分发为 `dist/<version>/manual/`。`_site/`、`docs/manual/api/*.yml`、`docs/manual/concepts/`（生成的导航目录）、`docs/manual/_docfx.log` 都是生成物，已在 `.gitignore`，不入库；手写部分只有 `docfx.json`、`toc.yml`、`index.md`、`api/index.md`、`build.ps1`。
- **范围**：`Core.sln` 里的类库——`Core.Foundation/Numbers/Rules/Carriers/Gameplay/Sim`、`Presentation.Common`、`Adapters.Stub`，以及诊断转发工程 `Adapters.Unity.DiagnosticsForwarding`。`Directory.Build.props` 只对这些类库打开 `GenerateDocumentationFile`（测试/工具工程不开），并关闭 CS1591（公开成员缺注释）与五类注释内引用告警——注释完整性不由编译器把关。
- **已知缺口**：**Unity 适配层 UPM 包**（`adapters/unity/Packages/com.gamefoundation.adapter.unity`）由 Unity 编译、不在 `Core.sln` 里，**未纳入** API 参考（只有诊断转发工程按引用编译的三份源文件出现在手册里）；README/架构文档里指向源码、数据、脚本等非 markdown 文件的链接在站点里是断的，以仓库为准；注释里指向私有成员/测试类型/重载组的 `cref` 在页面上显示为纯文本。

### Unity 工作台（命令行跑测试）

先跑过一次 `build.ps1`（至少 `-SyncContent`），否则内容数据集与占位资源不会同步到 Unity 工程。

```powershell
Unity.exe -batchmode -nographics -projectPath adapters\unity -runTests -testPlatform EditMode -testResults <输出目录>\editmode.xml -logFile <输出目录>\editmode.log
Unity.exe -batchmode                -projectPath adapters\unity -runTests -testPlatform PlayMode -testResults <输出目录>\playmode.xml -logFile <输出目录>\playmode.log
```

`-runTests` 不要再加 `-quit`（两者同传会在测试真正跑起来前提前退出）；`-nographics` 只用于 EditMode，PlayMode 需要真实渲染/输入子系统，不能加。独立版 Windows 命令行构建、灰盒/Shell 场景重建、独立版无头冒烟等更详细的命令与边界说明，见 [adapters/unity/README.md](adapters/unity/README.md) 与 [adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md](adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md)。

### 消费方演练（`toolchain/consumer_smoke.ps1`）

从零搭建一个独立于本仓库源码树、只以 `dist/<version>/` 为输入的最小 Unity 消费方工程，完整走一遍
[games/_template/README.md](games/_template/README.md) 描述的接入步骤，验证"一个从未见过本仓库
源码的新游戏工程，照着文档操作能不能真正跑起来"——不是靠工作台工程（`adapters/unity`，源码级直接
引用本仓库内容）自证。共 10 步：确保分发包快照存在 → 准备全新工作目录 → 复制并改名
`games/_template` → 创建消费方工程骨架（`file:` 引用两个包）→ 同步内容数据集 + TextMeshPro 运行期
资源 + 占位场景/导航资源 → 首次批处理编译（0 编译错误）→ 生成 Shell + Map 场景 → 模板 PlayMode
测试 → 构建独立版 → `-gf-smoke-template` 无人值守冒烟（真正驱动"主菜单→新游戏→进图→移动→存档→
读档→退出"，断言日志出现 `RESULT=OK`）。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\consumer_smoke.ps1
```

默认版本号读仓库根 `VERSION`、工作目录落在系统临时目录下的 `gf_consumer_smoke_<8 位哈希>\`（哈希由
本检出的仓库根路径派生，主检出与各工作树互不共用；每次运行前清空重建）；`-DistVersion`/`-WorkDir`/`-UnityExe`/`-SkipCleanWorkDir` 可覆盖，见该脚本头注释。
`check.ps1` 默认会跑这一步（`-SkipConsumer` 跳过，`-SkipUnity` 时一并跳过），是 11 号文档"提交
门槛清单"的一部分。

## `check.ps1`（一键门禁，仓库根，见 11_工程规范与测试.md 第 8 节）

门禁提速重排（gate-speed 任务，2026-09-22，判断记录见 check.ps1 头部注释）：先串行跑一批秒级、不
依赖 Unity/重构建的快速检查——门禁自检 → 两道禁用词扫描（全仓库不出现具体游戏代号，改用 `git
grep` 只扫受版本管理的文件；`architecture` 正文不出现具体引擎/语言/框架/工具名）→ 版本一致性
（`VERSION`、两个 `package.json`、`packages-lock.json` 与 `CHANGELOG.md`）→ 数据校验（合并根 +
`data/_framework` 框架根单独完整校验）→ 元数据门禁 `validator --schema-audit` → 事件常量一致性
检查 → 数据表字段顺序检查 → 资产导入交叉校验 → 嵌入仿真数据集单独 `--strict` 校验 → 工作树文本
文件无 CR → Unity `.meta` 完整性检查；再把两条互不依赖的重步骤线并行跑（`Start-Job`，两个独立
`powershell.exe` 子进程）：一条"非 Unity 重步骤线"（`toolchain/_gate_line_heavy.ps1`）依次跑
`.NET` 构建 + 测试（六工程，含性能基线）→ ABI 探针 → 占位资产生成器一致性检查（`--check`，只读）
→ 样例导入幂等性门禁 → `toolchain` 自身的 Python 测试 → 数值仿真基线比对（`toolchain/simrunner`
对嵌入式最小仿真数据集跑三类场景，与既有基线比对统计量差异，见
[core/sim/README.md](core/sim/README.md)"命令行入口"一节；ADR-0035 决策 3/5）；另一条"Unity 串行
线"（`toolchain/_gate_line_unity.ps1`，内部必须串行——Unity 不允许同一工程有两个批处理实例同时
跑）依次跑 `build.ps1 -SkipTests` 同步 DLL → 包清单一致性（私服交付通道六个 npm 包版本号 + 包边界判定 +
`npm pack --dry-run` 排除规则，见"版本与发布"一节"私服通道"）→ Unity 编译检查 → Unity
EditMode/PlayMode 测试 → 独立版构建 + 两种无人值守冒烟（`-gf-smoke` 连续模式默认流程、
`-gf-smoke-discrete` 离散模式链路）→ 消费方演练（`toolchain/consumer_smoke.ps1`，见下一节）。
两条线都结束后把各自的结果合并进同一张汇总表（步骤逐行列出 PASS/FAIL/耗时，另加一行"并行阶段
墙钟"，不计入步骤总数）；每步单独计时与判定；任一步失败，整体以非 0 退出码结束——默认行为是跑完
全部步骤（不管前面是否已经失败），传 `-FailFast` 后任一步失败会让"该步骤所在的那条执行序列"
（前置快速检查阶段，或并行两条线之一）后续步骤全部立即改判可见 SKIP，不再白跑（跨两条并行线用
共享标记文件通信，见 check.ps1 `.PARAMETER FailFast` 判断记录）。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1              # 全量（含 Unity 相关步骤与消费方演练；不传 -FailFast，跑完全部步骤）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipUnity   # 跳过 Unity 相关步骤与消费方演练（Unity 编辑器被占用/未装时用）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipSmoke   # Unity 独立版仍构建，只跳过两种无人值守冒烟子步骤
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipConsumer # 跳过消费方演练这一步（其余 Unity 步骤仍跑）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -FailFast    # 任一步失败立即停止后续步骤（build.ps1 -Release 调用门禁时默认传本开关）
```

`-ArtifactsPath <dir>` 可覆盖 `dotnet`/Unity 产物落地目录（默认 `bin\_check_artifacts`，已被 `.gitignore` 的 `bin/` 规则忽略）；`-UnityExe <path>` 可显式指定 Unity 可执行文件路径（默认按 Unity Hub 常见安装位置猜测，找不到则要求显式传参）。

`check.ps1`（TOOL-116-01 措辞修订，外部审计 audit-24a11fe-20260910）不修改已跟踪文件，但这不等于
"无副作用"：`-ArtifactsPath`（默认 `bin\_check_artifacts`）与"包清单一致性"步骤内部调用的
`build.ps1 -SyncOnly -Dist auto`（见上方步骤列表）都会往 `.gitignore` 覆盖的 `bin/`、`dist/` 目录
写入构建产物/打包中间物这类中间物——即使是 `-SkipUnity` 也一样会写（这两步都不属于 Unity 相关
步骤）；只是这些目录都是本机构建缓存、不入库，`git status` 看不出变化，因此说"不写仓库文件"仅指
"不改写已跟踪的源码/文档"，不代表整个运行过程零副作用。Unity 编辑器进程本身则会在内容确有变化时
重写 `adapters/unity/ProjectSettings/ProjectSettings.asset`，以及在包清单内容确有变化时改写
`adapters/unity/Packages/manifest.json`/`packages-lock.json`（这三者是**已跟踪**文件，与本脚本
自身的中间物写入是两回事；触发条件是内容确有变化，不改内容的编译检查/EditMode 测试不会复现，
独立版构建这类步骤会）。三者 Unity 写出的行尾实测是 LF（2026-09-07 二次实测勘误，此前误判为
CRLF；实测方法见 `.gitattributes` 对应例外条目上方判断记录）。根 `.gitattributes` 已为这三类路径
显式声明 `eol=lf`（与 Unity 实际写出的一致），使 Unity 批处理跑完后工作树始终保持干净，不再依赖
运行机器本地的 `core.autocrlf` 配置。

`check.ps1` 另有一步"版本一致性"（不需要 `-Dist`，`-SkipUnity` 下同样会跑）：只读比较仓库根 `VERSION` 文件与两个 `package.json`（`adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json`、`games/_template/package.json`，含后者对适配层包的依赖版本号）是否一致；并校验根 `CHANGELOG.md` 含 `VERSION` 对应版本号的条目（形如 `## [X.Y.Z]`）或存在 `## [Unreleased]` 段，四处任一处漏改都会让这一步失败。

`check.ps1 -Quick`（工程收尾 K 新增，供 `.githooks/pre-commit` 调用）：只跑 dotnet build/test、两道数据校验、事件常量一致性检查、禁用词扫描、版本一致性这几步"秒级能跑完"的子集，跳过占位资产生成器检查、`toolchain` 自身 pytest、数值仿真基线比对（T-N6-7 新增，见上一节；不是"不重要"，是任务书硬性规则"禁止把数值仿真列为 -Quick 步骤"）、`build.ps1 -SkipTests` 同步、包清单一致性（私服交付通道新增，需要跑一遍 `build.ps1 -SyncOnly -Dist auto` + `npm pack`，与"build.ps1 -SkipTests 同步"同一类耗时构建期动作，且依赖后者先把 DLL 构建到 `bin\` 下，故排在其之后）、全部 Unity 相关步骤与消费方演练；与 `-SkipUnity` 可以同传但没有必要（`-Quick` 本身已经不跑 Unity 相关任何一步）。资产导入工具交叉校验（`import_assets.py check --dataset _sample`，全量交叉校验 sprite/vfx/sfx/world 四域，只比对文件是否存在、不读图片，秒级完成，见 [toolchain/README.md](toolchain/README.md)"`data/_sample` 的资产来源"一节）不属于可跳过的慢步骤，`-Quick` 下同样会跑。目标总用时 30 秒左右（视本机是否需要重新编译而定），供提交前钩子做"能拦住的先拦住，剩下的交给 CI/手工全量 `check.ps1`"这一级快速把关，不能替代完整门禁。

`check.ps1 -Il2cpp`（工程收尾 K 新增，默认不跑，因为耗时数分钟到十几分钟）：额外跑一遍 IL2CPP 脚本后端的独立版构建 + 两种无人值守冒烟（`-gf-smoke`/`-gf-smoke-discrete`），验证核心类库自写的零依赖 JSON 读写器等纯逻辑代码在 AOT 编译（无反射兜底）下的真实可运行性，而不是只靠 Mono 后端的默认独立版构建自证；见 [adapters/unity/README.md](adapters/unity/README.md)"IL2CPP 发布路径验证"一节与 [architecture/选型/01_引擎与语言选型评估.md](architecture/选型/01_引擎与语言选型评估.md) 补充的"发布形态验证"一节（实测数据、与 Mono 的耗时/体积对比）。`-Il2cpp` 与 `-SkipUnity` 互斥（`-SkipUnity` 优先，`-Il2cpp` 不生效）；可与 `-SkipConsumer`/`-SkipSmoke` 同传。

## 持续集成（GitHub Actions）

`.github/workflows/ci.yml`：`push` 到 `main` 与任意 `pull_request` 时，在 `windows-latest` 运行器上跑 `check.ps1 -SkipUnity`（安装 .NET 8.0.x SDK 与 Python 3.12 + `toolchain/requirements.txt` 后执行；缓存 NuGet 包与 pip 依赖），并把控制台输出与门禁产物日志上传为 artifact。CI 不跑 Unity 相关四步与消费方演练——托管运行器既没有装 Unity，也无法激活个人版 Unity 授权，这部分职责仍由本机全量 `check.ps1`（含可选的 `-Il2cpp`）承担，见上一节。

`.github/workflows/release.yml`：推送形如 `v*` 的标签时触发（也支持 `workflow_dispatch` 手动指定 `tag` 输入重跑，用于对已推送的标签重新验证本工作流本身），跑一遍快速门禁（`check.ps1 -SkipUnity -Quick`，作为标签指向的提交未被意外改动的交叉验证——完整门禁已经在本机 `build.ps1 -Release` 第 5 步跑过）。附件策略是"本机已验证的产物优先"：先用 `gh release view` 查该标签对应 Release 是否已经有 `ws-game-<version>.zip`（即本机 `build.ps1 -Release -Publish` 已经上传过、且已在含 Unity 的完整门禁下验证过）——已有则跳过打包与上传，不覆盖（托管运行器重新构建的 DLL 因确定性构建仍嵌入 checkout 路径而哈希不同，覆盖会让 Release 附件与私服包不一致）；zip 在而其它附件有缺则从这份已验证 zip 里抽取/重算补齐（不构建）；zip 缺失（例如只推了 tag、本机未上传 Release 附件）则**明确失败并打印人工指引**——工作流不在托管运行器上重建任何发布产物（重建出的是同一版本号的第二套字节，且 tag 触发的运行里 `build.ps1` 的发布不可变守卫也会拒绝），Release 附件的唯一来源是本机 `build.ps1 -Release` 末尾打印的 `gh release create` 命令，补传后可用 `workflow_dispatch` 重跑验证。判定见 `toolchain/_release_assets_plan.ps1`。

## 提交前钩子（`.githooks/`）

仓库自带一份版本化的 `git` 钩子目录 `.githooks/`，默认不生效（`git` 的 `core.hooksPath` 默认指向 `.git/hooks/`，不会自动读取仓库内任意目录）。首次克隆后按需安装：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\install_hooks.ps1
```

该脚本把 `git config core.hooksPath` 指向 `.githooks/`（幂等，重复跑不报错）；`-Uninstall` 还原为默认值。`core.hooksPath` 写在共享的 `.git/config` 里，同一仓库的所有工作树自动生效，只有新克隆需要再跑一次。

`.githooks/commit-msg`（2026-10-09）：提交信息乱码拦截。含 U+FFFD、非法 UTF-8、典型 GBK/Latin-1/cp1252 误解码乱码、成串问号（中文被替换成 `?`）时拒绝提交并提示"把提交信息先写进 UTF-8 文件，再 `git commit -F <文件>`"。判定逻辑在 `toolchain/check_commit_msg.py`，用例 `toolchain/tests/test_commit_msg_hook.py`（含经真实 `git commit` 的整链路）；找不到 python 时拒绝而不是放行。

安装后每次 `git commit` 前，`pre-commit` 先按本次暂存改动清单（`git diff --cached --name-only`，含重命名/删除；合并提交同样按这份清单判断，仅在下面 MergeSkip 条件全部满足时才跳过）分级，把判定结果打印成一行说明（档位 + 理由），再决定跑多重的门禁。发布提交（ReleaseSkip）的判定仍是 `toolchain/_precommit_tiering_guard.ps1` 里的纯函数 `Get-PreCommitCheckTier`（`toolchain/tests/test_precommit_tiering_guard.py` 单独覆盖），其余按改动影响集分级：

- **ReleaseSkip**：`build.ps1 -Release` 在第 5 步门禁（全量，或复用已验证全量记录时的定向门禁，含 `-AbiStrict`）通过后，于"第 6 步"提交版本号改动前设置 `WS_GAME_RELEASE_COMMIT=1`，且暂存清单确实只含该步骤 `git add` 的那五个版本文件（`VERSION`、两个 `package.json`、`packages-lock.json`、`CHANGELOG.md`）——不重复跑 `check.ps1`（门禁已经在这次提交之前跑过）。
- **MergeSkip**（ADR-0156）：合并提交（`MERGE_HEAD` 恰有一个）且即将提交的树与第二父提交的树逐字节相同、该第二父提交在 `REGRESSION_LOG.md` 有含 Unity 全量通过记录背书（判定复用发布守卫，见 `toolchain/_precommit_tiering_guard.ps1` 的 `Get-PreCommitMergeSkip`）——被提交的内容就是测过的内容，不重复跑。主线有独立改动被合进来（树不同）、第二父提交没有记录、章鱼合并、判定出错都照常跑。
- **只改 `toolchain/gate_floors.json`**（ADR-0156）：判 T1，只跑 `floors_pytest`（`test_gate_floors_logic.py` 一个文件）加秒级基础步骤，不再跑 toolchain 全量 pytest；与 `toolchain/tests/**` 同批改动时全量 pytest 照旧。
- **其余一切情况**：跑 `check.ps1 -Staged -SkipUnity`，由 `toolchain/change_impact.py` 按暂存路径统一判级（ADR-0126，规则见 `toolchain/module_map.json`）。T0（全是文档，即旧 DocsOnly 档）只跑门禁自检、两道禁用词扫描（CLAUDE.md 硬性规则的唯一守门，任何档位都不跳过）、版本一致性、两个文档相关 pytest 用例，秒级完成；T1/T2 只跑命中层的测试工程与被触发的步骤；T3（共享面或未知路径）此前叫 Full，钩子对这一档额外带 `-Quick`（与旧 Full 档完全等价：`check.ps1 -SkipUnity -Quick`，目标总用时 30 秒左右，见上一节），完整全量留给里程碑与手工 `check.ps1`。每次判定先打印"本次判定"块，被跳过的步骤标 SKIP 与"T? 未触发"。

暂存清单为空、判级脚本调用失败（如 `python`/`powershell` 不可用）一律退回旧 Full 档（`check.ps1 -SkipUnity -Quick`，不走定向），不静默放行。未通过则本次提交被拦截（终端打印失败明细，同 `check.ps1` 汇总表）；紧急情况需要跳过时用 `git commit --no-verify`（不建议常态化使用）。

## 版本与发布

### 单一版本源

版本号的单一来源是仓库根的 `VERSION` 文件（纯文本，如 `0.2.0`，UTF-8 无 BOM）。版本号遵循语义化版本 `MAJOR.MINOR.PATCH`：MAJOR 表示不兼容变更（走 ADR 审批的契约签名变化、存档格式不兼容、数据表字段删改），MINOR 表示向后兼容的新增能力，PATCH 表示缺陷修复与文档勘误（判据见 [11_工程规范与测试.md](architecture/11_工程规范与测试.md) 第 7 节）。`adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json`、`games/_template/package.json` 两个 `package.json` 的 `version` 字段（含 `games/_template` 对适配层包的依赖版本号）与仓库根 `CHANGELOG.md`（[Keep a Changelog](https://keepachangelog.com/) 风格）需要与 `VERSION` 同步，`check.ps1` 的"版本一致性"步骤会校验这几处是否一致（见上一节）；日常开发中的变更先累积到 `CHANGELOG.md` 的 `## [Unreleased]` 段，发布时由 `build.ps1 -Release` 归档为对应版本号的条目。

### 发布不可变

每一个已发布的版本号一旦打了标签即视为定版，不得就地修改或用同一版本号重新打包替换；发现问题一律发新的 PATCH 版本修复，不兼容的修复经维护分支处理（见下方"维护分支与 PATCH 发布"）。

### `build.ps1 -Release`：完整发布流程

```powershell
powershell -File build.ps1 -Release 1.0.0                                    # 完整发布：校验 → 写回版本号 → 全量门禁 → 打包+zip+lock → 提交 → 打标签
powershell -File build.ps1 -Release 1.0.0 -DryRun                            # 只跑校验+打包，不改任何源码文件、不提交、不打标签（产物名带 -dryrun 后缀）
powershell -File build.ps1 -Release 1.0.0 -FullRegate                       # 第 5 步强制重跑全量门禁（默认复用已验证的全量记录、只跑定向门禁，见下"流程内部"第 5 步）
powershell -File build.ps1 -Release 1.0.0 -Publish                          # 打完标签后自动执行 git push 与创建 GitHub Release
powershell -File build.ps1 -Release 1.0.0 -Resume [同样的 -PublishRegistry/-Publish]   # 发布提交之后失败（打包/自检/标签/私服/推送/GitHub Release）：从失败的阶段续跑，不重跑全量门禁、不改写历史
```

流程内部依次做：

1. 校验版本号格式，且必须严格大于 `VERSION` 当前值（语义化版本数值比较）。
2. 校验 `git status` 干净（工作树不能有未提交改动）。
3. 校验 `CHANGELOG.md` 已存在 `## [X.Y.Z]` 条目（没有则报错，提示先补齐变更记录）。
   另校验 `REGRESSION_LOG.md` 里有对应当前 `HEAD` 的"含 Unity 全量通过"记录（记录的提交就是 `HEAD`，或是其祖先且其后只改了 `docs/`、`architecture/`、`*.md`；结果列须以"通过"开头并含"含 Unity"或"PlayMode N/N"字样），没有则拒绝发布并打印原因——先在有 Unity 的机器上对当前提交跑 `check.ps1` 全量并登记一行；`-DryRun` 只警告不拦。发布不再有"跳过 Unity"的开关。
4. 把版本号写回 `VERSION`、两个 `package.json` 与 `adapters/unity/Packages/packages-lock.json`（`com.gamefoundation.game-template` 条目下对适配层包依赖版本号的 UPM 镜像字段；`-DryRun` 时跳过这一步，不触碰任何源码文件）。
5. 跑门禁 `check.ps1`（固定传 `-AbiStrict -FailFast -NoTiming`：含 Unity 相关步骤；IL2CPP 三步仍是显式 `-Il2cpp` 开关，发布门禁不强制——构建机缺 Visual Studio C++ 工作负载与 Windows SDK，装好后再启用）。**默认复用第 3 步已验证的全量记录，不再重跑同一份全量**（ADR-0156）：第 3 步守卫放行、且工作树除第 4 步写回的版本文件外没有别的改动时，改跑定向门禁 `check.ps1 -Changed <记录提交>`（记录之后只有文档类改动 + 版本号写回；模块表规则 `release_version_files` 仍会跑包清单一致性、DLL 同步、Unity 编译、消费方演练），日志写明复用的记录（run_id + 提交）与定向门禁结论行；守卫不放行、`-DryRun`、或显式传 `-FullRegate` 时仍跑全量（约 50 分钟）。通过后（非 `-DryRun`）写门禁通过记录 `dist/release-<ver>.state.json`（被 `.gitignore` 覆盖，`-Resume` 的凭据）。
6. 非 `-DryRun` 时：门禁通过后立即提交 `VERSION`/两个 `package.json`/`packages-lock.json`/`CHANGELOG.md`（提交信息 `发布 <ver>`）——先于下一步打包，使打包阶段 `git rev-parse HEAD` 就是这次发布提交本身、工作树干净。
7. 打包 `dist/<ver>/`、`dist/ws-game-<ver>.zip`（zip 内顶层目录 `ws-game-<ver>/`）与 `dist/ws-game-<ver>.lock`（版本号、`git_commit`、六个核心 DLL 的 sha256）；非 `-DryRun` 时打包完成后自检 `git_commit` 必须等于上一步的发布提交且不带 `-dirty` 后缀，不满足则报错退出（此时提交已产生但未打标签：修复原因后用打印的 `-Resume` 命令续跑；只有要放弃本次发布时才 `git reset --soft` 回退）；自检通过后打带注释标签 `v<ver>`（标签信息取 `CHANGELOG.md` 该版本条目正文），并打印后续需要人工/设计层执行的两条命令：

   ```powershell
   git push origin <当前分支> refs/tags/v<ver>
   gh release create v<ver> dist/ws-game-<ver>.zip dist/ws-game-<ver>.lock --title v<ver> --notes-file dist/release-notes-<ver>.txt
   ```

   `<当前分支>` 取自 `git rev-parse --abbrev-ref HEAD`（`-Release` 全程不切换分支，就是打标签所在的那个分支：主线发布是 `main`，维护分支 PATCH 发布是 `release/X.Y.x`），只推本次新建的这一个标签（不带 `--tags` 全量推送）——维护分支上跑 `-Release`/`-Publish` 不会把 `main` 隐式往前推、也不会把本机其它未推送的本地标签一并带出去。传 `-Publish` 则自动执行这两条命令；省略时只打印，由人工确认后自行运行（`.github/workflows/release.yml` 在标签推送后会检查 Release 是否已有对应 zip 附件，已有则跳过重新打包上传，不覆盖本机已验证的产物；没有才走它自己的兜底打包上传路径，见"持续集成"一节）。若本次版本号的 MAJOR 或 MINOR 段发生变化，额外打印建议的维护分支创建命令。

**失败后续跑（`-Resume`）**：第 6 步产生发布提交之后，任一阶段（打包、自检、打标签、私服发布、推送、GitHub Release）失败，脚本末尾打印 `build.ps1 -Release <ver> -Resume [同样的开关]`。续跑只在状态文件与仓库现状严格吻合时执行（状态文件在且版本一致、`HEAD` 就是记录的发布提交、其父提交就是门禁测过的提交、发布提交只含那五个版本文件、工作树干净、`VERSION` 等于目标版本；任一不满足即拒绝并说明原因与下一步），跳过第 1～6 步，从第一个未完成的阶段起按序执行，每个阶段幂等：标签已在 `HEAD` 则跳过（指向别处则拒绝）；私服已有同版本包则比对 `integrity`（一致跳过、不一致拒绝，绝不覆盖；查询失败而非 404 一律中止）；远端已有标签且分支在 `HEAD` 则跳过推送；GitHub Release 已存在则只补传缺失附件（不带 `--clobber`，同名附件大小不符则拒绝）；打包重跑允许覆盖尚未打标签的 `dist/<ver>/` 与产物（标签已存在则由"发布不可变"守卫拒绝）。**不在续跑范围**：第 5 步（门禁）或更早、以及第 6 步提交之前的失败——此时没有门禁通过记录或没有发布提交，修好问题后重跑 `-Release`。`-Resume` 不能与 `-DryRun`、`-AllowOverwriteDist` 同传。

`dist/ws-game-<ver>.zip` 内的 `dist/<ver>/` 目录本身与既有 `-Dist` 打快照的产物结构一致（`MANIFEST.txt` 记录内容见下）；单独打 `dist/<ver>/` 而不做发布流程仍用 `build.ps1 -Dist <version>`；只想在已有 `dist/<ver>/` 基础上补一份 zip+lock（不校验/不提交/不打标签）用 `build.ps1 -Dist <version> -Zip`。

`MANIFEST.txt` 记录本次快照的可追溯信息（见 [11_工程规范与测试.md](architecture/11_工程规范与测试.md) 第 7 节"版本号必须可追溯到对应的架构文档版本与数据 schema 版本组合"）：

- `version`/`date`/`git_commit`（`git rev-parse --short HEAD`，打包时工作树不干净则追加 `-dirty`；`-Release` 流程里提交先于打包，正常情况下这里就是发布提交本身、不带 `-dirty`，见上方流程第 6/7 步）；
- 各目录文件数（`[directory_file_counts]`）；
- `[architecture_docs]`：`architecture/0*.md`、`1*.md` 每篇文档标题里的版本号（如 `01_分层与依赖.md: v3`）——注意 `dist/` 本身不打包 `architecture/` 目录，这一节只是把"打这份快照时架构文档集处于哪个版本组合"记录下来，供事后核对；
- `[data_schemas]`：`data/_framework` 下每张表的 `table`/`schema_version`（`data/_sample` 不随 `dist` 分发，不列入）；
- `[core_assemblies]`：六个核心 DLL 的 sha256（与 `ws-game.lock` 的 `dlls` 字段同一份数据）。

### 维护分支与 PATCH 发布

一次 MAJOR 或 MINOR 发布之后，若该版本线需要修复缺陷但不能带上主线后续已经在开发的新功能，从对应标签切一条维护分支：

```powershell
git branch release/1.0.x v1.0.0
```

在 `release/1.0.x` 分支上修复缺陷、提交，再在该分支上跑 `build.ps1 -Release 1.0.1`（PATCH 递增，其余步骤与主线发布完全一致），发布完成后把修复本身（不是整条分支历史）回合（cherry-pick 或 PR）到 `main`，避免主线丢失同一处缺陷的修复。`-Publish`（或人工执行上一节打印的 `git push` 命令）推送的是当前所在的 `release/1.0.x` 分支本身与新打的标签，`main` 不受影响、不会被隐式推进。

### 游戏侧引用与升级

游戏侧不直接引用框架仓库的开发目录，而是按版本号引用一份发布产物快照：`toolchain/get_framework.ps1 -Version <ver> -Target packages` 从 GitHub Release 拉取 `ws-game-<ver>.zip`/`.lock`，校验六个核心 DLL 的哈希与锁文件一致（锁文件存在 `headless_dlls`/`validator_dlls` 字段时一并校验无头适配层 DLL/预编译 validator 的 DLL 哈希，ADR-0018 决策 3、消费方反馈 E1；老锁文件没有这两个字段时跳过并提示，向后兼容）后解压到 `packages/ws-game-<ver>/`，并在游戏仓库根写入/校验 `ws-game.lock`；`-FromLocalDist <本机 zip 路径>` 可离线来源（校验规则不变）。`get_framework.ps1` 自身自包含（消费方反馈 E2 根治），可以只下载这一个文件使用——**每次发布的 Release 附件集合**为：`ws-game-<ver>.zip`、`ws-game-<ver>.lock`、`ws-game-<ver>-samples.zip`（消费方反馈 E4 新增，内含 `data/_sample`/`assets/_sample` 验收数据集，`get_framework.ps1 -WithSamples` 下载/校验/合并落地）、`get_framework.ps1`、`_hash.ps1`（兼容旧还原脚本）、六个私服包 `.tgz`（`com.gamefoundation.adapter.unity`/`com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`/`com.gamefoundation.adapter.headless`，另有两个可选的手感实验室包 `com.gamefoundation.feel-lab.unity`/`com.gamefoundation.feel-lab.headless`，只作单独附件、不进主 zip，各附版本号）。这是 zip 快照通道；私服（按版本号依赖）是并存的第二条通道，见下一节"私服通道"。完整的四条消费通道、五条多游戏共用规则与升级步骤见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md) 第 3.5 节；接入步骤见 [architecture/13_新游戏接入指南.md](architecture/13_新游戏接入指南.md) 第 1 节；`get_framework.ps1` 详细参数与消费方反馈 E1～E4 的根治说明见 [toolchain/README.md](toolchain/README.md)"`get_framework.ps1`（游戏侧按版本号引用本框架）"一节。

### 私服通道

除 zip 快照通道外，框架同时提供一条私服（[Verdaccio](https://verdaccio.org/)，npm 兼容协议）通道：把框架拆成六个可独立按版本号依赖的包（`com.gamefoundation.adapter.unity`/`com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`/`com.gamefoundation.adapter.headless`——第四个包 ADR-0018 决策 3 新增，随框架版本号发布但不是 Unity 依赖，不写入游戏工程 `Packages/manifest.json` 的 `dependencies`，供内容编辑器等无头宿主按需 `npm install`；后两个是可选的开发期手感实验室包——`com.gamefoundation.feel-lab.unity` 只在编辑器编译、游戏独立版不带，`com.gamefoundation.feel-lab.headless` 带命令行与实验室数据，想校准手感时再装，ADR-0160），发布到私有包仓库，游戏侧 Unity 工程用作用域注册表（`scopedRegistries.scopes: ["com.gamefoundation"]`）依赖前三个包。两条通道打包内容一致（同一次 `build.ps1 -Dist`/`-Release` 产出），互不排斥，可任选其一或两者都配。完整设计、各包内容、两条通道取舍见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md) 第 3.5.1 节；快速开始、配置细节见 [toolchain/registry/README.md](toolchain/registry/README.md)。

```powershell
# 起私服（本机，默认 127.0.0.1:4873）
powershell -File toolchain\registry\start_registry.ps1 -Detach
# 无人值守建发布账号 + 令牌
powershell -File toolchain\registry\init_publisher.ps1
# 发布（-Release 已隐含打包六个 .tgz，-PublishRegistry 额外发到私服）
powershell -File build.ps1 -Release <version> -Publish -PublishRegistry

# 游戏侧：生成/更新 Packages/manifest.json 片段 + ws-game.lock
powershell -File toolchain\get_framework.ps1 -Version <version> -FromRegistry
# 首次解析完包后，把 framework-data 包内容同步到工程 StreamingAssets/TextMesh Pro
powershell -File toolchain\sync_package_content.ps1 -UnityProjectPath <你的 Unity 工程>
```

## 升级指南

跨版本升级路径（不同于 `CHANGELOG.md` 里逐版本的"迁移说明"，后者只记单版增量）维护在
[docs/升级指南/README.md](docs/升级指南/README.md)；覆盖数值设计专项 N0～N6 八个版本
（1.30.0～1.37.0，含深度复审修复版）的一份跨版本升级指南见
[docs/升级指南/1.29.0到1.37.0-数值设计专项.md](docs/升级指南/1.29.0到1.37.0-数值设计专项.md)。

## 新游戏如何消费本框架

原则：框架仓库是被依赖方，任何游戏不进入框架仓库。新游戏 = 自己目录里的一个 Unity 工程 + `data/` + `assets/` + 自己的设计文档与 git 仓库，按版本号引用框架的一份发布产物快照（经 `toolchain/get_framework.ps1` 拉取校验后 `file:` 相对路径引用，见上方"版本与发布"一节"游戏侧引用与升级"）。完整的四条消费通道、五条多游戏共用规则与升级步骤，见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md) 第 3.5 节；从"选引擎"到"跑验收"的完整立项步骤与检查表见 [architecture/13_新游戏接入指南.md](architecture/13_新游戏接入指南.md)。

## 文档入口

- 架构文档集导读（阅读顺序、强制约束、文件清单）：[architecture/README.md](architecture/README.md)
- ADR 索引（架构决策记录）：[architecture/adr/README.md](architecture/adr/README.md)
- 技术选型、工程结构、分工与分阶段落地计划、落地进度记录：[architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)
- 具体技术选型细节（引擎、语言、工具链等，`architecture/00~14` 与 `adr/` 本身不出现这些名字）：`architecture/选型/`

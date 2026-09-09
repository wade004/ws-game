# ws-game

[![CI](https://github.com/wade004/ws-game/actions/workflows/ci.yml/badge.svg)](https://github.com/wade004/ws-game/actions/workflows/ci.yml)

本仓库是「游戏技术基础架构」框架仓库：技术无关、游戏无关，面向后续所有游戏。仓库本身不承载任何具体游戏的开发与内容扩展，也不出现任何具体游戏代号。

`architecture/` 是已定稿的架构文档集，是本仓库唯一的规范来源，入口见 [architecture/README.md](architecture/README.md)；技术选型、工程结构、分工与分阶段落地计划见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)。

> 落地状态：阶段 0～5（环境骨架、L0 基础层、L1+L2 数值与规则、L3+L4 载体与玩法、Unity 适配层+表现层+UI 套件、美术管线与资产规格）均已完成，详见落地计划文档末节「落地进度记录」。3D 渲染（model 型外形）、装备外观、武器动画（`auto_attack_anim`/`cast_anim_override`）、关键帧反馈（`anim_keyframe_driven`）四项能力框架侧均**已实现**，**默认接线**由各装配根的口味配置开关控制（决策见 [ADR-0017](architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)，明细见落地计划「W6 表现能力补齐」小节）；"框架已实现"“默认接线”与"是否已有真实 Unity/消费方运行证据验证"是三件分开记录的事，不能互相替代，证据等级口径见各轮审计报告（`architecture/落地计划/audit-*/AUDIT_REPORT.md`）。除上述四项外，其余能力边界按该节表格的分类口径分属当前仍开放的状态——**未实现**（如天赋激活与持久化、孤儿检查、`ISkillHost.FindUnits`、位移轨迹碰撞、VFX 锚点持续跟随、nested teleport 元素/引用完整性校验、导航跨帧预算、空间查询完整索引化）、**已实现未默认接线**（如采集时钟、回放接入、`FeedbackRuleValidator`、`SpawnSummonOnlyCreatureRule` 的查询接入）、**明确非目标**（如 `day_cycle`、ATB，已有决策记录明确本版不展开，不是待接的缺口）、**暂不落地（用户拍板，2026-09-09）**（编辑器工具：产品文档已完成，实现按用户拍板暂缓，见 [editor/README.md](editor/README.md)）；框架提供 `TargetPoint` 字段（施法请求可携带的可空落点），地面点选到具体目标的解析/消费由上层 AI 或玩家辅助施法负责，不属于框架未实现项；已核实"至少一处生产装配根默认接入调用链"的**已实现且默认接线**能力（如上述四项、`target.chain` 形状范围目标查询）不再列入"能力边界"表格本体，归档在该节"已修复历史项"小节。逐项源码锚点与当前完整条目数以 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)「能力边界与未默认接入能力索引」一节的表格为准（本行不重复维护具体数字，避免与该表更新脱节）。

## 顶层目录结构

```
.github/workflows/      GitHub Actions 持续集成工作流（ci.yml + release.yml，见"持续集成"一节）
.githooks/              版本化 git 钩子（pre-commit，见"提交前钩子"一节）
architecture/          架构文档集（已定稿），本仓库唯一的规范来源；00~14 号文档 + adr/（19 条 ADR）+ 落地计划/ + 选型/
core/                  L0~L4 纯逻辑类库，零引擎依赖，目标框架 .NET Standard 2.1
  foundation/            L0 基础层：event_bus、rng、expr、data_registry、sim_loop、save_system、input_map、l10n、display_info、scene_router、hook_registry、app_lifecycle
  numbers/               L1 数值层：stat_block、power_set、progression、archetype、faction
  rules/                 L2 规则层：skill、combat、targeting、ai
  carriers/              L3 载体层：item、creature、gobj、summon
  gameplay/              L4 玩法层：loot、quest、dialog、encounter、difficulty、achievement、economy、world_state、area_trigger、spawn、death（死亡复活三策略执行主体）
presentation/           L5 表现层的引擎无关部分（Presentation.Common：渲染/相机/UI 数据绑定、反馈绑定、VFX/SFX 播放体系、纸娃娃合成与动画状态机/程序动画原语等，均不依赖具体引擎）
adapters/
  stub/                  桩适配层（Adapters.Stub，"无头适配层"），纯 .NET 实现；供 xUnit 测试/CI 驱动，同时是框架正式交付物之一（ADR-0018 决策 3）——随构建产物分发（zip 快照 `adapters/headless/`、私服第四个包 `com.gamefoundation.adapter.headless`），供内容编辑器等无头宿主使用
  unity/                 Unity 6 LTS 工作台工程；真正的框架交付物是内嵌 UPM 包 adapters/unity/Packages/com.gamefoundation.adapter.unity/
games/_template/        游戏层骨架模板（本地包 com.gamefoundation.game-template），新游戏复制本目录改名接入；真实游戏代码放各自仓库
data/_sample/           框架自测/校验器自测用的示例数据表，不代表任何真实游戏内容；真实游戏数据放各自仓库的 data/<game>/
assets/_placeholder/    灰盒竖切用的通用占位资产包（精灵、特效、音效、音乐、地图分层图、字体等源素材），随版本快照一并交付
assets/_sample/         由 toolchain/import_sample_assets.py 驱动资产导入工具真实产出并提交入库的样例资产（消费 assets/_placeholder 源素材生成），供 data/_sample 的 display/vfx/sfx/world 四张表引用；改了 assets/_placeholder 源素材或需修复 data/_sample 引用时重跑该脚本幂等重新生成，见 toolchain/README.md"data/_sample 的资产来源"一节
toolchain/              校验、构建、资产导入等跨游戏 Python 工具链（validate_data.py、import_assets.py 等）；get_framework.ps1 是游戏侧按版本号引用本框架的工具（zip 通道），见"版本与发布"一节
  registry/              私服（Verdaccio 注册表）交付通道：本机/局域网内起一个私有包仓库，发布四个可发布包（游戏侧按版本号依赖其中三个，第四个包按需自取），与 zip 通道并存，见 toolchain/registry/README.md
  sync_package_content.ps1  私服通道配套：把游戏工程解析到的 com.gamefoundation.framework-data 包内容同步到该工程的 StreamingAssets/TextMesh Pro
editor/                内容编辑器产品文档（markdown + 离线 HTML）；编辑器代码不在本仓库：基础套件与模板在独立的编辑器项目，编辑器实例随各游戏仓库走，见 editor/README.md
dist/<version>/         build.ps1 -Dist 产出的版本快照（构建产物，.gitignore，不入库，可由源码重建）；-Release/-Zip 额外产出 ws-game-<version>.zip/.lock
VERSION                 单一版本源（纯文本版本号，如 0.2.0），两个 package.json、CHANGELOG.md、dist 快照均以此为准，见"版本与发布"一节
CHANGELOG.md             变更日志（Keep a Changelog 风格），发布时随 VERSION 一并更新
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
| `powershell -File build.ps1 -Dist <version>` | 额外把适配层包、`games/_template`、`toolchain`（不含 `.venv`/`__pycache__`/`registry`/`node_modules`/`bin`/`obj`）、`assets/_placeholder`、`data/_framework`、`adapters/headless`（无头适配层 DLL + README，ADR-0018 决策 3）打成一份版本快照 `dist/<version>/`，并生成扩展后的 `MANIFEST.txt`；同时无条件额外组装私服交付通道的四个包到 `dist/<version>/packages/{四个包名}/` 并 `npm pack` 出四个 `.tgz`（见下方"版本与发布"一节"私服通道"） |
| `powershell -File build.ps1 -Dist auto` | 同上，但版本号不由调用方指定，改为读取仓库根 `VERSION` 文件当前内容 |
| `powershell -File build.ps1 -Dist <version> -Zip` | 在 `-Dist` 基础上额外打 `dist/ws-game-<version>.zip` + `dist/ws-game-<version>.lock`，不做任何版本号写回/提交/打标签（`.github/workflows/release.yml` 用这条路径） |
| `powershell -File build.ps1 -Release <version> [-DryRun] [-Publish] [-ReleaseSkipUnity] [-PublishRegistry [-RegistryUrl <url>]]` | 完整发布流程：校验 → 写回版本号 → 全量门禁 → 提交 → 打包 + zip + lock + 四个 npm 包（打包完成自检 `git_commit` 指向发布提交） → 打标签；`-PublishRegistry` 独立于 `-Publish` 控制是否额外 `npm publish` 四个包到私服；见下方"版本与发布"一节 |

同步与打包均按文件哈希比较、只处理变化的文件；`-Dist` 打的快照不入库，可随时由源码重新生成；`-Dist` 传入的版本号必须形如 `X.Y.Z`（三段纯数字），格式非法直接报错退出。

### Unity 工作台（命令行跑测试）

先跑过一次 `build.ps1`（至少 `-SyncContent`），否则内容数据集与占位资源不会同步到 Unity 工程。

```powershell
Unity.exe -batchmode -nographics -projectPath adapters\unity -runTests -testPlatform EditMode -testResults <输出目录>\editmode.xml -logFile <输出目录>\editmode.log
Unity.exe -batchmode                -projectPath adapters\unity -runTests -testPlatform PlayMode -testResults <输出目录>\playmode.xml -logFile <输出目录>\playmode.log
```

`-runTests` 不要再加 `-quit`（两者同传会在测试真正跑起来前提前退出）；`-nographics` 只用于 EditMode，PlayMode 需要真实渲染/输入子系统，不能加。独立版 Windows 命令行构建、灰盒/Shell 场景重建、独立版无头冒烟等更详细的命令与已知限制，见 [adapters/unity/README.md](adapters/unity/README.md) 与 [adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md](adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md)。

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

默认版本号读仓库根 `VERSION`、工作目录落在系统临时目录下的 `gf_consumer_smoke\`（每次运行前清空
重建）；`-DistVersion`/`-WorkDir`/`-UnityExe`/`-SkipCleanWorkDir` 可覆盖，见该脚本头注释。
`check.ps1` 默认会跑这一步（`-SkipConsumer` 跳过，`-SkipUnity` 时一并跳过），是 11 号文档"提交
门槛清单"的一部分。

## `check.ps1`（一键门禁，仓库根，见 11_工程规范与测试.md 第 8 节）

依次跑：`.NET` 构建 + 测试（六工程，含性能基线）→ 数据校验（合并根 + `data/_framework` 框架根单独完整校验）→ 事件常量/占位资产生成器一致性检查（`--check`，只读）→ `toolchain` 自身的 Python 测试 → 两道禁用词扫描（全仓库不出现具体游戏代号；`architecture` 正文不出现具体引擎/语言/框架/工具名）→ 版本一致性（`VERSION`、两个 `package.json` 与 `CHANGELOG.md`）→ `build.ps1 -SkipTests` 同步 DLL → 包清单一致性（私服交付通道四个 npm 包版本号 + `npm pack --dry-run` 排除规则，见"版本与发布"一节"私服通道"；排在"同步 DLL"之后是硬性依赖——它跑的 `build.ps1 -SyncOnly` 要求六个核心 DLL 已经构建在 `bin\` 下，见 check.ps1 该步骤判断记录）→ Unity 编译检查 → Unity EditMode/PlayMode 测试 → 独立版构建 + 两种无人值守冒烟（`-gf-smoke` 连续模式默认流程、`-gf-smoke-discrete` 离散模式链路）→ 消费方演练（`toolchain/consumer_smoke.ps1`，见下一节）。每步单独计时与判定，最后打印一张汇总表；任一步失败，整体以非 0 退出码结束。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1              # 全量（含 Unity 相关步骤与消费方演练）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipUnity   # 跳过 Unity 相关步骤与消费方演练（Unity 编辑器被占用/未装时用）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipSmoke   # Unity 独立版仍构建，只跳过两种无人值守冒烟子步骤
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipConsumer # 跳过消费方演练这一步（其余 Unity 步骤仍跑）
```

`-ArtifactsPath <dir>` 可覆盖 `dotnet`/Unity 产物落地目录（默认 `bin\_check_artifacts`，已被 `.gitignore` 的 `bin/` 规则忽略）；`-UnityExe <path>` 可显式指定 Unity 可执行文件路径（默认按 Unity Hub 常见安装位置猜测，找不到则要求显式传参）。

`check.ps1` 自身只读跑校验/测试/构建、不写仓库文件，但 Unity 编辑器进程本身会在内容确有变化时重写 `adapters/unity/ProjectSettings/ProjectSettings.asset`，以及在包清单内容确有变化时改写 `adapters/unity/Packages/manifest.json`/`packages-lock.json`（与本脚本无关；触发条件是内容确有变化，不改内容的编译检查/EditMode 测试不会复现，独立版构建这类步骤会）。三者 Unity 写出的行尾实测是 LF（2026-09-07 二次实测勘误，此前误判为 CRLF；实测方法见 `.gitattributes` 对应例外条目上方判断记录）。根 `.gitattributes` 已为这三类路径显式声明 `eol=lf`（与 Unity 实际写出的一致），使 Unity 批处理跑完后工作树始终保持干净，不再依赖运行机器本地的 `core.autocrlf` 配置。

`check.ps1` 另有一步"版本一致性"（不需要 `-Dist`，`-SkipUnity` 下同样会跑）：只读比较仓库根 `VERSION` 文件与两个 `package.json`（`adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json`、`games/_template/package.json`，含后者对适配层包的依赖版本号）是否一致；并校验根 `CHANGELOG.md` 含 `VERSION` 对应版本号的条目（形如 `## [X.Y.Z]`）或存在 `## [Unreleased]` 段，四处任一处漏改都会让这一步失败。

`check.ps1 -Quick`（工程收尾 K 新增，供 `.githooks/pre-commit` 调用）：只跑 dotnet build/test、两道数据校验、事件常量一致性检查、禁用词扫描、版本一致性这几步"秒级能跑完"的子集，跳过占位资产生成器检查、`toolchain` 自身 pytest、`build.ps1 -SkipTests` 同步、包清单一致性（私服交付通道新增，需要跑一遍 `build.ps1 -SyncOnly -Dist auto` + `npm pack`，与"build.ps1 -SkipTests 同步"同一类耗时构建期动作，且依赖后者先把 DLL 构建到 `bin\` 下，故排在其之后）、全部 Unity 相关步骤与消费方演练；与 `-SkipUnity` 可以同传但没有必要（`-Quick` 本身已经不跑 Unity 相关任何一步）。资产导入工具交叉校验（`import_assets.py check --dataset _sample`，全量交叉校验 sprite/vfx/sfx/world 四域，只比对文件是否存在、不读图片，秒级完成，见 [toolchain/README.md](toolchain/README.md)"`data/_sample` 的资产来源"一节）不属于可跳过的慢步骤，`-Quick` 下同样会跑。目标总用时 30 秒左右（视本机是否需要重新编译而定），供提交前钩子做"能拦住的先拦住，剩下的交给 CI/手工全量 `check.ps1`"这一级快速把关，不能替代完整门禁。

`check.ps1 -Il2cpp`（工程收尾 K 新增，默认不跑，因为耗时数分钟到十几分钟）：额外跑一遍 IL2CPP 脚本后端的独立版构建 + 两种无人值守冒烟（`-gf-smoke`/`-gf-smoke-discrete`），验证核心类库自写的零依赖 JSON 读写器等纯逻辑代码在 AOT 编译（无反射兜底）下的真实可运行性，而不是只靠 Mono 后端的默认独立版构建自证；见 [adapters/unity/README.md](adapters/unity/README.md)"IL2CPP 发布路径验证"一节与 [architecture/选型/01_引擎与语言选型评估.md](architecture/选型/01_引擎与语言选型评估.md) 补充的"发布形态验证"一节（实测数据、与 Mono 的耗时/体积对比）。`-Il2cpp` 与 `-SkipUnity` 互斥（`-SkipUnity` 优先，`-Il2cpp` 不生效）；可与 `-SkipConsumer`/`-SkipSmoke` 同传。

## 持续集成（GitHub Actions）

`.github/workflows/ci.yml`：`push` 到 `main` 与任意 `pull_request` 时，在 `windows-latest` 运行器上跑 `check.ps1 -SkipUnity`（安装 .NET 8.0.x SDK 与 Python 3.12 + `toolchain/requirements.txt` 后执行；缓存 NuGet 包与 pip 依赖），并把控制台输出与门禁产物日志上传为 artifact。CI 不跑 Unity 相关四步与消费方演练——托管运行器既没有装 Unity，也无法激活个人版 Unity 授权，这部分职责仍由本机全量 `check.ps1`（含可选的 `-Il2cpp`）承担，见上一节。

`.github/workflows/release.yml`：推送形如 `v*` 的标签时触发（也支持 `workflow_dispatch` 手动指定 `tag` 输入重跑，用于对已推送的标签重新验证本工作流本身），跑一遍快速门禁（`check.ps1 -SkipUnity -Quick`，作为标签指向的提交未被意外改动的交叉验证——完整门禁已经在本机 `build.ps1 -Release` 第 5 步跑过）。附件策略是"本机已验证的产物优先"：先用 `gh release view` 查该标签对应 Release 是否已经有 `ws-game-<version>.zip`（即本机 `build.ps1 -Release -Publish` 已经上传过、且已在含 Unity 的完整门禁下验证过）——已有则跳过打包与上传，不覆盖（托管运行器重新构建的 DLL 因确定性构建仍嵌入 checkout 路径而哈希不同，覆盖会让 Release 附件与私服包不一致）；没有（例如只推了 tag、未传 `-Publish`）才用 `build.ps1 -SyncOnly -Dist <标签去掉 v 前缀> -Zip` 重新打包出 `dist/ws-game-<version>.zip`/`.lock` 并上传为该标签对应 GitHub Release 的附件（Release 不存在则新建），作为兜底路径。

## 提交前钩子（`.githooks/`）

仓库自带一份版本化的 `git` 钩子目录 `.githooks/`（`pre-commit` 调用 `check.ps1 -SkipUnity -Quick`，见上一节），默认不生效（`git` 的 `core.hooksPath` 默认指向 `.git/hooks/`，不会自动读取仓库内任意目录）。首次克隆后按需安装：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\install_hooks.ps1
```

该脚本把 `git config core.hooksPath` 指向 `.githooks/`（幂等，重复跑不报错）；`-Uninstall` 还原为默认值。安装后每次 `git commit` 前会自动跑一遍 `check.ps1 -SkipUnity -Quick`，未通过则本次提交被拦截（终端打印失败明细，同 `check.ps1` 汇总表）；紧急情况需要跳过时用 `git commit --no-verify`（不建议常态化使用）。

## 版本与发布

### 单一版本源

版本号的单一来源是仓库根的 `VERSION` 文件（纯文本，如 `0.2.0`，UTF-8 无 BOM）。版本号遵循语义化版本 `MAJOR.MINOR.PATCH`：MAJOR 表示不兼容变更（走 ADR 审批的契约签名变化、存档格式不兼容、数据表字段删改），MINOR 表示向后兼容的新增能力，PATCH 表示缺陷修复与文档勘误（判据见 [11_工程规范与测试.md](architecture/11_工程规范与测试.md) 第 7 节）。`adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json`、`games/_template/package.json` 两个 `package.json` 的 `version` 字段（含 `games/_template` 对适配层包的依赖版本号）与仓库根 `CHANGELOG.md`（[Keep a Changelog](https://keepachangelog.com/) 风格）需要与 `VERSION` 同步，`check.ps1` 的"版本一致性"步骤会校验这几处是否一致（见上一节）；日常开发中的变更先累积到 `CHANGELOG.md` 的 `## [Unreleased]` 段，发布时由 `build.ps1 -Release` 归档为对应版本号的条目。

### 发布不可变

每一个已发布的版本号一旦打了标签即视为定版，不得就地修改或用同一版本号重新打包替换；发现问题一律发新的 PATCH 版本修复，不兼容的修复经维护分支处理（见下方"维护分支与 PATCH 发布"）。

### `build.ps1 -Release`：完整发布流程

```powershell
powershell -File build.ps1 -Release 1.0.0                                    # 完整发布：校验 → 写回版本号 → 全量门禁 → 打包+zip+lock → 提交 → 打标签
powershell -File build.ps1 -Release 1.0.0 -ReleaseSkipUnity                  # 同上，但门禁跳过 Unity 相关步骤（没装 Unity 的机器）
powershell -File build.ps1 -Release 1.0.0 -DryRun -ReleaseSkipUnity          # 只跑校验+打包，不改任何源码文件、不提交、不打标签（产物名带 -dryrun 后缀）
powershell -File build.ps1 -Release 1.0.0 -Publish                          # 打完标签后自动执行 git push 与创建 GitHub Release
```

流程内部依次做：

1. 校验版本号格式，且必须严格大于 `VERSION` 当前值（语义化版本数值比较）。
2. 校验 `git status` 干净（工作树不能有未提交改动）。
3. 校验 `CHANGELOG.md` 已存在 `## [X.Y.Z]` 条目（没有则报错，提示先补齐变更记录）。
4. 把版本号写回 `VERSION`、两个 `package.json` 与 `adapters/unity/Packages/packages-lock.json`（`com.gamefoundation.game-template` 条目下对适配层包依赖版本号的 UPM 镜像字段；`-DryRun` 时跳过这一步，不触碰任何源码文件）。
5. 跑一遍 `check.ps1`（默认全量，`-ReleaseSkipUnity` 传 `-SkipUnity` 给它）。
6. 非 `-DryRun` 时：门禁通过后立即提交 `VERSION`/两个 `package.json`/`packages-lock.json`/`CHANGELOG.md`（提交信息 `发布 <ver>`）——先于下一步打包，使打包阶段 `git rev-parse HEAD` 就是这次发布提交本身、工作树干净。
7. 打包 `dist/<ver>/`、`dist/ws-game-<ver>.zip`（zip 内顶层目录 `ws-game-<ver>/`）与 `dist/ws-game-<ver>.lock`（版本号、`git_commit`、六个核心 DLL 的 sha256）；非 `-DryRun` 时打包完成后自检 `git_commit` 必须等于上一步的发布提交且不带 `-dirty` 后缀，不满足则报错退出（此时提交已产生但未打标签，按打印的提示 `git reset --soft` 回退后修复重跑）；自检通过后打带注释标签 `v<ver>`（标签信息取 `CHANGELOG.md` 该版本条目正文），并打印后续需要人工/设计层执行的两条命令：

   ```powershell
   git push origin <当前分支> refs/tags/v<ver>
   gh release create v<ver> dist/ws-game-<ver>.zip dist/ws-game-<ver>.lock --title v<ver> --notes-file dist/release-notes-<ver>.txt
   ```

   `<当前分支>` 取自 `git rev-parse --abbrev-ref HEAD`（`-Release` 全程不切换分支，就是打标签所在的那个分支：主线发布是 `main`，维护分支 PATCH 发布是 `release/X.Y.x`），只推本次新建的这一个标签（不带 `--tags` 全量推送）——维护分支上跑 `-Release`/`-Publish` 不会把 `main` 隐式往前推、也不会把本机其它未推送的本地标签一并带出去。传 `-Publish` 则自动执行这两条命令；省略时只打印，由人工确认后自行运行（`.github/workflows/release.yml` 在标签推送后会检查 Release 是否已有对应 zip 附件，已有则跳过重新打包上传，不覆盖本机已验证的产物；没有才走它自己的兜底打包上传路径，见"持续集成"一节）。若本次版本号的 MAJOR 或 MINOR 段发生变化，额外打印建议的维护分支创建命令。

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

游戏侧不直接引用框架仓库的开发目录，而是按版本号引用一份发布产物快照：`toolchain/get_framework.ps1 -Version <ver> -Target packages` 从 GitHub Release 拉取 `ws-game-<ver>.zip`/`.lock`，校验六个核心 DLL 的哈希与锁文件一致（锁文件存在 `headless_dlls` 字段时一并校验无头适配层 DLL 的哈希，ADR-0018 决策 3；老锁文件没有该字段时跳过并提示，向后兼容）后解压到 `packages/ws-game-<ver>/`，并在游戏仓库根写入/校验 `ws-game.lock`；`-FromLocalDist <本机 zip 路径>` 可离线来源（校验规则不变）。这是 zip 快照通道；私服（按版本号依赖）是并存的第二条通道，见下一节"私服通道"。完整的四条消费通道、五条多游戏共用规则与升级步骤见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md) 第 3.5 节；接入步骤见 [architecture/13_新游戏接入指南.md](architecture/13_新游戏接入指南.md) 第 1 节。

### 私服通道

除 zip 快照通道外，框架同时提供一条私服（[Verdaccio](https://verdaccio.org/)，npm 兼容协议）通道：把框架拆成四个可独立按版本号依赖的包（`com.gamefoundation.adapter.unity`/`com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`/`com.gamefoundation.adapter.headless`——第四个包 ADR-0018 决策 3 新增，随框架版本号发布但不是 Unity 依赖，不写入游戏工程 `Packages/manifest.json` 的 `dependencies`，供内容编辑器等无头宿主按需 `npm install`），发布到私有包仓库，游戏侧 Unity 工程用作用域注册表（`scopedRegistries.scopes: ["com.gamefoundation"]`）依赖前三个包。两条通道打包内容一致（同一次 `build.ps1 -Dist`/`-Release` 产出），互不排斥，可任选其一或两者都配。完整设计、四个包内容、两条通道取舍见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md) 第 3.5.1 节；快速开始、配置细节见 [toolchain/registry/README.md](toolchain/registry/README.md)。

```powershell
# 起私服（本机，默认 127.0.0.1:4873）
powershell -File toolchain\registry\start_registry.ps1 -Detach
# 无人值守建发布账号 + 令牌
powershell -File toolchain\registry\init_publisher.ps1
# 发布（-Release 已隐含打包四个 .tgz，-PublishRegistry 额外发到私服）
powershell -File build.ps1 -Release <version> -Publish -PublishRegistry

# 游戏侧：生成/更新 Packages/manifest.json 片段 + ws-game.lock
powershell -File toolchain\get_framework.ps1 -Version <version> -FromRegistry
# 首次解析完包后，把 framework-data 包内容同步到工程 StreamingAssets/TextMesh Pro
powershell -File toolchain\sync_package_content.ps1 -UnityProjectPath <你的 Unity 工程>
```

## 新游戏如何消费本框架

原则：框架仓库是被依赖方，任何游戏不进入框架仓库。新游戏 = 自己目录里的一个 Unity 工程 + `data/` + `assets/` + 自己的设计文档与 git 仓库，按版本号引用框架的一份发布产物快照（经 `toolchain/get_framework.ps1` 拉取校验后 `file:` 相对路径引用，见上方"版本与发布"一节"游戏侧引用与升级"）。完整的四条消费通道、五条多游戏共用规则与升级步骤，见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md) 第 3.5 节；从"选引擎"到"跑验收"的完整立项步骤与检查表见 [architecture/13_新游戏接入指南.md](architecture/13_新游戏接入指南.md)。

## 文档入口

- 架构文档集导读（阅读顺序、强制约束、文件清单）：[architecture/README.md](architecture/README.md)
- ADR 索引（17 条架构决策记录）：[architecture/adr/README.md](architecture/adr/README.md)
- 技术选型、工程结构、分工与分阶段落地计划、落地进度记录：[architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)
- 具体技术选型细节（引擎、语言、工具链等，`architecture/00~14` 与 `adr/` 本身不出现这些名字）：`architecture/选型/`

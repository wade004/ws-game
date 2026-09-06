# ws-game

[![CI](https://github.com/wade004/ws-game/actions/workflows/ci.yml/badge.svg)](https://github.com/wade004/ws-game/actions/workflows/ci.yml)

本仓库是「游戏技术基础架构」框架仓库：技术无关、游戏无关，面向后续所有游戏。仓库本身不承载任何具体游戏的开发与内容扩展，也不出现任何具体游戏代号。

`architecture/` 是已定稿的架构文档集，是本仓库唯一的规范来源，入口见 [architecture/README.md](architecture/README.md)；技术选型、工程结构、分工与分阶段落地计划见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)。

> 落地状态：阶段 0～5（环境骨架、L0 基础层、L1+L2 数值与规则、L3+L4 载体与玩法、Unity 适配层+表现层+UI 套件、美术管线与资产规格）均已完成，详见落地计划文档末节「落地进度记录」。

## 顶层目录结构

```
.github/workflows/      GitHub Actions 持续集成工作流（ci.yml，见"持续集成"一节）
.githooks/              版本化 git 钩子（pre-commit，见"提交前钩子"一节）
architecture/          架构文档集（已定稿），本仓库唯一的规范来源；00~14 号文档 + adr/（16 条 ADR）+ 落地计划/ + 选型/
core/                  L0~L4 纯逻辑类库，零引擎依赖，目标框架 .NET Standard 2.1
  foundation/            L0 基础层：event_bus、rng、expr、data_registry、sim_loop、save_system、input_map、l10n、display_info、scene_router、hook_registry、app_lifecycle
  numbers/               L1 数值层：stat_block、power_set、progression、archetype、faction
  rules/                 L2 规则层：skill、combat、targeting、ai
  carriers/              L3 载体层：item、creature、gobj、summon
  gameplay/              L4 玩法层：loot、quest、dialog、encounter、difficulty、achievement、economy、world_state、area_trigger、spawn、death（死亡复活三策略执行主体）
presentation/           L5 表现层的引擎无关部分（Presentation.Common：渲染/相机/UI 数据绑定、反馈绑定、VFX/SFX 播放体系、纸娃娃合成与动画状态机/程序动画原语等，均不依赖具体引擎）
adapters/
  stub/                  桩适配层（Adapters.Stub），纯 .NET 实现，专供 xUnit 测试与 CI，不对外发布
  unity/                 Unity 6 LTS 工作台工程；真正的框架交付物是内嵌 UPM 包 adapters/unity/Packages/com.gamefoundation.adapter.unity/
games/_template/        游戏层骨架模板（本地包 com.gamefoundation.game-template），新游戏复制本目录改名接入；真实游戏代码放各自仓库
data/_sample/           框架自测/校验器自测用的示例数据表，不代表任何真实游戏内容；真实游戏数据放各自仓库的 data/<game>/
assets/_placeholder/    灰盒竖切用的通用占位资产包（精灵、特效、音效、音乐、地图分层图、字体等源素材），随版本快照一并交付
assets/_sample/         由 toolchain/import_sample_assets.py 驱动资产导入工具真实产出并提交入库的样例资产（消费 assets/_placeholder 源素材生成），供 data/_sample 的 display/vfx/sfx/world 四张表引用；改了 assets/_placeholder 源素材或需修复 data/_sample 引用时重跑该脚本幂等重新生成，见 toolchain/README.md"data/_sample 的资产来源"一节
toolchain/              校验、构建、资产导入等跨游戏 Python 工具链（validate_data.py、import_assets.py 等）
editor/                游戏内容编辑器（Windows 桌面程序）：docs/ 产品文档（markdown + 离线 HTML）；实现尚未开始
dist/<version>/         build.ps1 -Dist 产出的版本快照（构建产物，.gitignore，不入库，可由源码重建）
VERSION                 单一版本源（纯文本版本号，如 0.2.0），两个 package.json 与 dist 快照均以此为准，见"版本与快照"一节
Core.sln                六个核心类库 + 六个测试工程的 .NET 解决方案
build.ps1               DLL 同步、内容同步、版本快照打包脚本（PowerShell 5.1 兼容）
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
| `powershell -File build.ps1 -Dist <version>` | 额外把适配层包、`games/_template`、`toolchain`（不含 `.venv`/`__pycache__`）、`assets/_placeholder`、`data/_framework` 打成一份版本快照 `dist/<version>/`，并生成扩展后的 `MANIFEST.txt`（见下方"版本与快照"一节） |
| `powershell -File build.ps1 -Dist auto` | 同上，但版本号不由调用方指定，改为读取仓库根 `VERSION` 文件当前内容 |

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

依次跑：`.NET` 构建 + 测试（六工程，含性能基线）→ 数据校验（合并根 + `data/_framework` 框架根单独完整校验）→ 事件常量/占位资产生成器一致性检查（`--check`，只读）→ `toolchain` 自身的 Python 测试 → 两道禁用词扫描（全仓库不出现具体游戏代号；`architecture` 正文不出现具体引擎/语言/框架/工具名）→ 版本一致性（`VERSION` 与两个 `package.json`）→ `build.ps1 -SkipTests` 同步 DLL → Unity 编译检查 → Unity EditMode/PlayMode 测试 → 独立版构建 + 两种无人值守冒烟（`-gf-smoke` 连续模式默认流程、`-gf-smoke-discrete` 离散模式链路）→ 消费方演练（`toolchain/consumer_smoke.ps1`，见下一节）。每步单独计时与判定，最后打印一张汇总表；任一步失败，整体以非 0 退出码结束。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1              # 全量（含 Unity 相关步骤与消费方演练）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipUnity   # 跳过 Unity 相关步骤与消费方演练（Unity 编辑器被占用/未装时用）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipSmoke   # Unity 独立版仍构建，只跳过两种无人值守冒烟子步骤
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipConsumer # 跳过消费方演练这一步（其余 Unity 步骤仍跑）
```

`-ArtifactsPath <dir>` 可覆盖 `dotnet`/Unity 产物落地目录（默认 `bin\_check_artifacts`，已被 `.gitignore` 的 `bin/` 规则忽略）；`-UnityExe <path>` 可显式指定 Unity 可执行文件路径（默认按 Unity Hub 常见安装位置猜测，找不到则要求显式传参）。

`check.ps1` 另有一步"版本一致性"（不需要 `-Dist`，`-SkipUnity` 下同样会跑）：只读比较仓库根 `VERSION` 文件与两个 `package.json`（`adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json`、`games/_template/package.json`，含后者对适配层包的依赖版本号）是否一致，三处任一处漏改都会让这一步失败。

`check.ps1 -Quick`（工程收尾 K 新增，供 `.githooks/pre-commit` 调用）：只跑 dotnet build/test、两道数据校验、事件常量一致性检查、禁用词扫描、版本一致性这几步"秒级能跑完"的子集，跳过占位资产生成器检查、`toolchain` 自身 pytest、`build.ps1 -SkipTests` 同步、全部 Unity 相关步骤与消费方演练；与 `-SkipUnity` 可以同传但没有必要（`-Quick` 本身已经不跑 Unity 相关任何一步）。资产导入工具交叉校验（`import_assets.py check --dataset _sample`，全量交叉校验 sprite/vfx/sfx/world 四域，只比对文件是否存在、不读图片，秒级完成，见 [toolchain/README.md](toolchain/README.md)"`data/_sample` 的资产来源"一节）不属于可跳过的慢步骤，`-Quick` 下同样会跑。目标总用时 30 秒左右（视本机是否需要重新编译而定），供提交前钩子做"能拦住的先拦住，剩下的交给 CI/手工全量 `check.ps1`"这一级快速把关，不能替代完整门禁。

`check.ps1 -Il2cpp`（工程收尾 K 新增，默认不跑，因为耗时数分钟到十几分钟）：额外跑一遍 IL2CPP 脚本后端的独立版构建 + 两种无人值守冒烟（`-gf-smoke`/`-gf-smoke-discrete`），验证核心类库自写的零依赖 JSON 读写器等纯逻辑代码在 AOT 编译（无反射兜底）下的真实可运行性，而不是只靠 Mono 后端的默认独立版构建自证；见 [adapters/unity/README.md](adapters/unity/README.md)"IL2CPP 发布路径验证"一节与 [architecture/选型/01_引擎与语言选型评估.md](architecture/选型/01_引擎与语言选型评估.md) 补充的"发布形态验证"一节（实测数据、与 Mono 的耗时/体积对比）。`-Il2cpp` 与 `-SkipUnity` 互斥（`-SkipUnity` 优先，`-Il2cpp` 不生效）；可与 `-SkipConsumer`/`-SkipSmoke` 同传。

## 持续集成（GitHub Actions）

`.github/workflows/ci.yml`：`push` 到 `main` 与任意 `pull_request` 时，在 `windows-latest` 运行器上跑 `check.ps1 -SkipUnity`（安装 .NET 8.0.x SDK 与 Python 3.12 + `toolchain/requirements.txt` 后执行；缓存 NuGet 包与 pip 依赖），并把控制台输出与门禁产物日志上传为 artifact。CI 不跑 Unity 相关四步与消费方演练——托管运行器既没有装 Unity，也无法激活个人版 Unity 授权，这部分职责仍由本机全量 `check.ps1`（含可选的 `-Il2cpp`）承担，见上一节。

## 提交前钩子（`.githooks/`）

仓库自带一份版本化的 `git` 钩子目录 `.githooks/`（`pre-commit` 调用 `check.ps1 -SkipUnity -Quick`，见上一节），默认不生效（`git` 的 `core.hooksPath` 默认指向 `.git/hooks/`，不会自动读取仓库内任意目录）。首次克隆后按需安装：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\install_hooks.ps1
```

该脚本把 `git config core.hooksPath` 指向 `.githooks/`（幂等，重复跑不报错）；`-Uninstall` 还原为默认值。安装后每次 `git commit` 前会自动跑一遍 `check.ps1 -SkipUnity -Quick`，未通过则本次提交被拦截（终端打印失败明细，同 `check.ps1` 汇总表）；紧急情况需要跳过时用 `git commit --no-verify`（不建议常态化使用）。

## 版本与快照

版本号的单一来源是仓库根的 `VERSION` 文件（纯文本，如 `0.2.0`，UTF-8 无 BOM）：升级版本号时只改这一处，`adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json`、`games/_template/package.json` 两个 `package.json` 的 `version` 字段（含 `games/_template` 对适配层包的依赖版本号）需要同步改成同一个值，`check.ps1` 的"版本一致性"步骤会校验这三处是否一致（见上一节）。

`build.ps1 -Dist <version>` 打快照时会把最终使用的版本号（显式传参，或 `-Dist auto` 时从 `VERSION` 读到的值）回写进 `dist/<version>/` 内两个 `package.json` 的 `version` 字段，并生成扩展后的 `MANIFEST.txt`，记录本次快照的可追溯信息（见 [11_工程规范与测试.md](architecture/11_工程规范与测试.md) 第 7 节"版本号必须可追溯到对应的架构文档版本与数据 schema 版本组合"）：

- `version`/`date`/`git_commit`（`git rev-parse --short HEAD`，打包时工作树不干净则追加 `-dirty`）；
- 各目录文件数（`[directory_file_counts]`）；
- `[architecture_docs]`：`architecture/0*.md`、`1*.md` 每篇文档标题里的版本号（如 `01_分层与依赖.md: v3`）——注意 `dist/` 本身不打包 `architecture/` 目录，这一节只是把"打这份快照时架构文档集处于哪个版本组合"记录下来，供事后核对；
- `[data_schemas]`：`data/_framework` 下每张表的 `table`/`schema_version`（`data/_sample` 不随 `dist` 分发，不列入）；
- `[core_assemblies]`：六个核心 DLL 的 sha256。

## 新游戏如何消费本框架

原则：框架仓库是被依赖方，任何游戏不进入框架仓库。新游戏 = 自己目录里的一个 Unity 工程 + `data/` + `assets/` + 自己的设计文档与 git 仓库，经 `file:` 相对路径引用框架某个 `dist/<version>/` 版本快照。完整的四条消费通道、五条多游戏共用规则与升级步骤，见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md) 第 3.5 节；从"选引擎"到"跑验收"的完整立项步骤与检查表见 [architecture/13_新游戏接入指南.md](architecture/13_新游戏接入指南.md)。

## 文档入口

- 架构文档集导读（阅读顺序、强制约束、文件清单）：[architecture/README.md](architecture/README.md)
- ADR 索引（16 条架构决策记录）：[architecture/adr/README.md](architecture/adr/README.md)
- 技术选型、工程结构、分工与分阶段落地计划、落地进度记录：[architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)
- 具体技术选型细节（引擎、语言、工具链等，`architecture/00~14` 与 `adr/` 本身不出现这些名字）：`architecture/选型/`

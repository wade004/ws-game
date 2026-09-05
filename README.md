# ws-game

本仓库是「游戏技术基础架构」框架仓库：技术无关、游戏无关，面向后续所有游戏。仓库本身不承载任何具体游戏的开发与内容扩展，也不出现任何具体游戏代号。

`architecture/` 是已定稿的架构文档集，是本仓库唯一的规范来源，入口见 [architecture/README.md](architecture/README.md)；技术选型、工程结构、分工与分阶段落地计划见 [architecture/落地计划/落地方案与分阶段计划.md](architecture/落地计划/落地方案与分阶段计划.md)。

> 落地状态：阶段 0～5（环境骨架、L0 基础层、L1+L2 数值与规则、L3+L4 载体与玩法、Unity 适配层+表现层+UI 套件、美术管线与资产规格）均已完成，详见落地计划文档末节「落地进度记录」。

## 顶层目录结构

```
architecture/          架构文档集（已定稿），本仓库唯一的规范来源；00~14 号文档 + adr/（16 条 ADR）+ 落地计划/ + 选型/
core/                  L0~L4 纯逻辑类库，零引擎依赖，目标框架 .NET Standard 2.1
  foundation/            L0 基础层：event_bus、rng、expr、data_registry、sim_loop、save_system、input_map、l10n、display_info、scene_router、hook_registry、app_lifecycle
  numbers/               L1 数值层：stat_block、power_set、progression、archetype、faction
  rules/                 L2 规则层：skill、combat、targeting、ai
  carriers/              L3 载体层：item、creature、gobj、summon
  gameplay/              L4 玩法层：loot、quest、dialog、encounter、difficulty、achievement、economy、world_state、area_trigger、spawn
presentation/           L5 表现层的引擎无关部分（Presentation.Common：渲染/相机/UI 数据绑定、反馈绑定、VFX/SFX 播放体系、纸娃娃合成逻辑等，均不依赖具体引擎）
adapters/
  stub/                  桩适配层（Adapters.Stub），纯 .NET 实现，专供 xUnit 测试与 CI，不对外发布
  unity/                 Unity 6 LTS 工作台工程；真正的框架交付物是内嵌 UPM 包 adapters/unity/Packages/com.gamefoundation.adapter.unity/
games/_template/        游戏层骨架模板（本地包 com.gamefoundation.game-template），新游戏复制本目录改名接入；真实游戏代码放各自仓库
data/_sample/           框架自测/校验器自测用的示例数据表，不代表任何真实游戏内容；真实游戏数据放各自仓库的 data/<game>/
assets/_placeholder/    灰盒竖切用的通用占位资产包（精灵、特效、音效、字体等），随版本快照一并交付
toolchain/              校验、构建、资产导入等跨游戏 Python 工具链（validate_data.py、import_assets.py 等）
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
| `powershell -File build.ps1` | 完整流程：`dotnet build/test` → 同步六个核心 DLL 到 Unity 适配层包 `Runtime/Plugins/Core/` → 同步 `data/_sample`/`assets/_placeholder` 到 Unity 工程 `StreamingAssets/GameFoundation/` |
| `powershell -File build.ps1 -SkipTests` | 同上，跳过 `dotnet test` |
| `powershell -File build.ps1 -SyncOnly` | 跳过 `dotnet build/test`，只做 DLL 同步 + 内容同步（要求此前至少完整 build 过一次） |
| `powershell -File build.ps1 -SyncContent` | 只做内容同步（跳过 `dotnet build/test` 与 DLL 同步）；只改了 `data/_sample`/`assets/_placeholder`、没改任何 C# 代码时的快速路径 |
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

## `check.ps1`（一键门禁，仓库根，见 11_工程规范与测试.md 第 8 节）

依次跑：`.NET` 构建 + 测试（六工程，含性能基线）→ 数据校验（合并根）→ 事件常量/占位资产生成器一致性检查（`--check`，只读）→ `toolchain` 自身的 Python 测试 → 两道禁用词扫描（全仓库不出现具体游戏代号；`architecture` 正文不出现具体引擎/语言/框架/工具名）→ 版本一致性（`VERSION` 与两个 `package.json`）→ `build.ps1 -SkipTests` 同步 DLL → Unity 编译检查 → Unity EditMode/PlayMode 测试 → 独立版构建 + `-gf-smoke` 无人值守冒烟。每步单独计时与判定，最后打印一张汇总表；任一步失败，整体以非 0 退出码结束。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1              # 全量（含 Unity 四步）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipUnity   # 跳过 Unity 相关四步（Unity 编辑器被占用/未装时用）
powershell -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipSmoke   # Unity 独立版仍构建，只跳过 -gf-smoke 冒烟子步骤
```

`-ArtifactsPath <dir>` 可覆盖 `dotnet`/Unity 产物落地目录（默认 `bin\_check_artifacts`，已被 `.gitignore` 的 `bin/` 规则忽略）；`-UnityExe <path>` 可显式指定 Unity 可执行文件路径（默认按 Unity Hub 常见安装位置猜测，找不到则要求显式传参）。

`check.ps1` 另有一步"版本一致性"（不需要 `-Dist`，`-SkipUnity` 下同样会跑）：只读比较仓库根 `VERSION` 文件与两个 `package.json`（`adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json`、`games/_template/package.json`，含后者对适配层包的依赖版本号）是否一致，三处任一处漏改都会让这一步失败。

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

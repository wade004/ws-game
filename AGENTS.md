# AGENTS.md（面向执行 agent 的派单规则单一来源）

本文件是派发给执行 agent 的重复硬性规则的单一来源。今后派单提示词只写"先读取并遵守 AGENTS.md"，
不再逐条复述本文件已有的规则；派单提示词只写该任务特有的目标、范围与验收标准。

## 0. 通用硬约束（每张派单都适用）

- **你就是执行者**：任务自己做完，不再转派子 agent，也不另派验收 agent；做完直接向派单方汇报。
- **代码注释或文档里写下的任何"已知限制 / 不覆盖的情形 / 边缘路径不处理"，必须逐条原样出现在汇报里**，由派单方决定是否接受；只写在注释里不汇报，等于把缺陷藏进代码（2026-09-26 第三十五批教训：冷加载路径不登记被写成注释里的"已知限制"，消费方首播即踩中）。
- **冷加载（资源异步加载完成）路径必须与热路径行为一致**：凡改表现层/资源相关代码，"首次引用、加载尚未完成、加载完成后的迟到回填"这条路径要和同步命中缓存的路径走同一出口、同一规则，并作为不变量用例的一支；把它写成"已知限制"一律退回（第三十五、四十四批各返工一轮）。
- **引擎侧 PlayMode 用例必须自带渲染隔离**：凡读像素、读场景对象、依赖相机取景的 PlayMode 用例，用专属 Layer + 相机 cullingMask 只看自己的对象，并在结束时还原；不得依赖套件里其它用例的清理是否完整（1.84.0 门禁：定向过滤 43/43 绿、全量 342 例里被别的用例残留场景污染而红）。
- **核心层移动/导航改动必须跑引擎侧移动 PlayMode 定向回归**：这类"跨层交互例外"已登记进 `toolchain/module_map.json`（模块 `unit` 的 `MovementTickHandler.cs`、模块 `engine_adapter` 的导航契约 → 引擎侧 `MovementStopAndBlockingPlayModeTests`，1.84.0 门禁因漏跑返工一轮），`pwsh check.ps1 -Changed main -SkipUnity` 的"本次判定"块会列出命中的例外与待跑的引擎分类，在主检出或路径足够短的工作树里执行。
- **返工修复未发布版本自己引入的回归时，不往 CHANGELOG `[Unreleased]` 加条目**（从未发布的缺陷对消费方不存在）。
- **提交用显式 pathspec**：`git commit -F <提交信息文件> -- <路径…>`（`-F` 在 `--` 之前）。禁 `git add -A`、
  `git add .`、`git commit -a`；禁 `--amend` 与任何历史改写——写错了用新提交订正。只提交本任务
  自己的改动，同一工作树里别人的未提交改动一律不碰、不还原。
- **提交信息一律 `git commit -F <UTF-8 无 BOM 文件> -- <路径…>`，不用 `-m` 写中文**：Bash 工具下 `-m` 带中文
  会按系统代码页乱码入库（`0d2eb79c` 就是实例，只能事后用新提交订正）。先把信息写进 scratchpad 里的 UTF-8
  无 BOM 文本文件（尾行署名照写），再用 `-F` 提交；提交后 `git log -1 --format=%B` 核对无乱码。纯 ASCII 信息
  同样走 `-F`，不留例外。
- **生成物不进 git**：回归证据、报告 JSON、日志、截图、场景副本、zip、构建产物一律只留本地并
  确保被 `.gitignore` 覆盖，不许提交。发现已入库的此类文件就地清掉（`git rm` + 忽略规则），
  不要新写"把证据提交上来"的检查或规则。工具顺手改动的无关文件（如被重存的场景）提交前还原。
  每轮全量回归只在 `REGRESSION_LOG.md` 追加一行（run_id / 通过或失败 / 对应提交）。
- **上游缺口不绕行**：依赖的上游（框架、库、别的会话负责的仓库）缺能力或有缺陷时，该功能停工
  并在汇报里写清楚；不硬编码替代、不自建平行机制、不"先绕着做以后再换"，也不去改上游仓库。
- **信任边界**：写域只有本仓库 `D:\workespace\ws-game`。其它仓库（消费方项目等）**只读**，
  一个字节都不许改。不向任何远端 push（发布与推送由主会话按发布流程做）。
- **提交、测试、构建、Unity 批处理一律前台执行，等它真正结束再汇报**：不许用后台运行、
  `Start-Process`、后台作业去跑它们，也不许汇报"还在后台跑、完成后接着做"。你的后台子进程会随
  你的回合结束被一起结束，派单方永远等不到结果（2026-09-25 一次提交因此卡死）。单条命令超过工具
  超时上限时，拆成多步前台执行，或把输出写进 scratchpad 日志后分段核对，不要转后台。汇报时必须
  已经拿到提交 sha 与实测输出；拿不到就如实写"未完成 + 卡在哪一步"，不许写成进行中。

## 1. 工作树与 git

- 每个任务开独立工作树，位置固定为短路径 `D:\wt\<name>`（不放系统临时目录/scratchpad：路径浅，Unity 步骤不触 MAX_PATH）：
  `git -C D:\workespace\ws-game worktree add D:\wt\<name> -b <branch> main`。**不得把 `dist` 复制进工作树**
  （整份约十几 GB）：ABI 基线由 `toolchain/abi_probe.ps1` 在工作树里找不到时自动回落到主检出的 `dist`，
  pytest 的真实 dist 产物用例同理；任务收尾 `git worktree remove D:\wt\<name>`。
- `dist/` 发版后自动瘦身：`build.ps1 -Release`（非 `-DryRun`）成功打完标签后会调用 `toolchain/prune_dist.ps1 -Apply`
  （保留当前与上一版本、ABI 基线 zip、全部 lock/release-notes，其余历史产物与全部 `-dryrun` 产物删除，需要时按标签
  `build.ps1 -Dist <ver>` 重建）；手动：`pwsh toolchain\prune_dist.ps1 -Apply`（先不带 `-Apply` 看清单）。
- 所有 git 命令用 `git -C "<工作树绝对路径>"`；其它命令须在同一个工具调用内 `Push-Location … Pop-Location`（PowerShell/Bash 工具调用之间，当前目录会重置回主树）。
- 提交前先打印 `git -C "<工作树>" rev-parse --show-toplevel` 核对确实在工作树内。
- **文件编辑工具（Edit/Write 等）一律传工作树的绝对路径**，禁止相对路径、禁止主检出路径。上一条讲的是
  命令与 `cd`，这一条讲的是编辑：本会话有两个 agent 命令侧守规矩、却把编辑落到了主检出，早期门禁因此
  测的是未改动的代码，属于假验证。
- **跑测试/门禁前先自证改动在工作树里**：`git -C "<工作树>" status --short` 应有改动，
  `git -C "D:\workespace\ws-game" status --short` 应为空。两者不符立刻停下搬运改动，不要继续跑。
- 误写主检出时，用反向补丁（`git diff` + `git apply -R`）把改动搬回工作树，**不要用 `checkout`/`reset --hard`**
  等破坏性命令还原主检出（主检出可能有主会话正在进行的工作）。
- 只 `git add` 明确列出的路径；绝不用 `-A`/`.`/`--force`/`reset --hard`/`stash`。
- 不 merge、不 push（这两步由主会话执行）。
- 不改 `VERSION`/`package.json`/`packages-lock.json`/`games/_template/package.json`（这些文件由 `build.ps1 -Release` 统一写回）。
- 提交署名尾行固定 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`，不按执行 agent 自己的模型改名。
- 同一工作树同一时间只允许一个 agent 提交，避免并发写冲突。
- **派单若会在 Unity 导入范围内新建文件**（`adapters/unity/Assets`、
  `adapters/unity/Packages/com.gamefoundation.adapter.unity`、`adapters/conformance`、
  `games/_template` 四个根）**，执行 agent 做完改动后不要自己提交**：新文件的 `.meta` 必须由
  真实 Unity 导入生成，而执行 agent 不得开 Unity，于是 meta 门禁会让分支提交不了自己（已连续
  两轮踩到：1.46.0 一次、1.48.0 一次）。正确做法：执行 agent 把改动 `git add` 暂存后停下汇报，
  由主会话搬到主检出、跑一次含 Unity 的门禁生成 `.meta`、连同改动一并正常提交。**不要用
  `--no-verify` 绕过**。
- **并行派单时，向有编号的列表追加条目要先声明编号可能撞车**：模块 README 的“判断记录”、
  `architecture/adr/` 编号、ADR 索引计数这类**同层级顺序追加**，git 自动合并**不会报冲突**，
  会把两条同号条目并排放进来。派单侧的做法：ADR 编号由主会话预先分配写进提示词；“判断记录”
  编号由执行 agent 照常写，主会话在合并后统一核对重号并重排。执行 agent 不必为此互相等待，
  但要在汇报里写明自己追加到了哪个编号。

## 1b. 分支与版本约定（2026-10-01 项目负责人拍板，对所有会话、所有人生效）

- **禁止直接在 `main` 上提交。** 任何改动（代码、文档、发布收口）都从 `main` 拉分支，在自己的分支上
  提交，再合并回 `main`。主检出 `D:\workespace\ws-game` 只用于合并、跑合并后的全量门禁与发布；
  日常改动一律在工作树分支里做。
- **分支命名**：
  - 需求变更：`feature/<需求名称>_<年月日>`，例 `feature/targeted-gate_20261001`。
  - 缺陷修复：`bugfix/<缺陷名称>_<年月日>`，例 `bugfix/cold-load-attach-key_20261001`。
  - **名称只用英文**（小写字母、数字、连字符），不含中文、不含空格；年月日为拉分支当天，八位数字。
  - 发布维护分支沿用既有 `release/X.Y.x`（§5），不套用上面两种前缀。
- **分支版本号**：格式 `<拉分支时 main 的版本号>_<分支名去掉 feature/ 或 bugfix/ 前缀>`，例
  `1.91.0_targeted-gate_20261001`。合并回 `main` 后，版本号按 `main` 当前版本升级（语义化版本规则，
  §5），格式 `<升级后版本号>_release`，例 `1.92.0_release`。
  - 现状：`VERSION` 与包版本保持语义化版本 `X.Y.Z`；`_<分支名>` / `_release` 标签由
    `toolchain/version_label.py` 自动推导（门禁开头与汇总末尾打印、写入程序集信息版本、发布说明首行），
    不手写进 `VERSION`（ADR-0127）。
  - 执行 agent 不改 `VERSION`（§1 既有规则不变）；合并后的版本号由 `build.ps1 -Release` 写回。
  - 分支名是否合规由门禁步骤「分支名规范」检查（`feature/`、`bugfix/` 前缀的分支不合 §1b 规范即 FAIL）。
- **合并前后都跑全量门禁**：合并到 `main` 之前，在分支上跑一次全量（含引擎侧）；合并之后在 `main` 上
  再跑一次全量。两次都在 `REGRESSION_LOG.md` 追加一行。这是 §4 回归分级里全量回归的第三种
  时刻；定向门禁（`check.ps1 -Changed`）只用于分支内的日常迭代，不替代合并前后的全量。
- **合并后记录怎么入库**：`main` 上的合并后全量把耗时写进被 .gitignore 覆盖的待领目录
  `timing/_pending/`（不写已跟踪文件，主检出保持干净，不挡 `build.ps1 -Release` 与 `git merge --ff-only`）。
  **下一条要合并的分支**在跑合并前全量之前，先在自己的工作树里运行 `python toolchain/claim_pending_records.py`
  把耗时领走（并入 `timing/<日期>_main.jsonl`、删除主检出里被领走的待领文件），并在 `REGRESSION_LOG.md` 补上
  上一次的合并后行，一起提交。理由：`main` 禁止直接提交；而为一行记录单独开分支，这个分支自己的合并前后全量又会产生新记录，无限递归。
- **只改文档与登记的分支不另跑合并前后全量**：分支 diff 仅限 `*.md`、`docs/`、`architecture/`、`timing/*.jsonl`（发布守卫的文档类路径
  `Test-DocsOnlyPath` 含这四类，`timing/*.jsonl` 是门禁自动写入的耗时记录，属登记数据）时，合并前跑定向门禁即可，不另跑合并前后全量；其正确性由发布守卫"其后只改文档类文件"规则与下一次全量
  （含 `-Release` 第 5 步，其默认形态是复用全量记录的定向门禁，见 §5）覆盖。理由：避免登记分支递归触发全量。
- **合并提交与下限文件的钩子档位（ADR-0156）**：合并提交的树与"有含 Unity 全量记录背书的第二父提交"逐字节相同时，提交钩子判 `MergeSkip` 不再重复跑快速全量；
  只改 `toolchain/gate_floors.json` 的提交只跑 `floors_pytest`（`test_gate_floors_logic.py`），不跑 toolchain 全量 pytest。判定规则见 `toolchain/_precommit_tiering_guard.ps1`
  与 `toolchain/module_map.json`，不要为此用 `--no-verify` 绕过。

## 1c. 耗时记录（2026-10-01 项目负责人拍板，每个任务必做）

- **每个任务都记录每个阶段、每个步骤的耗时**，写到仓库根 `timing/` 目录，一个任务一个文件：
  `timing/<年月日>_<分支名去前缀>.jsonl`（同一分支多次任务追加到同一文件）。
- 每行一条 JSON，字段固定：`task`（任务一句话）、`branch`、`phase`、`step`、`start`、`end`
  （ISO 8601 本地时间，精确到秒）、`seconds`、`result`（`PASS`/`FAIL`/`SKIP`/`DONE`）、`note`（可空）。
- `phase` 取固定值之一：`勘察`、`设计`、`编码`、`定向门禁`、`全量门禁`、`提交`、`汇报`、`等待`
  （等用户/等上游/排队等引擎）。`step` 自由命名但要稳定（同一动作每次同名），便于跨任务汇总。
- 门禁运行的逐步耗时由 `check.ps1` 在每次运行结束（通过或失败）自动追加到当前分支的任务文件：`phase` 定向
  模式记 `定向门禁`、否则 `全量门禁`，`step` 用步骤稳定 Id，另有一行 `_total` 记脚本总墙钟，start/end 是每步
  真实起止时间；`-NoTiming` 跳过（预提交钩子与 `build.ps1 -Release` 已带）。**人只补非门禁阶段**（勘察、设计、
  编码、提交、汇报、等待）。在 `main`/游离 HEAD 上运行时改写 gitignore 的 `timing/_pending/`（见 §1b 末条）。
  统计：`python toolchain/timing_report.py [--since --branch --phase --json --no-pending]`（默认含待领目录）。
- 这个目录**入库**，是「生成物不进 git」规则的明示例外：每行只有一条短记录，是统计的原始数据。
  日志全文仍不入库。
- 汇报里的「实测耗时」一律引用 `timing/` 里的行，不另抄一份。

## 2. 仓库硬性规则

- `architecture/00～14`、`architecture/adr/`、`数值设计/` 正文不出现任何引擎/语言/框架/工具名（`immunity` 例外）；具体技术名只允许写在 `architecture/选型/`、`architecture/落地计划/`、`toolchain/`、`adapters/`、`docs/`。
- 全仓库任何文件不出现具体游戏代号，用 `<game>` 或 `sample_*` 占位。
- 改动架构结论要先出 ADR；只是细节勘误按 `architecture/12_扩展与变更流程.md` §5 处理——版本号不变、变更记录写"勘误："、关联 ADR 列 "—" 或对应 ADR 编号。
- 每个模块 README 的"判断记录"要记录关键取舍及理由，不能只改代码不留痕。
- 新增含非 ASCII 字符的 `.ps1`/`.psm1` 脚本必须带 UTF-8 BOM（无 BOM 在目标机 ANSI 代码页下会被误读，已有专门门禁测试）。
- 新文件统一 LF 换行。

## 3. 代码规则

- ABI 只新增：新增重载、新增类型、默认接口成员、可选属性；不删改既有公开签名、不给既有构造/方法加参数、不删已废弃字段（要退役用 `[Obsolete]` 之类的元数据标注，不物理删除）**可选参数也算加参数**（编译期糖，物理签名变了，旧编译的消费方运行期抛 `MissingMethodException`）：要扩参就新增重载并原样保留旧签名转调。改了任何公开签名后必须自己单跑 `dotnet build Core.sln -c Release` + `pwsh toolchain/abi_probe.ps1`（单跑读 `core/*/bin/Release/` 默认输出，不读 `bin/_check_artifacts`），`RESULT=OK` 才算完。
- 不删测试、不放宽既有断言、不加重试掩盖问题——先复现缺陷再根治，不能只是让测试变绿。
- 保持确定性：不依赖字典枚举顺序、`GetHashCode`、系统时间、当前文化；浮点格式化固定用不变文化（`InvariantCulture`）。
- 运行时路径不静默降级；只读分析类入口在遇到阻断态时降级要显式标记（不能悄悄吞掉问题当作正常返回）。
- **测试里起 git 子进程必须走干净环境辅助函数**（`toolchain/tests/_git_env.py` 的 `git_env`/`run_git`/`init_temp_repo`），不得继承调用方的 `GIT_*` 变量（预提交钩子里跑 pytest 时会被注入 `GIT_DIR` 等，临时目录里的 `git init`/`config` 会作用到真实仓库，2026-10-01 事故）。

## 4. 构建与门禁

- `dotnet` 命令一律带 `--artifacts-path "<scratchpad>\build_<task>"`，不用仓库根默认路径，避免污染主树/工作树。
  **例外**：改动会编译进 `Runtime/Plugins/Core/` 六个核心 DLL 的代码（`core/`、`presentation/`、
  `adapters/stub/` 等，具体六个程序集见 `build.ps1` 的 `$CoreAssemblies`）、且接下来要跑 Unity
  侧行为（PlayMode/EditMode 测试、`build.ps1 -SyncOnly`/`check.ps1` 的 Unity 步骤）时，必须
  **不带 `--artifacts-path`、用默认路径**跑 `dotnet build`，再执行 `build.ps1 -SyncOnly` 完成
  DLL 同步——`build.ps1` 的同步步骤固定读取默认输出路径（`<程序集目录>\bin\$Configuration\
  netstandard2.1\`），带 `--artifacts-path` 构建产物落在别处，同步会读到默认路径下更早一次构建
  留下的陈旧 DLL，Unity 实际加载的是这份陈旧二进制而不是本次改动。只跑 `dotnet build`/`test`
  做门禁自检、不涉及 Unity 侧验证时，仍按前一条规则带 `--artifacts-path`。
  （build.ps1 现已对此加了陈旧检测：源 DLL 比对应源码目录下最新的 `.cs` 更旧时会直接报错终止，
  见判断记录 `build.ps1` 内 `Test-CoreAssemblyDllStale` 函数注释；但检测只能拦住"用错路径导致
  同步了旧产物"，不能替代这条规则本身。）
- **验证任何 Unity 侧行为前，必须先确认 DLL 与源码同步**：只切分支、只 `git checkout` 不会更新
  `Runtime/Plugins/Core/*.dll`（这是 gitignore 的构建产物，Unity 实际加载的是这份二进制副本，
  git 操作从不触碰它）；确认方式是按上一条重新构建+`-SyncOnly` 同步，而不是假设"代码已经是最新
  就代表 Unity 看到的也是最新"。详见
  `docs/复盘/排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md` 追加节。
- Unity 只允许通过 `build.ps1`/`check.ps1` 以 `-batchmode` 方式调用，不直接手工开 Unity 编辑器操作工作树。
- **不要在深层 scratchpad 工作树里跑 Unity 测试步骤**（EditMode/PlayMode/独立版构建/消费方演练）：
  `git worktree add` 到系统临时目录下的工作树，根路径比主检出深很多，`GameTemplateResidentTests.
  ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly` 用例运行期会把
  `data/game` 整棵目录树复制到 StreamingAssets 下一个带 32 位十六进制 GUID 的新目录，工作树根
  一深，复制出来的文件绝对路径就可能超过 Windows 260 字符 MAX_PATH——而 Mono/.NET 旧式路径 API
  在这种情况下抛的是 `DirectoryNotFoundException` 而不是 `PathTooLongException`，症状会伪装成
  "目录没建出来"，极易被误判为产品缺陷。`check.ps1` 已在 Unity 相关四步入口加了
  `Test-UnityWorkingTreePathLength` 快速失败守卫（`toolchain/_unity_path_length_guard.ps1`），
  超阈值会在跑任何 Unity 批处理之前直接报错终止并给出根路径长度/预估最长路径/怎么办；但更省
  时间的做法是根本不在深层工作树里跑这几步——Unity 侧验证交主会话在主检出（`D:\workespace\
  ws-game`）跑，或临时用路径足够短的工作树；工作树内跑 `check.ps1 -Quick`（隐含 `-SkipUnity`）
  等非 Unity 步骤不受影响。
- **回归分级（唯一口径，替换此前"每次改动跑全量"的旧条款——旧条款与本条不并存）**：
  - **日常切片**：以 `toolchain/module_map.json` 为准，切片级跑 `pwsh check.ps1 -Changed main -SkipUnity`
    （改动 → 影响集 T0～T3 → 只跑被触发的步骤与测试工程，规则见 ADR-0126）；引擎侧待跑的 PlayMode
    分类由判定块给出，在主检出或路径足够短的工作树里跑（见本节 MAX_PATH 条；深层 scratchpad 工作树的任务交主会话）。不要求跑 G1/G2 的全量部分。
  - **全量回归**（G1 的 `dotnet test Core.sln` 全量、`pytest toolchain/tests -q` 全量、三套数据根
    全量校验；G2 的 `check.ps1 -Quick` 全量）只在以下三种时刻跑：① 里程碑收口；② 升级框架/依赖
    版本之后；③ 合并到 `main` 之前与之后各一次（§1b）。切片级一律按 `toolchain/module_map.json`
    判级：装配层（`*Assembly` 等组装点）与层内共享面记层级范围 T2，登记表 schema 等多模块共享
    数据与工程文件落在 T3；改了装配入口或共享数据的场景由合并前后的全量覆盖，切片内不为它们另跑全量。
  - **执行 agent 不得为"再确认一次"一类理由自行追加全量回归**：如切片跑完后仍觉得有必要跑全量，
    在汇报里写明理由，是否补跑由派单方（设计层或验收 agent）决定，不擅自执行。
- **G1**：`dotnet build` 0 警告；`dotnet test Core.sln` 全过；`python -m pytest toolchain/tests -q` 全过；三套数据根跑 `validate_data.py --strict`——默认数据根与 `games/_template/data/game` 必须 warnings 为 0，`core/sim/tests/data` 只允许已确认的探针项警告。**全量部分只在上述三种时刻跑**；日常切片跑该切片自己的运行时冒烟 + 直接波及模块的定向子集。
- **G2**：`check.ps1 -Quick` 全 PASS。**全量部分只在上述三种时刻跑**；日常切片跑该切片自己的运行时冒烟 + 直接波及模块的定向重跑，不必跑 `check.ps1 -Quick` 全量。
- **G3**：`dotnet build -c Release --artifacts-path X` 之后跑 `toolchain\abi_probe.ps1 -ArtifactsPath X -OutDir <scratchpad>\abi_<task>`，要求 breaks=0。基线版本取自 `toolchain/abi_probe_baseline.txt`，探针先找本工作树 `dist\ws-game-<基线版本>.zip`，没有就自动回落到主检出的 `dist`（输出标"来自主工作树"）；工作树里不需要也不得复制 `dist`，要换基线才显式传 `-BaselineZip`。
- **G4**：模块 README 的"判断记录" + `CHANGELOG.md` `[Unreleased]` 段新增条目——除非派单说明本次改动由后续整合单统一写变更记录。
- 涉及仿真相关改动，三份基线要零差异（`simrunner run --scenario all …`）。**`Added`（基线里从未
  记录过的统计量）也算差异**，不是只看 `Exceeded`/`Removed`：`simrunner` 自身退出码 0 只承诺
  "无 Exceeded/Removed"（契约见 `core/sim/README.md`"命令行入口"一节），不把 `Added` 算作阻断，
  `check.ps1`"6b. 数值仿真基线比对"步骤此前只看这个退出码，2026-09-16 一次新增测试技能漏烘焙
  coverage 基线的 `Added` 差异因此在 31/31 全 PASS 下潜伏了 4 天、跨两次发布才被复审发现——
  **"31 步全 PASS"不等于"三份基线零差异"**，这是此前的认知错误，成因是门禁判据比这条规则字面
  更松。现已在 `check.ps1` 该步骤里额外解析场景摘要行的 `added=<n>` 字段强制拦截（不改
  `simrunner` 退出码语义，理由见 `core/sim/README.md`"T-N6-7 判断记录"57），今后确有新增探针/
  技能导致的合法 `Added`，必须同一提交按"基线更新流程"重新烘焙，不能留待下次顺手处理。

## 5. 发布单专用

- 长时间命令前台执行，只管道 stdout（如 `| Tee-Object -FilePath <scratchpad>\release_X.log`）；绝不 `2>&1`、绝不用 `Start-Process`/隐藏窗口/后台作业驱动发布脚本（会让子进程在无控制台环境下静默中断）。
- 子 agent 启动的 `build.ps1 -Release` 会随该 agent 回合结束被连带终止（1.39.0 首跑在 `Compress-Archive` 处留下 0 字节 zip 与未打标签的发布提交）；因此**发布脚本只由主会话前台执行**，子 agent 不再直接启动 `-Release`，只做发布前收口与发布后核对。
- `build.ps1 -Release X -PublishRegistry` 之前，必须先：提交 CHANGELOG `## [X]` 条目、确认 `git status` 干净、确认私服状态（`start_registry.ps1 -Status`/需要时 `-Detach`）。
- **第 5 步默认复用全量记录，只跑定向门禁（ADR-0156）**：第 3b 步守卫放行（`REGRESSION_LOG.md` 有含 Unity 的全量通过记录，记录提交是 HEAD 或其祖先且其后只改文档类文件）、
  且工作树除第 4 步写回的版本文件外无别的改动时，第 5 步改跑 `check.ps1 -Changed <记录提交> -AbiStrict -FailFast -NoTiming`（版本号写回波及的包清单一致性、DLL 同步、Unity 编译、
  消费方演练仍会跑；规则 `release_version_files`），日志与状态文件写明复用的记录（run_id + 提交）与定向门禁结论行。因此**合并前的全量必须在发布前登记进 `REGRESSION_LOG.md`**
  （含 Unity，结果列含"含 Unity"或"PlayMode N/N"），否则守卫拒绝发布；要强制重跑全量传 `-FullRegate`（约 50 分钟）；`-DryRun` 与守卫不放行时仍跑全量。
- 成功的唯一标记：日志末尾出现提示行 `git push origin main refs/tags/vX`。
- 失败标记：出现"门禁失败"/"发布流程终止"/"自检失败"字样。
- 一旦被拦截或失败，立刻停下汇报，不自行回退、不自行重试、不自行改动版本文件。
- 半途恢复流程（`git restore --staged --worktree` 五个版本文件 + 删 `dist/X`、`dist/release-notes-X.txt`）只能由主会话决定是否执行，执行 agent 不擅自做。
- 半途状态若"发布提交已产生但无标签"（或标签之后的私服/推送/GitHub Release 阶段失败），首选主会话修好原因后用 `build.ps1 -Release X -Resume [同样的 -PublishRegistry/-Publish]` 续跑（不重跑全量门禁、不改写历史；前置校验不满足会拒绝并说明原因）。第 5 步（门禁）或更早失败不在续跑范围，修好后重跑 `-Release`。仅当决定放弃本次发布时才回退：`git reset --soft <发布前提交>` 再按路径 `git restore --staged --worktree` 五个版本文件并删 `dist/X` 产物，不是只还原文件。
- Unity PlayMode 测试失败先用 `python toolchain/unity_test_triage.py` 分诊，不要直接改测试或改断言；
  分诊后核对断言是否在给"已知即将修复的旧错误行为"拍照（修复生效后断言过期是测试侧问题，不是
  回归），排除测试侧问题后才怀疑产品代码，详见
  `docs/复盘/排查复盘-2026-09-15-PlayMode-PRES180.md`"标准流程清单"。
- 不要靠"用例之间清空全局资源缓存"解决 PlayMode 测试串味：`DontDestroyOnLoad` 单例的缓存字段
  可能有其它模块按同一份数据维护的去重表/索引等派生状态，清缓存不清派生状态会让资源永不重新
  加载，制造更隐蔽的新故障；应给失败用例配专属输入（专属资源引用字面量/数据行/占位资产），让
  断言不依赖执行顺序，详见
  `docs/复盘/排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md`。
- Unity 许可排障（Hub 界面显示 Personal 许可已激活，但批处理仍报错误码 198"无有效许可"）：Hub
  界面显示的是账号层面的授权状态，Unity 批处理真正读取的是本机磁盘上的许可文件（由 Unity 许可
  客户端写出、定期续签），两者会脱节。处理顺序：① 先在 Hub 里点"刷新"（通常会触发重新拉取并
  写盘，多数情况下这一步就能解决）；② 无效时退出账号重新登录；③ 仍无效则以管理员身份运行 Hub
  后重新激活；④ 移除许可后重新添加。已核实的相关事实：`check.ps1` 的 Unity 步骤只会因显式传
  `-SkipUnity`/`-Quick` 而 SKIP，许可失败会表现为 FAIL 而不是静默跳过；`check.ps1` 开跑前会
  检测同工程残留的 `Unity.exe` 进程并直接中止，因此跑门禁前不要在 Hub 里开着同一工程。
- 引擎步骤几秒内退出、日志含 `IPC stream failed to read`（包管理器子进程中途消失）时，重跑一次，通过则在 `REGRESSION_LOG.md` 登记为环境性失败，并附上步骤 Detail 里自动抓取的现场摘要（`包管理器子进程退出码=…；现场已存 <路径>`，落在 `bin/_check_artifacts/upm_evidence/`）。
- PowerShell 工具前台跑 `-Release` 超过 600s 会被"转入后台继续运行"而非杀死，退出通知后再核对结果即可，不必视为失败。
- 后台任务的 output 文件含 stderr 全量（如 npm notice 上千行），不要 `Read` 整个文件，交子 agent `Grep`；管道过滤用 `Select-String '门禁通过|门禁失败| FAIL '`。
- 同一 minor 出补丁版时 `release/X.Y.x` 已存在，用非强制快进：先本地 `git push . <hash>:refs/heads/release/X.Y.x` 再 `git push origin <hash>:refs/heads/release/X.Y.x`，不用 `-f`。

## 6. 汇报格式

- 中文，先说结论，再给证据。
- 汇报要点：提交哈希、改动文件列表、每项修法各一句话说明、新增测试名、测试总数（含新增几例）、门禁结果、CHANGELOG 条目文本、待设计层确认的事项。
- 大段输出（完整日志、长表格）落盘到 scratchpad，汇报里只给路径和关键数字，不整段贴出来。
- 不擅自拍板设计冲突，遇到设计取舍未定的地方，明确标注"待设计层确认"。

## 7. 复审/验收单专用

- **独立验收 agent 只在里程碑收口时派一次**（对应第 4 节"回归分级"里全量回归的几种时刻之一）；
  升级依赖、合并前后触发的全量回归由执行/复核方自行核对，不必单独派验收
  agent。**文档类、登记类提交不派验收**（如只改 `architecture/` 正文、README"判断记录"、
  CHANGELOG、登记表勘误，未触碰代码/构建产物）。
- 只读：不改任何文件、不跑任何 git 写命令。
- 需要实证某处缺陷时，可以建一个临时工作树写一次性验证测试，验证结束后删除该工作树，不留痕在主树/正式分支。
- 报告落盘到 `<scratchpad>\review_<x>.md`，不直接改动被复审的文档/代码。

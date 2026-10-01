<#
.SYNOPSIS
    仓库根一键门禁脚本（见 11_工程规范与测试.md 第 8 节"提交门槛清单"）：跑 .NET 构建与测试、
    Python 数据/工具链校验、禁用词扫描、构建产物同步、Unity 编译检查与测试、独立版构建与无人
    值守冒烟，逐步打印耗时与结果，结束时汇总一张表；任一步失败，整体以非 0 退出码结束。

    判断记录（gate-speed 任务，2026-09-22，门禁提速重排 + 分线并行）：31 步全量门禁改造前是
    严格串行、总耗时约 486 秒，且任一步失败仍会白跑完剩余全部步骤才报错（实测过一次 pytest 在
    约 3 分钟处失败，仍跑到 566 秒才收尾）。本次改造三件事，判断记录写在这里（唯一出处，不在
    别处重复登记）：

    1) **失败即停（`-FailFast`，新增开关）**：`-FailFast` 打开后，任一步骤 FAIL 会让"该步骤所在
       的那条执行序列"（主进程的快速前置阶段，或下面两条并行子线中的一条）后续步骤全部立即改判
       SKIP（原因写清楚"上游步骤已失败"/"并行的另一条线已失败"），不再白跑。`build.ps1 -Release`
       调用门禁时固定传本开关（见 build.ps1"第 5 步"调用点）；日常直接跑 `check.ps1`（不传
       `-FailFast`）保持原行为——跑完全部步骤，一次看全，不因为一步失败就看不到别的问题。

    2) **重排步骤顺序，便宜的先跑**：把秒级、不依赖 Unity/重构建的检查（门禁自检、两道禁用词
       扫描、版本一致性、几道数据/schema 校验、工作树 CR 检查、Unity .meta 完整性）挪到最前面，
       串行跑完；这批步骤互相之间、与后面的重步骤之间都没有真实依赖，纯粹是"先看便宜的，尽快
       给出第一个可能的 FAIL"。真正有依赖的地方保持依赖顺序不变，没有重新发明：`dotnet build`
       仍在 `dotnet test`/ABI 探针/数值仿真基线比对之前（后三者都要读它的构建产物）；"同步 DLL"
       （`build.ps1 -SkipTests`）仍在全部 Unity 相关步骤之前（Unity 加载的是这份同步过去的 DLL
       副本，不同步就是在验证陈旧代码，见 AGENTS.md 第 4 节）。

    3) **两条线并行**：`dotnet build`/`dotnet test`/ABI 探针/占位资产生成器检查/样例导入幂等性
       门禁/`toolchain` 自身 pytest/数值仿真基线比对这几步不需要 Unity，收拢进
       `toolchain/_gate_line_heavy.ps1`；DLL 同步/包清单一致性/Unity 编译检查/EditMode/
       PlayMode/独立版构建+两种冒烟/IL2CPP 三步/消费方演练需要 Unity（且互相之间必须串行——
       Unity 不允许同一工程有两个批处理实例同时跑），收拢进 `toolchain/_gate_line_unity.ps1`。
       `check.ps1` 本体用 `Start-Job -ScriptBlock { & $ScriptPath @Params }`（两个独立
       `powershell.exe` 子进程，PowerShell 5.1 内建能力，不依赖 `Start-ThreadJob`/`ForEach-Object
       -Parallel` 等 PS 7+ 专属机制）并行启动这两个脚本，`Wait-Job` 等两者都结束后，把各自落盘的
       JSON 结果文件（`$ArtifactsPath\gate_line_heavy_results.json`/
       `gate_line_unity_results.json`）读回合并进主进程自己的汇总表——选"落盘 JSON 再读回"而不是
       直接吃 `Receive-Job` 的返回对象，是为了避开 PowerShell 后台作业跨进程反序列化对象类型的
       不确定性（`Receive-Job` 拿到的复杂对象经过 CliXml 反序列化后不一定还是同一个 .NET 类型），
       JSON 是更可控、也方便人工事后翻看的选择。两条线各自的输出目录/临时文件天然不相交（见
       `toolchain/_gate_line_heavy.ps1`/`_gate_line_unity.ps1` 头部判断记录），不需要额外加锁。

       **FailFast 在并行下的语义**（按任务书"按实现难度选，写进判断记录"授权自行选定）：两条线
       各自是独立子进程，内存状态不共享，只能靠共享文件系统上的一个标记文件
       （`$ArtifactsPath\gate_failfast.flag`）跨进程通信——一条线的某一步失败时，除了让本进程
       内的判定短路，还会创建这个标记文件；另一条线在它自己"下一步"真正开始执行之前会先检查这个
       文件是否存在，存在就同样短路成 SKIP。选的是"不抢占正在执行中的那一步"（不强行 Kill 另一
       进程正在跑的 dotnet/Unity 子进程）——已经启动的步骤会跑完，只是不再启动新的步骤；抢占式
       终止另一进程里可能正在写盘的原生命令（尤其 Unity 批处理）实现复杂度更高、还可能留下半吊子
       产物，不在本次任务范围内。

       汇总表仍按步骤逐行列出各自 PASS/FAIL/耗时（两条线的结果合并后一起打印，不分组），另加一行
       仅用于展示的"并行阶段墙钟"（两条线各自 Seconds 之和通常大于这一行——因为是并行执行的实际
       耗时，不是求和；这一行不计入 `$script:Results`，不影响步骤计数与"门禁通过：全部 N 步"里
       的 N，只是额外打印，避免打乱 `-Quick`/`-SkipUnity`/全量三种模式下沿用已久的步骤计数——
       `README.md`/`.githooks/pre-commit` 里"28 步"/"31 步"这类描述引用的正是 `$script:Results`
       的实际条目数）。

    4) **禁用词扫描改用 `git grep`（只扫受版本管理的文件）**：原实现用 `Get-ChildItem -Recurse`
       走全仓库物理目录树、只在文件名/目录名层面按黑名单（`.git`/`bin`/`obj`/`Library`/
       `StreamingAssets`/`dist`）过滤——问题是 `Get-ChildItem -Recurse` 会先把这些目录（尤其
       Unity 的 `Library\`，本机实测可以有数万个文件）完整枚举一遍才轮到按名字排除，这正是该步骤
       原本要跑 36 秒的根因，不是"匹配"本身慢。改用 `git grep -n -i -I -F <词> -- . ":(exclude)
       check.ps1"`（`-I` 跳过二进制、`-F` 按固定字符串而不是正则、`-i` 大小写不敏感），
       Git 内部直接对象数据库层面搜索被跟踪的内容，根本不触碰 `.gitignore` 排除的目录，语义上
       也更贴合"这是版本管理里的仓库内容"这条硬性规则本身（既有的手工排除名单本质是在近似"已跟踪
       文件"这个概念，`git grep` 直接就是它）。退出码约定：0=有命中（判失败）、1=无命中（判
       通过）、>1=`git grep` 自身执行出错（判失败并报错）。实测（本仓库当前状态）：改前/改后各跑
       一次，命中数一致（均为 0 处）；另外造过一个临时受跟踪文件写入禁用词，`git grep`
       能正确命中，随后已撤掉该临时文件，不留痕。`architecture` 正文技术名扫描（第二道）本身只
       遍历 `architecture/0*.md`/`1*.md`/`architecture/adr/*.md` 这一小撮文件，不是耗时来源，
       维持原 `Get-ChildItem` 实现不变。

    5) **pytest 并行（`pytest-xdist`）——本次未启用**：任务书要求"先查 anaconda 环境是否已装
       `pytest-xdist`，已装才用 `-n auto`，没装就跳过不装"。实测核对本机两个 conda 环境
       （`base`、`python13`，`pip show pytest-xdist` 均报 `Package(s) not found`）均未安装，
       按规则不新增安装，`toolchain/_gate_line_heavy.ps1` 里 `pytest toolchain/tests -q` 命令行
       与改造前完全一致，不追加 `-n auto`。

    6) **`test_registry_stop_pidfile_rewrite_timestamp.py::test_detach_twice_then_stop_succeeds`
       抽出并行线、改到本阶段串行跑（gate-parallel-ctrlc 修复，2026-09-22 追加，见分支
       `fix/gate-parallel-ctrlc`）**：1.62.0 发布门禁连续两次在该用例失败——一次是第二次
       `-Detach` 调用返回码 1，一次是返回码 3221225786（0xC000013A，`STATUS_CONTROL_C_EXIT`，
       进程被控制台控制事件终止），但该用例单独跑（`pytest
       toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py -q`）两例均过、12 秒，
       1.59～1.61 三次发布门禁里也都过。根因排查（工作树 `wt_flake` 内实测，证据见该次会话）：
       该用例的行为级回归（`test_detach_twice_then_stop_succeeds`）用
       `subprocess.run(..., creationflags=CREATE_NEW_CONSOLE)` 启动 4 个独立
       `powershell.exe` 子进程（跑 `start_registry.ps1` 的 `-Detach`/`-Status`/`-Stop`），
       是 `toolchain/tests` 全套件里唯一一个会启动多个"各自独立控制台"的 Windows 进程自动化
       用例，因此是全套件里对"控制台控制事件"最敏感的一个。实测复现确认：一个
       `CREATE_NEW_CONSOLE` 启动的 PowerShell 子进程，只要有别的进程 `AttachConsole` 到它的
       控制台后调用 `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)`，会在没有自定义控制台控制
       处理器时被系统默认处理器直接终止，退出码正是 3221225786——与两次真实失败完全一致。
       仓库全文搜索确认本仓库自身代码里不存在任何 `AttachConsole`/`GenerateConsoleCtrlEvent`
       调用，触发源不在本仓库代码内；进一步实测排查了两种"让 PowerShell 子进程自身免疫控制台
       控制事件"的标准手段——(a) 通过 `Add-Type` 定义 P/Invoke 委托再 `SetConsoleCtrlHandler`
       注册一个恒返回 `$true` 的处理器；(b) `[Console]::TreatControlCAsInput = $true`——两者
       在本机、本类宿主环境下对"外部进程主动 `AttachConsole` + `GenerateConsoleCtrlEvent`
       发来的事件"均**未能**生效（子进程仍被终止），说明"让 PowerShell 脚本进程自证免疫"这条
       路线不可靠，不能作为可信的根治手段。鉴于触发源确认不在本仓库代码内、且找不到可靠的
       进程自身免疫方案，按任务书"最后手段"条款处理：不再让该用例随
       `toolchain/_gate_line_heavy.ps1` 的整套 `pytest toolchain/tests -q` 一起跑在"两条线
       并行"阶段（该阶段是全流程里耗时最长的部分，历史上单跑能到数百秒，統计上暴露窗口最大），
       改为单独抽成本阶段（阶段一，两条并行线派生之前）的一个新步骤，串行跑、且与
       Unity 串行线完全不重叠；`toolchain/_gate_line_heavy.ps1` 步骤 6 的
       `pytest toolchain/tests -q` 加 `--ignore` 排除这个文件，避免重复跑。这不改变该用例
       本身的断言/重试策略（不属于"放宽断言"或"重试掩盖"），只改变它在整条门禁时间线上的
       调度位置，把暴露窗口从"门禁里最长的并行阶段"换成"门禁里最短的串行前置阶段"。

    7) **测试步骤用例数下限（`toolchain/gate_floors.json`，复盘 I-2，2026-10-01）**：dotnet test、
       pytest、Unity EditMode、Unity PlayMode 四步此前只看退出码/结果 Passed，整批用例被悄悄丢掉
       （程序集没编进来、发现规则坏了、批量 Assert.Ignore、过滤器写错）时剩下的照样全绿。现在四步
       各自解析结果文件（dotnet test 的 trx、pytest 的 junitxml、Unity 的 NUnit XML）里的
       total/passed/skipped/inconclusive，与 gate_floors.json 登记的下限比较：passed < min_passed
       即 FAIL，skipped+inconclusive > max_skipped 即 FAIL，四个数恒写进步骤 Detail。解析与判定
       逻辑集中在 `toolchain/_gate_test_floors.ps1`（`toolchain/tests/test_gate_floors_logic.py`
       用伪造的 trx/junit/NUnit 夹具验证低于下限 FAIL、超过 skip FAIL、正常 PASS）。
       **下限取值规则**（唯一出处，数字只在 gate_floors.json）：min_passed = 当前实测 passed 的 90%
       向下取整到十位；max_skipped = 当前实测 skipped 数。**维护义务**：套件明显增长后（新增一批
       测试、跑过一轮全量确认实测数），要按同一规则把 min_passed 抬高并更新 measured_*，否则下限
       形同虚设；确属有意删减用例才下调，且在提交信息里写明理由。`-Quick`/`-SkipUnity` 下被跳过的
       步骤本来就不跑，不受下限约束；pytest 一项只覆盖 `_gate_line_heavy.ps1` 实际跑的那批用例。
       同一轮还做了两件小事：模板数据根 `games/_template/data/game` 的 `validate_data.py --strict`
       单独成一步（复盘 I-1，见下方 10b 步骤注释，秒级，`-Quick`/`-SkipUnity` 下也跑）；
       dotnet test 步骤名里的测试工程数改为脚本从 Core.sln 数出来（复盘 I-11，此前写死"六工程"，
       实际早已不是六个）。

    8) **测试覆盖第四批的门禁调整（2026-10-01，复盘 docs/复盘/测试覆盖剩余项-2026-10-01.md 末节
       "拍板"，下面各条是这些决定在门禁里的唯一落点）**：
       - **I-5 缩减版（环境矩阵）**：全量门禁（不含 `-Quick`、不含 `-SkipUnity`，即也不含 CI 的固定
         形态）在 `toolchain/_gate_line_heavy.ps1` 多两步——6c 把 `PYTHONUTF8`/`PYTHONIOENCODING`
         摘掉后整套 `pytest toolchain/tests` 再跑一遍；6d 把"启动 PowerShell 子进程跑 .ps1"的那批
         pytest 文件在 Windows PowerShell 5.1 与 PowerShell 7 两个宿主各整批跑一遍（环境变量
         `WS_GAME_PS_HOST`，实现与宿主核对见 `toolchain/tests/conftest.py`）。两步里 skipped 一律
         FAIL。`-Quick`/`-SkipUnity` 下这两步登记为可见 SKIP，所以三种形态的总步骤数各 +2。
       - **I-6（IL2CPP/AOT）**：IL2CPP 三步（构建 + 两种冒烟）仍为显式开关，只由 `-Il2cpp` 开启，
         默认 `check.ps1`（含 pre-commit、CI、日常全量）不跑——代码现状即如此（`_gate_line_unity.ps1`
         里 `if (-not $Il2cpp)` 分支登记三个 SKIP，`-Il2cpp` 默认 `$false`）。**发布门禁不强制**，
         `build.ps1 -Release` 第 5 步不传 `-Il2cpp`（2026-10-01 回退此前"发布门禁固定传"的拍板）。
         原因：构建机缺 Visual Studio C++ 工作负载与 Windows SDK，IL2CPP 构建必败；装好后再启用。
       - **I-7（不做）**：不为 `build.ps1 -Release`/`-Dist` 全流程写自动化 dry-run 用例——脚本近两千
         行、仅 Windows、耗时长，写稳的成本高于收益；发布流程靠真实发布 + `REGRESSION_LOG.md` 登记
         + 既有的 `toolchain/_dist_immutability_guard.ps1` 等纯函数守卫的单测兜底。
       - **I-13（发布必须有含 Unity 全量记录）**：`build.ps1` 里"发布时跳过 Unity"的开关删除（发布
         门禁恒为全量）；`-Release` 在写回版本号之前先核对 `REGRESSION_LOG.md`，没有对应当前 HEAD
         （或其祖先且其后只改了文档）的含 Unity 全量通过记录就拒绝发布，判定函数与规则见
         `toolchain/_release_regression_guard.ps1`。CI 仍不跑 Unity。
       - **I-2 余项**：dotnet test 步骤里找不到性能基线诊断行（PerfBaselineTests 被排除/没编进来）
         由"黄色警告照样 PASS"改为 FAIL（`Get-PerfDiagnosticLines`，伪造 trx 夹具用例在
         `toolchain/tests/test_gate_floors_logic.py`）。
       - **I-8 / I-12 余项**：门禁自身的判定逻辑抽成纯函数后用 pytest 子进程直接测——Unity 结果
         XML 判定、冒烟日志判定、npm 包清单必需文件清单（`toolchain/_gate_unity_verdicts.ps1`）、
         `Resolve-UnityExe`/`Invoke-NativeAndWait`/`Test-NoResidualUnityProcess`（`_gate_step_runner.ps1`
         原函数，直接测）；汇总段新增"环境性 SKIP"单独计数并逐条打印（`Get-EnvironmentalSkipRows`：
         不是 `-Quick`/`-SkipUnity`/`-DocsOnly`/`-SkipConsumer`/未传 `-Il2cpp`/FailFast 短路造成的 SKIP，
         如 ABI 探针本机无基线发行包、样例导入幂等性门禁因工作树有未提交改动而跳过，意味着该步骤本次
         没有验证）。pytest 环境性 skip 早已因 `max_skipped=0` 判 FAIL，不变。
    9) **定向门禁（ADR-0126，2026-10-01）：`-Changed`/`-Staged`/`-Modules`/`-DryRun`**。全量门禁
       约 7 分钟，其中真正贵的是引擎线、工具链 pytest（130 秒，与核心层改动无关）和进上下文的日志文本。
       改动路径 -> 级别 T0～T3 -> 要跑的步骤/测试工程/引擎侧分类，全部由 `toolchain/change_impact.py`
       读 `toolchain/module_map.json` 判定（执行 agent 不自己决定跑什么）；本脚本只做三件事：调用判定器、
       在开头打印「本次判定」、按判定结果给每个 `Invoke-CheckStep -Id` 放行或改判可见 SKIP
       （原因写成『T? 未触发』）。不传这四个参数时本段完全不生效，行为与此前一致。
       **日志分流**：定向模式下本脚本把自己再起一个子进程（带隐藏开关 `-TargetedInner`）跑真正的步骤，
       父进程把子进程的全文输出落盘到 `$ArtifactsPath\check_targeted.log`（给了 `-LogFile` 就落到
       `-LogFile`，复用既有开关、不另造），控制台只留：每步一行结果（`[步骤] 通过/失败，用时`）、末尾
       汇总表、首个失败步骤的最后 30 行。引擎侧待跑的 PlayMode 分类过滤串在「本次判定」里单独列出——
       这块在主检出或路径足够短的工作树里执行（深层 scratchpad 工作树交主会话）。判定失败（参数错、git 出错）直接退出码 1，
       不退回全量。
    10) **耗时自动记录（AGENTS.md 1c，2026-10-01）**：每次运行结束（通过或失败都写）把每个步骤追加到
       `timing/<年月日>_<分支名去 feature/ bugfix/ 前缀>.jsonl`（main 上为 `<年月日>_main.jsonl`），字段
       task/branch/phase/step/start/end/seconds/result/note，phase 定向模式记 `定向门禁`、否则 `全量门禁`，
       step 用步骤稳定 `-Id`，另加一行 step=`_total`（脚本总墙钟）。start/end 在 `Invoke-CheckStep` 里每步
       前后各取一次系统时间（并行线在各自子进程里取，经 JSON 带回），不是整次起点 + 秒数。实现在
       `toolchain/_gate_timing.ps1`，统计用 `toolchain/timing_report.py`；`-NoTiming` 跳过（预提交钩子与
       `build.ps1 -Release` 调用时带——否则每次提交/发布都弄脏工作树，发布打包自检会看到 -dirty）；
       写入失败不影响门禁结论，只在汇总末尾打一行警告。定向模式由干活子进程写（父进程只传 `-TimingTask`
       与 `-NoTiming`），所以 `_total` 不含父进程的判定与启动时间。

.PARAMETER NoTiming
    跳过耗时自动记录（见 .SYNOPSIS 判断记录 10)）。`.githooks/pre-commit` 与 `build.ps1 -Release` 调用门禁时
    固定带本开关。

.PARAMETER TimingTask
    耗时记录里 `task` 字段的一句话；缺省为 `check.ps1 <参数串>`（参数串取自本次显式传的参数，内部参数不进）。

.PARAMETER SkipUnity
    跳过 Unity 相关四步（编译检查、EditMode、PlayMode、独立版构建 + 冒烟）与消费方演练；只跑
    .NET/Python/禁用词/DLL 同步/包清单一致性几步。同一仓库内并行有人独占 Unity 编辑器时用这个
    开关。判断记录：本开关不影响"9. build.ps1 -SkipTests（同步 DLL）"与"9.5 包清单一致性"两步
    是否跑——这是迁移前 check.ps1 的既有语义（这两步只受 `-Quick` 门控），本次改造只搬了代码
    位置（挪进 `toolchain/_gate_line_unity.ps1`），不改判定条件。

.PARAMETER SkipSmoke
    仍跑 Unity 独立版构建，但跳过"-gf-smoke 无人值守冒烟"这一子步骤（-SkipUnity 已整体跳过
    Unity 时本开关不生效）。

.PARAMETER SkipConsumer
    跳过"消费方演练"这一步（toolchain/consumer_smoke.ps1，见该脚本头注释）。该步骤会从零搭建一个
    独立于本仓库源码树的最小 Unity 消费方工程，耗时较长（包含至少四次 Unity 批处理调用：首次
    编译/包解析、生成场景、PlayMode 测试、构建独立版）；-SkipUnity 已整体跳过 Unity 时本开关不
    生效（该步骤本身就需要 Unity）。

.PARAMETER ArtifactsPath
    dotnet build/test 的 --artifacts-path。默认 <仓库根>\bin\_check_artifacts（"bin"
    这一层已被 .gitignore 的 `bin/` 规则忽略，不会误入库）；Unity 编译日志/测试结果 XML/
    独立版构建产物也落在这个目录的 `unity\` 子目录下；两条并行线各自的日志/JSON 结果文件
    （`gate_line_heavy.log`/`gate_line_heavy_results.json` 等）与 FailFast 跨进程标记文件
    （`gate_failfast.flag`）同样落在这个目录下（均不入库）。

.PARAMETER UnityExe
    Unity 可执行文件完整路径。默认按 Unity Hub 常见安装位置尝试
    `%ProgramFiles%\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe`；找不到则退化为裸文件名
    `Unity.exe`（要求已在 PATH 上），仍找不到时对应步骤记为失败并在明细里提示改用本参数显式
    指定。

.PARAMETER Configuration
    dotnet 构建配置，默认 Release。

.PARAMETER LogFile
    持续集成修复新增：给出路径时，用 Start-Transcript 把本次运行的完整控制台输出（含每步
    PASS/FAIL 明细与最后的汇总表）额外落一份文本文件到该路径，同时仍然正常打印到控制台；
    省略（默认空字符串）时不额外落日志，行为与之前完全一致。判断记录（gate-speed 任务追加）：
    两条并行线各自跑在独立子进程里，各自的 Write-Host 只会进各自的子日志文件
    （`$ArtifactsPath\gate_line_heavy.log`/`gate_line_unity.log`，`Start-Transcript` 同理各自
    独立），不会自动汇入主进程的 `-LogFile`；主进程在两条线都结束、合并结果之后，会把这两份子
    日志的内容依次 `Get-Content | Write-Host` 回主进程的输出流，因此仍然能在同一份 `-LogFile`
    里看到完整的三段输出（主进程快速前置阶段 + 非 Unity 重步骤线 + Unity 串行线），只是子线的
    这部分内容在时间顺序上是"两条线都跑完之后一次性追加"，不是实时交织——这是"落盘再合并"这个
    选择本身带来的、可接受的副作用，不影响任何人事后读日志排查问题。

.PARAMETER Quick
    工程收尾 K 新增，供 `.githooks/pre-commit` 调用：只跑"秒级能跑完"的子集——dotnet
    build/test、四道数据校验（合并根 + data/_framework 框架根 + core/sim/tests/data 嵌入仿真
    数据集，反馈 46 后续新增第三道，见步骤 6a 判断记录；再加 games/_template/data/game 模板数据根
    第四道，见步骤 10b 注释）、事件常量一致性检查、两道禁用词
    扫描、版本一致性；跳过占位资产生成器检查（`gen_placeholder_assets.py --check`，需要 Pillow
    且逐张比较占位图较慢）、`toolchain` 自身 pytest、数值仿真基线比对（T-N6-7 新增，见该步骤
    判断记录——任务书硬性规则"禁止把数值仿真列为 -Quick 步骤"，本开关下始终 SKIP，不代表其不重要）、
    `build.ps1 -SkipTests` 同步、包清单一致性
    （私服交付通道新增，需要跑一遍 `build.ps1 -SyncOnly -Dist auto` + `npm pack`，与
    `build.ps1 -SkipTests` 同步同一类"非 Unity 但耗时的构建期动作"，且依赖它先把六个核心 DLL
    构建到 `bin\` 下——`-SyncOnly` 要求产物已存在，见该步骤判断记录，故排在其之后）、全部
    Unity 相关步骤与消费方演练——本开关本身就意味着不跑任何 Unity 步骤（等价于隐含 -SkipUnity，
    同传 -SkipUnity 不冲突也没有必要）。不能替代完整门禁，只用于提交前快速把关。

.PARAMETER AbiStrict
    外部审计 audit-76d16a5-20260910（PJ114-02）新增：把"ABI 探针基线发行包缺失"从可见 SKIP
    升级为 FAIL（透传 `toolchain/abi_probe.ps1 -SkipIfBaselineMissing:$false`，见该脚本
    `.PARAMETER SkipIfBaselineMissing` 判断记录——基线缺失时退出码从 SKIP 的 3 变成 FAIL 的 1）。
    本地开发机 `dist/` 未必有历史版本 zip，缺基线时看不到 ABI 验证是正常状态、不该拖住日常提交；
    但发布机在跑 `build.ps1 -Release` 之前 `dist/` 一定已经有基线版本（历次发布都会落地），此时
    "探针没跑"本身就是发布链路故障，必须失败而不是安静跳过——`build.ps1 -Release` 调用全量 check
    时固定传本开关（见该脚本调用点判断记录）。`-Quick`/`-SkipUnity` 均不影响本开关是否生效（本开关
    只改变"基线缺失"这一种局面下 ABI 步骤的判定，`-Quick` 下 ABI 步骤本身整体 SKIP，不受影响）。

.PARAMETER Il2cpp
    工程收尾 K 新增，默认不跑（因为耗时数分钟到十几分钟，见 adapters/unity/README.md"IL2CPP
    发布路径验证"一节判断记录）：额外跑一遍 IL2CPP 脚本后端的独立版构建
    （Adapter.Unity.EditorTools.Il2CppPlayerBuilder.BuildWindows64PlayerIl2cpp，构建前临时切
    NamedBuildTarget.Standalone 的脚本后端到 IL2CPP，构建后还原，不永久修改 ProjectSettings）+
    两种无人值守冒烟（-gf-smoke / -gf-smoke-discrete），验证核心类库在 AOT 编译（无反射兜底）下
    的真实可运行性。`-SkipUnity` 时本开关不生效（-SkipUnity 已整体跳过 Unity）。`build.ps1 -Release`
    第 5 步不传本开关（复盘 I-6 的 2026-10-01 回退，见 .SYNOPSIS 判断记录 8)：构建机缺 VS C++
    工作负载与 Windows SDK，装好后再启用）；日常/pre-commit/CI 同样默认不跑。

.PARAMETER DocsOnly
    提交前钩子分级任务新增（2026-09-22），供 `.githooks/pre-commit` 在判定本次提交暂存改动
    全部是 `.md` 文档时调用：只跑"与文档相关"的几步——门禁自检（下方判定逻辑本身依赖的
    `Test-NativeExitCode` 正确性探针，近乎零成本，但没有它其余步骤的 PASS/FAIL 判定都不可信）、
    两道禁用词扫描（游戏代号 + architecture 正文技术名——CLAUDE.md 的硬性规则唯一靠它们守住，
    任何档位都不能跳过）、版本一致性（含 CHANGELOG.md 条目校验）、以及 `toolchain/tests` 里两个
    文档相关 pytest 用例（`test_markdown_relative_links.py` 校验 md 相对链接、
    `test_editor_doc_consistency.py` 校验编辑器文档两版一致）；其余全部步骤跳过，标注原因
    "-DocsOnly"。隐含 `-SkipUnity`。判断记录（gate-speed 任务确认未变）：`-DocsOnly` 下两条并行
    线（`toolchain/_gate_line_heavy.ps1`/`_gate_line_unity.ps1`）里没有一步带 `-DocRelevant`
    标记，`Invoke-CheckStep` 会把它们各自的每一步都短路成 SKIP（原因 "-DocsOnly"）——两条线仍然
    会被正常 `Start-Job` 启动（不特殊跳过 fork 本身），但因为内部全是近乎零成本的短路判断，实际
    运行时间可忽略，不需要为 `-DocsOnly` 单独写一条"不 fork"的分支，保持主流程代码单一路径、
    不增加特例。

.PARAMETER FailFast
    gate-speed 任务新增（见本文件顶部 `.SYNOPSIS` 判断记录 1)）：任一步骤 FAIL 后，该步骤所在的
    执行序列（主进程快速前置阶段，或并行的两条线之一）后续步骤全部立即改判可见 SKIP，不再白跑，
    汇总表用 Detail 列写清楚原因。`build.ps1 -Release` 调用门禁时默认传本开关（见 build.ps1
    "第 5 步"调用点判断记录）；日常直接跑 `check.ps1` 不传本开关，保持"跑完全部、一次看全"的
    原行为。

.PARAMETER Changed
    定向门禁（ADR-0126）：基线提交/分支，默认 `main`。判定范围 = 工作树（含暂存、未暂存、未跟踪）相对
    `merge-base(基线, HEAD)` 的全部改动。传了本参数（或下面任一定向参数）即进入定向模式：先打印「本次
    判定」，再按判定结果只跑相关步骤，全文日志落盘（见 .SYNOPSIS 判断记录 8)）。典型用法：切片级
    `pwsh check.ps1 -Changed main -SkipUnity`。

.PARAMETER Staged
    定向门禁：只看暂存区（`git diff --cached`），供 `.githooks/pre-commit` 用。

.PARAMETER Modules
    定向门禁：手动指定子模块（逗号分隔，名字见 `toolchain/module_map.json`），按 T1（子模块内部）处理；
    单独给 `-Modules` 时只看这些模块，同时给了 `-Changed`/`-Staged` 则与路径判定取并集。

.PARAMETER DryRun
    定向门禁：只打印「本次判定」（含将传给 Unity 的 `-testCategory` 分类过滤串）后退出，不执行任何步骤。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符；两条并行线用 `Start-Job -ScriptBlock`（PS
    5.1 内建的后台作业机制，不依赖 `Start-ThreadJob`/`ForEach-Object -Parallel` 等 PS 7+ 专属
    能力）。
    本脚本只读跑校验/测试/构建，不修改仓库内容（`toolchain/gen_event_constants.py`/
    `gen_placeholder_assets.py` 都用 `--check` 只读校验模式，不落地写文件；`-Il2cpp` 步骤对
    ProjectSettings 的脚本后端改动只发生在 Unity 子进程内存里，见 Il2CppPlayerBuilder.cs 判断
    记录，不落盘）。

    判断记录（2026-09-07 补充，"只读"的范围边界；同日二次实测勘误）：上面"不修改仓库内容"说的
    是本脚本自身不写任何文件；但 Unity 编辑器进程本身会在内容确有变化时重写
    `adapters/unity/ProjectSettings/ProjectSettings.asset`，以及在包清单内容确有变化时改写
    `adapters/unity/Packages/manifest.json`、`packages-lock.json`——这是 Unity 编辑器固有行为，
    与本脚本无关，也不受 `-Il2cpp` 影响；但触发条件是"内容确有变化"，不是任意一次启动/关闭：
    单独跑一次不改内容的编译检查或 EditMode 测试不会复现，只有像"独立版构建"这种会让 Unity
    真正回写内容的步骤才会。三者 Unity 写出的字节实测是 LF（此前"三者 Unity 写出的字节都是
    CRLF"的结论有误，把本机 `core.autocrlf=true` 检出态的 CRLF 误当成了 Unity 写出的字节；
    实测方法与过程见 `.gitattributes` 对应例外条目上方的判断记录）。仓库根 `.gitattributes`
    已为这三类路径显式声明 `eol=lf`（与 Unity 实际写出的行尾一致），使 Unity 批处理跑完后
    `git status` 始终保持干净，不再依赖运行机器本地的 `core.autocrlf` 配置。
#>
param(
    [switch]$SkipUnity,
    [switch]$SkipSmoke,
    [switch]$SkipConsumer,
    [switch]$Quick,
    [switch]$DocsOnly,
    [switch]$AbiStrict,
    [switch]$Il2cpp,
    [switch]$FailFast,
    # 定向门禁（ADR-0126），见 .SYNOPSIS 判断记录 8) 与各 .PARAMETER。四个都不传 = 行为与此前一致。
    [string]$Changed = "main",
    [switch]$Staged,
    [string[]]$Modules = @(),
    [switch]$DryRun,
    # 耗时自动记录（见 .SYNOPSIS 判断记录 10)）：默认每次运行结束把逐步耗时追加进 timing/；-NoTiming 跳过
    # （预提交钩子与 build.ps1 -Release 调用时带本开关，免得每次提交/发布都弄脏工作树）；-TimingTask 是
    # 任务一句话，缺省为 "check.ps1 <参数串>"。
    [switch]$NoTiming,
    [string]$TimingTask = "",
    # 以下两个是定向模式父进程调用自己的内部开关，不是给人用的：-TargetedInner 表示"我就是干活的子进程，
    # 不要再套一层日志分流"，-PlanFile 是父进程写好的判定 JSON。
    [switch]$TargetedInner,
    [string]$PlanFile = "",
    [string]$ArtifactsPath = "",
    [string]$UnityExe = "",
    [string]$Configuration = "Release",
    [string]$LogFile = "",
    # 仅供并行编排自证测试使用（见 check.ps1 验收记录/toolchain/tests 对应用例）：插入到两条并行
    # 线各自第一步之前的模拟耗时（秒）。默认 0（都不注入）表示正常门禁行为，不受本参数影响。
    [int]$InjectMockSleepHeavySeconds = 0,
    [int]$InjectMockSleepUnitySeconds = 0
)

# -Quick 隐含不跑任何 Unity 步骤（见 .PARAMETER Quick 说明），与显式 -SkipUnity 合并为同一个
# 内部开关，下面 Unity 四步 + 消费方演练的 if ($SkipUnity) 分支判断处两者等价处理。-DocsOnly
# 同理隐含 -SkipUnity（见 .PARAMETER DocsOnly 说明）。
if ($Quick -or $DocsOnly) {
    $SkipUnity = $true
}

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot

# 耗时自动记录：脚本总起点与默认任务描述。必须在定向父进程分支之前取，父进程把算好的任务描述经
# -TimingTask 传给干活的子进程，让两边记同一句话。
$script:TimingScriptStart = Get-Date
. (Join-Path $RepoRoot "toolchain\_gate_timing.ps1")
if ($TimingTask -eq "") {
    $TimingTask = Get-GateTimingDefaultTask -BoundParameters $PSBoundParameters
}

# 版本标签（ADR-0127，AGENTS.md §1b）：由 toolchain/version_label.py 按当前分支自动推导（feature/bugfix 分支
# `<VERSION>_<名>`，main `<VERSION>_release`），不写进 VERSION。开头打印、汇总末尾再打印一次；同时经环境变量
# WsGameVersionLabel 传给本进程启动的 dotnet 构建（Directory.Build.props 据此写程序集信息版本）。
# 推导失败（无 python/无 git）只提示，不影响门禁判定——"分支名规范"步骤会单独把关。
$VersionLabel = ""
try {
    $labelOut = & python (Join-Path $RepoRoot "toolchain\version_label.py") "--repo-root" $RepoRoot
    if ($LASTEXITCODE -eq 0 -and $labelOut) { $VersionLabel = ([string]@($labelOut)[0]).Trim() }
} catch {
    $VersionLabel = ""
}
if ($VersionLabel -ne "") {
    $env:WsGameVersionLabel = $VersionLabel
    Write-Host "版本标签：$VersionLabel" -ForegroundColor Cyan
} else {
    Write-Host "版本标签：（推导失败：需要 python 与 git 可用）" -ForegroundColor Yellow
}

if ($ArtifactsPath -eq "") {
    $ArtifactsPath = Join-Path $RepoRoot "bin\_check_artifacts"
}
if (-not (Test-Path $ArtifactsPath)) {
    New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
}
$UnityOutDir = Join-Path $ArtifactsPath "unity"
if (-not (Test-Path $UnityOutDir)) {
    New-Item -ItemType Directory -Force -Path $UnityOutDir | Out-Null
}

# -----------------------------------------------------------------------------
# 定向门禁（ADR-0126）：父进程。调 toolchain/change_impact.py 判定 -> 打印「本次判定」-> -DryRun 到此
# 为止；否则把自己再起一个子进程（-TargetedInner）跑真正的步骤，全文输出落盘，控制台只留每步一行结果、
# 汇总表、首个失败步骤的最后 30 行（日志分流，见 .SYNOPSIS 判断记录 8)）。不传任何定向参数时整段跳过。
# 判定失败不退回全量：直接退出码 1。
# -----------------------------------------------------------------------------
$TargetedMode = $PSBoundParameters.ContainsKey("Changed") -or $Staged -or ($Modules.Count -gt 0) -or $DryRun -or $TargetedInner
if ($TargetedMode -and -not $TargetedInner) {
    $impactScript = Join-Path $RepoRoot "toolchain\change_impact.py"
    $planPath = Join-Path $ArtifactsPath "change_impact_plan.json"
    $planTextPath = Join-Path $ArtifactsPath "change_impact_plan.txt"
    $moduleList = @($Modules | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })

    $impactArgs = @($impactScript)
    if ($Staged) {
        $impactArgs += "--staged"
    } elseif ($PSBoundParameters.ContainsKey("Changed") -or $moduleList.Count -eq 0) {
        $impactArgs += @("--base", $Changed)
    }
    if ($moduleList.Count -gt 0) {
        $impactArgs += @("--modules", ($moduleList -join ","))
    }
    $impactArgs += @("--out", $planPath, "--text-out", $planTextPath, "--quiet")

    $ErrorActionPreference = "Continue"
    & python @impactArgs
    $impactExit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($impactExit -ne 0 -or -not (Test-Path -LiteralPath $planTextPath)) {
        Write-Host "定向判定失败（toolchain/change_impact.py 退出码 $impactExit），不退回全量，请修正参数后重跑。" -ForegroundColor Red
        exit 1
    }
    Get-Content -LiteralPath $planTextPath -Encoding UTF8 | ForEach-Object { Write-Host $_ }

    if ($DryRun) {
        $dryPlan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]$dryPlan.engine.playmode_filter -ne "") {
            Write-Host "将传给 Unity 的 PlayMode 参数：-runTests -testPlatform PlayMode -testCategory `"$($dryPlan.engine.playmode_filter)`"" -ForegroundColor Cyan
        } elseif ([string]$dryPlan.engine.mode -eq "all") {
            Write-Host "将传给 Unity 的 PlayMode 参数：-runTests -testPlatform PlayMode（不加 -testCategory，跑全部）" -ForegroundColor Cyan
        }
        Write-Host "（-DryRun：只打印判定，未执行任何步骤）" -ForegroundColor Yellow
        exit 0
    }

    $fullLog = if ($LogFile -ne "") { $LogFile } else { Join-Path $ArtifactsPath "check_targeted.log" }
    $fullLogDir = Split-Path -Parent $fullLog
    if ($fullLogDir -and -not (Test-Path $fullLogDir)) {
        New-Item -ItemType Directory -Force -Path $fullLogDir | Out-Null
    }
    $innerArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath,
        "-TargetedInner", "-PlanFile", $planPath, "-ArtifactsPath", $ArtifactsPath, "-Configuration", $Configuration,
        "-TimingTask", $TimingTask)
    foreach ($sw in @("SkipUnity", "SkipSmoke", "SkipConsumer", "Quick", "DocsOnly", "AbiStrict", "Il2cpp", "FailFast", "NoTiming")) {
        if ((Get-Variable -Name $sw -ValueOnly)) { $innerArgs += "-$sw" }
    }
    if ($UnityExe -ne "") { $innerArgs += @("-UnityExe", $UnityExe) }

    Write-Host ""
    Write-Host "==== 开始执行（全文日志：$fullLog；控制台只显示每步结果、汇总表、首个失败的最后 30 行） ====" -ForegroundColor Cyan
    $hostExe = (Get-Process -Id $PID).Path
    $logWriter = New-Object System.IO.StreamWriter($fullLog, $false, (New-Object System.Text.UTF8Encoding($false)))
    try {
        & $hostExe @innerArgs | ForEach-Object {
            $logLine = [string]$_
            $logWriter.WriteLine($logLine)
            if ($logLine -match '^\[.+\] (通过|失败)，用时') {
                Write-Host $logLine
            }
        }
        $innerExit = $LASTEXITCODE
    } finally {
        $logWriter.Flush()
        $logWriter.Close()
    }

    $logLines = @(Get-Content -LiteralPath $fullLog -Encoding UTF8)
    $summaryIdx = -1
    $firstFailIdx = -1
    for ($i = 0; $i -lt $logLines.Count; $i++) {
        if ($summaryIdx -lt 0 -and $logLines[$i] -eq "==== 汇总 ====") { $summaryIdx = $i }
        if ($firstFailIdx -lt 0 -and $logLines[$i] -match '^\[.+\] 失败，用时') { $firstFailIdx = $i }
    }
    if ($firstFailIdx -ge 0) {
        $failStart = [Math]::Max(0, $firstFailIdx - 29)
        Write-Host ""
        Write-Host "---- 首个失败步骤的输出（最后 30 行；全文见 $fullLog） ----" -ForegroundColor Red
        $logLines[$failStart..$firstFailIdx] | ForEach-Object { Write-Host $_ }
    }
    if ($summaryIdx -ge 0) {
        $logLines[$summaryIdx..($logLines.Count - 1)] | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host ""
        Write-Host "子进程没有打印汇总表（可能异常退出），日志最后 30 行：" -ForegroundColor Red
        $tailStart = [Math]::Max(0, $logLines.Count - 30)
        $logLines[$tailStart..($logLines.Count - 1)] | ForEach-Object { Write-Host $_ }
    }
    Write-Host "完整日志：$fullLog"
    exit $innerExit
}

# -----------------------------------------------------------------------------
# -LogFile：见 .PARAMETER LogFile 判断记录。Start-Transcript 会原样录下本脚本之后所有
# Write-Host/输出到宿主的内容；脚本正常从两个 exit 出口结束时会显式 Stop-Transcript；trap 兜底
# 覆盖"某处抛出未被 Invoke-CheckStep 接住的异常、脚本非正常终止"这一少见路径。
# -----------------------------------------------------------------------------
$script:TranscriptStarted = $false
if ($LogFile -ne "") {
    $logFileDir = Split-Path -Parent $LogFile
    if ($logFileDir -and -not (Test-Path $logFileDir)) {
        New-Item -ItemType Directory -Force -Path $logFileDir | Out-Null
    }
    try {
        Start-Transcript -Path $LogFile -Force | Out-Null
        $script:TranscriptStarted = $true
    } catch {
        Write-Host "警告：Start-Transcript 失败（$($_.Exception.Message)），本次运行不落 -LogFile，仅打印到控制台。" -ForegroundColor Yellow
    }
}
trap {
    if ($script:TranscriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
        $script:TranscriptStarted = $false
    }
}

$OverallStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# -----------------------------------------------------------------------------
# 步骤汇总基础设施：见 toolchain/_gate_step_runner.ps1（Invoke-CheckStep/Add-SkippedStep/
# Test-NativeExitCode 等，原 check.ps1 内联实现原样搬入该文件，供本脚本与两条并行子线共用）。
# -----------------------------------------------------------------------------
$script:Results = New-Object System.Collections.Generic.List[Object]
$script:GateFailed = $false
# 主进程自己的"快速前置阶段"跑在单一进程里，不涉及跨进程通信，不需要 FailFast 标记文件——
# 置 $null 时 Invoke-CheckStep 只看本进程内的 $script:GateFailed，等价于旧行为。
$script:FailFastFlagPath = $null

. (Join-Path $RepoRoot "toolchain\_gate_step_runner.ps1")
# 定向门禁子进程：载入父进程写好的判定 JSON（非定向模式 $PlanFile 为空串，什么都不做）。
Import-GatePlan -Path $PlanFile

# =============================================================================
# 阶段一：快速前置步骤（串行，见本文件顶部判断记录 2)——秒级、不依赖 Unity/重构建，排在最前面）
# =============================================================================

# -----------------------------------------------------------------------------
# 0. 门禁自检（F1 根治回归，audit-20260907/delivery-validation.md）：
#    Test-NativeExitCode 此前会把原生命令的 stdout 泄漏进 Invoke-CheckStep 的结果判定，导致
#    "有输出且退出码非零"的失败命令被误判为 PASS（复现细节见 toolchain/_gate_step_runner.ps1
#    两函数上方判断记录）。本步骤在全部真正的检查步骤之前，用两个独立探针（失败/成功各一次，均带
#    stdout 输出）验证判定逻辑本身是可信的——如果这一步本身失败，说明门禁基础设施有问题，后续
#    全部步骤的 PASS/FAIL 都不可信，理应第一个报告。
# -----------------------------------------------------------------------------
Invoke-CheckStep "门禁自检：Test-NativeExitCode 对失败/成功原生命令正确判定" -DocRelevant -Id "self_check" {
    $failProbeOk = Test-NativeExitCode "powershell.exe" @("-NoProfile", "-Command", "Write-Output 'F1_SELF_CHECK_PROBE'; exit 7")
    if ($failProbeOk) {
        return [PSCustomObject]@{ Ok = $false; Detail = "失败探针（stdout 非空 + exit 7）被误判为成功——Test-NativeExitCode 回归，见该函数判断记录" }
    }

    $passProbeOk = Test-NativeExitCode "powershell.exe" @("-NoProfile", "-Command", "Write-Output 'F1_SELF_CHECK_PROBE'; exit 0")
    if (-not $passProbeOk) {
        return [PSCustomObject]@{ Ok = $false; Detail = "成功探针（stdout 非空 + exit 0）被误判为失败" }
    }

    cmd.exe /c exit 0 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "探针 3 前置条件失败：未能把 `$LASTEXITCODE 钉在 0" }
    }
    $missingExeProbeOk = Test-NativeExitCode "__ws_game_check_missing_executable__" @()
    if ($missingExeProbeOk) {
        return [PSCustomObject]@{ Ok = $false; Detail = "缺失可执行文件探针被误判为成功——Test-NativeExitCode 对 TOOL-01 回归，见该函数判断记录" }
    }

    $true
}

# -----------------------------------------------------------------------------
# 1. 禁用词扫描：全仓库不出现具体游戏代号（见 CLAUDE.md 硬性规则）。
#    gate-speed 任务改造：改用 `git grep`，只扫受版本管理的文件，不再靠目录名黑名单排除
#    Get-ChildItem -Recurse 遍历出来的构建产物/缓存目录（判断记录见本文件顶部 4)）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "禁用词扫描：全仓库不出现具体游戏代号（git grep，只扫受版本管理的文件）" -DocRelevant -Id "ban_codename" {
    # 见原实现同一处注释：字符串拼接构造被扫描词，避免脚本自身源码里出现完整拼写。
    $bannedCodename = "note" + "moss"
    Push-Location $RepoRoot
    try {
        $grepOutput = & git grep -n -i -I -F -- $bannedCodename . ":(exclude)check.ps1" 2>&1
        $grepExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($grepExit -gt 1) {
        return [PSCustomObject]@{ Ok = $false; Detail = "git grep 执行失败（退出码 $grepExit）：$($grepOutput -join '; ')" }
    }
    if ($grepExit -eq 0) {
        $lines = @($grepOutput)
        throw "发现 $($lines.Count) 处具体游戏代号命中：`n$($lines -join "`n")"
    }
    $true
}

# -----------------------------------------------------------------------------
# 2. 禁用词扫描：architecture 正文不出现引擎/语言/框架/工具名（immunity 例外）。只遍历
#    architecture/0*.md、1*.md、architecture/adr/*.md 这一小撮文件，本身不是耗时来源，维持
#    Get-ChildItem 实现不变。
# -----------------------------------------------------------------------------
function Get-ScannableFiles {
    param([string]$Root, [string[]]$ExtraExcludeFullNames)
    $excludedDirs = @(".git", "bin", "obj", "Library", "StreamingAssets", "dist")
    Get-ChildItem -Path $Root -Recurse -File -Force -ErrorAction SilentlyContinue | Where-Object {
        $relative = $_.FullName.Substring($Root.Length).TrimStart("\", "/")
        $segments = $relative -split "[\\/]"
        $hit = $false
        foreach ($seg in $segments) {
            foreach ($ex in $excludedDirs) {
                if ($seg -ieq $ex) { $hit = $true }
            }
        }
        if ($ExtraExcludeFullNames -contains $_.FullName) { $hit = $true }
        -not $hit
    }
}

Invoke-CheckStep "禁用词扫描：architecture 正文不出现引擎/语言/框架/工具名（immunity 例外）" -DocRelevant -Id "ban_arch_terms" {
    $targets = @()
    $targets += Get-ChildItem -Path (Join-Path $RepoRoot "architecture") -Filter "0*.md" -File -ErrorAction SilentlyContinue
    $targets += Get-ChildItem -Path (Join-Path $RepoRoot "architecture") -Filter "1*.md" -File -ErrorAction SilentlyContinue
    $adrDir = Join-Path $RepoRoot "architecture\adr"
    if (Test-Path $adrDir) {
        $targets += Get-ChildItem -Path $adrDir -Filter "*.md" -File -ErrorAction SilentlyContinue
    }

    $unityPattern = "(?<![A-Za-z])unity(?![A-Za-z])"
    $plainWords = @("c#", "csharp", "\.net", "xunit", "python", "powershell")
    $combinedPattern = $unityPattern + "|" + ($plainWords -join "|")

    $hits = @()
    foreach ($f in $targets) {
        $m = Select-String -Path $f.FullName -Pattern $combinedPattern -AllMatches -CaseSensitive:$false -ErrorAction SilentlyContinue
        if ($m) { $hits += $m }
    }
    if ($hits.Count -gt 0) {
        $lines = $hits | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw "发现 $($hits.Count) 处技术名命中：`n$($lines -join "`n")"
    }
    $true
}

# -----------------------------------------------------------------------------
# 3. 版本一致性：VERSION、两个 package.json、packages-lock.json 与 CHANGELOG.md
# -----------------------------------------------------------------------------
Invoke-CheckStep "版本一致性：VERSION、两个 package.json、packages-lock.json 与 CHANGELOG.md" -DocRelevant -Id "version_consistency" {
    $versionPath = Join-Path $RepoRoot "VERSION"
    if (-not (Test-Path $versionPath)) {
        throw "找不到版本文件：$versionPath"
    }
    $version = (Get-Content -Path $versionPath -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "VERSION 内容格式非法：'$version'（需形如 X.Y.Z）"
    }

    $adapterPkgPath = Join-Path $RepoRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\package.json"
    $templatePkgPath = Join-Path $RepoRoot "games\_template\package.json"

    $adapterPkg = (Get-Content -Path $adapterPkgPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $templatePkg = (Get-Content -Path $templatePkgPath -Raw -Encoding UTF8) | ConvertFrom-Json

    $mismatches = @()
    if ($adapterPkg.version -ne $version) {
        $mismatches += "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json version=$($adapterPkg.version) != VERSION=$version"
    }
    if ($templatePkg.version -ne $version) {
        $mismatches += "games/_template/package.json version=$($templatePkg.version) != VERSION=$version"
    }
    $templateDepVersion = $templatePkg.dependencies."com.gamefoundation.adapter.unity"
    if ($templateDepVersion -ne $version) {
        $mismatches += "games/_template/package.json dependencies.com.gamefoundation.adapter.unity=$templateDepVersion != VERSION=$version"
    }

    $packagesLockPath = Join-Path $RepoRoot "adapters\unity\Packages\packages-lock.json"
    if (-not (Test-Path $packagesLockPath)) {
        $mismatches += "找不到 $packagesLockPath，无法核对 com.gamefoundation.game-template 依赖版本号"
    } else {
        $packagesLockRaw = [System.IO.File]::ReadAllText($packagesLockPath)
        $lockDepMatch = [regex]::Match($packagesLockRaw, '"com\.gamefoundation\.adapter\.unity":\s*"(\d+\.\d+\.\d+)"')
        if (-not $lockDepMatch.Success) {
            $mismatches += "$packagesLockPath 中未找到 'com.gamefoundation.adapter.unity' 依赖字段"
        } else {
            $lockDepVersion = $lockDepMatch.Groups[1].Value
            if ($lockDepVersion -ne $version) {
                $mismatches += "adapters/unity/Packages/packages-lock.json com.gamefoundation.game-template.dependencies.com.gamefoundation.adapter.unity=$lockDepVersion != VERSION=$version"
            }
        }
    }

    $changelogPath = Join-Path $RepoRoot "CHANGELOG.md"
    if (-not (Test-Path $changelogPath)) {
        $mismatches += "找不到 CHANGELOG.md（见根 README.md'版本与发布'一节）"
    } else {
        $changelogLines = Get-Content -Path $changelogPath -Encoding UTF8
        $versionHeadingPattern = '^##\s*\[' + [regex]::Escape($version) + '\]'
        $hasVersionEntry = $false
        $hasUnreleasedSection = $false
        foreach ($line in $changelogLines) {
            if ($line -match $versionHeadingPattern) { $hasVersionEntry = $true }
            if ($line -match '^##\s*\[Unreleased\]') { $hasUnreleasedSection = $true }
        }
        if ((-not $hasVersionEntry) -and (-not $hasUnreleasedSection)) {
            $mismatches += "CHANGELOG.md 既没有 '## [$version]' 条目，也没有 '## [Unreleased]' 段——VERSION=$version 的变更记录缺失"
        }
    }

    if ($mismatches.Count -gt 0) {
        throw ("版本不一致：`n" + ($mismatches -join "`n"))
    }
    [PSCustomObject]@{ Ok = $true; Detail = "VERSION=$version，两个 package.json、packages-lock.json 与 CHANGELOG.md 一致" }
}

# -----------------------------------------------------------------------------
# 3a. 分支名规范（ADR-0127，AGENTS.md §1b）：feature/、bugfix/ 前缀的分支必须形如
#     `feature|bugfix/<小写英文数字连字符>_<八位年月日>`；main、release/X.Y.x、游离 HEAD 不判定。
#     纯 Python、毫秒级，所有模式都跑（含 -DocsOnly）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "分支名规范（python toolchain/version_label.py --check-branch-name，ADR-0127）" -DocRelevant -Id "branch_name" {
    $branchOut = & python (Join-Path $RepoRoot "toolchain\version_label.py") "--repo-root" $RepoRoot "--check-branch-name"
    $branchExit = $LASTEXITCODE
    $branchText = (@($branchOut) -join " ").Trim()
    if ($branchExit -ne 0) {
        Write-Host $branchText -ForegroundColor Red
    }
    [PSCustomObject]@{ Ok = ($branchExit -eq 0); Detail = $branchText }
}

# -----------------------------------------------------------------------------
# 3b. 模块表自检（ADR-0126）：toolchain/module_map.json 必须覆盖所有子模块目录——出现未登记的子模块目录
#     或表里登记了不存在的目录即 FAIL（定向门禁按这张表选测，表漂移会让"该跑的测试没被选中"）。
#     纯 Python、毫秒级，所有模式（全量/-Quick/定向）都跑；-DocsOnly 下不跑（改文档不会增删子模块目录）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "模块表覆盖所有子模块目录（python toolchain/gen_module_map.py --check，ADR-0126）" -Id "module_map_check" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/gen_module_map.py", "--check")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 4. 数据校验（合并根：data/_framework + data/_sample）
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --strict（合并根）" -Id "validate_merged" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/validate_data.py", "--strict")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 5. 框架根单独完整校验（不加 --strict，见 data/README.md"与校验器的关系"一节判断记录）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --data-root data/_framework（框架根单独完整校验）" -Id "validate_framework" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/validate_data.py", "--data-root", "data/_framework")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 6. 元数据门禁：validator --schema-audit（ADR-0018 决策 3/ADR-0019 决策 4）——秒级，不加载
#    任何数据。
# -----------------------------------------------------------------------------
Invoke-CheckStep "元数据门禁：validator --schema-audit（ADR-0018 决策 3/ADR-0019 决策 4）" -Id "schema_audit" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "dotnet" @("run", "--project", "toolchain/validator", "--", "--schema-audit", "--allowlist", "toolchain/schema_audit_allowlist.json")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 7. 事件常量生成器一致性检查
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/gen_event_constants.py --check" -Id "event_constants" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/gen_event_constants.py", "--check")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 8. 数据表字段顺序与 schema 登记顺序一致性检查（消费方反馈 E11）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/format_data.py --schema-order --check（消费方反馈 E11）" -Id "schema_order" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/format_data.py", "--schema-order", "--check", "--data-root", "data/_framework", "--data-root", "data/_sample")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 9. 资产导入工具交叉校验（import_assets.py check，全量交叉校验 sprite/vfx/sfx/world 四域，只
#    比对文件是否存在、不读图片，秒级完成）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/import_assets.py check --dataset _sample" -Id "import_assets_check" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/import_assets.py", "check", "--dataset", "_sample")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 10. core/sim/tests/data（嵌入仿真数据集）单独 validate_data.py --strict 校验（反馈 46 后续，
#     见 core/sim/README.md 判断记录 41——秒级的纯数据/元数据校验，不跑仿真、不编译 Unity）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --strict --data-root core/sim/tests/data（嵌入仿真数据集单独校验）" -Id "validate_sim_data" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/validate_data.py", "--strict", "--framework-root", "data/_framework", "--data-root", "core/sim/tests/data")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 10b. 模板数据根 games/_template/data/game 单独 validate_data.py --strict 校验（复盘 I-1，
#      2026-10-01）。AGENTS.md §4 G1 要求"默认数据根与 games/_template/data/game 必须 warnings 为
#      0"，但此前门禁只覆盖合并根、data/_framework、core/sim/tests/data 三套，模板数据根从未进过
#      任何自动步骤——它随分发包发给每个新游戏，一旦带着错误/警告出门，消费方第一次校验就红。秒级
#      步骤，-Quick / -SkipUnity 下同样跑（放在"便宜的先跑"这批里）。
#      判定比 --strict 多一层：--strict 只把"可升级"的警告升成错误，validator 输出里标了
#      non-escalatable 的警告规则即使命中也不会让退出码变非 0，所以额外解析汇总行
#      `errors N, warnings M`，warnings 必须恰为 0（G1 原话）。games/_template/validate.ps1 是随模板
#      分发给消费方的校验入口（还会跑资产交叉校验、需要消费方自己的目录布局），不是门禁步骤，
#      与本步骤不重复调用同一件事，保留不动。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --strict --data-root games/_template/data/game（模板数据根单独校验，warnings 须为 0）" -Id "validate_template_data" {
    Push-Location $RepoRoot
    try {
        $ErrorActionPreference = "Continue"
        $templateOutput = @(& python "toolchain/validate_data.py" "--strict" "--framework-root" "data/_framework" "--data-root" "games/_template/data/game")
        $templateExit = $LASTEXITCODE
        $templateOutput | ForEach-Object { Write-Host $_ }
    } finally {
        Pop-Location
    }
    if ($templateExit -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "validate_data.py 退出码=$templateExit，见上方输出" }
    }
    $summary = @($templateOutput | Where-Object { $_ -match 'errors\s+(\d+),\s*warnings\s+(\d+)' })
    if ($summary.Count -eq 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "未在输出里找到 'errors N, warnings M' 汇总行，无法确认 warnings 为 0" }
    }
    [void]($summary[-1] -match 'errors\s+(\d+),\s*warnings\s+(\d+)')
    $tplErrors = [int]$Matches[1]
    $tplWarnings = [int]$Matches[2]
    if ($tplErrors -ne 0 -or $tplWarnings -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "模板数据根应为 0 error 0 warning（AGENTS.md §4 G1），实际 errors=$tplErrors warnings=$tplWarnings" }
    }
    [PSCustomObject]@{ Ok = $true; Detail = "errors=0 warnings=0" }
}

# -----------------------------------------------------------------------------
# 10c. 手感实验室数据集 data/_lab 单独 validate_data.py --strict 校验（手感设计/06，实验室切片）。
#      _lab 是框架级数据集（占位命名，不进合并根 data/_framework 与默认游戏根），只被实验室命令行/测试按
#      "框架根 + _lab" 叠加装载；判定同模板数据根：退出码 0 且汇总行 warnings 恰为 0。秒级步骤。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --strict --data-root data/_lab（手感实验室数据集单独校验，warnings 须为 0）" -Id "validate_lab_data" {
    Push-Location $RepoRoot
    try {
        $ErrorActionPreference = "Continue"
        $labDataOutput = @(& python "toolchain/validate_data.py" "--strict" "--framework-root" "data/_framework" "--data-root" "data/_lab")
        $labDataExit = $LASTEXITCODE
        $labDataOutput | ForEach-Object { Write-Host $_ }
    } finally {
        Pop-Location
    }
    if ($labDataExit -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "validate_data.py 退出码=$labDataExit，见上方输出" }
    }
    $labSummaryLines = @($labDataOutput | Where-Object { $_ -match 'errors\s+(\d+),\s*warnings\s+(\d+)' })
    if ($labSummaryLines.Count -eq 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "未在输出里找到 'errors N, warnings M' 汇总行，无法确认 warnings 为 0" }
    }
    [void]($labSummaryLines[-1] -match 'errors\s+(\d+),\s*warnings\s+(\d+)')
    $labErrors = [int]$Matches[1]
    $labWarnings = [int]$Matches[2]
    if ($labErrors -ne 0 -or $labWarnings -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "实验室数据集应为 0 error 0 warning，实际 errors=$labErrors warnings=$labWarnings" }
    }
    [PSCustomObject]@{ Ok = $true; Detail = "errors=0 warnings=0" }
}

# -----------------------------------------------------------------------------
# 10d. 占位装备集数据 data/_equip 单独 validate_data.py --strict 校验（手感设计/08，装备资产包切片）。
#      _equip 是框架级参照实现数据集（与 _lab 同类，不进合并根与游戏根），装备行里的 feel.weapon 引用要靠
#      data/_feel（框架手感档案）解析，所以按"框架根 + data/_feel + data/_equip"三根叠加装载；判定同实验室
#      数据集：退出码 0 且汇总行 warnings 恰为 0。秒级步骤。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --strict --data-root data/_equip（占位装备集数据，叠加框架根与 data/_feel，warnings 须为 0）" -Id "validate_equip_data" {
    Push-Location $RepoRoot
    try {
        $ErrorActionPreference = "Continue"
        $equipDataOutput = @(& python "toolchain/validate_data.py" "--strict" "--framework-root" "data/_framework" "--data-root" "data/_feel" "--data-root" "data/_equip")
        $equipDataExit = $LASTEXITCODE
        $equipDataOutput | ForEach-Object { Write-Host $_ }
    } finally {
        Pop-Location
    }
    if ($equipDataExit -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "validate_data.py 退出码=$equipDataExit，见上方输出" }
    }
    $equipSummaryLines = @($equipDataOutput | Where-Object { $_ -match 'errors\s+(\d+),\s*warnings\s+(\d+)' })
    if ($equipSummaryLines.Count -eq 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "未在输出里找到 'errors N, warnings M' 汇总行，无法确认 warnings 为 0" }
    }
    [void]($equipSummaryLines[-1] -match 'errors\s+(\d+),\s*warnings\s+(\d+)')
    $equipErrors = [int]$Matches[1]
    $equipWarnings = [int]$Matches[2]
    if ($equipErrors -ne 0 -or $equipWarnings -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "占位装备集数据应为 0 error 0 warning，实际 errors=$equipErrors warnings=$equipWarnings" }
    }
    [PSCustomObject]@{ Ok = $true; Detail = "errors=0 warnings=0" }
}

# -----------------------------------------------------------------------------
# 10d. 手感框架数据集 data/_feel 单独 validate_data.py --strict 校验（手感落地 S1 收 S0 遗留 (a)）。
#      _feel 随 data/_framework 一起被框架消费方装载；它必须能"自己"过校验（含缺省标定行 feel.calibration.framework_default），
#      不依赖游戏根补标定。判定同实验室数据集：退出码 0 且汇总行 errors/warnings 均为 0。秒级步骤。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --strict --data-root data/_feel（手感框架数据集单独校验，errors/warnings 须为 0）" -Id "validate_feel_data" {
    Push-Location $RepoRoot
    try {
        $ErrorActionPreference = "Continue"
        $feelDataOutput = @(& python "toolchain/validate_data.py" "--strict" "--data-root" "data/_feel")
        $feelDataExit = $LASTEXITCODE
        $feelDataOutput | ForEach-Object { Write-Host $_ }
    } finally {
        Pop-Location
    }
    if ($feelDataExit -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "validate_data.py 退出码=$feelDataExit，见上方输出" }
    }
    $feelSummaryLines = @($feelDataOutput | Where-Object { $_ -match 'errors\s+(\d+),\s*warnings\s+(\d+)' })
    if ($feelSummaryLines.Count -eq 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "未在输出里找到 'errors N, warnings M' 汇总行，无法确认 errors/warnings 为 0" }
    }
    [void]($feelSummaryLines[-1] -match 'errors\s+(\d+),\s*warnings\s+(\d+)')
    $feelErrors = [int]$Matches[1]
    $feelWarnings = [int]$Matches[2]
    if ($feelErrors -ne 0 -or $feelWarnings -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "手感框架数据集应为 0 error 0 warning，实际 errors=$feelErrors warnings=$feelWarnings" }
    }
    [PSCustomObject]@{ Ok = $true; Detail = "errors=0 warnings=0" }
}

# -----------------------------------------------------------------------------
# 10e. 装备完整性检查（import_assets.py equip，手感设计/08 第 5 节）：装备资产包（图标、外观映射、纸娃娃层 ×
#      必备/推荐姿势键 × 方向档、武器双表、音效材质）+ 界面皮肤包（槽位框/品质框/拖拽态/提示框/预览区/主题）。
#      只比对文件与数据行是否存在，秒级。--strict-warnings：框架占位装备集是参照实现，必须零错误零警告
#      （游戏自己的装备集默认警告不阻断，见 toolchain/asset_import/equip_cmd.py 判断记录）。报告写
#      bin/_check_artifacts/equip_report/（已在 .gitignore，只留本地）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/import_assets.py equip --strict-warnings（占位装备集 + 皮肤包完整性）" -Id "equip_pack_check" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/import_assets.py", "equip", "--strict-warnings")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 11. 工作树文本文件无 CR（消费方反馈 E7 根治）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "工作树文本文件无 CR（.gitattributes 声明 eol=lf 的路径，消费方反馈 E7）" -Id "crlf_check" {
    Push-Location $RepoRoot
    try {
        $eolOutput = & git ls-files --eol
        if ($LASTEXITCODE -ne 0) {
            throw "git ls-files --eol 失败，退出码 $LASTEXITCODE"
        }
        $violations = @($eolOutput | Where-Object { $_ -match 'w/crlf' -and $_ -match 'eol=lf' })
        if ($violations.Count -gt 0) {
            throw ("发现 " + $violations.Count + " 个文件已在 .gitattributes 声明 eol=lf，但工作树实际是 CRLF：`n  " + ($violations -join "`n  "))
        }
        $true
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 12. Unity .meta 完整性检查（不依赖 Unity 本体，toolchain/check_unity_meta.py）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "Unity .meta 完整性检查（不依赖 Unity，toolchain/check_unity_meta.py）" -Id "unity_meta" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/check_unity_meta.py")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 12.6 verdaccio -Stop 误判回归（test_registry_stop_pidfile_rewrite_timestamp.py）单独抽出，
#     串行跑在两条并行线派生之前——不与 Unity 串行线共享执行时间窗口。判断记录（根因/为什么这样
#     处理）见本文件顶部 .SYNOPSIS 判断记录 6)（gate-parallel-ctrlc 修复，2026-09-22）。
#     -Quick 跳过（该用例本身要跑真实 Windows 进程自动化，非秒级）；-DocsOnly 下由
#     Invoke-CheckStep 通用短路逻辑自动 SKIP（未标 -DocRelevant）。
# -----------------------------------------------------------------------------
if ($Quick) {
    Add-SkippedStep "python -m pytest toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py -q（隔离于并行线之外）" "-Quick" -Id "registry_pytest"
} else {
    Invoke-CheckStep "python -m pytest toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py -q（隔离于并行线之外，见 .SYNOPSIS 判断记录 6)）" -Id "registry_pytest" {
        $prevPythonUtf8 = $env:PYTHONUTF8
        $env:PYTHONUTF8 = "1"
        Push-Location $RepoRoot
        try {
            Test-NativeExitCode "python" @(
                "-m", "pytest",
                "toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py",
                "-q")
        } finally {
            Pop-Location
            $env:PYTHONUTF8 = $prevPythonUtf8
        }
    }
}

# -----------------------------------------------------------------------------
# 12.5 -DocsOnly 专用：toolchain 自身 pytest 套件里两个文档相关用例。
# -----------------------------------------------------------------------------
if ($DocsOnly -or $script:GateStepPlan) {
    Invoke-CheckStep "python -m pytest toolchain/tests -q（文档相关子集：markdown 链接 + 编辑器文档一致性，-DocsOnly/定向）" -DocRelevant -Id "docs_pytest" {
        $prevPythonUtf8 = $env:PYTHONUTF8
        $env:PYTHONUTF8 = "1"
        Push-Location $RepoRoot
        try {
            Test-NativeExitCode "python" @(
                "-m", "pytest",
                "toolchain/tests/test_markdown_relative_links.py",
                "toolchain/tests/test_editor_doc_consistency.py",
                "-q")
        } finally {
            Pop-Location
            $env:PYTHONUTF8 = $prevPythonUtf8
        }
    }
}

# -----------------------------------------------------------------------------
# 12.6 定向专用：提交前钩子与忽略规则（.githooks/**、.gitignore，ADR-0126 路径规则 git_hooks_and_ignore）
#      只跑钩子相关的 pytest 子集——提交前分级守卫（钩子里"发布提交放行"的判定纯函数）。
#      只在定向模式（有判定结果）下出现；全量门禁由 toolchain 全量 pytest 覆盖，不重复。
# -----------------------------------------------------------------------------
if ($script:GateStepPlan) {
    Invoke-CheckStep "python -m pytest toolchain/tests/test_precommit_tiering_guard.py -q（钩子相关子集，定向）" -Id "hooks_pytest" {
        $prevPythonUtf8 = $env:PYTHONUTF8
        $env:PYTHONUTF8 = "1"
        Push-Location $RepoRoot
        try {
            Test-NativeExitCode "python" @(
                "-m", "pytest",
                "toolchain/tests/test_precommit_tiering_guard.py",
                "-q")
        } finally {
            Pop-Location
            $env:PYTHONUTF8 = $prevPythonUtf8
        }
    }
}

# =============================================================================
# 阶段二：两条线并行（见本文件顶部判断记录 3)）
# =============================================================================

$parallelSeconds = 0.0
$heavyResultsJson = Join-Path $ArtifactsPath "gate_line_heavy_results.json"
$unityResultsJson = Join-Path $ArtifactsPath "gate_line_unity_results.json"
$heavyLog = Join-Path $ArtifactsPath "gate_line_heavy.log"
$unityLog = Join-Path $ArtifactsPath "gate_line_unity.log"
$failFastFlagPath = Join-Path $ArtifactsPath "gate_failfast.flag"
foreach ($staleFile in @($heavyResultsJson, $unityResultsJson, $heavyLog, $unityLog, $failFastFlagPath)) {
    if (Test-Path -LiteralPath $staleFile) {
        Remove-Item -LiteralPath $staleFile -Force -ErrorAction SilentlyContinue
    }
}

if ($FailFast -and $script:GateFailed) {
    Write-Host ""
    Write-Host "==== 非 Unity 重步骤线 / Unity 串行线 ====" -ForegroundColor Cyan
    Write-Host "已跳过：前置的快速检查步骤已失败（-FailFast），两条并行线均不再启动。" -ForegroundColor Yellow
    $script:Results.Add((New-GateResultRow -Step "非 Unity 重步骤线（整体）" -Result "SKIP" -Seconds 0 -Detail "前置快速检查失败（-FailFast），本线未启动" -StepId "line_heavy"))
    $script:Results.Add((New-GateResultRow -Step "Unity 串行线（整体）" -Result "SKIP" -Seconds 0 -Detail "前置快速检查失败（-FailFast），本线未启动" -StepId "line_unity"))
} else {
    $heavyScript = Join-Path $RepoRoot "toolchain\_gate_line_heavy.ps1"
    $unityScript = Join-Path $RepoRoot "toolchain\_gate_line_unity.ps1"

    $heavyParams = @{
        RepoRoot                  = $RepoRoot
        ArtifactsPath              = $ArtifactsPath
        Configuration              = $Configuration
        Quick                      = [bool]$Quick
        DocsOnly                   = [bool]$DocsOnly
        FailFast                   = [bool]$FailFast
        AbiStrict                  = [bool]$AbiStrict
        SkipUnity                  = [bool]$SkipUnity
        FailFastFlagPath           = $failFastFlagPath
        PlanFile                   = $PlanFile
        ResultsJsonPath            = $heavyResultsJson
        TranscriptPath             = $heavyLog
        InjectMockSleepSeconds     = $InjectMockSleepHeavySeconds
    }
    $unityParams = @{
        RepoRoot                  = $RepoRoot
        ArtifactsPath              = $ArtifactsPath
        UnityOutDir                = $UnityOutDir
        Configuration              = $Configuration
        Quick                      = [bool]$Quick
        DocsOnly                   = [bool]$DocsOnly
        FailFast                   = [bool]$FailFast
        SkipUnity                  = [bool]$SkipUnity
        SkipSmoke                  = [bool]$SkipSmoke
        SkipConsumer                = [bool]$SkipConsumer
        Il2cpp                     = [bool]$Il2cpp
        UnityExe                   = $UnityExe
        FailFastFlagPath           = $failFastFlagPath
        PlanFile                   = $PlanFile
        ResultsJsonPath            = $unityResultsJson
        TranscriptPath             = $unityLog
        InjectMockSleepSeconds     = $InjectMockSleepUnitySeconds
    }

    $parallelSw = [System.Diagnostics.Stopwatch]::StartNew()

    $jobHeavy = Start-Job -Name "gate_line_heavy" -ScriptBlock {
        param($ScriptPath, $Params)
        & $ScriptPath @Params
    } -ArgumentList $heavyScript, $heavyParams

    $jobUnity = Start-Job -Name "gate_line_unity" -ScriptBlock {
        param($ScriptPath, $Params)
        & $ScriptPath @Params
    } -ArgumentList $unityScript, $unityParams

    Wait-Job -Job $jobHeavy, $jobUnity | Out-Null
    $parallelSw.Stop()
    $parallelSeconds = [Math]::Round($parallelSw.Elapsed.TotalSeconds, 1)

    foreach ($j in @($jobHeavy, $jobUnity)) {
        if ($j.State -eq "Failed") {
            $jobErr = (Receive-Job -Job $j -ErrorAction SilentlyContinue 2>&1 | Out-String)
            $script:Results.Add((New-GateResultRow -Step "$($j.Name)：后台作业本身异常终止" -Result "FAIL" -Seconds 0 -Detail "Job State=$($j.State)；$jobErr" -StepId "$($j.Name)_job_failed"))
        } else {
            Receive-Job -Job $j -ErrorAction SilentlyContinue | Out-Null
        }
        Remove-Job -Job $j -Force -ErrorAction SilentlyContinue
    }

    Write-Host ""
    Write-Host "==== 非 Unity 重步骤线（子进程控制台输出） ====" -ForegroundColor Cyan
    if (Test-Path -LiteralPath $heavyLog) {
        Get-Content -LiteralPath $heavyLog | Write-Host
    }
    Write-Host ""
    Write-Host "==== Unity 串行线（子进程控制台输出） ====" -ForegroundColor Cyan
    if (Test-Path -LiteralPath $unityLog) {
        Get-Content -LiteralPath $unityLog | Write-Host
    }

    Import-GateLineResults -JsonPath $heavyResultsJson -LineLabel "非 Unity 重步骤线" -LineId "line_heavy"
    Import-GateLineResults -JsonPath $unityResultsJson -LineLabel "Unity 串行线" -LineId "line_unity"
}

# -----------------------------------------------------------------------------
# 汇总
# -----------------------------------------------------------------------------
$OverallStopwatch.Stop()
$overallEnd = Get-Date
$overallSeconds = [Math]::Round($OverallStopwatch.Elapsed.TotalSeconds, 1)

Write-Host ""
Write-Host "==== 汇总 ====" -ForegroundColor Cyan
# 并行阶段墙钟只是额外打印的展示行，不写进 $script:Results——不计入步骤总数与失败判定，
# 见本文件顶部判断记录 3) 末段。
$displayRows = New-Object System.Collections.Generic.List[Object]
$displayRows.AddRange($script:Results)
$displayRows.Add([PSCustomObject]@{
    Step    = "（并行阶段墙钟：非 Unity 重步骤线 + Unity 串行线）"
    Result  = "INFO"
    Seconds = $parallelSeconds
    Detail  = "两条线并行执行的实际耗时（不是各步骤 Seconds 求和）；脚本总墙钟 ${overallSeconds}s"
})
$displayRows | Format-Table -AutoSize Step, Result, Seconds, Detail | Out-String -Width 4096 | Write-Host

# 收边任务修正（严重的单步失败漏判 bug）：不加 @() 强制数组上下文时，Where-Object 恰好只匹配到
# 一个对象会返回裸的 PSCustomObject 标量而不是集合——标量没有 Count 属性，$failed.Count 取到
# $null，下面 `$null -gt 0` 在 PowerShell 里是 $false，导致"恰好只有一步失败"这一种情况被误判为
# "门禁通过"。加 @() 强制数组上下文后 .Count 在 0/1/多个匹配下都正确。
$failed = @($script:Results | Where-Object { $_.Result -eq "FAIL" })
$totalSeconds = ($script:Results | Measure-Object -Property Seconds -Sum).Sum

# 环境性 SKIP 单独计数并逐条打印（复盘 I-12 余项，判断记录见 toolchain/_gate_step_runner.ps1
# Get-EnvironmentalSkipRows 与本文件顶部 .SYNOPSIS 判断记录 8)）。不改变通过/失败判定，只让"其实没
# 验证"的 SKIP 在汇总里一眼可辨。
$envSkips = @(Get-EnvironmentalSkipRows -Results $script:Results)
if ($envSkips.Count -gt 0) {
    Write-Host "环境性 SKIP：$($envSkips.Count) 项（非开关导致，该步骤本次没有验证，请确认是否预期）：" -ForegroundColor Yellow
    foreach ($envSkip in $envSkips) {
        Write-Host "  - $($envSkip.Step)：$($envSkip.Detail)" -ForegroundColor Yellow
    }
} else {
    Write-Host "环境性 SKIP：0 项" -ForegroundColor DarkGray
}

if ($failed.Count -gt 0) {
    Write-Host "门禁失败：$($failed.Count) 步未通过（共 $($script:Results.Count) 步，步骤耗时求和 ${totalSeconds}s，脚本总墙钟 ${overallSeconds}s）。" -ForegroundColor Red
    $exitCode = 1
} else {
    Write-Host "门禁通过：全部 $($script:Results.Count) 步（步骤耗时求和 ${totalSeconds}s，脚本总墙钟 ${overallSeconds}s）。" -ForegroundColor Green
    $exitCode = 0
}
if ($VersionLabel -ne "") {
    Write-Host "版本标签：$VersionLabel" -ForegroundColor Cyan
} else {
    Write-Host "版本标签：（推导失败：需要 python 与 git 可用）" -ForegroundColor Yellow
}

# 耗时自动记录（判断记录 10)）：通过/失败都写；写入失败不影响门禁结论，只在汇总末尾打一行警告。
if (-not $NoTiming) {
    $timingPhase = if ($TargetedMode) { "定向门禁" } else { "全量门禁" }
    $timingResult = if ($exitCode -eq 0) { "PASS" } else { "FAIL" }
    $timingError = Write-GateTimingFromRun -RepoRoot $RepoRoot -Results $script:Results -Task $TimingTask `
        -Phase $timingPhase -TotalStart $script:TimingScriptStart -TotalEnd $overallEnd `
        -TotalSeconds $overallSeconds -TotalResult $timingResult `
        -TotalNote "parallel_wall=${parallelSeconds}s; env_skips=$($envSkips.Count)"
    if ($timingError) {
        Write-Host "警告：耗时记录写入 timing/ 失败（$timingError），不影响本次门禁结论。" -ForegroundColor Yellow
    }
}

if ($script:TranscriptStarted) {
    try { Stop-Transcript | Out-Null } catch {}
    $script:TranscriptStarted = $false
}
exit $exitCode

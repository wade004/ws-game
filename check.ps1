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
    build/test、三道数据校验（合并根 + data/_framework 框架根 + core/sim/tests/data 嵌入仿真
    数据集，反馈 46 后续新增第三道，见步骤 6a 判断记录）、事件常量一致性检查、两道禁用词
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
    的真实可运行性。`-SkipUnity` 时本开关不生效（-SkipUnity 已整体跳过 Unity）。

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

# =============================================================================
# 阶段一：快速前置步骤（串行，见本文件顶部判断记录 2)——秒级、不依赖 Unity/重构建，排在最前面）
# =============================================================================

# -----------------------------------------------------------------------------
# 0. 门禁自检（F1 根治回归，architecture/落地计划/audit-20260907/delivery-validation.md）：
#    Test-NativeExitCode 此前会把原生命令的 stdout 泄漏进 Invoke-CheckStep 的结果判定，导致
#    "有输出且退出码非零"的失败命令被误判为 PASS（复现细节见 toolchain/_gate_step_runner.ps1
#    两函数上方判断记录）。本步骤在全部真正的检查步骤之前，用两个独立探针（失败/成功各一次，均带
#    stdout 输出）验证判定逻辑本身是可信的——如果这一步本身失败，说明门禁基础设施有问题，后续
#    全部步骤的 PASS/FAIL 都不可信，理应第一个报告。
# -----------------------------------------------------------------------------
Invoke-CheckStep "门禁自检：Test-NativeExitCode 对失败/成功原生命令正确判定" -DocRelevant {
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
Invoke-CheckStep "禁用词扫描：全仓库不出现具体游戏代号（git grep，只扫受版本管理的文件）" -DocRelevant {
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

Invoke-CheckStep "禁用词扫描：architecture 正文不出现引擎/语言/框架/工具名（immunity 例外）" -DocRelevant {
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
Invoke-CheckStep "版本一致性：VERSION、两个 package.json、packages-lock.json 与 CHANGELOG.md" -DocRelevant {
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
# 4. 数据校验（合并根：data/_framework + data/_sample）
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py --strict（合并根）" {
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
Invoke-CheckStep "python toolchain/validate_data.py --data-root data/_framework（框架根单独完整校验）" {
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
Invoke-CheckStep "元数据门禁：validator --schema-audit（ADR-0018 决策 3/ADR-0019 决策 4）" {
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
Invoke-CheckStep "python toolchain/gen_event_constants.py --check" {
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
Invoke-CheckStep "python toolchain/format_data.py --schema-order --check（消费方反馈 E11）" {
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
Invoke-CheckStep "python toolchain/import_assets.py check --dataset _sample" {
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
Invoke-CheckStep "python toolchain/validate_data.py --strict --data-root core/sim/tests/data（嵌入仿真数据集单独校验）" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/validate_data.py", "--strict", "--framework-root", "data/_framework", "--data-root", "core/sim/tests/data")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 11. 工作树文本文件无 CR（消费方反馈 E7 根治）。
# -----------------------------------------------------------------------------
Invoke-CheckStep "工作树文本文件无 CR（.gitattributes 声明 eol=lf 的路径，消费方反馈 E7）" {
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
Invoke-CheckStep "Unity .meta 完整性检查（不依赖 Unity，toolchain/check_unity_meta.py）" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/check_unity_meta.py")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 12.5 -DocsOnly 专用：toolchain 自身 pytest 套件里两个文档相关用例。
# -----------------------------------------------------------------------------
if ($DocsOnly) {
    Invoke-CheckStep "python -m pytest toolchain/tests -q（文档相关子集：markdown 链接 + 编辑器文档一致性，-DocsOnly）" -DocRelevant {
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
    $script:Results.Add([PSCustomObject]@{ Step = "非 Unity 重步骤线（整体）"; Result = "SKIP"; Seconds = 0; Detail = "前置快速检查失败（-FailFast），本线未启动" })
    $script:Results.Add([PSCustomObject]@{ Step = "Unity 串行线（整体）"; Result = "SKIP"; Seconds = 0; Detail = "前置快速检查失败（-FailFast），本线未启动" })
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
        FailFastFlagPath           = $failFastFlagPath
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
            $script:Results.Add([PSCustomObject]@{
                Step    = "$($j.Name)：后台作业本身异常终止"
                Result  = "FAIL"
                Seconds = 0
                Detail  = "Job State=$($j.State)；$jobErr"
            })
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

    function Import-GateLineResults {
        param([string]$JsonPath, [string]$LineLabel)
        if (-not (Test-Path -LiteralPath $JsonPath)) {
            $script:Results.Add([PSCustomObject]@{
                Step    = "$LineLabel：结果文件缺失"
                Result  = "FAIL"
                Seconds = 0
                Detail  = "子进程未生成 $JsonPath，可能异常退出，请查看对应 .log"
            })
            return
        }
        $raw = Get-Content -LiteralPath $JsonPath -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            $script:Results.Add([PSCustomObject]@{
                Step    = "$LineLabel：结果文件为空"
                Result  = "FAIL"
                Seconds = 0
                Detail  = "$JsonPath 内容为空"
            })
            return
        }
        $items = $raw | ConvertFrom-Json
        if ($items -isnot [array]) { $items = @($items) }
        foreach ($item in $items) {
            $script:Results.Add([PSCustomObject]@{
                Step    = [string]$item.Step
                Result  = [string]$item.Result
                Seconds = [double]$item.Seconds
                Detail  = [string]$item.Detail
            })
        }
    }

    Import-GateLineResults -JsonPath $heavyResultsJson -LineLabel "非 Unity 重步骤线"
    Import-GateLineResults -JsonPath $unityResultsJson -LineLabel "Unity 串行线"
}

# -----------------------------------------------------------------------------
# 汇总
# -----------------------------------------------------------------------------
$OverallStopwatch.Stop()
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

if ($failed.Count -gt 0) {
    Write-Host "门禁失败：$($failed.Count) 步未通过（共 $($script:Results.Count) 步，步骤耗时求和 ${totalSeconds}s，脚本总墙钟 ${overallSeconds}s）。" -ForegroundColor Red
    $exitCode = 1
} else {
    Write-Host "门禁通过：全部 $($script:Results.Count) 步（步骤耗时求和 ${totalSeconds}s，脚本总墙钟 ${overallSeconds}s）。" -ForegroundColor Green
    $exitCode = 0
}

if ($script:TranscriptStarted) {
    try { Stop-Transcript | Out-Null } catch {}
    $script:TranscriptStarted = $false
}
exit $exitCode

<#
.SYNOPSIS
    仓库根一键门禁脚本（见 11_工程规范与测试.md 第 8 节"提交门槛清单"）：依次跑 .NET 构建与
    测试、Python 数据/工具链校验、禁用词扫描、构建产物同步、Unity 编译检查与测试、独立版构建
    与无人值守冒烟，逐步打印耗时与结果，结束时汇总一张表；任一步失败，整体以非 0 退出码结束。

.PARAMETER SkipUnity
    跳过 Unity 相关四步（编译检查、EditMode、PlayMode、独立版构建 + 冒烟）；只跑 .NET/Python/
    禁用词/DLL 同步几步。同一仓库内并行有人独占 Unity 编辑器时用这个开关。

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
    独立版构建产物也落在这个目录的 `unity\` 子目录下。

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
    省略（默认空字符串）时不额外落日志，行为与之前完全一致。用于替代调用方在外层再包一层
    `... 2>&1 | Tee-Object` 的做法——见 .github/workflows/ci.yml 判断记录：外层包一层
    `2>&1` 会把子进程（本脚本）的原生 stderr 输出合并进管道，在 PowerShell 5.1 +
    `$ErrorActionPreference = "Stop"`（GitHub Actions 的 `shell: powershell` 步骤默认注入
    该偏好）组合下，第一行 stderr 就会被提升成终止性的 NativeCommandError 异常，把本该完整
    打印的汇总表和失败明细整个吞掉；改为本脚本自己控制日志落盘，调用方只需直接跑
    `check.ps1 ... -LogFile <path>`、不再包外层管道，退出码仍然是本脚本最后 `exit 0`/
    `exit 1` 的真实值，原样透传给调用方的 `$LASTEXITCODE`。

.PARAMETER Quick
    工程收尾 K 新增，供 `.githooks/pre-commit` 调用：只跑"秒级能跑完"的子集——dotnet
    build/test、两道数据校验（合并根 + data/_framework 框架根）、事件常量一致性检查、两道禁用词
    扫描、版本一致性；跳过占位资产生成器检查（`gen_placeholder_assets.py --check`，需要 Pillow
    且逐张比较占位图较慢）、`toolchain` 自身 pytest、`build.ps1 -SkipTests` 同步、包清单一致性
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

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
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
    [switch]$AbiStrict,
    [switch]$Il2cpp,
    [string]$ArtifactsPath = "",
    [string]$UnityExe = "",
    [string]$Configuration = "Release",
    [string]$LogFile = ""
)

# -Quick 隐含不跑任何 Unity 步骤（见 .PARAMETER Quick 说明），与显式 -SkipUnity 合并为同一个
# 内部开关，下面 Unity 四步 + 消费方演练的 if ($SkipUnity) 分支判断处两者等价处理。
if ($Quick) {
    $SkipUnity = $true
}

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot "Core.sln"

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
# Write-Host/输出到宿主的内容（不需要逐处 Write-Host 调用另外写文件），脚本正常从两个 exit
# 出口结束时会显式 Stop-Transcript；trap 兜底覆盖"某处抛出未被 Invoke-CheckStep 接住的
# 异常、脚本非正常终止"这一少见路径——先关闭 transcript（保证已产生的内容落盘、不因为文件
# 句柄未释放而在 CI 的 upload-artifact 步骤里读到空文件或半截文件），再把异常继续往外抛
# （trap 结尾不写 continue/break 时默认行为就是重新抛出，退出码/错误信息不受影响）。
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

# -----------------------------------------------------------------------------
# 步骤汇总基础设施
# -----------------------------------------------------------------------------

$script:Results = New-Object System.Collections.Generic.List[Object]

function Write-StepHeader {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# $Action 是一个不带参数的 scriptblock：约定返回值有三种形状——
#   1) $null：通过，明细列留空（既有大多数步骤的写法）；
#   2) $true/$false：直接就是通过/失败，明细列留空；
#   3) [PSCustomObject]@{ Ok = <bool>; Detail = <string> }：通过/失败取 Ok，明细列取 Detail
#      （H5 新增，供 Unity EditMode/PlayMode 步骤把 total/passed/failed 计数写进汇总表）；
#   4) [PSCustomObject]@{ Skip = $true; Reason = <string> }：判定为可见 SKIP（不是 PASS，也不是
#      FAIL），明细列取 Reason（外部审计 audit-76d16a5-20260910 PJ114-02 根治新增，等价于
#      Add-SkippedStep，但用在"是否跳过要等脚本内部跑了一步才知道"的场景——例如 ABI 探针要先跑
#      一次 toolchain/abi_probe.ps1 拿到其退出码是不是"基线缺失"的 3，不能像别的步骤那样在
#      Invoke-CheckStep 调用之前就用 if/else 决定要不要整体换成 Add-SkippedStep）。
# 抛异常同样记为失败（异常消息进明细列）。任一步骤失败都不会中断后续步骤（"顺序执行并汇总"，
# 见任务书）。
function Invoke-CheckStep {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-StepHeader $Name
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $ok = $false
    $detail = ""
    try {
        $result = & $Action

        # F1 根治第二层防护（Test-NativeExitCode 本身已经不再泄漏原生命令的 stdout 到管道，见该
        # 函数判断记录；这里额外加一道防线，防止今后有人写出新的 helper 同样把中间输出漏进
        # scriptblock 的返回值）：$result 若是"多元素集合"，说明 $Action 在真正的结果之前还
        # 产生过别的管道输出——按 PowerShell"scriptblock 最后一条表达式的值即返回值"的惯例，
        # 只取最后一个元素参与判定，同时显式告警，让这种"本不该发生"的情况在日志里可见，而不是
        # 被 [bool] 数组转换悄悄吞成恒真（这正是 F1 复现的根因：非空 System.Object[] 经 [bool]
        # 转换恒为 $true，与内容/元素数量无关）。单元素集合（PowerShell 常见的"标量结果被包成
        # 一元数组"情形）直接拆包，不告警。
        if ($result -is [array]) {
            if ($result.Count -gt 1) {
                Write-Host "[$Name] 警告：检查步骤脚本块返回了 $($result.Count) 个对象（应恰好一个），只取最后一个参与判定——前面的对象可能是原生命令泄漏的输出，请检查该步骤实现" -ForegroundColor Yellow
            }
            $result = if ($result.Count -gt 0) { $result[-1] } else { $null }
        }

        if ($result -is [pscustomobject] -and ($result.PSObject.Properties.Name -contains "Skip") -and [bool]$result.Skip) {
            $skipReason = ""
            if (($result.PSObject.Properties.Name -contains "Reason") -and $result.Reason) {
                $skipReason = [string]$result.Reason
            }
            $sw.Stop()
            $seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
            $script:Results.Add([PSCustomObject]@{
                Step    = $Name
                Result  = "SKIP"
                Seconds = $seconds
                Detail  = $skipReason
            })
            Write-Host "[$Name] 已跳过：$skipReason" -ForegroundColor Yellow
            return
        }

        if ($null -eq $result) {
            $ok = $true
        } elseif ($result -is [bool]) {
            $ok = $result
        } elseif ($result -is [pscustomobject] -and ($result.PSObject.Properties.Name -contains "Ok")) {
            $ok = [bool]$result.Ok
            if (($result.PSObject.Properties.Name -contains "Detail") -and $result.Detail) {
                $detail = [string]$result.Detail
            }
        } else {
            $ok = [bool]$result
        }
    } catch {
        $ok = $false
        $detail = $_.Exception.Message
        Write-Host $detail -ForegroundColor Red
    }
    $sw.Stop()
    $seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)

    $script:Results.Add([PSCustomObject]@{
        Step    = $Name
        Result  = if ($ok) { "PASS" } else { "FAIL" }
        Seconds = $seconds
        Detail  = $detail
    })

    if ($ok) {
        Write-Host "[$Name] 通过，用时 ${seconds}s" -ForegroundColor Green
    } else {
        Write-Host "[$Name] 失败，用时 ${seconds}s" -ForegroundColor Red
    }
}

function Add-SkippedStep {
    param([string]$Name, [string]$Reason)
    Write-StepHeader $Name
    Write-Host "已跳过：$Reason" -ForegroundColor Yellow
    $script:Results.Add([PSCustomObject]@{
        Step    = $Name
        Result  = "SKIP"
        Seconds = 0
        Detail  = $Reason
    })
}

# 跑一个原生可执行文件并按退出码判定通过/失败（PowerShell 5.1 对原生命令非零退出码不会抛出
# 终止性异常，需要手动读 $LASTEXITCODE；命令本身找不到会抛异常，由 Invoke-CheckStep 的 catch
# 接住）。
#
# 判断记录（持续集成修复：本函数内部临时把 $ErrorActionPreference 降级为 Continue）：
# 本脚本顶部把 $ErrorActionPreference 设成了 "Stop"（脚本作用域）。PowerShell 对"原生命令
# 写到 stderr 的每一行"有一条广为人知但违反直觉的行为——在 $ErrorActionPreference = "Stop"
# 下，只要原生命令往 stderr 写了任何一行东西（不管进程退出码是不是 0，例如某些工具的
# warning、或本例的 Python 未捕获异常 traceback），PowerShell 会把这一行提升成终止性的
# NativeCommandError 异常，当场中断 `& $Exe @ArgList` 这一句，只把"第一行" stderr 文本当成
# 异常消息抛出——后续 stderr 行（往往才是真正有诊断价值的部分，例如 Python traceback 的
# 具体报错类型与代码行）永远读不到。此前 `python toolchain/gen_event_constants.py --check`
# 在 CI 运行器上因控制台编码问题崩溃时，Invoke-CheckStep 汇总表 Detail 列里只剩一句
# "Traceback (most recent call last):" 的根因正在这里。
#
# 把 $ErrorActionPreference 赋值为函数局部变量（不加 $script:/$global: 前缀，PowerShell
# 变量赋值默认只在当前作用域生效，函数返回后自动失效，不影响脚本其余部分与调用方），让本函数
# 内的原生命令调用把 stderr 只当成普通输出流，不提升为异常；退出码判定逻辑完全不变，仍然只认
# $LASTEXITCODE。
#
# TOOL-01 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：本节上一版注释
# 曾声称"找不到可执行文件这类'启动失败'仍然会正常抛异常，由 Invoke-CheckStep 的 catch 接住"——
# 这句话是错的，且已被审计以有界复现证伪（见该报告 repro/tool-01-repro.ps1/.txt）：`& $Exe` 在
# 本函数已经把 $ErrorActionPreference 设为 "Continue" 的作用域内执行，PowerShell 找不到命令时
# 产生的是 non-terminating 错误——在 Continue 策略下只会打一条错误记录然后继续往下执行，不会
# 抛出终止性异常，因此根本不会被 Invoke-CheckStep 的 try/catch 接住；同时 `& $Exe` 从未真正
# 启动进程，$LASTEXITCODE 会原样保留上一条命令遗留的值（很可能恰好是 0），导致
# `return ($LASTEXITCODE -eq 0)` 把"根本没跑起来的检查"误判为 PASS。改法见下方函数体：调用前
# 先用 Get-Command 显式校验可执行文件存在，找不到就直接判 FAIL 并说明原因，不再依赖
# $LASTEXITCODE 的偶然残留值。
# F1 根治（architecture/落地计划/audit-20260907/delivery-validation.md）：此前 `& $Exe @ArgList`
# 直接执行，原生命令写到标准输出的每一行都会作为本函数自己的管道输出（PowerShell 函数没有显式
# `return` 拦截之前的语句同样会被收集进调用方拿到的结果）；调用方 Invoke-CheckStep 用
# `$result = & $Action` 捕获整个 scriptblock 的输出，一旦 scriptblock 是 `{ Test-NativeExitCode ... }`
# 这种形状，`$result` 就会变成"原生 stdout 的每一行字符串 + 末尾一个 [bool]"拼成的
# `System.Object[]`；`[bool]$result` 对非空数组恒为 `$true`（PowerShell 只看数组是否非空，不看
# 元素内容），导致"有任何一行 stdout 输出、同时退出码非 0"的失败命令被判为 PASS——已实测复现
# （见 delivery-validation.md F1、repro-check-native-exit.ps1）。
#
# 改法：把原生命令的输出通过管道显式送进 `Out-Host`——`Out-Host` 直接写到宿主（用户仍能在控制台/
# transcript 里看到完整的 dotnet/python/pytest 输出，`-LogFile` 的 transcript 同样会录到），
# 不进入 PowerShell 的成功输出流，因此不会被 `& $Action`/`$result = ...` 捕获到。本函数自身此后
# 只有末尾一条 `return` 语句产生输出，`Invoke-CheckStep` 拿到的 `$result` 保证是一个干净的
# `[bool]`，不需要调用方做任何特殊处理。
function Test-NativeExitCode {
    param(
        [string]$Exe,
        [string[]]$ArgList
    )
    # TOOL-01 根治：调用前先显式确认 $Exe 能被解析为一个真实的可执行文件/外部脚本；找不到就
    # 直接判 FAIL，不再尝试 `& $Exe`（那样会因 non-terminating 错误 + 陈旧 $LASTEXITCODE
    # 被误判为 PASS，见上方判断记录）。
    $resolved = Get-Command -Name $Exe -CommandType Application, ExternalScript -ErrorAction SilentlyContinue
    if (-not $resolved) {
        Write-Host "[Test-NativeExitCode] 可执行文件 '$Exe' 未找到（不在 PATH 中，或路径不存在），判定为 FAIL" -ForegroundColor Red
        return $false
    }
    $ErrorActionPreference = "Continue"
    & $Exe @ArgList | Out-Host
    return ($LASTEXITCODE -eq 0)
}

# H5 新增：跑一个"GUI 子系统"原生可执行文件（Unity.exe / 独立版 Shell.exe）并真正等待其退出、
# 拿到真实退出码。
#
# 判断记录（根因 + 为什么不能继续用 `& $Exe @ArgList` + $LASTEXITCODE）：Unity.exe 与独立版
# Shell.exe 都是 Windows GUI 子系统程序（不是控制台子系统程序）。PowerShell 的调用运算符 `&`
# 对 GUI 子系统程序只负责"启动"，不会阻塞等待其退出（这是 Windows 进程创建层面的行为，不是
# PowerShell 的 bug）——`& $Exe @ArgList` 这一行执行完就立即往下走，此时 Unity 可能才刚开始加载
# 许可证/域重载，$LASTEXITCODE 读到的是上一条别的原生命令留下的陈旧值。H4 版 check.ps1 的 Unity
# 四步之所以"0 秒内结束、编译步误判 PASS、其余步骤 FAIL"，根因正在这里：Unity 编译检查那一步
# 的 `& Unity.exe -quit` 一启动就立即返回（判定用的 $LASTEXITCODE 是陈旧值，凑巧还是 0），脚本
# 立即往下跑 EditMode/PlayMode/独立版构建，而这些新启动的 Unity 实例发现同一个工程已经被上一个
# （其实还在后台跑）Unity 实例打开，直接失败退出（EditMode/PlayMode 各自的 xml/log 里只有许可证
# 握手几行）。
#
# 改法：不用 `&`，改用 Start-Process -FilePath/-ArgumentList/-Wait/-PassThru/-NoNewWindow——
# -Wait 是 Start-Process 自己实现的"轮询进程句柄直到退出"，不依赖 GUI/控制台子系统的差异；
# -PassThru 拿到 Process 对象读真实 ExitCode（不再依赖 $LASTEXITCODE）。-ArgumentList 传数组
# （不是拼接成一整根字符串）：Start-Process 内部按数组元素分别加引号，含空格/中文的路径参数
# （如仓库路径、-logFile 输出路径）不需要调用方自己转义。
#
# $TimeoutSeconds > 0 时（独立版无人值守冒烟两步用到，见下方判断记录：无人值守冒烟一旦挂死，
# 没有人会去按任何键，必须有兜底）：不能再用 -Wait（-Wait 本身不接受超时参数），改成不传 -Wait
# 只传 -PassThru 拿到 Process 对象后，手工调用 Process.WaitForExit(毫秒) 限时等待；超时后
# WaitForExit 返回 $false，尝试 Kill 掉这个挂死的子进程（仅 Kill 本函数自己刚刚 Start-Process
# 启动的这一个句柄，不误杀任何其它进程），$TimedOut 置 $true、ExitCode 记为 -1（明确的失败标记，
# 不会与任何真实退出码混淆——真实 Windows 退出码不会是负数）。
#
# 判断记录（限时分支不传 -NoNewWindow，与"不限时"分支不同——已实测复现的 Windows PowerShell 5.1
# Start-Process 已知限制）：本函数最初两个分支都传 -NoNewWindow，实测（独立版冒烟两步）发现限时
# 分支下 `$proc.ExitCode` 读回空字符串——进程确实已退出（HasExited 为真、日志也证实 Shell.exe
# 内部 Application.Quit(0) 正常收尾），但 Start-Process 在"-PassThru 且不传 -Wait 却传了
# -NoNewWindow"这一种组合下返回的 Process 对象丢失了退出码句柄（去掉 -NoNewWindow 后同样的
# 手工 WaitForExit 流程能正确读到 ExitCode，实测反复复现两次，确认是这一个参数组合触发，不是
# 偶发）。"不限时"分支（-Wait -PassThru -NoNewWindow 三个一起传）当时不受影响——Start-Process
# 自己实现的 -Wait 走的是另一条内部代码路径，能正确保留退出码，这也曾是任务书要求的默认写法，
# 彼时原样保留。限时分支因此改为只传 -PassThru：本来就是 GUI 子系统程序（Unity/独立版 Shell.exe
# 本身不分配控制台窗口），-batchmode 命令行参数已经保证不出现可见窗口，去掉 -NoNewWindow 不影响
# 实际的"无人值守"效果，只是绕开这一条 Start-Process 自身的退出码丢失路径。
#
# 判断记录（2026-09-06 实测：不限时分支不再用 -Wait——Unity 退出后仍空等约 10 分钟的根因）：
# 上面"不限时分支保留 -Wait"的写法后来被证明还有第二个坑，与 ExitCode 丢失无关，而是"等太久"。
# Unity 6000.3.23f1 在批处理模式编译脚本时，会另外启动一个 Roslyn 编译器服务进程作为 Unity.exe
# 的子进程（命令行形如 `...\NetCoreRuntime\dotnet.exe exec ...\DotNetSdkRoslyn\VBCSCompiler.dll
# -pipename:...`），用于跨次编译复用、加速后续启动；这个子进程在 Unity.exe 本体退出、日志已经
# 写下"Exiting batchmode successfully now!"之后仍然存活，默认空闲保活时间约 600 秒才会自行退出。
# 而 Windows PowerShell 5.1 的 `Start-Process -Wait` 等待的不只是目标进程本身，而是"进程树"——
# 只要还有它派生出的子进程（含孙进程）活着就不返回。结果是 Unity 四步（编译检查/EditMode/
# PlayMode/独立版构建）以及 toolchain/consumer_smoke.ps1 里另外 4 处调用 Unity 的地方，每一步
# 都可能在 Unity 本体早已退出之后，被这个残留的 VBCSCompiler 子进程额外拖住最多约 10 分钟。
# 改法：不限时分支与限时分支统一改成只传 -PassThru（不传 -Wait、不传 -NoNewWindow），退出等待
# 改由手工调用 .NET 的 `Process.WaitForExit()`（无超时参数的重载）——它只轮询 Unity.exe 自己的
# 进程句柄，不关心其子孙进程是否还活着，VBCSCompiler 继续在后台跑不影响本函数返回；返回前统一
# `$proc.Refresh()` 后再读 `$proc.ExitCode`，与限时分支保持一致的读取方式。-NoNewWindow 两个
# 分支都不再传：一是上面已实测的 PS 5.1 限制（-PassThru 不配 -Wait 时若再传 -NoNewWindow，
# ExitCode 读回空字符串）；二是 Unity.exe / 独立版 Shell.exe 本身是 GUI 子系统程序且总带
# -batchmode 参数，不会弹出可见窗口，去掉 -NoNewWindow 不影响"无人值守"效果。
# 验证证据（2026-09-06）：新代码门禁实跑（check.ps1 -SkipConsumer -SkipSmoke）四步，脚本记录的
# Seconds 与对应 Unity 日志 CreationTime→LastWriteTime 之差逐步比对：编译检查 30.8s/30.3s、
# EditMode 9.4s/9.1s、PlayMode 27.8s/27.4s、独立版构建 29.2s/28.9s——每步相差不到 1 秒；且在
# 函数返回的瞬间确认编译服务进程（VBCSCompiler/dotnet.exe）仍然存活，证明它不再阻塞函数返回。
# 旧代码同日在另一份仍跑 -Wait 版本的检出上实测：compile.log 由 Unity 写在 18:29:35→18:29:53
# （Unity 本体 18 秒完事），但下一步 editmode.log 直到 18:48:08 才出现——脚本在两步之间空等约
# 18 分钟，恰好是 18:29:37 随该 Unity.exe 派生、父进程已退出的孤儿 VBCSCompiler 的存活时长
# （只要还有其他 Unity 运行复用它就不退出，最后一次被用后约 600 秒自行退出）。同一次运行里，
# 独立版构建这一步 Unity 于 18:49:03 退出前又在 18:48:50 新起一个 VBCSCompiler，脚本再次被拖住。
# 补充说明：旧代码约 600 秒的空等只在 Unity 需要自己新起 VBCSCompiler 子进程时出现（启动时没有
# 存活的服务可连，例如闲置超过 10 分钟后的第一次 Unity 运行，是 check.ps1 全新一轮的常见情形）；
# 若已有更早 Unity 实例留下的服务还活着，Unity 只是通过命名管道连接它、并非 Start-Process 的子
# 进程，旧代码 -Wait 同样能及时返回（实测过一次，只多等 1.4 秒）。新代码两种情况下都不受影响，
# 因为它只等待 Unity 自己的进程句柄。
function Invoke-NativeAndWait {
    param(
        [string]$Exe,
        [string[]]$ArgList,
        [int]$TimeoutSeconds = 0
    )

    if ($TimeoutSeconds -le 0) {
        $proc = Start-Process -FilePath $Exe -ArgumentList $ArgList -PassThru
        $proc.WaitForExit()
        $proc.Refresh()
        return [PSCustomObject]@{ ExitCode = $proc.ExitCode; TimedOut = $false }
    }

    $proc = Start-Process -FilePath $Exe -ArgumentList $ArgList -PassThru
    $exited = $proc.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) {
        try { $proc.Kill() } catch { }
        return [PSCustomObject]@{ ExitCode = -1; TimedOut = $true }
    }
    $proc.Refresh()
    return [PSCustomObject]@{ ExitCode = $proc.ExitCode; TimedOut = $false }
}

# H5 新增：Unity 四步开跑前检查"同一工程"是否已经有一个残留的 Unity.exe 进程在跑（例如上一次
# check.ps1 被中断、或另一个终端窗口手工开着 Unity Editor 独占了同一个工程的 .lock）——按命令行
# 里是否含本工程路径判断，不是"任意 Unity.exe 都算残留"（开发机上完全可能同时开着别的工程的
# Unity Editor）。发现残留只报告明确的失败信息（含 PID，方便手工定位/结束），不代为 Kill——
# 任务书硬性规则"不 kill 非本脚本启动的进程"，残留进程可能是人正在交互使用的 Editor 窗口。
function Test-NoResidualUnityProcess {
    param([string]$ProjectPath)

    try {
        $procs = Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop
    } catch {
        # 查询进程列表本身失败（权限/WMI 服务异常等）：不能确认"没有残留"，但也不应该让整个门禁
        # 因为一次诊断性查询失败而跳过 Unity 步骤——按"未发现残留"处理，继续往下跑。
        return
    }

    $needle = $ProjectPath.TrimEnd("\", "/")
    $hit = $procs | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($needle) }
    if ($hit) {
        $pids = ($hit | ForEach-Object { $_.ProcessId }) -join ", "
        throw "检测到同一工程已有残留 Unity.exe 进程在运行（PID: $pids，工程路径 $ProjectPath），本脚本不会代为结束——请先手工关闭该 Unity Editor 窗口/进程，确认 $ProjectPath\Temp\UnityLockfile 已释放后重跑。"
    }
}

# -----------------------------------------------------------------------------
# 0. 门禁自检（F1 根治回归，architecture/落地计划/audit-20260907/delivery-validation.md）：
#    Test-NativeExitCode 此前会把原生命令的 stdout 泄漏进 Invoke-CheckStep 的结果判定，导致
#    "有输出且退出码非零"的失败命令被误判为 PASS（复现细节见两函数上方判断记录）。本步骤在
#    全部真正的检查步骤之前，用两个独立探针（失败/成功各一次，均带 stdout 输出）验证判定逻辑
#    本身是可信的——如果这一步本身失败，说明门禁基础设施有问题，后续全部步骤的 PASS/FAIL 都
#    不可信，理应第一个报告。
# -----------------------------------------------------------------------------
Invoke-CheckStep "门禁自检：Test-NativeExitCode 对失败/成功原生命令正确判定" {
    # 探针 1：F1 复现的确切形状——打印一行 stdout，随后以非零退出码结束。修复前会被误判为成功。
    $failProbeOk = Test-NativeExitCode "powershell.exe" @("-NoProfile", "-Command", "Write-Output 'F1_SELF_CHECK_PROBE'; exit 7")
    if ($failProbeOk) {
        return [PSCustomObject]@{ Ok = $false; Detail = "失败探针（stdout 非空 + exit 7）被误判为成功——Test-NativeExitCode 回归，见该函数判断记录" }
    }

    # 探针 2：同样有 stdout 输出，但正常以 0 退出——必须仍判定为成功，证明探针 1 的修复没有
    # 反过来误伤真正成功、只是恰好有输出的命令（dotnet/python 几乎每次都会打印一些内容）。
    $passProbeOk = Test-NativeExitCode "powershell.exe" @("-NoProfile", "-Command", "Write-Output 'F1_SELF_CHECK_PROBE'; exit 0")
    if (-not $passProbeOk) {
        return [PSCustomObject]@{ Ok = $false; Detail = "成功探针（stdout 非空 + exit 0）被误判为失败" }
    }

    # 探针 3（TOOL-01 复现的确切形状）：先跑一次真实成功命令把 $LASTEXITCODE 钉在 0，
    # 再调用一个必定不存在的可执行文件名。修复前 `& $Exe` 对不存在的命令只产生
    # non-terminating 错误、不抛异常，$LASTEXITCODE 原样保留上一条命令留下的 0，
    # 会被误判为 PASS；修复后必须在调用前就用 Get-Command 判定失败。
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
# 1. dotnet build
# -----------------------------------------------------------------------------
Invoke-CheckStep "dotnet build Core.sln -c $Configuration" {
    Test-NativeExitCode "dotnet" @("build", $SolutionPath, "-c", $Configuration, "--artifacts-path", $ArtifactsPath)
}

# -----------------------------------------------------------------------------
# 2. dotnet test（六工程；未加任何 --filter，默认含 Category=Perf 的性能基线测试）
# -----------------------------------------------------------------------------
Invoke-CheckStep "dotnet test Core.sln -c $Configuration --no-build（六工程，含 Perf 类别）" {
    Test-NativeExitCode "dotnet" @("test", $SolutionPath, "-c", $Configuration, "--no-build", "--artifacts-path", $ArtifactsPath)
}

# -----------------------------------------------------------------------------
# 2b. ABI 探针（toolchain/abi_probe.ps1，第十六方深度审核跟进，见 architecture/11_工程规范与测试.md
#     第 7 节"发布说明不得宣称未经验证的二进制兼容"）：旧编译 consumer（针对
#     toolchain/abi_probe_baseline.txt 记录的基线版本）换上本次步骤 1 刚构建出的正式 DLL、不重新
#     编译，验证是否仍能正常运行；再加一道通用公开 API 表面差异比对（toolchain/abi_surface）——
#     两者任一不满足都说明存在未声明的破坏性变更（1.13.0 的真实教训，见 CHANGELOG.md"已知问题：
#     ABI/API 兼容性"）。-Quick 跳过：需要额外解压/编译独立 consumer 与 abi_surface 两个工程，
#     不是秒级步骤。另起一个 powershell 子进程跑（脚本内部用 exit 语句表达结果，惯例同下方"消费方
#     演练"步骤，避免子脚本的 exit 连带终止本脚本）。
#
#     PJ114-02 根治（外部审计 audit-76d16a5-20260910）：此前本机没有基线版本 dist zip
#     （.gitignore 排除的本机构建缓存）时子脚本打印警告并以退出码 0（PASS）收尾，这里又把子进程
#     stdout `| Out-Null` 丢弃——完整 transcript 里这一行永远显示 PASS，却从没有真的跑过一次
#     consumer，不是 ABI 验收证据。新语义：子脚本退出码 3 = 基线缺失，本步骤记为可见 SKIP（通过
#     Invoke-CheckStep 的 Skip 结果形状，见该函数头判断记录）；0 = PASS；其余非零 = FAIL。子进程
#     stdout/stderr 不再吃掉，改用 `*>` 落盘到 `$ArtifactsPath\abi_probe.log`，失败时回显完整内容
#     方便定位（PASS/SKIP 时只保留日志文件，不刷屏）——`*>` 重定向发生在这条命令自己的语句里，不
#     进入 `& $Action` 的返回值管道，同样不会污染 Invoke-CheckStep 的返回值判定。`-AbiStrict` 透传
#     给子脚本的 `-SkipIfBaselineMissing`（取反），发布模式下基线缺失直接判 FAIL（子脚本退出码 1）。
#
#     判断记录（改用 `-Command "& ... -SkipIfBaselineMissing:$literal"`，不用 `-File` + 参数数组）：
#     实测本机 Windows PowerShell 5.1 下，`-File` 调用子进程时给一个非 `[switch]` 的 `[bool]`
#     类型形参传值（不论是单独一个数组元素 `$true`/`$false`，还是 `"-Name:$true"`/`"-Name True"`
#     这类字符串形式），参数绑定器一律报
#     `Cannot process argument transformation ... Cannot convert value "System.String" to type
#     "System.Boolean"`——`-File` 把随后每个 token 都当成原始字符串塞进子进程的 argv，其自动类型转换
#     在这条路径上不生效（哪怕错误消息本身声称"接受 1/0"）；`[switch]` 类型的显式 `:$false` 语法同样
#     复现这个问题（本仓库其余 `& powershell @xxxArgs -File ...` 调用点都没有传过需要显式取值的
#     布尔/开关参数，此前未暴露）。改成 `-Command "& '<script>' ... -SkipIfBaselineMissing:$true"`
#     这种形式后，`$true`/`$false` 是被子进程自己的 PowerShell 解析器当场解析成的原生布尔字面量
#     （同一路径下人工在交互式提示符里直接敲 `.\abi_probe.ps1 -SkipIfBaselineMissing:$false` 一样
#     正常工作，问题只出在"数组化参数 + -File"这一种调用形状），实测两种取值都能正确送达子脚本。
# -----------------------------------------------------------------------------
if ($Quick) {
    Add-SkippedStep "ABI 探针（toolchain/abi_probe.ps1）" "-Quick"
} else {
    Invoke-CheckStep "ABI 探针（toolchain/abi_probe.ps1）" {
        $abiProbeScript = Join-Path $RepoRoot "toolchain\abi_probe.ps1"
        if (-not (Test-Path -LiteralPath $ArtifactsPath)) {
            New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
        }
        $abiProbeLog = Join-Path $ArtifactsPath "abi_probe.log"
        $skipMissingLiteral = if ($AbiStrict) { '$false' } else { '$true' }
        $quotedScript = "'" + $abiProbeScript.Replace("'", "''") + "'"
        $quotedArtifacts = "'" + $ArtifactsPath.Replace("'", "''") + "'"
        $quotedConfiguration = "'" + $Configuration.Replace("'", "''") + "'"
        # 判断记录（命令末尾追加 `; exit $LASTEXITCODE`）：实测 `-Command "& '<script>' ..."` 这条
        # 调用形状下，被调用脚本内部的 `exit N` 不会原样成为宿主 powershell.exe 进程自身的退出码
        # （`-File` 才会）——`-Command` 下子脚本 `exit 3` 之后，宿主进程自己却报 `$LASTEXITCODE=1`。
        # 显式在同一条 `-Command` 文本末尾追加 `exit $LASTEXITCODE`，让宿主进程的退出码等于调用
        # 子脚本后 `$LASTEXITCODE` 的当前值（子脚本 `exit N` 会先设置这个变量），实测能正确得到
        # 0/1/3 三种预期退出码。
        $abiProbeCommand = "& $quotedScript -ArtifactsPath $quotedArtifacts -Configuration $quotedConfiguration -SkipIfBaselineMissing:$skipMissingLiteral; exit `$LASTEXITCODE"
        & powershell -NoProfile -ExecutionPolicy Bypass -Command $abiProbeCommand *> $abiProbeLog
        $abiExit = $LASTEXITCODE

        if ($abiExit -eq 3) {
            return [PSCustomObject]@{ Skip = $true; Reason = "基线发行包不存在（详见 $abiProbeLog）" }
        }
        if ($abiExit -ne 0) {
            Get-Content -LiteralPath $abiProbeLog | Write-Host
            return [PSCustomObject]@{ Ok = $false; Detail = "abi_probe.ps1 退出码=$abiExit，详见 $abiProbeLog" }
        }
        return $true
    }
}

# -----------------------------------------------------------------------------
# 3. 数据校验（合并根：data/_framework + data/_sample）
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py（合并根）" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/validate_data.py")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 3b. 框架根单独完整校验（加固J3：core/carriers/item 的预算超标规则改为"item.template 一行
#     都没有时跳过"后，data/_framework 单独跑完整两道校验不再需要 --skip-dotnet 规避，见
#     data/README.md"与校验器的关系"一节判断记录）。
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
# 3c. 元数据门禁：validator --schema-audit（ADR-0018 决策 3/ADR-0019 决策 4，F3 新增）——审计全部
#     已登记 TableSchema/FieldSchema 的结构声明本身（不加载任何数据，秒级），-Quick 下也跑（见
#     Presentation.Assembly.SchemaAudit 类型注释）。白名单固定读仓库根
#     toolchain/schema_audit_allowlist.json。
# -----------------------------------------------------------------------------
Invoke-CheckStep "元数据门禁：validator --schema-audit（ADR-0018 决策 3/ADR-0019 决策 4）" {
    Push-Location $RepoRoot
    try {
        # 惯例同 toolchain/validate_data.py 调用 toolchain/validator 的写法（dotnet run --project
        # <path> -- <args>，不显式传 -c/--artifacts-path——首次运行自动编译，用 dotnet 默认输出
        # 目录，不与本脚本步骤 1/2 的 --artifacts-path 构建产物混淆）。
        Test-NativeExitCode "dotnet" @("run", "--project", "toolchain/validator", "--", "--schema-audit", "--allowlist", "toolchain/schema_audit_allowlist.json")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 4. 事件常量生成器一致性检查
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
# 5. 占位资产生成器一致性检查（-Quick 跳过：需要 Pillow 且逐张比较占位图，不是秒级步骤）
# -----------------------------------------------------------------------------
if ($Quick) {
    Add-SkippedStep "python toolchain/gen_placeholder_assets.py --check" "-Quick"
} else {
    Invoke-CheckStep "python toolchain/gen_placeholder_assets.py --check" {
        Push-Location $RepoRoot
        try {
            Test-NativeExitCode "python" @("toolchain/gen_placeholder_assets.py", "--check")
        } finally {
            Pop-Location
        }
    }
}

# -----------------------------------------------------------------------------
# 5b. 资产导入工具交叉校验（import_assets.py check，见 11 第 8 节"新增资产已经过导入工具并
#     通过资产校验"）：全量交叉校验（sprite/vfx/sfx/world 四域）——data/_sample 的
#     display/vfx/sfx/world 四张表引用的资产均已经由 toolchain/import_sample_assets.py 驱动
#     import_assets.py 的 sprite/icon/vfx/sfx/map 子命令导入到 assets/_sample/（见
#     toolchain/README.md"data/_sample 的资产来源（import_sample_assets.py）"一节），四域在
#     当前数据集下应始终通过，不属于 -Quick 可跳过的慢步骤（不读图片，只比对文件是否存在）。
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
# 6. toolchain 自身的 pytest 套件（-Quick 跳过：见 .PARAMETER Quick 说明）
# -----------------------------------------------------------------------------
if ($Quick) {
    Add-SkippedStep "python -m pytest toolchain/tests -q" "-Quick"
} else {
    Invoke-CheckStep "python -m pytest toolchain/tests -q" {
        # 判断记录（第九轮审计工具链条目，本机 GBK 控制台下 toolchain/tests 里跑
        # subprocess.run(...).stdout/.stderr 解码用的 locale.getpreferredencoding() 是 cp936，
        # 已在 toolchain/tests/test_get_framework_path_boundary.py 改为显式
        # encoding="utf-8", errors="replace" 根治那一处不再依赖控制台代码页；这里额外给 pytest
        # 本身的子进程环境同步设置 PYTHONUTF8=1，与 .github/workflows/ci.yml 的作业级 PYTHONUTF8/
        # PYTHONIOENCODING 兜底、.githooks/pre-commit 的同名设置保持一致（双保险：即便未来哪个
        # toolchain 测试新增了不带 encoding 的子进程调用，也不至于在本机默认编码下直接报错）。
        # 用完恢复原值，不污染 check.ps1 调用方后续步骤的环境。
        $prevPythonUtf8 = $env:PYTHONUTF8
        $env:PYTHONUTF8 = "1"
        Push-Location $RepoRoot
        try {
            Test-NativeExitCode "python" @("-m", "pytest", "toolchain/tests", "-q")
        } finally {
            Pop-Location
            $env:PYTHONUTF8 = $prevPythonUtf8
        }
    }
}

# -----------------------------------------------------------------------------
# 7. 禁用词扫描
#    a) 全仓库不得出现某个具体游戏代号（见 CLAUDE.md 硬性规则；本文件下面用字符串拼接构造该词、
#       不直接拼出完整拼写，避免本脚本自身的源码触发这一步扫描），排除 .git/bin/obj/Library/
#       StreamingAssets/dist 几个构建期/缓存目录，并排除本脚本自身（同一原因）。
#    b) architecture 正文（00~14 号文档 + adr/）不得出现具体引擎/语言/框架/工具名，
#       immunity 例外（含 unity 子串但不是该词本身）。
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

Invoke-CheckStep "禁用词扫描：全仓库不出现具体游戏代号" {
    # 见上方注释：字符串拼接构造被扫描词，避免脚本自身源码里出现完整拼写。
    $bannedCodename = "note" + "moss"
    $files = Get-ScannableFiles -Root $RepoRoot -ExtraExcludeFullNames @($PSCommandPath)
    $hits = @()
    foreach ($f in $files) {
        try {
            $m = Select-String -Path $f.FullName -Pattern $bannedCodename -SimpleMatch -CaseSensitive:$false -ErrorAction SilentlyContinue
            if ($m) { $hits += $m }
        } catch {
            # 二进制文件等读取失败直接跳过，不计入命中
        }
    }
    if ($hits.Count -gt 0) {
        $lines = $hits | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
        throw "发现 $($hits.Count) 处具体游戏代号命中：`n$($lines -join "`n")"
    }
    $true
}

Invoke-CheckStep "禁用词扫描：architecture 正文不出现引擎/语言/框架/工具名（immunity 例外）" {
    $targets = @()
    $targets += Get-ChildItem -Path (Join-Path $RepoRoot "architecture") -Filter "0*.md" -File -ErrorAction SilentlyContinue
    $targets += Get-ChildItem -Path (Join-Path $RepoRoot "architecture") -Filter "1*.md" -File -ErrorAction SilentlyContinue
    $adrDir = Join-Path $RepoRoot "architecture\adr"
    if (Test-Path $adrDir) {
        $targets += Get-ChildItem -Path $adrDir -Filter "*.md" -File -ErrorAction SilentlyContinue
    }

    # unity 单独用左右非字母边界匹配，避免命中 immunity；其余几个词本身不太会作为其它中文/英文
    # 词的子串出现，按普通子串匹配即可（与任务验收命令 grep -niwE 的整词语义等价）。
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
# 8. 版本一致性（版本可追溯任务新增，见 11_工程规范与测试.md 第 7 节"版本号必须可追溯到
#    对应的架构文档版本与数据 schema 版本组合"）：单一版本源仓库根 VERSION 文件必须与
#    adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json、
#    games/_template/package.json 两处 version 字段一致（含 games/_template 对适配层包的
#    依赖版本号），避免三处手改漏掉其中一处导致 dist 快照与源码割裂。只读比较，不改写任何文件。
#    版本管理方案新增：额外校验仓库根 CHANGELOG.md 含 VERSION 对应版本号的条目（形如
#    "## [X.Y.Z]"），或存在 "## [Unreleased]" 段——覆盖两种合法状态：已发布版本（VERSION 与
#    CHANGELOG 条目一一对应）与开发中版本（VERSION 尚指向上一个已发布版本，变更累积在
#    [Unreleased] 段，等下一次 build.ps1 -Release 时归档），避免改了代码却忘了写变更记录。
#    写回遗漏根治（2026-09-07）新增：额外校验 adapters/unity/Packages/packages-lock.json 里
#    "com.gamefoundation.game-template" 条目下 dependencies."com.gamefoundation.adapter.unity"
#    这一镜像字段同样等于 VERSION——这个字段是 UPM 自动维护的，此前 build.ps1 -Release 写回没有
#    覆盖它，门禁跑 Unity 相关步骤时 UPM 会自己改写，导致发布提交完成后工作树仍不干净（1.0.0 首次
#    发布实测复现，见 CHANGELOG.md [1.0.0] 修复记录）；build.ps1 -Release 写回已同步覆盖，这里
#    补一道只读校验兜底。
# -----------------------------------------------------------------------------
Invoke-CheckStep "版本一致性：VERSION、两个 package.json、packages-lock.json 与 CHANGELOG.md" {
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

    # 判断记录：两个 package.json 是不带 BOM 的 UTF-8；Windows PowerShell 5.1 的 Get-Content
    # 在没有 BOM 时按系统 ANSI 代码页猜编码，读中文会乱码甚至让 ConvertFrom-Json 报错，
    # 必须显式 -Encoding UTF8（与 build.ps1 打包步骤同一判断记录）。
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

    # 写回遗漏根治（2026-09-07）新增：adapters/unity/Packages/packages-lock.json 里
    # "com.gamefoundation.game-template" 条目下 dependencies."com.gamefoundation.adapter.unity"
    # 是 games/_template/package.json 同名依赖版本号的镜像（UPM 读本地文件依赖时自动写入的锁定
    # 值）。此前 build.ps1 -Release 写回没有覆盖这个字段，门禁跑 Unity 相关步骤时 UPM 会自己把它
    # 改成当前版本号，导致发布提交完成后工作树仍不干净（1.0.0 首次发布实测复现，见 CHANGELOG.md
    # [1.0.0] 修复记录）；build.ps1 -Release 写回已同步覆盖这个字段（见该脚本 Set-
    # PackagesLockGameTemplateDependency 判断记录），这里补一道只读校验，不一致就 FAIL，与上面
    # 两个 package.json 的校验同一治理方式。只读比较，不改写文件——用正则文本读取（与 build.ps1
    # 写回同一保守做法，避免整体 JSON 解析/序列化打乱这份 UPM 生成文件的原始格式）。
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
# 9. build.ps1 -SkipTests（同步六个核心 DLL 到 Unity 适配层包 + 同步内容数据集）
#    另起一个 powershell 子进程跑，避免 build.ps1 内部的 exit 语句连带终止本脚本。
#    -Quick 跳过：这一步只有 Unity 相关步骤需要（同步 DLL/内容数据集给 Unity 工程用），
#    -Quick 本身不跑任何 Unity 步骤，跳过它不影响 -Quick 覆盖的 dotnet/python 校验结论。
# -----------------------------------------------------------------------------
if ($Quick) {
    Add-SkippedStep "build.ps1 -SkipTests（同步 DLL）" "-Quick"
} else {
    Invoke-CheckStep "build.ps1 -SkipTests（同步 DLL）" {
        $buildScript = Join-Path $RepoRoot "build.ps1"
        # 同源假阳性同一修法，判断记录见下方"消费方演练"步骤：原生调用未消费的 stdout 会混进
        # scriptblock 返回值把失败判成 PASS，用 `| Out-Null` 吃掉即可，$LASTEXITCODE 不受影响。
        & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SkipTests | Out-Null
        return ($LASTEXITCODE -eq 0)
    }
}

# -----------------------------------------------------------------------------
# 9.5 包清单一致性（私服交付通道新增，见 toolchain/registry/README.md、build.ps1 -Dist"私服交付
#     通道新增"说明）：跑一遍 `build.ps1 -SyncOnly -Dist auto`（不需要 Unity，只是文件同步 +
#     npm pack，复用同一份 -Quick 判断——见下方 if ($Quick) 分支，-Quick 下同样跳过），核对：
#       1) 组装出的四个包（ADR-0018 决策 3 新增 com.gamefoundation.adapter.headless）package.json
#          的 version 字段都等于 VERSION（跟"8. 版本一致性"校验的是两份提交进源码库的 package.json
#          不同，这里校验的是 build.ps1 打包逻辑本身有没有正确把解析出的版本号写进新组装的四个包，
#          属于"打包逻辑自检"而不是"源码一致性"）；
#       2) 对每个包目录跑 `npm pack --dry-run --json`，核对文件清单里不包含
#          __pycache__/bin/obj/storage（含 registry/ 相关的排除规则真的生效，见 build.ps1
#          Copy-DistDir 调用列表的 -ExcludeDirNames）。
#
#     判断记录（2026-09-07，CI f0389f8 失败，根治：本步骤挪到"9. build.ps1 -SkipTests"之后，
#     不再挪到之前）：本步骤依赖的 `build.ps1 -SyncOnly -Dist auto` 有一条硬性前置条件——
#     `-SyncOnly` 要求六个核心 DLL（见 build.ps1 `$CoreAssemblies` 列表的六个 `Dir`）已经存在于
#     各自工程的 `bin\$Configuration\netstandard2.1\` 下（build.ps1 里 `-SyncOnly` 分支不跑
#     `dotnet build`，只做同步；找不到源 DLL 时判断记录写得很直白："-SyncOnly 要求产物已存在，
#     请先不带 -SyncOnly 跑一次完整构建"，随即 `exit 1`）。而在本仓库现有的步骤顺序里，真正会把
#     这六个 DLL 构建到 `bin\` 下的是本脚本"1. dotnet build"（用 `--artifacts-path`，产物落在
#     `$ArtifactsPath` 而不是 `bin\` 下）与"9. build.ps1 -SkipTests"（内部跑不带
#     `--artifacts-path` 的 `dotnet build`，产物才会落在 `bin\Release\netstandard2.1\`，见
#     build.ps1"1. dotnet build"一节）。此前本步骤排在"8. 版本一致性"之后、"9. build.ps1
#     -SkipTests"之前（旧编号"8.5"），在本机能通过纯属侥幸——本机仓库历史上跑过多次不带
#     `-SyncOnly` 的 `build.ps1`，`bin\` 下留有陈旧但存在的 DLL；GitHub Actions 的
#     `windows-latest` 运行器每次都是全新 checkout，`bin\` 目录不存在，本步骤在 CI 上必然在
#     `-SyncOnly` 内部的 DLL 存在性检查处以退出码 1 失败（见 CI 运行 f0389f8，Detail 只有一句
#     "build.ps1 -SyncOnly -Dist auto 失败，退出码 1"，因为当时调用处用 `| Out-Null` 把
#     build.ps1 自己打印的"找不到构建产物：...""-SyncOnly 要求产物已存在..."两行诊断信息吞掉了，
#     见本步骤下方"不再吞输出"的判断记录）。根治方案二选一：a) 把本步骤挪到"9. build.ps1
#     -SkipTests"之后（依赖关系上"先有构建产物，再打包"，本步骤现在采用的方案）；b) 让本步骤自身
#     在检测到 DLL 缺失时自动改调 `build.ps1 -SkipTests -Dist auto`（不用 -SyncOnly）。选 a）
#     不选 b）：b) 会让本步骤内部再悄悄多做一遍"9. build.ps1 -SkipTests"同样的构建+同步工作，
#     `-Quick` 之外的正常全量门禁跑两次实质等价的构建同步、更慢且更难追踪是哪一次真正产生的
#     `bin\Plugins\Core\` 内容；a) 只是单纯调整步骤顺序（依赖方在依赖项之后跑，符合直觉），两个
#     步骤各自职责不变（9 管"构建产物落地"，9.5 管"打包清单是否正确"），不引入任何隐式的重复构建。
# -----------------------------------------------------------------------------
if ($Quick) {
    Add-SkippedStep "包清单一致性（四个 npm 包版本号 + npm pack --dry-run 排除规则）" "-Quick"
} else {
    Invoke-CheckStep "包清单一致性（四个 npm 包版本号 + npm pack --dry-run 排除规则）" {
        $versionPath = Join-Path $RepoRoot "VERSION"
        $version = (Get-Content -Path $versionPath -Raw).Trim()

        $buildScript = Join-Path $RepoRoot "build.ps1"
        # 判断记录（不再用 `| Out-Null` 吞掉 build.ps1 的输出）：此前失败时 Detail 列只有一句
        # "build.ps1 -SyncOnly -Dist auto 失败，退出码 N"，看不到 build.ps1 自己打印的具体原因
        # （例如"找不到构建产物：..."这一行）——CI 上 f0389f8 那次失败就是因为这一行被吞掉，
        # 排查时只能凭猜测。改法：局部把 $ErrorActionPreference 降级为 Continue（原因同
        # Test-NativeExitCode 函数判断记录：`&` 调用外部 powershell.exe 时，Stop 偏好会把它写到
        # stderr 的任意一行提升成终止性异常，只保留第一行），把 stdout/stderr 逐行同时
        # Write-Host（控制台/-LogFile transcript 仍能实时看到完整输出，行为与之前一致）和收集进
        # 列表；失败时把收集到的最后 30 行并入 throw 的消息，让汇总表 Detail 列也能看到根因，不需要
        # 额外翻 -LogFile。这里对该 scriptblock 的局部赋值不影响脚本其余部分（`&` 调用操作符本身
        # 创建新作用域）。
        $ErrorActionPreference = "Continue"
        $buildOutputLines = New-Object System.Collections.Generic.List[string]
        & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SyncOnly -Dist auto 2>&1 | ForEach-Object {
            $line = $_.ToString()
            Write-Host $line
            $buildOutputLines.Add($line)
        }
        if ($LASTEXITCODE -ne 0) {
            $tailLines = $buildOutputLines | Select-Object -Last 30
            throw ("build.ps1 -SyncOnly -Dist auto 失败，退出码 $LASTEXITCODE。最后 " + $tailLines.Count + " 行输出：`n" + ($tailLines -join "`n"))
        }

        $packagesRoot = Join-Path $RepoRoot ("dist\" + $version + "\packages")
        # ADR-0018 决策 3 新增第四个包 com.gamefoundation.adapter.headless（无头适配层交付）；
        # 见 build.ps1 5.15 节、toolchain/registry/registry.json、toolchain/get_framework.ps1
        # $ThreePackageNames（zip/私服两条通道各自维护一份包名清单，四处一致性由
        # toolchain/tests/test_package_name_consistency.py 断言，见该测试文件判断记录）。
        $packageNames = @(
            "com.gamefoundation.adapter.unity",
            "com.gamefoundation.framework-data",
            "com.gamefoundation.toolchain",
            "com.gamefoundation.adapter.headless"
        )
        $forbiddenSegments = @("__pycache__", "bin", "obj", "storage")

        $problems = @()
        foreach ($pkgName in $packageNames) {
            $pkgDir = Join-Path $packagesRoot $pkgName
            $pkgJsonPath = Join-Path $pkgDir "package.json"
            if (-not (Test-Path $pkgJsonPath)) {
                $problems += "$pkgName：找不到 $pkgJsonPath"
                continue
            }
            $pkgObj = (Get-Content -Path $pkgJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
            if ($pkgObj.version -ne $version) {
                $problems += "$pkgName：package.json version=$($pkgObj.version) != VERSION=$version"
            }

            $dryRunJson = & npm pack $pkgDir --dry-run --json 2>$null
            if ($LASTEXITCODE -ne 0) {
                $problems += "$pkgName：npm pack --dry-run 失败（退出码 $LASTEXITCODE）"
                continue
            }
            $dryRunObj = ($dryRunJson -join "`n") | ConvertFrom-Json
            $fileEntries = $dryRunObj[0].files
            $hitSegments = New-Object System.Collections.Generic.HashSet[string]
            foreach ($entry in $fileEntries) {
                $entryPathSegments = $entry.path -split '[\\/]'
                foreach ($seg in $forbiddenSegments) {
                    if ($entryPathSegments -contains $seg) {
                        [void]$hitSegments.Add($seg)
                    }
                }
            }
            if ($hitSegments.Count -gt 0) {
                $problems += ("$pkgName：npm pack --dry-run 文件清单命中排除名单：" + (($hitSegments) -join ", "))
            }

            # PJ130-02 根治新增（审计 architecture/落地计划/audit-5c444f1-20260908/AUDIT_REPORT.md
            # PJ130-02，见 build.ps1 "5.055" 节判断记录）：com.gamefoundation.adapter.unity 包现在
            # 应该额外含 model/anim 占位资产（放进 Runtime/Resources/GameFoundation/，Unity 会自动
            # 导入的非 ~ 目录）与其生成器脚本（放进 Editor/）；这里核对 npm pack --dry-run 的文件
            # 清单里确实含这些路径，防止将来 Copy-DistDir 调用列表或本节新增的补齐逻辑被回退/漏改后
            # 又悄悄丢失这批资源却没有任何门禁步骤发现。
            if ($pkgName -eq "com.gamefoundation.adapter.unity") {
                $entryPaths = @($fileEntries | ForEach-Object { ($_.path -replace '\\', '/') })
                $requiredModelAssetSuffixes = @(
                    "Runtime/Resources/GameFoundation/models/placeholder_biped.prefab",
                    "Runtime/Resources/GameFoundation/models/placeholder_biped.controller",
                    "Runtime/Resources/GameFoundation/anim_clips/idle.anim",
                    "Runtime/Resources/GameFoundation/anim_clips/attack.anim",
                    "Runtime/Resources/GameFoundation/anim_clips/cast.anim",
                    "Runtime/Resources/GameFoundation/anim_clips/hit.anim",
                    "Editor/GeneratePlaceholderModelAssets.cs"
                )
                $missingModelAssets = @()
                foreach ($suffix in $requiredModelAssetSuffixes) {
                    $hit = @($entryPaths | Where-Object { $_ -like "*$suffix" })
                    if ($hit.Count -eq 0) {
                        $missingModelAssets += $suffix
                    }
                }
                if ($missingModelAssets.Count -gt 0) {
                    $problems += ("$pkgName：npm pack --dry-run 文件清单缺失 model/anim 占位资产或生成器（PJ130-02）：" + ($missingModelAssets -join ", "))
                }
            }
        }

        if ($problems.Count -gt 0) {
            throw ("包清单一致性校验失败：`n  " + ($problems -join "`n  "))
        }
        [PSCustomObject]@{ Ok = $true; Detail = "四个包 version=$version 一致，npm pack --dry-run 清单均不含排除项，adapter.unity 包含 model/anim 占位资产与生成器" }
    }
}

# -----------------------------------------------------------------------------
# Unity 相关四步（-SkipUnity 时整体跳过；见 adapters/unity/README.md"命令行跑测试"一节，
# 命令写法与该节保持一致：-runTests 不与 -quit 同传，PlayMode 不加 -nographics）。
# -----------------------------------------------------------------------------
function Resolve-UnityExe {
    param([string]$Explicit)
    if ($Explicit -ne "") {
        return $Explicit
    }
    $candidate = Join-Path $env:ProgramFiles "Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe"
    if (Test-Path $candidate) {
        return $candidate
    }
    # 找不到固定安装路径时退化为裸文件名，指望 PATH 上能解析到；解析不到会在调用处抛异常。
    return "Unity.exe"
}

if ($SkipUnity) {
    Add-SkippedStep "Unity 编译检查" "-SkipUnity"
    Add-SkippedStep "Unity EditMode 测试" "-SkipUnity"
    Add-SkippedStep "Unity PlayMode 测试" "-SkipUnity"
    Add-SkippedStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" "-SkipUnity"
    Add-SkippedStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" "-SkipUnity"
    Add-SkippedStep "消费方演练" "-SkipUnity"
} else {
    $resolvedUnityExe = Resolve-UnityExe -Explicit $UnityExe
    $unityProjectPath = Join-Path $RepoRoot "adapters\unity"

    # H5 起：Unity 四步一律用 Invoke-NativeAndWait（不再用 Test-NativeExitCode 那套
    # `& $Exe @ArgList` + $LASTEXITCODE——见该函数判断记录，对 GUI 子系统程序不阻塞，会导致本步骤
    # 在 Unity 真正跑完之前就误判结束）；每步开跑前先查一次同工程有没有残留 Unity.exe（见
    # Test-NoResidualUnityProcess）。

    Invoke-CheckStep "Unity 编译检查" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $log = Join-Path $UnityOutDir "compile.log"
        $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $unityProjectPath,
            "-logFile", $log
        )
        [PSCustomObject]@{
            Ok     = ($proc.ExitCode -eq 0)
            Detail = "Unity 退出码 $($proc.ExitCode)"
        }
    }

    Invoke-CheckStep "Unity EditMode 测试" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $resultsXml = Join-Path $UnityOutDir "editmode.xml"
        $log = Join-Path $UnityOutDir "editmode.log"
        $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode", "-nographics",
            "-projectPath", $unityProjectPath,
            "-runTests", "-testPlatform", "EditMode",
            "-testResults", $resultsXml,
            "-logFile", $log
        )
        if ($proc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)" }
        }
        if (-not (Test-Path $resultsXml)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
        }
        # NUnit 结果 XML：根节点 result 属性非 Passed 视为失败（覆盖 Unity 某些版本"有失败用例
        # 但进程退出码仍为 0"的已知情况，不能只信退出码）。
        [xml]$xml = Get-Content -Path $resultsXml -Raw
        $root = $xml.DocumentElement
        [PSCustomObject]@{
            Ok     = ($root.result -eq "Passed")
            Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)"
        }
    }

    Invoke-CheckStep "Unity PlayMode 测试" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $resultsXml = Join-Path $UnityOutDir "playmode.xml"
        $log = Join-Path $UnityOutDir "playmode.log"
        # PlayMode 不加 -nographics（见 adapters/unity/README.md 判断记录：需要真实渲染/输入子系统）。
        $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode",
            "-projectPath", $unityProjectPath,
            "-runTests", "-testPlatform", "PlayMode",
            "-testResults", $resultsXml,
            "-logFile", $log
        )
        if ($proc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)" }
        }
        if (-not (Test-Path $resultsXml)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
        }
        [xml]$xml = Get-Content -Path $resultsXml -Raw
        $root = $xml.DocumentElement
        # H5 新增：除根节点 result 外，把 total/passed/failed 计数写进汇总表 Detail 列。
        [PSCustomObject]@{
            Ok     = ($root.result -eq "Passed")
            Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)"
        }
    }

    Invoke-CheckStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $buildLog = Join-Path $UnityOutDir "build.log"
        $exePath = Join-Path $UnityOutDir "Shell.exe"
        $buildProc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $unityProjectPath,
            "-buildWindows64Player", $exePath,
            "-logFile", $buildLog
        )
        if ($buildProc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "Unity 构建退出码 $($buildProc.ExitCode)" }
        }
        if (-not (Test-Path $exePath)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成独立版产物：$exePath" }
        }

        if ($SkipSmoke) {
            Write-Host "已跳过 -gf-smoke 冒烟子步骤（-SkipSmoke），只验证了构建产物存在。" -ForegroundColor Yellow
            return [PSCustomObject]@{ Ok = $true; Detail = "已跳过 -gf-smoke（-SkipSmoke），只验证构建产物存在" }
        }

        # 注意不加 -nographics（见包 README"独立版无头冒烟"判断记录）。H5 新增：加 180s 超时保护
        # ——无人值守冒烟一旦挂死（例如场景资源加载死锁），没有人会去按任何键，Invoke-NativeAndWait
        # 的 -Wait 会无限期挂住整个门禁脚本，必须有兜底。
        $smokeLog = Join-Path $UnityOutDir "smoke_player.log"
        $smokeProc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 180 -ArgList @(
            "-batchmode", "-gf-smoke",
            "-logFile", $smokeLog,
            "-screen-width", "800", "-screen-height", "600"
        )
        if ($smokeProc.TimedOut) {
            return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
        }
        if ($smokeProc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
        }
        if (-not (Test-Path $smokeLog)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
        }
        $logText = Get-Content -Path $smokeLog -Raw
        [PSCustomObject]@{
            Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK")
            Detail = "见 $smokeLog"
        }
    }

    # H4 新增：离散链路冒烟（-gf-smoke-discrete，见 Adapter.Unity.Shell.SmokeRunner.RunDiscreteSequence
    # 判断记录）——同一份独立版构建产物（上一步已生成），另起一次进程跑离散分支，验证"进入战斗→
    # awaiting_input→结束回合→AI 行动→战斗结束"这条链路本身（-SkipSmoke 时同样跳过，只验证过
    # 构建产物存在这一步已经在上一步做过，本步不重复）。
    Invoke-CheckStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" {
        $exePath = Join-Path $UnityOutDir "Shell.exe"
        if (-not (Test-Path $exePath)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成独立版产物：$exePath" }
        }

        if ($SkipSmoke) {
            Write-Host "已跳过 -gf-smoke-discrete 冒烟子步骤（-SkipSmoke）。" -ForegroundColor Yellow
            return [PSCustomObject]@{ Ok = $true; Detail = "已跳过 -gf-smoke-discrete（-SkipSmoke）" }
        }

        # H5 新增：同上一步，180s 超时保护。
        $smokeLog = Join-Path $UnityOutDir "smoke_player_discrete.log"
        $smokeProc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 180 -ArgList @(
            "-batchmode", "-gf-smoke-discrete",
            "-logFile", $smokeLog,
            "-screen-width", "800", "-screen-height", "600"
        )
        if ($smokeProc.TimedOut) {
            return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke-discrete 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
        }
        if ($smokeProc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
        }
        if (-not (Test-Path $smokeLog)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
        }
        $logText = Get-Content -Path $smokeLog -Raw
        [PSCustomObject]@{
            Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK") -and ($logText -match "step=discrete_round ok")
            Detail = "见 $smokeLog"
        }
    }

    # -------------------------------------------------------------------
    # IL2CPP 发布路径验证（工程收尾 K 新增，-Il2cpp 显式开启才跑，默认跳过——耗时数分钟到十几
    # 分钟，见 adapters/unity/README.md"IL2CPP 发布路径验证"一节）：额外用 IL2CPP 脚本后端构建
    # 一份独立的独立版产物（与上面两步默认 Mono 后端的产物分开落地，互不覆盖），再跑一遍同样的
    # 两种无人值守冒烟，验证核心类库（含自写零依赖 JSON 读写器等原本就是为 AOT 场景设计、但此前
    # 从未在 IL2CPP 下实测过的代码）在真正的 AOT 编译（无反射兜底）下可运行，而不是只靠默认 Mono
    # 后端的构建自证。
    # -------------------------------------------------------------------
    if (-not $Il2cpp) {
        Add-SkippedStep "IL2CPP 独立版构建" "未传 -Il2cpp"
        Add-SkippedStep "IL2CPP 独立版 -gf-smoke 冒烟" "未传 -Il2cpp"
        Add-SkippedStep "IL2CPP 独立版 -gf-smoke-discrete 冒烟" "未传 -Il2cpp"
    } else {
        # 判断记录（2026-09-06 实跑暴露）：IL2CPP 独立版产物不能和上一步 Mono 独立版产物共用同一个
        # 输出目录——即使 .exe 文件名不同（Shell.exe vs Shell_il2cpp.exe，各自的 "<name>_Data"
        # 子目录名也因此不同），Unity 的 BuildPipeline 仍会报
        # "Build path contains a project previously built with the Mono2x scripting backend,
        # the current setting is for IL2CPP"——它按输出目录（而不是按 <name>_Data 子目录名）记录
        # 上一次构建这个目录用的脚本后端，同目录换后端会被直接拒绝。改法：IL2CPP 产物落在
        # $UnityOutDir 下一个独立子目录 il2cpp\，与 Mono 产物所在目录完全分开，不复用同一个输出
        # 目录。
        $il2cppOutDir = Join-Path $UnityOutDir "il2cpp"
        if (-not (Test-Path $il2cppOutDir)) {
            New-Item -ItemType Directory -Force -Path $il2cppOutDir | Out-Null
        }
        $il2cppExePath = Join-Path $il2cppOutDir "Shell_il2cpp.exe"

        Invoke-CheckStep "IL2CPP 独立版构建" {
            Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
            $buildLog = Join-Path $UnityOutDir "build_il2cpp.log"
            # 判断记录：输出路径经 GF_IL2CPP_OUTPUT_PATH 环境变量传给 Editor 方法（见
            # Il2CppPlayerBuilder.cs 头注释——命令行参数与环境变量二选一，这里选环境变量，避免
            # Start-Process -ArgumentList 数组里额外插入一对自定义参数与 Unity 自身参数混在一起
            # 不易辨认）；只在本次子进程调用的范围内设置，不污染 check.ps1 之外的环境。
            $prevEnv = $env:GF_IL2CPP_OUTPUT_PATH
            $env:GF_IL2CPP_OUTPUT_PATH = $il2cppExePath
            try {
                $buildProc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
                    "-batchmode", "-nographics", "-quit",
                    "-projectPath", $unityProjectPath,
                    "-executeMethod", "Adapter.Unity.EditorTools.Il2CppPlayerBuilder.BuildWindows64PlayerIl2cpp",
                    "-logFile", $buildLog
                )
            } finally {
                $env:GF_IL2CPP_OUTPUT_PATH = $prevEnv
            }
            if ($buildProc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "Unity 构建退出码 $($buildProc.ExitCode)，见 $buildLog" }
            }
            if (-not (Test-Path $il2cppExePath)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成 IL2CPP 独立版产物：$il2cppExePath" }
            }
            [PSCustomObject]@{ Ok = $true; Detail = "见 $buildLog" }
        }

        Invoke-CheckStep "IL2CPP 独立版 -gf-smoke 冒烟" {
            if (-not (Test-Path $il2cppExePath)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成 IL2CPP 独立版产物：$il2cppExePath" }
            }
            if ($SkipSmoke) {
                return [PSCustomObject]@{ Ok = $true; Detail = "已跳过（-SkipSmoke），只验证构建产物存在" }
            }
            $smokeLog = Join-Path $UnityOutDir "smoke_player_il2cpp.log"
            $smokeProc = Invoke-NativeAndWait -Exe $il2cppExePath -TimeoutSeconds 180 -ArgList @(
                "-batchmode", "-gf-smoke",
                "-logFile", $smokeLog,
                "-screen-width", "800", "-screen-height", "600"
            )
            if ($smokeProc.TimedOut) {
                return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
            }
            if ($smokeProc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
            }
            if (-not (Test-Path $smokeLog)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
            }
            $logText = Get-Content -Path $smokeLog -Raw
            [PSCustomObject]@{
                Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK")
                Detail = "见 $smokeLog"
            }
        }

        Invoke-CheckStep "IL2CPP 独立版 -gf-smoke-discrete 冒烟" {
            if (-not (Test-Path $il2cppExePath)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成 IL2CPP 独立版产物：$il2cppExePath" }
            }
            if ($SkipSmoke) {
                return [PSCustomObject]@{ Ok = $true; Detail = "已跳过（-SkipSmoke）" }
            }
            $smokeLog = Join-Path $UnityOutDir "smoke_player_il2cpp_discrete.log"
            $smokeProc = Invoke-NativeAndWait -Exe $il2cppExePath -TimeoutSeconds 180 -ArgList @(
                "-batchmode", "-gf-smoke-discrete",
                "-logFile", $smokeLog,
                "-screen-width", "800", "-screen-height", "600"
            )
            if ($smokeProc.TimedOut) {
                return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke-discrete 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
            }
            if ($smokeProc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
            }
            if (-not (Test-Path $smokeLog)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
            }
            $logText = Get-Content -Path $smokeLog -Raw
            [PSCustomObject]@{
                Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK") -and ($logText -match "step=discrete_round ok")
                Detail = "见 $smokeLog"
            }
        }
    }

    # -------------------------------------------------------------------
    # 消费方演练（第七项验收关卡的自动化形式，见 13_新游戏接入指南.md 第 7 节 /
    # toolchain/consumer_smoke.ps1 头注释）：默认跑，-SkipConsumer 跳过；另起一个 powershell 子
    # 进程跑，避免其内部的 exit 语句连带终止本脚本（惯例同 build.ps1 步骤）。耗时较长（内部至少
    # 四次 Unity 批处理调用），不计入本脚本自身的 Unity 编译检查/EditMode/PlayMode/独立版构建
    # 四步。
    # -------------------------------------------------------------------
    if ($SkipConsumer) {
        Add-SkippedStep "消费方演练" "-SkipConsumer"
    } else {
        Invoke-CheckStep "消费方演练（toolchain/consumer_smoke.ps1）" {
            $consumerScript = Join-Path $RepoRoot "toolchain\consumer_smoke.ps1"
            $consumerArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $consumerScript)
            if ($UnityExe -ne "") {
                $consumerArgs += @("-UnityExe", $resolvedUnityExe)
            }
            # 判断记录（门禁误判修复：子脚本真失败仍被记 PASS）：`Invoke-CheckStep` 用
            # `$result = & $Action` 取整个 scriptblock 的输出流当返回值——不止最后一句 `return`，
            # scriptblock 内每一句"没被消费"的输出都会被收进去。`& powershell @consumerArgs` 这个
            # 原生调用一旦不赋值/不重定向，它自己的整段控制台输出（consumer_smoke.ps1 内部每一步
            # 的 Write-Host，含步骤头、PASS/FAIL、汇总表——不区分它用的是 Write-Host 还是
            # Write-Output，从外层进程看都是同一股 stdout）就会被 PowerShell 当成管道对象，跟下一句
            # `return ($LASTEXITCODE -eq 0)` 的布尔值一起，被 `& $Action` 收成一个非空数组
            # （形如 @("...若干行文本...", $false)）。`Invoke-CheckStep` 的分类逻辑对数组
            # 落到 `else { $ok = [bool]$result } ` 分支——PowerShell 把"非空数组"直接转 `$true`，
            # 不看数组元素内容，于是不管子脚本真实退出码是什么，这一步恒定判 PASS（该子脚本控制台
            # 输出因此也从没打进 check.ps1 自己的 -LogFile transcript，是这个误判的旁证）。
            # 复现与验证见 toolchain/consumer_smoke.ps1 头注释旁的验证记录（本次改动未留仓库内）。
            # 修法：先用 `| Out-Null` 把子进程那股 stdout 在管道里吃掉，不让它混进 scriptblock 的
            # 返回值；$LASTEXITCODE 由子进程退出码设置，不受管道重定向影响，随后单独读取不受影响。
            & powershell @consumerArgs | Out-Null
            return ($LASTEXITCODE -eq 0)
        }
    }
}

# -----------------------------------------------------------------------------
# 汇总
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "==== 汇总 ====" -ForegroundColor Cyan
$script:Results | Format-Table -AutoSize Step, Result, Seconds, Detail | Out-String -Width 4096 | Write-Host

# 收边任务修正（严重的单步失败漏判 bug）：不加 @() 强制数组上下文时，Where-Object 恰好只匹配到
# 一个对象会返回裸的 PSCustomObject 标量而不是集合——标量没有 Count 属性，$failed.Count 取到
# $null，下面 `$null -gt 0` 在 PowerShell 里是 $false，导致"恰好只有一步失败"这一种情况被误判为
# "门禁通过"（0 步失败、2+ 步失败都不受影响：前者 Where-Object 本就返回 $null 恰好也是"假"，
# 后者返回真正的数组 .Count 正常工作，只有"恰好 1 步失败"这个边界会静默漏判）。实测复现：本次
# 收边 I3 门禁第一次跑 Unity PlayMode 测试步骤失败（本步骤本身汇总多个 NUnit 用例结果为一个
# FAIL），全部其余 14 步通过，$failed 因此恰好命中这个漏洞，脚本打印"门禁通过：全部 15 步"、
# exit 0——与该步骤自己汇总表里明确打印的 FAIL 行直接矛盾。加 @() 强制数组上下文后 .Count 在
# 0/1/多个匹配下都正确。
$failed = @($script:Results | Where-Object { $_.Result -eq "FAIL" })
$totalSeconds = ($script:Results | Measure-Object -Property Seconds -Sum).Sum

if ($failed.Count -gt 0) {
    Write-Host "门禁失败：$($failed.Count) 步未通过（共 $($script:Results.Count) 步，总用时 ${totalSeconds}s）。" -ForegroundColor Red
    $exitCode = 1
} else {
    Write-Host "门禁通过：全部 $($script:Results.Count) 步（总用时 ${totalSeconds}s）。" -ForegroundColor Green
    $exitCode = 0
}

# 两个出口统一在这里落地：先关 transcript（保证汇总表本身也写进 -LogFile，不止步骤明细），
# 再退出，退出码原样透传给调用方的 $LASTEXITCODE（见 .PARAMETER LogFile 判断记录）。
if ($script:TranscriptStarted) {
    try { Stop-Transcript | Out-Null } catch {}
    $script:TranscriptStarted = $false
}
exit $exitCode

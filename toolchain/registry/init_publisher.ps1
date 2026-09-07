<#
.SYNOPSIS
    无人值守创建/更新私服发布账号，并把认证令牌写入一份项目本地的 .npmrc（不动用户全局
    ~/.npmrc），供 build.ps1 -PublishRegistry 用 `npm publish --userconfig <该文件>` 完成发布。

.PARAMETER RegistryUrl
    私服地址，默认读本目录 registry.json 的 url 字段。

.PARAMETER Username
    发布账号用户名，默认 ws-game-publisher。

.PARAMETER Password
    发布账号密码，默认 ws-game-publisher-local（仅用于本机/局域网内部私服，不是对外服务的账号体系，
    见本目录 README.md"发布账号不是对外身份体系"一节）；需要更强密码时显式传入。

.PARAMETER Email
    发布账号邮箱，Verdaccio 建账户接口要求该字段存在但不校验有效性，默认
    ws-game-publisher@local.invalid。

.PARAMETER NpmrcPath
    输出的 .npmrc 路径，默认本目录 .npmrc（.gitignore 已忽略，含令牌，不得提交）。

.PARAMETER HtpasswdPath
    直接写入的 htpasswd 文件路径，默认本目录 htpasswd（与 config.yaml `auth.htpasswd.file: ./htpasswd`
    一致）。见下方"P06 根治"判断记录——本脚本改为先直接把发布账号的 bcrypt 哈希写进这个文件，
    再发 HTTP 请求换取令牌，不再依赖 Verdaccio 的自注册端点。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。

    判断记录（无人值守发布账号的实现方式）：
    `npm adduser`/`npm login` 是交互式命令（会在终端弹用户名/密码/邮箱提示，或起浏览器走 OAuth），
    无法在脚本里无人值守调用。Verdaccio 内置的 htpasswd 认证插件复用了 npm 经典的 CouchDB 兼容
    用户接口：`PUT /-/user/org.couchdb.user:<username>`，请求体 `{name, password}`，服务端建
    （或更新）该用户的 htpasswd 条目并在响应体里直接返回一个可用的认证令牌（`token` 字段）——这正是
    `npm adduser` 内部实际调用的同一个 HTTP 端点，只是 npm CLI 把"发 HTTP 请求"包在了交互式提示
    后面。令牌写入项目本地 `.npmrc`（`//<host>/:_authToken=<token>`），不写用户全局 `~/.npmrc`——
    `npm publish --userconfig <本地 .npmrc 路径>` 即可用该令牌完成发布，不污染本机其它 npm 配置、
    不需要任何人工在终端里输入用户名密码。

    判断记录（P06 根治，2026-09-07，htpasswd 预写入，见 config.yaml 同批修改）：`config.yaml`
    把 `auth.htpasswd.max_users` 改成 `-1`（彻底禁用自注册，堵住"任何访问者自行注册即可获得
    发布权限"这个 P06 漏洞）之后，原来"直接发 PUT 请求建号"的方式会失败——Verdaccio 未预先以
    目标用户身份认证的 PUT 请求走 `auth.add_user`，与 `npm adduser`/任意访客自注册走的是同一条
    受 `max_users` 门槛限制的路径，无法自己豁免自己。改为：发 PUT 请求前，先用本脚本内嵌的一段
    Node 脚本（复用已随 Verdaccio 一起安装、`toolchain/registry/node_modules/bcryptjs` 提供的
    bcrypt 实现，rounds=10，与 verdaccio-htpasswd 插件默认哈希算法/轮数一致，见其 `htpasswd.js`
    构造函数）直接把该账号的哈希写进 `-HtpasswdPath` 指向的文件（若该用户名已有旧条目，先移除
    再追加新的，天然支持"重跑本脚本=改密码"）。这样发起 PUT 请求时，请求自带的 Basic 认证头已经
    能通过 htpasswd 文件验证，Verdaccio 的 `user.js` 路由处理器判定"请求已经以目标用户身份认证"
    （`remoteName === name`），改走"重新登录换新令牌"分支（`auth.authenticate`），完全不经过
    `auth.add_user`/`sanityCheck`/`max_users` 这条门槛——这是 Verdaccio 自己路由逻辑里本来就有
    的分支，不是绕开安全检查的后门；效果是"只有知道本脚本、能在本机文件系统写 htpasswd 文件的人
    才能创建初始发布账号"，普通网络访问者（哪怕是局域网内其它机器）无法再通过 HTTP 自注册拿到
    任何账号，与 P06 的目标一致。
#>
param(
    [string]$RegistryUrl = "",
    [string]$Username = "ws-game-publisher",
    [string]$Password = "ws-game-publisher-local",
    [string]$Email = "ws-game-publisher@local.invalid",
    [string]$NpmrcPath = "",
    [string]$HtpasswdPath = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

if ($RegistryUrl -eq "") {
    $registryJsonPath = Join-Path $ScriptDir "registry.json"
    if (-not (Test-Path $registryJsonPath)) {
        Write-Host "找不到 $registryJsonPath，且未显式传 -RegistryUrl" -ForegroundColor Red
        exit 1
    }
    $registryObj = (Get-Content -Path $registryJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $RegistryUrl = $registryObj.url
}
$RegistryUrl = $RegistryUrl.TrimEnd('/')

if ($NpmrcPath -eq "") {
    $NpmrcPath = Join-Path $ScriptDir ".npmrc"
}
if ($HtpasswdPath -eq "") {
    $HtpasswdPath = Join-Path $ScriptDir "htpasswd"
}

# -----------------------------------------------------------------------------
# P06 根治：config.yaml 的 auth.htpasswd.max_users 已改为 -1（彻底禁用自注册），下面第二步的
# HTTP PUT 建号请求必须先以目标用户身份"已认证"才能走"换令牌"分支而不触发这道门槛（见文件头
# "P06 根治"判断记录）。这里先用内嵌 Node 脚本直接把该账号的 bcrypt 哈希写进 htpasswd 文件——
# 复用随 Verdaccio 一起装好的 node_modules/bcryptjs（无需额外安装依赖），rounds=10 与
# verdaccio-htpasswd 插件默认一致。若该用户名已有旧条目（重跑本脚本/改密码），先移除再追加新的。
# -----------------------------------------------------------------------------
Write-Step "直接写入 htpasswd 账号条目（P06 根治：绕开受 max_users 门槛限制的自注册端点）：$HtpasswdPath"
$nodeCmd = Get-Command node -ErrorAction SilentlyContinue
if (-not $nodeCmd) {
    Write-Host "找不到 node（本目录 node_modules/bcryptjs 需要用 node 运行），请先安装 Node.js" -ForegroundColor Red
    exit 1
}
$bcryptjsDir = Join-Path $ScriptDir "node_modules\bcryptjs"
if (-not (Test-Path $bcryptjsDir)) {
    Write-Host "找不到 $bcryptjsDir（先跑 npm ci 安装 toolchain/registry 的依赖，见该目录 README.md）" -ForegroundColor Red
    exit 1
}

$seedScriptPath = Join-Path $env:TEMP ("ws_game_htpasswd_seed_" + [guid]::NewGuid().ToString("N") + ".js")
$seedScriptLines = @(
    "const fs = require('fs');",
    "const bcrypt = require(process.argv[2]);",
    "const htpasswdPath = process.argv[3];",
    "const username = process.argv[4];",
    "const password = process.argv[5];",
    "const hash = bcrypt.hashSync(password, 10);",
    "const comment = 'autocreated ' + new Date().toJSON();",
    "const newLine = username + ':' + hash + ':' + comment;",
    "let lines = [];",
    "if (fs.existsSync(htpasswdPath)) {",
    "  const raw = fs.readFileSync(htpasswdPath, 'utf8');",
    "  lines = raw.split(/\r?\n/).filter(function (line) {",
    "    if (!line.trim()) return false;",
    "    const existingUser = line.split(':', 1)[0];",
    "    return existingUser !== username;",
    "  });",
    "}",
    "lines.push(newLine);",
    "fs.writeFileSync(htpasswdPath, lines.join('\n') + '\n', 'utf8');",
    "console.log('seeded: ' + username);"
)
[System.IO.File]::WriteAllLines($seedScriptPath, $seedScriptLines, (New-Object System.Text.UTF8Encoding($false)))
try {
    & node $seedScriptPath $bcryptjsDir $HtpasswdPath $Username $Password
    if ($LASTEXITCODE -ne 0) {
        Write-Host "写入 htpasswd 账号条目失败，退出码 $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
    Write-Host "  已写入 $HtpasswdPath 的账号条目：$Username"
} finally {
    Remove-Item -Path $seedScriptPath -Force -ErrorAction SilentlyContinue
}

Write-Step "确认私服可访问：$RegistryUrl/-/ping"
try {
    $pingResp = Invoke-WebRequest -Uri ($RegistryUrl + "/-/ping") -UseBasicParsing -TimeoutSec 5
    if ($pingResp.StatusCode -ne 200) {
        throw "非 200 响应：$($pingResp.StatusCode)"
    }
    Write-Host "  可访问（200）"
} catch {
    Write-Host "私服未就绪：$($_.Exception.Message)（先跑 start_registry.ps1）" -ForegroundColor Red
    exit 1
}

# -----------------------------------------------------------------------------
# 建账号（首次调用）/ 重新登录取新令牌（账号已存在时的等价效果）：PUT /-/user/org.couchdb.user:
# <username>。判断记录（为什么请求要带 Basic 认证头，即便是首次创建）：Verdaccio 这个端点的
# 处理器（node_modules/verdaccio/build/api/endpoint/api/user.js user_default）按"请求是否已经
# 以目标用户身份认证"分两条路径——已认证（`req.remote_user.name === name`，即请求带了该用户名/
# 密码算出的 Basic 认证头且验证通过）走"重新登录换新令牌"路径，未认证走"新建用户"路径（该路径对
# 已存在的用户名会报 "username is already registered" 并失败）。实测复现：第一次不带认证头的
# PUT 创建成功，第二次同样不带认证头再跑一遍脚本就会失败在"已存在"上——不满足"重复跑不报错"这条
# 无人值守要求。统一在每次请求都带上 Basic 认证头解决：账号不存在时该认证头验证不通过、请求退化
# 为匿名，落到"新建"路径，成功创建；账号已存在时该认证头验证通过，落到"重新登录"路径，成功换发
# 一个新令牌——两种情况都以 201 成功收尾，天然幂等，不需要先探测账号是否存在再分支处理。
# -----------------------------------------------------------------------------
Write-Step "创建/更新发布账号：$Username"
$userDocId = "org.couchdb.user:" + $Username
$userDocUrl = $RegistryUrl + "/-/user/" + $userDocId
$userDocBody = @{
    name     = $Username
    password = $Password
    email    = $Email
    type     = "user"
    roles    = @()
} | ConvertTo-Json

$basicAuthBytes = [System.Text.Encoding]::UTF8.GetBytes($Username + ":" + $Password)
$basicAuthHeader = "Basic " + [Convert]::ToBase64String($basicAuthBytes)

$tokenResponse = $null
try {
    $tokenResponse = Invoke-RestMethod -Method Put -Uri $userDocUrl -Body $userDocBody -ContentType "application/json" -Headers @{ Authorization = $basicAuthHeader }
} catch {
    $errorDetail = $_.Exception.Message
    if ($_.ErrorDetails) { $errorDetail = $_.ErrorDetails.Message }
    Write-Host "创建/更新账号失败：$errorDetail" -ForegroundColor Red
    exit 1
}

if ($null -eq $tokenResponse.token) {
    Write-Host "响应体里没有 token 字段，无法继续（响应：$($tokenResponse | ConvertTo-Json -Depth 5)）" -ForegroundColor Red
    exit 1
}
$authToken = $tokenResponse.token
Write-Host "  账号就绪，已拿到认证令牌（长度 $($authToken.Length)）"

# -----------------------------------------------------------------------------
# 写 .npmrc：注册表主机部分（不含协议前缀）+ 令牌；额外写作用域到注册表的映射，方便
# `npm publish --registry <url>` 与包名里的作用域一致时也能免另传 --registry。
# -----------------------------------------------------------------------------
Write-Step "写入 $NpmrcPath"
$registryUri = [System.Uri]$RegistryUrl
$hostPart = $registryUri.Authority
# 判断记录：不写 always-auth=true —— npm 7+ 已不识别这个经典配置项（`npm publish` 会打印
# "Unknown user config 'always-auth'" 警告，不影响功能但徒增噪音，实测发现后去掉）；令牌本身
# 通过 `//<host>/:_authToken=` 这一行按 registry host 精确匹配生效，不需要它。
$npmrcLines = @(
    "//$hostPart/:_authToken=$authToken"
)
[System.IO.File]::WriteAllLines($NpmrcPath, $npmrcLines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  已写入：$NpmrcPath"

Write-Host ""
Write-Host "==== init_publisher.ps1 完成：账号 $Username 已就绪，令牌已写入 $NpmrcPath ====" -ForegroundColor Green
Write-Host "  发布用法：npm publish <包目录或 .tgz> --registry $RegistryUrl --userconfig `"$NpmrcPath`""

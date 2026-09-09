# 私服（Verdaccio 注册表）

本目录是框架四个可发布包（`com.gamefoundation.adapter.unity`/`com.gamefoundation.framework-data`/
`com.gamefoundation.toolchain`/`com.gamefoundation.adapter.headless`——第四个包 ADR-0018 决策 3
新增，见下）的私有 npm 兼容注册表运行时，供内网/本机搭一个不依赖任何外部公网服务的包仓库；游戏侧
Unity 工程用作用域注册表（`scopedRegistries.scopes: ["com.gamefoundation"]`）按版本号依赖前三个包
（第四个包不是 Unity 依赖，不写入 `Packages/manifest.json`，供内容编辑器等无头宿主按需
`npm install`，见 `manifests/adapter-headless/README.md` 判断记录），是 `toolchain/get_framework.ps1`
（拉发布产物 zip）之外的第二条消费通道，两条通道并存、互不排斥（见根 README.md"版本与发布"一节、
落地计划 3.5 节"私服通道"）。

## 快速开始

```powershell
# 1. 起私服（前台，Ctrl+C 停止）
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\registry\start_registry.ps1

# 1'. 或后台常驻（写 PID 到 toolchain/registry/verdaccio.pid）
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\registry\start_registry.ps1 -Detach
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\registry\start_registry.ps1 -Status
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\registry\start_registry.ps1 -Stop

# 2. 无人值守建发布账号 + 令牌（写入本目录 .npmrc，不动全局 ~/.npmrc）
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\registry\init_publisher.ps1

# 3. 打包 + 发布四个包（见仓库根 build.ps1 -PublishRegistry）
powershell -File build.ps1 -Dist auto
powershell -File build.ps1 -Release <version> -Publish -PublishRegistry
# 或单独只发注册表、不走完整 -Release 流程（已有 dist/<ver>/packages/ 时）：
npm publish dist\<ver>\packages\com.gamefoundation.adapter.unity   --registry http://127.0.0.1:4873 --userconfig toolchain\registry\.npmrc
npm publish dist\<ver>\packages\com.gamefoundation.framework-data  --registry http://127.0.0.1:4873 --userconfig toolchain\registry\.npmrc
npm publish dist\<ver>\packages\com.gamefoundation.toolchain       --registry http://127.0.0.1:4873 --userconfig toolchain\registry\.npmrc
npm publish dist\<ver>\packages\com.gamefoundation.adapter.headless --registry http://127.0.0.1:4873 --userconfig toolchain\registry\.npmrc
```

`curl http://127.0.0.1:4873/-/ping` 返回 200 即服务已就绪。

## 游戏侧接入（`Packages/manifest.json`）

```json
{
  "scopedRegistries": [
    {
      "name": "ws-game private registry",
      "url": "http://127.0.0.1:4873",
      "scopes": ["com.gamefoundation"]
    }
  ],
  "dependencies": {
    "com.gamefoundation.adapter.unity": "1.0.0",
    "com.gamefoundation.framework-data": "1.0.0",
    "com.gamefoundation.toolchain": "1.0.0"
  }
}
```

局域网内其它机器访问时把 `url` 换成私服所在机器的地址（`start_registry.ps1 -Listen 0.0.0.0:4873`
监听所有网卡）；`toolchain/get_framework.ps1 -FromRegistry` 可以帮忙生成/更新这段 `manifest.json`
片段并打印说明，见该脚本头注释。

第四个包 `com.gamefoundation.adapter.headless`（ADR-0018 决策 3，无头适配层）不是 Unity 依赖，
不属于上面 `dependencies` 片段——内容编辑器等无头宿主按需自行 `npm install
com.gamefoundation.adapter.headless@<version> --registry <私服地址>` 获取，见
`manifests/adapter-headless/README.md`。`get_framework.ps1 -FromRegistry` 会把它登记进
`ws-game.lock` 的 `source.optional_packages` 字段（可选包清单），并在命令行输出中提示这条
`npm install` 命令，不会静默略过。

## 升级 / 回退

- **升级**：改 `manifest.json` 里三个依赖的版本号为目标版本，重新解析包（Unity 编辑器自动，或命令行
  `-executeMethod` 触发一次包解析）。
- **回退**：同样只是改回旧版本号——每个已发布版本号都是不可变的（见下"不可变发布"），旧版本号
  永远可以重新解析到，不存在"回退不到"的情况。
- 私服（`storage/` 目录）是这台机器的本地状态；换一台机器/重建私服后，只要把 `storage/` 目录整体
  拷过去，或者重新对每个历史版本号跑一遍 `npm publish`（前提是仍持有对应版本的 `dist/<ver>/
  packages/`），已发布的版本就能在新私服上复现。

## 离线 zip 通道并存说明

私服（本目录）与 `toolchain/get_framework.ps1` 默认走的离线 zip + 锁文件通道（`gh release
download` 或 `-FromLocalDist`）是并列的两条消费通道，不是"私服替代 zip"的关系：

- 私服通道优点：游戏侧只改版本号即可升级，不需要手工下载/解压/校验哈希，适合内网多台机器、多个
  游戏并行开发、需要频繁试验不同版本号的场景。
- zip 通道优点：不依赖任何常驻服务，产物本身自带哈希锁文件（`ws-game.lock`）可离线校验完整性，
  适合没有内网私服、或需要把某个版本号的产物长期归档保存的场景。
- 两条通道打包的内容一致（同一份 `dist/<ver>/`，只是私服通道额外把 `adapters/unity/Packages/
  com.gamefoundation.adapter.unity`、`data/_framework` + `assets/_placeholder` + `assets/
  textmesh_pro_essentials`、`toolchain/`（不含本目录）、`adapters/headless/`（ADR-0018 决策 3
  新增）四部分各自独立打成 npm 包，供 UPM 按包名单独解析、而不是整个 zip 一次性拿全部内容），选
  哪条通道不影响该版本号对应的实际内容。

## 设计判断记录

- **为什么不代理任何上游注册表**：见 `config.yaml` 头部注释——本私服只服务框架自己的四个包，
  Unity 官方包（`com.unity.*`）走 Unity 自己的默认注册表，不需要经过这个作用域注册表；`npm ci`
  安装 Verdaccio 本身用的是 npm 默认公网注册表，与 Verdaccio 服务启动后代理什么是两回事。
- **为什么包名规则写 `com.gamefoundation.*` 而不是任务描述里字面的 `com.gamefoundation/*`**：
  Unity UPM 包名是形如 `com.gamefoundation.adapter.unity` 的点分字符串，不带 `@` 前缀，不是 npm
  意义上的"作用域包"（`@scope/pkg`）；`com.gamefoundation/*` 这个 glob 会匹配"名字正好是
  `com.gamefoundation`、路径下有子级"的包，匹配不到任何真实包名，必须改成按名字前缀匹配的
  `com.gamefoundation.*`。这是任务描述与 npm/Verdaccio 实际语法之间的一处术语误用，本次落地时
  按 Verdaccio 真实语法改写，效果（四个包允许匿名读、发布需登录）与任务描述的意图一致。
- **无人值守发布账号**：见 `init_publisher.ps1` 头部判断记录——先直接把账号的 bcrypt 哈希写进
  htpasswd 文件，再调用 Verdaccio 内置 htpasswd 插件复用的 `PUT /-/user/org.couchdb.user:<name>`
  接口（`npm adduser` 内部实际调用的同一个 HTTP 端点，此时请求已经能用刚写入的凭据完成 Basic
  认证，换到的是"重新登录发新令牌"分支而不是受 `max_users` 门槛限制的自注册分支，见下一条），
  拿到令牌后写进项目本地 `.npmrc`（不碰用户全局 `~/.npmrc`）。
- **LAN 场景下普通访问者不能自注册获得发布权限（P06 根治，2026-09-07）**：`config.yaml` 的
  `auth.htpasswd.max_users` 设为 `-1`，彻底禁用 Verdaccio 的自注册端点（任何未预先认证的
  `PUT /-/user/...`/`npm adduser` 请求一律被拒）；`publish`/`unpublish` 从 `$authenticated`
  （任何登录成功的账号都算，包括自注册出来的）改为显式指定发布账号用户名 `ws-game-publisher`
  （与 `init_publisher.ps1 -Username` 默认值一致——htpasswd 插件不提供真正的"用户组"，Verdaccio
  的包访问规则支持直接写字面用户名，见 `config.yaml` 同批判断记录）。`access`（读）不变，仍是
  `$all`，`-Listen 0.0.0.0` 暴露到局域网时消费方（`npm install`/UPM 拉包）依旧不需要任何账号。
  `max_users: -1` 会连带挡住 `init_publisher.ps1` 原来"直接发 PUT 请求建号"的方式（那条路径与
  普通自注册走同一道 `max_users` 门槛），因此该脚本改为上一条"无人值守发布账号"描述的"先写
  htpasswd 文件、再走已认证请求换令牌"这条不受该门槛限制的路径——效果是只有能在本机文件系统写
  `toolchain/registry/htpasswd` 的人（即跑这个脚本的人）才能建到发布账号，局域网内其它机器的
  访问者即使能连到私服地址，也无法再靠自注册拿到任何账号（更谈不上发布权限）。
- **发布账号不是对外身份体系**：`init_publisher.ps1` 的默认用户名/密码是本机/局域网内部私服的
  占位凭据，不代表任何真实人员身份，也不用于鉴别"谁能读包"（读包本身匿名开放）——它只是
  "谁有权限往这个私服里推包"这一件事的最小实现，等价于给 CI/构建机发一把只写不读的部署密钥。
  对外提供服务、需要真实用户体系时应替换为该场景下合适的身份方案，不在本次交付范围内。
- **`storage/`/`node_modules/`/`htpasswd`/`.npmrc`/`verdaccio.pid`/`*.log` 均已 `.gitignore`**：
  这些要么是可重新生成的依赖安装产物，要么是本机私服的运行状态/凭据，不应提交进源码仓库。
- **`-Stop` 停不掉私服 / `-PidFile` 记的 PID 不一定是真正监听端口的进程（P07 根治，2026-09-08）**：
  `start_registry.ps1` 头部判断记录二——`-Detach` 就绪后额外按 `-Listen` 端口用
  `Get-NetTCPConnection -LocalPort <port> -State Listen` 核实一次真正监听该端口的进程 PID，与
  `Start-Process` 记录的不一致时以端口查到的为准重写 PID 文件；`-Stop` 先按 PID 文件停一次，再
  按端口兜底查一次仍在监听的进程一并停止，最后轮询确认端口已释放才算真正停止成功，可重复调用
  （幂等）。新增 `-Status` 只读诊断 PID 文件记录的 PID 是否存活、端口当前真正监听的进程
  PID——两者不一致时以后者为准。本机实测：`-Detach` → `/-/ping` 200 → `-Status`（真实 PID 与
  端口一致）→ `-Stop`（端口释放、`ping` 超时）→ `-Status`（未监听）→ 重复 `-Stop`（幂等，
  提示"本来就是释放状态"）；另单独验证了 PID 文件被删除/损坏但服务仍在跑时 `-Stop` 仍能靠端口
  兜底找到并停止。

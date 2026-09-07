# 项目与发行审查（基线 7e63d66）

本表记录项目脚本、发行物和消费者边界。PJT-A/B/C 是本轮真实隔离项目复现，分别对应 P01/P03/P02；其余项目条目为当前源码与配置静态审阅。不能把 GitHub 绿灯、空模板测试或源仓库校验当作独立包消费证明。

## P01（P1）无预上传附件时 Release fallback 不能从干净 checkout 打包

**触发与实际行为。** 在 git archive HEAD 得到的干净 fixture 中执行 Release 同款 Quick 检查，再执行 build.ps1 -SyncOnly -Dist 1.0.0 -Zip。Quick 退出 0，产物落到指定外部 artifacts；SyncOnly 退出 1，首个缺失为 core/foundation/bin/Release/netstandard2.1/Core.Foundation.dll。Quick 中 Validator 可能生成 Debug core/bin，因此结论是缺少默认 Release DLL，不是 fixture 完全没有 bin。证据见 [PJT-A Quick](evidence/pjt-a-quick-check.stdout.log) 和 [PJT-A SyncOnly](evidence/pjt-a-synconly.stdout.log)。

**原因与影响。** Release workflow 在 [release.yml:146](D:/workespace/ws-game-review-7e63d66/.github/workflows/release.yml:146) 把 Quick 构建放在 runner 临时 artifacts，check.ps1 主 dotnet build 输出也在该路径（[check.ps1:460](D:/workespace/ws-game-review-7e63d66/check.ps1:460)），而 Quick 跳过正常目录的 build fallback（[check.ps1:732](D:/workespace/ws-game-review-7e63d66/check.ps1:732)）。随后 build.ps1 固定读取 Release/netstandard2.1 DLL（[build.ps1:545](D:/workespace/ws-game-review-7e63d66/build.ps1:545)），缺失即退出（[build.ps1:551](D:/workespace/ws-game-review-7e63d66/build.ps1:551)）。已有 ZIP 时 fallback 被跳过，所以最新绿灯和已有 v1.0.0 附件没有覆盖该分支。

**建议与验收。** 在发布分支把构建、同步和打包绑定同一 artifacts 根，或在无附件分支显式先生成默认 Release DLL；验收干净 checkout、无预上传 ZIP 时能生成 ZIP，且包清单与 lock/hash 一致。证据等级：隔离项目 REPRODUCED。

## P02（P2）独立 ZIP 与 UPM toolchain 都不能完成真实 validator 消费

**触发与实际行为。** 对真实 dist/ws-game-1.0.0.zip 解压后运行 python toolchain/validate_data.py --data-root <zip>/data/_framework：骨架 5 files 通过，第二道因缺少 presentation/Presentation.Common.csproj 和 adapters/stub/Adapters.Stub.csproj 退出 1。对真实 toolchain TGZ 解压的 Tools~ 按 README 命令运行时，find_repo_root 拼出不存在的 <package>/toolchain/validator；直接 build Tools~/validator 也因相同项目引用缺失退出 1。证据见 [ZIP 输出](evidence/pjt-c-zip-validate.stdout.log)、[UPM 输出](evidence/pjt-c-toolchain-validate.stdout.log) 和 [UPM build 输出](evidence/pjt-c-toolchain-build.stdout.log)。两种包装是一个独立消费缺口的两个入口证据。

**原因与影响。** ZIP 打包脚本排除 bin/obj 且交付列表不含 presentation、adapters/stub/core 源码，但 [Validator.csproj:16](D:/workespace/ws-game-review-7e63d66/toolchain/validator/Validator.csproj:16) 和 [Validator.csproj:17](D:/workespace/ws-game-review-7e63d66/toolchain/validator/Validator.csproj:17) 仍引用这些项目。UPM 放置逻辑在 [build.ps1:979](D:/workespace/ws-game-review-7e63d66/build.ps1:979)，而 validator 脚本按 parent.parent 解析根并在 [validate_data.py:225](D:/workespace/ws-game-review-7e63d66/toolchain/validate_data.py:225)、[validate_data.py:373](D:/workespace/ws-game-review-7e63d66/toolchain/validate_data.py:373) 查找固定路径。--skip-dotnet 或源仓库校验不能覆盖此问题。

**建议与验收。** 明确 validator 是随包自包含，或改用包内可解析的独立校验器和依赖；README 命令、ZIP、UPM 目录布局应由干净解压 fixture 验证。验收两个独立包均能完成非 --skip-dotnet 校验。证据等级：ZIP/UPM 隔离消费 REPRODUCED。

## P03（P1）同步公共 TMP 目录会删除消费者自有文件

**触发与实际行为。** 新建 Unity fixture，预置 framework TMP 文件和 Assets/TextMesh Pro/game-owned-sentinel.txt，先通过绝对路径护栏，再运行真实 sync_package_content.ps1。脚本退出 0、框架文件同步成功，但 sentinel 被删除，标记 REPRODUCED。证据见 [路径护栏](evidence/pjt-b-path-guard.log) 与 [同步输出](evidence/pjt-b-sync.stdout.log)。所有递归删除目标均在新 fixture 内。

**原因与影响。** 脚本把目标公共目录设为 [sync_package_content.ps1:175](D:/workespace/ws-game-review-7e63d66/toolchain/sync_package_content.ps1:175)，对整树执行同步（[sync_package_content.ps1:177](D:/workespace/ws-game-review-7e63d66/toolchain/sync_package_content.ps1:177)），并在 [sync_package_content.ps1:159](D:/workespace/ws-game-review-7e63d66/toolchain/sync_package_content.ps1:159) 删除不在 framework keep 集合中的目标文件。消费者字体、材质和 meta 因此会被清掉；Assets/Framework/Resources/Fonts 的 additive 路径不改变 TMP 公共目录的风险。

**建议与验收。** 使用包专属子目录、只删除由包管理的清单，或显式保护消费者文件；验收预置 sentinel、字体、材质和 meta 同步后仍存在，框架 stale 文件仍能清理。证据等级：隔离项目 REPRODUCED。

## P04（P2）get_framework 允许请求版本与本地归档版本不一致

**触发与实际行为。** 对合法 1.0.0 本地归档执行 get_framework.ps1 -Version 1.0.1 -FromLocalDist。当前实现只 warning，不阻断，在 [get_framework.ps1:354](D:/workespace/ws-game-review-7e63d66/toolchain/get_framework.ps1:354) 按请求版本命名目录并可能先删除该目录（[get_framework.ps1:355](D:/workespace/ws-game-review-7e63d66/toolchain/get_framework.ps1:355)），实际 1.0.0 内容会落入 ws-game-1.0.1。它不是 hash 绕过或 lock 被篡改。

**原因与影响。** 头部契约说明版本不匹配不落地，但实现明确采取宽松 warning 行为（[get_framework.ps1:282](D:/workespace/ws-game-review-7e63d66/toolchain/get_framework.ps1:282)、[get_framework.ps1:292](D:/workespace/ws-game-review-7e63d66/toolchain/get_framework.ps1:292)）。错误请求可能覆盖已有正确目标目录，版本目录名与实际 lock 身份不一致。

**建议与验收。** 默认严格匹配并按归档实际版本命名；若确需迁移，增加显式 allow-mismatch 并输出源/目标身份。验收错版本请求不会删除或覆盖目标，宽松模式必须显式可见。证据等级：静态，NOT_EXECUTED。

## P05（P2）维护分支发布硬编码只推 main 和全部 tags

**触发与实际行为。** 按 README 的 release/1.0.x 维护分支发布流程运行 build.ps1 -Publish，脚本仍执行 git push origin main --tags（[build.ps1:1218](D:/workespace/ws-game-review-7e63d66/build.ps1:1218)、[build.ps1:1241](D:/workespace/ws-game-review-7e63d66/build.ps1:1241)）。tag 可正确指向发布 commit，但维护分支远端不会前进，且本地 main 的提交存在被一并推送的风险。

**建议与验收。** 发布目标应取当前维护分支并只推送该分支及明确创建的 tag；验收维护分支、tag、远端状态与发布 commit 一致，main 不被隐式推进。证据等级：静态，NOT_EXECUTED。

## P06（P1）LAN registry 中普通自注册用户可获得发布权限

**触发与实际行为。** 当 registry 按 README 以 -Listen 0.0.0.0 暴露 LAN 时，任何可访问者可先自行注册再登录，成为 $authenticated，从而匹配 publish/unpublish 规则。当前未注册账号、发布或删除包，结论来自配置和本机依赖静态审阅；不是匿名用户直接发布。

**原因与影响。** 配置没有 max_users（[config.yaml:32](D:/workespace/ws-game-review-7e63d66/toolchain/registry/config.yaml:32)），publish/unpublish 规则均匹配 $authenticated（[config.yaml:41](D:/workespace/ws-game-review-7e63d66/toolchain/registry/config.yaml:41)、[config.yaml:46](D:/workespace/ws-game-review-7e63d66/toolchain/registry/config.yaml:46)、[config.yaml:51](D:/workespace/ws-game-review-7e63d66/toolchain/registry/config.yaml:51)），而文档只允许 init_publisher 推包（[config.yaml:18](D:/workespace/ws-game-review-7e63d66/toolchain/registry/config.yaml:18)）。本机依赖 verdaccio 6.10.3、verdaccio-htpasswd 13.1.3；htpasswd 实现 [htpasswd.js:31](D:/workespace/ws-game/toolchain/registry/node_modules/verdaccio-htpasswd/build/htpasswd.js:31) 默认 max_users 为 Infinity，用户实现 [user.js:30](D:/workespace/ws-game/toolchain/registry/node_modules/@verdaccio/config/build/user.js:30) 将登录用户归入 authenticated，授权实现 [utils.js:116](D:/workespace/ws-game/toolchain/registry/node_modules/@verdaccio/auth/build/utils.js:116) 匹配组，:154 的 unpublish 复用同类逻辑。

**建议与验收。** LAN 模式限制 publisher 组/owner，并把普通消费者与发布凭证分离；验收普通自注册账号只能读取，不能 publish/unpublish，且初始化发布者仍可执行正向流程。证据等级：静态，NOT_EXECUTED。

## P07（P2）同步占位资源路径与 Unity loader 资源路径不一致

**触发与实际行为。** 包同步脚本把 sfx.ui_click_01.wav 放到 GameFoundation/assets/_placeholder/sfx/ui_click_01.wav（[sync_package_content.ps1:171](D:/workespace/ws-game-review-7e63d66/toolchain/sync_package_content.ps1:171)），而 Unity loader 查找 GameFoundation/audio/ui_click_01.wav（[UnityResourceLoader.cs:508](D:/workespace/ws-game-review-7e63d66/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs:508)），sprite 和 vfx 也分别查正式目录（[UnityResourceLoader.cs:498](D:/workespace/ws-game-review-7e63d66/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs:498)、[UnityResourceLoader.cs:522](D:/workespace/ws-game-review-7e63d66/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs:522)）。新消费者按 sync 后的资源名请求时会找不到，可能回退。

**原因与影响。** 对应的 build 映射在 [build.ps1:683](D:/workespace/ws-game-review-7e63d66/build.ps1:683)、[build.ps1:686](D:/workespace/ws-game-review-7e63d66/build.ps1:686)、[build.ps1:693](D:/workespace/ws-game-review-7e63d66/build.ps1:693)，但 sync 与 loader 没有共同的资源布局契约；包 README 仍把两者描述为同职责流程。空模板不含 display 内容，不能据此声称默认启动必崩。

**建议与验收。** 统一占位目录与 loader 查找路径，或在同步后执行确定性的资源映射步骤；验收新消费者的 sprite、audio、vfx 三个实际目录及 sfx.ui_click_01 均能从独立包按声明路径加载。证据等级：静态，NOT_EXECUTED。

## 项目结论与优先顺序

| 维度 | 当前判断 | 证据与适用范围 |
|---|---|---|
| 架构复用 | 正向，可以复用规则、数据契约和基础服务 | 核心分层、引擎接口、策略与装配根已经存在；不同游戏仍需确认其需求落在已实现能力内，不能推导为任意类型游戏无需改框架。 |
| 核心与工具测试 | 有可靠的自动化基础，但组合边界不足 | 本轮 2,214 项 .NET 与 46 项 Python 测试通过；真实 Inventory/SaveSystem 及包消费复现仍暴露缺陷，fake 测试不能替代跨模块状态验证。 |
| 默认游戏入口 | 最小接入骨架 | 模板已明确不含战斗、技能、任务内容和可见玩家；当前 smoke 不能证明完整新游戏、重开游戏状态隔离或完整表现已完成。 |
| 发布与安装 | 基础设施已有，关键分支未闭合 | 现有 1.0.0 文件身份及 DLL hash 一致；P01–P07 分别揭示重建、工具消费、资源保护、版本、分支、权限与布局问题。 |
| Unity 与性能 | 有历史记录，本轮不作最终包验收 | 本轮未执行 Unity、独立版、真实游戏长时间运行或目标设备性能测量；.NET 门禁包含现有 Perf 类别，不能替代游戏场景容量、帧时间和内存预算。 |
| 文档可信度 | 需要按能力及证据重新标注 | 架构规格、默认装配、模块实现和已运行验收应分别列出；“阶段完成”不能替代这些边界。 |

新游戏接入建议按依赖顺序完成以下工作：

1. 锁定发布提交、包与 hash，明确时间模型、sprite/model 路线、移动/选目标/攻击方式和资源属性；先排除需求依赖尚未实现能力的情况。
2. 完成游戏装配：新局重置与退出重入、存档段与迁移、日历服务、召唤归属、自动任务驱动、商店入口等按需求接入，明确哪些状态由游戏拥有。
3. 填充并校验游戏内容：角色、技能、物品、任务、地图和数值数据；配置显示映射、动画、音效与特效，制作输入和 UI。框架提供机制，具体攻击动作、操作手感和内容仍需制作与调试。
4. 用最终交付包搭建独立工程，验收新局→移动攻击→奖励/装备/任务→死亡恢复→存读档→退出重入；加入本文的满包、部分扣除、来源销毁、冷资源超时和重复读档边界，再评估目标设备性能。

核心与引擎接口分层、数据 schema、策略注入和六个测试程序集说明了可复用方向；复用的是规则和管线。新游戏仍需选择 continuous/discrete 行为、sprite/3D 路线、数据资产输入、UI 以及 game lifecycle/provider 装配。优先修复 C/P1 的存档、奖励/任务和效果生命周期，再修复干净 ZIP/UPM 消费与消费者文件保护；随后用最终包 hash 绑定一次真实游戏纵切（新局、移动攻击、装备/任务、死亡、存读档、退出重入），最后同步矩阵与升级文档。当前证据不足以批准生产就绪。

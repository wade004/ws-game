# 1.14.0 项目发现

审计对象是冻结 detached worktree `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`（`VERSION=1.14.0`）。本文件只记录本轮对当前源码、正式包和独立 consumer 的结论；旧 c9ff301 审计与 `followup-2026-09-10` 仅作为变更背景。

## PJ114-01：SkillHost 旧二进制构造签名不兼容

- **契约/owner/条件**：`CHANGELOG.md` 将 1.14.0 定义为 MINOR，并声明已编译旧 consumer 替换正式 DLL 后无需重编译。该承诺属于框架公开 ABI，由框架发布维护者负责；条件是消费方只替换正式 1.14 DLL，不重建 consumer。
- **实际**：当前 `SkillHost` 只有含新增 `IFactionMatrix? factions` 的 18 参数构造器。用 1.13.0 正式 DLL 编译的 consumer 运行时进入旧 17 参数构造器并以预期 `ArgumentNullException=dataRegistry` 退出 0；仅替换五个 Core DLL 为正式 1.14.0 后，同一 consumer 退出 11，报告 `MissingMethodException`。脚本只替换五个依赖 DLL，未重建 consumer；保留的 consumer 程序集 SHA256 为 `13500df60474618a7960649f8ff1241a2cad460cfe576e30bbd033fd5457e00a`。
- **预期**：若继续保持 MINOR/二进制兼容承诺，17 参数入口必须可解析并转发到 18 参数实现；旧 consumer 替换 DLL 后应退出 0，重新编译的旧源码也应通过。若产品决定接受破坏性变更，则必须按工程规范升级版本/迁移契约并完整记录迁移；迁移说明不能把旧 binary 失败改写为兼容通过。
- **源码证据**：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\rules\skill\core\SkillHost.cs:90`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\CHANGELOG.md:106`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\11_工程规范与测试.md:181`。
- **复现/证据等级**：`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\docs-project\api-compat\run-skillhost-abi.ps1`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\docs-project\logs\api-compat\api-compat.log`；old/new 原始运行日志同目录 `run-old-formal.log`、`run-new-formal.log`。独立 consumer 的正式包替换运行，等级 A。
- **最小修复验收**：固定 1.13 consumer 程序集 hash；对 1.13 正式 DLL 运行 exit 0；只替换五个 1.14 正式 Core DLL，运行仍 exit 0；再以旧源码编译 consumer 通过。若走破坏性路径，验证新版本号、迁移后源码 consumer 通过，并保留旧 binary 失败作为已知兼容差异。

## PJ114-02：内置 ABI 门禁在基线缺失时把跳过显示为 PASS

- **契约/owner/条件**：发布门禁应让兼容性声明有可见、可判定的证据；`check.ps1 -SkipUnity` 仍执行 ABI 步骤。门禁/工具链维护者负责。条件是基线发行包存在时运行真实 consumer probe。
- **实际**：本轮冻结树不存在 `dist\ws-game-1.12.0.zip`。`toolchain\abi_probe.ps1` 默认 `SkipIfBaselineMissing=$true`，缺包时输出警告后 exit 0；`check.ps1` 对该子进程使用 `| Out-Null`，因此完整 transcript 中该步骤显示 `PASS`，没有 ABI 运行证明。这个 PASS 不能计入 ABI 验收。
- **预期**：基线缺失应显示为显式 `SKIP`/无资格状态，或在发布兼容性门禁中失败；不能在报告中当作 PASS。为 MINOR ABI 声明必须有对应兼容基线的旧 consumer 实跑；本轮 1.13.0 正式包只是覆盖当前改动的最小证据。
- **源码证据**：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\toolchain\abi_probe.ps1:68`、`:104`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\check.ps1:473`、`:486`、`:492`。
- **复现/证据等级**：`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\check\check-1.14.0-skipunity.log:109` 显示 ABI 行 PASS；同一命令的基线缺失事实由冻结树与脚本逻辑静态核对，等级 B。SkillHost 独立 probe 是等级 A 的补充。
- **最小修复验收**：在无基线时输出可见 SKIP 且汇总不写 PASS；在基线存在时记录 old/new exit、consumer hash 与实际 DLL hash；发布模式对兼容承诺缺证据返回非零或阻断。

## PJ114-03：显式 ABI 输出目录可被无边界递归删除（工具安全）

- **契约/owner/条件**：工具链只能清理自己创建且已验证范围内的临时目录。owner 是工具链维护者；条件是调用者传入 `-OutDir`。
- **实际**：`abi_probe.ps1:116-117` 对已存在的显式 `$OutDir` 直接 `Remove-Item -Recurse -Force`，没有验证目标位于本轮唯一临时目录，也没有拒绝用户已有目录。本轮未用已有资料目录执行该路径，未进行破坏性复现。
- **预期/最小修复验收**：默认创建唯一新目录；显式目录已存在时直接拒绝，或先验证其完整路径位于专用临时根且带本次唯一前缀；不得递归删除用户指定已有目录。用 sentinel 做有界测试，证明仓库外已有目录不被触碰。证据等级 B（源码审计）。

## 其它当前结论

1. **没有把未提供能力自动列为框架缺陷。** `IArchetypeRegistry` 是查询/应用接口，没有既定 allocator 契约；完整 talent 分配、激活、撤销和持久化需要另行定义 owner 与契约。离散召唤跳过是当前时间模型边界，连续跟随已有机制；escort 没有既定 route provider 接口，宿主自行驱动窄 API。
2. **已实现与默认接线分开。** `FindUnits` 已通过 `ISpatialQuery.QueryShape` 实现；Quest/Dialog/Economy/Item 等定义 reload 入口存在但生产驱动按宿主选择；热重载是模板 Editor/Development 工具，监听实际 StreamingAssets 内容根，源目录变更需 `-SyncContent`。
3. **schema 门禁载体分开。** `SchemaAudit` 做结构登记自洽检查；模块 coverage tests 做运行时注册集合对账。当前 check 的 63 tables/783 fields/0 errors/0 warnings 只证明前者；不能将命令行 audit 当成全部 runtime registration 证明。
4. **本轮没有以 `-SkipUnity` 宣称完整发布认证。** Unity 编译、EditMode、PlayMode、独立版和 consumer 步骤均在本次命令中 SKIP；具体 Unity 结果由 presentation 证据套件单独界定。

## 建议顺序

先决定 ABI 兼容路线并使门禁可判定，再修复门禁输出目录安全边界；随后按可选模块契约分别补充 runtime/consumer 证据。具体游戏内容、策略、编辑器 UI 和非目标系统不应因本次框架门禁结果被自动排成框架修复任务。


## CORE114-01：QuestHost reload 后目标数组越界（P2）

- **契约/owner/条件**：启用 Gameplay QuestHost 后，活跃任务 reload 定义仍须能安全处理后续进度；框架 owner。
- **实际**：旧定义一条 kill 目标、已有进度，reload 同 id 为两条目标，再更新新目标 index 1，`IndexOutOfRangeException`。
- **预期/验收**：迁移/调整旧计数数组，或原子拒绝不兼容 reload；目标增加、减少、重排均须有明确结果，不能让合法更新越界。
- **证据**：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\quest\core\QuestHost.cs:134`、`:226`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\CoreBoundaryProbe.current-v5.log`；独立 probe，等级 A。

## CORE114-02：Economy none→timer 遗留 null timer（P2）

- **契约/owner/条件**：启用 EconomyHost 后，既有 vendor/item reload 为 timer 定义必须建立可用 timer；框架 owner。
- **实际**：旧 none 状态库存耗尽为 0，reload timer 后既有 host 的 timer 仍 null，Update 后仍 0；fresh host 对照可从 0 补回 2。
- **预期/验收**：定义转换初始化/重采样 timer，明确 none/on-map-enter/timer 转换及已消费库存守恒；旧 host 到期应按新 `stock_limit=2` 恢复。
- **证据**：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\economy\core\EconomyHost.cs:103`、`:555`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\CoreBoundaryProbe.current-v5.log`；等级 A。

## CORE114-03：StatHost 派生缓存未随 reload 一致失效（P2）

- **契约/owner/条件**：启用 StatHost 后，未显式覆盖的派生值下一次访问应使用新 definition；框架 owner。
- **实际**：两个相同 unit 在 reload 前后，先访问的 A 查询缓存保持 0，B 得 77；两者 `GetBase` 都为 77，说明 derived cache 与定义分叉。
- **预期/验收**：失效 derived cache，或显式记录 base 与 derived 的区别并提供一致重算；未显式覆盖的 A/B 都应为 77，显式 runtime base 才保留差异。
- **证据**：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\numbers\stat_block\core\StatHost.cs:73`、`:264`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\CoreBoundaryProbe.current-v5.log`；等级 A。

## CORE114-04：ExprValueJson 非法 Id 使正式校验冒泡异常（P2）

- **契约/owner/条件**：正式 ContentValidationAssembly 对 Quest world flag value 的错误形状必须产生可定位报告；框架 validator owner。
- **实际**：`{"$id":"BAD"}` 进入 `ExprValueJson.IsValid` 时冒泡 `ArgumentException`，Quest 正式链没有把异常转换为 report。
- **预期/验收**：`IsValid` 捕获非法 Id（或先 `Id.TryParse`），正式 Quest/Dialog validation 输出 blocking 字段错误；合法 Bool/Number/String/Id 仍通过。
- **证据**：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\common\contracts\ExprValueJson.cs:16`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\QuestWorldFlagValueBoundaryProbe.current-v3.log`；等级 A。

CORE probe 的 Gobj/Quest 正向、Skill cache 正向和旧 CORE/UI/TP 回归均保持各自报告边界；Equipment set cache 只有静态候选，本轮未列 confirmed P2。



## P3 与静态候选（不与 6 项 P2 混计）

- **ADR-0019 门禁载体文案漂移**：`SchemaAudit` 做结构登记自洽，模块 coverage tests 做 runtime 注册集合对账；两者职责不同。当前实现有 coverage 载体，问题是文档需要精确同步。
- **Periodic `scaling_stat` metadata**：字段应保持可选 `Reference(stat.definition)`，且与非周期效果的结算消费一致；这是 schema/说明一致性核对，不把游戏数值策略列为缺陷。
- **WorldMap 点用途边界**：`PointItemSchema` 已有部分结构与首个 spawn position 规则；命名点/跨图引用的 required 性按具体 parser/引用规则判定，不要求所有内容点填满字段。旧索引/顶部注释需避免“仅数组类型”或“所有点必填”的过期解读。
- **Equipment set cache**：存在静态审查候选，但本轮没有完成合法 item.template 引用下 resident equip→reload→unequip 的独立负例，不能列确认 P2。

这些文案更新位置、建议措辞和是否需代码先修均列在矩阵的“需要更新的文档/元数据清单”。

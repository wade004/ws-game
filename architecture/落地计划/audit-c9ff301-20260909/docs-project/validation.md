# ws-game 1.13.0 验证与证据边界

基线为恢复仓 `D:\workespace\ws-game-artifacts\audit-c9ff301-resumed`、HEAD `c9ff30107413083188c597c0b65cf1691c9dfe9b`、`VERSION=1.13.0`。原仓 `D:\workespace\ws-game` 只读；未启动 registry、未 publish、未修改产品或测试。本轮是责任与报告重审，复用已保存日志/XML/probe，不重新运行测试。

## 历史 headless 门禁

以下是此前已保存的一次 `check.ps1 -SkipUnity`，不是本轮重新执行。原命令使用已被外部清理的旧 review worktree，完整命令和日志路径按原样保留：

`powershell -NoProfile -ExecutionPolicy Bypass -File D:\workespace\ws-game-review-c9ff301\check.ps1 -SkipUnity -ArtifactsPath D:\workespace\ws-game-review-c9ff301\architecture\落地计划\audit-c9ff301-20260909\docs-project\check-artifacts -LogFile D:\workespace\ws-game-review-c9ff301\architecture\落地计划\audit-c9ff301-20260909\docs-project\check-skipunity.log`

完整输出：[check-skipunity.log](check-skipunity.log)，exit：[check-skipunity.exit.txt](check-skipunity.exit.txt) = `0`。恢复仓命令仅作为可执行重现入口，不伪称为本轮历史执行：

`powershell -NoProfile -ExecutionPolicy Bypass -File D:\workespace\ws-game-artifacts\audit-c9ff301-resumed\check.ps1 -SkipUnity -ArtifactsPath D:\workespace\ws-game-artifacts\audit-c9ff301-resumed\architecture\落地计划\audit-c9ff301-20260909\docs-project\check-artifacts -LogFile D:\workespace\ws-game-artifacts\audit-c9ff301-resumed\architecture\落地计划\audit-c9ff301-20260909\docs-project\check-skipunity.log`

| 检查 | 已保存结果 | 证据口径 |
|---|---:|---|
| self-check / guard | PASS | headless 门禁 |
| Core.sln Release build | PASS，0 warning/0 error | 重建/编译证据，不是正式 ZIP 身份 |
| 6 个 Core 测试程序集 | 2774 passed / 0 failed / 0 skipped | 模块单元/集成回归 |
| validate_data merged | PASS，60 tables / 288 records / 0 errors / 3 warnings / 1 override | warning/override 单独保留 |
| validate_data framework | PASS，5 tables / 124 records / 0 errors / 1 warning | warning 单独保留 |
| `schema-audit` | PASS，63 tables / 781 fields / 0 errors / 0 warnings | 结构递归检查，不独立证明 runtime 集合 |
| event constants / placeholder / import assets | PASS，90 / 92/92 / 10 display + 3 vfx + 3 sfx + 1 world | 框架资产/常量门禁 |
| toolchain pytest | PASS，98 passed / 2 skipped | 工具链单测 |
| version/package consistency | PASS | 版本/包元数据 |
| build `-SkipTests` sync / package consistency | PASS；四包均 1.13.0 | 不运行 registry/publish |
| Unity/standalone/IL2CPP/consumer smoke | SKIP 或未在此命令执行 | `-SkipUnity` 的显式边界 |
| 门禁汇总 | **15 PASS / 6 SKIP / 0 FAIL** | 不把 21 项读成全通过 |

11 工程规范变更记录和 §8 规定完整门禁还包含 consumer 演练；13:150 的 `consumer_smoke` 是独立最小模板工程，验证基础设施，不验证某一游戏的技能、战斗或任务。IL2CPP/发布形态是可选快照门槛。本轮复用的 headless 数字不能扩大为完整发布认证。

## 历史 Runtime 与 Core 证据

| 证据 | 已保存事实 | 责任限制 |
|---|---|---|
| Core three | CORE-111-01、UI-111-01、TP-111-01 probe 通过 | 框架/独立 host 语义；不要求具体游戏采用相同装配 |
| Unity full | EditMode 70/70（69 基线 + 1 Deleted 诊断），PlayMode 270/270 | 外部 Unity sample/template 路径；诊断 test 通过是捕获缺陷 |
| Unity NAV/SPATIAL | NAV 2 项、SPATIAL 4 项真实 oracle 通过 | 不证明跨帧预算或全索引性能，因为 02 已收窄为边界 |
| Changed-file hot reload | template PlayMode 正向 reload 通过 | 只覆盖启用模板的 changed-file 路径，不证明 resident SkillHost cache 或 Deleted |
| SkillHost cache | existing host 7/0，fresh host 99/5 | 独立 .NET fake combat host；base value 不是 HP damage |
| Deleted override | after delete 仍为 2，manual reload 回到 1 | EditMode 诊断 test 1/1 Passed 代表抓到缺陷 |
| ExistingView | correct fixture 0/1：equipment=0，既有 socket child=1 | 只约束启用装备表现且已有 View 的可选契约 |
| Quest/Gobj | formal report 先 0 error，后分别 FormatException/DataFieldException | 负例证明 gate/parser 不一致；正向合法值另有通过记录 |

基线回归和新增语义失败分开：全量测试的通过数不吸收 six P2 负例；Deleted 诊断虽在 EditMode 结果中 Passed，含义是捕获 watcher 缺陷；ExistingView 是独立 0/1 失败。P2 由各自 oracle 的结果确认。

## 正式发行物与重建物

正式发行物仅读取原仓：

- `dist/ws-game-1.13.0.zip`：53,075,472 bytes，SHA-256 `cbe6261b35c1a6f4b418c28a4809dc45f283f25021f92df64a86cc5fb4f3469f`。
- `dist/ws-game-1.13.0.lock`：912 bytes，SHA-256 `82381043d3c8644ca1f4fbf5128b3e11362d36ed35e853281f89682d58c69ddb`，`git_commit=609cec4`。
- ZIP 有 1053 entries；七个 Core/Presentation/Stub DLL 以精确 entry 流 hash 与 lock 全部匹配；四个 package manifest 精确版本均为 1.13.0。

`verify_release_zip.py`（原件未随本次归档提交到本仓库——只在审计执行时的 review worktree 内使用，见 `toolchain/tests/.linkcheck-ignore` 说明）的恢复版只读取 `--repo` 指定的正式 ZIP，缺 entry/hash/version 会非零退出。磁盘解包目录 hash 是另一层历史核验；check-artifacts 重建 DLL 只证明冻结源码可构建，不能当正式 ZIP 运行证明。

## ABI 证据

材料：[api-compat.log](api-compat/api-compat.log)、[api-compat-repro.log](api-compat/api-compat-repro.log)、[run-api-compat.ps1](api-compat/run-api-compat.ps1)、[Program.cs](api-compat/Program.cs)、[FieldSchemaConsumer.csproj](api-compat/FieldSchemaConsumer.csproj)。此前结果为：正式 1.12 DLL 编译/运行 exit 0；只换正式 1.13 `Core.Foundation.dll` 不重编译，出现 `MissingMethodException`。旧七参数 ctor 与五个删除的 public rule 类型必须保留 façade 或逐项迁移；源码重编译可用可选参数不等于二进制兼容。

## 可复现命令与限制

在一次性解压的 `framework-scope-review.zip` 副本中运行，避免覆盖本目录保存的 raw log/XML：

```powershell
$AuditRoot = 'D:\workespace\ws-game-scope-review-unpacked'
$FrozenRepo = 'D:\workespace\ws-game-artifacts\audit-c9ff301-resumed'
Set-Location $AuditRoot
pwsh -File .\run-probes.ps1 -RepoRoot $FrozenRepo -FrameworkRoot (Join-Path $AuditRoot 'rebuild')
pwsh -File .\docs-project\api-compat\run-api-compat.ps1 -ReleaseRepo D:\workespace\ws-game
python .\docs-project\verify_release_zip.py --repo D:\workespace\ws-game
```

这些命令是复现入口，不是本轮重跑声明。证据包不携带产品源码镜像、bin/obj、Unity Library 或 DLL/PDB/EXE；源码结论以冻结仓链接、行号和代理报告为准。

# ws-game 1.16.1 深度审计归档（codex 第十六轮，基线 24a11fe）

本目录归档 codex 第十六轮外部深度审核（`D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\`）的
证据与报告原件。权威基线是主检出提交 `24a11fe28f9647cd532c41f56f7ab18c00fb8516`（`VERSION=1.16.1`）；
原始输出根在归档提交后仍保留于 `D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\`，本目录是其
可长期随仓库留存的子集副本。

## 原件未归档

以下路径在原始审计输出根中存在，但按体积/重复内容/构建产物原则未纳入本次归档（本目录内没有任何
md 文件以相对链接指向被排除的路径本身；docs-project/、abi-gate-negative-oracle-rerun*/ 内引用到的
具体文本证据文件——如 `summary.txt`、`*.log`、`surface-*.txt`——均已选择性保留，只排除同目录下的
`bin`/`obj`/`*.dll`/`*.exe`/`*.pdb`/`*.deps.json`/`*.runtimeconfig.json`）：

| 路径（相对原始输出根） | 大小 | 原因 |
|---|---|---|
| `audit-24a11fe-20260910-final.zip`、`audit-24a11fe-20260910-safe.zip` | 各约 768KB | 本目录内容的便携压缩副本，与已展开归档的文件逐字节重复，保留一份即可 |
| `check/`（`check/artifacts/bin`\|`obj`\|`unity` 三类构建/Unity 中间产物） | 约 53MB | `check.ps1 -SkipUnity` 生成的构建中间物，不是证据原件；权威结果已由本目录 `check/check-skipunity-transcript.txt`（原 `logs/check-skipunity-transcript.txt`）与 `docs-project/check-summary-count.txt` 文字记录 |
| `core/build/` | 约 45MB | `CoreBoundaryProbe` 独立 probe 历次重跑的 bin/obj 构建中间产物，非源码/证据；权威结果见本目录 `core/core-findings.md` 及其链接的 `core/repro/`、`core/logs/`、`core/evidence/` |
| `schema/build/`、`schema/runner-check/`、`schema/runner-check2/`、`schema/runner-check3/`、`schema/runner-test2/`、`schema/runner-test3/` | 共约 39MB | 历次重跑各自的 bin/obj 构建产物 + 与 `schema/repro/` 内容重复的 repro 源码副本；本目录只保留一份规范的 `schema/repro/`（已排除其中的 `obj/`）与 `schema/raw/` 原始输出 |
| `docs-project/abi-gate-negative-oracle/`、`docs-project/abi-strict-1.12/`、`docs-project/validate-precompiled-probe/` 三个子目录内的 `bin`/`obj`/`*.dll`/`*.exe`/`*.pdb` | 共约 11MB | 同目录下的构建中间产物；三个子目录本身连同其余文本/源码文件（`*.ps1`/`*.cs`/`*.csproj`/`*.txt`/`*.log`/`*.json` 非缓存文件）已保留，`docs-project/README.md`/`validation.md` 引用到的具体证据文件均可解析 |
| `abi-gate-negative-oracle-rerun{,2,3}/` 三个目录内的 `bin`/`obj`/`*.dll`/`*.exe`/`*.pdb` | 共约 2.4MB | 同上，同目录下的复现源码（`ApiBaseline`/`ApiCurrent`/`Consumer`/`toolchain/abi_surface` 源码）、脚本与日志/dump/report 全部保留，只排除构建中间产物 |
| 各 `__pycache__/`、`*.pyc` | 数 KB | Python 字节码缓存，非源码/证据 |
| `abi-gate-negative-oracle-rerun{,2,3}/build/{baseline,current}/ApiContract.deps.json`、`abi-gate-negative-oracle-rerun3/abi_surface_tool/AbiSurface.{deps.json,runtimeconfig.json}`、`docs-project/abi-gate-negative-oracle/build/{baseline,current}/ApiContract.deps.json`、`docs-project/abi-strict-1.12/abi_surface_tool/AbiSurface.{deps.json,runtimeconfig.json}`、`docs-project/abi-strict-1.12/formal-zip-consumer/AbiProbeConsumer.{deps.json,runtimeconfig.json}` | 共 14 个文件，数十 KB | 整合时复核发现：上一轮归档已排除同目录 `bin`/`obj`/`*.dll`/`*.exe`/`*.pdb`，但遗漏了同属构建产物的 `*.deps.json`/`*.runtimeconfig.json`（dotnet 发布/运行时清单，非源码/证据，内容可由同目录保留的 `*.csproj` 与构建日志复现）；整合时一并剔除，剔除后产生的空目录（`build/baseline`、`build/current`、`abi_surface_tool`、`formal-zip-consumer` 等）一并删除 |

原始输出根本身（含上述未归档路径）不受本次归档影响，仍按其自身生命周期保留在
`D:\workespace\ws-game-artifacts\` 下。归档时用统一规则筛选：排除任何 `bin`/`obj` 目录、
`*.dll`/`*.exe`/`*.pdb`/`*.pyc` 文件、单文件超过 2MB 的文件，其余全部按原始相对路径保留。

## 先读

- [AUDIT_REPORT.md](AUDIT_REPORT.md)：总报告，本轮结论（ADR 索引/Skill README/DataRegistry
  README/ADR-0022 措辞四项文档 finding + ABI-116-01 门禁覆盖 finding + F-01/F-02/F-03 框架数据/
  表达式合同 finding）、问题汇总表、责任边界、证据入口。
- [docs-project/README.md](docs-project/README.md)、
  [docs-project/doc-code-matrix.md](docs-project/doc-code-matrix.md)、
  [docs-project/project-findings.md](docs-project/project-findings.md)、
  [docs-project/validation.md](docs-project/validation.md)：文档—代码矩阵、每项 finding 的 contract/
  owner/条件/actual/expected/复现与最小验收、一次 `check.ps1 -SkipUnity`（17 PASS/7 SKIP）与正式
  ZIP/lock/hash/consumer 复现记录。
- [schema/schema-findings.md](schema/schema-findings.md)：F-01（非有限 Number 放行）、F-03（Expr
  整数溢出外抛）等框架数据/表达式合同 finding 的判断记录。
- [core/core-findings.md](core/core-findings.md)：CORE114-01～04 历史 P2 复测结果（均未复现）。
- [presentation/presentation-findings.md](presentation/presentation-findings.md)：Unity 侧当前
  1.16.1 独立复核记录（EditMode 70/70、PlayMode 272/272）。
- [followup-2026-09-10c.md](followup-2026-09-10c.md)：本轮修复跟进核实表（本次归档新增，
  ABI-116-01/DOC-116-01～04/TOOL-116-01 由本 agent 落地；F-01～03 由并行执行的另一位 agent 落地，
  提交哈希由整合 agent 回填）。

## 目录结构

```
AUDIT_REPORT.md                总报告
README.md                      本文件
baseline.txt                   冻结 HEAD/VERSION 记录
followup-2026-09-10c.md        本轮修复跟进核实（本次归档新增）
check/                         check.ps1 -SkipUnity 的 transcript 与计数汇总（原始产物在 bin/obj/
                                unity 之外的部分）
logs/                          ABI 相关 raw 日志（negative oracle 各轮重跑、1.12 strict、正式包流式核验）
docs-project/                  文档—代码矩阵、findings、scope、validation、ABI/validator 复现工程
                                （已排除 bin/obj/DLL）与日志
schema/                        schema-findings、scope-coverage、规范 repro 源码（已排除 obj/）与
                                raw 原始输出
core/                          core 代理报告（findings/coverage/scope-review）、独立 probe 源码
                                （core/repro）、日志（core/logs）与哈希证据（core/evidence）
presentation/                  presentation/Unity 代理报告、独立 XML/log/hash、repro 源码
abi-gate-negative-oracle-rerun{,2,3}/
                                ABI-116-01 negative oracle 三轮独立复现（源码 + 日志 + dump/report，
                                已排除构建中间产物）
```

## 重现原则（与原始输出根一致，转录自 [../audit-76d16a5-20260910/README.md](../audit-76d16a5-20260910/README.md) 同类归档惯例）

原始日志/XML 和证据目录保持不可变，本归档同样不回头改写已复制的原始日志/XML 内容（除本 README 与
新增的 followup 文档外）。任何重跑都应在新的临时目录进行，不直接在本目录或原始输出根执行会覆盖日志
的 runner；具体重现命令见 [AUDIT_REPORT.md](AUDIT_REPORT.md) 与
`docs-project/README.md`"重跑命令"相关小节。

## 证据边界

`-SkipUnity` 结果不是完整发布认证。Unity 代理独立保存的当前 1.16.1 验证为 EditMode 70/70、PlayMode
272/272，未由本次 SkipUnity 命令证明；Standalone、IL2CPP 与 consumer rehearsal 未在本轮 `-SkipUnity`
检查中执行，分别保留为 SKIP 边界（见 AUDIT_REPORT.md"当前状态"一节）。ABI-116-01 是"门禁覆盖"
finding——最小 oracle 证明的是"public→protected 这类可见性收窄在旧版工具下漏报"，不代表 1.16.1
当前六个 DLL 已经发生过这类破坏；本轮用修复后的新版 `toolchain/abi_surface` 对 1.12.0/1.13.0 两份
历史基线重跑当前工作树，结果仍为 `breaks=0`，未发现历史真实破坏（见根目录
`toolchain/README.md`"`abi_surface` dump/compare 覆盖范围"一节）。

旧 1.11～1.15 审计 ZIP/candidate 只作历史输入；旧 raw 与结论不覆盖当前 1.16.1。

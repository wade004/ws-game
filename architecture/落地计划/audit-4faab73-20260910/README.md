# ws-game 1.16.2 深度审计归档（codex 第十七轮，基线 4faab73）

本目录归档 codex 第十七轮外部深度审核（`D:\workespace\ws-game-artifacts\audit-4faab73-20260910\`）的
证据与报告原件。权威基线是主检出提交 `4faab73e7081f2984e7addb88fee051b6c0d3d02`（`VERSION=1.16.2`）；
原始输出根在归档提交后仍保留于 `D:\workespace\ws-game-artifacts\audit-4faab73-20260910\`，本目录是其
可长期随仓库留存的子集副本。

## 原件未归档

以下路径在原始审计输出根中存在，但按体积/重复内容/构建产物原则未纳入本次归档：

| 路径（相对原始输出根） | 大小 | 原因 |
|---|---|---|
| `archive/` | 未量 | 归档 zip 便携副本，与已展开归档的文件逐字节重复，保留一份即可（按任务边界排除） |
| `auditRoot/` | 数 GB | 冻结检出根+正式包+Unity 工程等构建/验证工作区，是本轮全部产物的生成地，不是证据原件；权威结果已由本目录各处 raw 日志/manifest/md 文字记录 |
| `archive-audit.ps1` | 18KB | 原始输出根自带的证据封存脚本，作用域覆盖整个原始输出根（含本目录未纳入的 `archive/`/`auditRoot/`），不在本轮 AUDIT_REPORT.md/ISSUES.md/README.md/docs/validation/validation-rerun/delivery 六项归档范围内 |
| `validation/build/`、`validation-rerun/build/` | 各约 12MB | `pconsumer/`（六个核心 DLL + `.pdb`/`.deps.json` 编译产物）与 `source/`（构建这些 DLL 所需的完整 `core/` 源码镜像，逐字节等同于仓库本身在该提交下的源码，不是独立证据）；权威复现依据是本目录保留的 `validation/repro/`（探针源码：`PresentationConsumer.csproj`/`.Program.cs`/`prepare-source.ps1`/`run-presentation-consumer.ps1`/`verify-archive.py`）与 `validation/raw/`、`validation-rerun/raw/` 原始日志 |
| `validation/repro/obj/` | 数百 KB | `dotnet build` 中间产物（`project.assets.json`、`*.cache`、`bin/obj` 下的 `.dll`/`.pdb`/`apphost.exe`），非探针源码本身 |
| `delivery/output/build/`（`check/bin`\|`obj` 六个工程） | 数十 MB | `check.ps1` 全量步骤重新编译产生的构建中间物，非证据原件；门禁结果已由本目录 `delivery/output/raw/check.log` 文字记录（3045 .NET/176 Python/4 skipped、17 PASS/7 SKIP） |
| `delivery/output/unity-copy/` | 约 1.6GB | 供 Unity EditMode/PlayMode 证据运行使用的完整仓库镜像副本（`adapters`/`assets`/`data`/`games`），与仓库本身内容重复，不是独立证据；权威结果是本目录保留的 `delivery/output/unity-evidence2/full-edit.xml`（EditMode 70/70 Passed）、`full-play.xml`（PlayMode 272/272 Passed）及同目录 `logs/`、`*.exit.txt` |
| `delivery/output/unity-evidence/`（不带 `2`） | 约 300KB | 早于 `unity-evidence2` 的一次不完整中间产出（只有 `full-edit.xml`，缺 `full-play.xml`），已被 `unity-evidence2` 完整覆盖取代，保留会与权威结果重复 |
| `delivery/output/abi-probe/`、`delivery/output/formal-zip/` 及各 `delivery/run{2..9}/output/formal-zip/`、`delivery/run{2..9}/output/indexer-oracle/` | 共数十 MB | consumer/lib 编译产物（`.dll`/`.pdb`/`.exe`）与逐轮复现探针源码（`ApiBaseline`/`ApiCurrent`/`Consumer`/`IndexerBaseline`/`IndexerCurrent` 等）——按任务边界，`delivery/` 本轮只归档日志与清单，不归档源码或二进制；逐轮判定结果已由对应 `delivery/run{N}/output/raw/*.log`\|`*.txt` 与顶层 `delivery/run{N}-console.log` 完整保留 |
| `delivery/reproduce-formal-and-indexer.ps1`、`delivery/unity-repro/*.ps1` | 数十 KB | 复现脚本本身，不属于本轮"日志与清单"归档范围；具体调用方式与参数见 `delivery/delivery.md` |
| `delivery/run2/output/raw/` | 0 字节（空目录） | 该轮次在到达"输出 raw 日志"阶段前已终止（早期尝试），原始输出根内本就是空目录，无内容可归档 |

原始输出根本身（含上述未归档路径）不受本次归档影响，仍按其自身生命周期保留在
`D:\workespace\ws-game-artifacts\` 下。归档时用统一规则筛选：任务范围限定为 `AUDIT_REPORT.md`/
`ISSUES.md`/`README.md`（本文件）/`docs/`（全部）/`validation/` 与 `validation-rerun/` 的
md/日志/探针源码子集/`delivery/` 的日志与清单子集；在此范围内进一步排除任何 `bin`/`obj` 目录、
`*.dll`/`*.exe`/`*.pdb`/`*.deps.json`/`*.runtimeconfig.json` 文件、单文件超过 2MB 的文件；
PowerShell 默认 UTF-16LE 编码的日志文件已重新编码为 UTF-8，全部文本文件行结束符统一转换为 LF。

## 先读

- [AUDIT_REPORT.md](AUDIT_REPORT.md)：总报告，本轮结论（4 条 P2：V-01/V-02/V-03/ABI-1162-01 +
  5 条独立 P3 文档问题：DOC-162-01～05）、问题汇总表、能力边界与责任、验证与发布证据、覆盖与
  未读范围。
- [ISSUES.md](ISSUES.md)：按优先级排列的问题清单与"不计入问题的边界"归类表。
- [docs/doc-code-matrix.md](docs/doc-code-matrix.md)、[docs/docs-findings.md](docs/docs-findings.md)：
  architecture 00–14/ADR 0001–0023/模块 README 的文档—代码矩阵与当前文档漂移逐条证据。
- [validation/validation.md](validation/validation.md)：V-01（`schema_version` 32 位回绕）、
  V-02（`CurrencyPersistable` long 精度）、V-03（`JsonNumber.TryGetInt64` 上界）及旧
  F-01/F-02/F-03 复测的定向验证记录，配 [validation/raw/](validation/raw/) 原始日志与
  [validation/repro/](validation/repro/) 探针源码。
- [delivery/delivery.md](delivery/delivery.md)：整库 `check.ps1`、正式包、旧 consumer、indexer
  oracle 与 Unity 证据的入口说明；配 [delivery/output/raw/](delivery/output/raw/)（`check.log`、
  ABI 探针日志、哈希清单等）、[delivery/output/unity-evidence2/](delivery/output/unity-evidence2/)
  （Unity EditMode/PlayMode 结果 XML）与各 `delivery/run{2..9}-console.log`／
  `delivery/run{2..9}/output/raw/`（indexer oracle 逐轮复现日志与 hash 清单）。
- [followup-2026-09-10d.md](followup-2026-09-10d.md)：本轮修复跟进核实表（本次归档新增，
  ABI-1162-01/DOC-162-02/03/04 与本节归档由本 agent 落地；V-01/V-02/V-03/DOC-162-01/05 由并行
  执行的其余 agent 落地，提交哈希由整合 agent 回填）。

## 目录结构

```
AUDIT_REPORT.md                总报告
ISSUES.md                      问题清单
README.md                      本文件
followup-2026-09-10d.md        本轮修复跟进核实（本次归档新增）
docs/                          文档—代码矩阵、文档漂移 findings
validation/
  validation.md                 V-01/V-02/V-03 与旧 F-01/F-02/F-03 定向验证记录
  raw/                          原始日志（presentation-consumer 五次尝试 + rerun）与
                                 final-baseline.txt（主仓/冻结根/正式包/Unity 进程快照）
  repro/                        探针源码（PresentationConsumer 工程 + prepare/run/verify 脚本，
                                 已排除 obj/ 构建产物）
validation-rerun/
  raw/                          rerun 的 presentation-consumer 原始日志
delivery/
  delivery.md                   证据入口说明
  run{2..9}-console.log          顶层逐轮控制台日志
  output/raw/                    check.log、ABI 探针日志、正式包/Unity 拷贝哈希清单等
  output/unity-evidence2/        Unity EditMode（full-edit.xml/log）与 PlayMode
                                 （full-play.xml/log）结果，含退出码 *.exit.txt
  run{2..9}/output/raw/          各轮 indexer oracle 复现的日志与 hash 清单
```

## 重现原则（与原始输出根一致，转录自
[../audit-24a11fe-20260910/README.md](../audit-24a11fe-20260910/README.md) 同类归档惯例）

原始日志/XML 和证据目录保持不可变，本归档同样不回头改写已复制的原始日志/XML 内容（除本 README 与
新增的 followup 文档外）。任何重跑都应在新的临时目录进行，不直接在本目录或原始输出根执行会覆盖日志
的 runner；具体重现命令见 [AUDIT_REPORT.md](AUDIT_REPORT.md) 与 `delivery/delivery.md`。

## 证据边界

本轮覆盖未确认"完全缺失且已承诺"的通用 API；不声称逐行全仓审计、全 API 无缺失或全发布认证。
Standalone、IL2CPP、独立版 smoke、独立 Unity 消费方仍按 `AUDIT_REPORT.md`"验证与发布证据"/
"覆盖与未读范围"两节的证据边界处理。ABI-1162-01 是"门禁覆盖"finding——最小 oracle 证明的是
"public indexer 索引参数类型变化在旧版工具下漏报"，不代表 1.16.2 当前六个核心 DLL 已经发生过这类
破坏；本轮用修复后的新版 `toolchain/abi_surface` 对 1.12.0（完整探针）、1.13.0（dump/compare）两份
历史基线重跑当前工作树，结果均为 `breaks=0`，未发现历史真实破坏（见根目录 `toolchain/README.md`
"`abi_surface` dump/compare 覆盖范围补齐"一节）。

旧 1.11～1.16.1 审计 ZIP/candidate 只作历史输入；旧 raw 与结论不覆盖当前 1.16.2。

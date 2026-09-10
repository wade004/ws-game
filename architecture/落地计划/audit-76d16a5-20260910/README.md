# ws-game 1.14.0 深度审计归档（codex 第十五轮，基线 76d16a5）

本目录归档 codex 第十五轮外部深度审核（`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\`）的
证据与报告原件。权威基线是冻结 detached worktree
`76d16a54e54f0f11204d97d7460563c8a0dc8cd8`（`VERSION=1.14.0`）；原始输出根在归档提交后仍保留于
`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\`，本目录是其可长期随仓库留存的子集副本。

## 原件未归档

以下路径在原始审计输出根中存在，但按体积/重复内容原则未纳入本次归档（本目录内没有任何 md 文件以
相对链接指向它们，未触发链接检查失效）：

| 路径（相对原始输出根） | 大小 | 原因 |
|---|---|---|
| `build/` | 约 77–83MB（`abi-skillhost/`+`check-artifacts/`+`core/` 三个子目录，含 `bin`/`obj` 构建中间产物） | `check.ps1`/ABI 探针生成的构建中间物，不是证据原件，体积远超文档归档合理范围；权威结果已由本目录内的日志/报告文字记录（`check/check-1.14.0-skipunity.log`、`logs/release-formal-1.14.0.log` 等） |
| `framework-scope-review.zip` | 约 487KB | 本目录内容的便携压缩副本，与已展开归档的文件逐字节重复，保留一份即可 |
| 各 `*/obj/` 子目录（如原 `core/repro/obj/`） | 数十 KB～数百 KB | `dotnet` 编译期生成的 NuGet 恢复缓存，非源码/证据 |

原始输出根本身（含 `build/`）不受本次归档影响，仍按其自身生命周期保留在
`D:\workespace\ws-game-artifacts\` 下。

## 先读

- [AUDIT_REPORT.md](AUDIT_REPORT.md)：总报告、六项 P2（PJ114-01/02、CORE114-01～04）+ 一项静态工具
  风险（PJ114-03）+ P3、交付门禁结果、正式包核验、建议顺序。
- [docs-project/doc-code-matrix.md](docs-project/doc-code-matrix.md)：architecture 00–14、ADR、能力
  索引、模板/module README 与源码责任对照，末尾"需要更新的文档/元数据清单"是本轮文档同步的权威
  依据。
- [docs-project/project-findings.md](docs-project/project-findings.md)：每项 finding 的 contract、
  owner、条件、actual、expected、复现和最小验收。
- [docs-project/scope-and-evidence.md](docs-project/scope-and-evidence.md)：框架通用契约、可选模块、
  宿主/游戏责任、未提供能力、非目标与证据限制。
- [docs-project/validation.md](docs-project/validation.md)：一次 `check.ps1 -SkipUnity`、正式
  ZIP/lock/hash 和 SkillHost consumer 复现。
- [evidence-index.md](evidence-index.md)：原始日志、脚本、源码定位和归档限制。
- [core/core-findings.md](core/core-findings.md)：核心侧四项 CORE114 独立 probe 判断记录。
- [presentation/presentation-findings.md](presentation/presentation-findings.md)：表现/Unity 侧当前
  1.14 验证记录。
- [followup-2026-09-10b.md](followup-2026-09-10b.md)：本轮修复跟进核实表（本次归档新增，修复由并行
  执行的 WA/WB 两个 agent 落地，提交哈希由整合 agent 回填）。

## 目录结构

```
AUDIT_REPORT.md                总报告
README.md                      本文件
baseline.txt                   冻结 HEAD/VERSION/worktree 记录
evidence-index.md              证据索引
followup-2026-09-10b.md        本轮修复跟进核实（本次归档新增）
make-portable-archive.ps1      归档打包脚本（原件）
check/                         一次 check.ps1 -SkipUnity 的完整 transcript 与 exit
logs/                          正式包核验日志
hashes/                        六 DLL / StreamingAssets / tracked cs 的哈希清单
docs-project/                  文档—代码矩阵、findings、scope、validation、ABI 复现工程与日志
core/                          core 代理报告、独立 probe 源码/日志/evidence/coverage
presentation/                  presentation/Unity 代理报告、独立 XML/log/hash、repro 源码
```

## 重现原则（与原始输出根一致，转录自 [README.md](../audit-c9ff301-20260909/README.md) 同类归档
惯例）

原始日志/XML 和证据目录保持不可变，本归档同样不回头改写已复制的原始日志/XML 内容（除本 README 与
新增的 followup 文档外）。任何重跑都应在新的临时目录进行，不直接在本目录或原始输出根执行会覆盖日志
的 runner；具体重现命令见 [AUDIT_REPORT.md](AUDIT_REPORT.md)"建议顺序"与
[evidence-index.md](evidence-index.md)。

## 证据边界

`-SkipUnity` 结果不是完整发布认证。Unity 代理独立保存的当前 1.14 验证为 EditMode 70/70、PlayMode
272/272，未由本次 SkipUnity 命令证明；独立版、IL2CPP、consumer smoke 同样未覆盖。ABI 探针在冻结树
缺省 1.12.0 基线时按设计以 `SkipIfBaselineMissing=$true` 跳过，`check.ps1` 汇总行仍显示 PASS——这
是本轮 PJ114-02 指出的门禁判定问题本身，不是本归档新引入的解释偏差。

旧 1.13 及更早审计 ZIP/candidate 只作历史输入；旧 raw 与结论不覆盖当前 1.14。

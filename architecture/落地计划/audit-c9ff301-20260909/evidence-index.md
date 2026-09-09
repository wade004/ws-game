# 证据索引：框架范围复核

根目录：`D:\workespace\ws-game-artifacts\audit-c9ff301-resumed\architecture\落地计划\audit-c9ff301-20260909`。冻结基线为 `c9ff30107413083188c597c0b65cf1691c9dfe9b`，版本 `1.13.0`。

## 权威入口

| 路径 | 用途 | 证据性质 |
|---|---|---|
| `AUDIT_REPORT.md` | 总结框架与游戏边界、六项条件 P2、历史验证和发布判断 | 解释汇总 |
| `docs-project/scope-and-evidence.md` | 责任分类、契约矩阵、证据用途/限制、旧结论变更 | 本轮权威责任口径 |
| `docs-project/doc-code-matrix.md` | 00—14、ADR、能力索引和 metadata 更新 | 文档/源码对照 |
| `docs-project/project-findings.md` | P2/P3 的 owner、启用条件、oracle 和最小验收 | 条件 findings |
| `docs-project/validation.md` | 历史 check、Core/Unity、正式 ZIP/lock、ABI 数字与未重跑说明 | 门禁/证据边界 |
| `docs-project/document-inventory.log` | architecture、ADR、editor、README 清单与 hash | 输入范围 |

## 原始证据继承

本轮复核不重跑测试，保留并引用此前已保存的 raw log/XML/probe。旧正式审计包 `ws-game-1.13.0-c9ff301-deep-audit.zip`（SHA-256 `105bb7c09716cda3006e3e1c2c9cc0d7d6e0321b9b9de98941c0a0dc97609ee5`）中的 71 个 log/XML/cs/csproj/py/sha256/original/txt raw 文件与当时 live 证据逐字节一致；candidate 是另一个曾出现 stale 的历史输入，不能继承该 71 文件结论。报告只重写解释和链接，旧 ZIP 本身不改写。

| 路径 | 用途 | 限制 |
|---|---|---|
| `docs-project/check-skipunity.log`、`check-skipunity.exit.txt` | 一次历史 `check.ps1 -SkipUnity` 完整输出 | 15 PASS/6 SKIP/0 FAIL；不是本轮重跑，也不是 Unity/完整发布认证 |
| `docs-project/release-zip-lock-hash*.log`、`verify_release_zip.py` | 正式 ZIP 精确 DLL stream hash、四 manifest 版本 | 原仓正式发行物；不把重建 DLL当正式 ZIP |
| `docs-project/api-compat/*` | 1.12 old DLL → 1.13 formal DLL ABI consumer 结果及重现脚本 | 只覆盖 FieldSchema probe 和已对照的五个 public rule 删除 |
| `core/core-findings.md`、`core/logs/*`、`core/repro/*`、`core/evidence/source-copy-hashes.txt` | 独立 Core semantic probes、旧问题复测、源码 hash；`core-findings.md` 是当前权威 Core 报告 | pure .NET；exit 0 要结合 probe oracle 阅读 |
| `presentation/presentation-findings.md`、`presentation/*-final.xml/.log` | Unity full、NAV/SPATIAL、热重载和 ExistingView 结果；`presentation-findings.md` 是当前权威 Presentation 报告 | 外部 template/sample/Unity 路径；不泛化为所有游戏 |
| `presentation/DataHotReloadDeletedOverlayAuditTests.cs`、`ExistingViewEquipmentSaveLoadAuditTests.cs` | 两个负例的正确 oracle 与 fixture | Deleted test 通过表示抓到缺陷；ExistingView 为隔离 0/1 失败 |
| `presentation/fixture-*`、tracked hash logs | fixture 往返恢复与 117 个 tracked CS 文件一致性 | 复现输入，不是框架源码镜像交付 |

## 新范围包纳入边界

范围复核包使用新名 `framework-scope-review.zip`，不得覆盖旧 ZIP。新包包含上面的报告、scope、validation、matrix、findings、document inventory、raw evidence 引用，以及 `make-portable-archive.ps1` 生成的 manifest；同时纳入 core/presentation 两代理的当前权威报告和必要的独立 probe/runner 文本证据。

归档 allowlist 必须：

1. 包含 `AUDIT_REPORT.md`、`README.md`、`evidence-index.md`、`docs-project/scope-and-evidence.md`、四份 docs-project 主文档、`core/core-findings.md`、`presentation/presentation-findings.md` 和 raw evidence 日志。
2. 保留独立 probe 的 `.cs/.csproj`、runner、fixture 原始字节/hash；不复制产品源码镜像作为报告证据。
3. 排除旧 ZIP/candidate、`core`/`presentation`/`adapters/stub` 产品源码副本、Unity `Library`、`bin`、`obj`、`build`、临时 `dist`、repro-run，以及所有 DLL/PDB/EXE/deps/runtimeconfig 二进制产物。
4. 对包内每个文件生成 SHA-256 manifest，解压后逐 entry 检查 manifest、排除项和必需报告可读性。manifest 证明归档字节完整，不证明未重新运行的测试。

正式发行 ZIP/lock 的核验仍以 `D:\workespace\ws-game\dist` 原仓只读文件为准；冻结仓重建产物、旧范围包和新范围包是三种不同证据层。

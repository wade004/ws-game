# docs-project 交付索引

本目录是 `audit-24a11fe-20260910` 的文档/工具链审计子产物，不修改 `ws-game` 产品源码。

- [doc-code-matrix.md](doc-code-matrix.md)：architecture 00–14、ADR-0020/21/22 与当前代码/证据矩阵。
- [project-findings.md](project-findings.md)：需更新文档、ABI 门禁漏报和已核实边界。
- [scope-and-evidence.md](scope-and-evidence.md)：只读边界、责任分类、证据层级和总报告整合接口。
- [validation.md](validation.md)：check、严格 ABI、预编译 validator JSON、正式包流式核对记录。
- [check-summary-count.txt](check-summary-count.txt)：从 check transcript 汇总区机器计数（24 steps、17 PASS、7 SKIP）；由 `count-check-summary.ps1` 复现。
- [verify-formal-release.ps1](verify-formal-release.ps1)：正式 ZIP 流式 lock/DLL/manifest/hash 复现脚本。
- [abi-strict-1.12/formal-zip-abi-summary.txt](abi-strict-1.12/formal-zip-abi-summary.txt)：显式 1.12.0 ABI baseline、1.16.1 正式 ZIP DLL consumer probe。
- [abi-gate-negative-oracle/summary.txt](abi-gate-negative-oracle/summary.txt)：public→protected surface 门禁漏报的最小复现结果；源码和 raw output 同目录保留。
- [validate-precompiled-probe/validator-list-tables-json.stdout.txt](validate-precompiled-probe/validator-list-tables-json.stdout.txt)：预编译 validator 与 `validate_data.py --json` 的 stdout/stderr 证据。

schema/Unity 代理报告由对应 agent 提供后，由根 `AUDIT_REPORT.md` 引用；本目录不重复运行 Unity。

ABI negative oracle 可在新目录复跑（拒绝覆盖既有证据，并从冻结树镜像 `abi_surface` 源码）：

```powershell
& 'D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\docs-project\abi-gate-negative-oracle\run-negative-oracle.ps1' `
  -FrozenRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-frozen' `
  -OutputRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\abi-gate-negative-oracle-rerun'
```

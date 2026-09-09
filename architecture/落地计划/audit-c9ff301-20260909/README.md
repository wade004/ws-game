# ws-game 1.13.0 框架范围与文档—代码审计

唯一审计基线是恢复仓 `D:\workespace\ws-game-artifacts\audit-c9ff301-resumed` 的 `c9ff30107413083188c597c0b65cf1691c9dfe9b`、`VERSION=1.13.0`。`D:\workespace\ws-game` 只读。本轮只复核责任边界并改写审计文档，不重新运行测试，不修改产品、既有测试、原始 log/XML/probe 或旧 ZIP。

先读 [docs-project/scope-and-evidence.md](docs-project/scope-and-evidence.md)：它定义框架通用契约、可选模块启用后契约、游戏接入/内容、宿主扩展点、未实现、非目标和用户暂缓的分类，也记录证据用途与旧结论变更。

入口：

- [AUDIT_REPORT.md](AUDIT_REPORT.md)：中文总报告、六项条件 P2、基线复测和整体边界结论。
- [docs-project/doc-code-matrix.md](docs-project/doc-code-matrix.md)：architecture 00—14、ADR-0001—0019、能力索引和 metadata 更新表。
- [docs-project/project-findings.md](docs-project/project-findings.md)：逐项 trigger→actual→expected→owner→oracle→最小验收。
- [docs-project/validation.md](docs-project/validation.md)：历史 check、正式 ZIP/lock、ABI、Unity/Core 证据和限制。
- [evidence-index.md](evidence-index.md)：raw 证据继承、历史归档与新归档边界。

复现入口（只在一次性解压的范围包副本中运行，避免覆盖本目录 raw 证据）：

```powershell
$AuditRoot = 'D:\workespace\ws-game-scope-review-unpacked'
$FrozenRepo = 'D:\workespace\ws-game-artifacts\audit-c9ff301-resumed'
Set-Location $AuditRoot
pwsh -File .\run-probes.ps1 -RepoRoot $FrozenRepo -FrameworkRoot (Join-Path $AuditRoot 'rebuild')
pwsh -File .\docs-project\api-compat\run-api-compat.ps1 -ReleaseRepo D:\workespace\ws-game
python .\docs-project\verify_release_zip.py --repo D:\workespace\ws-game
```

`run-probes.ps1`、Unity XML/log、Core/Presentation findings 是分层证据，不互相替代。`consumer_smoke` 是 13:150 所述独立最小模板工程；具体游戏技能/战斗/任务 E2E 不属于框架基础证明。正式 ZIP/lock 与重建 DLL 也分开核验。

旧 `ws-game-1.13.0-c9ff301-deep-audit.zip`/candidate 及源码镜像 README 是上一轮历史输入；本轮范围复核包使用新名 `framework-scope-review.zip`，不得覆盖旧 ZIP。

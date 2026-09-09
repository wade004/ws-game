# 1.11.0 项目验证记录

## 基线与范围

- 冻结工作树：`D:\workespace\ws-game-review-6739f50`，HEAD `6739f50e44ba39a023c6209af2673aaf6a1c1fdc`，版本 `1.11.0`。
- 原仓：`D:\workespace\ws-game`，本轮只读。验证不改产品、既有测试、启动 registry 或正式发布源。
- 环境：Windows NT 10.0.26200.0；Windows PowerShell 5.1.26100.9168；CLR 4.0.30319.42000；VSTest 17.11.1 x64；Core 测试目标 net8.0。

## 唯一 check 门禁

命令（本轮仅执行一次）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\workespace\ws-game-review-6739f50\check.ps1 -SkipUnity -LogFile D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\docs-project\check-skipunity.log
```

结果：exit `0`，门禁汇总 **14 PASS / 6 SKIP / 0 FAIL**，完整 transcript 为 [check-skipunity.log](check-skipunity.log)，退出码记录为 [check-skipunity.exit.txt](check-skipunity.exit.txt)。

- .NET：`dotnet build Core.sln -c Release` 通过，0 warning/error；六工程 `dotnet test --no-build` 通过 **2547/2547**（Foundation 685、Numbers 107、Carriers 354、Rules 404、Presentation.Common 498、Gameplay 499），含 Perf 类别。
- Python/toolchain：合并根校验 60 表/285 记录、0 error/0 warning/1 override；framework 根校验 5 表/124 记录、0 error/1 设计 warning；占位资产 92/92；toolchain pytest **92 passed, 2 skipped**；事件常量与 sample 导入检查均通过。
- 数据与包：合并 60 表/285 记录、0 error/0 warning/1 override；framework 5 表/124 记录、0 error/1 设计 warning；常量 90、placeholder 92/92、sample asset issues 0；三个 npm 包版本 1.11.0 且 dry-run 排除规则通过。
- SKIP 的六项为 Unity 编译、Unity EditMode、Unity PlayMode、独立版连续冒烟、独立版离散冒烟、消费方演练。`-SkipUnity` 是本次命令边界，不是这些能力的未实现结论；完整 Unity 结论由主审/表现报告管理。
- check 允许产生冻结仓 ignored `bin/_check_artifacts` 与 `dist/1.11.0`，这些重建物不是原仓正式发布 hash 证据。

## 正式 1.11 ZIP/lock 只读核验

[release-zip-lock-hash.log](release-zip-lock-hash.log) 使用原仓 `D:\workespace\ws-game\dist\ws-game-1.11.0.zip` 与同名 lock，逐项读取 ZIP 两个插件路径的六 DLL 并 SHA-256 对比 lock。ZIP 整体 SHA-256：`523D39853AFD27F3A9F891176633DCBC89B8311DABCE60CB9E4F1F6B6DB43C65`；lock 整体 SHA-256：`082AB1F7DC30CC9DCA620F501B676174040C86B8101780BE957BEBFE647D796C`；lock 声明 `version=1.11.0`、`git_commit=6739f50`。两插件路径六 DLL **12/12 MATCH**，完整逐项 hash 只以该日志为准，未将冻结仓重建 `dist/1.11.0` 当正式发布证据。

## API 兼容与包结构

- 归档旧 API probe 使用 `GameObjectHost.PendingChestLootSnapshot` / `RestorePendingChestLoot` 两个旧名，当前 DLL 仍提供 `[Obsolete]` 兼容别名。初次按仓库默认 TreatWarningsAsErrors 构建会把预期 CS0618 当 error；保留该初始记录为 [initial-warnaserror](api-compat/api-compat-current-rebuild-initial-warnaserror.log)，随后按兼容探针语义关闭 TreatWarningsAsErrors 得到最终成功记录。以 `-p:FrameworkRoot=<冻结仓本轮重建 DLL 目录>` 编译成功，exit 0、0 error、2 条预期 CS0618；源码、项目和最终日志分别为 [ApiCompatProbe.cs](api-compat/ApiCompatProbe.cs)、[ApiCompatLibrary.csproj](api-compat/ApiCompatLibrary.csproj)、[api-compat-current-rebuild-final.log](api-compat/api-compat-current-rebuild-final.log)。这是当前重建 DLL 的公开 API 编译证据，不冒充原始 ZIP 二进制运行证据。
- API 项目 HintPath 已参数化为 `$(FrameworkRoot)`，换工作目录只需按 README 传 DLL 根；旧“机器绝对 HintPath 不可移植”结论不适用于当前文件。
- `check.ps1` 的包清单检查、npm pack dry-run、DLL 同步与版本锁检查均通过；正式 ZIP/lock 另有上一节的只读证据。

## Markdown 相对链接检查器边界

`toolchain/tests/test_markdown_relative_links.py:12-33,78-124,220-249` 的实际行为：只用 `git ls-files '*.md'` 扫描 tracked Markdown；剥离 title、`#anchor` 和 `:line` 行号后缀，只判断文件存在；外部/绝对/锚点链接跳过；指向 `.gitignore` 覆盖路径跳过；`toolchain/tests/.linkcheck-ignore` 仅允许逐条登记“原件确认不可找回且正文已说明”的证据链接。当前文件有 7 条有效登记，均针对历史 audit-e070e3f/audit-3224ca1/audit-ac3b622 的缺失 XML、日志、源码快照或证据包；没有本轮 docs-project 的豁免项。check 中 pytest 结果 92 passed, 2 skipped，说明门禁自身通过。

因此本目录的三份新报告属于本轮未跟踪审计产物，按设计不会被该 tracked-only 检查器纳入；本轮仍对报告中的相对证据链接逐一使用当前文件存在性检查。该覆盖边界是验证限制，不是报告链接自动正确的保证，也不把历史归档缺失证据改写成当前运行时缺陷。

## 未执行边界

文档工程子任务没有启动 Unity；`-SkipUnity` 仅属于本次 check.ps1 步骤的命令边界。独立表现验证已运行，结果和证据见 [presentation-findings.md](../presentation/presentation-findings.md)，没有基于 check 的 SKIP 项作运行时结论。未执行 standalone、IL2CPP、长时压力和完整真实美术验收；静态结论限于当前源码/文档/项目文件和已留存日志。





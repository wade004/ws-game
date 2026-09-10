# 1.14.0 证据索引

本索引只管理 `audit-76d16a5-20260910` 当前审计输出。源码输入位于只读冻结树 `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen`；构建中间产物位于 `build`，不作为便携交付内容。

| 路径 | 用途 | 证据等级/限制 |
|---|---|---|
| `baseline.txt` | 冻结 HEAD、VERSION、worktree 与相对 c9ff301 的记录 | 基线记录，不是运行证明 |
| `check/check-1.14.0-skipunity.log` | 一次 `check.ps1 -SkipUnity` 完整 transcript | A/B；22 steps=16 PASS/6 SKIP/0 FAIL；ABI PASS 行因缺基线实际跳过 |
| `check/check-1.14.0-skipunity.exit` | 上述 check 进程 exit | exit 0；不能改变 SKIP 解释 |
| `logs/release-formal-1.14.0.log` | 直接从正式 ZIP entry 流计算七 DLL hash、核对 lock、四 manifest version | A；证明 ZIP 字节与 lock，对重建 DLL 和 Runtime 不作证明 |
| `docs-project/verify-formal-release.ps1` | 正式包核验脚本 | 只读 ZIP/lock 输入，输出路径应为审计日志 |
| `docs-project/api-compat/Program.cs` | SkillHost 17 参数旧 consumer | A 复现源码；编译后程序集不随正文归档为产品源码 |
| `docs-project/api-compat/SkillHostAbiConsumer.csproj` | 上述 consumer 工程 | A 复现输入 |
| `docs-project/api-compat/run-skillhost-abi.ps1` | 1.13 formal 编译、替换 1.14 formal DLL 的步骤 | 只使用新建专用输出目录；旧运行结果见稳定日志 |
| `docs-project/logs/api-compat/api-compat.log` | ABI 复现摘要、输入包、consumer hash、替换边界 | A；new exit 11 是负向兼容证据 |
| `docs-project/logs/api-compat/run-old-formal.log` | 旧正式 DLL consumer 运行 | A；exit 0，进入旧构造器并抛预期参数异常 |
| `docs-project/logs/api-compat/run-new-formal.log` | 只替换 1.14 DLL 后 consumer 运行 | A；exit 11，MissingMethodException |
| `docs-project/logs/api-compat/build-old.log` | 对 1.13 formal DLL 编译 consumer | A；只证明该最小 consumer 编译 |
| `docs-project/doc-code-matrix.md` | 00–14/ADR/能力索引/template/module README 与源码矩阵 | B/C；逐项说明责任和适用条件 |
| `docs-project/project-findings.md` | 当前项目 findings 与最小验收 | A/B/C；不把未提供能力当缺陷 |
| `docs-project/validation.md` | 命令、计数、包核验和 ABI 边界 | A/B；不替代 Unity/发布形态证明 |
| `docs-project/scope-and-evidence.md` | 权威责任矩阵、证据等级和旧结论映射 | C/B；oracle 栏均为预期，不表示通过 |
| `core/` | core 代理报告与其独立 probe/log | 以该目录报告注明的实际 evidence grade 为准 |
| `presentation/` | presentation/Unity 代理报告与独立 XML/log/hash | 以该目录报告注明的实际 evidence grade 为准；本轮文档不重跑 Unity |
| `build/check-artifacts/` | `check.ps1` 生成的 DLL/测试输出 | 构建中间物；不当作正式 ZIP，不纳入便携归档 |

正式包只读输入：`D:\workespace\ws-game\dist\ws-game-1.14.0.zip`，SHA256 `68e4cdec66333931d679ea2ebd1c97d77e8bcca9675061c74b8d5531fd841561`；对应 `.lock` 声明 version `1.14.0`、git commit `76d16a5`。原始正式包不复制进审计归档。

旧 1.13 审计 ZIP、candidate 和旧 raw 只作为历史/输入资料；它们的数字不自动继承到当前 1.14 结论。当前报告中的历史说明不能替代当前源码、正式 ZIP 或独立 consumer 证据。

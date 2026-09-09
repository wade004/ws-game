# 1.10.0 验证记录

## 基线与范围

- 冻结工作树：`D:\workespace\ws-game-review-ac3b622`。
- HEAD：`ac3b622041c348e87a469959c09d8a541a7c1351`；`VERSION=1.10.0`。
- 原仓：`D:\workespace\ws-game`，本轮只读；未启动 registry/release，未修改产品源码、既有测试或提交。
- 本记录的唯一门禁命令显式使用 `-SkipUnity`，因此记录的是全量 Unity 门禁跳过；整体审计的定向 Unity 证据另见表现目录与总报告。本记录同时覆盖静态/工程复核。

## 唯一门禁运行

命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\workespace\ws-game-review-ac3b622\check.ps1 -SkipUnity -LogFile D:\workespace\ws-game-review-ac3b622\architecture\落地计划\audit-ac3b622-20260909\docs-project\check-skipunity.log
```

完整原始日志：[check-skipunity.log](check-skipunity.log)。开始时间 `20260909033352`，结束时间 `20260909033446`，门禁报告总用时 54.1 秒，进程退出码 0。实际汇总为 **20 步：14 PASS / 6 SKIP / 0 FAIL**。

| 分项 | 实际结果 |
|---|---|
| .NET Release build | PASS，0 warnings / 0 errors |
| .NET 测试（含 Perf） | PASS：Foundation 683、Numbers 107、Carriers 347、Rules 404、Presentation.Common 496、Gameplay 496；合计 **2533 passed，0 skipped** |
| 合并数据校验 | PASS：60 tables、285 records、0 errors、0 warnings、1 override |
| framework 数据校验 | PASS：5 tables、124 records、0 errors、1 warning（`l10n.text` 未加载，按脚本设计不阻断） |
| 事件常量/占位资产/sample 资产 | PASS：90 constants、92/92 placeholders、0 sample issues |
| Python toolchain | PASS：**92 passed、2 skipped**，18.37s |
| 禁用词/架构正文扫描 | PASS |
| 版本一致性 | PASS：VERSION、两个 package.json、packages-lock.json、CHANGELOG 均 1.10.0 |
| DLL 同步 | PASS：0 copied / 6 skipped，内容未变化 |
| npm 包清单 | PASS：3 包 1.10.0，dry-run 排除项通过 |
| Unity 编译、EditMode、PlayMode | **SKIP**（`-SkipUnity`） |
| standalone 连续/离散 smoke、消费方演练 | **SKIP**（`-SkipUnity`） |

环境：Windows `10.0.26200.0`，PowerShell `5.1.26100.9168`，VSTest `17.11.1 (x64)`；日志中保留命令与脚本输出。check 允许在冻结仓产生 ignored `bin/_check_artifacts`、`dist/1.10.0` 和占位场景/导航文件，本轮不将它们当正式发布物。

## 发布 ZIP 与 lock

原仓只读 ZIP：`D:\workespace\ws-game\dist\ws-game-1.10.0.zip`，SHA-256 `60b98d28e33785b40744af1f21dff2b873b589c5810b81e37a7dc557a1c9fb8b`。

原仓只读 lock：`D:\workespace\ws-game\dist\ws-game-1.10.0.lock`，SHA-256 `9c187c89f0baf5ca03e3127b7efd8c0cd6f2fbc7350e72309f34285bbde709dc`。ZIP 内 `packages/.../Runtime/Plugins/Core` 与 `adapters/unity/.../Runtime/Plugins/Core` 两组插件路径各含六 DLL；逐条与 lock 匹配：

| DLL | SHA-256（ZIP 与 lock 相同） |
|---|---|
| Core.Foundation.dll | `5de3ae406308f7b0abf74e2ed3baef7e68342dd077b000bb03b31039c029a03c` |
| Core.Numbers.dll | `09034acaa0d7c6e3be79c938450aa1c93dade723647e4271dfd6f6c33beb18a8` |
| Core.Rules.dll | `96bc2c4d9e108675b1296838d6b0e2befc2ffb6feead3019a1d5df1d946eccd6` |
| Core.Carriers.dll | `deaf065a7b4374f35a54ce778bbb0d003d8754875e89a11d15f22b22176ad9dd` |
| Core.Gameplay.dll | `5208849e1f7f4b8f31e542aa8ad84830227df5023b403960bde5840ab53ce5d8` |
| Presentation.Common.dll | `7dd99d84a8c06e5ef713a62167ab8119a04be129f09d2f2e5a9f849f11dba288` |

结论仅为“原始 1.10.0 ZIP 与 lock 同源”；不以冻结仓 `dist/1.10.0` 的重建 hash 代替正式发布证据。重建路径会进入字节，`-SkipUnity` 不生成完整 Unity 元数据，审计工作树还有 untracked 状态，均属于验证边界。

## API 兼容与归档证据

- 当前重建 DLL 的旧 API 兼容探针 restore/build：成功，exit 0；预期两个 CS0618 过时警告，日志为 [api-compat-current-rebuild.log](api-compat/api-compat-current-rebuild.log)。验证对象是冻结仓 `dist/1.10.0` 本轮重建 DLL，不冒充原 ZIP。
- `current17-only` 当前 csproj 已改为 `$(FrameworkRoot)` 参数化。无参数 restore 0/build 1 的 MSB3245/CS0246 仅证明缺少 README 要求的参数，日志为 [api-compat-current17-absolute-path.log](api-compat/api-compat-current17-absolute-path.log)，不是工程缺陷；按 `-p:FrameworkRoot=<冻结仓重建 DLL 目录>` 传参后 restore/build 均为 0，见 [api-compat-current17-parameterized.log](api-compat/api-compat-current17-parameterized.log)。
- 旧 e070 审计证据存在性扫描为 7 个 Markdown、9 个真实缺失引用，详见 [archived-e070-link-audit.log](archived-e070-link-audit.log)。源码行号链接已剥离行号后确认文件存在；剩余缺失项集中在旧 core/presentation 日志和被删除 worktree 路径；当前报告不引用这些缺失证据。

## 静态验证边界

现行 02/05/06 与 W9 导航实现已静态核对：`INavigation2D` 的版本计数、精确端点、内部相交规则、禁止切角和 MovementHost 四步策略均有代码与单测锚点；全量 Unity 门禁因 `-SkipUnity` 跳过，Unity 定向结果由整体审计中的表现证据单独给出。方向移动只检查单位间阻挡，05 文档该边界列为更新项。

当前能力分类已写入 [doc-code-matrix.md](doc-code-matrix.md)：编辑器、天赋运行时激活、`FindUnits`、位移轨迹碰撞、VFX 持续跟随、孤儿检查等是未实现；TargetPoint 字段已实现但目标解析、采集/Quest/provider/Replay/Feedback/coverage 需显式接线或由游戏负责；day_cycle/ATB 是明确非目标；地图四扩展字段是 schema 代码待补。CORE-110-01/02/03 的 core 复现已确认，结论见 [core-findings.md](../core/core-findings.md)。

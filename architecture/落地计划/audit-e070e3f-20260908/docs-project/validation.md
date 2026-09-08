# v1.8.0 项目验证记录（e070e3f）

## 验证范围与环境

- 工作目录：`D:\workespace\ws-game-review-e070e3f`，冻结仓 HEAD `e070e3f`，版本 `1.8.0`。
- 原仓 `D:\workespace\ws-game` 只读；未修改原仓、启动 registry、产品代码、既有测试或提交。
- Windows `10.0.26200.0`，PowerShell `5.1.26100.9168`，VSTest `17.11.1 (x64)`；完整 PowerShell transcript 记录了用户名、进程和命令行。
- 唯一一次全量门禁命令：

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File D:\workespace\ws-game-review-e070e3f\check.ps1 -SkipUnity -LogFile D:\workespace\ws-game-review-e070e3f\architecture\落地计划\audit-e070e3f-20260908\docs-project\check-skipunity.log
  ```

- 原始日志：`check-skipunity.log`；开始 `20260908230954`，结束 `20260908231049`，总耗时 54.7 秒，退出码 0。脚本产生的 ignored `bin/_check_artifacts` 与 `dist/1.8.0` 属允许的冻结仓验证产物；全量门禁命令使用 `-SkipUnity`。clean Unity 定向 PlayMode 结果见同审计 `../presentation/presentation-findings.md`，旧混版副本已排除。

## 门禁结果（实际计数）

共 20 步：**14 PASS / 6 SKIP / 0 FAIL**。

| 范畴 | 实际结果 |
|---|---|
| 原生命令退出码自检 | PASS |
| .NET Release build | PASS，0 warnings / 0 errors |
| .NET 测试 | PASS：Foundation 662、Numbers 107、Carriers 329、Rules 404、Presentation.Common 491、Gameplay 491；合计 2484 passed、0 skipped |
| 合并数据校验 | PASS，60 tables / 285 records / 0 errors / 0 warnings / 1 override |
| `data/_framework` 单独校验 | PASS，5 tables / 124 records / 0 errors / 1 warning（`l10n.text` 未加载，跳过文本键存在性检查；脚本按设计仍通过） |
| 事件常量检查 | PASS，90 constants |
| 占位资产检查 | PASS，92/92 |
| sample 资产导入检查 | PASS，0 issues |
| Python toolchain tests | PASS：92 passed、2 skipped，20.34s |
| 禁用词/架构正文扫描 | PASS |
| VERSION/package/lock/CHANGELOG 一致性 | PASS，1.8.0 |
| `build.ps1 -SkipTests` DLL 同步 | PASS，DLL 内容未变化、6 个跳过同步 |
| npm 包清单 | PASS，3 个包均 1.8.0，`npm pack --dry-run` 未含排除项 |
| Unity 编译、EditMode、PlayMode | SKIP（`-SkipUnity`） |
| standalone 连续/离散 smoke | SKIP（`-SkipUnity`） |
| 消费方演练 | SKIP（`-SkipUnity`） |

六个核心程序集的 Release DLL 已在 `bin/_check_artifacts/bin/*/release` 生成，供后续 core 探针和 Unity 副本使用；本次不重复六项目全量 TRX。

## 表现专项（独立于全量门禁）

clean Unity 副本的定向 PlayMode 隔离组为 8/8，生产入口组为 7/7；三份 clean XML 合并去重后为 58/58，8+7 均已包含在 58 个唯一 testcase 内。clean 副本的 `UnityViewFactory.cs` 与冻结仓 SHA-256 一致，六个 Core DLL 当前 hash 也与冻结仓一致，`UnityProcessCount=0`。该结果证明表现专项的剪辑隔离、入口登记、Animator 实例播放和既有回归边界，不等同于完整 Unity 门禁或真实游戏验收。详见 [presentation/presentation-findings.md](../presentation/presentation-findings.md) 及其 XML 证据。

表现层同图 `RestoreFromSlot` 的掉落物探针已由主审独立 `dotnet run --no-build` 验证，exit 0，真实输出 `RESULT=PASS status=Loaded entity_present=True binder_views=0 created_views=1 loot_id=loot.inst_1`。它证明真实 .NET 绑定层存在 `WorldSim` 有实体而 `ViewBinder` 未建 View 的交互候选，原因与 CORE-180-01 共享事件抑制；没有将其新增定级，也不能直接推导为 Unity 屏幕缺陷。尚未验证的是完整 `Gameplay.RestoreFromSlot` + Presentation + Unity 画面链。

## 额外工程复核

### 旧 API 兼容

归档 `ApiCompatProbe.cs` 调用旧的 `GameObjectHost.PendingChestLootSnapshot` 与 `RestorePendingChestLoot`。用本轮冻结仓 `check.ps1` 重建的 `dist/1.8.0` DLL 作为 `FrameworkRoot`、独立的新 obj/bin 目录运行归档 `ApiCompatLibrary.csproj`，构建成功（预期的 CS0618 过时警告在验证命令中屏蔽，接口调用本身可解析）。证据：`api-compat-oldapi-portable-build.log`；该结果针对本轮重建 DLL，不冒充原始发布产物。当前别名位于 `core/carriers/gobj/core/GameObjectHost.cs:146-152`，新 API 仍为 `PendingLootSnapshot`/`RestorePendingLoot`。

归档 `current17-only/ApiCompatCurrent17Only.csproj` 不能直接复现：其 HintPath 硬编码已经不存在的 `D:\workespace\ws-game-review-8160178\...\current17zip\*.dll`。使用新 obj/bin 避免污染归档后，restore 成功但 build 失败，错误为找不到 Core 引用并连带 CS0246；证据：`archive-current17-only-absolute-path-restore-build.log`。这是归档审计工程的可移植性问题，不能推导为当前框架 API 断裂。

### 包、锁文件与发布复现

当前门禁确认 `VERSION`、两个 package.json、Unity `packages-lock.json` 和 CHANGELOG 版本均为 1.8.0，三个 npm 包 dry-run 清单通过。原仓只读发布基线 `D:\workespace\ws-game\dist\ws-game-1.8.0.lock` 的 commit 为 `e070e3f`；直接读取原始 `D:\workespace\ws-game\dist\ws-game-1.8.0.zip` 的 `packages/.../Runtime/Plugins/Core` 六个 DLL，逐一 SHA-256 与 lock 全部 MATCH：Carriers `a51c54cc8e728631a2bf1e22323f0463d618e25b2cba22dab235bd2bb91cf1db`、Foundation `e93028ea5463adb9ad9d6b1f9ef784ad8c29f2cd26439b0034994de057724dca`、Gameplay `4e17a3f8d666e550d5b579450670eed1b2753268d3f9ab351d2cf567588b2294`、Numbers `387592b20c6c98d2970cddec33a32e2dbcc6b06994f95a1ad3d7d82b66f9981a`、Rules `1d7a53c5d9d1530957a2797b9e1be1627f810a146e40c2a7cd1eaa95788ea6e5`、Presentation `5f0f49fea34c39087a443260117e3cdb12e42ceb89812524d49c63812224b401`。冻结仓 `-SkipUnity` 生成的 MANIFEST 为 `e070e3f-dirty`、adapter 228 files，少了六个 Unity `.meta` sidecar，且 hash 不同；这是构建路径/SkipUnity/审计状态造成的验证边界，不把冻结仓目录当正式发布物。

因此本次验证可以证明源码门禁、工具链校验通过，且原始 1.8.0 ZIP 与 lock 同源；不能用 `check.ps1 -SkipUnity` 的本地快照代表正式发布 zip 的字节复现。本轮没有运行完整 Release/Zip，也没有扩大发布写入范围。

## 验证边界

本报告的 PASS 是 Core/.NET、Python、静态扫描、版本/包清单和脚本路径的本项目验证证据；全量门禁中的 Unity 步骤是 SKIP，另有 clean 副本定向 PlayMode 8/8、7/7、去重 58/58 证据，详见 `../presentation/presentation-findings.md`。本报告不覆盖完整 Unity 门禁、真实游戏消费方接入、性能、用户验收；当前 core Load 探针已记录 rating 5（预期 10）、max/health 100（预期 200），证据在 `../core/core-findings.md`，本门禁日志不替代该 core 报告。

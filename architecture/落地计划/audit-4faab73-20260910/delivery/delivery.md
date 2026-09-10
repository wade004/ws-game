# 1.16.2 冻结交付验证

冻结源：`D:\workespace\ws-game-artifacts\audit-4faab73-frozen`，HEAD `4faab73e7081f2984e7addb88fee051b6c0d3d02`，`VERSION=1.16.2`。主仓 `D:\workespace\ws-game` 全程只读；冻结仓和主仓最终 `git status` 均干净。

## 整库 check

执行命令：

```powershell
& 'D:\workespace\ws-game-artifacts\audit-4faab73-frozen\check.ps1' `
  -SkipUnity `
  -ArtifactsPath 'D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\output\build\check' `
  -LogFile 'D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\output\raw\check.log'
```

实际退出码 `0`。24 步为 `17 PASS / 0 FAIL / 7 SKIP`。SKIP 是默认 ABI 探针缺少冻结树内 `dist/ws-game-1.12.0.zip`，以及 `-SkipUnity` 带来的六个 Unity/消费方步骤。六个 .NET 测试工程合计 `3045/3045` 通过（852+127+398+454+564+650）；toolchain pytest 为 `176 passed, 4 skipped`。原始记录见 [check.log](output/raw/check.log)。产物写入 `output/build/check`，check 的打包步骤另在冻结树写入被忽略的 `dist/1.16.2` 缓存；均未产生 tracked 漂移。首次写入的独立 sentinel 见 [sentinel.txt](output/sentinel.txt)。

## 1.12 ABI 与正式包

显式调用 `toolchain/abi_probe.ps1`，参数为基线 `D:\workespace\ws-game\dist\ws-game-1.12.0.zip`、`-OutDir output/abi-probe`、`-ArtifactsPath output/build/check`、`-Configuration Release`。实际退出码 `0`：旧 consumer 针对 1.12 DLL 运行退出 `0`，替换冻结树六 DLL 后不重编译运行退出 `0`，consumer hash 前后一致，`abi_surface compare breaks=0 allowed=0 additions=221 RESULT=OK`。原始日志见 [abi-probe.log](output/raw/abi-probe.log)，汇总见 [summary.txt](output/abi-probe/summary.txt)。

可见性收窄（`public -> protected`）的真实 DLL + old consumer E2E 已由 `toolchain/tests/test_abi_surface_compare.py:458` 覆盖，并包含在本轮 `176 passed` 的 pytest 运行中；本交付未重复另存该单测的独立 raw。

独立 OutDir 安全复现先写入 sentinel；把非空目录传给探针时实际退出 `1`，目录被拒绝且 sentinel 保留，见 [abi-outdir-safety.log](output/raw/abi-outdir-safety.log)。

正式包从只读主仓读取：`D:\workespace\ws-game\dist\ws-game-1.16.2.zip`。ZIP SHA256 为 `544bedaf54bf6c97cd0ad7b5afd1b51f0e210c165ee756efcb2c34319683f454`；lock SHA256 为 `7a1f8f5a01e928d9262632e0c2c7b86f5af9a2cbb4305bc0f4b6da8af483b453`；lock 的版本/提交为 `1.16.2`/`4faab73`。六个核心 DLL 在 ZIP 中各有 6 份、每个仅一个唯一 hash 且与 lock 匹配；Adapters.Stub 与 Validator 各有 2 份且匹配。四个根 package manifest 均为 `1.16.2`。已编译的 1.12 consumer 被复制后替换正式 ZIP 六 DLL，没有重编译；实际退出 `0`、`ABI_PROBE_ALL_OK`，consumer hash 前后均为 `9fc5af67b481462da45d40d318cb35a9f8f92b23dd21caae69f95eade46a7de6`。证据见 [formal-and-indexer.raw.txt](run5/output/raw/formal-and-indexer.raw.txt) 与 [formal-zip-consumer.log](run5/output/raw/formal-zip-consumer.log)。

复现脚本源为 [reproduce-formal-and-indexer.ps1](reproduce-formal-and-indexer.ps1)。脚本默认要求唯一、空的 `DeliveryRoot`，不会覆盖已有 raw；`-OnlyIndexer` 可单独复现 indexer oracle。完整 hash 清单见 [delivery-artifact-hashes.sha256](output/raw/delivery-artifact-hashes.sha256)。

## ABI indexer 盲点 oracle

最小真实 oracle 将 `public int this[int i]` 改为 `public int this[string i]`。旧 consumer 只编译一次，换上当前 DLL 后实际抛出 `MissingMethodException`，退出码 `-532462766`；当前 surface tool 两份 dump hash 相同、`compare exit=0`。旧 consumer hash 前后一致，因而确认“indexer 参数变化会被现有 surface 门禁漏检”这一具体问题，不泛化到其他 API 形式。增强脚本的独立重跑结果见 [indexer-oracle-current.log](run9/output/raw/indexer-oracle-current.log) 和 [surface-report.txt](run9/output/indexer-oracle/surface-report.txt)。

## Unity Runtime

隔离副本路径为 `output/unity-copy`；源/数据/资源/conformance/template 已从冻结树复制，包含 StreamingAssets 数据、场景/导航资源和测试所需的 conformance/template 数据。复制的非 meta 输入逐项 hash 全部一致，Library 和 Unity 导入生成的 meta 已明确排除，见 [unity-input-copy-hashes.txt](output/raw/unity-input-copy-hashes.txt)。六个注入 DLL 的来源是 check 构建产物 `output/build/check/bin/<assembly>/release`，不是正式 ZIP；逐项 source/target hash 全部相同，见 [unity-dll-copy-hashes.txt](output/raw/unity-dll-copy-hashes.txt)。复制准备源和 runner 源在 [prepare-unity-copy.ps1](unity-repro/prepare-unity-copy.ps1) 与 [run-unity-current.ps1](unity-repro/run-unity-current.ps1)。

Unity `6000.3.23f1` 在隔离副本运行，修正版 runner 实际退出 `0`：

- [full-edit.xml](output/unity-evidence2/full-edit.xml)：`result=Passed total=70 passed=70 failed=0`，exit `0`。
- [full-play.xml](output/unity-evidence2/full-play.xml)：`result=Passed total=272 passed=272 failed=0`，exit `0`。
- 原始日志：[full-edit.log](output/unity-evidence2/logs/full-edit.log)、[full-play.log](output/unity-evidence2/logs/full-play.log)。

此前复用的旧 runner 因 `Start-Process -Wait` 等待进程树卡住，仅留下 Edit XML/log、没有完整 exit；该过程保留在 [unity-run-console.log](output/raw/unity-run-console.log)，不作为当前成功证据。当前 evidence2 是修正版 runner 的真实 Edit/Play 结果。Standalone、IL2CPP、consumer smoke 未运行。

## 重现入口

先在新的 repro 根构建 DLL 并运行 ABI 探针，确保后续脚本有已编译旧 consumer 和 `abi_surface` 工具：

```powershell
$f='D:\workespace\ws-game-artifacts\audit-4faab73-frozen'
$o='D:\workespace\ws-game-artifacts\audit-4faab73-repro-20260910'
New-Item -ItemType Directory -Force -Path "$o\build\check" | Out-Null
& "$f\check.ps1" -SkipUnity -ArtifactsPath "$o\build\check" -LogFile "$o\check.log"
New-Item -ItemType Directory -Force -Path "$o\abi-probe" | Out-Null
& "$f\toolchain\abi_probe.ps1" -BaselineZip 'D:\workespace\ws-game\dist\ws-game-1.12.0.zip' -OutDir "$o\abi-probe" -ArtifactsPath "$o\build\check" -Configuration Release
```

随后用新的空 `DeliveryRoot` 运行 [reproduce-formal-and-indexer.ps1](reproduce-formal-and-indexer.ps1)，并把 `-AbiProbeOutputRoot` 指向上述 repro 输出根。该脚本只做 ZIP 流式读取、正式包 DLL 替换和最小 indexer oracle，不执行 release/publish。

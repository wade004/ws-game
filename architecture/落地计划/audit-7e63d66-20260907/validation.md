# 验证记录

基线：`7e63d6695644644e20f30e006b9efa7e531afa34`。工作树为 `D:/workespace/ws-game-review-7e63d66`。本轮不启动 Unity、不发布、不联网下载、不触碰 `D:/workespace/ws-game` 用户存档。

## 门禁

命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File check.ps1 -SkipUnity -LogFile bin/audit-7e63d66/check.log
```

退出码 0。20 步中 14 PASS、6 个 Unity 相关步骤 SKIP。`dotnet build Core.sln -c Release` 通过；六程序集测试合计 2,214 passed、0 failed、0 skipped。门禁中的 `python -m pytest toolchain/tests -q` 为 46 passed；合并数据校验和 `_framework` 校验均通过；事件常量、placeholder、sample 资产、禁用词、版本、DLL 同步、三 npm 包清单均通过。Unity 编译、EditMode、PlayMode、两个独立版冒烟和消费方演练按 `-SkipUnity` 跳过。原始日志：[evidence/check.log](evidence/check.log)。

## 有界复现

| 编号 | 命令/结果 | 证据 |
|---|---|---|
| C01 | 真实 `SaveSystem` console：合法正式槽 `slot.a.bak1` 后 `Load(slot.a)` 实际 `LoadedFromBackup`，业务段也被加载；`REPRODUCED`，退出 0。 | [core-repros-final-run.stdout.log](evidence/core-repros-final-run.stdout.log) |
| C05 | 真实 `InventoryHost` + `RewardDispatcher`：Partial A10 实际只增 5，B1 失败后 A5→A0；`REPRODUCED`，退出 0。 | [core-repros-final-run.stdout.log](evidence/core-repros-final-run.stdout.log) |
| P01 | clean `git archive` fixture 的 `check.ps1 -SkipUnity -Quick -ArtifactsPath ...` 退出 0；同 fixture `build.ps1 -SyncOnly -Dist 1.0.0 -Zip` 退出 1，缺默认 `core/*/bin/Release/netstandard2.1` DLL。 | [pjt-a-quick-check.log](evidence/pjt-a-quick-check.log)、[pjt-a-synconly.stdout.log](evidence/pjt-a-synconly.stdout.log) |
| P02 | 真实 1.0.0 ZIP 解压包运行 `python toolchain/validate_data.py --data-root <zip>/data/_framework`：骨架通过，第二道因发行包缺 `presentation`/`adapters/stub` 项目引用退出 1。真实 toolchain TGZ 的 README 同命令解析到不存在的 `<package>/toolchain/validator`，退出 1；直接 `dotnet build Tools~/validator` 也退出 1。两者是同一独立包消费问题的证据。 | [pjt-c-zip-validate.stdout.log](evidence/pjt-c-zip-validate.stdout.log)、[pjt-c-toolchain-validate.stdout.log](evidence/pjt-c-toolchain-validate.stdout.log)、[pjt-c-toolchain-build.stdout.log](evidence/pjt-c-toolchain-build.stdout.log) |
| P03 | 新 Unity fixture 先通过绝对路径护栏；真实 `sync_package_content.ps1` 退出 0，但删除了 `Assets/TextMesh Pro/game-owned-sentinel.txt`，因此 `REPRODUCED`。 | [pjt-b-path-guard.log](evidence/pjt-b-path-guard.log)、[pjt-b-sync.stdout.log](evidence/pjt-b-sync.stdout.log) |

复现程序源码保留在 [repro/Program.cs](repro/Program.cs)，项目文件在 [repro/BoundaryRepros.csproj](repro/BoundaryRepros.csproj)。`REPRODUCED` 表示实际触发了预期缺陷；源码静态条目不使用该标签。

## 发行与历史证据

只读检查 `D:/workespace/ws-game/dist/ws-game-1.0.0.zip`、三个 TGZ、`dist/1.0.0/MANIFEST.txt` 和 `ws-game-1.0.0.lock`：版本均为 1.0.0，MANIFEST/lock 的发布提交均为 `ab52097`，六个 DLL SHA256 与 lock 全部匹配，ZIP SHA256 为 `e5921c429f9a70503a85f9f1f525fe876fd5544535f1921362ef3c0e56454d33`。这是 `v1.0.0` 发布身份；当前审计 HEAD 为 `7e63d66`，不是漂移错误。详见 [release-materials-consistency.log](evidence/release-materials-consistency.log)。

GitHub 只读证据显示 CI `34095448082` 与 Release workflow `34095456791` 均 success；Release workflow 的打包和上传步骤 skipped，不能把它写成当前 Release 失败。`v1.0.0` release 已有 ZIP、lock 和三个 TGZ 共五个附件。详见 [github-readonly.log](evidence/github-readonly.log)。

`historical-unity-editmode.xml`（54/54）和 `historical-unity-playmode.xml`（170/170）是主目录既有记录，开始时间为 2026-09-07 07:01:11Z/07:01:23Z；本轮未重跑，也未与最终 ZIP 哈希绑定，详见 [evidence/historical-unity-editmode.xml](evidence/historical-unity-editmode.xml) 和 [evidence/historical-unity-playmode.xml](evidence/historical-unity-playmode.xml)。

## 边界

通过 .NET/Python 门禁只证明这些入口在当前源码树可执行；测试通过不等于可用游戏、真实 Unity 工程、包消费方或发布权限已验收。私服权限配置及本机依赖实现已静态审核；未实际注册账号、发布或删除包，未运行 registry 联网写入验证。版本升级文档应按实际旧 lock 与提交身份列迁移矩阵；早期 68c9bed 消费者遇到的接口变化（`IRewardDispatcher.Grant` 返回值、`ITargetHost.FilterExplicitTargets`、`IFeedbackSink.PendingPlaybackChanged`）需单独记录，不能用当前 0.2.0 磁盘快照替代。

# 有界复现工程

基线为 7e63d6695644644e20f30e006b9efa7e531afa34。本目录只放复现工程与结果说明；原始输出已复制到 [../evidence](../evidence/)，未启动 Unity、未发布、未接触主目录用户存档。

## 门禁与项目通道

全量 check.ps1 -SkipUnity 退出 0：14 PASS、6 Unity SKIP；六个 .NET 测试程序集为 2,214 passed、0 failed、0 skipped，门禁内 pytest 为 46 passed。完整日志见 [check.log](../evidence/check.log)。

PJT-A 使用 git archive HEAD 的 clean fixture。Quick 退出 0，生成外部 artifacts；同 fixture 的 build.ps1 -SyncOnly -Dist 1.0.0 -Zip 退出 1，首个缺少 core/foundation/bin/Release/netstandard2.1/Core.Foundation.dll。见 [Quick 日志](../evidence/pjt-a-quick-check.stdout.log) 和 [SyncOnly 日志](../evidence/pjt-a-synconly.stdout.log)。

PJT-B 使用新 Unity fixture 和绝对路径护栏。真实同步退出 0，但删除 Assets/TextMesh Pro/game-owned-sentinel.txt；正确行为应保留消费者文件。见 [护栏日志](../evidence/pjt-b-path-guard.log) 和 [同步日志](../evidence/pjt-b-sync.stdout.log)。

PJT-C 对真实 1.0.0 ZIP 解压包执行完整 validate_data.py，第二道因发行包缺 presentation/adapters/stub 项目引用退出 1；真实 toolchain TGZ 按 README 和直接 build Tools~/validator 也退出 1。见 [ZIP 日志](../evidence/pjt-c-zip-validate.stdout.log)、[UPM validator 日志](../evidence/pjt-c-toolchain-validate.stdout.log) 和 [UPM build 日志](../evidence/pjt-c-toolchain-build.stdout.log)。

## Core 复现

BoundaryRepros.csproj 只引用真实 Core.Foundation、Core.Carriers、Core.Gameplay 和 Adapters.Stub；代码见 [BoundaryRepros.csproj](BoundaryRepros.csproj) 与 [Program.cs](Program.cs)。

在本审计工作树根目录执行：

```powershell
dotnet build architecture/落地计划/audit-7e63d66-20260907/repro/BoundaryRepros.csproj -c Release --artifacts-path bin/audit-7e63d66/core-repro-artifacts-v3
dotnet bin/audit-7e63d66/core-repro-artifacts-v3/bin/BoundaryRepros/release/BoundaryRepros.dll
```

此程序是当前缺陷的探测器：退出 0 表示两个缺陷均被观察到，不代表框架正常。修复后应把对应预期转成正式回归断言，不能把此探测器的退出 0 当作产品验收条件。

CORE-A：只用真实 SaveSystem 正常保存合法正式槽 slot.a.bak1，再 Load(slot.a)。正确应 NotFound；实际 LoadedFromBackup，并加载 meta.slot_id 和 repro.marker，生成新布局 backup，标记 REPRODUCED。

CORE-B：真实 InventoryHost 为 MaxSlots=1、FullPolicy=Partial，初始 A5、stack_size=10，奖励 [A10,B1]。A 实际只增 5，B 失败，RewardDispatcher 返回 false 但最终 A0，标记 REPRODUCED。

构建、运行和完整输出见 [build 日志](../evidence/core-repros-final-build.stdout.log)、[run 日志](../evidence/core-repros-final-run.stdout.log) 与 [摘要日志](../evidence/core-repros-final.log)。REPRODUCED 表示实际触发缺陷；静态条目不使用该标签。

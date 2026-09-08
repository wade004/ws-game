# ws-game 1.7.0 文档与项目审计验证记录

## 基线与范围

- 冻结仓：`D:\workespace\ws-game-review-8160178`。
- `HEAD=8160178b76fb51ae704a8f14b428decf228cc33e`，`VERSION=1.7.0`。
- 原仓 `D:\workespace\ws-game` 只读，仅读取原始 1.5.0/1.7.0 zip 与 lock；未改源码、原仓 dist、网络服务或 registry。
- 本轮可写范围为本目录；`bin/_check_artifacts`、`dist/1.7.0` 等检查衍生产物由仓库规则忽略。本项目验证子任务未执行完整 Unity 门禁；整体专项验证见 `../presentation/presentation-findings.md` 与 `../core/core-findings.md`。

## 指定门禁

命令（工作目录为冻结仓）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\workespace\ws-game-review-8160178\check.ps1 -SkipUnity -LogFile D:\workespace\ws-game-review-8160178\architecture\落地计划\audit-8160178-20260908\docs-project\check-skipunity.raw.log
```

退出码记录于 `check-skipunity.exit.txt`，值为 `0`。`check-skipunity.raw.log` 的最终汇总为 **14 PASS、0 FAIL、6 SKIP**，总计 20 个显示步骤中，Unity 编译、EditMode、PlayMode、连续独立版冒烟、离散独立版冒烟、消费方演练 6 项因 `-SkipUnity` 明确 SKIP。PASS 细目包括：

|项目|结果|
|---|---|
|`dotnet build Core.sln -c Release`|PASS，0 warning、0 error|
|`dotnet test Core.sln -c Release --no-build`|PASS；Foundation 659、Numbers 107、Carriers 323、Rules 404、PresentationCommon 491、Gameplay 476，合计 2460|
|数据合并根/框架根校验|PASS；合并根 60 tables/285 records/0 errors/0 warnings/1 override；框架根 5 tables/124 records/0 errors/1 warning（缺 l10n 表的已知 warning）|
|事件常量、占位资产、`import_assets check`|PASS；90 常量、92/92 占位资产、sample 资源 0 问题|
|`pytest toolchain/tests -q`|PASS，92 passed、2 skipped|
|禁用词、版本一致性|PASS；`VERSION=1.7.0` 与 package/lock/CHANGELOG 一致|
|`build.ps1 -SkipTests`、包清单|PASS；六 DLL/内容同步无差异，3 个 npm 包均为 1.7.0，清单未命中排除项|

此处的 `Tests passed` 只证明 Native/工具链测试进程的结果；`dotnet test` 全量结果包含已归类的 .NET Perf 测试。本项目验证子任务仍未执行 Unity、实际游戏性能/压测、持久化真实场景、消费方或用户验收；整体专项验证见 `../presentation/presentation-findings.md` 与 `../core/core-findings.md`，不能把本子任务结果扩展成这些证明。

## 六项目独立 TRX

为避免并行测试覆盖同一个结果文件，六个项目以独立 `--results-directory` 和独立 `LogFileName` 执行，结果均退出 0：

|项目|计数|证据|
|---|---:|---|
|Tests.Foundation|659/659|`trx/Foundation.console.log`、`trx/Foundation.console.log.run.log`|
|Tests.Numbers|107/107|`trx/Numbers.console.log`、`trx/Numbers.console.log.run.log`|
|Tests.Rules|404/404|`trx/Rules.console.log`、`trx/Rules.console.log.run.log`|
|Tests.Carriers|323/323|`trx/Carriers.console.log`、`trx/Carriers.console.log.run.log`|
|Tests.Gameplay|476/476|`trx/Gameplay.console.log`、`trx/Gameplay.console.log.run.log`|
|Tests.PresentationCommon|491/491|`trx/PresentationCommon.console.log`、`trx/PresentationCommon.console.log.run.log`|

独立 TRX 仍属于 Native 测试证据；SaveSystem 的失败回滚缺陷由 Core 代理单独复现，不能用这张全绿表覆盖。

## API 兼容复核

原始 1.5 zip 解压出的六个 DLL 与 `D:\workespace\ws-game\dist\ws-game-1.5.0.lock` 六项 hash 全 MATCH；zip SHA256 为 `4C596D4A93C802C2FD065EB3ADC0F4ABC113B3B2687CC4F901FFA5B3C7E8C76B`。原始 1.7 zip 与 `ws-game-1.7.0.lock` 六项 hash 全 MATCH；zip SHA256 为 `1B72927AFFBD5E5AAF38FA9AA40A619F8F460F2F021EF0DFD799A597FB3B9A42`。逐项记录见 `api-compat-hash-verify.log` 和 `api-compat-current17zip-hash.log`。

使用同一个 `ApiCompatProbe.cs`，两个隔离项目分别硬引用各自解压 DLL，避免 MSBuild 共享引用缓存：

```powershell
dotnet build D:\workespace\ws-game-review-8160178\architecture\落地计划\audit-8160178-20260908\docs-project\api-compat\old15-only\ApiCompatOld15Only.csproj -c Release -t:Rebuild -o D:\workespace\ws-game-review-8160178\architecture\落地计划\audit-8160178-20260908\docs-project\api-compat\old15-only\out
dotnet build D:\workespace\ws-game-review-8160178\architecture\落地计划\audit-8160178-20260908\docs-project\api-compat\current17-only\ApiCompatCurrent17Only.csproj -c Release -t:Rebuild -o D:\workespace\ws-game-review-8160178\architecture\落地计划\audit-8160178-20260908\docs-project\api-compat\current17-only\out
```

两份最终构建日志首部记录各自绝对 DLL `HintPath` 与 SHA256，未使用 `FrameworkRoot` 或共享 `obj`；1.5 构建退出 0、0 warning；1.7 构建退出 0、0 error，并报告两个预期 CS0618 warning。反射显示 1.5 只有 `PendingChestLootSnapshot`/`RestorePendingChestLoot`；1.7 同时有 `PendingLootSnapshot`/`RestorePendingLoot` 与两个 `[Obsolete]` 旧名转发。此结论是公开 API 编译兼容，和旧存档 `PersistableThrew` 或字段迁移是两条独立证据。

## 本机工具与 BOM

|检查|结果|
|---|---|
|Python|`C:\ProgramData\anaconda3\python.exe`，Python 3.13.9；`preferred_encoding=cp936`|
|Windows PowerShell|5.1.26100.9168|
|pwsh|7.6.5|
|`toolchain/_hash.ps1`|前三字节 `239,187,191`，UTF-8 BOM=True|
|哈希工具链|`Get-FileHash` 可用路径与 .NET SHA256 fallback 均由本轮门禁覆盖|

本机工具检查通过不等于所有 CI runner 的环境通过；本机 `cp936` 只说明脚本必须显式 UTF-8 解码，不能推断其他宿主的默认编码。

## 未执行与证据边界

本项目验证子任务没有执行 Unity 编译、EditMode、PlayMode、连续/离散独立版冒烟、IL2CPP、消费方演练、网络 registry/Release 或用户验收；整体专项验证见 `../presentation/presentation-findings.md` 与 `../core/core-findings.md`。这些未执行项只标记为本子任务边界，不评级为失败。当前报告的文档更新建议仍需主审决定是否回写 `IPersistable.cs`、`CHANGELOG.md`、能力索引和 toolchain README。


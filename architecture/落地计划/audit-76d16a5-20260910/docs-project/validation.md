# 1.14.0 验证记录

## 基线与执行边界

- 冻结 worktree：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen`，detached HEAD `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`，`VERSION=1.14.0`，冻结树 status 为空。
- 原仓 `D:\workespace\ws-game` 只作为只读输入。审计期间主仓出现的未提交 `toolchain/registry/start_registry.ps1` 变动，以及未跟踪 `toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py` 不是本冻结基线，也未被本审计纳入或清理。
- 文档子任务没有再次启动 Unity；Unity/表现代理的结果必须引用其当前独立证据文件，不能从本次 `-SkipUnity` 推导。

## 本轮一次性 check

执行命令（原始 transcript 首行同样记录）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\check.ps1 -SkipUnity `
  -ArtifactsPath D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\build\check-artifacts `
  -LogFile D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\check\check-1.14.0-skipunity.log
```

- 进程 exit：`0`，记录于 `D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\check\check-1.14.0-skipunity.exit`。
- 汇总：22 steps，16 PASS、6 SKIP、0 FAIL。不能把 22 步写成 22 项已通过。
- .NET 六工程：2,840 passed、0 skipped、0 failed；分项为 123 / 737 / 383 / 439 / 545 / 613。
- Python 工具链：100 passed、2 skipped；合并数据 60 tables/290 records，0 errors、3 warnings、1 override；framework 根 5 tables/124 records，0 errors、1 warning；两个校验器输出均为 0 errors。
- schema audit：63 tables、783 fields、0 errors、0 warnings。
- 事件常量：90 个一致；placeholder assets：92/92；sample asset import：0 issues。
- 禁用词两项、版本一致性、同步 DLL/内容与 npm 包清单步骤均通过。
- Unity 编译、EditMode、PlayMode、独立版连续/离散冒烟和 consumer smoke 六项均因 `-SkipUnity` SKIP。

### ABI 行的特殊解释

`check-1.14.0-skipunity.log:109` 把 ABI 行显示为 PASS，但冻结树没有 `dist\ws-game-1.12.0.zip`。当前 `toolchain\abi_probe.ps1` 的默认 `SkipIfBaselineMissing=$true` 在缺失时 exit 0，而 `check.ps1` 又将子进程 stdout 丢弃，因此该 PASS 是“脚本未取得基线而跳过”的门禁显示，不能计作 ABI 运行证明。原始 log 保留不改；项目发现见 `project-findings.md`。

## 正式 1.14.0 ZIP / lock

只读核验输入为：

- ZIP：`D:\workespace\ws-game\dist\ws-game-1.14.0.zip`
- lock：`D:\workespace\ws-game\dist\ws-game-1.14.0.lock`
- ZIP SHA256：`68e4cdec66333931d679ea2ebd1c97d77e8bcca9675061c74b8d5531fd841561`
- lock version：`1.14.0`；lock git commit：`76d16a5`

`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\logs\release-formal-1.14.0.log` 对正式 ZIP 的精确 entry 流计算 SHA256，并逐项与 lock 比对。六个 Unity Core DLL 与 headless `Adapters.Stub.dll` 全部 match=True；四个 package manifest 的精确 entry version 全部为 `1.14.0`。这证明正式 ZIP 字节与 lock 的对应关系，不证明本次 check 生成的重建 DLL 等于正式 ZIP，也不把重建产物当正式发行包运行证明。

## SkillHost ABI 独立 consumer

探针源码与脚本：

- `D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\docs-project\api-compat\Program.cs`
- `D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\docs-project\api-compat\SkillHostAbiConsumer.csproj`
- `D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\docs-project\api-compat\run-skillhost-abi.ps1`

运行时使用原 `ws-game-1.13.0.zip` 编译 consumer，再只替换五个 Core DLL 为正式 1.14 DLL；没有重编译 consumer。稳定日志位于 `D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\docs-project\logs\api-compat\`：

- old formal：exit 0，`SKILLHOST_LEGACY_SIGNATURE_FOUND ArgumentNullException=dataRegistry`，说明旧 17 参数签名已被解析并进入构造器。
- new formal：exit 11，`SKILLHOST_LEGACY_SIGNATURE_MISSING MissingMethodException`，说明替换后旧二进制无法解析 17 参数签名。
- consumer 程序集 SHA256：`13500df60474618a7960649f8ff1241a2cad460cfe576e30bbd033fd5457e00a`；脚本未重建 consumer，日志未另行采集替换前后两份 hash。

这是负向兼容性证据，不能标为功能 PASS。它独立证明了当前 1.13→1.14 SkillHost ABI 缺口；内置 check ABI 行没有覆盖该 1.13 formal consumer。

## 证据边界

本验证集是静态源码/模块测试、一次 SkipUnity 门禁、正式包字节核验和独立 .NET consumer 的组合。它不覆盖完整 Unity 发布门禁、IL2CPP、压力、所有游戏内容或所有宿主策略；这些结论需各自契约和对应证据，不由本文件扩张。




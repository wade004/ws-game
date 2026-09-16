# 性能基线测试

见 `PerfBaselineTests.cs`（11_工程规范与测试.md 第 5、6 节"性能约定""性能基线、回放回归"）。
四项：
- 一次 tick 平均耗时——合成场景（`tick_*`，最小必要组件合成的 N=200 单位，不经完整
  技能/战斗/AI 管线，见该文件顶部判断记录）。
- 一次 tick 平均耗时——完整管线（`full_pipeline_tick_*`，收边任务补齐：改用
  `Tests.Gameplay.EndToEnd.GameWorldFixture` 装配的真实 `GameplayAssembly`，N=200 单位含 AI
  决策/施法管线/战斗结算，见 `TickCost_FullPipeline_MedianOfSampledTicks_WithinBaselineThreshold`
  判断记录）。
- 大规模空间查询耗时（1000 次 `QueryRadius`）。
- 存档序列化耗时。

固定场景：种子固定（合成场景）；完整管线场景用玩家 + 199 个 `creature.sample_beast`
（散布在感知半径内的螺旋阵列，持续围攻玩家，测试逐 tick 把玩家生命值打满以维持采样窗口内的
持续战斗负载，见该用例判断记录）。

## 阈值机制

`perf_baseline.json` 记录四项各自的中位数（`*_median_ms`，本机实测）与阈值
（`*_threshold_ms` = 中位数 × 5）；测试实际中位数超过阈值即失败，同时把实测值写进断言失败消息，
便于区分"确实变慢了"还是"这台机器本来就比记录基线的机器慢"。

## 机器归一化口径

背景：1.36.0 发布时 `TickCost_FullPipeline_MedianOfSampledTicks_WithinBaselineThreshold` 在
GitHub Release 工作流的共享 runner 上连续两次失败（实测中位数 46.25/46.07ms，超过阈值
32.222ms），第三次重跑通过；本机对比同一提交前后两个版本的中位数均约 7.3～7.5ms，确认无真实
回归——单纯是共享 runner 那几次比记录基线时的机器慢/忙。`perf_baseline.json` 记录的四项阈值是
"基线机"（32 逻辑核）的绝对毫秒数，对慢/忙于基线机的运行环境没有任何归一化，会把"机器慢"误判为
"性能回归"。

做法：新增 `PerfMachineCalibration`（同目录 `PerfMachineCalibration.cs`，internal，判断记录见该
文件顶部注释）——一段确定性、与被测生产代码完全无关的固定工作量（单线程整数/浮点混合循环 + 一次
固定大小的字典/列表分配与遍历，不使用 `Parallel`/线程池），取 5 次运行的中位数作为
`PerfMachineCalibration.ReferenceMs`（进程内只测一次并缓存，四条用例共享）。`perf_baseline.json`
新增 `reference_workload_ms`（记录基线时，基线机跑同一参考负载 5 次的中位数）与
`calibration_factor_max`（系数上限，当前 8）。四条用例的断言从"`median <= threshold`"改为：

```
factor = clamp(PerfMachineCalibration.ReferenceMs / baseline.reference_workload_ms, 1.0, calibration_factor_max)
median <= threshold * factor
```

基线机上 `factor ≈ 1`，断言与改动前等价；运行机器比基线机慢/忙，`factor > 1`，阈值按比例放大；
系数限幅 [1, 8]——下限 1 保证运行机器比基线机快时不会把阈值收紧到低于原基线（改动前的既有行为不
应变严），上限 8 保证病态慢/忙的机器仍然会失败（不会把"这台机器/这次运行环境本身有问题"误吸收成
"正常波动"），选 8 的依据见 `PerfMachineCalibration.cs` 判断记录（本次触发任务的两次失败实测约为
基线阈值的 1.43～1.44 倍，即比基线中位数慢约 7.15～7.2 倍，8 倍留出余量）。

四条用例无论成败都会通过 `ITestOutputHelper` 输出一行：

```
perf <用例名> median=<ms> threshold=<ms> factor=<x.xx> effective_threshold=<ms> reference=<ms>
```

断言失败消息同样带上 `factor` 与两边的参考负载耗时，供排查"是否只是机器慢"。`check.ps1` 的
`dotnet test Core.sln` 步骤改用 trx logger 落盘结果、跑完后从 trx 里挑出以 `perf ` 开头的行显式
`Write-Host`，让这四行确定性地进入 CI transcript，同时不改变该步骤对其余全部用例的输出量（判断
记录见 `check.ps1` 该步骤注释——`console;verbosity=normal/detailed` 会把全部用例的通过行一起
刷出来，不是"只有 Perf 四条用例输出"的最小改动）。

## 如何更新基线

1. 临时在 `LoadBaseline()` 调用处后面加一行 `Console.WriteLine("MEDIAN_MS=" + median)`（或直接
   跑一次测试、观察断言失败消息里报出的实测中位数——阈值判定失败时消息也会带上当前中位数）。
2. 用 `dotnet test core/gameplay/tests/Tests.Gameplay.csproj -c Release --filter "FullyQualifiedName~PerfBaseline" --logger "console;verbosity=detailed"` 跑一遍，记录四项 `MEDIAN_MS`，以及输出行里的
   `reference=<ms>`（即本机 `PerfMachineCalibration.ReferenceMs` 的当次实测值）。
3. 把新的中位数写入 `perf_baseline.json` 对应的 `*_median_ms`，`*_threshold_ms` = 中位数 × 5；
   更新 `machine`/`measured_at` 两个说明字段。
4. **同时**把上一步记录的 `reference=<ms>` 写入 `reference_workload_ms`——这一步不可省略：
   `*_median_ms`/`*_threshold_ms` 与 `reference_workload_ms` 必须是同一台机器、同一次运行内先后
   测出的一组数字，口径才自洽；只更新前者不更新后者，会让这台"新基线机"自己的机器系数偏离 1，
   四条用例的有效阈值会被这一遗漏错误放大或收紧。若同时调整了 `PerfMachineCalibration` 的常量
   （工作量形态/规模），也必须重新测这个值。
5. 撤销第 1 步的临时调试输出（若加了）。
6. 更新基线是一次有意的决策（"这台机器/这次改动之后的性能水平就是新的基准"），不要为了让测试
   通过而随手放大阈值——性能明显退化时应该先排查原因，而不是直接改基线掩盖。机器系数（第 4 步）
   只吸收"运行机器比基线机慢/忙"这一维度，不能用来掩盖真实的性能回归。

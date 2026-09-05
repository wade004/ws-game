# 性能基线测试

见 `PerfBaselineTests.cs`（11_工程规范与测试.md 第 5、6 节"性能约定""性能基线、回放回归"）。
三项：一次 tick 平均耗时、大规模空间查询耗时（1000 次 `QueryRadius`）、存档序列化耗时，
固定场景（种子固定，N=200 单位混战的合成场景，见该文件顶部判断记录）。

## 阈值机制

`perf_baseline.json` 记录三项各自的中位数（`*_median_ms`，本机实测）与阈值
（`*_threshold_ms` = 中位数 × 5）；测试实际中位数超过阈值即失败，同时把实测值写进断言失败消息，
便于区分"确实变慢了"还是"这台机器本来就比记录基线的机器慢"。

## 如何更新基线

1. 临时在 `LoadBaseline()` 调用处后面加一行 `Console.WriteLine("MEDIAN_MS=" + median)`（或直接
   跑一次测试、观察断言失败消息里报出的实测中位数——阈值判定失败时消息也会带上当前中位数）。
2. 用 `dotnet test core/gameplay/tests/Tests.Gameplay.csproj -c Release --filter "FullyQualifiedName~PerfBaseline" --logger "console;verbosity=detailed"` 跑一遍，记录三项 `MEDIAN_MS`。
3. 把新的中位数写入 `perf_baseline.json` 对应的 `*_median_ms`，`*_threshold_ms` = 中位数 × 5；
   更新 `machine`/`measured_at` 两个说明字段。
4. 撤销第 1 步的临时调试输出（若加了）。
5. 更新基线是一次有意的决策（"这台机器/这次改动之后的性能水平就是新的基准"），不要为了让测试
   通过而随手放大阈值——性能明显退化时应该先排查原因，而不是直接改基线掩盖。

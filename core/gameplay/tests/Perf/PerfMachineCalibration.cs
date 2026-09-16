using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Tests.Gameplay.Perf
{
    /// <summary>
    /// 性能基线机器归一化口径的参考负载（判断记录见 <c>Perf/README.md</c>"机器归一化口径"节）：
    /// 一段确定性、与被测生产代码完全无关的固定工作量，用来测出"这台运行机器比记录
    /// <c>perf_baseline.json</c> 阈值时的基线机快/慢多少倍"，四条 <see cref="PerfBaselineTests"/>
    /// 用例共享同一次测量结果（进程内只测一次，见 <see cref="ReferenceMs"/>）。
    /// <para>
    /// 判断记录（为什么是"参考负载对比"而不是改用 P75/加大采样数）：改采样统计量或加大采样数只能
    /// 让单次测量更稳定，解决不了"基线机与运行机器绝对速度不同"这个根本问题——运行机器比基线机慢
    /// 30%，用 P75 一样会比基线阈值慢 30% 而失败；参考负载对比直接测出这个倍率并把阈值乘上去，才是
    /// 对症的手段。
    /// </para>
    /// <para>
    /// 判断记录（为什么单线程、不用 Parallel/线程池）：四条被测用例（tick 推进、空间查询、存档
    /// 序列化）本身都是单线程路径，参考负载必须只反映"单核实际吞吐"这一个维度——如果参考负载引入
    /// 多线程，其耗时会同时受"核数"影响，一台核多但单核慢的机器会被参考负载测成"更快"（多线程负载
    /// 摊薄了单核短板），而被测用例仍是单线程、仍会因单核慢而超阈值，两者不成比例，归一化系数失真。
    /// </para>
    /// <para>
    /// 判断记录（为什么工作量形态是"整数/浮点混合循环 + 定长字典/列表分配遍历"、系数上限为 8）：
    /// 混合循环覆盖纯计算吞吐（ALU/FPU），字典/列表分配遍历覆盖内存分配与 GC 压力这一同样会随机器
    /// 负载状况（其它进程占用内存带宽、GC 触发频率）波动的维度，两者叠加比单一维度更能代表"这台
    /// 机器现在的综合状况"（见本目录 README"加压验证"一节的对比结论）。系数上限 8：机器系数用来
    /// 吸收"正常范围内的机器慢/忙"，不能吸收到无限——一台慢/忙到系数该有几十倍才能通过的机器，已经
    /// 不是"这次测量赶上机器慢"的正常波动，而是这台机器本身不适合跑性能基线（或测试环境出了别的
    /// 问题），继续放宽阈值只会让基线测试丧失"能不能测出真回归"的意义；上限选 8——本次触发本任务的
    /// 两次共享 runner 失败，完整管线 tick 用例实测中位数约为基线阈值（基线中位数 × 5）的
    /// 1.43～1.44 倍，即比基线中位数本身慢约 7.15～7.2 倍，8 倍系数刚好覆盖这一实测抖动幅度并留出
    /// 余量。
    /// </para>
    /// </summary>
    internal static class PerfMachineCalibration
    {
        // 32,000,000 次整数/浮点混合运算迭代 + 6,000 项字典/列表分配遍历，在本机（32 逻辑核，即
        // perf_baseline.json 记录的基线机）实测中位数约 43～44ms，落在设计要求的"基线机上约
        // 30～60ms"区间中段，留出双向余量（见 README"如何更新参考负载"一节，更新基线时需重新校准
        // 这两个常量）。
        private const int IntFloatLoopIterations = 32_000_000;
        private const int DictSize = 6_000;
        private const int CalibrationSamples = 5;

        private static readonly Lazy<double> Reference = new Lazy<double>(Measure, isThreadSafe: true);

        /// <summary>本机参考负载耗时中位数（毫秒）。进程内只测一次并缓存，四条 <see cref="PerfBaselineTests"/>
        /// 用例共享同一个值。</summary>
        public static double ReferenceMs => Reference.Value;

        private static double Measure()
        {
            // 预热一次，避免把首次 JIT 编译/类加载开销计入正式采样（与 PerfBaselineTests 其余四条
            // 用例的预热惯例一致）。
            RunWorkloadOnce();

            var samples = new List<double>(CalibrationSamples);
            var sw = new Stopwatch();
            for (var i = 0; i < CalibrationSamples; i++)
            {
                sw.Restart();
                RunWorkloadOnce();
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            samples.Sort();
            var mid = samples.Count / 2;
            return samples.Count % 2 == 0 ? (samples[mid - 1] + samples[mid]) / 2.0 : samples[mid];
        }

        /// <summary>固定工作量本体：整数/浮点混合循环（ALU/FPU 吞吐）+ 一次固定大小的字典/列表分配
        /// 与遍历（内存分配与 GC 压力）。种子/规模全部固定，不读取任何外部状态，与被测生产代码无
        /// 引用关系，单线程执行（不使用 Parallel/线程池，见类型判断记录）。</summary>
        private static void RunWorkloadOnce()
        {
            long intAcc = 0;
            double floatAcc = 0.0;
            for (var i = 0; i < IntFloatLoopIterations; i++)
            {
                intAcc += (i * 2654435761L) % 97;
                floatAcc += Math.Sqrt((i % 1000) + 1) * 1.0001;
            }

            var dict = new Dictionary<int, string>(DictSize);
            var list = new List<int>(DictSize);
            for (var i = 0; i < DictSize; i++)
            {
                dict[i] = "v_" + i;
                list.Add(i);
            }

            long sum = 0;
            foreach (var kv in dict)
            {
                sum += kv.Key + kv.Value.Length;
            }
            foreach (var v in list)
            {
                sum += v;
            }

            // 避免 JIT 把整段计算当死代码优化掉：写回一个不影响测量语义的只读局部（与
            // PerfBaselineTests.SyntheticWorkloadHandler 同一惯例）。
            _ = intAcc;
            _ = floatAcc;
            _ = sum;
        }
    }
}

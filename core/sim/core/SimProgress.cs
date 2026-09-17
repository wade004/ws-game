using System;

namespace Core.Sim
{
    /// <summary>
    /// 消费方反馈第 50 条（2026-09-17）：四个长任务 <c>Run</c> 入口（<see cref="FightRunner"/>/
    /// <see cref="GrowthSimulation"/>/<see cref="ArenaSimulation"/>/<see cref="CoverageSimulation"/>）
    /// 新增 <c>IProgress&lt;SimProgress&gt;</c> 重载共用的进度负载形状——只在各自入口的"外层迭代边界"
    /// 上报（种子/等级×偏移矩阵格子/技能-装备-生物遍历项，见各类型对应"消费方反馈第 49-51 条判断
    /// 记录"），不在单场战斗的 tick 循环内上报（<see cref="Total"/> 事先可知，tick 级上报只会让
    /// 进度回调本身的开销盖过取消响应延迟带来的收益，见任务书判断）。
    /// <para>
    /// 判断记录（<see cref="Stage"/> 用字符串常量而非枚举）：四个入口的"一次迭代"语义互不相同
    /// （<c>GrowthSimulation</c> 按种子、<c>ArenaSimulation</c> 按矩阵格子、<c>CoverageSimulation</c>
    /// 按技能/装备/生物三段各自独立遍历、<c>FightRunner</c> 只有一次"整场战斗"这一个阶段），且
    /// <c>CoverageSimulation</c> 单次 <see cref="M:Core.Sim.CoverageSimulation.Run"/> 调用内部会依次
    /// 经历三个阶段（技能→装备→生物）——枚举需要跨类型共用一份不断增长的取值集合，字符串常量（各
    /// 类型自持，如 <c>GrowthSimulation.ProgressStageRun</c>）更贴近"模块自持一份事件/阶段常量"的
    /// 既有惯例（同 <c>RulesEventKeys</c>/<c>PowerEventKeys</c>）,且不强迫四个入口对齐一份没有实际
    /// 共享语义的公共阶段表。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="Completed"/>/<see cref="Total"/> 为 <c>int</c> 而非百分比）：任务书原文
    /// "已完成外层迭代数/总迭代数"——整数对上下游都更精确（调用方可自行换算百分比，反过来从百分比
    /// 换算回整数会丢精度），且与 <c>CoverageSimulation</c>"总数跨三类分析异质"这一已知设计风险
    /// 配合时，保留原始整数计数比强行统一成一个可能不连续跳跃的百分比更诚实。
    /// </para>
    /// </summary>
    public readonly struct SimProgress
    {
        /// <summary>阶段标识，见各 <c>Run</c> 入口所在类型的 <c>ProgressStage*</c> 常量。</summary>
        public string Stage { get; }

        /// <summary>已完成的外层迭代数（含本次上报对应的这一次）。</summary>
        public int Completed { get; }

        /// <summary>本次 <c>Run</c> 调用的外层迭代总数，事先可知、全程不变。</summary>
        public int Total { get; }

        /// <summary>可选的补充说明（如 <c>CoverageSimulation</c> 当前正在分析的具体 id），供人读日志/
        /// 界面展示，不参与任何调用方的程序化判断（不保证格式稳定）。</summary>
        public string? Detail { get; }

        public SimProgress(string stage, int completed, int total, string? detail = null)
        {
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Completed = completed;
            Total = total;
            Detail = detail;
        }
    }
}

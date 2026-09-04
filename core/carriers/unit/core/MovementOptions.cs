using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <c>MovementTickHandler</c> 的口味配置项（见任务书拍板：速度属性 id 与缺省速度、到达判定
    /// 阈值，均可按具体游戏口味调整，不属于架构层面的固定语义，惯例同 <c>core/rules/ai</c> 的
    /// <c>AiOptions</c>）。
    /// </summary>
    public sealed class MovementOptions
    {
        /// <summary>速度来源属性 id（见 05 第 6.2 节"速度来源：来自该 Unit 的 StatBlock 中的移动速度
        /// 属性"），默认 <c>stat.move_speed</c>。</summary>
        public Id MoveSpeedStat { get; set; } = new Id("stat.move_speed");

        /// <summary><see cref="MoveSpeedStat"/> 缺失（<c>IStatHost.GetStat</c> 返回非正值，判断记录见
        /// <c>MovementTickHandler.ResolveSpeed</c>）时使用的缺省速度，默认 4（同
        /// <c>core/rules/ai</c> <c>AiOptions.MoveSpeed</c> 默认值同量级）。</summary>
        public double DefaultSpeed { get; set; } = 4.0;

        /// <summary>"已到达"路点判定的距离阈值，默认 0.01（同 <c>MoveIntentHandler</c> 一类最小实现
        /// 惯例，取一个远小于典型移动速度×步长的量级，避免因浮点误差导致永远差一点点到不了）。</summary>
        public double ArrivalEpsilon { get; set; } = 0.01;
    }
}

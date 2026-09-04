using Core.Foundation.Common;

namespace Core.Rules.Ai
{
    /// <summary>
    /// AI 模块的口味配置项（见任务书拍板：<c>AttackRange</c>/<c>MoveSpeed</c>/<c>FleeReengage</c>/
    /// <c>RandomTieBreak</c>/<c>RngStream</c>/<c>ArrivalEpsilon</c> 六项 + 判断记录补充的
    /// <see cref="MapId"/>）。全部字段可按具体游戏口味调整，不属于架构层面的固定语义。
    /// </summary>
    public sealed class AiOptions
    {
        /// <summary>进入攻击范围（<c>chase</c>→<c>combat</c>）与被追上（<c>flee</c>→<c>combat</c>）
        /// 判定用的距离阈值，默认 2。</summary>
        public double AttackRange { get; set; } = 2.0;

        /// <summary>位移意图的速度，单位为"每模拟时间单位移动的距离"，默认 4。</summary>
        public double MoveSpeed { get; set; } = 4.0;

        /// <summary><c>flee</c> 态被追上后是否允许回到 <c>combat</c>，默认 true（见 06 第 6.1 节
        /// "被追上继续战斗则回 combat（策略配置）"）。</summary>
        public bool FleeReengage { get; set; } = true;

        /// <summary>选目标/选平局候选时是否允许经 <see cref="Core.Foundation.Rng.IRngHost"/> 分流随机，
        /// 默认 false（默认平局按 Id 序，见本模块 README）。</summary>
        public bool RandomTieBreak { get; set; } = false;

        /// <summary><see cref="RandomTieBreak"/> 为 true 时使用的 RNG 分流 stream id。</summary>
        public Id RngStream { get; set; } = new Id("ai.decision");

        /// <summary>"已到达"判定的距离阈值（<c>return</c> 到出生点/巡逻路径起点、<c>patrol</c> 到
        /// 路径点），默认 0.5（见任务书 <c>return → idle/patrol：到达 SpawnPoint（距离 &lt; 0.5）</c>）。</summary>
        public double ArrivalEpsilon { get; set; } = 0.5;

        /// <summary>
        /// 判断记录（契约缺口）：<c>IUnitAccess</c> 不暴露单位所属地图 id（<c>MapId</c> 只存在于
        /// <c>Core.Foundation.SimLoop.Entity</c>，见本模块 README"契约缺口"一节），而
        /// <see cref="Core.Foundation.EngineAdapter.INavigation2D"/> 的全部方法都要求传入
        /// <c>mapId</c>。本任务拍板：用单个固定 <see cref="MapId"/> 代表"AI 使用的寻路地图"，
        /// 只适用于单地图场景；<c>null</c>（默认）表示不做寻路查询，一律退化为直线移动
        /// （即便调用方传入了非 null 的 <c>INavigation2D</c> 实例）。多地图场景需要集成任务扩展
        /// <c>IUnitAccess</c> 暴露 <c>GetMapId</c> 后才能真正按单位所在地图分别寻路。
        /// </summary>
        public Id? MapId { get; set; } = null;
    }
}

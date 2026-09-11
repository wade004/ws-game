namespace Core.Rules.Common
{
    /// <summary>
    /// 依赖倒置接口（同 <c>core/rules/common/contracts/IProjectileSpawner.cs</c> 顶部判断记录
    /// "依赖倒置"）：ADR-0026《技能位移的连续模式》——<c>move</c> 效果原语 <c>motion: continuous</c>
    /// 需要把位移交给一个能逐 tick 推进、做碰撞/导航裁决的"受控位移"任务，但这个任务（"受控位移与
    /// 玩家/AI 寻路移动互斥"，见 <c>MovementHost</c>/<c>MovementTickHandler</c>）属于 L3 载体层
    /// （<c>core/carriers/unit</c>，见 01 第 3 节依赖矩阵"L2 只允许依赖 L0/L1"），
    /// <c>core/rules/skill</c>（L2）不得直接引用 L3 类型。本接口是"开始一次受控位移"这一最小能力的
    /// 契约，由 L3 <c>core/carriers/unit.MovementHost</c> 直接实现（不另设适配器，惯例同
    /// <c>core/carriers/projectile.ProjectileHost : IProjectileSpawner</c>），组装期
    /// （<c>Core.Carriers.Assembly.CarriersAssembly</c>）经 <c>Core.Rules.Skill.SkillHost.DisplacementSink</c>
    /// 这个新增可写属性注入（不是构造函数参数——见该属性判断记录"ABI 安全：新增属性而非新增构造参数"），
    /// 最终换入 <c>core/rules/skill/core/EffectDispatcher.cs</c> 处理 <c>move</c> 效果原语
    /// <c>motion: continuous</c> 分支的落地出口。
    /// </summary>
    public interface IControlledDisplacementSink
    {
        /// <summary>
        /// 开始一次受控位移：<paramref name="request"/> 携带起点/终点/速度/阻挡策略/采样步长（见
        /// <see cref="ControlledDisplacementRequest"/>）。实现方按 <c>ControlledDisplacementRequest.Speed
        /// × dt</c> 每个模拟步沿直线从当前位置推进，每次最多推进
        /// <see cref="ControlledDisplacementRequest.SampleStep"/> 距离即做一次阻挡裁决（"路径采样"，
        /// 与既有目标/方向移动同源的 Raycast 判定一致，见 02 第 1.8 节勘误"统一可通行规则"）：
        /// 采样受阻时按 <see cref="ControlledDisplacementRequest.Blocking"/> 停在阻挡前最后可通行
        /// 采样点（<see cref="DisplacementBlockingPolicy.Stop"/>）或回到起点
        /// （<see cref="DisplacementBlockingPolicy.Revert"/>）并结束；到达终点、单位被控制（禁止移动
        /// 标志位）、单位死亡、或调用方经既有 <c>MovementHost.Stop</c> 显式请求停止时同样结束——全部
        /// 终止路径都经既有 <c>MovementHost.OnMoveStopped</c> 事件通知（携带原因，见
        /// <c>Core.Carriers.Unit.MoveStopReason</c> 新增的四个 <c>Displacement*</c> 枚举成员）。位移
        /// 期间该单位的普通寻路/方向移动意图被拒绝（"位移与移动互斥"，与既有"控制期间禁止移动"同一
        /// 落地位置，见 <c>MovementTickHandler.ApplyIntent</c> 判断记录）。时间模型暂停（本次推进的
        /// <c>dt</c> 为 0）时不推进，不产生任何位移/事件。离散模式（回合制）下一次性完成整段位移，
        /// 不按连续时间分帧（ADR-0026 决策 2"离散模式一次性完成"）。
        /// </summary>
        void BeginControlledDisplacement(ControlledDisplacementRequest request);
    }
}

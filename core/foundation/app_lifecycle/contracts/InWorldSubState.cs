namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// <see cref="AppState.InWorld"/> 内置子状态（见 03_运行时骨架.md 第 1、2 节）。
    /// <para>
    /// 判断记录：03 第 2 节状态机表里 Combat 子状态内部还有两个仅离散时间模型下出现的附加
    /// 子态 <c>awaiting_input</c>、<c>playing_back</c>（由 <c>core/foundation/sim_loop</c> 的
    /// <c>TurnScheduler</c>/<c>PacingPolicy</c> 驱动，见 03 第 3.2 节）。sim_loop 模块 T1-5
    /// 已明确"离散时间模型本项目暂不启用"（见该模块 README），本模块与之保持一致，不把这
    /// 两个附加子态收进本枚举——它们不是 <see cref="AppState.InWorld"/> 平级的独立子状态，
    /// 而是 Combat 内部因回合制推进节奏产生的更细粒度状态，本项目暂不需要建模。
    /// </para>
    /// </summary>
    public enum InWorldSubState
    {
        Explore,
        Combat,
        Dialog,
        MenuOverlay,
        Cutscene
    }
}

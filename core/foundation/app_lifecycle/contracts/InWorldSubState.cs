namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// <see cref="AppState.InWorld"/> 内置子状态（见 03_运行时骨架.md 第 1、2 节）。
    /// <para>
    /// 判断记录：03 第 2 节状态机表里 Combat 子状态内部还有两个仅离散时间模型下出现的附加
    /// 子态 <c>awaiting_input</c>、<c>playing_back</c>（由 <c>core/foundation/sim_loop</c> 的
    /// <c>TurnScheduler</c>/<c>PacingPolicy</c> 驱动，见 03 第 3.2 节）。ADR-0013 离散时间模型
    /// 现已接线（<c>core/gameplay/assembly.GameplayAssembly</c> 经 <c>AppStateMachineConfig.
    /// AddCustomSubState</c> 把它们登记为 <see cref="AppState.InWorld"/> 的自定义子状态扩展位），
    /// 但本枚举本身仍不收纳这两个附加子态——它们不是 <see cref="AppState.InWorld"/> 平级的独立
    /// 子状态，而是 Combat 内部因回合制推进节奏产生的更细粒度状态，用自定义子状态扩展点承载，
    /// 不改动本枚举（不新增原语）。
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

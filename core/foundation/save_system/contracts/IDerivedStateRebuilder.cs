namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// CORE-180-01/03 根治（architecture/落地计划/audit-e070e3f-20260908，P1/P2）：读档成功路径中，
    /// 某些内部派生缓存——评级换算属性（<c>Core.Numbers.StatBlock.StatHost.RecomputeRatingStats</c>）、
    /// 资源池上限（<c>Core.Numbers.PowerSet.PowerHost.RecomputeMax</c>）、种族/职业被动光环——此前
    /// 只能靠"内部同步事件"（<c>stat.changed</c>/<c>progression.state_restored</c> 等）驱动的订阅
    /// 才会重算；<c>SaveSystem.Load</c> 把整段"逐段 Load + 失败回滚"纳入
    /// <see cref="Core.Foundation.EventBus.IEventBus.SuppressDispatch"/> 抑制作用域（见该方法判断
    /// 记录"读档不是业务事件"，CORE-170-03 根治），这些内部同步事件与外部业务事件一起被无差别丢弃，
    /// 导致上述缓存在成功读档后维持读档前的陈旧值（真实探针复现，见 core-findings.md
    /// CORE-180-01：等级恢复到 10 但 Rating 缓存仍是 5、装备恢复但 Power max/当前生命仍是旧值）。
    /// <para>
    /// 判断记录（为什么不是"给内部同步事件开一个白名单，抑制作用域内照常派发"）：<c>stat.changed</c>
    /// 这个 key 同时被 <c>RulesAssembly</c> 自己的内部重算订阅、也被外部业务/测试订阅者共享——
    /// <c>EventBus.DispatchOne</c> 按 key 派发给该 key 下的全部订阅者，无法只挑内部订阅者放行、
    /// 外部订阅者继续抑制；CORE-170-03(b) 的既有回归测试（<c>core/gameplay/assembly/tests/
    /// CORE_170_03_SaveRollbackEventSuppressionTests.cs</c>
    /// <c>Load_BadShapeEquipmentSection_RestoresEquipment_AndDoesNotLeakEventsToObservers</c>）
    /// 显式断言排空事件队列后 <c>StatChanged</c> 一个都不应该出现在外部订阅者手里，给 <c>stat.changed</c>
    /// 开白名单会直接违反这条已经写死的验收——因此本次改用完全绕开事件总线的显式回调机制。
    /// </para>
    /// <para>
    /// <see cref="Core.Foundation.SaveSystem.SaveSystem"/>（L0）自身不认识 <c>StatHost</c>/
    /// <c>PowerHost</c>/<c>ProgressionHost</c>/<c>ArchetypeRegistry</c> 等 L1/L2 类型，不能直接调用
    /// 它们的重算方法；本接口是一个不依赖具体类型的窄回调，由装配根（<c>Core.Gameplay.Assembly.
    /// GameplayAssembly</c>）实现并经 <see cref="ISaveSystem.SetDerivedStateRebuilder"/> 注入——可选
    /// （未注入时 <see cref="SaveSystem"/> 内部引用为 null，全部调用点按 null 条件调用短路，行为与
    /// 引入本接口之前完全一致）。<see cref="SaveSystem.Load"/> 直接调用（<c>SaveSystem.Load(slotId)</c>
    /// 本身，不经过 <c>GameplayAssembly.RestoreFromSlot</c>）与经 <c>RestoreFromSlot</c> 调用共享
    /// 同一份注入，两条路径都能拿到正确的派生状态（真实探针两处均直接调用 <c>SaveSystem.Load</c>，
    /// 见 core-findings.md CORE-180-01 两段探针）。
    /// </para>
    /// </summary>
    public interface IDerivedStateRebuilder
    {
        /// <summary>
        /// <see cref="SaveSystem.Load"/> 真正开始逐段调用 <see cref="IPersistable.Load"/> 之前调用
        /// 一次（早于任何段的 Load，含 <see cref="OnSectionLoaded"/> 首次调用）——供实现快照"读档前"
        /// 的状态（如玩家当前 <c>ArchetypeId</c>/<c>RaceId</c>，见 CORE-180-03 根治：<see
        /// cref="OnSectionLoaded"/> 触发时对应字段已经被改写成存档里的新值，只有在这里才能读到
        /// "旧值"）。只在确实要开始逐段 Load 时调用（<c>NotFound</c>/<c>Corrupted</c>/
        /// <c>MigrationFailed</c> 不触碰任何 <see cref="IPersistable"/>，不调用本方法）。
        /// </summary>
        void BeforeLoad();

        /// <summary>
        /// 某个已注册段的 <see cref="IPersistable.Load"/> 成功返回（未抛异常）之后立即调用一次，
        /// 仍在 <see cref="Core.Foundation.EventBus.IEventBus.SuppressDispatch"/> 作用域内——但本
        /// 接口的实现应直接调用目标模块的方法（不经过事件总线），不受抑制影响。<see
        /// cref="SaveSystem"/> 本身不判断"哪个段之后该做什么"，只如实转发"刚成功完成的是哪个段"
        /// （<paramref name="sectionKey"/>，取值见 <see cref="SaveSections"/>），具体触发哪些重算/
        /// 重放完全由实现决定。只在读档主循环（成功路径）调用，不在失败回滚
        /// （<c>RollbackLoadedSections</c>）时调用——回滚路径的正确性由 CORE-180-02 根治（回滚改为
        /// 与正常读档同一顺序）本身保证，不依赖本钩子。
        /// </summary>
        void OnSectionLoaded(string sectionKey);
    }
}

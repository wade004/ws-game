namespace Presentation.Render
{
    /// <summary>
    /// 接收姿势上下文来源的视图工厂可选契约（手感设计/04 第 2 节、08 第 1 节换装链的表现端落点）：<c>IViewFactory</c> 的实现若同时实现本接口，
    /// <c>PresentationAssembly</c> 在手感启用时（装配出 <see cref="PoseSelector"/>）于构造早期把它交给工厂——工厂构造出的动画解析器据此按
    /// 武器族、步态解析运动态姿势，换装后待机/移动/攻击姿势随武器族自动切换，游戏不写代码。手感未启用时从不调用。
    /// <para>
    /// 判断记录（为什么是工厂的可选接口而不是装配参数）：视图工厂在装配根里先于 <c>PresentationAssembly</c> 构造（它是后者的构造参数），
    /// 姿势选择器却由后者装配，二者构造顺序反向；用可选接口让装配根在构造期补接，既不改 <c>PresentationAssembly</c> 与各工厂的既有构造签名，
    /// 也不要求每个游戏的装配根手写这一步。不实现本接口的工厂行为与此前逐位一致（姿势族只由 <see cref="PoseSelector"/> 持有，没有消费者）。
    /// </para>
    /// </summary>
    public interface IPoseContextReceiver
    {
        /// <summary>
        /// 交付姿势上下文来源。装配根保证在任何视图创建之前调用，且至多调用一次；实现方在已经使用过自己的姿势解析器之后收到调用应显式报错，
        /// 不得静默忽略。
        /// </summary>
        void SetPoseContextSource(IPoseContextSource source);
    }
}

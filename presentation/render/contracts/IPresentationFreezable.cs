namespace Presentation.Render
{
    /// <summary>
    /// 局部顿帧的表现冻结能力（手感设计/07 第 5 节，手感落地 M2-A）：<c>feel.hitstop_started</c> 到 <c>feel.hitstop_ended</c> 之间，
    /// 被冻结单位的动画时间轴（序列帧/骨骼必冻）与程序动画原语时间轴停住，其它单位、界面、镜头照常；闪白参数时间轴与动画时间轴分开，
    /// 冻结中的实体仍可被闪白。解冻后从冻结点继续。
    /// <para>
    /// 判断记录（独立成可选能力接口，不给 <see cref="ICharacterRig"/> 加成员）：同 <see cref="IHitFrameEmitter"/> 的做法——
    /// <see cref="ICharacterRig"/> 是必需接口，既有实现（含消费方自写）不必为一个可选能力改代码；探测写法
    /// <c>rig is IPresentationFreezable freezable</c>，探测不到视为"该 rig 不响应顿帧表现冻结"，不抛异常。
    /// <see cref="SpriteCharacterRig"/>/<see cref="ModelCharacterRig"/> 两个框架自带实现都实现本接口。
    /// </para>
    /// <para>
    /// 判断记录（幂等的布尔状态，不计数）：冻结/解冻是集合语义（顿帧宿主按单位集合发起始/结束，同一单位冻结中再被命中只延长时长、
    /// 不重复发结束），所以本接口是"冻结 = 真/假"的幂等开关，不做引用计数。
    /// </para>
    /// </summary>
    public interface IPresentationFreezable
    {
        /// <summary>当前是否处于顿帧表现冻结中。</summary>
        bool IsPresentationFrozen { get; }

        /// <summary>
        /// 进入冻结（幂等）。<paramref name="freezeTrail"/> 对应反馈包 <c>freeze_layers.trail</c>：为假时拖尾原语的时间轴继续推进，
        /// 为真时随动画一起冻结。
        /// </summary>
        void FreezePresentation(bool freezeTrail);

        /// <summary>解除冻结（幂等），从冻结点继续。</summary>
        void UnfreezePresentation();
    }
}

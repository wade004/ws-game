namespace Presentation.Render
{
    /// <summary>
    /// 可选能力接口（同 <c>Presentation.Common.IModelHandleProvider</c> 一贯的"能力接口"惯例，见该
    /// 类型判断记录"用能力接口而不是塞进 IView 本体"）：暴露一个 <see cref="IView"/> 实现内部持有的
    /// <see cref="ICharacterRig"/>，供反馈/表现帧驱动代码（如 <c>FeedbackAction.Flash</c> 的落地
    /// 委托、每帧 <c>ICharacterRig.Update</c> 推进代码）经 <c>view is IHasCharacterRig</c> 判定后取用，
    /// 不强求每个 <c>IView</c> 实现都持有 rig（09 第 4.1 节 CharacterRig 是"把逻辑单位画成可动角色"
    /// 这一类 View 的职责，非渲染性质的 View——如纯静态背景装饰——不需要）。
    /// </summary>
    public interface IHasCharacterRig
    {
        ICharacterRig Rig { get; }
    }
}

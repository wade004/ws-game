namespace Presentation.Render
{
    /// <summary>
    /// 动画状态机的状态集合（见 09_表现层.md 第 4.2 节"拍板的最小可扩展集合"）：
    /// <c>enum AnimState { idle, move, attack, cast, hit, death, jump }</c>。具体游戏在审批流程
    /// （见 [12_扩展与变更流程.md](../../architecture/12_扩展与变更流程.md)）下新增状态，本枚举本身
    /// 不因新增状态而改变已声明成员的数值（新增成员追加在末尾，避免破坏既有存档/回放里可能间接引用
    /// 到的枚举顺序——虽然本状态机本身不参与存档，见 09 第 1 节铁律"表现层里唯一允许保存的状态是
    /// 纯表现状态……不参与存档"，这里仍按惯例保留追加原则）。
    /// </summary>
    public enum AnimState
    {
        Idle,
        Move,
        Attack,
        Cast,
        Hit,
        Death,
        Jump,
    }
}

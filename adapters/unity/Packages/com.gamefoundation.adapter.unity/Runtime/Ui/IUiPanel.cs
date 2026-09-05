#nullable enable
// IUiPanel：十个界面单元的公共形状（任务书"每个面板一个 MonoBehaviour 绑定对应视图模型"）。
//
// 判断记录（订阅 vs 每帧 Refresh，二选一）：presentation/ui 的十个视图模型均已经在内部订阅
// IEventBus 相关事件并自动刷新自己的公开属性（构造期完成一次 Refresh 并注册订阅，见各
// ViewModel 源码），但均未额外暴露一个".NET 事件"（如 event Action Changed）供外部得知"属性变了、
// 该重新贴图了"——新增这样的事件需要改 presentation/ui（不在本任务允许改动范围，见任务书规则 2
// "presentation/ 只允许在被阻断时做最小改动"，这不构成"阻断"）。本套 UI 组件因此选择"二选一"
// 里的"每帧 Refresh"：面板不重复调用 viewModel.Refresh()（那是 view model 内部由事件驱动的职责，
// 重复调用只是浪费但不产生错误），只在自己的 Update() 里把 view model 当前（已经保持最新的）
// 属性值刷到 uGUI 控件上——面板数量与控件规模都很小（几个文本/按钮），逐帧整体重绘的开销可忽略。
using UnityEngine;

namespace Adapter.Unity.Ui
{
    /// <summary>面板与视图模型的绑定形态：见文件顶部"判断记录"。</summary>
    public interface IUiPanel
    {
        GameObject Root { get; }

        bool IsOpen { get; }

        void Show();

        void Hide();

        void Toggle();

        /// <summary>把 view model 当前属性值刷到本面板的 uGUI 控件上；由 <see cref="UiPanelHost"/>
        /// 每帧对"当前处于打开状态"的面板逐一调用。</summary>
        void RefreshUi();
    }

    /// <summary>供各面板复用的最小基类：Root 的显隐即 Show/Hide/Toggle 的落地方式。</summary>
    public abstract class UiPanelBehaviour : MonoBehaviour, IUiPanel
    {
        public GameObject Root => gameObject;

        public bool IsOpen => gameObject.activeSelf;

        public void Show() => gameObject.SetActive(true);

        public void Hide() => gameObject.SetActive(false);

        public void Toggle() => gameObject.SetActive(!gameObject.activeSelf);

        public abstract void RefreshUi();
    }
}

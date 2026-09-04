using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 离散输入事件的种类。文档 02 第 1.5 节只给出 pollEvents(): List&lt;InputEvent&gt;，
    /// 未展开 InputEvent 的字段；本类型按该节语义（"按下/抬起/设备接入断开"）与
    /// isKeyDown/getMousePosition/getGamepadAxis 反推出最小字段集，供 InputMap
    /// （core/foundation/input_map，本阶段未创建）后续按需扩展（见任务汇报第 5 节判断记录）。
    /// </summary>
    public enum InputEventKind
    {
        KeyDown,
        KeyUp,
        MouseButtonDown,
        MouseButtonUp,
        MouseMoved,
        GamepadButtonDown,
        GamepadButtonUp,
        GamepadConnected,
        GamepadDisconnected
    }

    /// <summary>
    /// 一条离散输入事件。Key 用于键盘/鼠标按键类事件（如 "A"、"MouseLeft"）；
    /// Position 用于 MouseMoved；GamepadIndex 用于手柄相关事件；不适用的字段取默认值。
    /// </summary>
    public readonly struct InputEvent
    {
        public InputEventKind Kind { get; }
        public string Key { get; }
        public Vec2 Position { get; }
        public int GamepadIndex { get; }

        public InputEvent(InputEventKind kind, string key = "", Vec2 position = default, int gamepadIndex = -1)
        {
            Kind = kind;
            Key = key;
            Position = position;
            GamepadIndex = gamepadIndex;
        }
    }

    /// <summary>
    /// 设备事件、键鼠手柄、文本输入（见 02_引擎适配层.md 第 1.5 节）。必需接口。
    /// PollEvents 每帧调用一次，不得丢事件（多次按下需全部保留在返回列表中）。
    /// </summary>
    public interface IInput
    {
        /// <summary>返回自上次调用以来的离散输入事件，供 InputMap 转译为游戏动作。</summary>
        IReadOnlyList<InputEvent> PollEvents();

        bool IsKeyDown(string key);

        Vec2 GetMousePosition();

        double GetGamepadAxis(int gamepadIndex, string axis);

        /// <summary>用于存档命名等少量场景，开启一个平台原生或引擎内置的文本输入会话。</summary>
        void BeginTextInput(string placeholder);

        /// <summary>结束文本输入会话并返回最终文本。</summary>
        string EndTextInput();
    }
}

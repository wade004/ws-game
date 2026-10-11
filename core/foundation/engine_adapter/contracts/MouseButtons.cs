using System;
using System.Collections.Generic;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 鼠标按钮的规范名词汇表（ADR-0182）。**一份名字，三处共用**：
    /// <list type="bullet">
    /// <item>适配层经 <see cref="IInput.PollEvents"/> 发出的 <see cref="InputEventKind.MouseButtonDown"/>/
    /// <see cref="InputEventKind.MouseButtonUp"/> 事件的 <see cref="InputEvent.Key"/>；</item>
    /// <item><see cref="IInput.IsKeyDown"/> 查询鼠标按钮当前是否按住时传入的名字；</item>
    /// <item>输入映射绑定字符串 <c>mouse:&lt;button&gt;</c> 里的 <c>&lt;button&gt;</c>
    /// （<c>Core.Foundation.InputMap.BindingParser</c> 只接受这里列出的名字，其余在解析时即抛
    /// <see cref="ArgumentException"/>，不会"解析成功但永远不触发"）。</item>
    /// </list>
    /// 适配层的职责是把引擎自己的鼠标按钮命名翻译成这里的规范名；本类型不接受别名，
    /// 也不做大小写宽容——两侧命名一旦不一致必须当场暴露，而不是各自悄悄兼容。
    /// </summary>
    public static class MouseButtons
    {
        /// <summary>主键（通常是左键）。</summary>
        public const string Left = "left";

        /// <summary>副键（通常是右键）。</summary>
        public const string Right = "right";

        /// <summary>中键（滚轮按下）。</summary>
        public const string Middle = "middle";

        /// <summary>全部规范名，固定顺序（左、右、中）。</summary>
        public static IReadOnlyList<string> All { get; } = new[] { Left, Right, Middle };

        /// <summary><paramref name="name"/> 是否恰好是某个规范名（序数比较，区分大小写）。</summary>
        public static bool IsCanonical(string? name)
        {
            if (name == null) return false;
            for (var i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i], name, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>规范名清单的可读文本（用于报错信息），形如 <c>left/right/middle</c>。</summary>
        public static string Describe() => string.Join("/", All);
    }
}

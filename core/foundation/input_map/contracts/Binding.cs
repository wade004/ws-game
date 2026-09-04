using System;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 一条绑定字符串解析后的种类（见本模块 README"绑定字符串小语法"一节）。
    /// <see cref="Key"/>/<see cref="Mouse"/>/<see cref="PadButton"/> 是"数字/按下型"绑定，
    /// 供 <see cref="ActionKind.Button"/> 动作与 <see cref="Composite2D"/> 的四个方向使用；
    /// <see cref="PadAxis"/>/<see cref="PadStick"/>/<see cref="Composite2D"/> 是"模拟/轴型"绑定，
    /// 供 <see cref="ActionKind.Axis1D"/>/<see cref="ActionKind.Axis2D"/> 动作使用。
    /// </summary>
    public enum BindingKind
    {
        /// <summary><c>key:&lt;name&gt;</c>：键盘按键。</summary>
        Key,

        /// <summary><c>mouse:&lt;button&gt;</c>：鼠标按键。</summary>
        Mouse,

        /// <summary><c>pad:&lt;button&gt;</c>：手柄按键。</summary>
        PadButton,

        /// <summary><c>pad_axis:&lt;axis&gt;</c>：手柄一维轴，<c>axis</c> 原样透传给
        /// <c>IInput.GetGamepadAxis</c>，本模块不限定轴名字取值集合。</summary>
        PadAxis,

        /// <summary><c>pad_stick:&lt;left|right&gt;</c>：手柄摇杆二维轴，取值只能是字面量
        /// <c>left</c> 或 <c>right</c>（见本模块 README 判断记录：轴名约定为
        /// <c>{left|right}x</c>/<c>{left|right}y</c>，经 <c>IInput.GetGamepadAxis</c> 取值）。</summary>
        PadStick,

        /// <summary><c>composite2d:&lt;up&gt;|&lt;down&gt;|&lt;left&gt;|&lt;right&gt;</c>：
        /// 四个按下型子绑定合成的二维轴，见 <see cref="ParsedBinding.Up"/> 等四个子字段。</summary>
        Composite2D,
    }

    /// <summary>
    /// 一条绑定字符串的解析结果（见本模块 README"绑定字符串小语法"）。不可变；
    /// <see cref="BindingParser.Parse"/> 是唯一构造入口。
    /// </summary>
    public sealed class ParsedBinding
    {
        /// <summary>原始绑定字符串，未做任何规范化（大小写、空白按原样保留）。</summary>
        public string Raw { get; }

        public BindingKind Kind { get; }

        /// <summary><see cref="BindingKind.Key"/>/<see cref="BindingKind.Mouse"/>/
        /// <see cref="BindingKind.PadButton"/>/<see cref="BindingKind.PadAxis"/> 的设备侧名字；
        /// <see cref="BindingKind.PadStick"/> 时取值固定为 <c>"left"</c> 或 <c>"right"</c>；
        /// <see cref="BindingKind.Composite2D"/> 时为 null（用四个子字段代替）。</summary>
        public string? Name { get; }

        /// <summary><see cref="BindingKind.Composite2D"/> 专用：向上方向的子绑定
        /// （只能是 Key/Mouse/PadButton 三种按下型绑定）；其余 <see cref="Kind"/> 为 null。</summary>
        public ParsedBinding? Up { get; }

        public ParsedBinding? Down { get; }

        public ParsedBinding? Left { get; }

        public ParsedBinding? Right { get; }

        internal ParsedBinding(string raw, BindingKind kind, string? name,
            ParsedBinding? up = null, ParsedBinding? down = null, ParsedBinding? left = null, ParsedBinding? right = null)
        {
            Raw = raw ?? throw new ArgumentNullException(nameof(raw));
            Kind = kind;
            Name = name;
            Up = up;
            Down = down;
            Left = left;
            Right = right;
        }

        /// <summary>是否为按下型（数字）绑定：<see cref="BindingKind.Key"/>/
        /// <see cref="BindingKind.Mouse"/>/<see cref="BindingKind.PadButton"/>。</summary>
        public bool IsDigital => Kind == BindingKind.Key || Kind == BindingKind.Mouse || Kind == BindingKind.PadButton;
    }

    /// <summary>
    /// 绑定字符串小语法的解析器（见 01_分层与依赖.md L0 模块表 <c>input_map</c> 行"绑定解析"、
    /// 本模块 README"绑定字符串小语法"）。语法：
    /// <list type="bullet">
    /// <item><c>key:&lt;name&gt;</c> 键盘按键</item>
    /// <item><c>mouse:&lt;button&gt;</c> 鼠标按键</item>
    /// <item><c>pad:&lt;button&gt;</c> 手柄按键</item>
    /// <item><c>pad_axis:&lt;axis&gt;</c> 手柄一维轴</item>
    /// <item><c>pad_stick:&lt;left|right&gt;</c> 手柄摇杆二维轴</item>
    /// <item><c>composite2d:&lt;up&gt;|&lt;down&gt;|&lt;left&gt;|&lt;right&gt;</c>
    /// 四个按下型子绑定合成的二维轴</item>
    /// </list>
    /// 设备侧名字（键名、手柄按钮名、轴名）本模块不做取值集合限定，原样透传给
    /// <c>IInput</c>（见 engine_adapter/contracts/IInput.cs 的 <c>InputEvent.Key</c>/
    /// <c>IInput.GetGamepadAxis</c> 同样不限定具体取值，由各引擎适配层实现约定）。
    /// </summary>
    public static class BindingParser
    {
        private const string KeyPrefix = "key";
        private const string MousePrefix = "mouse";
        private const string PadButtonPrefix = "pad";
        private const string PadAxisPrefix = "pad_axis";
        private const string PadStickPrefix = "pad_stick";
        private const string Composite2DPrefix = "composite2d";

        /// <summary>解析一条绑定字符串；格式非法时抛 <see cref="ArgumentException"/>。</summary>
        public static ParsedBinding Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new ArgumentException("绑定字符串不能为空", nameof(raw));
            }

            var colonIndex = raw.IndexOf(':');
            if (colonIndex < 0)
            {
                throw new ArgumentException($"绑定字符串 \"{raw}\" 缺少 \":\" 分隔符", nameof(raw));
            }

            var prefix = raw.Substring(0, colonIndex);
            var rest = raw.Substring(colonIndex + 1);

            switch (prefix)
            {
                case KeyPrefix:
                    return new ParsedBinding(raw, BindingKind.Key, RequireSimpleName(raw, rest, "key"));
                case MousePrefix:
                    return new ParsedBinding(raw, BindingKind.Mouse, RequireSimpleName(raw, rest, "mouse"));
                case PadButtonPrefix:
                    return new ParsedBinding(raw, BindingKind.PadButton, RequireSimpleName(raw, rest, "pad"));
                case PadAxisPrefix:
                    return new ParsedBinding(raw, BindingKind.PadAxis, RequireSimpleName(raw, rest, "pad_axis"));
                case PadStickPrefix:
                    if (rest != "left" && rest != "right")
                    {
                        throw new ArgumentException(
                            $"绑定字符串 \"{raw}\" 的 pad_stick 取值必须是 \"left\" 或 \"right\"，实际 \"{rest}\"", nameof(raw));
                    }
                    return new ParsedBinding(raw, BindingKind.PadStick, rest);
                case Composite2DPrefix:
                    return ParseComposite2D(raw, rest);
                default:
                    throw new ArgumentException(
                        $"绑定字符串 \"{raw}\" 的前缀 \"{prefix}\" 不是已知类型（key/mouse/pad/pad_axis/pad_stick/composite2d）", nameof(raw));
            }
        }

        private static string RequireSimpleName(string raw, string rest, string kindLabel)
        {
            if (string.IsNullOrEmpty(rest) || rest.IndexOf('|') >= 0)
            {
                throw new ArgumentException($"绑定字符串 \"{raw}\" 的 {kindLabel} 名字不能为空或包含 \"|\"", nameof(raw));
            }
            return rest;
        }

        private static ParsedBinding ParseComposite2D(string raw, string rest)
        {
            var parts = rest.Split('|');
            if (parts.Length != 4)
            {
                throw new ArgumentException(
                    $"绑定字符串 \"{raw}\" 的 composite2d 必须恰好是 4 段 \"up|down|left|right\"，实际 {parts.Length} 段", nameof(raw));
            }

            var up = ParseCompositePart(raw, parts[0], "up");
            var down = ParseCompositePart(raw, parts[1], "down");
            var left = ParseCompositePart(raw, parts[2], "left");
            var right = ParseCompositePart(raw, parts[3], "right");

            return new ParsedBinding(raw, BindingKind.Composite2D, name: null, up: up, down: down, left: left, right: right);
        }

        private static ParsedBinding ParseCompositePart(string raw, string part, string slot)
        {
            if (string.IsNullOrEmpty(part))
            {
                throw new ArgumentException($"绑定字符串 \"{raw}\" 的 composite2d {slot} 子绑定不能为空", nameof(raw));
            }

            ParsedBinding sub;
            try
            {
                sub = Parse(part);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"绑定字符串 \"{raw}\" 的 composite2d {slot} 子绑定 \"{part}\" 解析失败：{ex.Message}", nameof(raw));
            }

            if (!sub.IsDigital)
            {
                throw new ArgumentException(
                    $"绑定字符串 \"{raw}\" 的 composite2d {slot} 子绑定 \"{part}\" 必须是按下型绑定（key/mouse/pad），实际 {sub.Kind}", nameof(raw));
            }

            return sub;
        }
    }
}

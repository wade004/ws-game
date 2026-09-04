// StubUISurface：IUISurface 的最小可用桩实现——只记录调用，不做任何真实界面绘制。
// 用途：测试断言"某个界面承载面被创建、布局被设置成了什么、焦点在哪个元素上"，
// 而不启动任何 UI 框架或字体渲染。
// 与真实实现的差异：DrawText 只追加到一份调用记录里，不产生任何像素输出；
// SetFocus 对未创建的 surface 不做任何校验（保持最小实现），焦点状态是全局单一的
// "当前聚焦元素"，与文档语义一致（GetFocusedElement 不区分 surface）。
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubUISurface : IUISurface
    {
        public readonly struct DrawTextCall
        {
            public readonly Id SurfaceId;
            public readonly string Text;
            public readonly Vec2 Position;
            public readonly Id FontId;
            public readonly double Size;

            public DrawTextCall(Id surfaceId, string text, Vec2 position, Id fontId, double size)
            {
                SurfaceId = surfaceId;
                Text = text;
                Position = position;
                FontId = fontId;
                Size = size;
            }
        }

        public readonly Dictionary<Id, (int Width, int Height)> Surfaces = new Dictionary<Id, (int, int)>();
        public readonly Dictionary<Id, string> Layouts = new Dictionary<Id, string>();
        public readonly List<DrawTextCall> DrawTextCalls = new List<DrawTextCall>();

        private Id? _focusedElement;

        public void CreateSurface(Id surfaceId, int width, int height)
        {
            Surfaces[surfaceId] = (width, height);
        }

        public void SetLayout(Id surfaceId, string layoutData)
        {
            Layouts[surfaceId] = layoutData;
        }

        public void DrawText(Id surfaceId, string text, Vec2 position, Id fontId, double size)
        {
            DrawTextCalls.Add(new DrawTextCall(surfaceId, text, position, fontId, size));
        }

        public void SetFocus(Id elementId)
        {
            _focusedElement = elementId;
        }

        public Id? GetFocusedElement() => _focusedElement;

        /// <summary>测试用：清空当前焦点。</summary>
        public void ClearFocusForTest() => _focusedElement = null;
    }
}

using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// UI 承载、布局、字体、输入焦点（见 02_引擎适配层.md 第 1.10 节）。必需接口。
    /// layoutData 是结构化布局描述，格式待技术选型阶段确定，本层只当作不透明字符串传递。
    /// </summary>
    public interface IUISurface
    {
        void CreateSurface(Id surfaceId, int width, int height);

        void SetLayout(Id surfaceId, string layoutData);

        void DrawText(Id surfaceId, string text, Vec2 position, Id fontId, double size);

        void SetFocus(Id elementId);

        Id? GetFocusedElement();
    }
}

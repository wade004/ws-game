#nullable enable
// LabPanelFlow：试玩面板里"一组并排按钮按面板宽折行"的规划（纯函数，ADR-0154 修复"按钮/条文字空白"缺陷用）。
//
// 判断记录（根因与修法）：缺陷现象是场景页大片按钮没有字。根因是布局——武器/体型/预设/效果开关各是一整排不换行的切换按钮，数据里模板很多时那一排
// 的自然宽度（实测 4176 逻辑像素）远大于面板（380），滚动区内容宽被它撑大，同页其它按钮被拉成同样宽，居中的文字落在可见区之外，看上去是空按钮。
// 不是字体缺字（同页的纯文本标签、调参页的按钮都正常显示中文）。修法是把每组按钮按面板宽折行；折行规划做成不依赖界面的纯函数，
// 批处理（无 OnGUI）下也能测。
using Adapter.Unity;
using System;
using System.Collections.Generic;

namespace FeelLab.Unity
{
    public static class LabPanelFlow
    {
        /// <summary>并排按钮之间的间距（逻辑像素）。</summary>
        public const float Spacing = 4f;

        /// <summary>面板框的内边距与纵向滚动条占掉的宽度（逻辑像素）：可用于按钮的宽度 = 面板宽 − 这个值。</summary>
        public const float PanelChrome = 44f;

        /// <summary>
        /// 把宽度依次为 <paramref name="widths"/> 的按钮折成若干行，每行总宽（含间距）不超过 <paramref name="maxWidth"/>；
        /// 单个按钮比一行还宽时它独占一行（由按钮自身裁切，不再撑宽别的行）。保持原顺序，返回每行的按钮下标。
        /// </summary>
        public static List<int[]> Plan(IReadOnlyList<float> widths, float maxWidth, float spacing)
        {
            var rows = new List<int[]>();
            var current = new List<int>();
            var used = 0f;
            for (var i = 0; i < widths.Count; i++)
            {
                var w = Math.Max(0f, widths[i]);
                var need = current.Count == 0 ? w : used + spacing + w;
                if (current.Count > 0 && need > maxWidth)
                {
                    rows.Add(current.ToArray());
                    current.Clear();
                    need = w;
                }

                current.Add(i);
                used = need;
            }

            if (current.Count > 0)
            {
                rows.Add(current.ToArray());
            }

            return rows;
        }
    }
}

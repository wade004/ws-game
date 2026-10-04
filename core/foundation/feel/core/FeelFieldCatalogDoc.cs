using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 字段登记的文档片段生成器（手感设计/05 第 3.1 节，ADR-0146）：把 <see cref="FeelFieldSet"/> 渲染成按分组分表的 markdown
    /// 片段，设计文档引用这份片段而不手抄字段清单（手抄的清单每加一个字段就漂移一次）。
    /// <para>
    /// 判断记录：①渲染是纯函数（同一份登记 → 逐字节相同的文本，行尾恒为 LF，数值按不变文化格式化，分组与字段顺序取登记顺序），
    /// 测试 <c>FeelFieldCatalogDocTests</c> 把它与入库的片段文件逐字节比较，登记变了而片段没重新生成即失败。
    /// ②片段只说"字段是什么、取值范围、谁为主、生效与否"，不写推荐值（推荐值在预设行里）。
    /// ③不写任何具体技术名（片段位于设计文档目录，受同一份禁用词扫描）。
    /// </para>
    /// </summary>
    public static class FeelFieldCatalogDoc
    {
        /// <summary>片段文件相对仓库根的路径（设计文档引用它）。</summary>
        public const string RelativePath = "architecture/手感设计/05a_字段登记表.md";

        private static readonly FeelGroup[] GroupOrder =
        {
            FeelGroup.Input, FeelGroup.Movement, FeelGroup.Action, FeelGroup.Reaction, FeelGroup.Camera, FeelGroup.Effects, FeelGroup.Audio,
        };

        public static string Render(FeelFieldSet fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var sb = new StringBuilder();
            sb.Append("# 05a 字段登记表\n\n");
            sb.Append("> 本文件由字段登记自动生成，**不要手改**：字段的增删、范围、半属、合成来源与说明一律改登记，再重新生成（方法见手感核心模块说明）；"
                + "登记与本文件不一致时测试失败。字段的语义与分层规则见 [05_手感档案与解析.md](05_手感档案与解析.md)。\n\n");

            var active = 0;
            var planned = 0;
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Status == FeelFieldStatus.Planned) planned++;
                else active++;
            }
            sb.Append("共 ").Append(fields.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个字段：已生效 ")
                .Append(active.ToString(CultureInfo.InvariantCulture)).Append(" 个，已登记未生效 ")
                .Append(planned.ToString(CultureInfo.InvariantCulture)).Append(" 个。\n\n");
            sb.Append("列说明：**半属**——判定（改变模拟结果）或呈现（只改变看到听到的）；**类型与范围**——数值与整数给出登记范围（限幅依据，不是推荐值），"
                + "枚举给出取值；**操作**——该字段允许的覆盖操作；**合成来源**——武器层与体型层谁为主；**单位**——相对量由标定换算；"
                + "**状态**——已生效，或已登记但尚无消费方（数据里可以写，但目前不改变任何行为）。标 \"可选\" 的字段在预设里可以缺省。\n");

            for (var g = 0; g < GroupOrder.Length; g++)
            {
                var group = GroupOrder[g];
                var rows = new List<FeelFieldDef>();
                for (var i = 0; i < fields.Count; i++)
                {
                    if (fields[i].Group == group) rows.Add(fields[i]);
                }
                if (rows.Count == 0) continue;

                sb.Append("\n## ").Append(GroupName(group)).Append("（").Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个）\n\n");
                sb.Append("| 字段 | 半属 | 类型与范围 | 操作 | 合成来源 | 单位 | 状态 | 说明 |\n");
                sb.Append("|---|---|---|---|---|---|---|---|\n");
                for (var i = 0; i < rows.Count; i++) AppendRow(sb, rows[i]);
            }

            return sb.ToString();
        }

        private static void AppendRow(StringBuilder sb, FeelFieldDef f)
        {
            sb.Append("| `").Append(f.Name).Append("` | ")
                .Append(f.Half == FeelHalf.Judging ? "判定" : "呈现").Append(" | ")
                .Append(TypeAndRange(f)).Append(f.Optional ? "，可选" : string.Empty).Append(" | ")
                .Append(Ops(f.Ops)).Append(" | ")
                .Append(Composition(f)).Append(" | ")
                .Append(UnitName(f.Unit)).Append(" | ")
                .Append(f.Status == FeelFieldStatus.Planned ? "未生效：" + Escape(f.StatusNote ?? string.Empty) : "已生效").Append(" | ")
                .Append(Escape(f.Description)).Append(" |\n");
        }

        private static string TypeAndRange(FeelFieldDef f)
        {
            switch (f.Kind)
            {
                case FeelFieldKind.Number: return "数值 [" + Num(f.Min) + ", " + Num(f.Max) + "]";
                case FeelFieldKind.Int: return "整数 [" + Num(f.Min) + ", " + Num(f.Max) + "]";
                case FeelFieldKind.Bool: return "布尔";
                case FeelFieldKind.Enum: return "枚举 " + string.Join("、", f.EnumValues ?? Array.Empty<string>());
                case FeelFieldKind.Text: return "文本";
                case FeelFieldKind.Id: return f.SoftReferenceTable == null ? "引用" : "引用 " + f.SoftReferenceTable;
                case FeelFieldKind.List: return "列表";
                default: return f.Kind.ToString();
            }
        }

        private static string Num(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "-";

        private static string Ops(FeelOpSet ops)
        {
            var parts = new List<string>();
            if ((ops & FeelOpSet.Set) != 0) parts.Add("set");
            if ((ops & FeelOpSet.Multiply) != 0) parts.Add("multiply");
            if ((ops & FeelOpSet.Add) != 0) parts.Add("add");
            if ((ops & FeelOpSet.Remove) != 0) parts.Add("remove");
            return string.Join("/", parts);
        }

        private static string Composition(FeelFieldDef f)
        {
            string text;
            switch (f.Composition)
            {
                case FeelComposition.CharacterPrimary: text = "角色为主"; break;
                case FeelComposition.WeaponPrimary: text = "武器为主"; break;
                case FeelComposition.AttackOverride: text = "攻击期间武器临时覆盖"; break;
                default: text = f.Composition.ToString(); break;
            }
            return f.OffhandStackable ? text + "（副手可叠加）" : text;
        }

        private static string UnitName(FeelUnit unit)
        {
            switch (unit)
            {
                case FeelUnit.None: return "-";
                case FeelUnit.Milliseconds: return "毫秒";
                case FeelUnit.BodyHeights: return "身高倍数";
                case FeelUnit.BaseSpeedSeconds: return "基础移速下的秒数（不推荐，没有字段使用）";
                case FeelUnit.BaseSpeedRatio: return "移动速度属性的倍数";
                case FeelUnit.DegreesPerSecond: return "度/秒";
                case FeelUnit.Degrees: return "度";
                case FeelUnit.Ratio: return "倍率/比例";
                case FeelUnit.ClipRatio: return "剪辑长度比例";
                case FeelUnit.ScreenHeightRatio: return "画面高度比例";
                case FeelUnit.IntensityTier: return "强度档";
                case FeelUnit.Count: return "计数";
                default: return unit.ToString();
            }
        }

        private static string GroupName(FeelGroup group)
        {
            switch (group)
            {
                case FeelGroup.Input: return "输入组";
                case FeelGroup.Movement: return "移动组";
                case FeelGroup.Action: return "动作组";
                case FeelGroup.Reaction: return "受击组";
                case FeelGroup.Camera: return "镜头组";
                case FeelGroup.Effects: return "特效组";
                case FeelGroup.Audio: return "音频组";
                default: return group.ToString();
            }
        }

        private static string Escape(string text) => text.Replace("|", "\\|").Replace("\r", string.Empty).Replace("\n", " ");
    }
}

using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;

namespace Lab
{
    /// <summary>
    /// 第 8 层覆盖与脚本事件的互转（ADR-0150，手感设计 06 第 3.4 节、第 4 节调参面板）：调参面板每次改值都落成一条 <see cref="ScriptEventKind.Override"/> 事件
    /// （整局可被无头宿主逐 tick 重放），宿主按同一份编解码把事件还原成 <see cref="FeelWrite"/>。
    /// <para>
    /// 载荷约定（按字段登记里的值类型）：数值与整数字段——<see cref="ScriptEvent.Value"/> 的 X 是数值（与 ADR-0141 的既有事件逐位相同，没有文本载荷）；
    /// 布尔字段——X 为 0 或 1；枚举、文本、引用字段——取值放 <see cref="ScriptEvent.Text"/>；列表字段——元素以逗号连接放 <see cref="ScriptEvent.Text"/>
    /// （元素里不得含逗号，写入前校验）。Y 是操作码：0 = set、1 = multiply、2 = add、3 = remove（只列表字段）。
    /// </para>
    /// <para>
    /// 判断记录（为什么把取值放到新的文本载荷而不是编码成数）：枚举下标、布尔、引用 id 编成数会让脚本文件读不懂，而且字段取值集合一变下标就错位；
    /// 单独的文本载荷只在用到时才序列化（既有脚本与既有事件的文本逐字节不变），读起来就是"字段名 + 取值"。
    /// </para>
    /// </summary>
    public static class LabOverrideCodec
    {
        public const double OpSet = 0.0;
        public const double OpMultiply = 1.0;
        public const double OpAdd = 2.0;
        public const double OpRemove = 3.0;

        /// <summary>覆盖操作 → 事件里 Y 的操作码。</summary>
        public static double OpCode(FeelOp op)
        {
            switch (op)
            {
                case FeelOp.Set: return OpSet;
                case FeelOp.Multiply: return OpMultiply;
                case FeelOp.Add: return OpAdd;
                case FeelOp.Remove: return OpRemove;
                default: throw new ArgumentOutOfRangeException(nameof(op), op, "未知覆盖操作");
            }
        }

        /// <summary>操作码 → 覆盖操作（沿用 ADR-0141 的分界：小于 0.5 为 set、小于 1.5 为 multiply、小于 2.5 为 add，其余为 remove）。</summary>
        public static FeelOp OpFromCode(double code) =>
            code < 0.5 ? FeelOp.Set : code < 1.5 ? FeelOp.Multiply : code < 2.5 ? FeelOp.Add : FeelOp.Remove;

        /// <summary>
        /// 一条覆盖写入编码成脚本事件（<paramref name="actor"/>：空 = 全局，<c>player</c> = 玩家，其它 = 靶子出场标签）。
        /// 字段必须在 <paramref name="fields"/> 里登记且值种类与字段一致，否则抛 <see cref="LabFormatException"/>（不静默吞掉）。
        /// </summary>
        public static ScriptEvent Encode(FeelFieldSet fields, FeelWrite write, string actor)
        {
            if (!fields.TryGet(write.Field, out var def))
            {
                throw new LabFormatException($"手感字段 {write.Field} 未登记，不能写成覆盖事件");
            }

            var code = OpCode(write.Op);
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                    Require(write.Value.Kind == FeelValueKind.Number, def, write);
                    return new ScriptEvent(0, write.Field, ScriptEventKind.Override, new Vec2(write.Value.AsNumber(), code), null, actor);
                case FeelFieldKind.Bool:
                    Require(write.Value.Kind == FeelValueKind.Bool, def, write);
                    return new ScriptEvent(0, write.Field, ScriptEventKind.Override, new Vec2(write.Value.AsBool() ? 1.0 : 0.0, code), null, actor);
                case FeelFieldKind.List:
                    if (write.Value.Kind == FeelValueKind.List)
                    {
                        foreach (var item in write.Value.AsList())
                        {
                            RequireListItem(item, def);
                        }

                        return new ScriptEvent(
                            0, write.Field, ScriptEventKind.Override, new Vec2(0.0, code), null, actor, string.Join(",", write.Value.AsList()));
                    }

                    Require(write.Value.Kind == FeelValueKind.Text, def, write);
                    RequireListItem(write.Value.AsText(), def);
                    return new ScriptEvent(0, write.Field, ScriptEventKind.Override, new Vec2(0.0, code), null, actor, write.Value.AsText());
                default:
                    Require(write.Value.Kind == FeelValueKind.Text, def, write);
                    return new ScriptEvent(0, write.Field, ScriptEventKind.Override, new Vec2(0.0, code), null, actor, write.Value.AsText());
            }
        }

        /// <summary>覆盖事件还原成覆盖写入；字段未登记或载荷与字段种类不符抛 <see cref="LabFormatException"/>。</summary>
        public static FeelWrite Decode(FeelFieldSet fields, ScriptEvent e)
        {
            if (!fields.TryGet(e.Action, out var def))
            {
                throw new LabFormatException($"脚本 override 事件的手感字段 {e.Action} 未登记");
            }

            var op = OpFromCode(e.Value.Y);
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                    return new FeelWrite(e.Action, op, FeelValue.Of(e.Value.X));
                case FeelFieldKind.Bool:
                    return new FeelWrite(e.Action, op, FeelValue.Of(e.Value.X != 0.0));
                case FeelFieldKind.List:
                    if (op == FeelOp.Set)
                    {
                        return new FeelWrite(e.Action, op, FeelValue.OfList(e.Text.Length == 0 ? new string[0] : e.Text.Split(',')));
                    }

                    return new FeelWrite(e.Action, op, FeelValue.Of(e.Text));
                default:
                    if (e.Text.Length == 0)
                    {
                        throw new LabFormatException($"脚本 override 事件的字段 {e.Action}（{def.Kind}）缺少文本取值（text）");
                    }

                    return new FeelWrite(e.Action, op, FeelValue.Of(e.Text));
            }
        }

        /// <summary>清除覆盖的事件：<paramref name="field"/> 非空时只清该字段在 <paramref name="actor"/> 作用域里的覆盖；空串清全部。</summary>
        public static ScriptEvent EncodeClear(string field, string actor) =>
            new ScriptEvent(0, field ?? string.Empty, ScriptEventKind.ClearOverrides, default, null, actor ?? string.Empty);

        private static void Require(bool ok, FeelFieldDef def, FeelWrite write)
        {
            if (!ok)
            {
                throw new LabFormatException($"字段 {def.Name}（{def.Kind}）的覆盖值种类不对：{write.Value.Kind}");
            }
        }

        private static void RequireListItem(string item, FeelFieldDef def)
        {
            if (item.Length == 0 || item.IndexOf(',') >= 0)
            {
                throw new LabFormatException($"列表字段 {def.Name} 的元素不能为空、不能含逗号：\"{item}\"");
            }
        }
    }
}

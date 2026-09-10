using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// <see cref="ExprValue"/> 与 <see cref="JsonValue"/> 之间的编码约定：Bool/Number 直接映射，
    /// 整数用不带小数点的原始文本区分 Int/Number，Id 用 <c>{"$id": "..."}</c> 包装区分 String
    /// （惯例照抄 <c>core/gameplay/world_state</c> 的 <c>WorldState.ToJson/FromJson</c>——该类型未
    /// 公开这两个方法，本类型把同一套约定单独抽出，供 <see cref="RewardBundle"/>（<c>world_flags</c>
    /// 奖励项）与 <c>core/gameplay/dialog</c>（<c>set_flag</c> 动作的 <c>params.value</c>）共同复用，
    /// 避免第三处再手抄一遍同样的编解码逻辑）。
    /// </summary>
    public static class ExprValueJson
    {
        public static ExprValue Parse(JsonValue value)
        {
            switch (value)
            {
                case JsonBool b:
                    return ExprValue.OfBool(b.Value);
                case JsonNumber n:
                    // V-02 根治顺带发现并修复（第十七方深度审核，写 FromInt64 精确往返用例时暴露）：
                    // 此前 `(long)n.Value` 直接截断 double，即便 n.RawNumberText 携带精确的十进制原文
                    // 也绕过不用——ToJson 的 Int 分支已经改成 FromInt64（带精确 RawNumberText）之后，
                    // 这里仍按 double 截断会让"写出精确、读回丢精度"，V-02 的精确往返承诺在
                    // RewardBundle/dialog 这两个消费方（本类型的调用方）身上名存实亡。改用
                    // TryGetInt64——优先按 RawNumberText 精确解析，与 WorldState.FromJson 同款
                    // RequireInt64 helper 对齐；同时顺带堵住"IsIntegerRawText 为 true 但数字本身超出
                    // long 范围"（如 50 位纯数字整数文本）这类此前会静默产出垃圾值的边界，统一转成
                    // FormatException。
                    return IsIntegerRawText(n) ? ExprValue.OfInt(RequireInt64(n)) : ExprValue.OfNumber(n.Value);
                case JsonString s:
                    return ExprValue.OfString(s.Value);
                case JsonObject o when o.Count == 1 && o.TryGetValue("$id", out var idField) && idField is JsonString idText:
                    // CORE114-04 根治（外部审计 audit-76d16a5-20260910）：判断记录——此前直接
                    // `new Id(idText.Value)`，Id 构造期格式校验失败抛 ArgumentException，与本方法
                    // "格式错误一律 FormatException"的对外合同（见 IsValid 判断记录"Parse 抛
                    // FormatException"）不一致，导致 IsValid 的 try/catch(FormatException) 接不住
                    // 这条异常、原样冒泡到调用方（正式 QuestContentValidationRule/
                    // DialogContentValidationRule 校验阶段），使恶意/错误内容能让校验入口整体崩溃
                    // 而不是形成一条可定位的 report（外部审计 QuestWorldFlagValueBoundaryProbe 复现：
                    // {"$id":"BAD"} 报 invalid_id_formal=throws;type=ArgumentException）。改用
                    // Id.TryParse——失败时统一转译为 FormatException（携带原始 "$id" 文本），
                    // 使 Parse 对"合法 JSON 形状、但 Id 内容不合法"与"JSON 形状本身不在受支持并集内"
                    // 两类错误都归一为同一种异常类型，IsValid 不需要再区分。
                    if (!Id.TryParse(idText.Value, out var parsedId))
                    {
                        throw new FormatException($"\"$id\" 字段不是合法的 Id 格式：\"{idText.Value}\"");
                    }
                    return ExprValue.OfId(parsedId);
                default:
                    throw new FormatException($"不是受支持的 ExprValue JSON 形状（实际种类：{value.Kind}）");
            }
        }

        /// <summary>
        /// 校验 <paramref name="value"/> 是否是 <see cref="Parse"/> 能接受的形状（P2-06 根治）：不
        /// 构造 <see cref="ExprValue"/>、不抛异常，供内容校验阶段（<c>quest.def</c>
        /// <c>rewards.world_flags[].value</c>、<c>dialog.gossip_menu</c>
        /// <c>options[].actions[].params.value</c>（<c>set_flag</c> 动作）等）在 report 阶段就地
        /// 拒绝 <see cref="Parse"/> 无法处理的形状，不再要求消费方等到运行期
        /// <see cref="Parse"/> 抛 <see cref="FormatException"/> 才发现内容缺陷。刻意直接调用
        /// <see cref="Parse"/> 并吞掉其异常，而不是重新实现一份平行的判别 switch——两处独立维护同一
        /// 判别逻辑迟早会漂移（校验通过了但解析失败，或反之），本方法与 <see cref="Parse"/> 永远
        /// 接受同一个形状集合。
        /// <para>
        /// CORE114-04 根治（外部审计 audit-76d16a5-20260910）：只捕获 <see cref="FormatException"/>
        /// ——这是 <see cref="Parse"/> 对外承诺的"格式错误"异常类型（见上方 <c>$id</c> 分支判断记录，
        /// Parse 内部已把 Id 构造失败转译成 FormatException，不会再冒泡 ArgumentException）。不额外
        /// 捕获 <see cref="ArgumentException"/> 或更宽的 <see cref="Exception"/>——那样会连同吞掉
        /// Parse/Id 实现里其他类别的编程错误（例如误传 null），掩盖真正的 bug 而不是内容缺陷。
        /// </para>
        /// </summary>
        public static bool IsValid(JsonValue value)
        {
            try
            {
                Parse(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static JsonValue ToJson(ExprValue value)
        {
            switch (value.Kind)
            {
                case ExprValueKind.Bool:
                    return JsonBool.Of(value.AsBool);
                case ExprValueKind.Int:
                    // 改用 JsonNumber.FromInt64（第十七方深度审核 V-02 收口后新增的统一工厂），行为
                    // 与原手写 ToString(InvariantCulture) 一致，收敛到同一处判断记录（见
                    // WorldState.ToJson 同款分支）。
                    return JsonNumber.FromInt64(value.AsInt);
                case ExprValueKind.Number:
                    return new JsonNumber(value.AsNumber, FormatNumberWithDecimalPoint(value.AsNumber));
                case ExprValueKind.String:
                    return new JsonString(value.AsString);
                case ExprValueKind.Id:
                    return new JsonObjectBuilder().Add("$id", new JsonString(value.AsId.Value)).Build();
                default:
                    throw new InvalidOperationException($"未知的 ExprValueKind：{value.Kind}");
            }
        }

        private static bool IsIntegerRawText(JsonNumber n)
        {
            if (n.RawNumberText == null)
            {
                return n.TryGetInt64(out _);
            }
            return n.RawNumberText.IndexOf('.') < 0 && n.RawNumberText.IndexOf('e') < 0 && n.RawNumberText.IndexOf('E') < 0;
        }

        /// <summary>与 <c>WorldState.RequireInt64</c> 同款判断记录：<see cref="IsIntegerRawText"/>
        /// 只确认"看起来像整数文本"（没有小数点/指数），不确认真的落在 long 范围内（如超过 19 位的
        /// 纯数字文本）；这里统一按 <see cref="JsonNumber.TryGetInt64"/> 做最终裁决，失败则报
        /// <see cref="FormatException"/>，不静默产出截断/溢出的垃圾值。</summary>
        private static long RequireInt64(JsonNumber n)
        {
            if (!n.TryGetInt64(out var v))
            {
                throw new FormatException(
                    $"数字 \"{n.RawNumberText ?? n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" 看起来是整数但无法解析为 Int64");
            }
            return v;
        }

        private static string FormatNumberWithDecimalPoint(double value)
        {
            var s = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (s.IndexOf('.') < 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0)
            {
                s += ".0";
            }
            return s;
        }
    }
}

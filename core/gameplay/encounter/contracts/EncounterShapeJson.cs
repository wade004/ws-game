using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def.arena_rules.bounds_shape</c> 的 JSON 编码约定（任务书拍板补录：05 第
    /// 3.5 节只定义了 <see cref="Shape"/> 这一运行期联合类型的四种形状与字段，未规定数据表里的
    /// JSON 形状——本类型是"该字段落到具体 JSON 后长什么样"这一契约缺口的落地，格式为
    /// <c>{"kind": "circle|cone|line|rect", "origin": {"x","y"}, ...视 kind 而定的其余字段}</c>，
    /// 字段命名对齐 <see cref="Shape"/> 各属性的 snake_case 形式）。
    /// </summary>
    public static class EncounterShapeJson
    {
        public static Shape Parse(JsonObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            if (!obj.TryGetValue("kind", out var kindRaw) || !(kindRaw is JsonString kindStr))
            {
                throw new FormatException("bounds_shape.kind 必须是字符串（circle|cone|line|rect）");
            }

            switch (kindStr.Value)
            {
                case "circle":
                    return Shape.Circle(ParseVec2(obj, "origin"), RequireNumber(obj, "radius"));
                case "cone":
                    return Shape.Cone(ParseVec2(obj, "origin"), RequireNumber(obj, "direction"), RequireNumber(obj, "angle"), RequireNumber(obj, "radius"));
                case "line":
                    return Shape.Line(ParseVec2(obj, "origin"), RequireNumber(obj, "direction"), RequireNumber(obj, "length"), RequireNumber(obj, "width"));
                case "rect":
                    return Shape.Rect(ParseVec2(obj, "origin"), ParseVec2(obj, "half_extents"), RequireNumber(obj, "rotation"));
                default:
                    throw new FormatException($"bounds_shape.kind 未知取值 \"{kindStr.Value}\"（合法值：circle|cone|line|rect）");
            }
        }

        private static Vec2 ParseVec2(JsonObject obj, string field)
        {
            if (!obj.TryGetValue(field, out var raw) || !(raw is JsonObject vecObj)
                || !vecObj.TryGetValue("x", out var xRaw) || !(xRaw is JsonNumber xNum)
                || !vecObj.TryGetValue("y", out var yRaw) || !(yRaw is JsonNumber yNum))
            {
                throw new FormatException($"bounds_shape.{field} 必须是 {{\"x\": Number, \"y\": Number}}");
            }
            return new Vec2(xNum.Value, yNum.Value);
        }

        private static double RequireNumber(JsonObject obj, string field)
        {
            if (!obj.TryGetValue(field, out var raw) || !(raw is JsonNumber num))
            {
                throw new FormatException($"bounds_shape.{field} 必须是数值");
            }
            return num.Value;
        }
    }
}

using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// <c>area.trigger_def.shape</c>（及 <c>gobj.template.type_data.trap.trigger_shape</c>，见 07 第
    /// 3.1 节该字段判断记录"未深入解析……由 L4 区域触发装配的调用方"负责）的 JSON 形状 →
    /// <see cref="Shape"/> 解析（见 05 第 3.5 节四种形状）。
    /// <para>
    /// 判断记录：与 <c>core/rules/targeting</c> 的 <c>TargetChainDef.ParseShape</c> 不同——那里的
    /// shape 是"模板"，<c>center</c>/<c>rotation</c> 恒为零、由施法者当前坐标/朝向在使用时重新锚定；
    /// 本类型解析的是区域触发的固定世界坐标形状，<c>center</c>（circle 的圆心/cone·line·rect 的
    /// 原点）与 <c>rotation</c>（cone/line 的方向、rect 的旋转）都是数据里给定的绝对值，不做二次
    /// 锚定。JSON 字段集合按任务书拍板取 targeting 的 <c>{kind, radius, angle, length, width}</c> 并
    /// 追加 <c>center{x,y}</c>、<c>rotation</c>：circle 只读 <c>radius</c>/<c>center</c>；cone 读
    /// <c>radius</c>/<c>angle</c>/<c>center</c>/<c>rotation</c>（<c>rotation</c> 即 Shape.Direction）；
    /// line 读 <c>length</c>/<c>width</c>/<c>center</c>/<c>rotation</c>（同上，<c>center</c> 是线段
    /// 起点）；rect 读 <c>length</c>/<c>width</c>/<c>center</c>/<c>rotation</c>，<c>length</c>/
    /// <c>width</c> 按 <c>TargetChainDef.ParseShape</c> 同一惯例视为"全宽/全高"，换算成
    /// <see cref="Shape.HalfExtents"/> 时各自除 2。角度单位（<c>angle</c>/<c>rotation</c>）不做任何
    /// 换算，与 <see cref="Shape"/> 类型注释"由调用方与实现方约定"一致：本模块统一按弧度处理（见
    /// <see cref="AreaTriggerShapeGeometry"/>）。
    /// </para>
    /// </summary>
    public static class AreaTriggerShapeJson
    {
        /// <summary>解析失败（<c>kind</c> 缺失/非法）时抛 <see cref="ArgumentException"/>，不携带
        /// 数据表定位信息——调用方（<see cref="AreaTriggerDef.FromRecord"/>）在此基础上补充表名/
        /// 记录 key/字段名，转换为 <see cref="Core.Foundation.DataRegistry.DataFieldException"/>；
        /// 其它调用方（如陷阱装配）按普通 <see cref="ArgumentException"/> 处理即可。</summary>
        public static Shape Parse(JsonObject obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (!obj.TryGetValue("kind", out var kindValue) || !(kindValue is JsonString kindStr))
            {
                throw new ArgumentException("shape 缺少字符串 kind 字段");
            }

            var center = ParseCenter(obj);
            var rotation = GetNumber(obj, "rotation");

            switch (kindStr.Value)
            {
                case "circle":
                    return Shape.Circle(center, GetNumber(obj, "radius"));

                case "cone":
                    return Shape.Cone(center, rotation, GetNumber(obj, "angle"), GetNumber(obj, "radius"));

                case "line":
                    return Shape.Line(center, rotation, GetNumber(obj, "length"), GetNumber(obj, "width"));

                case "rect":
                    return Shape.Rect(center, new Vec2(GetNumber(obj, "length") / 2.0, GetNumber(obj, "width") / 2.0), rotation);

                default:
                    throw new ArgumentException($"shape.kind 非法取值 \"{kindStr.Value}\"，只允许 circle|cone|line|rect");
            }
        }

        private static Vec2 ParseCenter(JsonObject obj)
        {
            if (obj.TryGetValue("center", out var centerValue) && centerValue is JsonObject centerObj
                && centerObj.TryGetValue("x", out var xv) && xv is JsonNumber xn
                && centerObj.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                return new Vec2(xn.Value, yn.Value);
            }

            return Vec2.Zero;
        }

        private static double GetNumber(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonNumber n ? n.Value : 0.0;
    }
}

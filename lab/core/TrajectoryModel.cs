using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Lab
{
    /// <summary>一个速度矢量采样（起点、世界单位/秒的速度）。</summary>
    public sealed class VelocitySample
    {
        public int Tick { get; }

        public Vec2 Position { get; }

        public Vec2 Velocity { get; }

        internal VelocitySample(int tick, Vec2 position, Vec2 velocity)
        {
            Tick = tick;
            Position = position;
            Velocity = velocity;
        }
    }

    /// <summary>判定形状的视图：判定标记那一刻的轮廓，加上判定相内逐 tick 的扫掠轮廓。</summary>
    public sealed class HitShapeView
    {
        public HitShapeRecord Source { get; }

        public IReadOnlyList<Vec2> Outline { get; }

        /// <summary>扫掠体：判定相内每个 tick 的形状轮廓（形状模板在该 tick 的施法者位姿处重新锚定）；施法者不是玩家或没有判定相时为空。</summary>
        public IReadOnlyList<KeyValuePair<int, IReadOnlyList<Vec2>>> Sweep { get; }

        internal HitShapeView(HitShapeRecord source, IReadOnlyList<Vec2> outline, IReadOnlyList<KeyValuePair<int, IReadOnlyList<Vec2>>> sweep)
        {
            Source = source;
            Outline = outline;
            Sweep = sweep;
        }
    }

    /// <summary>一个接触点（来自命中确认事件）：接触点、法线（目标中心指向接触点）、裁决结果。</summary>
    public sealed class ContactView
    {
        public int Tick { get; }

        public string Attacker { get; }

        public string Target { get; }

        public Vec2 Point { get; }

        public Vec2 Normal { get; }

        public string Result { get; }

        internal ContactView(int tick, string attacker, string target, Vec2 point, Vec2 normal, string result)
        {
            Tick = tick;
            Attacker = attacker;
            Target = target;
            Point = point;
            Normal = normal;
            Result = result;
        }
    }

    /// <summary>目标辅助的转向：攻击方位置、被辅助到的靶子位置、转过的角度（度）。</summary>
    public sealed class AssistView
    {
        public int Tick { get; }

        public Vec2 From { get; }

        public Vec2 To { get; }

        public double DeltaDegrees { get; }

        internal AssistView(int tick, Vec2 from, Vec2 to, double deltaDegrees)
        {
            Tick = tick;
            From = from;
            To = to;
            DeltaDegrees = deltaDegrees;
        }
    }

    /// <summary>
    /// 轨迹叠层的视图模型（ADR-0150，手感设计 06 第 4 节"轨迹"）：移动路径、速度矢量、判定形状与扫掠体、接触点与法线、目标辅助转角。
    /// 纯 C#，只读录制；引擎侧把这些世界坐标折线叠到场景上画出来。
    /// <para>
    /// 判断记录（近似的边界）：位置与朝向取每个固定步结束时的样本，判定形状取"判定标记被派发那一刻"施法者的位姿（见 <see cref="HitShapeRecord"/>），
    /// 扫掠体是把形状模板重新锚定到判定相内每个 tick 结束时的位姿——它们是"这一击大致扫过哪里"的直观显示，不是命中判定本身的逐位复刻
    /// （命中判定在固定步内按当时位姿结算，面板不改它）。接触点与法线是命中确认事件携带的原值。
    /// </para>
    /// </summary>
    public static class TrajectoryModel
    {
        private const int CirclePoints = 24;
        private const int ArcPoints = 14;

        /// <summary>玩家位置路径（[<paramref name="fromTick"/>, <paramref name="toTick"/>] 内每个 tick 一个点）。</summary>
        public static List<Vec2> PlayerPath(LabRecording recording, int fromTick, int toTick)
        {
            var path = new List<Vec2>();
            foreach (var t in recording.Ticks)
            {
                if (t.Tick >= fromTick && t.Tick <= toTick)
                {
                    path.Add(t.Position);
                }
            }

            return path;
        }

        /// <summary>某靶子的位置路径（取手感记录里每 tick 的靶子位置）。</summary>
        public static List<Vec2> DummyPath(LabRecording recording, string label, int fromTick, int toTick)
        {
            var path = new List<Vec2>();
            var feel = recording.Feel;
            if (feel == null)
            {
                return path;
            }

            foreach (var t in feel.Ticks)
            {
                if (t.Tick < fromTick || t.Tick > toTick)
                {
                    continue;
                }

                foreach (var pair in t.TargetPositions)
                {
                    if (string.Equals(pair.Key, label, StringComparison.Ordinal))
                    {
                        path.Add(pair.Value);
                    }
                }
            }

            return path;
        }

        /// <summary>玩家速度矢量采样：每隔 <paramref name="every"/> 个 tick 取一个（后向差分：位移 / 步长）。</summary>
        public static List<VelocitySample> PlayerVelocities(LabRecording recording, int fromTick, int toTick, int every = 6)
        {
            var list = new List<VelocitySample>();
            var ticks = recording.Ticks;
            for (var i = 1; i < ticks.Count; i++)
            {
                var t = ticks[i];
                if (t.Tick < fromTick || t.Tick > toTick || (t.Tick - fromTick) % Math.Max(1, every) != 0)
                {
                    continue;
                }

                var d = t.Position - ticks[i - 1].Position;
                list.Add(new VelocitySample(t.Tick, t.Position, new Vec2(d.X / recording.StepSeconds, d.Y / recording.StepSeconds)));
            }

            return list;
        }

        /// <summary>判定形状（含扫掠体）：[<paramref name="fromTick"/>, <paramref name="toTick"/>] 内发生判定标记的每一次。</summary>
        public static List<HitShapeView> HitShapes(LabRecording recording, int fromTick, int toTick)
        {
            var result = new List<HitShapeView>();
            var feel = recording.Feel;
            if (feel == null)
            {
                return result;
            }

            foreach (var record in feel.HitShapes)
            {
                if (record.Tick < fromTick || record.Tick > toTick)
                {
                    continue;
                }

                var sweep = new List<KeyValuePair<int, IReadOnlyList<Vec2>>>();
                if (string.Equals(record.Actor, TimelineModel.PlayerLabel, StringComparison.Ordinal))
                {
                    var range = ActiveRange(feel, record);
                    foreach (var t in recording.Ticks)
                    {
                        if (t.Tick >= range.Key && t.Tick < range.Value)
                        {
                            var shape = ShapeGeometry.RebaseAt(record.Template, t.Position, t.Facing);
                            sweep.Add(new KeyValuePair<int, IReadOnlyList<Vec2>>(t.Tick, Outline(shape)));
                        }
                    }
                }

                result.Add(new HitShapeView(record, Outline(record.Shape), sweep));
            }

            return result;
        }

        /// <summary>该判定标记所在动作的判定相区间 [起, 止)：包含标记 tick 的那段 <c>Active</c> 相；找不到时退化为只含标记那一个 tick。</summary>
        private static KeyValuePair<int, int> ActiveRange(FeelRecording feel, HitShapeRecord record)
        {
            var start = -1;
            var inActive = false;
            var end = -1;
            foreach (var e in feel.Events)
            {
                if (!string.Equals(e.Actor, record.Actor, StringComparison.Ordinal)
                    || (e.Kind != "action_phase" && e.Kind != "action_finished" && e.Kind != "action_cancelled"))
                {
                    continue;
                }

                if (e.Tick <= record.Tick)
                {
                    inActive = e.Kind == "action_phase" && string.Equals(e.Detail, "Active", StringComparison.Ordinal);
                    start = e.Tick;
                }
                else
                {
                    end = e.Tick;
                    break;
                }
            }

            if (!inActive)
            {
                return new KeyValuePair<int, int>(record.Tick, record.Tick + 1);
            }

            return new KeyValuePair<int, int>(start, end < 0 ? record.Tick + 1 : end);
        }

        /// <summary>接触点与法线（命中确认事件携带；没有几何的事件跳过）。</summary>
        public static List<ContactView> Contacts(LabRecording recording, int fromTick, int toTick)
        {
            var list = new List<ContactView>();
            var feel = recording.Feel;
            if (feel == null)
            {
                return list;
            }

            foreach (var e in feel.Events)
            {
                if (e.Kind == "hit_confirmed" && e.HasGeometry && e.Tick >= fromTick && e.Tick <= toTick)
                {
                    list.Add(new ContactView(e.Tick, e.Actor, e.Target, e.Contact, e.Normal, e.Detail));
                }
            }

            return list;
        }

        /// <summary>目标辅助的转角：攻击方与被辅助靶子在该 tick 的位置，加转过的角度。</summary>
        public static List<AssistView> Assists(LabRecording recording, int fromTick, int toTick)
        {
            var list = new List<AssistView>();
            var feel = recording.Feel;
            if (feel == null)
            {
                return list;
            }

            foreach (var e in feel.Events)
            {
                if (e.Kind != "target_assisted" || e.Tick < fromTick || e.Tick > toTick)
                {
                    continue;
                }

                var from = PositionOf(recording, e.Actor, e.Tick);
                var to = PositionOf(recording, e.Target, e.Tick);
                if (from.HasValue && to.HasValue)
                {
                    list.Add(new AssistView(e.Tick, from.Value, to.Value, e.D));
                }
            }

            return list;
        }

        private static Vec2? PositionOf(LabRecording recording, string label, int tick)
        {
            if (string.Equals(label, TimelineModel.PlayerLabel, StringComparison.Ordinal))
            {
                foreach (var t in recording.Ticks)
                {
                    if (t.Tick == tick)
                    {
                        return t.Position;
                    }
                }

                return null;
            }

            var sample = recording.Feel == null ? null : TimelineModel.SampleAt(recording.Feel, tick);
            if (sample != null)
            {
                foreach (var pair in sample.TargetPositions)
                {
                    if (string.Equals(pair.Key, label, StringComparison.Ordinal))
                    {
                        return pair.Value;
                    }
                }
            }

            return null;
        }

        /// <summary>形状的闭合轮廓折线（世界坐标）：圆取 24 边形、扇形含顶点与弧线、线/矩形取四角。</summary>
        public static List<Vec2> Outline(Shape shape)
        {
            var points = new List<Vec2>();
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    for (var i = 0; i < CirclePoints; i++)
                    {
                        var a = 2.0 * Math.PI * i / CirclePoints;
                        points.Add(new Vec2(shape.Origin.X + shape.Radius * Math.Cos(a), shape.Origin.Y + shape.Radius * Math.Sin(a)));
                    }

                    break;
                case ShapeKind.Cone:
                    points.Add(shape.Origin);
                    for (var i = 0; i <= ArcPoints; i++)
                    {
                        var a = shape.Direction - shape.Angle / 2.0 + shape.Angle * i / ArcPoints;
                        points.Add(new Vec2(shape.Origin.X + shape.Radius * Math.Cos(a), shape.Origin.Y + shape.Radius * Math.Sin(a)));
                    }

                    break;
                case ShapeKind.Line:
                {
                    var fx = Math.Cos(shape.Direction);
                    var fy = Math.Sin(shape.Direction);
                    var nx = -fy * shape.Width / 2.0;
                    var ny = fx * shape.Width / 2.0;
                    var end = new Vec2(shape.Origin.X + fx * shape.Length, shape.Origin.Y + fy * shape.Length);
                    points.Add(new Vec2(shape.Origin.X + nx, shape.Origin.Y + ny));
                    points.Add(new Vec2(end.X + nx, end.Y + ny));
                    points.Add(new Vec2(end.X - nx, end.Y - ny));
                    points.Add(new Vec2(shape.Origin.X - nx, shape.Origin.Y - ny));
                    break;
                }

                case ShapeKind.Rect:
                {
                    var c = Math.Cos(shape.Rotation);
                    var s = Math.Sin(shape.Rotation);
                    var h = shape.HalfExtents;
                    foreach (var corner in new[] { new Vec2(-h.X, -h.Y), new Vec2(h.X, -h.Y), new Vec2(h.X, h.Y), new Vec2(-h.X, h.Y) })
                    {
                        points.Add(new Vec2(shape.Origin.X + corner.X * c - corner.Y * s, shape.Origin.Y + corner.X * s + corner.Y * c));
                    }

                    break;
                }
            }

            return points;
        }
    }
}

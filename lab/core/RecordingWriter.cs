using System.Collections.Generic;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>
    /// 把一次运行的完整记录（三条时间线）写成人读 JSON，供本地排查。记录文件只落本地输出目录
    /// （<c>.gitignore</c> 已忽略），不入库、不参与比较；入库与比较的只有脚本夹具与指纹基线。
    /// </summary>
    public static class RecordingWriter
    {
        public static string ToJson(LabRecording recording)
        {
            var ticks = new List<JsonValue>();
            foreach (var t in recording.Ticks)
            {
                ticks.Add(new JsonObjectBuilder()
                    .Add("tick", LabJson.Num(t.Tick))
                    .Add("pos", LabJson.Vec(t.Position))
                    .Add("facing", LabJson.Num(MetricSink.Round(t.Facing)))
                    .Add("state", LabJson.Str(t.MovementState))
                    .Add("alive", LabJson.Bool(t.Alive))
                    .Add("moveRequested", LabJson.Bool(t.MoveRequested))
                    .Add("axis", LabJson.Vec(t.MoveAxis))
                    .Build());
            }

            var intents = new List<JsonValue>();
            foreach (var i in recording.Intents)
            {
                intents.Add(new JsonObjectBuilder()
                    .Add("tick", LabJson.Num(i.Tick))
                    .Add("action", LabJson.Str(i.Action))
                    .Add("skill", LabJson.Str(i.SkillId))
                    .Build());
            }

            var events = new List<JsonValue>();
            foreach (var e in recording.Events)
            {
                events.Add(new JsonObjectBuilder()
                    .Add("tick", LabJson.Num(e.Tick))
                    .Add("kind", LabJson.Str(e.Kind))
                    .Add("source", LabJson.Str(e.Source))
                    .Add("target", LabJson.Str(e.Target))
                    .Add("skill", LabJson.Str(e.SkillId))
                    .Add("instance", LabJson.Num(e.Instance))
                    .Add("amount", LabJson.Num(MetricSink.Round(e.Amount)))
                    .Add("detail", LabJson.Str(e.Detail))
                    .Build());
            }

            var frames = new List<JsonValue>();
            foreach (var f in recording.Frames)
            {
                frames.Add(new JsonObjectBuilder()
                    .Add("frame", LabJson.Num(f.Frame))
                    .Add("time", LabJson.Num(MetricSink.Round(f.Time)))
                    .Add("ticksDone", LabJson.Num(f.TicksDone))
                    .Add("alpha", LabJson.Num(MetricSink.Round(f.Alpha)))
                    .Add("hasPose", LabJson.Bool(f.HasPose))
                    .Add("pos", LabJson.Vec(f.ViewPosition))
                    .Add("facing", LabJson.Num(MetricSink.Round(f.ViewFacingRadians)))
                    .Add("dirIndex", LabJson.Num(f.ViewDirectionIndex))
                    .Add("dirCount", LabJson.Num(f.ViewDirectionCount))
                    .Build());
            }

            var real = new JsonObjectBuilder()
                .Add("frameMs", new JsonArray(recording.Real.FrameMilliseconds.ConvertAll(v => (JsonValue)LabJson.Num(System.Math.Round(v, 4)))))
                .Add("frameAllocBytes", new JsonArray(recording.Real.FrameAllocatedBytes.ConvertAll(v => (JsonValue)LabJson.Num(v))))
                .Build();

            var dummies = new List<JsonValue>();
            foreach (var d in recording.Dummies)
            {
                dummies.Add(new JsonObjectBuilder().Add("name", LabJson.Str(d.Key)).Add("pos", LabJson.Vec(d.Value)).Build());
            }

            return LabJson.Write(new JsonObjectBuilder()
                .Add("script", LabJson.Str(recording.Script.Meta.ScriptId))
                .Add("cell", LabJson.Str(recording.Cell.Cell))
                .Add("stepSeconds", LabJson.Num(recording.StepSeconds))
                .Add("start", LabJson.Vec(recording.StartPosition))
                .Add("dummies", new JsonArray(dummies))
                .Add("logic", new JsonObjectBuilder()
                    .Add("ticks", new JsonArray(ticks))
                    .Add("intents", new JsonArray(intents))
                    .Add("events", new JsonArray(events))
                    .Build())
                .Add("presentation", new JsonObjectBuilder().Add("frames", new JsonArray(frames)).Build())
                .Add("realTime", real)
                .Build());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.InputMap;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>时间线块的作者态权威来源（手感设计/01 第 3.2 节）；运行期永远只读本块，本字段只影响校验器的比对口径。</summary>
    public enum TimelineSource
    {
        /// <summary>本块的数值是权威，剪辑里的对应标记由校验器比对，偏差超过容差给警告。</summary>
        Data,

        /// <summary>剪辑的标记是权威，内容导入工具把标记时间抄写进本块，校验器要求两者一致，不一致为错误。</summary>
        Clip,
    }

    /// <summary>资源扣除时刻（手感设计/01 第 3.1 节 <c>cost_at</c>）。</summary>
    public enum TimelineCostAt
    {
        /// <summary>动作开始（缺省）。</summary>
        Commit,

        /// <summary>进入判定相的那一 tick（本实现新增的取值，见 skill 模块 README 判断记录）。</summary>
        Active,

        /// <summary>首个 <c>hit</c> 标记到达的那一 tick（派生 <c>cost</c> 标记）。</summary>
        FirstHit,
    }

    /// <summary>冷却起算时刻（手感设计/01 第 3.1 节 <c>cooldown_at</c>）。</summary>
    public enum TimelineCooldownAt
    {
        /// <summary>动作开始（缺省）。</summary>
        Commit,

        /// <summary>进入判定相的那一 tick（本实现新增的取值）。</summary>
        Active,

        /// <summary>动作结束（自然结束或被取消/打断的那一刻）。</summary>
        Finish,
    }

    /// <summary>命中解析方式（手感设计/01 第 3.1 节、03 第 2.2 节）；本切片只登记字段与解析，空间命中由时间线命中切片消费。</summary>
    public enum TimelineHitPolicy
    {
        /// <summary>每个 <c>hit</c> 标记到达时解析一次目标并结算。</summary>
        Marker,

        /// <summary>判定相内逐 tick 解析（未实现，按 <see cref="Marker"/> 处理并记警告，见 README）。</summary>
        Continuous,
    }

    /// <summary>蓄力相声明（手感设计/01 第 3.1 节 <c>charge</c>）。</summary>
    public sealed class TimelineCharge
    {
        public double MinMs { get; }

        public double MaxMs { get; }

        public TimelineCharge(double minMs, double maxMs)
        {
            MinMs = minMs;
            MaxMs = maxMs;
        }
    }

    /// <summary>一个时间标记（手感设计/01 第 3.3 节）：<c>hit:&lt;段&gt;</c> 写法在解析时归一为名 <c>hit</c> + 参数 <c>segment</c>。</summary>
    public sealed class TimelineMarker
    {
        public string Name { get; }

        /// <summary>相对动作开始（不含蓄力）的毫秒数，未经速率重映射。</summary>
        public double AtMs { get; }

        public IReadOnlyDictionary<string, string> Args { get; }

        public TimelineMarker(string name, double atMs, IReadOnlyDictionary<string, string> args)
        {
            Name = name;
            AtMs = atMs;
            Args = args;
        }
    }

    /// <summary>一个取消窗口（手感设计/01 第 3.4 节）。</summary>
    public sealed class TimelineCancelWindow
    {
        public ActionClass Class { get; }

        public double OpenMs { get; }

        /// <summary>关闭时刻；null 表示到动作结束。</summary>
        public double? CloseMs { get; }

        public TimelineCancelWindow(ActionClass actionClass, double openMs, double? closeMs)
        {
            Class = actionClass;
            OpenMs = openMs;
            CloseMs = closeMs;
        }
    }

    /// <summary>连招接续块（手感设计/01 第 3.6 节）。</summary>
    public sealed class TimelineCombo
    {
        public Id Next { get; }

        public double OpenMs { get; }

        public double CloseMs { get; }

        public TimelineCombo(Id next, double openMs, double closeMs)
        {
            Next = next;
            OpenMs = openMs;
            CloseMs = closeMs;
        }
    }

    /// <summary>
    /// <c>skill.def.timeline</c> 块的解析结果（手感设计/01 第 3.1 节 <c>ActionTimeline</c>）。运行期只读本类型，不读剪辑
    /// （规则层不读表现域，手感设计/01 第 3.2 节）。未声明 <c>timeline</c> 的技能 <see cref="SkillDef.Timeline"/> 为 null，
    /// 行为与引入本块之前逐位一致。
    /// </summary>
    public sealed class TimelineDef
    {
        public TimelineSource Source { get; }

        public TimelineCharge? Charge { get; }

        public double StartupMs { get; }

        public double ActiveMs { get; }

        public double RecoveryMs { get; }

        public IReadOnlyList<TimelineMarker> Markers { get; }

        public IReadOnlyList<TimelineCancelWindow> CancelWindows { get; }

        public TimelineCombo? Combo { get; }

        public TimelineHitPolicy HitPolicy { get; }

        public TimelineCostAt CostAt { get; }

        public TimelineCooldownAt CooldownAt { get; }

        public ActionMotion? Motion { get; }

        /// <summary>当前动作层的手感覆盖行 id（<c>feel.action</c>），可空。</summary>
        public string? FeelRef { get; }

        /// <summary>三相之和（毫秒，不含蓄力）。</summary>
        public double TotalMs => StartupMs + ActiveMs + RecoveryMs;

        public TimelineDef(
            TimelineSource source,
            TimelineCharge? charge,
            double startupMs,
            double activeMs,
            double recoveryMs,
            IReadOnlyList<TimelineMarker> markers,
            IReadOnlyList<TimelineCancelWindow> cancelWindows,
            TimelineCombo? combo,
            TimelineHitPolicy hitPolicy,
            TimelineCostAt costAt,
            TimelineCooldownAt cooldownAt,
            ActionMotion? motion,
            string? feelRef)
        {
            Source = source;
            Charge = charge;
            StartupMs = startupMs;
            ActiveMs = activeMs;
            RecoveryMs = recoveryMs;
            Markers = markers ?? Array.Empty<TimelineMarker>();
            CancelWindows = cancelWindows ?? Array.Empty<TimelineCancelWindow>();
            Combo = combo;
            HitPolicy = hitPolicy;
            CostAt = costAt;
            CooldownAt = cooldownAt;
            Motion = motion;
            FeelRef = feelRef;
        }

        /// <summary><c>hit</c> 标记名。</summary>
        public const string HitMarker = "hit";

        /// <summary>解析 <c>skill.def.timeline</c> 的 JSON 对象（字段已经 schema 校验，这里只做结构化）。</summary>
        public static TimelineDef Parse(JsonObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            var source = Str(obj, "source") == "clip" ? TimelineSource.Clip : TimelineSource.Data;

            TimelineCharge? charge = null;
            if (obj.TryGetValue("charge", out var chargeVal) && chargeVal is JsonObject chargeObj)
            {
                charge = new TimelineCharge(Num(chargeObj, "min_ms"), Num(chargeObj, "max_ms"));
            }

            var markers = new List<TimelineMarker>();
            if (obj.TryGetValue("markers", out var markersVal) && markersVal is JsonArray markerArr)
            {
                var hitOrdinal = 0;
                for (var i = 0; i < markerArr.Count; i++)
                {
                    var m = (JsonObject)markerArr[i];
                    var rawName = Str(m, "name") ?? string.Empty;
                    var at = Num(m, "at_ms");
                    var args = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    if (m.TryGetValue("args", out var argsVal) && argsVal is JsonObject argsObj)
                    {
                        foreach (var kv in argsObj)
                        {
                            args[kv.Key] = ScalarText(kv.Value);
                        }
                    }

                    var name = rawName;
                    // 多段命中约定：剪辑事件名 hit:<segment>（手感设计/01 第 3.3 节"可多个，每个带 segment 序号"）。
                    if (rawName.StartsWith(HitMarker + ":", StringComparison.Ordinal))
                    {
                        name = HitMarker;
                        args["segment"] = rawName.Substring(HitMarker.Length + 1);
                    }

                    if (name == HitMarker)
                    {
                        if (!args.ContainsKey("segment"))
                        {
                            args["segment"] = hitOrdinal.ToString(CultureInfo.InvariantCulture);
                        }

                        hitOrdinal++;
                    }

                    markers.Add(new TimelineMarker(name, at, args));
                }
            }

            var windows = new List<TimelineCancelWindow>();
            if (obj.TryGetValue("cancel_windows", out var winVal) && winVal is JsonArray winArr)
            {
                for (var i = 0; i < winArr.Count; i++)
                {
                    var w = (JsonObject)winArr[i];
                    var cls = ParseActionClass(Str(w, "class")!);
                    double? close = w.TryGetValue("close_ms", out var cv) && cv is JsonNumber cn ? cn.Value : (double?)null;
                    windows.Add(new TimelineCancelWindow(cls, Num(w, "open_ms"), close));
                }
            }

            TimelineCombo? combo = null;
            if (obj.TryGetValue("combo", out var comboVal) && comboVal is JsonObject comboObj)
            {
                combo = new TimelineCombo(new Id(Str(comboObj, "next")!), Num(comboObj, "open_ms"), Num(comboObj, "close_ms"));
            }

            var hitPolicy = Str(obj, "hit_policy") == "continuous" ? TimelineHitPolicy.Continuous : TimelineHitPolicy.Marker;
            var costAt = Str(obj, "cost_at") switch
            {
                "active" => TimelineCostAt.Active,
                "first_hit" => TimelineCostAt.FirstHit,
                _ => TimelineCostAt.Commit,
            };
            var cooldownAt = Str(obj, "cooldown_at") switch
            {
                "active" => TimelineCooldownAt.Active,
                "finish" => TimelineCooldownAt.Finish,
                _ => TimelineCooldownAt.Commit,
            };

            ActionMotion? motion = null;
            if (obj.TryGetValue("motion", out var motionVal) && motionVal is JsonObject motionObj)
            {
                motion = ParseMotion(motionObj);
            }

            var feelRef = Str(obj, "feel_ref");

            return new TimelineDef(
                source, charge, Num(obj, "startup_ms"), Num(obj, "active_ms"), Num(obj, "recovery_ms"),
                markers, windows, combo, hitPolicy, costAt, cooldownAt, motion, string.IsNullOrEmpty(feelRef) ? null : feelRef);
        }

        private static ActionMotion ParseMotion(JsonObject m)
        {
            var driver = Str(m, "driver") == "root_motion" ? ActionMotionDriver.RootMotion : ActionMotionDriver.Code;
            var kind = Str(m, "kind") switch
            {
                "dash" => ActionMotionKind.Dash,
                "step_back" => ActionMotionKind.StepBack,
                "charge" => ActionMotionKind.Charge,
                _ => ActionMotionKind.Lunge,
            };
            var direction = Str(m, "direction") switch
            {
                "input_snapshot" => ActionMotionDirection.InputSnapshot,
                "toward_target" => ActionMotionDirection.TowardTarget,
                _ => ActionMotionDirection.Facing,
            };
            var blocking = Str(m, "blocking") == "slide" ? ActionMotionBlocking.Slide : ActionMotionBlocking.Stop;
            // max_turn_deg 缺省 180（不限制偏转）：只有 toward_target/charge 逐 tick 重算方向时才读它，charge 必须声明（校验规则）。
            var maxTurn = m.TryGetValue("max_turn_deg", out var mt) && mt is JsonNumber mtn ? mtn.Value : 180.0;
            var curve = Str(m, "curve");
            return new ActionMotion(
                driver, kind, Num(m, "distance"), string.IsNullOrEmpty(curve) ? "linear" : curve!, direction, maxTurn, blocking);
        }

        /// <summary>输入类别名（小写，同 <c>found.input_action.class</c> 取值）→ <see cref="ActionClass"/>。</summary>
        public static ActionClass ParseActionClass(string name)
        {
            switch (name)
            {
                case "move": return ActionClass.Move;
                case "attack": return ActionClass.Attack;
                case "skill": return ActionClass.Skill;
                case "dodge": return ActionClass.Dodge;
                case "interact": return ActionClass.Interact;
                case "item": return ActionClass.Item;
                case "menu": return ActionClass.Menu;
                default: throw new ArgumentException("未知输入类别：" + name, nameof(name));
            }
        }

        /// <summary><see cref="ActionClass"/> → 小写类别名。</summary>
        public static string ActionClassName(ActionClass actionClass)
        {
            switch (actionClass)
            {
                case ActionClass.Move: return "move";
                case ActionClass.Attack: return "attack";
                case ActionClass.Skill: return "skill";
                case ActionClass.Dodge: return "dodge";
                case ActionClass.Interact: return "interact";
                case ActionClass.Item: return "item";
                default: return "menu";
            }
        }

        private static string? Str(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonString s ? s.Value : null;

        private static double Num(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : 0.0;

        private static string ScalarText(JsonValue v)
        {
            switch (v)
            {
                case JsonString s: return s.Value;
                case JsonNumber n: return n.Value.ToString("R", CultureInfo.InvariantCulture);
                case JsonBool b: return b.Value ? "true" : "false";
                default: return string.Empty;
            }
        }
    }
}

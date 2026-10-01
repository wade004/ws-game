using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Sim;
using Presentation.Common;
using Presentation.ViewBinding;

namespace Lab
{
    /// <summary>无头实验室宿主的运行选项。</summary>
    public sealed class LabHostOptions
    {
        /// <summary>数据来源（框架根在前、实验室数据集在后，同 <c>HeadlessWorldOptions.DataSources</c>）。</summary>
        public IReadOnlyList<IDataSource> DataSources { get; set; } = Array.Empty<IDataSource>();

        public string PlayerClassId { get; set; } = "arch.class.lab_hero";

        public string PlayerFactionId { get; set; } = "fac.player";

        public string PlayerId { get; set; } = "unit.lab_player";

        public ulong Seed { get; set; } = 20261002UL;

        /// <summary>移动动作 id；宿主把它重绑到左摇杆以便注入任意模长的轴值（见 <see cref="LabHost"/> 判断记录）。</summary>
        public string MoveAction { get; set; } = "input.action.move";
    }

    /// <summary>
    /// 无头实验室宿主（06 第 2 节"无头宿主"）：用 <see cref="HeadlessWorldBuilder"/> 装出世界，再按引擎侧宿主引导代码
    /// 同一套逐步顺序驱动——桩输入 → 输入映射 → 移动请求/施放意图 → <c>Gameplay.Advance</c>——并经表现层公共部分的
    /// <see cref="ViewBinder"/> + 记录型假 View 采集表现时间线，经 Stopwatch/分配计数采集真实时间。
    /// <para>
    /// 判断记录（宿主逐步顺序）：照抄引擎侧宿主引导代码的固定步回调（<c>OnFixedStep</c>/<c>HandleFixedInput</c>）：
    /// ① <c>InputMap.Update(input)</c>；② 移动轴平方长 &gt; 0.0001 时每步重新提交一次方向移动请求（移动请求不持久，
    /// 须逐步重提）；③ 按钮上升沿提交一条 <c>cast</c> 意图（参数 <c>skill_id</c>，不带目标，由技能的目标链解析）；④
    /// <c>Gameplay.Advance(step)</c>。引擎侧的 <c>AppState</c>/节奏门判定在连续模式下恒通过，无头宿主不复刻。
    /// </para>
    /// <para>
    /// 判断记录（移动轴注入走左摇杆）：框架默认的移动绑定是键盘四向合成轴，只能产出 8 个单位向量，无法表达"小幅轴值"
    /// 这类标准脚本。宿主把移动动作重绑为 <c>pad_stick:left</c>，经 <see cref="StubInput.SetAxis"/> 注入任意模长的轴值；
    /// 输入映射对摇杆值不做归一化与死区处理（<c>InputMapHost.EvaluatePadStick</c>），归一化发生在
    /// <c>MovementTickHandler</c>——这正是实验室要度量的"当前行为"。
    /// </para>
    /// <para>
    /// 判断记录（空间索引位置同步）：<see cref="StubSpatialQuery"/> 的位置不会随实体移动自动更新（引擎侧由物理空间查询
    /// 适配器每个固定步同步）；宿主在每步 <c>Advance</c> 返回后把玩家位置写回空间索引，等价引擎侧"固定步后同步"。
    /// </para>
    /// <para>
    /// 判断记录（动作式格子）：时间线机制（<c>timeline</c> 块）尚未落地，动作式格子（<c>settlement: action</c>）在宿主里
    /// 与目标选择式格子行为一致——两者的差异只在数据集与预设（ADR-0122 决策 4），宿主不读取 <c>settlement</c> 之外的
    /// 呈现字段（<c>form</c>/<c>camera_mode</c>/<c>control_space</c>/<c>hit_shape</c>），只有 <c>facing</c> 决定表现时间线里
    /// 方向量化的档位。
    /// </para>
    /// </summary>
    public static class LabHost
    {
        /// <summary>桩适配层在本宿主上提供的能力集合（空：桩没有自由视角、体积扫掠等能力）。</summary>
        public static IReadOnlyCollection<string> AvailableCapabilities { get; } = Array.Empty<string>();

        /// <summary>数据集内容哈希：对全部数据来源的全部表文本（换行归一）求 SHA-256 前 16 位十六进制。</summary>
        public static string ComputeDatasetHash(IReadOnlyList<IDataSource> sources)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var parts = new List<string>();
                for (var s = 0; s < sources.Count; s++)
                {
                    var tables = new List<DataTableSource>(sources[s].ListTables());
                    tables.Sort((a, b) =>
                    {
                        var c = string.CompareOrdinal(a.TableName, b.TableName);
                        return c != 0 ? c : string.CompareOrdinal(a.Location, b.Location);
                    });
                    foreach (var table in tables)
                    {
                        var text = table.ReadText();
                        parts.Add(s + ":" + table.TableName + "\n" + text.Replace("\r\n", "\n"));
                    }
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\u0001", parts));
                var hash = sha.ComputeHash(bytes);
                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < 8; i++)
                {
                    sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>装一个只为读数据（格子、地形、靶子集）的世界。</summary>
        public static HeadlessWorld BuildProbe(LabHostOptions options) =>
            HeadlessWorldBuilder.Build(CreateWorldOptions(options, new Id("world.lab_arena"), Vec2.Zero, 0.02, null));

        private static HeadlessWorldOptions CreateWorldOptions(
            LabHostOptions options, Id mapId, Vec2 start, double stepSeconds, StubNavigation2D? navigation)
        {
            return new HeadlessWorldOptions
            {
                DataSources = options.DataSources,
                Seed = options.Seed,
                MapId = mapId,
                PlayerId = new Id(options.PlayerId),
                PlayerFactionId = new Id(options.PlayerFactionId),
                PlayerClassId = new Id(options.PlayerClassId),
                PlayerLevel = 1,
                PlayerSpawnPosition = start,
                GameId = new Id("game.lab"),
                StepSeconds = stepSeconds,
                EnableDiscreteTimeModel = true,
                Navigation = navigation,
            };
        }

        /// <summary>跑一份脚本在一个格子上的完整过程并返回三条时间线的记录。</summary>
        public static LabRecording Run(LabHostOptions options, LabScenario cell, InputScript script, LabCatalog? catalog = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            if (script == null) throw new ArgumentNullException(nameof(script));

            var runnability = cell.CheckRunnable(AvailableCapabilities);
            if (!runnability.Runnable)
            {
                throw new LabCellNotRunnableException(runnability);
            }

            var meta = script.Meta;
            var step = 1.0 / meta.TickRate;
            var nav = new StubNavigation2D();

            // 先用探针世界读出格子关联的地形与靶子集（它们在数据里，格子才知道地图 id）。
            catalog ??= new LabCatalog(BuildProbe(options).Registry);
            var arena = catalog.GetArena(cell.ArenaId);
            var dummySet = catalog.GetDummySet(cell.DummySetId);

            var rects = new List<Rect>(arena.Blocks.Count);
            foreach (var block in arena.Blocks)
            {
                rects.Add(new Rect(block.Min, block.Max));
            }

            nav.SetBlocking(arena.MapId, rects);

            var world = HeadlessWorldBuilder.Build(CreateWorldOptions(options, arena.MapId, meta.PlayerStart, step, nav));
            var playerId = world.Player.EntityId;
            var recording = new LabRecording(script, cell, step) { StartPosition = meta.PlayerStart };

            // 技能：按职业技能书的等级 1 全部学会（动作绑定到哪个技能由格子决定）。
            var classRecord = world.Registry.Get("arch.class", new Id(options.PlayerClassId))
                ?? throw new LabFormatException($"数据集里没有职业 {options.PlayerClassId}");
            if (classRecord.TryGetId("skill_book_ref", out var bookId))
            {
                world.Gameplay.Carriers.Rules.Skill.LearnFromBook(playerId, bookId, 1);
            }

            // 靶子：按脚本选择的分组出场，默认关闭 AI（保证可复现），保留 AI 的条目（巡逻靶）按数据声明。
            var labels = new Dictionary<Id, string> { { playerId, "player" } };
            var wanted = new HashSet<string>(meta.DummyGroups, StringComparer.Ordinal);
            foreach (var dummy in dummySet.Entries)
            {
                if (!wanted.Contains(dummy.Group))
                {
                    continue;
                }

                var count = string.Equals(dummy.Kind, "swarm", StringComparison.Ordinal) ? dummy.Count : 1;
                for (var i = 0; i < count; i++)
                {
                    var pos = count > 1
                        ? new Vec2(dummy.Position.X + (i - (count - 1) / 2.0) * dummy.Spacing, dummy.Position.Y)
                        : dummy.Position;
                    var label = count > 1 ? $"{dummy.Name}#{i + 1}" : dummy.Name;
                    var id = world.Gameplay.Carriers.Creatures.Spawn(dummy.CreatureId, arena.MapId, pos, Math.PI, null, 1);
                    world.Spatial.Register(id, pos, 0.5);
                    if (!dummy.KeepAi)
                    {
                        var ai = world.Gameplay.Carriers.Rules.Ai;
                        foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
                        {
                            if (registered.Equals(id))
                            {
                                ai.UnregisterUnit(id);
                                break;
                            }
                        }
                    }

                    labels[id] = label;
                    recording.Dummies.Add(new KeyValuePair<string, Vec2>(label, pos));
                }
            }

            // 输入：声明 found.input_action 全部动作，移动重绑到左摇杆。
            var input = new StubInput();
            var inputMap = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action"))
            {
                definitions.Add(ActionDefinition.FromRecord(record));
            }

            inputMap.DeclareActionSet(new Id("actionset.lab_input_action"), definitions);
            if (!inputMap.Rebind(options.MoveAction, "pad_stick:left"))
            {
                throw new LabFormatException($"移动动作 {options.MoveAction} 无法重绑到左摇杆");
            }

            // 表现：ViewBinder + 记录型假 View，只关心玩家那一个 View 的位姿。
            var directionCount = string.Equals(cell.Facing, "flip", StringComparison.Ordinal) ? 2 : 8;
            var factory = new RecordingViewFactory();
            var binder = new ViewBinder(
                world.Bus, factory, new WorldSimSnapshot(world.World), new DisplayInfoRegistry(world.Registry, world.Bus),
                new ViewBinderOptions(null, directionCount));
            binder.OnEntityCreated(playerId, world.Player.Kind, world.Player.TemplateId ?? playerId);

            // 脚本事件按 tick 分桶；同 tick 内保持脚本里的先后顺序。
            var byTick = new Dictionary<int, List<ScriptEvent>>();
            foreach (var e in script.Events)
            {
                if (!byTick.TryGetValue(e.Tick, out var list))
                {
                    list = new List<ScriptEvent>();
                    byTick[e.Tick] = list;
                }

                list.Add(e);
            }

            var bindings = new List<KeyValuePair<string, Id>>(cell.SkillBindings);
            var wasActive = new Dictionary<string, bool>(StringComparer.Ordinal);
            var instanceOrdinals = new Dictionary<Id, int>();
            var eventCursor = 0;
            var tick = 0;
            var duration = meta.DurationTicks;
            var frameDt = 1.0 / meta.FrameRateCap;
            var frame = 0;

            void ApplyScriptEvent(ScriptEvent e)
            {
                recording.InjectedInputs.Add(e);
                var first = FirstBinding(inputMap, e.Action);
                switch (e.Kind)
                {
                    case ScriptEventKind.Axis:
                        if (!first.StartsWith("pad_stick:", StringComparison.Ordinal))
                        {
                            throw new LabFormatException($"脚本轴事件的动作 {e.Action} 没有摇杆绑定（当前首绑定 {first}）");
                        }

                        var stick = first.Substring("pad_stick:".Length);
                        input.SetAxis(0, stick + "x", e.Value.X);
                        input.SetAxis(0, stick + "y", e.Value.Y);
                        break;
                    case ScriptEventKind.Press:
                        input.Press(KeyOf(first, e.Action));
                        break;
                    case ScriptEventKind.Release:
                        input.Release(KeyOf(first, e.Action));
                        break;
                }
            }

            void OnFixedStep(double stepSeconds)
            {
                if (tick >= duration)
                {
                    return;
                }

                if (byTick.TryGetValue(tick, out var events))
                {
                    foreach (var e in events)
                    {
                        ApplyScriptEvent(e);
                    }
                }

                inputMap.Update(input);

                var axis = inputMap.GetActionAxis(options.MoveAction);
                var moveRequested = axis.SqrLength > 0.0001;
                if (moveRequested)
                {
                    world.Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(playerId, axis));
                }

                foreach (var binding in bindings)
                {
                    var active = inputMap.IsActionActive(binding.Key);
                    var before = wasActive.TryGetValue(binding.Key, out var w) && w;
                    wasActive[binding.Key] = active;
                    if (active && !before)
                    {
                        var args = new JsonObjectBuilder().Add("skill_id", new JsonString(binding.Value.Value)).Build();
                        world.World.SubmitIntent(new Intent(playerId, "cast", args));
                        recording.Intents.Add(new CastIntentRecord(tick, binding.Key, binding.Value.Value));
                    }
                }

                world.Gameplay.Advance(stepSeconds);
                world.Spatial.UpdatePosition(playerId, world.Player.Position);

                recording.Ticks.Add(new TickSample(
                    tick, world.Player.Position, world.Player.Facing, world.Player.MovementState.Mode.ToString(), world.Player.Alive,
                    moveRequested ? axis : Vec2.Zero, moveRequested));

                while (eventCursor < world.Events.Count)
                {
                    RecordEvent(world.Events[eventCursor++], tick, labels, instanceOrdinals, recording);
                }

                tick++;
            }

            void OnFrame(double dt)
            {
                var alpha = world.Gameplay.InterpolationAlpha;
                binder.SyncAll(alpha < 0 ? 0 : alpha > 1 ? 1 : alpha);
                var view = factory.PlayerView;
                var continuous = string.Equals(cell.Facing, "continuous", StringComparison.Ordinal);
                recording.Frames.Add(view == null || !view.HasPose
                    ? new FrameSample(frame, frame * frameDt, tick, alpha, false, Vec2.Zero, 0, 0, 0)
                    : new FrameSample(
                        frame, frame * frameDt, tick, alpha, true, view.Position, view.Facing.RawRadians,
                        continuous ? 0 : view.Facing.Index, continuous ? 0 : view.Facing.DirectionCount));
                frame++;
            }

            var clock = new StubClock();
            clock.RequestFixedStep(step, OnFixedStep);
            clock.OnFrame(OnFrame);

            // 真实时间采样：每次帧推进（含其中触发的全部固定步与表现同步）一个样本。
            var watch = new Stopwatch();
            var guard = 0;
            var maxFrames = (int)Math.Ceiling(duration * step / frameDt) + 10 + duration;
            while (tick < duration)
            {
                if (++guard > maxFrames)
                {
                    throw new InvalidOperationException("实验室宿主在预期帧数内没有推进完全部固定步（时钟累加异常）");
                }

                var allocBefore = GC.GetAllocatedBytesForCurrentThread();
                watch.Restart();
                clock.Advance(frameDt);
                watch.Stop();
                recording.Real.FrameMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                recording.Real.FrameAllocatedBytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocBefore);
            }

            recording.TotalEventCount = world.Events.Count;
            binder.Dispose();
            return recording;
        }

        private static string FirstBinding(InputMapHost inputMap, string action)
        {
            var bindings = inputMap.GetBindings(action);
            if (bindings.Count == 0)
            {
                throw new LabFormatException($"脚本引用的动作 {action} 没有任何绑定（动作未声明？）");
            }

            return bindings[0];
        }

        private static string KeyOf(string firstBinding, string action)
        {
            if (!firstBinding.StartsWith("key:", StringComparison.Ordinal))
            {
                throw new LabFormatException($"脚本按钮事件的动作 {action} 首绑定不是键盘键：{firstBinding}");
            }

            return firstBinding.Substring("key:".Length);
        }

        private static void RecordEvent(
            IEvent evt, int tick, Dictionary<Id, string> labels, Dictionary<Id, int> instanceOrdinals, LabRecording recording)
        {
            string Label(Id id) => labels.TryGetValue(id, out var l) ? l : id.Value;
            int Ordinal(Id? id)
            {
                if (!id.HasValue)
                {
                    return 0;
                }

                if (!instanceOrdinals.TryGetValue(id.Value, out var n))
                {
                    n = instanceOrdinals.Count + 1;
                    instanceOrdinals[id.Value] = n;
                }

                return n;
            }

            switch (evt)
            {
                case SkillCastSuccessEvent s:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "cast_success", Label(s.CasterId), s.Targets.Count > 0 ? Label(s.Targets[0]) : string.Empty,
                        s.SkillId.Value, Ordinal(s.CastInstanceId), s.Targets.Count, s.IsInstant ? "instant" : "timed"));
                    break;
                case SkillCastFailedEvent f:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "cast_failed", Label(f.CasterId), string.Empty, f.SkillId.Value, Ordinal(f.CastInstanceId), 0,
                        f.ReasonCode.ToString()));
                    break;
                case CombatDamageDealtEvent d:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "damage", Label(d.SourceId), Label(d.TargetId), d.SkillId?.Value ?? string.Empty,
                        Ordinal(d.AttackInstanceId), d.Amount, d.HitResult.ToString()));
                    break;
                case CombatAttackAvoidedEvent a:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "avoided", Label(a.SourceId), Label(a.TargetId), a.SkillId?.Value ?? string.Empty,
                        Ordinal(a.AttackInstanceId), 0, a.HitResult.ToString()));
                    break;
                case UnitDiedEvent u:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "died", u.KillerId.HasValue ? Label(u.KillerId.Value) : string.Empty, Label(u.UnitId), string.Empty, 0, 0,
                        string.Empty));
                    break;
            }
        }

        private sealed class RecordingView : IView
        {
            public Id EntityId { get; private set; }

            public bool IsAlive { get; private set; }

            public bool HasPose { get; private set; }

            public Vec2 Position { get; private set; }

            public Direction Facing { get; private set; }

            public void Bind(Id entityId)
            {
                EntityId = entityId;
                IsAlive = true;
            }

            public void OnEvent(IEvent evt)
            {
            }

            public void SyncPose(Vec2 pos, Direction facing, double height)
            {
                HasPose = true;
                Position = pos;
                Facing = facing;
            }

            public void Destroy()
            {
                IsAlive = false;
            }
        }

        private sealed class RecordingViewFactory : IViewFactory
        {
            private readonly Dictionary<Id, RecordingView> _views = new Dictionary<Id, RecordingView>();

            public RecordingView? PlayerView { get; private set; }

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                var view = new RecordingView();
                _views[entityId] = view;
                if (kind == ViewKind.Unit && PlayerView == null)
                {
                    // 第一个被创建的单位视图是玩家（宿主在靶子出场之前手动创建了玩家视图）。
                    PlayerView = view;
                }

                return view;
            }
        }
    }

    /// <summary>格子在当前适配层上不可运行（预留空间模型或缺能力）：显式抛出，不静默跳过。</summary>
    public sealed class LabCellNotRunnableException : Exception
    {
        public CellRunnability Runnability { get; }

        public LabCellNotRunnableException(CellRunnability runnability)
            : base(runnability.ToString())
        {
            Runnability = runnability;
        }
    }
}

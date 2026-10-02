// FeelRigFreezeWiringTests：手感落地 M2-A——命中顿帧经生产装配（HeadlessWorldBuilder → GameplayAssembly → PresentationAssembly）后，
// 落到各单位视图的渲染 rig（sprite 与 model 两类）：攻击方/被击方 rig 的"表现冻结"持续的 tick 数 = 顿帧档案毫秒按标定步长换算的值，
// 不在名单里的旁观单位不冻结；手感关闭时同一场景不产生任何冻结。期望值全部由手感解析出的档案毫秒与标定 tick 率算出，不写死裸数。
// 注：本测试不依赖任何引擎；引擎侧（Unity）同样的链路由 Adapter.Unity.Tests.Runtime 的 FeelEngineWiringTests 另行覆盖。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SceneRouter;
using Core.Rules.Common;
using Core.Sim;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.Presentation.Assembly
{
    public sealed class FeelRigFreezeWiringTests : IDisposable
    {
        private const double Dt = 0.02;
        private const string Calibration = "feel.calibration.framework_default";

        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id SwingSkill = new Id("skill.m2a_swing");

        // 时间线技能：前摇 100 ms、有效 60 ms、后摇 240 ms，hit 标记落在有效帧起点（与核心侧手感端到端用例同一形状）。
        private const string SkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [ {
    ""id"": ""skill.m2a_swing"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0.4,
    ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
    ""timeline"": { ""startup_ms"": 100, ""active_ms"": 60, ""recovery_ms"": 240, ""markers"": [ { ""name"": ""hit"", ""at_ms"": 100 } ] },
    ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""scaling"": [ { ""stat"": ""stat.attack_power"", ""coefficient"": 1.0 } ], ""school"": ""school.physical"" } } ]
  } ]
}";

        private const string ActionJson = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [ { ""key"": ""input.action.m2a_attack"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
                ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_0"" } ]
}";

        // 打击反馈包：(medium, hit) 变体带镜头冲量（增益 0.5，衰减 120 ms）；打击类别由手感缺省给出 medium，档案引用靠调试覆盖挂到玩家。
        private const double ProfileImpulseGain = 0.5;
        private const double ProfileDecayMs = 120;
        private const string ProfileId = "feedback.impact_profile.m2a";
        private const string ImpactProfileJson = @"{
  ""table"": ""feedback.impact_profile"", ""schema_version"": 1,
  ""rows"": [ { ""id"": ""feedback.impact_profile.m2a"", ""variants"": [
    { ""class"": ""medium"", ""outcome"": ""hit"", ""camera"": { ""impulse_gain"": 0.5, ""decay_ms"": 120 } } ] } ]
}";

        // 命中确认事件 → 播放打击反馈包（生产数据的写法：游戏的反馈规则里挂 play_impact，档案引用来自手感字段）。
        private const string ImpactBindingJson = @"{
  ""table"": ""feedback.binding"", ""schema_version"": 1,
  ""rows"": [ { ""id"": ""feedback.m2a_impact"", ""event"": ""combat.hit_confirmed"", ""actions"": [ { ""kind"": ""play_impact"", ""params"": {} } ] } ]
}";

        /// <summary>支持镜头冲击能力的记录相机（引擎适配层 ICameraImpulse 的无引擎替身）：只记录冲击调用，其余转给桩相机。</summary>
        private sealed class ImpulseRecordingCamera : ICamera, ICameraImpulse
        {
            private readonly StubCamera _inner = new StubCamera();
            public readonly List<(Vec2 Direction, double Magnitude, double DecayMs)> Impulses = new List<(Vec2, double, double)>();
            public bool SupportsCameraImpulse => true;
            public void Impulse(Vec2 direction, double magnitude, double decayMs) => Impulses.Add((direction, magnitude, decayMs));
            public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange) => _inner.Configure(pitchDegrees, yawDegrees, zoomRange);
            public void Follow(Vec2 planePos, double smoothing) => _inner.Follow(planePos, smoothing);
            public void SetZoom(double zoom) => _inner.SetZoom(zoom);
            public Vec2 WorldToScreen(Vec2 planePos, double height) => _inner.WorldToScreen(planePos, height);
            public Vec2? ScreenToWorld(Vec2 screen) => _inner.ScreenToWorld(screen);
            public void Shake(double intensity, double durationSeconds, double frequency) => _inner.Shake(intensity, durationSeconds, frequency);
        }

        private static DisplayInfo SpriteInfo() =>
            new DisplayInfo(
                new Id("display.m2a_sprite"), DisplayCategory.Creature, new Id("creature.m2a_sprite"), DisplayKind.Sprite,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.m2a", 8, mirrorPairs: null, paperdollLayers: null, anchorPoints: null), null);

        private static DisplayInfo ModelDisplay() =>
            new DisplayInfo(
                new Id("display.m2a_model"), DisplayCategory.Creature, new Id("creature.m2a_model"), DisplayKind.Model,
                null, null, null, 1.0, Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null, null,
                new ModelInfo(new Id("model.m2a"), new Id("display.anim_set.m2a")));

        /// <summary>每个实体一个真实 rig 的视图：玩家用 sprite rig，其余用 model rig（两类 rig 都要被顿帧冻到）。</summary>
        private sealed class RigView : IView, IHasCharacterRig
        {
            public ICharacterRig Rig { get; }
            public Id EntityId { get; private set; }
            public bool IsAlive { get; private set; }

            public RigView(ICharacterRig rig) => Rig = rig;

            public void Bind(Id entityId) { EntityId = entityId; IsAlive = true; }
            public void OnEvent(IEvent evt) { }
            public void SyncPose(Vec2 pos, Direction facing, double height) { }
            public void Destroy() => IsAlive = false;
        }

        private sealed class RigViewFactory : IViewFactory
        {
            private readonly StubRenderer2D _r2 = new StubRenderer2D();
            private readonly StubRenderer3D _r3 = new StubRenderer3D();
            public readonly Dictionary<Id, IPresentationFreezable> Rigs = new Dictionary<Id, IPresentationFreezable>();

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                ICharacterRig rig;
                if (entityId.Equals(PlayerId))
                {
                    var handle = _r2.CreateSpriteInstance(new Id("sprite.m2a"));
                    rig = new SpriteCharacterRig(
                        entityId, _r2, handle, new RenderConventionHost(), SpriteInfo(),
                        resourceTracker: null, frameAnimPlayer: null, options: null);
                }
                else
                {
                    var handle = _r3.CreateModelInstance(new Id("model.m2a"));
                    rig = new ModelCharacterRig(entityId, _r3, handle, ModelDisplay());
                }

                Rigs[entityId] = (IPresentationFreezable)rig;
                return new RigView(rig);
            }
        }

        private readonly HeadlessWorld _world;
        private readonly PresentationAssembly _presentation;
        private readonly RigViewFactory _factory = new RigViewFactory();
        private readonly ImpulseRecordingCamera _camera = new ImpulseRecordingCamera();
        private readonly StubInput _input = new StubInput();
        private readonly List<IEvent> _log = new List<IEvent>();
        private int _cursor;

        public FeelRigFreezeWiringTests() : this(feel: true) { }

        private FeelRigFreezeWiringTests(bool feel)
        {
            var fs = new StubFileSystem();
            CopyDisk(fs, "data/_framework");
            CopyDisk(fs, "data/_feel");
            CopyDisk(fs, "data/_lab");
            fs.WriteTextAtomic("test/_m2a/skill/skill.def.json", SkillJson);
            fs.WriteTextAtomic("test/_m2a/found/found.input_action.json", ActionJson);
            fs.WriteTextAtomic("test/_m2a/feedback/feedback.impact_profile.json", ImpactProfileJson);
            fs.WriteTextAtomic("test/_m2a/feedback/feedback.binding.json", ImpactBindingJson);

            _world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_m2a"),
                },
                FileSystem = fs,
                Seed = 20261002UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.m2a"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = feel ? new CarriersFeelOptions { CalibrationId = Calibration } : null,
            });
            Assert.False(_world.LoadReport.IsBlocking, string.Join("\n", _world.LoadReport.Issues));

            var engine = new StubEngine();
            var sceneRouter = new SceneRouter(
                _world.Registry, engine.ResourceLoader, _world.Gameplay.AppState, _world.World, _world.Gameplay.Hooks, _world.Bus);
            _presentation = new PresentationAssembly(
                _world.Gameplay, _world.World, _world.Registry, _world.Bus, new RngHost(2),
                _factory, engine.Renderer2D, _camera, engine.Audio, engine.FileSystem, sceneRouter);

            var definitions = new List<ActionDefinition>();
            foreach (var record in _world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            _presentation.InputMap.DeclareActionSet(new Id("actionset.m2a"), definitions);

            _world.Gameplay.Carriers.Rules.Skill.LearnSkill(PlayerId, SwingSkill);
            Assert.True(_world.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_0", SwingSkill));

            // 玩家在表现装配构造之前已创建，补一次视图绑定（与其它装配测试同一做法）。
            _presentation.ViewBinder.OnEntityCreated(PlayerId, "player", new Id("display.map.lab_player"));
        }

        public void Dispose() => _presentation.Dispose();

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException());
            for (var i = 0; i < 3; i++) dir = dir.Parent ?? throw new InvalidOperationException();
            return dir.FullName;
        }

        private static void CopyDisk(StubFileSystem fs, string relativeRoot)
        {
            var absoluteRoot = Path.Combine(FindRepoRoot(), relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.GetFiles(absoluteRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(absoluteRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic(relativeRoot + "/" + rel, File.ReadAllText(file));
            }
        }

        private Id SpawnDummy(Vec2 pos)
        {
            var id = _world.Gameplay.Carriers.Creatures.Spawn(StakeTemplate, MapId, pos, Math.PI, null, 1);
            _world.Spatial.Register(id, pos, 0.5);
            var ai = _world.Gameplay.Carriers.Rules.Ai;
            foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
            {
                if (registered.Equals(id)) { ai.UnregisterUnit(id); break; }
            }

            return id;
        }

        private void Step()
        {
            _presentation.InputMap.Update(_input);
            _world.Gameplay.Advance(Dt);
            _world.Spatial.UpdatePosition(PlayerId, _world.Player.Position);
            while (_cursor < _world.Events.Count) _log.Add(_world.Events[_cursor++]);
        }

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        /// <summary>跑 <paramref name="ticks"/> 个固定步，逐 tick 记录各 rig 是否处于冻结。</summary>
        private Dictionary<Id, List<bool>> Observe(IEnumerable<Id> ids, int ticks)
        {
            var traces = ids.ToDictionary(i => i, _ => new List<bool>());
            for (var i = 0; i < ticks; i++)
            {
                Step();
                foreach (var pair in traces) pair.Value.Add(_factory.Rigs[pair.Key].IsPresentationFrozen);
            }

            return traces;
        }

        [Fact]
        public void Hit_FreezesAttackerAndTargetRigsByProfileTicks_BystanderUnaffected()
        {
            var target = SpawnDummy(new Vec2(1.5, 0));
            var bystander = SpawnDummy(new Vec2(60, 60));
            Step(); // 实体创建事件在固定步末尾派发，表现装配据此建视图
            Assert.True(
                _factory.Rigs.ContainsKey(PlayerId) && _factory.Rigs.ContainsKey(target) && _factory.Rigs.ContainsKey(bystander),
                "表现装配应已为各单位建视图");

            // 攻击方与被击方的顿帧毫秒调成明显不同的两档，避免相等时测不出谁冻了多久（同核心侧手感端到端用例的做法）。
            var feel = _world.Gameplay.Feel!;
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(50)));
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Set, FeelValue.Of(110)));
            var judging = feel.Resolver.ResolveJudging(PlayerId);
            var attackerTicks = Ticks(judging.GetNumber(FeelFieldNames.AttackerHitstopMs));
            var targetTicks = Ticks(judging.GetNumber(FeelFieldNames.TargetHitstopMs));
            Assert.True(attackerTicks > 0 && targetTicks > 0 && attackerTicks != targetTicks);

            _input.Press("j");
            Step();
            _input.Release("j");
            var traces = Observe(new[] { PlayerId, target, bystander }, Ticks(100 + 60 + 240) + 20);

            var hit = _log.OfType<CombatHitConfirmedEvent>().Single();
            Assert.Equal(attackerTicks, hit.AttackerHitStopTicks);
            Assert.Equal(targetTicks, hit.TargetHitStopTicks);
            Assert.Equal(attackerTicks, traces[PlayerId].Count(f => f));
            Assert.Equal(targetTicks, traces[target].Count(f => f));
            Assert.Equal(0, traces[bystander].Count(f => f));

            // 不变量：冻结是连续的一段，结束后全部恢复（没有残留冻结）。
            foreach (var id in new[] { PlayerId, target })
            {
                var t = traces[id];
                var first = t.IndexOf(true);
                Assert.True(first >= 0);
                Assert.True(t.Skip(first).TakeWhile(f => f).Count() == t.Count(f => f), "冻结应为连续一段");
                Assert.False(_factory.Rigs[id].IsPresentationFrozen);
            }
        }

        [Fact]
        public void Hit_TriggersCameraImpulse_MagnitudeFromProfileAndFeel()
        {
            var target = SpawnDummy(new Vec2(1.5, 0));
            var feel = _world.Gameplay.Feel!;
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.ImpactProfileRef, FeelOp.Set, FeelValue.Of(ProfileId)));
            Assert.Empty(_camera.Impulses);

            _input.Press("j");
            Step();
            _input.Release("j");
            Observe(new[] { PlayerId }, Ticks(100 + 60 + 240) + 20);

            // 期望值由规则算出：min(角色基础冲量增益 × 变体增益 × 强度因子(无曲线/非暴击/非击杀 = 1), 震屏上限)。
            var presenting = feel.Resolver.ResolvePresenting(PlayerId);
            var baseGain = presenting.GetRaw(FeelFieldNames.CameraImpulseGain).AsNumber();
            var cap = presenting.GetRaw(FeelFieldNames.CameraShakeCap).AsNumber();
            var expected = Math.Min(baseGain * ProfileImpulseGain, cap);
            Assert.True(expected > 0);

            var impulse = Assert.Single(_camera.Impulses);
            Assert.Equal(expected, impulse.Magnitude, 9);
            Assert.Equal(ProfileDecayMs, impulse.DecayMs, 9);
            // 不变量：方向是单位向量或零向量，幅度不超过上限。
            Assert.True(impulse.Direction.Length < 1e-9 || Math.Abs(impulse.Direction.Length - 1.0) < 1e-9);
            Assert.True(impulse.Magnitude <= cap + 1e-12);
        }

        [Fact]
        public void FeelOff_SameScene_NothingFreezes()
        {
            // 不开手感：没有输入缓冲，普攻改由游戏侧直接施法（旧路径）；命中后没有顿帧，任何 rig 都不冻结。
            using var off = new FeelRigFreezeWiringTests(feel: false);
            Assert.Null(off._world.Gameplay.Feel);
            var target = off.SpawnDummy(new Vec2(1.5, 0));
            var cast = off._world.Gameplay.Carriers.Rules.Skill.CastSkill(PlayerId, SwingSkill, new[] { target });
            Assert.True(cast.Success, cast.Reason.ToString());

            var traces = off.Observe(new[] { PlayerId, target }, Ticks(100 + 60 + 240) + 20);

            Assert.Equal(0, traces[PlayerId].Count(f => f));
            Assert.Equal(0, traces[target].Count(f => f));
        }
    }
}

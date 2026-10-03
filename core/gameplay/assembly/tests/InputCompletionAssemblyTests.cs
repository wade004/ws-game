// InputCompletionAssemblyTests：输入层补全（ADR-0143）经生产装配（HeadlessWorldBuilder → GameplayAssembly → Carriers/Rules）的运行时冒烟。
// 覆盖：点按/按住变体、蓄力自动释放与下限门槛（技能 timeline.charge 经装配注入输入缓冲）、跳跃输入类（跳跃缓冲、土狼时间、可变跳高）、
// AI 施法经输入缓冲（可选开关，缺省关）。期望值全部由数据字段、档案毫秒与标定 tick 率算出，不写死裸数；每个用例带"量从 X 变到 Y"的复现或不变量。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Sim;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    public sealed class InputCompletionAssemblyTests
    {
        private const double Dt = 0.02;
        private const string FrameworkCalibration = "feel.calibration.framework_default";
        private const double JumpHeight = 1.5;
        private const double Gravity = 30.0;
        private const double JumpCut = 0.5;
        private const double HoldThresholdMs = 300;
        private const double ChargeMinMs = 400;
        private const double ChargeMaxMs = 1000;

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id TapSkill = new Id("skill.m5_tap");
        private static readonly Id HoldSkill = new Id("skill.m5_hold");
        private static readonly Id ChargeSkill = new Id("skill.m5_charge");
        private static readonly Id ChargeCancelSkill = new Id("skill.m5_charge_cancel");
        private static readonly Id LongSkill = new Id("skill.m5_long");
        private static readonly Id AiSkill = new Id("skill.m5_ai");

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        // ------------------------------------------------------------------ 数据

        private const string SkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""skill.m5_tap"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
    { ""id"": ""skill.m5_hold"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
    { ""id"": ""skill.m5_charge"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.2,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""timeline"": { ""startup_ms"": 100, ""active_ms"": 40, ""recovery_ms"": 60, ""markers"": [ { ""name"": ""hit"", ""at_ms"": 120 } ],
        ""charge"": { ""min_ms"": 400, ""max_ms"": 1000 } },
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
    { ""id"": ""skill.m5_charge_cancel"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.2,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""timeline"": { ""startup_ms"": 100, ""active_ms"": 40, ""recovery_ms"": 60, ""markers"": [ { ""name"": ""hit"", ""at_ms"": 120 } ],
        ""charge"": { ""min_ms"": 400, ""max_ms"": 1000, ""below_min"": ""cancel"" } },
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
    { ""id"": ""skill.m5_ai"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0,
      ""cooldown_duration"": 0, ""respects_gcd"": true, ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
    { ""id"": ""skill.m5_long"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 1.0,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""timeline"": { ""startup_ms"": 100, ""active_ms"": 100, ""recovery_ms"": 800, ""markers"": [ { ""name"": ""hit"", ""at_ms"": 150 } ] },
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] }
  ]
}";

        private const string ActionJson = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.action.m5_attack"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 200, ""hold_threshold_ms"": 300, ""skill_slot"": ""slot_tap"", ""hold_skill_slot"": ""slot_hold"" },
    { ""key"": ""input.action.m5_charge"", ""kind"": ""button"", ""default_bindings"": [ ""key:k"" ], ""rebind_group"": ""default"",
      ""class"": ""skill"", ""buffer_ms"": 200, ""hold_threshold_ms"": 100, ""skill_slot"": ""slot_charge"" },
    { ""key"": ""input.action.m5_charge_cancel"", ""kind"": ""button"", ""default_bindings"": [ ""key:l"" ], ""rebind_group"": ""default"",
      ""class"": ""skill"", ""buffer_ms"": 200, ""hold_threshold_ms"": 100, ""skill_slot"": ""slot_charge_cancel"" },
    { ""key"": ""input.action.m5_jump"", ""kind"": ""button"", ""default_bindings"": [ ""key:space"" ], ""rebind_group"": ""default"",
      ""class"": ""jump"", ""buffer_ms"": 200, ""jump_cut_ratio"": 0.5, ""grace_conditions"": [ ""input.grace.builtin_grounded"" ] },
    { ""key"": ""input.action.m5_jump_plain"", ""kind"": ""button"", ""default_bindings"": [ ""key:x"" ], ""rebind_group"": ""default"",
      ""class"": ""jump"", ""buffer_ms"": 200 }
  ]
}";

        // ------------------------------------------------------------------ 装配

        private sealed class Rig
        {
            public HeadlessWorld World = null!;
            public StubInput Input = new StubInput();
            public InputMapHost Map = null!;
            public int Tick;
            public readonly List<(int Tick, IEvent Event)> Log = new List<(int, IEvent)>();
            private int _cursor;

            public CarriersFeelSystem Feel => World.Gameplay.Feel!;
            public SkillHost Skills => World.Gameplay.Carriers.Rules.Skill;
            public IVerticalMotion Vertical => World.Gameplay.Carriers.VerticalMotion!;
            public double Height => World.Player.HeightOffset;

            public void Step()
            {
                Map.Update(Input);
                World.Gameplay.Advance(Dt);
                World.Spatial.UpdatePosition(PlayerId, World.Player.Position);
                while (_cursor < World.Events.Count) Log.Add((Tick, World.Events[_cursor++]));
                Tick++;
            }

            public void Run(int ticks)
            {
                for (var i = 0; i < ticks; i++) Step();
            }

            public IEnumerable<(int Tick, T Event)> Of<T>() where T : IEvent =>
                Log.Where(l => l.Event is T).Select(l => (l.Tick, (T)l.Event));

            /// <summary>瞬发技能的施放（<c>skill.cast_success</c>）。</summary>
            public IEnumerable<(int Tick, SkillCastSuccessEvent Event)> Casts(Id skill) =>
                Of<SkillCastSuccessEvent>().Where(e => e.Event.SkillId.Equals(skill));
        }

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException());
            for (var i = 0; i < 4; i++) dir = dir.Parent ?? throw new InvalidOperationException();
            return dir.FullName;
        }

        private static void CopyDisk(StubFileSystem fs, string repoRoot, string relativeRoot)
        {
            var absoluteRoot = Path.Combine(repoRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.GetFiles(absoluteRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(absoluteRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic(relativeRoot + "/" + rel, File.ReadAllText(file));
            }
        }

        /// <summary>平台（x &lt; 5，高 3）与地面（其余，高 0）：玩家站在平台上，往 x &gt; 5 走出去就是边缘下落。</summary>
        private static Rig Build(bool withTerrain = false, bool aiThroughBuffer = false, Action<VerticalAxisOptions>? configureVertical = null)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            fs.WriteTextAtomic("test/_m5/skill/skill.def.json", SkillJson);
            fs.WriteTextAtomic("test/_m5/found/found.input_action.json", ActionJson);

            var vertical = new VerticalAxisOptions { Gravity = Gravity, JumpHeight = JumpHeight };
            if (withTerrain)
            {
                var terrain = new MapTerrainHeights();
                terrain.SetRegions(MapId, new[] { new TerrainRegion(new Vec2(-1000, -1000), new Vec2(5, 1000), ground: 3.0) });
                vertical.Terrain = terrain;
                vertical.StepHeight = 0.5;
            }

            configureVertical?.Invoke(vertical);

            var worldOptions = new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_m5"),
                },
                FileSystem = fs,
                Seed = 20261004UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.lab"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = new CarriersFeelOptions { CalibrationId = FrameworkCalibration, AiIntentsThroughBuffer = aiThroughBuffer },
                MovementOptions = new MovementOptions { Vertical = vertical },
            };
            var world = HeadlessWorldBuilder.Build(worldOptions);
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));
            Assert.Equal(0, world.LoadReport.ErrorCount);

            var rig = new Rig { World = world };
            rig.Map = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Map.DeclareActionSet(new Id("actionset.m5"), definitions);
            world.Gameplay.Feel!.InputBuffer.BindLocalInput(rig.Map, PlayerId, null);

            var skills = world.Gameplay.Carriers.Rules.Skill;
            foreach (var skill in new[] { TapSkill, HoldSkill, ChargeSkill, ChargeCancelSkill, LongSkill, AiSkill }) skills.LearnSkill(PlayerId, skill);
            var bindings = world.Gameplay.Carriers.SkillBindings;
            Assert.True(bindings.Bind(PlayerId, "slot_tap", TapSkill));
            Assert.True(bindings.Bind(PlayerId, "slot_hold", HoldSkill));
            Assert.True(bindings.Bind(PlayerId, "slot_charge", ChargeSkill));
            Assert.True(bindings.Bind(PlayerId, "slot_charge_cancel", ChargeCancelSkill));
            return rig;
        }

        private static void Press(Rig rig, string key) => rig.Input.Press(key);

        private static void Release(Rig rig, string key) => rig.Input.Release(key);

        // ------------------------------------------------------------------ 1. 点按 / 按住变体

        [Fact]
        public void TapHoldVariant_TapCastsTheTapSkill_HoldPastThresholdCastsTheHoldSkill()
        {
            var thresholdTicks = Ticks(HoldThresholdMs);

            // 复现：点按 → 点按技能；没有按住技能的施放。
            var tap = Build();
            Press(tap, "j");
            tap.Step();
            Release(tap, "j");
            tap.Run(thresholdTicks + 10);
            Assert.Single(tap.Casts(TapSkill));
            Assert.Empty(tap.Casts(HoldSkill));

            // 同一个动作按住超过阈值再抬起 → 按住技能。
            var hold = Build();
            Press(hold, "j");
            hold.Run(thresholdTicks + 3);
            Release(hold, "j");
            hold.Run(10);
            Assert.Single(hold.Casts(HoldSkill));
            Assert.Empty(hold.Casts(TapSkill));
        }

        [Fact]
        public void TapHoldVariant_ThresholdBoundary_JustUnderIsTapJustOverIsHold()
        {
            var thresholdTicks = Ticks(HoldThresholdMs);

            var under = Build();
            Press(under, "j");
            under.Run(thresholdTicks - 1);
            Release(under, "j");
            under.Run(10);
            Assert.Single(under.Casts(TapSkill));
            Assert.Empty(under.Casts(HoldSkill));

            var over = Build();
            Press(over, "j");
            over.Run(thresholdTicks + 1);
            Release(over, "j");
            over.Run(10);
            Assert.Single(over.Casts(HoldSkill));
            Assert.Empty(over.Casts(TapSkill));
        }

        // ------------------------------------------------------------------ 2. 蓄力

        [Fact]
        public void Charge_HeldPastMax_AutoReleasesWithFullChargeAndEmitsChargeReady_WithoutReleasingTheKey()
        {
            var rig = Build();
            var maxTicks = Ticks(ChargeMaxMs);
            Press(rig, "k");
            var pressTick = rig.Tick;
            rig.Run(maxTicks + 15); // 键始终按着

            var ready = Assert.Single(rig.Of<InputChargeReadyEvent>());
            Assert.Equal(maxTicks, ready.Event.HeldTicks);
            Assert.True(ready.Tick >= pressTick + maxTicks - 1 && ready.Tick <= pressTick + maxTicks + 1);

            // 自动释放后技能起手（不必等玩家抬键），蓄力比例满。
            var started = Assert.Single(rig.Of<ActionStartedEvent>().Where(e => e.Event.SkillId.Equals(ChargeSkill)));
            Assert.Equal(1.0, started.Event.ChargeRatio, 9);
            Assert.True(started.Tick >= ready.Tick);
        }

        [Fact]
        public void Charge_BelowMin_DefaultReleasesAtLowestTier_CancelDropsTheInputWithChargeBelowMin()
        {
            // held 取"过了按住阈值、不足下限"。
            var held = Ticks(ChargeMinMs) - 3;

            // 缺省（release）：按最低档释放——技能起手、蓄力比例 0。
            var release = Build();
            Press(release, "k");
            release.Run(held);
            Release(release, "k");
            release.Run(20);
            var started = Assert.Single(release.Of<ActionStartedEvent>().Where(e => e.Event.SkillId.Equals(ChargeSkill)));
            Assert.Equal(0.0, started.Event.ChargeRatio, 9);

            // cancel：不起手，缓冲记录以 ChargeBelowMin 丢弃。
            var cancel = Build();
            Press(cancel, "l");
            cancel.Run(held);
            Release(cancel, "l");
            cancel.Run(20);
            Assert.Empty(cancel.Of<ActionStartedEvent>().Where(e => e.Event.SkillId.Equals(ChargeCancelSkill)));
            var dropped = Assert.Single(cancel.Of<InputBufferDroppedEvent>().Where(e => e.Event.Reason == BufferDropReason.ChargeBelowMin));
            Assert.Equal(new Id("input.action.m5_charge_cancel"), dropped.Event.ActionId);

            // 不变量：达到下限的蓄力，cancel 配置下也正常释放。
            var enough = Build();
            Press(enough, "l");
            enough.Run(Ticks(ChargeMinMs) + 2);
            Release(enough, "l");
            enough.Run(20);
            Assert.Single(enough.Of<ActionStartedEvent>().Where(e => e.Event.SkillId.Equals(ChargeCancelSkill)));
            Assert.Empty(enough.Of<InputBufferDroppedEvent>().Where(e => e.Event.Reason == BufferDropReason.ChargeBelowMin));
        }

        // ------------------------------------------------------------------ 3. 跳跃

        private static int GraceTicks(Rig rig) =>
            Ticks(rig.Feel.Resolver.ResolveJudging(PlayerId).GetNumber(FeelFieldNames.GraceMs));

        [Fact]
        public void Jump_OnGround_StartsAFlightAtFullJumpSpeed_AndNoSkillIsCast()
        {
            var rig = Build();
            Assert.False(rig.Vertical.IsAirborne(PlayerId));
            Press(rig, "x"); // 没有 cut 比例的跳跃动作
            rig.Step();
            Assert.True(rig.Vertical.IsAirborne(PlayerId));
            var expectedSpeed = Math.Sqrt(2.0 * Gravity * JumpHeight);
            Assert.InRange(rig.Vertical.GetVerticalSpeed(PlayerId), expectedSpeed - Gravity * Dt * 2, expectedSpeed);
            Assert.Empty(rig.Of<SkillCastSuccessEvent>());
            Assert.Empty(rig.Of<SkillCastFailedEvent>());
        }

        [Fact]
        public void JumpBuffer_PressJustBeforeLanding_JumpsOnLanding_PressBeyondTheWindowDoesNot()
        {
            var bufferTicks = Ticks(200);

            // 起跳，上升下落，在落地前 bufferTicks-1 个 tick 再按一次：落地当下接上第二跳。
            var near = Build();
            Press(near, "x");
            near.Step();
            Release(near, "x");
            var airTicks = 0;
            while (near.Vertical.IsAirborne(PlayerId) && airTicks < 400) { near.Step(); airTicks++; }
            Assert.False(near.Vertical.IsAirborne(PlayerId));

            var near2 = Build();
            Press(near2, "x");
            near2.Step();
            Release(near2, "x");
            // 推进到落地前 (bufferTicks - 2) 个 tick。
            for (var i = 0; i < airTicks - (bufferTicks - 2); i++) near2.Step();
            Assert.True(near2.Vertical.IsAirborne(PlayerId));
            Press(near2, "x");
            near2.Step();
            Release(near2, "x");
            var landedThenJumped = false;
            for (var i = 0; i < bufferTicks + 5; i++)
            {
                var wasAirborne = near2.Vertical.IsAirborne(PlayerId);
                near2.Step();
                if (!wasAirborne && near2.Vertical.IsAirborne(PlayerId)) landedThenJumped = true;
                if (wasAirborne && near2.Vertical.IsAirborne(PlayerId) && near2.Vertical.GetVerticalSpeed(PlayerId) > 0) landedThenJumped = true;
            }

            Assert.True(landedThenJumped, "落地前窗口内按下的跳跃应在落地时接上");

            // 窗口之外太早按：记录过期，落地后不再起跳。
            var far = Build();
            Press(far, "x");
            far.Step();
            Release(far, "x");
            for (var i = 0; i < airTicks - (bufferTicks + 12); i++) far.Step();
            Assert.True(far.Vertical.IsAirborne(PlayerId));
            Press(far, "x");
            far.Step();
            Release(far, "x");
            var jumpedAfterLanding = false;
            var landed = false;
            for (var i = 0; i < bufferTicks + 40; i++)
            {
                far.Step();
                if (!far.Vertical.IsAirborne(PlayerId)) landed = true;
                else if (landed) jumpedAfterLanding = true;
            }

            Assert.True(landed);
            Assert.False(jumpedAfterLanding, "超出缓冲窗口的提前按下不应在落地后起跳");
        }

        [Fact]
        public void Coyote_JumpWithinTheGroundedGraceAfterWalkingOffALedge_Succeeds_BeyondItFails()
        {
            // 复现：走出平台边缘后在宽限窗口内按跳跃——起跳成功（竖直速度由下落变为向上），不占空中跳跃次数；窗口之外同样的按键起不来。
            var inside = Build(withTerrain: true);
            inside.World.Player.Position = new Vec2(2, 0);
            inside.Step();
            Assert.Equal(3.0, inside.Height);
            var grace = GraceTicks(inside);
            Assert.True(grace >= 2, "框架缺省档案的宽限窗口应至少 2 个 tick");

            inside.World.Player.Position = new Vec2(6, 0); // 走出边缘
            inside.Step();
            Assert.True(inside.Vertical.IsAirborne(PlayerId));
            Assert.True(inside.Vertical.IsLedgeFall(PlayerId));

            // 窗口内最后一个 tick 按下。
            inside.Run(grace - 1);
            Press(inside, "space");
            inside.Step();
            Assert.True(inside.Vertical.GetVerticalSpeed(PlayerId) > 0, "宽限窗口内按下应按地面起跳");
            Assert.Equal(0, inside.Vertical.AirJumpsUsed(PlayerId));

            // 窗口之外：同一个动作再早/晚一个 tick 都起不来（边缘下落继续，竖直速度保持向下）。
            var outside = Build(withTerrain: true);
            outside.World.Player.Position = new Vec2(2, 0);
            outside.Step();
            outside.World.Player.Position = new Vec2(6, 0);
            outside.Step();
            outside.Run(grace + 1);
            Press(outside, "space");
            outside.Step();
            Assert.True(outside.Vertical.GetVerticalSpeed(PlayerId) < 0, "超出宽限窗口不应起跳");
        }

        [Fact]
        public void Coyote_NeedsTheGroundedConditionOnTheAction_AndNeverGrantsAnExtraJumpAfterARealJump()
        {
            // 动作没有声明 builtin_grounded（m5_jump_plain）：没有土狼时间。
            var plain = Build(withTerrain: true);
            plain.World.Player.Position = new Vec2(2, 0);
            plain.Step();
            plain.World.Player.Position = new Vec2(6, 0);
            plain.Step();
            plain.Step();
            Press(plain, "x");
            plain.Step();
            Assert.True(plain.Vertical.GetVerticalSpeed(PlayerId) < 0);

            // 真跳之后的空中：不是边缘下落，即便还在"刚在地面"的窗口内也不允许再次宽限起跳（没有开空中跳跃）。
            var rig = Build(withTerrain: true);
            rig.World.Player.Position = new Vec2(2, 0);
            rig.Step();
            Press(rig, "space");
            rig.Step();
            Release(rig, "space");
            Assert.True(rig.Vertical.IsAirborne(PlayerId));
            Assert.False(rig.Vertical.IsLedgeFall(PlayerId));
            var speed = rig.Vertical.GetVerticalSpeed(PlayerId);
            Press(rig, "space");
            rig.Step();
            Assert.True(rig.Vertical.GetVerticalSpeed(PlayerId) < speed, "空中再按跳跃不应重新起跳");
        }

        [Fact]
        public void VariableJumpHeight_ReleaseWhileAscending_CutsTheSpeedByTheRatio_HoldKeepsFullSpeed()
        {
            // 长按到顶：不截断。
            var held = Build();
            Press(held, "space");
            held.Step();
            held.Run(3);
            var fullSpeed = held.Vertical.GetVerticalSpeed(PlayerId);
            Assert.True(fullSpeed > 0);

            // 短按（起跳后下一 tick 就抬）：速度被截断为当时速度的 JumpCut 倍。
            var tap = Build();
            Press(tap, "space");
            tap.Step();
            var beforeRelease = tap.Vertical.GetVerticalSpeed(PlayerId);
            Release(tap, "space");
            tap.Step();
            var afterRelease = tap.Vertical.GetVerticalSpeed(PlayerId);
            // 抬起这个 tick：先截断（输入步骤 1），再积分一个 tick 的重力。
            Assert.InRange(afterRelease, beforeRelease * JumpCut - Gravity * Dt * 2, beforeRelease * JumpCut);

            // 最高点：短按的飞行高度远低于长按。
            double Apex(Rig rig, bool releaseEarly)
            {
                var apex = rig.Height;
                for (var i = 0; i < 400 && rig.Vertical.IsAirborne(PlayerId); i++)
                {
                    if (releaseEarly && i == 0) Release(rig, "space");
                    rig.Step();
                    apex = Math.Max(apex, rig.Height);
                }

                return apex;
            }

            var a = Build();
            Press(a, "space");
            a.Step();
            var fullApex = Apex(a, releaseEarly: false);
            var b = Build();
            Press(b, "space");
            b.Step();
            var shortApex = Apex(b, releaseEarly: true);
            Assert.True(fullApex > 0.8 * JumpHeight);
            Assert.True(shortApex < fullApex * 0.6, $"短按最高点 {shortApex} 应明显低于长按 {fullApex}");
        }

        // ------------------------------------------------------------------ 4. AI 经输入缓冲（可选开关）

        [Fact]
        public void AiThroughBuffer_DefaultOff_LeavesAiCastingDirect_OptInInstallsTheRouter()
        {
            var off = Build();
            Assert.Null(off.World.Gameplay.Carriers.Rules.Ai.CastRouter);
            Assert.Null(off.Feel.InputBuffer.GetDefinition(BufferedAiCastRouter.ActionId));

            var on = Build(aiThroughBuffer: true);
            Assert.NotNull(on.World.Gameplay.Carriers.Rules.Ai.CastRouter);
            Assert.NotNull(on.Feel.InputBuffer.GetDefinition(BufferedAiCastRouter.ActionId));
        }

        [Fact]
        public void AiThroughBuffer_SubmissionInsideTheBufferWindowBeforeTheActionEnds_StartsRightAfterIt_WhereADirectCastIsRefused()
        {
            var rig = Build(aiThroughBuffer: true);
            var router = rig.World.Gameplay.Carriers.Rules.Ai.CastRouter!;
            var window = rig.Feel.Resolver.ResolveJudging(PlayerId).GetTicks(FeelFieldNames.BufferMs);
            Assert.True(window >= 2, "框架缺省档案的缓冲窗口应至少 2 个 tick");
            var total = Ticks(1000);

            // 先让玩家起一个长动作（时间线技能，1 秒），推进到离结束只剩半个缓冲窗口。
            Assert.True(rig.Skills.CastSkill(PlayerId, LongSkill, Array.Empty<Id>()).Success);
            while (rig.Skills.ActionStateQuery.Current(PlayerId)!.Value.ElapsedTicks < total - window / 2) rig.Step();

            // 对照：动作进行中直接施法被动作锁拒绝（没有"提前输入"）。
            Assert.False(rig.Skills.CastSkill(PlayerId, AiSkill, Array.Empty<Id>()).Success);

            // 经路由提交：记录入缓冲；动作结束后的下一个步骤 1 接上，施放技能。
            Assert.True(router.TrySubmit(PlayerId, AiSkill, Array.Empty<Id>()));
            var finishedAt = -1;
            for (var i = 0; i < 200 && !rig.Casts(AiSkill).Any(); i++)
            {
                rig.Step();
                if (finishedAt < 0 && rig.Of<ActionFinishedEvent>().Any()) finishedAt = rig.Of<ActionFinishedEvent>().First().Tick;
            }

            var cast = Assert.Single(rig.Casts(AiSkill));
            Assert.True(finishedAt >= 0);
            Assert.InRange(cast.Tick - finishedAt, 0, 2); // 动作一结束就接上，不晚于下一个 tick 的步骤 1
        }

        [Fact]
        public void AiThroughBuffer_SubmissionFarEarlierThanTheBufferWindow_Expires_LikeAPlayerPress()
        {
            var rig = Build(aiThroughBuffer: true);
            var router = rig.World.Gameplay.Carriers.Rules.Ai.CastRouter!;
            Assert.True(rig.Skills.CastSkill(PlayerId, LongSkill, Array.Empty<Id>()).Success);
            rig.Run(2);
            Assert.True(router.TrySubmit(PlayerId, AiSkill, Array.Empty<Id>()));
            rig.Run(Ticks(1000) + 20);
            Assert.Empty(rig.Casts(AiSkill));
            Assert.Contains(rig.Of<InputBufferDroppedEvent>(), e => e.Event.Reason == BufferDropReason.Expired);
        }
    }
}

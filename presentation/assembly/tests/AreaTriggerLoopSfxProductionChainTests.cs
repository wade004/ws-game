using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.Assembly;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 消费方第三十六批（ADR-0089 循环音效，<c>area.trigger_entered</c>/<c>area.trigger_left</c> 驱动
    /// <c>play_sfx</c>/<c>stop_sfx{attach: source}</c>）生产装配级复现：真实 <see cref="GameplayAssembly"/>
    /// （<c>AreaTriggerHost</c> + <c>AreaTriggerTickHandler</c> 挂在真实 <see cref="WorldSim"/> tick 上）+
    /// 真实事件总线 + 真实 <see cref="PresentationAssembly"/>（<c>FeedbackBinder</c> →
    /// <c>CompositeFeedbackSink</c> → <c>SfxPlayer</c>）+ 真实 <c>feedback.binding</c>/<c>sfx.def</c>/
    /// <c>area.trigger_def</c> 数据解析；音频/资源加载用 <see cref="StubAudio"/>/<see cref="StubResourceLoader"/>
    /// （<see cref="StubResourceLoader.DeferCallbacks"/>=true 模拟引擎音频解码永远异步）。
    /// <para>
    /// 触发体拓扑照消费方现场形状（三个半径 9 的圆沿 x 轴相切排列，玩家从左侧区域真实逐 tick 走进
    /// 中间区域、停留远超一整段循环、再走进右侧相邻区域），全部用 sample 占位 id。
    /// </para>
    /// <para>
    /// 结论（见各用例判断记录）：玩家自己这一条链路在生产装配下停得掉；消费方观测到的"离开后仍在响"
    /// 来自区域内<b>其它单位</b>——<c>area.trigger_entered</c> 对每个进入（含出生即在区域内）的单位都
    /// 发出，未按单位过滤的 <c>play_sfx{attach: source}</c> 规则会给每个这样的单位各起一个循环实例，
    /// 玩家离开只停玩家自己那一个。
    /// </para>
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private static readonly Id LoopZoneId = new Id("area.sample_loop_zone");
        private static readonly Id LoopZoneEntryId = new Id("area.sample_loop_zone_entry");
        private static readonly Id LoopZoneNeighborId = new Id("area.sample_loop_zone_neighbor");
        private static readonly Id ZoneLoopSfxId = new Id("sfx.sample_zone_ambient_loop");
        private static readonly Id ZoneLoopResourceId = new Id("sfx.sample_zone_ambient_loop");

        /// <summary>三个半径 9 的圆：入口 (-11,0)、中间 (7,0)、相邻 (25,0)——相邻两圆在 x=-2、x=16 处
        /// 相切（边界点同时属于两圆，<c>AreaTriggerShapeGeometry.Contains</c> 圆形判定含等号）。</summary>
        private static void AddLoopZoneTables(InMemoryDataSource source, string? extraCondition)
        {
            source.Add("area.trigger_def",
                "{\"table\": \"area.trigger_def\", \"schema_version\": 1, \"rows\": [" +
                ZoneRow(LoopZoneEntryId, -11.0) + "," + ZoneRow(LoopZoneId, 7.0) + "," + ZoneRow(LoopZoneNeighborId, 25.0) +
                "]}");
            source.Add("sfx.def",
                "{\"table\": \"sfx.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + ZoneLoopSfxId.Value + "\", \"layer\": \"ambient\", \"priority\": 1, " +
                "\"resource_ref\": \"" + ZoneLoopResourceId.Value + "\", \"loop\": true}" +
                "]}");

            var condition = "event.trigger_id == " + LoopZoneId.Value + (extraCondition == null ? string.Empty : " and " + extraCondition);
            source.Add("feedback.binding",
                "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"feedback.sample_zone_loop_start\", \"event\": \"area.trigger_entered\", " +
                "\"condition\": \"" + condition + "\", \"actions\": [" +
                "{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + ZoneLoopSfxId.Value + "\", \"attach\": \"source\"}}" +
                "]}," +
                "{\"id\": \"feedback.sample_zone_loop_stop\", \"event\": \"area.trigger_left\", " +
                "\"condition\": \"" + condition + "\", \"actions\": [" +
                "{\"kind\": \"stop_sfx\", \"params\": {\"sfx_id\": \"" + ZoneLoopSfxId.Value + "\", \"attach\": \"source\"}}" +
                "]}" +
                "]}");
        }

        private static string ZoneRow(Id id, double centerX) =>
            "{\"id\": \"" + id.Value + "\", \"map_id\": \"" + SampleMapId.Value + "\", " +
            "\"shape\": {\"kind\": \"circle\", \"radius\": 9, \"center\": {\"x\": " +
            centerX.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"y\": 0}}, " +
            "\"trigger_type\": \"quest_explore\", \"one_shot\": false, \"params\": {}}";

        /// <summary>生产装配 + 区域数据 + 冷资源异步加载的共用夹具。<paramref name="npcPosition"/> 非
        /// null 时在区域加载前于该位置生成一个非玩家单位（阵营 <c>fac.sample_hostile</c>），模拟"区域内
        /// 本来就站着的 NPC"。</summary>
        private sealed class LoopZoneFixture
        {
            public PresentationAssembly Presentation = null!;
            public GameplayAssembly Gameplay = null!;
            public WorldSim World = null!;
            public StubEngine Engine = null!;
            public Id PlayerId;
            public Id? NpcId;
            public readonly List<string> Trace = new List<string>();
            private int _tick;

            public static LoopZoneFixture Create(string? extraCondition, Vec2? npcPosition)
            {
                var f = new LoopZoneFixture();
                f.Presentation = Build(out f.Gameplay, out f.World, out f.Engine, out var bus,
                    withResourceLoader: true, extraTables: s => AddLoopZoneTables(s, extraCondition));

                // 引擎音频解码永远异步（UnityResourceLoader.LoadAsync 判断记录"音频解码"）：首播必走
                // SfxPlayer 排队路径，由测试显式 CompletePending 模拟解码完成。
                f.Engine.ResourceLoader.DeferCallbacks = true;
                f.Engine.ResourceLoader.Register(ZoneLoopResourceId);

                f.PlayerId = f.Gameplay.PlayerUnitProvider();
                f.Gameplay.Carriers.Units.SetPosition(f.PlayerId, new Vec2(-11.0, 0.0));
                if (npcPosition.HasValue)
                {
                    f.NpcId = f.Gameplay.Carriers.Creatures.Spawn(SampleTargetTemplateId, SampleMapId, npcPosition.Value, 0.0);
                }

                bus.Subscribe(AreaTriggerEventKeys.TriggerEntered, e =>
                {
                    var a = (AreaTriggerEnteredEvent)e;
                    f.Trace.Add($"t{f._tick} entered {a.TriggerId} unit={f.Who(a.UnitId)}");
                });
                bus.Subscribe(AreaTriggerEventKeys.TriggerLeft, e =>
                {
                    var a = (AreaTriggerLeftEvent)e;
                    f.Trace.Add($"t{f._tick} left {a.TriggerId} unit={f.Who(a.UnitId)}");
                });

                f.Gameplay.AreaTrigger.LoadForMap(SampleMapId, f.Gameplay.Carriers.Rules.Registry);
                return f;
            }

            private string Who(Id unitId) =>
                unitId.Equals(PlayerId) ? "player" : NpcId.HasValue && unitId.Equals(NpcId.Value) ? "npc" : unitId.Value;

            /// <summary>当前 <see cref="StubAudio"/> 里仍在播放的该循环资源实例数（等价消费方
            /// "场景里任一 AudioSource.isPlaying 且 clip 匹配"的采样口径）。</summary>
            public int PlayingLoopInstances =>
                Engine.Audio.ActiveSfxPlaybacks.Values.Count(p => p.SoundId.Equals(ZoneLoopResourceId) && p.Loop);

            public void Tick(double dt = 0.1)
            {
                World.Tick(SimStep.Continuous(dt));
                Presentation.UpdatePlaybackMaintenance(dt);
                _tick++;
            }

            /// <summary>模拟真实移动：每 tick 沿 x 轴前进 <paramref name="stepPerTick"/>，逐 tick 跨越
            /// 半径边界，到达 <paramref name="targetX"/> 后再多推进 <paramref name="settleTicks"/> tick。</summary>
            public void WalkTo(double targetX, double stepPerTick = 0.7, int settleTicks = 3)
            {
                var x = Gameplay.Carriers.Units.GetPosition(PlayerId).X;
                while (System.Math.Abs(targetX - x) > 1e-9)
                {
                    var delta = targetX - x;
                    x += System.Math.Abs(delta) <= stepPerTick ? delta : System.Math.Sign(delta) * stepPerTick;
                    Gameplay.Carriers.Units.SetPosition(PlayerId, new Vec2(x, 0.0));
                    Tick();
                }

                for (var i = 0; i < settleTicks; i++)
                {
                    Tick();
                }
            }

            public void CompleteLoopDecodeIfPending()
            {
                if (Engine.ResourceLoader.HasPending(ZoneLoopResourceId))
                {
                    Engine.ResourceLoader.CompletePending(ZoneLoopResourceId);
                    Trace.Add($"t{_tick} decode-complete {ZoneLoopResourceId}");
                }
            }

            public string Dump() => string.Join(" | ", Trace) + $" || playing={PlayingLoopInstances}";
        }

        [Fact]
        public void AreaTriggerLoopSfx_PlayerOnly_ColdLoad_WalkIntoNeighborZone_StopsLoop_ProductionChain()
        {
            var f = LoopZoneFixture.Create(extraCondition: null, npcPosition: null);

            // 起点 (-11,0) 在入口区域内，首 tick 评估后玩家进入入口区域。
            f.Tick();
            Assert.Equal(0, f.PlayingLoopInstances);

            // 走到中间区域圆心：跨过 x=-2 边界进入中间区域 -> play_sfx 排队等冷资源解码。
            f.WalkTo(7.0);
            Assert.True(f.Engine.ResourceLoader.HasPending(ZoneLoopResourceId), f.Dump());
            Assert.Equal(0, f.PlayingLoopInstances);

            // 解码完成（异步回调晚于进入若干 tick）-> 真正起播；再停留 100 tick（10s，远超一段循环）。
            f.CompleteLoopDecodeIfPending();
            for (var i = 0; i < 100; i++)
            {
                f.Tick();
            }

            // 阳性对照：确实在播，且只有玩家这一个实例。
            Assert.True(f.PlayingLoopInstances == 1, f.Dump());

            // 走进相邻区域圆心：途经 x=16 两圆相切点（同时属于两圆），之后离开中间区域。
            f.WalkTo(25.0);

            Assert.Contains(f.Trace, t => t.EndsWith("left " + LoopZoneId.Value + " unit=player"));
            Assert.DoesNotContain(LoopZoneId, f.Gameplay.AreaTrigger.GetActiveTriggerIds(f.PlayerId));
            Assert.True(f.PlayingLoopInstances == 0, f.Dump());
        }

        /// <summary>
        /// 复现（消费方第三十六批现场）：区域里本来就站着一个非玩家单位，<c>feedback.binding</c> 的
        /// 条件只按 <c>event.trigger_id</c> 过滤、不按单位过滤。
        /// <para>
        /// 判断记录（这是契约行为，不是缺陷）：<c>area.trigger_entered</c>/<c>_left</c> 对每个单位各发
        /// 一次（05 第 7.1 节，<c>AreaTriggerTickHandler</c> 逐个评估 <c>IUnitAccess.AllUnits</c>，出生即
        /// 在区域内的单位在首次评估时就收到"进入"）；<c>attach: source</c> 取该事件的 <c>unitId</c>，
        /// 于是每个进入的单位各有一个 <c>(sfxId, unitId)</c> 键、各起一个循环实例，彼此独立。玩家离开
        /// 只停玩家那一个；NPC 那一个在 NPC 离开区域前一直在播——按"场景里任一该 clip 的音源在播"采样
        /// 就会得到"离开后仍在响"。本用例把这条时间线逐点钉住（区域加载即起播 1 个、玩家进入后 2 个、
        /// 玩家离开后剩 1 个且玩家的离开事件确实已派发）。
        /// </para>
        /// </summary>
        [Fact]
        public void AreaTriggerLoopSfx_NpcStandingInZone_UnfilteredBinding_PlaysPerUnit_PlayerLeaveStopsOnlyPlayersInstance()
        {
            var f = LoopZoneFixture.Create(extraCondition: null, npcPosition: new Vec2(7.0, 2.0));

            // 区域加载后首 tick：NPC 出生即在区域内 -> 收到"进入"，玩家尚在入口区域。
            f.Tick();
            Assert.Contains(f.Trace, t => t.EndsWith("entered " + LoopZoneId.Value + " unit=npc"));
            f.CompleteLoopDecodeIfPending();
            f.Tick();
            Assert.True(f.PlayingLoopInstances == 1, "玩家还没进入区域，NPC 的循环已经在播：" + f.Dump());

            f.WalkTo(7.0);
            f.CompleteLoopDecodeIfPending();
            for (var i = 0; i < 100; i++)
            {
                f.Tick();
            }

            // 阳性对照：玩家进入后是两个独立实例（NPC 一个 + 玩家一个）。
            Assert.True(f.PlayingLoopInstances == 2, f.Dump());

            f.WalkTo(25.0);

            Assert.Contains(f.Trace, t => t.EndsWith("left " + LoopZoneId.Value + " unit=player"));
            Assert.True(f.PlayingLoopInstances == 1, "玩家离开只停玩家自己那一个实例，NPC 的仍在播：" + f.Dump());
        }

        /// <summary>
        /// 不变量（数据侧修法的阳性/阴性对照）：同一现场，规则条件追加按单位过滤
        /// （<c>self.faction == &lt;玩家阵营&gt;</c>，<c>self</c> 即事件 <c>unitId</c>）后，NPC 的"进入"
        /// 事件照发但不起播；玩家在区内时恰好 1 个实例（阳性对照），离开相邻区域后 0 个（阴性断言）。
        /// </summary>
        [Fact]
        public void AreaTriggerLoopSfx_NpcStandingInZone_BindingFilteredToPlayer_LeaveStopsAllInstances()
        {
            var f = LoopZoneFixture.Create(extraCondition: "self.faction == fac.sample_player", npcPosition: new Vec2(7.0, 2.0));

            f.Tick();
            Assert.Contains(f.Trace, t => t.EndsWith("entered " + LoopZoneId.Value + " unit=npc"));
            f.CompleteLoopDecodeIfPending();
            f.Tick();
            Assert.True(f.PlayingLoopInstances == 0, f.Dump());

            f.WalkTo(7.0);
            f.CompleteLoopDecodeIfPending();
            for (var i = 0; i < 100; i++)
            {
                f.Tick();
            }

            Assert.True(f.PlayingLoopInstances == 1, f.Dump());

            f.WalkTo(25.0);

            Assert.Contains(f.Trace, t => t.EndsWith("left " + LoopZoneId.Value + " unit=player"));
            Assert.True(f.PlayingLoopInstances == 0, f.Dump());
        }
    }
}

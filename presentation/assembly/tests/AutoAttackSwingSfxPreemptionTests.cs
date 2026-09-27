using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 消费方反馈第五十四批（"绑在 <c>combat.auto_attack_swing</c> 上的 <c>play_sfx</c> 规则观测不到
    /// 播放痕迹"）生产装配级复现：真实 <see cref="global::Presentation.Assembly.PresentationAssembly"/>
    /// （<c>FeedbackBinder</c> → <c>CompositeFeedbackSink</c> → <c>SfxPlayer</c>，<c>SfxOptions</c>
    /// 用生产缺省值）+ 真实 <c>feedback.binding</c>/<c>sfx.def</c> 解析 + <c>StubAudio</c> 桩音频后端。
    /// 数据形状照消费方现场：挥击音效 <c>layer: combat</c>、<c>priority: 2</c>、两个 <c>variants</c>，
    /// 条件 <c>self.faction == &lt;玩家阵营&gt;</c>；命中类音效同层、优先级更高，绑在紧随其后的
    /// <c>combat.damage_dealt</c> 上（同一次 <c>AutoAttackHost.Update</c> 内先后发布，这里同帧先后
    /// <c>PublishImmediate</c>）。每一轮之间上一轮全部音效已自然播完（主路径：后端回报；回退路径：
    /// 后端不回报、经真实 <c>UpdatePlaybackMaintenance</c> 推进超过保留时长）。
    /// <para>
    /// 桩音频后端不随时间自然播完（只在测试显式 <c>CompleteSfx</c> 时"播完"），断言时刻本轮句柄都未
    /// 被标记播完，因此 <c>StubAudio.ActiveSfxPlaybacks</c> 里本轮句柄消失只可能是 <c>SfxPlayer</c>
    /// 主动 <c>IAudio.StopSfx</c> 了它——断言口径就是"挥击音效在命中音效开始之后没有被框架主动掐掉"。
    /// </para>
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private static readonly Id SwingSfxId = new Id("sfx.sample_weapon_swing");
        private static readonly Id SwingSfxV1 = new Id("sfx.sample_weapon_swing_v1");
        private static readonly Id SwingSfxV2 = new Id("sfx.sample_weapon_swing_v2");
        private static readonly Id WeaponHitSfxId = new Id("sfx.sample_weapon_hit");
        private static readonly Id TargetHurtSfxId = new Id("sfx.sample_target_hurt");

        private static void AddSwingAndHitSfxTables(InMemoryDataSource source)
        {
            source.Add("sfx.def",
                "{\"table\": \"sfx.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + SwingSfxId.Value + "\", \"layer\": \"combat\", \"priority\": 2, " +
                "\"variants\": [\"" + SwingSfxV1.Value + "\", \"" + SwingSfxV2.Value + "\"], " +
                "\"resource_ref\": \"" + SwingSfxV1.Value + "\"}," +
                "{\"id\": \"" + WeaponHitSfxId.Value + "\", \"layer\": \"combat\", \"priority\": 5, " +
                "\"resource_ref\": \"" + WeaponHitSfxId.Value + "\"}," +
                "{\"id\": \"" + TargetHurtSfxId.Value + "\", \"layer\": \"combat\", \"priority\": 4, " +
                "\"resource_ref\": \"" + TargetHurtSfxId.Value + "\"}" +
                "]}");
            source.Add("feedback.binding",
                "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"feedback.sample_weapon_swing_sfx\", \"event\": \"combat.auto_attack_swing\", " +
                "\"condition\": \"self.faction == fac.sample_player\", \"actions\": [" +
                "{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + SwingSfxId.Value + "\"}}]}," +
                "{\"id\": \"feedback.sample_weapon_hit_sfx\", \"event\": \"combat.damage_dealt\", \"actions\": [" +
                "{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + WeaponHitSfxId.Value + "\"}}]}," +
                "{\"id\": \"feedback.sample_target_hurt_sfx\", \"event\": \"combat.damage_dealt\", " +
                "\"condition\": \"target.faction == fac.sample_hostile\", \"actions\": [" +
                "{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + TargetHurtSfxId.Value + "\"}}]}" +
                "]}");
        }

        /// <summary>复现（主路径，音频后端回报播放状态）：连续多轮真实挥击，每一轮结束后上一轮全部
        /// 音效由桩后端标记为自然播完（<c>CompleteSfx</c>，<c>IsSfxPlaying</c> 回报 false），两轮之间只
        /// 推进一帧（0.016 秒，远不到保留时长，排除回退路径的作用）。每一轮挥击音效都必须在同帧紧随
        /// 其后的命中音效开始之后仍在播放。修复前第 4 轮起挥击音效被命中音效的同层抢占当场停掉（层内
        /// 记账从不释放已经自然播完的一次性音效，层"永远满员"，最低优先级的新挥击声首当其冲）。</summary>
        [Fact]
        public void AutoAttackSwingSfx_FollowedBySameFrameDamageSfx_SameLayer_SwingKeepsPlaying_AcrossRounds()
        {
            RunSwingThenHitRounds(backendReportsPlayback: true);
        }

        /// <summary>复现（回退路径，音频后端不回报播放状态，<c>IsSfxPlaying</c> 恒 null）：两轮之间推进
        /// 3 秒（超过保留时长），按 <c>SfxOptions.OneShotLayerSlotHoldSeconds</c> 推定上一轮已播完。</summary>
        [Fact]
        public void AutoAttackSwingSfx_BackendNoPlaybackReport_HoldFallback_SwingKeepsPlaying_AcrossRounds()
        {
            RunSwingThenHitRounds(backendReportsPlayback: false);
        }

        private static void RunSwingThenHitRounds(bool backendReportsPlayback)
        {
            var presentation = Build(out var gameplay, out _, out var engine, out var bus,
                extraTables: AddSwingAndHitSfxTables);
            engine.Audio.ReportsPlaybackState = backendReportsPlayback;
            var playerId = gameplay.PlayerUnitProvider();
            var targetId = gameplay.Carriers.Creatures.Spawn(SampleTargetTemplateId, SampleMapId, Vec2.Zero, 0.0);
            var audio = engine.Audio;
            var diag = presentation.SfxPlaybackDiagnostics;
            var trace = new List<string>();

            for (var round = 1; round <= 8; round++)
            {
                var before = new HashSet<int>(audio.ActiveSfxPlaybacks.Keys);
                bus.PublishImmediate(new AutoAttackSwingEvent(playerId, targetId));
                var swingHandles = audio.ActiveSfxPlaybacks
                    .Where(kv => !before.Contains(kv.Key) && (kv.Value.SoundId.Equals(SwingSfxV1) || kv.Value.SoundId.Equals(SwingSfxV2)))
                    .Select(kv => kv.Key).ToList();

                // 追问 ①②③：事件键已订阅、条件求值为真、SafeDispatch → DispatchPlaySfx → sink.PlaySfx →
                // SfxPlayer.Play → IAudio.PlaySfx 整条链走到底——每一轮挥击都真实产生了一个新的播放句柄。
                Assert.True(swingHandles.Count == 1, $"第 {round} 轮挥击事件没有真实调用 IAudio.PlaySfx（新句柄数 {swingHandles.Count}）");
                var swingHandle = swingHandles[0];
                var lastPlayAfterSwing = diag.LastPlay!.Value.ResourceRef;

                bus.PublishImmediate(new CombatDamageDealtEvent(
                    playerId, targetId, new Id("skill.school.physical"), 10.0, isCrit: false, HitResult.Hit));

                var hitStarted = audio.ActiveSfxPlaybacks.Values.Any(p => p.SoundId.Equals(WeaponHitSfxId));
                var swingAlive = audio.ActiveSfxPlaybacks.ContainsKey(swingHandle);
                trace.Add($"round={round} swing_handle={swingHandle} last_play_after_swing={lastPlayAfterSwing} " +
                          $"last_play_after_hit={diag.LastPlay!.Value.ResourceRef} hit_started={hitStarted} swing_alive_after_hit={swingAlive} " +
                          $"requested={diag.PlayRequestedCount} started={diag.PlayStartedCount} dropped={diag.PlayDroppedCount}");

                Assert.True(hitStarted, $"第 {round} 轮命中音效未开始播放。\n" + string.Join("\n", trace));
                Assert.True(swingAlive, $"第 {round} 轮挥击音效在同帧命中音效开始后被框架主动停止。\n" + string.Join("\n", trace));

                if (backendReportsPlayback)
                {
                    // 上一轮全部一次性音效自然播完（引擎回报 false）；只推进一帧，不靠保留时长。
                    foreach (var handle in audio.ActiveSfxPlaybacks.Keys.ToList())
                    {
                        audio.CompleteSfx(new Core.Foundation.EngineAdapter.SfxHandle(handle));
                    }
                    presentation.UpdatePlaybackMaintenance(0.016);
                }
                else
                {
                    // 两轮之间推进 3 秒：上一轮全部一次性音效在真实引擎里早已自然播完（后端不回报，
                    // 只能按保留时长推定）。
                    presentation.UpdatePlaybackMaintenance(3.0);
                }
            }

            Assert.Equal(0, diag.PlayDroppedCount);
            Assert.Equal(diag.PlayRequestedCount, diag.PlayStartedCount);
        }
    }
}

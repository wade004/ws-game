using System;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Achievement;
using Core.Gameplay.Loot;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Tests.Gameplay.EndToEnd;
using Xunit;

namespace Tests.Gameplay
{
    /// <summary>
    /// 阶段 3 集成收尾"事项五"端到端集成测试：只经契约/事件驱动
    /// <c>core/gameplay/tests/EndToEnd/GameWorldFixture.cs</c> 装配出的整套 L0～L4 世界——进入场景、
    /// 接任务、击杀、拾取掉落、交任务、存读档、装备穿脱、非法状态转移、掉落确定性——覆盖落地计划
    /// 第 13 节阶段 3 验收 1（装备变化）、验收 2（固定种子掉落一致）、验收 3（非法转移被拒）。
    /// </summary>
    public sealed class EndToEndTests
    {
        // -----------------------------------------------------------------
        // 帮助方法：把"进场景→接任务→击杀→拾取→目标达成"这段全部用例共用的前置流程收敛成一个方法。
        // -----------------------------------------------------------------

        /// <summary>进场景（触发 <see cref="GameWorldFixture.SpawnBeastField"/> 生成一只
        /// <see cref="GameWorldFixture.CreatureBeast"/>）+ 经对话接取 <see cref="GameWorldFixture.QuestHunt"/>。
        /// 返回本次生成的野兽运行期 id。</summary>
        private static Id EnterAndAcceptQuest(GameWorldFixture.Fixture fx)
        {
            fx.Gameplay.EnterMap(GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            var spawnRecord = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnBeastField);
            Assert.NotNull(spawnRecord);
            Assert.True(spawnRecord!.EntityId.HasValue, "spawn.sample_beast_field 在 EnterMap 后应已生成一个实体");
            var beastId = spawnRecord.EntityId!.Value;

            // CreatureFactory.Spawn 经 WorldUnitAccess.SetPosition 写入位置，但不会自动同步进本测试
            // 自己的 StubSpatialQuery（该桩实现没有 ISpatialIndexSync，见 GameWorldFixture 判断记录）；
            // 手动登记，供 target.chain.sample_nearest_enemy 的 nearest_in_shape 来源找到它。
            var beastPos = fx.Gameplay.Carriers.Units.GetPosition(beastId);
            fx.Spatial.Register(beastId, beastPos, 0.5);

            fx.Gameplay.Dialog.OpenGossip(GameWorldFixture.PlayerId, GameWorldFixture.NpcId, GameWorldFixture.DialogMenu);
            var accepted = fx.Gameplay.Dialog.ChooseOption(GameWorldFixture.PlayerId, 0); // quest_accept
            Assert.True(accepted);

            Assert.Equal(QuestState.Active, fx.Gameplay.Quest.GetState(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt));

            return beastId;
        }

        /// <summary>在 <see cref="EnterAndAcceptQuest"/> 基础上继续跑到"两条目标都达标"
        /// （<see cref="QuestState.ObjectivesComplete"/>）：击杀野兽、拾取掉落里的信物。</summary>
        private static Id RunToObjectivesComplete(GameWorldFixture.Fixture fx)
        {
            var beastId = EnterAndAcceptQuest(fx);

            var died = fx.CastUntilDead(GameWorldFixture.SkillStrike, beastId);
            Assert.True(died, "野兽应当在有限次数的攻击尝试内死亡");

            // kill 目标（objectives[0]）应已自动记 1 次进度。
            var log = fx.Gameplay.Quest.GetLog(GameWorldFixture.PlayerId);
            var progress = log.Single(p => p.QuestId.Equals(GameWorldFixture.QuestHunt));
            Assert.Equal(1, progress.ObjectiveCounts[0]);

            // 死亡位置应当出现一份地面掉落（loot.sample_beast 保底掉 1 个 item.sample_token）。
            Assert.NotEmpty(fx.Gameplay.Loot.ActiveLootIds);
            var lootInstanceId = fx.Gameplay.Loot.ActiveLootIds[0];
            Assert.True(fx.Gameplay.Loot.TryGetDropped(lootInstanceId, out var dropped));
            Assert.Contains(dropped.Items, i => i.TemplateId.Equals(GameWorldFixture.ItemToken));

            // 拾取前把玩家挪到掉落物所在位置，规避 LootOptions.PickupRange 限制。
            fx.Gameplay.Carriers.Units.SetPosition(GameWorldFixture.PlayerId, dropped.Position);

            var pickup = fx.Gameplay.Loot.PickUp(GameWorldFixture.PlayerId, lootInstanceId);
            Assert.True(pickup.Success);
            Assert.Contains(pickup.ItemsTaken, i => i.TemplateId.Equals(GameWorldFixture.ItemToken));

            Assert.True(fx.Gameplay.Carriers.Inventory.CountOf(GameWorldFixture.PlayerId, GameWorldFixture.ItemToken) >= 1);

            // item.added 走 IEventBus.Enqueue（同 EquipBlade 用例判断记录）；QuestHost 订阅的
            // collect 目标自动推进依赖这条事件真正派发到订阅者，本方法后续要立即断言
            // ObjectivesComplete，必须先手动 flush 一次。
            fx.Bus.DispatchPending();

            Assert.Equal(QuestState.ObjectivesComplete, fx.Gameplay.Quest.GetState(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt));

            return beastId;
        }

        // -----------------------------------------------------------------
        // 1. 进入场景：SpawnHost 按 spawn.table 生成野兽，玩家已注册并处于出生点。
        // -----------------------------------------------------------------

        [Fact]
        public void EnterMap_SpawnsBeastFromSpawnTable_AndRegistersPlayer()
        {
            var fx = GameWorldFixture.Build();

            fx.Gameplay.EnterMap(GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            var record = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnBeastField);
            Assert.NotNull(record);
            Assert.True(record!.EntityId.HasValue);
            Assert.True(fx.Gameplay.Carriers.Units.Exists(record.EntityId!.Value));
            Assert.Equal(GameWorldFixture.CreatureBeast, fx.Gameplay.Carriers.Units.GetTemplateId(record.EntityId.Value));

            Assert.True(fx.Gameplay.Carriers.Units.Exists(GameWorldFixture.PlayerId));
            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(GameWorldFixture.PlayerId));
        }

        // -----------------------------------------------------------------
        // 1a. 缺口 14：encounter.def.units[].spawn_ref 经 SpawnRequester → ISpawnHost.SpawnNow
        //     生成参战单位（此前 GameplayAssembly 的 spawnRequester 是"总是返回空列表"的占位，
        //     data/_sample/encounter/encounter.def.json 此前用 template_ref 绕开这条路径——见
        //     GameplayAssembly.cs 第 12 步判断记录）。spawn.sample_encounter_ambusher 的
        //     respawn_policy 是 never，EnterMap（ApplyForMap）不会自动生成它，只有本次
        //     Encounter.Start 才会经 spawnRequester 触发。
        // -----------------------------------------------------------------

        [Fact]
        public void Encounter_Start_SpawnsUnit_ViaSpawnRequester_ReachingSpawnTableEntry()
        {
            var fx = GameWorldFixture.Build();
            fx.Gameplay.EnterMap(GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            var beforeRecord = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnEncounterAmbusher);
            Assert.True(beforeRecord == null || !beforeRecord.EntityId.HasValue,
                "spawn.sample_encounter_ambusher 是 never 策略，EnterMap 不应自动生成");

            fx.Gameplay.Encounter.Start(GameWorldFixture.EncounterBeastFight, GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            var afterRecord = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnEncounterAmbusher);
            Assert.NotNull(afterRecord);
            Assert.True(afterRecord!.EntityId.HasValue,
                "encounter.sample_beast_fight 的 units[0].spawn_ref 应经 SpawnRequester(=ISpawnHost.SpawnNow) 生成一个实体");
            Assert.True(fx.Gameplay.Carriers.Units.Exists(afterRecord.EntityId!.Value));
        }

        // -----------------------------------------------------------------
        // 2. 接任务：经 dialog.gossip_menu 的 quest_accept 动作，任务日志状态机进入 Active。
        // -----------------------------------------------------------------

        [Fact]
        public void AcceptQuest_ViaDialogGossip_TransitionsToActive()
        {
            var fx = GameWorldFixture.Build();

            Assert.Equal(QuestState.Available, fx.Gameplay.Quest.GetState(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt));

            EnterAndAcceptQuest(fx);

            Assert.Contains(fx.Events, e => e.Key.Equals(QuestEventKeys.Accepted));
        }

        // -----------------------------------------------------------------
        // 3. 击杀 + 掉落 + 拾取：objectives 两条全部达标，ObjectivesComplete。
        // -----------------------------------------------------------------

        [Fact]
        public void KillBeast_DropsLoot_PickUp_CompletesBothObjectives()
        {
            var fx = GameWorldFixture.Build();

            RunToObjectivesComplete(fx);

            Assert.Contains(fx.Events, e => e.Key.Equals(RulesEventKeys.UnitDied));
            Assert.Contains(fx.Events, e => e.Key.Equals(LootEventKeys.PickedUp));
            Assert.Contains(fx.Events, e => e.Key.Equals(QuestEventKeys.Completed));
        }

        // -----------------------------------------------------------------
        // 4/5. 交任务：quest_turn_in 结算奖励（经验/货币）、任务转 TurnedIn、成就解锁。
        // -----------------------------------------------------------------

        [Fact]
        public void TurnInQuest_GrantsRewards_AndUnlocksAchievement()
        {
            var fx = GameWorldFixture.Build();
            RunToObjectivesComplete(fx);

            var coinBefore = fx.Gameplay.Economy.GetBalance(GameWorldFixture.PlayerId, GameWorldFixture.CurrencyCoin);

            fx.Gameplay.Dialog.OpenGossip(GameWorldFixture.PlayerId, GameWorldFixture.NpcId, GameWorldFixture.DialogMenu);
            var turnedIn = fx.Gameplay.Dialog.ChooseOption(GameWorldFixture.PlayerId, 1); // quest_turn_in
            Assert.True(turnedIn);

            Assert.Equal(QuestState.TurnedIn, fx.Gameplay.Quest.GetState(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt));

            var coinAfter = fx.Gameplay.Economy.GetBalance(GameWorldFixture.PlayerId, GameWorldFixture.CurrencyCoin);
            Assert.Equal(coinBefore + 10, coinAfter); // quest.sample_hunt.rewards.currency = 10

            Assert.Contains(fx.Events, e => e.Key.Equals(QuestEventKeys.TurnedIn));

            // achv.sample_hunter：kill_count(creature.sample_beast) >= 1，本流程已经击杀过一次，
            // AchievementHost 订阅 unit.died 早于交任务已经完成累计，交任务本身不参与判定。
            Assert.True(fx.Gameplay.Achievement.IsUnlocked(GameWorldFixture.PlayerId, GameWorldFixture.AchvHunter));
            Assert.Contains(fx.Events, e => e.Key.Equals(AchievementEventKeys.Unlocked));
        }

        // -----------------------------------------------------------------
        // 6（验收 3）：目标未达标时交任务被拒。
        // -----------------------------------------------------------------

        [Fact]
        public void TurnIn_BeforeObjectivesComplete_IsRejected()
        {
            var fx = GameWorldFixture.Build();
            EnterAndAcceptQuest(fx);

            Assert.Equal(QuestState.Active, fx.Gameplay.Quest.GetState(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt));

            var turnedIn = fx.Gameplay.Quest.TurnIn(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt);

            Assert.False(turnedIn);
            Assert.Equal(QuestState.Active, fx.Gameplay.Quest.GetState(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt));
            Assert.DoesNotContain(fx.Events, e => e.Key.Equals(QuestEventKeys.TurnedIn));
        }

        // -----------------------------------------------------------------
        // 7（验收 1）：装备穿脱——属性变化 + item.equipped 事件。
        // -----------------------------------------------------------------

        [Fact]
        public void EquipBlade_ChangesStrength_AndEmitsItemEquippedEvent()
        {
            var fx = GameWorldFixture.Build();

            var strengthBefore = fx.Gameplay.Carriers.Rules.Stats.GetStat(GameWorldFixture.PlayerId, GameWorldFixture.StatStrength);

            var added = fx.Gameplay.Carriers.Inventory.AddItem(GameWorldFixture.PlayerId, GameWorldFixture.ItemBlade, 1);
            Assert.True(added);

            var instance = fx.Gameplay.Carriers.Inventory.ListItems(GameWorldFixture.PlayerId)
                .Single(i => i.TemplateId.Equals(GameWorldFixture.ItemBlade));

            var result = fx.Gameplay.Carriers.Equipment.Equip(GameWorldFixture.PlayerId, instance.InstanceId, GameWorldFixture.ItemSlotMainHand);
            Assert.True(result.Success, result.Reason.ToString());

            var strengthAfter = fx.Gameplay.Carriers.Rules.Stats.GetStat(GameWorldFixture.PlayerId, GameWorldFixture.StatStrength);
            Assert.Equal(strengthBefore + 2, strengthAfter, 6); // item.sample_blade.stats: stat.strength flat +2

            // item.equipped 走 IEventBus.Enqueue（tick 末 EventDispatch 阶段才真正派发给订阅者，见
            // 03 第 9 节），本用例不推进任何 tick，手动 DispatchPending 一次才能观察到它。
            fx.Bus.DispatchPending();
            Assert.Contains(fx.Events, e => e.Key.Equals(CarriersEventKeys.ItemEquipped));
        }

        // -----------------------------------------------------------------
        // 8（验收 2）：固定种子重复两次，掉落结果逐项相等（LootHost.Roll 走 IRngHost 独立流）。
        // -----------------------------------------------------------------

        [Fact]
        public void LootRoll_SameSeed_TwoIndependentRuns_ProduceIdenticalDrops()
        {
            const ulong seed = 20260905UL;

            var fxA = GameWorldFixture.Build(seed);
            var contextA = new RollContext(new Id("unit.a_source"), new Id("unit.a_killer"), 1.0, new Id("unit.a_source"));
            var itemsA = fxA.Gameplay.Loot.Roll(new Id("loot.sample_beast"), contextA);

            var fxB = GameWorldFixture.Build(seed);
            var contextB = new RollContext(new Id("unit.a_source"), new Id("unit.a_killer"), 1.0, new Id("unit.a_source"));
            var itemsB = fxB.Gameplay.Loot.Roll(new Id("loot.sample_beast"), contextB);

            Assert.Equal(itemsA.Count, itemsB.Count);
            for (var i = 0; i < itemsA.Count; i++)
            {
                Assert.Equal(itemsA[i].TemplateId, itemsB[i].TemplateId);
                Assert.Equal(itemsA[i].Count, itemsB[i].Count);
            }
        }

        // -----------------------------------------------------------------
        // 9. 存档/读档：背包 + 任务 + 世界状态三类逐项断言与存档前一致。
        // -----------------------------------------------------------------

        [Fact]
        public void SaveThenLoad_FreshAssembly_RestoresInventoryQuestAndWorldState()
        {
            var fs = new StubFileSystem();
            var fxA = GameWorldFixture.Build(fileSystem: fs);

            RunToObjectivesComplete(fxA);

            fxA.Gameplay.Dialog.OpenGossip(GameWorldFixture.PlayerId, GameWorldFixture.NpcId, GameWorldFixture.DialogMenu);
            Assert.True(fxA.Gameplay.Dialog.ChooseOption(GameWorldFixture.PlayerId, 1)); // quest_turn_in

            // 额外落一条世界状态标志，覆盖"世界状态"这一类断言（05 第 8.2 节 Set，经公开契约调用）。
            var flagKey = new Id("world.sample_field.explored");
            fxA.Gameplay.WorldState.Set(flagKey, ExprValue.OfBool(true), new Id("test.e2e"));

            fxA.Gameplay.Carriers.Units.SetPosition(GameWorldFixture.PlayerId, new Vec2(3, 4));

            var player = fxA.Player;
            player.MapId = GameWorldFixture.MapId;

            fxA.Gameplay.RegisterPersistables(fxA.SaveSystem, player);

            var saveResult = fxA.SaveSystem.Save(new Core.Foundation.SaveSystem.SaveRequest(GameWorldFixture.SaveSlot, "2026-09-05T00:00:00Z"));
            Assert.True(saveResult.Success, saveResult.Message);

            var tokenCountBefore = fxA.Gameplay.Carriers.Inventory.CountOf(GameWorldFixture.PlayerId, GameWorldFixture.ItemToken);
            var coinBefore = fxA.Gameplay.Economy.GetBalance(GameWorldFixture.PlayerId, GameWorldFixture.CurrencyCoin);
            var achvUnlockedBefore = fxA.Gameplay.Achievement.IsUnlocked(GameWorldFixture.PlayerId, GameWorldFixture.AchvHunter);

            // ---- 全新装配一套（同一个 StubFileSystem，模拟同一台机器上的磁盘）----
            // GameWorldFixture.Build 本身已经创建并注册好一个全新的玩家单位（fxB.Player），不需要
            // 再手工 new 一个同 id 的 PlayerUnit——那会与 World.AddEntity 的"实体 id 不可重复"约束
            // 冲突（该单位在本次全新装配里已经存在过一次）。
            var fxB = GameWorldFixture.Build(fileSystem: fs);
            var playerB = fxB.Player;
            fxB.Gameplay.RegisterPersistables(fxB.SaveSystem, playerB);

            var loadResult = fxB.SaveSystem.Load(GameWorldFixture.SaveSlot);
            Assert.Equal(Core.Foundation.SaveSystem.LoadStatus.Loaded, loadResult.Status);

            // 背包：token 数量与存档前一致。
            Assert.Equal(tokenCountBefore, fxB.Gameplay.Carriers.Inventory.CountOf(GameWorldFixture.PlayerId, GameWorldFixture.ItemToken));

            // 任务：TurnedIn 状态与目标计数一致。
            Assert.Equal(QuestState.TurnedIn, fxB.Gameplay.Quest.GetState(GameWorldFixture.PlayerId, GameWorldFixture.QuestHunt));
            var logB = fxB.Gameplay.Quest.GetLog(GameWorldFixture.PlayerId).Single(p => p.QuestId.Equals(GameWorldFixture.QuestHunt));
            Assert.Equal(1, logB.ObjectiveCounts[0]);
            Assert.True(logB.ObjectiveCounts[1] >= 1);

            // 世界状态：读档后应恢复我们手动落的标志。
            Assert.True(fxB.Gameplay.WorldState.Has(flagKey));
            Assert.True(fxB.Gameplay.WorldState.Get(flagKey).AsBool);

            // 货币 + 成就 + 位置 一并核对（覆盖任务书列出的其余几类）。
            Assert.Equal(coinBefore, fxB.Gameplay.Economy.GetBalance(GameWorldFixture.PlayerId, GameWorldFixture.CurrencyCoin));
            Assert.Equal(achvUnlockedBefore, fxB.Gameplay.Achievement.IsUnlocked(GameWorldFixture.PlayerId, GameWorldFixture.AchvHunter));
            Assert.Equal(new Vec2(3, 4), playerB.Position);
            Assert.Equal(GameWorldFixture.MapId, playerB.MapId);

            // 刷新点状态：spawn_state 段应恢复出"该刷新点已生成过至少一次"的记录。
            var spawnRecordB = fxB.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnBeastField);
            Assert.NotNull(spawnRecordB);
            Assert.True(spawnRecordB!.SpawnCount >= 1);
        }

        // -----------------------------------------------------------------
        // 10. 缺口 16：save_point 交互经 GobjOptions.SaveRequester → GameplayAssembly.SaveSystem
        //     触发一次自动存档（此前 SaveRequester 是 "_ => { }" 占位，交互不产生任何副作用；
        //     GameplayAssembly.cs 第 16 步判断记录）。
        // -----------------------------------------------------------------

        [Fact]
        public void InteractWithSavePoint_TriggersAutosave_WritesAutosaveSlotFile()
        {
            var fx = GameWorldFixture.Build();

            var autosaveSlotId = new Id("slot.autosave");
            Assert.False(fx.Gameplay.SaveSystem.SlotExists(autosaveSlotId));

            // 存档点摆在玩家出生位置（Vec2.Zero），落在 GobjOptions.InteractRange 默认值 2 以内。
            var savePointId = fx.Gameplay.Carriers.GameObjects.Spawn(
                GameWorldFixture.GobjSavePoint, GameWorldFixture.MapId, Vec2.Zero, 0.0);

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(GameWorldFixture.PlayerId, savePointId);

            Assert.True(result.Success);
            Assert.True(fx.Gameplay.SaveSystem.SlotExists(autosaveSlotId),
                "save_point 交互应经 GobjOptions.SaveRequester 触发 GameplayAssembly.SaveSystem.Save，" +
                "在默认自动存档槽 \"slot.autosave\" 下写出文件");
        }
    }
}

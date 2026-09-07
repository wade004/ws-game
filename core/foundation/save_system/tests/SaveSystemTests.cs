using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Foundation.SaveSystem
{
    public class SaveSystemTests
    {
        private static readonly Id GameId = new Id("game.demo");

        private static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(SaveEventKeys.SaveCompleted, "save", new[] { "slotId" }),
                new EventDefinition(SaveEventKeys.SaveLoaded, "save", new[] { "slotId" }),
                new EventDefinition(SaveEventKeys.SaveMigrated, "save", new[] { "slotId", "fromVersion", "toVersion" }),
            });

            return new EventBus(catalog);
        }

        private static Core.Foundation.SaveSystem.SaveSystem CreateSut(
            StubFileSystem fs, SaveSystemOptions? options = null, IEventBus? bus = null, ISaveDiagnostics? diagnostics = null)
        {
            return new Core.Foundation.SaveSystem.SaveSystem(fs, options ?? new SaveSystemOptions(GameId), bus, diagnostics);
        }

        private static string SlotPath(string slotId, string savesDir = "saves") => $"user://{savesDir}/{slotId}.json";

        // FND-01 收口：备份路径搬进独立子目录 backups/，见 SaveSystem.BackupPath 判断记录。
        private static string BackupPath(string slotId, int index, string savesDir = "saves") =>
            $"user://{savesDir}/backups/{slotId}.bak{index}.json";

        // ---- 测试用 Persistable ------------------------------------------------

        /// <summary>最小可用 Persistable：直接持有一个 JsonValue，Load 时记录调用顺序（可选）。</summary>
        private sealed class RecordingPersistable : IPersistable
        {
            public string SectionKey { get; }

            public JsonValue Value { get; set; }

            public List<string>? OrderLog { get; set; }

            public bool ThrowOnSave { get; set; }

            public bool ThrowOnLoad { get; set; }

            public RecordingPersistable(string sectionKey, JsonValue initial)
            {
                SectionKey = sectionKey;
                Value = initial;
            }

            public JsonValue Save()
            {
                if (ThrowOnSave)
                {
                    throw new InvalidOperationException($"boom-save:{SectionKey}");
                }

                return Value;
            }

            public void Load(JsonValue data)
            {
                if (ThrowOnLoad)
                {
                    throw new InvalidOperationException($"boom-load:{SectionKey}");
                }

                Value = data;
                OrderLog?.Add(SectionKey);
            }
        }

        /// <summary>把某个存档段的 key 从 <see cref="OldKey"/> 改名为 <see cref="NewKey"/> 的迁移函数，
        /// 供测试迁移链使用。</summary>
        private sealed class RenameSectionMigration : ISaveMigration
        {
            public int FromVersion { get; }

            public int ToVersion { get; }

            public bool Irreversible { get; }

            public string OldKey { get; }

            public string NewKey { get; }

            public RenameSectionMigration(int fromVersion, int toVersion, string oldKey, string newKey, bool irreversible = false)
            {
                FromVersion = fromVersion;
                ToVersion = toVersion;
                OldKey = oldKey;
                NewKey = newKey;
                Irreversible = irreversible;
            }

            public JsonObject Migrate(JsonObject document)
            {
                var sections = (JsonObject)document["sections"];
                var builder = new JsonObjectBuilder();
                foreach (var entry in sections)
                {
                    builder.Add(entry.Key == OldKey ? NewKey : entry.Key, entry.Value);
                }

                return new JsonObjectBuilder()
                    .Add("save_version", new JsonNumber(ToVersion))
                    .Add("sections", builder.Build())
                    .Build();
            }
        }

        private static JsonObject BuildMinimalMetaJson(int saveVersion, string slotId, string gameId = "game.demo")
        {
            return new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(saveVersion))
                .Add("slot_id", new JsonString(slotId))
                .Add("created_at", new JsonString("t0"))
                .Add("updated_at", new JsonString("t0"))
                .Add("game_id", new JsonString(gameId))
                .Build();
        }

        // ==== 1. 往返一致性 ======================================================

        [Fact]
        public void RoundTrip_SaveThenLoad_RestoresSectionsAndRngContinuesSameSequence()
        {
            var fs = new StubFileSystem();
            var bus = CreateBus();
            var slotId = new Id("slot.round_trip");

            var rngA = new RngHost(12345UL);
            var flagsA = new RecordingPersistable(SaveSections.WorldStateFlags, new JsonObjectBuilder().Add("door_open", JsonBool.True).Build());
            var notesA = new RecordingPersistable("custom.notes", new JsonString("hello"));
            var rngPersistableA = new RngStreamsPersistable(rngA);
            // 触发流创建，产生一些内部状态。
            rngA.Next(new Id("stream.loot"));

            var sutA = CreateSut(fs, bus: bus);
            sutA.RegisterPersistable(flagsA);
            sutA.RegisterPersistable(notesA);
            sutA.RegisterPersistable(rngPersistableA);

            var saveResult = sutA.Save(new SaveRequest(slotId, "t1"));
            Assert.True(saveResult.Success);

            // 未存读的对照实例：继续消耗同一条流，作为"应得"的后续序列基准。
            var referenceRng = new RngHost(12345UL);
            referenceRng.Next(new Id("stream.loot"));
            var expectedNext = referenceRng.Next(new Id("stream.loot"));

            var rngB = new RngHost(999UL); // 故意用不同种子构造，验证 Load 后被正确覆盖。
            var flagsB = new RecordingPersistable(SaveSections.WorldStateFlags, JsonNull.Instance);
            var notesB = new RecordingPersistable("custom.notes", JsonNull.Instance);
            var rngPersistableB = new RngStreamsPersistable(rngB);

            var sutB = CreateSut(fs, bus: bus);
            sutB.RegisterPersistable(flagsB);
            sutB.RegisterPersistable(notesB);
            sutB.RegisterPersistable(rngPersistableB);

            var loadResult = sutB.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.True(((JsonObject)flagsB.Value)["door_open"] is JsonBool b && b.Value);
            Assert.Equal("hello", ((JsonString)notesB.Value).Value);

            var actualNext = rngB.Next(new Id("stream.loot"));
            Assert.Equal(expectedNext, actualNext);
        }

        /// <summary>P1-04 收口回归：读档前该 <see cref="IRngHost"/> 实例上已经被访问过、但存档里
        /// 没有记录的"残留流"，读档后必须被清空（Load 前 Reset），不能继续携带读档前的状态——否则
        /// 该残留流后续的随机序列与"从未创建过"的正确重放行为不一致。</summary>
        [Fact]
        public void Load_ClearsStaleStreamsNotPresentInSave_AndRestoresMasterSeedDerivation()
        {
            var fs = new StubFileSystem();
            var bus = CreateBus();
            var slotId = new Id("slot.rng_residual");
            var streamA = new Id("stream.a");
            var streamB = new Id("stream.b");

            // 存档一侧：只创建并保存 A，从未创建过 B。
            var rngA = new RngHost(12345UL);
            rngA.Next(streamA);
            var sutA = CreateSut(fs, bus: bus);
            sutA.RegisterPersistable(new RngStreamsPersistable(rngA));
            Assert.True(sutA.Save(new SaveRequest(slotId, "t1")).Success);

            // 干净参照：与存档使用同一主种子，从未读过档，直接创建 B——代表"B 首次出现时应得的序列"。
            var cleanReferenceRng = new RngHost(12345UL);
            var expectedFirstB = cleanReferenceRng.Next(streamB);

            // 读档一侧：读档前先在同一个 Host 实例上访问过 B（制造"残留流"），且用不同的构造种子。
            var rngLoaded = new RngHost(777UL);
            rngLoaded.Next(streamB); // 残留：读档前已经被访问、消耗过随机数，且不在存档里。
            var sutLoad = CreateSut(fs, bus: bus);
            sutLoad.RegisterPersistable(new RngStreamsPersistable(rngLoaded));
            var loadResult = sutLoad.Load(slotId);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);

            // 读档后 B 是"首次被访问"（残留状态已被 Reset 清空），应得到与干净参照一致的序列，
            // 而不是延续读档前的残留状态、也不是延续构造时的错误主种子 777。
            var actualFirstB = rngLoaded.Next(streamB);
            Assert.Equal(expectedFirstB, actualFirstB);

            // A 仍应按存档状态续接（同 RoundTrip 测试）。
            var referenceRngA = new RngHost(12345UL);
            referenceRngA.Next(streamA);
            var expectedNextA = referenceRngA.Next(streamA);
            Assert.Equal(expectedNextA, rngLoaded.Next(streamA));
        }

        /// <summary>P1-03 收口回归：<see cref="RngStreamsPersistable.Save"/> 必须写出
        /// <c>master_seed</c> 字段，供 <see cref="RngStreamsPersistable.Load"/> 恢复用；旧格式
        /// （无该字段）读档时退化为不 Reset，只逐条 SetStreamState（已知的旧存档兼容边界）。</summary>
        [Fact]
        public void Save_WritesMasterSeedField_LoadFallsBackWhenFieldMissing()
        {
            var rng = new RngHost(0xABCDUL);
            var persistable = new RngStreamsPersistable(rng);
            var saved = persistable.Save();

            Assert.IsType<JsonObject>(saved);
            var savedObject = (JsonObject)saved;
            Assert.True(savedObject.TryGetValue("master_seed", out var masterSeedValue));
            var masterSeedText = Assert.IsType<JsonString>(masterSeedValue).Value;
            Assert.Equal(0xABCDUL, ulong.Parse(masterSeedText, System.Globalization.NumberStyles.AllowHexSpecifier));

            // 旧格式（没有 master_seed 字段）：Load 不应抛异常，按旧行为只恢复已存的流。
            var legacyJson = new JsonObjectBuilder()
                .Add("stream.legacy", new JsonString(new RngStreamState(1, 2, 3, 4).ToString()))
                .Build();
            var rngLegacyTarget = new RngHost(999UL);
            new RngStreamsPersistable(rngLegacyTarget).Load(legacyJson);
            Assert.Equal(new RngStreamState(1, 2, 3, 4), rngLegacyTarget.GetStreamState(new Id("stream.legacy")));
        }

        // ==== 2. 加载顺序 ========================================================

        [Fact]
        public void Load_CallsPersistablesInSaveSectionsOrder_CustomSectionsLast()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.order");
            var log = new List<string>();

            var flags = new RecordingPersistable(SaveSections.WorldStateFlags, new JsonObjectBuilder().Build()) { OrderLog = log };
            var notes = new RecordingPersistable("custom.notes", new JsonString("n")) { OrderLog = log };
            // 用 RecordingPersistable 而不是真实 RngStreamsPersistable 承载 rng.stream_states 段：
            // 本测试只关心段的调用顺序，不关心 rng 语义，RecordingPersistable 才会记录调用顺序。
            var rng = new RecordingPersistable(SaveSections.RngStreamStates, new JsonObjectBuilder().Build()) { OrderLog = log };

            var sutSave = CreateSut(fs);
            sutSave.RegisterPersistable(flags);
            sutSave.RegisterPersistable(notes);
            sutSave.RegisterPersistable(rng);
            Assert.True(sutSave.Save(new SaveRequest(slotId, "t1")).Success);

            var flags2 = new RecordingPersistable(SaveSections.WorldStateFlags, JsonNull.Instance) { OrderLog = log };
            var notes2 = new RecordingPersistable("custom.notes", JsonNull.Instance) { OrderLog = log };
            var rng2 = new RecordingPersistable(SaveSections.RngStreamStates, JsonNull.Instance) { OrderLog = log };

            var sutLoad = CreateSut(fs);
            sutLoad.RegisterPersistable(flags2);
            sutLoad.RegisterPersistable(notes2);
            sutLoad.RegisterPersistable(rng2);

            log.Clear();
            var result = sutLoad.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal(new[] { SaveSections.WorldStateFlags, SaveSections.RngStreamStates, "custom.notes" }, log);
        }

        [Fact]
        public void Load_WorldAppendageAndTurnStateSections_FollowKnownOrder_7aBefore7b()
        {
            // W2 收边补齐（A4 审计 F2）：世界附属段（10 第 3 节步骤 7a：world.dropped_loot/
            // world.vendor_stock/world.difficulty/spawn_state）与 sim.turn_state（步骤 7b）此前
            // 未登记进 SaveSections.KnownOrder，落入自定义段分支按 key 的 Ordinal 序数排序，导致
            // sim.turn_state 实际排在全部 7a 段之前，与文档"7a 后 7b"的文字顺序不一致。现已登记，
            // 本测试验证实际调用顺序与 SaveSections.KnownOrder（进而与 10 文档）一致。
            var fs = new StubFileSystem();
            var slotId = new Id("slot.order_7a_7b");
            var log = new List<string>();

            var droppedLoot = new RecordingPersistable(SaveSections.WorldDroppedLoot, new JsonString("a")) { OrderLog = log };
            var vendorStock = new RecordingPersistable(SaveSections.WorldVendorStock, new JsonString("b")) { OrderLog = log };
            var difficulty = new RecordingPersistable(SaveSections.WorldDifficulty, new JsonString("c")) { OrderLog = log };
            var spawnState = new RecordingPersistable(SaveSections.SpawnState, new JsonString("d")) { OrderLog = log };
            var turnState = new RecordingPersistable(SaveSections.SimTurnState, new JsonString("e")) { OrderLog = log };
            var rng = new RecordingPersistable(SaveSections.RngStreamStates, new JsonObjectBuilder().Build()) { OrderLog = log };

            var sutSave = CreateSut(fs);
            // 故意乱序注册（不按 KnownOrder 顺序），验证实际写入顺序只看 SaveSections.KnownOrder，
            // 与 RegisterPersistable 调用顺序无关。
            sutSave.RegisterPersistable(turnState);
            sutSave.RegisterPersistable(rng);
            sutSave.RegisterPersistable(spawnState);
            sutSave.RegisterPersistable(droppedLoot);
            sutSave.RegisterPersistable(difficulty);
            sutSave.RegisterPersistable(vendorStock);
            Assert.True(sutSave.Save(new SaveRequest(slotId, "t1")).Success);

            var sutLoad = CreateSut(fs);
            sutLoad.RegisterPersistable(new RecordingPersistable(SaveSections.SimTurnState, JsonNull.Instance) { OrderLog = log });
            sutLoad.RegisterPersistable(new RecordingPersistable(SaveSections.RngStreamStates, JsonNull.Instance) { OrderLog = log });
            sutLoad.RegisterPersistable(new RecordingPersistable(SaveSections.SpawnState, JsonNull.Instance) { OrderLog = log });
            sutLoad.RegisterPersistable(new RecordingPersistable(SaveSections.WorldDroppedLoot, JsonNull.Instance) { OrderLog = log });
            sutLoad.RegisterPersistable(new RecordingPersistable(SaveSections.WorldDifficulty, JsonNull.Instance) { OrderLog = log });
            sutLoad.RegisterPersistable(new RecordingPersistable(SaveSections.WorldVendorStock, JsonNull.Instance) { OrderLog = log });

            log.Clear();
            var result = sutLoad.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal(
                new[]
                {
                    SaveSections.WorldDroppedLoot, SaveSections.WorldVendorStock, SaveSections.WorldDifficulty,
                    SaveSections.SpawnState, SaveSections.SimTurnState, SaveSections.RngStreamStates,
                },
                log);
        }

        // ==== 3. meta 字段往返 ====================================================

        [Fact]
        public void Save_Meta_RoundTripsVersionSlotGameTimestampsAndDisplaySummary()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.meta");
            var options = new SaveSystemOptions(GameId) { CurrentSaveVersion = 5 };
            var sut = CreateSut(fs, options);

            var summary = new Dictionary<string, string> { ["character_name"] = "Aria", ["level"] = "12" };
            var result = sut.Save(new SaveRequest(slotId, "2026-09-05T00:00:00", playTimeSeconds: 3600, displaySummary: summary, difficultyId: new Id("difficulty.normal")));
            Assert.True(result.Success);

            var loadResult = sut.Load(slotId);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            var meta = loadResult.Meta!;

            Assert.Equal(5, meta.SaveVersion);
            Assert.Equal(slotId, meta.SlotId);
            Assert.Equal(GameId, meta.GameId);
            Assert.Equal("2026-09-05T00:00:00", meta.CreatedAt);
            Assert.Equal("2026-09-05T00:00:00", meta.UpdatedAt);
            Assert.Equal((long?)3600, meta.PlayTimeSeconds);
            Assert.Equal("Aria", meta.DisplaySummary["character_name"]);
            Assert.Equal("12", meta.DisplaySummary["level"]);
            Assert.Equal((Id?)new Id("difficulty.normal"), meta.DifficultyId);
        }

        [Fact]
        public void Save_Meta_PreservesCreatedAtAcrossOverwrite()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.meta_overwrite");
            var sut = CreateSut(fs);

            Assert.True(sut.Save(new SaveRequest(slotId, "t_first")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t_second")).Success);

            var meta = sut.Load(slotId).Meta!;
            Assert.Equal("t_first", meta.CreatedAt);
            Assert.Equal("t_second", meta.UpdatedAt);
        }

        // ==== 3a. LoadResult.CurrentMapId / CurrentPosition（缺口 11）============

        [Fact]
        public void Load_PopulatesCurrentMapIdAndPosition_EvenWithoutRegisteredPersistable()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.world_position");
            var mapId = new Id("world.sample_field");

            var sutSave = CreateSut(fs);
            sutSave.RegisterPersistable(new RecordingPersistable(SaveSections.WorldCurrentMapId, new JsonString(mapId.Value)));
            sutSave.RegisterPersistable(new RecordingPersistable(
                SaveSections.WorldCurrentPosition,
                new JsonObjectBuilder().Add("x", new JsonNumber(12.5)).Add("y", new JsonNumber(-3.25)).Build()));
            Assert.True(sutSave.Save(new SaveRequest(slotId, "t1")).Success);

            // 读档一侧刻意不注册这两个段对应的 IPersistable（模拟"PlayerUnit 尚不存在，场景路由需要
            // 先知道要加载哪张地图"的调用时机），验证 LoadResult 仍能直接从文档解析出这两个字段。
            var sutLoad = CreateSut(fs);
            var result = sutLoad.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal((Id?)mapId, result.CurrentMapId);
            Assert.Equal((Vec2?)new Vec2(12.5, -3.25), result.CurrentPosition);
        }

        [Fact]
        public void Load_OldSaveWithoutWorldPositionSections_CurrentMapIdAndPositionAreNull()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.legacy_save");

            // 旧存档：不注册 world.current_map_id/current_position 对应的段，模拟本字段引入之前
            // 写出的存档文件里根本不存在这两个 key。
            var sutSave = CreateSut(fs);
            sutSave.RegisterPersistable(new RecordingPersistable(SaveSections.WorldStateFlags, new JsonObjectBuilder().Build()));
            Assert.True(sutSave.Save(new SaveRequest(slotId, "t1")).Success);

            var sutLoad = CreateSut(fs);
            var result = sutLoad.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Null(result.CurrentMapId);
            Assert.Null(result.CurrentPosition);
        }

        // ==== 4. ListSlots / DeleteSlot =========================================

        [Fact]
        public void ListSlots_ReturnsSlotsSortedById_AndSkipsUnparsableFile()
        {
            var fs = new StubFileSystem();
            var diagnostics = new InMemorySaveDiagnostics();
            var sut = CreateSut(fs, diagnostics: diagnostics);

            Assert.True(sut.Save(new SaveRequest(new Id("slot.b"), "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(new Id("slot.a"), "t1")).Success);

            // 人为写一份无法解析的"槽文件"（不经 SaveSystem），验证 ListSlots 跳过而不是抛异常。
            fs.WriteTextAtomic(SlotPath("slot.broken"), "{ not valid json");

            var slots = sut.ListSlots();

            Assert.Equal(new[] { "slot.a", "slot.b" }, new[] { slots[0].SlotId.Value, slots[1].SlotId.Value });
            Assert.NotEmpty(diagnostics.Warnings);
        }

        [Fact]
        public void DeleteSlot_RemovesFormalFileAndBackups()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.delete_me");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 2 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success); // 产生 bak1
            Assert.True(sut.Save(new SaveRequest(slotId, "t3")).Success); // 产生 bak1、bak2

            Assert.True(fs.Exists(SlotPath("slot.delete_me")));
            Assert.True(fs.Exists(BackupPath("slot.delete_me", 1)));

            var deleted = sut.DeleteSlot(slotId);

            Assert.True(deleted);
            Assert.False(fs.Exists(SlotPath("slot.delete_me")));
            Assert.False(fs.Exists(BackupPath("slot.delete_me", 1)));
            Assert.False(fs.Exists(BackupPath("slot.delete_me", 2)));
            Assert.False(sut.DeleteSlot(slotId)); // 第二次删除：槽已不存在
        }

        // ==== 5. 备份回退 ========================================================

        [Fact]
        public void Load_FormalFileCorrupted_FallsBackToBackup_ReturnsLoadedFromBackup()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.backup_fallback");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success); // 此时 bak1 = t1 版本内容

            // 人为改坏正式文件（不经 writeTextAtomic 语义之外的手段，纯测试 fixture）。
            fs.WriteTextAtomic(SlotPath("slot.backup_fallback"), "{ this is not json");

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal("t1", result.Meta!.UpdatedAt);
        }

        [Fact]
        public void Load_FormalAndBackupBothCorrupted_ReturnsCorrupted_AndDoesNotTouchFiles()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.all_corrupted");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success);

            fs.WriteTextAtomic(SlotPath("slot.all_corrupted"), "{ broken");
            fs.WriteTextAtomic(BackupPath("slot.all_corrupted", 1), "{ also broken");

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.Corrupted, result.Status);
            // 原始（损坏的）文件应保持原样，不被删除或覆盖。
            Assert.True(fs.Exists(SlotPath("slot.all_corrupted")));
            Assert.Equal("{ broken", fs.ReadText(SlotPath("slot.all_corrupted")));
            Assert.Equal("{ also broken", fs.ReadText(BackupPath("slot.all_corrupted", 1)));
        }

        /// <summary>FND-07 收口回归（外部审核 code-review.md）：正式文件根本不存在（不是"存在但
        /// 损坏"）、但备份完好时，此前 <c>Load</c> 会在检查 <c>_fs.Exists(formalPath)</c> 那一步
        /// 就直接返回 <c>NotFound</c>，从不尝试任何备份——本用例验证修复后会正确回退到备份并
        /// 返回 <c>LoadedFromBackup</c>。</summary>
        [Fact]
        public void Load_FormalFileMissing_BackupExists_FallsBackToBackup_ReturnsLoadedFromBackup()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.formal_missing");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success); // 此时 bak1 = t1 版本内容
            Assert.True(fs.Exists(BackupPath("slot.formal_missing", 1)));

            // 正式文件被删除（不经 DeleteSlot——只删正式文件，模拟"正式文件丢失但备份还在"）。
            fs.DeleteFile(SlotPath("slot.formal_missing"));
            Assert.False(sut.SlotExists(slotId));

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal("t1", result.Meta!.UpdatedAt);
        }

        /// <summary>FND-07 收口回归：信封校验此前只用 <c>ContainsKey</c> 判断
        /// <c>save_version</c>/<c>sections</c> 两个字段是否存在，不检查类型——
        /// <c>{"save_version":1,"sections":null}</c> 这类"字段名齐全、内容是垃圾"的正式文件
        /// 会被当成合法候选放行，从此不再尝试任何备份，直到 <c>Load</c> 更深处的
        /// <c>sections</c> 类型检查才失败，但那时已经错过了本该被尝试的有效备份。本用例验证：
        /// <c>sections:null</c> 的正式文件被信封校验直接拒绝，正确回退到有效备份。</summary>
        [Fact]
        public void Load_FormalFileHasNullSections_TreatedAsInvalidEnvelope_FallsBackToBackup()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.null_sections");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success); // 此时 bak1 = t1 版本内容

            // 人为把正式文件改成"字段名齐全但 sections 是 null"的浅层合法文档。
            fs.WriteTextAtomic(SlotPath("slot.null_sections"), "{\"save_version\":1,\"sections\":null}");

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal("t1", result.Meta!.UpdatedAt);
        }

        // ==== 5b. N15 收边补齐：信封校验仍浅于 Load 后续实际要求 =================
        // 外部审计 68c9bed：FND-07 只把校验加深到"两个顶层字段的容器类型正确"，仍浅于 Load
        // 后续两处实际校验（TryGetInt 要求整数、TryGetSectionsMeta 要求 sections.meta 存在且为
        // 对象）。以下两个复现分别对应审计 repro 的 N15-A、N15-B。

        /// <summary>N15-A：<c>{"save_version":1,"sections":{}}</c>——sections 本身是合法对象（能
        /// 通过 FND-07 的"是不是对象"检查），但缺失必填的 <c>meta</c> 子段。修复前会被当成合法
        /// 候选放行，从此不再尝试备份，直到 Load 更深处的 <c>TryGetSectionsMeta</c> 检查才失败，
        /// 判 Corrupted；此时已经错过本该被尝试的有效备份。</summary>
        [Fact]
        public void Load_FormalFileHasEmptySections_TreatedAsInvalidEnvelope_FallsBackToBackup()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.empty_sections");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success); // 此时 bak1 = t1 版本内容

            fs.WriteTextAtomic(SlotPath("slot.empty_sections"), "{\"save_version\":1,\"sections\":{}}");

            var result = sut.Load(slotId);

            // 修复前该断言会失败：result.Status 会是 Corrupted，不是 LoadedFromBackup。
            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal("t1", result.Meta!.UpdatedAt);
        }

        /// <summary>N15-B：顶层 <c>save_version</c> 被破坏成 <c>1.5</c>（非整数，但仍是合法
        /// <see cref="JsonNumber"/>，能通过 FND-07 的"是不是数字"检查），嵌套的
        /// <c>sections.meta.save_version</c> 保持完好的 <c>1</c>（与外部审计 repro 的构造方式一致：
        /// 只破坏顶层版本号，其余内容完整合法）。修复前会被当成合法候选放行，直到 Load 更深处的
        /// <c>TryGetInt</c> 整数校验才失败，判 Corrupted。</summary>
        [Fact]
        public void Load_FormalFileHasNonIntegerTopLevelVersion_TreatedAsInvalidEnvelope_FallsBackToBackup()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.non_integer_version");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success); // 此时 bak1 = t1 版本内容（完整合法）。

            // 只破坏顶层 save_version（改成非整数 1.5），sections.meta.save_version 原样保留为 1。
            var corruptedDoc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1.5))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(1, "slot.non_integer_version"))
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.non_integer_version"), JsonWriter.Write(corruptedDoc));

            var result = sut.Load(slotId);

            // 修复前该断言会失败：result.Status 会是 Corrupted，不是 LoadedFromBackup。
            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal("t1", result.Meta!.UpdatedAt);
        }

        // ==== 5a. FND-01 收口回归：备份路径与合法槽路径不再可能碰撞 =============

        /// <summary>FND-01 收口回归（外部审核 code-review.md，验证复现 validation-repros.txt R3）：
        /// 合法槽 `slot.a.bak1` 的正式文件名此前与槽 `slot.a` 的第 1 份备份路径逐字节相同——
        /// 保存/覆盖 `slot.a` 会连带覆盖 `slot.a.bak1` 的内容，`ListSlots` 还会把 `slot.a.bak1`
        /// 误判成备份而从结果里隐藏。本用例验证修复后两个槽完全独立：各自的内容互不覆盖，都能
        /// 被 `ListSlots`/`Load`/`DeleteSlot` 正确处理。</summary>
        [Fact]
        public void SlotIdLooksLikeBackupFileName_IsIndependentFromRealBackup_ListedLoadedAndDeletedCorrectly()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });
            var lookAlikeSlot = new Id("slot.a.bak1"); // 名字"长得像" slot.a 的第 1 份备份。
            var realSlot = new Id("slot.a");

            var payload = new RecordingPersistable("custom.payload", new JsonString("X"));
            sut.RegisterPersistable(payload);

            // 先保存"长得像备份"的合法槽，写出内容 X。
            payload.Value = new JsonString("X");
            Assert.True(sut.Save(new SaveRequest(lookAlikeSlot, "t0")).Success);

            // 再保存真正的 slot.a 两次，产生它自己的 bak1（内容应为 A，不应触碰 lookAlikeSlot）。
            payload.Value = new JsonString("A");
            Assert.True(sut.Save(new SaveRequest(realSlot, "t1")).Success);
            payload.Value = new JsonString("B");
            Assert.True(sut.Save(new SaveRequest(realSlot, "t2")).Success);

            // 1) lookAlikeSlot 自己的正式文件内容不受 slot.a 备份轮转影响，仍是 X。
            var lookAlikeLoad = sut.Load(lookAlikeSlot);
            Assert.Equal(LoadStatus.Loaded, lookAlikeLoad.Status);
            Assert.Equal("X", ((JsonString)payload.Value).Value);

            // 2) slot.a 自己的备份（独立子目录）内容正确，是它自己迁移前的版本 A，不是 X。
            var slotABackupText = fs.ReadText(BackupPath("slot.a", 1));
            Assert.NotNull(slotABackupText);
            Assert.Contains("\"A\"", slotABackupText);

            // 3) ListSlots 同时列出两个槽（此前的按文件名过滤会把 lookAlikeSlot 隐藏）。
            var slotIds = sut.ListSlots().Select(s => s.SlotId.Value).ToArray();
            Assert.Contains("slot.a", slotIds);
            Assert.Contains("slot.a.bak1", slotIds);
            Assert.Equal(2, slotIds.Length);

            // 4) 删除 lookAlikeSlot 不影响 slot.a 及其备份。
            Assert.True(sut.DeleteSlot(lookAlikeSlot));
            Assert.True(sut.SlotExists(realSlot));
            Assert.True(fs.Exists(BackupPath("slot.a", 1)));
        }

        // ==== 5c. N16 收边补齐：旧顶层备份布局兼容读取 ==========================
        // 外部审计 68c9bed：FND-01 把备份挪到 backups/ 子目录后，代码只会向新路径读写，升级前
        // 遗留在顶层的旧布局备份文件（<slotId>.bakN.json，与正式槽同目录）从此既读不到也用不上。
        // 判断记录见 SaveSystem.TryReadLegacyBackup 类型注释：只按精确路径只读探测、不做目录扫描、
        // 不删除/不移动旧文件——那种做法会与上面 FND-01 回归测试锁定的"槽 id 允许长得像
        // <slot>.bakN"这一硬约束冲突（同一个文件名歧义，回归测试已经证明"猜测式过滤/迁移"在这里
        // 是真实的数据损坏风险），所以本组测试不验证"旧文件被移动/删除"或"不计入 ListSlots 配额"
        // ——那两点在现有硬约束下无法安全达成，见 WA.md 判断记录。

        /// <summary>N16 核心验收：旧布局主档损坏、且新布局备份也不存在时，仍能从旧顶层布局遗留的
        /// 备份文件恢复。</summary>
        [Fact]
        public void Load_FormalCorrupted_NoNewLayoutBackup_LegacyTopLevelBackupExists_RecoversFromIt()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.legacy_backup");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            // 模拟"升级前用旧代码产生的备份"：直接在顶层目录（与正式槽同目录，FND-01 之前的布局）
            // 写一份合法的旧顶层备份文件，不经由本版本的 RotateBackups（那只会写新布局路径）。
            var legacyDoc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(1, "slot.legacy_backup"))
                    .Add("legacy.payload", new JsonString("from-legacy-backup"))
                    .Build())
                .Build();
            fs.WriteTextAtomic("user://saves/slot.legacy_backup.bak1.json", JsonWriter.Write(legacyDoc));

            // 正式文件损坏，新布局备份（backups/slot.legacy_backup.bak1.json）不存在。
            fs.WriteTextAtomic(SlotPath("slot.legacy_backup"), "{ broken");
            Assert.False(fs.Exists(BackupPath("slot.legacy_backup", 1)));

            var result = sut.Load(slotId);

            // 修复前该断言会失败：result.Status 会是 Corrupted（旧顶层备份完全不会被尝试）。
            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal("slot.legacy_backup", result.Meta!.SlotId.Value);
        }

        /// <summary>N16 附加验收：从旧顶层备份恢复后，内容会顺手续存到新布局路径，供之后正常的
        /// 备份轮转机制接续使用（见 SaveSystem.TryReadLegacyBackup 判断记录"顺手续存"）。</summary>
        [Fact]
        public void Load_RecoveredFromLegacyBackup_AlsoPersistsCopyUnderNewLayoutPath()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.legacy_backup_persist");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            var legacyDoc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(1, "slot.legacy_backup_persist"))
                    .Build())
                .Build();
            var legacyText = JsonWriter.Write(legacyDoc);
            fs.WriteTextAtomic("user://saves/slot.legacy_backup_persist.bak1.json", legacyText);
            fs.WriteTextAtomic(SlotPath("slot.legacy_backup_persist"), "{ broken");

            var result = sut.Load(slotId);
            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);

            var newLayoutText = fs.ReadText(BackupPath("slot.legacy_backup_persist", 1));
            Assert.NotNull(newLayoutText);
        }

        /// <summary>旧顶层备份不存在（全新槽/从未在旧版本下运行过）时，行为与修复前完全一致——
        /// 正式文件与新布局备份都缺失即 NotFound，不会因为多探测了一条路径而产生任何副作用。</summary>
        [Fact]
        public void Load_NoFormalNoNewBackupNoLegacyBackup_ReturnsNotFound_Unaffected()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.never_existed");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.NotFound, result.Status);
        }

        // ==== 5d. C01 收口（外部审计 7e63d66 第四轮）：合法槽名与旧顶层备份路径撞名时不跨槽误读 ==
        // TryReadLegacyBackup 按精确路径 "<SavesDir>/<slotId>.bakN.json" 只读探测；这条路径本身
        // 也是一个合法槽名（例如 "slot.a.bak1"）的正式文件路径。此前只要该路径下内容能通过信封
        // 校验就无条件当作 slotId 的备份返回，会把另一个独立正式槽的存档当成 slotId 的旧备份读
        // 出来（外部审计 CORE-A 真实复现，见 audit-7e63d66-20260907/repro/Program.cs）。

        /// <summary>核心复现：真实保存合法正式槽 "slot.a.bak1"，随后 Load("slot.a")（该槽从未
        /// 保存过、也没有任何新布局备份）。修复前结果是 LoadedFromBackup 并读到 slot.a.bak1 的
        /// 业务内容；修复后必须是 NotFound，且不产生任何跨槽副作用（不会往 slot.a 的新布局备份
        /// 目录写入从 slot.a.bak1 抄来的内容）。</summary>
        [Fact]
        public void Load_RequestedSlotHasNoOwnBackup_ButPathCollidesWithAnotherRealSlotsFormalFile_ReturnsNotFound_NoCrossSlotRead()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });
            var lookAlikeSlot = new Id("slot.a.bak1"); // 名字与 "slot.a" 的旧顶层备份路径逐字节相同。
            var requestedSlot = new Id("slot.a");

            var payload = new RecordingPersistable("custom.payload", new JsonString("from-other-slot"));
            sut.RegisterPersistable(payload);

            Assert.True(sut.Save(new SaveRequest(lookAlikeSlot, "t0")).Success);

            // 把段值改掉，验证 Load(slot.a) 不会把它跨槽读回来。
            payload.Value = new JsonString("should-not-leak-into-slot-a");

            var result = sut.Load(requestedSlot);

            // 修复前该断言会失败：result.Status 会是 LoadedFromBackup，marker 会是 "from-other-slot"。
            Assert.Equal(LoadStatus.NotFound, result.Status);

            // slot.a.bak1 作为独立正式槽仍然完整可用：能被单独 Load、出现在 ListSlots、能被单独删除。
            var lookAlikeLoad = sut.Load(lookAlikeSlot);
            Assert.Equal(LoadStatus.Loaded, lookAlikeLoad.Status);

            var slotIds = sut.ListSlots().Select(s => s.SlotId.Value).ToArray();
            Assert.Contains("slot.a.bak1", slotIds);
            Assert.DoesNotContain("slot.a", slotIds);

            Assert.False(fs.Exists(BackupPath("slot.a", 1))); // 不应产生跨槽续存副作用。
            Assert.True(sut.DeleteSlot(lookAlikeSlot));
        }

        /// <summary>meta.slot_id 缺失的真正旧存档（早于 slot_id 字段引入）仍然按原语义放行——
        /// C01 收口只拒绝"显式声明的 slot_id 与请求槽名不一致"的候选，不影响 N16 已有回归。</summary>
        [Fact]
        public void Load_LegacyBackupWithoutSlotIdField_StillRecoversForRequestedSlot()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.legacy_no_slot_id");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            var legacyMetaWithoutSlotId = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("created_at", new JsonString("t0"))
                .Add("updated_at", new JsonString("t0"))
                .Add("game_id", new JsonString("game.demo"))
                .Build();
            var legacyDoc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, legacyMetaWithoutSlotId)
                    .Build())
                .Build();
            fs.WriteTextAtomic("user://saves/slot.legacy_no_slot_id.bak1.json", JsonWriter.Write(legacyDoc));
            fs.WriteTextAtomic(SlotPath("slot.legacy_no_slot_id"), "{ broken");

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
        }

        // ==== 5e. C10 收口（外部审计 7e63d66 第四轮）：候选筛选阶段复用 meta 语义校验 =========
        // TryParseEnvelope 只校验信封顶层形状（save_version 是整数、sections.meta 是对象），不校验
        // ParseMeta 实际要求的语义必填字段。此前候选一旦通过顶层形状校验就被选中并停止尝试其它
        // 候选，真正解析 meta 时才判 Corrupted，即便存在健康备份也不会被尝试。

        /// <summary>核心验收：正式文件顶层信封形状合法，但 sections.meta 缺失 save_version/
        /// created_at 等语义必填字段；存在一份健康备份。修复前 Load 直接判 Corrupted，不会尝试
        /// 备份；修复后必须回退到健康备份并保留诊断（LoadedFromBackup）。</summary>
        [Fact]
        public void Load_FormalPassesShapeCheckButMetaMissingRequiredFields_FallsBackToHealthyBackup()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.meta_semantic_gap");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.Save(new SaveRequest(slotId, "t2")).Success); // 此时 bak1 = t1 的完整合法内容。

            // 顶层形状合法（save_version 是整数、sections.meta 是对象），meta 内部却是空对象——
            // TryParseEnvelope 会放行，ParseMeta 会因缺少 save_version/created_at 等必填字段失败。
            var brokenMetaDoc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, new JsonObjectBuilder().Build())
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.meta_semantic_gap"), JsonWriter.Write(brokenMetaDoc));

            var result = sut.Load(slotId);

            // 修复前该断言会失败：result.Status 会是 Corrupted，不是 LoadedFromBackup。
            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal("t1", result.Meta!.UpdatedAt);
        }

        /// <summary>没有任何健康候选时，meta 语义缺陷仍然正确判 Corrupted（不是误判为 NotFound
        /// 或静默放行一份读不出必填字段的 meta）。</summary>
        [Fact]
        public void Load_FormalMetaSemanticGap_NoHealthyCandidateAvailable_ReturnsCorrupted()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.meta_semantic_gap_no_backup");
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 1 });

            var brokenMetaDoc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, new JsonObjectBuilder().Build())
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.meta_semantic_gap_no_backup"), JsonWriter.Write(brokenMetaDoc));

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.Corrupted, result.Status);
        }

        // ==== 6. 写入失败 ========================================================

        [Fact]
        public void Save_WriteFails_ReturnsWriteFailed_AndOldSaveStillLoadable()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.write_fail");
            // BackupCount=0：避免备份轮转消耗掉 FailNextWrite 这一次失败机会（本测试要精确
            // 模拟"正式文件那一次 WriteTextAtomic 调用失败"，备份轮转本身也会调用
            // WriteTextAtomic，见 SaveSystem.RotateBackups）。
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { BackupCount = 0 });

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);

            fs.FailNextWrite();
            var result = sut.Save(new SaveRequest(slotId, "t2"));

            Assert.False(result.Success);
            Assert.Equal(SaveFailureReason.WriteFailed, result.Reason);

            var loadResult = sut.Load(slotId);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);
            Assert.Equal("t1", loadResult.Meta!.UpdatedAt);
        }

        // ==== 7. 版本迁移 ========================================================

        [Fact]
        public void Load_TwoStepMigrationChain_MigratesAndPublishesMigratedEvent()
        {
            var fs = new StubFileSystem();
            var bus = CreateBus();
            var slotId = new Id("slot.migrate");

            var v1Doc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(1, "slot.migrate"))
                    .Add("legacy.notes", new JsonString("hello-v1"))
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.migrate"), JsonWriter.Write(v1Doc));

            var options = new SaveSystemOptions(GameId) { CurrentSaveVersion = 3 };
            var sut = CreateSut(fs, options, bus);
            sut.RegisterMigration(new RenameSectionMigration(1, 2, "legacy.notes", "mid.notes"));
            sut.RegisterMigration(new RenameSectionMigration(2, 3, "mid.notes", "final.notes"));

            var finalPersistable = new RecordingPersistable("final.notes", JsonNull.Instance);
            sut.RegisterPersistable(finalPersistable);

            SaveMigratedEvent? migratedEvent = null;
            bus.Subscribe<SaveMigratedEvent>(SaveEventKeys.SaveMigrated, e => migratedEvent = e);

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal((int?)1, result.MigratedFromVersion);
            Assert.Equal("hello-v1", ((JsonString)finalPersistable.Value).Value);

            Assert.NotNull(migratedEvent);
            Assert.Equal(slotId, migratedEvent!.SlotId);
            Assert.Equal(1, migratedEvent.FromVersion);
            Assert.Equal(3, migratedEvent.ToVersion);
        }

        [Fact]
        public void Load_MissingMigrationLink_ReturnsMigrationFailed()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.migrate_missing");

            var v1Doc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(1, "slot.migrate_missing"))
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.migrate_missing"), JsonWriter.Write(v1Doc));

            var options = new SaveSystemOptions(GameId) { CurrentSaveVersion = 3 };
            var sut = CreateSut(fs, options);
            sut.RegisterMigration(new RenameSectionMigration(1, 2, "a", "b")); // 缺 2 -> 3

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.MigrationFailed, result.Status);
        }

        /// <summary>FND-09 收口回归（外部审核 code-review.md，验证复现 validation-repros.txt R5
        /// 对应场景）：登记了一条 <c>1 → 3</c> 的迁移函数，但当前运行时版本只到 2——此前的循环
        /// 条件只看 <c>version &lt; CurrentSaveVersion</c>，跑完这一步后 version 变成 3（不再
        /// 小于 2），循环误判"已到达终点"并返回成功，越级迁移到一个从未经过当前版本验证的结构。
        /// 本用例验证：修复后这种"单步迁移会越过当前运行时版本"的情形被拒绝为
        /// <c>MigrationFailed</c>，不加载任何段、不覆盖原始文件。</summary>
        [Fact]
        public void Load_MigrationStepOvershootsCurrentVersion_ReturnsMigrationFailed_DoesNotLoadSections()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.migrate_overshoot");

            var v1Doc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(1, "slot.migrate_overshoot"))
                    .Add("legacy.notes", new JsonString("hello-v1"))
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.migrate_overshoot"), JsonWriter.Write(v1Doc));

            // 当前运行时版本只到 2，但登记的迁移函数是 1 → 3（越过了当前版本）。
            var options = new SaveSystemOptions(GameId) { CurrentSaveVersion = 2 };
            var sut = CreateSut(fs, options);
            sut.RegisterMigration(new RenameSectionMigration(1, 3, "legacy.notes", "final.notes"));

            var finalPersistable = new RecordingPersistable("final.notes", JsonNull.Instance);
            sut.RegisterPersistable(finalPersistable);

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.MigrationFailed, result.Status);
            Assert.Equal(JsonNull.Instance, finalPersistable.Value); // 未被调用过 Load，仍是初始值。
            // 原始文件保持不变（内容仍是迁移前的 v1 文档，未被覆盖/删除）。
            Assert.Equal(JsonWriter.Write(v1Doc), fs.ReadText(SlotPath("slot.migrate_overshoot")));
        }

        /// <summary>FND-09 收口对照组：当前运行时版本恰好等于迁移函数的 ToVersion（不是"越过"，
        /// 是"恰好到达"）时，同一条 <c>1 → 3</c> 迁移函数应当继续正常成功——收口只拒绝越界，不
        /// 应误伤本来就合法的"终点恰好等于当前版本"场景。</summary>
        [Fact]
        public void Load_MigrationStepLandsExactlyOnCurrentVersion_StillSucceeds()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.migrate_exact");

            var v1Doc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(1))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(1, "slot.migrate_exact"))
                    .Add("legacy.notes", new JsonString("hello-v1"))
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.migrate_exact"), JsonWriter.Write(v1Doc));

            var options = new SaveSystemOptions(GameId) { CurrentSaveVersion = 3 };
            var sut = CreateSut(fs, options);
            sut.RegisterMigration(new RenameSectionMigration(1, 3, "legacy.notes", "final.notes"));

            var finalPersistable = new RecordingPersistable("final.notes", JsonNull.Instance);
            sut.RegisterPersistable(finalPersistable);

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal((int?)1, result.MigratedFromVersion);
            Assert.Equal("hello-v1", ((JsonString)finalPersistable.Value).Value);
        }

        [Fact]
        public void Load_DocVersionHigherThanCurrent_ReturnsMigrationFailed()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.future_version");

            var v4Doc = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(4))
                .Add("sections", new JsonObjectBuilder()
                    .Add(SaveSections.Meta, BuildMinimalMetaJson(4, "slot.future_version"))
                    .Build())
                .Build();
            fs.WriteTextAtomic(SlotPath("slot.future_version"), JsonWriter.Write(v4Doc));

            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { CurrentSaveVersion = 3 });

            var result = sut.Load(slotId);

            Assert.Equal(LoadStatus.MigrationFailed, result.Status);
        }

        [Fact]
        public void ISaveMigration_IrreversibleFlag_IsReadable()
        {
            var migration = new RenameSectionMigration(1, 2, "a", "b", irreversible: true);
            Assert.True(migration.Irreversible);

            var reversible = new RenameSectionMigration(2, 3, "b", "c");
            Assert.False(reversible.Irreversible);
        }

        // ==== 8. MaxSlots ========================================================

        [Fact]
        public void Save_MaxSlotsReached_NewSlotFails_ButOverwriteOfExistingSlotSucceeds()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs, new SaveSystemOptions(GameId) { MaxSlots = 1 });

            var firstResult = sut.Save(new SaveRequest(new Id("slot.first"), "t1"));
            Assert.True(firstResult.Success);

            var secondResult = sut.Save(new SaveRequest(new Id("slot.second"), "t1"));
            Assert.False(secondResult.Success);
            Assert.Equal(SaveFailureReason.SlotLimitReached, secondResult.Reason);

            var overwriteResult = sut.Save(new SaveRequest(new Id("slot.first"), "t2"));
            Assert.True(overwriteResult.Success);
        }

        // ==== 9. 事件字段 ========================================================

        [Fact]
        public void Events_SaveCompletedAndSaveLoaded_CarryCorrectSlotId()
        {
            var fs = new StubFileSystem();
            var bus = CreateBus();
            var slotId = new Id("slot.events");
            var sut = CreateSut(fs, bus: bus);

            SaveCompletedEvent? completed = null;
            SaveLoadedEvent? loaded = null;
            bus.Subscribe<SaveCompletedEvent>(SaveEventKeys.SaveCompleted, e => completed = e);
            bus.Subscribe<SaveLoadedEvent>(SaveEventKeys.SaveLoaded, e => loaded = e);

            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.NotNull(completed);
            Assert.Equal(slotId, completed!.SlotId);

            Assert.Equal(LoadStatus.Loaded, sut.Load(slotId).Status);
            Assert.NotNull(loaded);
            Assert.Equal(slotId, loaded!.SlotId);
        }

        // ==== 10. ShouldAutoSave 默认策略 ========================================

        [Fact]
        public void ShouldAutoSave_DefaultPolicy_MatchesDocumentDefaults()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs);

            Assert.True(sut.ShouldAutoSave(AutoSaveTrigger.SavePoint));
            Assert.False(sut.ShouldAutoSave(AutoSaveTrigger.MapSwitch));
            Assert.True(sut.ShouldAutoSave(AutoSaveTrigger.QuestComplete));
        }

        [Fact]
        public void ShouldAutoSave_CustomPolicy_IsRespected()
        {
            var fs = new StubFileSystem();
            var options = new SaveSystemOptions(GameId)
            {
                AutoSave = new AutoSavePolicy { OnSavePoint = false, OnMapSwitch = true, OnQuestComplete = false },
            };
            var sut = CreateSut(fs, options);

            Assert.False(sut.ShouldAutoSave(AutoSaveTrigger.SavePoint));
            Assert.True(sut.ShouldAutoSave(AutoSaveTrigger.MapSwitch));
            Assert.False(sut.ShouldAutoSave(AutoSaveTrigger.QuestComplete));
        }

        // ==== 11. 设置存储 =======================================================

        [Fact]
        public void SettingsStore_LoadWithoutFile_ReturnsEmptyObject()
        {
            var fs = new StubFileSystem();
            var store = new Core.Foundation.SaveSystem.SettingsStore(fs);

            var loaded = store.Load();

            Assert.Empty(loaded);
            Assert.Equal(1, store.SettingsVersion);
        }

        [Fact]
        public void SettingsStore_SaveThenLoad_RoundTrips_AndDoesNotAffectSavesDir()
        {
            var fs = new StubFileSystem();
            var store = new Core.Foundation.SaveSystem.SettingsStore(fs);
            var sut = CreateSut(fs);

            var data = new JsonObjectBuilder().Add("volume_music", new JsonNumber(0.5)).Add("locale", new JsonString("zh-CN")).Build();
            Assert.True(store.Save(data));

            var loaded = store.Load();
            Assert.Equal(0.5, ((JsonNumber)loaded["volume_music"]).Value);
            Assert.Equal("zh-CN", ((JsonString)loaded["locale"]).Value);

            // 设置文件与存档目录互不影响：存档槽列表仍为空，设置文件不出现在其中。
            Assert.Empty(sut.ListSlots());
            Assert.True(fs.Exists("user://settings.json"));
            Assert.False(fs.Exists(SlotPath("settings")));
        }

        // ==== 12. 重复注册 / Save 异常 ============================================

        [Fact]
        public void RegisterPersistable_DuplicateSectionKey_Throws()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs);

            sut.RegisterPersistable(new RecordingPersistable("custom.dup", JsonNull.Instance));

            Assert.Throws<InvalidOperationException>(() =>
                sut.RegisterPersistable(new RecordingPersistable("custom.dup", JsonNull.Instance)));
        }

        [Fact]
        public void RegisterPersistable_MetaSectionKey_Throws()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs);

            Assert.Throws<ArgumentException>(() =>
                sut.RegisterPersistable(new RecordingPersistable(SaveSections.Meta, JsonNull.Instance)));
        }

        [Fact]
        public void RegisterMigration_DuplicateFromVersion_Throws()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs);

            sut.RegisterMigration(new RenameSectionMigration(1, 2, "a", "b"));

            Assert.Throws<InvalidOperationException>(() =>
                sut.RegisterMigration(new RenameSectionMigration(1, 2, "c", "d")));
        }

        [Fact]
        public void Save_PersistableSaveThrows_ReturnsPersistableThrew_AndWritesNoFile()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.save_throws");
            var sut = CreateSut(fs);
            sut.RegisterPersistable(new RecordingPersistable("custom.boom", JsonNull.Instance) { ThrowOnSave = true });

            var result = sut.Save(new SaveRequest(slotId, "t1"));

            Assert.False(result.Success);
            Assert.Equal(SaveFailureReason.PersistableThrew, result.Reason);
            Assert.False(fs.Exists(SlotPath("slot.save_throws")));
        }

        [Fact]
        public void Load_PersistableLoadThrows_ReturnsPersistableThrew()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.load_throws");
            var sut = CreateSut(fs);
            sut.RegisterPersistable(new RecordingPersistable("custom.boom", new JsonString("x")));
            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);

            var sutLoad = CreateSut(fs);
            sutLoad.RegisterPersistable(new RecordingPersistable("custom.boom", JsonNull.Instance) { ThrowOnLoad = true });

            var result = sutLoad.Load(slotId);

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);
            Assert.NotNull(result.Meta);
        }

        // ==== 基础行为 ===========================================================

        [Fact]
        public void Load_SlotNotFound_ReturnsNotFound()
        {
            var fs = new StubFileSystem();
            var sut = CreateSut(fs);

            var result = sut.Load(new Id("slot.nope"));

            Assert.Equal(LoadStatus.NotFound, result.Status);
        }

        [Fact]
        public void SlotExists_ReflectsFormalFileExistence()
        {
            var fs = new StubFileSystem();
            var slotId = new Id("slot.exists_check");
            var sut = CreateSut(fs);

            Assert.False(sut.SlotExists(slotId));
            Assert.True(sut.Save(new SaveRequest(slotId, "t1")).Success);
            Assert.True(sut.SlotExists(slotId));
        }
    }
}

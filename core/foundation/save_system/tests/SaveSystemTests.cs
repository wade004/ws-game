using System;
using System.Collections.Generic;
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

        private static string BackupPath(string slotId, int index, string savesDir = "saves") =>
            $"user://{savesDir}/{slotId}.bak{index}.json";

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

using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Xunit;
using SaveSys = Core.Foundation.SaveSystem.SaveSystem;

namespace Tests.Foundation.SaveSystem
{
    /// <summary>
    /// 测试覆盖梳理 T-H2（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：读档回调
    /// <see cref="IDerivedStateRebuilder.BeforeLoad"/>/<see cref="IDerivedStateRebuilder.OnSectionLoaded"/>
    /// 与 <c>Load(slot, deferLoadedNotification)</c>/<see cref="ISaveSystem.NotifyLoaded"/> 此前在
    /// foundation 内零测试（只在 gameplay/numbers 层被间接覆盖）。本文件固定：
    /// <list type="bullet">
    /// <item>成功路径的调用次数与顺序（BeforeLoad 先于任何段 Load；每个成功段恰好一次 OnSectionLoaded，
    /// 紧跟该段 Load；段顺序 = 已知段固定全序 + 自定义段 ordinal 序）；</item>
    /// <item>回滚路径：失败段之前的成功段各被再回调一次；失败段自身回滚成功才回调；player.vitals 追加回滚；</item>
    /// <item>NotFound / Corrupted / MigrationFailed 不调用任何回调；</item>
    /// <item>钩子自身抛异常只记诊断、不改变读档结果；</item>
    /// <item>延迟通知：defer 为 true 时 Load 不派发 save.loaded/save.migrated，<c>NotifyLoaded</c> 后才派发且一次；</item>
    /// <item>11 第 6 节勘误①"恢复完成同一时刻派生状态一致"：loaded 通知派发时派生值已等于按存档数据算出的值。</item>
    /// </list>
    /// </summary>
    public sealed class SaveSystemLoadCallbackTests
    {
        private static readonly Id GameId = new Id("game.demo");

        private static readonly Id Slot = new Id("slot.cb");

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

        private static SaveSys CreateSut(
            StubFileSystem fs, int saveVersion = 1, IEventBus? bus = null, ISaveDiagnostics? diagnostics = null)
        {
            return new SaveSys(fs, new SaveSystemOptions(GameId) { CurrentSaveVersion = saveVersion }, bus, diagnostics);
        }

        /// <summary>共享事件日志的段：<c>load:&lt;key&gt;</c> 记录每次 Load 调用；可按"收到的数据"决定是否抛异常。</summary>
        private sealed class LoggingSection : IPersistable
        {
            private readonly List<string> _log;

            public string SectionKey { get; }

            public JsonValue Value { get; set; }

            /// <summary>收到的数据满足该谓词时抛异常（默认永不抛）。</summary>
            public Func<JsonValue, bool> ThrowWhen { get; set; } = _ => false;

            public bool KeepStateWhenSectionMissing { get; set; }

            public LoggingSection(string key, JsonValue initial, List<string> log)
            {
                SectionKey = key;
                Value = initial;
                _log = log;
            }

            public JsonValue Save() => Value;

            public void Load(JsonValue data)
            {
                _log.Add("load:" + SectionKey);
                if (ThrowWhen(data))
                {
                    throw new InvalidOperationException("boom-load:" + SectionKey);
                }

                Value = data;
            }
        }

        private sealed class LoggingRebuilder : IDerivedStateRebuilder
        {
            private readonly List<string> _log;

            public int BeforeLoadCount { get; private set; }

            public List<string> SectionCalls { get; } = new List<string>();

            public bool ThrowOnBeforeLoad { get; set; }

            public bool ThrowOnSectionLoaded { get; set; }

            public Action<string>? OnSection { get; set; }

            public LoggingRebuilder(List<string> log) => _log = log;

            public void BeforeLoad()
            {
                BeforeLoadCount++;
                _log.Add("before");
                if (ThrowOnBeforeLoad)
                {
                    throw new InvalidOperationException("boom-before");
                }
            }

            public void OnSectionLoaded(string sectionKey)
            {
                SectionCalls.Add(sectionKey);
                _log.Add("rebuilt:" + sectionKey);
                OnSection?.Invoke(sectionKey);
                if (ThrowOnSectionLoaded)
                {
                    throw new InvalidOperationException("boom-rebuilt:" + sectionKey);
                }
            }
        }

        /// <summary>已知段按 <see cref="SaveSections.KnownOrder"/> 全序，其后自定义段按 ordinal 序——
        /// 与 <c>SaveSystem.ComputeWriteOrder</c> 文档化的规则一致（期望值由规则算出，不写死顺序）。</summary>
        private static List<string> ExpectedOrder(IEnumerable<string> registeredKeys)
        {
            var keys = registeredKeys.ToList();
            var known = SaveSections.KnownOrder.Where(keys.Contains).ToList();
            var custom = keys.Where(k => !SaveSections.KnownOrder.Contains(k)).OrderBy(k => k, StringComparer.Ordinal);
            known.AddRange(custom);
            return known;
        }

        /// <summary>用一个独立 SaveSystem 把 <paramref name="docValues"/> 存成槽文件（文档值）。</summary>
        private static void SaveDoc(StubFileSystem fs, int saveVersion, IReadOnlyDictionary<string, JsonValue> docValues)
        {
            var log = new List<string>();
            var saver = CreateSut(fs, saveVersion);
            foreach (var pair in docValues)
            {
                saver.RegisterPersistable(new LoggingSection(pair.Key, pair.Value, log));
            }

            Assert.True(saver.Save(new SaveRequest(Slot, "t")).Success);
        }

        private static JsonValue Text(string s) => new JsonString(s);

        // ---- 成功路径：次数与顺序 ---------------------------------------------------

        [Fact]
        public void Load_Success_CallsBeforeLoadOnceFirst_ThenLoadAndOnSectionLoadedPerSectionInOrder()
        {
            var fs = new StubFileSystem();
            var docKeys = new[] { "custom.z", SaveSections.WorldStateFlags, "custom.m", SaveSections.PlayerProgression };
            SaveDoc(fs, 1, docKeys.ToDictionary(k => k, k => Text("doc:" + k)));

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var sut = CreateSut(fs);
            foreach (var k in docKeys)
            {
                sut.RegisterPersistable(new LoggingSection(k, Text("pre:" + k), log));
            }

            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal(1, rebuilder.BeforeLoadCount);

            var order = ExpectedOrder(docKeys);
            var expected = new List<string> { "before" };
            foreach (var k in order)
            {
                expected.Add("load:" + k);
                expected.Add("rebuilt:" + k);
            }

            Assert.Equal(expected, log);
            // OnSectionLoaded 每个段恰好一次，且不含 meta 段。
            Assert.Equal(order, rebuilder.SectionCalls);
            Assert.DoesNotContain(SaveSections.Meta, rebuilder.SectionCalls);
        }

        [Fact]
        public void Load_Success_WithoutRebuilder_BehavesTheSame_NoCallbackRequired()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });

            var log = new List<string>();
            var sut = CreateSut(fs);
            var section = new LoggingSection("custom.a", Text("pre-a"), log);
            sut.RegisterPersistable(section);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal("doc-a", ((JsonString)section.Value).Value);
        }

        [Fact]
        public void SetDerivedStateRebuilder_Null_Throws()
        {
            var sut = CreateSut(new StubFileSystem());

            Assert.Throws<ArgumentNullException>(() => sut.SetDerivedStateRebuilder(null!));
        }

        [Fact]
        public void Load_SectionMissingFromDocument_LoadsJsonNullThenStillCallsOnSectionLoaded()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var sut = CreateSut(fs);
            var a = new LoggingSection("custom.a", Text("pre-a"), log);
            // custom.b 已注册但存档里没有这个段（旧档）：默认语义是 Load(JsonNull) 清空，同样算"成功加载了一段"。
            var b = new LoggingSection("custom.b", Text("pre-b"), log);
            sut.RegisterPersistable(a);
            sut.RegisterPersistable(b);
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.IsType<JsonNull>(b.Value);
            Assert.Equal(new[] { "custom.a", "custom.b" }, rebuilder.SectionCalls);
        }

        [Fact]
        public void Load_SectionMissing_KeepStateOptIn_CallsNeitherLoadNorOnSectionLoaded()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var sut = CreateSut(fs);
            var keeper = new LoggingSection("custom.keep", Text("pre-keep"), log) { KeepStateWhenSectionMissing = true };
            sut.RegisterPersistable(new LoggingSection("custom.a", Text("pre-a"), log));
            sut.RegisterPersistable(keeper);
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal("pre-keep", ((JsonString)keeper.Value).Value);
            Assert.DoesNotContain("load:custom.keep", log);
            Assert.Equal(new[] { "custom.a" }, rebuilder.SectionCalls);
            Assert.Equal(1, rebuilder.BeforeLoadCount);
        }

        // ---- 回滚路径 ---------------------------------------------------------------

        [Fact]
        public void Load_LaterSectionThrowsOnDocData_RollbackRecallsOnSectionLoadedForEachRolledBackSection()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue>
            {
                ["custom.a"] = Text("doc-a"),
                ["custom.b"] = Text("doc-b"),
                ["custom.c"] = Text("doc-c"),
            });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var sut = CreateSut(fs);
            var a = new LoggingSection("custom.a", Text("pre-a"), log);
            // b 只在收到文档值时抛；回滚用读档前快照 Load 时不抛，因此回滚对 b 也会成功并回调。
            var b = new LoggingSection("custom.b", Text("pre-b"), log)
            {
                ThrowWhen = v => v is JsonString s && s.Value == "doc-b",
            };
            var c = new LoggingSection("custom.c", Text("pre-c"), log);
            sut.RegisterPersistable(a);
            sut.RegisterPersistable(b);
            sut.RegisterPersistable(c);
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);
            Assert.Equal(1, rebuilder.BeforeLoadCount);

            // 正向：a 成功 -> 回调；b 抛异常（无回调）；c 从未被读到（无 load、无回调）。
            // 回滚（正向顺序，含失败段自身）：a、b 各 Load(快照) 后各回调一次。
            var expected = new[]
            {
                "before",
                "load:custom.a", "rebuilt:custom.a",
                "load:custom.b",
                "load:custom.a", "rebuilt:custom.a",
                "load:custom.b", "rebuilt:custom.b",
            };
            Assert.Equal(expected, log);

            // 调用次数：a 两次（正向 + 回滚）、b 一次（仅回滚）、c 零次。
            Assert.Equal(2, rebuilder.SectionCalls.Count(k => k == "custom.a"));
            Assert.Equal(1, rebuilder.SectionCalls.Count(k => k == "custom.b"));
            Assert.Equal(0, rebuilder.SectionCalls.Count(k => k == "custom.c"));

            // 字段类状态回到读档前。
            Assert.Equal("pre-a", ((JsonString)a.Value).Value);
            Assert.Equal("pre-b", ((JsonString)b.Value).Value);
            Assert.Equal("pre-c", ((JsonString)c.Value).Value);
        }

        [Fact]
        public void Load_FailingSectionAlsoFailsOnRollback_NoOnSectionLoadedForIt_OtherSectionsStillRolledBack()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue>
            {
                ["custom.a"] = Text("doc-a"),
                ["custom.b"] = Text("doc-b"),
            });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var diagnostics = new InMemorySaveDiagnostics();
            var sut = CreateSut(fs, diagnostics: diagnostics);
            var a = new LoggingSection("custom.a", Text("pre-a"), log);
            var b = new LoggingSection("custom.b", Text("pre-b"), log) { ThrowWhen = _ => true };
            sut.RegisterPersistable(a);
            sut.RegisterPersistable(b);
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);
            // b 的 Load 无论正向还是回滚都抛：从不回调；a 正向一次 + 回滚一次。
            Assert.Equal(new[] { "custom.a", "custom.a" }, rebuilder.SectionCalls);
            Assert.Equal("pre-a", ((JsonString)a.Value).Value);
            // 失败一次（正向）+ 回滚再失败一次（尽力而为）各记一条错误诊断。
            Assert.Equal(2, diagnostics.Errors.Count);
        }

        [Fact]
        public void Load_FailureBeforePlayerVitals_RollbackAlsoRollsBackAndRecallsVitals()
        {
            // CORE-110-01 子场景 B：player.vitals 在 KnownOrder 里排在 player.equipment 之后，失败发生在它被读到之前，
            // 回滚仍会把它（已注册且有读档前快照）追加进回滚列表并回调。
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue>
            {
                [SaveSections.PlayerProgression] = Text("doc-progression"),
                [SaveSections.PlayerEquipment] = Text("doc-equipment"),
                [SaveSections.PlayerVitals] = Text("doc-vitals"),
            });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var sut = CreateSut(fs);
            var progression = new LoggingSection(SaveSections.PlayerProgression, Text("pre-progression"), log);
            var equipment = new LoggingSection(SaveSections.PlayerEquipment, Text("pre-equipment"), log)
            {
                ThrowWhen = v => v is JsonString s && s.Value == "doc-equipment",
            };
            var vitals = new LoggingSection(SaveSections.PlayerVitals, Text("pre-vitals"), log);
            sut.RegisterPersistable(progression);
            sut.RegisterPersistable(equipment);
            sut.RegisterPersistable(vitals);
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);

            var expected = new[]
            {
                "before",
                "load:" + SaveSections.PlayerProgression, "rebuilt:" + SaveSections.PlayerProgression,
                "load:" + SaveSections.PlayerEquipment,
                // 回滚：已成功的 progression、失败段 equipment、追加的 vitals，按正向顺序。
                "load:" + SaveSections.PlayerProgression, "rebuilt:" + SaveSections.PlayerProgression,
                "load:" + SaveSections.PlayerEquipment, "rebuilt:" + SaveSections.PlayerEquipment,
                "load:" + SaveSections.PlayerVitals, "rebuilt:" + SaveSections.PlayerVitals,
            };
            Assert.Equal(expected, log);
            Assert.Equal("pre-vitals", ((JsonString)vitals.Value).Value);
        }

        [Fact]
        public void Load_FirstSectionThrows_StillCallsBeforeLoadOnce_AndNothingElseForSectionsNeverLoaded()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue>
            {
                ["custom.a"] = Text("doc-a"),
                ["custom.b"] = Text("doc-b"),
            });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var sut = CreateSut(fs);
            sut.RegisterPersistable(new LoggingSection("custom.a", Text("pre-a"), log)
            {
                ThrowWhen = v => v is JsonString s && s.Value == "doc-a",
            });
            sut.RegisterPersistable(new LoggingSection("custom.b", Text("pre-b"), log));
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);
            Assert.Equal(1, rebuilder.BeforeLoadCount);
            // 正向没有任何成功段；回滚只处理失败段 a 自身（回滚成功 -> 回调一次），b 完全没被触碰。
            Assert.Equal(new[] { "custom.a" }, rebuilder.SectionCalls);
            Assert.DoesNotContain("load:custom.b", log);
        }

        // ---- 不触碰段的失败状态：不调用任何回调 --------------------------------------

        private static (LoggingRebuilder Rebuilder, SaveSys Sut, LoggingSection Section, List<string> Log) BuildUntouchableSut(
            StubFileSystem fs, int saveVersion = 1)
        {
            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log);
            var sut = CreateSut(fs, saveVersion);
            var section = new LoggingSection("custom.a", Text("pre-a"), log);
            sut.RegisterPersistable(section);
            sut.SetDerivedStateRebuilder(rebuilder);
            return (rebuilder, sut, section, log);
        }

        [Fact]
        public void Load_NotFound_CallsNoCallback_AndDoesNotTouchSections()
        {
            var fs = new StubFileSystem();
            var (rebuilder, sut, section, log) = BuildUntouchableSut(fs);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.NotFound, result.Status);
            Assert.Equal(0, rebuilder.BeforeLoadCount);
            Assert.Empty(rebuilder.SectionCalls);
            Assert.Empty(log);
            Assert.Equal("pre-a", ((JsonString)section.Value).Value);
        }

        [Fact]
        public void Load_Corrupted_CallsNoCallback_AndDoesNotTouchSections()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic($"user://saves/{Slot.Value}.json", "this is not a save document");
            var (rebuilder, sut, section, log) = BuildUntouchableSut(fs);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.Corrupted, result.Status);
            Assert.Equal(0, rebuilder.BeforeLoadCount);
            Assert.Empty(rebuilder.SectionCalls);
            Assert.Empty(log);
            Assert.Equal("pre-a", ((JsonString)section.Value).Value);
        }

        [Fact]
        public void Load_MigrationFailed_CallsNoCallback_AndDoesNotTouchSections()
        {
            // 文档版本高于运行时版本 -> MigrationFailed。
            const int docVersion = 3;
            const int runtimeVersion = 1;
            var fs = new StubFileSystem();
            SaveDoc(fs, docVersion, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });
            var (rebuilder, sut, section, log) = BuildUntouchableSut(fs, runtimeVersion);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.MigrationFailed, result.Status);
            Assert.Equal(0, rebuilder.BeforeLoadCount);
            Assert.Empty(rebuilder.SectionCalls);
            Assert.Empty(log);
            Assert.Equal("pre-a", ((JsonString)section.Value).Value);
        }

        // ---- 钩子自身抛异常：只记诊断，不改读档结果 ---------------------------------------

        [Fact]
        public void Load_BeforeLoadHookThrows_LoadStillSucceeds_WarnsOnce()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log) { ThrowOnBeforeLoad = true };
            var diagnostics = new InMemorySaveDiagnostics();
            var sut = CreateSut(fs, diagnostics: diagnostics);
            var a = new LoggingSection("custom.a", Text("pre-a"), log);
            sut.RegisterPersistable(a);
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal("doc-a", ((JsonString)a.Value).Value);
            Assert.Equal(1, rebuilder.BeforeLoadCount);
            Assert.Equal(new[] { "custom.a" }, rebuilder.SectionCalls);
            Assert.Single(diagnostics.Warnings, w => w.Contains("BeforeLoad"));
        }

        [Fact]
        public void Load_OnSectionLoadedHookThrows_LoadStillSucceeds_EverySectionStillLoadedAndReported()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue>
            {
                ["custom.a"] = Text("doc-a"),
                ["custom.b"] = Text("doc-b"),
            });

            var log = new List<string>();
            var rebuilder = new LoggingRebuilder(log) { ThrowOnSectionLoaded = true };
            var diagnostics = new InMemorySaveDiagnostics();
            var sut = CreateSut(fs, diagnostics: diagnostics);
            var a = new LoggingSection("custom.a", Text("pre-a"), log);
            var b = new LoggingSection("custom.b", Text("pre-b"), log);
            sut.RegisterPersistable(a);
            sut.RegisterPersistable(b);
            sut.SetDerivedStateRebuilder(rebuilder);

            var result = sut.Load(Slot);

            // 钩子异常不升级成"段 Load 失败"：状态 Loaded、无回滚（每段 load 只一次）。
            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal(new[] { "custom.a", "custom.b" }, rebuilder.SectionCalls);
            Assert.Equal(1, log.Count(e => e == "load:custom.a"));
            Assert.Equal(1, log.Count(e => e == "load:custom.b"));
            Assert.Equal(2, diagnostics.Warnings.Count(w => w.Contains("派生状态重建钩子")));
        }

        // ---- 延迟通知 -----------------------------------------------------------------

        private sealed class EventTap
        {
            public List<SaveLoadedEvent> Loaded { get; } = new List<SaveLoadedEvent>();

            public List<SaveMigratedEvent> Migrated { get; } = new List<SaveMigratedEvent>();

            public List<string> Order { get; } = new List<string>();

            public EventTap(IEventBus bus)
            {
                bus.Subscribe<SaveLoadedEvent>(SaveEventKeys.SaveLoaded, e => { Loaded.Add(e); Order.Add("loaded"); });
                bus.Subscribe<SaveMigratedEvent>(SaveEventKeys.SaveMigrated, e => { Migrated.Add(e); Order.Add("migrated"); });
            }
        }

        [Fact]
        public void Load_NotDeferred_DispatchesSaveLoadedExactlyOnceAutomatically()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });
            var bus = CreateBus();
            var tap = new EventTap(bus);
            var sut = CreateSut(fs, bus: bus);
            sut.RegisterPersistable(new LoggingSection("custom.a", Text("pre-a"), new List<string>()));

            Assert.Equal(LoadStatus.Loaded, sut.Load(Slot).Status);
            Assert.Equal(LoadStatus.Loaded, sut.Load(Slot, deferLoadedNotification: false).Status);

            // 两次各自一次，合计两次（每次 Load 恰好一次，没有重复派发）。
            Assert.Equal(2, tap.Loaded.Count);
            Assert.All(tap.Loaded, e => Assert.Equal(Slot, e.SlotId));
            Assert.Empty(tap.Migrated);
        }

        [Fact]
        public void Load_Deferred_SectionsLoadedButNoEvent_UntilNotifyLoaded_ThenExactlyOnce()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });
            var bus = CreateBus();
            var tap = new EventTap(bus);
            var sut = CreateSut(fs, bus: bus);
            var a = new LoggingSection("custom.a", Text("pre-a"), new List<string>());
            sut.RegisterPersistable(a);

            var result = sut.Load(Slot, deferLoadedNotification: true);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal("doc-a", ((JsonString)a.Value).Value); // 段已经加载完成。
            Assert.Empty(tap.Loaded);                            // 但通知尚未派发。
            Assert.Empty(tap.Migrated);

            sut.NotifyLoaded(Slot, result.MigratedFromVersion);

            var loaded = Assert.Single(tap.Loaded);
            Assert.Equal(Slot, loaded.SlotId);
            Assert.Empty(tap.Migrated); // 未迁移：不发 save.migrated。
        }

        [Fact]
        public void Load_DeferredAfterMigration_NotifyLoadedDispatchesMigratedThenLoaded()
        {
            const int docVersion = 1;
            const int runtimeVersion = 2;

            var fs = new StubFileSystem();
            SaveDoc(fs, docVersion, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });
            var bus = CreateBus();
            var tap = new EventTap(bus);
            var sut = CreateSut(fs, runtimeVersion, bus);
            sut.RegisterMigration(new BumpVersionMigration(docVersion, runtimeVersion));
            sut.RegisterPersistable(new LoggingSection("custom.a", Text("pre-a"), new List<string>()));

            var result = sut.Load(Slot, deferLoadedNotification: true);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal((int?)docVersion, result.MigratedFromVersion);
            Assert.Empty(tap.Order);

            sut.NotifyLoaded(Slot, result.MigratedFromVersion);

            Assert.Equal(new[] { "migrated", "loaded" }, tap.Order);
            var migrated = Assert.Single(tap.Migrated);
            Assert.Equal(docVersion, migrated.FromVersion);
            Assert.Equal(runtimeVersion, migrated.ToVersion);
            Assert.Equal(Slot, migrated.SlotId);
        }

        [Fact]
        public void NotifyLoaded_WithoutPriorLoad_DispatchesLoadedEventAsGiven_EachCallOnce()
        {
            var bus = CreateBus();
            var tap = new EventTap(bus);
            var sut = CreateSut(new StubFileSystem(), bus: bus);

            sut.NotifyLoaded(Slot, null);
            Assert.Single(tap.Loaded);

            sut.NotifyLoaded(Slot, null);
            Assert.Equal(2, tap.Loaded.Count); // 不做"只发一次"的去重：每次调用各发一次，是调用方的责任。
            Assert.Empty(tap.Migrated);
        }

        [Fact]
        public void NotifyLoaded_WithoutBus_DoesNotThrow()
        {
            var sut = CreateSut(new StubFileSystem());

            sut.NotifyLoaded(Slot, 1);
            sut.NotifyLoaded(Slot, null);
        }

        [Fact]
        public void Load_Failure_NeverDispatchesLoaded_DeferredOrNot()
        {
            var fs = new StubFileSystem();
            SaveDoc(fs, 1, new Dictionary<string, JsonValue> { ["custom.a"] = Text("doc-a") });
            var bus = CreateBus();
            var tap = new EventTap(bus);
            var sut = CreateSut(fs, bus: bus);
            sut.RegisterPersistable(new LoggingSection("custom.a", Text("pre-a"), new List<string>()) { ThrowWhen = _ => true });

            Assert.Equal(LoadStatus.PersistableThrew, sut.Load(Slot).Status);
            Assert.Equal(LoadStatus.PersistableThrew, sut.Load(Slot, deferLoadedNotification: true).Status);

            Assert.Empty(tap.Loaded);
            Assert.Empty(tap.Migrated);
        }

        private sealed class BumpVersionMigration : ISaveMigration
        {
            public int FromVersion { get; }

            public int ToVersion { get; }

            public bool Irreversible => false;

            public BumpVersionMigration(int from, int to)
            {
                FromVersion = from;
                ToVersion = to;
            }

            public JsonObject Migrate(JsonObject document)
            {
                return new JsonObjectBuilder()
                    .Add("save_version", new JsonNumber(ToVersion))
                    .Add("sections", document["sections"])
                    .Build();
            }
        }

        // ---- 11 第 6 节勘误①：恢复完成同一时刻派生状态一致 -------------------------------

        /// <summary>
        /// 一个最小"派生状态"模型：权威字段 <c>level</c> 由 <c>custom.level</c> 段持有，派生值
        /// <see cref="Derived"/> 是 <c>level</c> 的函数（<see cref="Compute"/>），不是任何一个段自己的字段，
        /// 只能靠 <see cref="IDerivedStateRebuilder.OnSectionLoaded"/> 重算（形状同评级换算属性/资源池上限）。
        /// </summary>
        private sealed class DerivedModel : IPersistable, IDerivedStateRebuilder
        {
            public const string LevelKey = "custom.level";

            private const double BaseValue = 10;

            private const double PerLevel = 2.5;

            public long Level { get; private set; }

            public double Derived { get; private set; }

            public string SectionKey => LevelKey;

            public DerivedModel(long level)
            {
                Level = level;
                Derived = Compute(level);
            }

            public static double Compute(long level) => BaseValue + PerLevel * level;

            public JsonValue Save() => JsonNumber.FromInt64(Level);

            public void Load(JsonValue data)
            {
                if (!(data is JsonNumber n) || !n.TryGetInt64(out var level))
                {
                    throw new FormatException("level 形状非法");
                }

                Level = level; // 只改权威字段，故意不同步重算派生值。
            }

            public void BeforeLoad()
            {
            }

            public void OnSectionLoaded(string sectionKey)
            {
                if (sectionKey == LevelKey)
                {
                    Derived = Compute(Level);
                }
            }
        }

        private const long SavedLevel = 7;

        private const long PreLoadLevel = 2;

        private static StubFileSystem SaveLevelDoc()
        {
            var fs = new StubFileSystem();
            var saver = CreateSut(fs);
            saver.RegisterPersistable(new DerivedModel(SavedLevel));
            Assert.True(saver.Save(new SaveRequest(Slot, "t")).Success);
            return fs;
        }

        [Fact]
        public void Load_DerivedStateIsConsistentWithSavedData_AtTheMomentSaveLoadedIsDispatched()
        {
            var fs = SaveLevelDoc();
            var bus = CreateBus();
            var model = new DerivedModel(PreLoadLevel);
            var sut = CreateSut(fs, bus: bus);
            sut.RegisterPersistable(model);
            sut.SetDerivedStateRebuilder(model);

            // 前提：读档前派生值对应旧等级，且与存档等级对应的派生值不同——否则断言不区分"重算过/没重算"。
            Assert.NotEqual(DerivedModel.Compute(SavedLevel), model.Derived);

            var derivedSeenAtLoaded = new List<double>();
            var levelSeenAtLoaded = new List<long>();
            bus.Subscribe<SaveLoadedEvent>(SaveEventKeys.SaveLoaded, _ =>
            {
                derivedSeenAtLoaded.Add(model.Derived);
                levelSeenAtLoaded.Add(model.Level);
            });

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal(new[] { SavedLevel }, levelSeenAtLoaded);
            Assert.Equal(new[] { DerivedModel.Compute(SavedLevel) }, derivedSeenAtLoaded);
        }

        [Fact]
        public void Load_Deferred_DerivedStateAlreadyConsistentWhenLoadReturns_AndStillAtNotify()
        {
            var fs = SaveLevelDoc();
            var bus = CreateBus();
            var model = new DerivedModel(PreLoadLevel);
            var sut = CreateSut(fs, bus: bus);
            sut.RegisterPersistable(model);
            sut.SetDerivedStateRebuilder(model);

            var derivedAtLoaded = new List<double>();
            bus.Subscribe<SaveLoadedEvent>(SaveEventKeys.SaveLoaded, _ => derivedAtLoaded.Add(model.Derived));

            var result = sut.Load(Slot, deferLoadedNotification: true);

            // Load 返回（世界达到最终态）时派生值已一致，此时通知尚未发出。
            Assert.Equal(DerivedModel.Compute(SavedLevel), model.Derived);
            Assert.Empty(derivedAtLoaded);

            sut.NotifyLoaded(Slot, result.MigratedFromVersion);

            Assert.Equal(new[] { DerivedModel.Compute(SavedLevel) }, derivedAtLoaded);
        }

        [Fact]
        public void Load_WithoutRebuilder_DerivedStateStaysStale_ProvingTheHookIsWhatKeepsItConsistent()
        {
            // 对照组：同一模型不注入 rebuilder -> 派生值停留在读档前的旧值。说明上面的"一致"确实来自钩子。
            var fs = SaveLevelDoc();
            var model = new DerivedModel(PreLoadLevel);
            var sut = CreateSut(fs);
            sut.RegisterPersistable(model);

            Assert.Equal(LoadStatus.Loaded, sut.Load(Slot).Status);

            Assert.Equal(SavedLevel, model.Level);
            Assert.Equal(DerivedModel.Compute(PreLoadLevel), model.Derived);
        }

        [Fact]
        public void Load_FailedAfterLevelSection_RollbackRederivesFromPreLoadData_NotLeftAtSavedValue()
        {
            // 存档里还有一个会失败的后续段（custom.zz 按 ordinal 排在 custom.level 之后）：
            // 正向阶段 level 已载入存档值并重算；失败后回滚必须把权威字段和派生值都恢复到读档前。
            var fs = new StubFileSystem();
            var saver = CreateSut(fs);
            saver.RegisterPersistable(new DerivedModel(SavedLevel));
            saver.RegisterPersistable(new LoggingSection("custom.zz", Text("doc-zz"), new List<string>()));
            Assert.True(saver.Save(new SaveRequest(Slot, "t")).Success);

            var bus = CreateBus();
            var tap = new EventTap(bus);
            var model = new DerivedModel(PreLoadLevel);
            var sut = CreateSut(fs, bus: bus);
            sut.RegisterPersistable(model);
            sut.RegisterPersistable(new LoggingSection("custom.zz", Text("pre-zz"), new List<string>())
            {
                ThrowWhen = v => v is JsonString s && s.Value == "doc-zz",
            });
            sut.SetDerivedStateRebuilder(model);

            var result = sut.Load(Slot);

            Assert.Equal(LoadStatus.PersistableThrew, result.Status);
            Assert.Equal(PreLoadLevel, model.Level);
            Assert.Equal(DerivedModel.Compute(PreLoadLevel), model.Derived);
            Assert.Empty(tap.Loaded);
        }
    }
}

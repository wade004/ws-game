using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Foundation.SaveSystem
{
    /// <summary>
    /// 测试覆盖梳理 T-H4（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：<see cref="SettingsStore"/>
    /// 此前整个类只有 <c>SaveSystemTests</c> 里两例（无文件 / 存取往返）。本文件补：迁移链多跳与各种中断、
    /// <c>settings_version</c> 缺失/超前/形状非法、文件损坏与根非对象退化为空、<c>RegisterMigration</c> 三类
    /// 非法登记、<c>Save</c> 写失败返回 false 且不破坏旧内容、<see cref="SettingsStoreOptions"/> 每个选项一例。
    /// </summary>
    public sealed class SettingsStoreTests
    {
        private const string DefaultPath = "user://settings.json";

        /// <summary>迁移：把信封版本改为 <see cref="ToVersion"/>，并在 settings 里加一个 <c>hop_&lt;from&gt;</c>
        /// 标记键，供断言"走过哪些跳"。可配置为抛异常。</summary>
        private sealed class HopMigration : ISaveMigration
        {
            public int FromVersion { get; }

            public int ToVersion { get; }

            public bool Irreversible => false;

            public bool Throws { get; set; }

            public int Calls { get; private set; }

            public HopMigration(int from, int to)
            {
                FromVersion = from;
                ToVersion = to;
            }

            public static string HopKey(int from) => "hop_" + from;

            public JsonObject Migrate(JsonObject document)
            {
                Calls++;
                if (Throws)
                {
                    throw new InvalidOperationException("boom-migrate:" + FromVersion);
                }

                var settings = new JsonObjectBuilder();
                if (document.TryGetValue("settings", out var existing) && existing is JsonObject existingObj)
                {
                    foreach (var pair in existingObj)
                    {
                        settings.Add(pair.Key, pair.Value);
                    }
                }

                settings.Add(HopKey(FromVersion), JsonBool.True);

                return new JsonObjectBuilder()
                    .Add("settings_version", new JsonNumber(ToVersion))
                    .Add("settings", settings.Build())
                    .Build();
            }
        }

        private static string Envelope(object? version, string settingsJson = "{ \"a\": 1 }")
        {
            var versionPart = version == null ? "" : "\"settings_version\": " + version + ", ";
            return "{ " + versionPart + "\"settings\": " + settingsJson + " }";
        }

        private static StubFileSystem FsWith(string text)
        {
            var fs = new StubFileSystem();
            Assert.True(fs.WriteTextAtomic(DefaultPath, text));
            return fs;
        }

        private static bool HasHop(JsonObject settings, int from) => settings.TryGetValue(HopMigration.HopKey(from), out _);

        // ---- 迁移链 ---------------------------------------------------------------

        [Fact]
        public void Load_MultiHopChain_AppliesEveryHopInOrder_ReturnsMigratedSettings()
        {
            const int fileVersion = 1;
            const int currentVersion = 4;
            var fs = FsWith(Envelope(fileVersion));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = currentVersion });
            var hops = Enumerable.Range(fileVersion, currentVersion - fileVersion).Select(v => new HopMigration(v, v + 1)).ToList();
            // 乱序登记：按 FromVersion 取用，不依赖登记顺序。
            foreach (var hop in hops.AsEnumerable().Reverse())
            {
                store.RegisterMigration(hop);
            }

            var loaded = store.Load();

            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value); // 原有内容保留。
            for (var v = fileVersion; v < currentVersion; v++)
            {
                Assert.True(HasHop(loaded, v), $"缺少 hop_{v}");
            }

            Assert.All(hops, h => Assert.Equal(1, h.Calls));
        }

        [Fact]
        public void Load_MigrationThatSkipsVersions_FollowsToVersionNotPlusOne()
        {
            var fs = FsWith(Envelope(1));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 5 });
            var first = new HopMigration(1, 3);
            var second = new HopMigration(3, 5);
            var unused = new HopMigration(2, 3);
            store.RegisterMigration(first);
            store.RegisterMigration(unused);
            store.RegisterMigration(second);

            var loaded = store.Load();

            Assert.True(HasHop(loaded, 1));
            Assert.True(HasHop(loaded, 3));
            Assert.False(HasHop(loaded, 2));
            Assert.Equal(0, unused.Calls);
        }

        [Fact]
        public void Load_IncompleteChain_ReturnsSettingsMigratedUpToTheGap_WithoutThrowing()
        {
            var fs = FsWith(Envelope(1));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 4 });
            store.RegisterMigration(new HopMigration(1, 2));
            // 缺 2 -> 3；3 -> 4 存在但够不着。
            var beyondGap = new HopMigration(3, 4);
            store.RegisterMigration(beyondGap);

            var loaded = store.Load();

            Assert.True(HasHop(loaded, 1));
            Assert.False(HasHop(loaded, 3));
            Assert.Equal(0, beyondGap.Calls);
            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value);
        }

        [Fact]
        public void Load_NoMigrationsRegistered_OldVersionFile_ReturnsSettingsUnchanged()
        {
            var fs = FsWith(Envelope(1));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 3 });

            var loaded = store.Load();

            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value);
            Assert.Single(loaded);
        }

        [Fact]
        public void Load_MigrationThrowsMidChain_KeepsResultOfHopsBeforeIt()
        {
            var fs = FsWith(Envelope(1));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 4 });
            store.RegisterMigration(new HopMigration(1, 2));
            store.RegisterMigration(new HopMigration(2, 3) { Throws = true });
            var afterThrow = new HopMigration(3, 4);
            store.RegisterMigration(afterThrow);

            var loaded = store.Load();

            Assert.True(HasHop(loaded, 1));
            Assert.False(HasHop(loaded, 2));
            Assert.Equal(0, afterThrow.Calls);
            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value);
        }

        [Fact]
        public void Load_FileAtCurrentVersion_RunsNoMigration()
        {
            const int current = 3;
            var fs = FsWith(Envelope(current));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = current });
            var migration = new HopMigration(current - 1, current);
            store.RegisterMigration(migration);

            var loaded = store.Load();

            Assert.Equal(0, migration.Calls);
            Assert.Single(loaded);
        }

        [Fact]
        public void Load_FileVersionAheadOfRuntime_ReturnsSettingsAsIs_RunsNoMigration()
        {
            const int current = 2;
            var fs = FsWith(Envelope(current + 5));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = current });
            var migration = new HopMigration(1, 2);
            store.RegisterMigration(migration);

            var loaded = store.Load();

            Assert.Equal(0, migration.Calls);
            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value);
        }

        [Fact]
        public void Load_SettingsVersionMissing_ReturnsSettingsAsIs_RunsNoMigration()
        {
            var fs = FsWith(Envelope(null));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 3 });
            var migration = new HopMigration(1, 2);
            store.RegisterMigration(migration);

            var loaded = store.Load();

            Assert.Equal(0, migration.Calls);
            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value);
        }

        [Theory]
        [InlineData("\"1\"")]   // 版本是字符串
        [InlineData("1.5")]     // 版本不是整数
        [InlineData("null")]
        [InlineData("true")]
        public void Load_SettingsVersionWrongShape_TreatedLikeMissing_NoMigration(string versionJson)
        {
            var fs = FsWith(Envelope(versionJson));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 3 });
            var migration = new HopMigration(1, 2);
            store.RegisterMigration(migration);

            var loaded = store.Load();

            Assert.Equal(0, migration.Calls);
            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value);
        }

        // ---- 损坏 / 形状非法 ---------------------------------------------------------

        [Theory]
        [InlineData("{ not json")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{ \"settings_version\": 1, \"settings\": { \"a\": ")] // 截断
        public void Load_CorruptedText_DegradesToEmptyObject_NoThrow(string text)
        {
            var store = new SettingsStore(FsWith(text));

            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Empty(loaded);
        }

        [Theory]
        [InlineData("[1, 2, 3]")]
        [InlineData("42")]
        [InlineData("\"text\"")]
        [InlineData("null")]
        [InlineData("true")]
        public void Load_RootIsNotAnObject_DegradesToEmptyObject(string text)
        {
            var store = new SettingsStore(FsWith(text));

            var loaded = store.Load();

            Assert.Empty(loaded);
        }

        [Theory]
        [InlineData("{ \"settings_version\": 1 }")]                                // 无 settings
        [InlineData("{ \"settings_version\": 1, \"settings\": [1, 2] }")]          // settings 是数组
        [InlineData("{ \"settings_version\": 1, \"settings\": \"oops\" }")]        // settings 是字符串
        [InlineData("{ \"settings_version\": 1, \"settings\": null }")]            // settings 是 null
        [InlineData("{}")]
        public void Load_EnvelopeWithoutObjectSettings_DegradesToEmptyObject(string text)
        {
            var store = new SettingsStore(FsWith(text));

            Assert.Empty(store.Load());
        }

        [Fact]
        public void Load_CorruptedFile_ThenSave_OverwritesWithValidEnvelope()
        {
            var fs = FsWith("{ not json");
            var store = new SettingsStore(fs);
            Assert.Empty(store.Load());

            var data = new JsonObjectBuilder().Add("k", new JsonString("v")).Build();
            Assert.True(store.Save(data));

            Assert.Equal("v", ((JsonString)store.Load()["k"]).Value);
        }

        // ---- RegisterMigration 非法登记 ------------------------------------------------

        [Fact]
        public void RegisterMigration_Null_Throws()
        {
            var store = new SettingsStore(new StubFileSystem());

            Assert.Throws<ArgumentNullException>(() => store.RegisterMigration(null!));
        }

        [Theory]
        [InlineData(2, 2)] // FromVersion == ToVersion
        [InlineData(3, 2)] // FromVersion >  ToVersion
        public void RegisterMigration_FromNotLessThanTo_Throws(int from, int to)
        {
            var store = new SettingsStore(new StubFileSystem());

            var ex = Assert.Throws<ArgumentException>(() => store.RegisterMigration(new HopMigration(from, to)));

            Assert.Contains(from.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message);
        }

        [Fact]
        public void RegisterMigration_DuplicateFromVersion_Throws_DifferentFromVersionAccepted()
        {
            var store = new SettingsStore(new StubFileSystem());
            store.RegisterMigration(new HopMigration(1, 2));

            Assert.Throws<InvalidOperationException>(() => store.RegisterMigration(new HopMigration(1, 3)));

            store.RegisterMigration(new HopMigration(2, 3)); // 不同 FromVersion：正常。
        }

        [Fact]
        public void RegisterMigration_RejectedRegistration_DoesNotReplaceTheEarlierOne()
        {
            var fs = FsWith(Envelope(1));
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 2 });
            var first = new HopMigration(1, 2);
            store.RegisterMigration(first);
            Assert.Throws<InvalidOperationException>(() => store.RegisterMigration(new HopMigration(1, 2) { Throws = true }));

            var loaded = store.Load();

            Assert.Equal(1, first.Calls);
            Assert.True(HasHop(loaded, 1));
        }

        // ---- Save ------------------------------------------------------------------

        [Fact]
        public void Save_Null_Throws()
        {
            var store = new SettingsStore(new StubFileSystem());

            Assert.Throws<ArgumentNullException>(() => store.Save(null!));
        }

        [Fact]
        public void Save_WriteFails_ReturnsFalse_AndNoFileIsCreated()
        {
            var fs = new StubFileSystem();
            var store = new SettingsStore(fs);
            fs.FailNextWrite();

            var ok = store.Save(new JsonObjectBuilder().Add("k", new JsonNumber(1)).Build());

            Assert.False(ok);
            Assert.False(fs.Exists(DefaultPath));
            Assert.Empty(store.Load());
        }

        [Fact]
        public void Save_WriteFails_LeavesPreviouslySavedSettingsIntact_AndNextSaveSucceeds()
        {
            var fs = new StubFileSystem();
            var store = new SettingsStore(fs);
            Assert.True(store.Save(new JsonObjectBuilder().Add("volume", new JsonNumber(0.25)).Build()));

            fs.FailNextWrite();
            Assert.False(store.Save(new JsonObjectBuilder().Add("volume", new JsonNumber(0.75)).Build()));
            Assert.Equal(0.25, ((JsonNumber)store.Load()["volume"]).Value);

            // FailNextWrite 只影响一次：下一次保存正常。
            Assert.True(store.Save(new JsonObjectBuilder().Add("volume", new JsonNumber(0.75)).Build()));
            Assert.Equal(0.75, ((JsonNumber)store.Load()["volume"]).Value);
        }

        /// <summary>始终写失败、目录不带尾斜杠的文件系统桩：验证 Save 不吞失败，以及路径拼接的"无尾斜杠"分支。</summary>
        private sealed class AlwaysFailingFileSystem : IFileSystem
        {
            public string UserDir { get; set; } = "user:/data";

            public List<string> WrittenPaths { get; } = new List<string>();

            public List<string> ReadPaths { get; } = new List<string>();

            public string GetUserDataDir() => UserDir;

            public string GetContentRootDir() => "content://";

            public string? ReadText(string path)
            {
                ReadPaths.Add(path);
                return null;
            }

            public bool WriteTextAtomic(string path, string content)
            {
                WrittenPaths.Add(path);
                return false;
            }

            public bool Exists(string path) => false;

            public IReadOnlyList<string> ListFiles(string dirPath) => Array.Empty<string>();

            public bool DeleteFile(string path) => false;
        }

        [Fact]
        public void Save_FileSystemAlwaysFails_ReturnsFalseEveryTime_TargetsConfiguredPath()
        {
            var fs = new AlwaysFailingFileSystem();
            var store = new SettingsStore(fs);
            var data = new JsonObjectBuilder().Build();

            Assert.False(store.Save(data));
            Assert.False(store.Save(data));

            Assert.Equal(new[] { "user:/data/settings.json", "user:/data/settings.json" }, fs.WrittenPaths);
        }

        [Fact]
        public void Path_UserDirWithoutTrailingSlash_InsertsSeparator_WithTrailingSlashDoesNot()
        {
            var noSlash = new AlwaysFailingFileSystem { UserDir = "user:/data" };
            new SettingsStore(noSlash).Load();
            Assert.Equal(new[] { "user:/data/settings.json" }, noSlash.ReadPaths);

            var slash = new AlwaysFailingFileSystem { UserDir = "user:/data/" };
            new SettingsStore(slash).Load();
            Assert.Equal(new[] { "user:/data/settings.json" }, slash.ReadPaths);
        }

        [Fact]
        public void Save_WritesEnvelopeWithCurrentVersionAndSettings()
        {
            const int current = 7;
            var fs = new StubFileSystem();
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = current });

            Assert.True(store.Save(new JsonObjectBuilder().Add("locale", new JsonString("zh-CN")).Build()));

            var raw = (JsonObject)JsonReader.Parse(fs.ReadText(DefaultPath)!);
            Assert.Equal(current, ((JsonNumber)raw["settings_version"]).Value);
            Assert.Equal("zh-CN", ((JsonString)((JsonObject)raw["settings"])["locale"]).Value);
            Assert.Equal(2, raw.Count); // 信封只有这两个字段。
        }

        [Fact]
        public void SaveThenLoad_WithNewerRuntime_MigratesOldSavedFile()
        {
            var fs = new StubFileSystem();
            var oldStore = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 1 });
            Assert.True(oldStore.Save(new JsonObjectBuilder().Add("a", new JsonNumber(1)).Build()));

            var newStore = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 2 });
            newStore.RegisterMigration(new HopMigration(1, 2));

            var loaded = newStore.Load();

            Assert.True(HasHop(loaded, 1));
            Assert.Equal(1, ((JsonNumber)loaded["a"]).Value);
        }

        // ---- 构造与 SettingsStoreOptions ------------------------------------------------

        [Fact]
        public void Constructor_NullFileSystem_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SettingsStore(null!));
        }

        [Fact]
        public void Options_Defaults_AreVersionOneAndSettingsJson()
        {
            var options = new SettingsStoreOptions();

            Assert.Equal(1, options.CurrentVersion);
            Assert.Equal("settings.json", options.FileName);
        }

        [Fact]
        public void Options_NullOptions_FallBackToDefaults()
        {
            var fs = new StubFileSystem();
            var store = new SettingsStore(fs, null);

            Assert.Equal(new SettingsStoreOptions().CurrentVersion, store.SettingsVersion);
            Assert.True(store.Save(new JsonObjectBuilder().Build()));
            Assert.True(fs.Exists(DefaultPath));
        }

        [Fact]
        public void Options_CurrentVersion_IsReportedAndWrittenIntoEnvelope()
        {
            const int current = 12;
            var fs = new StubFileSystem();
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = current });

            Assert.Equal(current, store.SettingsVersion);
            Assert.True(store.Save(new JsonObjectBuilder().Build()));

            var raw = (JsonObject)JsonReader.Parse(fs.ReadText(DefaultPath)!);
            Assert.Equal(current, ((JsonNumber)raw["settings_version"]).Value);
        }

        [Fact]
        public void Options_FileName_DecidesTheFileUsedForSaveAndLoad_AndIsolatesStores()
        {
            const string customName = "profile_settings.json";
            var fs = new StubFileSystem();
            var custom = new SettingsStore(fs, new SettingsStoreOptions { FileName = customName });
            var standard = new SettingsStore(fs);

            Assert.True(custom.Save(new JsonObjectBuilder().Add("who", new JsonString("custom")).Build()));

            Assert.True(fs.Exists("user://" + customName));
            Assert.False(fs.Exists(DefaultPath));
            Assert.Equal("custom", ((JsonString)custom.Load()["who"]).Value);
            Assert.Empty(standard.Load()); // 默认文件名的仓库看不到自定义文件。

            Assert.True(standard.Save(new JsonObjectBuilder().Add("who", new JsonString("standard")).Build()));
            Assert.Equal("custom", ((JsonString)custom.Load()["who"]).Value);
            Assert.Equal("standard", ((JsonString)standard.Load()["who"]).Value);
        }

        [Fact]
        public void Options_CurrentVersion_DecidesWhetherAFileCountsAsOldAndNeedsMigration()
        {
            var fs = FsWith(Envelope(2));
            var migration = new HopMigration(2, 3);

            var atVersion2 = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 2 });
            atVersion2.RegisterMigration(migration);
            atVersion2.Load();
            Assert.Equal(0, migration.Calls);

            var atVersion3 = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 3 });
            atVersion3.RegisterMigration(migration);
            atVersion3.Load();
            Assert.Equal(1, migration.Calls);
        }
    }
}

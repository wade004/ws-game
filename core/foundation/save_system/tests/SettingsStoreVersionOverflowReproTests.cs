using Adapters.Stub;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Foundation.SaveSystem
{
    /// 复现：SettingsStore.Load 把 settings_version 用 (int)versionLong 无范围检查截断，
    /// 超出 int 范围的"超前版本"被回绕成小版本号，误触发迁移（SaveSystem.TryGetInt 有范围保护，二者不一致）。
    public sealed class SettingsStoreVersionOverflowReproTests
    {
        private sealed class Hop : ISaveMigration
        {
            public int FromVersion => 2;
            public int ToVersion => 3;
            public bool Irreversible => false;
            public int Calls;
            public JsonObject Migrate(JsonObject document)
            {
                Calls++;
                return document;
            }
        }

        [Fact]
        public void Load_SettingsVersionBeyondIntRange_IsTreatedAsAhead_NotWrappedIntoOldVersion()
        {
            const long wrapsToTwo = (1L << 32) + 2; // (int) 截断后等于 2
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("user://settings.json", "{ \"settings_version\": " + wrapsToTwo + ", \"settings\": { \"a\": 1 } }");
            var store = new SettingsStore(fs, new SettingsStoreOptions { CurrentVersion = 3 });
            var hop = new Hop();
            store.RegisterMigration(hop);

            store.Load();

            Assert.Equal(0, hop.Calls);
        }
    }
}

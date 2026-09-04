using Adapters.Stub;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    public class StubFileSystemTests
    {
        [Fact]
        public void WriteThenRead_RoundTrips()
        {
            var fs = new StubFileSystem();
            var ok = fs.WriteTextAtomic("save/slot1.json", "{\"level\":1}");

            Assert.True(ok);
            Assert.Equal("{\"level\":1}", fs.ReadText("save/slot1.json"));
        }

        [Fact]
        public void ReadText_ReturnsNullForMissingFile()
        {
            var fs = new StubFileSystem();
            Assert.Null(fs.ReadText("save/missing.json"));
        }

        [Fact]
        public void FailNextWrite_KeepsOldContentUnchanged()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("save/slot1.json", "old");

            fs.FailNextWrite();
            var ok = fs.WriteTextAtomic("save/slot1.json", "new");

            Assert.False(ok);
            Assert.Equal("old", fs.ReadText("save/slot1.json"));
        }

        [Fact]
        public void FailNextWrite_OnlyAffectsNextSingleWrite()
        {
            var fs = new StubFileSystem();
            fs.FailNextWrite();

            var firstOk = fs.WriteTextAtomic("a.txt", "1");
            var secondOk = fs.WriteTextAtomic("a.txt", "2");

            Assert.False(firstOk);
            Assert.True(secondOk);
            Assert.Equal("2", fs.ReadText("a.txt"));
        }

        [Fact]
        public void Exists_ReflectsWriteAndDelete()
        {
            var fs = new StubFileSystem();
            Assert.False(fs.Exists("a.txt"));

            fs.WriteTextAtomic("a.txt", "1");
            Assert.True(fs.Exists("a.txt"));

            var deleted = fs.DeleteFile("a.txt");
            Assert.True(deleted);
            Assert.False(fs.Exists("a.txt"));
        }

        [Fact]
        public void DeleteFile_ReturnsFalseForMissingFile()
        {
            var fs = new StubFileSystem();
            Assert.False(fs.DeleteFile("missing.txt"));
        }

        // 语义判断记录（T1-4）：ListFiles 返回相对 dirPath、'/' 分隔、按序数排序的路径列表，
        // 不含 dirPath 本身前缀（见 core/foundation/engine_adapter/README.md"IFileSystem"一节
        // 的实现级约定）。本用例断言随之从"完整 key"改为"相对路径"。
        [Fact]
        public void ListFiles_ReturnsStableSortedResultsRelativeToDir()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("save/slot2.json", "{}");
            fs.WriteTextAtomic("save/slot1.json", "{}");
            fs.WriteTextAtomic("settings/audio.json", "{}");

            var files = fs.ListFiles("save");

            Assert.Equal(new[] { "slot1.json", "slot2.json" }, files);
        }

        [Fact]
        public void ListFiles_IsRecursiveIntoSubdirectories()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("data/_sample/found/found.event_catalog.json", "{}");
            fs.WriteTextAtomic("data/_sample/stat/stat.definition.json", "{}");
            fs.WriteTextAtomic("data/_sample/l10n/l10n.locale.json", "{}");

            var files = fs.ListFiles("data/_sample");

            Assert.Equal(
                new[] { "found/found.event_catalog.json", "l10n/l10n.locale.json", "stat/stat.definition.json" },
                files);
        }

        [Fact]
        public void ListFiles_ExcludesTheDirMarkerFileItself()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("save", "{}");
            fs.WriteTextAtomic("save/slot1.json", "{}");

            var files = fs.ListFiles("save");

            Assert.Equal(new[] { "slot1.json" }, files);
        }

        [Fact]
        public void GetUserDataDir_ReturnsUserScheme()
        {
            var fs = new StubFileSystem();
            Assert.Equal("user://", fs.GetUserDataDir());
        }
    }
}

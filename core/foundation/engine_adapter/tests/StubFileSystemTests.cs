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

        [Fact]
        public void ListFiles_ReturnsStableSortedResultsUnderPrefix()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("save/slot2.json", "{}");
            fs.WriteTextAtomic("save/slot1.json", "{}");
            fs.WriteTextAtomic("settings/audio.json", "{}");

            var files = fs.ListFiles("save");

            Assert.Equal(new[] { "save/slot1.json", "save/slot2.json" }, files);
        }

        [Fact]
        public void GetUserDataDir_ReturnsUserScheme()
        {
            var fs = new StubFileSystem();
            Assert.Equal("user://", fs.GetUserDataDir());
        }
    }
}

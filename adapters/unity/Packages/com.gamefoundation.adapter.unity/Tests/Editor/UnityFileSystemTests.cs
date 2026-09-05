#nullable enable
using System.IO;
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityFileSystemTests
    {
        private UnityFileSystem _fs = null!;
        private string _subDir = null!;

        [SetUp]
        public void SetUp()
        {
            _fs = new UnityFileSystem();
            _subDir = "test_" + System.Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            var full = Path.Combine(Application.persistentDataPath, _subDir);
            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }
        }

        [Test]
        public void WriteTextAtomic_ThenReadText_ReturnsSameContent()
        {
            var path = _subDir + "/save1.json";
            var ok = _fs.WriteTextAtomic(path, "{\"hp\":10}");

            Assert.IsTrue(ok);
            Assert.AreEqual("{\"hp\":10}", _fs.ReadText(path));
        }

        [Test]
        public void WriteTextAtomic_Overwrite_ReplacesContentAtomically()
        {
            var path = _subDir + "/save2.json";
            _fs.WriteTextAtomic(path, "old");
            var ok = _fs.WriteTextAtomic(path, "new");

            Assert.IsTrue(ok);
            Assert.AreEqual("new", _fs.ReadText(path));
        }

        [Test]
        public void ListFiles_ReturnsRelativeSortedPaths()
        {
            _fs.WriteTextAtomic(_subDir + "/b.json", "b");
            _fs.WriteTextAtomic(_subDir + "/a.json", "a");
            _fs.WriteTextAtomic(_subDir + "/nested/c.json", "c");

            var files = _fs.ListFiles(_subDir);

            CollectionAssert.AreEqual(
                new[] { "a.json", "b.json", "nested/c.json" },
                files);
        }

        [Test]
        public void DeleteFile_RemovesFile_ReadTextReturnsNull()
        {
            var path = _subDir + "/to_delete.json";
            _fs.WriteTextAtomic(path, "x");

            var deleted = _fs.DeleteFile(path);

            Assert.IsTrue(deleted);
            Assert.IsNull(_fs.ReadText(path));
        }

        [Test]
        public void ReadText_NonExistentFile_ReturnsNull()
        {
            Assert.IsNull(_fs.ReadText(_subDir + "/does_not_exist.json"));
        }

        [Test]
        public void GetContentRootDir_DiffersFromGetUserDataDir_InUserDataMode()
        {
            // ADR-0016 决策 8：GetContentRootDir 与 GetUserDataDir 是两个独立目录。
            Assert.AreNotEqual(_fs.GetUserDataDir(), _fs.GetContentRootDir());
            StringAssert.Contains("StreamingAssets", _fs.GetContentRootDir());
        }

        [Test]
        public void ReadOnlyContentMode_WriteTextAtomic_ReturnsFalse_DoesNotThrow()
        {
            var contentFs = new UnityFileSystem(readOnlyContentMode: true);

            var ok = contentFs.WriteTextAtomic(_subDir + "/should_not_write.json", "x");

            Assert.IsFalse(ok);
            Assert.IsFalse(contentFs.Exists(_subDir + "/should_not_write.json"));
        }

        [Test]
        public void ReadOnlyContentMode_DeleteFile_ReturnsFalse_DoesNotThrow()
        {
            var contentFs = new UnityFileSystem(readOnlyContentMode: true);

            Assert.IsFalse(contentFs.DeleteFile(_subDir + "/anything.json"));
        }

        [Test]
        public void ReadOnlyContentMode_ResolvesUnderConfiguredContentRoot()
        {
            var contentRoot = Path.Combine(Application.persistentDataPath, _subDir);
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: contentRoot);

            // 直接用普通模式实例把探针文件写进 contentRoot 对应的目录（等价于测试夹具预置好一份
            // 内容），验证只读模式实例确实从这个自定义 contentRoot 读取，而不是
            // Application.persistentDataPath 根目录。
            _fs.WriteTextAtomic(_subDir + "/probe.json", "{\"ok\":true}");

            Assert.AreEqual("{\"ok\":true}", contentFs.ReadText("probe.json"));
            Assert.AreEqual(contentRoot, contentFs.GetContentRootDir());
        }
    }
}

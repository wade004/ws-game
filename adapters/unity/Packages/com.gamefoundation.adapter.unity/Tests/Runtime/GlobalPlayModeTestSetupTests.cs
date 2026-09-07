#nullable enable
// GlobalPlayModeTestSetupTests：回归测试，稳定复现第四方深度审核 followup-2026-09-07b 定位的
// 根因——GlobalPlayModeTestSetup 此前只清理 "game.sample." 前缀的存档槽文件，同一装配
// （Adapter.Unity.Tests.Runtime）里其它前缀（game.template./裸 slot.）新建的槽文件、以及
// FND-01 迁移前遗留在 saves/ 顶层的旧格式 "<slot>.bakN.json" 备份文件都不会被清理，跨越同一台
// 开发机上历次独立 -runTests 调用不断累积，最终在某次完整 165+ 条 PlayMode 套件运行的固定执行点
// 把槽数顶过 SaveSystemOptions.MaxSlots（默认 20），命中 SaveFailureReason.SlotLimitReached——
// 表现为 VerticalSliceTests 的 Paperdoll_LayerOrder_.../Pause_StopsWorldSimTick_.../
// Projectile_CastBoltSkill_.../YSorting_TwoEntitiesWithDifferentY_... 四条用例单独跑必过、混在
// 完整套件里跑必以 "Expected: InWorld, But was: MainMenu" 失败（ShellHost.NewGame 在
// _saveSystem.Save(...).Success 为 false 时直接 return false，根本不会走到
// _sceneRouter.LoadScene）。见 GlobalPlayModeTestSetup.cs 判断记录 2。
//
// 本测试不依赖真实场景/世界装配，只验证 GlobalPlayModeTestSetup.ClearAllSaveArtifacts 这一纯
// 文件系统操作的行为，因此用普通 [Test]（同步方法）而非 [UnityTest]，仍运行在本 PlayMode 程序集
// 里（Adapter.Unity.Tests.Runtime），不新增额外测试装配。
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class GlobalPlayModeTestSetupTests
    {
        private string _tempSavesDir = null!;

        [SetUp]
        public void SetUp()
        {
            _tempSavesDir = Path.Combine(Path.GetTempPath(), "gf_saves_cleanup_test_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempSavesDir);
            Directory.CreateDirectory(Path.Combine(_tempSavesDir, "backups"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempSavesDir))
            {
                Directory.Delete(_tempSavesDir, recursive: true);
            }
        }

        /// <summary>核心回归断言：修复前的实现（只清"game.sample."前缀顶层文件）对着这份布局跑完
        /// 会剩下 game.template./裸 slot. 前缀的顶层文件、backups/ 子目录里的文件、以及顶层的旧格式
        /// .bakN.json 备份——本测试对着修复后的 <see cref="GlobalPlayModeTestSetup.ClearAllSaveArtifacts"/>
        /// 断言这些一个不剩，稳定复现"只清一个前缀不够"这一原始缺陷（在本次修复之前对着旧实现的
        /// 等价清理范围断言会失败）。</summary>
        [Test]
        public void ClearAllSaveArtifacts_RemovesFilesAcrossAllPrefixesAndBackupsSubdir()
        {
            // 顶层：game.sample. 前缀（修复前唯一会被清理的一类）。
            WriteDummy("game.sample.slot_paperdoll.json");
            WriteDummy("game.sample.slot_pausetick.json");

            // 顶层：其它前缀，修复前的实现完全不会碰它们（真正的累积源头）。
            WriteDummy("game.template.slot_smoke.json");
            WriteDummy("game.template.slot_smoke_new.json");
            WriteDummy("slot.audit_blockers_death_reload.json");
            WriteDummy("slot.smoke.json");

            // 顶层：FND-01 迁移前遗留的旧格式备份文件（不匹配 "game.sample." 前缀，也不在新的
            // backups/ 子目录里，新版 CountSlots 会把它当成一个真实槽计入配额）。
            WriteDummy("slot.smoke.bak1.json");
            WriteDummy("game.template.slot_smoke.bak1.json");

            // FND-01 新格式：backups/ 子目录下的备份（CountSlots 本就正确排除在配额之外，但清理
            // 逻辑仍应把它们物理删除，不留过期内容）。
            WriteDummy(Path.Combine("backups", "game.sample.slot_paperdoll.bak1.json"));

            var totalBefore = Directory.GetFiles(_tempSavesDir, "*", SearchOption.AllDirectories).Length;
            Assert.AreEqual(9, totalBefore, "测试前置条件：应当已落盘 9 个文件（覆盖全部前缀 + backups/ 子目录）");

            var removed = GlobalPlayModeTestSetup.ClearAllSaveArtifacts(_tempSavesDir);

            Assert.AreEqual(9, removed, "应当报告删除了全部 9 个文件，不因前缀/所在子目录而遗漏");

            var remaining = Directory.GetFiles(_tempSavesDir, "*", SearchOption.AllDirectories);
            CollectionAssert.IsEmpty(remaining, "清理后 saves/ 目录树下不应再有任何文件残留（含 backups/ 子目录）");
        }

        [Test]
        public void ClearAllSaveArtifacts_DirectoryDoesNotExist_ReturnsZeroWithoutThrowing()
        {
            var missingDir = Path.Combine(_tempSavesDir, "does_not_exist");

            var removed = GlobalPlayModeTestSetup.ClearAllSaveArtifacts(missingDir);

            Assert.AreEqual(0, removed);
            Assert.IsFalse(Directory.Exists(missingDir), "不存在的目录不应被本方法意外创建出来");
        }

        /// <summary>N06 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// 本装置真正清理/重定向的目标必须是真实 <c>Application.persistentDataPath</c> 下的一个
        /// **专属子目录**，绝不能是该目录本身——否则"清空测试存档"与"清空玩家真实存档"是同一个
        /// 操作。本用例只做纯字符串层面的校验（不调用 OneTimeSetUp/OneTimeTearDown 本身——那两个
        /// 方法已经在本次 -runTests 调用里被 NUnit 自动跑过一次、覆盖着当前进程全程有效的
        /// <c>UnityFileSystem.UserDataRootOverride</c>，本测试方法内不应该改动这个全局状态，避免
        /// 影响同一次运行里的其它测试夹具），只验证"目标路径长什么样"这一静态事实。</summary>
        [Test]
        public void TestUserDataRoot_IsSubdirectoryOfRealPersistentDataPath_NotTheDirectoryItself()
        {
            var testRoot = GlobalPlayModeTestSetup.TestUserDataRoot;
            var realRoot = Application.persistentDataPath;

            Assert.AreNotEqual(realRoot, testRoot, "专属测试目录不能与真实存档根目录相同");
            StringAssert.StartsWith(realRoot, testRoot, "专属测试目录必须是真实存档根目录下的子目录，不能是无关路径");
            Assert.AreEqual("_playmode_tests", Path.GetFileName(testRoot));
        }

        /// <summary>N06 核心回归：本次 -runTests 调用已经跑过一次 <c>GlobalPlayModeTestSetup.
        /// OneTimeSetUp</c>（NUnit 装配级钩子，早于本方法运行）——到本测试方法执行的这一刻，
        /// <c>UnityFileSystem.UserDataRootOverride</c> 应该已经被重定向到
        /// <see cref="GlobalPlayModeTestSetup.TestUserDataRoot"/>，而不是保持默认 <c>null</c>（那样
        /// 全部 PlayMode 测试期间新建的 <c>UnityFileSystem</c> 实例都会写到真实存档目录）。</summary>
        [Test]
        public void UserDataRootOverride_AlreadyRedirectedByAssemblyLevelOneTimeSetUp()
        {
            Assert.AreEqual(
                GlobalPlayModeTestSetup.TestUserDataRoot,
                Adapter.Unity.EngineAdapter.UnityFileSystem.UserDataRootOverride,
                "本装配的 [SetUpFixture].OneTimeSetUp 应该已经在任何测试运行前把用户数据根重定向到专属测试目录");
        }

        private void WriteDummy(string relativePath)
        {
            var fullPath = Path.Combine(_tempSavesDir, relativePath);
            File.WriteAllText(fullPath, "{}");
        }
    }
}

#nullable enable
// GlobalPlayModeTestSetup：PlayMode 测试装配级清理（U3 排障新增）。
//
// 判断记录（根因：存档槽会累积在一个真实、跨运行持久化的操作系统目录里，不会随每次 Editor
// 测试运行自动清空）：ISaveSystem.Save 的目标文件系统是 UnityFileSystem.GetUserDataDir()（即
// Application.persistentDataPath），这是宿主机上一个真实存在、长期保留的文件夹，不是每次
// -runTests 都会重置的沙箱；本测试装配下多条用例（ShellFlowTests/VerticalSliceTests/
// UiSuiteTests 等）各自用固定的槽位 id（如 "game.sample.slot_vslice"）反复调用
// NewGame -> ISaveSystem.Save，跨多次 Editor 测试运行不断在磁盘上新增文件；ISaveSystem.Save
// 对"目标槽不存在 + 已存在槽数量达到 SaveSystemOptions.MaxSlots（默认 20）"时返回
// SaveFailureReason.SlotLimitReached（见 core/foundation/save_system/core/SaveSystem.cs
// Save 方法），这是存档系统按设计工作的正常拒绝路径，不是 bug。
//
// 实测复现（本次排障过程）：在 Application.persistentDataPath/saves 下发现恰好 20 个（含历史
// 调试反复尝试留下的 "slot_crit"/"slot_crit_0".."slot_crit_8" 等槽位）非备份 .json 槽文件，
// 命中上限；ShellFlowTests.DeleteSlot_RemovesSlotFromSaveSlotsViewModel（上一次运行的
// DeleteSlot 已经把自己的槽删除，本次运行时它是"新槽"）与
// VerticalSliceTests.Feedback_CritDamage_TriggersFreeze（槽位 "game.sample.slot_critbind"
// 从未被成功创建过，也是"新槽"）各自都在这一步命中上限，Presentation.Shell.ShellHost.NewGame
// 在 _saveSystem.Save(...).Success 为 false 时直接 return false（见该类型源码），根本不会走到
// _sceneRouter.LoadScene——加诊断日志实测证实：整个测试运行里，scene.sample_field/
// nav.sample_field 两个资源的每一次加载都在下一帧内成功，从未观察到一次真实的资源加载失败。
// 上一位 agent"场景资源加载偶发失败、回退 MainMenu"的判断记录是误判：真正卡住的是存档槽配额，
// 与场景资源加载无关（唯一的"读取失败"诊断日志来自 UnityResourceLoaderTests 故意构造的缺失
// 资源用例，与 NewGame 流程无关）。而 VerticalSliceTests.FullVerticalSlice_...
// 用的槽位 "game.sample.slot_vslice" 恰好是历史累积中已经存在的槽（已存在的槽不受 MaxSlots
// 限制，只有"新建槽"才受限），所以它的 NewGame/EnterInWorld 不受本问题影响、能正常进入
// InWorld——该用例的失败是另一个独立问题（示例生物未在给定 tick 数内死亡），与本类型无关。
//
// 判断记录（修法：装配级 OneTimeSetUp 清空示例存档槽，不是放宽 core 的配额策略）：MaxSlots
// 配额本身是存档系统的既定行为（见 core/foundation/save_system/README.md"已存在槽数已达
// MaxSlots"分支），不是本任务允许改动的契约，也不该改——这是为真实游戏設计的合理上限。真正该
// 修的是"测试环境卫生"：PlayMode 测试不应该把状态残留进一个跨越多次独立测试运行、会不断累积
// 的真实操作系统目录里。本类型在整个 Adapter.Unity.Tests.Runtime 装配的所有测试运行之前，清空
// Application.persistentDataPath 下存档目录里所有 "game.sample." 前缀的槽文件（正式槽 +
// 备份 .bakN.json），让每次 -runTests 调用都从同一个已知的空存档起点开始，不再受宿主机历史
// 累积槽位数量的影响，结果可稳定复现（也可稳定通过）。只清"game.sample."前缀，不整个清空
// saves 目录，避免误删同一台宿主机上其它非本示例数据集的存档。
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>NUnit 装配级清理：见文件顶部判断记录。作用范围是本命名空间
    /// （<c>Adapter.Unity.Tests.Runtime</c>）下的全部测试夹具，在其中任何一条测试运行之前
    /// 执行一次。</summary>
    [SetUpFixture]
    public sealed class GlobalPlayModeTestSetup
    {
        private const string SampleSlotPrefix = "game.sample.";

        [OneTimeSetUp]
        public void ClearSampleSaveSlotsBeforeAnyTestRuns()
        {
            var savesDir = Path.Combine(Application.persistentDataPath, "saves");
            if (!Directory.Exists(savesDir))
            {
                return;
            }

            var removed = 0;
            foreach (var file in Directory.GetFiles(savesDir)
                         .Where(f => Path.GetFileName(f).StartsWith(SampleSlotPrefix)))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch
                {
                    // 判断记录：清理失败（例如文件被占用）不阻断测试运行——尽力而为，最坏情况
                    // 退化回清理前的既有行为（可能命中 SlotLimitReached），不应该让整个测试运行
                    // 因为一次尽力而为的清理失败而无法启动。
                }
            }

            Debug.Log($"[GlobalPlayModeTestSetup] 清理示例存档槽：目录={savesDir}，删除文件数={removed}");
        }
    }
}

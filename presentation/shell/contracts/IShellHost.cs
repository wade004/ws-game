using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Presentation.Shell
{
    /// <summary>
    /// 游戏外壳契约（见 01_分层与依赖.md L5 模块表 <c>shell</c> 行"契约接口名：ShellHost"、
    /// 09_表现层.md 第 9 节）：主菜单、存档槽、新游戏与难度、加载画面、设置五个子模块的编排入口。
    /// </summary>
    public interface IShellHost
    {
        /// <summary>当前 Shell 页面（见 <see cref="ShellPage"/>）。</summary>
        ShellPage Page { get; }

        /// <summary>是否正在加载中（<c>scene.load_started</c> 到 <c>scene.load_finished</c>/
        /// 失败回退之间）。</summary>
        bool IsLoading { get; }

        /// <summary>当前加载进度 [0, 1]（透传 <see cref="Core.Foundation.SceneRouter.ISceneRouter.LoadProgress"/>）。</summary>
        double LoadingProgress { get; }

        /// <summary>Boot → MainMenu（见 03_运行时骨架.md 第 2 节状态机表）。</summary>
        bool Start();

        /// <summary>导航到存档槽页面（列表内容经 <see cref="ISaveSystem.ListSlots"/> 或
        /// <c>Presentation.Ui.SaveSlotsViewModel</c> 单独查询，本方法只负责页面切换）。</summary>
        void ShowSlots();

        /// <summary>导航到新游戏难度/职业选择页面。</summary>
        void ShowNewGameSetup();

        /// <summary>导航到设置页面。</summary>
        void OpenSettings();

        /// <summary>发起一局新游戏：应用难度、经 <see cref="NewGameStarter"/> 创建初始玩家状态、
        /// 写入初始存档、加载起始地图。失败（难度未登记、存档写入失败、场景加载被拒绝）返回
        /// false，不改变应用状态。</summary>
        bool NewGame(Id slotId, Id difficultyId, Id? archetypeId);

        /// <summary>读档并加载其记录的当前地图。</summary>
        LoadResult LoadGame(Id slotId);

        /// <summary>用当前游戏状态覆盖写入一个已存在（或新建）的存档槽（见 10 第 6 节"手动存档"）。
        /// 二次确认由 UI 侧负责，本方法不做确认。</summary>
        SaveResult OverwriteSlot(Id slotId, long? playTimeSeconds, System.Collections.Generic.IReadOnlyDictionary<string, string>? displaySummary);

        /// <summary>删除一个存档槽。二次确认由 UI 侧负责。</summary>
        bool DeleteSlot(Id slotId);

        /// <summary>返回主菜单：InWorld/Pause → MainMenu；已在 MainMenu 内部子页面时只重置到
        /// <see cref="ShellPage.MainMenu"/>。</summary>
        bool ReturnToMainMenu();

        /// <summary>请求退出应用（仅 MainMenu 下允许，见 <see cref="Core.Foundation.AppLifecycle.IAppStateHost.RequestExit"/>）。</summary>
        bool Quit();

        /// <summary>读取设置文件并把其中的按键绑定重新应用到 <see cref="Core.Foundation.InputMap.IInputMapHost"/>
        /// （见 10 第 7 节"按键绑定...持久化到设置"）。</summary>
        JsonObject LoadSettings();

        /// <summary>把 <paramref name="additionalFields"/>（音量、语言等设置项）连同当前按键绑定
        /// （<see cref="Core.Foundation.InputMap.IInputMapHost.ExportBindings"/>）一并落盘。</summary>
        bool SaveSettings(JsonObject additionalFields);

        /// <summary>推进场景加载（转发 <see cref="Core.Foundation.SceneRouter.ISceneRouter.Update"/>，
        /// 调用方按帧/按需调用）。</summary>
        void Update();
    }
}

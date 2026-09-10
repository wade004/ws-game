using Core.Foundation.Common;

namespace Presentation.Shell
{
    /// <summary>
    /// Shell 自身的页面导航状态（见 09_表现层.md 第 9 节 Shell 模块清单"主菜单/存档槽/新游戏与
    /// 难度/加载画面/设置"、任务书"状态：MainMenu → SaveSlots → NewGame(难度选择) → Loading →
    /// InWorld"）。<see cref="MainMenu"/>/<see cref="SaveSlots"/>/<see cref="NewGameSetup"/>/
    /// <see cref="Settings"/> 四者只在应用级 <c>AppState.MainMenu</c> 内部切换（见
    /// <see cref="ShellHost.Page"/>"判断记录"），<see cref="Loading"/>/<see cref="InWorld"/>/
    /// <see cref="Paused"/> 直接反映应用级主状态。
    /// </summary>
    public enum ShellPage
    {
        MainMenu,
        SaveSlots,
        NewGameSetup,
        Settings,
        Loading,
        InWorld,
        Paused,
    }

    /// <summary>
    /// 新游戏起始委托（见任务书"经注入的 NewGameStarter(slot, difficulty) 委托由游戏层创建玩家并
    /// ISceneRouter.LoadScene(起始地图)"）。
    /// <para>
    /// 判断记录：任务书原句把"创建玩家"与"调用 LoadScene"都算在委托要做的事里，但字面上没有
    /// 明确"到底是委托自己调 LoadScene，还是把起始地图交回 ShellHost 由后者调用"。本模块选择
    /// 后者——委托只做游戏层真正专属的部分（按 <paramref name="difficultyId"/>/<paramref name="archetypeId"/> 创建初始玩家单位、
    /// 写入各已注册 <c>IPersistable</c> 的初始状态），返回起始地图 id；<see cref="ShellHost.NewGame"/>
    /// 自己调用 <c>ISceneRouter.LoadScene</c>，理由：LoadScene 的调用时机与"新游戏流程还有没有
    /// 后续步骤（写初始存档等）"耦合在一起，让编排者（ShellHost）统一控制调用顺序，比让每个游戏层
    /// 实现各自决定"什么时候调 LoadScene"更不容易出现"忘记先写初始存档就切场景"一类顺序错误。
    /// </para>
    /// </summary>
    public delegate Id NewGameStarter(Id slotId, Id difficultyId, Id? archetypeId);

    /// <summary>
    /// 读档后取得"应加载的地图 id"的委托（见任务书"LoadGame(slotId)（ISaveSystem.Load → 读出
    /// world.current_map_id → LoadScene）"）。
    /// <para>
    /// 契约缺口（判断记录）：10_存档与持久化.md 第 3 节步骤 7 提到"world.current_map_id /
    /// current_position（触发场景路由加载对应地图）"，但 <see cref="Core.Foundation.SaveSystem.ISaveSystem.Load"/>
    /// 的返回值 <see cref="Core.Foundation.SaveSystem.LoadResult"/> 只携带 meta 段（见其字段表），
    /// 不携带 world 段内容；world 段的 <c>current_map_id</c> 由游戏层/L4 自己的
    /// <c>IPersistable</c>（本任务范围之外，未提供具体契约）持有，本模块没有可依赖的只读查询接口
    /// 能直接问到"刚刚读档读到的地图 id 是什么"。<see cref="ShellHost.LoadGame"/> 因此把这一步
    /// 表达为构造期注入的委托，由持有该 world 段 Persistable 引用的组装层提供；建议后续给
    /// world 段的宿主补一个 <c>CurrentMapId</c> 只读属性，届时可以去掉本委托直接查询。
    /// </para>
    /// </summary>
    public delegate Id LoadedMapIdResolver();
}

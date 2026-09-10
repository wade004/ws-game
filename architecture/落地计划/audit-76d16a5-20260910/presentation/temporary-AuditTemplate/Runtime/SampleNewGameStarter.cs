using Core.Carriers.Unit;
using Core.Foundation.Common;

namespace Game.Template
{
    /// <summary>
    /// 示例 <see cref="Presentation.Shell.NewGameStarter"/>（见
    /// <c>presentation/shell/contracts/ShellHostTypes.cs</c> 该委托类型的判断记录："经注入的
    /// NewGameStarter(slot, difficulty) 委托由游戏层创建玩家并返回起始地图 id"）：把玩家重置为
    /// <see cref="GameOptions.StartMapId"/>/<see cref="GameOptions.PlayerTemplateId"/> 描述的新游戏
    /// 起始状态，返回起始地图 id。
    /// <para>
    /// 可替换：这是最小示例实现（只重置位置/地图/外形模板），不做存档槽/难度/职业选择的任何分支
    /// 处理（<paramref name="difficultyId"/> 由 <c>ShellHost.NewGame</c> 在调用本委托之前已经应用
    /// 过一次 <c>IDifficultyHost.Apply</c>，本类型不需要重复处理；<paramref name="archetypeId"/>
    /// 当前未使用，恒使用 <see cref="GameOptions.PlayerClassId"/>）。真实游戏通常需要按存档槽/
    /// 难度/职业选择走不同的起始地图或初始装备，应替换本类型或直接在
    /// <see cref="GameBootstrap"/> 里换一个不同的 <c>NewGameStarter</c> 委托，不需要改
    /// <see cref="GameBootstrap"/> 其余装配代码——两者通过
    /// <c>PresentationAssemblyOptions.NewGameStarter</c> 这一个委托字段解耦。
    /// </para>
    /// </summary>
    public sealed class SampleNewGameStarter
    {
        private readonly GameBootstrap _bootstrap;

        public SampleNewGameStarter(GameBootstrap bootstrap)
        {
            _bootstrap = bootstrap;
        }

        /// <summary>匹配 <see cref="Presentation.Shell.NewGameStarter"/> 委托签名，供
        /// <c>PresentationAssemblyOptions.NewGameStarter</c> 直接赋值（方法组转委托）。</summary>
        public Id Start(Id slotId, Id difficultyId, Id? archetypeId)
        {
            var options = _bootstrap.RuntimeOptions;
            var player = _bootstrap.PlayerUnit;
            var startMapId = new Id(options.StartMapId);

            player.Position = Vec2.Zero;
            player.MapId = startMapId;
            player.TemplateId = new Id(options.PlayerTemplateId);

            return startMapId;
        }
    }
}

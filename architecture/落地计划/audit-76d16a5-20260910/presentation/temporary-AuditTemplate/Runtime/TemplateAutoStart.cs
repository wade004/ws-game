#nullable enable
// TemplateAutoStart：跳过主菜单直接开一局新游戏，供"首张地图"场景（见 Editor/GameSceneBuilder.cs
// 生成的 Map 场景）快速验证地图本身，不必每次都先点一遍主菜单——用途与
// adapters/unity/Assets/Editor/GreyBoxSceneBuilder.cs 生成的 GreyBox.unity（挂
// GameFoundationBootstrap，直接进世界，不经 Shell 流程）相同，只是本模板的 GameBootstrap 走的是
// 完整 Shell/存档/难度流程（见该类型顶部判断记录），所以需要这个极薄的驱动脚本替代"点一下主菜单
// 新游戏按钮"这一步，其余装配逻辑与真走一遍主菜单完全一致（同一个 GameBootstrap、同一个
// RequestNewGame/SampleNewGameStarter）。
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using UnityEngine;

namespace Game.Template
{
    public sealed class TemplateAutoStart : MonoBehaviour
    {
        private static readonly Id AutoStartSlotId = new Id("game.template.auto_start");

        private void Start()
        {
            var bootstrap = GameBootstrap.Ensure();
            if (bootstrap.BootstrapFailed)
            {
                Debug.LogError("[TemplateAutoStart] 游戏世界装配失败，无法自动开局。");
                return;
            }

            if (bootstrap.Gameplay.AppState.GetState() == AppState.Boot)
            {
                bootstrap.Presentation.Shell.Start();
            }

            var ok = bootstrap.RequestNewGame(AutoStartSlotId);
            if (!ok)
            {
                Debug.LogWarning("[TemplateAutoStart] RequestNewGame 返回失败。");
            }
        }
    }
}

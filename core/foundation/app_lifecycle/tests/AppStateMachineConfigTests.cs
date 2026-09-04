using System;
using Core.Foundation.AppLifecycle;
using Xunit;

namespace Tests.Foundation.AppLifecycle
{
    public class AppStateMachineConfigTests
    {
        // 1. Default() 的子状态默认表：Combat 上可叠加 MenuOverlay（见本模块判断记录 2）
        [Fact]
        public void Default_AllowsMenuOverlayOnCombat()
        {
            var config = AppStateMachineConfig.Default();

            Assert.True(config.IsSubTransitionAllowed(SubStateId.Combat, SubStateId.MenuOverlay));
        }

        // 2. Default() 不放行未声明的子转移
        [Fact]
        public void Default_DoesNotAllowUndeclaredSubTransition()
        {
            var config = AppStateMachineConfig.Default();

            Assert.False(config.IsSubTransitionAllowed(SubStateId.Dialog, SubStateId.Combat));
        }

        // 3. FromDefinitions 按 kind 分派构造出等价配置
        [Fact]
        public void FromDefinitions_BuildsExpectedConfig()
        {
            var config = AppStateMachineConfig.FromDefinitions(new[]
            {
                new GameStateTransitionDefinition("found.state.boot_to_main_menu", "Boot", "MainMenu", GameStateTransitionKind.Main),
                new GameStateTransitionDefinition("found.state.explore_to_combat", "Explore", "Combat", GameStateTransitionKind.Sub),
            });

            Assert.True(config.IsTransitionAllowed(AppState.Boot, AppState.MainMenu));
            Assert.True(config.IsSubTransitionAllowed(SubStateId.Explore, SubStateId.Combat));
            Assert.False(config.IsTransitionAllowed(AppState.MainMenu, AppState.Loading));
        }

        // 4. FromDefinitions 遇到非法 AppState 名字抛异常
        [Fact]
        public void FromDefinitions_InvalidAppStateName_Throws()
        {
            var definitions = new[]
            {
                new GameStateTransitionDefinition("found.state.bad", "NotAState", "MainMenu", GameStateTransitionKind.Main),
            };

            Assert.Throws<ArgumentException>(() => AppStateMachineConfig.FromDefinitions(definitions));
        }

        // 5. FromDefinitions 不隐式叠加 Default()
        [Fact]
        public void FromDefinitions_DoesNotImplicitlyIncludeDefault()
        {
            var config = AppStateMachineConfig.FromDefinitions(new[]
            {
                new GameStateTransitionDefinition("found.state.boot_to_main_menu", "Boot", "MainMenu", GameStateTransitionKind.Main),
            });

            Assert.False(config.IsTransitionAllowed(AppState.Loading, AppState.InWorld));
        }
    }
}

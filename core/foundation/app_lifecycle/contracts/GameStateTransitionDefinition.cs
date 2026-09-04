using System;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>区分 <see cref="GameStateTransitionDefinition"/> 描述的是主状态转移还是
    /// InWorld 子状态转移（对应数据表 <c>found.game_state</c> 的 <c>kind</c> 字段，见
    /// schema/found.game_state.md）。</summary>
    public enum GameStateTransitionKind
    {
        Main,
        Sub
    }

    /// <summary>
    /// 单条转移定义：对应数据表 <c>found.game_state</c> 的一行（见 04_数据与内容管线.md
    /// 第 1.1 节、schema/found.game_state.md）。供
    /// <see cref="AppStateMachineConfig.FromDefinitions"/> 从一组定义批量构造配置。
    /// </summary>
    public sealed class GameStateTransitionDefinition
    {
        /// <summary>转移 id（表中字段 <c>id</c>），格式 <c>found.state.&lt;from&gt;_to_&lt;to&gt;</c>。</summary>
        public string Id { get; }

        /// <summary>转移起点状态名（表中字段 <c>from</c>）：<see cref="GameStateTransitionKind.Main"/>
        /// 时取 <see cref="AppState"/> 枚举名，<see cref="GameStateTransitionKind.Sub"/> 时取
        /// <see cref="SubStateId"/> 名（内置枚举名或自定义子状态名）。</summary>
        public string From { get; }

        /// <summary>转移终点状态名（表中字段 <c>to</c>），取值规则同 <see cref="From"/>。</summary>
        public string To { get; }

        /// <summary>区分主状态转移还是子状态转移（表中字段 <c>kind</c>）。</summary>
        public GameStateTransitionKind Kind { get; }

        /// <summary>可选说明（表中字段 <c>description</c>）。</summary>
        public string? Description { get; }

        public GameStateTransitionDefinition(string id, string from, string to, GameStateTransitionKind kind, string? description = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("id 不能为空", nameof(id));
            }

            if (string.IsNullOrEmpty(from))
            {
                throw new ArgumentException("from 不能为空", nameof(from));
            }

            if (string.IsNullOrEmpty(to))
            {
                throw new ArgumentException("to 不能为空", nameof(to));
            }

            Id = id;
            From = from;
            To = to;
            Kind = kind;
            Description = description;
        }
    }
}

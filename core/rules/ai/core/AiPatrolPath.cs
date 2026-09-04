using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Ai
{
    /// <summary>
    /// <c>ai.patrol_path</c> 一条记录解析后的运行期形态（见 06 第 6.3 节"有序 Vec2 列表 +
    /// <c>loop</c>|<c>pingpong</c> 模式"）。本类型只在本模块内部使用。
    /// </summary>
    internal sealed class AiPatrolPath
    {
        public Id Id { get; }

        public IReadOnlyList<Vec2> Points { get; }

        public PatrolMode Mode { get; }

        public AiPatrolPath(Id id, IReadOnlyList<Vec2> points, PatrolMode mode)
        {
            Id = id;
            Points = points;
            Mode = mode;
        }
    }
}

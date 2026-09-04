using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 通用事件实现：<see cref="Key"/> + 一个字段名到值的字典。供测试与尚未定义强类型
    /// 事件类的发布方使用；正式业务事件建议各自定义实现 <see cref="IEvent"/> 的强类型类，
    /// 而不是长期依赖本类型（弱类型字典不利于编译期检查）。
    /// </summary>
    public sealed class GenericEvent : IEvent
    {
        private static readonly IReadOnlyDictionary<string, object?> EmptyFields =
            new Dictionary<string, object?>();

        public Id Key { get; }

        public IReadOnlyDictionary<string, object?> Fields { get; }

        public GenericEvent(Id key, IReadOnlyDictionary<string, object?>? fields = null)
        {
            Key = key;
            Fields = fields ?? EmptyFields;
        }
    }
}

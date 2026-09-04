using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json 里 <c>save.*</c> 三行）。与 hook_registry
    /// 的 <c>HookEventKeys</c>、sim_loop 的 <c>SimEventKeys</c> 同一惯例：模块自己持有一份
    /// 发出事件的 key 常量，不依赖 event_bus 模块的生成物 <c>EventKeys.g.cs</c>（两者值
    /// 始终一致，不产生冲突）。全部三个事件都经 <see cref="IEventBus.PublishImmediate"/>
    /// 发出（10 第 3 节步骤 9 的读档完成广播、以及存档/迁移完成都发生在非 tick 上下文，
    /// 见 <see cref="ISaveSystem.Save"/>/<see cref="ISaveSystem.Load"/> 实现）。
    /// </summary>
    public static class SaveEventKeys
    {
        public static readonly Id SaveCompleted = new Id("save.completed");
        public static readonly Id SaveLoaded = new Id("save.loaded");
        public static readonly Id SaveMigrated = new Id("save.migrated");
    }

    /// <summary>存档写入完成（found.event_catalog.json 登记字段：<c>slotId</c>）。</summary>
    public sealed class SaveCompletedEvent : IEvent
    {
        public Id Key => SaveEventKeys.SaveCompleted;

        public Id SlotId { get; }

        public SaveCompletedEvent(Id slotId)
        {
            SlotId = slotId;
        }
    }

    /// <summary>存档读取完成（found.event_catalog.json 登记字段：<c>slotId</c>）。发生在
    /// <see cref="ISaveSystem.Load"/> 成功恢复全部已注册段之后（10 第 3 节步骤 9）；
    /// 若某段 <see cref="IPersistable.Load"/> 抛出异常（<see cref="LoadStatus.PersistableThrew"/>），
    /// 本事件不会发出。</summary>
    public sealed class SaveLoadedEvent : IEvent
    {
        public Id Key => SaveEventKeys.SaveLoaded;

        public Id SlotId { get; }

        public SaveLoadedEvent(Id slotId)
        {
            SlotId = slotId;
        }
    }

    /// <summary>存档版本迁移完成（found.event_catalog.json 登记字段：<c>slotId</c>、
    /// <c>fromVersion</c>、<c>toVersion</c>）。仅在迁移链成功执行且文档版本确实发生变化时
    /// 发出，紧接在 <see cref="SaveLoadedEvent"/> 之前（10 第 5 节最后一步）。</summary>
    public sealed class SaveMigratedEvent : IEvent
    {
        public Id Key => SaveEventKeys.SaveMigrated;

        public Id SlotId { get; }

        public int FromVersion { get; }

        public int ToVersion { get; }

        public SaveMigratedEvent(Id slotId, int fromVersion, int toVersion)
        {
            SlotId = slotId;
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }
    }
}

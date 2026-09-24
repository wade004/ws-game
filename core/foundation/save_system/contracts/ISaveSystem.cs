using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 存档系统契约（见 01_分层与依赖.md L0 模块表 <c>save_system</c> 行"契约接口名：
    /// SaveSystem、Persistable"、10_存档与持久化.md 全文）。存档是本架构唯一的持久化机制
    /// （拍板决策 9，见 adr/0009-存档是唯一持久化.md）；本接口是上层唯一允许触碰的存档入口，
    /// 具体读写落盘经 <c>IFileSystem</c> 完成，本接口不暴露任何文件路径细节。
    /// </summary>
    public interface ISaveSystem
    {
        /// <summary>
        /// 登记一个存档段实现。<paramref name="persistable"/>.<see cref="IPersistable.SectionKey"/>
        /// 在同一实例内重复登记、或等于 <see cref="SaveSections.Meta"/> 时抛出
        /// <see cref="System.InvalidOperationException"/>/<see cref="System.ArgumentException"/>。
        /// </summary>
        void RegisterPersistable(IPersistable persistable);

        /// <summary>
        /// 登记一个版本迁移函数（见 10 第 5 节）。同一 <see cref="ISaveMigration.FromVersion"/>
        /// 重复登记时抛出 <see cref="System.InvalidOperationException"/>。
        /// </summary>
        void RegisterMigration(ISaveMigration migration);

        /// <summary>当前运行时的存档 schema 版本号（等于构造时传入的
        /// <see cref="SaveSystemOptions.CurrentSaveVersion"/>）。</summary>
        int CurrentSaveVersion { get; }

        /// <summary>
        /// 汇总全部已注册段并写入一个存档槽（10 第 3 节汇总、第 4 节原子写入）。手动存档与
        /// 自动存档触发后都走本方法，唯一区别是调用方是否先经过
        /// <see cref="ShouldAutoSave"/> 判断（见该方法文档、10 第 6 节）。
        /// </summary>
        SaveResult Save(SaveRequest request);

        /// <summary>
        /// 读取一个存档槽并按固定顺序（<see cref="SaveSections"/>）依次恢复全部已注册段
        /// （10 第 3、5 节）。
        /// </summary>
        LoadResult Load(Id slotId);

        /// <summary>
        /// 枚举全部存档槽的摘要（只读 meta 段，不加载完整存档内容），按
        /// <see cref="SaveSlotInfo.SlotId"/> 排序；无法解析的损坏文件被跳过并记入诊断，
        /// 不中断枚举（见 10 第 4 节"存档槽枚举经 listFiles……列举得到"）。
        /// </summary>
        IReadOnlyList<SaveSlotInfo> ListSlots();

        /// <summary>
        /// 删除一个存档槽的正式文件与其全部备份文件。返回该槽在删除前是否存在
        /// （即本次调用是否真正删除了什么）。
        /// </summary>
        bool DeleteSlot(Id slotId);

        /// <summary>判断某个存档槽的正式文件当前是否存在。</summary>
        bool SlotExists(Id slotId);

        /// <summary>
        /// CORE-180-01/03 根治：注入一个可选的派生状态重建钩子（见 <see
        /// cref="IDerivedStateRebuilder"/> 类型注释）——<see cref="Load"/> 在逐段读档过程中回调它，
        /// 弥补 <c>IEventBus.SuppressDispatch</c> 抑制作用域丢弃内部同步事件造成的缓存陈旧。至多
        /// 保留最近一次注入（重复调用直接覆盖，不追加），未调用时行为与引入本接口之前完全一致。
        /// C#8 默认接口方法：不覆盖时对全部既有 <see cref="ISaveSystem"/> 实现完全透明（默认空
        /// 实现，调用无效果），不需要逐一改动既有实现签名。
        /// </summary>
        void SetDerivedStateRebuilder(IDerivedStateRebuilder rebuilder)
        {
        }

        /// <summary>
        /// 按 <see cref="SaveSystemOptions.AutoSave"/> 策略判断给定触发点当前是否应当自动
        /// 存档。本方法只做策略判断，不产生任何副作用（不写盘、不发事件）；把某个触发点
        /// 接到具体游戏时机（场景切换完成、任务状态机进入完成节点……）并在返回 true 时
        /// 实际调用 <see cref="Save"/>，是上层（场景路由、任务系统等）的职责，本模块不
        /// 订阅任何触发点事件（10 第 6 节"基础架构提供触发点机制，不强制具体游戏必须启用
        /// 哪几个"——机制到此为止，接线是游戏层的事）。
        /// </summary>
        bool ShouldAutoSave(AutoSaveTrigger trigger);

        /// <summary>
        /// ADR-0085：与 <see cref="Load(Id)"/> 等价地读取并恢复一个存档槽，但当
        /// <paramref name="deferLoadedNotification"/> 为 <c>true</c> 时跳过末尾对
        /// <c>save.migrated</c>/<c>save.loaded</c>（<see cref="Core.Foundation.SaveSystem.SaveMigratedEvent"/>/
        /// <see cref="Core.Foundation.SaveSystem.SaveLoadedEvent"/>）的自动派发——调用方随后必须在
        /// 它认为"世界已经达到最终态"的时刻手动调用 <see cref="NotifyLoaded"/> 补发，否则这两个事件
        /// 永远不会发出。目前唯一的调用方是 <c>Core.Gameplay.Assembly.GameplayAssembly.RestoreFromSlot</c>
        /// 跨图分支（见该方法判断记录"时序源头"）：跨图读档需要先发起 <c>ISceneRouter.LoadScene</c>、
        /// 等 <c>ClearAll</c>/<c>post_load</c>（<c>EnterMap</c>）都跑完，"读档完成"的广播才真正对应
        /// "世界最终态"，而不是"逐段 Load 刚跑完、地图还没切"这个必然会被随后 <c>ClearAll</c> 整体
        /// 推翻的中间态。<paramref name="deferLoadedNotification"/> 为 <c>false</c> 时与
        /// <see cref="Load(Id)"/> 完全一致（本方法调用 <see cref="Load(Id)"/> 即是该行为）。
        /// <para>
        /// C#8 默认接口方法：默认实现直接退化为 <see cref="Load(Id)"/>（即忽略
        /// <paramref name="deferLoadedNotification"/>，行为等价于永远不延迟）——对既有
        /// <see cref="ISaveSystem"/> 实现透明，只有本仓库唯一实现 <c>Core.Foundation.SaveSystem.SaveSystem</c>
        /// 才需要、也确实覆盖了真正支持延迟派发的版本。
        /// </para>
        /// </summary>
        LoadResult Load(Id slotId, bool deferLoadedNotification) => Load(slotId);

        /// <summary>
        /// 配合 <see cref="Load(Id, bool)"/> 传入 <c>deferLoadedNotification: true</c> 使用：在调用方
        /// 认定的"世界最终态"时刻手动补发 <c>save.migrated</c>（<paramref name="migratedFromVersion"/>
        /// 非空时）与 <c>save.loaded</c>——取自 <see cref="LoadResult.MigratedFromVersion"/>，与
        /// <see cref="Load(Id)"/> 内部自动派发时用的是同一份判断（"迁移链确实执行过"）。对同一次读档
        /// 只应调用一次；不校验、不去重——重复调用会重复派发，调用方自己保证"只在世界真正达到最终态
        /// 的那一刻调用一次"（<c>GameplayAssembly.RestoreFromSlot</c> 用一个一次性 pending 字段保证
        /// 这一点，见该类型判断记录）。
        /// <para>
        /// C#8 默认接口方法：默认空实现（不派发任何事件）——只有支持
        /// <see cref="Load(Id, bool)"/> 延迟派发的实现才需要覆盖本方法；未覆盖 <see cref="Load(Id, bool)"/>
        /// 的实现也不会有任何调用方需要调用本方法（<see cref="Load(Id, bool)"/> 默认实现从不延迟）。
        /// </para>
        /// </summary>
        void NotifyLoaded(Id slotId, int? migratedFromVersion)
        {
        }
    }
}

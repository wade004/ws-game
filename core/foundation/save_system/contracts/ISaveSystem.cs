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
        /// 按 <see cref="SaveSystemOptions.AutoSave"/> 策略判断给定触发点当前是否应当自动
        /// 存档。本方法只做策略判断，不产生任何副作用（不写盘、不发事件）；把某个触发点
        /// 接到具体游戏时机（场景切换完成、任务状态机进入完成节点……）并在返回 true 时
        /// 实际调用 <see cref="Save"/>，是上层（场景路由、任务系统等）的职责，本模块不
        /// 订阅任何触发点事件（10 第 6 节"基础架构提供触发点机制，不强制具体游戏必须启用
        /// 哪几个"——机制到此为止，接线是游戏层的事）。
        /// </summary>
        bool ShouldAutoSave(AutoSaveTrigger trigger);
    }
}

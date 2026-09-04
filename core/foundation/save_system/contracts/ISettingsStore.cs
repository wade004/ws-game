using Core.Foundation.Common.Json;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 设置文件存储契约（见 10_存档与持久化.md 第 7 节"设置（画面、音效音量、按键绑定、
    /// 语言）与存档分离，单独落盘为一份不带 save_version 游戏进度语义的设置文档，可以有
    /// 自己独立的版本号用于设置项的迁移"）。与存档槽完全独立：删除任意存档槽、切换存档槽
    /// 都不影响设置文件，设置文件也不出现在 <see cref="ISaveSystem.ListSlots"/> 的结果里。
    /// 本契约不理解任何具体设置字段的含义（分辨率、音量、按键绑定、语言……），只负责
    /// "一份带独立版本号的 JSON 文档"的读写与迁移，具体字段结构由游戏层约定。
    /// </summary>
    public interface ISettingsStore
    {
        /// <summary>
        /// 读取设置内容。文件不存在时返回一个空对象（不视为错误）；文件存在但版本号低于
        /// <see cref="SettingsVersion"/> 时按 <see cref="RegisterMigration"/> 登记的迁移链
        /// 尽力升级（迁移链不完整时退回未迁移前的内容，不抛异常——设置文件损坏或迁移失败
        /// 不应阻断游戏启动，见本模块 README"设置文件"一节的判断记录）。返回值只是设置
        /// 内容本身（<c>settings_version</c> 信封字段不对外暴露）。
        /// </summary>
        JsonObject Load();

        /// <summary>把设置内容整体写入设置文件（<c>writeTextAtomic</c> 原子写入），
        /// 附带当前 <see cref="SettingsVersion"/>。返回写入是否成功。</summary>
        bool Save(JsonObject data);

        /// <summary>本运行时配置的设置文件版本号（构造期常量，不是"上次读到的版本号"，
        /// 与 <see cref="ISaveSystem.CurrentSaveVersion"/> 同一惯例）。</summary>
        int SettingsVersion { get; }

        /// <summary>登记一个设置文件版本迁移函数，复用 <see cref="ISaveMigration"/> 形状
        /// （<c>Migrate</c> 收到/返回的 <see cref="JsonObject"/> 是设置文件的完整信封
        /// <c>{ settings_version, settings }</c>，与存档文档信封同构但语义独立）。</summary>
        void RegisterMigration(ISaveMigration migration);
    }
}

using Core.Foundation.Common;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="ISaveSystem.ListSlots"/> 返回的单条存档槽摘要——只读 meta 段，不加载完整
    /// 存档内容，供存档槽 UI 列表展示使用（见 10_存档与持久化.md 第 4 节"存档槽枚举经
    /// listFiles 对用户数据目录下的存档子目录列举得到"）。
    /// </summary>
    public sealed class SaveSlotInfo
    {
        public Id SlotId { get; }

        public SaveMeta Meta { get; }

        /// <summary>正式存档文件的逻辑路径（<c>IFileSystem</c> 路径形态，非本机文件系统路径）。</summary>
        public string Path { get; }

        public SaveSlotInfo(Id slotId, SaveMeta meta, string path)
        {
            SlotId = slotId;
            Meta = meta;
            Path = path;
        }
    }
}

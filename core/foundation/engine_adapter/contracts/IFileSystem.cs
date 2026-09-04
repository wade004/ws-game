using System.Collections.Generic;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 用户目录、读写、原子写入（见 02_引擎适配层.md 第 1.6 节）。必需接口。
    /// WriteTextAtomic 必须真正具备原子性（典型做法是先写临时文件再原子替换），
    /// 保证写入过程要么完全成功要么保持旧文件不变，杜绝存档写到一半崩溃导致存档损坏。
    /// </summary>
    public interface IFileSystem
    {
        /// <summary>平台约定的用户数据目录（存档、设置的落盘位置）。</summary>
        string GetUserDataDir();

        string? ReadText(string path);

        bool WriteTextAtomic(string path, string content);

        bool Exists(string path);

        IReadOnlyList<string> ListFiles(string dirPath);

        bool DeleteFile(string path);
    }
}

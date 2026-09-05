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

        /// <summary>
        /// 只读内容根目录（数据表、静态资产的落盘位置），与用户数据目录分离（见 ADR-0016
        /// 决策 8）。对内容根目录下的路径调用 WriteTextAtomic/DeleteFile 一律返回 false，
        /// 不真正执行写入/删除，也不抛异常。
        /// </summary>
        string GetContentRootDir();

        string? ReadText(string path);

        /// <summary>内容根目录下的路径一律返回 false（见 GetContentRootDir 的语义条款）。</summary>
        bool WriteTextAtomic(string path, string content);

        bool Exists(string path);

        IReadOnlyList<string> ListFiles(string dirPath);

        /// <summary>内容根目录下的路径一律返回 false（见 GetContentRootDir 的语义条款）。</summary>
        bool DeleteFile(string path);
    }
}

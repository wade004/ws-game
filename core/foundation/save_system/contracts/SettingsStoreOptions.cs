namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="ISettingsStore"/> 的构造期配置。
    /// </summary>
    public sealed class SettingsStoreOptions
    {
        /// <summary>本运行时配置的设置文件版本号。默认 1。</summary>
        public int CurrentVersion { get; set; } = 1;

        /// <summary>设置文件名，相对 <c>IFileSystem.GetUserDataDir()</c>（不含目录，与存档槽
        /// 所在的 <see cref="SaveSystemOptions.SavesDirName"/> 子目录互不相关，见 10 第 7 节
        /// "与存档分离"）。默认 <c>"settings.json"</c>。</summary>
        public string FileName { get; set; } = "settings.json";
    }
}

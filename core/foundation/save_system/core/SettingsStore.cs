using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="ISettingsStore"/> 的默认实现（见 10_存档与持久化.md 第 7 节）。文件信封固定为
    /// <c>{ "settings_version": N, "settings": {...} }</c>，落盘路径为
    /// <c>&lt;GetUserDataDir()&gt;/&lt;FileName&gt;</c>（与 <see cref="SaveSystem"/> 的存档子目录
    /// 完全独立，不共享任何路径前缀，见本模块 README"设置文件"一节）。
    /// </summary>
    public sealed class SettingsStore : ISettingsStore
    {
        private readonly IFileSystem _fs;
        private readonly SettingsStoreOptions _options;
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        public SettingsStore(IFileSystem fileSystem, SettingsStoreOptions? options = null)
        {
            _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _options = options ?? new SettingsStoreOptions();
        }

        public int SettingsVersion => _options.CurrentVersion;

        public void RegisterMigration(ISaveMigration migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (migration.FromVersion >= migration.ToVersion)
            {
                throw new ArgumentException(
                    $"迁移函数 FromVersion({migration.FromVersion}) 必须小于 ToVersion({migration.ToVersion})",
                    nameof(migration));
            }

            if (_migrations.ContainsKey(migration.FromVersion))
            {
                throw new InvalidOperationException($"版本 {migration.FromVersion} 的迁移函数已注册，不能重复注册");
            }

            _migrations.Add(migration.FromVersion, migration);
        }

        public JsonObject Load()
        {
            var text = _fs.ReadText(Path());
            if (text == null)
            {
                return EmptyObject();
            }

            JsonObject envelope;
            try
            {
                var parsed = JsonReader.Parse(text);
                if (!(parsed is JsonObject obj))
                {
                    return EmptyObject();
                }

                envelope = obj;
            }
            catch (JsonParseException)
            {
                // 设置文件损坏不应阻断游戏启动：退化为空设置（见本模块 README 判断记录）。
                return EmptyObject();
            }

            if (!envelope.TryGetValue("settings_version", out var versionValue) ||
                !(versionValue is JsonNumber versionNumber) || !versionNumber.TryGetInt64(out var versionLong))
            {
                return ExtractSettings(envelope);
            }

            var version = (int)versionLong;
            if (version < _options.CurrentVersion)
            {
                envelope = RunMigrationChainBestEffort(envelope, version);
            }

            return ExtractSettings(envelope);
        }

        public bool Save(JsonObject data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var envelope = new JsonObjectBuilder()
                .Add("settings_version", new JsonNumber(_options.CurrentVersion))
                .Add("settings", data)
                .Build();

            return _fs.WriteTextAtomic(Path(), JsonWriter.Write(envelope));
        }

        /// <summary>尽力迁移：链条不完整或某一步抛异常时，直接返回迁移到此为止的结果
        /// （设置文件迁移失败不应阻断游戏启动，见 <see cref="Load"/> 与本模块 README）。</summary>
        private JsonObject RunMigrationChainBestEffort(JsonObject envelope, int fromVersion)
        {
            var current = envelope;
            var version = fromVersion;
            var hops = 0;

            while (version < _options.CurrentVersion && hops < 1000)
            {
                hops++;
                if (!_migrations.TryGetValue(version, out var migration))
                {
                    break;
                }

                JsonObject migrated;
                try
                {
                    migrated = migration.Migrate(current);
                }
                catch (Exception)
                {
                    break;
                }

                current = migrated;
                version = migration.ToVersion;
            }

            return current;
        }

        private static JsonObject ExtractSettings(JsonObject envelope)
        {
            if (envelope.TryGetValue("settings", out var settingsValue) && settingsValue is JsonObject settingsObj)
            {
                return settingsObj;
            }

            return EmptyObject();
        }

        private static JsonObject EmptyObject() => new JsonObjectBuilder().Build();

        private string Path() =>
            _fs.GetUserDataDir().EndsWith("/", StringComparison.Ordinal)
                ? _fs.GetUserDataDir() + _options.FileName
                : _fs.GetUserDataDir() + "/" + _options.FileName;
    }
}

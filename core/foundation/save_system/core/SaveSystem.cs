using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="ISaveSystem"/> 的默认实现（见本模块 README"文档格式与存储位置"一节）。
    /// 存档文档信封固定为 <c>{ "save_version": N, "sections": { "&lt;sectionKey&gt;": ... } }</c>，
    /// 落盘路径为 <c>&lt;GetUserDataDir()&gt;/&lt;SavesDirName&gt;/&lt;slotId.Value&gt;.json</c>，
    /// 备份路径为同目录下 <c>&lt;slotId.Value&gt;.bakN.json</c>（N 从 1 到
    /// <see cref="SaveSystemOptions.BackupCount"/>）。全程只经 <see cref="IFileSystem"/> 接触
    /// 存储，不直接触碰路径/平台 API；写入正式文件只调用一次 <c>WriteTextAtomic</c>，不绕过
    /// 其原子语义做多步写入（落地方案与分阶段计划.md T1-6 禁止事项）。无引擎依赖、不读系统
    /// 时间、无线程、无反射。
    /// </summary>
    public sealed class SaveSystem : ISaveSystem
    {
        /// <summary>迁移链单次调用允许的最大跳数，防止迁移函数登记出现环导致死循环。</summary>
        private const int MaxMigrationHops = 1000;

        private readonly IFileSystem _fs;
        private readonly SaveSystemOptions _options;
        private readonly IEventBus? _bus;
        private readonly ISaveDiagnostics _diagnostics;

        private readonly Dictionary<string, IPersistable> _persistables = new Dictionary<string, IPersistable>(StringComparer.Ordinal);
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        public SaveSystem(IFileSystem fileSystem, SaveSystemOptions options, IEventBus? bus = null, ISaveDiagnostics? diagnostics = null)
        {
            _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _bus = bus;
            _diagnostics = diagnostics ?? new InMemorySaveDiagnostics();
        }

        public int CurrentSaveVersion => _options.CurrentSaveVersion;

        public void RegisterPersistable(IPersistable persistable)
        {
            if (persistable == null)
            {
                throw new ArgumentNullException(nameof(persistable));
            }

            var key = persistable.SectionKey;
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("IPersistable.SectionKey 不能为空", nameof(persistable));
            }

            if (key == SaveSections.Meta)
            {
                throw new ArgumentException(
                    "\"meta\" 段由 SaveSystem 自身管理，不能通过 RegisterPersistable 注册", nameof(persistable));
            }

            if (_persistables.ContainsKey(key))
            {
                throw new InvalidOperationException($"存档段 \"{key}\" 已注册，不能重复注册");
            }

            _persistables.Add(key, persistable);
        }

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

        public bool ShouldAutoSave(AutoSaveTrigger trigger)
        {
            switch (trigger)
            {
                case AutoSaveTrigger.SavePoint: return _options.AutoSave.OnSavePoint;
                case AutoSaveTrigger.MapSwitch: return _options.AutoSave.OnMapSwitch;
                case AutoSaveTrigger.QuestComplete: return _options.AutoSave.OnQuestComplete;
                default: throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "未知的 AutoSaveTrigger");
            }
        }

        public bool SlotExists(Id slotId) => _fs.Exists(SlotPath(slotId));

        // ---- Save ---------------------------------------------------------

        public SaveResult Save(SaveRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var slotId = request.SlotId;
            var formalPath = SlotPath(slotId);
            var slotExists = _fs.Exists(formalPath);

            if (!slotExists && _options.MaxSlots > 0 && CountSlots() >= _options.MaxSlots)
            {
                return SaveResult.Fail(
                    SaveFailureReason.SlotLimitReached,
                    $"存档槽数量已达上限 {_options.MaxSlots.ToString(CultureInfo.InvariantCulture)}，且槽 \"{slotId}\" 不存在");
            }

            // 1. 收集全部已注册段（顺序：SaveSections.KnownOrder 中已注册的，随后自定义段按序数排序）。
            //    任一段 Save() 抛异常则整体中止，不写入任何文件。
            var writeOrder = ComputeWriteOrder();
            var sectionEntries = new List<KeyValuePair<string, JsonValue>>(writeOrder.Count);
            foreach (var key in writeOrder)
            {
                var persistable = _persistables[key];
                JsonValue sectionValue;
                try
                {
                    sectionValue = persistable.Save() ?? JsonNull.Instance;
                }
                catch (Exception ex)
                {
                    _diagnostics.Error($"存档段 \"{key}\" 的 Save() 抛出异常，存档已中止，未写入任何文件", ex);
                    return SaveResult.Fail(
                        SaveFailureReason.PersistableThrew,
                        $"存档段 \"{key}\" 的 Save() 抛出异常：{ex.Message}");
                }

                sectionEntries.Add(new KeyValuePair<string, JsonValue>(key, sectionValue));
            }

            // 2. meta：新建槽用本次时间戳作 created_at；覆盖已存在槽时尽量保留原 created_at
            //    （尽力而为——旧文件若无法解析，退化为把本次时间戳同时当作 created_at）。
            var createdAt = request.TimestampText;
            if (slotExists)
            {
                var existingText = _fs.ReadText(formalPath);
                if (existingText != null &&
                    TryParseEnvelope(existingText, out var existingEnvelope) &&
                    TryGetSectionsMeta(existingEnvelope, out var existingMetaObj))
                {
                    try
                    {
                        createdAt = ParseMeta(existingMetaObj, slotId).CreatedAt;
                    }
                    catch (FormatException)
                    {
                        // 旧 meta 段解析失败：不阻断本次存档，退化为用本次时间戳作 created_at。
                    }
                }
            }

            var meta = new SaveMeta(
                _options.CurrentSaveVersion,
                slotId,
                createdAt,
                request.TimestampText,
                request.PlayTimeSeconds,
                request.DisplaySummary,
                _options.GameId,
                request.DifficultyId);

            var sectionsBuilder = new JsonObjectBuilder().Add(SaveSections.Meta, BuildMetaJson(meta));
            foreach (var entry in sectionEntries)
            {
                sectionsBuilder.Add(entry.Key, entry.Value);
            }

            var envelope = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(_options.CurrentSaveVersion))
                .Add("sections", sectionsBuilder.Build())
                .Build();

            var text = JsonWriter.Write(envelope);

            // 3. 备份轮转（先于正式写入；即便随后正式写入失败，备份已经保留了旧内容的副本，
            //    正式文件本身因 WriteTextAtomic 语义在失败时保持不变，不会丢数据）。
            if (slotExists && _options.BackupCount > 0)
            {
                RotateBackups(slotId, formalPath);
            }

            var writeOk = _fs.WriteTextAtomic(formalPath, text);
            if (!writeOk)
            {
                _diagnostics.Error($"写入存档槽 \"{slotId}\" 失败（WriteTextAtomic 返回 false），旧存档内容保持不变", null);
                return SaveResult.Fail(SaveFailureReason.WriteFailed, $"写入存档槽 \"{slotId}\" 失败");
            }

            _bus?.PublishImmediate(new SaveCompletedEvent(slotId));
            return SaveResult.Ok();
        }

        private void RotateBackups(Id slotId, string formalPath)
        {
            // 从最旧的备份编号往新编号搬（BackupCount -> 2），最后把（迁移前的）正式文件内容
            // 写入 bak1。全部用 ReadText + WriteTextAtomic 完成"复制"，不使用重命名（IFileSystem
            // 未提供 rename，也不应绕过 writeTextAtomic 语义）。
            for (var i = _options.BackupCount; i >= 2; i--)
            {
                var src = BackupPath(slotId, i - 1);
                var content = _fs.ReadText(src);
                if (content != null)
                {
                    _fs.WriteTextAtomic(BackupPath(slotId, i), content);
                }
            }

            var formalContent = _fs.ReadText(formalPath);
            if (formalContent != null)
            {
                _fs.WriteTextAtomic(BackupPath(slotId, 1), formalContent);
            }
        }

        // ---- Load -----------------------------------------------------------

        public LoadResult Load(Id slotId)
        {
            var formalPath = SlotPath(slotId);
            if (!_fs.Exists(formalPath))
            {
                return LoadResult.NotFound();
            }

            var envelopeResult = ReadValidEnvelope(slotId, formalPath);
            if (envelopeResult == null)
            {
                _diagnostics.Error($"存档槽 \"{slotId}\" 正式文件与全部备份均无法解析，判定为损坏，原始文件保持不变", null);
                return LoadResult.Corrupted("正式文件与全部备份均无法解析为合法存档文档");
            }

            var (doc, status) = envelopeResult.Value;

            if (!TryGetInt(doc, "save_version", out var docVersion))
            {
                return LoadResult.Corrupted("文档缺少合法的 save_version 字段");
            }

            int? migratedFrom = null;
            if (docVersion > _options.CurrentSaveVersion)
            {
                return LoadResult.MigrationFailed(
                    $"存档版本 {docVersion.ToString(CultureInfo.InvariantCulture)} 高于当前运行时版本 " +
                    $"{_options.CurrentSaveVersion.ToString(CultureInfo.InvariantCulture)}，本架构不承诺向前兼容");
            }

            if (docVersion < _options.CurrentSaveVersion)
            {
                migratedFrom = docVersion;
                if (!TryRunMigrationChain(doc, docVersion, out doc, out var migrationError))
                {
                    _diagnostics.Error($"存档槽 \"{slotId}\" 迁移失败：{migrationError}", null);
                    return LoadResult.MigrationFailed(migrationError);
                }
            }

            if (!TryGetObject(doc, "sections", out var sections))
            {
                return LoadResult.Corrupted("文档缺少合法的 sections 字段");
            }

            if (!TryGetSectionsMeta(doc, out var metaObj))
            {
                return LoadResult.Corrupted("sections.meta 缺失或格式非法");
            }

            SaveMeta meta;
            try
            {
                meta = ParseMeta(metaObj, slotId);
            }
            catch (FormatException fe)
            {
                return LoadResult.Corrupted($"meta 段解析失败：{fe.Message}");
            }

            var readOrder = ComputeReadOrder(sections);
            foreach (var key in readOrder)
            {
                if (!_persistables.TryGetValue(key, out var persistable))
                {
                    _diagnostics.Warn($"存档段 \"{key}\" 未注册对应的 IPersistable，读档时已跳过该段");
                    continue;
                }

                if (!sections.TryGetValue(key, out var sectionValue))
                {
                    _diagnostics.Warn($"存档段 \"{key}\" 已注册 IPersistable 但文档中缺失该段，读档时已跳过对它的 Load 调用");
                    continue;
                }

                try
                {
                    persistable.Load(sectionValue);
                }
                catch (Exception ex)
                {
                    _diagnostics.Error($"存档段 \"{key}\" 的 Load() 抛出异常，此前已加载的段不会回滚", ex);
                    return LoadResult.PersistableThrew(meta, migratedFrom, $"存档段 \"{key}\" 的 Load() 抛出异常：{ex.Message}");
                }
            }

            if (migratedFrom.HasValue)
            {
                _bus?.PublishImmediate(new SaveMigratedEvent(slotId, migratedFrom.Value, _options.CurrentSaveVersion));
            }

            _bus?.PublishImmediate(new SaveLoadedEvent(slotId));

            return LoadResult.Loaded(meta, migratedFrom, status);
        }

        private (JsonObject doc, LoadStatus status)? ReadValidEnvelope(Id slotId, string formalPath)
        {
            var formalText = _fs.ReadText(formalPath);
            if (formalText != null && TryParseEnvelope(formalText, out var formalDoc))
            {
                return (formalDoc, LoadStatus.Loaded);
            }

            _diagnostics.Warn($"存档槽 \"{slotId}\" 正式文件无法解析为合法存档文档，尝试回退到备份");

            for (var i = 1; i <= _options.BackupCount; i++)
            {
                var backupText = _fs.ReadText(BackupPath(slotId, i));
                if (backupText != null && TryParseEnvelope(backupText, out var backupDoc))
                {
                    _diagnostics.Warn($"存档槽 \"{slotId}\" 已从备份 bak{i.ToString(CultureInfo.InvariantCulture)} 恢复读取");
                    return (backupDoc, LoadStatus.LoadedFromBackup);
                }
            }

            return null;
        }

        private bool TryRunMigrationChain(JsonObject doc, int fromVersion, out JsonObject result, out string error)
        {
            var current = doc;
            var version = fromVersion;
            var hops = 0;

            while (version < _options.CurrentSaveVersion)
            {
                hops++;
                if (hops > MaxMigrationHops)
                {
                    result = doc;
                    error = $"迁移链跳数超过安全上限 {MaxMigrationHops.ToString(CultureInfo.InvariantCulture)}，可能存在环";
                    return false;
                }

                if (!_migrations.TryGetValue(version, out var migration))
                {
                    result = doc;
                    error = $"缺少起始版本 {version.ToString(CultureInfo.InvariantCulture)} 的迁移函数，" +
                            $"无法升级到当前版本 {_options.CurrentSaveVersion.ToString(CultureInfo.InvariantCulture)}";
                    return false;
                }

                JsonObject migrated;
                try
                {
                    migrated = migration.Migrate(current) ?? throw new InvalidOperationException("迁移函数返回了 null");
                }
                catch (Exception ex)
                {
                    result = doc;
                    error = $"版本 {migration.FromVersion.ToString(CultureInfo.InvariantCulture)} → " +
                            $"{migration.ToVersion.ToString(CultureInfo.InvariantCulture)} 的迁移函数抛出异常：{ex.Message}";
                    return false;
                }

                current = WithField(migrated, "save_version", new JsonNumber(migration.ToVersion));
                version = migration.ToVersion;
            }

            result = current;
            error = string.Empty;
            return true;
        }

        // ---- ListSlots / DeleteSlot ------------------------------------------

        public IReadOnlyList<SaveSlotInfo> ListSlots()
        {
            var dir = SavesDir();
            var files = _fs.ListFiles(dir);
            var result = new List<SaveSlotInfo>();

            foreach (var relative in files)
            {
                if (relative.IndexOf('/') >= 0)
                {
                    continue; // 存档槽只存在存档目录的直接层级，忽略任何更深层级的文件。
                }

                if (!relative.EndsWith(".json", StringComparison.Ordinal) || IsBackupFileName(relative))
                {
                    continue;
                }

                var slotIdText = relative.Substring(0, relative.Length - ".json".Length);
                if (!Id.TryParse(slotIdText, out var slotId))
                {
                    _diagnostics.Warn($"存档目录下文件名 \"{relative}\" 不是合法的 Id 文本，ListSlots 已跳过");
                    continue;
                }

                var path = SlotPath(slotId);
                var text = _fs.ReadText(path);
                if (text == null || !TryParseEnvelope(text, out var doc) ||
                    !TryGetSectionsMeta(doc, out var metaObj))
                {
                    _diagnostics.Warn($"存档槽 \"{slotId}\" 的文件无法解析出合法 meta 段，ListSlots 已跳过（不回退备份，只影响列举摘要）");
                    continue;
                }

                SaveMeta meta;
                try
                {
                    meta = ParseMeta(metaObj, slotId);
                }
                catch (FormatException)
                {
                    _diagnostics.Warn($"存档槽 \"{slotId}\" 的 meta 段解析失败，ListSlots 已跳过");
                    continue;
                }

                result.Add(new SaveSlotInfo(slotId, meta, path));
            }

            result.Sort((a, b) => a.SlotId.CompareTo(b.SlotId));
            return result;
        }

        public bool DeleteSlot(Id slotId)
        {
            var formalPath = SlotPath(slotId);
            var existed = _fs.Exists(formalPath);

            _fs.DeleteFile(formalPath);
            for (var i = 1; i <= _options.BackupCount; i++)
            {
                _fs.DeleteFile(BackupPath(slotId, i));
            }

            return existed;
        }

        private int CountSlots()
        {
            var files = _fs.ListFiles(SavesDir());
            var count = 0;
            foreach (var f in files)
            {
                if (f.IndexOf('/') >= 0)
                {
                    continue;
                }

                if (f.EndsWith(".json", StringComparison.Ordinal) && !IsBackupFileName(f))
                {
                    count++;
                }
            }

            return count;
        }

        // ---- 段顺序 ------------------------------------------------------

        private List<string> ComputeWriteOrder()
        {
            var result = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (var key in SaveSections.KnownOrder)
            {
                if (key == SaveSections.Meta)
                {
                    continue;
                }

                if (_persistables.ContainsKey(key))
                {
                    result.Add(key);
                    used.Add(key);
                }
            }

            var customKeys = new List<string>();
            foreach (var key in _persistables.Keys)
            {
                if (!used.Contains(key))
                {
                    customKeys.Add(key);
                }
            }

            customKeys.Sort(StringComparer.Ordinal);
            result.AddRange(customKeys);
            return result;
        }

        private static List<string> ComputeReadOrder(JsonObject sections)
        {
            var result = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal) { SaveSections.Meta };

            foreach (var key in SaveSections.KnownOrder)
            {
                if (key == SaveSections.Meta)
                {
                    continue;
                }

                if (sections.ContainsKey(key))
                {
                    result.Add(key);
                    used.Add(key);
                }
            }

            var customKeys = new List<string>();
            foreach (var key in sections.Keys)
            {
                if (!used.Contains(key))
                {
                    customKeys.Add(key);
                }
            }

            customKeys.Sort(StringComparer.Ordinal);
            result.AddRange(customKeys);
            return result;
        }

        // ---- meta 段读写 ---------------------------------------------------

        private static JsonObject BuildMetaJson(SaveMeta meta)
        {
            var b = new JsonObjectBuilder()
                .Add("save_version", new JsonNumber(meta.SaveVersion))
                .Add("slot_id", new JsonString(meta.SlotId.Value))
                .Add("created_at", new JsonString(meta.CreatedAt))
                .Add("updated_at", new JsonString(meta.UpdatedAt));

            if (meta.PlayTimeSeconds.HasValue)
            {
                b.Add("play_time_seconds", new JsonNumber(meta.PlayTimeSeconds.Value));
            }

            if (meta.DisplaySummary.Count > 0)
            {
                var db = new JsonObjectBuilder();
                foreach (var kv in meta.DisplaySummary)
                {
                    db.Add(kv.Key, new JsonString(kv.Value));
                }

                b.Add("display_summary", db.Build());
            }

            b.Add("game_id", new JsonString(meta.GameId.Value));

            if (meta.DifficultyId.HasValue)
            {
                b.Add("difficulty_id", new JsonString(meta.DifficultyId.Value.Value));
            }

            return b.Build();
        }

        private static SaveMeta ParseMeta(JsonObject metaObj, Id slotIdFallback)
        {
            if (!TryGetInt(metaObj, "save_version", out var saveVersion))
            {
                throw new FormatException("meta 段缺少合法的 save_version 字段");
            }

            var slotId = slotIdFallback;
            if (metaObj.TryGetValue("slot_id", out var slotIdValue))
            {
                if (!(slotIdValue is JsonString slotIdText) || !Id.TryParse(slotIdText.Value, out slotId))
                {
                    throw new FormatException("meta 段的 slot_id 字段不是合法的 Id 文本");
                }
            }

            var createdAt = RequireString(metaObj, "created_at");
            var updatedAt = RequireString(metaObj, "updated_at");

            long? playTimeSeconds = null;
            if (metaObj.TryGetValue("play_time_seconds", out var playTimeValue))
            {
                if (!(playTimeValue is JsonNumber playTimeNumber) || !playTimeNumber.TryGetInt64(out var playTimeLong))
                {
                    throw new FormatException("meta 段的 play_time_seconds 字段不是合法的整数");
                }

                playTimeSeconds = playTimeLong;
            }

            Dictionary<string, string>? displaySummary = null;
            if (metaObj.TryGetValue("display_summary", out var displaySummaryValue))
            {
                if (!(displaySummaryValue is JsonObject displaySummaryObj))
                {
                    throw new FormatException("meta 段的 display_summary 字段不是 JSON 对象");
                }

                displaySummary = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in displaySummaryObj)
                {
                    if (!(entry.Value is JsonString text))
                    {
                        throw new FormatException($"meta 段的 display_summary.{entry.Key} 不是字符串");
                    }

                    displaySummary[entry.Key] = text.Value;
                }
            }

            var gameIdText = RequireString(metaObj, "game_id");
            if (!Id.TryParse(gameIdText, out var gameId))
            {
                throw new FormatException("meta 段的 game_id 字段不是合法的 Id 文本");
            }

            Id? difficultyId = null;
            if (metaObj.TryGetValue("difficulty_id", out var difficultyIdValue))
            {
                if (!(difficultyIdValue is JsonString difficultyIdText) || !Id.TryParse(difficultyIdText.Value, out var parsedDifficultyId))
                {
                    throw new FormatException("meta 段的 difficulty_id 字段不是合法的 Id 文本");
                }

                difficultyId = parsedDifficultyId;
            }

            return new SaveMeta(saveVersion, slotId, createdAt, updatedAt, playTimeSeconds, displaySummary, gameId, difficultyId);
        }

        private static string RequireString(JsonObject obj, string key)
        {
            if (!obj.TryGetValue(key, out var value) || !(value is JsonString text))
            {
                throw new FormatException($"meta 段缺少合法的 {key} 字段");
            }

            return text.Value;
        }

        // ---- 文档信封辅助 ---------------------------------------------------

        private static bool TryParseEnvelope(string text, out JsonObject doc)
        {
            try
            {
                var value = JsonReader.Parse(text);
                if (value is JsonObject obj && obj.ContainsKey("save_version") && obj.ContainsKey("sections"))
                {
                    doc = obj;
                    return true;
                }
            }
            catch (JsonParseException)
            {
                // 解析失败视为信封非法，落到下面的 doc = null!; return false。
            }

            doc = null!;
            return false;
        }

        private static bool TryGetSectionsMeta(JsonObject doc, out JsonObject metaObj)
        {
            if (TryGetObject(doc, "sections", out var sections) &&
                sections.TryGetValue(SaveSections.Meta, out var metaValue) &&
                metaValue is JsonObject meta)
            {
                metaObj = meta;
                return true;
            }

            metaObj = null!;
            return false;
        }

        private static bool TryGetObject(JsonObject doc, string key, out JsonObject value)
        {
            if (doc.TryGetValue(key, out var raw) && raw is JsonObject obj)
            {
                value = obj;
                return true;
            }

            value = null!;
            return false;
        }

        private static bool TryGetInt(JsonObject obj, string key, out int value)
        {
            if (obj.TryGetValue(key, out var raw) && raw is JsonNumber number && number.TryGetInt64(out var longValue) &&
                longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                value = (int)longValue;
                return true;
            }

            value = 0;
            return false;
        }

        private static JsonObject WithField(JsonObject obj, string key, JsonValue value)
        {
            var b = new JsonObjectBuilder();
            var replaced = false;
            foreach (var kv in obj)
            {
                if (kv.Key == key)
                {
                    b.Add(key, value);
                    replaced = true;
                }
                else
                {
                    b.Add(kv.Key, kv.Value);
                }
            }

            if (!replaced)
            {
                b.Add(key, value);
            }

            return b.Build();
        }

        private static bool IsBackupFileName(string fileName)
        {
            // "<slot>.bakN.json" 形态：从后往前找 ".json"，再找紧邻其前的 ".bak<数字>" 段。
            const string suffix = ".json";
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var withoutJson = fileName.Substring(0, fileName.Length - suffix.Length);
            var bakIndex = withoutJson.LastIndexOf(".bak", StringComparison.Ordinal);
            if (bakIndex < 0)
            {
                return false;
            }

            var digits = withoutJson.Substring(bakIndex + ".bak".Length);
            if (digits.Length == 0)
            {
                return false;
            }

            foreach (var c in digits)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        // ---- 路径拼装 ------------------------------------------------------

        private string SavesDir() => JoinPath(_fs.GetUserDataDir(), _options.SavesDirName);

        private string SlotPath(Id slotId) => JoinPath(SavesDir(), slotId.Value + ".json");

        private string BackupPath(Id slotId, int index) =>
            JoinPath(SavesDir(), slotId.Value + ".bak" + index.ToString(CultureInfo.InvariantCulture) + ".json");

        private static string JoinPath(string baseDir, string segment)
        {
            if (string.IsNullOrEmpty(baseDir))
            {
                return segment;
            }

            return baseDir.EndsWith("/", StringComparison.Ordinal) ? baseDir + segment : baseDir + "/" + segment;
        }
    }
}

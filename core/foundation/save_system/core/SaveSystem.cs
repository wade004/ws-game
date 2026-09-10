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
    /// 备份路径为独立子目录 <c>&lt;GetUserDataDir()&gt;/&lt;SavesDirName&gt;/backups/&lt;slotId.Value&gt;.bakN.json</c>
    /// （N 从 1 到 <see cref="SaveSystemOptions.BackupCount"/>；FND-01 收口：备份此前与正式槽文件
    /// 同目录、同 <c>.json</c> 后缀，与"槽 id 恰好长得像 <c>&lt;slot&gt;.bakN</c>"的合法槽路径可能
    /// 逐字节相同，见 <see cref="BackupPath"/> 判断记录）。全程只经 <see cref="IFileSystem"/> 接触
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

        /// <summary>CORE-180-01/03 根治：见 <see cref="ISaveSystem.SetDerivedStateRebuilder"/>/
        /// <see cref="IDerivedStateRebuilder"/> 判断记录。可选（默认 null，<see cref="Load"/> 内
        /// 全部调用点按 null 条件调用短路，行为与引入本字段之前完全一致）。</summary>
        private IDerivedStateRebuilder? _derivedStateRebuilder;

        public SaveSystem(IFileSystem fileSystem, SaveSystemOptions options, IEventBus? bus = null, ISaveDiagnostics? diagnostics = null)
        {
            _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _bus = bus;
            _diagnostics = diagnostics ?? new InMemorySaveDiagnostics();
        }

        public void SetDerivedStateRebuilder(IDerivedStateRebuilder rebuilder)
        {
            _derivedStateRebuilder = rebuilder ?? throw new ArgumentNullException(nameof(rebuilder));
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

            // FND-07 收口：正式文件与全部备份统一作为候选，不再在正式文件缺失时提前返回
            // NotFound——此前的提前返回会导致"正式文件被删/丢失，但备份仍然完好"这种本该可以
            // 恢复的场景被直接判定为槽不存在，永远不会尝试任何备份。只有当正式文件与全部备份都
            // 不存在（anyCandidateExisted 为 false）时才是真正的 NotFound；存在至少一个候选但
            // 没有一个能通过完整信封校验，判定为 Corrupted（与此前语义一致）。
            var envelopeResult = ReadValidEnvelope(slotId, formalPath, out var anyCandidateExisted);
            if (envelopeResult == null)
            {
                if (!anyCandidateExisted)
                {
                    return LoadResult.NotFound();
                }

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

            // 判断记录（CurrentMapId/CurrentPosition 直接读 sections，不依赖 IPersistable 注册）：
            // 见 LoadResult.CurrentMapId 文档——调用方可能需要在任何 world.current_map_id 对应的
            // IPersistable（如 Core.Carriers.Unit.UnitPersistable.CurrentMapId）注册之前就知道
            // 目标地图（例如驱动场景路由），因此这两个字段的解析独立于下面的 readOrder 循环，
            // 直接从已解析出的 sections 原始 JSON 取值；解析失败（段缺失/类型不符，如旧存档没有
            // 这两个段）不影响读档流程本身，静默为 null，不记诊断（10 文档未把这两个字段列为
            // "缺失即损坏"，只是"必填"——本模块对必填字段的缺失采取宽松兜底，呼应
            // UnitPersistable.Load 对 JsonNull 的处理）。
            var currentMapId = TryGetSectionId(sections, SaveSections.WorldCurrentMapId);
            var currentPosition = TryGetSectionVec2(sections, SaveSections.WorldCurrentPosition);

            // CR150-03 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：读取顺序按"全部已
            // 注册的 IPersistable"（惯例同 ComputeWriteOrder），不再只按"文档里实际存在哪些段"
            // 计算——后者会让已注册但文档缺失的段整段跳过 Load 调用，见 IPersistable.Load 判断
            // 记录。文档里存在但没有任何已注册 IPersistable 认领的自定义段（例如更新版本移除了
            // 某个模块、旧档仍带着它的段）单独警告一次，不影响本次读档流程。
            foreach (var key in sections.Keys)
            {
                if (key == SaveSections.Meta)
                {
                    continue;
                }

                if (!_persistables.ContainsKey(key))
                {
                    _diagnostics.Warn($"存档段 \"{key}\" 未注册对应的 IPersistable，读档时已跳过该段");
                }
            }

            var readOrder = ComputeReadOrder();

            // AUD-01 根治（architecture/落地计划/audit-85f1f4f-20260908，P1）：开始逐段 Load 之前，
            // 对全部已注册段各调用一次 Save() 做内存快照——不是为了写回磁盘，只是留一份"读档前
            // 状态"，供随后任一段 Load 失败时尽力回滚已经成功加载的前段（见下方
            // RollbackLoadedSections 判断记录、IPersistable.Load 判断记录修订、10 第 5 节勘误"段
            // 失败回滚到读档前状态"合同）。快照本身允许失败（某个段的 Save() 抛异常）——不因此中止
            // 读档流程，只是那个段之后万一需要回滚时无快照可用，尽力而为。
            var preLoadSnapshots = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var key in readOrder)
            {
                try
                {
                    preLoadSnapshots[key] = _persistables[key].Save() ?? JsonNull.Instance;
                }
                catch (Exception ex)
                {
                    _diagnostics.Warn(
                        $"存档段 \"{key}\" 读档前快照失败（{ex.Message}），若本次读档中途失败，该段将无法回滚");
                }
            }

            var loadedKeysInOrder = new List<string>(readOrder.Count);

            // CORE-180-01/03 根治：真正开始逐段 Load 之前调用一次 BeforeLoad（见 IDerivedStateRebuilder
            // 判断记录）——供实现快照"读档前"的字段值（如玩家当前 ArchetypeId/RaceId），供后续
            // OnSectionLoaded 判断某个字段是否真的发生了变化。允许失败：钩子本身抛异常不应该让"这次
            // 读档"被误判为某个存档段的 Load() 失败，只记诊断、按未注入处理继续往下走。
            try
            {
                _derivedStateRebuilder?.BeforeLoad();
            }
            catch (Exception ex)
            {
                _diagnostics.Warn($"派生状态重建钩子 BeforeLoad 抛出异常（{ex.Message}），本次读档继续");
            }

            // CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：整段"逐段 Load +
            // 失败回滚"逻辑（含 RollbackLoadedSections 内部重新调用的 Load）都在 IEventBus.
            // SuppressDispatch 作用域内进行——见该方法判断记录"读档不是业务事件"。EquipmentPersistable.
            // Load 为复用真实逻辑会调用真正的 Equip/Unequip，产生真实的 ItemEquipped/StatChanged 等
            // 领域事件；SaveSystem 逆序回滚失败读档时重放这些调用，会让 AchievementHost 一类计数类
            // 消费者把"读档/回滚期间的重放"误当成真实玩家操作再计一次数（真实探针复现：进度被回滚
            // 重放的事件从 1 错误推高到 2 并触发解锁）。作用域外（本方法末尾）才正常派发
            // SaveMigratedEvent/SaveLoadedEvent——"本次读档完成了"这个通知不是重放，理应正常送达；
            // using 保证无论正常结束还是提前 return（读档失败分支）都会释放作用域。
            using (_bus?.SuppressDispatch())
            {
                foreach (var key in readOrder)
                {
                    var persistable = _persistables[key];
                    var sectionPresent = sections.TryGetValue(key, out var sectionValue);

                    if (!sectionPresent)
                    {
                        if (persistable.KeepStateWhenSectionMissing)
                        {
                            // 显式声明"缺失即保留"：不调用 Load，当前状态原样不动（见 IPersistable.
                            // KeepStateWhenSectionMissing 判断记录）。
                            continue;
                        }

                        sectionValue = JsonNull.Instance;
                    }

                    try
                    {
                        persistable.Load(sectionValue);
                        loadedKeysInOrder.Add(key);

                        // CORE-180-01/03 根治：本段成功 Load 之后立即回调一次（见 IDerivedStateRebuilder
                        // 判断记录）——仍在上面的 SuppressDispatch 作用域内，但钩子实现直接调用目标
                        // 模块方法、不经事件总线，不受抑制影响。钩子本身抛异常同样只记诊断、不影响
                        // "这一段 Load 成功了"这一事实，不升级成本段的 PersistableThrew。
                        try
                        {
                            _derivedStateRebuilder?.OnSectionLoaded(key);
                        }
                        catch (Exception rebuildEx)
                        {
                            _diagnostics.Warn(
                                $"存档段 \"{key}\" 读档后派生状态重建钩子抛出异常（{rebuildEx.Message}），" +
                                "可能存在内部缓存未同步，本次读档继续");
                        }
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.Error(
                            $"存档段 \"{key}\" 的 Load() 抛出异常，正在按读档同一顺序尽力回滚此前已成功" +
                            "加载的段（含失败段自身，见 RollbackLoadedSections 判断记录）", ex);
                        // CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：修复前
                        // 只把 loadedKeysInOrder（Load() 没有抛异常、已经"成功加载"）里的段纳入回滚，
                        // 抛异常的这一段自身从不在这份列表里——如果它的 Load() 实现在校验数据形状之前
                        // 就已经修改了 live 状态（真实探针 EquipmentPersistable.Load 复现的正是这个
                        // 缺陷类别，见该方法判断记录），SaveSystem 这一层完全没有尝试恢复它，只回滚了
                        // "此前成功的其它段"。preLoadSnapshots 在进入本次读档循环之前已经对全部已注册段
                        // （不止成功段）各做过一次快照（见上方判断记录），因此把 key 自身一并加入回滚
                        // 列表末尾（回滚按与 readOrder 相同的正向顺序处理，见 CORE-180-02 根治、
                        // RollbackLoadedSections 判断记录——失败段本就是 readOrder 中排在
                        // loadedKeysInOrder 之后的下一个，直接追加到列表末尾天然仍是正向顺序，不需要
                        // 额外调整）成本很低：对已经遵循"先解析校验、再一次性提交"的段（本仓库当前已
                        // 审查的全部段，见各自 Load 判断记录），失败时 live 状态本就未被触碰，用
                        // preLoadSnapshots[key] 再 Load 一次是安全的幂等 no-op；对任何未来引入、仍不慎
                        // 踩了"先改状态后校验"这个坑的段，这一层作为额外防线尽力恢复，不依赖每个模块
                        // 各自完美遵守约定。
                        var rollbackKeysIncludingFailed = new List<string>(loadedKeysInOrder) { key };

                        // CORE-110-01 根治（architecture/落地计划/audit-ac3b622-20260909，P2，已确认，
                        // 子场景 B）：player.vitals 段（资源池当前值的唯一权威，见 SaveSections.
                        // PlayerVitals 判断记录"放在 player.* 分组末尾"）此前只有在它自己的 Load() 于
                        // 本次失败读档的正向阶段真的被成功调用过（即已经在 loadedKeysInOrder 里）才会
                        // 被回滚——但 player.equipment/player.race_id 段的 OnSectionLoaded 派生重建
                        // （见下方 RollbackLoadedSections 判断记录）会把资源池上限/当前值当作*副作用*
                        // 一并改写（如 PowerHost.RecomputeMax 在上限下降时连带 clamp 当前值），若失败
                        // 恰好发生在 player.vitals 段自身被读到之前（它在 SaveSections.KnownOrder 里
                        // 排在 player.equipment/player.known_skills 之后），这份副作用改写过的当前值
                        // 永远没有机会被 player.vitals 段自己的 Load() 用真实存档值纠正回来——真实探针
                        // 复现：装备段成功回滚、上限正确回到 200，但当前值仍停留在故障注入前那次
                        // RecomputeMax 下调 clamp 到的 100（应为 200）。player.vitals 是框架已知、
                        // 唯一对资源池当前值有最终解释权的段（见其类型判断记录"当前值不是能重新聚合
                        // 的派生结果"），本层因此始终尝试把它一并纳入回滚（如果确实已注册且存在读档前
                        // 快照）——已经在列表里（本就属于这次失败读档触碰过的段）则不重复追加；未注册
                        // 该段的宿主（不装配 GameplayAssembly 的最小场景）本就没有 player.vitals 这个
                        // key，短路为 no-op，行为与本次改动之前完全一致。
                        if (_persistables.ContainsKey(SaveSections.PlayerVitals) &&
                            preLoadSnapshots.ContainsKey(SaveSections.PlayerVitals) &&
                            !rollbackKeysIncludingFailed.Contains(SaveSections.PlayerVitals))
                        {
                            rollbackKeysIncludingFailed.Add(SaveSections.PlayerVitals);
                        }

                        RollbackLoadedSections(rollbackKeysIncludingFailed, preLoadSnapshots);
                        return LoadResult.PersistableThrew(
                            meta, migratedFrom, $"存档段 \"{key}\" 的 Load() 抛出异常：{ex.Message}",
                            currentMapId, currentPosition);
                    }
                }
            }

            if (migratedFrom.HasValue)
            {
                _bus?.PublishImmediate(new SaveMigratedEvent(slotId, migratedFrom.Value, _options.CurrentSaveVersion));
            }

            _bus?.PublishImmediate(new SaveLoadedEvent(slotId));

            return LoadResult.Loaded(meta, migratedFrom, status, currentMapId, currentPosition);
        }

        private static Id? TryGetSectionId(JsonObject sections, string key)
        {
            if (sections.TryGetValue(key, out var value) && value is JsonString text && Id.TryParse(text.Value, out var id))
            {
                return id;
            }

            return null;
        }

        private static Vec2? TryGetSectionVec2(JsonObject sections, string key)
        {
            if (sections.TryGetValue(key, out var value) && value is JsonObject obj
                && obj.TryGetValue("x", out var xv) && xv is JsonNumber xn
                && obj.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                return new Vec2(xn.Value, yn.Value);
            }

            return null;
        }

        /// <summary>
        /// FND-07 收口：把正式文件与全部备份统一当作候选，按优先级（正式文件 → bak1 → bak2 →
        /// … → bak&lt;BackupCount&gt;）依次尝试完整信封校验（<see cref="TryParseEnvelope"/>），
        /// 返回第一个通过校验的候选。<paramref name="anyCandidateExisted"/>（out）标记"是否至少
        /// 有一个候选文件在磁盘上存在过"——供调用方 <see cref="Load"/> 区分 <c>NotFound</c>
        /// （一个候选都不存在）与 <c>Corrupted</c>（存在候选但没有一个通过校验）。
        /// </summary>
        private (JsonObject doc, LoadStatus status)? ReadValidEnvelope(Id slotId, string formalPath, out bool anyCandidateExisted)
        {
            anyCandidateExisted = false;

            var formalText = _fs.ReadText(formalPath);
            if (formalText != null)
            {
                anyCandidateExisted = true;
                if (TryParseEnvelope(formalText, out var formalDoc) && IsCandidateMetaUsable(formalDoc, slotId))
                {
                    return (formalDoc, LoadStatus.Loaded);
                }

                _diagnostics.Warn($"存档槽 \"{slotId}\" 正式文件无法解析为合法存档文档，尝试回退到备份");
            }
            else
            {
                _diagnostics.Warn($"存档槽 \"{slotId}\" 正式文件不存在，尝试回退到备份");
            }

            for (var i = 1; i <= _options.BackupCount; i++)
            {
                var backupText = _fs.ReadText(BackupPath(slotId, i));
                if (backupText == null)
                {
                    continue;
                }

                anyCandidateExisted = true;
                if (TryParseEnvelope(backupText, out var backupDoc) && IsCandidateMetaUsable(backupDoc, slotId))
                {
                    _diagnostics.Warn($"存档槽 \"{slotId}\" 已从备份 bak{i.ToString(CultureInfo.InvariantCulture)} 恢复读取");
                    return (backupDoc, LoadStatus.LoadedFromBackup);
                }
            }

            // N16 收边补齐：新布局正式文件与全部新布局备份都不可用时，最后再按精确路径试一遍旧
            // 顶层布局遗留的备份（见 TryReadLegacyBackup 判断记录，只读不删、不做目录扫描）。
            var legacyResult = TryReadLegacyBackup(slotId, out var anyLegacyCandidateExisted);
            if (anyLegacyCandidateExisted)
            {
                anyCandidateExisted = true;
            }

            if (legacyResult != null)
            {
                return legacyResult;
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

                // FND-09 收口：拒绝任何会越过当前运行时版本的迁移步骤——RegisterMigration 只保证
                // FromVersion < ToVersion（见该方法），不保证 ToVersion 落在 CurrentSaveVersion
                // 以内；若登记了一条 1→3 的迁移函数、但当前运行时版本只到 2，此前的循环条件只看
                // "version < CurrentSaveVersion"，跑完这一步后 version 变成 3（不再小于 2），循环
                // 直接判定"已到达终点"退出成功，实际却把文档越级迁移到了一个当前运行时根本不认识、
                // 从未经过当前版本校验的结构，且报告的 migratedFrom/save_version 让调用方误以为
                // 迁移正常完成。10 第 5 节"存档版本高于当前运行时版本 → 不承诺向前兼容"这条拒绝
                // 语义必须对"迁移链中途产出的越界结果"同样成立，不能只检查文档最初的 save_version。
                if (migration.ToVersion > _options.CurrentSaveVersion)
                {
                    result = doc;
                    error = $"迁移函数 {migration.FromVersion.ToString(CultureInfo.InvariantCulture)} → " +
                            $"{migration.ToVersion.ToString(CultureInfo.InvariantCulture)} 会越过当前运行时版本 " +
                            $"{_options.CurrentSaveVersion.ToString(CultureInfo.InvariantCulture)}（本架构不承诺向前" +
                            "兼容，迁移链的终点必须恰好落在当前运行时版本上，不能途经或越过它）";
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

            // 双重防御：循环体内已经逐步拒绝任何会越过 CurrentSaveVersion 的单步迁移（见上），
            // 循环退出时 version 理应恰好等于 CurrentSaveVersion；这里再显式校验一次终点，防止
            // 未来维护本方法时误改循环条件或调整顺序导致上面的逐步校验被绕过而没有测试及时发现——
            // 循环终点必须恰好等于当前版本，而不只是"不小于"。
            if (version != _options.CurrentSaveVersion)
            {
                result = doc;
                error = $"迁移链结束于版本 {version.ToString(CultureInfo.InvariantCulture)}，与当前运行时版本 " +
                        $"{_options.CurrentSaveVersion.ToString(CultureInfo.InvariantCulture)} 不一致";
                return false;
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

                if (!relative.EndsWith(".json", StringComparison.Ordinal))
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

                if (f.EndsWith(".json", StringComparison.Ordinal))
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

        /// <summary>
        /// CR150-03 根治：读取顺序改按"全部已注册的 <see cref="IPersistable"/>"计算（与
        /// <see cref="ComputeWriteOrder"/> 完全同一套排序规则——已知段按 <see
        /// cref="SaveSections.KnownOrder"/>，自定义段按 key 的 ordinal 顺序排在已知段之后），
        /// 不再依赖当前这份存档文档里 <c>sections</c> 实际有哪些 key（那正是 CR150-03 复现的
        /// 缺陷根源：旧档缺失的段因此整个不出现在读取顺序里，永远不会被 <see cref="Load"/> 调用，
        /// 见 <see cref="IPersistable.Load"/> 判断记录）。是否真的调用某个段的 <c>Load</c>、以及
        /// 传入真实段内容还是 <see cref="JsonNull"/>，由 <see cref="Load"/> 主循环按段在文档中
        /// 是否存在决定，本方法只负责"该按什么顺序处理哪些已注册的段"这一件事。
        /// </summary>
        private List<string> ComputeReadOrder() => ComputeWriteOrder();

        /// <summary>
        /// AUD-01 根治：<see cref="Load"/> 主循环中途某段 <c>Load()</c> 抛异常时调用——用 <paramref
        /// name="preLoadSnapshots"/> 里对应段读档前的快照重新调用一次 <c>Load()</c>，把已经成功加载、
        /// 但整体读档结果注定失败（<see cref="LoadStatus.PersistableThrew"/>）的段尽力恢复回读档前的
        /// 状态，避免调用方看到一份"部分段已经是新档内容、部分段仍是旧内容、还有一段直接抛了异常"
        /// 的不一致中间态。
        /// <para>
        /// CORE-180-02 根治（architecture/落地计划/audit-e070e3f-20260908，P2）：按 <paramref
        /// name="loadedKeysInOrder"/> 的<b>正向</b>顺序回滚（与 <c>readOrder</c>/正常读档同一顺序），
        /// 取代此前"按逆序（后加载的先回滚）"的实现。逆序回滚违反了段与段之间真实存在的依赖顺序——
        /// <see cref="SaveSections.KnownOrder"/> 把 <see cref="SaveSections.PlayerProgression"/> 排在
        /// <see cref="SaveSections.PlayerEquipment"/> 之前正是因为装备重新装备（<c>EquipmentPersistable.
        /// Load</c> 复用真实 <c>EquipmentHost.Equip</c>）需要读到已经恢复到位的等级去做需求校验；
        /// 逆序回滚会先用读档前快照恢复 Equipment（此时等级字段仍是本次失败读档写入的低等级值），
        /// 装备因等级需求不满足而重新装备失败、物品留在背包，紧接着才轮到 Progression 用快照恢复
        /// 等级——为时已晚，没有人再重试装备（真实探针 <c>ROLLBACK-EQUIPMENT-BEFORE-PROGRESSION</c>
        /// 复现：<c>after_level=2</c> 等级已正确回滚，<c>equipped_after=False</c> 装备却没有跟着回来）。
        /// 改成正向顺序后，回滚本质上等价于"再做一次读档，只是把文档换成读档前的快照"——先恢复
        /// Progression（等级），再恢复 Equipment（此时能读到正确等级，重新装备按预期成功），与正常
        /// 读档路径共享同一套已经验证过的依赖顺序，不需要为回滚单独维护一份"应该谁先谁后"的规则。
        /// 对彼此没有依赖的段（多数自定义段），正向/逆序不影响最终恢复到读档前状态这一结果本身，
        /// 现有回归测试（<c>SaveSystemTests.Load_LaterSectionThrows_RollsBackEarlierSuccessfullyLoadedSection_ToPreLoadState</c>
        /// 等）不依赖具体回滚顺序，只依赖"最终恢复到位"，改动后仍然通过。
        /// </para>
        /// <para>
        /// 判断记录——为什么是"尽力而为"而不是必须成功：(a) 某段可能在快照阶段本身就失败（见 <see
        /// cref="Load"/> 快照循环），没有快照可回滚，只能跳过并记诊断；(b) 回滚调用的
        /// <c>Load(snapshot)</c> 本身也可能再次抛异常（例如该段的状态已经被后续段的联动改到某种
        /// 不允许再吃返回快照的中间形态）——此时记诊断并继续尝试回滚其它段，不让一个段的回滚失败
        /// 连锁阻断其它段的回滚，也不改变本次 <see cref="Load"/> 最终仍返回 <see
        /// cref="LoadStatus.PersistableThrew"/> 这一结果本身（回滚是尽力恢复现场，不是把失败的读档
        /// 伪装成功）。
        /// </para>
        /// <para>
        /// CORE-110-01 根治（architecture/落地计划/audit-ac3b622-20260909，P2，已确认）：某段的
        /// <c>Load(snapshot)</c> 回滚成功后，紧接着按与正常读档主循环完全相同的方式回调一次
        /// <see cref="IDerivedStateRebuilder.OnSectionLoaded"/>（仍在调用方 <see cref="Load"/> 的
        /// <see cref="Core.Foundation.EventBus.IEventBus.SuppressDispatch"/> 作用域内，抛异常同样只
        /// 记诊断、不影响"这一段已经回滚成功"这一事实）——此前只有正常读档主循环会调用这个钩子，
        /// 回滚完全不调用，依据是接口旧判断记录"回滚路径的正确性由 CORE-180-02（回滚顺序改判为
        /// 与正常读档同一正向顺序）本身保证，不依赖本钩子"；真实探针证明这个判断只对"字段类"状态
        /// 成立（<c>IPersistable.Load(snapshot)</c> 直接覆盖的字段确实会正确回滚），对"派生类"状态
        /// 不成立——评级换算属性、种族/职业被动光环、资源池上限都不是任何一个存档段自己的字段，
        /// 是若干个字段的一个函数，只把字段改回去、不重新算这个函数，函数结果永远停留在读档失败前
        /// 的陈旧值（例如种族字段已经回到 A，但评级/光环仍是 B，见 core-findings.md CORE-110-01
        /// 两个子场景）。本次回滚循环天然按与 <see cref="Load"/> 正常主循环相同的正向顺序处理
        /// <paramref name="loadedKeysInOrder"/>（CORE-180-02 既有结论），因此对每个成功回滚的段重放
        /// 同一个 <see cref="IDerivedStateRebuilder.OnSectionLoaded"/> 调用，等价于"用读档前快照
        /// 重新走一遍正常读档的派生重建依赖顺序"，不需要为回滚单独定义一套派生重建规则；
        /// <c>GameplayAssembly.DerivedStateRebuilder</c> 判断记录说明了这次复用为什么不需要区分
        /// "这次调用是正常读档还是回滚"。
        /// </para>
        /// </summary>
        private void RollbackLoadedSections(List<string> loadedKeysInOrder, Dictionary<string, JsonValue> preLoadSnapshots)
        {
            for (var i = 0; i < loadedKeysInOrder.Count; i++)
            {
                var key = loadedKeysInOrder[i];
                if (!preLoadSnapshots.TryGetValue(key, out var snapshot))
                {
                    _diagnostics.Error(
                        $"存档段 \"{key}\" 没有可用的读档前快照，无法回滚——该段可能仍停留在本次失败读档写入的中间值", null);
                    continue;
                }

                try
                {
                    _persistables[key].Load(snapshot);
                }
                catch (Exception ex)
                {
                    _diagnostics.Error($"存档段 \"{key}\" 回滚时再次抛出异常（尽力而为，不影响其它段的回滚）", ex);
                    continue;
                }

                try
                {
                    _derivedStateRebuilder?.OnSectionLoaded(key);
                }
                catch (Exception rebuildEx)
                {
                    _diagnostics.Warn(
                        $"存档段 \"{key}\" 回滚后派生状态重建钩子抛出异常（{rebuildEx.Message}），" +
                        "可能存在内部缓存未同步，继续回滚其它段");
                }
            }
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

        /// <summary>
        /// FND-07 收口：信封校验从"只看两个顶层 key 是否存在"加深到"顶层字段的类型也必须正确"——
        /// 此前只用 <c>JsonObject.ContainsKey</c> 判断 <c>save_version</c>/<c>sections</c> 是否
        /// 存在，不检查它们的值本身是不是"看起来合法"的类型，导致 <c>{"save_version":1,
        /// "sections":null}</c> 这类"只有字段名、内容却是垃圾"的文档被当成合法候选放行——若这份
        /// 文档恰好是正式文件，<see cref="ReadValidEnvelope"/> 会在它身上"成功"一次，从此不再尝试
        /// 任何备份，随后才在 <see cref="Load"/> 更深处的 <c>TryGetObject(doc, "sections", ...)</c>
        /// 检查里失败——但那时已经错过了本该被尝试的有效备份。
        /// <para>
        /// N15 收边补齐（外部审计 68c9bed，P2）：FND-07 的类型检查仍然比 <see cref="Load"/> 后续
        /// 实际要求的更浅，两个具体缺口（均为"能通过 FND-07 校验，但注定会在 <see cref="Load"/>
        /// 更深处判 Corrupted"的候选）：
        /// (1) <c>save_version</c> 只检查"是 <see cref="JsonNumber"/>"，不检查"是整数"——
        /// <c>1.5</c> 这类非整数版本号能通过本方法，却会在 <see cref="Load"/> 的
        /// <see cref="TryGetInt(JsonObject, string, out int)"/> 校验处判 Corrupted（该方法额外要求
        /// <see cref="JsonNumber.TryGetInt64"/> 成功）；
        /// (2) <c>sections</c> 只检查"是对象"，不检查"<c>sections.meta</c> 存在且也是对象"——
        /// <c>{"save_version":1,"sections":{}}</c> 这类"sections 本身合法但缺失必填 meta 子段"的
        /// 文档能通过本方法，却会在 <see cref="Load"/> 的 <see cref="TryGetSectionsMeta"/> 校验处
        /// 判 Corrupted。
        /// </para>
        /// <para>
        /// 两个缺口的共同后果：若这类"看起来通过信封校验、实际会在更深处判 Corrupted"的文档恰好是
        /// 正式文件，<see cref="ReadValidEnvelope"/> 仍然会在它身上"成功"一次并停止尝试任何备份——
        /// 即便存在完好可用的备份，整槽也会被判 <c>Corrupted</c> 而不是
        /// <see cref="LoadStatus.LoadedFromBackup"/>（外部审计 N15 两个复现场景）。现在直接复用
        /// <see cref="TryGetInt(JsonObject, string, out int)"/>/<see cref="TryGetSectionsMeta"/>
        /// 这两个 <see cref="Load"/> 实际使用的校验方法本身作为信封校验标准（不是再手写一份平行的、
        /// 容易再次悄悄漂移变浅的判断），确保"通过信封校验"与"<see cref="Load"/> 后续两处早期校验
        /// 一定能通过"这一保证不再依赖两处代码手工保持同步。本方法仍然不校验 <c>meta</c> 内部字段
        /// （<c>game_id</c> 等，见 <see cref="ParseMeta"/>）——那一层校验可能因迁移链而在不同版本间
        /// 有不同的必填字段形状，不适合在"选出哪个候选文档"这一步就假定当前版本的字段要求，留给
        /// <see cref="Load"/> 迁移完成后再校验，语义不变。
        /// </para>
        /// </summary>
        private static bool TryParseEnvelope(string text, out JsonObject doc)
        {
            try
            {
                var value = JsonReader.Parse(text);
                if (value is JsonObject obj &&
                    TryGetInt(obj, "save_version", out _) &&
                    TryGetSectionsMeta(obj, out _))
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

        /// <summary>
        /// C10 收口（外部审计 7e63d66 第四轮）：<see cref="TryParseEnvelope"/> 只校验信封顶层形状
        /// （<c>save_version</c> 是整数、<c>sections.meta</c> 是对象），不校验 meta 内部
        /// <see cref="ParseMeta"/> 实际要求的语义必填字段（<c>created_at</c>/<c>updated_at</c>/
        /// <c>game_id</c> 等）。此前 <see cref="ReadValidEnvelope"/> 只按 <see cref="TryParseEnvelope"/>
        /// 的结果选出候选并立即返回——若这份候选恰好是正式文件、顶层形状合法但 meta 语义字段缺失
        /// （例如 <c>{"save_version":1,"sections":{"meta":{}}}</c>），选择阶段会"成功"一次并不再
        /// 尝试任何备份，真正读取 meta 时才在 <see cref="Load"/> 更深处的 <see cref="ParseMeta"/>
        /// 失败判 <c>Corrupted</c>——即便存在完好可用的备份，也不会被尝试。
        /// <para>
        /// 让候选筛选阶段直接复用 <see cref="ParseMeta"/> 本身作为验收标准（不是再手写一份平行、
        /// 容易再次悄悄漂移变浅的语义校验），使"候选被选中"与"<see cref="Load"/> 后续对它的 meta
        /// 解析一定能成功"这一保证不再依赖两处代码手工同步——与 FND-07/N15 收口对
        /// <see cref="TryParseEnvelope"/> 本身采用的"复用而非平行手写"策略一致。
        /// </para>
        /// <para>
        /// 仅在候选顶层 <c>save_version</c> 已经等于当前运行时版本（<see cref="SaveSystemOptions.CurrentSaveVersion"/>，
        /// 即无需迁移）时才提前做这层语义校验；需要迁移的候选，其 <c>sections.meta</c> 在迁移完成
        /// 前的形状允许与当前版本的必填字段要求不同——这正是迁移链存在的意义，提前按当前版本的
        /// meta 形状去校验一份尚未迁移的旧文档并不恰当，维持原有行为，交给 <see cref="Load"/> 在
        /// 迁移完成之后再校验。
        /// </para>
        /// </summary>
        private bool IsCandidateMetaUsable(JsonObject doc, Id slotId)
        {
            if (!TryGetInt(doc, "save_version", out var docVersion) || docVersion != _options.CurrentSaveVersion)
            {
                return true;
            }

            if (!TryGetSectionsMeta(doc, out var metaObj))
            {
                return false;
            }

            try
            {
                ParseMeta(metaObj, slotId);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
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

        // ---- 路径拼装 ------------------------------------------------------

        private string SavesDir() => JoinPath(_fs.GetUserDataDir(), _options.SavesDirName);

        private string SlotPath(Id slotId) => JoinPath(SavesDir(), slotId.Value + ".json");

        /// <summary>
        /// FND-01 收口：备份不再落在存档目录的根层级（与合法槽文件同层、同 <c>.json</c> 后缀），
        /// 改放进独立子目录 <c>&lt;SavesDir&gt;/backups/</c>。此前 <c>&lt;slot&gt;.bakN.json</c>
        /// 与"槽 id 恰好长得像 <c>&lt;slot&gt;.bakN</c>"的合法槽正式文件同名同目录——例如槽
        /// <c>slot.a.bak1</c> 的正式文件路径与槽 <c>slot.a</c> 的第 1 份备份路径逐字节相同，
        /// 保存/删除其中一个会覆盖或删除另一个，<see cref="ListSlots"/>/<see cref="CountSlots"/>
        /// 还需要一个"这个文件名是不是备份"的启发式过滤（<c>IsBackupFileName</c>）来避免把备份
        /// 误列成槽，而这个过滤本身又会把"长得像备份"的合法槽误判成备份而从列表中隐藏。放进独立
        /// 子目录后两个问题一起解决：备份路径与任何合法槽路径不可能重合（一个是目录里的文件、一个
        /// 是目录本身，`slotId.Value` 不含 <c>/</c>，见 <c>Id</c> 格式约束），<see cref="_fs"/>.
        /// <c>ListFiles</c>（约定为递归列举、相对路径含 <c>/</c> 分隔层级，见
        /// <c>engine_adapter/README.md</c>"IFileSystem"一节）天然会把 <c>backups/&lt;...&gt;</c>
        /// 下的文件都归到"含 <c>/</c> 的更深层级"，<see cref="ListSlots"/>/<see cref="CountSlots"/>
        /// 现有的"跳过含 <c>/</c> 的相对路径"逻辑不需要改动就能正确排除全部备份，也不再需要、
        /// 也已移除按文件名猜测的过滤。
        /// </summary>
        private string BackupsDir() => JoinPath(SavesDir(), "backups");

        private string BackupPath(Id slotId, int index) =>
            JoinPath(BackupsDir(), slotId.Value + ".bak" + index.ToString(CultureInfo.InvariantCulture) + ".json");

        private static string JoinPath(string baseDir, string segment)
        {
            if (string.IsNullOrEmpty(baseDir))
            {
                return segment;
            }

            return baseDir.EndsWith("/", StringComparison.Ordinal) ? baseDir + segment : baseDir + "/" + segment;
        }

        // ---- N16：旧顶层备份布局兼容 -------------------------------------------

        /// <summary>
        /// N16 收边补齐（外部审计 68c9bed，P2）：FND-01 把备份从"与正式槽同目录、
        /// <c>&lt;slot&gt;.bakN.json</c>"迁到独立 <c>backups/</c> 子目录后，新代码只会向新路径
        /// （<see cref="BackupPath"/>）读写，<see cref="ReadValidEnvelope"/> 主档损坏/缺失时也只在
        /// 新路径下找备份——旧顶层布局遗留的备份文件（升级前产生、代码升级后从未被清理）从此彻底
        /// 找不到，即使内容完好也无法用于恢复。
        /// <para>
        /// 判断记录（为什么不做"扫描顶层目录、按文件名迁移/删除"）：<see cref="BackupPath"/> 类型
        /// 注释与 <c>SaveSystemTests.SlotIdLooksLikeBackupFileName_IsIndependentFromRealBackup_
        /// ListedLoadedAndDeletedCorrectly</c>（FND-01 收口回归测试，属"不放宽断言"范围）已经把
        /// "顶层目录里任何 <c>&lt;x&gt;.json</c> 文件都可能是一个货真价实、与任何备份无关的正式槽"
        /// 定为硬约束——槽 id 允许长得和 <c>&lt;slot&gt;.bakN</c> 一模一样。按文件名模式扫描顶层目录
        /// 并据此移动/删除文件，无法与"这就是一个真实正式槽"的情形区分，本任务早期实现过这种全量
        /// 扫描迁移，会在该回归测试里把真实槽 <c>slot.a.bak1</c> 的正式文件误判成 <c>slot.a</c> 的
        /// 旧备份并删除——这是真实的数据损坏风险，不是测试用例过严，因此放弃"迁移"（移动/删除旧
        /// 顶层文件）路线。
        /// </para>
        /// <para>
        /// 改为按需、只读、精确路径的兜底：只在 <see cref="ReadValidEnvelope"/> 已经确认某个具体
        /// <paramref name="slotId"/> 的正式文件与全部新布局备份（<see cref="BackupPath"/>）都不可用
        /// 之后，才去检查这个 slotId 派生出的精确旧路径 <c>&lt;SavesDir&gt;/&lt;slotId&gt;.bakN.json</c>
        /// （N 从 1 到 <see cref="SaveSystemOptions.BackupCount"/>）——不做任何目录扫描，只探测这
        /// 几个确定的路径，不可能命中"恰好同名的另一个真实槽"（那个槽会有自己独立的
        /// <c>slotId.Value</c>，不会与当前正在 Load 的这个 slotId 混淆；唯一的边界情形是这个精确
        /// 路径本身确实是另一个真实槽的正式文件——概率与 FND-01 已接受的固有命名歧义相同，见上一段，
        /// 本方法只读取，不删除、不移动，不会造成数据丢失，最坏情况是把内容误当作恢复来源，仍好于
        /// 直接判 Corrupted）。命中后顺手把内容原样复制一份到新布局路径（仅当新路径尚不存在同编号
        /// 备份时才写，不覆盖），使这份数据以后也能被正常的新布局备份轮转机制续用；旧顶层文件本身
        /// 不删除、不移动——<see cref="ListSlots"/>/<see cref="CountSlots"/>/<see cref="DeleteSlot"/>
        /// 的行为完全不受影响，与 FND-01 建立的既有语义保持一致。
        /// </para>
        /// </summary>
        private (JsonObject doc, LoadStatus status)? TryReadLegacyBackup(Id slotId, out bool anyLegacyCandidateExisted)
        {
            anyLegacyCandidateExisted = false;

            for (var i = 1; i <= _options.BackupCount; i++)
            {
                var legacyPath = JoinPath(SavesDir(), slotId.Value + ".bak" + i.ToString(CultureInfo.InvariantCulture) + ".json");
                var legacyText = _fs.ReadText(legacyPath);
                if (legacyText == null)
                {
                    continue;
                }

                if (!TryParseEnvelope(legacyText, out var legacyDoc))
                {
                    // 存在但无法解析的文件仍然是"这个精确路径上确实有过东西"的信号，计入候选存在，
                    // 让 Load 在没有任何其它候选时能判 Corrupted 而不是 NotFound。
                    anyLegacyCandidateExisted = true;
                    continue;
                }

                // C01 收口（外部审计 7e63d66 第四轮）：这个精确路径不仅可能是 slotId 真正的旧顶层
                // 备份，还可能恰好是另一个货真价实、id 长得像 "<slotId>.bakN" 的正式槽（例如请求
                // Load("slot.a") 时，"slot.a.bak1.json" 也可以是槽 "slot.a.bak1" 自己保存出来的
                // 正式文件——FND-01 判断记录已明确这种命名碰撞在 <see cref="BackupPath"/> 层面是
                // 允许的合法槽名）。此前只要这个路径下的内容能通过信封校验就无条件当作 slotId 的
                // 备份返回，会把另一个独立正式槽的存档跨槽"借"给 slotId，还会把它误写进 slotId 的
                // 新布局备份目录。现在核对信封内 meta.slot_id：显式声明了且与 slotId 不一致，说明
                // 这份内容属于别的槽，不是 slotId 的候选——既不当备份用，也不计入
                // anyLegacyCandidateExisted（它对 slotId 而言纯属路径命名巧合，与 slotId 是否存在
                // 无关，不能把它转化成 slotId 的 Corrupted 判定信号）。meta 未显式记录 slot_id 时
                // （早于 slot_id 字段引入的、真正意义上的旧存档）无法反证身份，按原语义放行，与
                // <see cref="ParseMeta"/> 对缺失 slot_id 时回退到请求槽名的宽松处理保持一致。
                if (!TryGetSectionsMeta(legacyDoc, out var metaObj) || !LegacyCandidateMatchesSlot(metaObj, slotId))
                {
                    continue;
                }

                anyLegacyCandidateExisted = true;

                if (!IsCandidateMetaUsable(legacyDoc, slotId))
                {
                    // C10 收口：身份核对通过，但 meta 语义必填字段（save_version/created_at 等）
                    // 缺失——与新布局候选一致，不能当作可用候选返回，继续尝试下一个编号。
                    continue;
                }

                _diagnostics.Warn(
                    $"存档槽 \"{slotId}\" 已从旧顶层布局备份 \"{legacyPath}\" 恢复读取（见 N16 收边补齐判断记录）");

                // 顺手续存到新布局，之后的 Save 备份轮转能接续使用；不覆盖已存在的新布局备份。
                var newPath = BackupPath(slotId, i);
                if (!_fs.Exists(newPath))
                {
                    _fs.WriteTextAtomic(newPath, legacyText);
                }

                return (legacyDoc, LoadStatus.LoadedFromBackup);
            }

            return null;
        }

        /// <summary>
        /// C01 收口辅助：核对旧顶层备份候选的信封内 <c>meta.slot_id</c> 与当前 <see cref="Load"/>
        /// 请求的槽名是否一致。<c>slot_id</c> 字段缺失（早于该字段引入的真正旧存档）视为无法反证，
        /// 放行；字段存在但类型非法或与请求槽名不同，判定为"属于另一个槽"，不匹配。
        /// </summary>
        private static bool LegacyCandidateMatchesSlot(JsonObject metaObj, Id slotId)
        {
            if (!metaObj.TryGetValue("slot_id", out var slotIdValue))
            {
                return true;
            }

            return slotIdValue is JsonString slotIdText &&
                   Id.TryParse(slotIdText.Value, out var parsedSlotId) &&
                   parsedSlotId == slotId;
        }
    }
}

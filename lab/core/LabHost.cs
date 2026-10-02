using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Sim;
using Presentation.Common;
using Presentation.ViewBinding;

namespace Lab
{
    /// <summary>无头实验室宿主的运行选项。</summary>
    public sealed class LabHostOptions
    {
        /// <summary>数据来源（框架根在前、实验室数据集在后，同 <c>HeadlessWorldOptions.DataSources</c>）。</summary>
        public IReadOnlyList<IDataSource> DataSources { get; set; } = Array.Empty<IDataSource>();

        public string PlayerClassId { get; set; } = "arch.class.lab_hero";

        public string PlayerFactionId { get; set; } = "fac.player";

        public string PlayerId { get; set; } = "unit.lab_player";

        public ulong Seed { get; set; } = 20261002UL;

        /// <summary>移动动作 id；宿主把它重绑到左摇杆以便注入任意模长的轴值（见 <see cref="LabHost"/> 判断记录）。</summary>
        public string MoveAction { get; set; } = "input.action.move";
    }

    /// <summary>
    /// 无头实验室宿主（06 第 2 节"无头宿主"）：用 <see cref="HeadlessWorldBuilder"/> 装出世界，再按引擎侧宿主引导代码
    /// 同一套逐步顺序驱动——桩输入 → 输入映射 → 移动请求/施放意图 → <c>Gameplay.Advance</c>——并经表现层公共部分的
    /// <see cref="ViewBinder"/> + 记录型假 View 采集表现时间线，经 Stopwatch/分配计数采集真实时间。
    /// <para>
    /// 判断记录（宿主逐步顺序）：照抄引擎侧宿主引导代码的固定步回调（<c>OnFixedStep</c>/<c>HandleFixedInput</c>）：
    /// ① <c>InputMap.Update(input)</c>；② 移动轴平方长 &gt; 0.0001 时每步重新提交一次方向移动请求（移动请求不持久，
    /// 须逐步重提）；③ 按钮上升沿提交一条 <c>cast</c> 意图（参数 <c>skill_id</c>，不带目标，由技能的目标链解析）；④
    /// <c>Gameplay.Advance(step)</c>。引擎侧的 <c>AppState</c>/节奏门判定在连续模式下恒通过，无头宿主不复刻。
    /// </para>
    /// <para>
    /// 判断记录（移动轴注入走左摇杆）：框架默认的移动绑定是键盘四向合成轴，只能产出 8 个单位向量，无法表达"小幅轴值"
    /// 这类标准脚本。宿主把移动动作重绑为 <c>pad_stick:left</c>，经 <see cref="StubInput.SetAxis"/> 注入任意模长的轴值；
    /// 输入映射对摇杆值不做归一化与死区处理（<c>InputMapHost.EvaluatePadStick</c>），归一化发生在
    /// <c>MovementTickHandler</c>——这正是实验室要度量的"当前行为"。
    /// </para>
    /// <para>
    /// 判断记录（空间索引位置同步）：<see cref="StubSpatialQuery"/> 的位置不会随实体移动自动更新（引擎侧由物理空间查询
    /// 适配器每个固定步同步）；宿主在每步 <c>Advance</c> 返回后把玩家位置写回空间索引，等价引擎侧"固定步后同步"。
    /// </para>
    /// <para>
    /// 判断记录（动作式格子）：宿主本身不区分动作式与目标选择式——两者的差异只在数据集与预设（ADR-0122 决策 4）。
    /// 既有脚本（meta 没有 <c>feel</c>）在两类格子上行为一致（基线不变）；手感场景脚本（meta <c>feel</c> 为真）经生产装配
    /// 开启手感系统：动作式格子用带 <c>timeline</c> 块的技能与 <c>arpg_responsive</c> 预设，目标选择式格子用同一批技能
    /// 剥掉 <c>timeline</c> 后与 <c>rpg_classic</c> 预设（见 <see cref="LabRunVariant"/>）。宿主不读取 <c>settlement</c> 之外的
    /// 呈现字段（<c>form</c>/<c>camera_mode</c>/<c>control_space</c>/<c>hit_shape</c>），只有 <c>facing</c> 决定表现时间线里
    /// 方向量化的档位。
    /// </para>
    /// <para>
    /// 判断记录（手感场景宿主顺序）：开手感时在既有逐步顺序上增加——脚本 <c>cast</c> 事件（靶子出手）在脚本事件处理里提交；
    /// 输入映射的按钮边沿经 <c>InputBufferHost.BindLocalInput</c> 进输入缓冲，由生产装配的 tick 步骤 1 处理器消费并施放
    /// （宿主不再自己提交施放意图）；每步末尾（事件派发之后）采缓冲槽与运动层状态并让反馈流水线兜底出批；
    /// 靶子（含 AI 巡逻靶）的位置每步同步进空间索引（引擎侧由物理空间查询适配器做）。可破坏障碍是真正的动态阻挡：
    /// 出场时在地形阻挡之外追加其占位矩形（半边长 0.5），被打死后经 <c>INavigation2D.RemoveBlocking</c> 增量移除，
    /// 阻挡版本号随之递增（06 第 10 节勘误 4 的收口）。动态阻挡由靶子数据声明（<c>block_half_extent</c>，<c>breakable</c> 缺省 0.5），
    /// 不限手感场景：基础靶子集里的可破坏障碍同样是真阻挡；阻挡变更另记一条 <c>blocking_changed</c> 逻辑事件。
    /// 靶子可选声明韧性（<c>poise</c>）：出场后写进韧性属性，受击裁决读它。
    /// </para>
    /// </summary>
    public static class LabHost
    {
        /// <summary>
        /// 跳跃输入动作（脚本 <c>press</c> 事件的动作 id）。宿主在该动作的按下沿向竖直运动服务提交跳跃（等价引擎侧宿主"跳跃键 → 跳跃请求"），
        /// 不经输入映射与输入缓冲（跳跃不是技能，不进缓冲槽，不污染输入缓冲度量）。世界没有竖直轴（<c>plane</c>）时请求被拒绝并计数，不静默吞掉。
        /// </summary>
        public const string JumpAction = "input.action.lab_jump";

        /// <summary>靶子的韧性值写进的属性 id（与受击裁决读取的缺省韧性属性一致，<c>HitFeelOptions.PoiseStat</c> 缺省值）。</summary>
        public const string PoiseStatId = "stat.poise";

        /// <summary>桩适配层在本宿主上提供的能力集合（空：桩没有自由视角、体积扫掠等能力）。</summary>
        public static IReadOnlyCollection<string> AvailableCapabilities { get; } = Array.Empty<string>();

        /// <summary>数据集内容哈希：对全部数据来源的全部表文本（换行归一）求 SHA-256 前 16 位十六进制。</summary>
        public static string ComputeDatasetHash(IReadOnlyList<IDataSource> sources)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var parts = new List<string>();
                for (var s = 0; s < sources.Count; s++)
                {
                    var tables = new List<DataTableSource>(sources[s].ListTables());
                    tables.Sort((a, b) =>
                    {
                        var c = string.CompareOrdinal(a.TableName, b.TableName);
                        return c != 0 ? c : string.CompareOrdinal(a.Location, b.Location);
                    });
                    foreach (var table in tables)
                    {
                        var text = table.ReadText();
                        parts.Add(s + ":" + table.TableName + "\n" + text.Replace("\r\n", "\n"));
                    }
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\u0001", parts));
                var hash = sha.ComputeHash(bytes);
                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < 8; i++)
                {
                    sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>装一个只为读数据（格子、地形、靶子集）的世界。</summary>
        public static HeadlessWorld BuildProbe(LabHostOptions options) =>
            HeadlessWorldBuilder.Build(CreateWorldOptions(options, new Id("world.lab_arena"), Vec2.Zero, 0.02, null));

        private static HeadlessWorldOptions CreateWorldOptions(
            LabHostOptions options, Id mapId, Vec2 start, double stepSeconds, StubNavigation2D? navigation,
            CarriersFeelOptions? feelOptions = null, MovementOptions? movementOptions = null,
            Core.Rules.Targeting.TargetingOptions? targetingOptions = null,
            Core.Rules.Skill.SkillOptions? skillOptions = null)
        {
            return new HeadlessWorldOptions
            {
                DataSources = options.DataSources,
                Seed = options.Seed,
                MapId = mapId,
                PlayerId = new Id(options.PlayerId),
                PlayerFactionId = new Id(options.PlayerFactionId),
                PlayerClassId = new Id(options.PlayerClassId),
                PlayerLevel = 1,
                PlayerSpawnPosition = start,
                GameId = new Id("game.lab"),
                StepSeconds = stepSeconds,
                EnableDiscreteTimeModel = true,
                Navigation = navigation,
                FeelOptions = feelOptions,
                MovementOptions = movementOptions,
                TargetingOptions = targetingOptions,
                SkillOptions = skillOptions,
            };
        }

        /// <summary>脚本声明了 <c>poiseImpactScale</c> 时的受击裁决选项：只填动态韧性的冲击等级倍率表，其余取缺省（手感落地 M4-W3）。</summary>
        private static Core.Rules.Combat.HitFeelOptions PoiseImpactHitFeel(ScriptMeta meta)
        {
            var table = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in meta.PoiseImpactScale)
            {
                table[pair.Key] = pair.Value;
            }

            return new Core.Rules.Combat.HitFeelOptions { PoiseDamageImpactMultipliers = table };
        }

        /// <summary>
        /// 手感场景用的标定行 id：脚本显式给了就用它；否则按预设短名取 <c>feel.calibration.lab_&lt;短名&gt;</c>
        /// （如 <c>feel.preset.arpg_responsive</c> → <c>feel.calibration.lab_arpg_responsive</c>，实验室动作式数据根里每个预设一行）。
        /// </summary>
        public static string CalibrationFor(ScriptMeta meta, string presetId)
        {
            if (meta.FeelCalibrationId.Length > 0)
            {
                return meta.FeelCalibrationId;
            }

            const string prefix = "feel.preset.";
            if (!presetId.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new LabFormatException($"脚本 {meta.ScriptId} 的预设 {presetId} 不是 {prefix}* 形式，无法推出标定行（请在 meta.feelCalibrationId 里指定）");
            }

            return "feel.calibration.lab_" + presetId.Substring(prefix.Length);
        }

        /// <summary>跑一份脚本在一个格子上的完整过程并返回三条时间线的记录。</summary>
        public static LabRecording Run(
            LabHostOptions options, LabScenario cell, InputScript script, LabCatalog? catalog = null, LabRunVariant? variant = null)
        {
            variant ??= LabRunVariant.Default;
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            if (script == null) throw new ArgumentNullException(nameof(script));

            var runnability = cell.CheckRunnable(AvailableCapabilities);
            if (!runnability.Runnable)
            {
                throw new LabCellNotRunnableException(runnability);
            }

            var meta = script.Meta;
            var step = 1.0 / meta.TickRate;
            var nav = new StubNavigation2D();

            // 先用探针世界读出格子关联的地形与靶子集（它们在数据里，格子才知道地图 id）。
            catalog ??= new LabCatalog(BuildProbe(options).Registry);
            // 脚本可覆盖格子缺省的地形（竖直轴能力包补完的地形脚本带数据高度场；平面格子同样用它，保证跨格子不变量的比较口径一致）。
            var spaceExt = meta.SpaceExt;
            var arena = catalog.GetArena(spaceExt != null && spaceExt.ArenaId.Length > 0 ? new Id(spaceExt.ArenaId) : cell.ArenaId);
            var dummySet = catalog.GetDummySet(meta.DummySetId.Length > 0 ? new Id(meta.DummySetId) : cell.DummySetId);

            // 手感场景：预设与标定；FeelOff 变体让同一脚本在旧路径上跑（不装手感系统）。
            var feelScene = meta.Feel;
            var feelOn = feelScene && !variant.FeelOff;
            var effectivePreset = variant.PresetId ?? (string.IsNullOrEmpty(meta.PresetId) ? cell.DefaultPreset : meta.PresetId);
            var calibrationId = feelScene ? CalibrationFor(meta, effectivePreset) : string.Empty;

            // 换装场景（meta.scene = equip）也经生产装配开启手感系统（换装链、武器优先的普攻映射、时间线协作者都取生产实例），
            // 但不走输入缓冲与反馈流水线（没有 FeelRecording，指纹里不出现手感条件度量组，既有换装基线不变）。
            var equipScene = string.Equals(meta.Scene, "equip", StringComparison.Ordinal);
            var feelOptions = feelOn
                ? new CarriersFeelOptions
                {
                    CalibrationId = calibrationId,
                    LocalMoveActionName = options.MoveAction,
                    HitFeel = meta.PoiseImpactScale.Count > 0 ? PoiseImpactHitFeel(meta) : null,
                }
                : equipScene
                    ? new CarriersFeelOptions
                    {
                        CalibrationId = meta.FeelCalibrationId.Length > 0 ? meta.FeelCalibrationId : null,
                        LocalMoveActionName = options.MoveAction,
                    }
                    : null;

            // 空间语义（06 第 10 节勘误 9）：plane 不装配任何空间能力（行为与引入前逐位一致）；side_2d/volume 装配竖直轴（重力下的跳跃/击飞/落地）
            // 并打开命中形状的高度窗口；volume 另外把"最近"改成含高度差的三维距离；side_2d 额外锁深度（输入的竖直分量不是深度）。
            var spaceModel = variant.SpaceOverride ?? cell.Space;
            var vertical = LabScenario.IsVerticalSpace(spaceModel);
            var depthLocked = string.Equals(spaceModel, "side_2d", StringComparison.Ordinal);
            MovementOptions? movementOptions = null;
            Core.Rules.Targeting.TargetingOptions? targetingOptions = null;
            Core.Rules.Skill.SkillOptions? skillOptions = null;
            var gravity = 0.0;
            var jumpHeight = 0.0;
            if (vertical)
            {
                var verticalOptions = new VerticalAxisOptions();
                if (cell.Gravity.HasValue)
                {
                    verticalOptions.Gravity = cell.Gravity.Value;
                }

                if (cell.JumpHeight.HasValue)
                {
                    verticalOptions.JumpHeight = cell.JumpHeight.Value;
                }

                if (spaceExt != null)
                {
                    // 竖直轴能力包补完（ADR-0130 追加决定）：核心层的可选能力，缺省全关；脚本声明了才打开。
                    verticalOptions.AirControl = spaceExt.AirControl;
                    verticalOptions.MaxAirJumps = spaceExt.MaxAirJumps;
                    verticalOptions.StepHeight = spaceExt.StepHeight;
                    if (spaceExt.Terrain)
                    {
                        verticalOptions.Terrain = new MapTerrainHeights(BuildProbe(options).Registry);
                    }

                    if (spaceExt.SpatialRange)
                    {
                        skillOptions = new Core.Rules.Skill.SkillOptions { SpatialRange = true };
                    }
                }

                gravity = verticalOptions.Gravity;
                jumpHeight = verticalOptions.JumpHeight;
                movementOptions = new MovementOptions { Vertical = verticalOptions };
                targetingOptions = new Core.Rules.Targeting.TargetingOptions
                {
                    VerticalHit = true,
                    SpatialDistance = string.Equals(spaceModel, "volume", StringComparison.Ordinal),
                };
            }

            var rects = new List<Rect>(arena.Blocks.Count);
            foreach (var block in arena.Blocks)
            {
                rects.Add(new Rect(block.Min, block.Max));
            }

            nav.SetBlocking(arena.MapId, rects);

            var dynamicBlocks = new List<KeyValuePair<Id, Rect>>();
            var world = HeadlessWorldBuilder.Build(CreateWorldOptions(
                options, arena.MapId, meta.PlayerStart, step, nav, feelOptions, movementOptions, targetingOptions, skillOptions));
            var playerId = world.Player.EntityId;
            var recording = new LabRecording(script, cell, step) { StartPosition = meta.PlayerStart };

            // 技能：按职业技能书的等级 1 全部学会（动作绑定到哪个技能由格子决定）。
            var classRecord = world.Registry.Get("arch.class", new Id(options.PlayerClassId))
                ?? throw new LabFormatException($"数据集里没有职业 {options.PlayerClassId}");
            if (classRecord.TryGetId("skill_book_ref", out var bookId))
            {
                world.Gameplay.Carriers.Rules.Skill.LearnFromBook(playerId, bookId, 1);
            }

            // 手感场景：学会槽位技能与额外技能，并把技能槽位绑到玩家（输入缓冲的出口按槽位查技能）。
            if (feelScene)
            {
                var skillHost = world.Gameplay.Carriers.Rules.Skill;
                foreach (var pair in meta.SkillSlots)
                {
                    skillHost.LearnSkill(playerId, new Id(pair.Value));
                    if (!world.Gameplay.Carriers.SkillBindings.Bind(playerId, pair.Key, new Id(pair.Value)))
                    {
                        throw new LabFormatException($"脚本 {meta.ScriptId} 的技能槽位绑定失败：{pair.Key} -> {pair.Value}");
                    }
                }

                foreach (var extra in meta.LearnSkills)
                {
                    skillHost.LearnSkill(playerId, new Id(extra));
                }
            }

            // 靶子：按脚本选择的分组出场，默认关闭 AI（保证可复现），保留 AI 的条目（巡逻靶）按数据声明。
            var labels = new Dictionary<Id, string> { { playerId, "player" } };
            var dummyUnits = new List<KeyValuePair<string, Id>>();
            var dummyByLabel = new Dictionary<string, Id>(StringComparer.Ordinal);
            var wanted = new HashSet<string>(meta.DummyGroups, StringComparer.Ordinal);

            // 空间记录：格子带竖直轴、脚本有跳跃事件或本次出场的靶子声明了出生高度时才建（否则为 null，既有脚本的指纹不出现 space 组）。
            var scriptHasJump = false;
            foreach (var scripted in script.Events)
            {
                if (string.Equals(scripted.Action, JumpAction, StringComparison.Ordinal))
                {
                    scriptHasJump = true;
                    break;
                }
            }

            var dummiesHaveHeight = false;
            foreach (var declared in dummySet.Entries)
            {
                if (declared.Height > 0.0 && wanted.Contains(declared.Group))
                {
                    dummiesHaveHeight = true;
                    break;
                }
            }

            SpaceRecording? space = null;
            if (vertical || scriptHasJump || dummiesHaveHeight)
            {
                space = new SpaceRecording(spaceModel, vertical, gravity, jumpHeight, depthLocked);
                recording.Space = space;
                if (vertical && spaceExt != null)
                {
                    space.Ext = new SpaceExtRecording(spaceExt);
                }
            }

            foreach (var dummy in dummySet.Entries)
            {
                if (!wanted.Contains(dummy.Group))
                {
                    continue;
                }

                var count = string.Equals(dummy.Kind, "swarm", StringComparison.Ordinal) ? dummy.Count : 1;
                for (var i = 0; i < count; i++)
                {
                    var pos = count > 1
                        ? new Vec2(dummy.Position.X + (i - (count - 1) / 2.0) * dummy.Spacing, dummy.Position.Y)
                        : dummy.Position;
                    var label = count > 1 ? $"{dummy.Name}#{i + 1}" : dummy.Name;
                    var id = world.Gameplay.Carriers.Creatures.Spawn(dummy.CreatureId, arena.MapId, pos, Math.PI, null, 1);
                    world.Spatial.Register(id, pos, 0.5);
                    if (!dummy.KeepAi)
                    {
                        var ai = world.Gameplay.Carriers.Rules.Ai;
                        foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
                        {
                            if (registered.Equals(id))
                            {
                                ai.UnregisterUnit(id);
                                break;
                            }
                        }
                    }

                    if (space != null)
                    {
                        space.DummyHeights[label] = new List<double>();
                        if (dummy.Height > 0.0)
                        {
                            space.DeclaredDummyHeights.Add(new KeyValuePair<string, double>(label, dummy.Height));
                            if (vertical && world.World.GetEntity(id) is Unit floating)
                            {
                                // 飘浮怪/悬空靶：静态出生高度，不受重力（只有被抛起的单位才进入竖直积分）。平面世界忽略。
                                floating.HeightOffset = dummy.Height;
                            }
                        }
                    }

                    labels[id] = label;
                    recording.Dummies.Add(new KeyValuePair<string, Vec2>(label, pos));
                    dummyUnits.Add(new KeyValuePair<string, Id>(label, id));
                    dummyByLabel[label] = id;
                    if (dummy.BlockHalfExtent is double half)
                    {
                        // 可破坏障碍（数据声明 block_half_extent，或 kind = breakable 取缺省）是动态阻挡：占位矩形随地形阻挡一起生效，
                        // 被打死后移除（见类型判断记录）；任何场景都生效，不限手感场景。
                        dynamicBlocks.Add(new KeyValuePair<Id, Rect>(
                            id, new Rect(new Vec2(pos.X - half, pos.Y - half), new Vec2(pos.X + half, pos.Y + half))));
                    }

                    if (dummy.Poise is double poise)
                    {
                        // 靶子声明的韧性值写进该单位的韧性属性（受击裁决读它；缺省不声明即韧性 0，行为与不支持韧性时一致）。
                        var stats = world.Gameplay.Carriers.Rules.Stats;
                        var poiseStat = new Id(PoiseStatId);
                        if (!stats.IsRegistered(id))
                        {
                            throw new LabFormatException($"靶子 {dummy.Name} 声明了 poise，但单位未注册属性（无法写韧性）");
                        }

                        try
                        {
                            stats.SetBase(id, poiseStat, poise);
                        }
                        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                        {
                            throw new LabFormatException(
                                $"靶子 {dummy.Name} 声明了 poise，但数据集里没有属性 {PoiseStatId} 的定义（{ex.Message}）；手感场景请叠加声明它的数据根（如 data/_lab_action）");
                        }
                    }
                }
            }

            // 可破坏障碍的占位矩形经增量接口逐块登记（INavigation2D.AddBlocking，M4-L；此前整批重发全部矩形）。
            foreach (var block in dynamicBlocks)
            {
                nav.AddBlocking(arena.MapId, block.Value);
            }

            // 空中姿势装置（脚本声明了合成姿势键表且格子带竖直轴时）。
            AirPoseRig? airPose = null;
            if (space?.Ext != null && spaceExt!.PoseKeys.Count > 0)
            {
                airPose = new AirPoseRig(
                    world.Bus, world.Gameplay.Carriers.VerticalMotion!, spaceExt, space.Ext, playerId,
                    id => labels.TryGetValue(id, out var l) ? l : id.Value);
            }

            // 输入：声明 found.input_action 全部动作，移动重绑到左摇杆。
            var input = new StubInput();
            var inputMap = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action"))
            {
                definitions.Add(ActionDefinition.FromRecord(record));
            }

            inputMap.DeclareActionSet(new Id("actionset.lab_input_action"), definitions);
            if (!inputMap.Rebind(options.MoveAction, "pad_stick:left"))
            {
                throw new LabFormatException($"移动动作 {options.MoveAction} 无法重绑到左摇杆");
            }

            // 手感场景：本地输入的按钮边沿接给输入缓冲（与 PresentationAssembly 的接线同一个调用）。
            if (feelOn)
            {
                world.Gameplay.Feel?.InputBuffer.BindLocalInput(inputMap, playerId, options.MoveAction);
            }

            // 表现：ViewBinder + 记录型假 View，只关心玩家那一个 View 的位姿。
            var directionCount = string.Equals(cell.Facing, "flip", StringComparison.Ordinal) ? 2 : 8;
            var factory = new RecordingViewFactory();
            var displayInfo = new DisplayInfoRegistry(world.Registry, world.Bus);
            var binder = new ViewBinder(
                world.Bus, factory, new WorldSimSnapshot(world.World), displayInfo,
                new ViewBinderOptions(null, directionCount));
            binder.OnEntityCreated(playerId, world.Player.Kind, world.Player.TemplateId ?? playerId);

            // 换装场景（脚本 meta.scene = equip）：装出换装链的全部生产部件，脚本里的 equip/unequip 事件经它执行。
            EquipRig? rig = null;
            if (equipScene)
            {
                rig = EquipRig.Create(world, meta, step, recording, displayInfo);
            }
            else if (meta.Scene.Length > 0)
            {
                throw new LabFormatException($"脚本 {meta.ScriptId} 的 scene 未知：{meta.Scene}（目前只有 equip）");
            }

            // 脚本事件按 tick 分桶；同 tick 内保持脚本里的先后顺序。
            var byTick = new Dictionary<int, List<ScriptEvent>>();
            foreach (var e in script.Events)
            {
                if (!byTick.TryGetValue(e.Tick, out var list))
                {
                    list = new List<ScriptEvent>();
                    byTick[e.Tick] = list;
                }

                list.Add(e);
            }

            var bindings = new List<KeyValuePair<string, Id>>(cell.SkillBindings);
            if (feelScene)
            {
                // 手感场景的施放走输入缓冲（开）或由按钮边沿直接提交施放意图（FeelOff 变体，槽位技能绑定到声明了槽位的动作）。
                bindings.Clear();
                if (variant.FeelOff)
                {
                    foreach (var definition in definitions)
                    {
                        if (definition.SkillSlot == null)
                        {
                            continue;
                        }

                        foreach (var slot in meta.SkillSlots)
                        {
                            if (string.Equals(slot.Key, definition.SkillSlot, StringComparison.Ordinal))
                            {
                                bindings.Add(new KeyValuePair<string, Id>(definition.ActionId.Value, new Id(slot.Value)));
                            }
                        }
                    }
                }
            }

            FeelRig? feelRig = null;
            Func<Id?, int>? ordinalOf = null;
            if (feelOn)
            {
                recording.Feel = new FeelRecording { Preset = effectivePreset, CalibrationId = calibrationId, Assembled = true };
            }

            var wasActive = new Dictionary<string, bool>(StringComparer.Ordinal);
            var instanceOrdinals = new Dictionary<Id, int>();
            if (feelOn)
            {
                ordinalOf = id =>
                {
                    if (!id.HasValue)
                    {
                        return 0;
                    }

                    if (!instanceOrdinals.TryGetValue(id.Value, out var n))
                    {
                        n = instanceOrdinals.Count + 1;
                        instanceOrdinals[id.Value] = n;
                    }

                    return n;
                };
                feelRig = new FeelRig(world, recording.Feel!, labels, ordinalOf, step, dummyUnits);
            }

            var eventCursor = 0;
            var tick = 0;
            var duration = meta.DurationTicks;
            var frameDt = 1.0 / meta.FrameRateCap;
            var frame = 0;

            void ApplyScriptEvent(ScriptEvent e)
            {
                if (string.Equals(e.Action, JumpAction, StringComparison.Ordinal))
                {
                    // 跳跃：宿主级请求，不经输入映射/缓冲（见 JumpAction）。只有按下沿算请求；抬起忽略。
                    if (e.Kind == ScriptEventKind.Press)
                    {
                        space!.JumpRequests++;
                        var wasAirborne = vertical && world.Gameplay.Carriers.VerticalMotion!.IsAirborne(playerId);
                        if (vertical && world.Gameplay.Carriers.VerticalMotion!.Jump(playerId))
                        {
                            space.JumpStartTicks.Add(tick);
                            if (wasAirborne && space.Ext != null)
                            {
                                space.Ext.AirJumpStarts++;
                            }
                        }
                        else if (wasAirborne && space.Ext != null)
                        {
                            space.Ext.AirJumpRefusals++;
                        }
                    }

                    return;
                }

                recording.InjectedInputs.Add(e);
                if (e.Kind == ScriptEventKind.Equip || e.Kind == ScriptEventKind.Unequip)
                {
                    if (rig == null)
                    {
                        throw new LabFormatException($"脚本事件 {e.Kind} 只能用在换装场景（meta.scene = equip）");
                    }

                    if (e.Kind == ScriptEventKind.Equip)
                    {
                        rig.Equip(tick, e.Action);
                    }
                    else
                    {
                        rig.Unequip(tick, e.Action);
                    }

                    return;
                }

                if (e.Kind == ScriptEventKind.ClearProjectiles)
                {
                    // 清场（地图切换/场景重置的投射物收尾）：先让簿记以 Cleared 结局通知钩子，再把世界里的投射物实体清掉。
                    world.Gameplay.Carriers.Projectiles.ClearAll();
                    foreach (var entity in world.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile)))
                    {
                        world.World.MarkForDestruction(entity.EntityId);
                    }

                    return;
                }

                if (e.Kind == ScriptEventKind.Cast)
                {
                    if (!dummyByLabel.TryGetValue(e.Actor, out var caster))
                    {
                        throw new LabFormatException($"脚本 cast 事件的行动者 {e.Actor} 不在本次出场的靶子里");
                    }

                    var casterSkill = new Id(e.Action);
                    var skillHost = world.Gameplay.Carriers.Rules.Skill;
                    skillHost.LearnSkill(caster, casterSkill);
                    skillHost.CastSkill(caster, casterSkill, Array.Empty<Id>());
                    return;
                }

                var first = FirstBinding(inputMap, e.Action);
                switch (e.Kind)
                {
                    case ScriptEventKind.Axis:
                        if (!first.StartsWith("pad_stick:", StringComparison.Ordinal))
                        {
                            throw new LabFormatException($"脚本轴事件的动作 {e.Action} 没有摇杆绑定（当前首绑定 {first}）");
                        }

                        var stick = first.Substring("pad_stick:".Length);
                        input.SetAxis(0, stick + "x", e.Value.X);
                        // 横版二维：控制空间把摇杆竖直分量留给"向上/向下"，不是深度——深度轴被锁死，丢掉该分量并计数（不静默）。
                        if (depthLocked && Math.Abs(e.Value.Y) > 0.0)
                        {
                            space!.DepthInputsDropped++;
                        }

                        input.SetAxis(0, stick + "y", depthLocked ? 0.0 : e.Value.Y);
                        break;
                    case ScriptEventKind.Press:
                        input.Press(KeyOf(first, e.Action));
                        break;
                    case ScriptEventKind.Release:
                        input.Release(KeyOf(first, e.Action));
                        break;
                }
            }

            void OnFixedStep(double stepSeconds)
            {
                if (tick >= duration)
                {
                    return;
                }

                feelRig?.BeginTick(tick);
                if (byTick.TryGetValue(tick, out var events))
                {
                    foreach (var e in events)
                    {
                        ApplyScriptEvent(e);
                    }
                }

                inputMap.Update(input);

                var axis = inputMap.GetActionAxis(options.MoveAction);
                var moveRequested = axis.SqrLength > 0.0001;
                if (moveRequested)
                {
                    world.Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(playerId, axis));
                }

                foreach (var binding in bindings)
                {
                    var active = inputMap.IsActionActive(binding.Key);
                    var before = wasActive.TryGetValue(binding.Key, out var w) && w;
                    wasActive[binding.Key] = active;
                    if (active && !before)
                    {
                        // 换装场景里普攻动作不走格子的固定绑定，而是按主手武器的 auto_attack_timeline_ref（空手回落空手普攻）解析。
                        var castSkill = binding.Value;
                        if (rig != null && string.Equals(binding.Key, "input.action.attack", StringComparison.Ordinal)
                            && !rig.TryResolveAttackSkill(binding.Key, tick, out castSkill))
                        {
                            continue;
                        }

                        var args = new JsonObjectBuilder().Add("skill_id", new JsonString(castSkill.Value)).Build();
                        world.World.SubmitIntent(new Intent(playerId, "cast", args));
                        recording.Intents.Add(new CastIntentRecord(tick, binding.Key, castSkill.Value));
                    }
                }

                world.Gameplay.Advance(stepSeconds);
                world.Spatial.UpdatePosition(playerId, world.Player.Position);
                if (feelScene)
                {
                    // 靶子（含 AI 巡逻靶、被击退的靶子）的位置同步进空间索引，等价引擎侧"固定步后同步"。
                    foreach (var dummyUnit in dummyUnits)
                    {
                        if (world.World.GetEntity(dummyUnit.Value) is Unit moved && moved.Alive)
                        {
                            world.Spatial.UpdatePosition(dummyUnit.Value, moved.Position);
                        }
                    }
                }

                if (space != null)
                {
                    space.PlayerHeights.Add(world.Player.HeightOffset);
                    space.PlayerDepths.Add(world.Player.Position.Y);
                    foreach (var pair in dummyUnits)
                    {
                        space.DummyHeights[pair.Key].Add(world.World.GetEntity(pair.Value) is Unit sampled ? sampled.HeightOffset : 0.0);
                    }

                    if (space.Ext != null)
                    {
                        var motion = world.Gameplay.Carriers.VerticalMotion!;
                        space.Ext.PlayerAirborne.Add(motion.IsAirborne(playerId));
                        space.Ext.PlayerVerticalSpeeds.Add(motion.GetVerticalSpeed(playerId));
                        space.Ext.PlayerMoveRequested.Add(moveRequested);
                    }
                }

                recording.Ticks.Add(new TickSample(
                    tick, world.Player.Position, world.Player.Facing, world.Player.MovementState.Mode.ToString(), world.Player.Alive,
                    moveRequested ? axis : Vec2.Zero, moveRequested));

                while (eventCursor < world.Events.Count)
                {
                    var dispatched = world.Events[eventCursor++];
                    rig?.OnEvent(dispatched, tick);
                    airPose?.OnEvent(dispatched, tick);
                    feelRig?.OnEvent(dispatched, tick);
                    RecordEvent(dispatched, tick, labels, instanceOrdinals, recording);
                    if (dispatched is UnitDiedEvent died && dynamicBlocks.Count > 0)
                    {
                        for (var b = 0; b < dynamicBlocks.Count; b++)
                        {
                            if (dynamicBlocks[b].Key.Equals(died.UnitId))
                            {
                                var removedRect = dynamicBlocks[b].Value;
                                dynamicBlocks.RemoveAt(b);
                                // 增量移除：只拿掉这一块，其余登记原样保留（INavigation2D.RemoveBlocking，M4-L）。
                                if (!nav.RemoveBlocking(arena.MapId, removedRect))
                                {
                                    throw new InvalidOperationException($"可破坏障碍 {labels[died.UnitId]} 的阻挡矩形不在导航登记里，增量移除失败");
                                }

                                var remainingCount = rects.Count + dynamicBlocks.Count;
                                var blockingVersion = nav.GetBlockingVersion(arena.MapId);
                                recording.Events.Add(new LogicEventRecord(
                                    tick, "blocking_changed", string.Empty, labels[died.UnitId], string.Empty, 0, remainingCount,
                                    "v" + blockingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                                feelRig?.NoteBlockingChanged(tick, labels[died.UnitId], remainingCount, blockingVersion);
                                break;
                            }
                        }
                    }
                }

                rig?.EndTick();
                airPose?.EndTick(tick, dummyByLabel.Values);
                feelRig?.EndTick(tick);

                tick++;
            }

            void OnFrame(double dt)
            {
                var alpha = world.Gameplay.InterpolationAlpha;
                binder.SyncAll(alpha < 0 ? 0 : alpha > 1 ? 1 : alpha);
                var view = factory.PlayerView;
                var continuous = string.Equals(cell.Facing, "continuous", StringComparison.Ordinal);
                recording.Frames.Add(view == null || !view.HasPose
                    ? new FrameSample(frame, frame * frameDt, tick, alpha, false, Vec2.Zero, 0, 0, 0)
                    : new FrameSample(
                        frame, frame * frameDt, tick, alpha, true, view.Position, view.Facing.RawRadians,
                        continuous ? 0 : view.Facing.Index, continuous ? 0 : view.Facing.DirectionCount));
                frame++;
            }

            var clock = new StubClock();
            clock.RequestFixedStep(step, OnFixedStep);
            clock.OnFrame(OnFrame);

            // 真实时间采样：每次帧推进（含其中触发的全部固定步与表现同步）一个样本。
            var watch = new Stopwatch();
            var guard = 0;
            var maxFrames = (int)Math.Ceiling(duration * step / frameDt) + 10 + duration;
            while (tick < duration)
            {
                if (++guard > maxFrames)
                {
                    throw new InvalidOperationException("实验室宿主在预期帧数内没有推进完全部固定步（时钟累加异常）");
                }

                var allocBefore = GC.GetAllocatedBytesForCurrentThread();
                watch.Restart();
                clock.Advance(frameDt);
                watch.Stop();
                recording.Real.FrameMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                recording.Real.FrameAllocatedBytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocBefore);
            }

            recording.TotalEventCount = world.Events.Count;
            feelRig?.Dispose();
            airPose?.Dispose();
            rig?.Dispose();
            binder.Dispose();
            return recording;
        }

        private static string FirstBinding(InputMapHost inputMap, string action)
        {
            var bindings = inputMap.GetBindings(action);
            if (bindings.Count == 0)
            {
                throw new LabFormatException($"脚本引用的动作 {action} 没有任何绑定（动作未声明？）");
            }

            return bindings[0];
        }

        private static string KeyOf(string firstBinding, string action)
        {
            if (!firstBinding.StartsWith("key:", StringComparison.Ordinal))
            {
                throw new LabFormatException($"脚本按钮事件的动作 {action} 首绑定不是键盘键：{firstBinding}");
            }

            return firstBinding.Substring("key:".Length);
        }

        private static void RecordEvent(
            IEvent evt, int tick, Dictionary<Id, string> labels, Dictionary<Id, int> instanceOrdinals, LabRecording recording)
        {
            string Label(Id id) => labels.TryGetValue(id, out var l) ? l : id.Value;
            int Ordinal(Id? id)
            {
                if (!id.HasValue)
                {
                    return 0;
                }

                if (!instanceOrdinals.TryGetValue(id.Value, out var n))
                {
                    n = instanceOrdinals.Count + 1;
                    instanceOrdinals[id.Value] = n;
                }

                return n;
            }

            switch (evt)
            {
                case SkillCastSuccessEvent s:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "cast_success", Label(s.CasterId), s.Targets.Count > 0 ? Label(s.Targets[0]) : string.Empty,
                        s.SkillId.Value, Ordinal(s.CastInstanceId), s.Targets.Count, s.IsInstant ? "instant" : "timed"));
                    break;
                case SkillCastFailedEvent f:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "cast_failed", Label(f.CasterId), string.Empty, f.SkillId.Value, Ordinal(f.CastInstanceId), 0,
                        f.ReasonCode.ToString()));
                    break;
                case CombatDamageDealtEvent d:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "damage", Label(d.SourceId), Label(d.TargetId), d.SkillId?.Value ?? string.Empty,
                        Ordinal(d.AttackInstanceId), d.Amount, d.HitResult.ToString()));
                    break;
                case CombatAttackAvoidedEvent a:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "avoided", Label(a.SourceId), Label(a.TargetId), a.SkillId?.Value ?? string.Empty,
                        Ordinal(a.AttackInstanceId), 0, a.HitResult.ToString()));
                    break;
                case UnitDiedEvent u:
                    recording.Events.Add(new LogicEventRecord(
                        tick, "died", u.KillerId.HasValue ? Label(u.KillerId.Value) : string.Empty, Label(u.UnitId), string.Empty, 0, 0,
                        string.Empty));
                    break;
            }
        }

        private sealed class RecordingView : IView
        {
            public Id EntityId { get; private set; }

            public bool IsAlive { get; private set; }

            public bool HasPose { get; private set; }

            public Vec2 Position { get; private set; }

            public Direction Facing { get; private set; }

            public void Bind(Id entityId)
            {
                EntityId = entityId;
                IsAlive = true;
            }

            public void OnEvent(IEvent evt)
            {
            }

            public void SyncPose(Vec2 pos, Direction facing, double height)
            {
                HasPose = true;
                Position = pos;
                Facing = facing;
            }

            public void Destroy()
            {
                IsAlive = false;
            }
        }

        private sealed class RecordingViewFactory : IViewFactory
        {
            private readonly Dictionary<Id, RecordingView> _views = new Dictionary<Id, RecordingView>();

            public RecordingView? PlayerView { get; private set; }

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                var view = new RecordingView();
                _views[entityId] = view;
                if (kind == ViewKind.Unit && PlayerView == null)
                {
                    // 第一个被创建的单位视图是玩家（宿主在靶子出场之前手动创建了玩家视图）。
                    PlayerView = view;
                }

                return view;
            }
        }
    }

    /// <summary>格子在当前适配层上不可运行（预留空间模型或缺能力）：显式抛出，不静默跳过。</summary>
    public sealed class LabCellNotRunnableException : Exception
    {
        public CellRunnability Runnability { get; }

        public LabCellNotRunnableException(CellRunnability runnability)
            : base(runnability.ToString())
        {
            Runnability = runnability;
        }
    }
}

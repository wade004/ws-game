using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Lab
{
    /// <summary>
    /// 脚本事件种类：按下、松开、轴值（轴值每个事件设一次并保持，直到下一个同动作的轴事件）；
    /// 换装场景（<see cref="ScriptMeta.Scene"/> = <c>equip</c>，格式版本 2）另有穿上物品与卸下槽位：
    /// <see cref="Equip"/> 的 <see cref="ScriptEvent.Action"/> 是物品模板 id，<see cref="Unequip"/> 的是装备槽位 id；
    /// 手感场景（<see cref="ScriptMeta.Feel"/>，格式版本 3）另有 <see cref="Cast"/>：<see cref="ScriptEvent.Actor"/> 指明的靶子
    /// （出场标签）施放 <see cref="ScriptEvent.Action"/> 技能（精英挥击等"靶子出手"）；<see cref="ClearProjectiles"/>（同为格式版本 3）
    /// 是"清场"：所有在飞的投射物以 Cleared 结局收场（<see cref="ScriptEvent.Action"/> 只作标签，惯例写 <c>projectiles</c>）。
    /// </summary>
    public enum ScriptEventKind
    {
        Press,
        Release,
        Axis,
        Equip,
        Unequip,
        Cast,
        ClearProjectiles,
    }

    /// <summary>
    /// 输入脚本里的一条事件（06 第 2 节"输入脚本：meta + 按 tick 的输入事件"）。
    /// <para><see cref="Tick"/> 是宿主固定步序号（从 0 开始）：该事件在第 <see cref="Tick"/> 步的"意图采集"之前注入
    /// 桩输入，与 tick 步骤 1（意图采集，03 第 3 节）同源——脚本事件走的是引擎侧宿主引导代码读取输入的同一条路径，
    /// 不绕开输入映射直接塞意图。<see cref="RealTimestamp"/> 只在回放导入（<see cref="ScriptReplayImporter"/>）时携带原始
    /// 真实时间戳（秒），注入时忽略，仅供人读与往返对照。</para>
    /// </summary>
    public sealed class ScriptEvent
    {
        public int Tick { get; }

        public string Action { get; }

        public ScriptEventKind Kind { get; }

        public Vec2 Value { get; }

        public double? RealTimestamp { get; }

        /// <summary>事件的行动者：空表示玩家（所有既有事件）；<see cref="ScriptEventKind.Cast"/> 里是靶子的出场标签。</summary>
        public string Actor { get; }

        public ScriptEvent(int tick, string action, ScriptEventKind kind, Vec2 value = default, double? realTimestamp = null, string actor = "")
        {
            if (tick < 0)
            {
                throw new LabFormatException($"脚本事件 tick 不能为负：{tick}");
            }

            Tick = tick;
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Kind = kind;
            Value = value;
            RealTimestamp = realTimestamp;
            Actor = actor ?? string.Empty;
        }
    }

    /// <summary>
    /// 输入脚本的 meta 段（06 第 2 节）：数据集根、预设 id、校准版本、tick 率、帧率上限、脚本版本，加上无头宿主
    /// 跑起来必需的扩展字段（脚本 id、玩家出生点、出场靶子分组、时长）。
    /// </summary>
    public sealed class ScriptMeta
    {
        public string ScriptId { get; set; } = string.Empty;

        /// <summary>脚本格式与内容版本；脚本内容改动时人工递增，基线键里携带它，防止拿旧基线比新脚本。</summary>
        public int ScriptVersion { get; set; } = 1;

        public string Description { get; set; } = string.Empty;

        /// <summary>数据集根（人读，仅作记录；实际数据根由运行入口给定）。</summary>
        public string DatasetRoot { get; set; } = "data/_lab";

        /// <summary>手感预设 id；空表示取格子的缺省预设。手感数据域落地前仅作标签。</summary>
        public string PresetId { get; set; } = string.Empty;

        public string CalibrationVersion { get; set; } = "none";

        /// <summary>摆姿集 id（手感设计 06 第 3.2 节假人姿态集）；本切片尚无摆姿机制，取 "none"。</summary>
        public string PoseSet { get; set; } = "none";

        public int TickRate { get; set; } = 50;

        public int FrameRateCap { get; set; } = 60;

        public int DurationTicks { get; set; } = 100;

        public Vec2 PlayerStart { get; set; } = Vec2.Zero;

        /// <summary>本次出场的靶子分组标签（<c>lab.dummy_set</c> 条目的 <c>group</c>）。</summary>
        public List<string> DummyGroups { get; } = new List<string>();

        /// <summary>
        /// 场景类型（格式版本 2）：空 = 既有的移动/攻击场景；<c>equip</c> = 换装场景（装出手感解析器、换装链、
        /// 姿势选择器与普攻映射，脚本里可用 <c>equip</c>/<c>unequip</c> 事件）。
        /// </summary>
        public string Scene { get; set; } = string.Empty;

        /// <summary>
        /// 在运行入口给定的数据集之外，本脚本额外叠加的数据根（格式版本 2，相对路径按运行入口的解析器解析）。
        /// 换装场景要叠加框架手感档案与占位装备集（<c>data/_feel</c>、<c>data/_equip</c>），既有脚本的数据集不受影响。
        /// </summary>
        public List<string> ExtraDataRoots { get; } = new List<string>();

        /// <summary>额外数据根里要整表剔除的表名（它们与基础数据集主键重复，例如两个根各自带一行同主键的本地化语言行）。</summary>
        public List<string> ExtraDataExcludeTables { get; } = new List<string>();

        /// <summary>
        /// 额外数据根里要剔除的单行（格式版本 2）：<c>表名/字段名=字段值</c>，例如 <c>l10n.text/key=l10n.power.health.name</c>
        /// 剔除该表里 <c>key</c> 字段等于该值的行（它们与基础数据集主键重复，且只重复个别行，不值得整表剔除）。
        /// </summary>
        public List<string> ExtraDataExcludeRows { get; } = new List<string>();

        /// <summary>换装场景用的手感标定行 id（<c>feel.calibration.*</c>）；数据里有多行标定时必须指定。</summary>
        public string FeelCalibrationId { get; set; } = string.Empty;

        /// <summary>
        /// 空手（主手没有武器行或武器行没声明普攻时间线）时的普通攻击技能 id（格式版本 2）：宿主把它绑到普攻输入动作声明的技能槽位
        /// （<c>found.input_action.skill_slot</c>），生产装配的武器优先映射在武器没有声明 <c>auto_attack_timeline_ref</c> 时回落到这个槽位绑定。
        /// 武器 → 普攻时间线的映射本身是数据（<c>feel.weapon.auto_attack_timeline_ref</c>），脚本不再声明。
        /// </summary>
        public string UnarmedAttackSkill { get; set; } = string.Empty;

        /// <summary>
        /// 手感场景开关（格式版本 3）：为真时宿主经<b>生产装配</b>开启手感系统（<c>HeadlessWorldOptions.FeelOptions</c>），
        /// 输入经输入缓冲与技能槽位绑定施放，并装出打击反馈流水线（假 sink）采表现时间线；为假（缺省）时一切与既有行为逐位一致。
        /// 预设取 <see cref="PresetId"/>（空取格子缺省），标定取 <see cref="FeelCalibrationId"/>（空按预设在数据里找
        /// <c>feel.calibration.lab_&lt;预设短名&gt;</c>）。
        /// </summary>
        public bool Feel { get; set; }

        /// <summary>技能槽位 → 技能 id（手感场景，格式版本 3）：宿主按它调用 <c>SkillBindings.Bind</c>，并让玩家学会这些技能。保持声明顺序。</summary>
        public List<KeyValuePair<string, string>> SkillSlots { get; } = new List<KeyValuePair<string, string>>();

        /// <summary>除槽位技能外玩家还要学会的技能（连招的后续段等，手感场景，格式版本 3）。</summary>
        public List<string> LearnSkills { get; } = new List<string>();

        /// <summary>覆盖格子缺省的靶子集 id（<c>lab.dummy_set</c>，手感场景，格式版本 3）；空取格子的靶子集。</summary>
        public string DummySetId { get; set; } = string.Empty;

        /// <summary>
        /// 竖直轴能力包补完的运行选项（<c>spaceExt</c> 块，格式版本 3；空间语义脚本用，缺省 null = 全部取 1.95.0 的缺省行为）。
        /// 只对装配竖直轴的格子生效（<c>side_2d</c>/<c>volume</c>）；平面格子忽略它，因此跨格子不变量不受影响。
        /// </summary>
        public ScriptSpaceOptions? SpaceExt { get; set; }

        /// <summary>
        /// 动态韧性伤害的冲击等级倍率表（<c>poiseImpactScale</c>，手感落地 M4-W3，格式版本 3；缺省空 = 不缩放）：
        /// 经 <c>HitFeelOptions.PoiseDamageImpactMultipliers</c> 传给手感装配，核心层的可选能力，实验室只是按脚本声明打开它。
        /// </summary>
        public List<KeyValuePair<string, double>> PoiseImpactScale { get; } = new List<KeyValuePair<string, double>>();

        /// <summary>是否用到了手感场景的格式版本 3 字段。</summary>
        public bool UsesFeelFormat =>
            Feel || SkillSlots.Count > 0 || LearnSkills.Count > 0 || DummySetId.Length > 0 || SpaceExt != null || PoiseImpactScale.Count > 0;

        /// <summary>是否用到了格式版本 2 的字段（决定序列化时写的 <c>formatVersion</c>）。</summary>
        public bool UsesExtendedFormat =>
            Scene.Length > 0 || ExtraDataRoots.Count > 0 || ExtraDataExcludeTables.Count > 0 || ExtraDataExcludeRows.Count > 0
            || FeelCalibrationId.Length > 0
            || UnarmedAttackSkill.Length > 0;
    }

    /// <summary>
    /// 脚本的竖直轴能力包补完选项（<c>meta.spaceExt</c>，ADR-0130 追加决定）：这些都是核心层的可选能力，实验室只是按脚本声明打开它们，
    /// 不自建平行机制。击飞叠加（<c>launch_stack</c>/<c>launch_stack_cap</c>）、空中受击反应（<c>air_hit_reaction</c>）、目标链形状竖直偏移
    /// （<c>height_offset</c>）本来就在数据里（预设/目标链），不需要宿主选项；这里只放必须由宿主装配期传入的东西。
    /// </summary>
    public sealed class ScriptSpaceOptions
    {
        /// <summary>空中水平控制比例（<c>VerticalAxisOptions.AirControl</c>）；null = 不限制（1.95.0 行为）。</summary>
        public double? AirControl { get; set; }

        /// <summary>空中跳跃次数上限（<c>VerticalAxisOptions.MaxAirJumps</c>）；null = 沿用 <c>AllowAirJump</c>。</summary>
        public int? MaxAirJumps { get; set; }

        /// <summary>台阶高度（<c>VerticalAxisOptions.StepHeight</c>）；null = 不做台阶阻挡。</summary>
        public double? StepHeight { get; set; }

        /// <summary>是否装配场景数据的高度场（<c>world.map.terrain</c>，经 <c>MapTerrainHeights</c> 读取）。</summary>
        public bool Terrain { get; set; }

        /// <summary>施法射程是否取三维距离（<c>SkillOptions.SpatialRange</c>）。</summary>
        public bool SpatialRange { get; set; }

        /// <summary>覆盖格子缺省的地形 id（<c>lab.arena</c>）；空取格子的地形。</summary>
        public string ArenaId { get; set; } = string.Empty;

        /// <summary>
        /// 合成姿势集的键表（非空时宿主装出空中姿势装置：姿势选择器 + 空中阶段喂入器，并按固定回落链解析 <c>jump.*</c>/<c>hit.air</c>/<c>attack.air</c>
        /// 请求）；空 = 不装。
        /// </summary>
        public List<string> PoseKeys { get; } = new List<string>();

        /// <summary>合成姿势里玩家的武器族（空 = 不指定）。</summary>
        public string PoseFamily { get; set; } = string.Empty;
    }

    /// <summary>
    /// 一份输入脚本：meta + 事件清单。可手写，也可由回放导入（<see cref="ScriptReplayImporter"/>）从带真实时间戳的输入
    /// 记录生成；序列化为规范 JSON（键序固定），因此同一脚本的文本永远相同。
    /// </summary>
    public sealed class InputScript
    {
        /// <summary>基础文件格式版本（既有脚本一律是它，序列化也保持写它）；格式不兼容变更时递增，读取方拒绝未知的更高版本。</summary>
        public const int FormatVersion = 1;

        /// <summary>
        /// 换装场景扩展格式版本：只加不改——新增 meta 字段与 <c>equip</c>/<c>unequip</c> 事件。只有脚本真的用到扩展字段
        /// （<see cref="ScriptMeta.UsesExtendedFormat"/> 或含换装事件）才写这个版本，其余脚本的序列化文本与扩展之前逐字相同。
        /// </summary>
        public const int ExtendedFormatVersion = 2;

        /// <summary>
        /// 手感场景格式版本：在扩展格式之上再加手感字段（<c>feel</c>、<c>skillSlots</c>、<c>learnSkills</c>、<c>dummySet</c>）与 <c>cast</c> 事件
        /// （含事件的 <c>actor</c> 字段）。只有脚本用到它们才写本版本，其余脚本的序列化文本与扩展之前逐字相同。
        /// </summary>
        public const int FeelFormatVersion = 3;

        /// <summary>
        /// 期望清单格式版本：在手感场景格式之上再加顶层 <c>expectations</c> 数组（手感设计 06 第 3.1 节）。只有脚本带期望才写本版本，
        /// 其余脚本的序列化文本与加入期望之前逐字相同；版本 1～3 的旧脚本照常读取（没有期望清单）。
        /// </summary>
        public const int ExpectFormatVersion = 4;

        /// <summary>本内核读取的最高格式版本。</summary>
        public const int MaxSupportedFormatVersion = ExpectFormatVersion;

        /// <summary>该脚本序列化时写的格式版本。</summary>
        public int EffectiveFormatVersion
        {
            get
            {
                if (Expectations.Count > 0)
                {
                    return ExpectFormatVersion;
                }

                if (Meta.UsesFeelFormat)
                {
                    return FeelFormatVersion;
                }

                foreach (var e in Events)
                {
                    if (e.Kind == ScriptEventKind.Cast || e.Kind == ScriptEventKind.ClearProjectiles || e.Actor.Length > 0)
                    {
                        return FeelFormatVersion;
                    }
                }

                if (Meta.UsesExtendedFormat)
                {
                    return ExtendedFormatVersion;
                }

                foreach (var e in Events)
                {
                    if (e.Kind == ScriptEventKind.Equip || e.Kind == ScriptEventKind.Unequip)
                    {
                        return ExtendedFormatVersion;
                    }
                }

                return FormatVersion;
            }
        }

        public ScriptMeta Meta { get; }

        public IReadOnlyList<ScriptEvent> Events { get; }

        /// <summary>
        /// 期望清单（06 第 3.1 节 <c>expectations</c>，格式版本 4）：对度量在若干格子上的断言，套件与运行命令按它判定；
        /// 没有期望时为空清单。期望不属于脚本的行为身份——增删期望不改 <see cref="ScriptMeta.ScriptVersion"/>，也不影响基线。
        /// </summary>
        public IReadOnlyList<Expectation> Expectations { get; }

        public InputScript(ScriptMeta meta, IReadOnlyList<ScriptEvent> events)
            : this(meta, events, Array.Empty<Expectation>())
        {
        }

        public InputScript(ScriptMeta meta, IReadOnlyList<ScriptEvent> events, IReadOnlyList<Expectation> expectations)
        {
            Meta = meta ?? throw new ArgumentNullException(nameof(meta));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Expectations = expectations ?? throw new ArgumentNullException(nameof(expectations));
        }

        /// <summary>同一份脚本换一份期望清单（"导出为测试"写回期望用）。</summary>
        public InputScript WithExpectations(IReadOnlyList<Expectation> expectations) => new InputScript(Meta, Events, expectations);

        public static InputScript Parse(string text, string what = "输入脚本")
        {
            var root = LabJson.ParseObject(text, what);
            var format = root.TryGetValue("formatVersion", out var f) && f is JsonNumber fn ? (int)fn.Value : 1;
            if (format > MaxSupportedFormatVersion)
            {
                throw new LabFormatException($"{what} 的 formatVersion={format} 高于本内核支持的 {MaxSupportedFormatVersion}");
            }

            var metaObj = LabJson.RequireObject(root, "meta", what);
            var meta = new ScriptMeta
            {
                ScriptId = LabJson.RequireString(metaObj, "scriptId", what + ".meta"),
                ScriptVersion = LabJson.RequireInt(metaObj, "scriptVersion", what + ".meta"),
                Description = LabJson.OptionalString(metaObj, "description", what + ".meta") ?? string.Empty,
                DatasetRoot = LabJson.OptionalString(metaObj, "datasetRoot", what + ".meta") ?? "data/_lab",
                PresetId = LabJson.OptionalString(metaObj, "presetId", what + ".meta") ?? string.Empty,
                CalibrationVersion = LabJson.OptionalString(metaObj, "calibrationVersion", what + ".meta") ?? "none",
                PoseSet = LabJson.OptionalString(metaObj, "poseSet", what + ".meta") ?? "none",
                TickRate = LabJson.RequireInt(metaObj, "tickRate", what + ".meta"),
                FrameRateCap = LabJson.RequireInt(metaObj, "frameRateCap", what + ".meta"),
                DurationTicks = LabJson.RequireInt(metaObj, "durationTicks", what + ".meta"),
            };
            if (metaObj.TryGetValue("playerStart", out var ps) && !(ps is JsonNull))
            {
                meta.PlayerStart = LabJson.ReadVec(ps, what + ".meta.playerStart");
            }

            if (metaObj.TryGetValue("dummyGroups", out var dg) && dg is JsonArray groups)
            {
                foreach (var g in groups)
                {
                    meta.DummyGroups.Add(g is JsonString gs
                        ? gs.Value
                        : throw new LabFormatException($"{what}.meta.dummyGroups 的元素必须是字符串"));
                }
            }

            meta.Scene = LabJson.OptionalString(metaObj, "scene", what + ".meta") ?? string.Empty;
            meta.FeelCalibrationId = LabJson.OptionalString(metaObj, "feelCalibrationId", what + ".meta") ?? string.Empty;
            meta.UnarmedAttackSkill = LabJson.OptionalString(metaObj, "unarmedAttackSkill", what + ".meta") ?? string.Empty;
            meta.DummySetId = LabJson.OptionalString(metaObj, "dummySet", what + ".meta") ?? string.Empty;
            if (metaObj.TryGetValue("spaceExt", out var spaceExtValue) && spaceExtValue is JsonObject spaceExtObj)
            {
                meta.SpaceExt = ReadSpaceExt(spaceExtObj, what + ".meta.spaceExt");
            }

            if (metaObj.TryGetValue("poiseImpactScale", out var scaleValue) && scaleValue is JsonObject scaleObj)
            {
                for (var i = 0; i < scaleObj.Count; i++)
                {
                    meta.PoiseImpactScale.Add(new KeyValuePair<string, double>(
                        scaleObj[i].Key,
                        scaleObj[i].Value is JsonNumber scaleNum ? scaleNum.Value : throw new LabFormatException($"{what}.meta.poiseImpactScale.{scaleObj[i].Key} 必须是数值")));
                }
            }

            meta.Feel = metaObj.TryGetValue("feel", out var feelFlag) && feelFlag is JsonBool feelBool && feelBool.Value;
            ReadStrings(metaObj, "learnSkills", meta.LearnSkills, what + ".meta");
            if (metaObj.TryGetValue("skillSlots", out var slots) && slots is JsonObject slotsObj)
            {
                for (var i = 0; i < slotsObj.Count; i++)
                {
                    meta.SkillSlots.Add(new KeyValuePair<string, string>(
                        slotsObj[i].Key,
                        slotsObj[i].Value is JsonString ss
                            ? ss.Value
                            : throw new LabFormatException($"{what}.meta.skillSlots.{slotsObj[i].Key} 必须是字符串")));
                }
            }

            ReadStrings(metaObj, "extraDataRoots", meta.ExtraDataRoots, what + ".meta");
            ReadStrings(metaObj, "extraDataExcludeTables", meta.ExtraDataExcludeTables, what + ".meta");
            ReadStrings(metaObj, "extraDataExcludeRows", meta.ExtraDataExcludeRows, what + ".meta");
            if (metaObj.TryGetValue("weaponAttackSkills", out var was) && was is JsonObject wasObj && wasObj.Count > 0)
            {
                // 旧版本脚本用它在内存里改写武器行；现在武器 → 普攻时间线是数据（feel.weapon.auto_attack_timeline_ref），不再接受脚本声明。
                throw new LabFormatException(
                    $"{what}.meta.weaponAttackSkills 已移除：武器的普攻时间线请写进 feel.weapon 行的 auto_attack_timeline_ref（数据根里用 override 行覆盖占位武器行）");
            }

            if (meta.TickRate <= 0 || meta.FrameRateCap <= 0 || meta.DurationTicks <= 0)
            {
                throw new LabFormatException($"{what}.meta 的 tickRate/frameRateCap/durationTicks 必须为正");
            }

            var events = new List<ScriptEvent>();
            foreach (var e in LabJson.RequireArray(root, "events", what))
            {
                if (!(e is JsonObject eo))
                {
                    throw new LabFormatException($"{what}.events 的元素必须是对象");
                }

                var tick = LabJson.RequireInt(eo, "tick", what + ".events[]");
                var action = LabJson.RequireString(eo, "action", what + ".events[]");
                var kindText = LabJson.RequireString(eo, "kind", what + ".events[]");
                ScriptEventKind kind;
                switch (kindText)
                {
                    case "press": kind = ScriptEventKind.Press; break;
                    case "release": kind = ScriptEventKind.Release; break;
                    case "axis": kind = ScriptEventKind.Axis; break;
                    case "equip": kind = ScriptEventKind.Equip; break;
                    case "unequip": kind = ScriptEventKind.Unequip; break;
                    case "cast": kind = ScriptEventKind.Cast; break;
                    case "clear_projectiles": kind = ScriptEventKind.ClearProjectiles; break;
                    default: throw new LabFormatException($"{what}.events[] 的 kind 未知：{kindText}（press|release|axis|equip|unequip|cast|clear_projectiles）");
                }

                var value = Vec2.Zero;
                if (kind == ScriptEventKind.Axis)
                {
                    value = LabJson.ReadVec(
                        eo.TryGetValue("value", out var v) ? v : JsonNull.Instance, what + ".events[].value");
                }

                double? ts = eo.TryGetValue("realTs", out var t) && t is JsonNumber tn ? tn.Value : (double?)null;
                var actor = LabJson.OptionalString(eo, "actor", what + ".events[]") ?? string.Empty;
                events.Add(new ScriptEvent(tick, action, kind, value, ts, actor));
            }

            var expectations = root.TryGetValue("expectations", out var ex) && !(ex is JsonNull)
                ? Expectation.ParseList(ex as JsonArray ?? throw new LabFormatException($"{what}.expectations 必须是数组"), what + ".expectations")
                : new List<Expectation>();
            return new InputScript(meta, events, expectations);
        }

        private static void ReadStrings(JsonObject obj, string key, List<string> into, string what)
        {
            if (!obj.TryGetValue(key, out var value) || !(value is JsonArray array))
            {
                return;
            }

            foreach (var item in array)
            {
                into.Add(item is JsonString str ? str.Value : throw new LabFormatException($"{what}.{key} 的元素必须是字符串"));
            }
        }

        private static string KindText(ScriptEventKind kind)
        {
            switch (kind)
            {
                case ScriptEventKind.Press: return "press";
                case ScriptEventKind.Release: return "release";
                case ScriptEventKind.Axis: return "axis";
                case ScriptEventKind.Equip: return "equip";
                case ScriptEventKind.Cast: return "cast";
                case ScriptEventKind.ClearProjectiles: return "clear_projectiles";
                default: return "unequip";
            }
        }

        private static ScriptSpaceOptions ReadSpaceExt(JsonObject obj, string what)
        {
            var ext = new ScriptSpaceOptions();
            if (obj.TryGetValue("airControl", out var ac) && !(ac is JsonNull))
            {
                ext.AirControl = ac is JsonNumber acn ? acn.Value : throw new LabFormatException($"{what}.airControl 必须是数值");
            }

            if (obj.TryGetValue("maxAirJumps", out var mj) && !(mj is JsonNull))
            {
                ext.MaxAirJumps = mj is JsonNumber mjn && mjn.TryGetInt64(out var mjl) ? (int)mjl : throw new LabFormatException($"{what}.maxAirJumps 必须是整数");
            }

            if (obj.TryGetValue("stepHeight", out var sh) && !(sh is JsonNull))
            {
                ext.StepHeight = sh is JsonNumber shn ? shn.Value : throw new LabFormatException($"{what}.stepHeight 必须是数值");
            }

            ext.Terrain = obj.TryGetValue("terrain", out var tr) && tr is JsonBool trb && trb.Value;
            ext.SpatialRange = obj.TryGetValue("spatialRange", out var sr) && sr is JsonBool srb && srb.Value;
            ext.ArenaId = LabJson.OptionalString(obj, "arena", what) ?? string.Empty;
            ext.PoseFamily = LabJson.OptionalString(obj, "poseFamily", what) ?? string.Empty;
            ReadStrings(obj, "poseKeys", ext.PoseKeys, what);
            return ext;
        }

        public string ToJson()
        {
            var meta = new JsonObjectBuilder()
                .Add("scriptId", LabJson.Str(Meta.ScriptId))
                .Add("scriptVersion", LabJson.Num(Meta.ScriptVersion))
                .Add("description", LabJson.Str(Meta.Description))
                .Add("datasetRoot", LabJson.Str(Meta.DatasetRoot))
                .Add("presetId", LabJson.Str(Meta.PresetId))
                .Add("calibrationVersion", LabJson.Str(Meta.CalibrationVersion))
                .Add("poseSet", LabJson.Str(Meta.PoseSet))
                .Add("tickRate", LabJson.Num(Meta.TickRate))
                .Add("frameRateCap", LabJson.Num(Meta.FrameRateCap))
                .Add("durationTicks", LabJson.Num(Meta.DurationTicks))
                .Add("playerStart", LabJson.Vec(Meta.PlayerStart))
                .Add("dummyGroups", new JsonArray(Meta.DummyGroups.ConvertAll(g => (JsonValue)LabJson.Str(g))));
            if (Meta.UsesFeelFormat)
            {
                // 手感场景字段只在用到时才写（先于扩展字段的块，键序固定）。
                meta.Add("feel", LabJson.Bool(Meta.Feel))
                    .Add("dummySet", LabJson.Str(Meta.DummySetId))
                    .Add("learnSkills", new JsonArray(Meta.LearnSkills.ConvertAll(g => (JsonValue)LabJson.Str(g))));
                var slotBuilder = new JsonObjectBuilder();
                foreach (var pair in Meta.SkillSlots)
                {
                    slotBuilder.Add(pair.Key, LabJson.Str(pair.Value));
                }

                meta.Add("skillSlots", slotBuilder.Build());
                if (Meta.SpaceExt != null)
                {
                    var ext = Meta.SpaceExt;
                    var extBuilder = new JsonObjectBuilder();
                    if (ext.AirControl.HasValue) extBuilder.Add("airControl", LabJson.Num(ext.AirControl.Value));
                    if (ext.MaxAirJumps.HasValue) extBuilder.Add("maxAirJumps", LabJson.Num(ext.MaxAirJumps.Value));
                    if (ext.StepHeight.HasValue) extBuilder.Add("stepHeight", LabJson.Num(ext.StepHeight.Value));
                    if (ext.Terrain) extBuilder.Add("terrain", LabJson.Bool(true));
                    if (ext.SpatialRange) extBuilder.Add("spatialRange", LabJson.Bool(true));
                    if (ext.ArenaId.Length > 0) extBuilder.Add("arena", LabJson.Str(ext.ArenaId));
                    if (ext.PoseKeys.Count > 0) extBuilder.Add("poseKeys", new JsonArray(ext.PoseKeys.ConvertAll(g => (JsonValue)LabJson.Str(g))));
                    if (ext.PoseFamily.Length > 0) extBuilder.Add("poseFamily", LabJson.Str(ext.PoseFamily));
                    meta.Add("spaceExt", extBuilder.Build());
                }

                if (Meta.PoiseImpactScale.Count > 0)
                {
                    var scaleBuilder = new JsonObjectBuilder();
                    foreach (var pair in Meta.PoiseImpactScale)
                    {
                        scaleBuilder.Add(pair.Key, LabJson.Num(pair.Value));
                    }

                    meta.Add("poiseImpactScale", scaleBuilder.Build());
                }
            }

            if (Meta.UsesExtendedFormat)
            {
                // 扩展字段只在用到时才写，既有脚本的序列化文本因此与扩展之前逐字相同。
                meta.Add("scene", LabJson.Str(Meta.Scene))
                    .Add("feelCalibrationId", LabJson.Str(Meta.FeelCalibrationId))
                    .Add("extraDataRoots", new JsonArray(Meta.ExtraDataRoots.ConvertAll(g => (JsonValue)LabJson.Str(g))))
                    .Add("extraDataExcludeTables", new JsonArray(Meta.ExtraDataExcludeTables.ConvertAll(g => (JsonValue)LabJson.Str(g))))
                    .Add("extraDataExcludeRows", new JsonArray(Meta.ExtraDataExcludeRows.ConvertAll(g => (JsonValue)LabJson.Str(g))));
                meta.Add("unarmedAttackSkill", LabJson.Str(Meta.UnarmedAttackSkill));
            }

            var metaValue = meta.Build();

            var events = new List<JsonValue>();
            foreach (var e in Events)
            {
                var b = new JsonObjectBuilder()
                    .Add("tick", LabJson.Num(e.Tick))
                    .Add("action", LabJson.Str(e.Action))
                    .Add("kind", LabJson.Str(KindText(e.Kind)));
                if (e.Kind == ScriptEventKind.Axis)
                {
                    b.Add("value", LabJson.Vec(e.Value));
                }

                if (e.Actor.Length > 0)
                {
                    b.Add("actor", LabJson.Str(e.Actor));
                }

                if (e.RealTimestamp.HasValue)
                {
                    b.Add("realTs", LabJson.Num(e.RealTimestamp.Value));
                }

                events.Add(b.Build());
            }

            var rootBuilder = new JsonObjectBuilder()
                .Add("formatVersion", LabJson.Num(EffectiveFormatVersion))
                .Add("meta", metaValue)
                .Add("events", new JsonArray(events));
            if (Expectations.Count > 0)
            {
                var expected = new List<JsonValue>();
                foreach (var e in Expectations)
                {
                    expected.Add(e.ToJson());
                }

                rootBuilder.Add("expectations", new JsonArray(expected));
            }

            return LabJson.Write(rootBuilder.Build());
        }
    }

    /// <summary>
    /// 回放导入：把"带真实时间戳的输入记录"换算成按 tick 的输入脚本（06 第 2 节"可手写，也可由回放导入"）。
    /// 换算规则：<c>tick = floor(timestamp * tickRate)</c>，同一 tick 内保持记录顺序；时间戳原值写入
    /// <see cref="ScriptEvent.RealTimestamp"/> 供对照，注入时不使用。
    /// </summary>
    public static class ScriptReplayImporter
    {
        public readonly struct TimedInput
        {
            public double Timestamp { get; }

            public string Action { get; }

            public ScriptEventKind Kind { get; }

            public Vec2 Value { get; }

            public TimedInput(double timestamp, string action, ScriptEventKind kind, Vec2 value = default)
            {
                Timestamp = timestamp;
                Action = action;
                Kind = kind;
                Value = value;
            }
        }

        public static InputScript FromTimedInputs(ScriptMeta meta, IReadOnlyList<TimedInput> inputs)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            var events = new List<ScriptEvent>(inputs.Count);
            var lastTick = 0;
            foreach (var input in inputs)
            {
                if (input.Timestamp < 0)
                {
                    throw new LabFormatException($"回放输入时间戳不能为负：{input.Timestamp}");
                }

                var tick = (int)Math.Floor(input.Timestamp * meta.TickRate);
                if (tick < lastTick)
                {
                    throw new LabFormatException("回放输入必须按时间戳升序");
                }

                lastTick = tick;
                events.Add(new ScriptEvent(tick, input.Action, input.Kind, input.Value, input.Timestamp));
            }

            return new InputScript(meta, events);
        }
    }
}

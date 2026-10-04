using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Lab
{
    /// <summary>衣橱里的一件装备：数据里一件可装备物品模板（<c>item.template</c> 的 <c>slot</c> 指向 <c>is_equipment</c> 槽位）。</summary>
    public sealed class WardrobeEntry
    {
        public string ItemId { get; set; } = string.Empty;

        public string SlotId { get; set; } = string.Empty;

        public int SortWeight { get; set; }

        public bool IsWeapon { get; set; }

        /// <summary>外观类别：<c>paperdoll</c>（slot_mesh 且 mesh_ref 是 paperdoll. 前缀，2D 图层）、<c>model</c>（其它外观行，非 2D）、<c>none</c>（没有外观行）。</summary>
        public string VisualKind { get; set; } = "none";

        public bool IsPaperdoll => string.Equals(VisualKind, "paperdoll", StringComparison.Ordinal);
    }

    /// <summary>衣橱报告里一件装备逐件穿戴那一步的事实（全部由运行记录与数据算出）。</summary>
    public sealed class WardrobeRow
    {
        public WardrobeEntry Entry { get; set; } = new WardrobeEntry();

        public bool Ok { get; set; }

        public string Icon { get; set; } = string.Empty;

        public string Visual { get; set; } = string.Empty;

        /// <summary>装备面板在这一步之后的纸娃娃图层数。</summary>
        public int PanelLayers { get; set; }

        /// <summary>由数据重放算出的这一步之后"该有的"纸娃娃图层数（已装备且外观是 paperdoll 的槽位数）。</summary>
        public int ExpectedPanelLayers { get; set; }

        /// <summary>界面库存视图、装备宿主、装备面板三方的槽位是否一致。</summary>
        public bool SlotsAgree { get; set; }
    }

    /// <summary>
    /// 衣橱报告（手感设计/06 第 3.6 节"逐件穿戴"、08 第 6 节）：每一项计数都由数据与运行记录算出，不写裸数；
    /// 与导入工具的静态校验报告逐项对应（图标缺失、外观缺失、图层缺失），宿主可以在其上再叠加资源侧核对。
    /// </summary>
    public sealed class WardrobeReport
    {
        public List<WardrobeRow> Rows { get; } = new List<WardrobeRow>();

        /// <summary>数据里可装备物品总数。</summary>
        public int Items { get; set; }

        /// <summary>数据里装备槽位总数。</summary>
        public int Slots { get; set; }

        public int WeaponItems { get; set; }

        public int PaperdollItems { get; set; }

        public int ModelItems { get; set; }

        public int NoVisualItems { get; set; }

        public int EquipSteps { get; set; }

        public int UnequipSteps { get; set; }

        public int StepsFailed { get; set; }

        public int IconMissing { get; set; }

        public int VisualMissing { get; set; }

        /// <summary>界面库存视图、装备宿主、装备面板三方槽位不一致的步数。</summary>
        public int SlotMismatch { get; set; }

        /// <summary>纸娃娃图层数与数据重放的期望不一致的步数。</summary>
        public int LayerMismatch { get; set; }

        /// <summary>宿主叠加的资源侧核对（图标与图层剪辑加载）：核对项数与不合格项数；无资源侧核对时为 -1。</summary>
        public int AuditTotal { get; set; } = -1;

        public int AuditBad { get; set; } = -1;

        public List<string> Problems { get; } = new List<string>();

        public bool Passed => Problems.Count == 0;

        /// <summary>写成确定性 JSON（键序固定），本地生成物，不进 git。</summary>
        public string ToJson()
        {
            var rows = new List<JsonValue>();
            foreach (var r in Rows)
            {
                rows.Add(new JsonObjectBuilder()
                    .Add("item", LabJson.Str(r.Entry.ItemId))
                    .Add("slot", LabJson.Str(r.Entry.SlotId))
                    .Add("isWeapon", LabJson.Bool(r.Entry.IsWeapon))
                    .Add("visualKind", LabJson.Str(r.Entry.VisualKind))
                    .Add("ok", LabJson.Bool(r.Ok))
                    .Add("icon", LabJson.Str(r.Icon))
                    .Add("visual", LabJson.Str(r.Visual))
                    .Add("panelLayers", LabJson.Num(r.PanelLayers))
                    .Add("expectedPanelLayers", LabJson.Num(r.ExpectedPanelLayers))
                    .Add("slotsAgree", LabJson.Bool(r.SlotsAgree))
                    .Build());
            }

            var problems = new List<JsonValue>();
            foreach (var p in Problems)
            {
                problems.Add(LabJson.Str(p));
            }

            var root = new JsonObjectBuilder()
                .Add("items", LabJson.Num(Items))
                .Add("slots", LabJson.Num(Slots))
                .Add("weaponItems", LabJson.Num(WeaponItems))
                .Add("paperdollItems", LabJson.Num(PaperdollItems))
                .Add("modelItems", LabJson.Num(ModelItems))
                .Add("noVisualItems", LabJson.Num(NoVisualItems))
                .Add("equipSteps", LabJson.Num(EquipSteps))
                .Add("unequipSteps", LabJson.Num(UnequipSteps))
                .Add("stepsFailed", LabJson.Num(StepsFailed))
                .Add("iconMissing", LabJson.Num(IconMissing))
                .Add("visualMissing", LabJson.Num(VisualMissing))
                .Add("slotMismatch", LabJson.Num(SlotMismatch))
                .Add("layerMismatch", LabJson.Num(LayerMismatch))
                .Add("auditTotal", LabJson.Num(AuditTotal))
                .Add("auditBad", LabJson.Num(AuditBad))
                .Add("passed", LabJson.Bool(Passed))
                .Add("problems", new JsonArray(problems))
                .Add("rows", new JsonArray(rows))
                .Build();
            return LabJson.Write(root);
        }
    }

    /// <summary>
    /// 衣橱（手感设计/06 第 3.6 节、08 第 6 节、ADR-0149）：把数据里"每一件可装备物品"逐件穿一遍的换装脚本与对应的完整度报告。
    /// <para>
    /// 判断记录（脚本由数据生成，不手写）：衣橱脚本不是夹具文件——物品清单、槽位次序全由数据算出（<see cref="ListEquippable"/>），
    /// 游戏换了装备数据，衣橱自动跟着变，不用改脚本。脚本沿用换装场景（<c>scene = equip</c>），元信息整份取自模板脚本
    /// （<c>equip_cycle</c>：数据根、标定、姿势集），只替换脚本 id、时长与事件，因此换装链每一段都和 <c>equip_cycle</c> 走同一条路径，
    /// 单件的事实（物品|槽位|是否武器|族|外观|图标|武器表现档案）与 <c>equip_cycle</c> 的 <c>item_facts</c> 逐项相同。
    /// </para>
    /// <para>
    /// 判断记录（不进基线、不进指纹）：衣橱是"数据换了就要重跑"的完整度检查，不是回归基线；它不新增度量组，既有指纹与基线逐位不变。
    /// 报告与拼图都是本地生成物，不进 git。
    /// </para>
    /// </summary>
    public static class EquipWardrobe
    {
        public const string ScriptId = "equip_wardrobe";

        /// <summary>相邻两次穿脱事件之间的间隔 tick（装备宿主在固定步末尾采快照，间隔 ≥ 1 即可，留 2 方便人读回放）。</summary>
        public const int StepGapTicks = 2;

        private const string PaperdollMeshPrefix = "paperdoll.";

        /// <summary>
        /// 列出数据里全部可装备物品：<c>item.template.slot</c> 指向 <c>item.slot_definition</c> 里 <c>is_equipment</c> 的槽位；
        /// 顺序为槽位 <c>sort_weight</c> 升序、再按槽位 id、再按物品 id（序数比较，不依赖数据行序）。
        /// </summary>
        public static List<WardrobeEntry> ListEquippable(IDataRegistryView registry, out int slotCount)
        {
            var slots = new Dictionary<string, (int Weight, bool IsWeapon)>(StringComparer.Ordinal);
            foreach (var row in registry.GetAll("item.slot_definition"))
            {
                if (row.TryGetBool("is_equipment", out var isEquipment) && isEquipment)
                {
                    slots[row.Key] = (row.TryGetInt("sort_weight", out var w) ? (int)w : 0, row.TryGetBool("is_weapon", out var wp) && wp);
                }
            }

            slotCount = slots.Count;

            var visuals = new Dictionary<string, DataRecord>(StringComparer.Ordinal);
            foreach (var row in registry.GetAll("display.equip_visual"))
            {
                if (row.TryGetString("item_id", out var itemId) && !visuals.ContainsKey(itemId))
                {
                    visuals[itemId] = row;
                }
            }

            var entries = new List<WardrobeEntry>();
            foreach (var row in registry.GetAll("item.template"))
            {
                if (!row.TryGetId("slot", out var slot) || !slots.TryGetValue(slot.Value, out var def))
                {
                    continue;
                }

                var kind = "none";
                if (visuals.TryGetValue(row.Key, out var visual))
                {
                    var isSlotMesh = visual.TryGetString("mode", out var mode) && string.Equals(mode, "slot_mesh", StringComparison.Ordinal);
                    kind = isSlotMesh && visual.TryGetId("mesh_ref", out var mesh)
                        && mesh.Value.StartsWith(PaperdollMeshPrefix, StringComparison.Ordinal)
                        && visual.TryGetString("slot_id", out _)
                            ? "paperdoll"
                            : "model";
                }

                entries.Add(new WardrobeEntry { ItemId = row.Key, SlotId = slot.Value, SortWeight = def.Weight, IsWeapon = def.IsWeapon, VisualKind = kind });
            }

            entries.Sort((a, b) =>
            {
                var c = a.SortWeight.CompareTo(b.SortWeight);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.SlotId, b.SlotId);
                return c != 0 ? c : string.CompareOrdinal(a.ItemId, b.ItemId);
            });
            return entries;
        }

        /// <summary>
        /// 一步到位：在模板脚本声明的数据集（基础数据 + 额外数据根）上列出可装备物品并生成衣橱脚本。
        /// </summary>
        public static InputScript Plan(LabRunner runner, InputScript template, out List<WardrobeEntry> entries, out int slotCount)
        {
            var dataset = runner.DatasetFor(template);
            var probe = LabHost.BuildProbe(dataset.HostOptions);
            entries = ListEquippable(probe.Registry, out slotCount);
            return BuildScript(template, entries);
        }

        /// <summary>
        /// 用模板脚本的元信息生成衣橱脚本：先按清单逐件穿上，再把每个用到的槽位卸空（次序同首次出现）。
        /// 模板须是换装场景脚本（<c>scene = equip</c>）；返回的脚本与模板互不共享状态。
        /// </summary>
        public static InputScript BuildScript(InputScript template, IReadOnlyList<WardrobeEntry> entries)
        {
            if (!string.Equals(template.Meta.Scene, "equip", StringComparison.Ordinal))
            {
                throw new LabFormatException($"衣橱脚本的模板 {template.Meta.ScriptId} 不是换装场景脚本（scene = equip）");
            }

            var clone = InputScript.Parse(template.ToJson());
            var meta = clone.Meta;
            meta.ScriptId = ScriptId;
            meta.ScriptVersion = 1;
            meta.Description = "衣橱（手感设计 06 第 3.6 节、08 第 6 节）：数据里每一件可装备物品逐件穿戴，再把用到的槽位卸空；由数据生成，不进基线。";

            var events = new List<ScriptEvent>();
            var tick = 0;
            var slotOrder = new List<string>();
            foreach (var entry in entries)
            {
                events.Add(new ScriptEvent(tick, entry.ItemId, ScriptEventKind.Equip));
                tick += StepGapTicks;
                if (!slotOrder.Contains(entry.SlotId))
                {
                    slotOrder.Add(entry.SlotId);
                }
            }

            foreach (var slot in slotOrder)
            {
                events.Add(new ScriptEvent(tick, slot, ScriptEventKind.Unequip));
                tick += StepGapTicks;
            }

            meta.DurationTicks = tick + StepGapTicks;
            return new InputScript(meta, events);
        }

        /// <summary>
        /// 把运行记录对着数据清单核对成报告：步数（穿 = 清单长度，脱 = 用到的槽位数）、穿脱成功、图标与外观不缺、
        /// 三方槽位一致、纸娃娃图层数 = 数据重放出的"已装备且外观是 paperdoll 的槽位数"。
        /// </summary>
        public static WardrobeReport BuildReport(IReadOnlyList<WardrobeEntry> entries, int slotCount, EquipRecording record)
        {
            var report = new WardrobeReport { Items = entries.Count, Slots = slotCount };
            foreach (var e in entries)
            {
                if (e.IsWeapon) report.WeaponItems++;
                if (e.IsPaperdoll) report.PaperdollItems++;
                else if (string.Equals(e.VisualKind, "model", StringComparison.Ordinal)) report.ModelItems++;
                else report.NoVisualItems++;
            }

            var byItem = new Dictionary<string, WardrobeEntry>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                byItem[e.ItemId] = e;
            }

            // 数据重放：每个槽位上现在穿着哪件（用于算"该有"的图层数）。
            var worn = new Dictionary<string, WardrobeEntry>(StringComparer.Ordinal);
            foreach (var step in record.Steps)
            {
                var isEquip = string.Equals(step.Op, "equip", StringComparison.Ordinal);
                if (isEquip)
                {
                    report.EquipSteps++;
                    if (byItem.TryGetValue(step.Arg, out var entry))
                    {
                        worn[entry.SlotId] = entry;
                    }
                }
                else
                {
                    report.UnequipSteps++;
                    worn.Remove(step.Arg);
                }

                var expectedLayers = 0;
                foreach (var w in worn.Values)
                {
                    if (w.IsPaperdoll) expectedLayers++;
                }

                var agree = string.Equals(step.PanelSlots, step.HostSlots, StringComparison.Ordinal)
                    && string.Equals(step.UiSlots, step.HostSlots, StringComparison.Ordinal);
                if (!step.Ok) report.StepsFailed++;
                if (!agree) report.SlotMismatch++;
                if (step.PanelLayers != expectedLayers) report.LayerMismatch++;
                if (step.PanelOccupied != worn.Count) report.LayerMismatch++;

                if (isEquip)
                {
                    if (step.Icon.Length == 0) report.IconMissing++;
                    if (step.Visual.Length == 0 && byItem.TryGetValue(step.Arg, out var e) && !string.Equals(e.VisualKind, "none", StringComparison.Ordinal))
                    {
                        report.VisualMissing++;
                    }

                    if (byItem.TryGetValue(step.Arg, out var rowEntry))
                    {
                        report.Rows.Add(new WardrobeRow
                        {
                            Entry = rowEntry,
                            Ok = step.Ok,
                            Icon = step.Icon,
                            Visual = step.Visual,
                            PanelLayers = step.PanelLayers,
                            ExpectedPanelLayers = expectedLayers,
                            SlotsAgree = agree,
                        });
                    }
                }
            }

            if (report.EquipSteps != entries.Count)
            {
                report.Problems.Add($"穿戴步数 {report.EquipSteps} 与数据里可装备物品数 {entries.Count} 不一致");
            }

            if (report.StepsFailed > 0) report.Problems.Add($"{report.StepsFailed} 步穿脱失败");
            if (report.IconMissing > 0) report.Problems.Add($"{report.IconMissing} 件装备没有图标 id");
            if (report.VisualMissing > 0) report.Problems.Add($"{report.VisualMissing} 件装备有外观声明却没有解析出外观");
            if (report.SlotMismatch > 0) report.Problems.Add($"{report.SlotMismatch} 步里界面库存、装备宿主、装备面板三方槽位不一致");
            if (report.LayerMismatch > 0) report.Problems.Add($"{report.LayerMismatch} 步里装备面板的图层数/占用数与数据重放不一致");
            return report;
        }

        /// <summary>报告的一行人读摘要（命令行与实验室界面用）。</summary>
        public static string Summary(WardrobeReport report) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "衣橱：{0} 件装备 / {1} 个槽位（武器 {2}、纸娃娃 {3}、模型 {4}、无外观 {5}）；穿 {6} 脱 {7}；图标缺失 {8}、外观缺失 {9}、槽位不一致 {10}、图层不一致 {11}{12}",
                report.Items, report.Slots, report.WeaponItems, report.PaperdollItems, report.ModelItems, report.NoVisualItems,
                report.EquipSteps, report.UnequipSteps, report.IconMissing, report.VisualMissing, report.SlotMismatch, report.LayerMismatch,
                report.AuditTotal >= 0 ? $"；资源核对 {report.AuditTotal} 项、不合格 {report.AuditBad}" : string.Empty);
    }
}

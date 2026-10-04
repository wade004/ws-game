#nullable enable
// ShowcaseDirector：演示场景（ADR-0154）的呈现导演——真实美术的地面/场景道具、事件驱动的特效（挥砍拖影、命中火花、尘土、冲击波环）、
// 飘字/血条/连击/技能栏的读模型（ShowcaseHudModel）。
//
// 判断记录（只读逻辑、不回流）：导演只读逻辑世界已经发出的事件与实体位置，往引擎侧（场景物体、读模型）写；它不碰逻辑世界、不改手感数据、
// 不注入事件，所以同一脚本在演示场景与原试玩场景上逻辑指纹逐字节一致（有测试守着）。伤害数字取自命中确认事件的伤害量本身，
// 不另算（"数字与手感数据一致"就是同一个量）。
// 判断记录（特效时间轴）：特效按舞台模拟时间推进（见 ShowcaseFxPlayer）；命中火花/尘土/冲击波在命中确认事件那一个固定步生成，
// 挥砍拖影在出手动画的 release/hit_frame 关键帧到达那一帧生成（与角色动画的出手点对齐，而不是与逻辑 tick 对齐——逻辑 tick 与动画差几帧是现象本身，
// 对齐误差由舞台的命中对齐度量单独记）。
// 判断记录（场景）：地面砖、道具、墙与立柱按美术资源贴；道具只做装饰，不进逻辑阻挡（阻挡仍是数据里的竞技场方块，画在方块处）。
using System;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Localization;
using Core.Rules.Common;
using Lab;
using UnityEngine;

namespace Adapter.Unity.LabHost
{
    /// <summary>一次命中确认事件的呈现侧记录。</summary>
    public sealed class ShowcaseHitLogEntry
    {
        public int Tick { get; }

        public Id Source { get; }

        public Id Target { get; }

        public double Amount { get; }

        public bool IsCrit { get; }

        public bool IsKill { get; }

        public string ImpactClass { get; }

        public string Reaction { get; }

        public int AttackerHitStopTicks { get; }

        public int TargetHitStopTicks { get; }

        public ShowcaseHitLogEntry(int tick, Id source, Id target, double amount, bool isCrit, bool isKill, string impactClass, string reaction, int attackerStop, int targetStop)
        {
            Tick = tick;
            Source = source;
            Target = target;
            Amount = amount;
            IsCrit = isCrit;
            IsKill = isKill;
            ImpactClass = impactClass;
            Reaction = reaction;
            AttackerHitStopTicks = attackerStop;
            TargetHitStopTicks = targetStop;
        }
    }

    public sealed class ShowcaseDirector : IDisposable
    {
        public static readonly Id SlashFx = new Id("vfx.show_slash");
        public static readonly Id SparkFx = new Id("vfx.show_spark");
        public static readonly Id DustFx = new Id("vfx.show_dust");
        public static readonly Id RingFx = new Id("vfx.show_ring");

        private const string IconPrefix = "icon.show.";
        private const int UnitsLayerBase = 2000;
        private const double HpMeaningfulLimit = 5000.0;

        private readonly Transform _root;
        private readonly int _layer;
        private readonly UnityResourceLoader _loader;
        private readonly LabHostContext _ctx;
        private readonly ShowcaseFxPlayer _fx;
        private readonly Action<Id> _flash;
        private readonly Dictionary<Id, double> _damageTaken = new Dictionary<Id, double>();
        private readonly Dictionary<Id, double> _maxHp = new Dictionary<Id, double>();
        private double _staminaIdle;
        private bool _disposed;

        internal ShowcaseDirector(Transform root, int layer, UnityResourceLoader loader, LabHostContext ctx, Action<Id> flash)
        {
            _flash = flash;
            _root = root;
            _layer = layer;
            _loader = loader;
            _ctx = ctx;
            _fx = new ShowcaseFxPlayer(root, layer, loader);
            _fx.Preload(SlashFx);
            _fx.Preload(SparkFx);
            _fx.Preload(DustFx);
            _fx.Preload(RingFx);
            Model.PlayerMaxHp = MaxHpOf(ctx.PlayerId);
            Model.PlayerHp = Model.PlayerMaxHp;
        }

        public ShowcaseHudModel Model { get; } = new ShowcaseHudModel();

        /// <summary>命中确认事件的呈现侧副本（测试核对：飘字数值、顿帧 tick、冲击等级都取自事件本身，不另算）。</summary>
        public List<ShowcaseHitLogEntry> HitLog { get; } = new List<ShowcaseHitLogEntry>();

        /// <summary>特效播放器累计生成数与当前活动数（测试用）。</summary>
        public int FxSpawned => _fx.Spawned;

        public int FxActive => _fx.ActiveCount;

        // ───────── 场景 ─────────

        private Sprite? LoadSprite(string name, float pixelsPerUnit, Vector2 pivot, bool repeat)
        {
            var id = new Id(IconPrefix + name);
            _loader.LoadAsync(id, ResourceKind.Image, (i, ok) => { });
            EngineLabStage.PumpLoader(_loader, 4000);
            if (!_loader.TryGetSprite(id, out var source) || source == null)
            {
                return null;
            }

            var tex = source.texture;
            if (repeat)
            {
                tex.wrapMode = TextureWrapMode.Repeat;
            }

            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }

        /// <summary>地面砖 + 竞技场方块（墙/立柱）+ 场景道具。只在试玩模式建；全部放在隔离层、挂在舞台根下，随舞台销毁。</summary>
        internal void BuildScene(LabHostContext ctx)
        {
            var ground = new GameObject("ShowcaseGround") { layer = _layer };
            ground.transform.SetParent(_root, false);
            ground.transform.position = new Vector3(0f, 0f, 0.05f);

            var tile = LoadSprite("floor_stone", 128f, new Vector2(0.5f, 0.5f), true);
            if (tile != null)
            {
                var r = ground.AddComponent<SpriteRenderer>();
                r.sprite = tile;
                r.drawMode = SpriteDrawMode.Tiled;
                r.size = new Vector2(60f, 60f);
                r.sortingOrder = -10000;
                r.color = new Color(0.82f, 0.84f, 0.88f, 1f);
            }

            // 竞技场方块：墙用更暗的石砖铺满方块，立柱用立柱美术立在方块中心。
            var wallTile = LoadSprite("floor_stone2", 128f, new Vector2(0.5f, 0.5f), true);
            var pillar = LoadSprite("prop_pillar", 100f, new Vector2(0.5f, 0.0f), false);
            try
            {
                var arena = new LabCatalog(ctx.World.Registry).GetArena(ctx.Cell.ArenaId);
                foreach (var block in arena.Blocks)
                {
                    var size = new Vector2((float)(block.Max.X - block.Min.X), (float)(block.Max.Y - block.Min.Y));
                    var center = new Vector2((float)((block.Max.X + block.Min.X) * 0.5), (float)((block.Max.Y + block.Min.Y) * 0.5));
                    if (string.Equals(block.Kind, "pillar", StringComparison.Ordinal) && pillar != null)
                    {
                        AddSprite("ShowcasePillar", pillar, new Vector2(center.x, center.y - size.y * 0.5f), 1.0f, Color.white);
                    }
                    else if (wallTile != null)
                    {
                        var wall = new GameObject("ShowcaseWall") { layer = _layer };
                        wall.transform.SetParent(_root, false);
                        wall.transform.position = new Vector3(center.x, center.y, 0.03f);
                        var wr = wall.AddComponent<SpriteRenderer>();
                        wr.sprite = wallTile;
                        wr.drawMode = SpriteDrawMode.Tiled;
                        wr.size = size;
                        wr.sortingOrder = UnitsLayerBase - (int)Math.Round(center.y - size.y * 0.5f) - 1;
                        wr.color = new Color(0.42f, 0.44f, 0.5f, 1f);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Showcase] 竞技场方块绘制失败：" + ex.Message);
            }

            // 装饰道具：固定摆位 + 种子散布（避开中央战斗区）。
            var brazier = LoadSprite("prop_brazier", 170f, new Vector2(0.5f, 0.0f), false);
            var barrel = LoadSprite("prop_barrel", 170f, new Vector2(0.5f, 0.0f), false);
            var crate = LoadSprite("prop_crate", 170f, new Vector2(0.5f, 0.0f), false);
            var rocks = LoadSprite("prop_rocks", 150f, new Vector2(0.5f, 0.0f), false);
            var fixedProps = new (Sprite? Sprite, float X, float Y)[]
            {
                (brazier, -4.4f, 2.5f), (brazier, 4.8f, 2.3f),
                (barrel, -4.7f, -1.9f), (barrel, -4.1f, -2.3f), (crate, 4.6f, -1.9f), (crate, 5.1f, -2.35f), (rocks, -2.6f, 2.8f), (rocks, 2.4f, -2.7f),
            };
            foreach (var p in fixedProps)
            {
                if (p.Sprite != null)
                {
                    AddSprite("ShowcaseProp", p.Sprite, new Vector2(p.X, p.Y), 1.0f, Color.white);
                }
            }

            var pool = new[] { rocks, barrel, crate, rocks, brazier };
            var state = 12345u;
            for (var i = 0; i < 46; i++)
            {
                state = state * 1664525u + 1013904223u;
                var rx = ((state >> 8) & 0xFFFF) / 65535f * 30f - 15f;
                state = state * 1664525u + 1013904223u;
                var ry = ((state >> 8) & 0xFFFF) / 65535f * 30f - 12f;
                if (rx > -6.5f && rx < 7f && ry > -3.8f && ry < 3.8f)
                {
                    continue;
                }

                var sprite = pool[i % pool.Length];
                if (sprite != null)
                {
                    AddSprite("ShowcaseScatter", sprite, new Vector2(rx, ry), 0.9f + (i % 3) * 0.1f, Color.white);
                }
            }
        }

        private void AddSprite(string name, Sprite sprite, Vector2 feet, float scale, Color tint)
        {
            var go = new GameObject(name) { layer = _layer };
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(feet.x, feet.y, 0f);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = tint;
            r.sortingOrder = UnitsLayerBase - (int)Math.Round(feet.y);
        }

        // ───────── 事件 ─────────

        private static string TailOf(Id skill)
        {
            var v = skill.Value;
            var at = v.LastIndexOf('_');
            // skill.lab_a_combo1 -> combo1；skill.lab_slash -> slash
            return at >= 0 ? v.Substring(at + 1) : v.Substring(v.LastIndexOf('.') + 1);
        }

        private double MaxHpOf(Id entity)
        {
            if (_maxHp.TryGetValue(entity, out var cached))
            {
                return cached;
            }

            var value = 100.0;
            try
            {
                var e = _ctx.World.World.GetEntity(entity);
                if (e != null && e.TemplateId.HasValue)
                {
                    var rec = _ctx.World.Registry.Get("creature.template", e.TemplateId.Value);
                    if (rec != null && rec.Raw.TryGetValue("base_stats", out var bs) && bs is JsonObject stats && stats.TryGetValue("stat.stamina", out var v) && v is JsonNumber n)
                    {
                        value = n.Value;
                    }
                }
            }
            catch (Exception)
            {
                // 读不到就用缺省（呈现用的血量上限，不影响逻辑）。
            }

            _maxHp[entity] = value;
            return value;
        }

        private IL10nHost? _l10n;
        private bool _l10nTried;
        private readonly Dictionary<Id, string> _names = new Dictionary<Id, string>();

        /// <summary>
        /// 敌人头顶显示名：取生物模板的 <c>name_key</c> 经本地化表解析（取数据声明的缺省语言）；
        /// 模板没声明名字键、本地化表里没有这条文案或本地化装配失败时，才回落短 id（出场标签）。只读数据，不碰逻辑。
        /// </summary>
        public string NameOf(Id entity)
        {
            if (_names.TryGetValue(entity, out var cached))
            {
                return cached;
            }

            var fallback = _ctx.Labels.TryGetValue(entity, out var label) ? label : entity.Value;
            var name = fallback;
            try
            {
                if (!_l10nTried)
                {
                    _l10nTried = true;
                    // 只读文案查询：不调用 SetLocale（会往世界事件总线发语言切换事件），用数据声明的缺省语言。
                    var host = new L10nHost(_ctx.World.Registry, _ctx.World.Bus);
                    _l10n = host;
                }

                var e = _ctx.World.World.GetEntity(entity);
                if (_l10n != null && e != null && e.TemplateId.HasValue)
                {
                    var rec = _ctx.World.Registry.Get("creature.template", e.TemplateId.Value);
                    if (rec != null && rec.Raw.TryGetValue("name_key", out var nk) && nk is JsonString ns && _l10n.HasText(new Id(ns.Value)))
                    {
                        name = _l10n.Text(new Id(ns.Value));
                    }
                }
            }
            catch (Exception)
            {
                // 本地化装配或读取失败：回落短 id（呈现用，不影响逻辑）。
            }

            _names[entity] = name;
            return name;
        }

        private ShowcaseBar BarOf(Id entity)
        {
            if (!Model.Bars.TryGetValue(entity, out var bar))
            {
                var max = MaxHpOf(entity);
                bar = new ShowcaseBar
                {
                    Entity = entity,
                    Label = NameOf(entity),
                    Hp = max,
                    MaxHp = max,
                    Hidden = max >= HpMeaningfulLimit,
                };
                Model.Bars[entity] = bar;
            }

            return bar;
        }

        private Vector2 PositionOf(Id entity, Vector2 fallback)
        {
            var e = _ctx.World.World.GetEntity(entity);
            return e == null ? fallback : new Vector2((float)e.Position.X, (float)e.Position.Y);
        }

        /// <summary>实体当前位置（HUD 定位用）；实体不存在返回 null。</summary>
        public Vector2? PositionOf(Id entity)
        {
            var e = _ctx.World.World.GetEntity(entity);
            return e == null ? (Vector2?)null : new Vector2((float)e.Position.X, (float)e.Position.Y);
        }

        public float HeadHeightOf(Id entity) => HeadHeight(entity);

        private float HeadHeight(Id entity)
        {
            if (entity.Equals(_ctx.PlayerId))
            {
                return 1.1f;
            }

            var look = ShowcaseDisplayRegistry.LookOf(_ctx.Labels.TryGetValue(entity, out var label) ? label : entity.Value);
            switch (look)
            {
                case ShowcaseDisplayRegistry.Brute: return 1.45f;
                case ShowcaseDisplayRegistry.Dummy: return 1.2f;
                default: return 0.85f;
            }
        }

        internal void OnLogicEvent(IEvent e, int tick)
        {
            switch (e)
            {
                case CombatHitConfirmedEvent hit:
                    OnHit(hit, tick);
                    break;
                case ActionStartedEvent started:
                    OnActionStarted(started);
                    break;
                case UnitDiedEvent died:
                    if (Model.Bars.TryGetValue(died.UnitId, out var dead))
                    {
                        dead.Alive = false;
                        dead.Hp = 0.0;
                    }

                    break;
            }
        }

        private int LiveNumbersOf(Id target)
        {
            var count = 0;
            foreach (var n in Model.Numbers)
            {
                if (n.Target.Equals(target))
                {
                    count++;
                }
            }

            return count;
        }

        private void OnHit(CombatHitConfirmedEvent hit, int tick)
        {
            var targetPos = PositionOf(hit.TargetId, new Vector2((float)hit.ContactPoint.X, (float)hit.ContactPoint.Y));
            var head = targetPos + new Vector2(0f, HeadHeight(hit.TargetId));
            var number = new ShowcaseDamageNumber
            {
                Tick = tick,
                Target = hit.TargetId,
                Amount = hit.Amount,
                IsCrit = hit.IsCrit,
                IsKill = hit.IsKill,
                Text = Math.Round(hit.Amount, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + (hit.IsCrit ? "!" : string.Empty),
                WorldPos = new Vec2(head.x, head.y),
                ImpactClass = hit.ImpactClass,
                Lane = LiveNumbersOf(hit.TargetId),
            };
            HitLog.Add(new ShowcaseHitLogEntry(tick, hit.SourceId, hit.TargetId, hit.Amount, hit.IsCrit, hit.IsKill, hit.ImpactClass, hit.Reaction.ToString(), hit.AttackerHitStopTicks, hit.TargetHitStopTicks));
            Model.Numbers.Add(number);
            Model.NumbersSpawned++;
            Model.LastNumber = number;
            Model.LastHitClass = hit.ImpactClass;
            Model.LastHitReaction = hit.Reaction.ToString();

            // HP 与连击。
            if (hit.TargetId.Equals(_ctx.PlayerId))
            {
                Model.PlayerHp = Math.Max(0.0, Model.PlayerHp - hit.Amount);
            }
            else
            {
                var bar = BarOf(hit.TargetId);
                bar.Hp = Math.Max(0.0, bar.Hp - hit.Amount);
                bar.SinceHit = 0.0;
                if (hit.IsKill)
                {
                    bar.Alive = false;
                    bar.Hp = 0.0;
                }
            }

            if (hit.SourceId.Equals(_ctx.PlayerId))
            {
                Model.Combo++;
                Model.ComboAge = 0.0;
                Model.BestCombo = Math.Max(Model.BestCombo, Model.Combo);
            }

            // 受击闪白：实验室数据里没有闪白配置行（打击反馈流水线不出闪白），演示场景由导演在命中确认时给受击方闪一下（纯呈现）。
            _flash(hit.TargetId);

            // 命中火花（按冲击等级放缩）、倒地/击退尘土、重击与击杀冲击波环。
            var scale = ScaleOf(hit.ImpactClass) * (hit.IsKill ? 1.4f : 1.0f);
            var contact = new Vector2((float)hit.ContactPoint.X, (float)hit.ContactPoint.Y) + new Vector2(0f, 0.45f);
            var order = UnitsLayerBase + 400;
            if (_fx.Spawn(SparkFx, contact, (tick * 47) % 360, scale, Color.white, order))
            {
                Model.SparksSpawned++;
            }

            if (hit.Reaction == HitReaction.Knockback || hit.Reaction == HitReaction.Knockdown || hit.IsKill)
            {
                var dir = new Vector2((float)hit.WorldDirection.X, (float)hit.WorldDirection.Y);
                if (_fx.Spawn(DustFx, targetPos + dir * 0.15f, 0f, 0.9f, new Color(1f, 1f, 1f, 0.9f), UnitsLayerBase + 300))
                {
                    Model.DustSpawned++;
                }
            }

            if (hit.Reaction == HitReaction.Knockdown || hit.IsKill || string.Equals(hit.ImpactClass, "heavy", StringComparison.Ordinal) || string.Equals(hit.ImpactClass, "massive", StringComparison.Ordinal))
            {
                if (_fx.Spawn(RingFx, targetPos, 0f, 0.8f, Color.white, UnitsLayerBase - (int)Math.Round(targetPos.y) - 1))
                {
                    Model.RingsSpawned++;
                }
            }
        }

        private static float ScaleOf(string impactClass)
        {
            switch (impactClass)
            {
                case "massive": return 1.35f;
                case "heavy": return 1.1f;
                case "medium": return 0.85f;
                default: return 0.62f;
            }
        }

        private void OnActionStarted(ActionStartedEvent started)
        {
            if (!started.ActorId.Equals(_ctx.PlayerId))
            {
                return;
            }

            var tail = TailOf(started.SkillId);
            var seconds = started.DurationTicks * _ctx.StepSeconds;
            foreach (var slot in Model.Slots)
            {
                if (Array.IndexOf(slot.SkillTails, tail) >= 0)
                {
                    slot.LockTotal = seconds;
                    slot.LockRemaining = seconds;
                    slot.LastSkill = tail;
                    slot.ComboIndex = started.ComboIndex;
                }
            }

            // 体力：纯呈现推演（见 ShowcaseHudModel 判断记录）。
            Model.PlayerStamina = Math.Max(0.0, Model.PlayerStamina - (string.Equals(tail, "dodge", StringComparison.Ordinal) ? 28.0 : 7.0));
            _staminaIdle = 0.0;
        }

        /// <summary>出手动画的 release/hit_frame 关键帧到达：在出手者身前放一道挥砍拖影（朝向取实体朝向）。</summary>
        internal void OnSwing(Id entity)
        {
            var e = _ctx.World.World.GetEntity(entity);
            if (e == null)
            {
                return;
            }

            var facing = e.Facing;
            var dir = new Vector2((float)Math.Cos(facing), (float)Math.Sin(facing));
            var pos = new Vector2((float)e.Position.X, (float)e.Position.Y) + dir * 0.35f + new Vector2(0f, 0.4f);
            var isPlayer = entity.Equals(_ctx.PlayerId);
            var tint = isPlayer ? Color.white : new Color(1f, 0.55f, 0.4f, 1f);
            var scale = isPlayer ? 1.0f : 1.5f;
            if (_fx.Spawn(SlashFx, pos, (float)(facing * 180.0 / Math.PI), scale, tint, UnitsLayerBase + 350))
            {
                Model.SlashesSpawned++;
            }
        }

        // ───────── 每帧 ─────────

        internal void Update(double dt)
        {
            if (_disposed)
            {
                return;
            }

            _fx.Update(dt);

            for (var i = Model.Numbers.Count - 1; i >= 0; i--)
            {
                var n = Model.Numbers[i];
                n.Age += dt;
                if (n.Age > 1.0)
                {
                    Model.Numbers.RemoveAt(i);
                }
            }

            if (Model.Combo > 0)
            {
                Model.ComboAge += dt;
                if (Model.ComboAge > Model.ComboWindow)
                {
                    Model.Combo = 0;
                }
            }

            foreach (var slot in Model.Slots)
            {
                if (slot.LockRemaining > 0.0)
                {
                    slot.LockRemaining = Math.Max(0.0, slot.LockRemaining - dt);
                }
            }

            foreach (var bar in Model.Bars.Values)
            {
                bar.SinceHit += dt;
            }

            _staminaIdle += dt;
            if (_staminaIdle > 0.6 && Model.PlayerStamina < Model.PlayerMaxStamina)
            {
                Model.PlayerStamina = Math.Min(Model.PlayerMaxStamina, Model.PlayerStamina + 30.0 * dt);
            }

            // 被清掉的靶子：血条跟着移除。
            List<Id>? gone = null;
            foreach (var pair in Model.Bars)
            {
                if (_ctx.World.World.GetEntity(pair.Key) == null)
                {
                    (gone ??= new List<Id>()).Add(pair.Key);
                }
            }

            if (gone != null)
            {
                foreach (var id in gone)
                {
                    Model.Bars.Remove(id);
                    _damageTaken.Remove(id);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _fx.Dispose();
        }
    }
}

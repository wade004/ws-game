using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 10 第 2.3 节 world 段里"跟当前玩家单位相关"的两个字段——<c>current_map_id</c>/
    /// <c>current_position</c>——各自的 <see cref="IPersistable"/> 实现（针对某一个指定的
    /// <see cref="PlayerUnit"/>）。<see cref="PlayerUnit"/> 其余段（<c>inventory</c>/<c>equipment</c>/
    /// <c>known_skills</c>/<c>quest_state</c> 等）由各自负责的模块（item/skill/quest）实现自己的
    /// <see cref="IPersistable"/>，不属于本类型职责（见 10 第 2.2 节字段表）。
    /// <para>
    /// 静态工厂类：<c>world.current_map_id</c>、<c>world.current_position</c> 是两个不同的存档段 key
    /// （见 <see cref="SaveSections.WorldCurrentMapId"/>/<see cref="SaveSections.WorldCurrentPosition"/>），
    /// 一个 <see cref="IPersistable"/> 实例只能声明一个 <see cref="IPersistable.SectionKey"/>，因此本
    /// 类型不直接实现该接口，而是各返回一个内部实现实例，调用方各自向 <c>ISaveSystem</c> 注册。
    /// </para>
    /// </summary>
    public static class UnitPersistable
    {
        /// <summary><c>world.current_map_id</c> 段（见 10 第 2.3 节）。</summary>
        public static IPersistable CurrentMapId(PlayerUnit player) => new CurrentMapIdPersistable(player);

        /// <summary>
        /// <c>world.current_position</c> 段（见 10 第 2.3 节）。
        /// <para>
        /// C11-RELOAD 根治（架构落地计划/消费方反馈-2026-09-11-读档空间索引与复活生命周期.md 第 1
        /// 项）判断记录：本重载保留 <see cref="Load"/> 直接写 <paramref name="player"/>.<see
        /// cref="PlayerUnit.Position"/> 字段的旧行为，绕开 <see cref="IUnitAccess.SetPosition"/>——
        /// 空间索引因此不会同步（<see cref="Core.Carriers.Unit.WorldUnitAccess.SetPosition"/> 才会
        /// 经 <c>ISpatialQuery.UpdatePosition</c> 同步，见该方法判断记录）。仅为源码兼容保留（本类型
        /// 是 <c>public static</c> 工厂，不能排除既有调用方直接以这个签名调用）；生产装配
        /// （<c>GameplayAssembly.RegisterPersistables</c>）已经改用下方
        /// <see cref="CurrentPosition(PlayerUnit, IUnitAccess)"/> 重载。
        /// </para>
        /// </summary>
        public static IPersistable CurrentPosition(PlayerUnit player) => new CurrentPositionPersistable(player, null);

        /// <summary>
        /// C11-RELOAD 根治新增：<c>world.current_position</c> 段，<see cref="IPersistable.Load"/>
        /// 改经 <paramref name="unitAccess"/>.<see cref="IUnitAccess.SetPosition"/> 写入位置——统一
        /// 单位访问层入口，若 <paramref name="unitAccess"/> 实际是 <see
        /// cref="Core.Carriers.Unit.WorldUnitAccess"/>（生产环境唯一实现），写入的同时会经其注入的
        /// <c>ISpatialQuery.UpdatePosition</c> 同步空间索引——根治"同图读档后玩家位置正确但空间索引
        /// 仍是移动前登记的旧位置（甚至查询不到）"（真实探针复现：<c>spatial_index_count=0</c>，见
        /// 消费方反馈第 1 项）。<see cref="Save"/> 不受影响，仍直接读 <paramref name="player"/>.<see
        /// cref="PlayerUnit.Position"/>——该字段本就与 <paramref name="unitAccess"/> 指向同一个运行期
        /// <c>Unit</c> 实体，两者读到的值恒一致，不需要改走 <see cref="IUnitAccess.GetPosition"/>。
        /// </summary>
        public static IPersistable CurrentPosition(PlayerUnit player, IUnitAccess unitAccess) =>
            new CurrentPositionPersistable(player, unitAccess ?? throw new ArgumentNullException(nameof(unitAccess)));

        /// <summary>
        /// W1 收边补齐（A4 审计 F1：10 第 2.2 节 <c>archetype_id</c> 字段"必填"，此前无任何
        /// <see cref="IPersistable"/> 落点，读档后玩家的职业模板引用完全不会被恢复）——
        /// <c>player.archetype</c> 段（见 <see cref="SaveSections.PlayerArchetype"/>）。
        /// <para>
        /// 判断记录（只存 <see cref="PlayerUnit.ArchetypeId"/>，不单独存种族——此段历史判断记录，
        /// 已被下方 <see cref="RaceId"/> 的引入部分推翻，保留原文供追溯）：05 第 1.2 节
        /// <c>PlayerUnit</c> 额外字段列表只给出 <c>archetypeId</c>（职业模板引用）一项，全仓
        /// 未见任何单位类型持有独立的"种族引用"字段——<c>Core.Numbers.Archetype.ArchetypeRegistry.ApplyTo</c>
        /// 的 <c>raceId</c> 参数只在应用当下使用（写基础属性/被动光环），应用完成后不会被任何
        /// L3 类型记住"这个单位是哪个种族"。10 第 2.2 节 <c>archetype_id</c> 的描述"职业/种族模板
        /// 引用"在当前框架实现下即等价于 <see cref="PlayerUnit.ArchetypeId"/> 本身——若某款游戏
        /// 需要种族也能读档后区分，应在自己的游戏层扩展一个独立字段与配套 Persistable，不属于
        /// 本框架级收边范围（框架不为具体游戏预判种族是否需要独立持久化）。
        /// </para>
        /// <para>
        /// 收边修订（种族被动光环跨图丢失根治，architecture/落地计划/audit-85f1f4f-20260908）：
        /// 上一段判断记录的前提已经不成立——<see cref="PlayerUnit"/> 现在确实持有一个可选的
        /// <see cref="PlayerUnit.RaceId"/> 字段（见该字段判断记录），配套的存档落点是下方
        /// <see cref="RaceId"/> 工厂方法，不再要求每个游戏自行扩展。原因：跨图 <c>World.ClearAll</c>
        /// 后需要重放种族被动光环（<see cref="Core.Rules.Assembly.RulesAssembly.ReapplyRacePassiveAuras"/>），
        /// 这一步骤发生在框架级的 <c>GameplayAssembly.EnterMap</c> 内，框架自己就需要知道"当前玩家
        /// 是哪个种族"，不能再把这份信息完全丢给游戏层。
        /// </para>
        /// </summary>
        public static IPersistable ArchetypeId(PlayerUnit player) => new ArchetypeIdPersistable(player);

        /// <summary>
        /// <c>player.race_id</c> 段（见 <see cref="SaveSections.PlayerRaceId"/> 判断记录）：可选的
        /// 种族模板引用，读档缺段/为 <c>null</c> 时按 <see cref="IPersistable"/> 默认语义清空为
        /// <c>null</c>（未设置种族），不声明 <see cref="IPersistable.KeepStateWhenSectionMissing"/>
        /// 例外——与 <see cref="CurrentMapIdPersistable"/>/<see cref="ArchetypeIdPersistable"/> 不同，
        /// <c>Id?</c> 本身就有一个明确、无歧义的"空"值可用，不存在"清空成什么才对"的两难。
        /// </summary>
        public static IPersistable RaceId(PlayerUnit player) => new RaceIdPersistable(player);

        private sealed class CurrentMapIdPersistable : IPersistable
        {
            private readonly PlayerUnit _player;

            public CurrentMapIdPersistable(PlayerUnit player)
            {
                _player = player ?? throw new ArgumentNullException(nameof(player));
            }

            public string SectionKey => SaveSections.WorldCurrentMapId;

            /// <summary>
            /// AUD-02 收边（architecture/落地计划/audit-85f1f4f-20260908，P2）：显式声明"缺段即保留"
            /// 例外（见 <see cref="IPersistable.KeepStateWhenSectionMissing"/> 判断记录）。理由——
            /// <c>Id</c>（<see cref="_player"/>.<c>MapId</c> 的类型）是必填标识符，本层（L3）不知道
            /// 任何"空地图 id"的合法默认值（那属于具体游戏内容，例如出生点地图），清空成任意占位
            /// 值都不比"保留 <see cref="PlayerUnit"/> 构造期已经设好的当前值"更正确——正常路径下
            /// 该字段在本方法被调用前已经由调用方（游戏引导代码）设置为一个真实存在的出生/存档点，
            /// 缺段（旧格式存档没有这个字段）时保留这个已知合法值优于覆盖成不知道该填什么的占位。
            /// </summary>
            public bool KeepStateWhenSectionMissing => true;

            public JsonValue Save() => new JsonString(_player.MapId.Value);

            public void Load(JsonValue data)
            {
                if (data is JsonNull)
                {
                    return;
                }

                if (!(data is JsonString text))
                {
                    throw new FormatException(
                        $"world.current_map_id 段的数据不是 JSON 字符串（实际种类：{data.Kind}）");
                }

                _player.MapId = new Id(text.Value);
            }
        }

        private sealed class RaceIdPersistable : IPersistable
        {
            private readonly PlayerUnit _player;

            public RaceIdPersistable(PlayerUnit player)
            {
                _player = player ?? throw new ArgumentNullException(nameof(player));
            }

            public string SectionKey => SaveSections.PlayerRaceId;

            public JsonValue Save() => _player.RaceId.HasValue ? new JsonString(_player.RaceId.Value.Value) : (JsonValue)JsonNull.Instance;

            public void Load(JsonValue data)
            {
                if (data is JsonNull)
                {
                    _player.RaceId = null;
                    return;
                }

                if (!(data is JsonString text))
                {
                    throw new FormatException(
                        $"{SectionKey} 段的数据不是 JSON 字符串（实际种类：{data.Kind}）");
                }

                _player.RaceId = new Id(text.Value);
            }
        }

        private sealed class ArchetypeIdPersistable : IPersistable
        {
            private readonly PlayerUnit _player;

            public ArchetypeIdPersistable(PlayerUnit player)
            {
                _player = player ?? throw new ArgumentNullException(nameof(player));
            }

            public string SectionKey => SaveSections.PlayerArchetype;

            /// <summary>见 <see cref="CurrentMapIdPersistable.KeepStateWhenSectionMissing"/> 判断
            /// 记录，同款理由：<c>archetype_id</c> 是必填的职业模板引用，本层不知道任何合法的
            /// "空职业"默认值，缺段时保留调用前已确定的值优于覆盖成占位。</summary>
            public bool KeepStateWhenSectionMissing => true;

            public JsonValue Save() => new JsonString(_player.ArchetypeId.Value);

            public void Load(JsonValue data)
            {
                if (data is JsonNull)
                {
                    return;
                }

                if (!(data is JsonString text))
                {
                    throw new FormatException(
                        $"player.archetype 段的数据不是 JSON 字符串（实际种类：{data.Kind}）");
                }

                _player.ArchetypeId = new Id(text.Value);
            }
        }

        private sealed class CurrentPositionPersistable : IPersistable
        {
            private readonly PlayerUnit _player;

            /// <summary>C11-RELOAD 根治新增：为 null 时（<see cref="CurrentPosition(PlayerUnit)"/>
            /// 旧工厂路径）<see cref="Load"/> 退回直接写 <see cref="_player"/>.Position 字段的旧行为；
            /// 非 null（<see cref="CurrentPosition(PlayerUnit, IUnitAccess)"/> 新工厂路径）时经它写入，
            /// 见两个工厂方法判断记录。</summary>
            private readonly IUnitAccess? _unitAccess;

            public CurrentPositionPersistable(PlayerUnit player, IUnitAccess? unitAccess)
            {
                _player = player ?? throw new ArgumentNullException(nameof(player));
                _unitAccess = unitAccess;
            }

            public string SectionKey => SaveSections.WorldCurrentPosition;

            /// <summary>见 <see cref="CurrentMapIdPersistable.KeepStateWhenSectionMissing"/> 判断
            /// 记录：本段与 <c>current_map_id</c> 是一对必须保持一致的字段（"在哪张图的哪个位置"），
            /// 若 <c>current_map_id</c> 缺段即保留旧地图 id，本段却清空成 <c>(0,0)</c>，会把玩家放在
            /// 旧地图上一个与当前地图无关、可能落在障碍物/地图外的坐标——两个字段必须同生共死地
            /// 保留或同生共死地清空，不能一个保留一个清零，而"清空成什么坐标才对"同样是本层不掌握
            /// 的游戏内容信息（出生点坐标），因此与 <c>current_map_id</c> 采用相同的例外语义。</summary>
            public bool KeepStateWhenSectionMissing => true;

            public JsonValue Save() => new JsonObjectBuilder()
                .Add("x", new JsonNumber(_player.Position.X))
                .Add("y", new JsonNumber(_player.Position.Y))
                .Build();

            public void Load(JsonValue data)
            {
                if (data is JsonNull)
                {
                    return;
                }

                if (!(data is JsonObject obj) ||
                    !obj.TryGetValue("x", out var xValue) || !(xValue is JsonNumber x) ||
                    !obj.TryGetValue("y", out var yValue) || !(yValue is JsonNumber y))
                {
                    throw new FormatException(
                        "world.current_position 段的数据不是 {x, y} 形状的 JSON 对象");
                }

                var position = new Vec2(x.Value, y.Value);
                if (_unitAccess != null)
                {
                    _unitAccess.SetPosition(_player.EntityId, position);
                }
                else
                {
                    _player.Position = position;
                }
            }
        }
    }
}

using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

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

        /// <summary><c>world.current_position</c> 段（见 10 第 2.3 节）。</summary>
        public static IPersistable CurrentPosition(PlayerUnit player) => new CurrentPositionPersistable(player);

        /// <summary>
        /// W1 收边补齐（A4 审计 F1：10 第 2.2 节 <c>archetype_id</c> 字段"必填"，此前无任何
        /// <see cref="IPersistable"/> 落点，读档后玩家的职业模板引用完全不会被恢复）——
        /// <c>player.archetype</c> 段（见 <see cref="SaveSections.PlayerArchetype"/>）。
        /// <para>
        /// 判断记录（只存 <see cref="PlayerUnit.ArchetypeId"/>，不单独存种族）：05 第 1.2 节
        /// <c>PlayerUnit</c> 额外字段列表只给出 <c>archetypeId</c>（职业模板引用）一项，全仓
        /// 未见任何单位类型持有独立的"种族引用"字段——<c>Core.Numbers.Archetype.ArchetypeRegistry.ApplyTo</c>
        /// 的 <c>raceId</c> 参数只在应用当下使用（写基础属性/被动光环），应用完成后不会被任何
        /// L3 类型记住"这个单位是哪个种族"。10 第 2.2 节 <c>archetype_id</c> 的描述"职业/种族模板
        /// 引用"在当前框架实现下即等价于 <see cref="PlayerUnit.ArchetypeId"/> 本身——若某款游戏
        /// 需要种族也能读档后区分，应在自己的游戏层扩展一个独立字段与配套 Persistable，不属于
        /// 本框架级收边范围（框架不为具体游戏预判种族是否需要独立持久化）。
        /// </para>
        /// </summary>
        public static IPersistable ArchetypeId(PlayerUnit player) => new ArchetypeIdPersistable(player);

        private sealed class CurrentMapIdPersistable : IPersistable
        {
            private readonly PlayerUnit _player;

            public CurrentMapIdPersistable(PlayerUnit player)
            {
                _player = player ?? throw new ArgumentNullException(nameof(player));
            }

            public string SectionKey => SaveSections.WorldCurrentMapId;

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

        private sealed class ArchetypeIdPersistable : IPersistable
        {
            private readonly PlayerUnit _player;

            public ArchetypeIdPersistable(PlayerUnit player)
            {
                _player = player ?? throw new ArgumentNullException(nameof(player));
            }

            public string SectionKey => SaveSections.PlayerArchetype;

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

            public CurrentPositionPersistable(PlayerUnit player)
            {
                _player = player ?? throw new ArgumentNullException(nameof(player));
            }

            public string SectionKey => SaveSections.WorldCurrentPosition;

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

                _player.Position = new Vec2(x.Value, y.Value);
            }
        }
    }
}

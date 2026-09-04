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

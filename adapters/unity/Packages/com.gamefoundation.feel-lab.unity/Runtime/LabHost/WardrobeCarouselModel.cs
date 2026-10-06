#nullable enable
// WardrobeCarouselModel：衣橱"方向 × 姿势键"轮播的纯逻辑（手感设计/06 第 3.6 节、ADR-0149）。
//
// 轮播遍历：每件纸娃娃装备 × 每个姿势键（EquipWardrobeRunner.Poses）× 每个已制作方向（PaperdollPreview.Directions），次序是方向最快、
// 其次姿势键、最后装备。总格数 = 纸娃娃件数 × 姿势键数 × 方向数，由数据算出；遍历一整圈每格恰好经过一次（不变量，用例核对）。
// 本类不碰引擎对象，场景（EquipWardrobeScene）与用例都读它。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using Lab;

namespace FeelLab.Unity
{
    public sealed class WardrobeCarouselModel
    {
        private readonly List<WardrobeEntry> _items = new List<WardrobeEntry>();
        private readonly string[] _directions;
        private int _cursor;

        public WardrobeCarouselModel(IReadOnlyList<WardrobeEntry> entries, IReadOnlyList<string> directions)
        {
            foreach (var entry in entries)
            {
                if (entry.IsPaperdoll)
                {
                    _items.Add(entry);
                }
            }

            _directions = new string[directions.Count];
            for (var i = 0; i < directions.Count; i++)
            {
                _directions[i] = directions[i];
            }
        }

        /// <summary>总格数。</summary>
        public int Count => _items.Count * EquipWardrobeRunner.Poses.Length * _directions.Length;

        public int Cursor => _cursor;

        public WardrobeEntry Item => _items[ItemIndex];

        public string Pose => EquipWardrobeRunner.Poses[PoseIndex].Name;

        public string Direction => _directions[DirectionIndex];

        private int DirectionIndex => _cursor % _directions.Length;

        private int PoseIndex => (_cursor / _directions.Length) % EquipWardrobeRunner.Poses.Length;

        private int ItemIndex => _cursor / (_directions.Length * EquipWardrobeRunner.Poses.Length);

        public bool IsEmpty => Count == 0;

        /// <summary>前进一格（走完一圈回到第一格）。</summary>
        public void Next()
        {
            if (Count > 0)
            {
                _cursor = (_cursor + 1) % Count;
            }
        }

        /// <summary>从头开始。</summary>
        public void Reset() => _cursor = 0;

        /// <summary>遍历一整圈，依次给出每格 (装备, 姿势, 方向)；结束后游标回到起点。</summary>
        public List<(string Item, string Pose, string Direction)> Lap()
        {
            var cells = new List<(string, string, string)>();
            _cursor = 0;
            for (var i = 0; i < Count; i++)
            {
                cells.Add((Item.ItemId, Pose, Direction));
                Next();
            }

            return cells;
        }
    }
}

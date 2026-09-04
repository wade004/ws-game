using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// <see cref="IDisplayInfoRegistry"/> 的默认实现（见 03_运行时骨架.md 第 9 节
    /// <c>DisplayInfoRegistry</c> 签名）。索引键是 <c>display.map</c> 记录的 <c>logical_id</c>
    /// 字段（不是记录自身的 <c>id</c>）——<see cref="Lookup"/> 按"要找哪个逻辑对象的外形"
    /// 查询，与 <c>display.map</c> 行自身的 id 是两个不同的轴。构造时与 <see cref="Reload"/>
    /// 时都会重建全部索引；同一 <c>logical_id</c> 出现在多条 <c>display.map</c> 记录里视为
    /// 装配期错误，直接抛 <see cref="InvalidOperationException"/>（与 hook_registry
    /// <c>Register</c> 对"接线错误尽快暴露"同一惯例，而不是静默取后一条覆盖前一条）。
    /// </summary>
    public sealed class DisplayInfoRegistry : IDisplayInfoRegistry
    {
        private readonly IDataRegistryView _registry;
        private readonly IEventBus _bus;

        private Dictionary<Id, DisplayInfo> _byLogicalId = null!;
        private List<DisplayInfo> _all = null!;
        private Dictionary<DisplayCategory, List<DisplayInfo>> _byCategory = null!;

        public DisplayInfoRegistry(IDataRegistryView registry, IEventBus bus)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            BuildIndex();
        }

        public DisplayInfo? Lookup(Id logicalId) =>
            _byLogicalId.TryGetValue(logicalId, out var info) ? info : null;

        public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) =>
            _byCategory.TryGetValue(category, out var list) ? list : Array.Empty<DisplayInfo>();

        public IReadOnlyList<DisplayInfo> All => _all;

        public void Reload()
        {
            BuildIndex();
            _bus.PublishImmediate(new DisplayInfoReloadedEvent());
        }

        private void BuildIndex()
        {
            var byLogicalId = new Dictionary<Id, DisplayInfo>();
            var all = new List<DisplayInfo>();
            var byCategory = new Dictionary<DisplayCategory, List<DisplayInfo>>();

            foreach (var record in _registry.GetAll(DisplaySchemas.Map.Name))
            {
                var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(record);

                if (byLogicalId.ContainsKey(info.LogicalId))
                {
                    throw new InvalidOperationException(
                        $"display.map 中 logical_id \"{info.LogicalId}\" 重复出现（记录 \"{record.Key}\" 与更早一条记录冲突）");
                }

                byLogicalId.Add(info.LogicalId, info);
                all.Add(info);

                if (!byCategory.TryGetValue(info.Category, out var list))
                {
                    list = new List<DisplayInfo>();
                    byCategory[info.Category] = list;
                }
                list.Add(info);
            }

            _byLogicalId = byLogicalId;
            _all = all;
            _byCategory = byCategory;
        }
    }
}

using System;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// 按模板生成/移除游戏对象实体（惯例同 07 第 2 节 <c>Creature</c> 一侧的
    /// <c>Core.Carriers.Common.ICreatureFactory</c>，但本类型不对外暴露为
    /// <c>core/carriers/common</c> 契约接口——07 第 9 节契约汇总表 GameObject 行只列出
    /// <c>GameObjectHost</c>（<c>interact</c>/<c>tryUnlock</c>）一个契约，"按模板生成实例"不是其它
    /// 模块需要跨模块调用的能力，故只在本模块内部提供，不新增 common 契约）。<see cref="Spawn"/> 本身
    /// 不读 <c>gobj.template</c> 装配任何字段——<see cref="GameObjectEntity"/> 只持有
    /// <c>templateId</c>/<c>lockId</c>，具体模板字段（<c>kind</c>/<c>type_data</c>/...）由
    /// <see cref="GameObjectHost"/> 按需通过 <c>IDataRegistryView</c> 现查现解析（同
    /// <c>core/rules/skill</c> 的 <c>SkillDefCache</c> 惯例之外的另一种选择：本模块记录条数少、不在
    /// 每帧热路径上，未引入缓存层，见 schema/README.md"判断记录"）。这一条只约束 <see cref="Spawn"/>
    /// 本身的语义（调用方决定 <c>lockId</c>，不强制照抄模板）；<see cref="SpawnFromTemplate"/> 是消费方
    /// 反馈第十三批新增的便捷方法，专门解决"按模板生成、锁默认照抄模板"这一常见场景，见该方法判断记录。
    /// </summary>
    public sealed class GameObjectFactory
    {
        private readonly IWorldSim _world;

        public GameObjectFactory(IWorldSim world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>在 <paramref name="mapId"/> 的 <paramref name="position"/>/<paramref name="facing"/>
        /// 生成一个游戏对象实体，<paramref name="lockId"/> 缺省时可后续由调用方按
        /// <c>GameObjectTemplate.LockId</c> 显式设置 <see cref="GameObjectEntity.LockId"/>。返回生成的
        /// 运行期实体 id（<c>"gobj.inst_&lt;n&gt;"</c>，见 <see cref="IWorldSim.AllocateEntityId"/>）。
        /// <para>
        /// CR150-02 根治：<paramref name="originKey"/> 缺省（<c>null</c>，绝大多数调用方，包括
        /// <c>SpawnHost</c> 经 <c>SpawnOptions.GobjSpawner</c> 间接调用本方法时的默认用法）时按
        /// <see cref="BuildFallbackOriginKey"/> 自动合成一个"地图 + 位置 + 模板"稳定键写入
        /// <see cref="GameObjectEntity.OriginKey"/>——刷新点的摆放位置（<c>spawn.table.position</c>）
        /// 是数据登记的固定值，同一刷新点历次重新生成（<c>ClearAll</c> 后重进地图）都会传入完全相同
        /// 的 <paramref name="mapId"/>/<paramref name="position"/>/<paramref name="templateId"/>，
        /// 自动合成出的键因此天然稳定，不需要调用方显式传入刷新点 id 才能获得跨重建关联能力。显式
        /// 传入 <paramref name="originKey"/>（如未来需要按刷新点 id 而非坐标关联）会覆盖自动合成。
        /// </para>
        /// </summary>
        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? lockId = null, Id? originKey = null)
        {
            var entityId = _world.AllocateEntityId(EntityKinds.Gobj);
            var entity = new GameObjectEntity(entityId, mapId, templateId, lockId)
            {
                Position = position,
                Facing = facing,
                OriginKey = originKey ?? BuildFallbackOriginKey(mapId, templateId, position),
            };

            _world.AddEntity(entity);
            return entityId;
        }

        /// <summary>
        /// 按 <c>gobj.template</c> 记录生成一个实体，<paramref name="lockId"/> 取模板声明的
        /// <see cref="GameObjectTemplate.LockId"/>（即顶层 <c>gobj.template.lock_id</c>）——本方法是
        /// <see cref="Spawn"/> 的便捷封装，不改变 <see cref="Spawn"/> 本身"调用方决定 lockId"的既有
        /// 语义（手工放置的物件仍可直接调 <see cref="Spawn"/> 并自行传入/覆盖 <paramref
        /// name="lockId"/>）。
        /// <para>
        /// 消费方反馈第十三批根治：框架默认生成路径（<c>core/gameplay/assembly/GameplayAssembly.cs</c>
        /// 给 <c>SpawnOptions.GobjSpawner</c> 装的默认委托）与示例 <c>GameFoundationBootstrap</c> 此前
        /// 各自手写一遍"读模板 → 取 lock_id → Spawn"（唯一正确示范只有测试辅助
        /// <c>core/carriers/gobj/tests/GobjTestSupport.SpawnFromTemplate</c>），导致框架自己的默认
        /// 组装路径经 <c>spawn.table</c> 刷出的物件从不上锁。判断记录：与其让"读模板→取锁→Spawn"这套
        /// 逻辑在默认委托、示例两处分别重复一遍，不如在本类型（读模板本就属于 <c>core/carriers/gobj</c>
        /// 领域内的语义，未越过"不引用 <c>Core.Gameplay</c>"边界）新增一个 ABI 纯新增的便捷入口，两处
        /// 调用方改调同一份实现，不再各自维护一份读模板逻辑。读取模板的方式与
        /// <c>GobjTestSupport.SpawnFromTemplate</c>/<see cref="GameObjectTemplate.FromRecord"/> 完全
        /// 一致，不另写一套解析。
        /// </para>
        /// </summary>
        public Id SpawnFromTemplate(
            IDataRegistryView registry, Id templateId, Id mapId, Vec2 position, double facing, Id? originKey = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var record = registry.Get(GobjSchemas.Template.Name, templateId)
                ?? throw new InvalidOperationException($"gobj.template \"{templateId}\" 不存在");
            var template = GameObjectTemplate.FromRecord(record);
            return Spawn(templateId, mapId, position, facing, template.LockId, originKey);
        }

        /// <summary>见 <see cref="Spawn"/> 判断记录：坐标分量必须编码成 <see cref="Id"/> 允许的字符集
        /// （<c>^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$</c>，不含负号、大写字母，见该类型格式说明）——不能
        /// 直接用 <c>ToString("G17")</c>/<c>ToString("R")</c> 这类往返精度格式化：负坐标会带负号、
        /// 极大/极小值可能落入大写 <c>E</c> 科学计数法，两者都会让拼出来的字符串直接在 <see
        /// cref="Id"/> 构造期抛异常。改用固定 6 位小数（<c>"F6"</c>，00 第 4.1 节"逻辑均为二维平面
        /// 坐标"这一架构定位下的常规世界坐标精度足够，不会出现需要科学计数法表示的量级）+
        /// <see cref="CultureInfo.InvariantCulture"/>；符号编码成前缀字母（<c>n</c>/<c>p</c>）而不是
        /// 保留 <c>-</c>，小数点替换成 <c>_</c> 以落在允许字符集内。同一 <see cref="Vec2"/> 值在任何
        /// 本地化环境/多次调用下都合成出逐字节相同的键；不同地图/位置/模板的组合、或坐标只在第 7 位
        /// 小数之后才有差异的极小概率情形，都视为"同一个摆放点"，属预期的合并，不是冲突。</summary>
        private static Id BuildFallbackOriginKey(Id mapId, Id templateId, Vec2 position)
        {
            var x = EncodeCoordinate(position.X);
            var y = EncodeCoordinate(position.Y);
            return new Id($"gobj.origin.{mapId.Value}.{templateId.Value}.{x}_{y}");
        }

        private static string EncodeCoordinate(double value)
        {
            var sign = value < 0 ? "n" : "p";
            var magnitudeText = Math.Abs(value).ToString("F6", CultureInfo.InvariantCulture).Replace('.', '_');
            return sign + magnitudeText;
        }

        /// <summary>移除一个游戏对象实体（见 <see cref="IWorldSim.MarkForDestruction"/>：真正的移除
        /// 发生在下一次生命周期清理阶段）。07 第 9 节契约汇总表未给 GameObject 登记
        /// <c>gobj.despawned</c>/<c>gobj.spawned</c> 一类事件（不同于 <c>creature.spawned</c>/
        /// <c>creature.despawned</c>），本方法不额外发事件，只依赖 <see cref="IWorldSim"/> 自身的
        /// <c>entity.created</c>/<c>entity.destroyed</c> 通用事件。</summary>
        public void Despawn(Id entityId) => _world.MarkForDestruction(entityId);
    }
}

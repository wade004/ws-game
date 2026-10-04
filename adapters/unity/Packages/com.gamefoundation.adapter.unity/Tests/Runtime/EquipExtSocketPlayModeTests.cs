#nullable enable
// EquipExtSocketPlayModeTests：副手与挂点装备夹具（lab/fixtures/data/equip_ext，ADR-0153，手感设计/08 第 2 节）的挂点附着验收。
//
// 占位提灯是 socket_attach 外观：挂点 socket.off_hand 由 model 型 display.map 行（display.map.std_socket_host，model_ref 指向占位假人模型预制体，
// 预制体带 socket.main_hand / socket.off_hand 两个挂点）声明。用真实数据（独立数据根 + 占位装备集 + 框架数据）+ 真实 EquipmentVisualSource +
// UnityViewFactory 的生产装配路径，验证：装备后子模型实例挂在数据声明的挂点（副手挂点）下而不是主手挂点下、卸装后清理、重复卸装幂等；
// 以及数据不变量：每个 socket_attach 外观行的挂点都被某个 model 型 display.map 行声明（与导入校验 equip_model_socket_unknown 同一规则）。
// 期望值取自数据行（挂点 id 取自 display.equip_visual.socket_id），不写裸字符串以外的常量。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:render")]
    public sealed class EquipExtSocketPlayModeTests : PlayModeTestBase
    {
        private static readonly Id HostLogicalId = new Id("creature.std_socket_host");
        private static readonly Id LanternTemplateId = new Id("item.std_lantern");

        private (IEventBus Bus, IDataRegistryView Registry, IDisplayInfoRegistry DisplayInfo, UnityEngineHost Host) BuildFixture()
        {
            var host = UnityEngineHost.Ensure();
            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");
            var feelSource = new FileSystemDataSource(contentFs, "data/_feel");
            var equipSource = new FileSystemDataSource(contentFs, "data/_equip");
            var extSource = new FileSystemDataSource(contentFs, "lab/fixtures/data/equip_ext");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(extSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, feelSource, equipSource, extSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var displayInfo = new DisplayInfoRegistry(registry, bus);
            return (bus, registry, displayInfo, host);
        }

        private static IReadOnlyDictionary<Id, EquipVisualDef> BuildCatalogByTemplateId(IDataRegistryView registry)
        {
            var catalog = new Dictionary<Id, EquipVisualDef>();
            foreach (var record in registry.GetAll("display.equip_visual"))
            {
                var def = EquipVisualDef.FromRecord(record);
                catalog[def.ItemId] = def;
            }

            return catalog;
        }

        [Test]
        public void SocketAttachItem_AttachesToTheDataDeclaredOffHandSocket_NotTheMainHandOne_UnequipClears_RepeatIsIdempotent()
        {
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);
            var visual = catalog[LanternTemplateId];
            Assert.AreEqual(EquipVisualMode.SocketAttach, visual.Mode, "提灯的外观行是 socket_attach");
            var socketName = visual.SocketId!.Value.Value;

            var equipSource = new EquipmentVisualSource(fx.Bus, catalog);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var entityId = new Id("unit.equip_ext_socket_test");
            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, HostLogicalId, entityId);
            view.Bind(entityId);
            var hostHandle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var visualRoot = renderer3D.GetModelVisualRoot(hostHandle)!;

            var offHand = FindDeep(visualRoot, socketName);
            Assert.IsNotNull(offHand, "宿主模型应当带有数据声明的挂点 " + socketName);
            var mainHand = FindDeep(visualRoot, "socket.main_hand");
            Assert.IsNotNull(mainHand);
            Assert.AreNotSame(offHand, mainHand, "副手挂点与主手挂点是两个不同的节点");
            Assert.AreEqual(0, offHand!.childCount);
            Assert.AreEqual(0, mainHand!.childCount);

            // 经真实总线广播入包与装备，并手工转发给视图（同 EquipVisualSocketClearTests 的两路投递说明）。
            var instanceId = new Id("item_instance.lantern_1");
            var slotId = new Id("item.slot.std_trinket");
            fx.Bus.PublishImmediate(new ItemAddedEvent(entityId, instanceId, LanternTemplateId, count: 1));
            var equipEvt = new ItemEquippedEvent(entityId, instanceId, slotId);
            fx.Bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);

            Assert.AreEqual(1, offHand.childCount, "装备后子模型实例挂在数据声明的挂点下");
            Assert.AreEqual(0, mainHand.childCount, "没有挂到主手挂点");

            var unequipEvt = new ItemUnequippedEvent(entityId, slotId, instanceId);
            fx.Bus.PublishImmediate(unequipEvt);
            view.OnEvent(unequipEvt);
            Assert.AreEqual(0, offHand.childCount, "卸装后挂点下的子模型实例被清理");
            Assert.DoesNotThrow(() => view.OnEvent(unequipEvt), "重复卸装幂等");

            equipSource.Dispose();
            view.Destroy();
        }

        [Test]
        public void EverySocketAttachRow_TargetsASocketDeclaredByAModelDisplayRow_AndTheOffHandSocketIsDeclared()
        {
            var fx = BuildFixture();
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in fx.Registry.GetAll("display.map"))
            {
                if (row.TryGetString("kind", out var kind) && kind == "model" && row.TryGetIdList("sockets", out var sockets))
                {
                    foreach (var s in sockets)
                    {
                        declared.Add(s.Value);
                    }
                }
            }

            var attachRows = fx.Registry.GetAll("display.equip_visual")
                .Where(r => r.TryGetString("mode", out var mode) && mode == "socket_attach").ToList();
            Assert.IsNotEmpty(attachRows, "夹具里至少有一件 socket_attach 外观的装备");
            foreach (var row in attachRows)
            {
                Assert.IsTrue(row.TryGetString("socket_id", out var socket) && declared.Contains(socket), row.Key + " 的挂点必须被 model 型 display.map 行声明");
            }

            CollectionAssert.Contains(declared, "socket.off_hand");
        }

        private static Transform? FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}

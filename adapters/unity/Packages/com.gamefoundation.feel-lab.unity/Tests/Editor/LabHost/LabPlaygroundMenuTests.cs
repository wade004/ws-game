#nullable enable
// LabPlaygroundMenuTests：手感试玩菜单的结构不变量（菜单分层）。
// 菜单条目只读反射 LabPlaygroundSceneBuilder 的 MenuItem 属性，不打开任何场景；期望值由规则得出：
// 顶层不放入口，占位美术入口收进 工程场景（占位美术）/，重建入口收进 维护/；占位场景覆盖三个视角。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FeelLab.Unity;
using FeelLab.Unity.Editor;
using NUnit.Framework;
using UnityEditor;

namespace FeelLab.Unity.Tests.Editor
{
    [Category("module:lab")]
    public sealed class LabPlaygroundMenuTests
    {
        private const string Root = "GameFoundation/手感试玩/";
        private const string PlaceholderSub = "工程场景（占位美术）/";
        private const string MaintenanceSub = "维护/";

        private sealed class Entry
        {
            public string Method = string.Empty;
            public string Path = string.Empty;
            public int Priority;

            /// <summary>相对 Root 的子路径（不含条目名）；顶层为空串。</summary>
            public string Sub => Path.Substring(Root.Length).Contains('/') ? Path.Substring(Root.Length, Path.Substring(Root.Length).LastIndexOf('/') + 1) : string.Empty;

            public string Leaf => Path.Substring(Path.LastIndexOf('/') + 1);
        }

        private static List<Entry> Entries()
        {
            var result = new List<Entry>();
            foreach (var m in typeof(LabPlaygroundSceneBuilder).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                foreach (var attr in m.GetCustomAttributes<MenuItem>())
                {
                    if (attr.menuItem.StartsWith(Root, StringComparison.Ordinal))
                    {
                        result.Add(new Entry { Method = m.Name, Path = attr.menuItem, Priority = attr.priority });
                    }
                }
            }

            return result;
        }

        [Test]
        public void TopLevel_HoldsNoSceneEntries_EverythingLivesUnderTheTwoSubmenus()
        {
            // 判断记录（ADR-0160）：真实美术的演示场景入口迁往样板仓库，本包的菜单里顶层不放场景入口；样板自己在同一个根菜单下追加顶层入口。
            var entries = Entries();
            Assert.AreEqual(0, entries.Count(e => e.Sub.Length == 0), "本包顶层不放入口：" + string.Join(", ", entries.Where(e => e.Sub.Length == 0).Select(e => e.Path)));
        }

        [Test]
        public void PlaceholderEntries_LiveUnderPlaceholderSubmenu_AndRebuildEntryUnderMaintenance()
        {
            var entries = Entries();
            var placeholders = entries.Where(e => e.Sub == PlaceholderSub).ToList();
            CollectionAssert.AreEquivalent(new[] { "Open2D", "Open25D", "Open3D" }, placeholders.Select(e => e.Method).ToList(), "三个占位美术入口收进 工程场景（占位美术）/");
            foreach (var e in placeholders)
            {
                StringAssert.Contains("占位", e.Path);
            }

            var maintenance = entries.Where(e => e.Sub == MaintenanceSub).ToList();
            CollectionAssert.AreEquivalent(new[] { "BuildAll" }, maintenance.Select(e => e.Method).ToList(), "重建入口收进 维护/");
            Assert.AreEqual(entries.Count, entries.Count(e => e.Sub == PlaceholderSub || e.Sub == MaintenanceSub), "只有这两个子菜单");
        }

        [Test]
        public void PlaceholderScenes_CoverTheThreeViewpoints_AndSceneNamesFollowTheCell()
        {
            CollectionAssert.AreEquivalent(new[] { "2d_action", "2_5d_action", "3d_action" }, LabPlaygroundSceneBuilder.Cells, "占位场景覆盖三个视角各一个");
            foreach (var cell in LabPlaygroundSceneBuilder.Cells)
            {
                StringAssert.EndsWith("LabPlayground_" + cell + ".unity", LabPlaygroundSceneBuilder.ScenePath(cell));
            }
        }
    }
}

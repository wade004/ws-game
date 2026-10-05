#nullable enable
// LabPlaygroundMenuTests：手感试玩菜单的结构不变量（菜单分层，演示场景三视角）。
// 菜单条目只读反射 LabPlaygroundSceneBuilder 的 MenuItem 属性，不打开任何场景；期望值由规则得出：
// 顶层只放三个真实美术演示场景入口（优先级连续、排在所有子菜单之前），占位美术入口收进 工程场景（占位美术）/，重建入口收进 维护/；
// 每个视角的占位场景提示点名的恰是同一视角的演示场景菜单条目；重建演示场景覆盖三个视角。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adapter.Unity.LabHost;
using Adapter.Unity.LabHost.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Adapter.Unity.Tests.LabHost.Editor
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

        // 方法名 -> 它打开的演示场景格子（按方法名固定，改名要同步这张表）。
        private static readonly KeyValuePair<string, string>[] ShowcaseOpeners =
        {
            new KeyValuePair<string, string>("OpenShowcase", "2d_action"),
            new KeyValuePair<string, string>("OpenShowcase25D", "2_5d_action"),
            new KeyValuePair<string, string>("OpenShowcase3d", "3d_action"),
        };

        [Test]
        public void TopLevel_HoldsOnlyTheThreeShowcaseEntries_ConsecutivePriorities_BeforeEverySubmenu()
        {
            var entries = Entries();
            var top = entries.Where(e => e.Sub.Length == 0).OrderBy(e => e.Priority).ToList();
            CollectionAssert.AreEqual(ShowcaseOpeners.Select(o => o.Key).ToList(), top.Select(e => e.Method).ToList(), "顶层只放三个演示场景入口，按 2D、2.5D、3D 排序");
            for (var i = 1; i < top.Count; i++)
            {
                Assert.AreEqual(top[i - 1].Priority + 1, top[i].Priority, "三个演示场景入口的优先级应连续（同一组，菜单里不出分隔线）");
            }

            var lastTop = top.Max(e => e.Priority);
            foreach (var e in entries.Where(e => e.Sub.Length > 0))
            {
                Assert.Greater(e.Priority, lastTop, e.Path + " 应排在演示场景入口之后");
            }

            foreach (var e in top)
            {
                StringAssert.EndsWith("（真实美术）", e.Leaf, "顶层入口都标明真实美术：" + e.Path);
            }
        }

        [Test]
        public void PlaceholderEntries_LiveUnderPlaceholderSubmenu_AndRebuildEntriesUnderMaintenance()
        {
            var entries = Entries();
            var placeholders = entries.Where(e => e.Sub == PlaceholderSub).ToList();
            CollectionAssert.AreEquivalent(new[] { "Open2D", "Open25D", "Open3D" }, placeholders.Select(e => e.Method).ToList(), "三个占位美术入口收进 工程场景（占位美术）/");
            foreach (var e in placeholders)
            {
                StringAssert.Contains("占位", e.Path);
            }

            var maintenance = entries.Where(e => e.Sub == MaintenanceSub).ToList();
            CollectionAssert.AreEquivalent(new[] { "BuildAll", "BuildShowcase" }, maintenance.Select(e => e.Method).ToList(), "两个重建入口收进 维护/");
            Assert.AreEqual(entries.Count, entries.Count(e => e.Sub.Length == 0 || e.Sub == PlaceholderSub || e.Sub == MaintenanceSub), "除顶层外只有这两个子菜单");
        }

        [Test]
        public void EachShowcaseOpener_IsNamedByTheSameComboHint_AndOpensThatCombosScene()
        {
            var entries = Entries();
            foreach (var opener in ShowcaseOpeners)
            {
                var cell = opener.Value;
                var menuName = LabPlayground.ShowcaseMenuNameOf(cell);
                Assert.IsNotNull(menuName, cell);
                var entry = entries.Single(e => e.Method == opener.Key);
                Assert.AreEqual(Root + menuName, entry.Path, cell + "：菜单条目名与占位场景提示点名的名字是同一个");
                StringAssert.Contains(menuName!, LabPlayground.PlaceholderHintFor(cell), cell + "：占位场景提示点名同一视角的演示场景");
                CollectionAssert.Contains(LabPlaygroundSceneBuilder.ShowcaseCells, cell, "重建演示场景覆盖 " + cell);
                StringAssert.EndsWith("LabShowcase_" + cell + ".unity", LabPlaygroundSceneBuilder.ShowcaseScenePathOf(cell));
            }

            CollectionAssert.AreEquivalent(ShowcaseOpeners.Select(o => o.Value).ToList(), LabPlaygroundSceneBuilder.ShowcaseCells, "重建演示场景恰好构建三个视角各一个");
        }
    }
}

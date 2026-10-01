#nullable enable
// EditorSetupTypesTests：测试覆盖第四批 T-L15——包内 Editor 程序集（Adapter.Unity.Editor）里的两个类型
// ProjectSetup 与 EnsureAdditiveShaderAlwaysIncluded 此前零直接引用，只靠门禁“Unity 编译/构建”步骤间接执行。
//
// 判断记录（用反射按程序集限定名取类型，而不是给本测试 asmdef 加 Adapter.Unity.Editor 引用并给包加
// InternalsVisibleTo）：EnsureAdditiveShaderAlwaysIncluded 是 internal 类，可见性要靠生产代码新增
// 程序集特性；而本任务口径是“只加/改测试”。反射只依赖两个稳定的类型/方法全名，缺失时用例会明确失败
// （不是静默跳过），改名时同步改常量即可。
//
// 两个类型都是“幂等地修改工程设置”的 Editor 工具，所以本文件的核心断言是：
//  ① 终态正确（URP 2D 管线接线、排序轴、Always Included Shaders 含 additive 占位着色器、输入处理方案）；
//  ② 幂等（再执行一次，被它们管理的磁盘文件字节不变）；
//  ③ EnsureAdditive...：条目被人为移除后再 Run 能补回，且恰好一份；用例结束把列表还原成原顺序。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class EditorSetupTypesTests
    {
        private const string EditorAssembly = "Adapter.Unity.Editor";
        private const string EnsureTypeName = "Adapter.Unity.Editor.EnsureAdditiveShaderAlwaysIncluded";
        private const string ProjectSetupTypeName = "GameFoundation.Setup.ProjectSetup";
        private const string AdditiveShaderName = "GameFoundation/Vfx/AdditiveUnlit";
        private const string GraphicsSettingsAssetPath = "ProjectSettings/GraphicsSettings.asset";
        private const string AlwaysIncluded = "m_AlwaysIncludedShaders";

        private static Type RequireType(string fullName)
        {
            var type = Type.GetType(fullName + ", " + EditorAssembly, throwOnError: false);
            Assert.IsNotNull(type, $"找不到 {fullName}（程序集 {EditorAssembly}）——类型改名/移动了？");
            return type!;
        }

        private static void InvokeStatic(string typeName, string methodName)
        {
            var type = RequireType(typeName);
            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"{typeName}.{methodName} 不存在");
            try
            {
                method!.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        private static string Sha(string relativeToProject)
        {
            var path = Path.Combine(ProjectRoot, relativeToProject);
            if (!File.Exists(path))
            {
                return "<missing>";
            }

            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        // ------------------------------------------------------------------
        // EnsureAdditiveShaderAlwaysIncluded
        // ------------------------------------------------------------------

        private static SerializedObject OpenGraphicsSettings()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsAssetPath);
            Assert.IsNotNull(assets);
            Assert.Greater(assets.Length, 0, "找不到 " + GraphicsSettingsAssetPath);
            return new SerializedObject(assets[0]);
        }

        private static List<UnityEngine.Object?> ReadAlwaysIncluded()
        {
            var so = OpenGraphicsSettings();
            var prop = so.FindProperty(AlwaysIncluded);
            Assert.IsNotNull(prop, AlwaysIncluded + " 字段缺失");
            var list = new List<UnityEngine.Object?>();
            for (var i = 0; i < prop.arraySize; i++)
            {
                list.Add(prop.GetArrayElementAtIndex(i).objectReferenceValue);
            }

            return list;
        }

        private static void WriteAlwaysIncluded(IReadOnlyList<UnityEngine.Object?> entries)
        {
            var so = OpenGraphicsSettings();
            var prop = so.FindProperty(AlwaysIncluded);
            prop.arraySize = entries.Count;
            for (var i = 0; i < entries.Count; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static Shader RequireAdditiveShader()
        {
            var shader = Shader.Find(AdditiveShaderName);
            Assert.IsNotNull(shader, $"前置条件：工作台工程应带着色器 {AdditiveShaderName}（Assets/Shaders/VfxAdditiveUnlit.shader）");
            return shader;
        }

        [Test]
        public void EnsureAdditiveShader_RunIsIdempotent_ShaderPresentExactlyOnce_ListUnchangedByAnExtraRun()
        {
            var shader = RequireAdditiveShader();
            InvokeStatic(EnsureTypeName, "Run");
            var afterFirst = ReadAlwaysIncluded();
            Assert.AreEqual(1, afterFirst.Count(e => e == shader), "Run 之后 additive 占位着色器应恰好出现一次");

            var fileBefore = Sha(GraphicsSettingsAssetPath);
            InvokeStatic(EnsureTypeName, "Run");
            var afterSecond = ReadAlwaysIncluded();

            CollectionAssert.AreEqual(afterFirst, afterSecond, "再执行一次不应改动列表内容与顺序");
            Assert.AreEqual(fileBefore, Sha(GraphicsSettingsAssetPath), "已注册时 Run 是空操作，不应重写 GraphicsSettings.asset");
        }

        [Test]
        public void EnsureAdditiveShader_EntryRemovedByHand_RunAddsItBackExactlyOnce_PreservingOtherEntries()
        {
            var shader = RequireAdditiveShader();
            InvokeStatic(EnsureTypeName, "Run");
            var original = ReadAlwaysIncluded();
            Assert.AreEqual(1, original.Count(e => e == shader));

            try
            {
                var withoutShader = original.Where(e => e != shader).ToList();
                WriteAlwaysIncluded(withoutShader);
                Assert.AreEqual(0, ReadAlwaysIncluded().Count(e => e == shader), "前置：手工移除应生效");

                InvokeStatic(EnsureTypeName, "Run");

                var restored = ReadAlwaysIncluded();
                Assert.AreEqual(1, restored.Count(e => e == shader), "移除后 Run 应补回，且恰好一份");
                CollectionAssert.AreEqual(withoutShader, restored.Where(e => e != shader).ToList(), "不得动消费方已有的其它条目及其顺序");
                Assert.AreSame(shader, restored[restored.Count - 1], "补回的条目追加在末尾");
            }
            finally
            {
                // 还原成用例开始前的精确顺序，避免把 GraphicsSettings.asset 留下仅顺序不同的改动。
                WriteAlwaysIncluded(original);
            }

            CollectionAssert.AreEqual(original, ReadAlwaysIncluded(), "用例结束后列表应与开始时完全一致");
        }

        // ------------------------------------------------------------------
        // ProjectSetup.Configure2D
        // ------------------------------------------------------------------

        private static readonly string[] ManagedFiles =
        {
            "Assets/Settings/Renderer2DData.asset",
            "Assets/Settings/URP-2D-Pipeline.asset",
            "ProjectSettings/ProjectSettings.asset",
        };

        [Test]
        public void ProjectSetup_Configure2D_LeavesUrp2DPipelineAsDefaultEverywhere_AndInputSystemActive()
        {
            InvokeStatic(ProjectSetupTypeName, "Configure2D");

            var defaultPipeline = GraphicsSettings.defaultRenderPipeline;
            Assert.IsNotNull(defaultPipeline, "Configure2D 之后应有默认渲染管线");
            Assert.AreEqual("UniversalRenderPipelineAsset", defaultPipeline.GetType().Name);
            Assert.AreEqual("Assets/Settings/URP-2D-Pipeline.asset", AssetDatabase.GetAssetPath(defaultPipeline));

            // 每个 Quality 档位都应指向同一份管线资产。
            var levelCount = QualitySettings.names.Length;
            Assert.Greater(levelCount, 0);
            for (var i = 0; i < levelCount; i++)
            {
                Assert.AreSame(defaultPipeline, QualitySettings.GetRenderPipelineAssetAt(i),
                    $"Quality 档位 {i}（{QualitySettings.names[i]}）应使用同一份 URP 2D 管线");
            }

            // 2D Renderer 的透明排序：自定义轴 (0,1,0)（Y 排序）。
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/Renderer2DData.asset");
            Assert.IsNotNull(rendererData);
            var so = new SerializedObject(rendererData);
            Assert.AreEqual((int)TransparencySortMode.CustomAxis, so.FindProperty("m_TransparencySortMode").intValue);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), so.FindProperty("m_TransparencySortAxis").vector3Value);

            // Active Input Handling = 1（Input System Package）。
            var projectSettingsText = File.ReadAllText(Path.Combine(ProjectRoot, "ProjectSettings", "ProjectSettings.asset"));
            StringAssert.IsMatch(@"(?m)^\s*activeInputHandler:\s*1\s*$", projectSettingsText);
        }

        [Test]
        public void ProjectSetup_Configure2D_IsIdempotent_ManagedAssetFilesAreByteIdenticalAfterAnotherRun()
        {
            InvokeStatic(ProjectSetupTypeName, "Configure2D");
            var before = ManagedFiles.ToDictionary(f => f, Sha);
            foreach (var kv in before)
            {
                Assert.AreNotEqual("<missing>", kv.Value, kv.Key + " 应存在");
            }

            InvokeStatic(ProjectSetupTypeName, "Configure2D");

            foreach (var file in ManagedFiles)
            {
                Assert.AreEqual(before[file], Sha(file), file + " 在第二次 Configure2D 之后不应有任何字节变化");
            }
        }
    }
}

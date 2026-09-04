using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameFoundation.Setup
{
    /// <summary>
    /// 工作台工程的一次性批处理设置脚本。
    /// 只负责搭建 URP + 2D Renderer 的图形管线配置与 Input System 的激活输入方案，
    /// 不包含任何具体游戏逻辑。所有方法均设计为幂等，可重复执行。
    /// </summary>
    public static class ProjectSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string RendererDataPath = SettingsFolder + "/Renderer2DData.asset";
        private const string PipelineAssetPath = SettingsFolder + "/URP-2D-Pipeline.asset";

        private static readonly Vector3 DesiredTransparencySortAxis = new Vector3(0f, 1f, 0f);

        /// <summary>
        /// 配置 2D 渲染管线：创建（或复用）URP Asset 与 2D Renderer Data，
        /// 设为工程默认管线并应用到所有 Quality 档位；同时把 Active Input Handling
        /// 切到 Input System Package。可通过 -executeMethod 批处理调用，也可重复执行。
        /// </summary>
        [MenuItem("GameFoundation/Setup/Configure 2D Render Pipeline")]
        public static void Configure2D()
        {
            EnsureSettingsFolder();

            Renderer2DData rendererData = LoadOrCreateRendererData();
            UniversalRenderPipelineAsset pipelineAsset = LoadOrCreatePipelineAsset(rendererData);

            ConfigureTransparencySort(rendererData);
            ApplyAsDefaultPipeline(pipelineAsset);
            ApplyToAllQualityLevels(pipelineAsset);
            ConfigureInputSystem();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GameFoundation.Setup] Configure2D 完成：URP + 2D Renderer 已生成并生效，Active Input Handling 已切到 Input System Package。");
        }

        private static void EnsureSettingsFolder()
        {
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }
        }

        private static Renderer2DData LoadOrCreateRendererData()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererDataPath);
            if (existing != null)
            {
                return existing;
            }

            var rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
            AssetDatabase.CreateAsset(rendererData, RendererDataPath);
            ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
            return rendererData;
        }

        private static UniversalRenderPipelineAsset LoadOrCreatePipelineAsset(Renderer2DData rendererData)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (existing != null)
            {
                return existing;
            }

            var pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
            return pipelineAsset;
        }

        private static void ConfigureTransparencySort(Renderer2DData rendererData)
        {
            var serializedObject = new SerializedObject(rendererData);
            var modeProperty = serializedObject.FindProperty("m_TransparencySortMode");
            var axisProperty = serializedObject.FindProperty("m_TransparencySortAxis");

            bool changed = false;

            if (modeProperty.intValue != (int)TransparencySortMode.CustomAxis)
            {
                modeProperty.intValue = (int)TransparencySortMode.CustomAxis;
                changed = true;
            }

            if (axisProperty.vector3Value != DesiredTransparencySortAxis)
            {
                axisProperty.vector3Value = DesiredTransparencySortAxis;
                changed = true;
            }

            if (changed)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rendererData);
            }
        }

        private static void ApplyAsDefaultPipeline(UniversalRenderPipelineAsset pipelineAsset)
        {
            if (GraphicsSettings.defaultRenderPipeline != pipelineAsset)
            {
                GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            }
        }

        private static void ApplyToAllQualityLevels(UniversalRenderPipelineAsset pipelineAsset)
        {
            int originalLevel = QualitySettings.GetQualityLevel();
            string[] levelNames = QualitySettings.names;

            for (int i = 0; i < levelNames.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                if (QualitySettings.renderPipeline != pipelineAsset)
                {
                    QualitySettings.renderPipeline = pipelineAsset;
                }
            }

            QualitySettings.SetQualityLevel(originalLevel, applyExpensiveChanges: false);
        }

        /// <summary>
        /// 把 Active Input Handling 切到 Input System Package。
        /// 当前 Unity 版本的公开 PlayerSettings API 不包含 activeInputHandling
        /// （编译期已验证：CS0117），因此退回到直接改写
        /// ProjectSettings/ProjectSettings.asset 文本中的 activeInputHandler 字段（1 = Input System Package）。
        /// 该方法幂等：字段已是目标值时不重复写入。
        /// </summary>
        private static void ConfigureInputSystem()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string settingsPath = Path.Combine(projectRoot, "ProjectSettings", "ProjectSettings.asset");

            if (!File.Exists(settingsPath))
            {
                Debug.LogWarning("[GameFoundation.Setup] 未找到 ProjectSettings/ProjectSettings.asset，跳过 Active Input Handling 设置。");
                return;
            }

            string text = File.ReadAllText(settingsPath);
            const string pattern = @"(?m)^(\s*activeInputHandler:\s*)\d+\s*$";
            var match = Regex.Match(text, pattern);

            if (!match.Success)
            {
                Debug.LogWarning("[GameFoundation.Setup] 未在 ProjectSettings.asset 中找到 activeInputHandler 字段，请手动确认 Active Input Handling 设置。");
                return;
            }

            string replacement = match.Groups[1].Value + "1";
            if (match.Value == replacement)
            {
                return;
            }

            string newText = text.Substring(0, match.Index) + replacement + text.Substring(match.Index + match.Length);
            File.WriteAllText(settingsPath, newText);
            Debug.Log("[GameFoundation.Setup] 已通过文本方式将 ProjectSettings.asset 的 activeInputHandler 设为 1（Input System Package）。");
        }
    }
}

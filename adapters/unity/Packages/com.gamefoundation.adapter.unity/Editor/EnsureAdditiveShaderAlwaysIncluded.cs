using UnityEditor;
using UnityEngine;

namespace Adapter.Unity.Editor
{
    /// <summary>ADR-0074 落地缺陷修复（2026-09-23，消费方反馈第二十四批）：<see
    /// cref="global::Adapter.Unity.EngineAdapter.EffectSequencePlayer"/> 的 additive 占位材质改在
    /// 运行期用 <c>Shader.Find</c> 现构造（见该类型 GetAdditiveMaterial 判断记录），不再经
    /// <c>Resources.Load</c>——但 Standalone Player 构建期的着色器裁剪只保留"确有资产引用"或
    /// "在 Always Included Shaders 列表"的着色器，本包自带的 <c>VfxAdditiveUnlit.shader</c> 运行期
    /// 从不被任何序列化资产引用（正是为了绕开 Resources.Load 在只读注册表包里的限制），若不做任何
    /// 处理会在打包时被裁掉，导致 <c>Shader.Find</c> 在构建产物里同样落空。
    ///
    /// 判断记录（为什么用 <c>[InitializeOnLoad]</c> 自动注册，不写进 games/_template/README.md
    /// 让消费方手工勾选 Always Included Shaders）：消费方反馈第二十四批決策 2——框架发布出去的能力，
    /// 在标准安装形态下必须直接可用，不接受"消费方自己再配置一步"作为交付。本类型每次域重新加载都
    /// 会自动核对一次（幂等：着色器已在列表里时直接返回，不重复写入/不触发无意义的
    /// AssetDatabase.SaveAssets），消费方只要在工程里引用了本包，Always Included Shaders 列表就会
    /// 在 Editor 首次加载本包的那一刻自动补齐这一条，构建独立版时天然已经生效，不需要额外步骤。
    ///
    /// 判断记录（为什么直接改 <c>ProjectSettings/GraphicsSettings.asset</c> 而不是要求消费方自己
    /// 改）：这份 asset 是消费方工程自己的文件，不是本仓库/本包的文件，因此不违反"写域只有发布出去
    /// 的包内容"——本类型只追加一条 Shader 引用，不覆盖消费方已有的其它 Always Included Shaders
    /// 条目，幂等且可逆（消费方可以随时手工从列表里移除）。</summary>
    [InitializeOnLoad]
    internal static class EnsureAdditiveShaderAlwaysIncluded
    {
        private const string AdditiveShaderName = "GameFoundation/Vfx/AdditiveUnlit";
        private const string GraphicsSettingsAssetPath = "ProjectSettings/GraphicsSettings.asset";
        private const string AlwaysIncludedShadersPropertyName = "m_AlwaysIncludedShaders";

        static EnsureAdditiveShaderAlwaysIncluded()
        {
            Run();
        }

        internal static void Run()
        {
            var shader = Shader.Find(AdditiveShaderName);
            if (shader == null)
            {
                // 包版本过旧/着色器资产被人为删除：不是本类型职责范围，GetAdditiveMaterial 自己的
                // 一次性 warning 已经覆盖这个场景，这里不重复报错。
                return;
            }

            var graphicsSettingsAssets = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsAssetPath);
            if (graphicsSettingsAssets == null || graphicsSettingsAssets.Length == 0)
            {
                Debug.LogWarning(
                    "[EnsureAdditiveShaderAlwaysIncluded] 找不到 " + GraphicsSettingsAssetPath +
                    "，无法自动注册 additive 占位着色器到 Always Included Shaders，独立版构建时该着色器可能被裁剪。");
                return;
            }

            var so = new SerializedObject(graphicsSettingsAssets[0]);
            var prop = so.FindProperty(AlwaysIncludedShadersPropertyName);
            if (prop == null)
            {
                Debug.LogWarning(
                    "[EnsureAdditiveShaderAlwaysIncluded] " + GraphicsSettingsAssetPath + " 缺少 " +
                    AlwaysIncludedShadersPropertyName + " 字段，无法自动注册（Unity 版本变化？）。");
                return;
            }

            for (int i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    return; // 已注册，幂等短路。
                }
            }

            int newIndex = prop.arraySize;
            prop.InsertArrayElementAtIndex(newIndex);
            prop.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[EnsureAdditiveShaderAlwaysIncluded] 已将 " + AdditiveShaderName + " 注册进 Always Included Shaders。");
        }
    }
}

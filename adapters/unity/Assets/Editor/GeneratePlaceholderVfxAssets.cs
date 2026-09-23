#nullable enable
// GeneratePlaceholderVfxAssets：一次性（可重复运行）生成 ADR-0074 additive 混合模式所需的占位材质
// 资产，同 GeneratePlaceholderModelAssets.cs 一贯的"占位美术，生成器随包分发，消费方可自行重新
// 生成"惯例（见该文件顶部判断记录）。
//
// 产出（落在 Assets/Resources/GameFoundation/materials/ 下，与
// EffectSequencePlayer.GetAdditiveMaterial 的约定路径 "GameFoundation/materials/vfx_additive"
// 逐字对应）：
//   materials/vfx_additive.mat —— 引用 adapters/unity/Assets/Shaders/VfxAdditiveUnlit.shader
//     （逐字复用 URP 内置 Sprite-Unlit-Default 的顶点/片元管线，唯一改动 Blend 状态为 additive，
//     见该 .shader 文件顶部注释）的材质资产，不覆盖任何属性（Properties 缺省值本身就是"白色贴图 +
//     不透明 Tint"，_MainTex 由 SpriteRenderer 逐实例自动指派，材质资产本身不需要预先赋值）。
//
// 判断记录（为什么材质资产要经本生成器用真实 Unity 创建，不手写 .mat YAML）：.mat 资产序列化引用
// 着色器要靠 GUID（Unity AssetDatabase 内部标识），手写文本容易写错/过期；本生成器用
// AssetDatabase.LoadAssetAtPath<Shader> 取到 Unity 自己导入 VfxAdditiveUnlit.shader 后分配的真实
// GUID 再创建材质，保证引用永远正确、.meta 也是 Unity 自己写出的（同 AGENTS.md"新文件 .meta 必须由
// 真实 Unity 导入生成"这条硬性规则）。
//
// 判断记录（可重复运行）：同 GeneratePlaceholderModelAssets 惯例——按固定路径 CreateAsset，重复运行
// 直接覆盖已存在的同名资产。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Adapter.Unity.EditorTools
{
    public static class GeneratePlaceholderVfxAssets
    {
        private const string MaterialsDir = "Assets/Resources/GameFoundation/materials";
        private const string AdditiveMaterialPath = MaterialsDir + "/vfx_additive.mat";
        private const string AdditiveShaderPath = "Assets/Shaders/VfxAdditiveUnlit.shader";

        [MenuItem("GameFoundation/Generate Placeholder Vfx Assets")]
        public static void Generate()
        {
            Directory.CreateDirectory(MaterialsDir);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AdditiveShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[GeneratePlaceholderVfxAssets] 找不到 {AdditiveShaderPath}，无法生成 additive 占位材质。");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(AdditiveMaterialPath) != null)
            {
                AssetDatabase.DeleteAsset(AdditiveMaterialPath);
            }

            var material = new Material(shader) { name = "vfx_additive" };
            AssetDatabase.CreateAsset(material, AdditiveMaterialPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[GeneratePlaceholderVfxAssets] 生成完成：{AdditiveMaterialPath}");
        }

        /// <summary>供 -executeMethod 批处理调用，同 GeneratePlaceholderModelAssets.GenerateAndExit
        /// 一贯惯例：生成后立即退出编辑器进程，退出码固定 0。</summary>
        public static void GenerateAndExit()
        {
            Generate();
            EditorApplication.Exit(0);
        }
    }
}

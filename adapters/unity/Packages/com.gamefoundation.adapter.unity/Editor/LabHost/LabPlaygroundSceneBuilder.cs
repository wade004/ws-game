#nullable enable
// LabPlaygroundSceneBuilder：生成手感实验室"人手试玩"的三个场景（ADR-0141，手感设计 06 第 1 节三种组合各一个）。
//
// 判断记录（薄场景、共用一个引导）：三个场景内容完全相同——一个挂了 LabPlayground 的空物体，只有"格子"字段不同
// （2d_action / 2_5d_action / 3d_action）；所有装配（数据根、靶子、舞台相机、地面）都在运行时由 LabPlayground 与 EngineLabStage 完成，
// 场景文件里不放第二台相机（舞台自带渲染相机与音频监听）。场景放在 Assets/Framework/Scenes/，不加入 Build Settings，
// 所以玩家构建不含它们，也不含 LabHost 程序集（实验室宿主不进玩家构建）。
// 判断记录（菜单路径）：编辑器面板 FeelLabWindow 占用了叶子菜单 "GameFoundation/手感实验室"，同名路径下不能再挂子菜单，
// 所以试玩场景的菜单放在 "GameFoundation/手感试玩/"。
//
// 命令行（不开编辑器界面）：
//   Unity.exe -batchmode -nographics -quit -projectPath <repo>\adapters\unity
//     -executeMethod Adapter.Unity.LabHost.Editor.LabPlaygroundSceneBuilder.BuildAll
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Adapter.Unity.LabHost.Editor
{
    public static class LabPlaygroundSceneBuilder
    {
        public const string SceneDir = "Assets/Framework/Scenes";

        public static readonly string[] Cells = { "2d_action", "2_5d_action", "3d_action" };

        public static string ScenePath(string cell) => SceneDir + "/LabPlayground_" + cell + ".unity";

        [MenuItem("GameFoundation/手感试玩/重建三个试玩场景")]
        public static void BuildAll()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            Directory.CreateDirectory(Path.Combine(projectRoot, SceneDir.Replace('/', Path.DirectorySeparatorChar)));
            AssetDatabase.Refresh();
            foreach (var cell in Cells)
            {
                Build(cell);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[LabPlaygroundSceneBuilder] scenes generated: " + string.Join(", ", Cells));
        }

        public static void Build(string cell)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("LabPlayground_" + cell);
            var playground = go.AddComponent<LabPlayground>();
            var so = new SerializedObject(playground);
            so.FindProperty("cell").stringValue = cell;
            so.ApplyModifiedPropertiesWithoutUndo();
            var path = ScenePath(cell);
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        // 判断记录（演示场景，ADR-0154）：真实美术的手感演示场景也是同一个薄场景——挂 LabPlayground、把"演示场景"字段打开（格子固定 2d_action，
        // 只做 2D 俯视动作）；美术资源、数据行、HUD 都在运行时由 LabPlayground / EngineLabStage 装配，场景文件里不放任何内容。
        public static string ShowcaseScenePath => SceneDir + "/LabShowcase_2d_action.unity";

        [MenuItem("GameFoundation/手感试玩/重建演示场景")]
        public static void BuildShowcase()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            Directory.CreateDirectory(Path.Combine(projectRoot, SceneDir.Replace('/', Path.DirectorySeparatorChar)));
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("LabShowcase_2d_action");
            var playground = go.AddComponent<LabPlayground>();
            var so = new SerializedObject(playground);
            so.FindProperty("cell").stringValue = "2d_action";
            so.FindProperty("showcase").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ShowcaseScenePath);
            AssetDatabase.ImportAsset(ShowcaseScenePath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[LabPlaygroundSceneBuilder] showcase scene generated: " + ShowcaseScenePath);
        }

        [MenuItem("GameFoundation/手感试玩/打开演示场景 2D（真实美术）")]
        public static void OpenShowcase() => EditorSceneManager.OpenScene(ShowcaseScenePath);

        [MenuItem("GameFoundation/手感试玩/打开试玩场景 2D（俯视精灵）")]
        public static void Open2D() => EditorSceneManager.OpenScene(ScenePath("2d_action"));

        [MenuItem("GameFoundation/手感试玩/打开试玩场景 2.5D（固定俯仰精灵）")]
        public static void Open25D() => EditorSceneManager.OpenScene(ScenePath("2_5d_action"));

        [MenuItem("GameFoundation/手感试玩/打开试玩场景 3D（固定俯仰模型）")]
        public static void Open3D() => EditorSceneManager.OpenScene(ScenePath("3d_action"));
    }
}

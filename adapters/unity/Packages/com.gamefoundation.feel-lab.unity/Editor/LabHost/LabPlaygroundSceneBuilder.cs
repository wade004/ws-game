#nullable enable
// LabPlaygroundSceneBuilder：生成手感实验室"人手试玩"的三个场景（ADR-0141，手感设计 06 第 1 节三种组合各一个）。
//
// 判断记录（薄场景、共用一个引导）：三个场景内容完全相同——一个挂了 LabPlayground 的空物体，只有"格子"字段不同
// （2d_action / 2_5d_action / 3d_action）；所有装配（数据根、靶子、舞台相机、地面）都在运行时由 LabPlayground 与 EngineLabStage 完成，
// 场景文件里不放第二台相机（舞台自带渲染相机与音频监听）。场景放在 Assets/Framework/Scenes/，不加入 Build Settings，
// 所以玩家构建不含它们，也不含实验室程序集（实验室只在编辑器编译，不进玩家构建）。
// 判断记录（菜单路径）：编辑器面板 FeelLabWindow 占用了叶子菜单 "GameFoundation/手感实验室"，同名路径下不能再挂子菜单，
// 所以试玩场景的菜单放在 "GameFoundation/手感试玩/"。
// 判断记录（只剩占位美术场景，ADR-0160）：真实美术的演示场景整体迁往样板仓库（它的场景生成器与菜单在那边），本包只生成占位美术的三个工程场景；
// 三个占位场景在子菜单 "工程场景（占位美术）"，重建入口在子菜单 "维护"。本包只在编辑器编译，游戏的独立版构建里没有它。
//
// 命令行（不开编辑器界面）：
//   Unity.exe -batchmode -nographics -quit -projectPath <repo>\adapters\unity
//     -executeMethod FeelLab.Unity.Editor.LabPlaygroundSceneBuilder.BuildAll
using Adapter.Unity;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FeelLab.Unity.Editor
{
    public static class LabPlaygroundSceneBuilder
    {
        public const string SceneDir = "Assets/Framework/Scenes";

        public static readonly string[] Cells = { "2d_action", "2_5d_action", "3d_action" };

        public static string ScenePath(string cell) => SceneDir + "/LabPlayground_" + cell + ".unity";

        [MenuItem("GameFoundation/手感试玩/维护/重建三个试玩场景", false, 200)]
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

        [MenuItem("GameFoundation/手感试玩/工程场景（占位美术）/打开试玩场景 2D（俯视精灵）", false, 100)]
        public static void Open2D() => EditorSceneManager.OpenScene(ScenePath("2d_action"));

        [MenuItem("GameFoundation/手感试玩/工程场景（占位美术）/打开试玩场景 2.5D（固定俯仰精灵）", false, 101)]
        public static void Open25D() => EditorSceneManager.OpenScene(ScenePath("2_5d_action"));

        [MenuItem("GameFoundation/手感试玩/工程场景（占位美术）/打开试玩场景 3D（固定俯仰模型）", false, 102)]
        public static void Open3D() => EditorSceneManager.OpenScene(ScenePath("3d_action"));
    }
}

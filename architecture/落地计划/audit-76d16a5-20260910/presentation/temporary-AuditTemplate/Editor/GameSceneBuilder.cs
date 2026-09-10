#nullable enable
// GameSceneBuilder：一键生成"Shell + 首张地图"两个场景并加入 Build Settings（architecture/
// 13_新游戏接入指南.md 第 1 节步骤"组装模块与策略配置"落地为可运行场景）。
//
// 判断记录（为什么用脚本生成场景而不是手工在编辑器里搭；为什么复制而不是引用工作台的
// GreyBoxSceneBuilder/ShellSceneBuilder）：同 adapters/unity/Assets/Editor/{GreyBoxSceneBuilder,
// ShellSceneBuilder}.cs 顶部判断记录——本任务硬性规则要求全程只用 Unity.exe -batchmode 命令行；
// 那两个类型属于工作台自己的 Assets/Editor（不在任何 asmdef 包里，是工作台专属脚本，不能被
// games/_template 这个独立包引用），任务书"复用 Adapter.Unity 已有的场景构建逻辑，若那些逻辑在
// 工作台 Assets/Editor 而非包内，把可复用部分复制为模板自己的实现，不改工作台文件"——本类型是
// 那两个类型的相机/EventSystem/占位地面搭建手法的复制适配，唯一的实质差异是场景里挂的组件
// （Shell 场景挂 Game.Template.TemplateShellUi，Map 场景挂 Game.Template.GameBootstrap +
// TemplateAutoStart，见各自 Build 方法）。
//
// 命令行调用：
//   Unity.exe -batchmode -nographics -quit -projectPath <repo>\adapters\unity
//     -executeMethod Game.Template.EditorTools.GameSceneBuilder.BuildAll
using System.Linq;
using Game.Template;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.Template.EditorTools
{
    public static class GameSceneBuilder
    {
        private const string ShellScenePath = "Assets/Framework/Scenes/GameTemplateShell.unity";
        private const string MapScenePath = "Assets/Framework/Scenes/GameTemplateMap.unity";

        [MenuItem("GameFoundation/Game Template/Rebuild Shell + Map Scenes")]
        public static void BuildAll()
        {
            BuildShellScene();
            BuildMapScene();
        }

        [MenuItem("GameFoundation/Game Template/Rebuild Shell Scene")]
        public static void BuildShellScene()
        {
            var scene = NewSceneAt(ShellScenePath);

            BuildMainCamera(new Color(0.05f, 0.05f, 0.07f, 1f));
            BuildEventSystem();
            BuildGround(new Color32(40, 46, 58, 255));
            BuildShellRoot();

            SaveAndRegister(scene, ShellScenePath, insertFirst: true);
            Debug.Log($"[GameSceneBuilder] 场景已生成：{ShellScenePath}");
        }

        [MenuItem("GameFoundation/Game Template/Rebuild Map Scene")]
        public static void BuildMapScene()
        {
            var scene = NewSceneAt(MapScenePath);

            BuildMainCamera(new Color(0.08f, 0.09f, 0.11f, 1f));
            BuildEventSystem();
            BuildGround(new Color32(46, 64, 46, 255));
            BuildAutoStartBootstrap();

            SaveAndRegister(scene, MapScenePath, insertFirst: false);
            Debug.Log($"[GameSceneBuilder] 场景已生成：{MapScenePath}");
        }

        // 判断记录：AssetDatabase.CreateFolder 在批处理下对 EditorSceneManager.SaveScene 内部的
        // 物理路径存在性检查不总是"立即生效"，改用 System.IO.Directory.CreateDirectory 直接建
        // 物理目录（幂等）+ AssetDatabase.Refresh() 登记进资产索引，两者都做，惯例同
        // GreyBoxSceneBuilder.Build。
        private static Scene NewSceneAt(string scenePath)
        {
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath)!.FullName;
            var sceneDirRelative = System.IO.Path.GetDirectoryName(scenePath)!.Replace('/', System.IO.Path.DirectorySeparatorChar);
            var sceneDirFull = System.IO.Path.Combine(projectRoot, sceneDirRelative);
            System.IO.Directory.CreateDirectory(sceneDirFull);
            AssetDatabase.Refresh();

            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void SaveAndRegister(Scene scene, string scenePath, bool insertFirst)
        {
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            // 判断记录：SaveScene 之后立即构造 EditorBuildSettingsScene 时实测拿到的 GUID 有时还是
            // 全零（资产导入尚未完成登记）；显式 ImportAsset 强制同步导入一次，惯例同
            // GreyBoxSceneBuilder/ShellSceneBuilder。
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
            AddToBuildSettings(scenePath, insertFirst);
        }

        private static void BuildMainCamera(Color backgroundColor)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 0f, -10f);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = backgroundColor;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            // URP 需要 UniversalAdditionalCameraData 才能被 2D Renderer 正确渲染，惯例同
            // GreyBoxSceneBuilder.BuildMainCamera。
            if (go.GetComponent<UniversalAdditionalCameraData>() == null)
            {
                go.AddComponent<UniversalAdditionalCameraData>();
            }

            go.AddComponent<AudioListener>();
        }

        private static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>占位地面：运行期生成的纯色纹理，不依赖 StreamingAssets 同步产物是否已经跑过，
        /// 手法同 GreyBoxSceneBuilder/ShellSceneBuilder.BuildGround。</summary>
        private static void BuildGround(Color32 color)
        {
            var go = new GameObject("Ground");
            go.transform.position = new Vector3(0f, 0f, 0.1f);
            go.transform.localScale = new Vector3(40f, 40f, 1f);

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "GameTemplateGroundTexture" };
            var pixels = Enumerable.Repeat(color, 16).ToArray();
            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = "GameTemplateGroundSprite";

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = -1000;
        }

        private static void BuildShellRoot()
        {
            var go = new GameObject("TemplateShellUi");
            go.AddComponent<TemplateShellUi>();
        }

        private static void BuildAutoStartBootstrap()
        {
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
            go.AddComponent<TemplateAutoStart>();
        }

        private static void AddToBuildSettings(string scenePath, bool insertFirst)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            var entry = new EditorBuildSettingsScene(scenePath, enabled: true);
            if (insertFirst)
            {
                scenes.Insert(0, entry);
            }
            else
            {
                scenes.Add(entry);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

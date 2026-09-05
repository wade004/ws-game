#nullable enable
// ShellSceneBuilder：程序化生成/重建 Shell 场景 Assets/Framework/Scenes/Shell.unity（U3-3）。
// 判断记录（为什么用脚本生成场景）：同 GreyBoxSceneBuilder.cs 顶部判断记录——本任务硬性规则 1
// 要求全程只用 Unity.exe -batchmode 命令行、不打开编辑器 GUI。
//
// 命令行调用：
//   Unity.exe -batchmode -nographics -quit -projectPath <repo>\adapters\unity
//     -executeMethod Adapter.Unity.EditorTools.ShellSceneBuilder.Build
using System.Linq;
using Adapter.Unity.Shell;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Adapter.Unity.EditorTools
{
    public static class ShellSceneBuilder
    {
        private const string ScenePath = "Assets/Framework/Scenes/Shell.unity";

        [MenuItem("GameFoundation/Rebuild Shell Scene")]
        public static void Build()
        {
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath)!.FullName;
            var sceneDirRelative = System.IO.Path.GetDirectoryName(ScenePath)!.Replace('/', System.IO.Path.DirectorySeparatorChar);
            var sceneDirFull = System.IO.Path.Combine(projectRoot, sceneDirRelative);
            System.IO.Directory.CreateDirectory(sceneDirFull);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildMainCamera();
            BuildEventSystem();
            BuildGround();
            BuildShellRoot();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            AddToBuildSettingsFirst(ScenePath);

            Debug.Log($"[ShellSceneBuilder] 场景已生成：{ScenePath}");
        }

        private static void BuildMainCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 0f, -10f);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

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

        /// <summary>占位地面：同 GreyBoxSceneBuilder.BuildGround 手法（运行期生成的纯色纹理），
        /// 供进入游戏内（InWorld）之后仍有可见的世界空间背景，不依赖 StreamingAssets 同步产物。</summary>
        private static void BuildGround()
        {
            var go = new GameObject("Ground");
            go.transform.position = new Vector3(0f, 0f, 0.1f);
            go.transform.localScale = new Vector3(40f, 40f, 1f);

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "ShellGroundTexture" };
            var pixels = System.Linq.Enumerable.Repeat(new Color32(40, 46, 58, 255), 16).ToArray();
            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = "ShellGroundSprite";

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = -1000;
        }

        private static void BuildShellRoot()
        {
            var go = new GameObject("ShellRoot");
            go.AddComponent<ShellRoot>();
        }

        private static void AddToBuildSettingsFirst(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, enabled: true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

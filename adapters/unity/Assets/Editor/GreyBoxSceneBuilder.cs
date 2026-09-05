#nullable enable
// GreyBoxSceneBuilder：一次性/可重跑的编辑器工具，程序化生成灰盒测试场景
// Assets/Framework/Scenes/GreyBox.unity（U2-4）。
//
// 判断记录（为什么用脚本生成场景而不是手工在编辑器里搭）：本任务的硬性规则 1 要求全程只用
// `Unity.exe -batchmode` 命令行、不打开编辑器 GUI；`-executeMethod` 是命令行下能触发任意编辑器
// 代码的标准方式，程序化生成场景既能满足"不开 GUI"的约束，也让场景内容可重复重建（场景文件
// 意外损坏/需要调整时重跑本方法即可，不依赖"记住当时在编辑器里点了哪些菜单"）。
//
// 命令行调用：
//   Unity.exe -batchmode -nographics -quit -projectPath <repo>\adapters\unity
//     -executeMethod Adapter.Unity.EditorTools.GreyBoxSceneBuilder.Build
using System.Linq;
using Adapter.Unity.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Adapter.Unity.EditorTools
{
    public static class GreyBoxSceneBuilder
    {
        private const string ScenePath = "Assets/Framework/Scenes/GreyBox.unity";

        [MenuItem("GameFoundation/Rebuild GreyBox Scene")]
        public static void Build()
        {
            // 判断记录：AssetDatabase.CreateFolder 逐级创建在批处理下实测对
            // EditorSceneManager.SaveScene 内部的物理路径存在性检查（CheckValidAssetPathAndThatDirectoryExists）
            // 不总是"立即生效"（该检查读的是磁盘路径，不是 AssetDatabase 索引）；改用
            // System.IO.Directory.CreateDirectory 直接建物理目录（幂等，目录已存在时无操作），
            // 再 AssetDatabase.Refresh() 让 Unity 把新目录登记进资产索引，两者都做，不依赖其中
            // 任一时序假设。
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath)!.FullName;
            var sceneDirRelative = System.IO.Path.GetDirectoryName(ScenePath)!.Replace('/', System.IO.Path.DirectorySeparatorChar);
            var sceneDirFull = System.IO.Path.Combine(projectRoot, sceneDirRelative);
            System.IO.Directory.CreateDirectory(sceneDirFull);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildMainCamera();
            BuildEventSystem();
            BuildGround();
            BuildBootstrap();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            // 判断记录：SaveScene 之后立即构造 EditorBuildSettingsScene 时实测拿到的
            // AssetDatabase.AssetPathToGUID(ScenePath) 有时还是全零 GUID（资产导入尚未完成登记）；
            // 显式 ImportAsset 强制同步导入一次，确保 GUID 已经可查，避免 Build Settings 里存进一条
            // 无效 GUID 的场景条目。
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            AddToBuildSettingsFirst(ScenePath);

            Debug.Log($"[GreyBoxSceneBuilder] 场景已生成：{ScenePath}");
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
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            // URP 需要 UniversalAdditionalCameraData 才能被 2D Renderer 正确渲染；纯脚本
            // AddComponent<Camera>() 不会自动附带，显式补上（同 URP 内置 Camera 编辑器菜单的
            // 行为，见 UnityEngine.Rendering.Universal 文档）。
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

        /// <summary>占位地面：一块纯色 SpriteRenderer（见任务书"占位地面（assets/_placeholder/maps
        /// 或纯色 tilemap/quad）"），不依赖 StreamingAssets 同步产物是否已经跑过（场景本身的生成
        /// 不应该反过来依赖 build.ps1 -SyncContent 的执行顺序），用运行期生成的纯色纹理，与
        /// UnityRenderer2D.GetPlaceholderSprite 同一手法（4x4 纯色像素图 Sprite.Create）。</summary>
        private static void BuildGround()
        {
            var go = new GameObject("Ground");
            go.transform.position = new Vector3(0f, 0f, 0.1f);
            go.transform.localScale = new Vector3(40f, 40f, 1f);

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "GreyBoxGroundTexture" };
            var pixels = Enumerable.Repeat(new Color32(46, 64, 46, 255), 16).ToArray();
            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = "GreyBoxGroundSprite";

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = -1000; // 恒在全部单位/gobj 之下。
        }

        private static void BuildBootstrap()
        {
            var go = new GameObject("GameFoundationBootstrap");
            go.AddComponent<GameFoundationBootstrap>();
        }

        private static void AddToBuildSettingsFirst(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, enabled: true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

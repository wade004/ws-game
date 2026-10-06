// 让实验室引擎宿主的测试程序集访问宿主内部的核对入口（例如 EquipLayerAudit.CompareClips），不对外公开。
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FeelLab.Unity.Tests")]

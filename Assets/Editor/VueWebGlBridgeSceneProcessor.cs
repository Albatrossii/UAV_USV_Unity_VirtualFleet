#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UavUsv.Editor.Tools
{
    /// <summary>
    /// Injects the Vue command receiver into the temporary scene instance used by
    /// a WebGL player build. The source scene asset is never changed or saved.
    /// </summary>
    public sealed class VueWebGlBridgeSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.WebGL)
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == "WebCommandBridge")
                {
                    if (!roots[i].GetComponent<UavUsv.PlatformTools.WebCommandBridge>())
                        roots[i].AddComponent<UavUsv.PlatformTools.WebCommandBridge>();
                    return;
                }
            }

            var host = new GameObject("WebCommandBridge");
            host.AddComponent<UavUsv.PlatformTools.WebCommandBridge>();
            SceneManager.MoveGameObjectToScene(host, scene);
        }
    }
}
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace UavUsv.PlatformTools
{
    /// <summary>
    /// Frontend-only WebGL presentation adapter. The Unity editor and Windows
    /// simulation keep their original trajectory inset; Vue's system overview
    /// receives the clean three-dimensional scene.
    /// </summary>
    public sealed class VueWebGlPresentationTool : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var host = new GameObject("Vue WebGL Presentation Tool");
            DontDestroyOnLoad(host);
            host.AddComponent<VueWebGlPresentationTool>();
        }

        private IEnumerator Start()
        {
            while (true)
            {
                UavUsv.ChaseCamera chaseCamera = FindObjectOfType<UavUsv.ChaseCamera>();
                if (chaseCamera)
                {
                    chaseCamera.showTacticalInset = false;
                    yield break;
                }

                yield return null;
            }
        }
    }
}
#endif

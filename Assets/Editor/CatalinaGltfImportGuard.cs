#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UavUsv.EditorTools
{
    /// <summary>
    /// Ensures Catalina is reimported after glTFast becomes available.
    /// On a fresh checkout Unity can discover the .gltf before resolving the
    /// package, leaving it temporarily registered as a plain DefaultAsset.
    /// </summary>
    [InitializeOnLoad]
    public static class CatalinaGltfImportGuard
    {
        private const string AssetPath =
            "Assets/Resources/CatalinaIslandGltf/scene.gltf";

        static CatalinaGltfImportGuard()
        {
            EditorApplication.delayCall += EnsureImported;
        }

        [MenuItem("UAV-USV/Tools/Reimport Catalina glTF")]
        public static void ReimportNow()
        {
            AssetDatabase.ImportAsset(
                AssetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate
            );

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
            if (prefab)
                Debug.Log("Catalina glTF imported and ready.");
            else
                Debug.LogError(
                    "Catalina glTF is still unavailable. " +
                    "Wait for Package Manager to finish resolving glTFast, " +
                    "then run this command again."
                );
        }

        private static void EnsureImported()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureImported;
                return;
            }

            if (!AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath))
                ReimportNow();
        }
    }
}
#endif

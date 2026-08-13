using UnityEngine;
using UnityEngine.Rendering;

namespace UavUsv
{
    /// <summary>
    /// Renders the same Catalina terrain used by the ROS/Gazebo
    /// heterogeneous_332 world. Unity keeps static mesh collision for local
    /// presentation safety; Gazebo remains authoritative in ROS sync mode.
    /// </summary>
    public static class CatalinaIslandRuntime
    {
        // The source glTF is the exact Gazebo visual asset. Gazebo applies
        // a uniform 0.024 mesh scale in catalina_island/model.sdf.
        private const float GazeboMeshScale = .024f;
        private const string GltfResourcePath = "CatalinaIslandGltf/scene";

        public static GameObject CreateVisualTerrain(
            Vector3 rosAlignedPosition,
            float presentationScale = 1f)
        {
            GameObject prefab = Resources.Load<GameObject>(
                GltfResourcePath
            );
            if (!prefab)
            {
                Debug.LogWarning(
                    "Catalina glTF terrain resource is unavailable."
                );
                return null;
            }

            GameObject terrain = new GameObject("catalina_island_terrain");
            GameObject visual = Object.Instantiate(prefab);
            visual.name = "gltf_visual";
            visual.transform.SetParent(terrain.transform, false);
            terrain.transform.position = rosAlignedPosition;
            // glTFast converts the right-handed glTF into Unity space. Its
            // horizontal axes are opposite to Gazebo ENU for this asset, so a
            // rigid 180-degree yaw on this wrapper restores +X east and +Z
            // north without overwriting the imported glTF root's Z-up to Y-up
            // transform.
            terrain.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            terrain.transform.localScale = Vector3.one * GazeboMeshScale *
                                           Mathf.Max(.001f, presentationScale);

            // Preserve imported terrain colliders. RuntimeCollisionSafety
            // configures these meshes and fills any missing collider entries.
            foreach (Rigidbody body in terrain.GetComponentsInChildren<Rigidbody>(true))
                Object.Destroy(body);

            foreach (Renderer renderer in terrain.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            return terrain;
        }
    }
}

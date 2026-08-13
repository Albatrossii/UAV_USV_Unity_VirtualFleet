using UnityEngine;
using UnityEngine.Rendering;

namespace UavUsv
{
    /// <summary>
    /// Visual-only Sydney outer coast matching Gazebo
    /// <c>model://sydney_coast_boundary</c> in heterogeneous_332.
    /// The mesh is remapped to Unity Y-up and placed at the ROS include pose.
    /// </summary>
    public static class SydneyCoastBoundaryRuntime
    {
        // Gazebo include pose from local heterogeneous_332.sdf.
        public static readonly Vector3 RosPoseEnu = new Vector3(-80f, -255f, -1.4f);

        public static GameObject CreateVisualBackdrop(Vector3 rosAlignedPosition)
        {
            GameObject prefab = Resources.Load<GameObject>(
                "SydneyCoastBoundary/sydney_coast_boundary"
            );
            Texture2D treeTexture = Resources.Load<Texture2D>(
                "SydneyCoastBoundary/TreeDiffuse"
            );
            if (!prefab)
            {
                Debug.LogWarning(
                    "Sydney coast boundary mesh is unavailable; falling back."
                );
                return SydneyCoastRuntime.CreateVisualBackdrop(rosAlignedPosition).gameObject;
            }

            GameObject root = new GameObject("sydney_coast_boundary");
            root.transform.position = rosAlignedPosition;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            GameObject visual = Object.Instantiate(prefab, root.transform);
            visual.name = "sydney_coast_visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                Object.Destroy(collider);
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                Object.Destroy(body);
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                Object.Destroy(camera);
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                Object.Destroy(light);

            ApplyMaterials(root, treeTexture);
            return root;
        }

        private static void ApplyMaterials(GameObject root, Texture2D treeTexture)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string key = (renderer.gameObject.name + " " + renderer.name)
                    .ToLowerInvariant();
                Material material;
                if (key.Contains("tree"))
                {
                    material = SceneFactory.Material(
                        "Sydney tree",
                        new Color(.78f, .80f, .70f),
                        0f,
                        .08f
                    );
                    if (treeTexture)
                    {
                        material.mainTexture = treeTexture;
                        material.SetFloat("_Mode", 1);
                        material.SetInt("_SrcBlend", (int)BlendMode.One);
                        material.SetInt("_DstBlend", (int)BlendMode.Zero);
                        material.SetInt("_ZWrite", 1);
                        material.EnableKeyword("_ALPHATEST_ON");
                        material.DisableKeyword("_ALPHABLEND_ON");
                        material.SetFloat("_Cutoff", .35f);
                        material.renderQueue = 2450;
                    }
                }
                else if (key.Contains("terrain"))
                {
                    material = SceneFactory.Material(
                        "Sydney terrain",
                        new Color(.30f, .40f, .26f),
                        0f,
                        .12f
                    );
                }
                else if (key.Contains("window"))
                {
                    material = SceneFactory.Material(
                        "Sydney window",
                        new Color(.04f, .20f, .28f),
                        .15f,
                        .88f
                    );
                }
                else if (key.Contains("roof"))
                {
                    material = SceneFactory.Material(
                        "Sydney roof",
                        new Color(.38f, .20f, .14f),
                        .05f,
                        .28f
                    );
                }
                else if (key.Contains("metal"))
                {
                    material = SceneFactory.Material(
                        "Sydney metal",
                        new Color(.46f, .50f, .52f),
                        .35f,
                        .62f
                    );
                }
                else if (key.Contains("dark"))
                {
                    material = SceneFactory.Material(
                        "Sydney dark dock",
                        new Color(.14f, .16f, .15f),
                        .08f,
                        .30f
                    );
                }
                else if (key.Contains("dock"))
                {
                    material = SceneFactory.Material(
                        "Sydney dock",
                        new Color(.42f, .34f, .24f),
                        0f,
                        .22f
                    );
                }
                else
                {
                    material = SceneFactory.Material(
                        "Sydney wall",
                        new Color(.54f, .51f, .44f),
                        0f,
                        .20f
                    );
                }

                Material[] materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }
    }
}

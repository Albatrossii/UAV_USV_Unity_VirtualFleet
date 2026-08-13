using System.Collections.Generic;
using UnityEngine;

namespace UavUsv
{
    /// <summary>
    /// Visual-only version of the former Unity coastline. It adds the familiar
    /// scenery around the ROS world without adding collision or simulation
    /// entities, so Gazebo remains authoritative.
    /// </summary>
    public sealed class SydneyCoastRuntime : MonoBehaviour
    {
        private const float GazeboYawDegrees = 27.215f;
        private const float OuterBoundaryScale = 2.15f;
        private const float GazeboOceanHalfWidth = 525f;
        private const float GazeboOceanHalfLength = 450f;
        private const float VisualCoastClearance = 12f;
        // Preserve the original outer-coast composition. The world position,
        // mesh size and transition width are all scaled by presentationScale,
        // so the green land stays at the far frame edge instead of following
        // the fleet into the central operating water.
        private const float RimDistanceMeters = 850f;
        private const float RimPieceScale = .00205f;
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
        private float presentationScale = 1f;

        public Transform collisionRoot { get; private set; }
        public bool isReady { get; private set; }

        [Tooltip("Cut the central open-water rectangle out of the outer scenery.")]
        public bool removeCenterChannel = true;
        public bool surroundWithOuterScenery = true;
        [Tooltip("Keep trees and green terrain only; hide dock/building blocks.")]
        public bool naturalEdgeScenery = true;

        public static SydneyCoastRuntime Create()
        {
            return CreateVisualBackdrop(Vector3.zero);
        }

        public static SydneyCoastRuntime CreateVisualBackdrop(
            Vector3 worldPosition,
            float displayScale = 1f)
        {
            var root = new GameObject("visual_environment_backdrop");
            SydneyCoastRuntime coast = root.AddComponent<SydneyCoastRuntime>();
            coast.presentationScale = Mathf.Max(.001f, displayScale);
            // Preserve the former scene composition: only the scenery beyond
            // the open-water rectangle remains visible at the far edges.
            coast.removeCenterChannel = true;
            root.transform.position = worldPosition;
            coast.Build();
            return coast;
        }

        public void RebuildVisuals()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            foreach (Material material in runtimeMaterials)
            {
                if (material)
                    Destroy(material);
            }
            runtimeMaterials.Clear();
            foreach (Mesh mesh in runtimeMeshes)
            {
                if (mesh)
                    Destroy(mesh);
            }
            runtimeMeshes.Clear();
            isReady = false;
            Build();
        }

        private void Build()
        {
            GameObject visualPrefab = Resources.Load<GameObject>(
                "SydneyCoast/sydney_regatta"
            );
            if (!visualPrefab)
            {
                Debug.LogWarning("Sydney coastline resources are unavailable.");
                return;
            }

            GameObject visualRoot = new GameObject(
                naturalEdgeScenery ? "Green Coastal Rim" : "Sydney Coast Visual"
            );
            visualRoot.transform.SetParent(transform, false);
            collisionRoot = null;

            if (naturalEdgeScenery)
                BuildNaturalGreenRim(visualRoot.transform, visualPrefab);
            else
                BuildLegacySurround(visualRoot.transform, visualPrefab);

            isReady = visualRoot.GetComponentInChildren<Renderer>() != null;
        }

        private void BuildNaturalGreenRim(Transform visualRoot, GameObject visualPrefab)
        {
            // Eight coastal strips around the ocean, facing inward toward the fleet.
            float[] headingsDeg = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
            for (int i = 0; i < headingsDeg.Length; i++)
            {
                float heading = headingsDeg[i] * Mathf.Deg2Rad;
                float east = Mathf.Sin(heading) * RimDistanceMeters *
                             presentationScale;
                float north = Mathf.Cos(heading) * RimDistanceMeters *
                              presentationScale;
                Vector3 worldPos = Coordinates.ToUnity(
                    east,
                    north,
                    -1.05f * presentationScale
                );

                GameObject visual = Instantiate(visualPrefab, visualRoot);
                visual.name = "Green Rim " + headingsDeg[i].ToString("000");
                visual.transform.position = worldPos;
                // Face the operating area; keep a slight Sydney yaw for variety.
                visual.transform.rotation = Quaternion.Euler(
                    0f,
                    headingsDeg[i] + 180f - GazeboYawDegrees,
                    0f
                );
                // Use the same compact environment scale as the mountain.
                // The former .38 minimum made eight coast copies overlap the
                // centre and fill almost the entire screen.
                float vegetationScale = presentationScale;
                visual.transform.localScale = Vector3.one * RimPieceScale *
                                              vegetationScale;
                if (removeCenterChannel)
                    ClipCenterChannelMeshes(visual);
                ApplyLightweightMaterials(visual);
                RemoveImportedComponents(visual);
                CreateCoastalTransition(
                    visualRoot,
                    worldPos,
                    headingsDeg[i],
                    420f * presentationScale,
                    150f * presentationScale
                );
            }
        }

        private void CreateCoastalTransition(
            Transform parent,
            Vector3 center,
            float headingDeg,
            float width,
            float depth)
        {
            Material grass = CreateMutedGrassMaterial("Coastal greensward");
            // Shore bands share hue with both the olive land and blue-grey water.
            Material wetBank = CreateShoreFadeMaterial(
                "Wet coastal bank",
                new Color(.34f, .32f, .24f, .48f),
                1.4f
            );
            Material shallows = CreateShoreFadeMaterial(
                "Coastal shallows",
                new Color(.16f, .22f, .22f, .30f),
                1.75f
            );
            Material foam = CreateShoreFadeMaterial(
                "Shore foam wash",
                new Color(.56f, .58f, .56f, .14f),
                2.2f
            );
            runtimeMaterials.Add(grass);
            runtimeMaterials.Add(wetBank);
            runtimeMaterials.Add(shallows);
            runtimeMaterials.Add(foam);

            float heading = headingDeg * Mathf.Deg2Rad;
            Vector3 outward = Coordinates.ToUnity(
                Mathf.Sin(heading),
                Mathf.Cos(heading),
                0f
            );
            Vector3 along = Coordinates.ToUnity(
                Mathf.Cos(heading),
                -Mathf.Sin(heading),
                0f
            );

            // Pull transition bands slightly onto the water so the seam closes.
            CreateIrregularShelf(
                parent,
                "Greensward " + headingDeg.ToString("000"),
                center + outward * (depth * .18f) + Vector3.up * .04f,
                along,
                outward,
                width,
                depth * .70f,
                .10f,
                .22f,
                grass,
                false
            );
            CreateIrregularShelf(
                parent,
                "Wet Bank " + headingDeg.ToString("000"),
                center + outward * (depth * -.12f) + Vector3.up * .03f,
                along,
                outward,
                width * .98f,
                depth * .32f,
                .18f,
                .30f,
                wetBank,
                true
            );
            CreateIrregularShelf(
                parent,
                "Shallows " + headingDeg.ToString("000"),
                center + outward * (depth * -.34f) + Vector3.up * .02f,
                along,
                outward,
                width * 1.02f,
                depth * .40f,
                .28f,
                .38f,
                shallows,
                true
            );
            CreateIrregularShelf(
                parent,
                "Foam Wash " + headingDeg.ToString("000"),
                center + outward * (depth * -.56f) + Vector3.up * .018f,
                along,
                outward,
                width * 1.06f,
                depth * .26f,
                .35f,
                .55f,
                foam,
                true
            );
        }

        private void CreateIrregularShelf(
            Transform parent,
            string name,
            Vector3 center,
            Vector3 along,
            Vector3 outward,
            float width,
            float depth,
            float innerWave,
            float outerWave,
            Material material,
            bool softEdge)
        {
            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float lateral = (t - .5f) * width;
                float coastNoise =
                    Mathf.Sin(t * 17.3f + width * .01f) * .45f +
                    Mathf.Sin(t * 31.7f + 1.7f) * .30f +
                    Mathf.Sin(t * 53.1f + 4.2f) * .25f;
                float endFade = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) * 7f);
                float inner = depth * .5f * (1f + coastNoise * innerWave) * (.70f + .30f * endFade);
                float outer = -depth * .5f * (1f + coastNoise * outerWave) * (.70f + .30f * endFade);

                vertices[i * 2] = center + along * lateral + outward * inner;
                vertices[i * 2 + 1] = center + along * lateral + outward * outer;
                uvs[i * 2] = new Vector2(t, 1f);
                uvs[i * 2 + 1] = new Vector2(t, 0f);
            }

            int tri = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                // Clockwise order as seen from below produced downward normals
                // and made every grass shelf disappear from the top camera.
                triangles[tri++] = a;
                triangles[tri++] = a + 2;
                triangles[tri++] = a + 1;
                triangles[tri++] = a + 1;
                triangles[tri++] = a + 2;
                triangles[tri++] = a + 3;
            }

            var mesh = new Mesh
            {
                name = name,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);

            GameObject go = new GameObject(
                name,
                typeof(MeshFilter),
                typeof(MeshRenderer)
            );
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = !softEdge;
        }

        private static Material CreateMutedGrassMaterial(string name)
        {
            Shader shader =
                Resources.Load<Shader>("MutedCoastGrass") ??
                Shader.Find("UavUsv/MutedCoastGrass");
            if (!shader)
            {
                return SceneFactory.Material(
                    name,
                    new Color(.31f, .32f, .24f),
                    0f,
                    .06f
                );
            }

            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(.30f, .32f, .24f, 1f));
            if (material.HasProperty("_DryColor"))
                material.SetColor("_DryColor", new Color(.38f, .35f, .26f, 1f));
            if (material.HasProperty("_DarkColor"))
                material.SetColor("_DarkColor", new Color(.20f, .22f, .17f, 1f));
            if (material.HasProperty("_NoiseScale"))
                material.SetFloat("_NoiseScale", .038f);
            return material;
        }

        private static Material CreateShoreFadeMaterial(
            string name,
            Color color,
            float fadePower)
        {
            Shader shader =
                Resources.Load<Shader>("ShoreFade") ??
                Shader.Find("UavUsv/ShoreFade");
            if (!shader)
                return SceneFactory.Material(name, color, 0f, .35f);

            Material material = new Material(shader)
            {
                name = name,
                color = color
            };
            if (material.HasProperty("_FadePower"))
                material.SetFloat("_FadePower", fadePower);
            return material;
        }

        private void BuildLegacySurround(Transform visualRoot, GameObject visualPrefab)
        {
            transform.rotation = Quaternion.Euler(0f, -GazeboYawDegrees, 0f);

            float[] rotations = surroundWithOuterScenery
                ? new[] { 0f, 90f, 180f, 270f }
                : new[] { 0f };

            for (int i = 0; i < rotations.Length; i++)
            {
                float yaw = rotations[i];
                GameObject visual = Instantiate(visualPrefab, visualRoot);
                visual.name = "Sydney Coast Visual " + yaw.ToString("000");
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                visual.transform.localScale =
                    Vector3.one * (.0015f * OuterBoundaryScale);
                if (removeCenterChannel)
                    ClipCenterChannelMeshes(visual);
                ApplyLightweightMaterials(visual);
                RemoveImportedComponents(visual);
            }
        }

        private void BuildShorelineBlend(Transform parent)
        {
            Material shallow = SceneFactory.Material(
                "Soft Shallow Water",
                new Color(.075f, .33f, .38f, .22f),
                0f,
                .74f
            );
            Material wetSand = SceneFactory.Material(
                "Soft Wet Shore",
                new Color(.34f, .43f, .31f, .26f),
                0f,
                .18f
            );
            runtimeMaterials.Add(shallow);
            runtimeMaterials.Add(wetSand);

            GameObject root = new GameObject("Soft Shoreline Blend");
            root.transform.SetParent(parent, false);

            CreateBlendStrip(
                root.transform,
                "North Shallow Blend",
                -130f,
                132f,
                58f,
                92f,
                true,
                shallow
            );
            CreateBlendStrip(
                root.transform,
                "South Shallow Blend",
                -130f,
                132f,
                -160f,
                -126f,
                true,
                shallow
            );
            CreateBlendStrip(
                root.transform,
                "East Shallow Blend",
                92f,
                126f,
                -160f,
                92f,
                false,
                shallow
            );
            CreateBlendStrip(
                root.transform,
                "West Shallow Blend",
                -126f,
                -92f,
                -160f,
                92f,
                false,
                shallow
            );

            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore",
                -168f,
                168f,
                92f,
                118f,
                true,
                wetSand
            );
            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore South",
                -168f,
                168f,
                -184f,
                -158f,
                true,
                wetSand
            );
            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore East",
                126f,
                152f,
                -184f,
                118f,
                false,
                wetSand
            );
            CreateBlendStrip(
                root.transform,
                "Outer Wet Shore West",
                -152f,
                -126f,
                -184f,
                118f,
                false,
                wetSand
            );
        }

        private void CreateBlendStrip(
            Transform parent,
            string name,
            float minX,
            float maxX,
            float minY,
            float maxY,
            bool horizontal,
            Material material
        )
        {
            const int segments = 56;
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float wave = (
                    Mathf.Sin(t * Mathf.PI * 6.3f) * 3.1f +
                    Mathf.Sin(t * Mathf.PI * 13.7f + 1.2f) * 1.4f
                );

                if (horizontal)
                {
                    float x = Mathf.Lerp(minX, maxX, t);
                    vertices[i * 2] = Coordinates.ToUnity(x, minY + wave, .035f);
                    vertices[i * 2 + 1] = Coordinates.ToUnity(x, maxY + wave * .55f, .045f);
                }
                else
                {
                    float y = Mathf.Lerp(minY, maxY, t);
                    vertices[i * 2] = Coordinates.ToUnity(minX + wave, y, .035f);
                    vertices[i * 2 + 1] = Coordinates.ToUnity(maxX + wave * .55f, y, .045f);
                }
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            int triangle = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[triangle++] = a;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = d;
            }

            Mesh mesh = new Mesh { name = name + " Mesh" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            runtimeMeshes.Add(mesh);

            GameObject strip = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            strip.transform.SetParent(parent, false);
            strip.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = strip.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void ApplyLightweightMaterials(GameObject visual)
        {
            Texture2D treeTexture = Resources.Load<Texture2D>(
                "SydneyCoast/TreeDiffuse"
            );
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                string key = (renderer.gameObject.name + " " + renderer.name).ToLowerInvariant();
                bool isTree = key.Contains("tree");
                bool isTerrain = key.Contains("terrain") || key.Contains("ground") ||
                                 key.Contains("grass") || key.Contains("land") ||
                                 key.Contains("hill") || key.Contains("cliff") ||
                                 key.Contains("rock") || key.Contains("soil");
                bool isUrban =
                    key.Contains("dock") || key.Contains("window") ||
                    key.Contains("roof") || key.Contains("metal") ||
                    key.Contains("wall") || key.Contains("building") ||
                    key.Contains("house") || key.Contains("pier") ||
                    key.Contains("crane") || key.Contains("boat") ||
                    key.Contains("ship") || key.Contains("container");
                if (naturalEdgeScenery && isUrban && !isTree && !isTerrain)
                {
                    // Hide docks/buildings; keep green fields + trees.
                    renderer.enabled = false;
                    continue;
                }

                Color color;
                float smoothness;
                if (isTree)
                {
                    // Warm desaturated canopy so trees sit with olive ground + grey sea.
                    color = new Color(.70f, .68f, .56f);
                    smoothness = .04f;
                }
                else if (isTerrain || (naturalEdgeScenery && !isUrban))
                {
                    // Dusty olive / khaki ground, closer to Catalina rock warmth.
                    color = new Color(.31f, .32f, .24f);
                    smoothness = .06f;
                }
                else if (key.Contains("window"))
                {
                    color = new Color(.035f, .18f, .25f);
                    smoothness = .82f;
                }
                else if (key.Contains("roof"))
                {
                    color = new Color(.36f, .18f, .12f);
                    smoothness = .25f;
                }
                else if (key.Contains("metal"))
                {
                    color = new Color(.43f, .48f, .5f);
                    smoothness = .62f;
                }
                else if (key.Contains("dockdark"))
                {
                    color = new Color(.11f, .13f, .14f);
                    smoothness = .28f;
                }
                else if (key.Contains("dock"))
                {
                    color = new Color(.37f, .29f, .2f);
                    smoothness = .22f;
                }
                else
                {
                    color = new Color(.52f, .49f, .41f);
                    smoothness = .18f;
                }

                Material material = SceneFactory.Material(
                    "Sydney " + renderer.gameObject.name,
                    color,
                    key.Contains("metal") ? .28f : 0f,
                    smoothness
                );
                if (isTree && treeTexture)
                    ConfigureTreeMaterial(material, treeTexture);
                runtimeMaterials.Add(material);
                Material[] materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private void ClipCenterChannelMeshes(GameObject root)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh)
                    continue;

                Mesh clipped = ClipMesh(filter.sharedMesh, filter.transform);
                if (!clipped)
                    continue;

                runtimeMeshes.Add(clipped);
                filter.sharedMesh = clipped;
            }
        }

        private Mesh ClipMesh(Mesh source, Transform meshTransform)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            var keptVertices = new List<Vector3>(vertices.Length);
            var keptNormals = normals != null && normals.Length == vertices.Length
                ? new List<Vector3>(normals.Length)
                : null;
            var keptUvs = uvs != null && uvs.Length == vertices.Length
                ? new List<Vector2>(uvs.Length)
                : null;
            var remap = new Dictionary<int, int>(vertices.Length);
            var trianglesBySubmesh = new List<int[]>();
            bool changed = false;

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] triangles = source.GetTriangles(submesh);
                var keptTriangles = new List<int>(triangles.Length);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    Vector3 worldA =
                        meshTransform.TransformPoint(vertices[a]);
                    Vector3 worldB =
                        meshTransform.TransformPoint(vertices[b]);
                    Vector3 worldC =
                        meshTransform.TransformPoint(vertices[c]);

                    if (OverlapsGazeboOcean(worldA, worldB, worldC))
                    {
                        changed = true;
                        continue;
                    }

                    keptTriangles.Add(GetMappedIndex(a));
                    keptTriangles.Add(GetMappedIndex(b));
                    keptTriangles.Add(GetMappedIndex(c));
                }
                trianglesBySubmesh.Add(keptTriangles.ToArray());
            }

            if (!changed)
                return null;

            Mesh mesh = new Mesh
            {
                name = source.name + " OuterOnly",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.SetVertices(keptVertices);
            if (keptNormals != null && keptNormals.Count == keptVertices.Count)
                mesh.SetNormals(keptNormals);
            if (keptUvs != null && keptUvs.Count == keptVertices.Count)
                mesh.SetUVs(0, keptUvs);
            mesh.subMeshCount = trianglesBySubmesh.Count;
            for (int submesh = 0; submesh < trianglesBySubmesh.Count; submesh++)
                mesh.SetTriangles(trianglesBySubmesh[submesh], submesh);
            if (keptNormals == null)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            int GetMappedIndex(int original)
            {
                if (remap.TryGetValue(original, out int mapped))
                    return mapped;

                mapped = keptVertices.Count;
                remap.Add(original, mapped);
                keptVertices.Add(vertices[original]);
                if (keptNormals != null)
                    keptNormals.Add(normals[original]);
                if (keptUvs != null)
                    keptUvs.Add(uvs[original]);
                return mapped;
            }
        }

        private bool OverlapsGazeboOcean(
            Vector3 worldA,
            Vector3 worldB,
            Vector3 worldC)
        {
            Vector3 a = Coordinates.ToEnu(worldA);
            Vector3 b = Coordinates.ToEnu(worldB);
            Vector3 c = Coordinates.ToEnu(worldC);
            float minEast = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float maxEast = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minNorth = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float maxNorth = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            // The whole environment is uniformly reduced for presentation,
            // therefore its central water cutout must use the same scale.
            float halfWidth =
                (GazeboOceanHalfWidth + VisualCoastClearance) *
                presentationScale;
            float halfLength =
                (GazeboOceanHalfLength + VisualCoastClearance) *
                presentationScale;

            // AABB overlap is intentionally conservative: large scenery
            // triangles touching the water boundary are removed completely,
            // preventing tree crowns or terrain wedges from leaking inward.
            return maxEast > -halfWidth && minEast < halfWidth &&
                   maxNorth > -halfLength && minNorth < halfLength;
        }

        private static void ConfigureTreeMaterial(
            Material material,
            Texture treeTexture
        )
        {
            material.mainTexture = treeTexture;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 1f);
            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", .18f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt(
                    "_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.One
                );
            if (material.HasProperty("_DstBlend"))
                material.SetInt(
                    "_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.Zero
                );
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 1);
            if (material.HasProperty("_Cull"))
                material.SetInt(
                    "_Cull",
                    (int)UnityEngine.Rendering.CullMode.Off
                );
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.enableInstancing = true;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        private static void RemoveImportedComponents(GameObject root)
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                Destroy(camera);
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                Destroy(light);
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
        }

        private void OnDestroy()
        {
            foreach (Material material in runtimeMaterials)
            {
                if (material)
                    Destroy(material);
            }
            runtimeMaterials.Clear();
            foreach (Mesh mesh in runtimeMeshes)
            {
                if (mesh)
                    Destroy(mesh);
            }
            runtimeMeshes.Clear();
        }
    }
}

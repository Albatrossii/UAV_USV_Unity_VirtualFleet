using UnityEngine;

namespace UavUsv
{
    public static class SceneFactory
    {
        public static Material Material(string name, Color color, float metallic = 0f, float smoothness = .35f)
        {
            var template = Resources.Load<Material>("RuntimeStandard");
            var material = template ? new Material(template) : new Material(Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse"));
            material.name = name;
            material.color = color;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (color.a < .999f)
            {
                material.SetFloat("_Mode", 3);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            return material;
        }

        public static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, Vector3 localEuler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            if (collider) Object.Destroy(collider);
            return go;
        }

        public static GameObject Cone(string name, Transform parent, Vector3 localPosition, float radius, float length,
            Material material, Vector3 localEuler, int sides = 32)
        {
            var mesh = new Mesh { name = name + " Mesh" };
            var vertices = new Vector3[sides + 2];
            var triangles = new int[sides * 6];
            vertices[0] = new Vector3(0, length * .5f, 0);
            vertices[1] = new Vector3(0, -length * .5f, 0);
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                vertices[i + 2] = new Vector3(Mathf.Cos(angle) * radius, -length * .5f, Mathf.Sin(angle) * radius);
                int next = (i + 1) % sides + 2;
                int t = i * 6;
                triangles[t] = 0; triangles[t + 1] = next; triangles[t + 2] = i + 2;
                triangles[t + 3] = 1; triangles[t + 4] = i + 2; triangles[t + 5] = next;
            }
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false); go.transform.localPosition = localPosition; go.transform.localEulerAngles = localEuler;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}

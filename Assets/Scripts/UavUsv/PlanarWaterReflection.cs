using UnityEngine;

namespace UavUsv
{
    [RequireComponent(typeof(Renderer))]
    public sealed class PlanarWaterReflection : MonoBehaviour
    {
        [Range(0.25f, 1f)] public float resolutionScale = .5f;
        public float clipPlaneOffset = .06f;
        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private int textureWidth;
        private int textureHeight;
        private static bool renderingReflection;

        private void OnWillRenderObject()
        {
            var source = Camera.current;
            var waterRenderer = GetComponent<Renderer>();
            if (!enabled || !waterRenderer.enabled || !source || renderingReflection) return;

            EnsureResources(source);
            CopyCamera(source, reflectionCamera);

            Vector3 planePosition = transform.position;
            Vector3 planeNormal = Vector3.up;
            float d = -Vector3.Dot(planeNormal, planePosition) - clipPlaneOffset;
            var reflectionPlane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, d);
            Matrix4x4 reflection = Matrix4x4.zero;
            CalculateReflectionMatrix(ref reflection, reflectionPlane);

            Vector3 oldPosition = source.transform.position;
            Vector3 reflectedPosition = reflection.MultiplyPoint(oldPosition);
            reflectionCamera.worldToCameraMatrix = source.worldToCameraMatrix * reflection;
            Vector4 clipPlane = CameraSpacePlane(reflectionCamera, planePosition, planeNormal, 1f);
            reflectionCamera.projectionMatrix = source.CalculateObliqueMatrix(clipPlane);
            reflectionCamera.cullingMask = source.cullingMask & ~(1 << gameObject.layer);
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.transform.position = reflectedPosition;
            Vector3 euler = source.transform.eulerAngles;
            reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);

            renderingReflection = true;
            bool oldCulling = GL.invertCulling;
            GL.invertCulling = true;
            reflectionCamera.Render();
            GL.invertCulling = oldCulling;
            renderingReflection = false;

            foreach (var material in waterRenderer.materials)
            {
                material.SetTexture("_PlanarReflectionTex", reflectionTexture);
                material.SetFloat("_ReflectionAvailable", 1f);
            }
        }

        private void EnsureResources(Camera source)
        {
            int width = Mathf.Max(256, Mathf.RoundToInt(source.pixelWidth * resolutionScale));
            int height = Mathf.Max(144, Mathf.RoundToInt(source.pixelHeight * resolutionScale));
            if (!reflectionTexture || width != textureWidth || height != textureHeight)
            {
                if (reflectionTexture) DestroyImmediate(reflectionTexture);
                textureWidth = width; textureHeight = height;
                reflectionTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGBHalf)
                {
                    name = "UAV-USV Planar Ocean Reflection",
                    hideFlags = HideFlags.DontSave,
                    useMipMap = true,
                    autoGenerateMips = true,
                    antiAliasing = 1
                };
            }
            if (!reflectionCamera)
            {
                var go = new GameObject("Ocean Reflection Camera", typeof(Camera), typeof(Skybox));
                go.hideFlags = HideFlags.HideAndDontSave;
                reflectionCamera = go.GetComponent<Camera>();
                reflectionCamera.enabled = false;
            }
        }

        private static void CopyCamera(Camera source, Camera destination)
        {
            destination.CopyFrom(source);
            destination.enabled = false;
            destination.useOcclusionCulling = false;
            destination.depthTextureMode = DepthTextureMode.None;
        }

        private Vector4 CameraSpacePlane(Camera camera, Vector3 position, Vector3 normal, float sideSign)
        {
            Vector3 offsetPosition = position + normal * clipPlaneOffset;
            Matrix4x4 matrix = camera.worldToCameraMatrix;
            Vector3 cameraPosition = matrix.MultiplyPoint(offsetPosition);
            Vector3 cameraNormal = matrix.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPosition, cameraNormal));
        }

        private static void CalculateReflectionMatrix(ref Matrix4x4 matrix, Vector4 plane)
        {
            matrix.m00 = 1f - 2f * plane[0] * plane[0]; matrix.m01 = -2f * plane[0] * plane[1]; matrix.m02 = -2f * plane[0] * plane[2]; matrix.m03 = -2f * plane[3] * plane[0];
            matrix.m10 = -2f * plane[1] * plane[0]; matrix.m11 = 1f - 2f * plane[1] * plane[1]; matrix.m12 = -2f * plane[1] * plane[2]; matrix.m13 = -2f * plane[3] * plane[1];
            matrix.m20 = -2f * plane[2] * plane[0]; matrix.m21 = -2f * plane[2] * plane[1]; matrix.m22 = 1f - 2f * plane[2] * plane[2]; matrix.m23 = -2f * plane[3] * plane[2];
            matrix.m30 = 0f; matrix.m31 = 0f; matrix.m32 = 0f; matrix.m33 = 1f;
        }

        private void OnDisable()
        {
            if (reflectionCamera) DestroyImmediate(reflectionCamera.gameObject);
            if (reflectionTexture) DestroyImmediate(reflectionTexture);
            reflectionCamera = null;
            reflectionTexture = null;
        }
    }
}

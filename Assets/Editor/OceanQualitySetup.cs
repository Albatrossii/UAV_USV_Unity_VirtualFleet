#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UavUsv.Editor
{
    [InitializeOnLoad]
    public static class OceanQualitySetup
    {
        static OceanQualitySetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (PlayerSettings.colorSpace != ColorSpace.Linear)
                {
                    PlayerSettings.colorSpace = ColorSpace.Linear;
                    Debug.Log("UAV-USV: switched project to Linear color space for physically plausible water lighting.");
                }
                const string skyMaterialPath = "Assets/Resources/Sky/PureOceanSky.mat";
                if (!AssetDatabase.LoadAssetAtPath<Material>(skyMaterialPath))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Resources/Sky/kloofendal_partly_cloudy_puresky_1k.hdr");
                    var shader = Shader.Find("Skybox/Panoramic");
                    if (texture && shader)
                    {
                        var material = new Material(shader) { name = "Partly Cloudy Pure Ocean Sky" };
                        material.SetTexture("_MainTex", texture);
                        material.SetFloat("_Exposure", .82f);
                        material.SetFloat("_Rotation", 72f);
                        AssetDatabase.CreateAsset(material, skyMaterialPath);
                        AssetDatabase.SaveAssets();
                    }
                }
            };
        }
    }
}
#endif

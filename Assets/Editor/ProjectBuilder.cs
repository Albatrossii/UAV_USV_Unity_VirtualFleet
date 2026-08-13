#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UavUsv.Editor
{
    public static class ProjectBuilder
    {
        private const string ScenePath = "Assets/Scenes/UavUsvDemo.unity";

        [MenuItem("UAV-USV/Create Demo Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Resources");
            if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/RuntimeStandard.mat"))
            {
                var shader = Shader.Find("Standard");
                if (!shader) throw new UnityEditor.Build.BuildFailedException("Built-in Standard shader was not found.");
                AssetDatabase.CreateAsset(new Material(shader) { name = "RuntimeStandard" }, "Assets/Resources/RuntimeStandard.mat");
            }
            const string skyMaterialPath = "Assets/Resources/Sky/PureOceanSky.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(skyMaterialPath))
            {
                var skyTexture = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Resources/Sky/kloofendal_partly_cloudy_puresky_1k.hdr");
                var skyShader = Shader.Find("Skybox/Panoramic");
                if (skyTexture && skyShader)
                {
                    var skyMaterial = new Material(skyShader) { name = "Partly Cloudy Pure Ocean Sky" };
                    skyMaterial.SetTexture("_MainTex", skyTexture);
                    skyMaterial.SetFloat("_Exposure", .82f);
                    skyMaterial.SetFloat("_Rotation", 72f);
                    AssetDatabase.CreateAsset(skyMaterial, skyMaterialPath);
                }
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("UAV-USV Simulation").AddComponent<SimulationBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        public static void BuildWindows()
        {
            CreateScene();
            Directory.CreateDirectory("Build");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Build/UAV_USV_Unity.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new UnityEditor.Build.BuildFailedException("Windows build failed: " + report.summary.result);
            Debug.Log("UAV-USV Windows build complete: " + Path.GetFullPath(options.locationPathName));
        }
    }
}
#endif

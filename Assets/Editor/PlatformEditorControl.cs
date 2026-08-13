#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UavUsv.Editor
{
    [InitializeOnLoad]
    public static class PlatformEditorControl
    {
        private const string ScenePath = "Assets/Scenes/UavUsvDemo.unity";
        private static readonly string ControlDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Library", "PlatformControl");
        private static readonly string StartRequest = Path.Combine(ControlDirectory, "start.request");
        private static readonly string StopRequest = Path.Combine(ControlDirectory, "stop.request");
        private static readonly bool ExitOnStop = HasArgument("--platform-exit-on-stop");
        private static double nextCheck;
        private static bool exitPending;

        static PlatformEditorControl()
        {
            Directory.CreateDirectory(ControlDirectory);
            if (HasArgument("--platform-auto-play"))
                File.WriteAllText(StartRequest, "start");
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + .5d;

            if (File.Exists(StopRequest))
            {
                File.Delete(StopRequest);
                if (EditorApplication.isPlaying)
                {
                    exitPending = ExitOnStop;
                    EditorApplication.isPlaying = false;
                }
                else if (ExitOnStop)
                {
                    EditorApplication.Exit(0);
                }
            }

            if (exitPending && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.Exit(0);
                return;
            }

            if (!File.Exists(StartRequest) || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            File.Delete(StartRequest);
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
            }
        }

        private static bool HasArgument(string expected)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument == expected)
                    return true;
            }
            return false;
        }
    }
}
#endif

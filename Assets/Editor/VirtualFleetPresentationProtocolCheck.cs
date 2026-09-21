#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UavUsv.PlatformTools;

namespace UavUsv.Editor.Tools
{
    public static class VirtualFleetPresentationProtocolCheck
    {
        [MenuItem("UAV-USV/Tools/Check Presentation Protocol")]
        public static void Run()
        {
            string json = VirtualFleetProtocol.SerializePresentationReport(
                new PresentationReportResponse
                {
                    type = VirtualFleetMessageTypes.PresentationReport,
                    protocolVersion = "unity.presentation.v1",
                    kind = "SCENE_READY",
                    executionId = null,
                    applied = true
                });

            if (!json.Contains("\"kind\":\"SCENE_READY\""))
                throw new BuildFailedException("SCENE_READY kind is missing from the presentation report");
            if (!json.Contains("\"executionId\":null"))
                throw new BuildFailedException("SCENE_READY executionId must be JSON null");
            if (json.Contains("\"executionId\":\"\""))
                throw new BuildFailedException("SCENE_READY executionId must not be an empty string");

            Debug.Log("Presentation protocol check passed: SCENE_READY executionId=null");
        }
    }
}
#endif

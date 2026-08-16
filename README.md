# UAV-USV Unity Virtual Fleet

Unity WebGL virtual fleet simulation for UAV/USV capture and escort algorithm validation.

This project provides a virtual simulation environment only. It does not connect to
ROS, ROS2, Gazebo, PX4, real vehicles, GPS, radar, or video sensors.

## Project Status

- Runtime mode: `VIRTUAL_SIMULATION`
- Coordinate frame sent to Unity: `GLOBAL_ENU`
- Unity version: `2022.3.57f1`
- Main scene: `Assets/Scenes/UavUsvVirtualFleet.unity`
- WebGL bridge: `VirtualFleetPlatformBridge` and `WebCommandBridge`
- Supported algorithms:
  - `GB_SFLA_CS`: GB-SFLA-CS cooperative capture simulation
  - `ESCORT_GUARD`: mixed UAV/USV escort and guard simulation

## Features

- Runtime generation of UAV, USV, and target devices.
- Device counts from 1 to 100 UAVs and 1 to 100 USVs.
- Standard device identifiers:

```text
UAV-001 ~ UAV-100
USV-001 ~ USV-100
TARGET-001
```

- Batch pose updates through `ApplyPoseBatch`.
- Mission state control: start, pause, stop, and reset.
- Device selection and follow camera.
- Overview camera with automatic framing for large fleets.
- Runtime virtual fleet trails.
- WebGL acknowledgements with matching `requestId`.
- Pose sequence and run validation.

## Message Flow

The normal WebGL integration flow is:

```text
initializePlatform
    -> loadScenario
    -> scenarioReady
    -> applyPoseBatch
    -> poseFrameApplied
    -> setCameraMode
    -> missionStart / missionPause / missionStop / missionReset
```

`loadScenario` should provide the runtime mode, run identifier, algorithm code,
and requested UAV/USV counts. The Unity response reports the actual device list
and mission state.

For pose updates:

- Use `GLOBAL_ENU` coordinates.
- `eastM`, `northM`, and `upM` are expressed in meters.
- `headingDeg` is the device heading in degrees.
- `sequence` must increase monotonically within a run.
- `speedMps` is available for validation and status display.

The Bridge preserves the original request `requestId` in its acknowledgement or
event response. Important responses include:

```text
scenarioReady
poseFrameApplied
commandAck
cameraChanged
```

## Running in the Unity Editor

1. Open the project with Unity `2022.3.57f1`.
2. Open:

   ```text
   Assets/Scenes/UavUsvVirtualFleet.unity
   ```

3. Enter Play mode.
4. Use the scene test controls or the WebGL frontend to:
   - generate a scenario;
   - apply a pose batch;
   - start, pause, stop, or reset a mission;
   - select a device;
   - switch between follow and overview cameras.

The scene should contain one Bridge host with:

```text
VirtualFleetPlatformBridge
WebCommandBridge
```

Avoid adding duplicate Bridge instances because duplicate listeners can produce
duplicate browser events.

## Building WebGL

Close any open Unity Editor instance using this project before running a
batchmode build. Then run PowerShell:

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.57f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "C:\path\to\UAV_USV_Unity_VirtualFleet" `
  -buildTarget WebGL `
  -executeMethod UavUsv.Editor.Tools.VueWebGlBuildTool.BuildVirtualFleet `
  -logFile "Temp\webgl-build.log"
```

The generated WebGL files can be copied to the frontend project under:

```text
frontend/public/unity
```

After replacing the build, use a hard refresh in the browser to avoid loading
an old WebGL cache.

## Validation

The current acceptance record is available at:

```text
docs/virtual-fleet-acceptance-2026-08-16.md
```

Validated scenarios:

| Scenario | Expected result |
| --- | --- |
| 3 UAV + 3 USV + 1 Target | `sent=7`, `applied=7`, `unknown=[]`, `missing=[]`, `success=true` |
| 100 UAV + 100 USV + 1 Target | `sent=201`, `applied=201`, `unknown=[]`, `missing=[]`, `success=true` |

The validation also confirmed:

- UAV, USV, and Target pose updates are visible in Unity.
- `poseFrameApplied.success=true`.
- 200 device trails can be refreshed for the 100+100 case.
- Overview camera framing works for the large fleet.
- `missionReset` allows a new scenario to be generated.
- No ROS connection or real-device command is used.

## Repository Layout

```text
Assets/Scenes/UavUsvVirtualFleet.unity
Assets/Scripts/UavUsv/PlatformTools/
    VirtualFleetPlatformBridge.cs
    VirtualFleetMessageValidator.cs
    VirtualFleetPoseOwnershipGuard.cs
    WebCommandBridge.cs
    WebDeviceObserverCamera.cs
Assets/Resources/RuntimeStandard.mat
docs/virtual-fleet-acceptance-2026-08-16.md
VirtualFleetRequirements-v2.zh-CN.md
WebSocketBridge/
```

The `PlatformTools` scripts handle protocol validation, WebGL command forwarding,
pose ownership, camera control, and response generation. Scene construction and
runtime device generation remain in the virtual fleet scene and its manager
components.

## Related Documentation

- `docs/virtual-fleet-protocol-v1.zh-CN.md`: protocol and message definitions.
- `docs/virtual-fleet-acceptance-2026-08-16.md`: latest acceptance record.
- `VirtualFleetRequirements-v2.zh-CN.md`: project requirements.

## Scope and Boundaries

This repository is responsible for the Unity virtual simulation and its Bridge
protocol. The frontend WebGL page is maintained in the separate
`UAV_USV_Platform` repository.

Do not use this project to control real UAVs or USVs. Any future real-vehicle
integration must be implemented as a separate runtime mode with an explicit
review of safety, transport, and authorization boundaries.

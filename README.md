# UAV-USV Unity 虚拟编队

本项目是基于 Unity WebGL 的 UAV/USV 虚拟编队仿真项目，用于验证协同围捕和混合护航算法。

当前阶段仅使用虚拟设备，不连接 ROS、ROS2、Gazebo、PX4，也不控制真实无人机或无人船。

## 项目状态

- 运行模式：`VIRTUAL_SIMULATION`
- 坐标系：`GLOBAL_ENU`
- Unity 版本：`2022.3.57f1`
- 主场景：`Assets/Scenes/UavUsvVirtualFleet.unity`
- WebGL Bridge：`VirtualFleetPlatformBridge`、`WebCommandBridge`
- 前端项目：`UAV_USV_Platform`

支持的算法编号：

```text
GB_SFLA_CS   GB-SFLA-CS 协同围捕（模拟）
ESCORT_GUARD 混合 UAV/USV 护航守卫（模拟）
```

## 主要功能

- 运行时动态生成 UAV、USV 和 Target。
- 支持 1~100 台 UAV、1~100 台 USV。
- 支持批量位姿更新。
- 支持任务开始、暂停、停止和重置。
- 支持设备选择和设备跟随相机。
- 支持全局视角和大规模设备自动取景。
- 支持虚拟设备运动轨迹。
- 支持 WebGL 回执和 `requestId` 对齐。
- 支持 `runId`、位姿序列和任务状态校验。

标准设备编号：

```text
UAV-001 ~ UAV-100
USV-001 ~ USV-100
TARGET-001
```

## WebGL 消息流程

正常联调流程如下：

```text
initializePlatform
    -> loadScenario
    -> scenarioReady
    -> applyPoseBatch
    -> poseFrameApplied
    -> setCameraMode
    -> missionStart / missionPause / missionStop / missionReset
```

重要回执类型：

```text
scenarioReady
poseFrameApplied
commandAck
cameraChanged
```

位姿数据约定：

- Unity 接收的坐标系必须为 `GLOBAL_ENU`。
- `eastM`、`northM`、`upM` 的单位为米。
- `headingDeg` 表示设备朝向，单位为度。
- 同一个运行实例中的 `sequence` 必须递增。
- `speedMps` 用于速度校验或状态显示。
- Unity 回执中的 `requestId` 必须与前端请求一致。

## 在 Unity 编辑器中运行

1. 使用 Unity `2022.3.57f1` 打开本项目。
2. 打开场景：

   ```text
   Assets/Scenes/UavUsvVirtualFleet.unity
   ```

3. 点击 Play 进入运行模式。
4. 使用场景测试控件或前端页面执行：
   - 生成场景；
   - 发送批量位姿；
   - 开始、暂停、停止或重置任务；
   - 选择设备；
   - 切换设备跟随和全局视角。

场景中应保持一个 Bridge 宿主，并包含：

```text
VirtualFleetPlatformBridge
WebCommandBridge
```

不要重复添加 Bridge 实例，否则可能造成浏览器收到重复回执。

## 构建 Unity WebGL

构建前先关闭使用本项目的 Unity 编辑器实例，然后在 PowerShell 中执行：

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.57f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "C:\path\to\UAV_USV_Unity_VirtualFleet" `
  -buildTarget WebGL `
  -executeMethod UavUsv.Editor.Tools.VueWebGlBuildTool.BuildVirtualFleet `
  -logFile "Temp\webgl-build.log"
```

构建完成后，将 WebGL 文件替换到前端项目：

```text
UAV_USV_Platform/frontend/public/unity
```

替换构建包后，浏览器需要执行强制刷新，避免使用旧的 WebGL 缓存。

## 验收结果

验收记录见：

```text
docs/virtual-fleet-acceptance-2026-08-16.md
```

已验证场景：

| 场景 | 验证结果 |
| --- | --- |
| 3 UAV + 3 USV + 1 Target | `sent=7`、`applied=7`、`unknown=[]`、`missing=[]`、`success=true` |
| 100 UAV + 100 USV + 1 Target | `sent=201`、`applied=201`、`unknown=[]`、`missing=[]`、`success=true` |

已确认：

- UAV、USV 和 Target 均可实际移动。
- `poseFrameApplied.success=true`。
- 100+100 场景可刷新 200 条设备轨迹。
- 全局相机可以根据设备数量和分布自动取景。
- `missionReset` 后可以重新生成场景。
- 测试过程未连接 ROS，也未发送真实设备控制指令。

## 目录说明

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

`PlatformTools` 主要负责协议校验、WebGL 命令转发、位姿所有权、相机控制和回执生成。
虚拟场景、设备生成和设备管理由 Unity 场景及其管理组件负责。

## 相关文档

- `docs/virtual-fleet-protocol-v1.zh-CN.md`：接口协议和消息定义。
- `docs/virtual-fleet-acceptance-2026-08-16.md`：最新验收记录。
- `VirtualFleetRequirements-v2.zh-CN.md`：项目需求文档。

## 项目边界

本仓库负责 Unity 虚拟仿真场景和 Bridge 协议。
前端 WebGL 页面维护在独立的 `UAV_USV_Platform` 仓库中。

本项目不得用于控制真实 UAV 或 USV。未来如需接入真实设备，应新增独立运行模式，
并单独审核通信、安全和授权边界。

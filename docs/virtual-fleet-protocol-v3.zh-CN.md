# UAV-USV 虚拟编队接口协议 v3.0

状态：A/B 联调协议  
适用项目：`Albatrossii/UAV_USV_Unity_VirtualFleet`  
运行模式：`VIRTUAL_SIMULATION`  
更新时间：2026-08-15

## 1. 协议目标

本协议用于约定前端、算法适配层、Unity WebGL Bridge 和 Unity 虚拟编队运行时之间的数据格式。

支持算法：

```text
GB_SFLA_CS    GB-SFLA-CS 协同围捕
ESCORT_GUARD  混合 UAV/USV 护航守卫
```

本阶段：

- 不连接 ROS/ROS2；
- 不连接 Gazebo、PX4 或真实设备；
- 不读取真实 GPS、雷达、视频或其他传感器；
- 不发送真实控制指令；
- 所有设备均标记为虚拟设备。

## 2. 消息外层格式

所有消息通过 Unity WebGL iframe 的 `postMessage` 通道传输。

```json
{
  "type": "loadScenario",
  "requestId": "loadScenario:001",
  "timestamp": 1786700000000,
  "payload": {}
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| `type` | string | 是 | camelCase 消息类型 |
| `requestId` | string | 是 | 请求唯一编号 |
| `timestamp` | integer | 是 | Unix 毫秒时间戳 |
| `payload` | object | 是 | 消息内容 |

规则：

1. Unity 必须在回执中原样返回请求的 `requestId`。
2. 未知消息类型返回 `unsupported_message`。
3. 不支持的字段可以忽略，但不得改变已定义字段的含义。
4. `runtimeMode` 必须为 `VIRTUAL_SIMULATION`。

## 3. 设备和算法标识

设备编号：

```text
UAV-001 ~ UAV-100
USV-001 ~ USV-100
TARGET-001 ~ TARGET-020
```

算法编号：

```text
GB_SFLA_CS
ESCORT_GUARD
```

## 4. 坐标、速度和朝向

算法运行时输出以编队中心为原点的局部 ENU 坐标，单位为米：

```text
coordinateFrame = FLEET_LOCAL_ENU
x = 局部东向
y = 局部北向
z = 局部上向
```

当前虚拟编队全局 ENU 原点为：

```text
fleetOriginEastM  = -75
fleetOriginNorthM = -310
fleetOriginUpM    = 0
```

B 侧算法适配器必须先转换为全局 ENU：

```text
eastM  = x + fleetOriginEastM
northM = y + fleetOriginNorthM
upM    = z + fleetOriginUpM
```

`applyPoseBatch` 中的 `eastM`、`northM`、`upM` 始终为全局 ENU。
Unity 不再增加编队原点偏移，只执行表现层坐标转换：

```text
Unity world = (eastM, upM, northM) * PresentationCoordinateScale
```

因此局部算法坐标 `(0,0,0)` 对应：

```text
全局 ENU = (-75,-310,0)
Unity    = (-13.5,0,-55.8)
```

若未来算法直接输出全局 ENU，必须明确声明
`coordinateFrame = GLOBAL_ENU`，B 侧不得再增加原点偏移。
未声明 `coordinateFrame` 的算法帧必须拒绝，不允许自动猜测。

朝向字段 `headingDeg`：

```text
0   = 北
90  = 东
180 = 南
270 = 西
```

设备速度限制：

| 设备 | 最大速度 |
|---|---:|
| UAV | 15 m/s |
| USV | 2 m/s |

所有协议速度字段均使用真实世界单位 `m/s`。

```text
UAV: 0 <= speedMps <= 15
USV: 0 <= speedMps <= 2
```

前端输入的初始速度也必须使用真实世界单位，并且不能超过对应设备的速度上限。

### 4.2 Unity 表现层速度换算

当前 Unity 使用：

```text
Coordinates.PresentationCoordinateScale = 0.18
```

真实世界位置和 Unity 表现层位置的换算为：

```text
unityPosition = realPosition * PresentationCoordinateScale
```

真实世界速度和 Unity 内部表现层速度的换算为：

```text
unitySpeedUnitsPerSecond = realSpeedMps * PresentationCoordinateScale
realSpeedMps = unitySpeedUnitsPerSecond / PresentationCoordinateScale
```

换算示例：

```text
UAV 15 m/s -> Unity 2.70 units/s
USV 2 m/s  -> Unity 0.36 units/s
```

前端和算法服务只传真实世界的米、米每秒，不得自行乘以 `0.18`。算法适配层必须先将归一化速度转换为真实 `m/s`，再由 Unity 转换为表现层速度。

## 5. 初始化接口

### 5.1 `initializePlatform`

请求：

```json
{
  "type": "initializePlatform",
  "requestId": "initializePlatform:001",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "protocolVersion": "3.0",
    "buildId": "unity-virtual-fleet-v3"
  }
}
```

回执：

```json
{
  "type": "platformBridgeReady",
  "requestId": "initializePlatform:001",
  "timestamp": 1786700000000,
  "payload": {
    "ready": true,
    "runtimeMode": "VIRTUAL_SIMULATION",
    "protocolVersion": "3.0",
    "buildId": "unity-virtual-fleet-v3",
    "cameraReady": true,
    "controlsReady": true,
    "algorithmReady": true,
    "maxUavCount": 100,
    "maxUsvCount": 100,
    "capabilities": [
      "virtual-fleet",
      "dynamic-generation",
      "random-spawn",
      "pose-batch",
      "mission-control",
      "camera-control"
    ]
  }
}
```

## 6. 场景配置接口

### 6.1 `loadScenario`

请求：

```json
{
  "type": "loadScenario",
  "requestId": "loadScenario:001",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "algorithmCode": "GB_SFLA_CS",
    "runId": 7001,
    "uavCount": 3,
    "usvCount": 3,
    "targetCount": 1,
    "initialSpeedMps": 2.0,
    "seed": 20260814
  }
}
```

字段要求：

| 字段 | 规则 |
|---|---|
| `algorithmCode` | `GB_SFLA_CS` 或 `ESCORT_GUARD` |
| `runId` | 正整数 |
| `uavCount` | 1~100 |
| `usvCount` | 1~100 |
| `targetCount` | 1~20 |
| `initialSpeedMps` | 可选，真实世界速度，单位 `m/s`；必须符合对应设备上限 |
| `seed` | 整数，用于复现实验布局 |

Unity 根据 `seed` 生成初始位置。初始位置必须满足：

- UAV 位于有效空域；
- USV 位于海面；
- 设备之间不重叠；
- 设备和目标满足最小安全距离。

`loadScenario` 成功后，Unity 必须在 `scenarioReady.payload.initialPoses`
中返回本次实际生成的每台设备初始位姿。该字段是算法服务初始化的唯一依据，
不能由算法服务重新生成固定网格覆盖这些位置。

`initialPoses` 中的坐标必须使用 `GLOBAL_ENU`：

```json
{
  "deviceCode": "UAV-001",
  "deviceType": "UAV",
  "eastM": -120.5,
  "northM": -260.2,
  "upM": 28.0,
  "headingDeg": 90.0,
  "speedMps": 0.0,
  "state": "STOPPED",
  "valid": true
}
```

同时返回：

```json
{
  "initialPosesCoordinateFrame": "GLOBAL_ENU",
  "fleetOrigin": {
    "eastM": -75.0,
    "northM": -310.0,
    "upM": 0.0
  }
}
```

算法服务必须先读取 `initialPoses`，再按设备编号建立初始状态：

```text
localEast  = globalEast  - fleetOrigin.eastM
localNorth = globalNorth - fleetOrigin.northM
localUp    = globalUp    - fleetOrigin.upM
```

算法内部可以使用 `FLEET_LOCAL_ENU` 计算，但发送给 Unity 的
`ApplyPoseBatch` 必须转换回 `GLOBAL_ENU`。没有 `initialPoses` 时，
仅允许使用固定网格作为兼容回退，并应在日志中明确标记。

前端不再发送以下字段：

```text
initialHeadingDeg
formationType
```

`initialSpeedMps` 作为兼容字段时，必须表示真实世界速度，单位为 `m/s`，并满足：

1. UAV 场景不能大于 `15`；
2. USV 场景不能大于 `2`；
3. Unity 内部使用 `Coordinates.PresentationCoordinateScale` 转换；
4. 混合 UAV/USV 场景不应使用一个场景级速度同时代表两种设备；
5. 混合场景优先使用 `ApplyPoseBatch` 中每台设备的 `speedMps`。

初始朝向由算法方向或速度方向计算，不再由前端发送 `initialHeadingDeg`。

### 6.2 `regenerateScenario`

请求结构与 `loadScenario` 相同：

```json
{
  "type": "regenerateScenario",
  "requestId": "regenerateScenario:001",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "algorithmCode": "ESCORT_GUARD",
    "runId": 7002,
    "uavCount": 5,
    "usvCount": 5,
    "targetCount": 1,
    "initialSpeedMps": 1.5,
    "seed": 20260815
  }
}
```

只允许在 `STOPPED` 或 `RESETTING` 状态执行。任务运行中返回：

```text
scenario_locked
```

## 7. 算法位姿批量接口

### 7.1 `applyPoseBatch`

请求：

```json
{
  "type": "applyPoseBatch",
  "requestId": "applyPoseBatch:7001:128",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 7001,
    "sequence": 128,
    "sampleTime": 1786700000123,
    "vehicles": [
      {
        "deviceCode": "UAV-001",
        "deviceType": "UAV",
        "eastM": 35.26,
        "northM": -12.47,
        "upM": 30.0,
        "velocityEastMps": 3.0,
        "velocityNorthMps": 0.0,
        "velocityUpMps": 1.0,
        "headingDeg": 18.4,
        "speedMps": 3.16,
        "role": "capture",
        "targetCode": "TARGET-001",
        "state": "AIRBORNE",
        "valid": true
      },
      {
        "deviceCode": "USV-001",
        "deviceType": "USV",
        "eastM": 18.2,
        "northM": 4.1,
        "upM": 0.0,
        "velocityEastMps": 1.2,
        "velocityNorthMps": 0.0,
        "velocityUpMps": 0.0,
        "headingDeg": 90.0,
        "speedMps": 1.2,
        "role": "escort",
        "targetCode": "TARGET-001",
        "state": "SAILING",
        "valid": true
      }
    ],
    "targets": [
      {
        "deviceCode": "TARGET-001",
        "eastM": 0.0,
        "northM": 0.0,
        "upM": 0.0,
        "headingDeg": 0.0,
        "speedMps": 0.0,
        "state": "ACTIVE",
        "valid": true
      }
    ]
  }
}
```

位姿字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `deviceCode` | string | 标准设备编号 |
| `deviceType` | string | `UAV`、`USV` 或 `TARGET` |
| `eastM/northM/upM` | number | ENU 位置 |
| `velocity*Mps` | number | 算法期望速度分量 |
| `headingDeg` | number | 设备朝向 |
| `speedMps` | number | 真实世界速度，单位 `m/s`；用于速度校验和状态显示 |
| `role` | string | `capture`、`escort`、`core`、`wing` 等 |
| `targetCode` | string | 关联目标 |
| `state` | string | 设备状态 |
| `valid` | boolean | 当前数据是否有效 |

Unity 必须：

1. 校验 `runtimeMode`；
2. 校验 `runId`；
3. 拒绝 `sequence` 小于或等于上一帧的批次；
4. 校验设备编号和设备类型；
5. 对 UAV 限制为 `0~15 m/s`；
6. 对 USV 限制为 `0~2 m/s`；
7. 将真实速度乘以 `Coordinates.PresentationCoordinateScale` 后用于 Unity 内部运动；
8. 根据有效速度方向更新模型朝向；
9. 单个未知设备不得导致整批失败；
10. 每帧只应用最新有效批次；
11. 将限幅前后结果用于诊断和状态显示。

### 7.2 `poseFrameApplied`

```json
{
  "type": "poseFrameApplied",
  "requestId": "applyPoseBatch:7001:128",
  "timestamp": 1786700000000,
  "payload": {
    "success": true,
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 7001,
    "sequence": 128,
    "appliedCount": 2,
    "speedLimitedCount": 0,
    "missingDeviceCodes": [],
    "unknownDeviceCodes": []
  }
}
```

## 8. 任务控制接口

支持：

```text
missionStart
missionPause
missionResume
missionStop
missionReset
```

请求示例：

```json
{
  "type": "missionStart",
  "requestId": "missionStart:001",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 7001
  }
}
```

状态：

```text
STOPPED
RUNNING
PAUSED
CAPTURING
ESCORTING
THREAT_DETECTED
COMPLETED
RESETTING
ERROR
```

基本转换：

```text
STOPPED -> RUNNING
RUNNING -> PAUSED
PAUSED  -> RUNNING
RUNNING -> STOPPED
PAUSED  -> STOPPED
STOPPED -> RESETTING
RESETTING -> STOPPED
```

回执：

```json
{
  "type": "missionStateChanged",
  "requestId": "missionStart:001",
  "timestamp": 1786700000000,
  "payload": {
    "success": true,
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 7001,
    "missionState": "RUNNING"
  }
}
```

## 9. 相机和设备选择接口

### 9.1 `selectDevice`

```json
{
  "type": "selectDevice",
  "requestId": "selectDevice:001",
  "timestamp": 1786700000000,
  "payload": {
    "deviceCode": "UAV-001"
  }
}
```

### 9.2 `setCameraMode`

```json
{
  "type": "setCameraMode",
  "requestId": "setCameraMode:001",
  "timestamp": 1786700000000,
  "payload": {
    "mode": "device-follow",
    "deviceCode": "UAV-001"
  }
}
```

支持模式：

```text
overview
device-follow
```

### 9.3 `cameraChanged`

```json
{
  "type": "cameraChanged",
  "requestId": "setCameraMode:001",
  "timestamp": 1786700000000,
  "payload": {
    "success": true,
    "deviceCode": "UAV-001",
    "mode": "device-follow",
    "status": "Camera following UAV-001"
  }
}
```

`cameraChanged.requestId` 必须与原始 `setCameraMode.requestId` 完全一致。

## 10. 场景和任务回执

### 10.1 `scenarioReady`

```json
{
  "type": "scenarioReady",
  "requestId": "loadScenario:001",
  "timestamp": 1786700000000,
  "payload": {
    "success": true,
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 7001,
    "algorithmCode": "GB_SFLA_CS",
    "uavCount": 3,
    "usvCount": 3,
    "targetCount": 1,
    "seed": 20260814,
    "deviceCodes": [
      "UAV-001",
      "UAV-002",
      "UAV-003",
      "USV-001",
      "USV-002",
      "USV-003",
      "TARGET-001"
    ],
    "initialPosesCoordinateFrame": "GLOBAL_ENU",
    "fleetOrigin": {
      "eastM": -75.0,
      "northM": -310.0,
      "upM": 0.0
    },
    "initialPoses": [
      {
        "deviceCode": "UAV-001",
        "deviceType": "UAV",
        "eastM": -120.5,
        "northM": -260.2,
        "upM": 28.0,
        "headingDeg": 90.0,
        "speedMps": 0.0,
        "state": "STOPPED",
        "valid": true
      }
    ],
    "missionState": "STOPPED"
  }
}
```

### 10.2 错误回执

```json
{
  "type": "commandAck",
  "requestId": "applyPoseBatch:7001:127",
  "timestamp": 1786700000000,
  "payload": {
    "success": false,
    "code": "sequence_rewind",
    "message": "Pose sequence is not newer than the last accepted sequence",
    "runId": 7001
  }
}
```

标准错误码：

```text
unsupported_message
invalid_payload
invalid_runtime_mode
invalid_algorithm
invalid_count
invalid_device_code
invalid_device_type
invalid_speed
scenario_locked
run_not_loaded
run_mismatch
sequence_rewind
mission_state_conflict
device_not_found
camera_bridge_not_ready
```

## 11. 算法适配要求

### 11.1 GB-SFLA-CS

适配层至少应输出：

- 设备到目标的分配关系；
- 围捕航点；
- UAV/USV 速度向量；
- 设备 heading；
- 捕获半径；
- 捕获状态；
- 目标负载均衡；
- 重配置次数；
- 捕获成功率。

### 11.2 ESCORT_GUARD

适配层至少应输出：

- 威胁目标位置；
- 威胁方向；
- 核心阻断设备；
- 翼侧守卫设备；
- 阻断点；
- 护航弧或护航区域；
- 避让方向；
- 各设备目标位置；
- 各设备速度向量；
- 严格阻断约束是否满足。

## 12. 兼容和迁移规则

v3 的非兼容调整：

- `initialSpeedMps` 改为真实世界单位 `m/s`，并受设备类型速度上限校验；
- 删除 `initialHeadingDeg`；
- 删除 `formationType`；
- 速度由算法输出并经过设备类型限幅；
- 朝向由算法方向或速度方向更新；
- `ApplyPoseBatch` 增加速度向量、角色和目标关联字段。

当前 Unity 代码中暂时保留的旧字段只能按以下规则处理：

```text
initialSpeedMps
initialHeadingDeg
formationType
```

其中 `initialSpeedMps` 可以继续作为兼容输入，但必须按本协议解释为真实 `m/s`；`initialHeadingDeg` 和 `formationType` 应被忽略。

## 13. 联调验收清单

- [x] `platformBridgeReady` 返回协议版本和能力列表；
- [x] `loadScenario` 支持两个算法；
- [x] 前端不发送 `initialHeadingDeg` 和 `formationType`；
- [x] `initialSpeedMps` 若存在，按真实 `m/s` 解释；
- [x] 前端输入速度不超过对应设备上限；
- [x] Unity 使用 `PresentationCoordinateScale` 转换内部速度；
- [x] 相同 `seed` 能复现初始布局；
- [x] `ApplyPoseBatch` 能正确回执 `requestId`；
- [x] `ApplyPoseBatch.speedMps` 用于速度校验或状态显示；
- [x] UAV 速度不会超过 15 m/s；
- [x] USV 速度不会超过 2 m/s；
- [x] 旧 `sequence` 会被拒绝；
- [x] 未知设备不会导致整批失败；
- [x] GB-SFLA-CS 能驱动围捕轨迹；
- [x] ESCORT_GUARD 能驱动护航轨迹；
- [x] `missionStateChanged` 状态正确；
- [x] `cameraChanged` 的 `requestId` 与请求一致；
- [x] 任务运行中不能修改设备数量；
- [x] 任务运行中不能重新生成场景；
- [x] 100 UAV + 100 USV 能稳定运行；
- [x] 全流程不连接 ROS。

补充验证：Python 适配层 `11/11` 测试通过；算法首帧使用 Unity
返回的 `initialPoses`，后续帧按 sequence 顺序连续发送。

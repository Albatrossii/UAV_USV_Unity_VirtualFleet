# UAV-USV 虚拟编队协议 V2.0

状态：A/B 联调协议
适用项目：`Albatrossii/UAV_USV_Unity_VirtualFleet`
运行模式：`VIRTUAL_SIMULATION`
更新时间：2026 年 8 月 14 日

## 1. 协议目标

本协议用于约定前端平台、Unity WebGL Bridge 和 Unity 虚拟编队运行时之间的接口。

本阶段只做虚拟设备算法验证，不连接：

- ROS、ROS2
- Gazebo、PX4
- 真实无人机和无人船
- 真实 GPS、雷达、视频或其他传感器

本阶段支持以下两个算法：

```text
GB_SFLA_CS      GB-SFLA-CS 协同围捕（模拟）
ESCORT_GUARD    混合 UAV/USV 护航守卫（模拟）
```

UAV 和 USV 从随机初始位置生成。前端不指定固定队形，算法根据目标、
设备位置和任务状态自动计算围捕或护航部署。

## 2. 消息传输

继续使用现有 Unity WebGL iframe `postMessage` 通道。

所有消息使用以下外层结构：

```json
{
  "type": "loadScenario",
  "requestId": "loadScenario:1786700000000:abc123",
  "timestamp": 1786700000000,
  "payload": {}
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `type` | string | 是 | 消息类型，使用 camelCase |
| `requestId` | string | 命令必填 | 请求唯一编号 |
| `timestamp` | integer | 是 | Unix 毫秒时间戳 |
| `payload` | object | 是 | 具体消息内容 |

接收方必须忽略未知字段。带有 `requestId` 的未知消息类型必须返回
`unsupported_message`。

Unity 返回的回执必须沿用原请求的 `requestId`。不得在 Bridge 适配层
无故替换成新的请求编号。

## 3. 设备编号、算法和运行模式

标准设备编号：

```text
UAV-001 ~ UAV-100
USV-001 ~ USV-100
TARGET-001
```

输入端可以兼容旧编号，例如：

```text
uav_01
UAV-01
```

进入 Unity 内部和所有回执后必须标准化为三位编号：

```text
UAV-001
```

算法编号：

```text
GB_SFLA_CS
ESCORT_GUARD
```

运行模式：

```text
VIRTUAL_SIMULATION
```

本阶段不得连接 `REAL_ROS`。

## 4. 坐标、速度和随机生成

配置数据和位姿数据统一使用局部 ENU 坐标，单位为米：

```text
eastM  = 东向
northM = 北向
upM    = 上向
```

航向角字段为 `headingDeg`，以北为 0 度，顺时针增加：

```text
0   = 北
90  = 东
180 = 南
270 = 西
```

Unity 只在适配层进行一次坐标转换：

```text
Unity world = (eastM, upM, northM)
```

前端和算法服务不得直接发送 Unity X/Z 坐标冒充 ENU 坐标。

设备初始位置由 Unity 运行时随机生成。`seed` 用于复现随机结果。
初始位置必须满足：

- UAV、USV 和 Target 不重叠；
- 设备不超出场景有效区域；
- UAV 位于有效空中高度；
- USV 位于有效水面区域；
- Target 位于可观测范围内。

`initialSpeedMps` 和 `initialHeadingDeg` 用于设置初始运动参数。

## 5. 初始化接口

### 5.1 `initializePlatform`

Unity 加载完成后，平台发送初始化消息：

```json
{
  "type": "initializePlatform",
  "requestId": "initializePlatform:001",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "protocolVersion": "2.0",
    "buildId": "unity-virtual-fleet-v2"
  }
}
```

Unity 返回 `platformBridgeReady`：

```json
{
  "type": "platformBridgeReady",
  "requestId": "initializePlatform:001",
  "timestamp": 1786700000000,
  "payload": {
    "ready": true,
    "runtimeMode": "VIRTUAL_SIMULATION",
    "protocolVersion": "2.0",
    "buildId": "unity-virtual-fleet-v2",
    "cameraReady": true,
    "controlsReady": true,
    "algorithmReady": true,
    "visualSensorReady": false,
    "maxUavCount": 100,
    "maxUsvCount": 100,
    "capabilities": [
      "virtual-fleet",
      "dynamic-generation",
      "random-spawn",
      "object-pool",
      "pose-batch",
      "mission-control",
      "camera-control"
    ]
  }
}
```

## 6. 场景配置接口

### 6.1 `loadScenario`

加载算法和虚拟编队配置，但不自动开始任务。

```json
{
  "type": "loadScenario",
  "requestId": "loadScenario:001",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "algorithmCode": "GB_SFLA_CS",
    "runId": 7001,
    "uavCount": 10,
    "usvCount": 8,
    "targetCount": 1,
    "initialSpeedMps": 2.0,
    "initialHeadingDeg": 0.0,
    "seed": 20260814
  }
}
```

字段要求：

| 字段 | 规则 |
| --- | --- |
| `runtimeMode` | 必须为 `VIRTUAL_SIMULATION` |
| `algorithmCode` | `GB_SFLA_CS` 或 `ESCORT_GUARD` |
| `runId` | 正整数 |
| `uavCount` | 1~100 |
| `usvCount` | 1~100 |
| `targetCount` | 当前版本固定为 1 |
| `initialSpeedMps` | 大于等于 0 |
| `initialHeadingDeg` | 自动归一化到 [0, 360) |
| `seed` | 整数，用于复现随机初始位置 |

`loadScenario` 不再包含以下字段：

```text
formationType
RANDOM
CIRCLE
ENCIRCLEMENT
ESCORT
```

围捕和护航部署由算法自动计算，前端不得指定固定队形。

### 6.2 `regenerateScenario`

停止或重置任务后，清理旧设备并按新配置重新随机生成场景。

```json
{
  "type": "regenerateScenario",
  "requestId": "regenerateScenario:001",
  "timestamp": 1786700000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "algorithmCode": "ESCORT_GUARD",
    "runId": 7002,
    "uavCount": 12,
    "usvCount": 10,
    "targetCount": 1,
    "initialSpeedMps": 2.0,
    "initialHeadingDeg": 90.0,
    "seed": 20260815
  }
}
```

只有 `STOPPED` 或 `RESET` 状态允许重新生成。
`RUNNING` 或 `PAUSED` 状态必须返回 `scenario_locked`。

重新生成后，Unity 必须返回新的设备编号列表和新的 `runId`。

## 7. 自动围捕和护航行为

### 7.1 `GB_SFLA_CS`

Unity 或算法适配层根据以下信息自动计算围捕行为：

- Target 当前位姿；
- UAV 和 USV 当前位姿；
- UAV 和 USV 数量；
- 设备速度和航向；
- 场景有效区域；
- 当前任务状态。

算法应自动完成：

- 追踪、拦截和封锁角色分配；
- 围捕半径计算；
- 设备接近方向计算；
- 目标移动时的持续重规划；
- 设备碰撞距离约束。

### 7.2 `ESCORT_GUARD`

Unity 或算法适配层根据以下信息自动计算护航行为：

- 护航目标当前位姿；
- UAV 和 USV 当前位姿；
- 威胁目标或威胁方向；
- 设备速度和航向；
- 场景有效区域；
- 当前任务状态。

算法应自动完成：

- UAV 空中观察和预警；
- USV 水面伴随和侧翼保护；
- 威胁出现后的设备重新部署；
- 护航目标安全距离约束；
- 任务完成后的返回或收拢。

## 8. 任务状态接口

平台向 Unity 发送：

```text
missionStart
missionPause
missionResume
missionStop
missionReset
```

允许的状态转换：

```text
STOPPED -> RUNNING
RUNNING -> PAUSED
PAUSED  -> RUNNING
RUNNING -> STOPPED
PAUSED  -> STOPPED
STOPPED -> RESET
RESET   -> STOPPED
```

任务运行期间：

- 不允许修改 UAV 数量；
- 不允许修改 USV 数量；
- 不允许切换算法；
- 不允许重新生成场景。

`missionReset` 清除位姿历史并恢复最近一次生成的初始状态，
不改变设备数量和算法配置。

任务命令示例：

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

## 9. 批量位姿接口

### 9.1 `applyPoseBatch`

这是虚拟编队位姿更新的唯一批量输入接口。

平台队列中同一运行批次只保留最新的待处理位姿帧。

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
        "headingDeg": 45.0,
        "speedMps": 2.1,
        "state": "AIRBORNE",
        "valid": true
      },
      {
        "deviceCode": "USV-001",
        "deviceType": "USV",
        "eastM": 18.2,
        "northM": 4.1,
        "upM": 0.0,
        "headingDeg": 90.0,
        "speedMps": 4.0,
        "state": "SAILING",
        "valid": true
      }
    ],
    "targets": [
      {
        "deviceCode": "TARGET-001",
        "targetType": "CAPTURE_TARGET",
        "eastM": 0.0,
        "northM": 0.0,
        "upM": 0.0,
        "headingDeg": 0.0,
        "valid": true
      }
    ]
  }
}
```

Unity 必须执行以下校验：

1. `runtimeMode` 必须为 `VIRTUAL_SIMULATION`；
2. `runId` 必须等于当前加载的运行批次；
3. `sequence` 必须严格大于上一帧；
4. 旧批次、重复帧和乱序帧直接忽略；
5. 未知设备编号不应导致整帧失败；
6. 缺少的设备保持上一位姿，并在回执中列出；
7. 每个渲染帧最多应用一份最新位姿批次；
8. `sampleTime` 只用于诊断，不控制 Unity 渲染时钟。

## 10. 相机和设备控制接口

### 10.1 `selectDevice`

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

### 10.2 `setCameraMode`

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

允许的相机模式：

```text
overview
device-follow
```

### 10.3 `setTrajectoryVisible`

```json
{
  "type": "setTrajectoryVisible",
  "requestId": "setTrajectoryVisible:001",
  "timestamp": 1786700000000,
  "payload": {
    "visible": true
  }
}
```

## 11. Unity 回执

### 11.1 `scenarioReady`

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
    "uavCount": 10,
    "usvCount": 8,
    "targetCount": 1,
    "deviceCodes": [
      "UAV-001",
      "UAV-002",
      "USV-001",
      "USV-002",
      "TARGET-001"
    ],
    "missionState": "STOPPED"
  }
}
```

`scenarioReady.payload.uavCount` 和 `usvCount` 必须与实际生成数量一致。

### 11.2 `poseFrameApplied`

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
    "appliedCount": 18,
    "missingDeviceCodes": [],
    "unknownDeviceCodes": []
  }
}
```

该回执只表示 Unity 已经应用显示数据，不表示真实设备执行成功，
也不表示算法任务已经完成。

### 11.3 `missionStateChanged`

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

### 11.4 `cameraChanged`

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

### 11.5 错误回执

```json
{
  "type": "commandAck",
  "requestId": "regenerateScenario:001",
  "timestamp": 1786700000000,
  "payload": {
    "success": false,
    "code": "scenario_locked",
    "message": "任务运行中，不能重新生成场景",
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
scenario_locked
run_not_loaded
run_mismatch
sequence_rewind
mission_state_conflict
device_not_found
```

## 12. A/B 开发边界

### 成员 A：Unity 虚拟编队运行时

负责：

- Unity 虚拟编队场景；
- UAV、USV、Target 动态生成；
- 随机初始位置和设备编号；
- 对象池；
- 设备数量控制；
- 自动围捕和护航行为；
- 设备碰撞距离和场景边界；
- 100 UAV + 100 USV 性能验证；
- 提供稳定的运行时接口。

建议模块：

```text
VirtualVehicleFactory.cs
VirtualFleetManager.cs
VirtualFleetScenarioController.cs
```

### 成员 B：前端、Bridge 和接口联调

负责：

- 前端算法和数量配置；
- 两个算法的独立数量保存；
- 任务运行期间配置锁定；
- Unity WebGL `postMessage` 转发；
- `PlatformBridge` 消息适配；
- `requestId` 保持和回执校验；
- `scenarioReady`、`poseFrameApplied`、`missionStateChanged`、
  `cameraChanged` 接口测试；
- WebGL 构建包替换和前后端联调。

建议模块：

```text
VirtualFleetPanel.vue
virtualSimulation.ts
VirtualFleetPlatformBridge.cs
WebCommandBridge.cs
```

B 不直接修改 A 的对象池、设备生成和场景布局实现。

## 13. 版本和兼容规则

当前协议版本为 `2.0`。

V2.0 的不兼容变更：

- 删除 `formationType`；
- 删除固定队形枚举；
- 设备初始位置改为 Unity 随机生成；
- 围捕和护航部署改为算法自动计算；
- `scenarioReady` 必须返回实际 UAV/USV 数量；
- Bridge 必须保留原始 `requestId`。

允许的兼容变更：

- 增加可选字段；
- 增加新的回执 payload 字段；
- 增加新的 capabilities 项。

修改字段含义、坐标定义、消息名称或状态转换规则时，必须升级主版本。

旧的 `poseFrame` 可以作为迁移期兼容别名，但新虚拟编队代码统一使用
`applyPoseBatch`。

## 14. 联调验收清单

- [ ] Unity 上报 `VIRTUAL_SIMULATION`；
- [ ] Unity 不启动 ROS 和 Gazebo；
- [ ] `GB_SFLA_CS` 可以加载；
- [ ] `ESCORT_GUARD` 可以加载；
- [ ] 1 UAV + 1 USV 可以随机生成；
- [ ] 3 UAV + 3 USV 可以随机生成；
- [ ] 总计 100 台设备可以稳定运行；
- [ ] UAV 100 + USV 100 架构测试无致命错误；
- [ ] 设备编号唯一且使用三位编号；
- [ ] 前端不发送 `formationType`；
- [ ] 随机种子可以复现场景；
- [ ] 运行中或暂停时不能重新生成；
- [ ] 运行中不能修改 UAV/USV 数量；
- [ ] 旧 `runId` 的位姿帧被忽略；
- [ ] 重复和乱序 `sequence` 被忽略；
- [ ] Unity 每帧只处理最新位姿批次；
- [ ] 回报已应用、缺失和未知设备数量；
- [ ] 开始、暂停、恢复、停止、重置状态转换正确；
- [ ] UAV/USV 可以自动形成围捕部署；
- [ ] UAV/USV 可以自动形成护航部署；
- [ ] 设备相机跟随返回 `cameraChanged`；
- [ ] 所有回执 `requestId` 与原请求一致；
- [ ] 无 ROS 环境可以独立运行。

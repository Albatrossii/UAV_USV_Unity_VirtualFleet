# UAV-USV 虚拟编队协议 v1

状态：A/B 联调前协议草案
适用项目：`Albatrossii/UAV_USV_Unity_VirtualFleet`
运行模式：`VIRTUAL_SIMULATION`

## 1. 协议目标

本协议用于约定前端平台与 Unity 虚拟编队运行时之间的接口。

本阶段只做虚拟设备算法验证，不连接：

- ROS、ROS2
- Gazebo、PX4
- 真实无人机和无人艇
- 真实 GPS、雷达、视频或其他传感器

第一阶段验收目标为总计 100 台设备稳定运行，架构预留 UAV 100 台和
USV 100 台。

## 2. 消息传输

继续使用现有 Unity WebGL iframe `postMessage` 通道。

所有消息使用以下外层结构：

```json
{
  "type": "loadScenario",
  "requestId": "loadScenario:1720000000000:abc123",
  "timestamp": 1720000000000,
  "payload": {}
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `type` | string | 是 | 消息类型，使用 camelCase |
| `requestId` | string | 命令必填 | 请求唯一编号 |
| `timestamp` | integer | 是 | Unix 毫秒时间戳 |
| `payload` | object | 是 | 具体消息内容 |

接收方必须忽略未知字段。带有 `requestId` 的未知消息类型应返回
`unsupported_message`。

## 3. 设备编号和运行模式

标准设备编号：

```text
UAV-001 ~ UAV-100
USV-001 ~ USV-100
TARGET-001
```

内部设备字典和 Unity 输出消息必须使用三位编号。

输入端可以兼容旧编号，例如：

```text
uav_01
UAV-01
```

但进入 Unity 内部后必须标准化为：

```text
UAV-001
```

算法编号：

```text
GB_SFLA_CS
ESCORT_GUARD
```

本协议唯一允许的运行模式：

```text
VIRTUAL_SIMULATION
```

## 4. 坐标和朝向

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

## 5. 初始化接口

### 5.1 `initializePlatform`

Unity 加载完成后，平台发送初始化消息：

```json
{
  "type": "initializePlatform",
  "requestId": "init:001",
  "timestamp": 1720000000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "protocolVersion": "1.0",
    "buildId": "unity-virtual-fleet-v1"
  }
}
```

Unity 应返回 `platformBridgeReady`。

## 6. 场景配置接口

### 6.1 `loadScenario`

加载算法和虚拟编队配置，但不自动开始任务。

```json
{
  "type": "loadScenario",
  "requestId": "load:001",
  "timestamp": 1720000000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "algorithmCode": "GB_SFLA_CS",
    "runId": 42,
    "uavCount": 3,
    "usvCount": 3,
    "targetCount": 1,
    "formationType": "ENCIRCLEMENT",
    "initialSpeedMps": 5.0,
    "initialHeadingDeg": 90.0,
    "seed": 20260813
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
| `targetCount` | v1 固定为 1 |
| `formationType` | 见下方枚举 |
| `initialSpeedMps` | 大于等于 0 |
| `initialHeadingDeg` | 自动归一化到 [0, 360) |

编队类型：

```text
RANDOM       随机布局
CIRCLE       圆形编队
ENCIRCLEMENT 围捕编队
ESCORT       护航编队
```

### 6.2 `regenerateScenario`

清理或复用对象池中的旧设备，并按新配置重新生成场景。

```json
{
  "type": "regenerateScenario",
  "requestId": "regenerate:001",
  "timestamp": 1720000000000,
  "payload": {
    "runId": 42,
    "uavCount": 10,
    "usvCount": 20,
    "formationType": "CIRCLE",
    "seed": 20260813
  }
}
```

只有 `STOPPED` 或 `RESET` 状态允许重新生成。
`RUNNING` 或 `PAUSED` 状态必须返回 `scenario_locked`。

## 7. 任务状态接口

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

`missionReset` 只清除位姿历史并恢复最近一次生成的初始布局，
不改变设备数量。

## 8. 批量位姿接口

### 8.1 `applyPoseBatch`

这是虚拟编队移动的唯一输入接口。

平台队列中同一运行批次只保留最新的待处理位姿帧。

```json
{
  "type": "applyPoseBatch",
  "requestId": "pose:42:128",
  "timestamp": 1720000000000,
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 42,
    "sequence": 128,
    "sampleTime": 1720000000123,
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

1. `runId` 必须等于当前加载的运行批次；
2. `sequence` 必须严格大于上一帧；
3. 旧批次、重复帧和乱序帧直接忽略；
4. 未知设备编号不应导致整帧失败；
5. 缺少的设备保持上一位姿，并在回执中列出；
6. 每个渲染帧最多应用一份最新位姿批次；
7. `sampleTime` 只用于诊断，不控制 Unity 渲染时钟。

## 9. Unity 回执

### 9.1 `platformBridgeReady`

```json
{
  "type": "platformBridgeReady",
  "requestId": "",
  "timestamp": 1720000000000,
  "payload": {
    "ready": true,
    "runtimeMode": "VIRTUAL_SIMULATION",
    "protocolVersion": "1.0",
    "buildId": "unity-virtual-fleet-v1",
    "maxUavCount": 100,
    "maxUsvCount": 100,
    "capabilities": [
      "virtual-fleet",
      "dynamic-generation",
      "object-pool",
      "pose-batch",
      "mission-control"
    ]
  }
}
```

### 9.2 `scenarioReady`

```json
{
  "type": "scenarioReady",
  "requestId": "load:001",
  "timestamp": 1720000000000,
  "payload": {
    "success": true,
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 42,
    "algorithmCode": "GB_SFLA_CS",
    "uavCount": 3,
    "usvCount": 3,
    "targetCount": 1,
    "deviceCodes": [
      "UAV-001",
      "UAV-002",
      "UAV-003",
      "USV-001",
      "USV-002",
      "USV-003"
    ],
    "missionState": "STOPPED"
  }
}
```

### 9.3 `poseFrameApplied`

```json
{
  "type": "poseFrameApplied",
  "requestId": "pose:42:128",
  "timestamp": 1720000000000,
  "payload": {
    "success": true,
    "runId": 42,
    "sequence": 128,
    "appliedCount": 6,
    "missingDeviceCodes": [],
    "unknownDeviceCodes": []
  }
}
```

该回执只表示 Unity 已经应用显示数据，不表示真实设备执行成功，
也不表示算法任务已经完成。

### 9.4 `missionStateChanged`

```json
{
  "type": "missionStateChanged",
  "requestId": "missionStart:001",
  "timestamp": 1720000000000,
  "payload": {
    "success": true,
    "runId": 42,
    "missionState": "RUNNING"
  }
}
```

### 9.5 错误回执

```json
{
  "type": "commandAck",
  "requestId": "regenerate:001",
  "timestamp": 1720000000000,
  "payload": {
    "success": false,
    "code": "scenario_locked",
    "message": "任务运行中，不能重新生成场景",
    "runId": 42
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

## 10. A/B 开发边界

成员 A 负责 Unity 虚拟编队运行时，至少提供以下稳定接口：

```csharp
Configure(VirtualFleetConfig config);
Regenerate();
ApplyPoseBatch(VirtualPoseBatch batch);
StartMission();
PauseMission();
ResumeMission();
StopMission();
ResetMission();
```

成员 B 负责前端、Bridge、算法数据和任务控制。
B 只通过上述接口驱动 Unity，不直接修改对象池和设备内部实现。

成员 A 建议模块：

```text
VirtualVehicleFactory.cs
VirtualFleetManager.cs
FormationLayoutGenerator.cs
VirtualFleetScenarioController.cs
```

成员 B 建议模块：

```text
VirtualFleetPanel.vue
virtualSimulation.ts
virtualPoseGenerator.ts
```

## 11. 版本规则

当前协议版本为 `1.0`。

新增可选字段属于兼容变更；修改字段含义、坐标定义、消息名称或状态
转换规则，必须升级主版本。

旧的 `poseFrame` 可以作为迁移期兼容别名，但新虚拟编队代码统一使用
`applyPoseBatch`。

## 12. 联调验收清单

- [ ] Unity 上报 `VIRTUAL_SIMULATION`；
- [ ] Unity 不启动 ROS 和 Gazebo；
- [ ] 1 UAV + 1 USV 可以生成；
- [ ] 总计 100 台设备可以生成；
- [ ] 设备编号唯一且使用三位编号；
- [ ] 运行中或暂停时不能重新生成；
- [ ] 旧 runId 的位姿帧被忽略；
- [ ] 重复和乱序 sequence 被忽略；
- [ ] Unity 每帧只处理最新位姿批次；
- [ ] 回报已应用、缺失和未知设备数量；
- [ ] 开始、暂停、恢复、停止、重置状态转换正确；
- [ ] 无 ROS 环境可以独立运行。

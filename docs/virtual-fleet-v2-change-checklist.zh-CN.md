# 虚拟编队需求 V2.0 改动与联调清单

本文对应 `VirtualFleetRequirements-v2.zh-CN.md`，用于 A、B 和前端联调。

## 一、当前需求结论

- 初始位置由 Unity 运行时随机生成。
- UAV 在海域上方随机高度生成。
- USV 在海平面随机生成。
- 相同 `seed` 必须得到可复现的初始布局。
- 初始随机布局不代表围捕或护航队形。
- 围捕、护航部署由算法在任务运行期间计算。
- 前端不再发送或选择 `formationType`。

## 二、A 侧 Unity 已改动

### 1. 随机设备生成

涉及文件：

```text
Assets/Scripts/UavUsv/VirtualVehicleFactory.cs
Assets/Scripts/UavUsv/VirtualFleetManager.cs
Assets/Scripts/UavUsv/VirtualFleetScenarioController.cs
```

当前行为：

- UAV 在海域范围内随机生成，高度约为 `7~12` Unity 单位。
- USV 在海平面生成，根节点高度约为 `0.03`。
- UAV 和 USV 使用随机朝向。
- 同类设备按最小水平间距采样，减少重叠。
- `seed=0` 时使用默认种子 `20260814`。
- 相同种子可复现相同设备布局。
- `Regenerate()` 会把场景配置中的 `seed` 传给设备生成器。

### 2. 相机目标刷新

涉及文件：

```text
Assets/Scripts/UavUsv/SimulationBootstrap.cs
```

当前行为：

- `VirtualFleetManager.FleetChanged` 触发后刷新相机目标。
- 重新生成后更新默认跟随的 UAV、USV。
- 全局视角重新包含当前所有 UAV、USV。
- SensorViewPip 的设备数组同步更新。

验证方式：

```text
Unity Play
Virtual Fleet Test -> Generate 100 + 100
按键 1 或前端选择全局视角
```

预期：相机自动拉远，尽量完整显示当前设备范围。

### 3. 本地临时测试工具

文件：

```text
Assets/Editor/VirtualFleetManualTester.cs
```

该文件只用于 Unity Editor 验证，不应进入 WebGL 正式构建。验证完成后可以删除，也不要把它与 `ProjectSettings` 临时变化一起提交。

## 三、B 侧必须改动

### 1. 移除 formationType 的协议依赖

当前 Unity Bridge 仍在以下位置强制校验 `formationType`：

```text
Assets/Scripts/UavUsv/PlatformTools/VirtualFleetMessageValidator.cs
```

B 需要修改：

- `ValidateLoadScenario()` 不再要求 `formationType`。
- `ValidateRegenerateScenario()` 不再要求 `formationType`。
- `VirtualFleetConfigPayload` 删除字段，或在过渡期保留但标记为 deprecated。
- `RegenerateScenarioPayload` 删除字段，或在过渡期忽略。
- `VirtualFleetPlatformBridge` 不再根据前端 `formationType` 决定初始布局。

算法行为应由 `algorithmCode` 决定：

```text
GB_SFLA_CS   -> 围捕算法内部策略
ESCORT_GUARD -> 护航算法内部策略
```

初始随机位置不应被解释成算法队形。

### 2. 前端必须改动

前端应：

- 删除编队类型下拉框。
- 删除 `formationType` 的发送。
- 保留算法选择。
- 保留 UAV/USV 数量、速度、朝向和 seed。
- 两个算法分别保存自己的数量配置。
- 任务运行期间锁定数量、算法和重新生成按钮。

`loadScenario` 的 V2 payload 应类似：

```json
{
  "runtimeMode": "VIRTUAL_SIMULATION",
  "runId": 7001,
  "algorithmCode": "GB_SFLA_CS",
  "uavCount": 10,
  "usvCount": 8,
  "targetCount": 1,
  "initialSpeedMps": 2.0,
  "initialHeadingDeg": 0,
  "seed": 20260814
}
```

## 四、接口文档必须调整

需要同步更新：

```text
docs/virtual-fleet-runtime-api.zh-CN.md
```

接口文档应删除：

- `formationType` 必填规则。
- `VirtualFleetFormations` 作为前端配置项的说明。
- 前端主动指定圆形、围捕、护航布局的示例。

接口文档应补充：

- `seed` 的含义和可复现规则。
- 初始位置是随机部署，不是任务队形。
- `algorithmCode` 决定任务算法。
- `scenarioReady` 返回数量、runId 和设备编号。
- 任务开始后不得重新生成。

## 五、目前不需要改动的部分

- `ApplyPoseBatch` 的 `runId`、`sequence` 规则不需要因为随机生成而改变。
- `SelectDevice` 和 `SetCameraMode` 的请求格式不需要改变。
- `VIRTUAL_SIMULATION` 和 ROS 隔离规则不需要改变。
- `VirtualFleetScenarioController` 仍然是 B 调用 Unity 的稳定边界。

## 六、当前仍需单独跟踪的问题

### 1. Target-001

当前 `VirtualFleetManager` 主要管理 UAV 和 USV，`TARGET-001` 仍需要独立的 Target 运行时对象和位姿更新逻辑。

不能只在 `scenarioReady.deviceCodes` 中声明 `TARGET-001`，却不在场景中生成对应对象。

### 2. 初始位置范围配置

目前随机范围仍是 Unity 侧默认常量。若前端要设置位置范围，需要后续增加配置字段，例如：

```text
spawnCenter
spawnSize
uavMinAltitude
uavMaxAltitude
```

### 3. 对象池

当前重新生成仍然使用 `Destroy` 后重新创建，尚未完成真正对象池。100+100 稳定后再优化对象池。

## 七、联调顺序

1. Unity Editor 验证 `3+3` 随机生成。
2. 使用相同 seed 重新生成，确认布局一致。
3. 修改 seed，确认布局变化。
4. 验证 USV 在海平面、UAV 在空中。
5. 验证 `100+100` 无大面积重叠。
6. 切换全局视角，确认相机自动取景。
7. B 修改 Bridge 校验器，移除 `formationType` 强制要求。
8. 前端删除 `formationType` 并发送 V2 payload。
9. 联调 `loadScenario -> scenarioReady`。
10. 联调 `applyPoseBatch -> poseFrameApplied`。
11. 联调相机和任务状态接口。
12. 最后重新构建 WebGL，进行 100+100 压力测试。

## 八、提交边界

A 提交：

```text
VirtualFleetFactory.cs
VirtualFleetManager.cs
VirtualFleetScenarioController.cs
SimulationBootstrap.cs
```

B/前端提交：

```text
VirtualFleetMessageValidator.cs
VirtualFleetProtocolModels.cs
VirtualFleetPlatformBridge.cs
前端 VirtualSimulationStore
前端 VirtualFleetPanel
```

临时测试文件和 Unity 自动生成的无关 ProjectSettings 修改不要提交。

# 虚拟编队 Runtime 接口联调说明

## B 侧唯一依赖

Bridge 和算法适配层只依赖：

```csharp
UavUsv.IVirtualFleetRuntime
```

不要直接调用 `VirtualFleetManager` 的设备列表，也不要操作 UAV/USV 的 `Transform`。

当前场景中的 `VirtualFleetScenarioController` 已实现该接口：

```csharp
IVirtualFleetRuntime runtime =
    FindObjectOfType<VirtualFleetScenarioController>();
```

## 场景配置

```csharp
runtime.Configure(new VirtualFleetConfig
{
    runtimeMode = "VIRTUAL_SIMULATION",
    algorithmCode = "GB_SFLA_CS",
    runId = 42,
    uavCount = 3,
    usvCount = 3,
    targetCount = 1,
    formationType = VirtualFleetFormationType.Encirclement,
    initialSpeedMps = 5f,
    initialHeadingDeg = 90f,
    seed = 20260813
});
runtime.Regenerate();
```

`Configure` 只保存并校验配置，`Regenerate` 才清理旧设备并按数量重新生成。运行中或暂停时调用 `Regenerate` 会抛出 `scenario_locked`。

## 批量位姿

```csharp
VirtualPoseBatchApplyResult result =
    runtime.ApplyPoseBatch(new VirtualPoseBatch
    {
        runtimeMode = "VIRTUAL_SIMULATION",
        runId = 42,
        sequence = 128,
        sampleTime = 1720000000123,
        vehicles = poses
    });
```

Unity 侧会校验：

- `runtimeMode` 必须为 `VIRTUAL_SIMULATION`
- `runId` 必须等于当前配置
- `sequence` 必须严格大于上一帧
- 未知设备进入 `unknownDeviceCodes`
- 当前批次没有上报的设备进入 `missingDeviceCodes`
- 合法设备位姿按 ENU 米制坐标转换到 Unity 展示坐标
- `headingDeg` 自动归一化到 `[0, 360)`

批次部分应用成功时仍返回 `success = true`，`appliedCount`、`missingDeviceCodes` 和 `unknownDeviceCodes` 用于诊断。

## 注意

当前 `VirtualPose` 只应用 `vehicles`，`targets` 预留给 Target 管理模块；不会因为暂未生成 Target 而影响 UAV/USV 位姿帧。

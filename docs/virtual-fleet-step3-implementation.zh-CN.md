# 虚拟编队第三步实现说明

## 本阶段范围

本阶段在 Unity 独立仓库中实现虚拟 UAV/USV 的运行时管理：

- 初始生成 `3` 架 UAV 和 `3` 艘 USV。
- 运行前支持逐台添加 UAV 或 USV。
- UAV、USV 分别最多 `100` 台。
- 使用统一编号：`UAV-001`、`USV-001`。
- 任务运行或暂停后禁止添加、删除和重新初始化设备。
- 虚拟设备状态使用 `VIRTUAL_SIMULATION`，不连接 ROS。

## 核心接口

`VirtualFleetScenarioController` 是前端桥接和算法适配层的调用门面。

```csharp
bool AddUav();
bool AddUsv();
bool RemoveUav(string deviceCode);
bool RemoveUsv(string deviceCode);

void StartMission();
void PauseMission();
void ResumeMission();
void StopMission();
void ResetMission();

VirtualFleetSnapshot GetSnapshot();
```

底层 `VirtualFleetManager` 负责设备列表、编号、数量上限和任务状态锁。B 成员不应直接操作 UAV/USV 的 `Transform` 数组。

## 状态规则

| 任务状态 | 添加/删除设备 | 重新初始化 |
| --- | --- | --- |
| `STOPPED` | 允许 | 允许 |
| `RESET` | 允许 | 允许 |
| `RUNNING` | 禁止 | 禁止 |
| `PAUSED` | 禁止 | 禁止 |

## 文件

```text
Assets/Scenes/UavUsvVirtualFleet.unity
Assets/Scripts/UavUsv/VirtualFleetTypes.cs
Assets/Scripts/UavUsv/VirtualVehicleFactory.cs
Assets/Scripts/UavUsv/VirtualFleetManager.cs
Assets/Scripts/UavUsv/VirtualFleetScenarioController.cs
```

## 当前限制与后续联调

1. 新增设备先使用预设扩展位置，圆形、围捕和护航布局可以在后续场景配置接口中覆盖。
2. 当前场景的相机和碰撞系统在启动时读取初始设备数组；B 成员接入批量位姿和任务状态时，应通过 `VirtualFleetScenarioController` 获取最新快照，并同步更新相关观察目标。
3. 对象池尚未接入。当前先完成正确的动态生成和删除，达到 50~100 台压力测试前再将 `Destroy` 替换为池回收。
4. Unity Editor 不在本次工作环境的 PATH 中，尚未执行 Unity batchmode 编译；打开项目后应先检查 Console，再进行 WebGL 构建。

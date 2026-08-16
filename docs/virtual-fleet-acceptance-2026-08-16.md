# 虚拟编队验收记录

日期：2026-08-16

## 测试环境

- Unity 场景：`UavUsvVirtualFleet.unity`
- 运行模式：`VIRTUAL_SIMULATION`
- 坐标系：`GLOBAL_ENU`
- ROS：未连接
- Bridge：单实例 `VirtualFleetPlatformBridge` + `WebCommandBridge`

## 3 UAV + 3 USV + 1 Target

场景配置：

- UAV：3
- USV：3
- Target：1
- 总设备数：7

`ApplyPoseBatch` 验证结果：

```text
sent=7
applied=7
unknown=[]
missing=[]
success=true
```

验证结论：

- `scenarioReady` 返回数量正确。
- `poseFrameApplied.success=true`。
- UAV、USV、Target 均可接收并应用位姿。
- UAV、USV、Target 均可实际移动。
- 已创建 6 条设备轨迹。
- 全局相机可正常取景，设备跟随可正常切换。
- 未出现 `run_mismatch`、`sequence_rewind`、`mission_state_conflict`。

## 100 UAV + 100 USV + 1 Target

场景配置：

- UAV：100
- USV：100
- Target：1
- 总设备数：201

`ApplyPoseBatch` 验证结果：

```text
sent=201
applied=201
unknown=[]
missing=[]
success=true
```

验证结论：

- `scenarioReady` 返回 `uavCount=100`、`usvCount=100`、`targetCount=1`。
- `poseFrameApplied.success=true`。
- 201 个设备均可接收并应用位姿。
- UAV、USV、Target 均可实际移动。
- `VirtualFleetTrails refreshed: 200 devices`。
- 全局相机可根据设备数量和分布自适应缩放。
- `missionReset` 后可以重新生成场景。
- 未连接 ROS，未发送真实设备控制指令。

## 本次提交范围

- Bridge 消息校验、任务状态和位姿所有权处理。
- WebGL 命令转发和回执字段补齐。
- 全局相机自适应取景与虚拟轨迹刷新。
- 3+3 与 100+100 验收记录。


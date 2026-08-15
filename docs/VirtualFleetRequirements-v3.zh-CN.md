# UAV-USV 虚拟编队围捕与护航算法验证平台需求文档 v3

## 1. 项目定位

本项目在现有前端和 Unity WebGL 平台基础上，构建 UAV/USV 虚拟仿真环境，用于验证：

- GB-SFLA-CS 协同围捕算法；
- 混合 UAV/USV 护航守卫算法。

本阶段只运行虚拟设备，不连接真实 ROS，不控制真实无人机或无人船。

运行模式固定为：

```text
VIRTUAL_SIMULATION
```

## 2. 本版本核心调整

1. UAV 和 USV 的运动速度参考实际设备参数。
2. 初始速度不再由前端手动输入，也不采用无约束随机速度。
3. UAV/USV 的运动方向、目标航点和轨迹由所选算法计算。
4. Unity 负责按照算法输出执行运动、显示模型和回传状态。
5. 算法输出速度不得超过对应设备的最大速度约束。

## 3. 设备速度参数

### 3.1 UAV 参数

参考设备型号：M3-F900。

| 参数 | 值 |
|---|---:|
| 最大飞行速度 | >= 15 m/s |
| 仿真速度上限 | 15 m/s |
| 运行空间 | 三维空间 |
| 高度范围 | 由 Unity 场景和任务配置限制 |

Unity 中的 UAV 速度应满足：

```text
0 <= |velocity| <= 15 m/s
```

### 3.2 USV 参数

参考设备型号：USV-M1500。

| 参数 | 值 |
|---|---:|
| 航速 | 2 m/s |
| 仿真速度上限 | 2 m/s |
| 运行空间 | 海面二维平面 |
| 高度 | 固定为海面高度 |

Unity 中的 USV 速度应满足：

```text
0 <= |velocity| <= 2 m/s
```

### 3.3 速度解释

算法脚本中的速度参数可能是归一化仿真单位，例如：

- GB-SFLA-CS 脚本中的 `V_MAX_UAV`、`V_MAX_USV`；
- 护航脚本中的 `max_speed`、`gain`、`cruise_speed`。

这些参数不能直接当作真实米每秒传给 Unity。算法适配层必须将算法输出映射到设备物理速度范围：

```text
UAV: 0 ~ 15 m/s
USV: 0 ~ 2 m/s
```

## 4. 设备数量和编号

前端支持配置：

- UAV：1~100 台；
- USV：1~100 台；
- Target：默认 1 个，围捕算法可扩展多个目标。

设备编号统一为：

```text
UAV-001 ~ UAV-100
USV-001 ~ USV-100
TARGET-001 ~ TARGET-020
```

Unity 运行时动态生成设备，不手动复制 100 个模型。

## 5. 初始状态

### 5.1 初始位置

设备初始位置可以由场景生成器随机生成，但必须满足：

- UAV 位于允许的空域范围；
- USV 位于海面；
- 设备之间不发生初始重叠；
- 设备与目标保持最小安全距离；
- 使用 `seed` 时能够复现实验结果。

### 5.2 初始速度

初始速度由算法运行状态确定，不由前端手动填写。

推荐初始化为：

```text
任务开始前：0 m/s
任务开始后：由算法输出速度
```

若算法需要巡航初始化速度，则由算法适配层按设备类型生成，并限制在对应速度上限内。

### 5.3 初始朝向

初始朝向可由初始位置、目标位置或算法初始航点计算。前端不再发送 `initialHeadingDeg`。

任务开始后，朝向应根据当前速度方向或算法给出的 heading 更新。

## 6. GB-SFLA-CS 协同围捕算法

### 6.1 算法职责

GB-SFLA-CS 负责：

- UAV/USV 与目标之间的任务分配；
- 粒球划分、分裂、合并和重配置；
- 计算围捕航点；
- 计算目标负载均衡；
- 计算捕获半径和捕获状态；
- 生成每个设备下一时刻的期望位置或速度方向。

### 6.2 Unity 执行职责

Unity 不重新实现 GB-SFLA-CS 优化过程，只执行算法输出：

- 接收批量位姿；
- 对速度进行设备类型限幅；
- 根据位置变化更新朝向；
- 更新设备模型；
- 显示围捕范围、目标和轨迹；
- 回传任务状态和设备状态。

### 6.3 围捕约束

围捕任务至少应验证：

- 目标是否进入围捕区域；
- 围捕半径是否稳定；
- UAV/USV 是否超过速度上限；
- 设备之间是否发生碰撞；
- 是否存在设备掉队；
- 目标是否成功捕获；
- 任务分配是否发生异常频繁重配置。

默认捕获半径由算法配置传递，Unity 不擅自修改。

## 7. 混合 UAV/USV 护航守卫算法

### 7.1 算法职责

护航算法负责：

- 维护被护航目标的运动；
- 根据威胁目标位置计算威胁方向；
- 计算核心阻断点；
- 选取核心守卫和翼侧守卫；
- 生成护航弧或护航区域；
- 计算避让方向；
- 在威胁出现时重新部署守卫设备；
- 保持设备角色和目标分配稳定。

### 7.2 典型算法输出

每个仿真步应输出：

- 被护航目标位置；
- 威胁目标位置；
- 核心阻断设备编号；
- 翼侧守卫设备编号；
- 各设备目标位置；
- 各设备期望速度；
- 各设备 heading；
- 当前护航阶段；
- 威胁方向；
- 避让方向；
- 阻断误差；
- 是否满足严格阻断约束。

### 7.3 护航速度约束

护航算法输出速度经过统一限幅：

```text
UAV <= 15 m/s
USV <= 2 m/s
```

算法中的比例控制增益只用于计算运动趋势，不得绕过设备速度上限。

### 7.4 护航验证指标

- 被护航目标是否保持安全距离；
- 核心守卫是否位于己方目标与威胁目标之间；
- 阻断点误差是否小于配置阈值；
- UAV/USV 是否保持在护航区域；
- 威胁改变方向后是否重新部署；
- 是否发生设备碰撞；
- 是否超过 UAV/USV 速度上限；
- 任务完成后设备是否返回指定区域。

## 8. 统一算法输出模型

算法适配层应将两个 Python 算法的输出转换为统一模型：

```json
{
  "runtimeMode": "VIRTUAL_SIMULATION",
  "runId": 7001,
  "algorithmCode": "GB_SFLA_CS",
  "sequence": 12,
  "timestamp": 0,
  "poses": [
    {
      "deviceCode": "UAV-001",
      "type": "UAV",
      "position": [10.0, 20.0, 30.0],
      "velocity": [3.0, 0.0, 1.0],
      "speedMps": 3.16,
      "headingDeg": 18.4,
      "role": "capture",
      "targetCode": "TARGET-001"
    },
    {
      "deviceCode": "USV-001",
      "type": "USV",
      "position": [12.0, 18.0, 0.0],
      "velocity": [1.2, 0.0, 0.0],
      "speedMps": 1.2,
      "headingDeg": 0.0,
      "role": "escort",
      "targetCode": "TARGET-001"
    }
  ]
}
```

字段要求：

- `velocity` 表示算法期望速度方向和大小；
- `speedMps` 表示经过物理速度限幅后的速度；
- `headingDeg` 由算法方向或速度方向计算；
- `role` 表示当前任务角色；
- `targetCode` 表示当前关联目标。

## 9. Bridge 接口要求

继续使用现有接口：

```text
InitializePlatform
LoadScenario
ApplyPoseBatch
SetMissionState
SelectDevice
SetCameraMode
SetTrajectoryVisible
```

### 9.1 LoadScenario

用于加载设备数量、算法和随机种子：

```json
{
  "type": "loadScenario",
  "requestId": "loadScenario-001",
  "payload": {
    "runtimeMode": "VIRTUAL_SIMULATION",
    "runId": 7001,
    "algorithmCode": "GB_SFLA_CS",
    "uavCount": 3,
    "usvCount": 3,
    "targetCount": 1,
    "seed": 42
  }
}
```

本接口不再要求：

```text
initialSpeedMps
initialHeadingDeg
formationType
```

### 9.2 ApplyPoseBatch

用于传递算法计算后的批量状态。Unity 必须：

1. 校验设备编号；
2. 校验 `sequence`；
3. 校验设备类型；
4. 对 UAV/USV 速度执行限幅；
5. 根据最终速度方向更新朝向；
6. 忽略过期批次；
7. 返回处理结果。

### 9.3 SetMissionState

任务状态包括：

```text
STOPPED
READY
RUNNING
PAUSED
CAPTURING
ESCORTING
THREAT_DETECTED
COMPLETED
RESETTING
ERROR
```

## 10. 前端要求

前端保留：

- 算法选择；
- UAV 数量；
- USV 数量；
- 目标数量；
- 随机种子；
- 重新生成场景；
- 开始、暂停、恢复、停止、重置；
- 虚拟仿真状态；
- 设备卡片和任务指标；
- 设备视角切换。

前端删除：

- 初始速度输入；
- 初始朝向输入；
- 由用户直接指定设备航向；
- 由用户直接指定单个设备速度。

前端展示的速度应来自 Unity 或算法回执，不应由前端自行伪造。

## 11. 数据流程

```text
前端配置算法和设备数量
        ↓
VirtualSimulationStore
        ↓
算法适配层
        ↓
GB-SFLA-CS 或护航守卫算法
        ↓
统一 ApplyPoseBatch
        ↓
Unity PlatformBridge
        ↓
速度限幅、位置更新、朝向更新
        ↓
Unity WebGL 画面和状态回执
```

## 12. 模式隔离

本阶段必须满足：

- 不连接 ROS；
- 不调用 ROS Action；
- 不调用 ROS Service；
- 不读取真实传感器；
- 不发送真实控制指令；
- 不将算法结果发送给真实设备；
- 所有设备状态标记为 `VIRTUAL`。

## 13. 性能要求

- 支持 100 UAV + 100 USV；
- 位姿更新频率建议 10~20 Hz；
- Unity 每帧只处理最新有效批次；
- 使用对象池；
- 关闭非必要轨迹和特效；
- 设备卡片支持滚动或分页；
- 100+100 设备运行时不得出现严重卡顿；
- 批量更新不得因为单个设备异常而中断整批处理。

## 14. 分工

### A：Unity 场景和设备运行时

- 虚拟场景；
- UAV/USV/Target 生成；
- 设备编号；
- 对象池；
- 设备位置更新；
- 设备朝向显示；
- 速度限幅；
- 轨迹和相机；
- 100+100 性能优化。

### B：Bridge 和算法适配

- 前端协议对齐；
- Python 算法输入输出适配；
- GB-SFLA-CS 输出转换；
- 护航守卫算法输出转换；
- `ApplyPoseBatch` 数据发送；
- 任务状态转换；
- 接口校验和测试；
- WebGL 回执链路；
- 不修改 A 的场景生成和对象池核心代码。

## 15. 验收标准

1. UAV 最大仿真速度不超过 15 m/s。
2. USV 最大仿真速度不超过 2 m/s。
3. 前端不再配置初始速度和初始朝向。
4. GB-SFLA-CS 能输出围捕航点和设备位姿。
5. 护航守卫算法能输出护航目标、阻断点和守卫位姿。
6. Unity 能执行算法输出的运动轨迹和方向。
7. `ApplyPoseBatch` 能返回正确的 `requestId` 和 `sequence`。
8. 相同 `seed` 能复现实验初始布局。
9. 设备数量可配置为 1~100。
10. 100 UAV + 100 USV 可以稳定运行。
11. 全流程保持 `VIRTUAL_SIMULATION`，不连接真实 ROS。

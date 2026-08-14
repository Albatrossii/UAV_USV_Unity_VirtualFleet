# UAV-USV 虚拟编队围捕与护航算法验证平台需求文档

版本：V2.0
日期：2026 年 8 月 14 日
运行模式：`VIRTUAL_SIMULATION`

## 一、项目概述

### 1. 项目名称

UAV-USV 虚拟编队围捕与护航算法验证平台

### 2. 项目基础

前端项目：

```text
C:\Users\86188\Desktop\mxy\UAV_USV_Platform
```

Unity 项目：

```text
C:\Users\86188\Desktop\mxy\UAV_USV_Unity
```

当前实际虚拟编队 Unity 项目以团队 GitHub 仓库版本为准。

### 3. 项目定位

在现有前端和 Unity WebGL 展示基础上，增加大规模虚拟 UAV、USV 动态生成和算法验证能力，用于验证：

- GB-SFLA-CS 协同围捕算法
- 混合 UAV/USV 护航守卫算法

本阶段仅使用虚拟设备，不连接真实 ROS，不控制真实无人机和无人船。

## 二、建设目标

1. 新建一个与现有场景风格一致的 Unity 虚拟编队场景。
2. 支持前端配置 UAV 和 USV 数量。
3. 支持 UAV 和 USV 数量范围为 1~100 台。
4. 设备在 Unity 中运行时随机生成，不手动复制固定数量模型。
5. 围捕和护航队形由算法根据目标、设备位置和任务状态自动计算。
6. 通过 Unity WebGL 显示虚拟设备和运动结果。
7. 验证围捕算法和护航算法。
8. 保持现有页面布局和操作方式。
9. 保留已有场景重新生成、初始位置、速度和朝向配置能力。
10. 保留现有相机、轨迹和 PlatformBridge 能力。

## 三、算法与前端功能设计

### 1. 算法选择

在当前页面算法选择区域中保留并支持以下两个算法：

```text
GB-SFLA-CS 协同围捕（模拟）
混合 UAV/USV 护航守卫（模拟）
```

算法选择只决定任务行为和自动编队策略，不提供人工队形选择。

### 2. UAV 数量

- 范围：1~100
- 默认值：3
- 用于设置虚拟无人机数量
- 任务运行期间禁止修改
- 修改数量后必须重新生成场景

### 3. USV 数量

- 范围：1~100
- 默认值：3
- 用于设置虚拟无人船数量
- 任务运行期间禁止修改
- 修改数量后必须重新生成场景

### 4. 算法独立配置

两个算法分别保存自己的 UAV 和 USV 数量。

GB-SFLA-CS 协同围捕：

```text
UAV：3
USV：3
```

混合 UAV/USV 护航守卫：

```text
UAV：3
USV：3
```

切换算法后，前端自动加载该算法最近一次保存的数量配置。

### 5. 虚拟仿真状态显示

页面需要显示：

- 当前运行模式：`VIRTUAL_SIMULATION`
- 当前算法
- UAV 数量
- USV 数量
- Target 数量
- Unity WebGL 连接状态
- PlatformBridge 就绪状态
- 当前任务状态：`STOPPED`、`RUNNING`、`PAUSED`
- 当前场景运行编号 `runId`
- 最新位姿序号 `sequence`

## 四、场景设置功能

以下功能继续保留：

- 重新生成场景
- 设置设备初始位置范围
- 设置初始速度
- 设置初始朝向

删除以下功能：

- 编队类型下拉框
- 随机布局选项
- 圆形编队选项
- 围捕编队选项
- 护航编队选项
- 前端主动指定 `formationType`

设备初始位置可以随机生成，但初始随机布局不代表任务队形。任务启动后，围捕或护航算法负责根据目标状态动态调整 UAV 和 USV 的位置。

## 五、自动编队和虚拟场景设计

### 1. 新 Unity 场景

建议新增：

```text
Assets/Scenes/UavUsvVirtualFleet.unity
```

保留现有：

```text
Assets/Scenes/UavUsvDemo.unity
```

新场景复用现有：

- 海面环境
- 灯光和天空盒
- 相机系统
- UAV 模型
- USV 模型
- Target 模型
- 轨迹显示
- Unity WebGL Bridge

### 2. 设备随机生成

设备由运行时动态生成：

```text
UAV-001 ~ UAV-100
USV-001 ~ USV-100
TARGET-001
```

设备初始位置应满足：

- 不重叠
- 不超出场景有效区域
- UAV 和 USV 可以分布在不同高度或水面区域
- Target 位于可观测范围内
- 生成结果支持固定随机种子复现

### 3. 自动围捕行为

选择 `GB_SFLA_CS` 后：

- UAV 和 USV 从随机初始位置出发
- 根据 Target 位置计算接近方向
- 自动分配追踪、拦截和封锁角色
- 根据设备数量动态计算包围半径
- 自动形成围捕区域
- 目标移动时持续更新设备位置
- 不允许前端指定固定三角形、圆形或其他队形

### 4. 自动护航行为

选择 `ESCORT_GUARD` 后：

- UAV 和 USV 从随机初始位置出发
- 自动识别护航目标和威胁方向
- 根据护航目标移动路线动态调整设备位置
- UAV 负责空中观察、预警和快速响应
- USV 负责水面伴随、侧翼保护和阻断
- 威胁出现后自动重新分配防护位置
- 不允许前端指定固定护航队形

## 六、运行流程

```text
用户选择算法
    ↓
选择 UAV 数量
    ↓
选择 USV 数量
    ↓
设置初始位置范围、速度和朝向
    ↓
点击“重新生成场景”
    ↓
Unity 清理旧设备
    ↓
Unity 随机创建新的 UAV、USV 和 Target
    ↓
用户启动任务
    ↓
算法根据目标状态自动计算围捕或护航行为
    ↓
Unity WebGL 显示设备运动结果
```

任务运行过程中：

- 禁止修改 UAV 数量
- 禁止修改 USV 数量
- 禁止直接重新生成场景
- 禁止切换算法
- 必须先停止、暂停或重置任务后，才能重新配置

## 七、系统数据流程

```text
前端虚拟编队配置
        ↓
VirtualSimulationStore
        ↓
生成虚拟场景配置
        ↓
Unity WebGL Bridge
        ↓
Unity PlatformBridge
        ↓
VirtualFleetManager
        ↓
随机生成 UAV、USV、Target
        ↓
围捕/护航算法自动计算设备行为
        ↓
更新 Unity 模型和轨迹
```

### 1. `loadScenario` 配置示例

```json
{
  "type": "loadScenario",
  "requestId": "loadScenario:20260814:001",
  "timestamp": 1786700000000,
  "payload": {
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
}
```

注意：协议中不再包含 `formationType`。

### 2. 使用现有接口

```text
InitializePlatform
LoadScenario
ApplyPoseBatch
SetMissionState
SelectDevice
SetCameraMode
SetTrajectoryVisible
```

## 八、算法验证内容

### 1. 围捕算法

验证：

- UAV 和 USV 是否从随机位置有效接近目标
- 是否自动形成稳定围捕区域
- 目标是否进入包围范围
- 围捕半径是否随设备数量合理变化
- 多设备是否发生碰撞
- 是否存在设备掉队
- 目标是否成功捕获
- 目标移动时围捕是否持续有效

### 2. 护航算法

验证：

- UAV 和 USV 是否从随机位置自动形成护航部署
- 护航目标是否沿设定路线移动
- UAV 和 USV 是否保持合理安全距离
- 威胁目标出现后是否及时响应
- 护航目标是否处于安全范围
- 设备是否发生碰撞或阻塞
- 任务完成后设备是否返回指定位置

## 九、ROS 隔离要求

虚拟仿真模式下：

- 不连接 ROS
- 不调用 ROS Action
- 不调用 ROS Service
- 不读取真实设备状态
- 不发送真实控制指令
- 不使用真实 GPS、雷达和视频数据
- 所有设备状态标记为 `VIRTUAL`

运行模式定义：

```text
VIRTUAL_SIMULATION
REAL_ROS
```

当前阶段只实现：

```text
VIRTUAL_SIMULATION
```

## 十、建议模块

### 1. 前端

```text
frontend/src/components/virtual/VirtualFleetPanel.vue
frontend/src/stores/virtualSimulation.ts
```

前端模块职责：

- 选择算法
- 配置 UAV 数量
- 配置 USV 数量
- 保存两个算法的独立数量配置
- 设置随机种子、初始速度和初始朝向
- 触发场景重新生成
- 显示虚拟仿真状态
- 禁止任务运行期间修改场景配置

前端不负责计算实际围捕或护航队形。

### 2. Unity

```text
Assets/Scripts/UavUsv/VirtualFleetManager.cs
Assets/Scripts/UavUsv/VirtualVehicleFactory.cs
Assets/Scripts/UavUsv/VirtualFleetScenarioController.cs
Assets/Scripts/UavUsv/PlatformTools/VirtualFleetPlatformBridge.cs
Assets/Scripts/UavUsv/PlatformTools/WebCommandBridge.cs
```

Unity 模块职责：

- 清理和重新生成虚拟设备
- 随机分配初始位置
- 管理 UAV、USV 和 Target
- 根据算法输入自动计算设备行为
- 执行围捕或护航任务
- 批量更新设备位姿
- 返回场景和任务状态

## 十一、性能要求

- UAV 数量支持 1~100
- USV 数量支持 1~100
- 第一阶段至少支持总数 100 台稳定运行
- 架构预留 UAV 100 台 + USV 100 台
- 使用模型对象池
- 使用批量位姿数据
- 位姿更新频率建议为 10~20 Hz
- Unity 每帧只处理最新位姿
- 支持关闭轨迹和特效
- 设备数量较多时支持设备卡片滚动或分页
- 不得出现黑屏
- 不得出现模型错位
- 不得出现严重卡顿
- 随机生成不能导致设备大面积重叠

## 十二、开发阶段

### 第一阶段：新场景搭建

- 复制现有 Unity 场景环境
- 创建虚拟编队场景
- 配置 UAV、USV 和 Target 模型
- 配置相机和轨迹系统
- 配置 Unity WebGL Bridge

### 第二阶段：动态设备生成

- 实现 UAV/USV/Target 动态生成
- 实现设备编号
- 实现随机初始位置
- 实现数量控制
- 实现重新生成场景
- 实现对象池

### 第三阶段：前端功能

- 增加算法选择
- 增加 UAV 数量控件
- 增加 USV 数量控件
- 保存两个算法独立的数量配置
- 保留初始位置、速度和朝向设置
- 删除编队类型选择
- 增加虚拟仿真状态显示
- 增加任务配置锁定

### 第四阶段：算法验证

- 接入 GB-SFLA-CS 协同围捕算法
- 接入混合 UAV/USV 护航守卫算法
- 由算法自动形成围捕或护航部署
- 显示位姿和轨迹
- 增加任务开始、暂停、恢复和重置
- 验证随机初始位置下的任务稳定性

### 第五阶段：压力测试

- 10 台设备
- 50 台设备
- 100 台设备
- UAV 100 + USV 100 架构测试
- 长时间运行
- 无 ROS 环境运行
- 多次随机种子重复测试

## 十三、验收标准

项目完成后应满足：

- 页面布局基本保持不变
- 支持两个算法类型选择
- UAV 数量范围支持 1~100
- USV 数量范围支持 1~100
- 两个算法分别保存自己的数量配置
- 保留重新生成场景功能
- 保留初始位置、速度和朝向设置
- 删除编队类型选择和固定队形配置
- UAV 和 USV 从随机初始位置生成
- 围捕算法能够自动形成围捕部署
- 护航算法能够自动形成护航部署
- Unity 可以动态显示对应数量设备
- 可以运行围捕算法
- 可以运行护航算法
- 任务运行期间不能修改数量或重新生成场景
- 全过程不连接真实 ROS
- `VIRTUAL_SIMULATION` 状态显示正确
- `scenarioReady` 回执中的数量与前端配置一致
- `requestId` 能够正确回传
- 100 台设备情况下系统可以稳定运行
- UAV 100 + USV 100 架构下不存在致命错误

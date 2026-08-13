# ROS heterogeneous_332 舰队桥接

本配置与 `Suu0129/UAV_USV` 当前 `main` 的统一世界保持一致：

- Unity 环境仓库：`wangling416614-create/UAV_USV_Unity`
- Gazebo 世界：`heterogeneous_332`
- 世界文件：`src/uav_usv_gazebo/worlds/heterogeneous_332.sdf`
- 主启动：`fleet_dynamic_capture_live_perception.launch.py`
- 无人机：`uav_01`、`uav_02`、`uav_03`
- 无人艇：`usv_01`、`usv_02`、`usv_03`
- 保护船：`friendly_ship`
- 敌方目标：`enemy_ship`

## 启动

先启动 ROS 统一场景：

```bash
cd <UAV_USV ROS工作区>
source /opt/ros/humble/setup.bash
source install/setup.bash
ros2 launch uav_usv_bringup fleet_dynamic_capture_live_perception.launch.py
```

再启动只读 Unity 桥接：

```bash
cd <UAV_USV_Unity仓库>/WebSocketBridge
UAV_USV_WS=<UAV_USV ROS工作区> ./run_fleet_bridge.sh
```

例如ROS工作区位于师妹截图中的WSL挂载路径时：

```bash
cd <UAV_USV_Unity仓库>/WebSocketBridge
UAV_USV_WS=/mnt/f/UVA_USV/UAV_USV_ROS ./run_fleet_bridge.sh
```

`platform_command_bridge`与本桥默认都会监听
`ws://0.0.0.0:8765/uav_usv`，因此不能同时运行。启动本桥前先在
`platform_command_bridge`终端按`Ctrl+C`；正常日志应包含
`Unity WebSocket bridge listening`、`fleet=3 USV + 3 UAV`和
`control mode=observe`。

桥接订阅 `/world/heterogeneous_332/pose/info`。`observe` 模式只向 Unity 转发
Gazebo 位姿，不接受 Unity 的运动命令；ROS/PX4/Nav2 始终是运动状态的权威来源。

## Unity 与 Gazebo 对齐内容

Unity 和 Gazebo 都加载同一份 Catalina glTF、二进制缓冲和卫星纹理。
Unity 通过 `glTFast 6.12.1` 在编辑器中将其导入为原生资源，运行时保留
glTF 节点变换，并与 Gazebo 一样应用 `0.024` 缩放、相同 ENU 原点和
`z=-0.8 m` 根高度。Unity 的外层对齐节点绕 Y 轴旋转 `180°`，用于抵消
glTFast 与 Gazebo 的水平轴约定差异，不使用负缩放或镜像网格。山脚基地
与三机平台的相对位置和贴坡关系由 SDF 位姿直接复现。Unity 另外保留远景
Sydney 海岸和无限海面填充作为纯视觉背景；这些视觉对象不带碰撞、
NavMesh 或运动控制，不参与协同计算。Gazebo 继续负责物理、碰撞和任务
状态。
Unity 的动态根对象使用与 SDF 相同的 ENU 坐标和初始位姿：

- `island_uav_base`：`(-75, -215, 0)`，偏航 `0.559 rad`。
- `shore_command_base`：`(-35, -190, 17.5)`，偏航 `0.559 rad`。
- UAV：`(-86.86, -222.43, 19.75)`、`(-75, -215, 19.75)`、
  `(-63.14, -207.57, 19.75)`。
- USV：`(-120, -305, 0)`、`(-75, -320, 0)`、`(-30, -305, 0)`。
- `friendly_ship`：`(-150, -355, 0)`。
- `enemy_ship`：`(-80, -315, 0)`。
- 海面：Unity 与 Gazebo 均以 ENU `(0, 0)` 为中心，尺寸统一为
  `1050 m × 900 m`。Gazebo 使用 `1050 × 900 × 0.1 m` 的有限薄盒体作为
  权威碰撞域；Unity 动态海面只负责同步显示，不重复执行物理碰撞。

Unity 中的 Catalina 网格只负责显示，不附加 Collider、NavMesh 或运动控制
组件；Gazebo 的 `shore_collision_boundary` 和
`catalina_collision_proxy` 仍是岸线碰撞的唯一权威。
旧灯塔、浮标、任务点和额外障碍物不加载，避免出现 ROS 中不存在的
可交互对象。

Unity 与 Gazebo 的模型外观和内部网格可以不同。协同依赖的固定契约是：

- 两边必须具有相同数量的任务实体和唯一 ID。
- 根节点必须使用相同 ENU 初始位置和偏航。
- `usvs[]`、`uavs[]`、`friendly_ship`、`target` 必须按 ID 映射，不能依赖
  数组顺序。
- Unity 基站可以继续使用原来的美术模型，但 `island_uav_base` 和
  `shore_command_base` 两个根节点的名称、数量和世界位姿必须与 SDF 一致。
- 运动、碰撞、传感器和任务状态以 ROS/Gazebo 为准；Unity 只负责显示。

Unity 默认由 `GazeboComparisonCamera` 在每帧末尾锁定到 SDF 的
`camera_pose=-430 -560 420 0 0.78 0.72`，水平视场角为 `90°`，因此不会被
任务追踪相机覆盖。按 `G` 可随时恢复 Gazebo 对照视角；按 `C`、`1`、`2`、
`3`、`4` 或 `Tab` 切换到 Unity 任务相机。

## 协议

协议版本 2 提供：

- `usvs[]`、`uavs[]`：完整 3+3 舰队 ID、位姿和状态。
- `friendly_ship`：保护船 ID 和位姿。
- `target`：`enemy_ship` 的 ID 和位姿。
- `fleet`：期望数量、已接收数量和就绪状态。
- `mission`：围捕状态与角色分配。

`boat`、`drone`、`target_vessel` 仅保留用于旧客户端兼容。可使用以下命令检查
实时帧：

```bash
python3 test_fleet_bridge_frame.py
```

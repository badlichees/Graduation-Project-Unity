# TurtleBot3 Unity 仿真端

## 1. 架构

```txt
Unity 场景
  ├─ 地图生成与障碍物编辑
  ├─ TurtleBot3 物理仿真
  ├─ ROS topic 发布
  ├─ 目标点与算法选择
  └─ 路径和统计可视化
        │
        │ ROS TCP Connector: 127.0.0.1:10000
        ▼
ROS2 / Nav2
  ├─ ros_tcp_endpoint
  ├─ Nav2 navigation stack
  ├─ tb3_unity_nav
  └─ grid_planners
```

主要 topic：

| Topic | 方向 | 类型 | 用途 |
|---|---|---|---|
| `/map_raw` | Unity -> ROS2 | `nav_msgs/OccupancyGrid` | Unity 原始栅格地图 |
| `/odom` | Unity -> ROS2 | `nav_msgs/Odometry` | 机器人里程计 |
| `/scan` | Unity -> ROS2 | `sensor_msgs/LaserScan` | 雷达扫描 |
| `/imu` | Unity -> ROS2 | `sensor_msgs/Imu` | IMU 数据 |
| `/joint_states` | Unity -> ROS2 | `sensor_msgs/JointState` | 轮关节状态 |
| `/goal_pose` | Unity -> ROS2 | `geometry_msgs/PoseStamped` | 导航目标点 |
| `/planner_selector_unity` | Unity -> ROS2 | `std_msgs/String` | Unity 侧算法选择 |
| `/cmd_vel` | ROS2 -> Unity | `geometry_msgs/Twist` | Nav2 输出速度控制 |
| `/plan` | ROS2 -> Unity | `nav_msgs/Path` | 全局规划路径 |
| `/planner_stats` | ROS2 -> Unity | `std_msgs/String` | 规划性能统计 |

坐标约定：Unity 使用 XZ 平面，ROS2 使用 XY 平面。项目中目标点、地图、里程计和路径显示均按同一套映射处理：

```txt
ROS x = Unity z
ROS y = -Unity x
```

## 2. 依赖

Unity 版本：

```txt
Unity 2022.3.57f1c1
```

主要 Unity Package：

- `com.unity.robotics.ros-tcp-connector` v0.7.0
- `com.unity.robotics.urdf-importer` v0.5.2
- `com.unity.robotics.visualizations` v0.7.0
- `com.unity.render-pipelines.universal` 14.0.9

ROS2 侧需要先启动 `/home/$USERNAME/ros2_ws` 中的联调栈。

ROS TCP 连接配置：

```txt
IP: 127.0.0.1
Port: 10000
Prefab: Assets/Resources/ROSConnectionPrefab.prefab
```

## 3. 模块介绍

| 模块 | 文件 | 说明 |
|---|---|---|
| 地图生成 | `Assets/Scripts/MapGeneration/MapGenerator.cs` | 生成连通栅格地图、维护障碍物数据 |
| 地图发布 | `Assets/Scripts/RosBridge/OccupancyGridPublisher.cs` | 将 Unity 地图发布为 `/map_raw` |
| 目标发布 | `Assets/Scripts/RosBridge/GoalPublisher.cs` | 发布 `/goal_pose`，并发布算法选择 |
| 参数发布 | `Assets/Scripts/RosBridge/PlannerParameterPublisher.cs` | 将 Unity 面板中的规划参数发布到 ROS2 |
| 速度控制 | `Assets/Scripts/RosBridge/TurtleBotController.cs` | 订阅 `/cmd_vel` 并驱动左右轮 |
| 里程计 | `Assets/Scripts/RosBridge/OdometryPublisher.cs` | 发布 `/odom` |
| 雷达 | `Assets/Scripts/RosBridge/LidarPublisher.cs` | 通过 Raycast 发布 `/scan` |
| IMU | `Assets/Scripts/RosBridge/ImuPublisher.cs` | 发布 `/imu` |
| 关节状态 | `Assets/Scripts/RosBridge/JointStatePublisher.cs` | 发布 `/joint_states` |
| 路径显示 | `Assets/Scripts/Visualization/PathVisualizer.cs` | 订阅 `/plan` 并显示路径 |
| 统计接收 | `Assets/Scripts/Visualization/PlannerStatsReceiver.cs` | 订阅 `/planner_stats`，到达目标后才提交到面板 |
| 运行监控 | `Assets/Scripts/Visualization/NavigationRunMonitor.cs` | 判定 Goal 完成并统计行驶距离 |
| 统计面板 | `Assets/Scripts/Editor/PlannerDashboardWindow.cs` | 显示并导出算法统计结果 |
| 参数面板 | `Assets/Scripts/Editor/PlannerParameterWindow.cs` | 调整 WA*、RRT* 和 DWB 参数 |
| 地图参数面板 | `Assets/Scripts/Editor/MapParameterWindow.cs` | 运行时调整地图种子、障碍物密度和保护半径 |
| 机器人重置 | `Assets/Scripts/RosBridge/RobotResetController.cs` | 重置 TurtleBot3 位姿并清理旧路径 |

主要场景：

```txt
Assets/Scenes/TurtleBot3.unity
```

## 4. 启动与操作

### 4.1 启动 ROS2 侧

在 WSL2 中执行：

```bash
cd /home/$USERNAME/ros2_ws
source /opt/ros/humble/setup.bash
colcon build --packages-select grid_planners tb3_unity_nav
source install/setup.bash
ros2 launch tb3_unity_nav unity_nav2.launch.py
```

### 4.2 启动 Unity 侧

1. 使用 Unity 2022.3.57f1c1 打开本项目。
2. 打开场景：`Assets/Scenes/TurtleBot3.unity`。
3. 确认 `ROSConnectionPrefab` 使用 `127.0.0.1:10000`。
4. 点击 Play。

### 4.3 操作方式

| 操作 | 方式 |
|---|---|
| 发布目标点 | 修改 `GoalPublisher.targetPositionXZ` 后按 `Space` |
| 切换规划算法 | Play Mode 下按 `Tab` |
| 调整规划参数 | Unity 菜单 `Tools/规划参数面板` |
| 调整地图参数 | Unity 菜单 `Tools/地图参数面板` |
| 重置机器人 | 按 `R` |
| 查看路径 | 场景中观察彩色 LineRenderer |
| 打开统计面板 | Unity 菜单 `Tools/算法性能面板` |
| 导出统计数据 | 统计面板中点击 `导出 CSV` |

当前支持的算法：

```txt
Astar, Dijkstra, Greedy, NavFn, RRTStar, DLite, JPS, WAStar
```

## 5. 实验

建议实验变量：

| 变量 | 建议固定或记录 |
|---|---|
| 地图尺寸 | 例如 `31 x 31` |
| 障碍物比例 | 例如 `0.3` |
| 地图随机种子 | 固定 seed，保证可复现 |
| 起点 | 使用机器人初始位置 |
| 目标点 | 使用固定 `targetPositionXZ` |
| 重复次数 | 每个算法多次运行 |

CSV 导出字段：

| 字段 | 说明 |
|---|---|
| 总次数 / 成功次数 / 失败次数 | 规划尝试统计 |
| 实验失败次数 | 超时或机器人未能到达目标 |
| 平均耗时 (ms) | 规划器从开始计算到生成结果的时间（NavFn 不支持） |
| 平均行驶距离 (m) | 机器人实际行驶总路程 |
| 平均展开节点数 | 搜索过程中扩展的栅格节点数（NavFn 不支持） |

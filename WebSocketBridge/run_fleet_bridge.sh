#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

exec "$SCRIPT_DIR/run_bridge.sh" --ros-args \
  -p gazebo_pose_topic:=/world/heterogeneous_332/pose/info \
  -p control_mode:=observe \
  -p friendly_ship_name:=friendly_ship \
  -p target_vessel_name:=enemy_ship \
  -p max_abs_coordinate:=600.0 \
  -p enable_camera_stream:=true \
  -p camera_publish_rate:=8.0 \
  -p camera_jpeg_quality:=55 \
  -p camera_max_width:=640 \
  -p camera_max_height:=360 \
  -p default_camera_id:=usv_01 \
  -p usv_names:="['usv_01','usv_02','usv_03']" \
  -p uav_names:="['uav_01','uav_02','uav_03']" \
  "$@"

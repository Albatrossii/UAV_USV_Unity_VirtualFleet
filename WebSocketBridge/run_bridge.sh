#!/usr/bin/env bash
set -euo pipefail

# ROS setup scripts may read optional environment variables. Temporarily disable
# nounset so this launcher remains compatible with a strict shell.
set +u
source /opt/ros/humble/setup.bash

ROS_WORKSPACE="${UAV_USV_WS:-$HOME/project/UAV_USV}"
if [[ -f "$ROS_WORKSPACE/install/setup.bash" ]]; then
  source "$ROS_WORKSPACE/install/setup.bash"
fi
set -u

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
RUNTIME_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)/.runtime"
mkdir -p "$RUNTIME_DIR/ros_log"
export ROS_LOG_DIR="$RUNTIME_DIR/ros_log"

exec python3 "$SCRIPT_DIR/unity_websocket_bridge.py" "$@"

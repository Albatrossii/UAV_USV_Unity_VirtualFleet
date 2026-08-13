#!/usr/bin/env python3

import base64
import hashlib
import json
import math
import queue
import socket
import struct
import threading
import time

from action_msgs.msg import GoalStatus
from geometry_msgs.msg import PoseStamped
from gz.msgs10.pose_v_pb2 import Pose_V
from gz.msgs10.twist_pb2 import Twist
from gz.transport13 import Node as GzTransportNode
import numpy as np
import rclpy
from rclpy.action import ActionClient
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import DurabilityPolicy
from rclpy.qos import QoSProfile
from rclpy.qos import ReliabilityPolicy
from sensor_msgs.msg import Image

try:
    import cv2
except ImportError:
    cv2 = None

try:
    from nav2_msgs.action import NavigateToPose
except ImportError:
    NavigateToPose = None

try:
    from uav_usv_interfaces.msg import CaptureAssignmentArray
    from uav_usv_interfaces.msg import CaptureState
    from uav_usv_interfaces.msg import VehicleState
except ImportError:
    CaptureAssignmentArray = None
    CaptureState = None
    VehicleState = None


def clamp(value, lower, upper):
    return max(lower, min(upper, value))


def wrap_pi(angle):
    return math.atan2(math.sin(angle), math.cos(angle))


def yaw_from_quaternion(quaternion):
    x, y, z, w = quaternion
    siny_cosp = 2.0 * (w * z + x * y)
    cosy_cosp = 1.0 - 2.0 * (y * y + z * z)
    return math.atan2(siny_cosp, cosy_cosp)


class UnityWebSocketBridge(Node):
    def __init__(self):
        super().__init__('unity_websocket_bridge')

        self.declare_parameter('gazebo_pose_topic', '/world/default/pose/info')
        self.declare_parameter('boat_name', 'landing_boat')
        self.declare_parameter('drone_name', 'x500_0')
        self.declare_parameter('usv_names', ['landing_boat'])
        self.declare_parameter('uav_names', ['x500_0'])
        self.declare_parameter('lighthouse_name', 'navigation_lighthouse')
        self.declare_parameter('buoy_west_name', 'medium_buoy_west_channel')
        self.declare_parameter('buoy_south_name', 'medium_buoy_south_channel')
        self.declare_parameter('buoy_east_name', 'medium_buoy_east_channel')
        self.declare_parameter('friendly_ship_name', 'friendly_ship')
        self.declare_parameter('target_vessel_name', 'target_vessel')
        self.declare_parameter('ws_host', '0.0.0.0')
        self.declare_parameter('ws_port', 8765)
        self.declare_parameter('publish_rate', 30.0)
        self.declare_parameter('pose_stale_timeout', 1.0)
        self.declare_parameter('control_mode', 'direct')
        self.declare_parameter('boat_cmd_topic', '/model/simple_boat/cmd_vel')
        self.declare_parameter('nav2_action_name', 'navigate_to_pose')
        self.declare_parameter('control_rate', 15.0)
        self.declare_parameter('waypoint_radius', 2.0)
        self.declare_parameter('final_arrival_radius', 1.0)
        self.declare_parameter('slow_radius', 6.0)
        self.declare_parameter('max_speed', 1.25)
        self.declare_parameter('min_speed', 0.25)
        self.declare_parameter('turn_gain', 1.5)
        self.declare_parameter('max_turn', 1.8)
        self.declare_parameter('heading_slowdown_yaw', 1.0)
        self.declare_parameter('max_path_points', 512)
        self.declare_parameter('max_abs_coordinate', 200.0)
        self.declare_parameter('capture_state_topic', '/capture/state')
        self.declare_parameter('capture_roles_topic', '/capture/roles')
        self.declare_parameter('fleet_state_topic', '/fleet/state')
        self.declare_parameter('enable_camera_stream', True)
        self.declare_parameter('camera_publish_rate', 8.0)
        self.declare_parameter('camera_jpeg_quality', 55)
        self.declare_parameter('camera_max_width', 640)
        self.declare_parameter('camera_max_height', 360)
        self.declare_parameter('default_camera_id', 'usv_01')

        self.pose_topic = self.get_parameter('gazebo_pose_topic').value
        self.ws_host = self.get_parameter('ws_host').value
        self.ws_port = int(self.get_parameter('ws_port').value)
        self.publish_rate = float(self.get_parameter('publish_rate').value)
        self.pose_stale_timeout = float(
            self.get_parameter('pose_stale_timeout').value
        )
        self.control_mode = str(
            self.get_parameter('control_mode').value
        ).strip().lower()
        if self.control_mode not in ('observe', 'direct', 'nav2'):
            raise ValueError(
                'control_mode must be "observe", "direct", or "nav2"'
            )
        self.boat_cmd_topic = self.get_parameter('boat_cmd_topic').value
        self.control_rate = max(
            1.0,
            float(self.get_parameter('control_rate').value),
        )
        self.waypoint_radius = max(
            0.1,
            float(self.get_parameter('waypoint_radius').value),
        )
        self.final_arrival_radius = max(
            0.1,
            float(self.get_parameter('final_arrival_radius').value),
        )
        self.slow_radius = max(
            self.final_arrival_radius,
            float(self.get_parameter('slow_radius').value),
        )
        self.max_speed = max(0.0, float(self.get_parameter('max_speed').value))
        self.min_speed = clamp(
            float(self.get_parameter('min_speed').value),
            0.0,
            self.max_speed,
        )
        self.turn_gain = float(self.get_parameter('turn_gain').value)
        self.max_turn = max(0.0, float(self.get_parameter('max_turn').value))
        self.heading_slowdown_yaw = max(
            0.01,
            float(self.get_parameter('heading_slowdown_yaw').value),
        )
        self.max_path_points = max(
            2,
            int(self.get_parameter('max_path_points').value),
        )
        self.max_abs_coordinate = max(
            1.0,
            float(self.get_parameter('max_abs_coordinate').value),
        )

        self.usv_names = tuple(
            str(value) for value in self.get_parameter('usv_names').value
            if str(value)
        )
        self.uav_names = tuple(
            str(value) for value in self.get_parameter('uav_names').value
            if str(value)
        )
        if not self.usv_names:
            self.usv_names = (str(self.get_parameter('boat_name').value),)
        if not self.uav_names:
            self.uav_names = (str(self.get_parameter('drone_name').value),)
        self.target_entity_name = str(
            self.get_parameter('target_vessel_name').value
        )
        self.friendly_ship_name = str(
            self.get_parameter('friendly_ship_name').value
        )

        self.entity_names = {
            self.get_parameter('lighthouse_name').value: 'lighthouse',
            self.get_parameter('buoy_west_name').value: 'buoy_west',
            self.get_parameter('buoy_south_name').value: 'buoy_south',
            self.get_parameter('buoy_east_name').value: 'buoy_east',
            self.friendly_ship_name: self.friendly_ship_name,
            self.target_entity_name: self.target_entity_name,
        }
        for name in self.usv_names:
            self.entity_names[name] = name
        for name in self.uav_names:
            self.entity_names[name] = name

        self.latest = {}
        self.capture_state = {}
        self.capture_roles = {}
        self.vehicle_states = {}
        self.last_gazebo_update = None
        self.last_boat_update = None
        self.sequence = 0
        self.lock = threading.Lock()
        self.client_lock = threading.Lock()
        self.ws_clients = []
        self.client_send_locks = {}
        self.running = True
        self.server_socket = None
        self.command_queue = queue.Queue(maxsize=64)
        self.active_path = []
        self.active_path_id = 0
        self.waypoint_index = 0
        self.control_state = (
            'observe' if self.control_mode == 'observe' else 'idle'
        )
        self.control_message = (
            'ROS fleet/base station is authoritative'
            if self.control_mode == 'observe'
            else 'Waiting for a Unity path'
        )
        self.nav2_goal_handle = None
        self.nav2_goal_pending = False

        self.enable_camera_stream = bool(
            self.get_parameter('enable_camera_stream').value
        )
        self.camera_publish_rate = max(
            1.0,
            float(self.get_parameter('camera_publish_rate').value),
        )
        self.camera_jpeg_quality = int(
            clamp(float(self.get_parameter('camera_jpeg_quality').value), 20, 95)
        )
        self.camera_max_width = max(
            80, int(self.get_parameter('camera_max_width').value)
        )
        self.camera_max_height = max(
            60, int(self.get_parameter('camera_max_height').value)
        )
        self.selected_camera_id = str(
            self.get_parameter('default_camera_id').value
        ).strip() or 'usv_01'
        self.camera_topics = {}
        self.latest_camera_msgs = {}
        self.latest_camera_times = {}
        self.camera_lock = threading.Lock()
        self.last_streamed_camera_stamp = {}

        self.gz_node = GzTransportNode()
        subscribed = self.gz_node.subscribe(Pose_V, self.pose_topic, self._on_pose)
        if not subscribed:
            raise RuntimeError(f'Unable to subscribe to {self.pose_topic}')
        self.boat_cmd_pub = self.gz_node.advertise(self.boat_cmd_topic, Twist)

        self.nav2_client = None
        if self.control_mode == 'nav2':
            if NavigateToPose is None:
                raise RuntimeError(
                    'control_mode=nav2 requires ros-humble-nav2-msgs'
                )
            self.nav2_client = ActionClient(
                self,
                NavigateToPose,
                self.get_parameter('nav2_action_name').value,
            )

        self.server_thread = threading.Thread(
            target=self._serve, name='unity-websocket-server', daemon=True
        )
        self.server_thread.start()

        self.create_timer(1.0 / max(self.publish_rate, 1.0), self._publish_frame)
        self.create_timer(1.0 / self.control_rate, self._control_tick)
        if self.enable_camera_stream:
            self._setup_camera_subscriptions()
            self.create_timer(
                1.0 / self.camera_publish_rate,
                self._publish_selected_camera,
            )
        if CaptureState is not None:
            fleet_state_qos = QoSProfile(depth=10)
            fleet_state_qos.reliability = ReliabilityPolicy.BEST_EFFORT
            fleet_state_qos.durability = DurabilityPolicy.VOLATILE
            self.create_subscription(
                CaptureState,
                str(self.get_parameter('capture_state_topic').value),
                self._on_capture_state,
                10,
            )
            self.create_subscription(
                CaptureAssignmentArray,
                str(self.get_parameter('capture_roles_topic').value),
                self._on_capture_roles,
                10,
            )
            self.create_subscription(
                VehicleState,
                str(self.get_parameter('fleet_state_topic').value),
                self._on_vehicle_state,
                fleet_state_qos,
            )
        camera_note = (
            f'camera stream on ({self.camera_publish_rate:.0f} Hz, '
            f'selected={self.selected_camera_id})'
            if self.enable_camera_stream
            else 'camera stream off'
        )
        self.get_logger().info(
            f'Unity WebSocket bridge listening on ws://{self.ws_host}:{self.ws_port}/uav_usv; '
            f'reading Gazebo poses from {self.pose_topic}; '
            f'fleet={len(self.usv_names)} USV + {len(self.uav_names)} UAV; '
            f'control mode={self.control_mode}; {camera_note}'
        )

    def _setup_camera_subscriptions(self):
        if cv2 is None:
            self.get_logger().warn(
                'OpenCV (cv2) unavailable; Gazebo camera stream disabled'
            )
            self.enable_camera_stream = False
            return

        sensor_qos = QoSProfile(depth=1)
        sensor_qos.reliability = ReliabilityPolicy.BEST_EFFORT
        sensor_qos.durability = DurabilityPolicy.VOLATILE

        for usv_id in self.usv_names:
            topic = f'/fleet/uplink/{usv_id}/camera'
            self.camera_topics[usv_id] = topic
            self.create_subscription(
                Image,
                topic,
                self._make_camera_callback(usv_id),
                sensor_qos,
            )
            self.get_logger().info(f'Subscribed Gazebo USV camera {topic}')

        for uav_id in self.uav_names:
            topic = f'/fleet/uplink/{uav_id}/camera/image_raw'
            self.camera_topics[uav_id] = topic
            self.create_subscription(
                Image,
                topic,
                self._make_camera_callback(uav_id),
                sensor_qos,
            )
            self.get_logger().info(f'Subscribed Gazebo UAV camera {topic}')

        if self.selected_camera_id not in self.camera_topics:
            self.selected_camera_id = next(iter(self.camera_topics), 'usv_01')

    def _make_camera_callback(self, camera_id):
        def callback(msg):
            with self.camera_lock:
                self.latest_camera_msgs[camera_id] = msg
                self.latest_camera_times[camera_id] = time.monotonic()

        return callback

    def _accept_select_camera(self, command):
        camera_id = str(command.get('camera_id', '')).strip()
        if not camera_id:
            self.get_logger().warn('select_camera missing camera_id')
            return
        if self.camera_topics and camera_id not in self.camera_topics:
            self.get_logger().warn(
                f'Unknown camera_id {camera_id!r}; known={list(self.camera_topics)}'
            )
            return
        with self.camera_lock:
            self.selected_camera_id = camera_id
            self.last_streamed_camera_stamp.pop(camera_id, None)
        self.get_logger().info(f'Unity selected Gazebo camera {camera_id}')

    def _publish_selected_camera(self):
        if not self.enable_camera_stream or cv2 is None:
            return
        with self.client_lock:
            if not self.ws_clients:
                return

        with self.camera_lock:
            camera_id = self.selected_camera_id
            msg = self.latest_camera_msgs.get(camera_id)
            received_at = self.latest_camera_times.get(camera_id)

        if msg is None or received_at is None:
            return

        stamp_key = (msg.header.stamp.sec, msg.header.stamp.nanosec, msg.height, msg.width)
        if self.last_streamed_camera_stamp.get(camera_id) == stamp_key:
            return

        try:
            frame = self._decode_image(msg)
            height, width = frame.shape[:2]
            scale = min(
                1.0,
                float(self.camera_max_width) / max(width, 1),
                float(self.camera_max_height) / max(height, 1),
            )
            if scale < 0.999:
                frame = cv2.resize(
                    frame,
                    (
                        max(1, int(round(width * scale))),
                        max(1, int(round(height * scale))),
                    ),
                    interpolation=cv2.INTER_AREA,
                )
            ok, encoded = cv2.imencode(
                '.jpg',
                frame,
                [int(cv2.IMWRITE_JPEG_QUALITY), self.camera_jpeg_quality],
            )
            if not ok:
                return
            jpeg_b64 = base64.b64encode(encoded.tobytes()).decode('ascii')
            out_h, out_w = frame.shape[:2]
            age = max(0.0, time.monotonic() - received_at)
            payload = {
                'type': 'camera_frame',
                'camera_id': camera_id,
                'encoding': 'jpeg',
                'width': int(out_w),
                'height': int(out_h),
                'timestamp_ms': int(time.time() * 1000),
                'age_seconds': round(age, 3),
                'jpeg_base64': jpeg_b64,
            }
            self._broadcast(json.dumps(payload, separators=(',', ':')))
            self.last_streamed_camera_stamp[camera_id] = stamp_key
        except Exception as exc:
            self.get_logger().warn(
                f'Unable to stream camera {camera_id}: {exc}',
                throttle_duration_sec=5.0,
            )

    @staticmethod
    def _decode_image(msg):
        channels = {
            'rgb8': 3,
            'bgr8': 3,
            'rgba8': 4,
            'bgra8': 4,
            'mono8': 1,
        }.get(msg.encoding.lower())
        if channels is None:
            raise ValueError(f'unsupported encoding {msg.encoding}')
        rows = np.frombuffer(msg.data, dtype=np.uint8).reshape(
            msg.height, msg.step
        )
        image = rows[:, : msg.width * channels].reshape(
            msg.height, msg.width, channels
        )
        encoding = msg.encoding.lower()
        if encoding == 'rgb8':
            return cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
        if encoding == 'rgba8':
            return cv2.cvtColor(image, cv2.COLOR_RGBA2BGR)
        if encoding == 'bgra8':
            return cv2.cvtColor(image, cv2.COLOR_BGRA2BGR)
        if encoding == 'mono8':
            return cv2.cvtColor(image, cv2.COLOR_GRAY2BGR)
        return image.copy()

    def _on_pose(self, msg):
        updates = {}
        for pose in msg.pose:
            short_name = self.entity_names.get(pose.name)
            if short_name is None:
                continue

            updates[short_name] = {
                'position': [
                    float(pose.position.x),
                    float(pose.position.y),
                    float(pose.position.z),
                ],
                'orientation': [
                    float(pose.orientation.x),
                    float(pose.orientation.y),
                    float(pose.orientation.z),
                    float(pose.orientation.w),
                ],
            }

        if updates:
            with self.lock:
                self.latest.update(updates)
                self.last_gazebo_update = time.monotonic()
                if self.usv_names[0] in updates:
                    self.last_boat_update = self.last_gazebo_update

    def _on_capture_state(self, msg):
        state = {
            'state': int(msg.state),
            'state_name': str(msg.state_name),
            'target_id': str(msg.target_id),
            'reason': str(msg.reason),
            'configured_uavs': int(msg.configured_uavs),
            'configured_usvs': int(msg.configured_usvs),
            'active_uavs': int(msg.active_uavs),
            'active_usvs': int(msg.active_usvs),
            'allocation_generation': int(msg.allocation_generation),
            'degraded': bool(msg.degraded),
        }
        with self.lock:
            self.capture_state = state

    def _on_capture_roles(self, msg):
        assignments = []
        for assignment in msg.assignments:
            assignments.append({
                'vehicle_id': str(assignment.vehicle_id),
                'vehicle_type': int(assignment.vehicle_type),
                'role_type': int(assignment.role_type),
                'role_name': str(assignment.role_name),
                'target_pose': self._ros_pose(assignment.target_pose),
                'assignment_cost': float(assignment.assignment_cost),
                'active': bool(assignment.active),
                'status': str(assignment.status),
            })
        roles = {
            'target_id': str(msg.target_id),
            'capture_center': [
                float(msg.capture_center.x),
                float(msg.capture_center.y),
                float(msg.capture_center.z),
            ],
            'capture_radius': float(msg.capture_radius),
            'generation': int(msg.generation),
            'assignments': assignments,
        }
        with self.lock:
            self.capture_roles = roles

    def _on_vehicle_state(self, msg):
        state = {
            'vehicle_type': int(msg.vehicle_type),
            'online': bool(msg.online),
            'armed': bool(msg.armed),
            'mode': str(msg.mode),
            'pose': self._ros_pose(msg.pose),
            'twist': {
                'linear': [
                    float(msg.twist.linear.x),
                    float(msg.twist.linear.y),
                    float(msg.twist.linear.z),
                ],
                'angular': [
                    float(msg.twist.angular.x),
                    float(msg.twist.angular.y),
                    float(msg.twist.angular.z),
                ],
            },
            'battery_percent': float(msg.battery_percent),
            'active_command_id': str(msg.active_command_id),
            'status_text': str(msg.status_text),
        }
        with self.lock:
            self.vehicle_states[str(msg.vehicle_id)] = state

    @staticmethod
    def _ros_pose(pose):
        return {
            'position': [
                float(pose.position.x),
                float(pose.position.y),
                float(pose.position.z),
            ],
            'orientation': [
                float(pose.orientation.x),
                float(pose.orientation.y),
                float(pose.orientation.z),
                float(pose.orientation.w),
            ],
        }

    def _serve(self):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
            self.server_socket = server
            server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            server.bind((self.ws_host, self.ws_port))
            server.listen(8)
            server.settimeout(0.5)

            while self.running:
                try:
                    client, address = server.accept()
                except socket.timeout:
                    continue
                except OSError:
                    break

                client.settimeout(2.0)
                if self._handshake(client):
                    client.settimeout(None)
                    with self.client_lock:
                        self.ws_clients.append(client)
                        self.client_send_locks[client] = threading.Lock()
                    self.get_logger().info(f'Unity WebSocket client connected: {address[0]}:{address[1]}')
                    threading.Thread(
                        target=self._receive_client,
                        args=(client, address),
                        name=f'unity-ws-{address[0]}:{address[1]}',
                        daemon=True,
                    ).start()
                else:
                    client.close()

    def _handshake(self, client):
        try:
            request = b''
            while b'\r\n\r\n' not in request and len(request) < 8192:
                chunk = client.recv(1024)
                if not chunk:
                    return False
                request += chunk

            headers = request.decode('utf-8', errors='ignore').split('\r\n')
            key = None
            for header in headers:
                if header.lower().startswith('sec-websocket-key:'):
                    key = header.split(':', 1)[1].strip()
                    break
            if not key:
                return False

            accept = base64.b64encode(
                hashlib.sha1((key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest()
            ).decode()
            response = (
                'HTTP/1.1 101 Switching Protocols\r\n'
                'Upgrade: websocket\r\n'
                'Connection: Upgrade\r\n'
                f'Sec-WebSocket-Accept: {accept}\r\n'
                '\r\n'
            )
            client.sendall(response.encode('ascii'))
            return True
        except OSError:
            return False

    def _receive_client(self, client, address):
        try:
            while self.running:
                opcode, payload = self._read_frame(client)
                if opcode == 0x8:
                    break
                if opcode == 0x9:
                    self._send_frame(client, payload, opcode=0xA)
                    continue
                if opcode != 0x1:
                    continue
                self._queue_command(payload.decode('utf-8'))
        except (ConnectionError, OSError, UnicodeDecodeError, ValueError):
            pass
        finally:
            self._remove_client(client)
            self.get_logger().info(
                f'Unity WebSocket client disconnected: {address[0]}:{address[1]}'
            )

    def _queue_command(self, text):
        try:
            command = json.loads(text)
            if not isinstance(command, dict):
                raise ValueError('command must be a JSON object')
            self.command_queue.put_nowait(command)
        except (json.JSONDecodeError, ValueError) as exc:
            self._set_control_status('error', f'Invalid Unity command: {exc}')
        except queue.Full:
            self._set_control_status('error', 'Unity command queue is full')

    @staticmethod
    def _recv_exact(client, count):
        chunks = []
        remaining = count
        while remaining:
            chunk = client.recv(remaining)
            if not chunk:
                raise ConnectionError('WebSocket closed')
            chunks.append(chunk)
            remaining -= len(chunk)
        return b''.join(chunks)

    @classmethod
    def _read_frame(cls, client):
        header = cls._recv_exact(client, 2)
        opcode = header[0] & 0x0F
        masked = (header[1] & 0x80) != 0
        length = header[1] & 0x7F
        if length == 126:
            length = struct.unpack('!H', cls._recv_exact(client, 2))[0]
        elif length == 127:
            length = struct.unpack('!Q', cls._recv_exact(client, 8))[0]
        if length > 1024 * 1024:
            raise ValueError('WebSocket command exceeds 1 MiB')
        if not masked:
            raise ValueError('Client WebSocket frames must be masked')

        mask = cls._recv_exact(client, 4)
        payload = bytearray(cls._recv_exact(client, length))
        for index in range(length):
            payload[index] ^= mask[index % 4]
        return opcode, bytes(payload)

    def _send_frame(self, client, payload, opcode=0x1):
        if isinstance(payload, str):
            payload = payload.encode('utf-8')
        length = len(payload)
        first = 0x80 | opcode
        if length < 126:
            header = bytes([first, length])
        elif length <= 0xFFFF:
            header = bytes([first, 126]) + struct.pack('!H', length)
        else:
            header = bytes([first, 127]) + struct.pack('!Q', length)

        with self.client_lock:
            send_lock = self.client_send_locks.get(client)
        if send_lock is None:
            raise ConnectionError('WebSocket client is not registered')
        with send_lock:
            frame = header + payload
            sent = client.send(frame, socket.MSG_DONTWAIT)
            if sent != len(frame):
                raise ConnectionError('WebSocket client is not consuming frames')

    def _remove_client(self, client):
        with self.client_lock:
            was_registered = client in self.ws_clients
            if client in self.ws_clients:
                self.ws_clients.remove(client)
            self.client_send_locks.pop(client, None)
            no_clients_remain = not self.ws_clients
        try:
            client.close()
        except OSError:
            pass
        if was_registered and no_clients_remain and self.active_path:
            try:
                self.command_queue.put_nowait({'type': 'boat_stop'})
            except queue.Full:
                self._publish_boat_cmd(0.0, 0.0)

    def _control_tick(self):
        self._drain_commands()

        if self.control_mode == 'observe':
            return
        if self.control_mode == 'nav2':
            self._update_nav2_control()
        else:
            self._update_direct_control()

    def _drain_commands(self):
        for _ in range(16):
            try:
                command = self.command_queue.get_nowait()
            except queue.Empty:
                return

            command_type = command.get('type')
            # Camera selection is always allowed (including observe mode).
            if command_type == 'select_camera':
                self._accept_select_camera(command)
                continue

            if self.control_mode == 'observe':
                self._set_control_status(
                    'observe',
                    'ROS fleet/base station is authoritative; Unity commands ignored',
                )
                continue

            if command_type == 'boat_path':
                self._accept_boat_path(command)
            elif command_type == 'boat_stop':
                self._stop_boat('Stopped from Unity', clear_path=True)
            else:
                self._set_control_status(
                    'error',
                    f'Unknown command type: {command_type!r}',
                )

    def _accept_boat_path(self, command):
        raw_points = command.get('points')
        if not isinstance(raw_points, list):
            self._set_control_status('error', 'boat_path.points must be an array')
            return
        if len(raw_points) < 2 or len(raw_points) > self.max_path_points:
            self._set_control_status(
                'error',
                f'Path requires 2..{self.max_path_points} points',
            )
            return

        points = []
        try:
            for point in raw_points:
                x = float(point['x'])
                y = float(point['y'])
                if not math.isfinite(x) or not math.isfinite(y):
                    raise ValueError('coordinates must be finite')
                if abs(x) > self.max_abs_coordinate or abs(y) > self.max_abs_coordinate:
                    raise ValueError('coordinate is outside the configured world bounds')
                points.append((x, y))
        except (KeyError, TypeError, ValueError) as exc:
            self._set_control_status('error', f'Invalid boat path: {exc}')
            return

        try:
            path_id = int(command.get('path_id', int(time.time() * 1000)))
        except (TypeError, ValueError):
            path_id = int(time.time() * 1000)

        self._cancel_nav2_goal()
        self.active_path = points
        self.active_path_id = path_id
        self.waypoint_index = self._first_unreached_waypoint(points)
        if self.waypoint_index >= len(points):
            self._complete_path()
            return

        self.nav2_goal_pending = False
        self._set_control_status(
            'tracking',
            f'Accepted Unity A* path {path_id}',
        )
        self.get_logger().info(
            'Accepted Unity A* path %d with %d waypoints; starting at %d'
            % (path_id, len(points), self.waypoint_index)
        )

    def _first_unreached_waypoint(self, points):
        boat = self._boat_snapshot()
        if boat is None:
            return 0
        boat_x, boat_y, _ = boat
        index = 0
        while index < len(points) - 1:
            if math.hypot(points[index][0] - boat_x, points[index][1] - boat_y) > self.waypoint_radius:
                break
            index += 1
        return index

    def _update_direct_control(self):
        if not self.active_path:
            return

        boat = self._boat_snapshot()
        if boat is None:
            self._publish_boat_cmd(0.0, 0.0)
            self._set_control_status('waiting_pose', 'Waiting for fresh Gazebo boat pose')
            return

        boat_x, boat_y, boat_yaw = boat
        while self.waypoint_index < len(self.active_path):
            goal_x, goal_y = self.active_path[self.waypoint_index]
            distance = math.hypot(goal_x - boat_x, goal_y - boat_y)
            radius = (
                self.final_arrival_radius
                if self.waypoint_index == len(self.active_path) - 1
                else self.waypoint_radius
            )
            if distance > radius:
                break
            self.waypoint_index += 1

        if self.waypoint_index >= len(self.active_path):
            self._complete_path()
            return

        goal_x, goal_y = self.active_path[self.waypoint_index]
        dx = goal_x - boat_x
        dy = goal_y - boat_y
        distance = math.hypot(dx, dy)
        yaw_error = wrap_pi(math.atan2(dy, dx) - boat_yaw)
        speed_scale = clamp(distance / self.slow_radius, 0.0, 1.0)
        linear_x = max(self.min_speed, self.max_speed * speed_scale)
        heading_scale = clamp(
            1.0 - abs(yaw_error) / self.heading_slowdown_yaw,
            0.12,
            1.0,
        )
        linear_x *= heading_scale
        angular_z = clamp(
            self.turn_gain * yaw_error,
            -self.max_turn,
            self.max_turn,
        )
        self._publish_boat_cmd(linear_x, angular_z)
        self._set_control_status(
            'tracking',
            'Direct heading controller',
        )

    def _update_nav2_control(self):
        if (
            not self.active_path
            or self.nav2_goal_pending
            or self.nav2_goal_handle is not None
        ):
            return
        if not self.nav2_client.server_is_ready():
            self._set_control_status('waiting_nav2', 'Waiting for NavigateToPose')
            return

        if self.waypoint_index >= len(self.active_path):
            self._complete_path()
            return

        goal_x, goal_y = self.active_path[self.waypoint_index]
        goal = NavigateToPose.Goal()
        goal.pose = PoseStamped()
        goal.pose.header.frame_id = 'map'
        goal.pose.header.stamp = self.get_clock().now().to_msg()
        goal.pose.pose.position.x = goal_x
        goal.pose.pose.position.y = goal_y
        goal.pose.pose.orientation.w = 1.0

        path_id = self.active_path_id
        waypoint_index = self.waypoint_index
        self.nav2_goal_pending = True
        future = self.nav2_client.send_goal_async(goal)
        future.add_done_callback(
            lambda result: self._on_nav2_goal_response(
                result,
                path_id,
                waypoint_index,
            )
        )
        self._set_control_status('tracking', 'Nav2 MPPI controller')

    def _on_nav2_goal_response(self, future, path_id, waypoint_index):
        self.nav2_goal_pending = False
        try:
            handle = future.result()
        except Exception as exc:
            self._fail_path(f'Nav2 goal failed: {exc}')
            return

        if path_id != self.active_path_id or waypoint_index != self.waypoint_index:
            if handle.accepted:
                handle.cancel_goal_async()
            return
        if not handle.accepted:
            self._fail_path('Nav2 rejected a Unity waypoint')
            return

        self.nav2_goal_handle = handle
        result_future = handle.get_result_async()
        result_future.add_done_callback(
            lambda result: self._on_nav2_result(
                result,
                path_id,
                waypoint_index,
            )
        )

    def _on_nav2_result(self, future, path_id, waypoint_index):
        if path_id != self.active_path_id or waypoint_index != self.waypoint_index:
            return
        self.nav2_goal_handle = None
        try:
            wrapped_result = future.result()
        except Exception as exc:
            self._fail_path(f'Nav2 result failed: {exc}')
            return
        if wrapped_result.status != GoalStatus.STATUS_SUCCEEDED:
            self._fail_path(f'Nav2 stopped with status {wrapped_result.status}')
            return

        self.waypoint_index += 1
        if self.waypoint_index >= len(self.active_path):
            self._complete_path()

    def _boat_snapshot(self):
        primary_usv = self.usv_names[0]
        with self.lock:
            if (
                self.last_boat_update is None
                or time.monotonic() - self.last_boat_update > self.pose_stale_timeout
                or primary_usv not in self.latest
            ):
                return None
            boat = self.latest[primary_usv]
            position = tuple(boat['position'])
            orientation = tuple(boat['orientation'])
        return position[0], position[1], yaw_from_quaternion(orientation)

    def _complete_path(self):
        completed_id = self.active_path_id
        self._publish_boat_cmd(0.0, 0.0)
        self.waypoint_index = len(self.active_path)
        self._set_control_status('complete', f'Path {completed_id} complete')
        self.get_logger().info(f'Unity path {completed_id} complete')
        self.active_path = []
        self.nav2_goal_handle = None
        self.nav2_goal_pending = False

    def _fail_path(self, message):
        self._publish_boat_cmd(0.0, 0.0)
        self.active_path = []
        self.nav2_goal_handle = None
        self.nav2_goal_pending = False
        self._set_control_status('error', message)
        self.get_logger().error(message)

    def _stop_boat(self, message, clear_path):
        if clear_path and not self.active_path and self.control_state == 'stopped':
            return
        self._cancel_nav2_goal()
        self._publish_boat_cmd(0.0, 0.0)
        if clear_path:
            self.active_path = []
            self.active_path_id = 0
            self.waypoint_index = 0
        self._set_control_status('stopped', message)
        self.get_logger().info(message)

    def _cancel_nav2_goal(self):
        if self.nav2_goal_handle is not None:
            try:
                self.nav2_goal_handle.cancel_goal_async()
            except Exception:
                pass
        self.nav2_goal_handle = None
        self.nav2_goal_pending = False

    def _publish_boat_cmd(self, linear_x, angular_z):
        if self.control_mode == 'observe':
            return
        message = Twist()
        message.linear.x = float(linear_x)
        message.angular.z = float(angular_z)
        self.boat_cmd_pub.publish(message)

    def _set_control_status(self, state, message):
        with self.lock:
            self.control_state = state
            self.control_message = message

    def _publish_frame(self):
        with self.lock:
            if (
                self.last_gazebo_update is None
                or time.monotonic() - self.last_gazebo_update > self.pose_stale_timeout
            ):
                return

            usvs = self._fleet_items(self.usv_names)
            uavs = self._fleet_items(self.uav_names)
            if (
                self.usv_names[0] not in self.latest
                or self.uav_names[0] not in self.latest
            ):
                return

            self.sequence += 1
            payload = {
                'schema_version': 2,
                'timestamp_ms': int(time.time() * 1000),
                'sequence': self.sequence,
                'fleet': {
                    'expected_usvs': len(self.usv_names),
                    'expected_uavs': len(self.uav_names),
                    'received_usvs': len(usvs),
                    'received_uavs': len(uavs),
                    'ready': (
                        len(usvs) == len(self.usv_names)
                        and len(uavs) == len(self.uav_names)
                    ),
                },
                'usvs': usvs,
                'uavs': uavs,
                # Keep the original single-vehicle fields for the current Unity client.
                'boat': self.latest[self.usv_names[0]],
                'drone': self.latest[self.uav_names[0]],
                'mission': {
                    'capture': dict(self.capture_state),
                    'roles': dict(self.capture_roles),
                },
                'control': {
                    'state': self.control_state,
                    'message': self.control_message,
                    'path_id': self.active_path_id,
                    'waypoint_index': self.waypoint_index,
                    'waypoint_count': len(self.active_path),
                    'mode': self.control_mode,
                },
            }
            for name in (
                'lighthouse',
                'buoy_west',
                'buoy_south',
                'buoy_east',
            ):
                if name in self.latest:
                    payload[name] = self.latest[name]
            if self.target_entity_name in self.latest:
                target = self.latest[self.target_entity_name]
                payload['target'] = {
                    'id': self.target_entity_name,
                    **target,
                }
                payload['target_vessel'] = target
            if self.friendly_ship_name in self.latest:
                friendly = self.latest[self.friendly_ship_name]
                payload['friendly_ship'] = {
                    'id': self.friendly_ship_name,
                    **friendly,
                }

        self._broadcast(json.dumps(payload, separators=(',', ':')))

    def _fleet_items(self, names):
        items = []
        for name in names:
            pose = self.latest.get(name)
            if pose is None:
                continue
            item = {
                'id': name,
                'position': list(pose['position']),
                'orientation': list(pose['orientation']),
            }
            state = self.vehicle_states.get(name)
            if state is not None:
                item['status'] = state
            items.append(item)
        return items

    def _broadcast(self, text):
        dead_clients = []

        with self.client_lock:
            clients = list(self.ws_clients)

        for client in clients:
            try:
                self._send_frame(client, text)
            except (ConnectionError, OSError):
                dead_clients.append(client)

        for client in dead_clients:
            self._remove_client(client)

    def destroy_node(self):
        self.running = False
        self._cancel_nav2_goal()
        self._publish_boat_cmd(0.0, 0.0)
        self.active_path = []
        if self.server_socket is not None:
            try:
                self.server_socket.close()
            except OSError:
                pass

        with self.client_lock:
            clients = list(self.ws_clients)

        for client in clients:
            self._remove_client(client)

        if self.nav2_client is not None:
            try:
                self.nav2_client.destroy()
            except Exception:
                pass

        super().destroy_node()


def main(args=None):
    rclpy.init(args=args)
    node = UnityWebSocketBridge()
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()

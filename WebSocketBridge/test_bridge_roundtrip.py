#!/usr/bin/env python3

import base64
import json
import math
import os
import socket
import struct
import time


HOST = os.environ.get('UNITY_BRIDGE_HOST', '127.0.0.1')
PORT = int(os.environ.get('UNITY_BRIDGE_PORT', '8765'))
PATH = os.environ.get('UNITY_BRIDGE_PATH', '/uav_usv')


def recv_exact(connection, count):
    chunks = []
    remaining = count
    while remaining:
        chunk = connection.recv(remaining)
        if not chunk:
            raise ConnectionError('WebSocket closed')
        chunks.append(chunk)
        remaining -= len(chunk)
    return b''.join(chunks)


def receive_text(connection):
    header = recv_exact(connection, 2)
    opcode = header[0] & 0x0F
    length = header[1] & 0x7F
    if length == 126:
        length = struct.unpack('!H', recv_exact(connection, 2))[0]
    elif length == 127:
        length = struct.unpack('!Q', recv_exact(connection, 8))[0]
    if header[1] & 0x80:
        raise ValueError('Server frames must not be masked')
    payload = recv_exact(connection, length)
    if opcode == 0x8:
        raise ConnectionError('WebSocket close frame received')
    if opcode != 0x1:
        return None
    return payload.decode('utf-8')


def send_json(connection, value):
    payload = json.dumps(value, separators=(',', ':')).encode('utf-8')
    mask = os.urandom(4)
    masked = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
    length = len(payload)
    if length < 126:
        header = bytes([0x81, 0x80 | length])
    elif length <= 0xFFFF:
        header = bytes([0x81, 0x80 | 126]) + struct.pack('!H', length)
    else:
        header = bytes([0x81, 0x80 | 127]) + struct.pack('!Q', length)
    connection.sendall(header + mask + masked)


def connect():
    connection = socket.create_connection((HOST, PORT), timeout=5.0)
    key = base64.b64encode(os.urandom(16)).decode('ascii')
    request = (
        f'GET {PATH} HTTP/1.1\r\n'
        f'Host: {HOST}:{PORT}\r\n'
        'Upgrade: websocket\r\n'
        'Connection: Upgrade\r\n'
        f'Sec-WebSocket-Key: {key}\r\n'
        'Sec-WebSocket-Version: 13\r\n'
        '\r\n'
    )
    connection.sendall(request.encode('ascii'))
    response = b''
    while b'\r\n\r\n' not in response:
        response += connection.recv(4096)
    if not response.startswith(b'HTTP/1.1 101'):
        raise ConnectionError(response.decode('utf-8', errors='replace'))
    connection.settimeout(3.0)
    return connection


def next_pose(connection):
    while True:
        text = receive_text(connection)
        if text is None:
            continue
        frame = json.loads(text)
        if 'boat' in frame:
            return frame


def main():
    connection = connect()
    try:
        initial = next_pose(connection)
        start_x, start_y, _ = initial['boat']['position']
        path_id = int(time.time() * 1000)
        send_json(
            connection,
            {
                'type': 'boat_path',
                'path_id': path_id,
                'points': [
                    {'x': start_x, 'y': start_y},
                    {'x': start_x + 4.0, 'y': start_y},
                    {'x': start_x + 8.0, 'y': start_y},
                ],
            },
        )

        maximum_distance = 0.0
        last_state = ''
        deadline = time.monotonic() + 25.0
        while time.monotonic() < deadline:
            frame = next_pose(connection)
            x, y, _ = frame['boat']['position']
            maximum_distance = max(
                maximum_distance,
                math.hypot(x - start_x, y - start_y),
            )
            control = frame.get('control', {})
            last_state = control.get('state', last_state)
            if last_state == 'complete' or maximum_distance >= 6.0:
                break

        send_json(
            connection,
            {
                'type': 'boat_stop',
                'command_id': int(time.time() * 1000),
            },
        )
        print(
            'bridge round-trip: start=(%.2f, %.2f), moved=%.2f m, state=%s'
            % (start_x, start_y, maximum_distance, last_state)
        )
        if maximum_distance < 1.5:
            raise RuntimeError('Gazebo boat did not move far enough')
    finally:
        connection.close()


if __name__ == '__main__':
    main()

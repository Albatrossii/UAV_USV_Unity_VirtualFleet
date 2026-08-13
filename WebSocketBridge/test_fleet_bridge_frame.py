#!/usr/bin/env python3

from test_bridge_roundtrip import connect
from test_bridge_roundtrip import next_pose
from test_bridge_roundtrip import send_json


EXPECTED_USVS = {'usv_01', 'usv_02', 'usv_03'}
EXPECTED_UAVS = {'uav_01', 'uav_02', 'uav_03'}


def main():
    connection = connect()
    try:
        frame = next_pose(connection)
        send_json(
            connection,
            {
                'type': 'boat_path',
                'path_id': 3,
                'points': [{'x': 0.0, 'y': 0.0}, {'x': 5.0, 'y': 0.0}],
            },
        )
        observed_frame = next_pose(connection)
    finally:
        connection.close()

    usv_ids = {vehicle['id'] for vehicle in frame.get('usvs', [])}
    uav_ids = {vehicle['id'] for vehicle in frame.get('uavs', [])}
    fleet = frame.get('fleet', {})

    if frame.get('schema_version') != 2:
        raise RuntimeError('bridge did not publish protocol schema version 2')
    if usv_ids != EXPECTED_USVS:
        raise RuntimeError(f'unexpected USV set: {sorted(usv_ids)}')
    if uav_ids != EXPECTED_UAVS:
        raise RuntimeError(f'unexpected UAV set: {sorted(uav_ids)}')
    if not fleet.get('ready'):
        raise RuntimeError(f'fleet frame is incomplete: {fleet}')
    if 'boat' not in frame or 'drone' not in frame:
        raise RuntimeError('legacy Unity boat/drone fields are missing')
    control = observed_frame.get('control', {})
    if control.get('mode') != 'observe' or control.get('waypoint_count') != 0:
        raise RuntimeError(f'observe mode accepted a Unity command: {control}')

    target = frame.get('target', {})
    friendly = frame.get('friendly_ship', {})
    print(
        'fleet bridge: schema=%s, USV=%s, UAV=%s, friendly=%s, target=%s, mode=%s'
        % (
            frame['schema_version'],
            ','.join(sorted(usv_ids)),
            ','.join(sorted(uav_ids)),
            friendly.get('id', 'not-yet-visible'),
            target.get('id', 'not-yet-visible'),
            control.get('mode', 'unknown'),
        )
    )


if __name__ == '__main__':
    main()

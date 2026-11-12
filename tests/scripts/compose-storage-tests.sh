#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

# Only renders Compose with synthetic values; no daemon, network or real .env.
"${PYTHON:-python3}" - <<'PY'
import json
import os
import subprocess

env = {k: v for k, v in os.environ.items() if not k.startswith(
    ('MEDIA_STORAGE_', 'MINIO_', 'POSTGRES_', 'JWT_', 'BOOTSTRAP_', 'COMPOSE_'))
    and k not in ('API_PORT', 'WEB_PORT', 'IMAGE_TAG')}
env.update(POSTGRES_PASSWORD='test-db-only', JWT_SIGNING_KEY='a' * 48,
           MEDIA_STORAGE_ENDPOINT='https://example.r2.cloudflarestorage.com',
           MEDIA_STORAGE_PUBLIC_ENDPOINT='https://example.r2.cloudflarestorage.com',
           MEDIA_STORAGE_BUCKET='english-center-files',
           MEDIA_STORAGE_ACCESS_KEY='test-access-only', MEDIA_STORAGE_SECRET_KEY='test-secret-only')


def render(files, values=None, env_file=None, success=True):
    command = ['docker', 'compose', '--env-file', env_file or os.devnull,
               '--project-name', 'ec-config-test']
    for file in files:
        command.extend(['-f', file])
    result = subprocess.run(command + ['config', '--format', 'json'],
                            env=env if values is None else values,
                            capture_output=True, text=True)
    assert (result.returncode == 0) == success, 'Unexpected Compose validation result'
    return json.loads(result.stdout) if success else None


def loopback(service):
    assert service['ports'], 'Expected a loopback port'
    assert all(port['host_ip'] == '127.0.0.1' for port in service['ports'])


base = render(['compose.yaml'])
assert set(base['services']) == {'api', 'postgres'}, 'Default deployment must not start MinIO'
assert set(base['volumes']) == {'postgres-data'}
assert base['volumes']['postgres-data']['name'] == 'ec-config-test_postgres-data'
api = base['services']['api']
storage = api['environment']
prefix = 'MediaNotification__Storage__'
for name in ('Endpoint', 'PublicEndpoint', 'Bucket', 'AccessKey', 'SecretKey'):
    key = 'MEDIA_STORAGE_' + {'PublicEndpoint': 'PUBLIC_ENDPOINT', 'AccessKey': 'ACCESS_KEY',
                              'SecretKey': 'SECRET_KEY'}.get(name, name.upper())
    assert storage[prefix + name] == env[key], name
assert storage[prefix + 'Region'] == 'auto'
assert storage[prefix + 'ForcePathStyle'].lower() == 'true'
assert set(api['depends_on']) == {'postgres'}
assert api['depends_on']['postgres']['condition'] == 'service_healthy'
assert not base['services']['postgres'].get('ports')
loopback(api)
assert any(v.get('source') == 'postgres-data' for v in base['services']['postgres']['volumes'])
print('PASS: default R2 services, credentials, region, ports and database volume')

for name in ('ENDPOINT', 'PUBLIC_ENDPOINT', 'BUCKET', 'ACCESS_KEY', 'SECRET_KEY'):
    for empty in (False, True):
        missing = dict(env)
        missing.pop('MEDIA_STORAGE_' + name)
        if empty:
            missing['MEDIA_STORAGE_' + name] = ''
        render(['compose.yaml'], missing, success=False)
print('PASS: missing or empty R2 settings fail closed')

web = render(['compose.yaml', 'compose.web.yaml'])
assert set(web['services']) == {'api', 'postgres', 'web'}
loopback(web['services']['web'])
render(['compose.yaml', 'compose.web.yaml'], env_file='.env.example')
print('PASS: R2 web overlay and environment example')

local_env = dict(env, MINIO_ROOT_USER='local-root-only', MINIO_ROOT_PASSWORD='local-root-secret')
local_env.update(MEDIA_STORAGE_PUBLIC_ENDPOINT='http://localhost:9000',
                 MEDIA_STORAGE_BUCKET='english-center-media', MEDIA_STORAGE_REGION='us-east-1')
local = render(['compose.minio.yaml'], local_env)
assert set(local['services']) == {'api', 'postgres', 'minio', 'minio-init'}
local_api = local['services']['api']
assert local_api['environment'][prefix + 'Endpoint'] == 'http://minio:9000'
assert local_api['environment'][prefix + 'PublicEndpoint'] == 'http://localhost:9000'
assert local_api['environment'][prefix + 'Region'] == 'us-east-1'
assert local_api['depends_on']['minio-init']['condition'] == 'service_completed_successfully'
assert 'MINIO_ROOT_PASSWORD' not in local_api['environment']
assert set(local['volumes']) == {'postgres-data', 'minio-data'}
assert not local['services']['postgres'].get('ports')
loopback(local_api)
loopback(local['services']['minio'])
render(['compose.minio.yaml', 'compose.web.yaml'], local_env, '.env.minio.example')
print('PASS: standalone local MinIO provisioning, ports, volumes and environment example')
PY

#!/bin/sh
set -eu

: "${MINIO_ROOT_USER:?Root user is required for provisioning}"
: "${MINIO_ROOT_PASSWORD:?Root password is required for provisioning}"
: "${MEDIA_STORAGE_ACCESS_KEY:?Separate app access key is required}"
: "${MEDIA_STORAGE_SECRET_KEY:?Separate app secret key is required}"
: "${MEDIA_STORAGE_BUCKET:?Bucket is required}"

# Validate before interpolating the bucket into a JSON policy.
case "$MEDIA_STORAGE_BUCKET" in
  ''|*[!a-z0-9.-]*) echo 'Invalid media bucket name' >&2; exit 1 ;;
esac
if [ "$MEDIA_STORAGE_ACCESS_KEY" = "$MINIO_ROOT_USER" ]; then
  echo 'The app must not use the MinIO root account' >&2
  exit 1
fi

export MC_CONFIG_DIR=/tmp/mc-config
trap 'rm -f /tmp/media-policy.json; rm -rf /tmp/mc-config' EXIT
attempt=0
until mc alias set local http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null 2>&1; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 60 ]; then echo 'MinIO did not become ready' >&2; exit 1; fi
  sleep 2
done

mc mb --ignore-existing "local/$MEDIA_STORAGE_BUCKET"
mc anonymous set none "local/$MEDIA_STORAGE_BUCKET"
cat > /tmp/media-policy.json <<EOF
{"Version":"2012-10-17","Statement":[
  {"Effect":"Allow","Action":["s3:GetBucketLocation","s3:ListBucket"],"Resource":["arn:aws:s3:::$MEDIA_STORAGE_BUCKET"]},
  {"Effect":"Allow","Action":["s3:GetObject","s3:PutObject","s3:DeleteObject"],"Resource":["arn:aws:s3:::$MEDIA_STORAGE_BUCKET/staging/*","arn:aws:s3:::$MEDIA_STORAGE_BUCKET/files/*"]}
]}
EOF
mc admin policy create local english-center-media /tmp/media-policy.json
mc admin user add local "$MEDIA_STORAGE_ACCESS_KEY" "$MEDIA_STORAGE_SECRET_KEY"
mc admin policy attach local english-center-media --user "$MEDIA_STORAGE_ACCESS_KEY"

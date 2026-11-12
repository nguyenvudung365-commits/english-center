# Official minio/mc container repositories were removed. Build the client from the
# signed RELEASE.2025-08-13T08-35-41Z commit, not a floating binary download.
FROM golang:1.26-alpine AS build
ENV CGO_ENABLED=0
RUN go install github.com/minio/mc@7394ce0dd2a80935aded936b09fa12cbb3cb8096

FROM alpine:3.22
RUN apk add --no-cache ca-certificates
COPY --from=build /go/bin/mc /usr/local/bin/mc
COPY deploy/minio-init.sh /usr/local/bin/minio-init.sh
ENTRYPOINT ["/bin/sh", "/usr/local/bin/minio-init.sh"]

#!/bin/bash

./check-arch.sh
set -e
arch=$(uname -m)
if [[ "$arch" == "arm64" || "$arch" == "aarch64" ]]; then
  image="ibm-mqadvanced-server-dev:9.3.3.0-arm64"
  if ! docker image inspect "$image" >/dev/null 2>&1; then
    echo "IBM MQ ARM64 image not found. Building it..."
    ./build-arm-mq-image.sh
  fi
fi

dotnet test Tix.IBMMQ.Bridge.IntegrationTests/Tix.IBMMQ.Bridge.IntegrationTests.csproj "$@"

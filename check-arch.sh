#!/bin/bash
arch=$(uname -m)
if [[ "$arch" == "arm64" || "$arch" == "aarch64" ]]; then
  echo "Running on ARM64 architecture ($arch)."
else
  echo "Running on $arch architecture."
fi

#!/bin/bash
set -e
repo_dir="mq-container"
if [ ! -d "$repo_dir" ]; then
  git clone https://github.com/ibm-messaging/mq-container.git "$repo_dir"
fi
cd "$repo_dir"
ARCH=arm64 make build-devserver

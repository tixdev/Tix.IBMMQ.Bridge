# Setup Guide

This document explains how to build, run and test the **Tix.IBMMQ.Bridge** project.

## Prerequisites

- .NET 8.0 SDK. On Ubuntu you can install it with:

  ```bash
  sudo apt-get install -y dotnet-sdk-8.0
  ```

- Access to one or more IBM MQ queue managers

## Build

```bash
dotnet build Tix.IBMMQ.Bridge.sln
```

## Run the service

Adjust `appsettings.json` to match your IBM MQ environments, then start the service:

```bash
dotnet run --project Tix.IBMMQ.Bridge
```

## Run the tests

Check the current architecture before running the tests:

```bash
./check-arch.sh
```

This script prints whether the machine is running on Apple Silicon/ARM64 or another architecture.

If it reports `arm64`, you can build a local IBM MQ developer image using the
[mq-container](https://github.com/ibm-messaging/mq-container) project:

```bash
git clone https://github.com/ibm-messaging/mq-container.git
cd mq-container
ARCH=arm64 make build-devserver
```

This creates an image tagged `ibm-mqadvanced-server-dev:9.3.3.0-arm64`.
The helper script `run-integration-tests.sh` will automatically build this image
if it's missing and then execute the tests. The script uses **Bash**, so on
macOS run it with `bash ./run-integration-tests.sh` if the default shell is not
Bash.

Execute unit tests with:

```bash
dotnet test Tix.IBMMQ.Bridge.Tests/Tix.IBMMQ.Bridge.Tests.csproj
```

Integration tests require Docker. The `run-integration-tests.sh` helper will
verify the architecture, build the ARM64 image when necessary and then invoke
`dotnet test`:

```bash
bash ./run-integration-tests.sh
```

> **Note**
> When running under Podman the Testcontainers "resource reaper" cannot start
> because Podman refuses to hijack the connection stream. The integration tests
> disable the reaper to avoid `cannot hijack chunked or content length stream`
> errors. You may need to manually remove containers after the tests complete.

# Setup Guide

This document explains how to build, run and test the **Tix.IBMMQ.Bridge** project.

## Prerequisites

- .NET 8.0 SDK
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

Execute unit tests with:

```bash
dotnet test Tix.IBMMQ.Bridge.Tests/Tix.IBMMQ.Bridge.Tests.csproj
```

Integration tests require Docker to be available to start a temporary IBM MQ
instance. Run them with:

```bash
dotnet test Tix.IBMMQ.Bridge.IntegrationTests/Tix.IBMMQ.Bridge.IntegrationTests.csproj
```

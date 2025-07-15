# Project Overview

The bridge hosts an ASP.NET Core background service that moves messages between
pairs of IBM MQ queues. Configuration is provided via `appsettings.json` and
bound to `MQBridgeOptions` at startup.

Each queue pair runs in its own background loop:
1. Connect to the inbound and outbound queue managers.
2. Read messages from the inbound queue under syncpoint.
3. Write the messages to the outbound queue.
4. Commit or roll back both queue managers depending on the outcome.

This approach ensures that messages are not lost if a failure occurs while
writing to the outbound queue.

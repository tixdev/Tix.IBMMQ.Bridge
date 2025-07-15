# Tix.IBMMQ.Bridge

This project is an ASP.NET Core 8.0 service that acts as a bridge between multiple IBM MQ servers using the managed client driver.

The bridge reads messages under *syncpoint* and only commits them after they have been forwarded. If an error occurs while writing to the outbound queue the read is rolled back, ensuring that no messages are lost.

## Configuration

Queue connections and queue pairs are configured in the `MQBridge` section of *appsettings.json*:

```json
{
  "MQBridge": {
    "Connections": {
      "ConnA": {
        "QueueManagerName": "QM1",
        "ConnectionName": "host1(1414)",
        "Channel": "SVRCONN.CHANNEL",
        "UserId": "user1",
        "Password": "pwd1"
      }
    },
    "QueuePairs": [
      {
        "InboundConnection": "ConnA",
        "InboundQueue": "IN.Q1",
        "OutboundConnection": "ConnB",
        "OutboundQueue": "OUT.Q1",
        "PollIntervalSeconds": 30
      }
    ]
  }
}
```

Run the service with `dotnet run`.

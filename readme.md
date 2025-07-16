# Tix.IBMMQ.Bridge

This project is an ASP.NET Core 8.0 service that acts as a bridge between multiple IBM MQ servers using the IBMMQDotnetClient library.

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
        "UserId": "user1",
        "Password": "pwd1"
      },
      "ConnB": {
        "QueueManagerName": "QM2",
        "ConnectionName": "host2(1414)",
        "UserId": "user2",
        "Password": "pwd2"
      }
    },
    "QueuePairs": [
      {
        "InboundConnection": "ConnA",
        "InboundChannel": "SVRCONN.CHANNEL",
        "InboundQueue": "IN.Q1",
        "OutboundConnection": "ConnB",
        "OutboundChannel": "SVRCONN.CHANNEL",
        "OutboundQueue": "OUT.Q1"
      },
      {
        "InboundConnection": "ConnB",
        "InboundChannel": "SVRCONN.CHANNEL",
        "InboundQueue": "IN.Q2",
        "OutboundConnection": "ConnA",
        "OutboundChannel": "SVRCONN.CHANNEL",
        "OutboundQueue": "OUT.Q2"
      }
    ]
  }
}
```

Run the service with `dotnet run`.

## Documentation

Additional usage and setup information can be found in the [docs](docs/) folder.

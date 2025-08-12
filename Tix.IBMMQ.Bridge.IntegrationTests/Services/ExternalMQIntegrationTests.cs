using IBM.WMQ;
using Shouldly;
using System.Collections;
using Xunit;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class ExternalMQIntegrationTests
{
    [Fact]
    public void Should_write_and_read_message_from_external_mq_server()
    {
        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, "6.tcp.eu.ngrok.io" },
            { MQC.PORT_PROPERTY, 15492 },
            { MQC.CHANNEL_PROPERTY, "DEV.APP.SVRCONN" },
            { MQC.USER_ID_PROPERTY, "app" },
            { MQC.PASSWORD_PROPERTY, "passw0rd" },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED }
        };

        const string queueName = "DEV.QUEUE.1";
        const string testMessage = "hello world from integration test";

        // Write the message
        using (var qMgr = new MQQueueManager("QM1", props))
        {
            using (var queue = qMgr.AccessQueue(queueName, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING))
            {
                var mqMessage = new MQMessage();
                mqMessage.WriteString(testMessage);
                queue.Put(mqMessage);
            }
        }

        // Read the message back
        using (var qMgr = new MQQueueManager("QM1", props))
        {
            using (var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING))
            {
                var receivedMessage = new MQMessage();
                queue.Get(receivedMessage, new MQGetMessageOptions());
                var messageText = receivedMessage.ReadString(receivedMessage.DataLength);
                messageText.ShouldBe(testMessage);
            }
        }
    }
}

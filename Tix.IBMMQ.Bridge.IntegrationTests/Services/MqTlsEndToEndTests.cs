using System.Collections;
using IBM.WMQ;
using Xunit;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MqTlsEndToEndTests
{
    [Fact]
    public void PutGet_Tls_Works()
    {
        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, "2.tcp.eu.ngrok.io" },
            { MQC.PORT_PROPERTY, 17083 },
            { MQC.CHANNEL_PROPERTY, "APP.TLS.SVRCONN" },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT },
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_RSA_WITH_AES_128_CBC_SHA256" }
        };

        using var qmgr = new MQQueueManager("QM1", props);

        using var queue = qmgr.AccessQueue(
            "DEV.QUEUE.1",
            MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_OUTPUT);

        var putMsg = new MQMessage();
        putMsg.WriteString("hello tls");
        queue.Put(putMsg, new MQPutMessageOptions());

        var got = new MQMessage();
        var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_WAIT, WaitInterval = 5000 };
        queue.Get(got, gmo);
        var body = got.ReadString(got.MessageLength);

        Assert.Equal("hello tls", body);
    }
}
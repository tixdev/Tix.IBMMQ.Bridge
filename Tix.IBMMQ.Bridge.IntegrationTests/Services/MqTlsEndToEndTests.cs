using System.Collections;
using IBM.WMQ;
using Xunit;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MqTlsEndToEndTests(MqTlsFixture fx, ITestOutputHelper logger) : IClassFixture<MqTlsFixture>
{
    [Fact(DisplayName = "Put/Get via TLS (managed .NET client)")]
    public void PutGet_Tls_Works()
    {
        var webPort = fx.Container.GetMappedPublicPort(9443);
        logger.WriteLine($"MQ Console → https://localhost:{webPort}/ibmmq/console/");
        
        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, fx.Host },
            { MQC.PORT_PROPERTY, fx.Port },
            { MQC.CHANNEL_PROPERTY, "APP.TLS.SVRCONN" },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT },
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_RSA_WITH_AES_128_CBC_SHA256" }
            //{ MQC.SSL_CERT_STORE_PROPERTY, "*USER" } // usa store CurrentUser
            // Per TLS 1.3 su client 9.4+ e QM abilitato: "ANY_TLS13_OR_HIGHER"
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
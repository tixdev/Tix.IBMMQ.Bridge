using System;
using System.Collections;
using System.IO;
using IBM.WMQ;
using Xunit;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MqTlsEndToEndTests
{
    [Fact]
    public void Should_forward_message_between_queues_with_tls()
    {
        var certPath = Path.Combine(AppContext.BaseDirectory, "ca.crt");
        
        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, "2.tcp.eu.ngrok.io" },
            { MQC.PORT_PROPERTY, 17083 },
            { MQC.CHANNEL_PROPERTY, "APP.TLS.SVRCONN" },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            
            //I'm not sure this is the way on linux, on other cases i need to create a store and and ca cert
            //{ MQC.SSL_CERT_STORE_PROPERTY, certPath },
            
            //I'm not sure if we need to use SSL_CIPHER_SUITE_PROPERTY or SSL_CIPHER_SPEC_PROPERTY
            //{ MQC.SSL_CIPHER_SUITE_PROPERTY, "TLS_RSA_WITH_AES_128_CBC_SHA256" }
            
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
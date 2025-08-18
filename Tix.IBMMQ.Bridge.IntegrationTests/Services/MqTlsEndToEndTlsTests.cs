using System;
using System.Collections;
using System.Diagnostics;
using IBM.WMQ;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MqTlsEndToEndTlsTests(MqTlsFixture fx, ITestOutputHelper logger) : IClassFixture<MqTlsFixture>
{
    [Fact]
    public void Should_forward_message_between_queues()
    {
        Environment.SetEnvironmentVariable("MQDOTNET_TRACE_ON", "0");
        
        var mqPort = fx.MqServer1.GetMappedPublicPort(1414);
        var mqHost = fx.Host;
        
        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, mqHost },
            { MQC.PORT_PROPERTY, mqPort },
            { MQC.CHANNEL_PROPERTY, "APP.TLS.SVRCONN" },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384" },
            { MQC.USER_ID_PROPERTY, "app" },
            { MQC.PASSWORD_PROPERTY, "passw0rd" }
        };

        var random = new Random();

        using var qmgr = new MQQueueManager("", props);
        
        for (int i = 0; i < 1000; i++)
        {
            var timer = Stopwatch.StartNew();

            var queueName = $"DEV.QUEUE.{random.Next(1, 1000)}";
            
            using var queue = qmgr.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_OUTPUT);
            
            var putMsg = new MQMessage();
            putMsg.WriteString("hello tls");
            queue.Put(putMsg, new MQPutMessageOptions());

            var got = new MQMessage();
            var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_WAIT, WaitInterval = 5000 };
            queue.Get(got, gmo);
            var body = got.ReadString(got.MessageLength);
            
            Assert.Equal("hello tls", body);
            
            timer.Stop();
            
            logger.WriteLine($"{i+1} - {queueName} {timer.ElapsedMilliseconds.ToString()}");
            
            timer.ElapsedMilliseconds.ShouldBeLessThanOrEqualTo(1000);
        }
    }
}
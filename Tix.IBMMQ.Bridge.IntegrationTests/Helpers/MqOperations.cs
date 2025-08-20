using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IBM.WMQ;
using Tix.IBMMQ.Bridge.Options;
using Tix.IBMMQ.Bridge.Services;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Helpers
{
    internal static class MqOperations
    {
        public static void DrainQueue(this ConnectionOptions conn, string channel, string queueName)
        {
            var props = BuildProperties(conn, channel);
            using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING);
            var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_NO_WAIT | MQC.MQGMO_ACCEPT_TRUNCATED_MSG };

            while (true)
            {
                try
                {
                    queue.Get(new MQMessage(), gmo);
                }
                catch (MQException ex) when (ex.Reason == MQC.MQRC_NO_MSG_AVAILABLE)
                {
                    break; // Queue is empty
                }
            }
        }

        private static Hashtable BuildProperties(ConnectionOptions opts, string channel)
        {
            var (host, port) = MQBridgeService.ParseConnectionName(opts.ConnectionName);
            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, host },
                { MQC.PORT_PROPERTY, port },
                { MQC.CHANNEL_PROPERTY, channel },
                { MQC.USER_ID_PROPERTY, opts.UserId },
                { MQC.PASSWORD_PROPERTY, opts.Password },
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED }
            };

            if (opts.UseTls)
                properties.Add(MQC.SSL_CIPHER_SPEC_PROPERTY, opts.SslCipherSpec);

            return properties;
        }

        public static void PutMessage(this ConnectionOptions conn, string channel, string queueName, string message)
        {
            var props = BuildProperties(conn, channel);
            using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);
            var mqMessage = new MQMessage();
            mqMessage.WriteString(message);
            queue.Put(mqMessage); // Not using syncpoint for test setup simplicity
        }

        public static int GetQueueDepth(this ConnectionOptions conn, string channel, string queueName)
        {
            var props = BuildProperties(conn, channel);
            using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING);
            return queue.CurrentDepth;
        }

        public static int GetQueueMaxDepth(this ConnectionOptions conn, string channel, string queueName)
        {
            var props = BuildProperties(conn, channel);
            using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING);
            return queue.MaximumDepth;
        }

        public static int GetQueueMaxMessageAvailableLength(this ConnectionOptions conn, string channel, string queueName)
        {
            var props = BuildProperties(conn, channel);
            using var qMgr = new MQQueueManager(conn.QueueManagerName, props);
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING);
            // Remove 128 bytes for message metadata overhead
            return queue.MaximumMessageLength - 128;
        }
    }
}

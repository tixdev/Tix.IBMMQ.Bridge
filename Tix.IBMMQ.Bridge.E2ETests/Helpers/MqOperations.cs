using System;
using System.Collections;
using System.Text;
using IBM.WMQ;
using Tix.IBMMQ.Bridge.Options;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers
{
    public class MqOperations
    {
        private static byte[] ToCorrelationId(string correlationId)
        {
            var corrBytes = Encoding.UTF8.GetBytes(correlationId ?? string.Empty);
            var corrId = new byte[24];
            Array.Clear(corrId, 0, corrId.Length);
            Array.Copy(corrBytes, corrId, Math.Min(corrBytes.Length, corrId.Length));
            return corrId;
        }
        private readonly ConnectionOptions _connectionOptions;

        public MqOperations(ConnectionOptions connectionOptions)
        {
            _connectionOptions = connectionOptions;
        }

        private MQQueueManager CreateQueueManager(string channel)
        {
            var host = _connectionOptions.ConnectionName.Split('(')[0]
                // Hack: normalize host for Ibm mq server used in container
                .Replace("host.docker.internal", "localhost");
            var port = int.Parse(_connectionOptions.ConnectionName.Split('(')[1].TrimEnd(')'));
            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, host },
                { MQC.PORT_PROPERTY, port },
                { MQC.CHANNEL_PROPERTY, channel },
                { MQC.USER_ID_PROPERTY, _connectionOptions.UserId },
                { MQC.PASSWORD_PROPERTY, _connectionOptions.Password }
            };

            if (_connectionOptions.UseTls)
                properties.Add(MQC.SSL_CIPHER_SPEC_PROPERTY, _connectionOptions.SslCipherSpec);

            return new MQQueueManager(_connectionOptions.QueueManagerName, properties);
        }

        public bool IsReachable(string channel)
        {
            try
            {
                using var qMgr = CreateQueueManager(channel);
                qMgr.Disconnect();
                return true;
            }
            catch (MQException ex)
            {
                Console.WriteLine($"MQException: {ex.ReasonCode} - {ex.Message}");
                return false;
            }
        }

        public void PutMessage(string channel, string queueName, string messageText, string correlationId = null)
        {
            using var qMgr = CreateQueueManager(channel);
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);

            var message = new MQMessage();
            message.WriteString(messageText);
            if (correlationId != null)
            {
                message.CorrelationId = ToCorrelationId(correlationId);
            }

            queue.Put(message);
        }

        public string GetMessage(string channel, string queueName, int timeoutMs, string correlationId = null)
        {
            using var qMgr = CreateQueueManager(channel);
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING);

            var message = new MQMessage();
            if (correlationId != null)
            {
                message.CorrelationId = ToCorrelationId(correlationId);
            }

            var gmo = new MQGetMessageOptions();
            gmo.Options = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING;
            gmo.WaitInterval = timeoutMs;

            try
            {
                queue.Get(message, gmo);
                return message.ReadString(message.MessageLength);
            }
            catch (MQException e) when (e.Reason == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                return null;
            }
        }
    }
}

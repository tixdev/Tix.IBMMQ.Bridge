using System;
using System.Collections;
using System.Text;
using System.Threading;
using IBM.WMQ;
using Tix.IBMMQ.Bridge.Options;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers
{
    public class MqE2EOperations
    {
        private readonly ConnectionOptions _connectionOptions;

        public MqE2EOperations(ConnectionOptions connectionOptions)
        {
            _connectionOptions = connectionOptions;
        }

        private MQQueueManager CreateQueueManager()
        {
            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, _connectionOptions.ConnectionName.Split('(')[0] },
                { MQC.PORT_PROPERTY, int.Parse(_connectionOptions.ConnectionName.Split('(')[1].TrimEnd(')')) },
                { MQC.USER_ID_PROPERTY, _connectionOptions.UserId },
                { MQC.PASSWORD_PROPERTY, _connectionOptions.Password }
            };
            return new MQQueueManager(_connectionOptions.QueueManagerName, properties);
        }

        public bool IsReachable(string channel)
        {
            try
            {
                using var qMgr = CreateQueueManager();
                qMgr.Disconnect();
                return true;
            }
            catch (MQException)
            {
                return false;
            }
        }

        public void PutMessage(string channel, string queueName, string messageText, string correlationId = null)
        {
            using var qMgr = CreateQueueManager();
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING);

            var message = new MQMessage();
            message.WriteString(messageText);
            if (correlationId != null)
            {
                message.CorrelationId = Encoding.UTF8.GetBytes(correlationId);
            }

            queue.Put(message);
        }

        public string GetMessage(string channel, string queueName, string correlationId, int timeoutSeconds)
        {
            using var qMgr = CreateQueueManager();
            using var queue = qMgr.AccessQueue(queueName, MQC.MQOO_INPUT | MQC.MQOO_FAIL_IF_QUIESCING);

            var message = new MQMessage();
            if (correlationId != null)
            {
                message.CorrelationId = Encoding.UTF8.GetBytes(correlationId);
            }

            var gmo = new MQGetMessageOptions();
            gmo.Options = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING;
            gmo.WaitInterval = timeoutSeconds * 1000;

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

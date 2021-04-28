using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using BreadTh.Bunder.Core;
using BreadTh.StronglyApied;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BreadTh.Bunder
{
    public class BunderQueue<TMessage> where TMessage : class
    {
        private readonly BunderNames _bunderNames;
        private readonly BunderMultiplexer _connection;
        private readonly string _processorName;
        
        public BunderQueue(string queueName, BunderMultiplexer connection, string processorName)
        {
            _bunderNames = new BunderNames(queueName);
            _connection = connection;
            _processorName = processorName;
        }

        public void Declare()
        {
            using var channel = _connection.CreateChannelOrThrow();

            void QueueDeclare(string queueName, IDictionary<string, object> args = null) =>
                channel.QueueDeclare(queueName, true, false, false, args);

            void ExchangeDeclare(string exchangeName) =>
                channel.ExchangeDeclare(exchangeName, ExchangeType.Fanout, true);

            void QueueBind(string queueName, string exchangeName) =>
                channel.QueueBind(queueName, exchangeName, "");

            void ExchangeBind(string toExchangeName, string fromExchangeName) =>
                channel.ExchangeBind(toExchangeName, fromExchangeName, "");


            //Declare the core queue itself
            ExchangeDeclare(_bunderNames.New);
            QueueDeclare(_bunderNames.Ready);
            QueueBind(_bunderNames.Ready, _bunderNames.New);
            
            
            //reject
            ExchangeDeclare(_bunderNames.Reject);
            QueueDeclare(_bunderNames.Rejected);
            QueueBind(_bunderNames.Rejected, _bunderNames.Reject);
            ExchangeDeclare(_bunderNames.Revive);
            QueueBind(_bunderNames.Ready, _bunderNames.Revive);
            //Don't actually connect revive with rejected. That's done with a manual shovel, when whatever caused the reject is fixed.


            //Timeout and retry
            ExchangeDeclare(_bunderNames.Retry);
            channel.ExchangeDeclare(_bunderNames.TimedOut, "x-delayed-message", true, false,
                new Dictionary<string, object>{{"x-delayed-type", "fanout"}});
            channel.ExchangeBind(_bunderNames.TimedOut, _bunderNames.Retry, "");
            QueueBind(_bunderNames.Ready, _bunderNames.TimedOut);


            //Logging components
            ExchangeDeclare(_bunderNames.Complete);
            ExchangeDeclare(_bunderNames.Log);

            ExchangeDeclare(_bunderNames.SharedLog);
            ExchangeDeclare(_bunderNames.SharedNew);
            ExchangeDeclare(_bunderNames.SharedRetry);
            ExchangeDeclare(_bunderNames.SharedResume);
            ExchangeDeclare(_bunderNames.SharedReject);
            ExchangeDeclare(_bunderNames.SharedRevive);
            ExchangeDeclare(_bunderNames.SharedCompleted);


            //Logging binding
            ExchangeBind(_bunderNames.SharedNew, _bunderNames.New);
            ExchangeBind(_bunderNames.SharedRetry, _bunderNames.Retry);
            ExchangeBind(_bunderNames.SharedResume, _bunderNames.TimedOut);
            ExchangeBind(_bunderNames.SharedReject, _bunderNames.Reject);
            ExchangeBind(_bunderNames.SharedRevive, _bunderNames.Revive);
            ExchangeBind(_bunderNames.SharedCompleted, _bunderNames.Complete);
            ExchangeBind(_bunderNames.SharedLog, _bunderNames.Log);
        }

        public EnqueueOutcome Enqueue(TMessage message, string traceId)
        {
            var now = DateTime.Now;
            Envelope<TMessage> envelope = new()
            {   letter = message
            ,   traceId = ExtendTraceId(traceId)
            ,   history = new EnvelopeHistory
                {   retryCounter = 0
                ,   enqueueTime = new EnqueueTime
                    {   original = now
                    ,   latest = now
                    }
                }
            ,   status = new EnvelopeStatus
                {   value = "new"
                ,   reasonForLatestChange = "Enqueued"
                ,   updatedBy = _processorName
                }
            };
            
            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));

            using var channel = _connection.CreateChannelOrThrow();
            channel.ConfirmSelect();    

            var props = channel.CreateBasicProperties();
            props.DeliveryMode = 2;
            props.ContentType = "text/plain";
           
            channel.BasicPublish(_bunderNames.New, "", props, messageBody);
            
            if (channel.WaitForConfirms())
                return new PublishSuccess();
            
            return new PublishFailure("message was never confirmed");
        }

        public void Log(object message, string traceId, string type = null)
        {
            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {   type = type is null ? "bunder:log" : "bunder:log:" + type,
                bunderName = _bunderNames.DisplayName,
                traceId = ExtendTraceId(traceId),
                message = message
            }));

            using var channel = _connection.CreateChannelOrThrow();
            channel.ConfirmSelect();

            var props = channel.CreateBasicProperties();
            props.DeliveryMode = 2;
            props.ContentType = "text/plain";

            channel.BasicPublish(_bunderNames.Log, "", props, messageBody);
            var abc = channel.WaitForConfirms();
            //Even though we want to ensure that the message is queued before we dispose the channel,
            //we don't want to use the outcome as the program should be unaffected by logging failures.
            //So we don't use the ack/nack and we just return void.
        }

        private string ExtendTraceId(string traceId) =>
            (string.IsNullOrEmpty(traceId) ? "noExternalId" : traceId) + ":" + Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=');

        public void AddListener(Func<Envelope<TMessage>, Task<ConsumptionOutcome>> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            //Don't dispose - The channel needs to stay open for the listener to remain active.
            var channel = _connection.CreateChannelOrThrow();

            EventingBasicConsumer consumer = new(channel);

            consumer.Received += async (_, args) =>
            {
                if (args is null)
                    throw new ArgumentNullException(nameof(args));

                var (envelope, errors) =
                    new ModelValidator().Parse<Envelope<TMessage>>(Encoding.UTF8.GetString(args.Body.ToArray()));

                if (errors.Count != 0)
                {
                    Reject(channel, args.DeliveryTag, envelope
                        , $"Model validation generated errors: {JsonConvert.SerializeObject(errors)}");
                    return;
                }

                ConsumptionOutcome handleMessageOutcome;
                try
                {
                    handleMessageOutcome = await handler(envelope);
                }
                catch (Exception e)
                {
                    Reject(channel, args.DeliveryTag, envelope, $"Listener threw exception: {e}");
                    return;
                }

                //Note: We must (n)ack a message on the same channel as it was received on, so we need to pass in the channel.
                //Though we can't send new messages on that same channel.
                handleMessageOutcome.Switch(
                    (ConsumptionSuccess success) => Success(channel, args.DeliveryTag, envelope, success),
                    (ConsumptionRetry retry) => Retry(channel, args.DeliveryTag, envelope, retry),
                    (ConsumptionReject rejection) => Reject(channel, args.DeliveryTag, envelope, rejection.Reason));
            };

            channel.BasicConsume(queue: _bunderNames.Ready, autoAck: false, consumer: consumer);
        }

        private void Success(IModel originChannel, ulong deliveryTag, Envelope<TMessage> envelope, ConsumptionSuccess success)
        {
            originChannel.BasicAck(deliveryTag, false);

            using var sendingChannel = _connection.CreateChannelOrThrow();

            var props = sendingChannel.CreateBasicProperties();
            props.DeliveryMode = 2;

            envelope.history.enqueueTime.latest = DateTime.Now;
            envelope.status.value = "completed";
            envelope.status.reasonForLatestChange = success.Reason ?? "[No reason given]";
            envelope.status.updatedBy = _processorName;

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_bunderNames.Complete, "", props, messageBytes);
            //waiting for ack does not make any sense here as we would do the same regardless if we got ack or nack.
            //That is to just carry on. 
        }

        private void Retry(IModel originChannel, ulong deliveryTag, Envelope<TMessage> envelope, ConsumptionRetry retry)
        {
            using var sendingChannel = _connection.CreateChannelOrThrow();
            sendingChannel.ConfirmSelect();

            var props = sendingChannel.CreateBasicProperties();
            props.DeliveryMode = 2;

            props.Headers = new Dictionary<string, object>
            {
                {"x-delay", retry.Delay.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}
            };

            envelope.history.retryCounter++;
            envelope.history.enqueueTime.latest = DateTime.Now;
            envelope.status.value = "retry";
            envelope.status.reasonForLatestChange = retry.Reason ?? "[no reason given]";
            envelope.status.updatedBy = _processorName;

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_bunderNames.Retry, "", props, messageBytes);

            if (sendingChannel.WaitForConfirms())
                originChannel.BasicAck(deliveryTag, false);
            else
                //If the message couldn't be put in the timeout exchange, the next best thing we can
                //do is to put it back where we got it - in the ready queue - even if that's not ideal.
                originChannel.BasicNack(deliveryTag, false, true);
        }

        private void Reject(IModel originChannel, ulong deliveryTag, Envelope<TMessage> envelope, string reason)
        {
            using var sendingChannel = _connection.CreateChannelOrThrow();
            sendingChannel.ConfirmSelect();

            var props = sendingChannel.CreateBasicProperties();
            props.DeliveryMode = 2;

            envelope.status.value = "reject";
            envelope.status.reasonForLatestChange = reason ?? "[no reason given]";
            envelope.status.updatedBy = _processorName;
            envelope.history.enqueueTime.latest = DateTime.Now;

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_bunderNames.Reject, "", props, messageBytes);

            if (sendingChannel.WaitForConfirms())
                originChannel.BasicAck(deliveryTag, false);
            else
                //If the message couldn't be put in the reject exchange, the next best thing we can
                //do is to nack it without requeue which ultimately also puts it in the deadletter queue
                //but through the expiration exchange, which is not what we wanted to do.
                //Still an acceptable fallback.
                originChannel.BasicNack(deliveryTag, false, false);
        }
    }
}
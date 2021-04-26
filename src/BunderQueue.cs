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
        private readonly BunderNames _names;
        private readonly BunderMultiplexer _connection;
        
        public BunderQueue(string name, BunderMultiplexer connection)
        {
            _names = new BunderNames(name);
            _connection = connection;
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


            //Declare the core queue itself
            ExchangeDeclare(_names.New);
            QueueDeclare(_names.Ready);
            QueueBind(_names.Ready, _names.New);
            
            
            //reject
            ExchangeDeclare(_names.Reject);
            QueueDeclare(_names.Rejected);
            QueueBind(_names.Rejected, _names.Reject);
            ExchangeDeclare(_names.Revive);
            QueueBind(_names.Ready, _names.Revive);
            //Don't actually connect revive with rejected. That's done with a manual shovel.


            //Timeout and retry
            ExchangeDeclare(_names.DelayedRetry);
            channel.ExchangeDeclare(_names.Delay, "x-delayed-message", true, false,
                new Dictionary<string, object>{{"x-delayed-type", "fanout"}});
            channel.ExchangeBind(_names.Delay, _names.DelayedRetry, "");
            QueueBind(_names.Ready, _names.Delay);


            //Logging components
            QueueDeclare(_names.Log);
            ExchangeDeclare(_names.Complete);
            ExchangeDeclare(_names.DirectToLog);


            //log binding
            QueueBind(_names.Log, _names.Complete);
            QueueBind(_names.Log, _names.DirectToLog);
            QueueBind(_names.Log, _names.Reject);
            QueueBind(_names.Log, _names.DelayedRetry);
            QueueBind(_names.Log, _names.Delay);
            QueueBind(_names.Log, _names.New);
            QueueBind(_names.Log, _names.Revive);
        }

        public void Undeclare()
        {
            using var channel = _connection.CreateChannelOrThrow();
            
            channel.ExchangeDelete(_names.New);
            channel.ExchangeDelete(_names.DelayedRetry);
            channel.ExchangeDelete(_names.Delay);
            channel.ExchangeDelete(_names.DirectToLog);
            channel.ExchangeDelete(_names.Complete);
            channel.ExchangeDelete(_names.Reject);
            channel.ExchangeDelete(_names.Revive);

            channel.QueueDelete(_names.Ready);
            channel.QueueDelete(_names.Rejected);

            //Don't delete _names.Log as it is shared.
        }
       
        public EnqueueOutcome Enqueue(TMessage message, string traceId)
        {
            var now = DateTime.UtcNow;
            Envelope<TMessage> envelope = new()
            {   letter = message
            ,   traceId = ExtendTraceId(traceId)
            ,   history = new MessageHistory
                {   retryCounter = 0
                ,   status = "new"
                ,   reasonForLatestStatusChange = "Enqueued"
                ,   enqueueTime = new EnqueueTime
                    {   original = now
                    ,   latest = now
                    }
                }
            };
            
            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));

            using var channel = _connection.CreateChannelOrThrow();
            channel.ConfirmSelect();    

            var props = channel.CreateBasicProperties();
            props.DeliveryMode = 2;
            props.ContentType = "text/plain";
           
            channel.BasicPublish(_names.New, "", props, messageBody);
            
            if (channel.WaitForConfirms())
                return new PublishSuccess();
            
            return new PublishFailure("message was never confirmed");
        }

        public void Log(object message, string traceId, string type = null)
        {
            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {   type = type is null ? "bunder:log" : "bunder:log:" + type,
                bunderName = _names.DisplayName,
                traceId = ExtendTraceId(traceId),
                message = message
            }));

            using var channel = _connection.CreateChannelOrThrow();
            channel.ConfirmSelect();

            var props = channel.CreateBasicProperties();
            props.DeliveryMode = 2;
            props.ContentType = "text/plain";

            channel.BasicPublish(_names.DirectToLog, "", props, messageBody);
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

            channel.BasicConsume(queue: _names.Ready, autoAck: false, consumer: consumer);
        }

        private void Success(IModel originChannel, ulong deliveryTag, Envelope<TMessage> envelope, ConsumptionSuccess success)
        {
            originChannel.BasicAck(deliveryTag, false);

            using var sendingChannel = _connection.CreateChannelOrThrow();

            var props = sendingChannel.CreateBasicProperties();
            props.DeliveryMode = 2;

            envelope.history.enqueueTime.latest = DateTime.UtcNow;
            envelope.history.status = "completed";
            envelope.history.reasonForLatestStatusChange = success.Reason ?? "[No reason given]";

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_names.Complete, "", props, messageBytes);
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
            envelope.history.status = "retry";
            envelope.history.reasonForLatestStatusChange = retry.Reason ?? "[no reason given]";
            envelope.history.enqueueTime.latest = DateTime.UtcNow;

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_names.DelayedRetry, "", props, messageBytes);

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

            envelope.history.status = "reject";
            envelope.history.reasonForLatestStatusChange = reason ?? "";
            envelope.history.enqueueTime.latest = DateTime.UtcNow;

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_names.Reject, "", props, messageBytes);

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
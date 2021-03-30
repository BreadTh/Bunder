using BreadTh.StronglyApied;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using BreadTh.Bunder.Core;

namespace BreadTh.Bunder.Core
{
    internal class Subscriber<TMessage> where TMessage : class
    {
        private readonly BunderNames _names;
        private readonly BunderMultiplexer _bunder;

        internal Subscriber(BunderMultiplexer bunder, BunderNames names)
        {
            _bunder = bunder;
            _names = names;
        }

        internal void AddListener(Func<Envelope<TMessage>, Task<ConsumptionOutcome>> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));
            
            //Don't dispose - The channel needs to stay open for the listener to remain active.
            var channel = _bunder.CreateChannelOrThrow();
            
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

            using var sendingChannel = _bunder.CreateChannelOrThrow();
            
            var props = sendingChannel.CreateBasicProperties();
            props.DeliveryMode = 2;

            envelope.history.enqueueTime.latest = DateTime.UtcNow;
            envelope.history.reasonForLatestStatusChange = success.Reason ?? "";

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_names.Complete, "", props, messageBytes);
            //waiting for ack does not make any sense here as we would do the same regardless if we got ack or nack.
            //That is to just carry on. 
        }

        private void Retry(IModel originChannel, ulong deliveryTag, Envelope<TMessage> envelope, ConsumptionRetry retry)
        {
            using var sendingChannel = _bunder.CreateChannelOrThrow();
            sendingChannel.ConfirmSelect();
            
            var props = sendingChannel.CreateBasicProperties();
            props.DeliveryMode = 2;

            props.Headers = new Dictionary<string, object>
            {
                {"x-delay", retry.Delay.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}
            };

            envelope.history.retryCounter++;
            envelope.history.reasonForLatestStatusChange = retry.Reason ?? "";
            envelope.history.enqueueTime.latest = DateTime.UtcNow;

            var messageBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            sendingChannel.BasicPublish(_names.DelayedRetry, "", props, messageBytes);

            if(sendingChannel.WaitForConfirms())
                originChannel.BasicAck(deliveryTag, false);
            else
                //If the message couldn't be put in the timeout exchange, the next best thing we can
                //do is to put it back where we got it - in the ready queue - even if that's not ideal.
                originChannel.BasicNack(deliveryTag, false, true);
        }

        private void Reject(IModel originChannel, ulong deliveryTag, Envelope<TMessage> envelope, string reason)
        {
            using var sendingChannel = _bunder.CreateChannelOrThrow();
            sendingChannel.ConfirmSelect();

            var props = sendingChannel.CreateBasicProperties();
            props.DeliveryMode = 2;
            
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

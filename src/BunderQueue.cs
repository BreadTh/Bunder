using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BreadTh.Bunder.Core;
using RabbitMQ.Client;

namespace BreadTh.Bunder
{
    public class BunderQueue<TMessage> where TMessage : class
    {
        private readonly BunderNames _names;
        private readonly BunderMultiplexer _connection;
        private readonly Subscriber<TMessage> _subscriber;
        private readonly Publisher<TMessage> _publisher;
        
        public BunderQueue(string name, BunderMultiplexer connection)
        {
            _names = new BunderNames(name);
            _connection = connection ?? new BunderMultiplexer();
            _publisher = new Publisher<TMessage>(_connection, _names);
            _subscriber = new Subscriber<TMessage>(_connection, _names);
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


            //Timeout and retry queue
            ExchangeDeclare(_names.DelayedRetry);
            channel.ExchangeDeclare(_names.Delay, "x-delayed-message", true, false,
                new Dictionary<string, object>{{"x-delayed-type", "fanout"}});
            channel.ExchangeBind(_names.Delay, _names.DelayedRetry, "");
            QueueBind(_names.Ready, _names.Delay);


            //log copies
            ExchangeDeclare(_names.Complete);
            QueueDeclare(_names.Log);
            QueueBind(_names.Log, _names.Complete);
            
            QueueDeclare(_names.Log);
            QueueBind(_names.Log, _names.Reject);

            QueueDeclare(_names.Log);
            QueueBind(_names.Log, _names.DelayedRetry);

            QueueDeclare(_names.Log);
            QueueBind(_names.Log, _names.Delay);

            QueueDeclare(_names.Log);
            QueueBind(_names.Log, _names.New);

            QueueDeclare(_names.Log);
            QueueBind(_names.Log, _names.Revive);
        }

        public void Undeclare()
        {
            using var channel = _connection.CreateChannelOrThrow();
            
            channel.ExchangeDelete(_names.New);
            channel.ExchangeDelete(_names.DelayedRetry);
            channel.ExchangeDelete(_names.Delay);
            channel.ExchangeDelete(_names.Complete);
            channel.ExchangeDelete(_names.Reject);
            channel.ExchangeDelete(_names.Revive);

            channel.QueueDelete(_names.Ready);
            channel.QueueDelete(_names.Rejected);
            channel.QueueDelete(_names.Log);
        }

        public EnqueueOutcome Enqueue(TMessage message, string traceId = null) =>
            _publisher.Enqueue(message, traceId);

        public void AddListener(Func<Envelope<TMessage>, Task<ConsumptionOutcome>> listener) =>
            _subscriber.AddListener(listener);
    }
}
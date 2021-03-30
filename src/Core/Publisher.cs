using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System;
using System.Text;

namespace BreadTh.Bunder.Core
{
    internal class Publisher<TMessage> where TMessage : class
    {
        private readonly BunderMultiplexer _bunderMultiplexer;
        private readonly BunderNames _names;
        
        internal Publisher(BunderMultiplexer bunderMultiplexer, BunderNames names)
        {
            _bunderMultiplexer = bunderMultiplexer;
            _names = names;
        }

        internal EnqueueOutcome Enqueue(TMessage message, string traceId = null)
        {
            traceId ??= Guid.NewGuid().ToString();

            var now = DateTime.UtcNow;
            Envelope<TMessage> envelope = new()
            {   letter = message
            ,   traceId = traceId
            ,   history = new MessageHistory
                {   retryCounter = 0
                ,   reasonForLatestStatusChange = "new"
                ,   enqueueTime = new EnqueueTime
                    {   original = now
                    ,   latest = now 
                    }
                }
            };
            
            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));

            using var channel = _bunderMultiplexer.CreateChannelOrThrow();
            channel.ConfirmSelect();

            var props = channel.CreateBasicProperties();
            props.DeliveryMode = 2;
            props.ContentType = "text/plain";
           
            channel.BasicPublish(_names.New, "", props, messageBody);
            
            if (channel.WaitForConfirms())
                return new PublishSuccess();
            
            
            return new PublishFailure("message was never confirmed");
        }
    }
}

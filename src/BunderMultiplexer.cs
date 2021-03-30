using System;
using RabbitMQ.Client;

namespace BreadTh.Bunder
{
    public class BunderMultiplexer
    {
        private readonly ConnectionFactory _connectionFactory;

        private IConnection _connection;

        public BunderMultiplexer(ConnectionFactory connectionFactory = null, bool enableAutomaticRecovery = true)
        {
            connectionFactory ??= new ConnectionFactory { HostName = "localhost" };

            if (enableAutomaticRecovery)
            {
                connectionFactory.AutomaticRecoveryEnabled = true;
                connectionFactory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);
            }

            _connectionFactory = connectionFactory;
        }

        public IModel CreateChannelOrThrow()
        {
            if (_connection is null || !_connection.IsOpen)
                _connection = _connectionFactory.CreateConnection();

            return _connection.CreateModel();
        }
    }
}
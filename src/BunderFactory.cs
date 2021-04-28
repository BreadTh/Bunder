namespace BreadTh.Bunder
{
    public class BunderFactory
    {
        private readonly BunderMultiplexer _connection;
        private readonly string _processorName;

        public BunderFactory(BunderMultiplexer connection, string processorName)
        {
            _connection = connection;
            _processorName = processorName;
        }

        public BunderQueue<T> Create<T>(string queueName) where T : class =>
            new (queueName, _connection, _processorName);

    }
}

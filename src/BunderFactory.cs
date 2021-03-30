namespace BreadTh.Bunder
{
    public class BunderFactory
    {
        private readonly BunderMultiplexer _connection;
        public BunderFactory(BunderMultiplexer connection)
        {
            _connection = connection;
        }

        public BunderQueue<T> Create<T>(string name) where T : class =>
            new (name, _connection);

    }
}

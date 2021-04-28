using BreadTh.StronglyApied.Attributes;

namespace BreadTh.Bunder
{
    [StronglyApiedRoot(DataModel.Json)]
    public class Envelope<T>
    {
        [StronglyApiedString]
        public string type = "bunder:asyncEnvelope";

        [StronglyApiedString]
        public string queue;

        [StronglyApiedString()]
        public string traceId;

        [StronglyApiedObject()]
        public T letter;
       
        [StronglyApiedObject()]
        public EnvelopeHistory history;

        [StronglyApiedObject()]
        public EnvelopeStatus status;
    }
}

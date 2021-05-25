using BreadTh.StronglyApied.Attributes;

namespace BreadTh.Bunder
{
    public class Envelope<T>
    {
        public string type = "bunder:asyncEnvelope";
        public string queue;
        public string traceId;
        public T letter;
        public EnvelopeHistory history;
        public EnvelopeStatus status;
    }
}

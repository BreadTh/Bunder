using BreadTh.StronglyApied.Attributes;

namespace BreadTh.Bunder.Core
{
    [StronglyApiedRoot(DataModel.Json)]
    public class StringMessageEnvelope
    {
        [StronglyApiedString]
        public string traceId;

        [StronglyApiedString]
        public string message;

        [StronglyApiedObject]
        public EnvelopeHistory history;
    }
}
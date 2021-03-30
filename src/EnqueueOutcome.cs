using OneOf;

namespace BreadTh.Bunder
{
    public class EnqueueOutcome : OneOfBase<PublishSuccess, PublishFailure>
    {
        private EnqueueOutcome(OneOf<PublishSuccess, PublishFailure> input) : base(input)
        {
        }

        public static implicit operator EnqueueOutcome(PublishSuccess x) => new(x);
        public static implicit operator EnqueueOutcome(PublishFailure x) => new(x);
    }
}
using OneOf;

namespace BreadTh.Bunder
{
    public class ConsumptionOutcome : OneOfBase<ConsumptionSuccess, ConsumptionRetry, ConsumptionReject>
    {
        protected ConsumptionOutcome(OneOf<ConsumptionSuccess, ConsumptionRetry, ConsumptionReject> input) : base(input) { }
        
        public static implicit operator ConsumptionOutcome(ConsumptionSuccess x) => new (x);
        public static implicit operator ConsumptionOutcome(ConsumptionRetry x) => new(x);
        public static implicit operator ConsumptionOutcome(ConsumptionReject x) => new(x);

        public static ConsumptionOutcome From(EnqueueOutcome enqueueOutcome) =>
            enqueueOutcome.Match<ConsumptionOutcome>(
                (PublishSuccess success) => new ConsumptionSuccess(),
                (PublishFailure failure) => new ConsumptionReject(
                    $"Could not finish consumption because dependent publish failed: {failure.Reason} ")
            );
    }
}
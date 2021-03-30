using System;

namespace BreadTh.Bunder
{
    public record ConsumptionRetry(TimeSpan Delay, string Reason = null);
}
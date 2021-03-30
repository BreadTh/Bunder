using BreadTh.StronglyApied.Attributes;

namespace BreadTh.Bunder.samples
{
    public class Participant
    {
        [StronglyApiedString]
        public string name;

        [StronglyApiedInt]
        public int accuracyPercentage;
    }
}
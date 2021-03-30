namespace BreadTh.Bunder.Core
{
    internal class BunderNames
    {

        private readonly string setName;
        public BunderNames(string setName)
        {
            this.setName = setName;
        }
        internal string New => setName + ".new";
        internal string Ready => setName + ".ready";
        internal string Complete => setName + ".complete";

        internal string DelayedRetry => setName + ".delayedRetry";
        internal string Delay => setName + ".delay";

        internal string Reject => setName + ".reject";
        internal string Rejected => setName + ".rejected";
        internal string Revive => setName + ".revive";

        internal string Log => setName + ".log";

        internal string DisplayName => setName;
    }
}
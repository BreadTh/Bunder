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

        internal string Retry => setName + ".delay";
        internal string TimedOut => setName + ".timedOut";

        internal string Reject => setName + ".reject";
        internal string Rejected => setName + ".rejected";
        internal string Revive => setName + ".revive";

        internal string Log => setName + ".log";
        internal string DisplayName => setName;

        
        internal string SharedLog => "_bunder.log";
        internal string SharedNew => "_bunder.new";
        internal string SharedRetry => "_bunder.retry";
        internal string SharedResume => "_bunder.resume";
        internal string SharedReject => "_bunder.reject";
        internal string SharedRevive => "_bunder.revive";
        internal string SharedCompleted => "_bunder.completed";
        
    }
}
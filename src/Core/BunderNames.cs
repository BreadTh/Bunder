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

        
        internal string SharedLog => "bunder.log";
        internal string SharedNew => "bunder.new";
        internal string SharedRetry => "bunder.retry";
        internal string SharedResume => "bunder.resume";
        internal string SharedReject => "bunder.reject";
        internal string SharedRevive => "bunder.revive";
        internal string SharedCompleted => "bunder.completed";
        
    }
}
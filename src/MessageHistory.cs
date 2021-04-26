using System;

using Newtonsoft.Json;
using BreadTh.StronglyApied.Attributes;

using BreadTh.Bunder.Helpers;

namespace BreadTh.Bunder
{
    public class MessageHistory
    {
        [StronglyApiedInt]
        public int retryCounter;

        [StronglyApiedObject]
        public EnqueueTime enqueueTime;

        [StronglyApiedString]
        public string status;

        [StronglyApiedString]
        public string reasonForLatestStatusChange;
        
    }
    
    public class EnqueueTime
    {
        [StronglyApiedDateTime(exactFormat: "yyyy/MM/dd HH':'mm':'ss'.'ff"), JsonConverter(typeof(SerializeDateFormatConverter), "yyyy/MM/dd HH':'mm':'ss'.'ff")]
        public DateTime original;

        [StronglyApiedDateTime(exactFormat: "yyyy/MM/dd HH':'mm':'ss'.'ff"), JsonConverter(typeof(SerializeDateFormatConverter), "yyyy/MM/dd HH':'mm':'ss'.'ff")]
        public DateTime latest;
    }
}

using System;

using Newtonsoft.Json;
using BreadTh.StronglyApied.Attributes;

using BreadTh.Bunder.Helpers;

namespace BreadTh.Bunder
{
    public class EnvelopeHistory
    {
        public int retryCounter;
        public EnqueueTime enqueueTime;        
    }
    
    public class EnqueueTime
    {
        [StronglyApiedDateTime(exactFormat: "yyyy/MM/dd HH':'mm':'ss'.'ff"), JsonConverter(typeof(SerializeDateFormatConverter), "yyyy/MM/dd HH':'mm':'ss'.'ff")]
        public DateTime original;
        [StronglyApiedDateTime(exactFormat: "yyyy/MM/dd HH':'mm':'ss'.'ff"), JsonConverter(typeof(SerializeDateFormatConverter), "yyyy/MM/dd HH':'mm':'ss'.'ff")]
        public DateTime latest;
    }

    public class EnvelopeStatus
    {
        public string value;
        public string reasonForLatestChange;
        public string updatedBy;
    }
}

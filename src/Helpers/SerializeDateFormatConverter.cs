using Newtonsoft.Json.Converters;

namespace BreadTh.Bunder.Helpers
{
    public class SerializeDateFormatConverter : IsoDateTimeConverter
    {
        public SerializeDateFormatConverter(string format)
        {
            DateTimeFormat = format;
        }
    }
}

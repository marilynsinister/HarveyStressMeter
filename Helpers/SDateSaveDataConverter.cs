using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Tolerates legacy save JSON where nullable SDate fields were serialized as Day=0.
    /// SMAPI's default SDate deserializer rejects day 0 unless allowDayZero is set.
    /// </summary>
    public sealed class SDateSaveDataConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(SDate)
                || Nullable.GetUnderlyingType(objectType) == typeof(SDate);
        }

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            bool nullable = Nullable.GetUnderlyingType(objectType) == typeof(SDate);

            if (reader.TokenType == JsonToken.Null)
                return nullable ? null : SDate.Now();

            var obj = JObject.Load(reader);
            int day = obj.Value<int?>("Day") ?? 0;
            int year = obj.Value<int?>("Year") ?? 0;
            string? season = obj.Value<string>("Season") ?? obj.Value<string>("SeasonKey");

            if (!IsValidDateParts(day, year, season))
                return nullable ? null : SDate.Now();

            try
            {
                return new SDate(day, season!, year);
            }
            catch
            {
                return nullable ? null : SDate.Now();
            }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var date = (SDate)value;
            writer.WriteStartObject();
            writer.WritePropertyName("Day");
            writer.WriteValue(date.Day);
            writer.WritePropertyName("Season");
            writer.WriteValue(date.SeasonKey);
            writer.WritePropertyName("Year");
            writer.WriteValue(date.Year);
            writer.WriteEndObject();
        }

        private static bool IsValidDateParts(int day, int year, string? season)
        {
            return day is >= 1 and <= 28
                && year >= 1
                && !string.IsNullOrWhiteSpace(season);
        }
    }
}

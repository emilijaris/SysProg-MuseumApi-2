using System.Text.Json;
namespace museum_api_sysprog_1.Mappers
{
    public static class JsonMapper
    {
        public static string GetStringSafe(this JsonElement element, string propertyName) =>
           element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? string.Empty : string.Empty;

        public static int GetIntSafe(this JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var prop) && prop.TryGetInt32(out var value) ? value : 0;

        public static bool GetBoolSafe(this JsonElement element, string propertyName) =>
           element.TryGetProperty(propertyName, out var prop) ? (prop.ValueKind == JsonValueKind.True ? true : false) : false;




        public static Painting MapFromJson(string json)
        {
            //  JsonDocument doc = JsonDocument.Parse(json);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                return new Painting
                {
                    objectID = root.GetIntSafe("objectID"),
                    isHighlight = root.GetBoolSafe("isHighlight"),
                    accessionNumber = root.GetStringSafe("accessionNumber"),
                    accessionYear = root.GetStringSafe("accessionYear"),
                    primaryImage = root.GetStringSafe("primaryImage"),
                    primaryImageSmall = root.GetStringSafe("primaryImageSmall"),
                    department = root.GetStringSafe("department"),
                    artistDisplayName = root.GetStringSafe("artistDisplayName"),
                    artistBeginDate = root.GetStringSafe("artistBeginDate"),
                    artistEndDate = root.GetStringSafe("artistEndDate")

                };
            }

        }
    }

}
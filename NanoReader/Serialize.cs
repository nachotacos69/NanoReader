using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NanoReader
{
    public class Serialize
    {
        public class JsonEntry
        {
            public string file { get; set; }
            public bool isCompressed { get; set; }
        }

        // Method to write the structured JSON data to file
        public static void WriteJson(Dictionary<string, Dictionary<int, JsonEntry>> data, string filePath)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }
}
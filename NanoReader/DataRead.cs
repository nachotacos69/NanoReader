// Note to Self: remove lots of these comments, some will be useless in the future.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.GZip;

namespace NanoReader
{
    public class DataRead
    {
        // Structure to represent the header of pa.bin (32 bytes)
        private struct Header
        {
            public int Magic;           // Magic number (0x414150)
            public int Padding1;        // 4 bytes padding
            public int BaseTableCount;  // Number of base table entries
            public int BaseTableOffset; // Offset to base table
            public int OffsetTableOffset; // Offset to offset table
            public int UnknownChunk1;   // Undocumented value
            public long Padding2;       // 8 bytes padding
        }

        // Structure for each base table entry (16 bytes)
        private struct BaseTableEntry
        {
            public int OffsetName;      // Offset to the file name string
            public int Size;            // Size of the data chunk
            public int UnknownData1;    // Undocumented value 1
            public int UnknownData2;    // Undocumented value 2
        }

        // Class for JSON serialization entries
        private class JsonEntry
        {
            public string file { get; set; }
            public bool isCompressed { get; set; }
        }

        // List to hold base table entries
        private static List<BaseTableEntry> baseTableEntries = new List<BaseTableEntry>();

        // List to hold offset table entries 
        private static List<int> offsetTableEntries = new List<int>();

        // Log file path for debug output
        private static string logFilePath;

        
        public static void ExtractFiles()
        {
            // Debug Log
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            logFilePath = $"Debug_{timestamp}.txt";

            LogMessage("Info: Starting extraction process.", ConsoleColor.Green);

            // Dictionary for JSON data (index as key)
            Dictionary<int, JsonEntry> jsonData = new Dictionary<int, JsonEntry>();

            // Read pa.bin
            using (FileStream binStream = new FileStream("pa.bin", FileMode.Open, FileAccess.Read))
            using (BinaryReader binReader = new BinaryReader(binStream))
            {
                // Read header
                Header header = ReadHeader(binReader);
                if (header.Magic != 0x414150)
                {
                    LogMessage("Error: Invalid magic number in pa.bin.", ConsoleColor.Red);
                    return;
                }

                LogMessage($"Info: Magic verified: 0x{header.Magic:X}.", ConsoleColor.Green);
                LogMessage($"Info: Base table count: {header.BaseTableCount}.", ConsoleColor.Green);
                LogMessage($"Info: Base table offset: 0x{header.BaseTableOffset:X}.", ConsoleColor.Green);
                LogMessage($"Info: Offset table offset: 0x{header.OffsetTableOffset:X}.", ConsoleColor.Green);
                LogMessage($"Info: Unknown chunk 1: 0x{header.UnknownChunk1:X}.", ConsoleColor.Green);

                // Read base table entries
                ReadBaseTable(binReader, header);
                if (baseTableEntries.Count != header.BaseTableCount)
                {
                    LogMessage("Warning: Mismatch in base table entry count.", ConsoleColor.Yellow);
                }

                // Read offset table entries
                ReadOffsetTable(binReader, header, header.BaseTableCount);

                // Ensure the number of offset table entries matches base table count
                if (offsetTableEntries.Count != baseTableEntries.Count)
                {
                    LogMessage("Error: Offset table count does not match base table count.", ConsoleColor.Red);
                    return;
                }
            }

            // Prepare output folder
            string outputFolder = "pa";
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                LogMessage($"Info: Created output folder: {outputFolder}.", ConsoleColor.Green);
            }

            // JSON file name based on pa.arc (without extension)
            string jsonFilePath = "pa.json";

            // Open pa.arc for data extraction
            using (FileStream arcStream = new FileStream("pa.arc", FileMode.Open, FileAccess.Read))
            using (BinaryReader arcReader = new BinaryReader(arcStream))
            {
                // Re-open pa.bin to read names
                using (FileStream binStream = new FileStream("pa.bin", FileMode.Open, FileAccess.Read))
                using (BinaryReader binReader = new BinaryReader(binStream))
                {
                    // Process each entry
                    for (int i = 0; i < baseTableEntries.Count; i++)
                    {
                        BaseTableEntry entry = baseTableEntries[i];
                        int dataOffset = offsetTableEntries[i];

                        LogMessage($"Info: Processing entry {i + 1}:", ConsoleColor.Green);
                        LogMessage($"  Offset Name: 0x{entry.OffsetName:X}", ConsoleColor.Green);
                        LogMessage($"  Size: {entry.Size} bytes", ConsoleColor.Green);
                        LogMessage($"  Unknown Data 1: 0x{entry.UnknownData1:X}", ConsoleColor.Green);
                        LogMessage($"  Unknown Data 2: 0x{entry.UnknownData2:X}", ConsoleColor.Green);
                        LogMessage($"  Data Offset: 0x{dataOffset:X}", ConsoleColor.Green);

                        // Read file name from offset in pa.bin
                        string fileName = ReadFileName(binReader, entry.OffsetName);
                        if (string.IsNullOrEmpty(fileName))
                        {
                            LogMessage($"Warning: Empty file name for entry {i + 1}. Skipping.", ConsoleColor.Yellow);
                            continue;
                        }

                        LogMessage($"Info: File name: {fileName}", ConsoleColor.Green);

                        // Create subdirectories if needed
                        string fullPath = Path.Combine(outputFolder, fileName);
                        string directory = Path.GetDirectoryName(fullPath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                            LogMessage($"Info: Created directory: {directory}", ConsoleColor.Green);
                        }

                        // Extract data from pa.arc
                        arcReader.BaseStream.Seek(dataOffset, SeekOrigin.Begin);
                        byte[] data = arcReader.ReadBytes(entry.Size);

                        if (data.Length != entry.Size)
                        {
                            LogMessage($"Warning: Read data size mismatch for {fileName}. Expected: {entry.Size}, Read: {data.Length}", ConsoleColor.Yellow);
                        }

                        // Determine if data is GZIP compressed (check header: 0x1F 0x8B)
                        bool isCompressed = data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B;

                        // Add to JSON data
                        int index = i + 1;
                        jsonData[index] = new JsonEntry { file = fileName, isCompressed = isCompressed };

                        if (isCompressed)
                        {
                            LogMessage($"Info: Detected GZIP compression for {fileName}.", ConsoleColor.Green);

                            // Check if file has .gz extension
                            // So when being decompressed, best way to not overwrite this is to create a output file without .gz)
                            // Example: data.pr.gz will turn to data.pr when decompressed
                            // for the time being, this will do the trick.
                            bool isGzFormat = fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

                            byte[] decompressedData;
                            try
                            {
                                // Decompress the data
                                using (MemoryStream inStream = new MemoryStream(data))
                                using (MemoryStream outStream = new MemoryStream())
                                {
                                    GZip.Decompress(inStream, outStream, false);
                                    decompressedData = outStream.ToArray();
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Error: Decompression failed for {fileName}: {ex.Message}", ConsoleColor.Red);
                                // Fallback: Write compressed data
                                File.WriteAllBytes(fullPath, data);
                                LogMessage($"Info: Wrote compressed data as fallback for {fullPath}", ConsoleColor.Green);
                                continue;
                            }

                            if (isGzFormat)
                            {
                                File.WriteAllBytes(fullPath, data);
                                LogMessage($"Info: Extracted compressed file: {fullPath}", ConsoleColor.Green);

                                // Write decompressed data to file without .gz format
                                string withoutGz = fileName.Substring(0, fileName.Length - 3);
                                string decompressedPath = Path.Combine(outputFolder, withoutGz);
                                string decompressedDir = Path.GetDirectoryName(decompressedPath);
                                if (!Directory.Exists(decompressedDir))
                                {
                                    Directory.CreateDirectory(decompressedDir);
                                    LogMessage($"Info: Created directory: {decompressedDir}", ConsoleColor.Green);
                                }
                                File.WriteAllBytes(decompressedPath, decompressedData);
                                LogMessage($"Info: Extracted decompressed file: {decompressedPath}", ConsoleColor.Green);
                            }
                            else
                            {
                                // Write decompressed data to the original path
                                File.WriteAllBytes(fullPath, decompressedData);
                                LogMessage($"Info: Extracted decompressed file: {fullPath}", ConsoleColor.Green);
                            }
                        }
                        else
                        {
                            // Not compressed: Write data directly
                            File.WriteAllBytes(fullPath, data);
                            LogMessage($"Info: Extracted file: {fullPath}", ConsoleColor.Green);
                        }
                    }
                }
            }

            // Serialize and write JSON file
            string json = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonFilePath, json);
            LogMessage($"Info: Generated JSON file: {jsonFilePath}", ConsoleColor.Green);

            LogMessage("Info: Extraction completed.", ConsoleColor.Green);
        }

        // Method to read the header structure
        private static Header ReadHeader(BinaryReader reader)
        {
            Header header = new Header();
            header.Magic = reader.ReadInt32();
            header.Padding1 = reader.ReadInt32();
            header.BaseTableCount = reader.ReadInt32();
            header.BaseTableOffset = reader.ReadInt32();
            header.OffsetTableOffset = reader.ReadInt32();
            header.UnknownChunk1 = reader.ReadInt32();
            header.Padding2 = reader.ReadInt64();
            return header;
        }

        // Method to read base table entries
        private static void ReadBaseTable(BinaryReader reader, Header header)
        {
            reader.BaseStream.Seek(header.BaseTableOffset, SeekOrigin.Begin);
            for (int i = 0; i < header.BaseTableCount; i++)
            {
                BaseTableEntry entry = new BaseTableEntry();
                entry.OffsetName = reader.ReadInt32();
                entry.Size = reader.ReadInt32();
                entry.UnknownData1 = reader.ReadInt32();
                entry.UnknownData2 = reader.ReadInt32();
                baseTableEntries.Add(entry);
            }
        }

        // Method to read offset table entries
        private static void ReadOffsetTable(BinaryReader reader, Header header, int count)
        {
            reader.BaseStream.Seek(header.OffsetTableOffset, SeekOrigin.Begin);
            for (int i = 0; i < count; i++)
            {
                int offsetData = reader.ReadInt32();
                offsetTableEntries.Add(offsetData);
            }
        }

        // Method to read null-terminated string for file name
        private static string ReadFileName(BinaryReader reader, int offset)
        {
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            StringBuilder sb = new StringBuilder();
            byte b;
            while ((b = reader.ReadByte()) != 0)
            {
                sb.Append((char)b);
            }
            return sb.ToString();
        }

       // Log Messages
        private static void LogMessage(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();

            // Append to log file
            using (StreamWriter writer = File.AppendText(logFilePath))
            {
                writer.WriteLine(message);
            }
        }
    }
}
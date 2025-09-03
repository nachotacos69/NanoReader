using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        // List to hold base table entries
        private static List<BaseTableEntry> baseTableEntries = new List<BaseTableEntry>();

        // List to hold offset table entries (data offsets)
        private static List<int> offsetTableEntries = new List<int>();

        // Log file path for debug output
        private static string logFilePath;

        // Method to perform the extraction process
        public static void ExtractFiles()
        {
            // Initialize log file with current date and time
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            logFilePath = $"Debug_{timestamp}.txt";

            LogMessage("Info: Starting extraction process.", ConsoleColor.Green);

            // Dictionaries for JSON data sections
            Dictionary<int, Serialize.JsonEntry> baseEntries = new Dictionary<int, Serialize.JsonEntry>();
            Dictionary<int, Serialize.JsonEntry> parEntries = new Dictionary<int, Serialize.JsonEntry>();

            int baseIndex = 1;
            int parIndex = 1;

            // List to hold paths of extracted files for PAR processing
            List<string> extractedPaths = new List<string>();

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

            // JSON file name
            string jsonFilePath = "pa.json";

            // Open pa.arc for data extraction
            using (FileStream arcStream = new FileStream("pa.arc", FileMode.Open, FileAccess.Read))
            using (BinaryReader arcReader = new BinaryReader(arcStream))
            {
                // Re-open pa.bin since we need to seek for names
                using (FileStream binStream = new FileStream("pa.bin", FileMode.Open, FileAccess.Read))
                using (BinaryReader binReader = new BinaryReader(binStream))
                {
                    // First pass: Extract all files
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
                        byte[] dataToWrite = data;
                        string writePath = fullPath;
                        bool entryIsCompressed = isCompressed;

                        if (isCompressed)
                        {
                            LogMessage($"Info: Detected GZIP compression for {fileName}.", ConsoleColor.Green);

                            try
                            {
                                using (MemoryStream inStream = new MemoryStream(data))
                                using (MemoryStream outStream = new MemoryStream())
                                {
                                    GZip.Decompress(inStream, outStream, false);
                                    dataToWrite = outStream.ToArray();
                                }
                                entryIsCompressed = false; // Decompressed data is not compressed

                                // If file has .gz extension and no other extension, keep name but write decompressed
                                if (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) &&
                                    !Path.GetFileNameWithoutExtension(fileName).Contains("."))
                                {
                                    // Overwrite with decompressed data, keep .gz name
                                    writePath = fullPath;
                                }
                                else if (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Remove .gz for decompressed data
                                    writePath = Path.Combine(outputFolder, fileName.Substring(0, fileName.Length - 3));
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Error: Decompression failed for {fileName}: {ex.Message}", ConsoleColor.Red);
                                // Fallback: Write compressed data
                                dataToWrite = data;
                                writePath = fullPath;
                                entryIsCompressed = true;
                            }
                        }

                        // Ensure directory for writePath
                        string writeDir = Path.GetDirectoryName(writePath);
                        if (!Directory.Exists(writeDir))
                        {
                            Directory.CreateDirectory(writeDir);
                            LogMessage($"Info: Created directory: {writeDir}", ConsoleColor.Green);
                        }

                        // Write the file
                        File.WriteAllBytes(writePath, dataToWrite);
                        LogMessage($"Info: Extracted file: {writePath}", ConsoleColor.Green);

                        // Add to base entries
                        baseEntries[baseIndex++] = new Serialize.JsonEntry { file = writePath, isCompressed = entryIsCompressed };

                        // Store path for PAR processing
                        extractedPaths.Add(writePath);
                    }
                }
            }

            // Process PAR files one by one
            foreach (string path in extractedPaths)
            {
                byte[] data;
                try
                {
                    data = File.ReadAllBytes(path);
                }
                catch (Exception ex)
                {
                    LogMessage($"Error: Failed to read file for PAR check {path}: {ex.Message}", ConsoleColor.Red);
                    continue;
                }

                // Check if data is a PAR archive
                bool isPar = data.Length >= 4 && data[0] == 0x50 && data[1] == 0x41 && data[2] == 0x52 && data[3] == 0x00;

                if (isPar)
                {
                    LogMessage($"Info: Detected PAR archive in {path}. Extracting subfiles.", ConsoleColor.Green);

                    // Create folder for PAR extraction
                    string parFolder = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                    if (!Directory.Exists(parFolder))
                    {
                        Directory.CreateDirectory(parFolder);
                        LogMessage($"Info: Created PAR output folder: {parFolder}", ConsoleColor.Green);
                    }

                    // Extract PAR subfiles
                    PARread.ExtractPAR(data, path, ref parIndex, parEntries);
                }
            }

            // Serialize and write JSON file with sections
            var allData = new Dictionary<string, Dictionary<int, Serialize.JsonEntry>>
            {
                ["BASE"] = baseEntries,
                ["PAR SECTION"] = parEntries
            };
            Serialize.WriteJson(allData, jsonFilePath);
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

        // null-terminated string
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

        
        public static void LogMessage(string message, ConsoleColor color)
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
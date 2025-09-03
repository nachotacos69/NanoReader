using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;

namespace NanoReader
{
    public class PARread
    {
        
        public static void ExtractPAR(byte[] parData, string intendedPath, ref int index, Dictionary<int, Serialize.JsonEntry> entries)
        {
            using (MemoryStream ms = new MemoryStream(parData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                if (parData.Length < 16)
                {
                    DataRead.LogMessage($"Error: PAR data for '{intendedPath}' is too small to be valid ({parData.Length} bytes).", ConsoleColor.Red);
                    return;
                }

                int magic = reader.ReadInt32();
                if (magic != 0x00524150)
                {
                    DataRead.LogMessage($"Error: Invalid PAR magic for {intendedPath}: 0x{magic:X}", ConsoleColor.Red);
                    return;
                }

                if (ms.Position + 12 > ms.Length)
                {
                    DataRead.LogMessage($"Error: Insufficient data for PAR header in '{intendedPath}'.", ConsoleColor.Red);
                    return;
                }

                int parData1 = reader.ReadInt32();
                int entryCount = reader.ReadInt32();
                int parData2 = reader.ReadInt32(); 

                DataRead.LogMessage($"Info: PAR Magic verified for {intendedPath}: 0x{magic:X}.", ConsoleColor.Green);
                DataRead.LogMessage($"Info: PAR Entry Count: {entryCount}.", ConsoleColor.Green);

                if (entryCount <= 0 || entryCount > 10240)
                {
                    DataRead.LogMessage($"Error: Invalid or unreasonable entry count ({entryCount}) in PAR '{intendedPath}'.", ConsoleColor.Red);
                    return;
                }

                if (ms.Position + (long)entryCount * 4 > ms.Length)
                {
                    DataRead.LogMessage($"Error: Insufficient data for offset table in '{intendedPath}'.", ConsoleColor.Red);
                    return;
                }

                List<int> subOffsets = new List<int>();
                for (int i = 0; i < entryCount; i++)
                {
                    subOffsets.Add(reader.ReadInt32());
                }

                long currentPos = ms.Position;
                long nextAlignedPos = (currentPos + 15) & ~15;

                if (nextAlignedPos >= ms.Length)
                {
                    // Some sizeless archives might not have a name table if there's only one entry.
                    if (entryCount > 1)
                    {
                        DataRead.LogMessage($"Error: No name table found in PAR {intendedPath}. Stream ended after offset table.", ConsoleColor.Red);
                        return;
                    }
                    // If one entry, we can proceed without a name table, but the name will be generic.
                }
                ms.Seek(nextAlignedPos, SeekOrigin.Begin);
                DataRead.LogMessage($"Info: Name table located at offset 0x{ms.Position:X} in PAR '{intendedPath}'.", ConsoleColor.Cyan);

                List<string> subNames = new List<string>();
                if (ms.Position + (long)entryCount * 32 <= ms.Length)
                {
                    for (int i = 0; i < entryCount; i++)
                    {
                        byte[] nameBytes = reader.ReadBytes(32);
                        string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                        subNames.Add(name);
                    }
                }
                else
                {
                    DataRead.LogMessage($"Warning: Insufficient data for name table in '{intendedPath}'. Generating generic names.", ConsoleColor.Yellow);
                    for (int i = 0; i < entryCount; i++)
                    {
                        subNames.Add($"entry_{i}");
                    }
                }

                // sizeless calculation
                DataRead.LogMessage("Info: Calculating all file sizes based on offset differences.", ConsoleColor.Magenta);
                List<int> subSizes = new List<int>(new int[entryCount]);

                // To calculate sizes, we need the offsets in ascending order.
                // We pair them with their original index to map the calculated size back correctly.
                var indexedOffsets = subOffsets
                    .Select((offset, originalIndex) => new { offset, originalIndex })
                    .OrderBy(item => item.offset)
                    .ToList();

                for (int i = 0; i < indexedOffsets.Count; i++)
                {
                    var currentEntry = indexedOffsets[i];
                    int size;

                    // Skip entries with invalid negative offsets, which can corrupt calculations.
                    if (currentEntry.offset < 0) continue;

                    if (i < indexedOffsets.Count - 1)
                    {
  
                        var nextEntry = indexedOffsets[i + 1];
                        size = nextEntry.offset - currentEntry.offset;
                    }
                    else
                    {
                      
                        size = (int)parData.Length - currentEntry.offset;
                    }

                    if (size < 0)
                    {
                        DataRead.LogMessage($"Error: Calculated a negative size ({size}) for entry '{subNames[currentEntry.originalIndex]}'. Aborting this PAR.", ConsoleColor.Red);
                        return;
                    }


                    subSizes[currentEntry.originalIndex] = size;
                }
              

                string parFolder = Path.Combine(Path.GetDirectoryName(intendedPath), Path.GetFileNameWithoutExtension(intendedPath));

                for (int i = 0; i < entryCount; i++)
                {
                    string originalFileName = subNames[i];
                    string subFileName = SanitizeFileName(originalFileName);

                    if (originalFileName != subFileName)
                    {
                        DataRead.LogMessage($"Info: Sanitized filename from '{originalFileName}' to '{subFileName}'.", ConsoleColor.DarkYellow);
                    }

                    int subSize = subSizes[i];
                    int subOffset = subOffsets[i];

                    if (string.IsNullOrEmpty(subFileName))
                    {
                        DataRead.LogMessage($"Warning: Skipping empty sub name in PAR entry {i + 1}.", ConsoleColor.Yellow);
                        continue;
                    }

                    if (subOffset < 0 || (long)subOffset + subSize > parData.Length)
                    {
                        DataRead.LogMessage($"[FATAL PARSING ERROR] in '{intendedPath}'", ConsoleColor.Red);
                        DataRead.LogMessage($"  File Entry #{i + 1}: '{subFileName}'", ConsoleColor.Red);
                        DataRead.LogMessage($"  Reason: Data chunk is out of bounds. Offset=0x{subOffset:X}, Size={subSize}, Total={parData.Length}", ConsoleColor.Red);
                        continue;
                    }

                    string subFullPath = Path.Combine(parFolder, subFileName);
                    string subDirectory = Path.GetDirectoryName(subFullPath);
                    if (!string.IsNullOrEmpty(subDirectory) && !Directory.Exists(subDirectory))
                    {
                        Directory.CreateDirectory(subDirectory);
                    }

                    ms.Seek(subOffset, SeekOrigin.Begin);
                    byte[] subData = reader.ReadBytes(subSize);

                    bool isCompressed = subData.Length >= 2 && subData[0] == 0x1F && subData[1] == 0x8B;
                    byte[] dataToWrite = subData;
                    string writePath = subFullPath;
                    bool entryIsCompressed = isCompressed;

                    if (isCompressed)
                    {
                        byte[] decompressedData;
                        try
                        {
                            using (MemoryStream inStream = new MemoryStream(subData))
                            using (MemoryStream outStream = new MemoryStream())
                            {
                                GZip.Decompress(inStream, outStream, false);
                                decompressedData = outStream.ToArray();
                            }
                        }
                        catch (Exception ex)
                        {
                            DataRead.LogMessage($"Error: Sub decompression failed for {subFileName}: {ex.Message}", ConsoleColor.Red);
                            File.WriteAllBytes(subFullPath, subData);
                            entries[index++] = new Serialize.JsonEntry { file = subFullPath, isCompressed = true };
                            continue;
                        }

                        if (subFileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(subFileName).Contains("."))
                        {
                            writePath = Path.Combine(parFolder, subFileName.Substring(0, subFileName.Length - 3));
                            string writeDir = Path.GetDirectoryName(writePath);
                            if (!string.IsNullOrEmpty(writeDir) && !Directory.Exists(writeDir))
                            {
                                Directory.CreateDirectory(writeDir);
                            }
                            dataToWrite = decompressedData;
                            entryIsCompressed = false;
                        }
                        else
                        {
                            dataToWrite = decompressedData;
                            writePath = subFullPath;
                            entryIsCompressed = false;
                        }
                    }

                    File.WriteAllBytes(writePath, dataToWrite);
                    DataRead.LogMessage($"Info: Extracted sub file: {writePath}", ConsoleColor.Green);
                    entries[index++] = new Serialize.JsonEntry { file = writePath, isCompressed = entryIsCompressed };

                    bool isPar = dataToWrite.Length >= 4 && dataToWrite[0] == 0x50 && dataToWrite[1] == 0x41 && dataToWrite[2] == 0x52 && dataToWrite[3] == 0x00;

                    if (isPar)
                    {
                        DataRead.LogMessage($"Info: Detected nested PAR in {writePath}. Extracting its contents...", ConsoleColor.Cyan);
                        string nestedParFolder = Path.Combine(Path.GetDirectoryName(writePath), Path.GetFileNameWithoutExtension(writePath));
                        if (!Directory.Exists(nestedParFolder))
                        {
                            Directory.CreateDirectory(nestedParFolder);
                        }
                        ExtractPAR(dataToWrite, writePath, ref index, entries);
                    }
                }
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in fileName)
            {
                if (invalidChars.Contains(c) || char.IsControl(c))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        
    }
}
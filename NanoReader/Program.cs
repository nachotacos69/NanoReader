using System;
using System.IO;

namespace NanoReader
{
    class Program
    {
        static void Main(string[] args)
        {
            
            if (args.Length > 0 && args[0] == "-x")
            {
                // Verify the presence of required files before proceeding
                if (!File.Exists("pa.bin"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: pa.bin file is missing.");
                    Console.ResetColor();
                    return;
                }

                if (!File.Exists("pa.arc"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: pa.arc file is missing.");
                    Console.ResetColor();
                    return;
                }

                // Proceed with extraction
                DataRead.ExtractFiles();
            }
            else
            {
                Console.WriteLine("Usage: Run with -x flag for extraction (e.g., NanoReader.exe -x)");
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using SqlToLinq.Core;
using System.Text.Json;
using System.Collections.Generic;

namespace SqlToLinq.Cli {

    public record TestCase(
        int Id,
        string Desc,
        string SqlInput,
        string ExpectedLinq
    );

    internal class Program {
        static void Main(string[] args) {

            string filePath = "IOPairs.json";

            if (!File.Exists(filePath)) {

                Console.WriteLine($"[ERROR]: File doesn't exists: {filePath}");
                Console.ReadLine();
                return;
            }

            bool dumpAst = args.Contains("--ast");
            string astDir = "asts";

            if (dumpAst) {
                Directory.CreateDirectory(astDir);
            }

            string jsonText = File.ReadAllText(filePath);
            var testCases = JsonSerializer.Deserialize<List<TestCase>>(jsonText);

            if (testCases != null) {
                Console.WriteLine($"[INFO] {testCases.Count} testcases successfully loaded!\n");

                for (int i = 0; i < testCases.Count; i++) {

                    Console.WriteLine($"#{testCases[i].Id} - {testCases[i].Desc}");
                    Console.WriteLine($"SQL: {testCases[i].SqlInput}");
                    Console.WriteLine($"{"Expected LINQ:", -25} {testCases[i].ExpectedLinq}");

                    try {

                        var (generatedLinq, sqlAstDot, linqAstDot) =
                            SqlToLinqConverter.ConvertWithAst(testCases[i].SqlInput);

                        Console.WriteLine($"{"Generated LINQ:",-25} {generatedLinq}\n\n");

                        if (dumpAst) {
                            File.WriteAllText(Path.Combine(astDir, $"{testCases[i].Id}_sql.dot"), sqlAstDot);
                            File.WriteAllText(Path.Combine(astDir, $"{testCases[i].Id}_linq.dot"), linqAstDot);
                        }

                    } catch (Exception ex) {

                        Console.WriteLine($"[ERROR] Error during parsing: {ex.Message}");
                    }
                }

                if (dumpAst) {
                    Console.WriteLine($"[INFO] AST Files: {Path.GetFullPath(astDir)}");
                }
            }
        }
    }
}
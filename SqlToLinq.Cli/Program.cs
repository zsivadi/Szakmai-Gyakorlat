
using System;
using System.IO;
using System.Linq;
using Antlr4.Runtime;
using System.Text.Json;
using System.Collections.Generic;

namespace SqlToLinq.Cli {

    public record User(
        int Id, 
        string Name, 
        int Age, 
        string Role, 
        int Points, 
        int Bonus
    );

    public record TestCase(
        int Id,
        string Desc,
        string SqlInput,
        string ExpectedLinq
    );

    public class DummyDatabase {
        public List<User> Users { get; } = new List<User>
        {
            new User(Id: 1, Name: "Admin", Age: 35, Role: "Admin", Points: 250, Bonus: 50),
            new User(Id: 2, Name: "Nagy Peti", Age: 17, Role: "Guest", Points: 20, Bonus: 20),
            new User(Id: 3, Name: "Anna", Age: 22, Role: "Member", Points: 80, Bonus: 150),
            new User(Id: 4, Name: "Gábor", Age: 18, Role: "Guest", Points: 15, Bonus: 5),
            new User(Id: 5, Name: "Lilla", Age: 28, Role: "Moderator", Points: 120, Bonus: 100)
        };
    }

    internal class Program {
        static void Main(string[] args) {

            string filePath = "IOPairs.json";

            if (!File.Exists(filePath)) {

                Console.WriteLine($"[ERROR]: File doesn't exists: {filePath}");
                Console.ReadLine();
                return;
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

                        string generatedLinq = SqlToLinqConverter.Convert(testCases[i].SqlInput);

                        Console.WriteLine($"{"Generated LINQ:", -25} {generatedLinq}\n\n");

                    } catch (Exception ex) {

                        Console.WriteLine($"[ERROR] Error during parsing: {ex.Message}");
                    }

                }
            }
        }
    }
}
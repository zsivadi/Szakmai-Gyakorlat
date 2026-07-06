using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using Antlr4.Runtime;

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
                    Console.WriteLine($"SQL:        {testCases[i].SqlInput}");
                    Console.WriteLine($"LINQ:       {testCases[i].ExpectedLinq}\n");
                    
                }
            }

            string sql = "SELECT Name,Age FROM Users WHERE Age>18;";

            var inputStream = new AntlrInputStream(sql);
            var lexer = new SqlParserLexer(inputStream);
            var tokenStream = new CommonTokenStream(lexer);

            var parser = new SqlParserParser(tokenStream);

            var tree = parser.query();

            string treeText = tree.ToStringTree(parser).Replace('(', '[').Replace(')', ']').Replace("<EOF>", "");
            Console.WriteLine(treeText);

            Console.ReadLine();

        }
    }
}
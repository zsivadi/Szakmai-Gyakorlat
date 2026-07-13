using System;
using System.IO;
using SqlToLinq.Core;
using Antlr4.Runtime;
using NUnit.Framework;
using System.Text.Json;
using System.Collections.Generic;

namespace SqlToLinq.Tests {
    [TestFixture]
    public class TranspilerTests { 

        public static IEnumerable<TestCaseData> GetSelectTestCases() {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IOPairs.json");

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"[ERROR] Test file cannot found: {jsonPath}");

            string jsonString = File.ReadAllText(jsonPath);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var testCases = JsonSerializer.Deserialize<List<TestCase>>(jsonString, options);

            foreach (var tc in testCases) {

                yield return new TestCaseData(tc.SqlInput, tc.ExpectedLinq)
                    .SetName($"Test_{tc.Id}_{tc.Desc.Replace(" ", "_")}");
            }
        }

        [TestCaseSource(nameof(GetSelectTestCases))]
        public void Sql_To_Linq_Conversion_Should_Match_Expected(string sqlInput, string expectedLinq) {

            string generatedLinq = SqlToLinqConverter.Convert(sqlInput);

            string cleanGenerated = generatedLinq.Replace(" ", "").Trim();
            string cleanExpected = expectedLinq.Replace(" ", "").Trim();

            Assert.That(cleanGenerated, Is.EqualTo(cleanExpected),
                $"\n[ERROR] Error while transpiling: {sqlInput}\nGenrated: {generatedLinq}\nExpected: {expectedLinq}\n");
        }
    }

    public class TestCase {
        public int Id { get; set; }
        public string Desc { get; set; }
        public string SqlInput { get; set; }
        public string ExpectedLinq { get; set; }
    }
}
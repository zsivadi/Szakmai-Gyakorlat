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

    [TestFixture]
    public class ErrorPathTests {


        [Test]
        public void Lexer_Error_Should_Throw_SqlSyntaxException() {
            
            Assert.Throws<SqlSyntaxException>(() =>
                SqlToLinqConverter.Convert("SELECT @ FROM Users"));
        }

        [Test]
        public void Parser_Error_Should_Throw_SqlSyntaxException() {
            
            Assert.Throws<SqlSyntaxException>(() =>
                SqlToLinqConverter.Convert("SELECT FROM FROM"));
        }

        [Test]
        public void Unsupported_Statement_Type_Should_Throw_NotSupportedException() {

            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("UPDATE Users SET Name = 'Admin' WHERE Id = 1"));
        }

        [Test]
        public void String_GreaterThan_Should_Throw_NotSupportedException() {

            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("SELECT * FROM Users WHERE Name > 'Alice'"));
        }

        [Test]
        public void String_LessThan_Should_Throw_NotSupportedException() {

            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("SELECT * FROM Users WHERE Name < 'Alice'"));
        }

        [Test]
        public void String_GreaterOrEqual_Should_Throw_NotSupportedException() {
            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("SELECT * FROM Users WHERE Name >= 'Alice'"));
        }

        [Test]
        public void String_LessOrEqual_Should_Throw_NotSupportedException() {

            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("SELECT * FROM Users WHERE 'Alice' <= Name"));
        }

        [Test]
        public void String_Between_Should_Throw_NotSupportedException() {

            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("SELECT * FROM Users WHERE Name BETWEEN 'Alice' AND 'Bob'"));
        }

        [Test]
        public void Distinct_OrderBy_Column_Not_In_Select_Should_Throw_NotSupportedException() {

            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("SELECT DISTINCT Name FROM Users ORDER BY Age DESC"));
        }

        [Test]
        public void Distinct_With_Unaliased_Case_And_OrderBy_Not_In_Select_Should_Throw_NotSupportedException() {

            Assert.Throws<NotSupportedException>(() =>
                SqlToLinqConverter.Convert("SELECT DISTINCT CASE WHEN Age > 18 THEN 1 ELSE 0 END FROM Users ORDER BY Age DESC"));
        }
    }
}
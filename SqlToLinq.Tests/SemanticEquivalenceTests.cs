using System;
using System.Linq;
using SqlToLinq.Core;
using NUnit.Framework;
using System.Reflection;
using System.Collections;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace SqlToLinq.Tests {

    public class ScriptGlobals {
        public TestDbContext db;
    }

    /// <summary>
    /// Semantic (execution-based) equivalence test: it executes every SqlInput
    /// in the JSON both as raw SQL AND as the LINQ generated from it
    /// on the same seeded SQLite in-memory database, then compares the two
    /// result sets. This test does not use the ExpectedLinq field,
    /// the string-based regression test is provided by TranspilerTests.
    /// </summary>

    [TestFixture]
    public class SemanticEquivalenceTests {

        private SqliteConnection _connection;
        private TestDbContext _db;

        [SetUp]
        public void SetUp() {

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            _db = new TestDbContext(_connection);
            _db.Database.EnsureCreated();

            TestSeedData.Seed(_db);
        }

        [TearDown]
        public void TearDown() {
            _db.Dispose();
            _connection.Dispose();
        }

        [TestCaseSource(typeof(TranspilerTests), nameof(TranspilerTests.GetSelectTestCases))]
        public async Task Generated_Linq_Should_Return_Same_Rows_As_Sql(string sqlInput, string _unusedExpectedLinq) {

            bool hasOuterJoin =
                sqlInput.IndexOf("LEFT JOIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sqlInput.IndexOf("RIGHT JOIN", StringComparison.OrdinalIgnoreCase) >= 0;

            bool hasGroupByOrderBy =
                sqlInput.IndexOf("GROUP BY", StringComparison.OrdinalIgnoreCase) >= 0 &&
                sqlInput.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase) >= 0;

            bool hasConcat =
                sqlInput.IndexOf("CONCAT(", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sqlInput.Contains("||");

            if (hasOuterJoin || hasGroupByOrderBy || hasConcat) {
                Assert.Ignore("Not supported in CSharpScript semantic test: outer join / GROUP BY+ORDER BY alias / CONCAT.");
            }

            var sqlRows = RunRawSql(sqlInput);

            string generatedLinq = SqlToLinqConverter.Convert(sqlInput);

            var linqRows = await RunGeneratedLinq(generatedLinq);

            bool orderSensitive = sqlInput.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase) >= 0;

            AssertRowsEqual(sqlRows, linqRows, orderSensitive,
                $"\nSQL: {sqlInput}\nGenerált LINQ: {generatedLinq}");
        }

        private List<Dictionary<string, object>> RunRawSql(string sql) {

            var rows = new List<Dictionary<string, object>>();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++) {
                    row[NormalizeKey(reader.GetName(i))] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
            return rows;
        }

        private async Task<List<Dictionary<string, object>>> RunGeneratedLinq(string linqCode) {

            var options = ScriptOptions.Default
                .WithReferences(
                    typeof(object).Assembly,
                    typeof(Enumerable).Assembly,
                    typeof(System.Text.RegularExpressions.Regex).Assembly,
                    typeof(TestDbContext).Assembly)
                .WithImports("System", "System.Linq", "System.Text.RegularExpressions", "System.Collections.Generic");

            var globals = new ScriptGlobals { db = _db };

            object result = await CSharpScript.EvaluateAsync<object>(linqCode, options, globals);

            return ToRowList(result);
        }

        private List<Dictionary<string, object>> ToRowList(object result) {

            if (result == null) return new List<Dictionary<string, object>>();

            if (result is ValueType || result is string) {
                return new List<Dictionary<string, object>> {
                    new Dictionary<string, object> { { "scalar_result", result } }
                };
            }

            if (result is not IEnumerable enumerable) {
                throw new InvalidOperationException(
                    $"[ERROR] The generated LINQ did not return an IEnumerable result: {result.GetType()}");
            }

            var rows = new List<Dictionary<string, object>>();

            foreach (var item in enumerable) {

                var row = new Dictionary<string, object>();

                foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    row[NormalizeKey(prop.Name)] = prop.GetValue(item);
                }
                rows.Add(row);
            }
            return rows;
        }

        private static string NormalizeKey(string name) => name.Replace("_", "").ToLowerInvariant();


        private void AssertRowsEqual(

            List<Dictionary<string, object>> expected,
            List<Dictionary<string, object>> actual,

            bool orderSensitive,
            string context) {

            Assert.That(actual.Count, Is.EqualTo(expected.Count),
                $"[ERROR] Different row count. {context}");

            if (expected.Count == 1 && actual.Count == 1 && expected[0].Count == 1 && actual[0].Count == 1) {

                var expectedVal = expected[0].Values.First();
                var actualVal = actual[0].Values.First();

                string expStr = expectedVal?.ToString() ?? "NULL";
                string actStr = actualVal?.ToString() ?? "NULL";

                Assert.That(actStr, Is.EqualTo(expStr), $"[ERROR] Scalar values differ. {context}");
                return;
            }

            var expectedSerialized = expected.Select(SerializeRow).ToList();
            var actualSerialized = actual.Select(SerializeRow).ToList();

            if (!orderSensitive) {

                expectedSerialized.Sort(StringComparer.Ordinal);
                actualSerialized.Sort(StringComparer.Ordinal);
            }

            Assert.That(actualSerialized, Is.EqualTo(expectedSerialized),
                $"[ERROR] The results are differ. {context}");
        }

        private static string SerializeRow(Dictionary<string, object> row) {
            var parts = row.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                            .Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}");
            return string.Join("|", parts);
        }

        [Test]
        public async Task Randomly_Generated_Fuzz_Sql_Should_Return_Same_Rows_As_Linq() {

            var generator = new RandomSqlGenerator(seed: 1111);
            int testCount = 300;

            string logFilePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "SemanticFuzzOutput.log");
            if (File.Exists(logFilePath)) {
                File.Delete(logFilePath);
            }
            TestContext.Progress.WriteLine($"[INFO] Place of the fuzz log: {logFilePath}");

            for (int i = 0; i < testCount; i++) {

                string sqlInput = generator.NextSelect();
                string generatedLinq = "";

                bool hasOuterJoin =
                    sqlInput.IndexOf("LEFT JOIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sqlInput.IndexOf("RIGHT JOIN", StringComparison.OrdinalIgnoreCase) >= 0;

                bool hasConcat =
                    sqlInput.IndexOf("CONCAT(", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sqlInput.Contains("||");

                if (hasOuterJoin || hasConcat) {
                    File.AppendAllText(logFilePath, $"[{i + 1:D3}/{testCount}] [SKIP] SQL: {sqlInput}\nReason: outer join / CONCAT not supported in CSharpScript semantic test\n--------------------------------------------------\n");
                    continue;
                }

                try {
                    generatedLinq = SqlToLinqConverter.Convert(sqlInput);

                    var sqlRows = RunRawSql(sqlInput);
                    var linqRows = await RunGeneratedLinq(generatedLinq);

                    bool orderSensitive = sqlInput.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase) >= 0;

                    AssertRowsEqual(sqlRows, linqRows, orderSensitive,
                        $"\nSQL: {sqlInput}\nGenerated LINQ: {generatedLinq}");

                    File.AppendAllText(logFilePath, $"[{i + 1:D3}/{testCount}] [PASS] SQL: {sqlInput}\n           LINQ: {generatedLinq}\n--------------------------------------------------\n");

                } catch (NotSupportedException ex) {

                    File.AppendAllText(logFilePath, $"[{i + 1:D3}/{testCount}] [SKIP] SQL: {sqlInput}\nReason: {ex.Message}\n--------------------------------------------------\n");

                } catch (Exception ex) {

                    File.AppendAllText(logFilePath, $"[{i + 1:D3}/{testCount}] [FAIL] SQL: {sqlInput}\n           LINQ: {generatedLinq}\nException: {ex.Message}\n--------------------------------------------------\n");

                    Assert.Fail($"[ERROR] Fuzz failed in {i + 1}. iteration!\nSQL: {sqlInput}\nLINQ: {generatedLinq}\n{ex.Message}");
                    return;
                }
            }
        }

    }
}
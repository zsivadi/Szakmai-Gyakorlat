using System;
using System.Linq;
using SqlToLinq.Core;
using NUnit.Framework;
using System.Reflection;
using System.Collections;
using Microsoft.Data.Sqlite;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;

namespace SqlToLinq.Tests {

    // These tests cover constructs where:
    //
    //   (1) the Roslyn compiler accepts the generated code, but
    //   (2) EF Core may fail to translate it to SQL server-side, OR
    //   (3) subtle type/null semantics differ between in-memory and real providers.
    //
    // Every test here runs via Roslyn + EF Core + SQLite so that EF Core actually
    // translates to SQL — if it cannot, it throws InvalidOperationException, which
    // is exactly what would happen in a real ASP.NET application.

    [TestFixture]
    public class EfCoreEdgeCaseTests {

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

        // LIKE - EF.Functions.Like

        [Test]
        public void Like_Pattern_Translates_Server_Side_Via_Ef_Functions() {
            string sql = "SELECT Name FROM Users WHERE Name LIKE 'B%'";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Not_Like_Pattern_Translates_Server_Side_Via_Ef_Functions() {
            string sql = "SELECT Name FROM Users WHERE Name NOT LIKE 'A%'";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Like_Underscore_Wildcard_Translates_Server_Side() {
            string sql = "SELECT Name FROM Users WHERE Name LIKE 'B_b'";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Like_Case_Sensitivity_Matches_Sql_Behaviour() {
            string sql = "SELECT Name FROM Users WHERE Name LIKE '%ob'";
            AssertSemanticallyEqual(sql);
        }

        // Math functions — explicit casts

        [Test]
        public void Abs_With_Explicit_Cast_Translates_On_Sqlite() {
            string sql = "SELECT ABS(Points) AS AbsPoints FROM Users";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Round_With_Explicit_Cast_Translates_On_Sqlite() {
            string sql = "SELECT ROUND(Age, 0) AS RoundedAge FROM Users";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Floor_With_Explicit_Cast_Translates_On_Sqlite() {
            string sql = "SELECT FLOOR(Age) AS FloorAge FROM Users";
            AssertSemanticallyEqual(sql);
        }

        // Nullable int IN list — type inference

        [Test]
        public void Nullable_Int_In_List_Translates_Correctly() {
            string sql = "SELECT Name FROM Users WHERE Id IN (1, 2, 6)";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Nullable_Int_Not_In_List_Translates_Correctly() {
            string sql = "SELECT Name FROM Users WHERE Id NOT IN (1, 2, 6)";
            AssertSemanticallyEqual(sql);
        }

        // NULL semantics in CASE WHEN

        [Test]
        public void Case_When_Without_Else_Returns_Null_Server_Side() {
            string sql = "SELECT Name, CASE WHEN Age > 30 THEN 'Senior' END AS Label FROM Users";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Case_When_In_Where_Clause_Translates_Server_Side() {
            string sql = "SELECT Name FROM Users WHERE CASE WHEN Age > 18 THEN 1 ELSE 0 END = 1";
            AssertSemanticallyEqual(sql);
        }

        // Scalar subquery vs aggregate return type

        [Test]
        public void Scalar_Subquery_Max_Does_Not_Double_Wrap() {
            string sql = "SELECT Name FROM Users WHERE Age = (SELECT MAX(Age) FROM Users)";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Scalar_Subquery_Count_In_Select_Does_Not_Double_Wrap() {
            string sql = "SELECT Name, (SELECT COUNT(*) FROM Orders o WHERE o.Owner = u.Id) AS OrderCount FROM Users u";
            AssertSemanticallyEqual(sql);
        }

        // LEFT JOIN null propagation

        [Test]
        public void Left_Join_Unmatched_Rows_Have_Null_Right_Side_Server_Side() {
            string sql = "SELECT u.Name, o.Item FROM Users u LEFT JOIN Orders o ON u.Id = o.Owner";
            AssertSemanticallyEqual(sql);
        }

        // Correlated subquery scope

        [Test]
        public void Correlated_Exists_Outer_Param_Resolved_Server_Side() {
            string sql = "SELECT Name FROM Users u WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.Owner = u.Id)";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Correlated_Not_Exists_Outer_Param_Resolved_Server_Side() {
            string sql = "SELECT Name FROM Users u WHERE NOT EXISTS (SELECT 1 FROM Orders o WHERE o.Owner = u.Id)";
            AssertSemanticallyEqual(sql);
        }

        [Test]
        public void Correlated_In_Subquery_Outer_Param_Resolved_Server_Side() {
            string sql = "SELECT Name FROM Users u WHERE u.Id IN (SELECT Owner FROM Orders o WHERE o.Qty > 1)";
            AssertSemanticallyEqual(sql);
        }

        // Date functions — static expected values based on seed data

        [Test]
        public void Year_Extracts_Correct_Year_From_Nullable_DateTime() {

            string sql = "SELECT Name, YEAR(CreatedAt) AS Yr FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["yr"]?.ToString(), Is.EqualTo("2022"), $"LINQ: {linq}");
        }

        [Test]
        public void Month_Extracts_Correct_Month_From_Nullable_DateTime() {

            string sql = "SELECT Name, MONTH(CreatedAt) AS Mo FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["mo"]?.ToString(), Is.EqualTo("1"), $"LINQ: {linq}");
        }

        [Test]
        public void Day_Extracts_Correct_Day_From_Nullable_DateTime() {

            string sql = "SELECT Name, DAY(CreatedAt) AS D FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["d"]?.ToString(), Is.EqualTo("15"), $"LINQ: {linq}");
        }

        [Test]
        public void Year_In_Where_Filters_By_Year() {
            
            string sql = "SELECT Name FROM Users WHERE YEAR(CreatedAt) = 2022";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            var names = rows.Select(r => r["name"]?.ToString()).OrderBy(n => n).ToList();
            Assert.That(names, Is.EqualTo(new[] { "Bab", "Bob" }), $"LINQ: {linq}");
        }

        [Test]
        public void Dateadd_Day_Adds_Correct_Days() {
           
            string sql = "SELECT Name, DATEADD('day', 7, CreatedAt) AS NextWeek FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");

            var nextWeek = (DateTime?)rows[0]["nextweek"];

            Assert.That(nextWeek?.Day, Is.EqualTo(22), $"LINQ: {linq}");
            Assert.That(nextWeek?.Month, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(nextWeek?.Year, Is.EqualTo(2022), $"LINQ: {linq}");
        }

        [Test]
        public void Dateadd_Month_Adds_Correct_Months() {
            
            string sql = "SELECT Name, DATEADD('month', 1, CreatedAt) AS NextMonth FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");

            var nextMonth = (DateTime?)rows[0]["nextmonth"];

            Assert.That(nextMonth?.Month, Is.EqualTo(2), $"LINQ: {linq}");
            Assert.That(nextMonth?.Year, Is.EqualTo(2022), $"LINQ: {linq}");
        }

        [Test]
        public void Getdate_Returns_DateTime_Greater_Than_All_Seed_Dates() {
            
            string sql = "SELECT Name FROM Users WHERE CreatedAt < GETDATE()";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(6), $"All 6 users should pass. LINQ: {linq}");
        }

        [Test]
        public void Datepart_Year_Extracts_Correct_Year() {

            string sql = "SELECT Name, DATEPART('year', CreatedAt) AS Yr FROM Users WHERE Name = 'Alice'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["yr"]?.ToString(), Is.EqualTo("2024"), $"LINQ: {linq}");
        }

        [Test]
        public void Datepart_Month_Extracts_Correct_Month() {
            
            string sql = "SELECT Name, DATEPART('month', CreatedAt) AS Mo FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["mo"]?.ToString(), Is.EqualTo("1"), $"LINQ: {linq}");
        }

        [Test]
        public void Datepart_Day_Extracts_Correct_Day() {
            
            string sql = "SELECT Name, DATEPART('day', CreatedAt) AS D FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["d"]?.ToString(), Is.EqualTo("15"), $"LINQ: {linq}");
        }

        [Test]
        public void Dateadd_Year_Adds_Correct_Years() {
            
            string sql = "SELECT Name, DATEADD('year', 1, CreatedAt) AS NextYear FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");

            var nextYear = (DateTime?)rows[0]["nextyear"];

            Assert.That(nextYear?.Year, Is.EqualTo(2023), $"LINQ: {linq}");
            Assert.That(nextYear?.Month, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(nextYear?.Day, Is.EqualTo(15), $"LINQ: {linq}");
        }

        [Test]
        public void Datediff_Year_Between_Dates() {
            
            string sql = "SELECT Name, DATEDIFF('year', CreatedAt, GETDATE()) AS Yrs FROM Users WHERE Name = 'bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");

            var yrs = Convert.ToInt32(rows[0]["yrs"]);

            Assert.That(yrs, Is.GreaterThanOrEqualTo(3), $"LINQ: {linq}");
        }

        [Test]
        public void Datediff_Day_Between_Dates() {
            
            string sql = "SELECT Name, DATEDIFF('day', CreatedAt, GETDATE()) AS Days FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");

            var days = Convert.ToInt32(rows[0]["days"]);

            Assert.That(days, Is.GreaterThanOrEqualTo(700), $"LINQ: {linq}");
        }

        [Test]
        public void Hour_Extracts_Hour_From_Utc_DateTime() {
            
            string sql = "SELECT Name, HOUR(CreatedAt) AS Hr FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["hr"]?.ToString(), Is.EqualTo("0"), $"LINQ: {linq}");
        }

        [Test]
        public void Minute_Extracts_Minute_From_Utc_DateTime() {

            string sql = "SELECT Name, MINUTE(CreatedAt) AS Mi FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["mi"]?.ToString(), Is.EqualTo("0"), $"LINQ: {linq}");
        }

        [Test]
        public void Second_Extracts_Second_From_Utc_DateTime() {

            string sql = "SELECT Name, SECOND(CreatedAt) AS Sc FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(1), $"LINQ: {linq}");
            Assert.That(rows[0]["sc"]?.ToString(), Is.EqualTo("0"), $"LINQ: {linq}");
        }

        [Test]
        public void Current_Date_Filters_All_Past_Seed_Dates() {

            string sql = "SELECT Name FROM Users WHERE CreatedAt < CURRENT_DATE";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(6), $"All 6 users should pass. LINQ: {linq}");
        }

        [Test]
        public void Current_Timestamp_Filters_All_Past_Seed_Dates() {

            string sql = "SELECT Name FROM Users WHERE CreatedAt < CURRENT_TIMESTAMP";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows.Count, Is.EqualTo(6), $"All 6 users should pass. LINQ: {linq}");
        }

        // String functions — static expected values

        [Test]
        public void Ltrim_Removes_Leading_Spaces() {

            string sql = "SELECT LTRIM(Name) AS T FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["t"]?.ToString(), Is.EqualTo("Bob"), $"LINQ: {linq}");
        }

        [Test]
        public void Rtrim_Removes_Trailing_Spaces() {

            string sql = "SELECT RTRIM(Name) AS T FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["t"]?.ToString(), Is.EqualTo("Bob"), $"LINQ: {linq}");
        }

        [Test]
        public void Concat_Joins_Two_Strings() {

            string sql = "SELECT CONCAT(Name, Role) AS Full FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["full"]?.ToString(), Is.EqualTo("BobAdmin"), $"LINQ: {linq}");
        }

        [Test]
        public void Instr_Returns_Position_Of_Substring() {
            
            string sql = "SELECT INSTR(Name, 'o') AS Pos FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["pos"]?.ToString(), Is.EqualTo("2"), $"LINQ: {linq}");
        }

        [Test]
        public void Charindex_Returns_Position_Of_Substring() {
            
            string sql = "SELECT CHARINDEX('o', Name) AS Pos FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["pos"]?.ToString(), Is.EqualTo("2"), $"LINQ: {linq}");
        }

        [Test]
        public void Reverse_Reverses_String() {
            
            string sql = "SELECT REVERSE(Name) AS Rev FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["rev"]?.ToString(), Is.EqualTo("boB"), $"LINQ: {linq}");
        }

        [Test]
        public void Repeat_Repeats_String() {
            
            string sql = "SELECT REPEAT(Name, 2) AS Rep FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["rep"]?.ToString(), Is.EqualTo("BobBob"), $"LINQ: {linq}");
        }

        [Test]
        public void Space_Generates_Correct_Number_Of_Spaces() {

            string sql = "SELECT SPACE(3) AS Spaces FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["spaces"]?.ToString(), Is.EqualTo("   "), $"LINQ: {linq}");
        }

        [Test]
        public void Lcase_Lowercases_String() {

            string sql = "SELECT LCASE(Name) AS Lower FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["lower"]?.ToString(), Is.EqualTo("bob"), $"LINQ: {linq}");
        }

        [Test]
        public void Ucase_Uppercases_String() {

            string sql = "SELECT UCASE(Name) AS Upper FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["upper"]?.ToString(), Is.EqualTo("BOB"), $"LINQ: {linq}");
        }

        [Test]
        public void Replace_Replaces_Substring() {

            string sql = "SELECT REPLACE(Name, 'Bob', 'Robert') AS NewName FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["newname"]?.ToString(), Is.EqualTo("Robert"), $"LINQ: {linq}");
        }

        [Test]
        public void Mod_Returns_Correct_Remainder() {
            
            string sql = "SELECT Name, MOD(Age, 2) AS AgeRem FROM Users WHERE Name = 'Bob'";
            string linq = SqlToLinqConverter.Convert(sql);
            var rows = RunGeneratedLinq(linq);

            Assert.That(rows[0]["agerem"]?.ToString(), Is.EqualTo("1"), $"LINQ: {linq}");
        }


        private void AssertSemanticallyEqual(string sql, bool orderSensitive = false) {

            var sqlRows = RunRawSql(sql);
            string linq = SqlToLinqConverter.Convert(sql);
            var linqRows = RunGeneratedLinq(linq);

            Assert.That(linqRows.Count, Is.EqualTo(sqlRows.Count),
                $"[ERROR] Row count differs.\nSQL: {sql}\nLINQ: {linq}");

            var expected = sqlRows.Select(SerializeRow).ToList();
            var actual = linqRows.Select(SerializeRow).ToList();

            if (!orderSensitive) {
                expected.Sort(StringComparer.Ordinal);
                actual.Sort(StringComparer.Ordinal);
            }

            Assert.That(actual, Is.EqualTo(expected),
                $"[ERROR] Results differ.\nSQL: {sql}\nLINQ: {linq}");
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

        private List<Dictionary<string, object>> RunGeneratedLinq(string linqCode) {

            string source = $@"
                using System;
                using System.Linq;
                using System.Collections.Generic;
                using System.Text.RegularExpressions;
                using Microsoft.EntityFrameworkCore;
                using SqlToLinq.Tests;

                public static class LinqRunner {{
                    public static object Run(TestDbContext db) {{
                        return {linqCode};
                    }}
            }}";

            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .ToList();

            var compilation = CSharpCompilation.Create(
                assemblyName: "EfCoreEdgeCaseRunner",
                syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new System.IO.MemoryStream();
            var result = compilation.Emit(ms);

            if (!result.Success) {

                var errors = string.Join("\n", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));

                throw new InvalidOperationException(
                    $"[ERROR] Roslyn compilation failed.\nLINQ: {linqCode}\nErrors:\n{errors}");
            }

            ms.Seek(0, System.IO.SeekOrigin.Begin);

            var assembly = Assembly.Load(ms.ToArray());
            var type = assembly.GetType("LinqRunner");
            var method = type.GetMethod("Run");

            return ToRowList(method.Invoke(null, new object[] { _db }));
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
                    $"[ERROR] Generated LINQ did not return IEnumerable: {result.GetType()}");
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

        private static string NormalizeValue(object value) {

            if (value == null) return "NULL";
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is decimal m) return m.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return value?.ToString() ?? "NULL";
        }

        private static string SerializeRow(Dictionary<string, object> row) {
            return string.Join("|", row.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                       .Select(kv => $"{kv.Key}={NormalizeValue(kv.Value)}"));
        }
    }
}
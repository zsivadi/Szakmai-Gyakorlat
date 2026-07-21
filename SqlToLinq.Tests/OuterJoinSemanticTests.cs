using System;
using System.Linq;
using SqlToLinq.Core;
using NUnit.Framework;
using System.Reflection;
using System.Collections;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace SqlToLinq.Tests {

    // --- Dedicated schema for outer join testing ----------------------------------
    //
    // The seed data is designed so that unmatched rows exist on BOTH sides of every
    // join relationship. This lets the tests verify that LEFT JOIN preserves left-only
    // rows with nulls on the right, and RIGHT JOIN preserves right-only rows with
    // nulls on the left — something that cannot be tested with a "complete" dataset
    // where every foreign key has a matching primary key.
    //
    // Customers:  1 Alice, 2 Bob, 3 Carol (no orders), 4 Dave (no orders)
    // Invoices:   1→1, 2→1, 3→2, 4→99 (owner 99 does not exist → RIGHT JOIN test row)

    public record Customer(int Id, string Name, string City);

    public record Invoice(int Id, int? Owner, string Product, int? Amount);

    public class OuterJoinDbContext : DbContext {

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Invoice> Invoices { get; set; }

        private readonly SqliteConnection _connection;

        public OuterJoinDbContext(SqliteConnection connection) {
            _connection = connection;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            optionsBuilder.UseSqlite(_connection);
        }
    }

    public class OuterJoinScriptGlobals {
        public OuterJoinDbContext db;
    }

    // --- Test fixture -------------------------------------------------------------

    [TestFixture]
    public class OuterJoinSemanticTests {

        private SqliteConnection _connection;
        private OuterJoinDbContext _db;

        [SetUp]
        public void SetUp() {

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            _db = new OuterJoinDbContext(_connection);
            _db.Database.EnsureCreated();

            _db.Customers.AddRange(
                new Customer(1, "Alice", "Budapest"),
                new Customer(2, "Bob", "Debrecen"),
                new Customer(3, "Carol", "Pécs"),       // no invoices → LEFT JOIN null row
                new Customer(4, "Dave", "Győr")        // no invoices → LEFT JOIN null row
            );

            _db.Invoices.AddRange(
                new Invoice(1, 1, "Laptop", 1200),  // Alice
                new Invoice(2, 1, "Mouse", 25),   // Alice
                new Invoice(3, 2, "Keyboard", 75),   // Bob
                new Invoice(4, 99, "Monitor", 350)    // owner 99 does not exist → RIGHT JOIN null row
            );

            _db.SaveChanges();
        }

        [TearDown]
        public void TearDown() {
            _db.Dispose();
            _connection.Dispose();
        }

        // --- LEFT JOIN -----------------------------------------------------------

        [Test]
        public async Task Left_Join_Preserves_Unmatched_Left_Rows() {

            // Carol and Dave have no invoices → they appear with null product/amount.
            // 4 customers × their invoices: Alice=2, Bob=1, Carol=0→1 null, Dave=0→1 null = 6 rows total.
            string sql = "SELECT c.name, i.product FROM Customers c LEFT JOIN Invoices i ON c.id = i.owner";
            await AssertSemanticallyEqual(sql);
        }

        [Test]
        public async Task Left_Join_Where_On_Left_Table() {

            // Only Alice and Bob pass age > 0 (effectively all matched customers).
            // Carol and Dave still appear because WHERE is on the left (preserved) side.
            string sql = "SELECT c.name, i.product FROM Customers c LEFT JOIN Invoices i ON c.id = i.owner WHERE c.city = 'Budapest'";
            await AssertSemanticallyEqual(sql);
        }

        [Test]
        public async Task Left_Join_Where_On_Right_Table_Filters_Nulls() {

            // WHERE on the right side implicitly removes null rows (Carol, Dave drop out).
            string sql = "SELECT c.name, i.product FROM Customers c LEFT JOIN Invoices i ON c.id = i.owner WHERE i.product = 'Laptop'";
            await AssertSemanticallyEqual(sql);
        }

        [Test]
        public async Task Left_Join_Order_By_Left_Column() {

            string sql = "SELECT c.name, i.product FROM Customers c LEFT JOIN Invoices i ON c.id = i.owner ORDER BY c.name ASC";
            await AssertSemanticallyEqual(sql, orderSensitive: true);
        }

        [Test]
        public async Task Left_Join_Limit() {

            string sql = "SELECT c.name, i.product FROM Customers c LEFT JOIN Invoices i ON c.id = i.owner ORDER BY c.name ASC LIMIT 3";
            await AssertSemanticallyEqual(sql, orderSensitive: true);
        }

        [Test]
        public async Task Left_Outer_Join_Explicit_Outer_Keyword() {

            string sql = "SELECT c.name, i.product FROM Customers c LEFT OUTER JOIN Invoices i ON c.id = i.owner";
            await AssertSemanticallyEqual(sql);
        }

        [Test]
        public async Task Left_Join_Reversed_On_Condition() {

            string sql = "SELECT c.name, i.product FROM Customers c LEFT JOIN Invoices i ON i.owner = c.id";
            await AssertSemanticallyEqual(sql);
        }

        // --- RIGHT JOIN ----------------------------------------------------------

        [Test]
        public async Task Right_Join_Preserves_Unmatched_Right_Rows() {

            // Invoice 4 has Owner=99 which does not exist → appears with null customer columns.
            // Alice=2, Bob=1, Invoice4=1 null customer = 4 rows total.
            string sql = "SELECT c.name, i.product FROM Customers c RIGHT JOIN Invoices i ON c.id = i.owner";
            await AssertSemanticallyEqual(sql);
        }

        [Test]
        public async Task Right_Join_Where_On_Right_Table() {

            string sql = "SELECT c.name, i.product FROM Customers c RIGHT JOIN Invoices i ON c.id = i.owner WHERE i.product = 'Monitor'";
            await AssertSemanticallyEqual(sql);
        }

        [Test]
        public async Task Right_Join_Reversed_On_Condition() {

            string sql = "SELECT c.name, i.product FROM Customers c RIGHT JOIN Invoices i ON i.owner = c.id";
            await AssertSemanticallyEqual(sql);
        }

        [Test]
        public async Task Right_Join_Order_By_Right_Column() {

            string sql = "SELECT c.name, i.product FROM Customers c RIGHT JOIN Invoices i ON c.id = i.owner ORDER BY i.product ASC";
            await AssertSemanticallyEqual(sql, orderSensitive: true);
        }

        [Test]
        public async Task Right_Outer_Join_Explicit_Outer_Keyword() {

            string sql = "SELECT c.name, i.product FROM Customers c RIGHT OUTER JOIN Invoices i ON c.id = i.owner";
            await AssertSemanticallyEqual(sql);
        }

        // --- INNER JOIN (for contrast) -------------------------------------------

        [Test]
        public async Task Inner_Join_Drops_Both_Unmatched_Sides() {

            // Carol, Dave (no invoices) and Invoice 4 (no customer) all drop out.
            string sql = "SELECT c.name, i.product FROM Customers c JOIN Invoices i ON c.id = i.owner";
            await AssertSemanticallyEqual(sql);
        }

        // --- Helpers -------------------------------------------------------------

        private async Task AssertSemanticallyEqual(string sql, bool orderSensitive = false) {

            var sqlRows = RunRawSql(sql);

            string generatedLinq = SqlToLinqConverter.Convert(sql);
            var linqRows = await RunGeneratedLinq(generatedLinq);

            Assert.That(linqRows.Count, Is.EqualTo(sqlRows.Count),
                $"[ERROR] Row count differs.\nSQL: {sql}\nLINQ: {generatedLinq}\nExpected: {sqlRows.Count}, Got: {linqRows.Count}");

            var expectedSerialized = sqlRows.Select(SerializeRow).ToList();
            var actualSerialized = linqRows.Select(SerializeRow).ToList();

            if (!orderSensitive) {
                expectedSerialized.Sort(StringComparer.Ordinal);
                actualSerialized.Sort(StringComparer.Ordinal);
            }

            Assert.That(actualSerialized, Is.EqualTo(expectedSerialized),
                $"[ERROR] Results differ.\nSQL: {sql}\nLINQ: {generatedLinq}");
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
                    typeof(OuterJoinDbContext).Assembly)
                .WithImports("System", "System.Linq", "System.Text.RegularExpressions", "System.Collections.Generic");

            var globals = new OuterJoinScriptGlobals { db = _db };

            object result = await CSharpScript.EvaluateAsync<object>(linqCode, options, globals);

            return ToRowList(result);
        }

        private List<Dictionary<string, object>> ToRowList(object result) {

            if (result == null) return new List<Dictionary<string, object>>();

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

        private static string SerializeRow(Dictionary<string, object> row) {
            return string.Join("|", row.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                       .Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"));
        }
    }
}
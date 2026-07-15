using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace SqlToLinq.Tests {

    public class RandomSqlGenerator {

        private readonly Random _rng;

        private static readonly string[] Tables = { "Users" };

        private static readonly string[] StringColumns = { "Name", "Role" };
        private static readonly string[] NumericColumns = { "Age", "Points", "Bonus", "Id" };
        private static readonly string[] AllColumns = StringColumns.Concat(NumericColumns).ToArray();

        private static readonly string[] StringOps = { "=", "<>" };
        private static readonly string[] NumericOps = { "=", "<>", ">=", "<=", ">", "<" };

        private static readonly string[] LikePatterns = { "B%", "_ob", "B_b", "%o%", "Ali%", "B.b", "%", "A%", "%in" };

        private static readonly string[] StringValues = { "Bob", "Alice", "Admin", "User", "Moderator", "Bab", "Bcb", "" };
        private static readonly string[] NumericValues = { "0", "5", "10", "15", "20", "50", "80", "100", "200" };

        private static readonly string[] RoleValues = { "Admin", "User", "Moderator" };
        private static readonly string[] NameValues = { "Bob", "Alice", "Bab" };

        public RandomSqlGenerator(int seed) {
            _rng = new Random(seed);
        }

        public string NextSelect() {

            string table = Pick(Tables);
            bool distinct = _rng.Next(4) == 0;
            string columns = BuildColumnList();

            var sql = new StringBuilder($"SELECT {(distinct ? "DISTINCT " : "")}{columns} FROM {table}");

            if (_rng.Next(2) == 0) {
                sql.Append($" WHERE {RandomCondition(depth: 0)}");
            }

            bool hasGroupBy = !distinct && _rng.Next(3) == 0;
            if (hasGroupBy) {
                sql.Append($" GROUP BY {Pick(AllColumns)}");

                if (_rng.Next(2) == 0) {
                    sql.Append($" HAVING COUNT(*) {Pick(NumericOps)} {_rng.Next(1, 6)}");
                }
            }

            if (_rng.Next(2) == 0) {

                // With DISTINCT, ORDER BY must use a column that is in the SELECT list,
                // otherwise the sort order after deduplication is undefined and the
                // visitor will reject the query.
                string orderCol;
                if (distinct && columns != "*") {
                    var selectCols = columns.Split(',')
                        .Select(c => c.Trim().Split(' ')[0])
                        .Where(c => !c.StartsWith("CASE"))
                        .ToArray();
                    orderCol = selectCols.Length > 0 ? Pick(selectCols) : Pick(AllColumns);
                } else {
                    orderCol = Pick(AllColumns);
                }

                sql.Append($" ORDER BY {orderCol} {(_rng.Next(2) == 0 ? "ASC" : "DESC")}");
            }

            if (_rng.Next(3) == 0) {

                int limit = _rng.Next(1, 5);
                sql.Append($" LIMIT {limit}");

                if (_rng.Next(2) == 0) {
                    sql.Append($" OFFSET {_rng.Next(0, 3)}");
                }
            }

            return sql.ToString() + ";";
        }

        private string BuildColumnList() {

            if (_rng.Next(3) == 0) return "*";

            var cols = RandomSubset(AllColumns).ToList();

            if (_rng.Next(4) == 0 && cols.Count > 0) {
                int idx = _rng.Next(cols.Count);
                cols[idx] = $"{RandomCaseExpr()} AS CaseResult";
            }

            return string.Join(", ", cols);
        }


        private string RandomCondition(int depth) {

            if (depth >= 2 || _rng.Next(3) == 0) {
                return RandomSimpleCondition();
            }

            if (_rng.Next(5) == 0) {
                return $"NOT ({RandomCondition(depth + 1)})";
            }

            string left = RandomCondition(depth + 1);
            string right = RandomCondition(depth + 1);
            string op = _rng.Next(2) == 0 ? "AND" : "OR";

            return _rng.Next(2) == 0 ? $"({left} {op} {right})" : $"{left} {op} {right}";
        }

        private string RandomSimpleCondition() {

            int kind = _rng.Next(10);

            if (kind < 4) return RandomCompareCondition();
            if (kind < 6) return RandomLikeCondition();
            if (kind < 8) return RandomBetweenCondition();
            if (kind < 9) return RandomInCondition();

            return RandomIsNullCondition();
        }

        private string RandomCompareCondition() {

            if (_rng.Next(2) == 0) {
                string col = Pick(StringColumns);
                return $"{col} {Pick(StringOps)} '{Pick(StringValues)}'";
            } else {
                string col = Pick(NumericColumns);
                string val = col == "Age" ? _rng.Next(15, 50).ToString() : Pick(NumericValues);
                return $"{col} {Pick(NumericOps)} {val}";
            }
        }

        private string RandomLikeCondition() {

            string col = Pick(StringColumns);
            string not = _rng.Next(3) == 0 ? "NOT " : "";
            return $"{col} {not}LIKE '{Pick(LikePatterns)}'";
        }

        private string RandomBetweenCondition() {

            string not = _rng.Next(3) == 0 ? "NOT " : "";
            string col = Pick(NumericColumns);
            int a = _rng.Next(0, 100), b = _rng.Next(0, 100);

            return $"{col} {not}BETWEEN {Math.Min(a, b)} AND {Math.Max(a, b)}";
        }

        private string RandomInCondition() {

            string not = _rng.Next(3) == 0 ? "NOT " : "";

            if (_rng.Next(2) == 0) {

                string col = Pick(StringColumns);

                var values = RandomSubset(col == "Role" ? RoleValues : NameValues)
                    .Select(v => $"'{v}'");
                string list = string.Join(", ", values);

                if (string.IsNullOrEmpty(list)) list = "'Admin'";

                return $"{col} {not}IN ({list})";
            } else {

                string col = Pick(NumericColumns);
                int count = _rng.Next(1, 4);

                string list = string.Join(", ", Enumerable.Range(0, count).Select(_ => _rng.Next(0, 200).ToString()));

                return $"{col} {not}IN ({list})";
            }
        }

        private string RandomIsNullCondition() {

            string col = Pick(StringColumns);
            string not = _rng.Next(2) == 0 ? "NOT " : "";

            return $"{col} IS {not}NULL";
        }

        private string RandomCaseExpr() {

            int branches = _rng.Next(1, 3);
            var sb = new StringBuilder("CASE");

            bool useNumeric = _rng.Next(2) == 0;

            for (int i = 0; i < branches; i++) {

                string cond = RandomSimpleCondition();
                string result = useNumeric
                    ? _rng.Next(0, 100).ToString()
                    : $"'{Pick(StringValues)}'";

                sb.Append($" WHEN {cond} THEN {result}");
            }

            string elseVal = useNumeric
                ? _rng.Next(0, 100).ToString()
                : $"'{Pick(StringValues)}'";

            sb.Append($" ELSE {elseVal} END");

            return sb.ToString();
        }

        private IEnumerable<string> RandomSubset(string[] items) {

            var subset = items.Where(_ => _rng.Next(2) == 0).ToList();
            return subset.Count > 0 ? subset : new List<string> { Pick(items) };
        }

        private IEnumerable<string> RandomSubset(IEnumerable<string> items) =>
            RandomSubset(items.ToArray());

        private string Pick(string[] items) => items[_rng.Next(items.Length)];
    }
}
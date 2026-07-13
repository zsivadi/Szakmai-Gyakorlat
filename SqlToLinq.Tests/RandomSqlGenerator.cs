using System;
using System.Collections.Generic;
using System.Linq;

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
        private static readonly string[] StringWords = { "Bob", "Alice", "Admin", "User", "Moderator", "Bab", "Bcb", "" };
        private static readonly string[] NumericWords = { "0", "5", "10", "15", "20", "50", "80", "100", "200" };

        public RandomSqlGenerator(int seed) {
            _rng = new Random(seed);
        }

        // Generates a random SQL SELECT statement with optional WHERE, GROUP BY, HAVING, and ORDER BY clauses. 
        public string NextSelect() {

            string table = Pick(Tables);
            string columns = _rng.Next(2) == 0 ? "*" : string.Join(", ", RandomSubset(AllColumns));

            string sql = $"SELECT {columns} FROM {table}";

            if (_rng.Next(2) == 0) {
                sql += $" WHERE {RandomCondition(depth: 0)}";
            }

            bool hasGroupBy = _rng.Next(3) == 0;
            if (hasGroupBy) {
                sql += $" GROUP BY {Pick(AllColumns)}";

                if (_rng.Next(2) == 0) {
                    sql += $" HAVING COUNT(*) {Pick(NumericOps)} {_rng.Next(1, 10)}";
                }
            }

            if (_rng.Next(2) == 0) {
                sql += $" ORDER BY {Pick(AllColumns)} {(_rng.Next(2) == 0 ? "ASC" : "DESC")}";
            }

            if (_rng.Next(2) == 0) {
                sql += $" LIMIT {_rng.Next(1, 4)} {(_rng.Next(2) == 0 ? $"OFFSET {_rng.Next(1, 3)}" : "")}";
            }

            return sql + ";";
        }

        private string RandomCondition(int depth) {

            // Depth limit to avoid too deep recursion 
            if (depth >= 2 || _rng.Next(3) == 0) {
                return RandomSimpleCondition();
            }

            string left = RandomCondition(depth + 1);
            string right = RandomCondition(depth + 1);
            string op = _rng.Next(2) == 0 ? "AND" : "OR";

            return _rng.Next(2) == 0 ? $"({left} {op} {right})" : $"{left} {op} {right}";
        }

        private string RandomSimpleCondition() {

            bool isStringCondition = _rng.Next(2) == 0;

            if (isStringCondition) {

                string column = Pick(StringColumns);

                if (_rng.Next(3) == 0) {
                    return $"{column} LIKE '{Pick(LikePatterns)}'";
                }

                string op = Pick(StringOps);
                string value = $"'{Pick(StringWords)}'";

                return $"{column} {op} {value}";
            } else {

                string column = Pick(NumericColumns);
                string op = Pick(NumericOps);
                string value = Pick(NumericWords);

                if (column == "Age" && _rng.Next(2) == 0) {
                    value = _rng.Next(15, 50).ToString();
                }

                return $"{column} {op} {value}";
            }
        }

        private IEnumerable<string> RandomSubset(string[] items) {
            var subset = items.Where(_ => _rng.Next(2) == 0).ToList();
            return subset.Count > 0 ? subset : new List<string> { Pick(items) };
        }

        private string Pick(string[] items) => items[_rng.Next(items.Length)];
    }
}
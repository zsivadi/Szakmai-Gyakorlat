using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlToLinq.Tests {
    
    public class RandomSqlGenerator {

        private readonly Random _rng;

        private static readonly string[] Tables = { "Users" };
        private static readonly string[] Columns = { "Name", "Role" };
        private static readonly string[] CompOps = { "=", "<>" };
        private static readonly string[] LikePatterns = { "B%", "_ob", "B_b", "%o%", "Ali%", "B.b", "%" };
        private static readonly string[] Words = { "Bob", "Alice", "Miskolc", "Eger", "" };

        public RandomSqlGenerator(int seed) {
            _rng = new Random(seed);
        }

        // Generates a random SQL SELECT statement with optional WHERE, GROUP BY, HAVING, and ORDER BY clauses. 

        public string NextSelect() {

            string table = Pick(Tables);
            string columns = _rng.Next(2) == 0 ? "*" : string.Join(", ", RandomSubset(Columns));

            string sql = $"SELECT {columns} FROM {table}";

            if (_rng.Next(2) == 0) {
                sql += $" WHERE {RandomCondition(depth: 0)}";
            }

            bool hasGroupBy = _rng.Next(3) == 0;
            if (hasGroupBy) {
                sql += $" GROUP BY {Pick(Columns)}";

                if (_rng.Next(2) == 0) {
                    sql += $" HAVING COUNT(*) {Pick(CompOps)} {_rng.Next(1, 10)}";
                }
            }

            if (_rng.Next(2) == 0) {
                sql += $" ORDER BY {Pick(Columns)} {(_rng.Next(2) == 0 ? "ASC" : "DESC")}";
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

            string column = Pick(Columns);

            if (_rng.Next(3) == 0) {
                return $"{column} LIKE '{Pick(LikePatterns)}'";
            }

            string op = Pick(CompOps);
            string value = column == "Age" ? _rng.Next(0, 100).ToString() : $"'{Pick(Words)}'";

            return $"{column} {op} {value}";
        }

        private IEnumerable<string> RandomSubset(string[] items) {
            var subset = items.Where(_ => _rng.Next(2) == 0).ToList();
            return subset.Count > 0 ? subset : new List<string> { Pick(items) };
        }

        private string Pick(string[] items) => items[_rng.Next(items.Length)];
    }
}

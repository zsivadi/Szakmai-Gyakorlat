using System.Linq;
using System.Net.Http.Headers;
using System.Collections.Generic;

namespace SqlToLinq.Core {

    public abstract class LinqNode {
        public abstract string ToCodeString();
    }

    // A LINQ query root node

    public class LinqQueryNode : LinqNode {

        // Name of the source table 
        public string SourceTable { get; set; }

        // List of chained LINQ method calls or join nodes
        public List<LinqNode> Methods { get; set; } = new List<LinqNode>();

        // Generates the LINQ query code as a string
        public override string ToCodeString() {

            string result = $"db.{SourceTable}";

            foreach (var method in Methods) {
                result += method.ToCodeString();
            }
            return result;
        }

        public string ToCodeStringUpToSelect() {

            string result = $"db.{SourceTable}";

            foreach (var method in Methods) {
                if (method is LinqMethodCallNode m &&
                    (m.MethodName == "Select" || m.MethodName == "ToList")) break;
                result += method.ToCodeString();
            }
            return result;
        }
    }

    // A LINQ method call

    public class LinqMethodCallNode : LinqNode {

        // Name of the LINQ method (e.g., "Where", "Select", "OrderBy")
        public string MethodName { get; set; }

        // List of arguments for the method call (e.g., lambda expressions, constants)
        public List<LinqNode> Arguments { get; set; } = new List<LinqNode>();

        // Generates the method call code as a string, including the method name and its arguments
        public override string ToCodeString() {

            if (Arguments == null || Arguments.Count == 0) {
                return $".{MethodName}()";
            }

            var argsStr = string.Join(", ", Arguments.Select(a => a.ToCodeString()));
            return $".{MethodName}({argsStr})";
        }
    }

    // A LINQ lambda expression

    public class LinqLambdaNode : LinqNode {

        // The name of the parameter used in the lambda expression 
        public string ParameterName { get; set; }

        // The body of the lambda expression
        public LinqNode Body { get; set; }

        // Generates the lambda expression code as a string in the form "parameter => body"
        public override string ToCodeString() {
            return $"{ParameterName} => {Body.ToCodeString()}";
        }
    }

    // Binary operations

    public class LinqBinaryExpressionNode : LinqNode {

        // Left side
        public LinqNode Left { get; set; }

        // Operator
        public string Operator { get; set; }

        // Right side
        public LinqNode Right { get; set; }

        public override string ToCodeString() {
            return $"{Left.ToCodeString()} {Operator} {Right.ToCodeString()}";
        }
    }

    // NOT (unary negation)

    public class LinqUnaryExpressionNode : LinqNode {

        // The C# prefix operator (currently only "!")
        public string Operator { get; set; }

        // The negated expression
        public LinqNode Operand { get; set; }

        public override string ToCodeString() {
            return $"{Operator}({Operand.ToCodeString()})";
        }
    }

    // IN (literal list)  or  IN (subquery)

    public class LinqInExpressionNode : LinqNode {

        public LinqNode Target { get; set; }

        public List<LinqNode> Values { get; set; } = new List<LinqNode>();

        public LinqSubqueryNode Subquery { get; set; }

        public override string ToCodeString() {

            if (Subquery != null) {

                string inner = ExtractScalarInnerQuery(Subquery.Inner);
                return $"({inner}).Contains({Target.ToCodeString()})";
            }

            var valuesStr = string.Join(", ", Values.Select(v => v.ToCodeString()));
            bool allNumeric = Values.All(v => v is LinqConstantNode c && c.Value is not string);
            string arrayType = allNumeric ? "int?" : "string";

            return $"new {arrayType}[] {{ {valuesStr} }}.Contains({Target.ToCodeString()})";
        }

        private static string ExtractScalarInnerQuery(LinqNode inner) {

            if (inner is not LinqQueryNode qn) {

                string raw = inner.ToCodeString();

                if (raw.EndsWith(".ToList()")) raw = raw.Substring(0, raw.Length - ".ToList()".Length);
                return raw;
            }

            var sb = new System.Text.StringBuilder($"db.{qn.SourceTable}");

            LinqMethodCallNode scalarSelect = null;

            foreach (var method in qn.Methods) {

                if (method is LinqMethodCallNode m) {
                    if (m.MethodName == "ToList") break;
                    if (m.MethodName == "Select") { scalarSelect = m; break; }
                }
                sb.Append(method.ToCodeString());
            }

            if (scalarSelect?.Arguments.Count == 1 &&
                scalarSelect.Arguments[0] is LinqLambdaNode lambda &&
                lambda.Body is LinqAnonymousObjectNode anon &&
                anon.Properties.Count == 1) {

                string param = lambda.ParameterName;
                string colExpr = anon.Properties[0].Expression.ToCodeString();

                sb.Append($".Select({param} => {colExpr})");
            } else if (scalarSelect != null) {
                sb.Append(scalarSelect.ToCodeString());
            }

            return sb.ToString();
        }
    }

    // Identifiers 

    public class LinqIdentifierNode : LinqNode {

        // The name of the identifier 
        public string Name { get; set; }

        public override string ToCodeString() {
            return Name;
        }
    }

    // Constant values

    public class LinqConstantNode : LinqNode {

        // The constant value 
        public object Value { get; set; }

        public override string ToCodeString() {

            if (Value is string str) {
                return $"\"{str}\"";
            }
            if (Value is bool b) {
                return b ? "true" : "false";
            }
            if (Value is double d) {
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return Value?.ToString() ?? "null";
        }
    }

    // Brackets

    public class LinqParensNode : LinqNode {

        // The inner expression inside the parentheses
        public LinqNode InnerNode { get; set; }

        public override string ToCodeString() {
            return $"({InnerNode.ToCodeString()})";
        }
    }

    // New anon object for Select()

    public class LinqAnonymousObjectNode : LinqNode {

        // A list of properties in the form of (property name, expression) pairs
        // The property name can be empty for anonymous properties
        public List<(string Name, LinqNode Expression)> Properties { get; set; } = new List<(string, LinqNode)>();

        // Generates the code for creating a new anonymous object 
        public override string ToCodeString() {

            var props = Properties.Select(p => {

                // If the property name is empty, just return the expression without a name

                if (string.IsNullOrEmpty(p.Name)) {
                    return p.Expression.ToCodeString();
                }
                return $"{p.Name} = {p.Expression.ToCodeString()}";
            });
            return $"new {{ {string.Join(", ", props)} }}";
        }
    }

    public class LinqStringFunctionNode : LinqNode {

        public string FunctionName { get; set; }

        public List<LinqNode> Arguments { get; set; } = new List<LinqNode>();

        public override string ToCodeString() {

            string arg0 = Arguments.Count > 0 ? Arguments[0].ToCodeString() : "";
            string arg1 = Arguments.Count > 1 ? Arguments[1].ToCodeString() : "";
            string arg2 = Arguments.Count > 2 ? Arguments[2].ToCodeString() : "";

            return FunctionName.ToUpperInvariant() switch {

                "UPPER" => $"{arg0}.ToUpper()",
                "LOWER" => $"{arg0}.ToLower()",
                "TRIM" => $"{arg0}.Trim()",
                "LTRIM" => $"{arg0}.TrimStart()",
                "RTRIM" => $"{arg0}.TrimEnd()",
                "LENGTH" => $"{arg0}.Length",
                "SUBSTRING" => $"{arg0}.Substring({arg1} - 1, {arg2})",
                "COALESCE" => $"({arg0} ?? {arg1})",
                "NULLIF" => $"({arg0} == {arg1} ? null : {arg0})",
                "CONCAT" => $"string.Concat({arg0}, {arg1})",

                "REPLACE" => $"{arg0}.Replace({arg1}, {arg2})",
                "LEFT" => $"{arg0}.Substring(0, {arg1})",
                "RIGHT" => $"({arg0}).Substring(({arg0}).Length - ({arg1}))",
                "CHARINDEX" => $"({arg1}).IndexOf({arg0}) + 1",
                "INSTR" => $"({arg0}).IndexOf({arg1}) + 1",
                "REPEAT" => $"string.Concat(System.Linq.Enumerable.Repeat({arg0}, {arg1}))",
                "REVERSE" => $"new string(({arg0}).Reverse().ToArray())",
                "SPACE" => $"new string(' ', {arg0})",
                "STUFF" => $"({arg0}).Remove({arg1} - 1, {arg2}).Insert({arg1} - 1, \"{arg2}\")",
                "STR" => $"({arg0}).ToString()",
                "LCASE" => $"{arg0}.ToLower()",
                "UCASE" => $"{arg0}.ToUpper()",

                "ROUND" => arg1 != "" ? $"Math.Round((double)({arg0}), {arg1})" : $"Math.Round((double)({arg0}))",
                "ABS" => $"Math.Abs((int)({arg0}))",
                "FLOOR" => $"Math.Floor((double)({arg0}))",
                "CEIL" => $"Math.Ceiling((double)({arg0}))",
                "CEILING" => $"Math.Ceiling((double)({arg0}))",
                "POWER" => $"Math.Pow((double)({arg0}), (double)({arg1}))",
                "SQRT" => $"Math.Sqrt((double)({arg0}))",
                "SIGN" => $"Math.Sign((int)({arg0}))",
                "MOD" => $"({arg0}) % ({arg1})",

                "YEAR" => $"({arg0}).Value.Year",
                "MONTH" => $"({arg0}).Value.Month",
                "DAY" => $"({arg0}).Value.Day",
                "HOUR" => $"({arg0}).Value.Hour",
                "MINUTE" => $"({arg0}).Value.Minute",
                "SECOND" => $"({arg0}).Value.Second",

                "FORMAT" => $"({arg0})!.ToString({arg1})",
                "CONVERT" => $"System.Convert.ToDateTime({arg1})",

                "DATEADD" => GenerateDateAdd(arg0, arg1, arg2),
                "DATEDIFF" => GenerateDateDiff(arg0, arg1, arg2),
                "DATEPART" => GenerateDatePart(arg0, arg1),

                _ => throw new System.NotSupportedException(

                    $"[ERROR] Unsupported function: '{FunctionName}'. " +
                    $"Supported string: UPPER, LOWER, TRIM, LTRIM, RTRIM, LENGTH, SUBSTRING, CONCAT, " +
                    $"COALESCE, NULLIF, REPLACE, LEFT, RIGHT, CHARINDEX, INSTR, REPEAT, REVERSE, SPACE, STR. " +
                    $"Supported math: ABS, FLOOR, CEIL, CEILING, ROUND, POWER, SQRT, SIGN, MOD. " +
                    $"Supported date: YEAR, MONTH, DAY, HOUR, MINUTE, SECOND, FORMAT, DATEADD, DATEDIFF, DATEPART.")
            };
        }

        private static string GenerateDateAdd(string part, string n, string date) {

            string p = part.Trim('"').Trim('\'').ToUpperInvariant()
                           .Replace("X.", "").Replace("_", "");
            return p switch {
                "YEAR" => $"({date}).Value.AddYears({n})",
                "MONTH" => $"({date}).Value.AddMonths({n})",
                "DAY" => $"({date}).Value.AddDays({n})",
                "HOUR" => $"({date}).Value.AddHours({n})",
                "MINUTE" => $"({date}).Value.AddMinutes({n})",
                "SECOND" => $"({date}).Value.AddSeconds({n})",
                _ => throw new System.NotSupportedException($"[ERROR] DATEADD: unsupported part '{part}'.")
            };
        }

        private static string GenerateDateDiff(string part, string start, string end) {

            string p = part.Trim('"').Trim('\'').ToUpperInvariant()
                           .Replace("X.", "").Replace("_", "");

            string s = IsDateTimeConstant(start) ? start : $"({start}).Value";
            string e = IsDateTimeConstant(end) ? $"({end})" : $"({end}).Value";

            return p switch {
                "YEAR" => $"({e}.Year - {s}.Year)",
                "MONTH" => $"(({e}.Year - {s}.Year) * 12 + {e}.Month - {s}.Month)",
                "DAY" => $"(int)({e} - {s}).TotalDays",
                "HOUR" => $"(int)({e} - {s}).TotalHours",
                "MINUTE" => $"(int)({e} - {s}).TotalMinutes",
                "SECOND" => $"(int)({e} - {s}).TotalSeconds",
                _ => throw new System.NotSupportedException($"[ERROR] DATEDIFF: unsupported part '{part}'.")
            };
        }

        private static bool IsDateTimeConstant(string expr) =>
    expr == "DateTime.Now" || expr == "DateTime.Today" || expr == "DateTime.UtcNow";

        private static string GenerateDatePart(string part, string date) {

            string p = part.Trim('"').Trim('\'').ToUpperInvariant()
                           .Replace("X.", "").Replace("_", "");
            return p switch {
                "YEAR" => $"({date}).Value.Year",
                "MONTH" => $"({date}).Value.Month",
                "DAY" => $"({date}).Value.Day",
                "HOUR" => $"({date}).Value.Hour",
                "MINUTE" => $"({date}).Value.Minute",
                "SECOND" => $"({date}).Value.Second",
                _ => throw new System.NotSupportedException($"[ERROR] DATEPART: unsupported part '{part}'.")
            };
        }
    }

    // GETDATE() / NOW() / CURRENT_DATE / CURRENT_TIMESTAMP

    public class LinqDateConstantNode : LinqNode {

        public string Kind { get; set; }

        public override string ToCodeString() => Kind == "Date" ? "DateTime.Today" : "DateTime.Now";
    }

    // LIKE / NOT LIKE

    public class LinqLikeNode : LinqNode {

        public LinqNode Target { get; set; }

        public string Pattern { get; set; }

        public override string ToCodeString() {
            string escapedPattern = Pattern?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
            return $"EF.Functions.Like({Target.ToCodeString()}, \"{escapedPattern}\")";
        }
    }

    // CASE WHEN ... THEN ... ELSE ... END

    public class LinqCaseWhenClause {
        public LinqNode Condition { get; set; }
        public LinqNode Result { get; set; }
    }

    public class LinqCaseNode : LinqNode {

        public LinqNode Operand { get; set; }

        public List<LinqCaseWhenClause> WhenClauses { get; set; } = new List<LinqCaseWhenClause>();

        public LinqNode ElseExpression { get; set; }

        public override string ToCodeString() {

            string elseStr = ElseExpression != null ? ElseExpression.ToCodeString() : "null";
            string result = elseStr;

            for (int i = WhenClauses.Count - 1; i >= 0; i--) {

                var clause = WhenClauses[i];
                string condStr;

                if (Operand != null) {
                    condStr = $"{Operand.ToCodeString()} == {clause.Condition.ToCodeString()}";
                } else {
                    condStr = clause.Condition.ToCodeString();
                }

                result = $"{condStr} ? {clause.Result.ToCodeString()} : {result}";
            }

            return $"({result})";
        }
    }

    public class LinqSubqueryNode : LinqNode {

        public LinqNode Inner { get; set; }

        public bool AsScalar { get; set; }

        public string OuterParam { get; set; }

        public override string ToCodeString() {

            string inner = Inner.ToCodeString();

            if (inner.EndsWith(".ToList()")) {
                inner = inner.Substring(0, inner.Length - ".ToList()".Length);
            }

            if (!AsScalar) return inner;

            bool alreadyScalar = inner.EndsWith(".Count()")
                || System.Text.RegularExpressions.Regex.IsMatch(
                       inner, @"\.(Sum|Max|Min|Average)\([^)]*\)$");

            return alreadyScalar ? $"({inner})" : $"({inner}).FirstOrDefault()";
        }
    }

    // EXISTS / NOT EXISTS 

    public class LinqExistsNode : LinqNode {

        public LinqSubqueryNode Subquery { get; set; }

        public bool Negated { get; set; }

        public override string ToCodeString() {

            string inner = Subquery.Inner is LinqQueryNode qn
                ? qn.ToCodeStringUpToSelect()
                : Subquery.Inner.ToCodeString();

            if (inner.EndsWith(".ToList()")) {
                inner = inner.Substring(0, inner.Length - ".ToList()".Length);
            }

            string anyCall = $"({inner}).Any()";
            return Negated ? $"!{anyCall}" : anyCall;
        }
    }

    // UNION / UNION ALL / INTERSECT / EXCEPT

    public class LinqSetOperationNode : LinqNode {

        public LinqNode Left { get; set; }

        public LinqNode Right { get; set; }

        public string MethodName { get; set; }

        public override string ToCodeString() {
            return $"{Left.ToCodeString()}.{MethodName}({Right.ToCodeString()})";
        }
    }

    public class LinqInlineViewQueryNode : LinqNode {

        public string SubqueryCode { get; set; }

        public string Alias { get; set; }

        public List<LinqNode> Methods { get; set; } = new List<LinqNode>();

        public override string ToCodeString() {

            string result = $"({SubqueryCode})";

            foreach (var method in Methods) {
                result += method.ToCodeString();
            }
            return result;
        }
    }

    // Common shape for all join node types

    public abstract class LinqJoinBaseNode : LinqNode {

        public string InnerTable { get; set; }

        public string OuterParam { get; set; }

        public string InnerParam { get; set; }

        public LinqNode ResultSelector { get; set; }

        protected string ResultSelectorCodeString() {
            return ResultSelector != null
                ? ResultSelector.ToCodeString()
                : $"new {{ {OuterParam}, {InnerParam} }}";
        }
    }

    // INNER JOIN

    public class LinqJoinNode : LinqJoinBaseNode {

        public LinqNode OuterKey { get; set; }

        public LinqNode InnerKey { get; set; }

        public override string ToCodeString() {

            return $".Join(db.{InnerTable}, " +
                   $"{OuterParam} => {OuterKey.ToCodeString()}, " +
                   $"{InnerParam} => {InnerKey.ToCodeString()}, " +
                   $"({OuterParam}, {InnerParam}) => {ResultSelectorCodeString()})";
        }
    }

    // LEFT JOIN — GroupJoin + SelectMany(DefaultIfEmpty)
    // RIGHT JOIN is rewritten as LEFT JOIN with swapped sides by the visitor,
    // so both map to this node.

    public class LinqLeftJoinNode : LinqJoinBaseNode {

        public LinqNode OuterKey { get; set; }

        public LinqNode InnerKey { get; set; }

        public List<string> OuterAliases { get; set; } = new List<string>();

        public override string ToCodeString() {

            string collectionParam = $"_{InnerParam}";
            string gj = "gj";

            string groupJoinResult = $"new {{ {OuterParam}, {collectionParam} }}";
            string selectManyResult;

            if (ResultSelector is LinqAnonymousObjectNode anonNode) {

                var rewritten = anonNode.Properties.Select(p => {

                    string expr = p.Expression is LinqIdentifierNode id ? id.Name : p.Expression.ToCodeString();

                    foreach (var alias in OuterAliases) {
                        if (expr == alias || expr.StartsWith(alias + ".")) {

                            expr = $"{gj}.{expr}";
                            break;
                        }
                    }
                    return string.IsNullOrEmpty(p.Name) ? expr : $"{p.Name} = {expr}";
                });
                selectManyResult = $"new {{ {string.Join(", ", rewritten)} }}";
            } else {
                selectManyResult = $"new {{ {gj}.{OuterParam}, {InnerParam} }}";
            }

            return
                $".GroupJoin(db.{InnerTable}, " +
                $"{OuterParam} => {OuterKey.ToCodeString()}, " +
                $"{InnerParam} => {InnerKey.ToCodeString()}, " +
                $"({OuterParam}, {collectionParam}) => {groupJoinResult})" +
                $".SelectMany({gj} => {gj}.{collectionParam}.DefaultIfEmpty(), " +
                $"({gj}, {InnerParam}) => {selectManyResult})";
        }
    }

    // CROSS JOIN

    public class LinqCrossJoinNode : LinqJoinBaseNode {

        public override string ToCodeString() {

            return $".SelectMany({OuterParam} => db.{InnerTable}, " +
                   $"({OuterParam}, {InnerParam}) => {ResultSelectorCodeString()})";
        }
    }

    // Aggregation functions (Count, Sum, Average, Min, Max)

    public class LinqAggregateNode : LinqNode {

        public string FunctionName { get; set; }
        public LinqNode Argument { get; set; }

        public override string ToCodeString() {

            if (Argument == null) {
                return $"g.{FunctionName}()";
            }

            return $"g.{FunctionName}(x => {Argument.ToCodeString()})";
        }
    }
}
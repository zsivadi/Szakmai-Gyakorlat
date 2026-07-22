using System.Linq;
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

    // IN 

    public class LinqInExpressionNode : LinqNode {

        public LinqNode Target { get; set; }

        public List<LinqNode> Values { get; set; } = new List<LinqNode>();

        public override string ToCodeString() {

            var valuesStr = string.Join(", ", Values.Select(v => v.ToCodeString()));
            bool allNumeric = Values.All(v => v is LinqConstantNode c && c.Value is not string);
            string arrayType = allNumeric ? "int?" : "string";

            return $"new {arrayType}[] {{ {valuesStr} }}.Contains({Target.ToCodeString()})";
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
                "ROUND" => arg1 != "" ? $"Math.Round((double)({arg0}), {arg1})" : $"Math.Round((double)({arg0}))",
                "ABS" => $"Math.Abs((int)({arg0}))",
                "FLOOR" => $"Math.Floor((double)({arg0}))",
                "CEIL" => $"Math.Ceiling((double)({arg0}))",
                "CEILING" => $"Math.Ceiling((double)({arg0}))",
                "POWER" => $"Math.Pow((double)({arg0}), (double)({arg1}))",
                "SQRT" => $"Math.Sqrt((double)({arg0}))",
                "SIGN" => $"Math.Sign((int)({arg0}))",

                _ => throw new System.NotSupportedException(
                    $"[ERROR] Unsupported function: '{FunctionName}'. " +
                    $"Supported: UPPER, LOWER, TRIM, LTRIM, RTRIM, LENGTH, SUBSTRING, " +
                    $"COALESCE, NULLIF, ROUND, ABS, FLOOR, CEIL, CEILING, POWER, SQRT, SIGN.")
            };
        }
    }

    // Regex for LIKE patterns

    public class LinqRegexMatchNode : LinqNode {

        public LinqNode Target { get; set; }

        public string Pattern { get; set; }

        public override string ToCodeString() {

            string escapedPattern = Pattern?.Replace("\\", "\\\\") ?? "";

            return $"System.Text.RegularExpressions.Regex.IsMatch({Target.ToCodeString()}, \"(?i){escapedPattern}\")";
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
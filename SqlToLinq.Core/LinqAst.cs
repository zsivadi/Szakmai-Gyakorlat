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

        // List of chained LINQ method calls 
        public List<LinqMethodCallNode> Methods { get; set; } = new List<LinqMethodCallNode>();

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

    // IN (value list)

    public class LinqInExpressionNode : LinqNode {

        public LinqNode Target { get; set; }

        public List<LinqNode> Values { get; set; } = new List<LinqNode>();
            
        public override string ToCodeString() {
            var valuesStr = string.Join(", ", Values.Select(v => v.ToCodeString()));
            return $"new[] {{ {valuesStr} }}.Contains({Target.ToCodeString()})";
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


    // Regex for LIKE patterns

    public class LinqRegexMatchNode : LinqNode {

        public LinqNode Target { get; set; }

        public string Pattern { get; set; }

        public override string ToCodeString() {

            string escapedPattern = Pattern?.Replace("\\", "\\\\") ?? "";

            return $"System.Text.RegularExpressions.Regex.IsMatch({Target.ToCodeString()}, \"(?i){escapedPattern}\")";
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
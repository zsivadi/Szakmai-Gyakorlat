using System.Linq;
using System.Collections.Generic;

namespace SqlToLinq.Cli {

    public abstract class LinqNode {
        public abstract string ToCodeString();
    }

    // The root point

    public class LinqQueryNode : LinqNode {
        public string SourceTable { get; set; }
        public List<LinqMethodCallNode> Methods { get; set; } = new List<LinqMethodCallNode>();

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
        public string MethodName { get; set; }
        public List<LinqNode> Arguments { get; set; } = new List<LinqNode>();

        public override string ToCodeString() {

            if (Arguments == null || Arguments.Count == 0) {
                return $".{MethodName}()";
            }

            var argsStr = string.Join(", ", Arguments.Select(a => a.ToCodeString()));
            return $".{MethodName}({argsStr})";
        }
    }

    // Creating a lambda expression

    public class LinqLambdaNode : LinqNode {
        public string ParameterName { get; set; }
        public LinqNode Body { get; set; }

        public override string ToCodeString() {
            return $"{ParameterName} => {Body.ToCodeString()}";
        }
    }

    // Binary operations

    public class LinqBinaryExpressionNode : LinqNode {
        public LinqNode Left { get; set; }
        public string Operator { get; set; }
        public LinqNode Right { get; set; }

        public override string ToCodeString() {
            return $"{Left.ToCodeString()} {Operator} {Right.ToCodeString()}";
        }
    }

    // Columns

    public class LinqIdentifierNode : LinqNode {
        public string Name { get; set; }

        public override string ToCodeString() {
            return Name;
        }
    }

    // Constant values

    public class LinqConstantNode : LinqNode {
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
        public LinqNode InnerNode { get; set; }

        public override string ToCodeString() {
            return $"({InnerNode.ToCodeString()})";
        }
    }

    // New anon object for Select()

    public class LinqAnonymousObjectNode : LinqNode {
        public List<string> Properties { get; set; } = new List<string>();

        public override string ToCodeString() {
            return $"new {{ {string.Join(", ", Properties)} }}";
        }
    }
    public class LinqStringMethodCallNode : LinqNode {
        public LinqNode Instance { get; set; }
        public string MethodName { get; set; }
        public LinqNode Argument { get; set; }

        public override string ToCodeString() {
            return $"{Instance.ToCodeString()}.{MethodName}({Argument.ToCodeString()})";
        }
    }

    public class LinqRegexMatchNode : LinqNode {
        public LinqNode Target { get; set; }
        public string Pattern { get; set; }

        public override string ToCodeString() {
            return $"System.Text.RegularExpressions.Regex.IsMatch({Target.ToCodeString()}, \"{Pattern}\")";
        }
    }
}
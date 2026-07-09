using Antlr4.Runtime;

namespace SqlToLinq.Cli {
    public static class SqlToLinqConverter {
        public static string Convert(string sqlInput) {

            var inputStream = new AntlrInputStream(sqlInput);
            var lexer = new SqlParserLexer(inputStream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new SqlParserParser(tokens);

            parser.RemoveErrorListeners();

            var tree = parser.query();
            var visitor = new SqlVisitor();

            LinqNode linqAst = visitor.Visit(tree);

            return linqAst.ToCodeString();
        }
    }
}
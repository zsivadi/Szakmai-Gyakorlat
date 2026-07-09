using Antlr4.Runtime;

namespace SqlToLinq.Cli {
    public static class SqlToLinqConverter {

        public static string Convert(string sqlInput) {

            var inputStream = new AntlrInputStream(sqlInput);

            var lexer = new SqlParserLexer(inputStream);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(ThrowingErrorListener.Instance);

            var tokens = new CommonTokenStream(lexer);
            var parser = new SqlParserParser(tokens);

            parser.RemoveErrorListeners();
            parser.AddErrorListener(ThrowingErrorListener.Instance);

            var tree = parser.query();
            var visitor = new SqlVisitor();

            LinqNode linqAst = visitor.Visit(tree);

            return linqAst.ToCodeString();
        }
    }
}
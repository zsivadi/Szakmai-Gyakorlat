using Antlr4.Runtime;

namespace SqlToLinq.Core {
    public static class SqlToLinqConverter {

        public static string Convert(string sqlInput) {
            var (linqCode, _, _) = ConvertWithAst(sqlInput);
            return linqCode;
        }

        public static (string LinqCode, string SqlAstDot, string LinqAstDot) ConvertWithAst(string sqlInput) {

            var inputStream = new AntlrInputStream(sqlInput);

            var lexer = new SqlParserLexer(inputStream);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(ThrowingErrorListener.Instance);

            var tokens = new CommonTokenStream(lexer);
            var parser = new SqlParserParser(tokens);

            parser.RemoveErrorListeners();
            parser.AddErrorListener(ThrowingErrorListener.Instance);

            var tree = parser.query();
            string sqlAstDot = AstVisualizer.ExportAntlrTreeToDot(tree, parser);

            var visitor = new SqlVisitor();
            LinqNode linqAst = visitor.Visit(tree);
            string linqAstDot = AstVisualizer.ExportLinqAstToDot(linqAst);

            return (linqAst.ToCodeString(), sqlAstDot, linqAstDot);
        }
    }
}
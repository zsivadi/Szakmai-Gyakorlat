using Antlr4.Runtime.Misc;
using Antlr4.Runtime;
using System;
using System.Net.ServerSentEvents;

namespace SqlToLinq.Cli {

    public class SqlVisitor : SqlParserBaseVisitor<string> {

        public override string VisitQuery([NotNull] SqlParserParser.QueryContext context) {
            return Visit(context.statement());
        }

        public override string VisitStatement([NotNull] SqlParserParser.StatementContext context) {

            if (context.selectStmt() != null) return Visit(context.selectStmt());
            if (context.updateStmt() != null) return Visit(context.updateStmt());
            if (context.insertStmt() != null) return Visit(context.insertStmt());
            if (context.deleteStmt() != null) return Visit(context.deleteStmt());

            throw new NotSupportedException("[ERROR] This is not SQL!");
        }

        public override string VisitSelectStmt([NotNull] SqlParserParser.SelectStmtContext context) {

            string tableName = context.tableName().GetText();
            string baseQuery = $"db.{tableName}";

            // WHERE check

            if (context.condition() != null) {
                string whereClause = Visit(context.condition());
                baseQuery += $".Where(x => {whereClause})";
            }

            // Get columns

            string columns = Visit(context.columnList());
            if (columns != "*") {
                baseQuery += $".Select(x => new {{ {columns} }})";
            }

            // End point

            baseQuery += ".ToList()";

            return baseQuery;
        }

        // '==' and '!=' operators

        public override string VisitCompareCondition([NotNull] SqlParserParser.CompareConditionContext context) {

            string left = Visit(context.left);
            string op = context.op.GetText();
            string right = Visit(context.right);

            if (op == "=") op = "==";
            if (op == "<>") op = "!=";

            return $"{left} {op} {right}";
        }

        // AND

        public override string VisitAndCondition([NotNull] SqlParserParser.AndConditionContext context) {
            
            string left = Visit(context.left);
            string right = Visit(context.right);

            return $"{left} && {right}";
        }

        // OR

        public override string VisitOrCondition([NotNull] SqlParserParser.OrConditionContext context) {
            
            string left = Visit(context.left);
            string right = Visit(context.right);

            return $"{left} || {right}";
        }

        // Brackets

        public override string VisitParensCondition([NotNull] SqlParserParser.ParensConditionContext context) {
            
            string innerCondition = Visit(context.condition());
            return $"({innerCondition})";
        }

        public override string VisitMathExpr([NotNull] SqlParserParser.MathExprContext context) {
            string left = Visit(context.left);
            string op = context.op.GetText(); 
            string right = Visit(context.right);

            return $"{left} {op} {right}";
        }

        // Text conditions

        public override string VisitColumnExpr([NotNull] SqlParserParser.ColumnExprContext context) {
            return $"x.{context.GetText()}";
        }

        // Number conditions

        public override string VisitNumberExpr([NotNull] SqlParserParser.NumberExprContext context) {
            return context.GetText();
        }

        // Changing '' to ""

        public override string VisitStringExpr([NotNull] SqlParserParser.StringExprContext context) {

            string text = context.GetText().Trim('\'');
            return $"\"{text}\"";
        }

        // Getting column list
        // If there is no list, then return with STAR

        public override string VisitColumnList([NotNull] SqlParserParser.ColumnListContext context) {

            if (context.idList() != null) {
                return Visit(context.idList());
            }

            return "*";
        }

        // Append columns with commas

        public override string VisitIdList([NotNull] SqlParserParser.IdListContext context) {

            var identifiers = context.IDENTIFIER();
            var columns = System.Linq.Enumerable.Select(identifiers, id => $"x.{id.GetText()}");

            return string.Join(", ", columns);
        }
    }
}
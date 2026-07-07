using System;
using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime.Misc;

namespace SqlToLinq.Cli {

    public class SqlVisitor : SqlParserBaseVisitor<LinqNode> {

        public override LinqNode VisitQuery([NotNull] SqlParserParser.QueryContext context) {
            return Visit(context.statement());
        }

        public override LinqNode VisitStatement([NotNull] SqlParserParser.StatementContext context) {

            if (context.selectStmt() != null) return Visit(context.selectStmt());

            throw new NotSupportedException("[ERROR] This is not a SELECT!");
        }

        private string ToPascalCase(string input) {

            if (string.IsNullOrEmpty(input)) return input;

            return char.ToUpper(input[0]) + input.Substring(1);
        }

        public override LinqNode VisitSelectStmt([NotNull] SqlParserParser.SelectStmtContext context) {

            // Table name

            var queryNode = new LinqQueryNode {
                SourceTable = ToPascalCase(context.tableName().GetText())
            };

            // WHERE

            if (context.condition() != null) {
                var conditionNode = Visit(context.condition());

                var whereMethod = new LinqMethodCallNode { MethodName = "Where" };

                whereMethod.Arguments.Add(new LinqLambdaNode {
                    ParameterName = "x",
                    Body = conditionNode
                });

                queryNode.Methods.Add(whereMethod);
            }

            // Columns

            var columnsNode = Visit(context.columnList());

            if (columnsNode is LinqAnonymousObjectNode anonNode) {

                var selectMethod = new LinqMethodCallNode { MethodName = "Select" };

                selectMethod.Arguments.Add(new LinqLambdaNode {
                    ParameterName = "x",
                    Body = anonNode
                });

                queryNode.Methods.Add(selectMethod);
            }

            // ToList() at the end

            queryNode.Methods.Add(new LinqMethodCallNode { MethodName = "ToList" });

            return queryNode;
        }


        public override LinqNode VisitColumnList([NotNull] SqlParserParser.ColumnListContext context) {

            if (context.STAR() != null) {
                return null; 
            }

            return Visit(context.idList());
        }

        public override LinqNode VisitIdList([NotNull] SqlParserParser.IdListContext context) {

            var anonNode = new LinqAnonymousObjectNode();

            foreach (var id in context.IDENTIFIER()) {
                anonNode.Properties.Add($"x.{id.GetText()}");
            }

            return anonNode;
        }

        // Operators

        public override LinqNode VisitCompareCondition([NotNull] SqlParserParser.CompareConditionContext context) {

            string op = context.op.GetText();

            if (op == "=") op = "==";
            if (op == "<>") op = "!=";

            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = op,
                Right = Visit(context.right)
            };
        }

        // AND

        public override LinqNode VisitAndCondition([NotNull] SqlParserParser.AndConditionContext context) {

            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = "&&",
                Right = Visit(context.right)
            };
        }

        // OR

        public override LinqNode VisitOrCondition([NotNull] SqlParserParser.OrConditionContext context) {
            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = "||",
                Right = Visit(context.right)
            };
        }

        // Brackets

        public override LinqNode VisitParensCondition([NotNull] SqlParserParser.ParensConditionContext context) {
            return new LinqParensNode {
                InnerNode = Visit(context.condition())
            };
        }

        // Math symbols

        public override LinqNode VisitMathExpr([NotNull] SqlParserParser.MathExprContext context) {
            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = context.op.GetText(),
                Right = Visit(context.right)
            };
        }

        // Expressions

        public override LinqNode VisitColumnExpr([NotNull] SqlParserParser.ColumnExprContext context) {
            string rawColumnName = context.GetText();
            return new LinqIdentifierNode { Name = $"x.{ToPascalCase(rawColumnName)}" };
        }

        public override LinqNode VisitNumberExpr([NotNull] SqlParserParser.NumberExprContext context) {
            return new LinqConstantNode { Value = int.Parse(context.GetText()) };
        }

        public override LinqNode VisitStringExpr([NotNull] SqlParserParser.StringExprContext context) {
            return new LinqConstantNode { Value = context.GetText().Trim('\'') };
        }
    }
}
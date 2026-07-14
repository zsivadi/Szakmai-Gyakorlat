using System;
using System.Linq;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SqlToLinq.Core {

    public class SqlVisitor : SqlParserBaseVisitor<LinqNode> {

        private bool _inWhereClause = false;
        private bool _inAggregate = false;

        private Dictionary<string, string> _selectAliases = new Dictionary<string, string>();

        public override LinqNode VisitQuery([NotNull] SqlParserParser.QueryContext context) {
            return Visit(context.statement());
        }

        public override LinqNode VisitStatement([NotNull] SqlParserParser.StatementContext context) {

            if (context.selectStmt() != null) return Visit(context.selectStmt());
            if (context.updateStmt() != null) return Visit(context.updateStmt());
            if (context.insertStmt() != null) return Visit(context.insertStmt());
            if (context.deleteStmt() != null) return Visit(context.deleteStmt());

            throw new NotSupportedException("[ERROR] Unknown or unsupported command!");
        }

        // Snake_case to PascalCase conversion for table and column names

        private string ToPascalCase(string input) {

            if (string.IsNullOrEmpty(input)) return input;

            var parts = input.Split('_');
            var sb = new StringBuilder();

            foreach (var part in parts) {
                if (part.Length == 0) continue;

                sb.Append(char.ToUpper(part[0]));

                if (part.Length > 1) {
                    sb.Append(part.Substring(1).ToLower());
                }
            }

            return sb.Length > 0 ? sb.ToString() : input;
        }

        // GROUP BY columns extraction

        private List<string> GetGroupByColumns(SqlParserParser.SelectStmtContext selectCtx) {

            if (selectCtx.groupClause() == null) return new List<string>();

            return selectCtx.groupClause().idList().IDENTIFIER()
                .Select(id => ToPascalCase(id.GetText()))
                .ToList();
        }

        // SELECT statement processing

        public override LinqNode VisitSelectStmt([NotNull] SqlParserParser.SelectStmtContext context) {

            if (context.columnList() != null && context.columnList().STAR() == null) {
                foreach (var item in context.columnList().selectItem()) {

                    if (item.IDENTIFIER() != null && item.expr() is SqlParserParser.ColumnExprContext colCtx) {

                        string aliasName = ToPascalCase(item.IDENTIFIER().GetText());
                        string originalName = ToPascalCase(colCtx.GetText());

                        _selectAliases[aliasName] = originalName;
                    }
                }
            }

            // Table name 

            var queryNode = new LinqQueryNode {
                SourceTable = ToPascalCase(context.tableName().GetText())
            };

            // WHERE 

            if (context.condition() != null) {

                _inWhereClause = true;
                var conditionNode = Visit(context.condition());
                _inWhereClause = false;

                var whereMethod = new LinqMethodCallNode { MethodName = "Where" };

                whereMethod.Arguments.Add(new LinqLambdaNode {
                    ParameterName = "x",
                    Body = conditionNode
                });

                queryNode.Methods.Add(whereMethod);
            }

            // GROUP BY 

            var groupKeys = GetGroupByColumns(context);
            bool hasGroupBy = groupKeys.Count > 0;

            if (hasGroupBy) {

                LinqNode keySelectorBody;

                if (groupKeys.Count == 1) {
                    keySelectorBody = new LinqIdentifierNode { Name = $"x.{groupKeys[0]}" };
                } else {
                    var compositeKey = new LinqAnonymousObjectNode();

                    foreach (var key in groupKeys) {
                        compositeKey.Properties.Add((null, new LinqIdentifierNode { Name = $"x.{key}" }));
                    }
                    keySelectorBody = compositeKey;
                }

                var groupByMethod = new LinqMethodCallNode { MethodName = "GroupBy" };

                groupByMethod.Arguments.Add(new LinqLambdaNode {
                    ParameterName = "x",
                    Body = keySelectorBody
                });
                queryNode.Methods.Add(groupByMethod);
            }

            // HAVING 

            if (context.havingClause() != null) {

                var havingCondition = Visit(context.havingClause().condition());

                var havingMethod = new LinqMethodCallNode { MethodName = "Where" };

                havingMethod.Arguments.Add(new LinqLambdaNode {
                    ParameterName = hasGroupBy ? "g" : "x",
                    Body = havingCondition
                });

                queryNode.Methods.Add(havingMethod);
            }

            // ORDER BY 

            if (context.orderClause() != null) {

                var orderItems = context.orderClause().orderItem();

                for (int i = 0; i < orderItems.Length; i++) {

                    var item = orderItems[i];
                    var itemNode = Visit(item.expr());
                    bool isDesc = item.DESC() != null;

                    string methodName = i == 0
                        ? (isDesc ? "OrderByDescending" : "OrderBy")
                        : (isDesc ? "ThenByDescending" : "ThenBy");

                    var orderMethod = new LinqMethodCallNode { MethodName = methodName };

                    orderMethod.Arguments.Add(new LinqLambdaNode {
                        ParameterName = hasGroupBy ? "g" : "x",
                        Body = itemNode
                    });

                    queryNode.Methods.Add(orderMethod);
                }
            }

            if (context.offsetClause() != null) {

                var offsetExpr = Visit(context.offsetClause().expr());
                var skipMethod = new LinqMethodCallNode { MethodName = "Skip" };

                skipMethod.Arguments.Add(offsetExpr);
                queryNode.Methods.Add(skipMethod);
            }

            if (context.limitClause() != null) {

                var limitExpr = Visit(context.limitClause().expr());
                var takeMethod = new LinqMethodCallNode { MethodName = "Take" };

                takeMethod.Arguments.Add(limitExpr);
                queryNode.Methods.Add(takeMethod);
            }

            // Columns  

            var columnsNode = Visit(context.columnList());
            bool isGlobalSingleAggregate = false;

            if (!hasGroupBy && columnsNode is LinqAnonymousObjectNode globalAnonNode && globalAnonNode.Properties.Count == 1 && globalAnonNode.Properties[0].Expression is LinqAggregateNode globalAggNode) {

                isGlobalSingleAggregate = true;

                var aggMethod = new LinqMethodCallNode { MethodName = globalAggNode.FunctionName };

                if (globalAggNode.Argument != null) {
                    aggMethod.Arguments.Add(new LinqLambdaNode {
                        ParameterName = "x",
                        Body = globalAggNode.Argument
                    });
                }

                queryNode.Methods.Add(aggMethod);

            } else if (columnsNode is LinqAnonymousObjectNode anonNode) {

                var selectMethod = new LinqMethodCallNode { MethodName = "Select" };

                selectMethod.Arguments.Add(new LinqLambdaNode {
                    ParameterName = hasGroupBy ? "g" : "x",
                    Body = anonNode
                });

                queryNode.Methods.Add(selectMethod);
            } else {

                // If there is a GROUP BY, we need to select the first item from each group

                if (hasGroupBy) {

                    var selectMethod = new LinqMethodCallNode { MethodName = "Select" };

                    selectMethod.Arguments.Add(new LinqLambdaNode {
                        ParameterName = "g",
                        Body = new LinqIdentifierNode { Name = "g.FirstOrDefault()" }
                    });
                    queryNode.Methods.Add(selectMethod);
                }
            }

            // ToList() at the end

            if (!isGlobalSingleAggregate) {
                queryNode.Methods.Add(new LinqMethodCallNode { MethodName = "ToList" });
            }

            return queryNode;
        }

        // SELECT column list processing

        public override LinqNode VisitColumnList([NotNull] SqlParserParser.ColumnListContext context) {
            if (context.STAR() != null) return null;

            var anonNode = new LinqAnonymousObjectNode();
            foreach (var item in context.selectItem()) {

                var exprNode = Visit(item.expr());

                string aliasName = item.IDENTIFIER() != null ? item.IDENTIFIER().GetText() : null;

                if (aliasName == null && item.expr() is SqlParserParser.ColumnExprContext colCtx) {
                    aliasName = ToPascalCase(colCtx.GetText());
                }

                anonNode.Properties.Add((aliasName, exprNode));
            }
            return anonNode;
        }

        // Aggregate function processing (COUNT, SUM, AVG, MIN, MAX)

        public override LinqNode VisitAggregateExpr([NotNull] SqlParserParser.AggregateExprContext context) {

            if (context.IDENTIFIER() == null) {
                throw new ArgumentException($"[ERROR] Syntax error in the aggregate function! {context.GetText()}");
            }

            string funcName = ToPascalCase(context.IDENTIFIER().GetText());
            if (funcName == "Avg") funcName = "Average";

            if (funcName == "Count") {
                return new LinqAggregateNode { FunctionName = "Count", Argument = null };
            }

            LinqNode argNode = null;
            if (context.expr() != null) {
                _inAggregate = true;
                argNode = Visit(context.expr());
                _inAggregate = false;
            }

            return new LinqAggregateNode {
                FunctionName = funcName,
                Argument = argNode
            };
        }

        // Conditions processing

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

        // LIKE condition processing, converting SQL LIKE patterns to C# regex patterns

        public override LinqNode VisitLikeCondition([NotNull] SqlParserParser.LikeConditionContext context) {

            var leftNode = Visit(context.left);

            string pattern = context.right.Text.Trim('\'');
            pattern = Regex.Replace(pattern, "%+", "%");

            var sb = new StringBuilder("^");

            foreach (char c in pattern) {
                if (c == '%') {
                    sb.Append(".*");
                } else if (c == '_') {
                    sb.Append(".");
                } else {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            sb.Append("$");

            LinqNode regexNode = new LinqRegexMatchNode {
                Target = leftNode,
                Pattern = sb.ToString()
            };

            if (context.NOT() != null) {
                return new LinqUnaryExpressionNode { Operator = "!", Operand = regexNode };
            }

            return regexNode;
        }

        // BETWEEN condition processing, converting to two chained comparisons joined with "&&"

        public override LinqNode VisitBetweenCondition([NotNull] SqlParserParser.BetweenConditionContext context) {

            var lowerBound = new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = ">=",
                Right = Visit(context.low)
            };

            var upperBound = new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = "<=",
                Right = Visit(context.high)
            };

            LinqNode betweenExpr = new LinqBinaryExpressionNode {
                Left = lowerBound,
                Operator = "&&",
                Right = upperBound
            };

            if (context.NOT() != null) {
                return new LinqUnaryExpressionNode { Operator = "!", Operand = betweenExpr };
            }

            return betweenExpr;
        }

        // NOT condition processing, converting to C# "!" prefix operator

        public override LinqNode VisitNotCondition([NotNull] SqlParserParser.NotConditionContext context) {
            return new LinqUnaryExpressionNode {
                Operator = "!",
                Operand = Visit(context.condition())
            };
        }

        // IS NULL / IS NOT NULL

        public override LinqNode VisitIsNullCondition([NotNull] SqlParserParser.IsNullConditionContext context) {
            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = context.NOT() != null ? "!=" : "==",
                Right = new LinqConstantNode { Value = null }
            };
        }

        // IN 

        public override LinqNode VisitInCondition([NotNull] SqlParserParser.InConditionContext context) {

            var inNode = new LinqInExpressionNode {
                Target = Visit(context.left)
            };

            foreach (var valueExpr in context.exprList().expr()) {
                inNode.Values.Add(Visit(valueExpr));
            }

            if (context.NOT() != null) return new LinqUnaryExpressionNode { Operator = "!", Operand = inNode };

            return inNode;
        }

        public override LinqNode VisitBooleanColumnCondition([NotNull] SqlParserParser.BooleanColumnConditionContext context) {
            return Visit(context.expr());
        }

        // AND condition processing, converting to C# "&&" operator

        public override LinqNode VisitAndCondition([NotNull] SqlParserParser.AndConditionContext context) {

            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = "&&",
                Right = Visit(context.right)
            };
        }

        // OR condition processing, converting to C# "||" operator

        public override LinqNode VisitOrCondition([NotNull] SqlParserParser.OrConditionContext context) {
            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = "||",
                Right = Visit(context.right)
            };
        }

        // Parentheses condition processing, wrapping the inner condition in parentheses

        public override LinqNode VisitParensCondition([NotNull] SqlParserParser.ParensConditionContext context) {
            return new LinqParensNode {
                InnerNode = Visit(context.condition())
            };
        }

        // Mathematical expression processing, converting to C# binary operations

        public override LinqNode VisitMathExpr([NotNull] SqlParserParser.MathExprContext context) {
            return new LinqBinaryExpressionNode {
                Left = Visit(context.left),
                Operator = context.op.GetText(),
                Right = Visit(context.right)
            };
        }

        // Column expression processing, converting to C# property access, considering GROUP BY context
        public override LinqNode VisitColumnExpr([NotNull] SqlParserParser.ColumnExprContext context) {

            string rawColumnName = ToPascalCase(context.GetText());

            if (_selectAliases.ContainsKey(rawColumnName)) {
                rawColumnName = _selectAliases[rawColumnName];
            }

            if (_inWhereClause || _inAggregate) {
                return new LinqIdentifierNode { Name = $"x.{rawColumnName}" };
            }

            RuleContext current = context.Parent;
            bool insideGroupBySelect = false;

            while (current != null) {
                if (current is SqlParserParser.SelectStmtContext selectCtx) {
                    if (selectCtx.groupClause() != null) {

                        insideGroupBySelect = true;

                        var idNodes = selectCtx.groupClause().idList().IDENTIFIER();

                        if (idNodes != null && idNodes.Length > 0) {

                            var groupKeys = idNodes.Select(id => ToPascalCase(id.GetText())).ToList();

                            if (groupKeys.Contains(rawColumnName)) {
                                if (groupKeys.Count == 1) {
                                    return new LinqIdentifierNode { Name = "g.Key" };
                                } else {
                                    return new LinqIdentifierNode { Name = $"g.Key.{rawColumnName}" };
                                }
                            }
                        }
                    }
                    break;
                }
                current = current.Parent;
            }

            if (insideGroupBySelect) {
                return new LinqIdentifierNode { Name = $"g.FirstOrDefault().{rawColumnName}" };
            }

            return new LinqIdentifierNode { Name = $"x.{rawColumnName}" };
        }

        // Number literal processing, converting to C# integer constant

        public override LinqNode VisitNumberExpr([NotNull] SqlParserParser.NumberExprContext context) {
            return new LinqConstantNode { Value = int.Parse(context.GetText()) };
        }

        // String literal processing, converting to C# string constant

        public override LinqNode VisitStringExpr([NotNull] SqlParserParser.StringExprContext context) {
            return new LinqConstantNode { Value = context.GetText().Trim('\'') };
        }
    }
}
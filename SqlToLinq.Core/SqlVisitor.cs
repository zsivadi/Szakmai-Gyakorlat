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
        private bool _hasJoin = false;
        private bool _inOrderBy = false;

        // Active only while resolving a single join step's ON-condition and flatten expressions.
        // Maps each alias visible at that point to the C# expression prefix needed to reach its
        // entity from within that step's outer/inner lambda scope (see the JOIN loop below).
        private Dictionary<string, string> _joinKeyAliasMap = null;

        private Dictionary<string, string> _selectAliases = new Dictionary<string, string>();
        private Dictionary<string, string> _tableAliases = new Dictionary<string, string>();

        public override LinqNode VisitQuery([NotNull] SqlParserParser.QueryContext context) {
            return Visit(context.selectQuery());
        }

        public override LinqNode VisitSelectQuery([NotNull] SqlParserParser.SelectQueryContext context) {

            var selects = context.selectStmt();

            if (selects.Length == 1) {
                return Visit(selects[0]);
            }

            var result = Visit(selects[0]);

            for (int i = 1; i < selects.Length; i++) {

                var right = Visit(selects[i]);

                bool isAll = context.ALL(i - 1) != null;
                bool isIntersect = context.INTERSECT(i - 1) != null;
                bool isExcept = context.EXCEPT(i - 1) != null;

                string methodName = isIntersect ? "Intersect"
                                  : isExcept ? "Except"
                                  : isAll ? "Concat"   
                                  : "Union";   

                result = new LinqSetOperationNode {
                    Left = result,
                    Right = right,
                    MethodName = methodName
                };
            }

            return result;
        }

        public override LinqNode VisitStatement([NotNull] SqlParserParser.StatementContext context) {

            if (context.selectStmt() != null) return Visit(context.selectStmt());

            throw new NotSupportedException("[ERROR] Unknown or unsupported command!");
        }

        // Snake_case to PascalCase conversion for table and column names

        private string ToPascalCase(string input) {

            if (string.IsNullOrEmpty(input)) return input;

            bool isSnakeCase = input.Contains('_');

            if (isSnakeCase) {
                var parts = input.Split('_');
                var sb = new StringBuilder();
                foreach (var part in parts) {
                    if (part.Length == 0) continue;
                    sb.Append(char.ToUpper(part[0]));
                    if (part.Length > 1) sb.Append(part.Substring(1).ToLower());
                }
                return sb.Length > 0 ? sb.ToString() : input;
            }

            bool isMixedCase = input.Any(char.IsUpper) && input.Any(char.IsLower);

            if (isMixedCase) {
                return char.ToUpper(input[0]) + input.Substring(1);
            }

            return char.ToUpper(input[0]) + input.Substring(1).ToLower();
        }

        // GROUP BY columns extraction

        private List<string> GetGroupByColumns(SqlParserParser.SelectStmtContext selectCtx) {

            if (selectCtx.groupClause() == null) return new List<string>();

            return selectCtx.groupClause().groupItem()
                .Select(item => {
                    var ids = item.IDENTIFIER();
                    return ids.Length == 2
                        ? $"{ids[0].GetText()}.{ToPascalCase(ids[1].GetText())}"
                        : ToPascalCase(ids[0].GetText());
                })
                .ToList();
        }

        private Dictionary<string, string> BuildSelectAliases(SqlParserParser.ColumnListContext columnList) {

            var aliases = new Dictionary<string, string>();

            if (columnList == null || columnList.STAR() != null) return aliases;

            foreach (var item in columnList.selectItem()) {
                if (item.IDENTIFIER() != null) {
                    string alias = ToPascalCase(item.IDENTIFIER().GetText());
                    if (item.expr() is SqlParserParser.ColumnExprContext colCtx) {

                        string original = ToPascalCase(colCtx.GetText());
                        aliases[alias] = original;
                    } else {
                        aliases[alias] = alias;
                    }
                }
            }
            return aliases;
        }

        // SELECT statement processing

        public override LinqNode VisitSelectStmt([NotNull] SqlParserParser.SelectStmtContext context) {

            _selectAliases = BuildSelectAliases(context.columnList());

            // FROM clause: base table + optional joins

            var fromCtx = context.fromClause();
            var baseRef = fromCtx.tableRef();

            string baseTable = ToPascalCase(baseRef.tableName().GetText());
            string baseAlias = baseRef.alias() != null
                ? baseRef.alias().GetText()
                : baseTable.Substring(0, 1).ToLower();

            _tableAliases.Clear();
            _tableAliases[baseAlias] = baseTable;

            var queryNode = new LinqQueryNode { SourceTable = baseTable };

            // JOIN
            
            // Each join step consumes either the raw base table (for the very first join) or the
            // flattened wrapper object produced by the previous step, and always flattens its own
            // result the same way. See BuildJoinChain for the details; this is what lets an
            // arbitrary number of JOIN / CROSS JOIN clauses be stacked instead of only ever
            // supporting exactly one.

            bool hasJoin = fromCtx.joinClause().Length > 0;
            _hasJoin = hasJoin;

            BuildJoinChain(fromCtx, baseAlias, queryNode);

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

                _inAggregate = true;
                var havingCondition = Visit(context.havingClause().condition());
                _inAggregate = false;

                var havingMethod = new LinqMethodCallNode { MethodName = "Where" };

                havingMethod.Arguments.Add(new LinqLambdaNode {
                    ParameterName = hasGroupBy ? "g" : "x",
                    Body = havingCondition
                });

                queryNode.Methods.Add(havingMethod);
            }

            bool hasDistinct = context.DISTINCT() != null;

            if (hasDistinct && context.orderClause() != null && context.columnList().STAR() == null) {

                var selectedColumns = context.columnList().selectItem()
                    .Select(item => {
                        if (item.IDENTIFIER() != null) return ToPascalCase(item.IDENTIFIER().GetText());
                        if (item.expr() is SqlParserParser.ColumnExprContext col) return ToPascalCase(col.GetText());
                        return null;
                    })
                    .Where(name => name != null)
                    .ToHashSet();

                foreach (var orderItem in context.orderClause().orderItem()) {
                    if (orderItem.expr() is SqlParserParser.ColumnExprContext orderCol) {
                        string orderColName = ToPascalCase(orderCol.GetText());
                        if (!selectedColumns.Contains(orderColName)) {
                            throw new NotSupportedException(
                                $"[ERROR] DISTINCT with ORDER BY '{orderColName}' is not supported " +
                                $"when '{orderColName}' is not in the SELECT list. " +
                                $"The sort order would be undefined after deduplication.");
                        }
                    }
                }
            }

            if (!hasDistinct && context.orderClause() != null) {

                var orderItems = context.orderClause().orderItem();

                for (int i = 0; i < orderItems.Length; i++) {

                    var item = orderItems[i];
                    _inOrderBy = true;
                    var itemNode = Visit(item.expr());
                    _inOrderBy = false;
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

                if (hasDistinct) {

                    queryNode.Methods.Add(new LinqMethodCallNode { MethodName = "Distinct" });

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
                                ParameterName = "x",
                                Body = itemNode
                            });

                            queryNode.Methods.Add(orderMethod);
                        }
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

                queryNode.Methods.Add(new LinqMethodCallNode { MethodName = "ToList" });
            }

            return queryNode;
        }

        // JOIN chain construction 

        private void BuildJoinChain(SqlParserParser.FromClauseContext fromCtx, string baseAlias, LinqQueryNode queryNode) {

            var aliasAccessPrefix = new Dictionary<string, string> { [baseAlias] = baseAlias };

            string previousWrapper = null;
            int joinStepIndex = 0;

            foreach (var joinCtx in fromCtx.joinClause()) {

                joinStepIndex++;

                bool isRight = joinCtx.joinType() != null && joinCtx.joinType().RIGHT() != null;

                if (isRight) {

                    var joinRef = joinCtx.tableRef();
                    string joinTable = ToPascalCase(joinRef.tableName().GetText());
                    string joinAlias = joinRef.alias() != null
                        ? joinRef.alias().GetText()
                        : joinTable.Substring(0, 1).ToLower();

                    _tableAliases[joinAlias] = joinTable;

                    queryNode.SourceTable = joinTable;

                    aliasAccessPrefix.Clear();
                    aliasAccessPrefix[joinAlias] = joinAlias;
                    aliasAccessPrefix[baseAlias] = baseAlias;

                    string outerParam = previousWrapper ?? joinAlias;
                    bool outerIsWrapper = previousWrapper != null;

                    _joinKeyAliasMap = BuildJoinKeyAliasMap(aliasAccessPrefix, outerParam, outerIsWrapper, baseAlias);

                    var flattenResultSelector = new LinqAnonymousObjectNode();
                    flattenResultSelector.Properties.Add((null, new LinqIdentifierNode { Name = joinAlias }));
                    flattenResultSelector.Properties.Add((null, new LinqIdentifierNode { Name = baseAlias }));

                    var onCondition = joinCtx.condition();
                    if (!(onCondition is SqlParserParser.CompareConditionContext cmpCtx) || cmpCtx.op.GetText() != "=") {
                        throw new NotSupportedException(
                            "[ERROR] RIGHT JOIN ON condition must be a simple equality (a.Key = b.Key).");
                    }

                    var (outerKey, innerKey) = ResolveJoinKeysDirectional(cmpCtx, joinAlias, baseAlias);

                    queryNode.Methods.Add(new LinqLeftJoinNode {
                        InnerTable = _tableAliases[baseAlias],
                        OuterParam = joinAlias,
                        InnerParam = baseAlias,
                        OuterKey = outerKey,
                        InnerKey = innerKey,
                        ResultSelector = flattenResultSelector,
                        OuterAliases = new List<string> { joinAlias },
                    });

                    _joinKeyAliasMap = null;

                    string wrapperName = $"j{joinStepIndex}";
                    aliasAccessPrefix[joinAlias] = $"{wrapperName}.{joinAlias}";
                    aliasAccessPrefix[baseAlias] = $"{wrapperName}.{baseAlias}";
                    previousWrapper = wrapperName;

                } else {
                    AddJoinStep(joinCtx, joinStepIndex, baseAlias, aliasAccessPrefix, ref previousWrapper, queryNode);
                }
            }
        }

        private void AddJoinStep(
            SqlParserParser.JoinClauseContext joinCtx,
            int joinStepIndex,
            string baseAlias,
            Dictionary<string, string> aliasAccessPrefix,
            ref string previousWrapper,
            LinqQueryNode queryNode) {

            var joinRef = joinCtx.tableRef();

            string joinTable = ToPascalCase(joinRef.tableName().GetText());
            string joinAlias = joinRef.alias() != null
                ? joinRef.alias().GetText()
                : joinTable.Substring(0, 1).ToLower();

            _tableAliases[joinAlias] = joinTable;

            string outerParam = previousWrapper ?? baseAlias;
            bool outerIsWrapper = previousWrapper != null;

            _joinKeyAliasMap = BuildJoinKeyAliasMap(aliasAccessPrefix, outerParam, outerIsWrapper, joinAlias);

            var flattenResultSelector = BuildFlattenResultSelector(aliasAccessPrefix.Keys, joinAlias);

            bool isCross = joinCtx.joinType() != null && joinCtx.joinType().CROSS() != null;
            bool isLeft = joinCtx.joinType() != null && joinCtx.joinType().LEFT() != null;

            if (isCross) {
                queryNode.Methods.Add(new LinqCrossJoinNode {
                    InnerTable = joinTable,
                    OuterParam = outerParam,
                    InnerParam = joinAlias,
                    ResultSelector = flattenResultSelector,
                });
            } else if (isLeft) {
                AddLeftJoinStep(joinCtx, joinTable, joinAlias, outerParam, aliasAccessPrefix, flattenResultSelector, queryNode);
            } else {
                AddEquiJoinStep(joinCtx, joinTable, joinAlias, outerParam, aliasAccessPrefix, flattenResultSelector, queryNode);
            }

            _joinKeyAliasMap = null;

            previousWrapper = AdvanceAliasAccessPrefix(aliasAccessPrefix, joinAlias, joinStepIndex);
        }

        private void AddEquiJoinStep(
            SqlParserParser.JoinClauseContext joinCtx,
            string joinTable,
            string joinAlias,
            string outerParam,
            Dictionary<string, string> aliasAccessPrefix,
            LinqAnonymousObjectNode flattenResultSelector,
            LinqQueryNode queryNode) {

            var onCondition = joinCtx.condition();

            if (onCondition is SqlParserParser.CompareConditionContext cmpCtx && cmpCtx.op.GetText() == "=") {

                var (outerKey, innerKey) = ResolveJoinKeys(cmpCtx, joinAlias, aliasAccessPrefix);

                queryNode.Methods.Add(new LinqJoinNode {
                    InnerTable = joinTable,
                    OuterParam = outerParam,
                    InnerParam = joinAlias,
                    OuterKey = outerKey,
                    InnerKey = innerKey,
                    ResultSelector = flattenResultSelector,
                });

            } else {

                queryNode.Methods.Add(new LinqCrossJoinNode {
                    InnerTable = joinTable,
                    OuterParam = outerParam,
                    InnerParam = joinAlias,
                    ResultSelector = flattenResultSelector,
                });

                _joinKeyAliasMap = null;
                LinqNode filterExpr = Visit(onCondition);

                queryNode.Methods.Add(new LinqMethodCallNode {
                    MethodName = "Where",
                    Arguments = new List<LinqNode> {
                        new LinqLambdaNode { ParameterName = "x", Body = filterExpr }
                    }
                });
            }
        }

        private void AddLeftJoinStep(
            SqlParserParser.JoinClauseContext joinCtx,
            string joinTable,
            string joinAlias,
            string outerParam,
            Dictionary<string, string> aliasAccessPrefix,
            LinqAnonymousObjectNode flattenResultSelector,
            LinqQueryNode queryNode) {

            var onCondition = joinCtx.condition();

            if (!(onCondition is SqlParserParser.CompareConditionContext cmpCtx) || cmpCtx.op.GetText() != "=") {
                throw new NotSupportedException(
                    "[ERROR] LEFT JOIN ON condition must be a simple equality (a.Key = b.Key).");
            }

            var (outerKey, innerKey) = ResolveJoinKeys(cmpCtx, joinAlias, aliasAccessPrefix);

            queryNode.Methods.Add(new LinqLeftJoinNode {
                InnerTable = joinTable,
                OuterParam = outerParam,
                InnerParam = joinAlias,
                OuterKey = outerKey,
                InnerKey = innerKey,
                ResultSelector = flattenResultSelector,
                OuterAliases = aliasAccessPrefix.Values.ToList(),
            });
        }

        private (LinqNode OuterKey, LinqNode InnerKey) ResolveJoinKeys(
            SqlParserParser.CompareConditionContext cmpCtx,
            string joinAlias,
            Dictionary<string, string> aliasAccessPrefix) {

            string LeftAlias() =>
                cmpCtx.left is SqlParserParser.QualifiedColumnExprContext lq ? lq.IDENTIFIER(0).GetText() : null;

            string RightAlias() =>
                cmpCtx.right is SqlParserParser.QualifiedColumnExprContext rq ? rq.IDENTIFIER(0).GetText() : null;

            string leftAlias = LeftAlias();
            string rightAlias = RightAlias();

            bool leftIsKnown = leftAlias != null && aliasAccessPrefix.ContainsKey(leftAlias);
            bool rightIsKnown = rightAlias != null && aliasAccessPrefix.ContainsKey(rightAlias);
            bool leftIsNew = leftAlias == joinAlias;
            bool rightIsNew = rightAlias == joinAlias;

            if (leftIsKnown && rightIsNew) {
                return (Visit(cmpCtx.left), Visit(cmpCtx.right));
            }

            if (leftIsNew && rightIsKnown) {
                return (Visit(cmpCtx.right), Visit(cmpCtx.left));
            }

            throw new NotSupportedException(
                $"[ERROR] JOIN ON condition must directly compare a previously introduced " +
                $"table alias with the newly joined alias ('{joinAlias}'), " +
                $"e.g. '{joinAlias}.Key = otherAlias.Key'.");
        }

        // Used by RIGHT JOIN where we already know which side is outer and which is inner —
        // outerAlias is the right-hand (preserved) table, innerAlias is the left-hand table.
        private (LinqNode OuterKey, LinqNode InnerKey) ResolveJoinKeysDirectional(
            SqlParserParser.CompareConditionContext cmpCtx,
            string outerAlias,
            string innerAlias) {

            string LeftAlias() =>
                cmpCtx.left is SqlParserParser.QualifiedColumnExprContext lq ? lq.IDENTIFIER(0).GetText() : null;

            string RightAlias() =>
                cmpCtx.right is SqlParserParser.QualifiedColumnExprContext rq ? rq.IDENTIFIER(0).GetText() : null;

            if (LeftAlias() == outerAlias && RightAlias() == innerAlias) {
                return (Visit(cmpCtx.left), Visit(cmpCtx.right));
            }

            if (LeftAlias() == innerAlias && RightAlias() == outerAlias) {
                return (Visit(cmpCtx.right), Visit(cmpCtx.left));
            }

            throw new NotSupportedException(
                $"[ERROR] RIGHT JOIN ON condition must compare '{outerAlias}' with '{innerAlias}'.");
        }

        private Dictionary<string, string> BuildJoinKeyAliasMap(
            Dictionary<string, string> aliasAccessPrefix,
            string outerParam,
            bool outerIsWrapper,
            string joinAlias) {

            var map = new Dictionary<string, string>();

            foreach (var known in aliasAccessPrefix.Keys) {
                map[known] = outerIsWrapper ? $"{outerParam}.{known}" : known;
            }
            map[joinAlias] = joinAlias;

            return map;
        }

        private LinqAnonymousObjectNode BuildFlattenResultSelector(IEnumerable<string> knownAliases, string joinAlias) {

            var node = new LinqAnonymousObjectNode();

            foreach (var known in knownAliases) {
                node.Properties.Add((null, new LinqIdentifierNode { Name = _joinKeyAliasMap[known] }));
            }
            node.Properties.Add((null, new LinqIdentifierNode { Name = joinAlias }));

            return node;
        }

        private string AdvanceAliasAccessPrefix(Dictionary<string, string> aliasAccessPrefix, string joinAlias, int joinStepIndex) {

            string wrapperName = $"j{joinStepIndex}";

            foreach (var known in aliasAccessPrefix.Keys.ToList()) {
                aliasAccessPrefix[known] = $"{wrapperName}.{known}";
            }
            aliasAccessPrefix[joinAlias] = $"{wrapperName}.{joinAlias}";

            return wrapperName;
        }

        // SELECT column list processing

        public override LinqNode VisitColumnList([NotNull] SqlParserParser.ColumnListContext context) {
            if (context.STAR() != null) return null;

            var anonNode = new LinqAnonymousObjectNode();
            foreach (var item in context.selectItem()) {

                var exprNode = Visit(item.expr());

                string aliasName = item.IDENTIFIER() != null ? item.IDENTIFIER().GetText() : null;

                if (aliasName == null && exprNode is LinqIdentifierNode idNode) {
                    string code = idNode.Name;
                    if (code == "g.Key") {
                        if (item.expr() is SqlParserParser.ColumnExprContext colCtx2) {
                            aliasName = ToPascalCase(colCtx2.GetText());
                        }
                    } else if (code.StartsWith("g.Key.")) {
                        aliasName = code.Substring("g.Key.".Length);
                    }
                }

                anonNode.Properties.Add((aliasName, exprNode));
            }
            return anonNode;
        }

        // Aggregate and string function processing

        private static readonly System.Collections.Generic.HashSet<string> StringFunctions =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase) {
                "UPPER", "LOWER", "TRIM", "LTRIM", "RTRIM", "LENGTH",
                "ABS", "FLOOR", "CEIL", "CEILING", "SQRT", "SIGN", "ROUND",
                "COALESCE", "NULLIF"
            };

        public override LinqNode VisitAggregateExpr([NotNull] SqlParserParser.AggregateExprContext context) {

            string rawName = context.IDENTIFIER().GetText();

            if (StringFunctions.Contains(rawName)) {

                var node = new LinqStringFunctionNode { FunctionName = rawName.ToUpperInvariant() };

                if (context.expr() != null) node.Arguments.Add(Visit(context.expr()));
                return node;
            }

            string funcName = ToPascalCase(rawName);
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

        // Two-argument functions: COALESCE, NULLIF, SUBSTRING (without length)

        public override LinqNode VisitStringFunc2Expr([NotNull] SqlParserParser.StringFunc2ExprContext context) {

            string funcName = context.IDENTIFIER().GetText().ToUpperInvariant();
            var node = new LinqStringFunctionNode { FunctionName = funcName };
            node.Arguments.Add(Visit(context.expr(0)));
            node.Arguments.Add(Visit(context.expr(1)));
            return node;
        }

        // Three-argument functions: SUBSTRING

        public override LinqNode VisitStringFunc3Expr([NotNull] SqlParserParser.StringFunc3ExprContext context) {

            string funcName = context.IDENTIFIER().GetText().ToUpperInvariant();
            var node = new LinqStringFunctionNode { FunctionName = funcName };
            node.Arguments.Add(Visit(context.expr(0)));
            node.Arguments.Add(Visit(context.expr(1)));
            node.Arguments.Add(Visit(context.expr(2)));
            return node;
        }

        // Conditions processing

        public override LinqNode VisitCompareCondition([NotNull] SqlParserParser.CompareConditionContext context) {

            string op = context.op.GetText();

            bool isOrderingOp = op == ">" || op == "<" || op == ">=" || op == "<=";

            if (isOrderingOp && (context.left is SqlParserParser.StringExprContext || context.right is SqlParserParser.StringExprContext)) {
                throw new NotSupportedException(
                    $"[ERROR] Ordering operator '{op}' is not supported for string operands. " +
                    $"Strings can only be compared with '=' or '<>'.");
            }

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

            if (context.low is SqlParserParser.StringExprContext || context.high is SqlParserParser.StringExprContext) {
                throw new NotSupportedException(
                    $"[ERROR] BETWEEN is not supported for string operands. " +
                    $"String ordering has no equivalent in LINQ.");
            }

            var leftNode = Visit(context.left);

            var lowerBound = new LinqBinaryExpressionNode {
                Left = leftNode,
                Operator = ">=",
                Right = Visit(context.low)
            };

            var upperBound = new LinqBinaryExpressionNode {
                Left = leftNode,
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

            if (context.NOT() != null) {
                return new LinqUnaryExpressionNode { Operator = "!", Operand = inNode };
            }

            return inNode;
        }

        public override LinqNode VisitBooleanColumnCondition([NotNull] SqlParserParser.BooleanColumnConditionContext context) {
            return Visit(context.expr());
        }

        // CASE WHEN 

        public override LinqNode VisitCaseExprAlt([NotNull] SqlParserParser.CaseExprAltContext context) {
            return Visit(context.caseExpr());
        }

        public override LinqNode VisitCaseExpr([NotNull] SqlParserParser.CaseExprContext context) {

            var caseNode = new LinqCaseNode();

            var allExprs = context.expr();
            var conditions = context.condition();

            bool hasOperand = context.caseOperand != null;
            int thenOffset = hasOperand ? 1 : 0;

            if (hasOperand) {
                caseNode.Operand = Visit(allExprs[0]);
            }

            for (int i = 0; i < conditions.Length; i++) {
                caseNode.WhenClauses.Add(new LinqCaseWhenClause {
                    Condition = Visit(conditions[i]),
                    Result = Visit(allExprs[thenOffset + i])
                });
            }

            if (context.elseExpr != null) {
                caseNode.ElseExpression = Visit(context.elseExpr);
            }

            return caseNode;
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

        public override LinqNode VisitQualifiedColumnExpr([NotNull] SqlParserParser.QualifiedColumnExprContext context) {

            string tableAlias = context.IDENTIFIER(0).GetText();
            string columnName = ToPascalCase(context.IDENTIFIER(1).GetText());

            if (_joinKeyAliasMap != null && _joinKeyAliasMap.TryGetValue(tableAlias, out var accessPrefix)) {
                return new LinqIdentifierNode { Name = $"{accessPrefix}.{columnName}" };
            }

            if (_hasJoin) {
                return new LinqIdentifierNode { Name = $"x.{tableAlias}.{columnName}" };
            }

            return new LinqIdentifierNode { Name = $"{tableAlias}.{columnName}" };
        }

        // Column expression processing, converting to C# property access, considering GROUP BY context
        public override LinqNode VisitColumnExpr([NotNull] SqlParserParser.ColumnExprContext context) {

            string rawColumnName = ToPascalCase(context.GetText());

            if (_inOrderBy && _selectAliases.ContainsKey(rawColumnName)) {

                string resolved = _selectAliases[rawColumnName];

                if (resolved == rawColumnName) {
                    return new LinqIdentifierNode { Name = $"g.FirstOrDefault().{rawColumnName}" };
                }
                rawColumnName = resolved;
            } else if (_selectAliases.ContainsKey(rawColumnName)) {
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

                        var groupKeys = selectCtx.groupClause().groupItem()
                            .Select(item => {
                                var ids = item.IDENTIFIER();
                                return ids.Length == 2
                                    ? ToPascalCase(ids[1].GetText())
                                    : ToPascalCase(ids[0].GetText());
                            }).ToList();

                        if (groupKeys.Count > 0) {

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

        public override LinqNode VisitFloatExpr([NotNull] SqlParserParser.FloatExprContext context) {
            return new LinqConstantNode { Value = double.Parse(context.GetText(), System.Globalization.CultureInfo.InvariantCulture) };
        }

        // Parenthesized expression: (expr)

        public override LinqNode VisitParenExpr([NotNull] SqlParserParser.ParenExprContext context) {
            return new LinqParensNode { InnerNode = Visit(context.expr()) };
        }

        // COUNT(DISTINCT col) — maps to .Select(x => x.Col).Distinct().Count()
        // but inside GROUP BY context it maps to g.Select(x => x.Col).Distinct().Count()

        public override LinqNode VisitDistinctAggregateExpr([NotNull] SqlParserParser.DistinctAggregateExprContext context) {

            string rawName = context.IDENTIFIER().GetText().ToUpperInvariant();

            if (rawName != "COUNT") {
                throw new NotSupportedException(
                    $"[ERROR] DISTINCT is only supported inside COUNT(). Got: {rawName}(DISTINCT ...).");
            }

            _inAggregate = true;
            LinqNode argNode = Visit(context.expr());
            _inAggregate = false;

            return new LinqIdentifierNode {
                Name = $"g.Select(x => {argNode.ToCodeString()}).Distinct().Count()"
            };
        }

        // String literal processing, converting to C# string constant

        public override LinqNode VisitStringExpr([NotNull] SqlParserParser.StringExprContext context) {
            return new LinqConstantNode { Value = context.GetText().Trim('\'') };
        }

        // String concatenation: a || b  →  string.Concat(a, b)
        // Also handles CONCAT(a, b) via stringFunc2Expr → LinqStringFunctionNode

        public override LinqNode VisitConcatExpr([NotNull] SqlParserParser.ConcatExprContext context) {
            var left = Visit(context.left);
            var right = Visit(context.right);
            return new LinqIdentifierNode {
                Name = $"string.Concat({left.ToCodeString()}, {right.ToCodeString()})"
            };
        }
    }
}
Ez a dokumentum bemutatja az SQL -> LINQ transzpiler működési folyamatát a nyers SQL-től a futtatható C# kódig.

```mermaid
flowchart TD

&#x20;   Input\["Source SQL Text<br/><i>'SELECT Name FROM Users LIMIT 5;'</i>"] --> Lexer\[SqlParserLexer]

&#x20;   

&#x20;   subgraph ANTLR4 \["ANTLR4 Parsing Stage"]

&#x20;       Lexer -->|Token Stream| Parser(\[SqlParserParser])

&#x20;       Parser -->|Syntax Parsing| AntlrTree\["ANTLR Parse Tree<br/><i>(Generic / Rule Nodes)</i>"]

&#x20;   end



&#x20;   subgraph Transform \["Transformation Stage"]

&#x20;       AntlrTree -->|SqlVisitor.Visit| Visitor\["SqlVisitor"]

&#x20;       Visitor -->|AST Mapping| LinqAst\["Custom LINQ AST<br/><i>(LinqQueryNode, LinqMethodCallNode, stb.)</i>"]

&#x20;   end



&#x20;   subgraph CodeGen \["Code Generation Stage"]

&#x20;       LinqAst -->|ToCodeString| CodeGenNode\["Code Generator"]

&#x20;   end

&#x20;   

&#x20;   CodeGenNode --> Output\["Generated LINQ C# Text<br/><i>'db.Users.Take(5).Select(...).ToList()'</i>"]




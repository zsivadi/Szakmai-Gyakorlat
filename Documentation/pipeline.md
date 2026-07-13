\# Transpiler Processing Pipeline



Ez a dokumentum bemutatja az SQL -> LINQ transzpiler működési folyamatát a nyers SQL-től a futtatható C# kódig.



\## Pipeline Diagram



```mermaid

flowchart TD

&#x20;   Input\["Source SQL Text\\n'SELECT Name FROM Users LIMIT 5;'"] --> Lexer\[SqlParserLexer]



&#x20;   subgraph ANTLR4 \["ANTLR4 Parsing Stage"]

&#x20;       Lexer -->|Token Stream| Parser(\[SqlParserParser])

&#x20;       Parser -->|Syntax Parsing| AntlrTree\["ANTLR Parse Tree\\n(Generic / Rule Nodes)"]

&#x20;   end



&#x20;   subgraph Transform \["Transformation Stage"]

&#x20;       AntlrTree -->|SqlVisitor.Visit| Visitor\[SqlVisitor]

&#x20;       Visitor -->|AST Mapping| LinqAst\["Custom LINQ AST\\n(LinqQueryNode, LinqMethodCallNode)"]

&#x20;   end



&#x20;   subgraph CodeGen \["Code Generation Stage"]

&#x20;       LinqAst -->|ToCodeString| CodeGenNode\[Code Generator]

&#x20;   end

&#x20;   

&#x20;   CodeGenNode --> Output\["Generated LINQ C# Text\\n'db.Users.Take(5).Select(...).ToList()'"]


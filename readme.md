# SQL → LINQ Transpiler

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp)
![ANTLR4](https://img.shields.io/badge/ANTLR-4-EF3B27)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow)

SQL:1999 kompatibilis lekérdezéseket EF Core 8.0 kompatibilis LINQ metódusszintaxisra fordító transpiler C#-ban.

**[Élő demo](https://zsivadi.github.io/Szakmai-Gyakorlat/)**

---

## Miről mire fordít

Az eszköz SQL utasításokat vesz bemenetként és provider-független EF Core LINQ metódushívásokat állít elő kimenetként, amelyek közvetlenül használhatók bármely EF Core által támogatott adatbázis-környezetben.

```
SQL utasítás  →  ANTLR4 parser  →  SQL AST  →  LINQ AST  →  C# LINQ kód
```

---

## Támogatott SQL elemek

### SELECT
| Funkció | Példa |
|---|---|
| Alap lekérdezés, szűrés | `SELECT`, `WHERE`, `DISTINCT` |
| Rendezés, lapozás | `ORDER BY`, `LIMIT`, `OFFSET` |
| Csoportosítás | `GROUP BY`, `HAVING` |
| Összekapcsolás | `INNER JOIN`, `LEFT JOIN`, `RIGHT JOIN`, `CROSS JOIN` |
| Halmazműveletek | `UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT` |
| Allekérdezések | `FROM (SELECT ...)`, `WHERE col IN (SELECT ...)`, `EXISTS`, korrelált allekérdezések |
| Feltételes kifejezés | `CASE WHEN ... THEN ... ELSE ... END` |
| Aggregáció | `COUNT`, `SUM`, `AVG`, `MIN`, `MAX` |
| Szövegfüggvények | `UPPER`, `LOWER`, `TRIM`, `LTRIM`, `RTRIM`, `LEN`, `SUBSTRING`, `REPLACE`, `STUFF`, `CHARINDEX`, `CONCAT` |
| Dátumfüggvények | `GETDATE`, `GETUTCDATE`, `CURRENT_DATE`, `CURRENT_TIMESTAMP`, `YEAR`, `MONTH`, `DAY`, `DATEADD`, `DATEDIFF`, `DATEPART` |
| Egyéb | `LIKE`, `BETWEEN`, `IN`, `IS NULL`, `IS NOT NULL`, `COALESCE`, `ISNULL`, `NULLIF`, `CAST`, `ABS`, `ROUND`, `CEILING`, `FLOOR` |

### DML
| Utasítás | Támogatott alakok |
|---|---|
| `DELETE` | `WHERE` feltétellel és anélkül, korrelált allekérdezésekkel |
| `INSERT` | `VALUES` (egy- és többsoros), `INSERT INTO ... SELECT` |
| `UPDATE` | `SET` konstans és kifejezés értékkel, `WHERE` feltétellel és anélkül, `NULL` értékkel |

---

## Architektúra

A fordítás négy lépésben zajlik:

**1. Lexikális és szintaktikai elemzés** - Az ANTLR4-gyel generált lexer és parser feldolgozza a bemeneti SQL szöveget és egy parse tree-t állít elő. A `ThrowingErrorListener` szintaktikai hiba esetén azonnal `SqlSyntaxException`-t dob.

**2. SQL AST → LINQ AST** - A `SqlVisitor` bejárja a parse tree-t és egy saját LINQ AST-ot épít fel (`LinqAst.cs`). Az AST node-ok típusosan reprezentálják a LINQ konstrukciókat: `LinqQueryNode`, `LinqMethodCallNode`, `LinqLambdaNode`, `LinqJoinNode`, `LinqDeleteNode`, `LinqInsertNode`, `LinqUpdateNode` stb.

**3. Kódgenerálás** - Minden AST node `ToCodeString()` metódusa rekurzívan állítja elő a végleges C# kódot.

**4. Névkonvenciók** - A tábla- és oszlopneveket a fordító automatikusan PascalCase-re alakítja, snake_case bemenetet is kezelve.

---

## Tesztelés

A projekt három szintű tesztelési stratégiát alkalmaz:

**Transzpiler tesztek** — Az `IOPairs.json` fájl 275+ SQL→LINQ párt tartalmaz. A `TranspilerTests` minden párra string-szintű egyezést ellenőriz.

**Szemantikai tesztek** — A `SemanticEquivalenceTests` Roslyn-alapú futtatókörnyezetben hajtja végre a generált LINQ kódot egy SQLite in-memory adatbázison, és az eredményhalmazt összeveti a nyers SQL által visszaadott sorokkal.

**Fuzz tesztek** — A `RandomSqlGenerator` véletlenszerű érvényes SQL lekérdezéseket generál SELECT, DELETE, INSERT és UPDATE utasításokra egyaránt. A `FuzzTests` 5500 iterációban szintaktikai helyességet, a `SemanticEquivalenceTests` szemantikai egyenértékűséget ellenőriz.

---

## Korlátok

- CTE (`WITH ... AS`) nem támogatott
- `FULL OUTER JOIN` nem támogatott
- `NULLS FIRST` / `NULLS LAST` rendezési módosítók nem támogatottak
- `LEFT` / `RIGHT` szövegfüggvények nem támogatottak 
- `COUNT(DISTINCT ...)` csak `GROUP BY` kontextusban támogatott
- Ablakfüggvények (`ROW_NUMBER`, `RANK` stb.) nem támogatottak

---

## Technológiák

- [.NET 10](https://dotnet.microsoft.com/)
- [ANTLR 4](https://www.antlr.org/) - lexer és parser generálás
- [Entity Framework Core 8](https://learn.microsoft.com/en-us/ef/core/) - célplatform
- [Roslyn](https://github.com/dotnet/roslyn) - szemantikai tesztekhez runtime C# fordítás
- [NUnit](https://nunit.org/) — tesztelési keretrendszer
- [SQLite in-memory](https://www.sqlite.org/) - szemantikai tesztek adatbázisa
- [Blazor WebAssembly](https://dotnet.microsoft.com/en-us/apps/aspnet/web-apps/blazor) - webes demo
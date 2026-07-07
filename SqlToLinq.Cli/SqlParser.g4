grammar SqlParser;

query : statement SEMICOLON? EOF ;

statement : selectStmt 
          | updateStmt 
          | insertStmt 
          | deleteStmt 
          ;


selectStmt : SELECT columnList FROM tableName (WHERE condition)? ;

updateStmt : UPDATE tableName SET setClause (WHERE condition)? ;

insertStmt : INSERT INTO tableName ('(' idList ')')? VALUES '(' valueList ')' ;

deleteStmt : DELETE FROM tableName (WHERE condition)? ;


setClause : assignment (COMMA assignment)* ;

assignment : IDENTIFIER EQ expr ;

columnList : STAR
           | idList ;

idList : IDENTIFIER (COMMA IDENTIFIER)* ;

valueList : expr (COMMA expr)* ;

tableName : IDENTIFIER ;


condition : '(' condition ')'                          # parensCondition
          | left=condition AND right=condition         # andCondition
          | left=condition OR right=condition          # orCondition
          | left=expr op=compOp right=expr             # compareCondition
          ;

expr : left=expr op=mathOp right=expr                  # mathExpr
     | IDENTIFIER                                      # columnExpr
     | NUMBER                                          # numberExpr
     | STRING_LITERAL                                  # stringExpr
     ;

compOp : EQ | NEQ | GT | LT | GTE | LTE ;
mathOp : PLUS | MINUS | STAR | DIV ;


UPDATE : [Uu][Pp][Dd][Aa][Tt][Ee] ;
SET    : [Ss][Ee][Tt] ;
INSERT : [Ii][Nn][Ss][Ee][Rr][Tt] ;
INTO   : [Ii][Nn][Tt][Oo] ;
VALUES : [Vv][Aa][Ll][Uu][Ee][Ss] ;
DELETE : [Dd][Ee][Ll][Ee][Tt][Ee] ;
SELECT : [Ss][Ee][Ll][Ee][Cc][Tt] ;
FROM   : [Ff][Rr][Oo][Mm] ;
WHERE  : [Ww][Hh][Ee][Rr][Ee] ;
AND    : [Aa][Nn][Dd] ;
OR     : [Oo][Rr] ;

STAR      : '*' ;
COMMA     : ',' ;
SEMICOLON : ';' ;

EQ  : '=' ;
NEQ : '<>' ;
GT  : '>' ;
LT  : '<' ;
GTE : '>=' ;
LTE : '<=' ;

PLUS  : '+' ;
MINUS : '-' ;
DIV   : '/' ;

IDENTIFIER     : [a-zA-Z_][a-zA-Z0-9_]* ; 
NUMBER         : [0-9]+ ;                 
STRING_LITERAL : '\'' ~'\''* '\'' ;       

WS : [ \t\r\n]+ -> skip ;
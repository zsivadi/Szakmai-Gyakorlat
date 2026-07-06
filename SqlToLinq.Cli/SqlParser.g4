grammar SqlParser;


query : selectStmt SEMICOLON? EOF ;

selectStmt : SELECT columnList FROM tableName (WHERE condition)? ;

columnList : STAR
           | IDENTIFIER (COMMA IDENTIFIER)* ;

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
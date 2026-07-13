grammar SqlParser;

query : statement SEMICOLON? EOF ;

statement : selectStmt 
          | updateStmt 
          | insertStmt 
          | deleteStmt 
          ;



selectStmt : SELECT columnList FROM tableName (WHERE condition)?
			 groupClause? havingClause? orderClause? limitClause? offsetClause? ;

updateStmt : UPDATE tableName SET setClause (WHERE condition)? ;

insertStmt : INSERT INTO tableName ('(' idList ')')? VALUES '(' valueList ')' ;

deleteStmt : DELETE FROM tableName (WHERE condition)? ;



setClause : assignment (COMMA assignment)* ;

assignment : IDENTIFIER EQ expr ;

columnList : STAR
           | selectItem (COMMA selectItem)* ;

selectItem : expr (AS? IDENTIFIER)? ;

groupClause : GROUP BY idList ;

idList : IDENTIFIER (COMMA IDENTIFIER)* ;

valueList : expr (COMMA expr)* ;

tableName : IDENTIFIER ;

havingClause : HAVING condition ;

orderClause  : ORDER BY orderItem (COMMA orderItem)* ;

orderItem    : expr (ASC | DESC)? ;

limitClause  :LIMIT expr ;

offsetClause : OFFSET expr ;



condition : '(' condition ')'                          		# parensCondition
          | left=expr op=compOp right=expr             		# compareCondition
          | left=expr LIKE right=STRING_LITERAL        		# likeCondition
		  | NOT condition                                   # notCondition
		  | left=expr NOT? LIKE right=STRING_LITERAL        # likeCondition
		  | left=expr NOT? BETWEEN low=expr AND high=expr   # betweenCondition
		  | left=condition AND right=condition         		# andCondition
          | left=condition OR right=condition          		# orCondition
          ;

expr : left=expr op=mathOp right=expr                  # mathExpr
     | IDENTIFIER '(' (STAR | expr) ')'                # aggregateExpr
     | IDENTIFIER                                      # columnExpr
     | NUMBER                                          # numberExpr
     | STRING_LITERAL                                  # stringExpr
     ;



compOp : EQ | NEQ | GT | LT | GTE | LTE ;
mathOp : PLUS | MINUS | STAR | DIV ;



UPDATE 	: [Uu][Pp][Dd][Aa][Tt][Ee] ;
SET    	: [Ss][Ee][Tt] ;
INSERT 	: [Ii][Nn][Ss][Ee][Rr][Tt] ;
INTO   	: [Ii][Nn][Tt][Oo] ;
VALUES 	: [Vv][Aa][Ll][Uu][Ee][Ss] ;
DELETE 	: [Dd][Ee][Ll][Ee][Tt][Ee] ;
SELECT 	: [Ss][Ee][Ll][Ee][Cc][Tt] ;
FROM   	: [Ff][Rr][Oo][Mm] ;
WHERE  	: [Ww][Hh][Ee][Rr][Ee] ;
AND    	: [Aa][Nn][Dd] ;
OR     	: [Oo][Rr] ;
LIKE   	: [Ll][Ii][Kk][Ee] ;
ORDER  	: [Oo][Rr][Dd][Ee][Rr] ;
BY    	: [Bb][Yy] ;
ASC  	: [Aa][Ss][Cc] ;
DESC   	: [Dd][Ee][Ss][Cc] ;
HAVING 	: [Hh][Aa][Vv][Ii][Nn][Gg] ;
AS     	: [Aa][Ss] ;
GROUP  	: [Gg][Rr][Oo][Uu][Pp] ;
BETWEEN : [Bb][Ee][Tt][Ww][Ee][Ee][Nn] ;
NOT     : [Nn][Oo][Tt] ;
LIMIT 	: [Ll][Ii][Mm][Ii][Tt] ;
OFFSET	: [Oo][Ff][Ff][Ss][Ee][Tt] ;



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
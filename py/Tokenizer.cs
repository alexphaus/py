using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;

namespace Py
{
    public enum Precedence
    {
        None,
        Exponentiation,
        Unary,
        Multiplicative,
        Additive,
        Shift,
        BitwiseAnd,
        BitwiseXor,
        BitwiseOr,
        Relational,
        LogicalNot,
        LogicalAnd,
        LogicalOr,
        Key,
        Colon,
        Assignment
    }
    
    enum Capture
    {
        None,
        Identifier,
        Number,
        String,
        DocString,
        Comment,
        Indent,
        Lambda
    }

    public enum TokenType
    {
        Identifier,
        Member,
        Number,
        String,
        DocString,
        Operator,
        Parenthesis,
        Brackets,
        Braces
    }

    public class Token
    {
        public TokenType Type;
        public string Value;
        public bool EndsComma;
        public Exp Subset;
    }
    
    public class Op : Token
    {
        public Precedence Priority;
        public bool RightToLeft;
        public Exp Left;
        public Exp Right;
        public System.Linq.Expressions.ExpressionType NodeType;
    }

    public class Exp : List<Token>
    {
        public List<Exp> Body;
        public string Keyword;
        public Stack<Exp> Tiers;
        public int Line;

        public Token Last { get => this[Count - 1]; }
        public Token Pop()
        {
            var popped = this[Count - 1];
            RemoveAt(Count - 1);
            return popped;
        }

        public List<Exp> SplitComma()
        {
            var arr = new List<Exp>();
            var expr = new Exp();
            foreach (Token tok in this)
            {
                expr.Add(tok);
                if (tok.EndsComma)
                {
                    tok.EndsComma = false;
                    arr.Add(expr);
                    expr = new Exp();
                }
            }
            if (arr.Count > 0 || expr.Count > 0) arr.Add(expr);
            return arr;
        }

        public Exp AsTree()
        {
            var operands = new List<Exp>();
            var operators = new List<Op>();
            var u_expr = new Exp();
            Token tok;

            for (int i = 0; i < Count; i++)
            {
                tok = this[i];
                if (tok is Op op && u_expr.Count > 0) // binary
                {
                    operands.Add(u_expr);
                    operators.Add(op);
                    u_expr = new Exp();
                }
                else
                    u_expr.Add(tok);
            }

            // append last token
            operands.Add(u_expr);

            while (operators.Count > 0)
            {
                int i = IndexOfMin(operators);
                Op op = operators[i];
                op.Left = operands[i];
                op.Right = operands[i + 1];
                operands[i] = new Exp { op };
                operands.RemoveAt(i + 1);
                operators.RemoveAt(i);
            }

            return operands[0];
        }

        /// <summary>
        /// Get the index of the lowest priority value
        /// </summary>
        public static int IndexOfMin(List<Op> lst)
        {
            int minIndex = 0;
            Op min = lst[0], op;
            for (int i = 1; i < lst.Count; i++)
            {
                op = lst[i];
                if (op.Priority < min.Priority || min.RightToLeft && op.Priority == min.Priority)
                {
                    min = op;
                    minIndex = i;
                }
            }
            return minIndex;
        }
    }

    partial class Py
    {
        List<Exp> Tokenize(string src)
        {
            Capture cap = Capture.None;
            var str = new StringBuilder();
            bool isMember = false;
            bool fstring = false;
            Op lambda = null;
            var stack = new Stack<Exp>();
            stack.Push(new Exp()); // main

            var map = new Dictionary<string, List<Exp>>();
            var __main__ = new List<Exp>();
            string ind = string.Empty;
            map.Add(ind, __main__);
            Exp colonline = null;
            int ln = 1;

            void push(TokenType type, string value) =>
                stack.Peek().Add(new Token { Type = type, Value = value });

            void pushGroup(TokenType type)
            {
                var grp = new Token { Type = type, Subset = stack.Pop() };
                stack.Peek().Add(grp);
            }

            void pushOp(string op, ExpressionType nodeType, Precedence pre, bool rightToLeft)
            {
                stack.Peek().Add(new Op
                {
                    Type = TokenType.Operator,
                    Value = op,
                    Priority = pre,
                    NodeType = nodeType,
                    RightToLeft = rightToLeft
                });
            }

            char c, f, l = '\0';
            src = src + "\r\n\r\n";
            int i = 0, to = src.Length - 1;

            void pushOpOrAssign(string op, ExpressionType nodeType, Precedence pre, bool rightToLeft)
            {
                if (f == '=')
                {
                    pushOp(op, nodeType, Precedence.Assignment, true);
                    i++;
                }
                else
                    pushOp(op, nodeType, pre, rightToLeft);
            }

            for (; i < to; i++)
            {
                c = src[i]; // current char
                f = src[i + 1]; // following char

                switch (cap)
                {
                    case Capture.Identifier:
                        if (char.IsLetterOrDigit(c) || c == '_')
                            str.Append(c);
                        else
                        {
                            string id = str.ToString();

                            if (id == "lambda")
                            {
                                push(TokenType.Number, string.Empty); // binary behaviour
                                pushOp(id, 0, Precedence.Key, true);
                                lambda = (Op)stack.Peek().Last;
                                cap = Capture.Lambda;
                                goto case Capture.Lambda;
                            }

                            Exp expr = stack.Peek();
                            switch (id)
                            {
                                case "is":
                                    pushOp(id, ExpressionType.TypeIs, Precedence.Relational, false);
                                    break;

                                case "in":
                                    if (expr.Last is Op op && op.Value == "not")
                                    {
                                        op.Value = "notin";
                                        op.Priority = Precedence.Relational;
                                        op.RightToLeft = false;
                                    }
                                    else
                                        pushOp("__contains__", 0, Precedence.Relational, false);
                                    break;

                                case "not":
                                    if (expr.Count > 0 && expr.Last is Op op1 && op1.Value == "is")
                                    {
                                        op1.Value = "isnot";
                                        op1.Priority = Precedence.Relational;
                                    }
                                    else
                                        pushOp("not", ExpressionType.Not, Precedence.LogicalNot, true);
                                    break;

                                case "and":
                                    pushOp(id, ExpressionType.AndAlso, Precedence.LogicalAnd, false);
                                    break;

                                case "or":
                                    pushOp(id, ExpressionType.OrElse, Precedence.LogicalOr, false);
                                    break;

                                case "if": case "elif": case "else": case "for": case "while":
                                case "class": case "def":
                                case "try": case "except": case "finally":
                                case "import": case "from": case "as":
                                case "return": case "yield": case "break": case "continue": case "pass":
                                case "global": case "nonlocal":
                                case "del": case "raise": case "assert":
                                    if (expr.Count == 0)
                                        expr.Keyword = id;
                                    else
                                        pushOp(id, 0, Precedence.Key, true);
                                    break;

                                default:
                                    if (isMember)
                                    {
                                        push(TokenType.Member, id);
                                        isMember = false;
                                    }
                                    else
                                        push(TokenType.Identifier, id);
                                    break;
                            }
                            str.Clear();
                            cap = Capture.None;
                            goto case Capture.None;
                        }
                        break;

                    case Capture.Number:
                        if (char.IsDigit(c) || c == '.')
                            str.Append(c);
                        else
                        {
                            push(TokenType.Number, str.ToString());
                            str.Clear();
                            cap = Capture.None;
                            goto case Capture.None;
                        }
                        break;

                    case Capture.Indent:
                        if (c == ' ' || c == '\t')
                            str.Append(c);
                        else if (c == '\n')
                        {
                            str.Clear();
                            ln++;
                        }
                        else
                        {
                            ind = str.ToString();
                            // create new block if colon + \n was passed
                            if (colonline != null)
                            {
                                map[ind] = colonline.Body;
                                colonline = null;
                            }
                            str.Clear();
                            cap = Capture.None;
                            goto case Capture.None;
                        }
                        break;

                    case Capture.None:
                        switch (c)
                        {
                            case 'f':
                                if (f == '"' || f == '\'')
                                {
                                    fstring = true;
                                    continue;
                                }
                                goto case '_';

                            case '_':
                            case var _ when char.IsLetter(c):
                                str.Append(c);
                                cap = Capture.Identifier;
                                break;

                            case var _ when char.IsDigit(c):
                                str.Append(c);
                                cap = Capture.Number;
                                break;
                                
                            case ' ':
                            case '\t':
                            case '\r':
                                // pass
                                break;

                            case '\n':
                                if (stack.Count == 1) // outside parenthesis, brackets or braces
                                {
                                    Exp expr = stack.Pop(); // take the last expression
                                    expr.Line = ln; // set line number
                                    if (colonline is null) // if there was no colon
                                        map[ind].Add(expr); // add to the current block
                                    else // if there was a colon
                                    {
                                        if (expr.Count > 0) // if there is an expression just after colon
                                        {
                                            colonline.Body.Add(expr); // add to the colon block
                                            colonline = null; // reset colonline
                                        }
                                        // else: just ignore the colon
                                    }

                                    stack.Push(new Exp());
                                    cap = Capture.Indent;
                                }
                                ln++;
                                // else: pass parenthesis, brackets or braces (stack.Count > 1)
                                break;

                            case '.':
                                isMember = true; // set member flag
                                break;

                            case ':':
                                if (stack.Count == 1) // main block
                                {
                                    colonline = stack.Pop(); // take the last expression
                                    colonline.Body = new List<Exp>(); // create a new block

                                    if (colonline.Keyword == "else" ||
                                        colonline.Keyword == "elif") // if the last expression was an else or elif
                                    {
                                        var block = map[ind]; // get the current block
                                        Exp expr = block[block.Count - 1]; // get the last if, elif, for, try.. in the block
                                        if (expr.Tiers is null) expr.Tiers = new Stack<Exp>();
                                        expr.Tiers.Push(colonline); // add the colonline to the if, elif, for, try.. tiers
                                    }
                                    else // if the last expression was not an else or elif
                                        map[ind].Add(colonline); // add the colonline to the current block

                                    stack.Push(new Exp());
                                }
                                else
                                    pushOp(":", 0, Precedence.Colon, true); // push the colon separator
                                break;
                                
                            case ',':
                                stack.Peek().Last.EndsComma = true;
                                break;

                            case ';':
                                if (colonline is null)
                                    map[ind].Add(stack.Pop()); // add to the current block
                                else
                                    colonline.Body.Add(stack.Pop()); // add to the colon block
                                stack.Push(new Exp());
                                break;

                            case '(':
                            case '[':
                            case '{':
                                stack.Push(new Exp());
                                break;

                            case ')':
                                pushGroup(TokenType.Parenthesis);
                                break;

                            case ']':
                                pushGroup(TokenType.Brackets);
                                break;

                            case '}':
                                pushGroup(TokenType.Braces);
                                break;

                            case '"':
                            case '\'':
                                if (f == c && src[i + 2] == c)
                                {
                                    cap = Capture.DocString;
                                    i += 2;
                                }
                                else
                                    cap = Capture.String;
                                l = c;
                                break;

                            case '#':
                                cap = Capture.Comment;
                                break;

                            case '~':
                                pushOp("__invert__", ExpressionType.OnesComplement, Precedence.Unary, false);
                                break;

                            case '+':
                                pushOpOrAssign("__add__", ExpressionType.Add, Precedence.Additive, false);
                                break;

                            case '-':
                                if (f == '>') // function return typing
                                {
                                    pushOp(":", ExpressionType.TypeAs, Precedence.Colon, true);
                                    i++;
                                }
                                else
                                    pushOpOrAssign("__sub__", ExpressionType.Subtract, Precedence.Additive, false);
                                break;

                            case '*':
                                if (f == '*')
                                {
                                    i++;
                                    f = src[i + 1];
                                    pushOpOrAssign("__pow__", ExpressionType.Power, Precedence.Exponentiation, true);
                                }
                                else
                                    pushOpOrAssign("__mul__", ExpressionType.Multiply, Precedence.Multiplicative, false);
                                break;

                            case '/':
                                if (f == '/')
                                {
                                    i++;
                                    f = src[i + 1];
                                    pushOpOrAssign("__floordiv__", ExpressionType.Divide, Precedence.Multiplicative, false);
                                }
                                else
                                    pushOpOrAssign("__div__", ExpressionType.Divide, Precedence.Multiplicative, false);
                                break;

                            case '%':
                                pushOpOrAssign("__mod__", ExpressionType.Modulo, Precedence.Multiplicative, false);
                                break;

                            case '<':
                                if (f == '=')
                                {
                                    pushOp("__le__", ExpressionType.LessThanOrEqual, Precedence.Relational, false);
                                    i++;
                                }
                                else if (f == '<')
                                {
                                    i++;
                                    f = src[i + 1];
                                    pushOpOrAssign("__lshift__", ExpressionType.LeftShift, Precedence.Shift, false);
                                }
                                else
                                    pushOp("__lt__", ExpressionType.LessThan, Precedence.Relational, false);
                                break;

                            case '>':
                                if (f == '=')
                                {
                                    pushOp("__ge__", ExpressionType.GreaterThanOrEqual, Precedence.Relational, false);
                                    i++;
                                }
                                else if (f == '>')
                                {
                                    i++;
                                    f = src[i + 1];
                                    pushOpOrAssign("__rshift__", ExpressionType.RightShift, Precedence.Shift, false);
                                }
                                else
                                    pushOp("__gt__", ExpressionType.GreaterThan, Precedence.Relational, false);
                                break;

                            case '&':
                                pushOpOrAssign("__and__", ExpressionType.And, Precedence.BitwiseAnd, false);
                                break;

                            case '^':
                                pushOpOrAssign("__xor__", ExpressionType.ExclusiveOr, Precedence.BitwiseXor, false);
                                break;

                            case '|':
                                pushOpOrAssign("__or__", ExpressionType.Or, Precedence.BitwiseOr, false);
                                break;

                            case '!':
                                if (f == '=')
                                {
                                    pushOp("__ne__", ExpressionType.NotEqual, Precedence.Relational, false);
                                    i++;
                                }
                                else
                                    pushOp("__not__", ExpressionType.Not, Precedence.BitwiseOr, false);
                                break;

                            case '=':
                                if (f == '=')
                                {
                                    pushOp("__eq__", ExpressionType.Equal, Precedence.Relational, false);
                                    i++;
                                }
                                else // null is '='
                                    pushOp(null, ExpressionType.Assign, Precedence.Assignment, true);
                                break;
                            
                            case '@':
                                pushOpOrAssign("@", ExpressionType.TypeAs, Precedence.None, true);
                                break;
                        }
                        break;

                    case Capture.String:
                        if (c == l)
                        {
                            push(TokenType.String, str.ToString());
                            str.Clear();
                            fstring = false;
                            cap = Capture.None;
                        }
                        else if (c == '\r' || c == '\n')
                            throw new Exception("EOL while scanning string literal");
                        else if (c == '\\')
                        {
                            if (f == 'n') str.Append("\n");
                            else if (f == 'r') str.Append("\r");
                            else if (f == '\\') str.Append(c);
                            i++;
                        }
                        else if (c == '{' && fstring)
                        {
                            push(TokenType.String, str.ToString());
                            pushOp("__add__", ExpressionType.Add, Precedence.Additive, false);
                            str.Clear();
                        }
                        else if (c == '}' && fstring)
                        {
                            stack.Peek().AddRange(Tokenize(str.ToString())[0]);
                            pushOp("__add__", ExpressionType.Add, Precedence.Additive, false);
                            str.Clear();
                        }
                        else
                            str.Append(c);
                        break;

                    case Capture.DocString:
                        if (c == l && f == l && src[i + 2] == l)
                        {
                            push(TokenType.String, str.ToString());
                            str.Clear();
                            cap = Capture.None;
                            i += 2;
                        }
                        else
                        {
                            if (c == '\n') ln++;
                            str.Append(c);
                        }
                        break;

                    case Capture.Comment:
                        if (f == '\n')
                            cap = Capture.None;
                        break; // ignore comments

                    case Capture.Lambda:
                        if (c == ':')
                        {
                            lambda.Value = str.ToString();
                            lambda = null;
                            str.Clear();
                            cap = Capture.None;
                        }
                        else if (c != ' ')
                            str.Append(c);
                        break;
                }
            }
            return __main__;
        }
    }
}
using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Py
{
    class ColonExpression : Expression
    {
        public Expression Left, Right;
    }

    class CallMethodExpression : Expression
    {
        public Expression Object;
        public string Name;
    }

    class TypeExpression : Expression
    {
        public Type MyType;
        public string Name = "";

        public TypeExpression(Type t)
        {
            MyType = t;
            Name = t.FullName;
        }

        public TypeExpression(string name) => MyType = Py.GetType(Name = name);
        public override ExpressionType NodeType => ExpressionType.Constant;
        public override Type Type => typeof(object);
    }

    public class UnpackExpression : Expression
    {
        public bool IsDictionary;
        public Expression Iterable;
    }

    public class LocalBuilder : Dictionary<string, Expression>
    {
        public List<ParameterExpression> Variables = new List<ParameterExpression>();
        public string ClassName = null; // if not null, this is a class

        public ParameterExpression AddVar(ParameterExpression id)
        {
            Variables.Add(id);
            Add(id.Name, id);
            return id;
        }
    }

    public class ParameterInfo
    {
        public string Name;
        public Type Type;
        public Expression DefaultValue;
        public bool IsKwargs;
        public bool IsArgs;
    }

    public class KeyValue
    {
        public object Key;
        public object Value;

        public KeyValue(object key, object value)
        {
            Key = key;
            Value = value;
        }
    }

    public class Parser
    {
        public Func<double, Expression> ParseFloat = (double d) => Expression.Constant(d, typeof(object));
        public Func<string, Expression> ParseString = (string s) => Expression.Constant(s, typeof(object));
        public Func<bool, Expression> ParseBool = (bool b) => Expression.Constant(b, typeof(object));
        public Func<string, Type, ParameterExpression> Variable = (string name, Type type) => Expression.Variable(typeof(object), name);
        public Func<Expression, Expression, Expression> ParseAs = (obj, type) => Expression.Call(Py.MagicMethod("__as__"), obj, type);
        public Func<Expression, Expression, Expression> ParseAt = (obj, type) =>
        {
            if (type is ConstantExpression c && c.Value is string s)
                return Expression.Convert(Expression.Convert(obj, Py.GetType(s)), typeof(object));
            return obj;
        };
        public Func<Expression, Expression, Expression> ParseIs = (objA, objB) =>
            Expression.Call(Py.MagicMethod("__is__"), objA, objB);
        public Func<Op, Expression, Expression, Expression> ParseOperation = (op, left, right) =>
            Expression.Call(Py.MagicMethod(op.Value), left, right);
        public Func<Op, Expression, Expression> ParseUnary = (op, expr) =>
            Expression.Call(Py.MagicMethod(op.Value), expr);
        public Func<Expression, string, Expression> ParseGetAttribute = (obj, name) =>
            Expression.Call(Py.MagicMethod("__getattribute__"), obj, Expression.Constant(name));
        public Func<Expression, string, Expression, Expression> ParseSetAttr = (obj, name, value) =>
            Expression.Call(Py.MagicMethod("__setattr__"), obj, Expression.Constant(name), value);
        public Func<Expression, Expression, Expression> ParseGetItem = (obj, index) =>
            Expression.Call(Py.MagicMethod("__getitem__"), obj, index);
        public Func<Expression, Expression, Expression, Expression> ParseSetItem = (obj, index, value) =>
            Expression.Call(Py.MagicMethod("__setitem__"), obj, index, value);
        public Func<Expression, Expression, Expression> ParseAnd;
        public Func<Expression, Expression, Expression> ParseOr;
        public Func<Expression, Expression> ParseNot = (expr) => Expression.Convert(Expression.Not(Py.AsBool(expr)), typeof(object));
    }

    partial class Py
    {
        Stack<LocalBuilder> Locals = new Stack<LocalBuilder>();

        /* control flow */
        Stack<LabelTarget> ret = new Stack<LabelTarget>();
        Stack<LabelTarget> con = new Stack<LabelTarget>();
        Stack<LabelTarget> br = new Stack<LabelTarget>();

        Stack<Expression> Decorators = new();

        public static Dictionary<string, Type> Types = new Dictionary<string, Type>
        {
            { "int", typeof(int) },
            { "float", typeof(double) },
            { "str", typeof(string) },
            { "bool", typeof(bool) },
            { "object", typeof(Object) },
            { "type", typeof(Class) },
            { "list", typeof(List<object>) },
            { "tuple", typeof(object[]) },
            { "dict", typeof(Dictionary<object, object>) },
            { "set", typeof(HashSet<object>) },
            { "function", typeof(Function) },
            { "Object", typeof(object) },
            { "Type", typeof(Type) }
        };

        public static Type GetType(string name)
        {
            if (Types.TryGetValue(name, out Type type))
                return type;
            else
                return System.Type.GetType(name);
        }

        public static System.Reflection.MethodInfo MagicMethod(string name)
        {
            return typeof(Py).GetMethod(name);
        }

        static Parser PyParser = new Parser();
        static Parser CParser = new Parser
        {
            ParseFloat = (double d) => Expression.Constant(d),
            ParseString = (string s) => Expression.Constant(s),
            ParseBool = (bool b) => Expression.Constant(b),
            Variable = (string name, Type type) => Expression.Variable(type, name),
            ParseUnary = (op, expr) => Expression.MakeUnary(op.NodeType, expr, expr.Type),
            ParseOperation = (op, left, right) =>
            {
                if (op.NodeType == ExpressionType.Add && left.Type == typeof(string) && right.Type == typeof(string))
                    return Expression.Call(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) }), left, right);
                return Expression.MakeBinary(op.NodeType, left, right);
            },
            ParseGetAttribute = (obj, name) => {
                if (obj is TypeExpression t)
                {
                    if (t.MyType is null)
                        return new TypeExpression($"{t.Name}.{name}");
                    if (HasMethod(t.MyType, name))
                        return new CallMethodExpression { Object = obj, Name = name };
                    if (t.MyType.GetProperty(name) != null)
                        return Expression.Property(null, t.MyType, name);
                    if (t.MyType.GetField(name) != null)
                        return Expression.Field(null, t.MyType, name);
                }
                if (HasMethod(obj.Type, name))
                    return new CallMethodExpression { Object = obj, Name = name };
                if (obj.Type == typeof(Object) && obj.Type.GetField(name) is null && obj.Type.GetProperty(name) is null)
                    return Expression.Property(Expression.Field(obj, "Dict"), "Item", Expression.Constant(name));
                return Expression.PropertyOrField(obj, name);
            },
            ParseSetAttr = (obj, name, value) => Expression.Assign(CParser.ParseGetAttribute(obj, name), value),
            ParseAs = (obj, type) => Expression.Call(Py.MagicMethod("__as__"), obj, type),
            ParseAt = (obj, type) =>
            {
                if (type is TypeExpression t)
                    return Expression.Convert(obj, t.MyType);
                if (obj is ConstantExpression c && c.Value is null)
                    return new TypeExpression(type.Type);
                return Expression.Convert(obj, type.Type);
            },
            ParseIs = (objA, objB) => Expression.TypeIs(objA, ((TypeExpression)objB).MyType),
            ParseGetItem = (obj, index) => {
                if (obj is TypeExpression t)
                {
                    var exprs = index is NewArrayExpression arr ? arr.Expressions.Cast<TypeExpression>().ToArray() : new[] { (TypeExpression)index };
                    return new TypeExpression($"{t.Name}`{exprs.Length}[{string.Join(",", exprs.Select(x => x.Name))}]");
                }
                return Expression.MakeIndex(obj, obj.Type.GetProperties().FirstOrDefault(x => x.GetIndexParameters().Length == 1), new[] { index });
            },
            ParseSetItem = (obj, index, value) => Expression.Assign(CParser.ParseGetItem(obj, index), value),
            ParseAnd = (left, right) => Expression.AndAlso(left, right),
            ParseOr = (left, right) => Expression.OrElse(left, right),
            ParseNot = (expr) => Expression.Not(expr)
        };
        public static Parser Parser = PyParser;
        bool typing => Parser == CParser;

        public static Expression AsBool(Expression expr)
        {
            if (expr.Type == typeof(bool))
                return expr;
            return Expression.Call(MagicMethod("__bool__"), expr);
        }

        Expression GlobalAccess(string name) =>
            Expression.Property(_G, "Item", Expression.Constant(name));

        Expression LocalAccess(string name, Type newVarType)
        {
            if (Locals.Count == 0)
                return GlobalAccess(name);

            else if (Locals.Peek().TryGetValue(name, out Expression var))
                return var;
            else
                return Locals.Peek().AddVar(Parser.Variable(name, newVarType));
        }

        public static string GenerateId() => Guid.NewGuid().ToString("N");

        (Expression Variable, Expression Assignment) VariableAssign(string name, Expression value)
        {
            var id = LocalAccess(name, value.Type);
            return (id, Expression.Assign(id, value));
        }

        Expression CallBuiltinFunc(string name, params Expression[] args)
        {
            var builtins = Expression.Field(null, typeof(Py).GetField("Builtins"));
            return Expression.Call(builtins, typeof(Py).GetMethod("CallFunc"),
                Expression.Constant(name), Expression.NewArrayInit(typeof(object), args));
        }

        string JoinDot(Exp expr)
        {
            return string.Join(".", expr.Select(x => x.Value));
        }

        Expression ParseTuple(List<Exp> expr)
        {
            if (expr.Count == 1)
                return ParseOperation(expr[0]);

            // single item
            if (expr.Count == 2 && expr[1].Count == 0)
                expr.RemoveAt(1);

            var items = expr.Select(ParseOperation).ToArray();

            // revise
            if (typing)
            {
                Type type = items.Length > 0 ? items[0].Type : typeof(object);
                return Expression.NewArrayInit(type, items);
            }

            if (TryUnpack(items, out var inits))
            {
                inits[^1] = Expression.Call(inits[^1], typeof(List<object>).GetMethod("ToArray"));
                return Expression.Block(inits);
            }
            else
                return Expression.NewArrayInit(typeof(object), items);
        }

        Expression ParseAssign(Op op)
        {
            List<Exp> vars = op.Left.SplitComma();
            List<Exp> values = op.Right.SplitComma();

            if (op.Value != null) // augmented assignment
            {
                if (vars.Count == 1) // single variable
                {
                    if (values.Count == 1) // single value
                    {
                        Expression var = Parse(vars[0]);
                        Expression value = Parse(values[0]);
                        return Expression.Assign(var, Parser.ParseOperation(op, var, value));
                    }
                    else if (values.Count > 1) // tuple 
                    {
                        // pass
                    }
                }
                else
                    throw new Exception("illegal expression for augmented assignment");
            }
            else if (vars.Count == values.Count) // normal assignment
            {
                var assignments = new List<Expression>();
                for (int j = 0; j < vars.Count; j++)
                    assignments.Add(Assign(vars[j], Parse(values[j])));
                return Expression.Block(assignments);
            }
            else if (vars.Count > 1 && values.Count == 1) // unpack
            {
                var assignments = new List<Expression>();
                var iterable = Parse(values[0]);
                var (iter, iter_assign) = VariableAssign(GenerateId(), iterable);

                assignments.Add(iter_assign);

                for (int j = 0; j < vars.Count; j++)
                    assignments.Add(Assign(vars[j], Parser.ParseGetItem(iter, ParseInteger(j))));

                return Expression.Block(assignments);
            }
            else if (vars.Count == 1 && values.Count > 1) // tuple
            {
                return Assign(vars[0], ParseTuple(values));
            }
            throw new Exception("SyntaxError: invalid syntax");
        }

        Expression Assign(Exp expr, Expression value)
        {
            if (expr.Count == 1)
            {
                Token tok = expr[0];
                if (tok.Type == TokenType.Identifier)
                    return VariableAssign(tok.Value, value).Assignment;
            }
            else
            {
                Token tok = expr.Pop();
                if (tok.Type == TokenType.Member)
                    return Parser.ParseSetAttr(Parse(expr), tok.Value, value);

                else if (tok.Type == TokenType.Brackets)
                    return Parser.ParseSetItem(Parse(expr), ParseOperation(tok.Subset), value);
            }
            throw new Exception("the left hand of the expression is not assignable");
        }

        public static MethodInfo Remove = typeof(Dictionary<object, object>).GetMethod("Remove", [typeof(object), typeof(object).MakeByRefType()]);

        Expression ParseFunc(Exp expr)
        {
            string name = expr[0].Value;
            string ClassName = Locals.Count == 0 ? null : Locals.Peek().ClassName;
            var (parameters, istyping) = ParseParameters(expr[1].Subset);
            if (istyping | expr.Count > 2) Parser = CParser;

            var locals = new LocalBuilder();

            // import outside variables
            if (Locals.Count > 0)
                foreach (var l in Locals.Peek().Variables)
                    if (!l.Name.StartsWith("_"))
                        locals.Add(l.Name, l);

            var IL = new List<Expression>();
            ret.Push(Expression.Label(typeof(object)));

            // load parameters
            var arg = Expression.Parameter(typeof(object[]), "__args__");
            locals.Add(arg.Name, arg);

            var arg_length = Expression.Variable(typeof(int), "__argslen__");
            var kwargs = Expression.Variable(typeof(Dictionary<object, object>), "__kwargs__");
            var tmp = Expression.Variable(typeof(object), "__tmp__");

            if (!typing)
            {
                locals.AddVar(arg_length);
                locals.AddVar(kwargs);
                locals.AddVar(tmp);
                IL.Add(Expression.Assign(kwargs, 
                    Expression.Call(typeof(Py).GetMethod("Getkwargs"), arg, arg_length)));
            }
        
            ParameterInfo pi;

            for (var i = 0; i < parameters.Length; i++)
            {
                pi = parameters[i];
                var p = Expression.Parameter(pi.Type ?? typeof(object), pi.Name);
                locals.AddVar(p);
                Expression ldarg;

                if (pi.IsArgs)
                {
                    ldarg = Expression.Call(typeof(Py).GetMethod("GetSubArgs"), arg, Expression.Constant(i), arg_length);
                }
                else if (pi.IsKwargs)
                {
                    ldarg = Expression.Assign(p, kwargs);
                }
                else
                {
                    ldarg = Expression.ArrayIndex(arg, Expression.Constant(i));

                    if (typing)
                    {
                        if (p.Type != ldarg.Type)
                            ldarg = Expression.Convert(ldarg, p.Type);
                        if (pi.DefaultValue != null)
                        {
                            ldarg = Expression.Condition(
                                Expression.GreaterThan(Expression.Property(arg, "Length"), Expression.Constant(i)),
                                ldarg, pi.DefaultValue.Type != p.Type ? Expression.Convert(pi.DefaultValue, p.Type) : pi.DefaultValue);
                        }
                    }
                    else
                    {
                        var kwcheck = Expression.Condition(
                        Expression.AndAlso(Expression.NotEqual(kwargs, Expression.Constant(null, kwargs.Type)),
                                Expression.Call(kwargs, Remove, Expression.Constant(p.Name, typeof(object)), tmp)),
                        tmp, pi.DefaultValue ?? None);

                        ldarg = Expression.Condition(
                                Expression.GreaterThan(arg_length, Expression.Constant(i)),
                                ldarg, kwcheck);
                    }
                }

                IL.Add(Expression.Assign(p, ldarg));
            }

            Locals.Push(locals);

            if (typing)
            {
                foreach (var t in Types)
                    locals.Add(t.Key, new TypeExpression(t.Value));
                locals.Add("System", new TypeExpression("System"));
                locals.Add("Py", new TypeExpression("Py"));

                locals.Add("range", Expression.Constant(Enumerable.Range));
            }
            
            IL.Add(ParseBlock(expr.Body));

            // add the return label target
            IL.Add(Expression.Label(ret.Pop(), None));

            var body = Expression.Block(Locals.Pop().Variables, IL);
            var handler = Expression.Lambda<Func<object[], object>>(body, $"#py:{this.Name}{(ClassName is null?"":"."+ClassName)}.{name}", new[] { arg });

            if (typing) Parser = PyParser;

            return Expression.New(
                    typeof(Function).GetConstructors()[0],
                    Expression.Constant(name),
                    Expression.Constant(parameters),
                    handler,
                    Expression.Constant(Locals.Count == 0 ? true : Locals.Peek().ClassName is null));
        }

        Expression Lambda(string[] parameters, Exp expr)
        {
            var subset = new Exp();
            subset.AddRange(parameters.Select(p => new Token() { Value = p, EndsComma = true }));
            if (subset.Count > 0) subset.Last.EndsComma = false;
            Exp def = [
                new() { Value = GenerateId() }, // name
                new() { Subset = subset } // parameters
            ];
            expr.Keyword = "return";
            def.Body = [expr];
            return ParseFunc(def);
        }

        Expression ParseClass(Exp expr)
        {
            string name = expr[0].Value;
            var IL = new List<Expression>();
            var p = Expression.Parameter(typeof(Class), "p");
            var dct = Expression.Field(p, typeof(Object), "Dict");

            IL.Add(Expression.Assign(Expression.Field(p, "Name"), Expression.Constant(name)));

            // inheritance
            if (expr.Count == 2)
            {
                Expression[] bases = expr[1].Subset.SplitComma().Select(Parse).ToArray();
                var m = typeof(List<Class>).GetMethod("Add");

                foreach(var b in bases)
                    IL.Add(Expression.Call(Expression.Field(p, "Bases"), m, Expression.Convert(b, typeof(Class))));
            }

            Locals.Push(new LocalBuilder { ClassName = name });

            // class definition
            foreach (Exp e in expr.Body)
                IL.Add(ParseStatement(e));

            foreach (var field in Locals.Peek().Variables)
                IL.Add(Expression.Assign(Expression.Property(dct, "Item", Expression.Constant(field.Name)), field));

            var body = Expression.Block(Locals.Pop().Variables, IL);
            var initf = Expression.Lambda<Action<Class>>(body, name, [ p ]);

            return Expression.New(typeof(Class).GetConstructors()[0], initf);
        }

        Expression ParseOperation(Exp expr) => Parse(expr.AsTree());

        Expression Parse(Exp expr)
        {
            Token tok;

            if (expr.Count == 0)
                return None; // todo: raise error

            if (expr.Count == 1)
            {
                tok = expr[0];

                switch (tok.Type)
                {
                    case TokenType.Identifier:
                        // constants
                        switch (tok.Value)
                        {
                            case "None":
                                return None;

                            case "nil":
                                return nil;

                            case "True":
                                return Parser.ParseBool(true);

                            case "False":
                                return Parser.ParseBool(false);
                        }
                    
                        // user identifiers
                        if (Locals.Count > 0 && Locals.Peek().TryGetValue(tok.Value, out Expression value))
                            return value;
                            // eval method (revise)
                        else if (typing && Types.TryGetValue(tok.Value, out var t))
                            return new TypeExpression(t);
                        else
                            return GlobalAccess(tok.Value);

                    case TokenType.Number:
                        if (int.TryParse(tok.Value, out int i))
                            return ParseInteger(i);

                        else if (double.TryParse(tok.Value, out double d))
                            return Parser.ParseFloat(d);
                        break;

                    case TokenType.String:
                        return Parser.ParseString(tok.Value);

                    case TokenType.Operator: // binary
                        Op op = (Op)tok;

                        if (op.Priority == Precedence.Assignment)
                            return ParseAssign(op); // (revise)
                            //return Assign(op.Left, Parse(op.Right));

                        // special case for lambda
                        if (op.Value.StartsWith("lambda"))
                        {
                            var parameters = tok.Value.Substring(6).Split(',');
                            return Lambda(parameters, op.Right);
                        }

                        switch (op.Value)
                        {
                            case "if":
                                Expression ifTrue = Parse(op.Left);
                                Op right = (Op)op.Right[0];
                                Expression test = AsBool(Parse(right.Left));
                                Expression ifFalse = Parse(right.Right);
                                return Expression.Condition(test, ifTrue, ifFalse);

                            case "for":
                                Exp expression = op.Left;
                                Exp condition = null;
                                Op _in = (Op)op.Right[0];
                                if (_in.Value == "if")
                                {
                                    condition = _in.Right;
                                    _in = (Op)_in.Left[0];
                                }

                                if (condition != null)
                                {
                                    expression.Add(new Op () { Value = "if", Type = TokenType.Operator, RightToLeft = true, Priority = Precedence.Key });
                                    expression.AddRange(condition);
                                    expression.Add(new Op () { Value = "else", Type = TokenType.Operator, RightToLeft = true, Priority = Precedence.Key });
                                    expression.Add(new Token { Value = "nil", Type = TokenType.Identifier});
                                    expression = expression.AsTree();
                                }
                                
                                string var = _in.Left[0].Value;
                                Expression iterable = Parse(_in.Right);

                                return Expression.New(typeof(ForGenerator).GetConstructor([typeof(IEnumerable), typeof(Function)]),
                                    Expression.Convert(iterable, typeof(IEnumerable)),
                                    Lambda([var], expression));

                            case "and":
                                var l = Parse(op.Left);
                                var r = Parse(op.Right);

                                if (Parser == PyParser)
                                {
                                    var assgn = VariableAssign(GenerateId(), l);
                                    return Expression.Block(
                                        assgn.Assignment,
                                        Expression.Condition(AsBool(assgn.Variable), r, assgn.Variable)
                                    );
                                }
                                return Parser.ParseAnd(l, r);

                            case "or":
                                l = Parse(op.Left);
                                r = Parse(op.Right);

                                if (Parser == PyParser)
                                {
                                    var assgn = VariableAssign(GenerateId(), l);
                                    return Expression.Block(
                                        assgn.Assignment,
                                        Expression.Condition(AsBool(assgn.Variable), assgn.Variable, r)
                                    );
                                }
                                return Parser.ParseOr(l, r);
                                
                            case "notin":
                                op.Value = "__contains__";
                                return Parser.ParseNot(Parse(expr));

                            case "isnot":
                                op.Value = "is";
                                return Parser.ParseNot(Parse(expr));

                            case ":":
                                return new ColonExpression { Left = Parse(op.Left), Right = Parse(op.Right) };

                            case "is":
                                return Parser.ParseIs(Parse(op.Left), Parse(op.Right));
                            
                            case "as":
                                return Parser.ParseAs(Parse(op.Left), Parse(op.Right));

                            case "@":
                                return Parser.ParseAt(Parse(op.Left), Parse(op.Right));
                        }

                        return Parser.ParseOperation(op, Parse(op.Left), Parse(op.Right));

                    case TokenType.Parenthesis:
                        List<Exp> paren = tok.Subset.SplitComma();

                        return ParseTuple(paren);

                    case TokenType.Brackets:
                        List<Exp> brackets = tok.Subset.SplitComma();

                        var newlist = Expression.New(typeof(List<object>));
                        if (brackets.Count == 0)
                            return newlist;
                        else
                        {
                            var arr = brackets.Select(ParseOperation).ToArray();

                            if (arr.Length == 1 && arr[0].Type == typeof(ForGenerator))
                            {
                                var castMethod = typeof(Enumerable).GetMethod("Cast").MakeGenericMethod(typeof(object));
                                var callExpression = Expression.Call(null, castMethod, arr[0]);
                                var listConstructor = typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)]);
                                return Expression.New(listConstructor, callExpression);
                            }
                            if (TryUnpack(arr, out var inits))
                                return Expression.Block(inits);
                            else
                                return Expression.ListInit(newlist, arr);
                        }

                    case TokenType.Braces:
                        List<Exp> braces = tok.Subset.SplitComma();

                        var newDict = Expression.New(typeof(Dictionary<object, object>));
                        var items = braces.Select(ParseOperation).ToArray();

                        if (items.Length == 0)
                            return newDict;

                        if (items[0] is ColonExpression || (items[0] is UnpackExpression upck && upck.IsDictionary))
                        {
                            if (TryUnpack(items, out var inits))
                            {
                                inits[^1] = Expression.Call(typeof(Py).GetMethod("ListToDict"), inits[^1]);
                                return Expression.Block(inits);
                            }
                            else
                            {
                                var rows = new List<ElementInit>();
                                var adder = typeof(Dictionary<object, object>).GetMethod("Add");

                                foreach (ColonExpression c in items)
                                    rows.Add(Expression.ElementInit(adder, c.Left, c.Right));

                                return Expression.ListInit(newDict, rows);
                            }
                        }

                        // set
                        if (TryUnpack(items, out var inits1))
                        {
                            inits1[^1] = Expression.Call(toHashSetMethod, inits1[^1]);
                            return Expression.Block(inits1);
                        }
                        else
                            return Expression.ListInit(Expression.New(typeof(HashSet<object>)), items);
                }
            }

            tok = expr[0];

            // unary operator
            if (tok.Type == TokenType.Operator)
            {
                expr.RemoveAt(0); // remove operator
                Expression value = Parse(expr);
                Op op = (Op)tok;

                switch (tok.Value)
                {
                    case "__add__":
                        op.Value = "__pos__";
                        op.NodeType = ExpressionType.UnaryPlus;
                        break;

                    case "__sub__":
                        op.Value = "__neg__";
                        op.NodeType = ExpressionType.Negate;
                        break;

                    case "not":
                        return Parser.ParseNot(value);

                    case "@":
                        Decorators.Push(value);
                        return None;

                    case ":":
                        return new ColonExpression { Left = None, Right = value };

                    case "__mul__":
                        return new UnpackExpression { Iterable = value };

                    case "__pow__":
                        return new UnpackExpression { Iterable = value, IsDictionary = true };
                }
                return Parser.ParseUnary(op, value);
            }

            // (), [], .xyz
            tok = expr.Pop();

            switch (tok.Type)
            {
                case TokenType.Parenthesis: // call
                    // special cases
                    if (expr.Count == 1)
                    {
                        switch (expr[0].Value)
                        {
                            case "eval":
                                Parser = CParser;
                                var c = ParseOperation(tok.Subset);
                                Parser = PyParser;
                                return c.Type != typeof(object) ? Expression.Convert(c, typeof(object)) : c;

                            case "super":
                                // super() -> super(None, __args__[0])
                                if (tok.Subset.Count == 0)
                                    return ParseCall(GlobalAccess("super"),
                                        [None, Parser.ParseGetItem(Locals.Peek()["__args__"], Expression.Constant(0, dynamic))]);
                                break;
                        }
                    }
                    return ParseCall(Parse(expr), ParseArguments(tok.Subset));

                case TokenType.Brackets: // index
                    var obj = Parse(expr);
                    Expression index = ParseTuple(tok.Subset.SplitComma());
                    if (index is ColonExpression colon)
                    {
                        Expression[] args;
                        if (colon.Right is ColonExpression rcolon)
                            args = [obj, colon.Left, rcolon.Left, rcolon.Right];
                        else
                            args = [obj, colon.Left, colon.Right];
                        return ParseCall(GlobalAccess("slice"), args);
                    }
                    return Parser.ParseGetItem(obj, index);

                case TokenType.Member: // member
                    return Parser.ParseGetAttribute(Parse(expr), tok.Value);
            }

            throw new Exception("SyntaxError: invalid syntax");
        }

        Expression ParseInteger(int i) => typing ? Expression.Constant(i) : Expression.Constant(i, dynamic);

        Expression Exec(string fx, params Expression[] args) =>
            Expression.Call(MagicMethod(fx), args);

        Expression ParseCall(Expression obj, Expression[] arg)
        {
            if (!typing)
            {
                if (TryUnpack(arg, out var inits))
                {
                    var arr = Expression.Call(inits[^1], typeof(List<object>).GetMethod("ToArray"));
                    inits[^1] = Exec("__call__", obj, arr);
                    return Expression.Block(inits);
                }
                else
                    return Expression.Call(MagicMethod("__call__"), obj, Expression.NewArrayInit(typeof(object), arg));
            }
            else
            {
                if (obj is TypeExpression t)
                    return Expression.New(t.MyType.GetConstructor(arg.Select(x => x.Type).ToArray()), arg);

                if (obj is CallMethodExpression c)
                {
                    if (c.Object is TypeExpression typee)
                        return Expression.Call(typee.MyType.GetMethod(c.Name, arg.Select(x => x.Type).ToArray()), arg);
                    return Expression.Call(c.Object, GetMethod(c.Object.Type, c.Name, arg.Select(x=>x.Type).ToArray()), arg);
                }
                // dynamic call
                if (obj.Type == dynamic)
                {
                    return Expression.Call(MagicMethod("__call__"), obj, Expression.NewArrayInit(typeof(object), arg));
                }
                return Expression.Invoke(obj, arg);
            }
        }

        Expression ParseBlock(List<Exp> body)
        {
            var IL = new List<Expression>();
            foreach (Exp expr in body)
            {
                //IL.Add(Expression.Assign(_L, Expression.Constant(expr.Line)));
                IL.Add(ParseStatement(expr));
            }
            return Expression.Block(IL);
        }

        Expression ParseStatement(Exp expr)
        {
            switch (expr.Keyword)
            {
                case null: // expression
                    return ParseOperation(expr);

                case "if":
                    return Expression.IfThenElse(
                        AsBool(ParseOperation(expr)),
                        ParseBlock(expr.Body),
                        Else(expr.Tiers));

                case "while":
                    return While(AsBool(ParseOperation(expr)), ParseBlock(expr.Body));

                case "for":
                {
                    Op In = (Op)expr.AsTree()[0];

                    var iterable = ParseOperation(In.Right);
                    var iter = GetIterator(iterable);

                    var i = LocalAccess(In.Left[0].Value, iter.current.Type);
                    var body = Expression.Block(Expression.Assign(i, iter.current), ParseBlock(expr.Body));
                    var loop = While(iter.next, body);
                    return Expression.Block(iter.assign, loop);
                }
                case "break":
                    return Expression.Goto(br.Peek());

                case "continue":
                    return Expression.Goto(con.Peek());

                case "pass":
                    return None;

                case "return":
                    var value = ParseOperation(expr);

                    if (typing && value.Type != dynamic)
                        value = Expression.Convert(value, dynamic);

                    return Expression.Goto(ret.Peek(), value);

                case "def":
                    var def = ParseFunc(expr);
                    if (Decorators.Count > 0)
                        def = ParseCall(Decorators.Pop(), [def]);
                    return VariableAssign(expr[0].Value, def).Assignment;

                case "class":
                    return VariableAssign(expr[0].Value, ParseClass(expr)).Assignment;
                    
                case "global":
                    List<Exp> variables = expr.SplitComma();
                    foreach (Exp var in variables)
                        Locals.Peek().Add(var[0].Value, GlobalAccess(var[0].Value));
                    return None;

                case "nonlocal":
                    // TODO
                    return null;

                case "try":
                    // TODO
                    return null;

                case "raise":
                    return Expression.Throw(ParseOperation(expr));

                case "yield":
                    // TODO
                    return null;

                case "assert":
                    // TODO
                    return null;

                case "del":
                    Token tok = expr.Pop();
                    if (tok.Type == TokenType.Member)
                        return Expression.Call(MagicMethod("__delattr__"), Parse(expr), Expression.Constant(tok.Value));
                    else if (tok.Type == TokenType.Brackets)
                        return Expression.Call(MagicMethod("__delitem__"), Parse(expr), ParseOperation(tok.Subset));
                    return null;

                case "import":
                    return CallBuiltinFunc("__import__", Expression.Constant(JoinDot(expr)), None);
                    
                case "from":
                    var import = (Op)expr.AsTree()[0];
                    return CallBuiltinFunc("__import__", Expression.Constant(JoinDot(import.Left)), 
                        Expression.NewArrayInit(typeof(object), import.Right.SplitComma().Select(x => Expression.Constant(JoinDot(x))).ToArray()));
            }
            return None;
        }

        (Expression assign, Expression next, Expression current) GetIterator(Expression iterable)
        {
            // Get the enumerable type
            Type enumerableType;
            Type elemType = GetAnyElementType(iterable.Type);
            if (elemType is null)
            {
                iterable = Expression.Convert(iterable, typeof(IEnumerable));
                enumerableType = iterable.Type;
            }
            else
                enumerableType = typeof(IEnumerable<>).MakeGenericType(elemType);
            
            var call = Expression.Call(iterable, enumerableType.GetMethod("GetEnumerator"));
            var iter = LocalAccess(GenerateId(), call.Type);
            var assign = Expression.Assign(iter, call);
            if (iter.Type == typeof(object))
                iter = Expression.Convert(iter, typeof(IEnumerator));
            var next = Expression.Call(iter, typeof(IEnumerator).GetMethod("MoveNext"));
            var current = Expression.Property(iter, "Current");
            
            return (assign, next, current);
        }

        Expression ForEach(Expression var, Expression iterable, Expression loopContent)
        {
            var iter = GetIterator(iterable);
            var body = Expression.Block(Expression.Assign(var, iter.current), loopContent);
            var loop = While(iter.next, body);
            return Expression.Block(iter.assign, loop);
        }

        Expression While(Expression test, Expression body)
        {
            br.Push(Expression.Label());
            con.Push(Expression.Label());

            return Expression.Loop(
                Expression.IfThenElse(test,
                    body, 
                    Expression.Break(br.Peek())
                ),
                br.Pop(),
                con.Pop()
            );
        }

        Expression Else(Stack<Exp> tiers)
        {
            if (tiers is null)
                return None;

            Expression r = tiers.Peek().Keyword == "else" ?
                ParseBlock(tiers.Pop().Body) :
                None;

            while (tiers.Count > 0)
            {
                Exp elif = tiers.Pop();
                r = Expression.IfThenElse(AsBool(ParseOperation(elif)), ParseBlock(elif.Body), r);
            }

            return r;
        }

        (ParameterInfo[], bool typing) ParseParameters(Exp parenthesisContents)
        {
            var parameters = new List<ParameterInfo>();
            var arr = parenthesisContents.SplitComma().Select(p => p.AsTree());
            bool typing = false;
            foreach (Exp p in arr)
            {
                var pi = new ParameterInfo();
                Exp exp = p;
    recheck:    if (exp[0] is Op op)
                {
                    switch (op.Value)
                    {
                        case null: // null is '='
                            pi.DefaultValue = ParseOperation(op.Right);
                            exp = op.Left;
                            goto recheck;
                        case ":":
                            pi.Type = GetType(string.Join(".", op.Right.Select(x => x.Value))); //TODO : handle nested types
                            exp = op.Left;
                            typing = true;
                            goto recheck;
                        case "__mul__": // *args
                            pi.IsArgs = true;
                            pi.Name = exp[1].Value;
                            break;
                        case "__pow__": // **kwargs
                            pi.IsKwargs = true;
                            pi.Name = exp[1].Value;
                            break;
                        default:
                            throw new Exception("SyntaxError: invalid syntax");
                    }
                }
                else
                    pi.Name = exp[0].Value;

                parameters.Add(pi);
            }
            return (parameters.ToArray(), typing);
        }

        Expression[] ParseArguments(Exp parenthesisContents)
        {
            List<Expression> expressions = [];
            var args =  parenthesisContents.SplitComma();

            foreach (Exp i in args)
            {
                var arg = i.AsTree();
                Expression exp = null;

                if (arg.Count == 1 && arg[0] is Op op && op.Priority == Precedence.Assignment) // kwarg
                {
                    exp = Expression.New(typeof(KeyValue).GetConstructor([dynamic, dynamic]),
                        Expression.Constant(op.Left[0].Value), Parse(op.Right));
                }
                else
                    exp = Parse(arg);

                expressions.Add(exp);
            }

            return expressions.ToArray();
        }

        public NewExpression NewKeyValue(Expression key, Expression value) =>
            Expression.New(typeof(KeyValue).GetConstructors().First(), key, value);

        public static Dictionary<object, object> ListToDict(List<object> lst)
        {
            var dct = new Dictionary<object, object>();
            foreach (var item in lst)
            {
                var kv = (KeyValue)item;
                dct.Add(kv.Key, kv.Value);
            }
            return dct;
        }

        public bool TryUnpack(Expression[] items, out List<Expression> inits)
        {
            if (!items.Any(x => x is UnpackExpression))
            {
                inits = null;
                return false;
            }
            
            inits = [];
            var newlist = Expression.New(typeof(List<object>));
            var (lst, assign) = VariableAssign(GenerateId(), newlist);
            inits.Add(assign);
            lst = Expression.Convert(lst, newlist.Type);
            var adder = newlist.Type.GetMethod("Add");
            var i = LocalAccess(GenerateId(), dynamic);

            foreach (Expression item in items)
            {
                if (item is UnpackExpression upck)
                {
                    var x = i;
                    if (upck.IsDictionary)
                    {
                        var kv = Expression.Convert(i, typeof(KeyValuePair<object, object>));
                        x = NewKeyValue(Expression.Property(kv, "Key"), Expression.Property(kv, "Value"));
                    }
                    inits.Add(ForEach(i, upck.Iterable, Expression.Call(lst, adder, x)));
                }
                else if (item is ColonExpression c) // for dictionary only
                    inits.Add(Expression.Call(lst, adder, NewKeyValue(c.Left, c.Right)));
                else
                    inits.Add(Expression.Call(lst, adder, item));
            }

            inits.Add(lst);

            return true;
        }

        public static bool HasMethod(Type type, string methodName)
        {
            return type.GetMethods().Any(method => method.Name == methodName);
        }

        public static object[] GetSubArgs(object[] array, int start, ref int end)
        {
            int length = end - start;
            end = start;
            if (length <= 0) return [];
            object[] result = new object[length];
            Array.Copy(array, start, result, 0, length);
            return result;
        }

        public static Dictionary<object, object> Getkwargs(object[] args, ref int pos)
        {
            Dictionary<object, object> kwargs = null;
            pos = args.Length - 1;
            while (pos >= 0)
            {
                if (args[pos] is KeyValue kw)
                {
                    if (kwargs is null) kwargs = new() {};
                    kwargs.Add(kw.Key, kw.Value);
                }
                else
                    break;
                pos--;
            }
            pos++;
            return kwargs;
        }
    }
}
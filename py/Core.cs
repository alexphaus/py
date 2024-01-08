using System.Collections;
using System.ComponentModel.Design;
using System.Reflection;

namespace Py
{
    public partial class Py
    {
        public static dynamic __add__(dynamic left, dynamic right) => left + right;
        public static dynamic __sub__(dynamic left, dynamic right) => left - right;
        public static dynamic __mul__(dynamic left, dynamic right) => left * right;
        public static dynamic __div__(dynamic left, dynamic right) => left / right;
        public static dynamic __mod__(dynamic left, dynamic right) => left % right;
        public static dynamic __pow__(dynamic left, dynamic right) => Math.Pow(left, right);
        public static dynamic __floordiv__(dynamic left, dynamic right) => Math.Floor(left / right);
        public static dynamic __lshift__(dynamic left, dynamic right) => left << right;
        public static dynamic __rshift__(dynamic left, dynamic right) => left >> right;
        public static dynamic __and__(dynamic left, dynamic right) => left & right;
        public static dynamic __xor__(dynamic left, dynamic right) => left ^ right;
        public static dynamic __or__(dynamic left, dynamic right) => left | right;
        public static dynamic __invert__(dynamic obj) => ~obj;
        public static dynamic __neg__(dynamic obj) => -obj;
        public static dynamic __pos__(dynamic obj) => +obj;
        public static dynamic __not__(dynamic obj) => !obj;
        public static dynamic __lt__(dynamic left, dynamic right) => left < right;
        public static dynamic __gt__(dynamic left, dynamic right) => left > right;
        public static dynamic __le__(dynamic left, dynamic right) => left <= right;
        public static dynamic __ge__(dynamic left, dynamic right) => left >= right;
        public static dynamic __eq__(dynamic left, dynamic right) => left == right;
        public static dynamic __ne__(dynamic left, dynamic right) => left != right;
        public static dynamic __getitem__(dynamic obj, dynamic index) => obj[index];
        public static dynamic __setitem__(dynamic obj, dynamic index, dynamic value) => obj[index] = value;

        public static object __delitem__(object obj, object index)
        {
            switch (obj)
            {
                case Object o:
                    o.Callvirt("__delitem__", index);
                    return null;

                case System.Collections.IList l:
                    l.RemoveAt((int)index);
                    return null;

                case System.Collections.IDictionary d:
                    d.Remove(index);
                    return null;
            }
            throw new Exception($"TypeError: '{obj.GetType().Name}' object does not support item deletion");
        }

        public static dynamic __contains__(object key, object iterable)
        {
            switch (iterable)
            {
                case Object o:
                    return o.Callvirt("__contains__", key);

                case string s:
                    return s.Contains(key.ToString());

                case IList l:
                    return l.Contains(key);

                case IDictionary d:
                    return d.Contains(key);

                case HashSet<object> s:
                    return s.Contains(key);
            }

            throw new Exception($"TypeError: argument of type '{iterable.GetType().Name}' is not iterable");
        }

        public static object __is__(object obj, object type)
        {
            switch (type)
            {
                case Class c:
                    return obj is Object o && c.IsAssignableFrom(o.Type);

                case Type t:
                    return obj?.GetType() == t;

                case CType s:
                    return obj?.GetType() == s.Type;
                
                default:
                    return ReferenceEquals(obj, type);
            }
        }

        public static object __as__(object obj, object conversionType)
        {
            Type type = null;
            if (conversionType is CType c)
                type = c.Type;
            if (conversionType is Type t)
                type = t;

            if (obj is List<object> list && type.IsArray)
            {
                Type elemType = type.GetElementType();
                Array array = Array.CreateInstance(elemType, list.Count);
                for (int i = 0; i < list.Count; i++)
                    array.SetValue(Convert.ChangeType(list[i], elemType), i);
                return array;
            }

            return Convert.ChangeType(obj, type);
        }

        public static object __call__(object obj, object[] arg)
        {
            switch (obj)
            {
                case Class type:
                    return type.CreateInstance(arg);

                case Function f:
                    return f.Handler.Invoke(arg);

                case MethodWrapper m:
                    return m.Method.Invoke(Unshift(m.Instance, arg));
                
                case Nil n:
                    return n.Invoke(arg);

                case Object o:
                    return o.Callvirt("__call__", arg);

                case CType c:
                    return CallType(c.Type, arg);

                case Type t:
                    return CallType(t, arg);

                case Delegate d:
                    return d.DynamicInvoke(arg);
            }
            throw new Exception($"TypeError: '{obj.GetType().Name}' object is not callable");
        }

        public static object CallType(Type type, object[] arg)
        {
            // extension
            if (Py.Extensions.TryGetValue((type, "__new__"), out var func))
                return func.Handler.Invoke(Unshift(type, arg));
                
            var ctor = type.GetConstructor(GetArgTypes(arg));
            if (ctor is null)
                throw new Exception($"TypeError: Type '{type.FullName}' has no constructor with given arguments.");
            return ctor.Invoke(arg);
        }

        public static object __getattribute__(object obj, string name)
        {
            if (obj is Object o)
            {
                return o.Callvirt("__getattribute__", name);
                // (revise for performance only 2x faster)
                // if (o.Dict.TryGetValue(name, out object value))
                //     return value;
                // // class dict
                // if (o.Type.Dict.TryGetValue(name, out value))
                // {
                //     if (value is Function def && !def.IsStatic)
                //         return new MethodWrapper { Instance = obj, Method = def };
                //     return value;
                // }
                // // try call __getattr__
                // if (o.Type.Dict.TryGetValue("__getattr__", out value))
                //     return ((Function)value).Handler.Invoke([obj, name]);
                // throw new Exception($"object '{o.Type.Name}' has no attribute '{name}'");
            }

            Type type = obj is CType c ? c.Type : obj.GetType();

            // extension
            if (Py.Extensions.TryGetValue((type, name), out var func))
                return new MethodWrapper { Instance = obj, Method = func };

            // property
            var pi = type.GetProperty(name);
            if (pi != null)
                return pi.GetValue(obj);

            // field
            var fi = type.GetField(name);
            if (fi != null)
                return fi.GetValue(obj);

            // nil or method
            return new Nil { Instance = obj, Name = name, Type = type };
        }

        public static object __setattr__(object obj, string name, object value)
        {
            if (obj is Object o)
                return o.Callvirt("__setattr__", name, value);

            Type type = obj is CType c ? c.Type : obj.GetType();

            // property
            var pi = type.GetProperty(name);
            if (pi != null)
            {
                pi.SetValue(obj, value);
                return null;
            }

            // field
            var fi = type.GetField(name);
            if (fi != null)
            {
                fi.SetValue(obj, value);
                return null;
            }

            // event
            var ei = type.GetEvent(name);
            if (ei != null)
            {
                Action<object, EventArgs> fx = (s, e) => ((Function)value).Invoke(new[] { s, e });
                Delegate handler = Delegate.CreateDelegate(ei.EventHandlerType, fx.Target, fx.Method);
                ei.AddEventHandler(obj, handler);
                return null;
            }

            throw new Exception($"AttributeError: '{type.Name}' object has no attribute '{name}'");
        }

        public static object __delattr__(object obj, object key)
        {
            if (obj is Object o)
                o.Callvirt("__delattr__", key);
            return null;
        }

        public static bool __bool__(object obj)
        {
            switch (obj)
            {
                case Object o: //TODO: when o.HasAttr("__bool__")
                    return (bool)o.Callvirt("__bool__");
                case bool b: return b;
                case int i: return i != 0;
                case double d: return d != 0;
                case string s: return s.Length != 0;
                case IList l: return l.Count != 0;
                case IDictionary d: return d.Count != 0;
                case HashSet<object> s: return s.Count != 0;
                case null: return false;
                default: return true;
            }
        }

        public static object[] Unshift(object obj, object[] arg)
        {
            object[] arr = new object[arg.Length + 1];
            arr[0] = obj;
            Array.Copy(arg, 0, arr, 1, arg.Length);
            return arr;
        }

        public static Type[] GetArgTypes(object[] args)
        {
            Type[] types = new Type[args.Length];
            object arg = null;

            for (int i = 0; i < args.Length; i++)
            {
                arg = args[i];
                if (arg is CType c)
                {
                    args[i] = c.Type;
                    types[i] = typeof(Type);
                }
                else
                    types[i] = arg?.GetType() ?? typeof(object);
            }
            return types;
        }

        public static MethodInfo GetMethod(Type type, string name, Type[] types)
        {
            foreach (MethodInfo m in type.GetMethods())
            {
                if (m.Name != name) continue;

                var parameters = m.GetParameters();
                if (parameters.Length != types.Length) continue;

                bool match = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type paramType = parameters[i].ParameterType;

                    if (paramType.IsByRef)
                        paramType = paramType.GetElementType();
                    
                    if (!paramType.IsAssignableFrom(types[i]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return m;
            }
            return null;
        }

        public static Type GetAnyElementType(Type type)
        {
            if (type == typeof(object))
                return null;
            // Type is Array
            // short-circuit if you expect lots of arrays 
            if (type.IsArray)
                return type.GetElementType();

            // type is IEnumerable<T>;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof (IEnumerable<>))
                return type.GetGenericArguments()[0];

            // type implements/extends IEnumerable<T>;
            var enumType = type.GetInterfaces()
                                    .Where(t => t.IsGenericType && 
                                            t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                                    .Select(t => t.GenericTypeArguments[0]).FirstOrDefault();
            return enumType;// ?? throw new Exception($"couldn't get element type from '{type}'");
        }
    }

    public class Object: IEnumerable, IEnumerator
    {
        public Class Type;
        public Dictionary<string, object> Dict;

        public dynamic this[dynamic index]
        {
            get => Callvirt("__getitem__", index);
            set => Callvirt("__setitem__", index, value);
        }

        public virtual object Callvirt(string name, object[] args) =>
            Type.CallMethod(name, Py.Unshift(this, args));

        public virtual object Callvirt(string name) =>
            Type.CallMethod(name, new object[] { this });

        public virtual object Callvirt(string name, object arg0) =>
            Type.CallMethod(name, new object[] { this, arg0 });

        public virtual object Callvirt(string name, object arg0, object arg1) =>
            Type.CallMethod(name, new object[] { this, arg0, arg1 });
        
        public override string ToString() => (string)Callvirt("__str__");

        // iterable
        public bool MoveNext() => (bool)Callvirt("__next__");
        public IEnumerator GetEnumerator() => (IEnumerator)Callvirt("__iter__");
        public void Reset() => Callvirt("reset");
        public object Current => Dict["current"];
    }

    public class Class : Object
    {
        public string Name;
        public List<Class> Bases;

        public Class(Action<Class> initf)
        {
            Type = Py.Builtins.GetClass("type");
            Dict = new Dictionary<string, object>();
            Bases = new List<Class>();
            
            initf(this);

            Dict["__name__"] = Name;
            Dict["__bases__"] = Bases;
            Dict["__module__"] = Py.Threads.Peek();
            Dict["__class__"] = Type;

            if (Bases.Count == 0)
                if (Name != "object")
                    Bases.Add(Py.Builtins.GetClass("object"));

            foreach (Class b in Bases)
                Inherit(b);
        }

        public object CallMethod(string name, object[] arg)
        {
            object value;
            if (Dict.TryGetValue(name, out value))
                return ((Function)value).Handler.Invoke(arg);
            else
                throw new Exception($"object '{Name}' has no method {name}.");
        }

        public object CreateInstance(object[] arg)
        {
            var r = CallMethod("__new__", Py.Unshift(this, arg));
            // init
            if (r is Object inst && inst.Type == this && Dict.TryGetValue("__init__", out var init))
                ((Function)init).Handler.Invoke(Py.Unshift(r, arg));
            return r;
        }

        public bool IsSubclassOf(Class parent)
        {
            if (Bases.Contains(parent))
                return true;
            foreach (Class b in Bases)
                if (b.IsSubclassOf(parent))
                    return true;
            return false;
        }

        public bool IsAssignableFrom(Class type)
        {
            if (ReferenceEquals(this, type))
                return true;
            return type.IsSubclassOf(this);
        }

        public void Inherit(Class parent)
        {
            foreach (var id in parent.Dict)
                if (!Dict.ContainsKey(id.Key))
                    Dict.Add(id.Key, id.Value);
        }
    }

    public class Function : Object
    {
        public string Name;
        public ParameterInfo[] Parameters;
        public Func<object[], object> Handler;
        public bool IsStatic;

        public Function(string name, ParameterInfo[] parameters, Func<object[], object> handler, bool isStatic)
        {
            Type = Py.Builtins.GetClass("function");
            Name = name;
            Parameters = parameters;
            Handler = handler;
            IsStatic = isStatic;
        }

        public MethodWrapper Bind(object instance)
        {
            return new MethodWrapper
            {
                Instance = instance,
                Method = this
            };
        }

        public dynamic Invoke(object[] arg)
        {
            return Handler(arg);
        }
    }

    public class MethodWrapper
    {
        public object Instance;
        public Function Method;
    }

    public class Nil // non-value
    {
        public object Instance;
        public Type Type;
        public string Name;

        public object Invoke(object[] arg)
        {
            var argTypes = Py.GetArgTypes(arg);
            var m = Type.GetMethod(Name, argTypes);

            if (m != null)
                return m.Invoke(Instance, arg);

            //raise error
            var sb = new System.Text.StringBuilder();
            sb.Append($"AttributeError: '{Type.Name}' object has no method '{Name}' with the given arguments: ");
            sb.AppendLine("(" + string.Join(", ", argTypes.Select(t => t.FullName)) + ")");
            sb.AppendLine("Methods matching name: ");
            foreach (var meth in Type.GetMethods())
                if (meth.Name == Name)
                    sb.AppendLine(meth.ToString());
            throw new Exception(sb.ToString());
        }

        public override string ToString() => "nil";
    }

    public class CType
    {
        public Type Type;
        public Dictionary<int, Type> GenericTypes;
        public CType(Type type) => Type = type;

        public CType this[object[] tuple]
        {
            get
            {
                if (tuple.Length == 0)
                    return new CType(Type.MakeArrayType());

                Py.GetArgTypes(tuple);
                var types = tuple.Select(x => x is CType c ? c.Type : (Type)x).ToArray();

                return new CType(GenericTypes[types.Length].MakeGenericType(types));
            }
        }
        public CType this[CType c] { get => new CType(GenericTypes[1].MakeGenericType(c.Type)); }
        public CType this[Type t] { get => new CType(GenericTypes[1].MakeGenericType(t)); }
        public override string ToString() => Type.ToString();
    }

    public class ForGenerator: IEnumerable, IEnumerator
    {
        IEnumerable iterable;
        IEnumerator iter;
        object current;
        Function lambda;

        public ForGenerator(IEnumerable iterable, Function lambda)
        {
            this.iterable = iterable;
            iter = iterable.GetEnumerator();
            this.lambda = lambda;
        }

        public bool MoveNext()
        {
            bool next = iter.MoveNext();
            if (next)
            {
                current = lambda.Invoke([iter.Current]);
                if (current is Nil)
                    return MoveNext();
            }
            return next;
        }
        public IEnumerator GetEnumerator() => this;
        public void Reset() => iter.Reset();
        public object Current => current;
    }
}
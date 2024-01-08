using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Py
{
    //[Serializable]
    public partial class Py
    {
        public Dictionary<string, object> Globals = new Dictionary<string, object>();
        public string Name;
        // Constants
        public readonly static Expression None = Expression.Constant(null);
        public readonly static Expression nil = Expression.Constant(new Nil(),typeof(object));
        public Expression _G = null;
        /// <summary>
        /// same as type(object)
        /// </summary>
        public static Type dynamic = typeof(object);
        public Expression _L = Expression.Field(null, typeof(Py), "CurrentLine");
        public static string execpath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        public static string cwd;
        public static int CurrentLine = 0;

        public static Py Builtins;
        public static Stack<Py> Threads = new Stack<Py>();
        public static Dictionary<(Type, string), Function> Extensions = [];
        static MethodInfo toHashSetMethod = typeof(Enumerable).GetMethods().Where(x => x.Name == "ToHashSet").First().MakeGenericMethod(typeof(object));

        public static void Main(string[] args)
        {
            string testpath = execpath + "/test.py";
            string filepath = (args.Length == 0) ? testpath : args[0];
            cwd = System.IO.Path.GetDirectoryName(filepath);

            //builtins
            if (Builtins == null)
                load_builtins();

            // if in debug mode
            if (Debugger.IsAttached)
            {
                // run test script
                var interpreter = new Py(testpath);
                interpreter.Execute();
            }
            else
            try
            {
                var interpreter = new Py(filepath);
                // run script
                interpreter.Execute();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                var frames = e.StackTrace.Split('\n');
                string frame;
                bool flag = true;
                for (int i = 0; i < frames.Length; i++)
                {
                    frame = frames[i];
                    if (frame.StartsWith("   at #py:")) {
                        frame = frame.Replace("#py:","");
                        Console.Write(frame.Substring(0, frame.IndexOf("(")));
                        if (flag)
                        {
                            Console.WriteLine(", line " + CurrentLine);
                            flag = false;
                        }
                        else Console.WriteLine();
                    }
                }
            }
        }

        Action __main__ = null;
        public Py(string fileName)
        {
            string src = System.IO.File.ReadAllText(fileName);
            this.Name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            _G = Expression.Constant(Globals);
            // add global variable that reference this module to be accessed from python
            Globals.Add("__py__", typeof(Py));
            Globals.Add("CType", typeof(CType));
            
            List<Exp> tokens = Tokenize(src);
            var lmb = Expression.Lambda<Action>(ParseBlock(tokens), "#py:"+Name, new ParameterExpression[0]);
            __main__ = lmb.Compile();

            // import builtins
            if (Builtins != null)
                foreach (var id in Builtins.Globals)
                    if (!this.Globals.ContainsKey(id.Key))
                        this.Globals.Add(id.Key, id.Value);
        }

        public static void load_builtins()
        {
            Builtins = new Py(execpath + "/lib/builtins.py");
            Builtins.Execute();
        }

        public object this[string name]
        {
            get => Globals[name];
            set => Globals[name] = value;
        }

        public dynamic CallFunc(string name, params object[] args)
        {
            if (Globals.TryGetValue(name, out object o) && o is Function f)
                return f.Invoke(args);
            return null;
        }

        public Class GetClass(string name)
        {
            if (Globals.TryGetValue(name, out object o) && o is Class c)
                return c;
            return null;
        }

        public void Execute()
        {
            Threads.Push(this);
            __main__.Invoke();
            Threads.Pop();
        }

        public dynamic Execute(string chunk)
        {
            Threads.Push(this);
            List<Exp> tokens = Tokenize(chunk);
            var lmb = Expression.Lambda<Action>(ParseBlock(tokens), "#py:chunk", new ParameterExpression[0]);
            lmb.Compile().Invoke();
            Threads.Pop();
            return null;
        }
    }
}
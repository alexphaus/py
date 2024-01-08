
class object:
	__class__ = None
	__doc__ = None

	def __new__(cls: type):
		inst = object()
		inst.Type = cls
		inst.Dict = System.Collections.Generic.Dictionary[str, Object]()
		inst.Dict["__dict__"] = inst.Dict
		inst.Dict["__class__"] = cls
		return inst

	def __init__(self): #, *args, **kwargs
		pass

	def __getattribute__(self: object, name: str):
		r = None
		if self.Dict.TryGetValue(name, r): # attribute
			return r
		elif self.Type.Dict.TryGetValue(name, r):
			if r is function:
				f = r@function
				if not f.IsStatic:
					return f.Bind(self) # method-wrapper
			return r
		elif self.Type.Dict.TryGetValue("__getattr__", r):
			return (r@function).Handler.Invoke(__args__)

		raise AttributeError("object '" + self.Type.Name + "' has no attribute '" + name + "'")

	def __setattr__(self: object, name: str, value):
		self.Dict[name] = value

	def __delattr__(self: object, name: str):
		self.Dict.Remove(name)

	def __dir__(self):
		pass

	def __str__(self: object):
		return "object '" + self.Type.Name + "'"

	def __repr__(self: object):
		return None

class type:
	def __getattribute__(self: type, name: str):
		r = None
		if self.Dict.TryGetValue(name, r):
			return r
		raise Exception("type '" + self.Name + "' has no attribute '" + name + "'")

	def __new__(cls: type): # , *args, **kwargs
		pass

	def __call__(self):
		"""Call self as a function."""
		return None

	def __str__(cls: type):
		return "class '" + cls.Name + "'"
	
def set_object_type(o:object, t:type): o.Type = t
set_object_type(object, type)

class function:
	def __call__(self):
		return None

	def __str__(self: function):
		return "function '" + self.Name + "'"

	def __bool__(self):
		return True

class range:
    def __new__(cls, start, end=None, step=1):
        # If stop is not specified, start from 0
        if end is None:
            end = start
            start = 0
        if step == 1:
            return System.Linq.Enumerable.Range(start, end - start)
        return object.__new__(cls, start, end, step)
        
    def __init__(self, start, end, step):
        # Check if the step is zero
        if step == 0:
            raise ValueError("Step cannot be zero")
        self.start = start
        self.end = end
        self.step = step
        self.current = start - step

    def __getitem__(self, i):
        if i < 0:
            # Handle negative indices
            i += (self.end - self.start) // self.step

        # Calculate the value at index i
        value = self.start + i * self.step

        # Check if the value is within the range bounds
        if (self.step > 0 and value >= self.end) or (self.step < 0 and value <= self.end):
            raise IndexError("Index out of range")

        return value

    def __iter__(self):
        return self

    def __next__(self:object):
        step = self.step@int
        current = self.current@int + step
        end = self.end@int
        self.current = current@Object
        return (step > 0 and current < end) or (step < 0 and current > end)

def iterable_tostring(iterable, open, close):
	sb = System.Text.StringBuilder()
	sb.Append(open)
	for i in iterable:
		sb.Append(i.ToString())
		sb.Append(", ")
	if len(iterable) > 0:
		sb.Length = sb.Length - 2
	sb.Append(close)
	return sb.ToString()

def print(*args, sep=' ', end='\n', file=None):
    # Convert all arguments to strings
    string_args = [str(arg) for arg in args]

    # Join the string arguments with the separator
    output = sep.join(string_args)

    # Add the end character
    output += end

    # Print to the specified file or standard output
    if file:
        print(output, file=file)
    else:
        System.Console.Write(output)

def len(obj)->int:
	"""Return the number of items in a container."""
	if obj is str:
		return (obj@str).Length
	elif obj is list:
		return (obj@list).Count
	elif obj is tuple:
		return (obj@tuple).Length
	elif obj is dict:
		return (obj@dict).Count
	elif obj is set:
		return (obj@set).Count
	#else: return obj.__len__()

def isinstance(obj, cls):
	return obj.GetType() == typeof(cls)

def issubclass(cls: type, base: type):
	return cls.IsSubclassOf(base)

def __import__(name, fromlist):
	# try to import from current directory
	file = name
	G = globals()
	if not System.IO.File.Exists(file):
		file = Py.cwd + "/" + name + ".py"
		if not System.IO.File.Exists(file):
			file = Py.cwd + "/" + name + ".dll"
			if not System.IO.File.Exists(file):
				file = Py.execpath + "/lib/" + name + ".py"
				if not System.IO.File.Exists(file):
					file = System.IO.Path.GetDirectoryName(mscorlib.Location) + "/" + name + ".dll"
					if not System.IO.File.Exists(file):
						raise Exception("No module named '" + name + "'")

	if file.EndsWith(".dll"):
		assembly = System.Reflection.Assembly.LoadFrom(file)
		if fromlist != None:
			if len(fromlist) == 1 and fromlist[0] == "__mul__":
				for t in assembly.GetTypes():
					if t.IsPublic:
						push_type(t, G)
			else:
				for i in fromlist:
					t = assembly.GetType(name + "." + i)
					if t == None:
						raise Exception("cannot import Type '" + i + "' from '" + name + "'")
					push_type(t, G)
		else:
			import_assembly(assembly, globals())
	else:
		mod = import_module(file)
		if fromlist != None:
			if len(fromlist) == 1 and fromlist[0] == "__mul__":
				for i in mod.Globals:
					G[i.Key] = i.Value
			else:
				for i in fromlist:
					if i not in mod.Globals:
						raise Exception("cannot import name '" + i + "' from '" + name + "'")
					G[i] = mod[i]
		else:
			G[name] = mod

def import_module(file):
	py = Py(file)
	py.Execute()
	return py

class Namespace:
	pass

def import_assembly(assembly:System.Reflection.Assembly, G:System.Collections.IDictionary=globals()):
	NS = Namespace@type
	nss = System.Collections.Generic.Dictionary[str, System.Collections.IDictionary]()
	for t in assembly.GetTypes():
		if t.IsPublic:
			ns = t.Namespace
			if not nss.ContainsKey(ns):
				tokens = ns.Split(('.'[0],), 0@System.StringSplitOptions)
				d = G
				for token in tokens:
					if not d.Contains(token): d[token] = NS.CreateInstance(())
					d = (d[token]@object).Dict
				nss[ns] = d
			# push type
			d = nss[ns]
			n = t.Name
			if n.Contains('`'): # generic
				n, i = n.Split(('`'[0],), 0@System.StringSplitOptions)
			if not d.Contains(n):
				d[n] = Py.CType(t)
			# push generic
			if t.IsGenericType:
				ct = d[n]@Py.CType
				if ct.GenericTypes == None:
					ct.GenericTypes = (None@ct.GenericTypes)()
				ct.GenericTypes.Add(int.Parse(i), t)

def using(namespace):
	"""Import a namespace."""
	d = globals()
	for ns in namespace.__dict__:
		d[ns.Key] = ns.Value

def typeof(c: Py.CType):
	return c.Type

def globals():
	"""Return the dictionary containing the current scope's global variables."""
	return Py.Threads.Peek().Globals

tmpobj959deb9e416d = object()
def super(Subclass:type=None, instance:object=None):
	if Subclass == None:
		Subclass = instance.Type
	parent = Subclass.Bases[Subclass.Bases.Count - 1]
	obj = tmpobj959deb9e416d@object
	obj.Type = parent
	obj.Dict = instance.Dict
	return obj

Py = CType(__py__)

# import mscorlib
mscorlib = (0).GetType().Assembly
import_assembly(mscorlib)
import System.Console
import System.Linq

using(System)
using(System.Collections.Generic)

int = Int32
float = Double
str = String
bool = Boolean
list = List[Object]
tuple = Object[]
dict = Dictionary[Object, Object]
set = HashSet[Object]

IndexError = Exception
AttributeError = Exception
ValueError = Exception

# Decorator @staticmethod
def staticmethod(func: function):
	func.IsStatic = True
	return func

# EXTENSIONS

ExtKey = ValueTuple[Type, str]
def extension(type, name):
    def my_decorator(func):
        Py.Extensions.Add(ExtKey(type, name), func)
        return func
    return my_decorator

# str

@extension(str, "replace")
def str_replace(self, a, b):
    return self.Replace(a, b)

@extension(str, "join")
def str_join(self, arr):
    return str.Join(self, arr as Object[])

@extension(str, "split")
def str_split(self: str, sep: str):
	return self.Split((sep[0],), 0@System.StringSplitOptions)

@extension(str, "__new__")
def str_new(self, obj):
	if obj is object: return obj.__str__()
	if obj is None: return "None"
	return obj.ToString()
# list

@extension(list, "append")
def list_append(self:list, obj):
	self.Add(obj)

@extension(list, "ToString")
def list_tostring(self):
	return iterable_tostring(self, "[", "]")

# tuple

@extension(tuple, "ToString")
def tuple_tostring(self):
	return iterable_tostring(self, "(", ")")

# dict

@extension(dict, "ToString")
def dict_tostring(self):
	return iterable_tostring(self, "{", "}")

@extension(KeyValuePair[Object, Object], "ToString")
def KeyValuePair_tostring(self):
	return self.Key + ": " + self.Value

# set

@extension(set, "ToString")
def set_tostring(self):
	if len(self) == 0: return "set()"
	return iterable_tostring(self, "{", "}")

# FUNCTIONS

def max(*args, key=None):
    """
    Custom max function that emulates Python's built-in max function.

    :param args: A single iterable or multiple arguments.
    :param key: An optional key function for comparison.
    :return: The maximum value.
    """
    if len(args) == 0:
        raise Exception("max() arg is an empty sequence")

    # Determine if args is a single iterable or multiple arguments
    if len(args) == 1:
        iterable = args[0]
    else:
        iterable = args

    # Initialize max value
    max_value = None

    for item in iterable:
        # Apply key function if provided
        if key:
            compare_value = key(item)
        else:
            compare_value = item

        # Set max value
        if max_value is None or compare_value > max_value:
            max_value = compare_value

    if max_value is None:
        raise Exception("max() arg is an empty sequence")

    return max_value

def min(*args, key=None):
    """
    Custom min function that emulates Python's built-in min function.

    :param args: A single iterable or multiple arguments.
    :param key: An optional key function for comparison.
    :return: The minimum value.
    """
    if len(args) == 0:
        raise Exception("min() arg is an empty sequence")

    # Determine if args is a single iterable or multiple arguments
    if len(args) == 1:
        iterable = args[0]
    else:
        iterable = args

    # Initialize min value
    min_value = None

    for item in iterable:
        # Apply key function if provided
        if key:
            compare_value = key(item)
        else:
            compare_value = item

        # Set min value
        if min_value is None or compare_value < min_value:
            min_value = compare_value

    if min_value is None:
        raise Exception("min() arg is an empty sequence")

    return min_value

def slice(lst, start=None, stop=None, step=1):
    """
    Custom slice function that emulates Python's list slicing, including the 'step' functionality and negative steps.
    """
    # Validate step - cannot be zero
    if step == 0:
        raise Exception("slice step cannot be zero")

    # Set default for start and stop depending on step positivity
    if start is None:
        start = 0 if step > 0 else len(lst) - 1
    if stop is None:
        stop = len(lst) if step > 0 else -1

    # Handle negative indices
    if start < 0:
        start = max(len(lst) + start, -1) if step > 0 else len(lst) + start
    if stop < 0:
        stop = max(len(lst) + stop, -1) if step > 0 else len(lst) + stop

    # Adjust start and stop for bounds
    start = min(max(start, -1), len(lst)) if step > 0 else min(max(start, 0), len(lst) - 1)
    stop = min(max(stop, -1), len(lst)) if step > 0 else min(max(stop, 0), len(lst))

    # Create the sliced list
    sliced_list = []

    # Perform slicing
    if step > 0:
        if start < stop:
            for i in range(start, stop, step):
                sliced_list.append(lst[i])
    else:
        if start > stop:
            for i in range(start, stop, step):
                sliced_list.append(lst[i])

    return sliced_list

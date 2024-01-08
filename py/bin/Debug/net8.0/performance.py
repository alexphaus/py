
def func(start, stop=None, step=1, *args, j=8, **kwargs):
    __kwargs__ = Getkwargs(__args__, __argslen__)

    #params
    start = __argslen__ > 0 ? __args__[0] : kwargs != null && kwargs.Remove('start', tmp) ? tmp : throw
    stop = __argslen__ > 1 ? __args__[1] : kwargs != null && kwargs.Remove('stop', tmp) ? tmp : None
    step = __argslen__ > 2 ? __args__[2] : kwargs != null && kwargs.Remove('step', tmp) ? tmp : 1
    args = getsubargs(__args__,3, __argslen__)

    j = __argslen__ > 4 ? __args__[4] : kwargs != null && kwargs.Remove('j', tmp) ? tmp : 8

    kwargs == __kwargs__
    

    # body
    return None


a = [1,2,3]
i = 0
while i < 100000000:
    range(0,1,1,2,3,4,1,1,1,1,1,1,1,1,1,1, x=2, y=9)
    i += 1

print("ok")

#
#-------TEST 2------------
# { 1.95s }

def foo(start, stop=None, step=1, *args, **kwargs):
    return None

i = 0
while i < 10000000:
    foo(1,2,3,4,5,j=1,y=2,t=0)
    i += 1

print("ok")

#-------------------------




def main():

    def hola2(a:str, b:str,c:str):
        return a.Replace(b, c)
    extension(str, "replace")(hola2)

    def hola3(lst, b):
        lst.Add(b)
    extension(list, "append")(hola3)

    #"hola".Replace("h", "m")   23.9 -> 28.2s  -> 30s -> 32s -> 45s

    class Animal:
        def hello(self):
            return None
        
    dog = Animal()
    f = []

    for i in range(100000000):
        (f@list).Add(i@int)

    #l = [c for c in "hola"]
    l = [1,2,3]
    print(l)

main()



#l = [c*2 for c in [1,2,3,4,5] if c > 2]
l = (c for c in "hola")

for i in l:
    print(i)

print(l)

class ForGenerator:
    def __init__(self, iterable, func):
        self.iterable = iterable
        self.index = 0
        self.current = None
        self.func = func

    def __iter__(self):
        return self

    def __next__(self):
        check = self.index < len(self.iterable)
        if check:
            value = self.iterable[self.index]
            self.index = self.index + 1

            self.current = func(value)
        
        return check
    
# ----------------------
    
def hola(*args, h=0,**k):
    print(k)

d = {'h':9,'c':77}
l = [1,2,3,4]
hola(1,*l,3,**d)


#----------------------

l = [1,2,3,4]


for i in range(1000000):
    l2 = (1,*l,3, *(i for i in range(0, 100) if i % 2 == 0))

print(l2)  # 9.2s




add = lambda a, b: a + b

def hola():
    pass
print(hola)
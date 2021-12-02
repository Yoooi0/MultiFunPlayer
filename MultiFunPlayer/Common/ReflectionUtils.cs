using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common;

public static class ReflectionUtils
{
    public static Assembly Assembly => Assembly.GetEntryAssembly();
    public static AssemblyName AssemblyName => Assembly.GetName();
    public static Version AssemblyVersion => AssemblyName.Version;

    public static IEnumerable<Type> FindImplementations<T>() => FindImplementations(typeof(T));
    public static IEnumerable<Type> FindImplementations(Type type)
        => Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract)
                              .Where(t => IsAssignableFromOrSubclass(type, t));

    public static bool IsAssignableFromOrSubclass(Type baseType, Type otherType)
        => baseType.IsInterface
            ? baseType.IsAssignableFrom(otherType)
            : otherType.IsSubclassOf(baseType) || baseType == otherType;
}

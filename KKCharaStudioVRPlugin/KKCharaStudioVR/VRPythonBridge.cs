using System;
using System.Collections.Generic;
using System.Reflection;
using VRGIN.Core;

namespace KKCharaStudioVR;

internal static class VRPythonBridge
{
    private static readonly HashSet<string> LoggedFailures = new HashSet<string>();
    private static object _mainEngine;
    private static MethodInfo _createScriptSourceMethod;
    private static object _scriptKindArgument;

    public static bool TryExecute(string code, string operation, out string error)
    {
        error = null;
        if (!TryResolveEngine(out error))
        {
            LogOnce(operation, error ?? "unknown", false);
            return false;
        }

        try
        {
            object[] createArguments = _scriptKindArgument == null
                ? new object[] { code }
                : new object[] { code, _scriptKindArgument };
            object scriptSource = _createScriptSourceMethod.Invoke(_mainEngine, createArguments);
            if (scriptSource == null)
                throw new InvalidOperationException("Python script source was not created.");

            // ScriptSource.Compile() is unique. CompiledCode.Execute() is not:
            // Execute() and Execute<T>() share an empty parameter list, and
            // Type.GetMethod(string, Type[]) throws AmbiguousMatchException.
            MethodInfo compileMethod = FindMethod(scriptSource.GetType(), "Compile", Type.EmptyTypes);
            if (compileMethod == null)
                throw new MissingMethodException(scriptSource.GetType().FullName, "Compile()");

            object compiledCode = compileMethod.Invoke(scriptSource, null);
            if (compiledCode == null)
                throw new InvalidOperationException("Python code was not compiled.");

            MethodInfo executeMethod = FindMethod(compiledCode.GetType(), "Execute", Type.EmptyTypes);
            if (executeMethod == null)
                throw new MissingMethodException(compiledCode.GetType().FullName, "Execute()");

            executeMethod.Invoke(compiledCode, null);
            VRLog.Info(operation + " completed.");
            return true;
        }
        catch (Exception ex)
        {
            Exception root = Unwrap(ex);
            error = string.IsNullOrEmpty(root.Message)
                ? root.GetType().Name
                : root.GetType().Name + ": " + root.Message;
            _mainEngine = null;
            _createScriptSourceMethod = null;
            _scriptKindArgument = null;
            LogOnce(operation, root.ToString(), true);
            return false;
        }
    }

    private static bool TryResolveEngine(out string error)
    {
        error = null;
        if (_mainEngine != null && _createScriptSourceMethod != null)
            return true;

        try
        {
            Assembly consoleAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Unity.Console")
                {
                    consoleAssembly = assembly;
                    break;
                }
            }

            if (consoleAssembly == null)
            {
                error = "Unity.Console is not loaded. VNGE/MMDD may still be starting.";
                return false;
            }

            Type programType = consoleAssembly.GetType("Unity.Console.Program");
            MethodInfo getMainEngine = programType?.GetMethod(
                "get_MainEngine",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _mainEngine = getMainEngine?.Invoke(null, null);
            if (_mainEngine == null)
            {
                error = "VNGE Python engine is not ready.";
                return false;
            }

            // Unity.Console runs multi-line scripts as SourceCodeKind.File.
            // The string-only overload is AutoDetect and is only a fallback.
            Type kindType = FindType("Microsoft.Scripting", "Microsoft.Scripting.SourceCodeKind");
            if (kindType != null)
            {
                _createScriptSourceMethod = FindMethod(
                    _mainEngine.GetType(),
                    "CreateScriptSourceFromString",
                    new[] { typeof(string), kindType });
                if (_createScriptSourceMethod != null)
                    _scriptKindArgument = Enum.Parse(kindType, "File");
            }
            if (_createScriptSourceMethod == null)
            {
                _scriptKindArgument = null;
                _createScriptSourceMethod = FindMethod(
                    _mainEngine.GetType(),
                    "CreateScriptSourceFromString",
                    new[] { typeof(string) });
            }

            if (_createScriptSourceMethod == null)
            {
                error = "VNGE Python engine is not ready.";
                _mainEngine = null;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = Unwrap(ex).Message;
            _mainEngine = null;
            _createScriptSourceMethod = null;
            _scriptKindArgument = null;
            return false;
        }
    }

    private static Type FindType(string assemblyName, string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                continue;
            Type type = assembly.GetType(typeName, false);
            if (type != null)
                return type;
        }
        return null;
    }

    // Skips generic definitions. GetMethod(name, types) treats Execute() and
    // Execute<T>() as the same signature and throws AmbiguousMatchException.
    private static MethodInfo FindMethod(Type type, string name, Type[] parameterTypes)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo match = null;
        foreach (MethodInfo method in type.GetMethods(flags))
        {
            if (method.Name != name || method.IsGenericMethodDefinition)
                continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != parameterTypes.Length)
                continue;
            bool same = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != parameterTypes[i])
                {
                    same = false;
                    break;
                }
            }
            if (!same)
                continue;
            if (match != null)
                return null;
            match = method;
        }
        return match;
    }

    private static void LogOnce(string operation, string message, bool error)
    {
        string distinct = operation + ": " + message;
        if (!LoggedFailures.Add(distinct))
            return;
        if (error)
            VRLog.Error(operation + " failed: " + message);
        else
            VRLog.Warn(operation + " failed: " + message);
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }
}

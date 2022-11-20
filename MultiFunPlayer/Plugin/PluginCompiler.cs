using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using MultiFunPlayer.Common;
using NLog;
using Stylet;
using StyletIoC;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace MultiFunPlayer.Plugin;

internal class PluginCompilationResult : IDisposable
{
    public Exception Exception { get; private set; }
    public AssemblyLoadContext Context { get; private set; }
    public PluginSettingsBase Settings { get; private set; }
    public UIElement SettingsView { get; private set; }

    private Func<PluginBase> PluginFactory { get; set; }

    public bool Success => Exception == null;

    private PluginCompilationResult() { }

    public static PluginCompilationResult FromFailure(Exception e) => new() { Exception = e };
    public static PluginCompilationResult FromFailure(AssemblyLoadContext context, Exception e)
    {
#pragma warning disable IDE0059 // Unnecessary assignment of a value
        context?.Unload();
        context = null;
#pragma warning restore IDE0059 // Unnecessary assignment of a value

        return new() { Exception = e };
    }

    public static PluginCompilationResult FromSuccess(AssemblyLoadContext context, Func<PluginBase> pluginFactory) 
        => new() { Context = context, PluginFactory = pluginFactory };
    public static PluginCompilationResult FromSuccess(AssemblyLoadContext context, Func<PluginBase> pluginFactory, PluginSettingsBase settings, UIElement settingsView)
        => new() { Context = context, PluginFactory = pluginFactory, Settings = settings, SettingsView = settingsView };

    public PluginBase CreatePluginInstance() => PluginFactory?.Invoke();

    protected virtual void Dispose(bool disposing)
    {
        PluginFactory = null;
        SettingsView = null;
        Settings = null;

        Context?.Unload();
        Context = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

internal static class PluginCompiler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static Regex ReferenceRegex { get; } = new Regex(@"^//#r\s+""(?<type>name|file):(?<value>.+?)""", RegexOptions.Compiled | RegexOptions.Multiline);

    private static IContainer Container { get; set; }
    private static IViewManager ViewManager { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void QueueCompile(FileInfo pluginFile, Action<PluginCompilationResult> callback)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = Compile(pluginFile);

            //TODO: for some reason compilation leaks a lot of unmanaged memory
            GC.Collect();
            GC.WaitForPendingFinalizers();

            callback(result);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static PluginCompilationResult Compile(FileInfo pluginFile)
    {
        var context = default(CollectibleAssemblyLoadContext);
        try
        {
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(PluginBase).Assembly.Location)
            };

            references.AddRange(ReflectionUtils.Assembly.GetReferencedAssemblies().Select(a => MetadataReference.CreateFromFile(Assembly.Load(a).Location)));

            var pluginSource = File.ReadAllText(pluginFile.FullName);
            foreach(var match in ReferenceRegex.Matches(pluginSource).NotNull())
            {
                var type = match.Groups["type"].Value;
                var value = match.Groups["value"].Value;

                var reference = type switch
                {
                    "name" => MetadataReference.CreateFromFile(Assembly.Load(value).Location),
                    "file" => MetadataReference.CreateFromFile(value),
                    _ => null
                };

                if (reference != null)
                    references.Add(reference);
            }

            var validPluginBaseClasses = new List<string>()
            {
                nameof(SyncPluginBase),
                nameof(AsyncPluginBase)
            };

            var sourcePath = pluginFile.FullName;
            var pdbPath = Path.ChangeExtension(sourcePath, ".pdb");

            var sourceBuffer = Encoding.UTF8.GetBytes(pluginSource);
            var sourceText = SourceText.From(
                sourceBuffer,
                sourceBuffer.Length,
                Encoding.UTF8,
                canBeEmbedded: true
            );

            var syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                path: sourcePath
            );

            var pluginClasses = syntaxTree.GetRoot()
                                          .DescendantNodes()
                                          .OfType<ClassDeclarationSyntax>()
                                          .Where(s => s.BaseList.Types.Any(x => validPluginBaseClasses.Contains(x.ToString())))
                                          .ToList();

            if (pluginClasses.Count == 0)
                return PluginCompilationResult.FromFailure(new Exception("Unable to find base Plugin class"));
            if (pluginClasses.Count > 1)
                return PluginCompilationResult.FromFailure(new Exception("Found more than one base Plugin class"));

            var assemblyName = $"Plugin_{Path.GetFileNameWithoutExtension(pluginFile.Name)}";
            var encoded = CSharpSyntaxTree.Create(
                syntaxTree.GetRoot() as CSharpSyntaxNode,
                null,
                sourcePath,
                Encoding.UTF8
            );

            var compilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: Debugger.IsAttached ? OptimizationLevel.Debug : OptimizationLevel.Release,
                warningLevel: 4,
                deterministic: true
            );

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { encoded },
                references: references,
                options: compilationOptions
            );

            var emitOptions = new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: pdbPath
            );

            var embeddedTexts = new List<EmbeddedText>
            {
                EmbeddedText.FromSource(sourcePath, sourceText)
            };

            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();

            var emitResult = compilation.Emit(
                peStream: peStream,
                pdbStream: pdbStream,
                options: emitOptions,
                embeddedTexts: embeddedTexts
            );

            if (!emitResult.Success)
            {
                var diagnostics = emitResult.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
                return PluginCompilationResult.FromFailure(new PluginCompileException("Plugin failed to compile due to errors", diagnostics));
            }

            peStream.Seek(0, SeekOrigin.Begin);
            pdbStream.Seek(0, SeekOrigin.Begin);

            context = new CollectibleAssemblyLoadContext();
            var assembly = context.LoadFromStream(peStream, pdbStream);
            var pluginType = Array.Find(assembly.GetExportedTypes(), t => t.IsAssignableTo(typeof(PluginBase)));

            if (pluginType == null)
                return PluginCompilationResult.FromFailure(context, new PluginCompileException("Unable to find exported Plugin type"));

            var pluginConstructors = pluginType.GetConstructors();
            if (pluginConstructors.Length != 1)
                return PluginCompilationResult.FromFailure(context, new PluginCompileException("Plugin can only have one constructor"));

            var constructorParameters = pluginConstructors[0].GetParameters();
            if (constructorParameters.Length > 1)
                return PluginCompilationResult.FromFailure(context, new PluginCompileException("Plugin constructor can only have zero or one parameters"));

            var settingsType = constructorParameters.FirstOrDefault()?.ParameterType;
            if (settingsType != null && !settingsType.IsAssignableTo(typeof(PluginSettingsBase)))
                return PluginCompilationResult.FromFailure(context, new PluginCompileException($"Plugin constructor parameter must extend \"{nameof(PluginSettingsBase)}\""));

            if (settingsType == null)
            {
                var pluginFactory = () =>
                {
                    var instance = Activator.CreateInstance(pluginType) as PluginBase;
                    Container.BuildUp(instance);
                    return instance;
                };

                return PluginCompilationResult.FromSuccess(context, pluginFactory);
            }
            else
            {
                var settings = settingsType != null ? Activator.CreateInstance(settingsType) as PluginSettingsBase : null;
                if (settingsType != null && settings == null)
                    return PluginCompilationResult.FromFailure(context, new PluginCompileException($"Unable to create settings instance"));

                var settingsView = default(UIElement);
                Execute.OnUIThreadSync(() =>
                {
                    settingsView = settings.CreateView();

                    if (settingsView != null)
                        ViewManager.BindViewToModel(settingsView, settings);
                });

                var pluginFactory = () =>
                {
                    var instance = Activator.CreateInstance(pluginType, new[] { settings }) as PluginBase;
                    Container.BuildUp(instance);
                    return instance;
                };

                return PluginCompilationResult.FromSuccess(context, pluginFactory, settings, settingsView);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Plugin compiler failed with exception");
            return PluginCompilationResult.FromFailure(context, new PluginCompileException("Plugin compiler failed with exception", e));
        }
    }

    public static void Initialize(IContainer container)
    {
        Container = container;
        ViewManager = container.Get<IViewManager>();
    }

    private class CollectibleAssemblyLoadContext : AssemblyLoadContext
    {
        public CollectibleAssemblyLoadContext()
            : base(isCollectible: true) { }

        protected override Assembly Load(AssemblyName assemblyName) => null;
    }

    private class PluginCompileException : Exception
    {
        public PluginCompileException() { }

        public PluginCompileException(string message)
            : base(message) { }

        public PluginCompileException(string message, Exception innerException)
            : base(message, innerException) { }

        public PluginCompileException(string message, IEnumerable<Diagnostic> diagnostics)
            : this($"{message}\n{string.Join("\n", diagnostics.Select(d => d.ToString()))}") { }
    }
}

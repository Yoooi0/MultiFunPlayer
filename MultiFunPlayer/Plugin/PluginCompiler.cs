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

public class PluginCompilationResult : IDisposable
{
    public PluginBase Instance { get; init; }
    public Exception Exception { get; init; }
    public AssemblyLoadContext Context { get; private set; }
    public UIElement View { get; private set; }

    public bool Success => Exception == null && Instance != null;

    public static PluginCompilationResult FromFailure(Exception e) => new() { Exception = e };
    public static PluginCompilationResult FromSuccess(PluginBase instance, AssemblyLoadContext context, UIElement view) => new() { Instance = instance, Context = context, View = view };

    protected virtual void Dispose(bool disposing)
    {
        if (Instance == null)
            return;

        Context?.Unload();
        Context = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public static class PluginCompiler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static Regex ReferenceRegex { get; } = new Regex(@"^//#r\s+""(?<type>name|file):(?<value>.+?)""", RegexOptions.Compiled | RegexOptions.Multiline);

    private static IContainer Container { get; set; }
    private static IViewManager ViewManager { get; set; }

    public static void QueueCompile(FileInfo pluginFile, Action<PluginCompilationResult> callback)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = Compile(pluginFile);
            callback(result);
        });
    }

    public static PluginCompilationResult Compile(FileInfo pluginFile)
    {
        try
        {
            var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(PluginBase).Assembly.Location)
            };

            references.AddRange(ReflectionUtils.Assembly.GetReferencedAssemblies().Select(a => MetadataReference.CreateFromFile(Assembly.Load(a).Location)));

            var pluginSource = File.ReadAllText(pluginFile.FullName);
            pluginSource = ReferenceRegex.Replace(pluginSource, m =>
            {
                var type = m.Groups["type"].Value;
                var value = m.Groups["value"].Value;

                var reference = type switch
                {
                    "name" => MetadataReference.CreateFromFile(Assembly.Load(value).Location),
                    "file" => MetadataReference.CreateFromFile(value),
                    _ => null
                };

                if (reference != null)
                    references.Add(reference);

                return m.Value;
            });

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

            var pluginClass = pluginClasses[0];
            var assemblyName = $"Plugin_{pluginClass.Identifier.Text}";

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

            var context = new CollectibleAssemblyLoadContext();
            var assembly = context.LoadFromStream(peStream, pdbStream);
            var pluginType = Array.Find(assembly.GetExportedTypes(), t => t.IsAssignableTo(typeof(PluginBase)));

            if (Activator.CreateInstance(pluginType) is not PluginBase instance)
                return PluginCompilationResult.FromFailure(new PluginCompileException("Failed to instantiate Plugin instance"));

            Container.BuildUp(instance);

            var view = default(UIElement);
            Execute.OnUIThreadSync(() => {
                view = instance.CreateView();

                if (view != null)
                    ViewManager.BindViewToModel(view, instance);
            });

            return PluginCompilationResult.FromSuccess(instance, context, view);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Plugin compiler failed with exception");
            return PluginCompilationResult.FromFailure(new PluginCompileException("Plugin compiler failed with exception", e));
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

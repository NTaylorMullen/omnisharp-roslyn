using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.LanguageServerProtocol;
using OmniSharp.Plugins;
using OmniSharp.Services;
using OmniSharp.Stdio.Eventing;
using OmniSharp.Stdio.Logging;

namespace OmniSharp.Stdio.Driver
{
    internal class Program
    {
        static int Main(string[] args) => HostHelpers.Start(() =>
        {
            var application = new StdioCommandLineApplication();
            application.OnExecute(() =>
            {
                // If an encoding was specified, be sure to set the Console with it before we access the input/output streams.
                // Otherwise, the streams will be created with the default encoding.
                if (application.Encoding != null)
                {
                    var encoding = Encoding.GetEncoding(application.Encoding);
                    Console.InputEncoding = encoding;
                    Console.OutputEncoding = encoding;
                }

                var cancellation = new CancellationTokenSource();

                if (application.Lsp)
                {
                    Configuration.ZeroBasedIndices = true;
                    using (var host = new LanguageServerHost(
                        Console.OpenStandardInput(),
                        Console.OpenStandardOutput(),
                        application,
                        cancellation))
                    {
                        host.Start().Wait();
                        cancellation.Token.WaitHandle.WaitOne();
                    }
                }
                else
                {
                    var input = Console.In;
                    var output = Console.Out;

                    var environment = application.CreateEnvironment();
                    Configuration.ZeroBasedIndices = application.ZeroBasedIndices;
                    var configuration = new ConfigurationBuilder(environment).Build();
                    var writer = new SharedTextWriter(output);
                    var serviceProvider = CompositionHostBuilder.CreateDefaultServiceProvider(environment, configuration, new StdioEventEmitter(writer),
                        configureLogging: builder => builder.AddStdio(writer));

                    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                    var assemblyLoader = serviceProvider.GetRequiredService<IAssemblyLoader>();

                    var plugins = application.CreatePluginAssemblies();

                    var compositionHostBuilder = new CompositionHostBuilder(serviceProvider)
                        .WithOmniSharpAssemblies();

                    var pluginAssemblies = LoadPluginAssemblies(loggerFactory, assemblyLoader, plugins);
                    compositionHostBuilder.WithAssemblies(pluginAssemblies);

                    using (var host = new Host(input, writer, environment, serviceProvider, compositionHostBuilder, loggerFactory, cancellation))
                    {
                        host.Start();
                        cancellation.Token.WaitHandle.WaitOne();
                    }
                }

                return 0;
            });

            return application.Execute(args);
        });

        private static Assembly[] LoadPluginAssemblies(
            ILoggerFactory loggerFactory,
            IAssemblyLoader assemblyLoader,
            PluginAssemblies plugins)
        {
            var logger = loggerFactory.CreateLogger<Program>();
            var pluginAssemblies = new List<Assembly>();

            foreach (var pluginAssemblyNameOrPath in plugins.AssemblyNames)
            {
                try
                {
                    var assembly = assemblyLoader.LoadByAssemblyNameOrPath(pluginAssemblyNameOrPath);
                    pluginAssemblies.Add(assembly);
                }
                catch (Exception exception)
                {
                    logger.LogError($"Failed to load plugin assembly '{pluginAssemblyNameOrPath}'.");
                    logger.LogError(exception.Message);
                }
            }

            return pluginAssemblies.ToArray();
        }
    }
}

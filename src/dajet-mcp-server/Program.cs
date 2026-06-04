using DaJet.Data;
using DaJet.Mcp.Server.Tools;
using DaJet.Scripting;
using DaJet.Scripting.Model;
using DaJet.TypeSystem;
using DaJet.Utilities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json;
using MetadataCache = DaJet.Metadata.MetadataProvider;

namespace DaJet.Mcp.Server
{
    public class Program
    {
        private static readonly Dictionary<string, McpServerTool> _tools = new();
        public static void Main(string[] args)
        {
            InitializeMetadataCache();
            InitializeDaJetScriptTools();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Host.UseSystemd();
            builder.Host.UseWindowsService();

            IMcpServerBuilder mcp = builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly();

            if (_tools.Count > 0)
            {
                mcp.WithTools(_tools.Values);
            }

            WebApplication app = builder.Build();

            app.MapMcp();

            app.Run();
        }
        private static void InitializeMetadataCache()
        {
            ServerSettings settings = new();

            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile("datasources.json", optional: false)
                .Build();

            config.Bind(settings);

            foreach (DataSourceSettings dataSource in settings.DataSources)
            {
                if (string.IsNullOrWhiteSpace(dataSource.Name))
                {
                    FileLogger.Default.Write($"Не указано имя источника данных.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dataSource.Type))
                {
                    FileLogger.Default.Write($"Не указан тип источника данных для '{dataSource.Name}'.");
                    continue;
                }

                if (!Enum.TryParse(dataSource.Type, out DataSourceType sourceType))
                {
                    FileLogger.Default.Write($"Указан неподдерживаемый тип источника данных '{dataSource.Type}' для '{dataSource.Name}'. Возможные значения: SqlServer или PostgreSql.");
                    continue;
                }

                try
                {
                    MetadataCache.Add(dataSource.Name, sourceType, dataSource.ConnectionString);
                }
                catch (Exception exception)
                {
                    string message = $"Ошибка регистрации источника данных '{dataSource.Name}': {exception.Message}";
                    FileLogger.Default.Write(message);
                    continue;
                }
            }
        }
        private static void InitializeDaJetScriptTools()
        {
            string catalogPath = Path.Combine(AppContext.BaseDirectory, "scripts");

            if (!Directory.Exists(catalogPath))
            {
                return;
            }

            InitializeDaJetScriptTools(in catalogPath);
        }
        private static void InitializeDaJetScriptTools(in string catalogPath)
        {
            foreach (string scriptPath in Directory.EnumerateFiles(catalogPath, "*.djs"))
            {
                InitializeDaJetScriptTool(in scriptPath);
            }

            foreach (string nestedCatalog in Directory.EnumerateDirectories(catalogPath))
            {
                InitializeDaJetScriptTools(in nestedCatalog);
            }
        }
        private static void InitializeDaJetScriptTool(in string scriptPath)
        {
            string settingsPath = Path.ChangeExtension(scriptPath, "json");

            if (!File.Exists(settingsPath))
            {
                FileLogger.Default.Write($"[TOOL][ERROR] {scriptPath}");
                FileLogger.Default.Write("Tool settings file is not found.");
                return;
            }

            if (!TryGetToolSettings(in settingsPath, out ToolSettings settings))
            {
                return;
            }

            try
            {
                string script = null;

                using (StreamReader reader = new(scriptPath, Encoding.UTF8))
                {
                    script = reader.ReadToEnd();
                }

                ScriptTool tool = new(in script, in settings);

                _tools.Add(tool.ProtocolTool.Name, tool);
            }
            catch (Exception error)
            {
                FileLogger.Default.Write($"[TOOL][ERROR][{settings.Name}] {scriptPath}");
                FileLogger.Default.Write(ExceptionHelper.GetErrorMessageAndStackTrace(error));
                return;
            }

            FileLogger.Default.Write($"[TOOL][LOADED][{settings.Name}] {scriptPath}");
        }
        private static bool TryGetToolSettings(in string settingsPath, out ToolSettings settings)
        {
            settings = null;

            try
            {
                using (StreamReader reader = new(settingsPath, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();

                    settings = JsonSerializer.Deserialize<ToolSettings>(json);
                }
            }
            catch (Exception exception)
            {
                FileLogger.Default.Write(ExceptionHelper.GetErrorMessageAndStackTrace(exception));
            }

            return settings is not null;
        }
    }
}
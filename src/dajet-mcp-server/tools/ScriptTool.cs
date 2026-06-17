using DaJet.Json;
using DaJet.Scripting;
using DaJet.Scripting.Model;
using DaJet.TypeSystem;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace DaJet.Mcp.Server.Tools
{
    public sealed class ScriptTool : McpServerTool
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
        static ScriptTool()
        {
            JsonOptions.Converters.Add(new DictionaryJsonConverter());
        }
        private readonly Tool _tool;
        private readonly IReadOnlyList<object> _metadata;
        private readonly Interpreter _executor;
        public override Tool ProtocolTool { get { return _tool; } }
        public override IReadOnlyList<object> Metadata { get { return _metadata; } }
        public ScriptTool(in Script script, in ToolSettings settings)
        {
            _executor = new Interpreter(in script);

            JsonObject input = script.GetInputJsonSchema();
            JsonElement inputSchema = JsonSerializer.Deserialize<JsonElement>(input);

            JsonObject output = script.GetOutputJsonSchema();
            JsonElement outputSchema = JsonSerializer.Deserialize<JsonElement>(output);

            _tool = new Tool()
            {
                Name = settings.Name,
                Title = settings.Title,
                Description = settings.Description,
                InputSchema = inputSchema,
                OutputSchema = outputSchema
            };

            MethodInfo method = typeof(ScriptTool).GetMethod(nameof(InvokeAsync),
                BindingFlags.Instance | BindingFlags.Public,
                [typeof(RequestContext<CallToolRequestParams>), typeof(CancellationToken)]);

            _metadata = new List<object>() { method }.AsReadOnly();
        }
        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult result = new();

            try
            {
                IDictionary<string, JsonElement> input = request.Params?.Arguments;

                Dictionary<string, object> parameters = GetInputFromParameters(in input);

                object output = _executor.Execute(in parameters);

                JsonObject value = GetSimpleTypeResult(in output);

                if (value is not null) // null, boolean, number, datetime, string, uuid, entity
                {
                    result.StructuredContent = JsonSerializer.SerializeToElement(value, JsonOptions);
                }
                else // object, array
                {
                    result.StructuredContent = JsonSerializer.SerializeToElement(output, JsonOptions);
                }

                result.Content.Add(new TextContentBlock()
                {
                    Text = result.StructuredContent.ToString()
                });

                result.IsError = false;
            }
            catch (Exception error)
            {
                result.IsError = true;

                result.Content.Add(new TextContentBlock() { Text = error.Message });
            }

            return ValueTask.FromResult(result);
        }
        private static JsonObject GetSimpleTypeResult(in object output)
        {
            JsonObject value = null;

            if (output is null)
            {
                value = new JsonObject() { ["value"] = null };
            }
            else if (output is bool boolean)
            {
                value = new JsonObject() { ["value"] = boolean };
            }
            else if (output is int integer)
            {
                value = new JsonObject() { ["value"] = integer };
            }
            else if (output is decimal number)
            {
                value = new JsonObject() { ["value"] = number };
            }
            else if (output is DateTime datetime)
            {
                value = new JsonObject() { ["value"] = datetime.ToString("yyyy-MM-ddTHH:mm:ss") };
            }
            else if (output is string text)
            {
                value = new JsonObject() { ["value"] = text };
            }
            else if (output is Guid uuid)
            {
                value = new JsonObject() { ["value"] = uuid.ToString() };
            }
            else if (output is Entity entity)
            {
                value = new JsonObject() { ["value"] = entity.ToString() };
            }

            return value;
        }
        private static Dictionary<string, object> GetInputFromParameters(in IDictionary<string, JsonElement> parameters)
        {
            Dictionary<string, object> input = new();

            if (parameters is null || parameters.Count == 0)
            {
                return input;
            }

            foreach (var parameter in parameters)
            {
                string name = parameter.Key;

                JsonElement value = parameter.Value;

                if (value.ValueKind == JsonValueKind.True)
                {
                    input.Add(name, true);
                }
                else if (value.ValueKind == JsonValueKind.False)
                {
                    input.Add(name, false);
                }
                else if (value.ValueKind == JsonValueKind.Number)
                {
                    input.Add(name, value.GetDecimal());
                }
                else if (value.ValueKind == JsonValueKind.String)
                {
                    string text = value.GetString();

                    if (Guid.TryParse(text, out Guid uuid))
                    {
                        input.Add(name, uuid);
                    }
                    else if (DateTime.TryParse(text, out DateTime datetime))
                    {
                        input.Add(name, datetime);
                    }
                    else if (text.StartsWith('{'))
                    {
                        if (!Entity.TryParse(text, out Entity entity))
                        {
                            throw new JsonException($"Input parameter '{name}' parse error. Incorrect value is {text}.");
                        }

                        input.Add(name, entity);
                    }
                    else
                    {
                        input.Add(name, text);
                    }
                }
            }

            return input;
        }
    }
}
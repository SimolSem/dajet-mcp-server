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

        private static JsonObject InferInputSchema(in Script script)
        {
            JsonArray required = new();
            JsonObject properties = new();

            JsonObject input = new()
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };

            foreach (SyntaxNode node in script.Statements)
            {
                if (node is DeclareStatement declare && !declare.IsPrivate)
                {
                    DataType type = declare.Type;

                    string name = declare.Identifier.TrimStart('@');

                    if (type.IsBoolean)
                    {
                        required.Add(name);
                        properties.Add(name, new JsonObject() { ["type"] = "boolean" });
                    }
                    else if (type.IsInteger || type.IsDecimal)
                    {
                        required.Add(name);
                        properties.Add(name, new JsonObject() { ["type"] = "number" });
                    }
                    else if (type.IsDateTime)
                    {
                        required.Add(name);
                        properties.Add(name, new JsonObject() { ["type"] = "string", ["format"] = "date-time" });
                    }
                    else if (type.IsString)
                    {
                        required.Add(name);
                        properties.Add(name, new JsonObject() { ["type"] = "string" });
                    }
                    else if (type.IsUuid)
                    {
                        required.Add(name);
                        properties.Add(name, new JsonObject() { ["type"] = "string", ["format"] = "uuid" });
                    }
                    else if (type.IsEntity) // {integer:uuid}
                    {
                        required.Add(name);
                        properties.Add(name, new JsonObject() { ["type"] = "string", ["pattern"] = "^{\\d+:[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}}$" });
                    }
                }
            }

            return input;
        }
        private static JsonObject InferOutputSchema(in Script script)
        {
            JsonArray required = new();
            JsonObject properties = new();

            JsonObject output = new()
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };

            DefineStatement returnSchema = null;

            int count = script.Statements.Count - 1;

            for (int i = count; i >= 0; i--)
            {
                SyntaxNode node = script.Statements[i];

                if (node is ReturnStatement _return)
                {
                    returnSchema = GetOutputSchema(_return.Expression);

                    break;
                }
            }

            foreach (DefineProperty property in returnSchema.Properties)
            {
                string name = property.Name;
                DataType type = property.Type;

                if (type.IsBoolean)
                {
                    required.Add(name);
                    properties.Add(name, new JsonObject() { ["type"] = "boolean" });
                }
                else if (type.IsInteger || type.IsDecimal)
                {
                    required.Add(name);
                    properties.Add(name, new JsonObject() { ["type"] = "number" });
                }
                else if (type.IsDateTime)
                {
                    required.Add(name);
                    properties.Add(name, new JsonObject() { ["type"] = "string", ["format"] = "date-time" });
                }
                else if (type.IsString)
                {
                    required.Add(name);
                    properties.Add(name, new JsonObject() { ["type"] = "string" });
                }
                else if (type.IsUuid)
                {
                    required.Add(name);
                    properties.Add(name, new JsonObject() { ["type"] = "string", ["format"] = "uuid" });
                }
                else if (type.IsEntity) // {integer:uuid}
                {
                    required.Add(name);
                    properties.Add(name, new JsonObject() { ["type"] = "string", ["pattern"] = "^{\\d+:[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}}$" });
                }
                else if (type.IsUnion)
                {
                    //TODO: "name": { "oneOf": [
                    // { "type": "string", "format": "date-time" },
                    // { "type": "string", "format": "uuid" },
                    // { "type": "string", "pattern": "^{\\d+:[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}}$" },
                    // { "type": "string", },
                    // { "type": "boolean" },
                    // { "type": "number"  }
                    //]
                }
            }

            return properties.Count > 0 ? output : null;

            //NOTE: null throws exception
            // The specified document is not a valid MCP tool output JSON schema. (Parameter 'OutputSchema')
            // at ModelContextProtocol.Protocol.Tool.set_OutputSchema(Nullable`1 value)
        }
        private static DefineStatement GetOutputSchema(in SyntaxNode expression)
        {
            if (expression is VariableReference variable)
            {
                return GetOutputSchema(in variable);
            }
            else if (expression is FunctionExpression function)
            {
                return GetOutputSchema(in function);
            }

            return null;
        }
        private static DefineStatement GetOutputSchema(in VariableReference expression)
        {
            if (expression is VariableReference variable &&
                variable.Binding is DeclareStatement declare)
            {
                DefineStatement schema = declare.Binding;

                if (schema is not null)
                {
                    if (declare.Type.IsArray)
                    {
                        schema.Token = Token.Array;
                    }
                    else if (declare.Type.IsObject)
                    {
                        schema.Token = Token.Object;
                    }
                }

                return schema;
            }

            return null;
        }
        private static DefineStatement GetOutputSchema(in FunctionExpression expression)
        {
            if (expression is FunctionExpression function
                && function.Name == nameof(JSON)
                && function.Parameters[0] is VariableReference parameter
                && parameter.Binding is DeclareStatement declare)
            {
                DefineStatement schema = declare.Binding;

                if (schema is not null)
                {
                    if (declare.Type.IsArray)
                    {
                        schema.Token = Token.Array;
                    }
                    else if (declare.Type.IsObject)
                    {
                        schema.Token = Token.Object;
                    }
                }

                return schema;
            }

            return null;
        }

        private readonly Tool _tool;
        private readonly IReadOnlyList<object> _metadata;
        private readonly Interpreter _executor;
        public override Tool ProtocolTool { get { return _tool; } }
        public override IReadOnlyList<object> Metadata { get { return _metadata; } }
        public ScriptTool(in string script, in ToolSettings settings)
        {
            _executor = new Interpreter(in script);

            Script model = typeof(Interpreter).GetField("_script",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_executor) as Script;

            JsonObject input = InferInputSchema(in model);
            JsonElement inputSchema = JsonSerializer.Deserialize<JsonElement>(input);

            JsonObject output = InferOutputSchema(in model);
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

            IDictionary<string, JsonElement> input = request.Params?.Arguments;

            Dictionary<string, object> parameters = GetInputFromParameters(in input);

            object output = _executor.Execute(in parameters);

            result.StructuredContent = JsonSerializer.SerializeToElement(output, JsonOptions);

            result.IsError = false;

            return ValueTask.FromResult(result);
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
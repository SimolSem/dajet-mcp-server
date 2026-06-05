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

            if (returnSchema is null)
            {
                return null; // invalid output expression
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
            if (expression is ScalarExpression scalar)
            {
                return GetOutputSchema(in scalar);
            }
            else if (expression is VariableReference variable)
            {
                return GetOutputSchema(in variable);
            }
            else if (expression is FunctionExpression function)
            {
                return GetOutputSchema(in function);
            }

            return null;
        }
        private static DefineStatement GetOutputSchema(in ScalarExpression expression)
        {
            if (expression is null)
            {
                return null;
            }

            DefineStatement schema = new();

            DefineProperty property = new() { Name = "value" };

            if (expression.Token == Token.Boolean)
            {
                property.Type = DataType.Boolean;
            }
            else if (expression.Token == Token.Integer)
            {
                property.Type = DataType.Integer();
            }
            else if (expression.Token == Token.Decimal)
            {
                property.Type = DataType.Decimal();
            }
            else if (expression.Token == Token.DateTime)
            {
                property.Type = DataType.DateTime;
            }
            else if (expression.Token == Token.String)
            {
                property.Type = DataType.String();
            }
            else if (expression.Token == Token.Uuid)
            {
                property.Type = DataType.Uuid();
            }
            else if (expression.Token == Token.Entity)
            {
                property.Type = DataType.Entity();
            }
            else
            {
                property.Type = DataType.String();
            }

            schema.Properties.Add(property);

            return schema;
        }
        private static DefineStatement GetOutputSchema(in VariableReference expression)
        {
            if (expression.Binding is not DeclareStatement declare)
            {
                return null; // critical error - unbound variable
            }

            DataType type = declare.Type;

            if (type.IsObject || type.IsArray)
            {
                return declare.Binding;
            }

            DefineStatement schema = new();

            DefineProperty property = new() { Name = "value" };

            if (type.IsBoolean)
            {
                property.Type = DataType.Boolean;
            }
            else if (type.IsInteger)
            {
                property.Type = DataType.Integer();
            }
            else if (type.IsDecimal)
            {
                property.Type = DataType.Decimal();
            }
            else if (type.IsDateTime)
            {
                property.Type = DataType.DateTime;
            }
            else if (type.IsString)
            {
                property.Type = DataType.String();
            }
            else if (type.IsUuid)
            {
                property.Type = DataType.Uuid();
            }
            else if (type.IsEntity)
            {
                property.Type = DataType.Entity();
            }
            else
            {
                property.Type = DataType.String();
            }

            schema.Properties.Add(property);

            return schema;
        }
        private static DefineStatement GetOutputSchema(in FunctionExpression expression)
        {
            if (expression is FunctionExpression function
                && function.Name == nameof(JSON)
                && function.Parameters[0] is VariableReference parameter
                && parameter.Binding is DeclareStatement declare)
            {
                return declare.Binding;
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

            //TODO: получать схемы данных Script'а через сервисы библиотеки DaJet.Scripting
            Script model = GetDaJetScript(in script);

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
        private static Script GetDaJetScript(in string source)
        {
            if (!new Parser().TryParse(in source, out Script script, out string error))
            {
                throw new InvalidOperationException(error);
            }

            Scripting.Binder binder = new();
            ISchemaProvider schema = new CacheableSchemaProvider();

            if (!binder.TryBind(in script, in schema, out List<string> errors))
            {
                throw new InvalidOperationException(string.Join('\n', errors));
            }

            return script;
        }
        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult result = new();

            try
            {
                IDictionary<string, JsonElement> input = request.Params?.Arguments;

                Dictionary<string, object> parameters = GetInputFromParameters(in input);

                object output = _executor.Execute(in parameters);

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

                if (value is not null) // simple data types
                {
                    result.StructuredContent = JsonSerializer.SerializeToElement(value, JsonOptions);
                }
                else
                {
                    result.StructuredContent = JsonSerializer.SerializeToElement(output, JsonOptions);
                }

                result.IsError = false;
            }
            catch (Exception error)
            {
                result.IsError = true;

                result.Content.Add(new TextContentBlock() { Text = error.Message });
            }

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
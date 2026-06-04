using DaJet.Json;
using DaJet.Metadata;
using DaJet.Scripting;
using DaJet.Scripting.Model;
using DaJet.TypeSystem;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MetadataCache = DaJet.Metadata.MetadataProvider;

namespace DaJet.Mcp.Server.Tools
{
    [McpServerToolType]
    public sealed class QueryTool
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
        static QueryTool()
        {
            JsonOptions.Converters.Add(new DictionaryJsonConverter());
        }

        private const string TOOL_DESCRIPTION =
            "Executes a parameterized DaJet Script query to a registered 1C database data source. " +
            "Returns an array of arbitrary JSON objects. Supports read-only SELECT queries only. " +
            "Suitable for cross-database reports, and parameterized data access by 1C metadata object names.";

        [McpServerTool, Description(TOOL_DESCRIPTION)]
        public CallToolResult ExecuteQuery(
            [Description("Registered database name")] string database,
            [Description("SELECT query text")] string script,
            [Description("SELECT query parameters")] Dictionary<string, JsonElement> parameters)
        {
            CallToolResult result = new();

            MetadataProvider provider;

            try
            {
                provider = MetadataCache.Get(in database);
            }
            catch (Exception exception)
            {
                result.IsError = true;
                result.Content.Add(new TextContentBlock() { Text = exception.Message });
                return result;
            }

            if (provider is null)
            {
                result.IsError = true;
                result.Content.Add(new TextContentBlock()
                {
                    Text = $"Database '{database}' is not found"
                });
                return result;
            }

            Dictionary<string, object> input;

            try
            {
                input = GetInputFromParameters(in parameters);
            }
            catch (Exception exception)
            {
                result.IsError = true;
                result.Content.Add(new TextContentBlock() { Text = exception.Message });
                return result;
            }

            Parser parser = new();

            if (!parser.TryParse(in script, out Script query, out string error))
            {
                result.IsError = true;
                result.Content.Add(new TextContentBlock() { Text = error });
                return result;
            }

            try
            {
                Script model = AssembleScriptWithParameters(in database, in query, in input);

                Interpreter executor = new(in model);

                object json = executor.Execute(in input);

                result.IsError = false;
                result.Content.Add(new TextContentBlock()
                {
                    Text = json.ToString()
                });
            }
            catch (Exception exception)
            {
                result.IsError = true;
                result.Content.Add(new TextContentBlock() { Text = exception.Message });
            }

            //TODO: ?
            //ToolResultContentBlock result = new()
            //{
            //    StructuredContent = JsonDocument.Parse(json).RootElement
            //};

            return result;
        }
        private static Dictionary<string, object> GetInputFromParameters(in Dictionary<string, JsonElement> parameters)
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
        private static Script AssembleScriptWithParameters(in string database, in Script query, in Dictionary<string, object> parameters)
        {
            Script script = new();

            foreach (var parameter in parameters)
            {
                DeclareStatement declare = new()
                {
                    Identifier = string.Format("@{0}", parameter.Key)
                };

                object value = parameter.Value;

                if (value is bool) { declare.Type = DataType.Boolean; }
                else if (value is decimal) { declare.Type = DataType.Decimal(); }
                else if (value is DateTime) { declare.Type = DataType.DateTime; }
                else if (value is string) { declare.Type = DataType.String(); }
                else if (value is Guid) { declare.Type = DataType.Uuid(); }
                else if (value is Entity) { declare.Type = DataType.Entity(); }
                else
                {
                    continue; // Unsupported parameter type
                }

                script.Statements.Add(declare);
            }

            string outputTable = "@query_output_table";

            script.Statements.Add(new DeclareStatement()
            {
                Type = DataType.Array,
                Identifier = outputTable
            });

            UseStatement use = new() { Source = database };

            foreach (SyntaxNode statement in query.Statements)
            {
                if (statement is SelectStatement select)
                {
                    if (select.Expression is SelectExpression expression)
                    {
                        expression.Into = new IntoClause()
                        {
                            Value = new VariableReference()
                            {
                                Identifier = outputTable
                            }
                        };
                    }

                    use.Statements.Add(select);

                    break; // use only the first SELECT statement - ignore the rest
                }
            }

            script.Statements.Add(use);

            script.Statements.Add(new ReturnStatement()
            {
                Expression = new FunctionExpression()
                {
                    Token = Token.UDF,
                    Name = "JSON",
                    Parameters =
                    [
                        new VariableReference()
                        {
                            Identifier = outputTable
                        }
                    ]
                }
            });

            return script;
        }
    }
}
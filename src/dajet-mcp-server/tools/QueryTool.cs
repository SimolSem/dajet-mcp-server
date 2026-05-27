using DaJet.Json;
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

        [McpServerTool, Description("Executes SQL query against database with parameters returning JSON result")]
        public CallToolResult ExecuteQuery(
            [Description("Database name")] string database,
            [Description("Query text")] string script,
            [Description("Query parameters")] Dictionary<string, JsonElement> attributes)
        {
            CallToolResult result = new();

            if (MetadataCache.Get(in database) is null)
            {
                result.IsError = true;
                result.Content.Add(new TextContentBlock()
                {
                    Text = $"База данных '{database}' не найдена"
                });
                return result;
            }

            Dictionary<string, object> parameters;

            try
            {
                parameters = GetParametersFromAttributes(in attributes);
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
                Script model = AssembleScriptWithParameters(in database, in query, in parameters);

                Interpreter executor = new(in model);

                object json = executor.Execute(in parameters);

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

            //ToolResultContentBlock result = new()
            //{
            //    StructuredContent = JsonDocument.Parse(json).RootElement
            //};

            return result;
        }
        private static Dictionary<string, object> GetParametersFromAttributes(in Dictionary<string, JsonElement> attributes)
        {
            Dictionary<string, object> parameters = new();

            if (attributes is null || attributes.Count == 0)
            {
                return parameters;
            }

            foreach (var attribute in attributes)
            {
                string name = attribute.Key;

                JsonElement value = attribute.Value;

                if (value.ValueKind == JsonValueKind.True)
                {
                    parameters.Add(name, true);
                }
                else if (value.ValueKind == JsonValueKind.False)
                {
                    parameters.Add(name, false);
                }
                else if (value.ValueKind == JsonValueKind.Number)
                {
                    parameters.Add(name, value.GetDecimal());
                }
                else if (value.ValueKind == JsonValueKind.String)
                {
                    string text = value.GetString();

                    if (Guid.TryParse(text, out Guid uuid))
                    {
                        parameters.Add(name, uuid);
                    }
                    else if (DateTime.TryParse(text, out DateTime datetime))
                    {
                        parameters.Add(name, datetime);
                    }
                    else if (text.StartsWith('{'))
                    {
                        if (!Entity.TryParse(text, out Entity entity))
                        {
                            throw new JsonException($"Input parameter '{name}' parse error. Incorrect value is {text}.");
                        }

                        parameters.Add(name, entity);
                    }
                    else
                    {
                        parameters.Add(name, text);
                    }
                }
            }

            return parameters;
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

                    use.Statements.Statements.Add(select);

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
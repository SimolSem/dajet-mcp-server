# Тестирование DaJet MCP Server при помощи CURL

Тестирование MCP-сервера можно выполнить по протоколу HTTP при помощи технологии Server-Sent Events (SSE).

Для этого нужно сделать следующие шаги.

### 1. Запустить MCP сервер, например как консольное приложение.

<img width="493" height="362" alt="image" src="https://github.com/user-attachments/assets/37dcb226-6b2e-4fc9-8ff0-d071bc8c9487" />

### 2. Подготовить файлы запросов к MCP серверу.

**Файл инициализации сессии SSE ```init-mcp-session.json```**

```JSON
{
  "jsonrpc": "2.0", "id": 1,
  "method": "initialize",
  "params":
  {
    "protocolVersion": "2025-03-26",
    "clientInfo": { "name": "test-client", "version": "1.0.0" },
    "capabilities": {}
  }
}
```

**Файл подтверждения сессии SSE ```init-mcp-confirm.json```**

```JSON
{
  "jsonrpc": "2.0",
  "method": "notifications/initialized"
}
```

**Файл для просмотра доступных инструментов MCP-сервера ```list-mcp-tools.json```**

```JSON
{
  "jsonrpc": "2.0", "id": 2,
  "method": "tools/list",
  "params": {}
}
```

**Файл для выполнения инструмента MCP-сервером ```call-mcp-tool.json```**

```JSON
{
  "jsonrpc": "2.0", "id": 3,
  "method": "tools/call",
  "params": {
    "name": "execute_query",
    "arguments": {
      "database": "MS_TEST",
      "script": "SELECT TOP 1 Ссылка, Код, Наименование FROM Справочник.Номенклатура WHERE Код = @Код",
      "parameters": {
        "Код": "00000333"
      }
    }
  }
}
```

### 3. Открыть новое консольное окно для работы CURL.

Перейти в каталог, где расположены выше созданные файлы. Выполнить команду CURL для открытия сессии SSE с MCP-сервером.

```
curl -v -X POST http://localhost:3000 -d @init-mcp-session.json -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream"
```

Получить примерно следующий ответ и скопировать для последующих вызовов значение заголовка ```Mcp-Session-Id```.

```
< HTTP/1.1 200 OK
< Content-Type: text/event-stream
< Date: Wed, 27 May 2026 15:07:04 GMT
< Server: Kestrel
< Cache-Control: no-cache,no-store
< Content-Encoding: identity
< Transfer-Encoding: chunked
< Mcp-Session-Id: lL0iEfhTISUQOuIxpoTwLA
< X-Accel-Buffering: no
event: message
data: {"result":{"protocolVersion":"2025-03-26","capabilities":{"logging":{},"tools":{"listChanged":true}},"serverInfo":{"name":"DaJet.Mcp.Server","version":"1.0.2.0"}},"id":1,"jsonrpc":"2.0"}
```

### 4. Подтвердить инициализацию сессии SSE.

Выполнить следующую команду CURL, не забыв подставить нужный заголовок ```Mcp-Session-Id```.

```
curl -v -X POST http://localhost:3000 -H "Mcp-Session-Id: lL0iEfhTISUQOuIxpoTwLA" -d @init-mcp-confirm.json -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream"
```

Ответ MCP-сервера должен выглядеть следующим образом:

```
< HTTP/1.1 202 Accepted
< Content-Length: 0
< Date: Wed, 27 May 2026 15:12:03 GMT
< Server: Kestrel
< Cache-Control: no-cache,no-store
< Content-Encoding: identity
< Mcp-Session-Id: lL0iEfhTISUQOuIxpoTwLA
< X-Accel-Buffering: no
```

### 5. Получить список доступных на MCP-сервере инструментов.

```
curl -v -X POST http://localhost:3000 -H "Mcp-Session-Id: lL0iEfhTISUQOuIxpoTwLA" -d @list-mcp-tools.json -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream"
```

Ответ будет содержать, например, такое описание инструмента ```execute_query```:

```JSON
{
  "result": {
    "tools": [
      {
        "name": "execute_query",
        "description": "Executes a parameterized DaJet Script query to a registered 1C database data source. Returns an array of arbitrary JSON objects. Supports read-only SELECT queries only. Suitable for cross-database reports, and parameterized data access by 1C metadata object names.",
        "inputSchema": {
          "type": "object",
          "properties": {
            "database": {
              "description": "Registered database name",
              "type": "string"
            },
            "script": {
              "description": "SELECT query text",
              "type": "string"
            },
            "parameters": {
              "description": "SELECT query parameters",
              "type": "object"
            }
          },
          "required": [ "database", "script", "parameters" ]
        }
      }
    ]
  },
  "id": 2,
  "jsonrpc": "2.0"
}
```

### 6. Выполнить инструмент ```execute_query``` на MCP-сервере.

```
curl -v -X POST http://localhost:3000 -H "Mcp-Session-Id: lL0iEfhTISUQOuIxpoTwLA" -d @call-mcp-tool.json -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream"
```

После возможной небольшой паузы (инициализация кэша метаданных конфигурации 1С) ответ сервера должен выглядеть следующим образом.

```
< HTTP/1.1 200 OK
< Content-Type: text/event-stream
< Date: Wed, 27 May 2026 15:18:13 GMT
< Server: Kestrel
< Cache-Control: no-cache,no-store
< Content-Encoding: identity
< Transfer-Encoding: chunked
< Mcp-Session-Id: lL0iEfhTISUQOuIxpoTwLA
< X-Accel-Buffering: no
```

Дополнительно должен появиться результат выполнения запроса SELECT в формате JSON.

```
event: message
data: {
  "result": {
    "content": [
      { "type": "text", "text": "[ здесь какие-то данные ]" }
    ],
    "isError": false
  },
  "id": 3,
  "jsonrpc": "2.0"
}
```

**Пример запроса, который AI-агент должен уметь формировать для инструмента ```execute_query```:**

```SQL
SELECT Ссылка, Код, Наименование
  FROM Справочник.Номенклатура
 WHERE Код = @Код
```
**Параметры для запроса передаются примерно в таком виде:**

```JSON
{
   "Код": "333"
}
```

**Пример запроса по протоколу MCP**

```JSON
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "execute_query",
    "arguments": {
      "database": "MS_TEST",
      "script": "SELECT TOP 1 Ссылка, Код, Наименование FROM Справочник.Номенклатура WHERE Код = @Код",
      "attributes": {
        "Код": "333"
      }
    }
  }
}
```

**Поддерживаемые типы данных параметров запроса**

```JSON
{
  "Булево": true,
  "Число": 1234,
  "ДатаВремя": "2026-01-01T12:34:56",
  "Строка": "Это строка",
  "Идентификатор": "1677349A-095F-4488-896F-93425B720FEB",
  "Ссылка": "{333:1677349A-095F-4488-896F-93425B720FEB}"
}
```

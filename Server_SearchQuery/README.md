# MCP Server

This README was created using the C# MCP server project template.
It demonstrates how you can easily create an MCP server using C# and run it as an ASP.NET Core web application.

The MCP server is built as a self-contained application and does not require the .NET runtime to be installed on the target machine.
However, since it is self-contained, it must be built for each target platform separately.
By default, the template is configured to build for:
* `win-x64`
* `win-arm64`
* `osx-arm64`
* `linux-x64`
* `linux-arm64`
* `linux-musl-x64`

If you require more platforms to be supported, update the list of runtime identifiers in the project's `<RuntimeIdentifiers />` element.

## Developing locally

To test this MCP server from source code locally, configure your MCP client or IDE:

```json
{
  "servers": {
    "GraphMcpServer": {
      "type": "http",
      "url": "http://localhost:3001"
    }
  }
}
```

Refer to the VS Code or Visual Studio documentation for more information on configuring and using MCP servers:

- [Use MCP servers in VS Code](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)
- [Use MCP servers in Visual Studio](https://learn.microsoft.com/visualstudio/ide/mcp-servers)

## Features

- **Protocol Version:** MCP 2.0 (`2026-07-28`)
- **Tools:** `find_user`, `user_profile`
- **Resources:** `users://me`, `users://{userId}`
- **Prompts:** `whom_olga_reports_to`, `who_are_olgas_peers`, `direct_reports_of_manager`, `search_users_by_dept_title`, `user_quick_lookup`

## More information

ASP.NET Core MCP servers use the [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) package from the MCP C# SDK. For more information about MCP:

- [Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://spec.modelcontextprotocol.io/)
- [GitHub Organization](https://github.com/modelcontextprotocol)
- [MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/)

# Large Microsoft Graph HTTP MCP 2.0 Server (`C:\MCP\LargeServer`)

Reads `C:\MCP\Csdl\Server_Large.csdl` and deterministically generates MCP 2.0 (`2026-07-28`) tools, resources, prompts, and entity/operation examples across Microsoft 365 workloads:

- **Entities (`EntityType` / `EntitySet` / `Singleton`)**: `User` (`users`, `me`), `Group` (`groups`), `Drive` (`drives`), `DriveItem` (`driveItems`), `Team` (`teams`), `Channel` (`channels`), `Chat` (`chats`), `ChatMessage` (`chatMessages`), `TodoTaskList` (`todoLists`), `TodoTask` (`todoTasks`)
- **Bound OData Operations (`Function` / `Action`)**: `searchDriveItems`, `recentDriveItems`, `deltaChannelMessages`, `sendChannelMessage`, `completeTodoTask`
- **Container Prompts (`@Agent.Prompts`)**: 22 single-resource and cross-workload M365 prompts
- **Entity & Operation Examples (`@Agent.Examples`)**: 28 annotated examples across entities, entity sets, singletons, functions, and actions
- **Endpoint**: `http://localhost:3003/`

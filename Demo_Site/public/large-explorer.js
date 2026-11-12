// ============================================================================
// Large Microsoft Graph MCP 2.0 Explorer (http://localhost:3003)
// Identical Graph Explorer layout with 4 Collapsible Sidebar Sections:
//   1. DISCOVERY - PROTOCOL CORE
//   2. RESOURCES
//   3. TOOLS
//   4. PROMPTS
// ============================================================================

const LARGE_SERVER_URL = 'http://localhost:3003';

function getSelectedLargeServerUrl() {
  const sel = document.getElementById('largeMcpEndpointInput');
  return String(sel?.value || LARGE_SERVER_URL).replace(/\/$/, '');
}

const LARGE_MCP_META = {
  'io.modelcontextprotocol/protocolVersion': '2026-07-28',
  'io.modelcontextprotocol/clientInfo': { name: 'GraphLargeMcpExplorer', version: '2.0.0' },
  'io.modelcontextprotocol/clientCapabilities': {}
};

const LARGE_SAMPLE_QUERIES = [
  {
    section: '1. DISCOVERY - PROTOCOL CORE',
    icon: '⚙️',
    items: [
      {
        id: 'lg-discover',
        badge: 'MCP 2.0',
        badgeClass: 'proto-badge',
        label: 'server/discover',
        method: 'server/discover',
        payload: { jsonrpc: '2.0', id: 1, method: 'server/discover', params: { _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'Protocol Negotiation (10 Entities, 5 Operations, 22 Prompts, 28 Examples)',
        entity: 'MCP 2.0 Core Discovery',
        args: []
      },
      {
        id: 'lg-tools-list',
        badge: 'CORE',
        badgeClass: 'proto-badge',
        label: 'tools/list (59 EDM Tools)',
        method: 'tools/list',
        payload: { jsonrpc: '2.0', id: 2, method: 'tools/list', params: { _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'CSDL EntitySets + NavProps + Bound Functions/Actions -> 59 MCP Tools',
        entity: 'Microsoft.Graph.GraphService',
        args: []
      },
      {
        id: 'lg-prompts-list',
        badge: 'CORE',
        badgeClass: 'proto-badge',
        label: 'prompts/list',
        method: 'prompts/list',
        payload: { jsonrpc: '2.0', id: 3, method: 'prompts/list', params: { _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/$metadata (@Agent.Prompts)',
        entity: 'Microsoft.Graph.GraphService/@Agent.Prompts',
        args: []
      },
      {
        id: 'lg-resources-list',
        badge: 'CORE',
        badgeClass: 'proto-badge',
        label: 'resources/list',
        method: 'resources/list',
        payload: { jsonrpc: '2.0', id: 4, method: 'resources/list', params: { _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'CSDL EntitySets & Singletons -> Direct MCP Resources',
        entity: 'Microsoft.Graph.GraphService',
        args: []
      },
      {
        id: 'lg-resource-templates-list',
        badge: 'LIST',
        badgeClass: 'proto-badge',
        label: 'resources/templates/list',
        method: 'resources/templates/list',
        payload: { jsonrpc: '2.0', id: 5, method: 'resources/templates/list', params: { _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'CSDL Keyed EntitySets -> Parameterized Resource Templates',
        entity: 'Microsoft.Graph.GraphService',
        args: []
      }
    ]
  },
  {
    section: '2. RESOURCES',
    icon: '📦',
    items: [
      {
        id: 'lg-res-me',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../me (Current User)',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 10, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/me', _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/me',
        entity: 'Microsoft.Graph.User (Singleton: me)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/me', isUri: true }]
      },
      {
        id: 'lg-res-user',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../users/{key_id}',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 11, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/users/avery.adams@example.com', _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')",
        entity: 'Microsoft.Graph.User (EntitySet: users)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/users/avery.adams@example.com', isUri: true }]
      },
      {
        id: 'lg-res-groups-all',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../groups (All Groups)',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 12, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/groups', _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/groups',
        entity: 'Microsoft.Graph.Group (EntitySet: groups)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/groups', isUri: true }]
      },
      {
        id: 'lg-res-group',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../groups/{key_id}',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 13, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/groups/11111111-2222-3333-4444-555555555555', _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')",
        entity: 'Microsoft.Graph.Group (EntitySet: groups)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/groups/11111111-2222-3333-4444-555555555555', isUri: true }]
      },
      {
        id: 'lg-res-drives-me',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../drives/drive-avery',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 14, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/drives/drive-avery', _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/drives('drive-avery')",
        entity: 'Microsoft.Graph.Drive (EntitySet: drives)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/drives/drive-avery', isUri: true }]
      },
      {
        id: 'lg-res-files-recent',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../driveItems (All Files)',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 15, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/driveItems', _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/driveItems',
        entity: 'Microsoft.Graph.DriveItem (EntitySet: driveItems)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/driveItems', isUri: true }]
      },
      {
        id: 'lg-res-file-item',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../driveItems/{key_id}',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 16, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/driveItems/file-mcp-poc-docx', _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/driveItems('file-mcp-poc-docx')",
        entity: 'Microsoft.Graph.DriveItem (EntitySet: driveItems)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/driveItems/file-mcp-poc-docx', isUri: true }]
      },
      {
        id: 'lg-res-teams-all',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../teams (All Teams)',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 17, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/teams', _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/teams',
        entity: 'Microsoft.Graph.Team (EntitySet: teams)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/teams', isUri: true }]
      },
      {
        id: 'lg-res-channel-msgs',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../channels/{key_id}',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 18, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/channels/ch-csdl-typespec', _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/channels('ch-csdl-typespec')",
        entity: 'Microsoft.Graph.Channel (EntitySet: channels)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/channels/ch-csdl-typespec', isUri: true }]
      },
      {
        id: 'lg-res-chats-all',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../chats (All Chats)',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 19, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/chats', _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/chats',
        entity: 'Microsoft.Graph.Chat (EntitySet: chats)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/chats', isUri: true }]
      },
      {
        id: 'lg-res-todo-lists',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../todoLists (To Do Lists)',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 20, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/todoLists', _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/todoLists',
        entity: 'Microsoft.Graph.TodoTaskList (EntitySet: todoLists)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/todoLists', isUri: true }]
      },
      {
        id: 'lg-res-todo-tasks',
        badge: 'READ',
        badgeClass: 'res-badge',
        label: 'odata://.../todoTasks (To Do Tasks)',
        method: 'resources/read',
        payload: { jsonrpc: '2.0', id: 21, method: 'resources/read', params: { uri: 'odata://Microsoft.Graph.GraphService/todoTasks', _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/todoTasks',
        entity: 'Microsoft.Graph.TodoTask (EntitySet: todoTasks)',
        args: [{ name: 'uri', label: 'Resource URI', default: 'odata://Microsoft.Graph.GraphService/todoTasks', isUri: true }]
      }
    ]
  },
  {
    section: '3. TOOLS',
    icon: '🛠️',
    items: [
      {
        id: 'lg-tool-search-intent',
        badge: ':3005',
        badgeClass: 'mcp-badge',
        label: 'search_intent ("last user to modify file")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 28, method: 'tools/call', params: { name: 'search_intent', arguments: { query: 'Who was the last user to modify Agent_Protocol_Demo.docx?' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'Step 1 (:3005): Search @Agent.Prompts, @Agent.Examples & CSDL Schema',
        entity: 'LargeServer_SearchQuery (:3005) — Intent Identification',
        args: [{ name: 'query', label: 'Natural Language Question', default: 'Who was the last user to modify Agent_Protocol_Demo.docx?' }]
      },
      {
        id: 'lg-tool-exec-odata',
        badge: ':3005',
        badgeClass: 'mcp-badge',
        label: 'execute_odata_query ("GET driveItems(...)/lastModifiedByUser")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 29, method: 'tools/call', params: { name: 'execute_odata_query', arguments: { odataQuery: "GET driveItems('file-mcp-poc-docx')/lastModifiedByUser?$select=id,displayName,userPrincipalName,jobTitle,department" }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/driveItems('file-mcp-poc-docx')/lastModifiedByUser?$select=id,displayName,userPrincipalName,jobTitle,department",
        entity: 'LargeServer_SearchQuery (:3005) — OData v4.01 Execution',
        args: [{ name: 'odataQuery', label: 'Canonical OData v4.01 Request', default: "GET driveItems('file-mcp-poc-docx')/lastModifiedByUser?$select=id,displayName,userPrincipalName,jobTitle,department" }]
      },
      {
        id: 'lg-tool-my-files',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'list_driveItems (All DriveItems)',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 30, method: 'tools/call', params: { name: 'list_driveItems', arguments: { select: 'id,name,size,webUrl,fileMimeType,lastModifiedDateTime,lastModifiedByDisplayName' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/driveItems?$select=id,name,size,webUrl,fileMimeType,lastModifiedDateTime,lastModifiedByDisplayName',
        entity: 'Microsoft.Graph.DriveItem (EntitySet: driveItems)',
        args: [{ name: 'select', label: '$select', default: 'id,name,size,webUrl,fileMimeType,lastModifiedDateTime,lastModifiedByDisplayName' }]
      },
      {
        id: 'lg-tool-search-files',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'drives_Microsoft_Graph_searchDriveItems ("CSDL")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 31, method: 'tools/call', params: { name: 'drives_Microsoft_Graph_searchDriveItems', arguments: { key_id: 'drive-avery', q: 'CSDL' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/drives('drive-avery')/Microsoft.Graph.searchDriveItems(q='CSDL')",
        entity: 'Bound Function: Microsoft.Graph.searchDriveItems',
        args: [
          { name: 'key_id', label: 'Drive ID (key_id)', default: 'drive-avery' },
          { name: 'q', label: 'Search Keyword (q)', default: 'CSDL' }
        ]
      },
      {
        id: 'lg-tool-file-modifier',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'get_driveItems_lastModifiedByUser ("file-mcp-poc-docx")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 32, method: 'tools/call', params: { name: 'get_driveItems_lastModifiedByUser', arguments: { key_id: 'file-mcp-poc-docx', select: 'id,displayName,userPrincipalName,jobTitle,department' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/driveItems('file-mcp-poc-docx')/lastModifiedByUser?$select=id,displayName,userPrincipalName,jobTitle,department",
        entity: 'NavigationProperty: DriveItem/lastModifiedByUser',
        args: [
          { name: 'key_id', label: 'DriveItem ID (key_id)', default: 'file-mcp-poc-docx' },
          { name: 'select', label: '$select', default: 'id,displayName,userPrincipalName,jobTitle,department' }
        ]
      },
      {
        id: 'lg-tool-list-teams',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'get_me_joinedTeams ("me")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 33, method: 'tools/call', params: { name: 'get_me_joinedTeams', arguments: { expand: 'channels' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/me/joinedTeams?$expand=channels',
        entity: 'Microsoft.Graph.Team (Singleton Nav: me/joinedTeams)',
        args: [{ name: 'expand', label: '$expand', default: 'channels' }]
      },
      {
        id: 'lg-tool-channel-messages',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'channels_Microsoft_Graph_deltaChannelMessages ("ch-csdl-typespec")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 34, method: 'tools/call', params: { name: 'channels_Microsoft_Graph_deltaChannelMessages', arguments: { key_id: 'ch-csdl-typespec' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/channels('ch-csdl-typespec')/Microsoft.Graph.deltaChannelMessages()",
        entity: 'Bound Function: Microsoft.Graph.deltaChannelMessages',
        args: [{ name: 'key_id', label: 'Channel ID (key_id)', default: 'ch-csdl-typespec' }]
      },
      {
        id: 'lg-tool-send-channel-msg',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'channels_Microsoft_Graph_sendChannelMessage ("ch-mcp-20")',
        method: 'tools/call',
        payload: {
          jsonrpc: '2.0',
          id: 35,
          method: 'tools/call',
          params: {
            name: 'channels_Microsoft_Graph_sendChannelMessage',
            arguments: {
              key_id: 'ch-mcp-20',
              subject: 'MCP 2.0 LargeServer Update',
              bodyContent: 'Verified all 59 CSDL-transformed tools and 21 resources on port 3003.'
            },
            _meta: { ...LARGE_MCP_META }
          }
        },
        graphTarget: "POST https://graph.microsoft.com/v1.0/channels('ch-mcp-20')/Microsoft.Graph.sendChannelMessage",
        entity: 'Bound Action: Microsoft.Graph.sendChannelMessage',
        args: [
          { name: 'key_id', label: 'Channel ID (key_id)', default: 'ch-mcp-20' },
          { name: 'subject', label: 'Subject', default: 'MCP 2.0 LargeServer Update' },
          { name: 'bodyContent', label: 'Message Body', default: 'Verified all 59 CSDL-transformed tools and 21 resources on port 3003.' }
        ]
      },
      {
        id: 'lg-tool-list-chats',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'get_me_chats ("me")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 36, method: 'tools/call', params: { name: 'get_me_chats', arguments: { expand: 'members,messages' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/me/chats?$expand=members,messages',
        entity: 'Microsoft.Graph.Chat (Singleton Nav: me/chats)',
        args: [{ name: 'expand', label: '$expand', default: 'members,messages' }]
      },
      {
        id: 'lg-tool-list-todo',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'list_todoTasks ("status ne completed")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 37, method: 'tools/call', params: { name: 'list_todoTasks', arguments: { filter: "status ne 'completed'" }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/todoTasks?$filter=status ne 'completed'",
        entity: 'Microsoft.Graph.TodoTask (EntitySet: todoTasks)',
        args: [{ name: 'filter', label: '$filter', default: "status ne 'completed'" }]
      },
      {
        id: 'lg-tool-complete-todo',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'todoTasks_Microsoft_Graph_completeTodoTask ("task-101")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 38, method: 'tools/call', params: { name: 'todoTasks_Microsoft_Graph_completeTodoTask', arguments: { key_id: 'task-101' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "POST https://graph.microsoft.com/v1.0/todoTasks('task-101')/Microsoft.Graph.completeTodoTask",
        entity: 'Bound Action: Microsoft.Graph.completeTodoTask',
        args: [{ name: 'key_id', label: 'Task ID (key_id)', default: 'task-101' }]
      },
      {
        id: 'lg-tool-find-user',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'list_users ("startswith(displayName,\'Avery\')")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 39, method: 'tools/call', params: { name: 'list_users', arguments: { filter: "startswith(displayName,'Avery')" }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'Avery')",
        entity: 'Microsoft.Graph.User (EntitySet: users)',
        args: [{ name: 'filter', label: '$filter', default: "startswith(displayName,'Avery')" }]
      },
      {
        id: 'lg-tool-user-profile',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'get_users ("avery.adams@example.com")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 40, method: 'tools/call', params: { name: 'get_users', arguments: { key_id: 'avery.adams@example.com', expand: 'manager,directReports' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')?$expand=manager,directReports",
        entity: 'Microsoft.Graph.User (EntitySet: users)',
        args: [
          { name: 'key_id', label: 'User ID or UPN (key_id)', default: 'avery.adams@example.com' },
          { name: 'expand', label: '$expand', default: 'manager,directReports' }
        ]
      },
      {
        id: 'lg-tool-find-group',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'list_groups ("startswith(displayName,\'Identity\')")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 41, method: 'tools/call', params: { name: 'list_groups', arguments: { filter: "startswith(displayName,'Identity')" }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/groups?$filter=startswith(displayName,'Identity')",
        entity: 'Microsoft.Graph.Group (EntitySet: groups)',
        args: [{ name: 'filter', label: '$filter', default: "startswith(displayName,'Identity')" }]
      },
      {
        id: 'lg-tool-group-details',
        badge: 'CALL',
        badgeClass: 'mcp-badge',
        label: 'get_groups ("11111111-2222-3333-4444-555555555555")',
        method: 'tools/call',
        payload: { jsonrpc: '2.0', id: 42, method: 'tools/call', params: { name: 'get_groups', arguments: { key_id: '11111111-2222-3333-4444-555555555555', expand: 'members,owners' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')?$expand=members,owners",
        entity: 'Microsoft.Graph.Group (EntitySet: groups)',
        args: [
          { name: 'key_id', label: 'Group GUID (key_id)', default: '11111111-2222-3333-4444-555555555555' },
          { name: 'expand', label: '$expand', default: 'members,owners' }
        ]
      }
    ]
  },
  {
    section: '4. PROMPTS',
    icon: '💬',
    items: [
      {
        id: 'lg-prompt-onedrive',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'GET my files in OneDrive',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 50, method: 'prompts/get', params: { name: 'my_onedrive_files', arguments: { userId: 'me' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/me/drive/root/children',
        entity: 'GraphService/@Agent.Prompts[my_onedrive_files]',
        args: [{ name: 'userId', label: 'User ID or me', default: 'me' }]
      },
      {
        id: 'lg-prompt-last-modifier',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'GET last user to modify a file',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 51, method: 'prompts/get', params: { name: 'last_user_to_modify_file', arguments: { fileName: 'Agent_Protocol_Demo.docx' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/driveItems('file-mcp-poc-docx')/lastModifiedByUser",
        entity: 'GraphService/@Agent.Prompts[last_user_to_modify_file]',
        args: [{ name: 'fileName', label: 'File Name', default: 'Agent_Protocol_Demo.docx' }]
      },
      {
        id: 'lg-prompt-search-files',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'Search OneDrive & SharePoint files',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 52, method: 'prompts/get', params: { name: 'search_files_by_keyword', arguments: { query: 'CSDL' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/drives('drive-avery')/Microsoft.Graph.searchDriveItems(q='CSDL')",
        entity: 'GraphService/@Agent.Prompts[search_files_by_keyword]',
        args: [{ name: 'query', label: 'Keyword', default: 'CSDL' }]
      },
      {
        id: 'lg-prompt-joined-teams',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'List my Microsoft Teams & channels',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 53, method: 'prompts/get', params: { name: 'my_joined_teams_and_channels', arguments: { userId: 'me' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/me/joinedTeams?$expand=channels',
        entity: 'GraphService/@Agent.Prompts[my_joined_teams_and_channels]',
        args: [{ name: 'userId', label: 'User ID or me', default: 'me' }]
      },
      {
        id: 'lg-prompt-teams-channels',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'Read messages in a Teams channel',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 54, method: 'prompts/get', params: { name: 'team_channel_messages', arguments: { teamName: 'Platform Engineering', channelName: 'CSDL & TypeSpec' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/channels('ch-csdl-typespec')/messages?$top=20",
        entity: 'GraphService/@Agent.Prompts[team_channel_messages]',
        args: [
          { name: 'teamName', label: 'Team Name', default: 'Platform Engineering' },
          { name: 'channelName', label: 'Channel Name', default: 'CSDL & TypeSpec' }
        ]
      },
      {
        id: 'lg-prompt-chats',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'List recent 1:1 and group chats',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 55, method: 'prompts/get', params: { name: 'list_recent_chats', arguments: { chatType: 'all' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/me/chats?$expand=members,messages($top=5)',
        entity: 'GraphService/@Agent.Prompts[list_recent_chats]',
        args: [{ name: 'chatType', label: 'Chat Type', default: 'all' }]
      },
      {
        id: 'lg-prompt-todo',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'List open Microsoft To Do tasks',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 56, method: 'prompts/get', params: { name: 'my_pending_todo_tasks', arguments: { importance: 'high' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/me/todoLists?$expand=tasks($filter=status ne 'completed')",
        entity: 'GraphService/@Agent.Prompts[my_pending_todo_tasks]',
        args: [{ name: 'importance', label: 'Importance', default: 'high' }]
      },
      {
        id: 'lg-prompt-unified-groups',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: "GET Microsoft 365 groups I'm a member of",
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 57, method: 'prompts/get', params: { name: 'my_m365_unified_groups', arguments: { userId: 'me' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'GET https://graph.microsoft.com/v1.0/me/memberOf?$select=id,displayName,description,mail,groupTypes,visibility',
        entity: 'GraphService/@Agent.Prompts[my_m365_unified_groups]',
        args: [{ name: 'userId', label: 'User ID or me', default: 'me' }]
      },
      {
        id: 'lg-prompt-360-brief',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'Prepare 360 collaboration brief (Join)',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 58, method: 'prompts/get', params: { name: 'meeting_attendee_collaboration_brief', arguments: { colleagueName: 'Jordan Lee' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'Cross-Resource Join: users + driveItems + chats + todoTasks',
        entity: 'GraphService/@Agent.Prompts[meeting_attendee_collaboration_brief]',
        args: [{ name: 'colleagueName', label: 'Colleague Name', default: 'Jordan Lee' }]
      },
      {
        id: 'lg-prompt-collab-synthesis',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'Correlate Teams, Files & To Do (Join)',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 59, method: 'prompts/get', params: { name: 'team_channel_files_and_action_items', arguments: { topic: 'MCP' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'Cross-Resource Join: teams/channels + drives/searchDriveItems + todoTasks',
        entity: 'GraphService/@Agent.Prompts[team_channel_files_and_action_items]',
        args: [{ name: 'topic', label: 'Topic Keyword', default: 'MCP' }]
      },
      {
        id: 'lg-prompt-reports-to',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'Whom Avery reports to?',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 60, method: 'prompts/get', params: { name: 'whom_olga_reports_to', arguments: { userName: 'Avery Adams' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')/manager",
        entity: 'GraphService/@Agent.Prompts[whom_olga_reports_to]',
        args: [{ name: 'userName', label: 'User Name', default: 'Avery Adams' }]
      },
      {
        id: 'lg-prompt-peers-in-group',
        badge: 'PROMPT',
        badgeClass: 'prompt-badge',
        label: 'Which user peers belong to a group?',
        method: 'prompts/get',
        payload: { jsonrpc: '2.0', id: 61, method: 'prompts/get', params: { name: 'user_peers_in_group', arguments: { userName: 'Avery Adams', groupName: 'Platform Engineering' }, _meta: { ...LARGE_MCP_META } } },
        graphTarget: 'Cross-Resource Join: users/manager/directReports + groups/members',
        entity: 'GraphService/@Agent.Prompts[user_peers_in_group]',
        args: [
          { name: 'userName', label: 'User Name', default: 'Avery Adams' },
          { name: 'groupName', label: 'Group Name', default: 'Platform Engineering' }
        ]
      }
    ]
  }
];

let currentLargeQuery = LARGE_SAMPLE_QUERIES[2].items[0];
let cachedLargeCsdl = null;
let cachedLargeExamples = [];

function escapeLargeHtml(str) {
  return String(str ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function buildLargeSidebar() {
  const container = document.getElementById('largeExplorerSidebar');
  if (!container) return;

  container.innerHTML = LARGE_SAMPLE_QUERIES.map(sec => `
    <details class="sidebar-category collapsible-sidebar-cat" open>
      <summary class="category-header collapsible-cat-summary">
        <span style="display:flex;align-items:center;gap:6px;">
          <span class="cat-chevron">▸</span>
          <span class="cat-icon">${sec.icon}</span>
          <span class="cat-title">${escapeLargeHtml(sec.section)}</span>
        </span>
        <span class="cat-count-pill">${sec.items.length}</span>
      </summary>
      <ul class="query-list" style="padding:4px 6px;">
        ${sec.items.map(item => `
          <li class="query-item ${item.id === currentLargeQuery.id ? 'active' : ''}" data-large-query="${item.id}">
            <span class="http-badge ${item.badgeClass}">${escapeLargeHtml(item.badge)}</span>
            <span class="query-label">${escapeLargeHtml(item.label)}</span>
          </li>
        `).join('')}
      </ul>
    </details>
  `).join('');

  container.querySelectorAll('[data-large-query]').forEach(el => {
    el.addEventListener('click', () => {
      const qid = el.getAttribute('data-large-query');
      for (const sec of LARGE_SAMPLE_QUERIES) {
        const found = sec.items.find(i => i.id === qid);
        if (found) {
          selectLargeQuery(found, true);
          break;
        }
      }
    });
  });

  const expandBtn = document.getElementById('btnLargeExpandAll');
  const collapseBtn = document.getElementById('btnLargeCollapseAll');
  if (expandBtn) {
    expandBtn.addEventListener('click', () => {
      container.querySelectorAll('details.collapsible-sidebar-cat').forEach(d => { d.open = true; });
    });
  }
  if (collapseBtn) {
    collapseBtn.addEventListener('click', () => {
      container.querySelectorAll('details.collapsible-sidebar-cat').forEach(d => { d.open = false; });
    });
  }
}

function selectLargeQuery(item, autoExecute = false) {
  currentLargeQuery = JSON.parse(JSON.stringify(item));
  document.querySelectorAll('[data-large-query]').forEach(el => {
    el.classList.toggle('active', el.getAttribute('data-large-query') === item.id);
  });

  const graphUrl = document.getElementById('largeUnderlyingGraphUrl');
  const entityChip = document.getElementById('largeTargetEntityChip');
  const editor = document.getElementById('largeRequestPayloadEditor');
  const hdrMethod = document.getElementById('largeHdrMcpMethod');
  const hdrName = document.getElementById('largeHdrMcpName');

  if (graphUrl) graphUrl.textContent = item.graphTarget;
  if (entityChip) entityChip.textContent = item.entity;
  if (editor) editor.value = JSON.stringify(currentLargeQuery.payload, null, 2);

  const mcpName = currentLargeQuery.payload?.params?.name || currentLargeQuery.payload?.params?.uri || 'N/A';
  if (hdrMethod) hdrMethod.textContent = item.method;
  if (hdrName) hdrName.textContent = mcpName;

  renderLargeArgsForm();
  if (autoExecute) executeLargeQuery();
}

function renderLargeArgsForm() {
  const form = document.getElementById('largeDynamicArgsForm');
  if (!form) return;
  const args = currentLargeQuery.args || [];
  if (args.length === 0) {
    form.innerHTML = `<p class="form-hint">No parameters required for <code>${escapeLargeHtml(currentLargeQuery.method)}</code>.</p>`;
    return;
  }

  form.innerHTML = args.map(arg => `
    <div class="arg-field-group" style="margin-bottom:10px;">
      <label class="arg-field-label" style="display:block;font-size:12px;font-weight:600;margin-bottom:4px;">${escapeLargeHtml(arg.label)} (<code>${escapeLargeHtml(arg.name)}</code>)</label>
      <input type="text" class="arg-field-input" data-large-arg="${escapeLargeHtml(arg.name)}" value="${escapeLargeHtml(arg.default)}" style="width:100%;padding:6px 8px;border:1px solid #c8c6c4;border-radius:4px;font-size:12px;">
    </div>
  `).join('');

  form.querySelectorAll('[data-large-arg]').forEach(inp => {
    inp.addEventListener('input', () => {
      const name = inp.getAttribute('data-large-arg');
      const val = inp.value;
      const argDef = args.find(a => a.name === name);
      if (argDef && argDef.isUri) {
        currentLargeQuery.payload.params.uri = val;
      } else if (currentLargeQuery.payload?.params?.arguments) {
        currentLargeQuery.payload.params.arguments[name] = val;
      }
      const editor = document.getElementById('largeRequestPayloadEditor');
      if (editor) editor.value = JSON.stringify(currentLargeQuery.payload, null, 2);
    });
  });
}

async function executeLargeQuery() {
  const editor = document.getElementById('largeRequestPayloadEditor');
  const statusContainer = document.getElementById('largeResponseStatusBadge');
  const statusBadge = document.getElementById('largeRespStatusBadge');
  const latencyBadge = document.getElementById('largeRespLatencyBadge');
  const jsonView = document.getElementById('largeResponseJsonView');
  const cardContainer = document.getElementById('largeRenderedCardContainer');
  const resHeadersTable = document.getElementById('largeResHeadersTable');

  if (!editor) return;
  let body = editor.value;

  try {
    const targetServer = getSelectedLargeServerUrl();
    const res = await fetch('/mcp-proxy', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Target-Url': targetServer
      },
      body
    });
    const audit = await res.json();
    const ok = (audit.statusCode || 200) < 400 && !audit.jsonRpcResponse?.error && !audit.jsonRpcResponse?.result?.isError;
    if (statusContainer) {
      statusContainer.classList.toggle('status-ok', ok);
      statusContainer.classList.toggle('status-err', !ok);
    }
    if (statusBadge) {
      statusBadge.textContent = `${audit.statusCode || 200} ${audit.statusText || 'OK'}`;
    }
    if (latencyBadge) latencyBadge.textContent = `${audit.elapsedMs || 0} ms`;
    if (jsonView) jsonView.textContent = JSON.stringify(audit.jsonRpcResponse || audit, null, 2);

    if (resHeadersTable) {
      const tbody = resHeadersTable.querySelector('tbody');
      const headers = audit.responseHeaders || {};
      const entries = Object.entries(headers);
      if (tbody) {
        tbody.innerHTML = entries.length
          ? entries.map(([k, v]) => `<tr><td>${escapeLargeHtml(k)}</td><td>${escapeLargeHtml(v)}</td></tr>`).join('')
          : `<tr><td colspan="2" class="empty-cell">No response headers returned.</td></tr>`;
      }
    }

    renderLargeResultCard(audit.jsonRpcResponse || audit, cardContainer);
    loadLargeTraces();
  } catch (err) {
    if (cardContainer) {
      cardContainer.innerHTML = `<div class="org-card" style="color:#a4262c;">Failed to query Large MCP Server on ${LARGE_SERVER_URL}: ${escapeLargeHtml(err.message)}</div>`;
    }
  }
}

async function loadLargeTraces() {
  const list = document.getElementById('largeTraceList');
  if (!list) return;
  try {
    const res = await fetch('/api/traces', { headers: { 'X-Target-Url': getSelectedLargeServerUrl() } });
    if (!res.ok) return;
    const traces = await res.json();
    list.innerHTML = traces.slice().reverse().map(t => `
      <div class="trace-item" style="border:1px solid #edebe9;border-radius:4px;padding:8px;margin-bottom:6px;background:#fff;font-size:12px;">
        <div style="display:flex;justify-content:space-between;align-items:center;">
          <span><strong>${escapeLargeHtml(t.method)}</strong> <code>${escapeLargeHtml(t.url)}</code></span>
          <span class="pill-badge ${t.statusCode < 300 ? 'pill-mcp-spec' : 'pill-annotation'}">HTTP ${escapeLargeHtml(t.statusCode)}</span>
        </div>
        <div style="color:#605e5c;font-size:11px;margin-top:4px;">${escapeLargeHtml(t.responseSummary || '')}</div>
      </div>
    `).join('');
  } catch (_) {}
}

function renderLargeResultCard(rpcResp, container) {
  if (!container) return;
  const result = rpcResp?.result;
  if (!result) {
    container.innerHTML = `<pre class="code-viewer">${escapeLargeHtml(JSON.stringify(rpcResp, null, 2))}</pre>`;
    return;
  }

  // 1. Prompt response (messages)
  if (Array.isArray(result.messages)) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;">
          <strong style="font-size:14px;color:#6a1b9a;">@Agent.Prompts Rendered Workflow Template</strong>
          <span class="pill-badge pill-annotation">Server_Large.csdl</span>
        </div>
        <p style="font-size:12px;color:#484644;margin-bottom:10px;">${escapeLargeHtml(result.description || '')}</p>
        ${result.messages.map(m => `
          <div style="background:#faf5ff;border:1px solid #dcb5ff;border-radius:6px;padding:10px;font-size:12.5px;line-height:1.5;">
            <div><strong style="color:#6a1b9a;">Role:</strong> <code>${escapeLargeHtml(m.role)}</code></div>
            <div style="margin-top:6px;white-space:pre-wrap;">${escapeLargeHtml(m.content?.text || '')}</div>
          </div>
        `).join('')}
      </div>
    `;
    return;
  }

  // 2. Tools list / Prompts list / Resources list
  if (Array.isArray(result.tools)) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 8px 0;color:#005a9e;">Projected MCP Tools (${result.tools.length})</h4>
        <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:8px;">
          ${result.tools.map(t => `
            <div style="border:1px solid #e1dfdd;border-radius:6px;padding:8px;background:#fff;">
              <div><code class="kw-mcp">${escapeLargeHtml(t.name)}</code></div>
              <div style="font-weight:600;font-size:12px;margin-top:2px;">${escapeLargeHtml(t.title || '')}</div>
              <div style="font-size:11.5px;color:#605e5c;margin-top:3px;">${escapeLargeHtml(t.description || '')}</div>
            </div>
          `).join('')}
        </div>
      </div>
    `;
    return;
  }

  if (Array.isArray(result.prompts)) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 8px 0;color:#6a1b9a;">Projected Container @Agent.Prompts (${result.prompts.length})</h4>
        <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:8px;">
          ${result.prompts.map(p => `
            <div style="border:1px solid #dcb5ff;border-radius:6px;padding:8px;background:#faf5ff;">
              <div><code class="kw-annot">${escapeLargeHtml(p.name)}</code></div>
              <div style="font-weight:600;font-size:12px;margin-top:2px;">${escapeLargeHtml(p.title || '')}</div>
              <div style="font-size:11.5px;color:#484644;margin-top:3px;">${escapeLargeHtml(p.description || '')}</div>
            </div>
          `).join('')}
        </div>
      </div>
    `;
    return;
  }

  if (Array.isArray(result.resources) || Array.isArray(result.resourceTemplates)) {
    const items = result.resources || result.resourceTemplates || [];
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 8px 0;color:#008272;">Projected MCP Resources &amp; Templates (${items.length})</h4>
        <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:8px;">
          ${items.map(r => `
            <div style="border:1px solid #e1dfdd;border-radius:6px;padding:8px;background:#fff;">
              <div><code class="kw-odata">${escapeLargeHtml(r.uri || r.uriTemplate)}</code></div>
              <div style="font-weight:600;font-size:12px;margin-top:2px;">${escapeLargeHtml(r.title || r.name || '')}</div>
              <div style="font-size:11.5px;color:#605e5c;margin-top:3px;">${escapeLargeHtml(r.description || '')}</div>
            </div>
          `).join('')}
        </div>
      </div>
    `;
    return;
  }

  // 3. Parse tool / resource text content
  let text = result.content?.[0]?.text || result.contents?.[0]?.text;
  let data = result;
  if (text) {
    try { data = JSON.parse(text); } catch (_) {}
  }
  if (data && data.data && data.uri) data = data.data;

  // Render OneDrive / Files
  const fileItems = data?.items || data?.Items;
  if (Array.isArray(fileItems)) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:10px;">
          <h4 style="margin:0;color:#005a9e;">📁 OneDrive &amp; SharePoint Files (${fileItems.length})</h4>
          ${data.name ? `<span class="pill-badge pill-mcp-spec">${escapeLargeHtml(data.name)}</span>` : ''}
        </div>
        <div style="display:flex;flex-direction:column;gap:8px;">
          ${fileItems.map(f => `
            <div style="border:1px solid #e1dfdd;border-radius:6px;padding:10px;background:#fff;display:flex;justify-content:space-between;align-items:center;gap:10px;">
              <div>
                <div style="font-weight:600;font-size:13px;color:#1b1a19;">${f.isFolder ? '📂' : '📄'} ${escapeLargeHtml(f.name)}</div>
                <div style="font-size:11.5px;color:#605e5c;margin-top:2px;">
                  Modified by <strong>${escapeLargeHtml(f.lastModifiedByDisplayName || 'Unknown')}</strong> (${escapeLargeHtml(f.lastModifiedByUpn || '')})
                  &bull; ${f.size ? `${(f.size / 1024).toFixed(1)} KB` : 'Folder'}
                </div>
              </div>
              <span class="pill-badge pill-annotation">${escapeLargeHtml(f.id)}</span>
            </div>
          `).join('')}
        </div>
      </div>
    `;
    return;
  }

  // Render Single DriveItem with lastModifiedByUser
  if (data && data.lastModifiedByUser && data.name) {
    const u = data.lastModifiedByUser;
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 8px 0;color:#005a9e;">📄 DriveItem &rarr; lastModifiedByUser</h4>
        <div style="border:1px solid #e1dfdd;border-radius:6px;padding:10px;margin-bottom:10px;background:#f8fbff;">
          <div><strong>File:</strong> ${escapeLargeHtml(data.name)} (<code>${escapeLargeHtml(data.id)}</code>)</div>
          <div style="font-size:12px;color:#605e5c;margin-top:3px;">Last Modified: ${escapeLargeHtml(data.lastModifiedDateTime)}</div>
        </div>
        <div style="border:1px solid #c7e0f4;border-radius:6px;padding:10px;background:#fff;">
          <div style="font-weight:700;color:#005a9e;">👤 ${escapeLargeHtml(u.displayName)}</div>
          <div style="font-size:12px;">${escapeLargeHtml(u.jobTitle)} &bull; ${escapeLargeHtml(u.department)}</div>
          <div style="font-size:12px;color:#605e5c;">${escapeLargeHtml(u.userPrincipalName)} &bull; ${escapeLargeHtml(u.officeLocation)}</div>
        </div>
      </div>
    `;
    return;
  }

  // Render Teams & Channels
  if (Array.isArray(data?.teams)) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 10px 0;color:#005a9e;">💬 Joined Microsoft Teams (${data.teams.length})</h4>
        ${data.teams.map(t => `
          <div style="border:1px solid #c7e0f4;border-radius:6px;padding:10px;margin-bottom:10px;background:#fff;">
            <div style="display:flex;justify-content:space-between;align-items:center;">
              <strong style="font-size:13.5px;color:#005a9e;">${escapeLargeHtml(t.displayName)}</strong>
              <span class="pill-badge pill-mcp-spec">${escapeLargeHtml(t.visibility)}</span>
            </div>
            <div style="font-size:12px;color:#605e5c;margin:4px 0 8px 0;">${escapeLargeHtml(t.description || '')}</div>
            <div style="display:flex;flex-direction:column;gap:6px;">
              ${(t.channels || []).map(ch => `
                <div style="background:#f5f9ff;border:1px solid #d0e7ff;border-radius:4px;padding:6px 8px;font-size:12px;">
                  <strong># ${escapeLargeHtml(ch.displayName)}</strong> (<code>${escapeLargeHtml(ch.id)}</code>) &mdash; ${escapeLargeHtml(ch.description || '')}
                  <span style="float:right;color:#005a9e;font-weight:600;">${(ch.messages || []).length} msgs</span>
                </div>
              `).join('')}
            </div>
          </div>
        `).join('')}
      </div>
    `;
    return;
  }

  // Render Channel Messages
  if (data && Array.isArray(data.messages) && data.displayName) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 6px 0;color:#005a9e;"># ${escapeLargeHtml(data.displayName)} (${data.messages.length} Messages)</h4>
        <div style="font-size:12px;color:#605e5c;margin-bottom:10px;">${escapeLargeHtml(data.description || '')}</div>
        <div style="display:flex;flex-direction:column;gap:8px;">
          ${data.messages.map(m => `
            <div style="border:1px solid #e1dfdd;border-left:4px solid ${m.importance === 'high' ? '#d83b01' : '#0078d4'};border-radius:6px;padding:10px;background:#fff;">
              <div style="display:flex;justify-content:space-between;align-items:center;">
                <strong>${escapeLargeHtml(m.subject || 'Channel Message')}</strong>
                <span class="pill-badge ${m.importance === 'high' ? 'pill-annotation' : 'pill-mcp-spec'}">${escapeLargeHtml(m.importance)}</span>
              </div>
              <div style="font-size:12px;color:#323130;margin:6px 0;">${escapeLargeHtml(m.bodyContent)}</div>
              <div style="font-size:11px;color:#605e5c;">From: <strong>${escapeLargeHtml(m.fromDisplayName)}</strong> (${escapeLargeHtml(m.fromUpn)}) &bull; ${escapeLargeHtml(m.createdDateTime)}</div>
            </div>
          `).join('')}
        </div>
      </div>
    `;
    return;
  }

  // Render Chats
  if (Array.isArray(data?.chats)) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 10px 0;color:#005a9e;">🗨️ Recent 1:1 &amp; Group Chats (${data.chats.length})</h4>
        ${data.chats.map(c => `
          <div style="border:1px solid #e1dfdd;border-radius:6px;padding:10px;margin-bottom:10px;background:#fff;">
            <div style="display:flex;justify-content:space-between;align-items:center;">
              <strong>${escapeLargeHtml(c.topic || 'Chat')}</strong>
              <span class="pill-badge pill-mcp-spec">${escapeLargeHtml(c.chatType)}</span>
            </div>
            <div style="font-size:11.5px;color:#605e5c;margin:4px 0 8px 0;">
              Members: ${(c.members || []).map(m => escapeLargeHtml(m.displayName)).join(', ')}
            </div>
            ${(c.messages || []).map(m => `
              <div style="background:#f8f8f8;border-radius:4px;padding:6px 8px;margin-top:4px;font-size:12px;">
                <strong>${escapeLargeHtml(m.fromDisplayName)}:</strong> ${escapeLargeHtml(m.bodyContent)}
              </div>
            `).join('')}
          </div>
        `).join('')}
      </div>
    `;
    return;
  }

  // Render To Do Task Lists
  if (Array.isArray(data?.todoLists)) {
    container.innerHTML = `
      <div class="org-card" style="padding:14px;">
        <h4 style="margin:0 0 10px 0;color:#005a9e;">✅ Microsoft To Do Task Lists (${data.todoLists.length})</h4>
        ${data.todoLists.map(list => `
          <div style="border:1px solid #c7e0f4;border-radius:6px;padding:10px;margin-bottom:10px;background:#fff;">
            <div style="font-weight:700;color:#005a9e;margin-bottom:6px;">📋 ${escapeLargeHtml(list.displayName)}</div>
            <div style="display:flex;flex-direction:column;gap:6px;">
              ${(list.tasks || []).map(t => `
                <div style="border:1px solid #e1dfdd;border-left:4px solid ${t.status === 'completed' ? '#107c41' : '#d83b01'};border-radius:4px;padding:8px;background:#fafafa;">
                  <div style="display:flex;justify-content:space-between;align-items:center;">
                    <strong>${t.status === 'completed' ? '☑️' : '⬜'} ${escapeLargeHtml(t.title)}</strong>
                    <span class="pill-badge ${t.importance === 'high' ? 'pill-annotation' : 'pill-mcp-spec'}">${escapeLargeHtml(t.status)} &bull; ${escapeLargeHtml(t.importance)}</span>
                  </div>
                  <div style="font-size:11.5px;color:#605e5c;margin-top:3px;">${escapeLargeHtml(t.bodyPreview || '')}</div>
                  <div style="font-size:11px;color:#605e5c;margin-top:3px;">ID: <code>${escapeLargeHtml(t.id)}</code> &bull; Assigned: ${escapeLargeHtml(t.assignedToUpn || '')}</div>
                </div>
              `).join('')}
            </div>
          </div>
        `).join('')}
      </div>
    `;
    return;
  }

  container.innerHTML = `<pre class="code-viewer">${escapeLargeHtml(JSON.stringify(data, null, 2))}</pre>`;
}

async function loadLargeMetadataAndExamples() {
  try {
    const targetServer = getSelectedLargeServerUrl();
    const [csdlRes, exRes, healthRes] = await Promise.all([
      fetch('/api/csdl', { headers: { 'X-Target-Url': targetServer } }),
      fetch('/api/examples', { headers: { 'X-Target-Url': targetServer } }),
      fetch('/health', { headers: { 'X-Target-Url': targetServer } })
    ]);
    if (csdlRes.ok) {
      cachedLargeCsdl = await csdlRes.json();
      const csdlView = document.getElementById('largeCsdlCodeView');
      if (csdlView) csdlView.textContent = cachedLargeCsdl.csdlXml || '';
    }
    if (exRes.ok) {
      cachedLargeExamples = await exRes.json();
      renderLargeExamplesTable();
    }
    if (healthRes.ok) {
      const h = await healthRes.json();
      const summaryEl = document.getElementById('largeProjectionSummary');
      if (summaryEl) {
        summaryEl.innerHTML = `Active Server: <code>${escapeLargeHtml(h.mcpEndpoint)}</code> &bull; CSDL: <code>${escapeLargeHtml(h.csdlPath)}</code> &bull; <strong>${h.entityTypesCount || 10} EntityTypes</strong> &bull; <strong>${h.operationsCount || 5} Bound Operations</strong> &bull; <strong>${(h.capabilities?.tools || []).length} Tools</strong> &bull; <strong>${(h.capabilities?.resources || []).length} Resources</strong> &bull; <strong>${(h.capabilities?.prompts || []).length} Prompts</strong> &bull; <strong>${h.examplesCount ?? 0} @Agent.Examples</strong>`;
      }
    }
  } catch (_) {}
}

function renderLargeExamplesTable() {
  const container = document.getElementById('largeExamplesTableContainer');
  if (!container) return;
  container.innerHTML = `
    <div style="margin-bottom:8px;font-size:12px;color:#484644;">
      <strong>28 Entity &amp; Operation <code>@Agent.Examples</code> Annotations</strong> across <code>EntityType</code>, <code>EntitySet</code>, <code>Singleton</code>, <code>Function</code>, and <code>Action</code> in <code>Server_Large.csdl</code>:
    </div>
    <table class="comparison-table" style="width:100%;font-size:12px;">
      <thead>
        <tr>
          <th>Target Kind</th>
          <th>CSDL Element</th>
          <th>Natural Language Utterance</th>
          <th>Canonical OData v4.01 Request</th>
        </tr>
      </thead>
      <tbody>
        ${cachedLargeExamples.map(ex => `
          <tr>
            <td><span class="pill-badge pill-mcp-spec">${escapeLargeHtml(ex.targetKind)}</span></td>
            <td><code>${escapeLargeHtml(ex.targetName)}</code></td>
            <td><strong>${escapeLargeHtml(ex.utterance)}</strong><div style="color:#605e5c;font-size:11px;">${escapeLargeHtml(ex.description)}</div></td>
            <td><code class="kw-odata">${escapeLargeHtml(ex.request)}</code></td>
          </tr>
        `).join('')}
      </tbody>
    </table>
  `;
}

function initLargeExplorer() {
  buildLargeSidebar();

  // Request subtab switching
  document.querySelectorAll('[data-large-req-tab]').forEach(btn => {
    btn.addEventListener('click', () => {
      const target = btn.getAttribute('data-large-req-tab');
      document.querySelectorAll('[data-large-req-tab]').forEach(b => b.classList.toggle('active', b === btn));
      ['payload', 'headers', 'args', 'mapping'].forEach(t => {
        const el = document.getElementById(`large-req-${t}`);
        if (el) el.classList.toggle('active', t === target);
      });
    });
  });

  // Response subtab switching
  document.querySelectorAll('[data-large-res-tab]').forEach(btn => {
    btn.addEventListener('click', () => {
      const target = btn.getAttribute('data-large-res-tab');
      document.querySelectorAll('[data-large-res-tab]').forEach(b => b.classList.toggle('active', b === btn));
      ['json', 'card', 'headers', 'trace', 'examples', 'csdl'].forEach(t => {
        const el = document.getElementById(`large-res-${t}`);
        if (el) el.classList.toggle('active', t === target);
      });
    });
  });

  const runBtn = document.getElementById('btnLargeRunQuery');
  if (runBtn) runBtn.addEventListener('click', executeLargeQuery);

  const btnXml = document.getElementById('btnLargeCsdlXml');
  const btnJson = document.getElementById('btnLargeCsdlJson');
  if (btnXml) {
    btnXml.addEventListener('click', () => {
      const v = document.getElementById('largeCsdlCodeView');
      if (v && cachedLargeCsdl) v.textContent = cachedLargeCsdl.csdlXml || '';
    });
  }
  if (btnJson) {
    btnJson.addEventListener('click', () => {
      const v = document.getElementById('largeCsdlCodeView');
      if (v && cachedLargeCsdl) v.textContent = cachedLargeCsdl.csdlJson || '';
    });
  }

  const serverSelect = document.getElementById('largeMcpEndpointInput');
  if (serverSelect) {
    serverSelect.addEventListener('change', async () => {
      await loadLargeMetadataAndExamples();
      const selectedUrl = getSelectedLargeServerUrl();
      if (selectedUrl.endsWith(':3005') && currentLargeQuery.method === 'tools/call' && !['search_intent', 'execute_odata_query'].includes(currentLargeQuery.payload?.params?.name)) {
        selectLargeQuery(LARGE_SAMPLE_QUERIES[2].items[0], true);
      } else {
        executeLargeQuery();
      }
    });
  }

  loadLargeMetadataAndExamples();
  selectLargeQuery(LARGE_SAMPLE_QUERIES[2].items[2], false);

  const largeTabBtn = document.querySelector('.ge-nav-tab[data-view="large-explorer"]');
  if (largeTabBtn) {
    largeTabBtn.addEventListener('click', () => {
      executeLargeQuery();
    });
  }
}

document.addEventListener('DOMContentLoaded', initLargeExplorer);

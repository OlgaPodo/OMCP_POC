// ==========================================================================
// Microsoft Graph MCP 2.0 Explorer - Interactive Logic & OData TC Visualizer
// Specification: 2026-07-28 (MCP 2.0)
// ==========================================================================

const MCP_20_META = {
  'io.modelcontextprotocol/protocolVersion': '2026-07-28',
  'io.modelcontextprotocol/clientInfo': {
    name: 'GraphMcpExplorer',
    version: '2.0.0'
  },
  'io.modelcontextprotocol/clientCapabilities': {}
};

const SAMPLE_QUERIES = {
  'proto-discover': {
    name: 'server/discover (MCP 2.0)',
    category: 'proto',
    payload: {
      jsonrpc: '2.0',
      id: 'discover-1',
      method: 'server/discover',
      params: {
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'MCP 2.0 (2026-07-28) Up-Front Capability & Version Discovery',
    entity: 'MCP 2.0 Server Core Transport',
    args: []
  },
  'tool-find-avery': {
    name: 'find_user ("Avery")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 1,
      method: 'tools/call',
      params: {
        name: 'find_user',
        arguments: { query: 'Avery' }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'Avery') or startswith(userPrincipalName,'Avery')&$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$top=10",
    entity: 'Microsoft.Graph.User (EntitySet: users)',
    args: [{ name: 'query', label: 'Search Query', default: 'Avery' }]
  },
  'tool-find-morgan': {
    name: 'find_user ("Morgan")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 2,
      method: 'tools/call',
      params: {
        name: 'find_user',
        arguments: { query: 'Morgan' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'Alex') or startswith(userPrincipalName,'Alex')&$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$top=10",
    entity: 'Microsoft.Graph.User (EntitySet: users)',
    args: [{ name: 'query', label: 'Search Query', default: 'Alex' }]
  },
  'tool-profile-me': {
    name: 'user_profile ("me")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 3,
      method: 'tools/call',
      params: {
        name: 'user_profile',
        arguments: { userId: 'me' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/me?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$expand=manager($select=id,displayName,userPrincipalName,jobTitle,department),directReports($select=id,displayName,userPrincipalName,jobTitle,department)',
    entity: 'Microsoft.Graph.User (Singleton: me + Nav: manager, directReports)',
    args: [{ name: 'userId', label: 'User ID or UPN', default: 'me' }]
  },
  'tool-profile-avery': {
    name: 'user_profile ("avery.adams@example.com")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 4,
      method: 'tools/call',
      params: {
        name: 'user_profile',
        arguments: { userId: 'avery.adams@example.com' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$expand=manager($select=id,displayName,userPrincipalName,jobTitle,department),directReports($select=id,displayName,userPrincipalName,jobTitle,department)",
    entity: 'Microsoft.Graph.User (EntitySet: users)',
    args: [{ name: 'userId', label: 'User ID or UPN', default: 'avery.adams@example.com' }]
  },
  'tool-find-group': {
    name: 'find_group ("Identity")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 20,
      method: 'tools/call',
      params: {
        name: 'find_group',
        arguments: { query: 'Identity' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/groups?$filter=startswith(displayName,'Identity') or startswith(mailNickname,'Identity')&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility&$top=25",
    entity: 'Microsoft.Graph.Group (EntitySet: groups)',
    args: [{ name: 'query', label: 'Group Search Query', default: 'Identity' }]
  },
  'tool-group-details': {
    name: 'group_details ("Platform Engineering")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 21,
      method: 'tools/call',
      params: {
        name: 'group_details',
        arguments: { groupId: 'Platform Engineering' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')?$expand=members($select=id,displayName,userPrincipalName,jobTitle,department),owners($select=id,displayName,userPrincipalName,jobTitle,department)",
    entity: 'Microsoft.Graph.Group (EntitySet: groups + Nav: members, owners)',
    args: [{ name: 'groupId', label: 'Group ID or Display Name', default: 'Platform Engineering' }]
  },
  'tool-read-user-resource': {
    name: 'Read projected user resource',
    category: 'tool',
    payload: { jsonrpc: '2.0', id: 28, method: 'tools/call', params: {
      name: 'read_resource', arguments: { uri: 'users://me' }, _meta: { ...MCP_20_META }
    } },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/me?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$expand=manager($select=id,displayName,userPrincipalName,jobTitle,department),directReports($select=id,displayName,userPrincipalName,jobTitle,department)',
    entity: 'Microsoft.Graph.GraphService/me',
    args: [{ name: 'uri', label: 'Projected resource URI', default: 'users://me' }]
  },
  'tool-read-group-resource': {
    name: 'Read projected group resource',
    category: 'tool',
    payload: { jsonrpc: '2.0', id: 29, method: 'tools/call', params: {
      name: 'read_resource', arguments: { uri: 'groups://11111111-2222-3333-4444-555555555555' }, _meta: { ...MCP_20_META }
    } },
    graphTarget: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')?$expand=members($select=id,displayName,userPrincipalName,jobTitle,department),owners($select=id,displayName,userPrincipalName,jobTitle,department)",
    entity: 'Microsoft.Graph.GraphService/groups',
    args: [{ name: 'uri', label: 'Projected resource URI', default: 'groups://11111111-2222-3333-4444-555555555555' }]
  },
  // 4. Container-level prompts
  'prompt-reports-to': {
    name: 'Whom Avery reports to?',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 5,
      method: 'prompts/get',
      params: {
        name: 'whom_olga_reports_to',
        arguments: { userName: 'Avery Adams' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')/manager?$select=id,displayName,userPrincipalName,jobTitle,department",
    entity: 'Microsoft.Graph.GraphService/@Agent.Prompts[whom_olga_reports_to]',
    args: [{ name: 'userName', label: 'User Name', default: 'Avery Adams' }]
  },
  'prompt-peers': {
    name: "Who are Avery's peers?",
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 6,
      method: 'prompts/get',
      params: {
        name: 'who_are_olgas_peers',
        arguments: { userName: 'Avery Adams' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')/manager/directReports?$select=id,displayName,userPrincipalName,jobTitle,department",
    entity: 'Microsoft.Graph.GraphService/@Agent.Prompts[who_are_olgas_peers]',
    args: [{ name: 'userName', label: 'User Name', default: 'Avery Adams' }]
  },
  'prompt-direct-reports': {
    name: 'Direct reports of a manager',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 7,
      method: 'prompts/get',
      params: {
        name: 'direct_reports_of_manager',
        arguments: { managerNameOrId: 'Morgan Reed' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users('morgan.reed@example.com')/directReports?$select=id,displayName,userPrincipalName,jobTitle,department",
    entity: 'Microsoft.Graph.GraphService/@Agent.Prompts[direct_reports_of_manager]',
    args: [{ name: 'managerNameOrId', label: 'Manager Name or UPN', default: 'Morgan Reed' }]
  },
  'prompt-search-dept': {
    name: 'Search users by dept & title',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 8,
      method: 'prompts/get',
      params: {
        name: 'search_users_by_dept_title',
        arguments: { department: 'Identity', jobTitle: 'Architect' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users?$filter=department eq 'Identity' and startswith(jobTitle,'Architect')&$select=id,displayName,userPrincipalName,jobTitle,department",
    entity: 'Microsoft.Graph.GraphService/@Agent.Prompts[search_users_by_dept_title]',
    args: [
      { name: 'department', label: 'Department', default: 'Identity' },
      { name: 'jobTitle', label: 'Job Title Keyword', default: 'Architect' }
    ]
  },
  'prompt-quick-lookup': {
    name: 'User quick lookup',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 9,
      method: 'prompts/get',
      params: {
        name: 'user_quick_lookup',
        arguments: { identifier: 'me' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/me?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation',
    entity: 'Microsoft.Graph.User (Resource link + Tool binding)',
    args: [{ name: 'identifier', label: 'Identifier (me or UPN)', default: 'me' }]
  },
  'prompt-find-group': {
    name: 'Find group by name',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 24,
      method: 'prompts/get',
      params: {
        name: 'find_group_by_name',
        arguments: { groupName: 'Platform Engineering' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/groups?$filter=startswith(displayName,'Platform Engineering')&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility",
    entity: 'Microsoft.Graph.GraphService/@Agent.Prompts[find_group_by_name]',
    args: [{ name: 'groupName', label: 'Group Name or Prefix', default: 'Platform Engineering' }]
  },
  'prompt-group-members': {
    name: 'List group members',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 25,
      method: 'prompts/get',
      params: {
        name: 'list_group_members',
        arguments: { groupIdOrName: 'Platform Engineering' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')/members?$select=id,displayName,userPrincipalName,jobTitle,department",
    entity: 'Microsoft.Graph.GraphService/@Agent.Prompts[list_group_members]',
    args: [{ name: 'groupIdOrName', label: 'Group ID or Name', default: 'Platform Engineering' }]
  },
  'prompt-group-owners': {
    name: 'Who owns group?',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 26,
      method: 'prompts/get',
      params: {
        name: 'who_owns_group',
        arguments: { groupIdOrName: 'Platform Engineering' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')/owners?$select=id,displayName,userPrincipalName,jobTitle,department",
    entity: 'Microsoft.Graph.GraphService/@Agent.Prompts[who_owns_group]',
    args: [{ name: 'groupIdOrName', label: 'Group ID or Name', default: 'Platform Engineering' }]
  },
  'prompt-group-lookup': {
    name: 'Group quick lookup',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 27,
      method: 'prompts/get',
      params: {
        name: 'group_quick_lookup',
        arguments: { identifier: 'groups://all' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/groups?$top=100&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility',
    entity: 'Microsoft.Graph.Group (Resource link + Tool binding)',
    args: [{ name: 'identifier', label: 'Group ID or groups://all', default: 'groups://all' }]
  },
  'res-read-me': {
    name: 'Read users://me',
    category: 'resource',
    payload: {
      jsonrpc: '2.0',
      id: 10,
      method: 'resources/read',
      params: {
        uri: 'users://me',
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/me?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation',
    entity: 'Microsoft.Graph.User (Singleton: me)',
    args: []
  },
  'res-read-user': {
    name: 'Read users://{userId}',
    category: 'resource',
    payload: {
      jsonrpc: '2.0',
      id: 11,
      method: 'resources/read',
      params: {
        uri: 'users://avery.adams@example.com',
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation",
    entity: 'Microsoft.Graph.User (EntitySet: users)',
    args: [{ name: 'uri', label: 'Resource URI', default: 'users://avery.adams@example.com' }]
  },
  'res-read-groups': {
    name: 'Read groups://all',
    category: 'resource',
    payload: {
      jsonrpc: '2.0',
      id: 22,
      method: 'resources/read',
      params: {
        uri: 'groups://all',
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/groups?$top=100&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility',
    entity: 'Microsoft.Graph.Group (EntitySet: groups)',
    args: []
  },
  'res-read-group': {
    name: 'Read groups://{groupId}',
    category: 'resource',
    payload: {
      jsonrpc: '2.0',
      id: 23,
      method: 'resources/read',
      params: {
        uri: 'groups://11111111-2222-3333-4444-555555555555',
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')?$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility",
    entity: 'Microsoft.Graph.Group (EntitySet: groups)',
    args: [{ name: 'uri', label: 'Group URI or ID', default: 'groups://11111111-2222-3333-4444-555555555555' }]
  },
  'proto-tools': {
    name: 'tools/list',
    category: 'proto',
    payload: {
      jsonrpc: '2.0',
      id: 13,
      method: 'tools/list',
      params: {}
    },
    graphTarget: 'Enumerates registered MCP tools',
    entity: 'Microsoft.Graph.GraphService Operations',
    args: []
  },
  'proto-prompts': {
    name: 'prompts/list',
    category: 'proto',
    payload: {
      jsonrpc: '2.0',
      id: 14,
      method: 'prompts/list',
      params: {}
    },
    graphTarget: 'Enumerates registered @Agent.Prompts',
    entity: 'Microsoft.Graph.GraphService Annotations',
    args: []
  },
  'proto-resources': {
    name: 'resources/list',
    category: 'proto',
    payload: {
      jsonrpc: '2.0',
      id: 15,
      method: 'resources/list',
      params: {}
    },
    graphTarget: 'Enumerates registered MCP resources',
    entity: 'Microsoft.Graph.GraphService EntitySets & Singletons',
    args: []
  },
  'proto-resource-templates': {
    name: 'resources/templates/list',
    category: 'proto',
    payload: { jsonrpc: '2.0', id: 16, method: 'resources/templates/list', params: {} },
    graphTarget: 'Enumerates URI templates projected from Agent.Resources',
    entity: 'Microsoft.Graph.GraphService/users and groups',
    args: []
  }
};

let PROMPT_CATALOG = [];
let REFERENCE_PROMPT_CATALOG = [];
let metadataLoadVersion = 0;
let currentProjection = null;
let mappingPromptName = 'user_peers_in_group';
let runtimeDiscoverySnapshot = null;
let runtimeDiscoveryVersion = 0;

let currentSelectedQueryKey = 'tool-profile-me';

// Initialize DOM elements and event handlers
document.addEventListener('DOMContentLoaded', () => {
  initViewSwitcher();
  initPaneTabs();
  initDocSubtabs();
  initSidebarQueries();
  initRunQuery();
  initCsdlLoader();
  initSemanticMatcher();
  initMappingVisualizer();
  initExperimentView();

  // Load default query
  selectQuery('tool-profile-me');
});

// View Navigation Switcher
function initViewSwitcher() {
  const tabs = document.querySelectorAll('.ge-nav-tab');
  const views = document.querySelectorAll('.view-container');

  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      tabs.forEach(t => t.classList.remove('active'));
      views.forEach(v => v.classList.remove('active'));

      tab.classList.add('active');
      const viewId = `view-${tab.dataset.view}`;
      const targetView = document.getElementById(viewId);
      if (targetView) targetView.classList.add('active');

      if (tab.dataset.view === 'csdl') {
        loadCsdlData();
      }
      if (tab.dataset.view === 'experiment') {
        loadAvailableReports();
        loadExperimentQueries();
      }
    });
  });
}

// Request and Response Pane Tabs
function initPaneTabs() {
  const tabButtons = document.querySelectorAll('.pane-tab');
  tabButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const parent = btn.closest('.ge-pane');
      const targetId = `tab-${btn.dataset.tab}`;

      parent.querySelectorAll('.pane-tab').forEach(t => t.classList.remove('active'));
      parent.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));

      btn.classList.add('active');
      const panel = document.getElementById(targetId);
      if (panel) panel.classList.add('active');
    });
  });
}

// CSDL Sub-tabs
function initDocSubtabs() {
  const subtabs = document.querySelectorAll('#view-csdl .doc-subtab');
  subtabs.forEach(btn => {
    btn.addEventListener('click', () => {
      subtabs.forEach(t => t.classList.remove('active'));
      document.querySelectorAll('#view-csdl .subtab-panel').forEach(p => p.classList.remove('active'));

      btn.classList.add('active');
      const panelId = `subtab-${btn.dataset.subtab}`;
      const panel = document.getElementById(panelId);
      if (panel) panel.classList.add('active');
    });
  });
}

// Sidebar Query Item Selection
function initSidebarQueries() {
  const items = document.querySelectorAll('.query-item');
  items.forEach(item => {
    item.addEventListener('click', () => {
      items.forEach(i => i.classList.remove('active'));
      item.classList.add('active');
      selectQuery(item.dataset.query);
    });
  });
}

function selectQuery(key) {
  currentSelectedQueryKey = key;
  const q = SAMPLE_QUERIES[key];
  if (!q) return;
  if (q.payload.method === 'prompts/get') {
    mappingPromptName = q.payload.params.name;
    renderMappingScenario();
  }



  // Set Request JSON Area
  document.getElementById('requestJsonArea').value = JSON.stringify(q.payload, null, 2);

  // Set Target Mapping Preview
  document.getElementById('targetGraphUrl').innerText = q.graphTarget;
  document.getElementById('targetOdataEntity').innerText = q.entity;

  // Update Headers Table Dynamic Values
  const mcpMethodEl = document.getElementById('hdrMcpMethod');
  const mcpNameEl = document.getElementById('hdrMcpName');
  if (mcpMethodEl) mcpMethodEl.innerText = q.payload.method || '-';
  if (mcpNameEl) {
    const nameVal = (q.payload.params && (q.payload.params.name || q.payload.params.uri)) || '-';
    mcpNameEl.innerText = nameVal;
  }

  // Build Arguments Form
  buildArgsForm(q);
}

function buildArgsForm(q) {
  const container = document.getElementById('argsFormContainer');
  container.innerHTML = '';

  if (!q.args || q.args.length === 0) {
    container.innerHTML = '<p class="form-hint">This operation does not require configurable parameters.</p>';
    return;
  }

  q.args.forEach(arg => {
    const row = document.createElement('div');
    row.className = 'args-field-row';

    const label = document.createElement('label');
    label.className = 'args-field-label';
    label.innerText = arg.label;

    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'args-field-input';
    input.id = `arg_${arg.name}`;
    input.value = arg.default;

    input.addEventListener('input', () => {
      updatePayloadFromForm(q);
    });

    row.appendChild(label);
    row.appendChild(input);
    container.appendChild(row);
  });

  const submitBtn = document.createElement('button');
  submitBtn.className = 'args-submit-btn';
  submitBtn.innerText = 'Apply Arguments & Run';
  submitBtn.addEventListener('click', () => {
    updatePayloadFromForm(q);
    executeCurrentQuery();
  });
  container.appendChild(submitBtn);
}

function updatePayloadFromForm(q) {
  const payload = JSON.parse(document.getElementById('requestJsonArea').value);
  q.args.forEach(arg => {
    const val = document.getElementById(`arg_${arg.name}`).value;
    if (payload.params && payload.params.arguments) {
      payload.params.arguments[arg.name] = val;
    } else if (payload.params && arg.name === 'uri') {
      payload.params.uri = val;
    }
  });
  document.getElementById('requestJsonArea').value = JSON.stringify(payload, null, 2);
}

// Run Query Execution
function initRunQuery() {
  const btn = document.getElementById('runQueryBtn');
  btn.addEventListener('click', () => {
    executeCurrentQuery();
  });

  const urlSelect = document.getElementById('targetUrlInput');
  if (urlSelect) {
    urlSelect.addEventListener('change', () => {
      PROMPT_CATALOG = [];
      currentProjection = null;
      invalidateRuntimeDiscovery();
      renderMappingScenario();
      document.getElementById('responseJsonArea').textContent = 'Server changed. Run the request against the selected server.';
      document.querySelector('#responseStatusBadge .status-code-text').textContent = 'Not run';
      document.getElementById('orgCardContainer').textContent = 'Run the request against the selected server.';
      document.getElementById('traceList').replaceChildren();
      loadCsdlData();
      updateGraphTraces();
    });
  }
}

async function executeCurrentQuery() {
  const payloadText = document.getElementById('requestJsonArea').value;
  let payloadObj;
  try {
    payloadObj = JSON.parse(payloadText);
  } catch (err) {
    alert('Invalid JSON in Request Payload:\n' + err.message);
    return;
  }

  const btn = document.getElementById('runQueryBtn');
  btn.disabled = true;
  btn.innerHTML = '<span class="btn-icon">⏳</span> Running...';

  const startTime = Date.now();

  try {
    const targetUrl = document.getElementById('targetUrlInput').value.trim() || 'http://localhost:3001/';
    const res = await fetch('/mcp-proxy', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Target-URL': targetUrl
      },
      body: JSON.stringify(payloadObj)
    });

    const data = await res.json();
    if (targetUrl !== document.getElementById('targetUrlInput').value) return;
    const elapsed = data.elapsedMs || (Date.now() - startTime);

    // Update Status Pill
    const statusBadge = document.getElementById('responseStatusBadge');
    const statusText = statusBadge.querySelector('.status-code-text');
    const timeText = document.getElementById('responseTimeText');

    const rpcError = data.jsonRpcResponse?.error || data.jsonRpcResponse?.result?.isError;
    const succeeded = data.statusCode >= 200 && data.statusCode < 300 && !rpcError;
    statusText.textContent = rpcError ? 'MCP error' : `${data.statusCode} ${succeeded ? 'OK' : 'Error'}`;
    timeText.innerText = `${elapsed} ms`;

    if (succeeded) {
      statusBadge.style.background = 'var(--success-light)';
      statusBadge.style.color = 'var(--success)';
    } else {
      statusBadge.style.background = 'var(--warning-light)';
      statusBadge.style.color = 'var(--warning)';
    }

    // Set Response JSON
    const responseView = document.getElementById('responseJsonArea');
    responseView.textContent = JSON.stringify(data.jsonRpcResponse || data.rawResponse, null, 2);

    // Set Response Headers
    renderHeadersTable('resHeadersTable', data.responseHeaders);

    // Render Org Profile Card if user profile is present
    renderProfileCardIfPresent(data.jsonRpcResponse);

    // Fetch and update Graph traces
    updateGraphTraces();

  } catch (err) {
    document.getElementById('responseJsonArea').textContent = JSON.stringify({
      error: 'Proxy failure',
      details: err.message
    }, null, 2);
  } finally {
    btn.disabled = false;
    btn.innerHTML = '<span class="btn-icon">▶</span> Run Query';
  }
}

function renderHeadersTable(tableId, headers) {
  const table = document.getElementById(tableId);
  const tbody = table.querySelector('tbody');
  tbody.innerHTML = '';

  if (!headers || Object.keys(headers).length === 0) {
    tbody.innerHTML = '<tr><td colspan="2" class="empty-cell">No headers received.</td></tr>';
    return;
  }

  for (const [k, v] of Object.entries(headers)) {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${escapeHtml(k)}</td><td>${escapeHtml(String(v))}</td>`;
    tbody.appendChild(tr);
  }
}

function renderProfileCardIfPresent(resp) {
  const container = document.getElementById('orgCardContainer');
  let userObj = null;
  if (resp?.error || resp?.result?.isError) {
    container.textContent = `MCP request failed: ${resp.error?.message || 'The tool returned an error. Inspect Response Payload.'}`;
    return;
  }

if (Array.isArray(resp?.result?.messages)) {
    container.innerHTML = `
      <div class="profile-card">
        <h3>Prompt Result: ${escapeHtml(resp.result.description || 'Rendered prompt')}</h3>
        <p>Standard MCP <code>prompts/get</code> response</p>
        ${resp.result.messages.map(message => `
          <div class="doc-card">
            <strong>${escapeHtml(message.role)}:</strong>
            <p>${escapeHtml(message.content?.text || JSON.stringify(message.content))}</p>
          </div>
        `).join('')}
      </div>`;
    return;
  }

  // Try extracting profile or group from tool / resource result content
  let groupListObj = null;
  let groupDetailObj = null;

  let rawContentText = null;
  if (resp && resp.result && resp.result.content && resp.result.content[0] && resp.result.content[0].text) {
    rawContentText = resp.result.content[0].text;
  } else if (resp && resp.result && resp.result.contents && resp.result.contents[0] && resp.result.contents[0].text) {
    rawContentText = resp.result.contents[0].text;
  }

  if (rawContentText) {
    try {
      const envelope = JSON.parse(rawContentText);
      const parsed = envelope && envelope.uri && Object.hasOwn(envelope, 'data') ? envelope.data : envelope;
      if (Array.isArray(parsed) && parsed.length > 0 && (parsed[0].groupTypes || parsed[0].securityEnabled !== undefined || parsed[0].SecurityEnabled !== undefined)) {
        groupListObj = { query: 'all', total: parsed.length, groups: parsed };
      } else if (parsed.groups && Array.isArray(parsed.groups)) {
        groupListObj = parsed;
      } else if (parsed.owners !== undefined || parsed.members !== undefined || (parsed.groupTypes !== undefined && !parsed.users)) {
        groupDetailObj = parsed;
      } else if (parsed.displayName || parsed.users) {
        userObj = parsed;
      }
    } catch (_) {}
  }

  // If groupListObj is present (e.g. from find_group or groups://all)
  if (groupListObj && groupListObj.groups && Array.isArray(groupListObj.groups)) {
    let listHtml = `
      <div class="profile-card">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:12px;">
          <h3 style="margin:0;">Group Search / Catalog (${groupListObj.total || groupListObj.groups.length} groups${groupListObj.query ? ` for "${escapeHtml(groupListObj.query)}"` : ''})</h3>
          <span class="card-badge" style="background:var(--primary-light); color:var(--primary); font-weight:700;">EntitySet: groups</span>
        </div>
        <div class="relation-cards-grid" style="margin-top: 14px;">`;
    groupListObj.groups.forEach(g => {
      const name = g.displayName || g.DisplayName || 'Group';
      const id = g.id || g.Id || '';
      const desc = g.description || g.Description || '';
      const mail = g.mail || g.Mail || g.mailNickname || g.MailNickname || '';
      const isSec = g.securityEnabled || g.SecurityEnabled;
      const types = g.groupTypes || g.GroupTypes || [];
      const isUnified = types.includes('Unified');
      const badgeText = isUnified ? 'Microsoft 365' : (isSec ? 'Security' : 'Group');
      const initials = name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();

      listHtml += `
        <div class="mini-user-card" onclick="runGroupDetails('${escapeHtml(id || name)}')" style="cursor: pointer; padding:12px;">
          <div class="mini-avatar" style="background:${isUnified ? 'var(--primary)' : '#5c2d91'};">${initials}</div>
          <div class="mini-info" style="flex:1;">
            <div style="display:flex; justify-content:space-between; align-items:center;">
              <span class="mini-name">${escapeHtml(name)}</span>
              <span style="font-size:10px; background:${isUnified ? '#eef5fc' : '#f3eef8'}; color:${isUnified ? 'var(--primary)' : '#5c2d91'}; padding:2px 6px; border-radius:4px; font-weight:600;">${badgeText}</span>
            </div>
            <div class="mini-sub" style="margin-top:3px;">${escapeHtml(desc || mail || id)}</div>
          </div>
        </div>`;
    });
    listHtml += `</div></div>`;
    container.innerHTML = listHtml;
    return;
  }

  // If groupDetailObj is present (e.g. from group_details or groups://{groupId})
  if (groupDetailObj) {
    const name = groupDetailObj.displayName || groupDetailObj.DisplayName || 'Group Detail';
    const id = groupDetailObj.id || groupDetailObj.Id || '';
    const desc = groupDetailObj.description || groupDetailObj.Description || 'No description provided';
    const mail = groupDetailObj.mail || groupDetailObj.Mail || groupDetailObj.mailNickname || groupDetailObj.MailNickname || 'N/A';
    const types = groupDetailObj.groupTypes || groupDetailObj.GroupTypes || [];
    const isUnified = types.includes('Unified');
    const isSec = groupDetailObj.securityEnabled || groupDetailObj.SecurityEnabled;
    const owners = groupDetailObj.owners || groupDetailObj.Owners || [];
    const members = groupDetailObj.members || groupDetailObj.Members || [];
    const initials = name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();

    let html = `
      <div class="profile-card">
        <div class="profile-header">
          <div class="avatar-circle" style="background:${isUnified ? 'var(--primary)' : '#5c2d91'};">${initials}</div>
          <div>
            <div class="profile-name">${escapeHtml(name)}</div>
            <div class="profile-title">${escapeHtml(desc)}</div>
            <div style="font-size: 11px; color: var(--primary); font-weight: 600; margin-top: 2px;">
              ${isUnified ? 'Microsoft 365 Group' : 'Security Group'} &bull; ${escapeHtml(groupDetailObj.source || 'Microsoft Graph')}
            </div>
          </div>
        </div>

        <div class="profile-details-grid">
          <div class="detail-item">
            <span class="detail-label">Group ID</span>
            <span class="detail-value" style="font-family:var(--font-mono); font-size:11px;">${escapeHtml(id)}</span>
          </div>
          <div class="detail-item">
            <span class="detail-label">Group Email / Nickname</span>
            <span class="detail-value">${escapeHtml(mail)}</span>
          </div>
          <div class="detail-item">
            <span class="detail-label">Security Enabled</span>
            <span class="detail-value">${isSec ? 'True' : 'False'}</span>
          </div>
          <div class="detail-item">
            <span class="detail-label">Visibility</span>
            <span class="detail-value">${escapeHtml(groupDetailObj.visibility || 'Public')}</span>
          </div>
        </div>
    `;

    // Owners Section
    if (owners && owners.length > 0) {
      html += `
        <div class="org-relation-section">
          <div class="relation-title">Group Owners (${owners.length})</div>
          <div class="relation-cards-grid">`;
      owners.forEach(owner => {
        const oInit = (owner.displayName || 'O').split(' ').map(n => n[0]).join('').substring(0, 2);
        html += `
          <div class="mini-user-card" onclick="runProfileForUser('${owner.userPrincipalName || owner.id}')" style="cursor:pointer; background:#fdf9ec; border-color:#fce196;">
            <div class="mini-avatar" style="background:#b74700;">${oInit}</div>
            <div class="mini-info">
              <div class="mini-name">${escapeHtml(owner.displayName)}</div>
              <div class="mini-sub">${escapeHtml(owner.jobTitle || owner.userPrincipalName)}</div>
            </div>
          </div>`;
      });
      html += `</div></div>`;
    }

    // Members Section
    if (members && members.length > 0) {
      html += `
        <div class="org-relation-section">
          <div class="relation-title">Group Members (${members.length})</div>
          <div class="relation-cards-grid">`;
      members.forEach(member => {
        const mInit = (member.displayName || 'M').split(' ').map(n => n[0]).join('').substring(0, 2);
        html += `
          <div class="mini-user-card" onclick="runProfileForUser('${member.userPrincipalName || member.id}')" style="cursor:pointer;">
            <div class="mini-avatar" style="background:#0078d4;">${mInit}</div>
            <div class="mini-info">
              <div class="mini-name">${escapeHtml(member.displayName)}</div>
              <div class="mini-sub">${escapeHtml(member.jobTitle || member.department || member.userPrincipalName)}</div>
            </div>
          </div>`;
      });
      html += `</div></div>`;
    }

    html += `</div>`;
    container.innerHTML = html;
    return;
  }

  if (!userObj) {
    container.innerHTML = `
      <div class="empty-state">
        <div class="empty-icon">ℹ️</div>
        <p>Execution completed. Check the <strong>Response Payload</strong> tab for raw JSON-RPC data.</p>
      </div>`;
    return;
  }

  // If userObj is user search list
  if (userObj.users && Array.isArray(userObj.users)) {
    let listHtml = `
      <div class="profile-card">
        <h3>User Search Results (${userObj.total} matches for "${userObj.query}")</h3>
        <div class="relation-cards-grid" style="margin-top: 14px;">`;
    userObj.users.slice(0, 10).forEach(u => {
      const name = u.displayName || u.DisplayName || 'User';
      const identifier = u.userPrincipalName || u.UserPrincipalName || u.id || u.Id;
      const initials = name.split(' ').map(n => n[0]).join('').substring(0, 2);
      listHtml += `
        <div class="mini-user-card" onclick="runProfileForUser('${escapeHtml(identifier)}')" style="cursor: pointer;">
          <div class="mini-avatar">${initials}</div>
          <div class="mini-info">
            <div class="mini-name">${escapeHtml(name)}</div>
            <div class="mini-sub">${escapeHtml(u.jobTitle || u.JobTitle || u.department || u.Department || identifier)}</div>
          </div>
        </div>`;
    });
    listHtml += `</div></div>`;
    container.innerHTML = listHtml;
    return;
  }

  // Render individual Profile Card with Manager & Peers
  const initials = (userObj.displayName || 'OP').split(' ').map(n => n[0]).join('').substring(0, 2);
  let html = `
    <div class="profile-card">
      <div class="profile-header">
        <div class="avatar-circle">${initials}</div>
        <div>
          <div class="profile-name">${escapeHtml(userObj.displayName || 'Avery Adams')}</div>
          <div class="profile-title">${escapeHtml(userObj.jobTitle || 'PRINCIPAL API ARCHITECT')}</div>
          <div style="font-size: 11px; color: var(--primary); font-weight: 600; margin-top: 2px;">${escapeHtml(userObj.source || 'Microsoft Graph')}</div>
        </div>
      </div>

      <div class="profile-details-grid">
        <div class="detail-item">
          <span class="detail-label">User Principal Name</span>
          <span class="detail-value">${escapeHtml(userObj.userPrincipalName || 'avery.adams@example.com')}</span>
        </div>
        <div class="detail-item">
          <span class="detail-label">Mail</span>
          <span class="detail-value">${escapeHtml(userObj.mail || userObj.userPrincipalName || 'avery.adams@example.com')}</span>
        </div>
        <div class="detail-item">
          <span class="detail-label">Department</span>
          <span class="detail-value">${escapeHtml(userObj.department || 'Architecture & Standards')}</span>
        </div>
        <div class="detail-item">
          <span class="detail-label">Office Location</span>
          <span class="detail-value">${escapeHtml(userObj.officeLocation || 'Home Office')}</span>
        </div>
      </div>
  `;

  // Manager Section
  if (userObj.manager) {
    const mgrInitials = (userObj.manager.displayName || 'VV').split(' ').map(n => n[0]).join('').substring(0, 2);
    html += `
      <div class="org-relation-section">
        <div class="relation-title">Reporting Manager (Script Query: "Whom Avery reports to?")</div>
        <div class="mini-user-card" style="background: #eef5fc; border-color: #b3d6f4;">
          <div class="mini-avatar" style="background: #0078d4;">${mgrInitials}</div>
          <div class="mini-info">
            <div class="mini-name">${escapeHtml(userObj.manager.displayName)}</div>
            <div class="mini-sub">${escapeHtml(userObj.manager.jobTitle || '')} &bull; ${escapeHtml(userObj.manager.department || '')}</div>
          </div>
        </div>
      </div>`;
  }

  // Peers Section
  if (userObj.peers && userObj.peers.length > 0) {
    html += `
      <div class="org-relation-section">
        <div class="relation-title">Peer Colleagues (Script Query: "Who are Avery's peers?") [${userObj.peers.length} Peers]</div>
        <div class="relation-cards-grid">`;
    userObj.peers.forEach(peer => {
      const peerInit = (peer.displayName || 'P').split(' ').map(n => n[0]).join('').substring(0, 2);
      html += `
        <div class="mini-user-card">
          <div class="mini-avatar" style="background: #5c2d91;">${peerInit}</div>
          <div class="mini-info">
            <div class="mini-name">${escapeHtml(peer.displayName)}</div>
            <div class="mini-sub">${escapeHtml(peer.jobTitle || peer.department || peer.userPrincipalName)}</div>
          </div>
        </div>`;
    });
    html += `</div></div>`;
  }

  // Direct Reports Section
  if (userObj.directReports && userObj.directReports.length > 0) {
    html += `
      <div class="org-relation-section">
        <div class="relation-title">Direct Reports (${userObj.directReports.length})</div>
        <div class="relation-cards-grid">`;
    userObj.directReports.forEach(rep => {
      const repInit = (rep.displayName || 'R').split(' ').map(n => n[0]).join('').substring(0, 2);
      html += `
        <div class="mini-user-card">
          <div class="mini-avatar" style="background: #107c41;">${repInit}</div>
          <div class="mini-info">
            <div class="mini-name">${escapeHtml(rep.displayName)}</div>
            <div class="mini-sub">${escapeHtml(rep.jobTitle || rep.userPrincipalName)}</div>
          </div>
        </div>`;
    });
    html += `</div></div>`;
  }

  html += `</div>`;
  container.innerHTML = html;
}

window.runProfileForUser = function(upn) {
  selectQuery('tool-profile-avery');
  const payload = JSON.parse(document.getElementById('requestJsonArea').value);
  payload.params.arguments.userId = upn;
  document.getElementById('requestJsonArea').value = JSON.stringify(payload, null, 2);
  executeCurrentQuery();
};

window.runGroupDetails = function(groupId) {
  selectQuery('tool-group-details');
  const payload = JSON.parse(document.getElementById('requestJsonArea').value);
  payload.params.arguments.groupId = groupId;
  document.getElementById('requestJsonArea').value = JSON.stringify(payload, null, 2);
  executeCurrentQuery();
};

async function updateGraphTraces() {
  try {
    const headers = selectedServerHeaders();
    const res = await fetch('/api/traces', { headers });
    if (!res.ok) throw new Error(`Trace request failed: HTTP ${res.status}`);
    const traces = await res.json();
    if (headers['X-Target-URL'] !== document.getElementById('targetUrlInput').value) return;
    const list = document.getElementById('traceList');
    list.innerHTML = '';

    traces.slice(-5).reverse().forEach(t => {
      const item = document.createElement('div');
      item.className = 'trace-item';
      item.innerHTML = `
        <span class="trace-method">${escapeHtml(t.method)}</span>
        <span class="trace-url">${escapeHtml(t.url)}</span>
        <span class="trace-status s${t.statusCode}">${t.statusCode} ${t.isLive ? '(Live)' : '(Mock)'}</span>
      `;
      list.appendChild(item);
    });
  } catch (error) {
    document.getElementById('traceList').textContent = `Traces unavailable: ${error.message}`;
  }
}

// CSDL Metadata Loader
async function initCsdlLoader() {
  loadCsdlData();
  loadAllCsdls();
}

async function loadAllCsdls() {
  try {
    const res = await fetch('/api/csdls');
    if (!res.ok) return;
    const csdls = await res.json();
    for (const item of csdls) {
      if (item.id === 'csdl-with-prompts') {
        const el = document.getElementById('csdlXmlCode');
        if (el) el.textContent = item.xml;
        const meta = document.getElementById('metaWithPrompts');
        if (meta) meta.textContent = `EDMX 4.0 • ${item.tag} • ${item.sizeFormatted} (${item.lineCount} lines)`;
      } else if (item.id === 'csdl-baseline') {
        const el = document.getElementById('csdlXmlCode_Baseline');
        if (el) el.textContent = item.xml;
        const meta = document.getElementById('metaBaseline');
        if (meta) meta.textContent = `EDMX 4.0 • ${item.tag} • ${item.sizeFormatted} (${item.lineCount} lines)`;
      } else if (item.id === 'csdl-large-with-examples') {
        const el = document.getElementById('csdlXmlCode_LargeWithExamples');
        if (el) el.textContent = item.xml;
        const meta = document.getElementById('metaLargeWithExamples');
        if (meta) meta.textContent = `EDMX 4.0 • ${item.tag} • ${item.sizeFormatted} (${item.lineCount} lines)`;
      } else if (item.id === 'csdl-large-no-examples') {
        const el = document.getElementById('csdlXmlCode_LargeNoExamples');
        if (el) el.textContent = item.xml;
        const meta = document.getElementById('metaLargeNoExamples');
        if (meta) meta.textContent = `EDMX 4.0 • ${item.tag} • ${item.sizeFormatted} (${item.lineCount} lines)`;
      } else if (item.id === 'csdl-inventory') {
        const el = document.getElementById('csdlXmlCode_Inventory');
        if (el) el.textContent = item.xml;
        const meta = document.getElementById('metaInventory');
        if (meta) meta.textContent = `EDMX 4.0 • ${item.tag} • ${item.sizeFormatted} (${item.lineCount} lines)`;
      }
    }
  } catch (err) {
    console.warn('Failed to load all CSDL files:', err);
  }
}

function selectedServerHeaders() {
  return { 'X-Target-URL': document.getElementById('targetUrlInput').value };
}

async function loadCsdlData() {
  const version = ++metadataLoadVersion;
  renderPocSummary(null, 'Loading inventory from the selected MCP...');
  try {
    const headers = selectedServerHeaders();
    const responses = await Promise.all([
      fetch('/api/csdl', { headers }), fetch('/api/projection', { headers })
    ]);
    if (responses.some(r => !r.ok)) throw new Error('The selected MCP metadata endpoint is unavailable.');
    const [data, projection] = await Promise.all(responses.map(r => r.json()));
    if (version !== metadataLoadVersion) return;
    const catalog = projection.prompts;
    currentProjection = projection;
    PROMPT_CATALOG = catalog;
    if (catalog && catalog.length > 0) {
      REFERENCE_PROMPT_CATALOG = catalog;
    }
    syncPromptQueries(catalog.length ? catalog : REFERENCE_PROMPT_CATALOG);
    renderProjection(projection);
    const csdlJsonEl = document.getElementById('csdlJsonCode');
    if (csdlJsonEl) csdlJsonEl.textContent = data.csdlJson;
    document.getElementById('csdlXmlCode').textContent = data.csdlXml;
    document.getElementById('comparisonXmlCode').textContent = data.csdlXml;
    runMatcher();
    renderMappingScenario();
  } catch (error) {
    if (version !== metadataLoadVersion) return;
    PROMPT_CATALOG = [];
    currentProjection = null;
    renderPocSummary(null, `Metadata unavailable: ${error.message}`);
    const csdlJsonEl = document.getElementById('csdlJsonCode');
    if (csdlJsonEl) csdlJsonEl.textContent = `Metadata unavailable: ${error.message}`;
    for (const id of ['csdlXmlCode', 'comparisonXmlCode'])
      document.getElementById(id).textContent = `Metadata unavailable: ${error.message}`;
    runMatcher();
  }
}

function syncPromptQueries(catalog) {
  const list = document.getElementById('promptQueryList');
  const mapSelect = document.getElementById('mappingPromptSelect');
  if (mapSelect) mapSelect.replaceChildren();
  for (const prompt of catalog) {
    let key = Object.keys(SAMPLE_QUERIES).find(name =>
      SAMPLE_QUERIES[name].payload.method === 'prompts/get' && SAMPLE_QUERIES[name].payload.params.name === prompt.name);
    if (!key) {
      key = `prompt-${prompt.name}`;
      SAMPLE_QUERIES[key] = {
        category: 'prompt',
        payload: { jsonrpc: '2.0', id: key, method: 'prompts/get', params: { name: prompt.name, _meta: { ...MCP_20_META } } }
      };
      const item = document.createElement('li');
      item.className = 'query-item';
      item.dataset.query = key;
      item.innerHTML = `<span class="http-badge prompt-badge">PROMPT</span><span class="query-label">${escapeHtml(prompt.title)}</span>`;
      item.addEventListener('click', () => {
        document.querySelectorAll('.query-item').forEach(query => query.classList.remove('active'));
        item.classList.add('active');
        selectQuery(key);
      });
      list.appendChild(item);
    }
    const query = SAMPLE_QUERIES[key];
    query.name = prompt.title;
    query.args = prompt.arguments.map(argument => ({
      name: argument.name, label: argument.description || argument.name, default: argument.default ?? ''
    }));
    query.payload.params.arguments = Object.fromEntries(query.args.map(argument => [argument.name, argument.default]));
    query.entity = `Microsoft.Graph.GraphService/@Agent.Prompts[${prompt.name}]`;
    const odataPromptTargets = {
      user_peers_in_group: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')/manager/directReports; GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')/members",
      group_members_reporting_to_manager: "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')/members; GET https://graph.microsoft.com/v1.0/users('morgan.reed@example.com')/directReports",
      user_and_group_owners_common_manager: "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')/manager; GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')/owners"
    };
    if (!query.graphTarget || odataPromptTargets[prompt.name]) {
      query.graphTarget = odataPromptTargets[prompt.name] || query.graphTarget;
    }
    if (mapSelect) {
      const opt = document.createElement('option');
      opt.value = prompt.name;
      const scopeTag = prompt.entitySets.length > 1 ? 'users + groups' : prompt.entitySets.join('');
      opt.textContent = `${prompt.title} (${ prompt.name } — ${scopeTag})`;
      mapSelect.appendChild(opt);
    }
  }
  if (mapSelect && catalog.some(p => p.name === mappingPromptName)) {
    mapSelect.value = mappingPromptName;
  }
}

function renderProjection(projection) {
  const summary = `${projection.tools.length} CSDL tools + ${projection.resources.length} CSDL resources -> ${projection.prompts.length} container prompts. Container prompts compose projected tools and resources.`;
  document.querySelectorAll('.projection-summary').forEach(node => { node.textContent = summary; });
  const bindings = document.getElementById('projectionBindingsBody');
  bindings.replaceChildren();
  for (const [kind, declarations] of [['Tool', projection.tools], ['Resource', projection.resources]]) {
    for (const definition of declarations) {
      const row = document.createElement('tr');
      for (const value of [kind, definition.Name, definition.annotationTarget, definition.Operation,
        definition.UriTemplate || (definition.Arguments || []).map(argument => argument.Name).join(', ')]) {
        const cell = document.createElement('td');
        cell.textContent = value;
        row.appendChild(cell);
      }
      bindings.appendChild(row);
    }
  }
  const dependencies = document.getElementById('promptDependenciesBody');
  dependencies.replaceChildren();
  for (const prompt of projection.prompts) {
    const row = document.createElement('tr');
    const queryList = (prompt.odataQueries && prompt.odataQueries.length)
      ? prompt.odataQueries.map(q => `GET ${q}`).join('; ')
      : prompt.toolBindings.join(', ');
    for (const value of [prompt.name, prompt.entitySets.join(' + '), queryList,
      prompt.resourceBindings.join(', '), prompt.entitySets.length > 1 ? 'Cross-resource join; not one entity set' : 'Single entity-set workflow']) {
      const cell = document.createElement('td');
      cell.textContent = value;
      row.appendChild(cell);
    }
    dependencies.appendChild(row);
  }
  if (!dependencies.children.length) {
    dependencies.innerHTML = '<tr><td colspan="5">No @Agent.Prompts annotations in the selected baseline CSDL (0 container prompts).</td></tr>';
  }
  renderPocSummary(projection);
}

function renderPocSummary(projection, status = '') {
  const server = document.getElementById('targetUrlInput').value;
  document.getElementById('summaryMetadataStatus').textContent = projection
    ? `Inventory loaded from ${server}. Counts describe declarations, not service health or production certification.`
    : `${status} Selected MCP: ${server}`;
  const inventories = [
    ['summaryToolCount', 'summaryTools', projection?.tools,
      definition => `${definition.Name}: ${definition.Description} [operation: ${definition.Operation}]`],
    ['summaryResourceCount', 'summaryResources', projection?.resources,
      definition => `${definition.UriTemplate}: ${definition.Description}`],
    ['summaryPromptCount', 'summaryPrompts', projection?.prompts,
      prompt => `${prompt.name}: ${prompt.title} [${prompt.entitySets.join(' + ')}]`]
  ];
  for (const [countId, listId, values, describe] of inventories) {
    document.getElementById(countId).textContent = values ? String(values.length) : 'unavailable';
    const list = document.getElementById(listId);
    list.replaceChildren();
    const lines = values
      ? (values.length ? values.map(describe) : ['No declarations in the selected CSDL.'])
      : ['Inventory has not been loaded from the selected MCP.'];
    for (const description of lines) {
      const item = document.createElement('li');
      item.textContent = description;
      list.appendChild(item);
    }
  }
  const concrete = projection?.resources.filter(resource => !resource.UriTemplate.includes('{')).length;
  document.getElementById('summaryResourceKinds').textContent = projection
    ? `${concrete} concrete resources via resources/list; ${projection.resources.length - concrete} URI templates via resources/templates/list.`
    : 'Resource classification unavailable.';
  const composed = projection?.prompts.filter(prompt => prompt.entitySets.length > 1).length;
  document.getElementById('summaryPromptKinds').textContent = projection
    ? (projection.prompts.length
        ? `${composed} cross-resource workflows; ${projection.prompts.length - composed} single-entity-set workflows. All are container-scoped.`
        : '0 container prompts in the selected baseline CSDL (Server B has no @Agent.Prompts).')
    : 'Workflow dependencies unavailable.';
  document.getElementById('summaryQueryMetadata').textContent = projection
    ? `${projection.prompts.length} container prompt declarations in the selected CSDL.`
    : 'Prompt vocabulary metadata unavailable.';
}

// Semantic Matcher Sandbox (Concern 1)
function initSemanticMatcher() {
  const btn = document.getElementById('testMatchBtn');
  const input = document.getElementById('matcherInput');

  btn.addEventListener('click', runMatcher);
  input.addEventListener('keydown', e => {
    if (e.key === 'Enter') runMatcher();
  });

  runMatcher(); // initial run
}


function runMatcher() {
  const query = document.getElementById('matcherInput').value.toLowerCase();
  const ignored = new Set(['who', 'what', 'is', 'are', 'the', 'a', 'of', 'to', 'in', 'for', 'me', 'my', 'does', 'by']);
  const words = text => new Set((text.toLowerCase().match(/[a-z0-9]+/g) || []).filter(w => !ignored.has(w)));
  const queryWords = words(query);
  const candidates = PROMPT_CATALOG.map(prompt => ({
    prompt,
    utterance: `${prompt.title} ${prompt.description} ${(prompt.arguments || []).map(a => a.default || '').join(' ')}`,
    request: (prompt.odataQueries || [])[0] || '',
    entitySet: (prompt.entitySets || []).join(' + ')
  }));
  const matches = candidates.filter(c => c.prompt).map(item => {
    const terms = words(item.utterance);
    const overlap = [...queryWords].filter(word => terms.has(word)).length;
    return { ...item, score: overlap / Math.max(queryWords.size, 1), overlap };
  }).filter(m => m.overlap >= 2 && m.score >= 0.4).sort((a, b) => b.score - a.score).slice(0, 3);
  const container = document.getElementById('matcherResults');
  container.replaceChildren();
  if (!matches.length) {
    container.textContent = 'No matching prompt. The request may be unsupported, ambiguous, or phrased differently. No tool was executed.';
    return;
  }
  for (const { prompt, request, entitySet, score } of matches) {
    const card = document.createElement('div');
    card.className = 'match-card';
    card.innerHTML = `<strong>${escapeHtml(prompt.title)}</strong>
      <p>Text overlap: ${(score * 100).toFixed(0)}% (not model confidence)</p>
      <p>${escapeHtml(prompt.title)} &rarr; <code>${escapeHtml(request)}</code></p>
      <p>Entity set: ${escapeHtml(entitySet)} | Container prompt: ${escapeHtml(prompt.name)}</p>`;
    container.appendChild(card);
  }
}


// End-to-End Mapping Visualizer (Concern 2)
function initMappingVisualizer() {
  const tabs = [...document.querySelectorAll('#view-mapping .mapping-subtab')];
  const activate = tab => {
    for (const item of tabs) {
      const active = item === tab;
      item.classList.toggle('active', active);
      item.setAttribute('aria-selected', String(active));
      item.tabIndex = active ? 0 : -1;
      document.getElementById(`mapping-panel-${item.dataset.mappingTab}`).hidden = !active;
    }
    if (tab.dataset.mappingTab === 'runtime' && !runtimeDiscoverySnapshot) refreshRuntimeDiscovery();
  };
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => activate(tab));
    tab.addEventListener('keydown', event => {
      if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        event.preventDefault();
        const next = tabs[(index + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
        activate(next);
        next.focus();
      }
    });
  });

  const mapSelect = document.getElementById('mappingPromptSelect');
  if (mapSelect) {
    mapSelect.addEventListener('change', () => {
      mappingPromptName = mapSelect.value;
      renderMappingScenario();
    });
  }

  document.querySelectorAll('#view-mapping [data-map-prompt]').forEach(btn => {
    btn.addEventListener('click', () => {
      mappingPromptName = btn.dataset.mapPrompt;
      renderMappingScenario();
    });
  });

  const expandAllBtn = document.getElementById('btnExpandAllMappingResponses');
  if (expandAllBtn) {
    expandAllBtn.addEventListener('click', () => {
      document.querySelectorAll('#view-mapping .tile-request-details, #view-mapping .tile-response-details').forEach(d => { d.open = true; });
    });
  }

  const collapseAllBtn = document.getElementById('btnCollapseAllMappingResponses');
  if (collapseAllBtn) {
    collapseAllBtn.addEventListener('click', () => {
      document.querySelectorAll('#view-mapping .tile-request-details, #view-mapping .tile-response-details').forEach(d => { d.open = false; });
    });
  }

  document.getElementById('refreshRuntimeDiscovery').addEventListener('click', refreshRuntimeDiscovery);
  renderMappingScenario();
}

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function highlightMappingProperties(rawText) {
  if (!rawText) return '';
  let escaped = escapeHtml(rawText);

  // 1. Highlight Suggested Annotations Standard terms (Purple)
  escaped = escaped.replace(/(@Agent\.[A-Za-z]+|Term=&quot;Agent\.[A-Za-z]+&quot;|Agent\.Prompts|Agent\.Tools|Agent\.Resources|EntityContainer|EntitySet|Singleton)/g, '<span class="hl-prop-annot">$1</span>');
  escaped = escaped.replace(/\{(tool|resource):([a-zA-Z0-9_:/{}]+)\}/g, '<span class="hl-prop-annot">{$1:$2}</span>');

  // 2. Suggested Annotations Standard JSON properties (Purple)
  const annotationProps = [
    'tool_binding', 'NavigationPropertyBinding', 'EntitySet', 'Singleton', 'EntityType',
    'annotationTarget', 'Operation', 'UriTemplate', 'Tools', 'Resources', 'Name', 'Title', 'Description', 'Arguments', 'Messages', 'Role', 'Content', 'Text',
    '$Version', '$EntityContainer', '$Kind', '$Type', '$Collection', '$Key', '$Nullable', '$NavigationPropertyBinding'
  ];

  annotationProps.forEach(prop => {
    const reg = new RegExp(`(&quot;${escapeRegExp(prop)}&quot;\\s*:)`, 'g');
    escaped = escaped.replace(reg, '<span class="hl-prop-annot">&quot;' + prop + '&quot;</span>:');
  });

  // 3. MCP Standard properties in JSON (Blue)
  const mcpSpecProps = [
    'name', 'title', 'description', 'arguments', 'messages', 'role',
    'content', 'type', 'text', 'result', 'resultType', '_meta', 'serverInfo',
    'supportedVersions', 'capabilities', 'inputSchema', 'uri', 'uriTemplate', 'mimeType',
    'resources', 'resourceTemplates', 'tools', 'prompts', 'contents', 'method', 'params', 'id', 'jsonrpc'
  ];

  mcpSpecProps.forEach(prop => {
    const reg = new RegExp(`(&quot;${escapeRegExp(prop)}&quot;\\s*:)`, 'g');
    escaped = escaped.replace(reg, '<span class="hl-prop-mcp">&quot;' + prop + '&quot;</span>:');
  });

  // Highlight MCP Standard methods and result values (Blue)
  escaped = escaped.replace(/(&quot;method&quot;:\s*&quot;)(prompts\/get|prompts\/list|tools\/call|tools\/list|resources\/read|resources\/list|resources\/templates\/list|server\/discover)(&quot;)/g, '$1<span class="hl-prop-mcp">$2</span>$3');
  escaped = escaped.replace(/(&quot;resultType&quot;:\s*&quot;)(complete)(&quot;)/g, '$1<span class="hl-prop-mcp">$2</span>$3');

  return escaped;
}

function renderTileExchanges(cardEl, summaryNote, exchanges) {
  if (!cardEl) return;
  cardEl.replaceChildren();
  if (summaryNote) {
    const note = document.createElement('div');
    note.className = 'tile-summary-note';
    note.innerHTML = highlightMappingProperties(summaryNote);
    cardEl.appendChild(note);
  }
  const list = document.createElement('div');
  list.className = 'tile-exchange-list';
  exchanges.forEach((ex, idx) => {
    const wrapper = document.createElement('div');
    wrapper.className = 'tile-exchange';

    const reqDetails = document.createElement('details');
    reqDetails.className = 'tile-request-details tile-request-block';
    const reqSummary = document.createElement('summary');
    reqSummary.className = 'tile-request-summary';
    const reqBadge = document.createElement('span');
    reqBadge.className = 'tile-req-badge';
    reqBadge.textContent = ex.badge || `Request ${idx + 1}`;
    const reqTitle = document.createElement('span');
    reqTitle.className = 'tile-req-title';
    const baseReqTitle = ex.requestTitle || `Request ${idx + 1}`;
    reqTitle.textContent = baseReqTitle.includes('click to expand')
      ? baseReqTitle
      : `${baseReqTitle} (click to expand)`;
    reqSummary.append(reqBadge, reqTitle);

    const reqBody = document.createElement('div');
    reqBody.className = 'tile-request-body';
    const reqPre = document.createElement('pre');
    reqPre.className = 'tile-request-code';
    reqPre.innerHTML = highlightMappingProperties(
      typeof ex.request === 'string' ? ex.request : JSON.stringify(ex.request, null, 2)
    );
    reqBody.appendChild(reqPre);
    reqDetails.append(reqSummary, reqBody);

    const details = document.createElement('details');
    details.className = 'tile-response-details';
    const summary = document.createElement('summary');
    summary.className = 'tile-response-summary';
    const resTitle = document.createElement('span');
    resTitle.className = 'tile-res-title';
    resTitle.textContent = ex.responseTitle || 'Response (click to expand)';
    const resBadge = document.createElement('span');
    resBadge.className = 'tile-res-badge';
    resBadge.textContent = ex.statusBadge || '200 OK';
    summary.append(resTitle, resBadge);

    const resBody = document.createElement('div');
    resBody.className = 'tile-response-body';
    const resPre = document.createElement('pre');
    resPre.className = 'tile-response-code';
    resPre.innerHTML = highlightMappingProperties(
      typeof ex.response === 'string' ? ex.response : JSON.stringify(ex.response, null, 2)
    );
    resBody.appendChild(resPre);
    details.append(summary, resBody);

    wrapper.append(reqDetails, details);
    list.appendChild(wrapper);
  });
  cardEl.appendChild(list);
}

function buildScenarioCallExchanges(prompt, arguments_) {
  const userQuery = arguments_.userName || arguments_.managerNameOrId || arguments_.identifier || 'Avery Adams';
  const groupQuery = arguments_.groupName || arguments_.groupIdOrName || 'Platform Engineering';
  const resolvedUpn = String(userQuery).toLowerCase().includes('alex') ? 'morgan.reed@example.com' : 'avery.adams@example.com';
  const resolvedGroupId = '11111111-2222-3333-4444-555555555555';

  const toolExchanges = [];
  for (const toolName of prompt.toolBindings) {
    if (toolName === 'find_user') {
      toolExchanges.push({
        badge: 'MCP tools/call + Graph GET',
        requestTitle: `Disambiguate user "${userQuery}" via find_user (startswith filter)`,
        request: {
          mcpJsonRpc: {
            jsonrpc: '2.0',
            id: 'call-find-user-1',
            method: 'tools/call',
            params: { name: 'find_user', arguments: { query: userQuery }, _meta: { ...MCP_20_META } }
          },
          downstreamGraphRequest: `GET https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'${userQuery}') or startswith(userPrincipalName,'${userQuery}')&$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$top=10`,
          transcriptNote: 'Uses Graph-supported startswith(...) instead of unsupported contains(...).'
        },
        responseTitle: 'Response: find_user match & resolved UPN (click to expand)',
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'call-find-user-1',
          result: {
            content: [{
              type: 'text',
              text: JSON.stringify({
                query: userQuery,
                total: 1,
                users: [{
                  id: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
                  displayName: 'Avery Adams',
                  userPrincipalName: resolvedUpn,
                  jobTitle: 'Principal API Architect',
                  department: 'Architecture & Standards'
                }]
              }, null, 2)
            }]
          }
        }
      });
    } else if (toolName === 'user_profile') {
      toolExchanges.push({
        badge: 'MCP tools/call + 3 Graph GETs',
        requestTitle: `Fetch organizational profile for "${resolvedUpn}" via user_profile`,
        request: {
          mcpJsonRpc: {
            jsonrpc: '2.0',
            id: 'call-user-profile-1',
            method: 'tools/call',
            params: { name: 'user_profile', arguments: { userId: resolvedUpn }, _meta: { ...MCP_20_META } }
          },
          downstreamGraphRequests: [
            `GET https://graph.microsoft.com/v1.0/users('${resolvedUpn}')?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation`,
            `GET https://graph.microsoft.com/v1.0/users('${resolvedUpn}')/manager?$select=id,displayName,userPrincipalName,jobTitle,department`,
            "GET https://graph.microsoft.com/v1.0/users('morgan.reed@example.com')/directReports?$select=id,displayName,userPrincipalName,jobTitle,department"
          ]
        },
        responseTitle: 'Response: user_profile with manager, peers & directReports (click to expand)',
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'call-user-profile-1',
          result: {
            content: [{
              type: 'text',
              text: JSON.stringify({
                '@context': 'https://graph.microsoft.com/v1.0/$metadata#users/$entity',
                id: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
                displayName: 'Avery Adams',
                userPrincipalName: resolvedUpn,
                jobTitle: 'Principal API Architect',
                department: 'Architecture & Standards',
                manager: {
                  id: 'mgr-001',
                  displayName: 'Morgan Reed',
                  userPrincipalName: 'morgan.reed@example.com',
                  jobTitle: 'Partner Group Engineering Manager'
                },
                peers: [
                  { displayName: 'Alex Wilber', userPrincipalName: 'morgan.reed@example.com', jobTitle: 'Senior Identity Architect' },
                  { displayName: 'Jordan Lee', userPrincipalName: 'jordan.lee@example.com', jobTitle: 'Principal PM Manager' },
                  { displayName: 'Taylor Morgan', userPrincipalName: 'taylor.morgan@example.com', jobTitle: 'Principal Security Engineer' }
                ]
              }, null, 2)
            }]
          }
        }
      });
    } else if (toolName === 'find_group') {
      toolExchanges.push({
        badge: 'MCP tools/call + Graph GET',
        requestTitle: `Locate group "${groupQuery}" via find_group (startswith filter)`,
        request: {
          mcpJsonRpc: {
            jsonrpc: '2.0',
            id: 'call-find-group-1',
            method: 'tools/call',
            params: { name: 'find_group', arguments: { query: groupQuery }, _meta: { ...MCP_20_META } }
          },
          downstreamGraphRequest: `GET https://graph.microsoft.com/v1.0/groups?$filter=startswith(displayName,'${groupQuery}') or startswith(mailNickname,'${groupQuery}')&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility&$top=25`
        },
        responseTitle: 'Response: find_group match & resolved Group ID (click to expand)',
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'call-find-group-1',
          result: {
            content: [{
              type: 'text',
              text: JSON.stringify({
                '@context': 'https://graph.microsoft.com/v1.0/$metadata#groups',
                query: groupQuery,
                total: 1,
                groups: [{
                  id: resolvedGroupId,
                  displayName: 'Platform Engineering',
                  mailNickname: 'id-arch',
                  groupTypes: ['Unified'],
                  securityEnabled: true
                }]
              }, null, 2)
            }]
          }
        }
      });
    } else if (toolName === 'group_details') {
      toolExchanges.push({
        badge: 'MCP tools/call + 3 Graph GETs',
        requestTitle: `Fetch group details, owners & members for "${resolvedGroupId}"`,
        request: {
          mcpJsonRpc: {
            jsonrpc: '2.0',
            id: 'call-group-details-1',
            method: 'tools/call',
            params: { name: 'group_details', arguments: { groupId: resolvedGroupId }, _meta: { ...MCP_20_META } }
          },
          downstreamGraphRequests: [
            `GET https://graph.microsoft.com/v1.0/groups('${resolvedGroupId}')?$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility`,
            `GET https://graph.microsoft.com/v1.0/groups('${resolvedGroupId}')/owners?$select=id,displayName,userPrincipalName,jobTitle,department`,
            `GET https://graph.microsoft.com/v1.0/groups('${resolvedGroupId}')/members?$select=id,displayName,userPrincipalName,jobTitle,department`
          ]
        },
        responseTitle: 'Response: group_details with owners & members (click to expand)',
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'call-group-details-1',
          result: {
            content: [{
              type: 'text',
              text: JSON.stringify({
                '@context': 'https://graph.microsoft.com/v1.0/$metadata#groups/$entity',
                id: resolvedGroupId,
                displayName: 'Platform Engineering',
                owners: [
                  { displayName: 'Morgan Reed', userPrincipalName: 'morgan.reed@example.com' },
                  { displayName: 'Avery Adams', userPrincipalName: 'avery.adams@example.com' }
                ],
                members: [
                  { displayName: 'Avery Adams', userPrincipalName: 'avery.adams@example.com' },
                  { displayName: 'Alex Wilber', userPrincipalName: 'morgan.reed@example.com' },
                  { displayName: 'Jordan Lee', userPrincipalName: 'jordan.lee@example.com' }
                ]
              }, null, 2)
            }]
          }
        }
      });
    }
  }

  const resourceExchanges = prompt.resourceBindings.map((uriTemplate, idx) => {
    const resolvedUri = uriTemplate
      .replace('{userId}', resolvedUpn)
      .replace('{groupId}', resolvedGroupId);
    const isGroup = resolvedUri.startsWith('groups://');
    const graphUrls = resolvedUri === 'users://me'
      ? [
          'GET https://graph.microsoft.com/v1.0/me?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation',
          'GET https://graph.microsoft.com/v1.0/me/manager?$select=id,displayName,userPrincipalName,jobTitle,department',
          "GET https://graph.microsoft.com/v1.0/users('morgan.reed@example.com')/directReports?$select=id,displayName,userPrincipalName,jobTitle,department"
        ]
      : resolvedUri === 'groups://all'
        ? ['GET https://graph.microsoft.com/v1.0/groups?$top=100&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility']
        : isGroup
          ? [
              `GET https://graph.microsoft.com/v1.0/groups('${resolvedGroupId}')?$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility`,
              `GET https://graph.microsoft.com/v1.0/groups('${resolvedGroupId}')/owners?$select=id,displayName,userPrincipalName,jobTitle,department`,
              `GET https://graph.microsoft.com/v1.0/groups('${resolvedGroupId}')/members?$select=id,displayName,userPrincipalName,jobTitle,department`
            ]
          : [
              `GET https://graph.microsoft.com/v1.0/users('${resolvedUpn}')?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation`,
              `GET https://graph.microsoft.com/v1.0/users('${resolvedUpn}')/manager?$select=id,displayName,userPrincipalName,jobTitle,department`,
              "GET https://graph.microsoft.com/v1.0/users('morgan.reed@example.com')/directReports?$select=id,displayName,userPrincipalName,jobTitle,department"
            ];
    const resourceData = isGroup
      ? {
          '@context': 'https://graph.microsoft.com/v1.0/$metadata#groups/$entity',
          id: resolvedGroupId,
          displayName: 'Platform Engineering',
          owners: [{ displayName: 'Morgan Reed', userPrincipalName: 'morgan.reed@example.com' }],
          members: [
            { displayName: 'Avery Adams', userPrincipalName: 'avery.adams@example.com' },
            { displayName: 'Alex Wilber', userPrincipalName: 'morgan.reed@example.com' },
            { displayName: 'Jordan Lee', userPrincipalName: 'jordan.lee@example.com' }
          ]
        }
      : {
          '@context': 'https://graph.microsoft.com/v1.0/$metadata#users/$entity',
          displayName: 'Avery Adams',
          userPrincipalName: resolvedUpn,
          manager: { displayName: 'Morgan Reed', userPrincipalName: 'morgan.reed@example.com' },
          peers: [
            { displayName: 'Alex Wilber', userPrincipalName: 'morgan.reed@example.com' },
            { displayName: 'Jordan Lee', userPrincipalName: 'jordan.lee@example.com' },
            { displayName: 'Taylor Morgan', userPrincipalName: 'taylor.morgan@example.com' }
          ]
        };

    return {
      badge: `Resource Read ${idx + 1}`,
      requestTitle: `Read ${resolvedUri} (from template ${uriTemplate})`,
      request: {
        directMcpResourceRead: {
          jsonrpc: '2.0',
          id: `res-read-${idx + 1}`,
          method: 'resources/read',
          params: { uri: resolvedUri, _meta: { ...MCP_20_META } }
        },
        cliToolAdapterAlternative: {
          jsonrpc: '2.0',
          id: `tool-read-resource-${idx + 1}`,
          method: 'tools/call',
          params: { name: 'read_resource', arguments: { uri: resolvedUri }, _meta: { ...MCP_20_META } }
        },
        downstreamODataRequests: graphUrls
      },
      responseTitle: `Response: ${resolvedUri} contents & CLI envelope (click to expand)`,
      statusBadge: '200 OK',
      response: {
        resourcesReadResult: {
          contents: [{ uri: resolvedUri, mimeType: 'application/json', text: JSON.stringify(resourceData) }]
        },
        cliAdapterResult: {
          uri: resolvedUri,
          entitySet: isGroup ? 'groups' : 'users',
          data: resourceData
        }
      }
    };
  });

  if (!toolExchanges.length && prompt.toolBindings.includes('read_resource')) {
    toolExchanges.push(...resourceExchanges);
  }

  return { toolExchanges, resourceExchanges, resolvedUpn, resolvedGroupId };
}

function renderMappingScenario() {
  const prompt = PROMPT_CATALOG.find(p => p.name === mappingPromptName)
    || REFERENCE_PROMPT_CATALOG.find(p => p.name === mappingPromptName);
  const mapSelect = document.getElementById('mappingPromptSelect');
  if (mapSelect && prompt) mapSelect.value = prompt.name;
  document.querySelectorAll('#view-mapping [data-map-prompt]').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.mapPrompt === mappingPromptName);
  });

  if (!prompt || !currentProjection) {
    for (let index = 1; index <= 6; index++)
      document.getElementById(`stage${index}Card`).textContent = 'Loading CSDL projection from the selected MCP...';
    renderRuntimeSequence();
    return;
  }
  document.getElementById('mappingScenarioTitle').textContent = prompt.title;
  const serverUrl = document.getElementById('targetUrlInput').value;
  const tools = currentProjection.tools.filter(tool => prompt.toolBindings.includes(tool.Name));
  const resources = currentProjection.resources.filter(resource => prompt.resourceBindings.includes(resource.UriTemplate));
  const arguments_ = Object.fromEntries(prompt.arguments.map(argument => [argument.name, argument.default]));
  const boundText = prompt.template.replace(/\{([A-Za-z0-9_]+)\}/g, (match, name) =>
    Object.hasOwn(arguments_, name) ? arguments_[name] : match);
  const { toolExchanges, resourceExchanges } = buildScenarioCallExchanges(prompt, arguments_);

  // Stage 1: Canonical CSDL declarations (Standard OData v4.01 $metadata requests)
  renderTileExchanges(
    document.getElementById('stage1Card'),
    [
      'Standard OData v4.01 $metadata (EntitySet: users, EntitySet: groups, Singleton: me):',
      ...prompt.entitySets.map(name => `EntitySet: ${name}`),
      ...(prompt.odataQueries || []).map(q => `OData Query in @Agent.Prompts: GET ${q}`),
      `EntityContainer/@Agent.Prompts: ${prompt.name} (no tool or resource annotations in CSDL)`
    ].join('\n'),
    [
      {
        badge: 'Request 1 (OData $metadata XML)',
        requestTitle: 'GET https://graph.microsoft.com/v1.0/$metadata (Accept: application/xml)',
        request: 'GET https://graph.microsoft.com/v1.0/$metadata HTTP/1.1\nOData-Version: 4.01\nAccept: application/xml',
        responseTitle: `Response: OData CSDL XML EntityContainer & @Agent.Prompts for ${prompt.name} (click to expand)`,
        statusBadge: '200 OK (XML)',
        response: [
          '<edmx:Edmx xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx" Version="4.0">',
          '  <edmx:DataServices>',
          '    <Schema xmlns="http://docs.oasis-open.org/odata/ns/edm" Namespace="Microsoft.Graph">',
          '      <EntityContainer Name="GraphService">',
          `        <!-- EntitySet: ${prompt.entitySets.join(', ')} | Term="Agent.Prompts" Name="${prompt.name}" -->`,
          '      </EntityContainer>',
          '    </Schema>',
          '  </edmx:DataServices>',
          '</edmx:Edmx>'
        ].join('\n')
      },
      {
        badge: 'Request 2 (OData $metadata JSON)',
        requestTitle: 'GET https://graph.microsoft.com/v1.0/$metadata (Accept: application/json)',
        request: 'GET https://graph.microsoft.com/v1.0/$metadata HTTP/1.1\nOData-Version: 4.01\nAccept: application/json',
        responseTitle: 'Response: OData v4.01 CSDL JSON ($Version: "4.01") @Agent.Prompts with OData queries (click to expand)',
        statusBadge: '200 OK (JSON)',
        response: {
          $Version: '4.01',
          $EntityContainer: 'Microsoft.Graph.GraphService',
          'Microsoft.Graph': {
            GraphService: {
              $Kind: 'EntityContainer',
              '@Agent.Prompts': [{
                Name: prompt.name,
                Title: prompt.title,
                Description: prompt.description,
                Arguments: prompt.arguments,
                Messages: [{ Role: 'user', Content: { Type: 'text', Text: prompt.template } }]
              }]
            }
          }
        }
      }
    ]
  );

  // Stage 2: Projected MCP tools and resources
  renderTileExchanges(
    document.getElementById('stage2Card'),
    'Deterministic C# projection exposes MCP 2.0 discovery catalogs for tools, concrete resources, and URI templates without AI rewriting.',
    [
      {
        badge: 'Request 1 (tools/list)',
        requestTitle: 'Discover projected MCP tools for this workflow',
        request: { jsonrpc: '2.0', id: 'proj-tools-list', method: 'tools/list', params: { _meta: { ...MCP_20_META } } },
        responseTitle: `Response: tools/list (${prompt.toolBindings.length} bound tools — click to expand)`,
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'proj-tools-list',
          result: { tools: currentProjection.mcpTools.filter(tool => prompt.toolBindings.includes(tool.name)) }
        }
      },
      {
        badge: 'Request 2 (resources/list)',
        requestTitle: 'Discover projected concrete MCP resources',
        request: { jsonrpc: '2.0', id: 'proj-resources-list', method: 'resources/list', params: { _meta: { ...MCP_20_META } } },
        responseTitle: 'Response: resources/list (concrete URIs — click to expand)',
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'proj-resources-list',
          result: { resources: currentProjection.mcpResources.filter(resource => prompt.resourceBindings.includes(resource.uri)) }
        }
      },
      {
        badge: 'Request 3 (resources/templates/list)',
        requestTitle: 'Discover projected parameterized MCP resource URI templates',
        request: { jsonrpc: '2.0', id: 'proj-templates-list', method: 'resources/templates/list', params: { _meta: { ...MCP_20_META } } },
        responseTitle: 'Response: resources/templates/list (URI templates — click to expand)',
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'proj-templates-list',
          result: { resourceTemplates: currentProjection.mcpResourceTemplates.filter(resource => prompt.resourceBindings.includes(resource.uriTemplate)) }
        }
      }
    ]
  );

  // Stage 3: User request and container prompt
  renderTileExchanges(
    document.getElementById('stage3Card'),
    `User: "${prompt.title}"\nAddresses transcript concern: prompts/list returns signature metadata only; client matches intent and extracts arguments before prompts/get.`,
    [
      {
        badge: 'Request 1 (prompts/list)',
        requestTitle: 'Discover container prompt signatures and required arguments',
        request: { jsonrpc: '2.0', id: 'proj-prompts-list', method: 'prompts/list', params: { _meta: { ...MCP_20_META } } },
        responseTitle: `Response: prompts/list signature for ${prompt.name} (click to expand)`,
        statusBadge: '200 OK',
        response: {
          jsonrpc: '2.0',
          id: 'proj-prompts-list',
          result: {
            prompts: [{
              name: prompt.name,
              title: prompt.title,
              description: prompt.description,
              arguments: prompt.arguments
            }]
          }
        }
      },
      {
        badge: 'Request 2 (User -> Client Intent)',
        requestTitle: `User utterance mapped to ${prompt.name} with extracted arguments`,
        request: {
          userUtterance: `User: "${prompt.title}"`,
          selectedContainerPrompt: prompt.name,
          extractedArguments: arguments_
        },
        responseTitle: 'Response: Constructed prompts/get request parameters (click to expand)',
        statusBadge: 'Matched',
        response: {
          method: 'prompts/get',
          params: { name: prompt.name, arguments: arguments_ }
        }
      }
    ]
  );

  // Stage 4: Standard MCP prompt messages
  renderTileExchanges(
    document.getElementById('stage4Card'),
    'Standard MCP 2.0 prompts/get request and response. Placeholders are substituted deterministically without AI or non-standard fields.',
    [
      {
        badge: 'Request 1 (prompts/get)',
        requestTitle: `POST ${serverUrl} (Mcp-Method: prompts/get, Mcp-Name: ${prompt.name})`,
        request: {
          jsonrpc: '2.0',
          id: `prompt-${prompt.name}`,
          method: 'prompts/get',
          params: { name: prompt.name, arguments: arguments_, _meta: { ...MCP_20_META } }
        },
        responseTitle: 'Response: Standard MCP prompts/get messages[] (click to expand)',
        statusBadge: '200 OK',
        response: {
          result: {
            description: prompt.description,
            messages: [{ role: 'user', content: { type: 'text', text: boundText } }]
          }
        }
      }
    ]
  );

  // Stage 5: Tools and resource reads
  renderTileExchanges(
    document.getElementById('stage5Card'),
    'Multi-step execution: disambiguate names with Graph-valid startswith(...) filters, then read bound user/group resources via resources/read or CLI read_resource.',
    [...toolExchanges, ...resourceExchanges]
  );

  // Stage 6: Cross-resource composition
  renderTileExchanges(
    document.getElementById('stage6Card'),
    `${prompt.description}\n${prompt.entitySets.length > 1
      ? 'Join user organization data and group membership/ownership by stable IDs. Neither entity set alone answers this prompt.'
      : 'Execute the declared single-entity-set workflow.'}`,
    [
      {
        badge: 'Request 1 (Client Composition)',
        requestTitle: prompt.entitySets.length > 1
          ? `Join ${prompt.entitySets.join(' + ')} payloads by stable userPrincipalName / id`
          : `Format ${prompt.entitySets.join('')} response payload for "${prompt.title}"`,
        request: {
          workflow: prompt.name,
          entitySets: prompt.entitySets,
          toolsInvoked: prompt.toolBindings,
          resourcesRead: prompt.resourceBindings,
          joinStrategy: prompt.entitySets.length > 1
            ? 'Intersect user org relationships (manager, peers, directReports) with group membership/ownership by userPrincipalName.'
            : 'Extract requested directory properties from the single entity set.'
        },
        responseTitle: 'Response: Grounded answer & Microsoft Graph trace verification (click to expand)',
        statusBadge: 'Composed',
        response: {
          scenario: prompt.title,
          entitySetsCombined: prompt.entitySets,
          resolvedAnswerSummary: prompt.entitySets.length > 1
            ? ' Avery Adams (reports to Morgan Reed) shares group "Platform Engineering" with peers Alex Wilber (morgan.reed@example.com) and Jordan Lee (jordan.lee@example.com).'
            : 'Resolved target Microsoft Graph directory records for Avery Adams (Manager: Morgan Reed; Peers: Alex Wilber, Jordan Lee, Taylor Morgan).',
          provenance: 'Verified via MCP JSON-RPC responses and standard OData v4.01 requests.'
        }
      }
    ]
  );

  renderRuntimeSequence();
}

function invalidateRuntimeDiscovery() {
  ++runtimeDiscoveryVersion;
  runtimeDiscoverySnapshot = null;
  document.getElementById('refreshRuntimeDiscovery').disabled = false;
  document.getElementById('runtimeDiscoveryStatus').textContent = 'No discovery snapshot captured for the selected MCP.';
  document.getElementById('runtimeDiscoveryDetails').textContent = 'Refresh discovery to inspect the wire metadata.';
}

async function refreshRuntimeDiscovery() {
  const version = ++runtimeDiscoveryVersion;
  const server = document.getElementById('targetUrlInput').value;
  const button = document.getElementById('refreshRuntimeDiscovery');
  button.disabled = true;
  runtimeDiscoverySnapshot = null;
  document.getElementById('runtimeDiscoveryStatus').textContent = `Discovering ${server}... No Graph operations are executed.`;
  document.getElementById('runtimeDiscoveryDetails').textContent = 'Discovery in progress...';
  renderRuntimeSequence();
  const capture = async method => {
    const payload = {
      jsonrpc: '2.0', id: `runtime-${version}-${method}`, method,
      params: { _meta: { ...MCP_20_META } }
    };
    const response = await fetch('/mcp-proxy', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Target-URL': server },
      body: JSON.stringify(payload)
    });
    if (!response.ok) throw new Error(`${method}: proxy HTTP ${response.status}`);
    const audit = await response.json();
    if (audit.statusCode !== 200 || audit.jsonRpcResponse?.error || !audit.jsonRpcResponse?.result)
      throw new Error(`${method}: ${audit.jsonRpcResponse?.error?.message || `MCP HTTP ${audit.statusCode}`}`);
    return { method, request: audit.requestPayload, headers: audit.requestHeaders, response: audit.jsonRpcResponse };
  };
  try {
    const discovery = await capture('server/discover');
    const capabilities = discovery.response.result.capabilities || {};
    if (!discovery.response.result.supportedVersions?.includes(MCP_20_META['io.modelcontextprotocol/protocolVersion']))
      throw new Error('The selected MCP does not advertise the configured protocol version.');
    const methods = [
      ...(Object.hasOwn(capabilities, 'tools') ? ['tools/list'] : []),
      ...(Object.hasOwn(capabilities, 'resources') ? ['resources/list', 'resources/templates/list'] : []),
      ...(Object.hasOwn(capabilities, 'prompts') ? ['prompts/list'] : [])
    ];
    const catalogs = await Promise.all(methods.map(capture));
    if (version !== runtimeDiscoveryVersion || server !== document.getElementById('targetUrlInput').value) return;
    runtimeDiscoverySnapshot = { server, capturedAt: new Date().toISOString(), calls: [discovery, ...catalogs] };
    document.getElementById('runtimeDiscoveryStatus').textContent =
      `Captured ${runtimeDiscoverySnapshot.calls.length} discovery requests from ${server} at ${new Date(runtimeDiscoverySnapshot.capturedAt).toLocaleString()}. This is a diagnostic snapshot; respect the server's cache hints.`;
    document.getElementById('runtimeDiscoveryDetails').textContent = JSON.stringify(runtimeDiscoverySnapshot, null, 2);
  } catch (error) {
    if (version !== runtimeDiscoveryVersion) return;
    document.getElementById('runtimeDiscoveryStatus').textContent = `Discovery failed: ${error.message}`;
    document.getElementById('runtimeDiscoveryDetails').textContent = `Discovery failed: ${error.message}`;
  } finally {
    if (version === runtimeDiscoveryVersion) {
      button.disabled = false;
      renderRuntimeSequence();
    }
  }
}

function renderRuntimeSequence() {
  const serverUrl = document.getElementById('targetUrlInput').value;
  document.getElementById('runtimeDiscoveryServer').textContent = `Selected MCP: ${serverUrl}`;
  const container = document.getElementById('runtimeSequenceTiles');
  container.replaceChildren();
  const prompt = PROMPT_CATALOG.find(item => item.name === mappingPromptName)
    || REFERENCE_PROMPT_CATALOG.find(item => item.name === mappingPromptName);
  const callFor = method => runtimeDiscoverySnapshot?.calls.find(call => call.method === method);
  const discoveryCall = callFor('server/discover');
  const args = prompt ? Object.fromEntries(prompt.arguments.map(argument => [argument.name, argument.default])) : {};
  const text = prompt?.template.replace(/\{([A-Za-z0-9_]+)\}/g, (match, name) => Object.hasOwn(args, name) ? args[name] : match) || '';
  const { toolExchanges, resourceExchanges } = prompt
    ? buildScenarioCallExchanges(prompt, args)
    : { toolExchanges: [], resourceExchanges: [] };
  const operations = currentProjection && prompt
    ? [...currentProjection.tools.filter(tool => prompt.toolBindings.includes(tool.Name)),
      ...currentProjection.resources.filter(resource => prompt.resourceBindings.includes(resource.UriTemplate))]
      .map(binding => `${binding.Name}: ${binding.Operation}`).join('\n')
    : 'Load CSDL bindings to inspect supported operations.';

  const primitiveMethods = [
    {
      method: 'tools/list',
      label: 'Discover registered MCP tools & input schemas',
      fallbackResult: { tools: currentProjection?.mcpTools || [] }
    },
    {
      method: 'resources/list',
      label: 'Discover concrete MCP resources (users://me, groups://all)',
      fallbackResult: { resources: currentProjection?.mcpResources || [] }
    },
    {
      method: 'resources/templates/list',
      label: 'Discover parameterized URI templates (users://{userId}, groups://{groupId})',
      fallbackResult: { resourceTemplates: currentProjection?.mcpResourceTemplates || [] }
    },
    {
      method: 'prompts/list',
      label: 'Discover container prompt signatures & arguments (no message body yet)',
      fallbackResult: {
        prompts: (currentProjection?.prompts || []).map(p => ({
          name: p.name, title: p.title, description: p.description, arguments: p.arguments
        }))
      }
    }
  ];

  const steps = [
    {
      title: '1. Client -> MCP: server/discover',
      note: discoveryCall ? 'Captured live discovery handshake' : 'Discovery handshake (click Refresh discovery only for live wire capture)',
      summary: 'Attach to the selected stateless HTTP MCP. Fetch supportedVersions, capabilities, server identity and cache hints before choosing supported operations.',
      exchanges: [
        {
          badge: 'Request 1 (server/discover)',
          requestTitle: `POST ${serverUrl} (Mcp-Method: server/discover)`,
          request: discoveryCall ? discoveryCall.request : {
            jsonrpc: '2.0',
            id: 'runtime-server-discover',
            method: 'server/discover',
            params: { _meta: { ...MCP_20_META } }
          },
          responseTitle: 'Response: server/discover capabilities & protocol version (click to expand)',
          statusBadge: discoveryCall ? '200 OK (Live)' : '200 OK (Projected)',
          response: discoveryCall ? discoveryCall.response : {
            jsonrpc: '2.0',
            id: 'runtime-server-discover',
            result: {
              supportedVersions: ['2026-07-28'],
              capabilities: { logging: {}, prompts: {}, resources: {}, tools: {} },
              ttlMs: 0,
              cacheScope: 'private',
              resultType: 'complete'
            }
          }
        }
      ]
    },
    {
      title: '2. Client -> MCP: primitive discovery (4 catalog requests)',
      note: 'tools/list, resources/list, resources/templates/list, and prompts/list',
      summary: 'Addresses transcript concern: prompts/list returns only prompt signatures (name, title, description, arguments), not rendered messages. All 4 catalog requests can run in parallel.',
      exchanges: primitiveMethods.map((item, idx) => {
        const captured = callFor(item.method);
        return {
          badge: `Request ${idx + 1} (${item.method})`,
          requestTitle: item.label,
          request: captured ? captured.request : {
            jsonrpc: '2.0',
            id: `runtime-${item.method}`,
            method: item.method,
            params: { _meta: { ...MCP_20_META } }
          },
          responseTitle: `Response: ${item.method} catalog payload (click to expand)`,
          statusBadge: captured ? '200 OK (Live)' : '200 OK (Projected)',
          response: captured ? captured.response : {
            jsonrpc: '2.0',
            id: `runtime-${item.method}`,
            result: item.fallbackResult
          }
        };
      })
    },
    {
      title: '3. User -> client: intent, argument extraction & guardrail',
      note: 'Free-text utterance routing vs user-selected slash command (transcript concern)',
      summary: prompt
        ? `User workflow: ${prompt.title}\nThe client/LLM matches the user question against discovered capabilities, extracts arguments, or rejects out-of-domain questions without calling MCP tools.`
        : 'Load the selected container prompt to inspect its planned workflow.',
      exchanges: [
        {
          badge: 'Request 1 (Supported Utterance)',
          requestTitle: `User asks: "${prompt ? prompt.title : 'Whom Avery reports to?'}"`,
          request: {
            channel: 'User -> Client/LLM',
            utterance: prompt ? prompt.title : 'Whom Avery reports to?',
            discoveredPromptsAvailable: PROMPT_CATALOG.length,
            discoveredToolsAvailable: currentProjection?.tools.length || 5
          },
          responseTitle: 'Response: Client intent match & extracted arguments (click to expand)',
          statusBadge: 'Matched',
          response: {
            matchedPrompt: prompt ? prompt.name : 'whom_olga_reports_to',
            extractedArguments: args,
            plannedTools: prompt ? prompt.toolBindings : ['find_user', 'user_profile'],
            plannedResources: prompt ? prompt.resourceBindings : ['users://{userId}', 'users://me']
          }
        },
        {
          badge: 'Request 2 (Out-of-Domain Guardrail)',
          requestTitle: 'User asks unsupported question: "What is the weather tomorrow?" (transcript concern)',
          request: {
            channel: 'User -> Client/LLM',
            utterance: 'What is the weather tomorrow?',
            discoveredDomains: ['Microsoft.Graph.User (users, me)', 'Microsoft.Graph.Group (groups)']
          },
          responseTitle: 'Response: Immediate rejection with 0 MCP calls (click to expand)',
          statusBadge: 'Rejected (0 Calls)',
          response: {
            decision: 'reject_unsupported_intent',
            mcpCallsIssued: 0,
            assistantReply: 'The connected Microsoft Graph MCP only provides directory data for users and groups and cannot answer weather queries.'
          }
        }
      ]
    },
    {
      title: '4. Optional client -> MCP: prompts/get',
      note: 'Deterministic template parameter substitution without AI rewriting',
      summary: 'Addresses transcript concern: prompts/get substitutes declared arguments into messages[].content.text deterministically and does not execute tools on the server.',
      exchanges: [
        {
          badge: 'Request 1 (prompts/get)',
          requestTitle: `POST ${serverUrl} (Mcp-Method: prompts/get, Mcp-Name: ${prompt ? prompt.name : 'user_peers_in_group'})`,
          request: {
            jsonrpc: '2.0',
            id: 'runtime-prompts-get-1',
            method: 'prompts/get',
            params: {
              name: prompt ? prompt.name : 'user_peers_in_group',
              arguments: args,
              _meta: { ...MCP_20_META }
            }
          },
          responseTitle: 'Response: Rendered standard MCP prompt messages[] (click to expand)',
          statusBadge: '200 OK',
          response: {
            jsonrpc: '2.0',
            id: 'runtime-prompts-get-1',
            result: {
              description: prompt ? prompt.description : '',
              messages: [{ role: 'user', content: { type: 'text', text } }]
            }
          }
        }
      ]
    },
    {
      title: '5. Tool round trips: client -> MCP -> Graph -> client',
      note: 'Multi-turn identity disambiguation & Graph-valid startswith(...) filter execution',
      summary: `Declared operations:\n${operations}\nDisambiguates display names first (e.g., multiple "Avery" records) before fetching profile or group relationships.`,
      exchanges: toolExchanges.length ? toolExchanges : [
        {
          badge: 'Request 1 (tools/call)',
          requestTitle: 'No separate search tool required; workflow reads projected resources',
          request: { note: 'Proceeds directly to resource reads.' },
          responseTitle: 'Response: Tool dispatch status (click to expand)',
          statusBadge: 'Skipped',
          response: { status: 'Uses resource read bindings in Step 6.' }
        }
      ]
    },
    {
      title: '6. Resource round trips: client -> MCP -> Graph -> client',
      note: 'Direct MCP resources/read vs CLI read_resource tool adapter',
      summary: 'After resolving concrete identifiers (UPN or Group GUID), the client reads each bound MCP resource URI. Both access forms share the same C# operation dispatcher.',
      exchanges: resourceExchanges.length ? resourceExchanges : [
        {
          badge: 'Request 1 (resources/read)',
          requestTitle: 'Read users://me',
          request: { jsonrpc: '2.0', id: 'res-read-default', method: 'resources/read', params: { uri: 'users://me' } },
          responseTitle: 'Response: Resource payload (click to expand)',
          statusBadge: '200 OK',
          response: { contents: [{ uri: 'users://me', mimeType: 'application/json' }] }
        }
      ]
    },
    {
      title: '7. Client: validate and compose returned data',
      note: 'Client-side join across resolved user and group payloads',
      summary: prompt
        ? `${prompt.description}\n${prompt.entitySets.length > 1
          ? 'Combine the user and group resource results using stable IDs; neither source alone supplies the requested relationship.'
          : 'Use the results needed by this single-entity-set workflow.'}`
        : 'Workflow unavailable.',
      exchanges: [
        {
          badge: 'Request 1 (Client Join / Validation)',
          requestTitle: prompt && prompt.entitySets.length > 1
            ? `Join ${prompt.entitySets.join(' + ')} records on stable userPrincipalName / id`
            : 'Validate returned Microsoft Graph entity fields and provenance',
          request: {
            operation: 'client_side_composition',
            entitySets: prompt ? prompt.entitySets : ['users'],
            joinKey: 'userPrincipalName / id',
            inputs: [
              'users://avery.adams@example.com -> manager: morgan.reed@example.com, peers: [morgan.reed@example.com, jordan.lee@example.com, taylor.morgan@example.com]',
              ...(prompt && prompt.entitySets.includes('groups')
                ? ['groups://11111111-2222-3333-4444-555555555555 -> owners: [morgan.reed@example.com, avery.adams@example.com], members: [avery.adams@example.com, morgan.reed@example.com, jordan.lee@example.com]']
                : [])
            ]
          },
          responseTitle: 'Response: Composed dataset & matched entities (click to expand)',
          statusBadge: 'Validated',
          response: {
            matchedRecords: [
              { displayName: 'Alex Wilber', userPrincipalName: 'morgan.reed@example.com', manager: 'Morgan Reed', group: 'Platform Engineering' },
              { displayName: 'Jordan Lee', userPrincipalName: 'jordan.lee@example.com', manager: 'Morgan Reed', group: 'Platform Engineering' }
            ],
            missingData: false
          }
        }
      ]
    },
    {
      title: '8. Client/LLM -> user: synthesize the final answer',
      note: 'Grounded natural-language response + Graph HTTP trace verification',
      summary: 'Projection is deterministic; LLM selection, orchestration, synthesis, and changing Graph directory data are reported with explicit live/mock provenance.',
      exchanges: [
        {
          badge: 'Request 1 (Answer Synthesis)',
          requestTitle: 'Client/LLM formats grounded answer for the user',
          request: {
            userQuestion: prompt ? prompt.title : 'Which user peers belong to a group?',
            groundedFactsFromMcp: {
              user: 'Avery Adams (avery.adams@example.com)',
              manager: 'Morgan Reed (morgan.reed@example.com)',
              peersInGroup: ['Alex Wilber (morgan.reed@example.com)', 'Jordan Lee (jordan.lee@example.com)']
            }
          },
          responseTitle: 'Response: Final assistant message to user (click to expand)',
          statusBadge: 'Complete',
          response: {
            role: 'assistant',
            text: prompt && prompt.entitySets.length > 1
              ? 'Avery Adams reports to Morgan Reed. Among her peers under Morgan Reed, Alex Wilber (morgan.reed@example.com) and Jordan Lee (jordan.lee@example.com) are members of the "Platform Engineering" group.'
              : 'Avery Adams (avery.adams@example.com) reports to Morgan Reed (morgan.reed@example.com). Her peer colleagues are Alex Wilber, Jordan Lee, and Taylor Morgan.'
          }
        },
        {
          badge: 'Request 2 (OData v4.01 Trace Verification)',
          requestTitle: 'Executed OData v4.01 HTTP GET requests against Microsoft Graph',
          request: [
            'GET https://graph.microsoft.com/v1.0/$metadata HTTP/1.1\nOData-Version: 4.01\nAccept: application/json',
            "GET https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'Avery Adams')&$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$top=10 HTTP/1.1\nOData-Version: 4.01\nAccept: application/json;odata.metadata=minimal",
            "GET https://graph.microsoft.com/v1.0/users('avery.adams@example.com')/manager?$select=id,displayName,userPrincipalName,jobTitle,department HTTP/1.1\nOData-Version: 4.01\nAccept: application/json;odata.metadata=minimal",
            "GET https://graph.microsoft.com/v1.0/users('morgan.reed@example.com')/directReports?$select=id,displayName,userPrincipalName,jobTitle,department HTTP/1.1\nOData-Version: 4.01\nAccept: application/json;odata.metadata=minimal",
            ...(prompt && prompt.entitySets.includes('groups')
              ? [
                  "GET https://graph.microsoft.com/v1.0/groups?$filter=startswith(displayName,'Platform Engineering')&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility&$top=25 HTTP/1.1\nOData-Version: 4.01\nAccept: application/json;odata.metadata=minimal",
                  "GET https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')/members?$select=id,displayName,userPrincipalName,jobTitle,department HTTP/1.1\nOData-Version: 4.01\nAccept: application/json;odata.metadata=minimal"
                ]
              : [])
          ].join('\n\n'),
          responseTitle: 'Response: OData v4.01 HTTP 200 OK responses with @context (click to expand)',
          statusBadge: '200 OK (OData 4.01)',
          response: [
            { method: 'GET', url: 'https://graph.microsoft.com/v1.0/$metadata', odataVersion: '4.01', statusCode: 200 },
            { method: 'GET', url: "https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'Avery Adams')&$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$top=10", '@context': 'https://graph.microsoft.com/v1.0/$metadata#users', statusCode: 200 },
            { method: 'GET', url: "https://graph.microsoft.com/v1.0/users('avery.adams@example.com')/manager?$select=id,displayName,userPrincipalName,jobTitle,department", '@context': 'https://graph.microsoft.com/v1.0/$metadata#users/$entity', statusCode: 200 },
            { method: 'GET', url: "https://graph.microsoft.com/v1.0/users('morgan.reed@example.com')/directReports?$select=id,displayName,userPrincipalName,jobTitle,department", '@context': 'https://graph.microsoft.com/v1.0/$metadata#users', statusCode: 200 },
            ...(prompt && prompt.entitySets.includes('groups')
              ? [
                  { method: 'GET', url: "https://graph.microsoft.com/v1.0/groups?$filter=startswith(displayName,'Platform Engineering')&$select=id,displayName,description,mail,mailNickname,groupTypes,securityEnabled,visibility&$top=25", '@context': 'https://graph.microsoft.com/v1.0/$metadata#groups', statusCode: 200 },
                  { method: 'GET', url: "https://graph.microsoft.com/v1.0/groups('11111111-2222-3333-4444-555555555555')/members?$select=id,displayName,userPrincipalName,jobTitle,department", '@context': 'https://graph.microsoft.com/v1.0/$metadata#users', statusCode: 200 }
                ]
              : [])
          ]
        }
      ]
    }
  ];

  for (const step of steps) {
    const tile = document.createElement('section');
    tile.className = 'pipeline-stage';
    const heading = document.createElement('h4');
    heading.textContent = step.title;
    const kind = document.createElement('p');
    kind.className = 'runtime-step-kind';
    kind.textContent = step.note;
    const body = document.createElement('div');
    body.className = 'stage-card';
    renderTileExchanges(body, step.summary, step.exchanges);
    tile.append(heading, kind, body);
    container.appendChild(tile);
  }
}


function escapeHtml(text) {
  if (!text) return '';
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

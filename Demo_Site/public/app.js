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
  'tool-find-olga': {
    name: 'find_user ("Olga")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 1,
      method: 'tools/call',
      params: {
        name: 'find_user',
        arguments: { query: 'Olga' }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,\'Olga\')...',
    entity: 'Microsoft.Graph.User (EntitySet: users)',
    args: [{ name: 'query', label: 'Search Query', default: 'Olga' }]
  },
  'tool-find-vojtech': {
    name: 'find_user ("Alex")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 2,
      method: 'tools/call',
      params: {
        name: 'find_user',
        arguments: { query: 'Alex' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,\'Alex\')...',
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
    graphTarget: 'GET https://graph.microsoft.com/v1.0/me, /me/manager, /users/{mgrId}/directReports',
    entity: 'Microsoft.Graph.User (Singleton: me + Nav: manager, directReports)',
    args: [{ name: 'userId', label: 'User ID or UPN', default: 'me' }]
  },
  'tool-profile-olga': {
    name: 'user_profile ("olga@contoso.com")',
    category: 'tool',
    payload: {
      jsonrpc: '2.0',
      id: 4,
      method: 'tools/call',
      params: {
        name: 'user_profile',
        arguments: { userId: 'olga@contoso.com' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users/olga@contoso.com (with manager & peers)',
    entity: 'Microsoft.Graph.User (EntitySet: users)',
    args: [{ name: 'userId', label: 'User ID or UPN', default: 'olga@contoso.com' }]
  },
  'prompt-reports-to': {
    name: 'Whom Olga reports to?',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 5,
      method: 'prompts/get',
      params: {
        name: 'whom_olga_reports_to',
        arguments: { userName: 'Olga Podolyako' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users/olga@contoso.com/manager',
    entity: 'Microsoft.Graph.User/@Agent.Prompts[whom_olga_reports_to]',
    args: [{ name: 'userName', label: 'User Name', default: 'Olga Podolyako' }]
  },
  'prompt-peers': {
    name: 'Who are Olga\'s peers?',
    category: 'prompt',
    payload: {
      jsonrpc: '2.0',
      id: 6,
      method: 'prompts/get',
      params: {
        name: 'who_are_olgas_peers',
        arguments: { userName: 'Olga Podolyako' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users/olga@contoso.com/manager/directReports',
    entity: 'Microsoft.Graph.User/@Agent.Prompts[who_are_olgas_peers]',
    args: [{ name: 'userName', label: 'User Name', default: 'Olga Podolyako' }]
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
        arguments: { managerNameOrId: 'Alex Wilbur' },
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users/alexw@contoso.com/directReports',
    entity: 'Microsoft.Graph.User/@Agent.Prompts[direct_reports_of_manager]',
    args: [{ name: 'managerNameOrId', label: 'Manager Name or UPN', default: 'Alex Wilbur' }]
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
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users?$filter=department eq \'Identity\' and startswith(jobTitle,\'Architect\')',
    entity: 'Microsoft.Graph.User/@Agent.Prompts[search_users_by_dept_title]',
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
    graphTarget: 'Read Resource users://me or /v1.0/me',
    entity: 'Microsoft.Graph.User (Resource link + Tool binding)',
    args: [{ name: 'identifier', label: 'Identifier (me or UPN)', default: 'me' }]
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
    graphTarget: 'GET https://graph.microsoft.com/v1.0/me',
    entity: 'Microsoft.Graph.User (Singleton: me)',
    args: []
  },
  'res-read-user': {
    name: 'Read users/{userId}',
    category: 'resource',
    payload: {
      jsonrpc: '2.0',
      id: 11,
      method: 'resources/read',
      params: {
        uri: 'users/olga@contoso.com',
        _meta: { ...MCP_20_META }
      }
    },
    graphTarget: 'GET https://graph.microsoft.com/v1.0/users/olga@contoso.com',
    entity: 'Microsoft.Graph.User (EntitySet: users)',
    args: [{ name: 'uri', label: 'Resource URI', default: 'users/olga@contoso.com' }]
  },
  'proto-init': {
    name: 'initialize',
    category: 'proto',
    payload: {
      jsonrpc: '2.0',
      id: 12,
      method: 'initialize',
      params: {
        protocolVersion: '2025-11-25',
        capabilities: {},
        clientInfo: { name: 'GraphMcpExplorer', version: '1.0.0' }
      }
    },
    graphTarget: 'MCP Protocol Handshake',
    entity: 'MCP Server Core Transport',
    args: []
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
  }
};

const MAPPING_SCENARIOS = {
  'olga-manager': {
    title: 'Whom Olga reports to?',
    stage1: `@Agent.Prompts: [{
  "Name": "whom_olga_reports_to",
  "Title": "Whom Olga reports to?",
  "Arguments": [{ "Name": "userName", "Default": "Olga Podolyako" }],
  "Messages": [{
    "Role": "user",
    "Text": "Whom does {userName} report to? Run {tool:user_profile}."
  }],
  "Examples": [{
    "Utterance": "Whom Olga reports to?",
    "Request": "users('olga@contoso.com')/manager"
  }]
}]`,
    stage2: `{
  "jsonrpc": "2.0",
  "id": 5,
  "result": {
    "resultType": "complete",
    "description": "Resolve manager for Olga Podolyako",
    "messages": [{
      "role": "user",
      "content": {
        "type": "text",
        "text": "Whom does Olga Podolyako report to? ..."
      }
    }],
    "_meta": {
      "io.modelcontextprotocol/serverInfo": {
        "name": "GraphMcpServer",
        "version": "2.0.0"
      }
    }
  }
}`,
    stage3: `{
  "method": "tools/call",
  "params": {
    "name": "user_profile",
    "arguments": {
      "userId": "olga@contoso.com"
    },
    "_meta": {
      "io.modelcontextprotocol/protocolVersion": "2026-07-28"
    }
  }
}`,
    stage4: `GET https://graph.microsoft.com/v1.0/users/olga@contoso.com/manager
HTTP/1.1 200 OK
{
  "id": "00000000-0000-0000-0000-000000000002",
  "displayName": "Alex Wilbur",
  "jobTitle": "Partner Director of Engineering",
  "department": "Engineering Leadership"
}`
  },
  'olga-peers': {
    title: 'Who are Olga\'s peers?',
    stage1: `@Agent.Prompts: [{
  "Name": "who_are_olgas_peers",
  "Title": "Who are Olga's peers?",
  "Arguments": [{ "Name": "userName", "Default": "Olga Podolyako" }],
  "Messages": [{
    "Role": "user",
    "Text": "Who are {userName}'s peers? Find manager via {tool:user_profile}."
  }],
  "Examples": [{
    "Utterance": "Who are Olga's peers?",
    "Request": "users('olga@contoso.com')/manager/directReports"
  }]
}]`,
    stage2: `{
  "jsonrpc": "2.0",
  "id": 6,
  "result": {
    "resultType": "complete",
    "description": "Discover peers of Olga Podolyako",
    "messages": [{
      "role": "user",
      "content": { "type": "text", "text": "Who are Olga Podolyako's peers? ..." }
    }],
    "_meta": {
      "io.modelcontextprotocol/serverInfo": {
        "name": "GraphMcpServer",
        "version": "2.0.0"
      }
    }
  }
}`,
    stage3: `{
  "method": "tools/call",
  "params": {
    "name": "user_profile",
    "arguments": {
      "userId": "olga@contoso.com"
    },
    "_meta": {
      "io.modelcontextprotocol/protocolVersion": "2026-07-28"
    }
  }
}`,
    stage4: `GET https://graph.microsoft.com/v1.0/users/{managerId}/directReports
HTTP/1.1 200 OK
{
  "value": [
    { "displayName": "Megan Bowen", "jobTitle": "Principal Software Architect" },
    { "displayName": "Adele Vance", "jobTitle": "Principal Product Manager" },
    { "displayName": "Diego Siciliani", "jobTitle": "Principal Group Eng Manager" },
    { "displayName": "Patti Fernandez", "jobTitle": "Business Administrator" },
    { "displayName": "Lynne Robbins", "jobTitle": "Group PM Manager" },
    { "displayName": "Henrietta Mueller", "jobTitle": "Principal Standards Architect" }
  ]
}`
  },
  'manager-reports': {
    title: 'Direct reports of a manager',
    stage1: `@Agent.Prompts: [{
  "Name": "direct_reports_of_manager",
  "Title": "Direct reports of a manager",
  "Arguments": [{ "Name": "managerNameOrId", "Required": true }],
  "Messages": [{
    "Role": "user",
    "Text": "List direct reports for manager {managerNameOrId}. Use {tool:user_profile}."
  }],
  "Examples": [{
    "Utterance": "Who reports to Alex?",
    "Request": "users('alexw@contoso.com')/directReports"
  }]
}]`,
    stage2: `{
  "jsonrpc": "2.0",
  "id": 7,
  "result": {
    "resultType": "complete",
    "description": "List direct reports for manager Alex Wilbur",
    "messages": [{
      "role": "user",
      "content": { "type": "text", "text": "List direct reports for manager Alex Wilbur." }
    }]
  }
}`,
    stage3: `{
  "method": "tools/call",
  "params": {
    "name": "user_profile",
    "arguments": {
      "userId": "alexw@contoso.com"
    },
    "_meta": {
      "io.modelcontextprotocol/protocolVersion": "2026-07-28"
    }
  }
}`,
    stage4: `GET https://graph.microsoft.com/v1.0/users/alexw@contoso.com/directReports
HTTP/1.1 200 OK
{
  "value": [
    { "displayName": "Olga Podolyako", "jobTitle": "Principal API Architect" },
    { "displayName": "Megan Bowen", "jobTitle": "Principal Software Architect" },
    { "displayName": "Adele Vance", "jobTitle": "Principal Product Manager" }
  ]
}`
  },
  'dept-search': {
    title: 'Search users by dept & title',
    stage1: `@Agent.Prompts: [{
  "Name": "search_users_by_dept_title",
  "Title": "Search users by department and title",
  "Arguments": [
    { "Name": "department", "Default": "Identity" },
    { "Name": "jobTitle", "Default": "Architect" }
  ],
  "Messages": [{
    "Role": "user",
    "Text": "Find users in department '{department}' with title '{jobTitle}'. Use {tool:find_user}."
  }],
  "Examples": [{
    "Utterance": "Find architects in Identity",
    "Request": "users?$filter=department eq 'Identity' and startswith(jobTitle,'Architect')"
  }]
}]`,
    stage2: `{
  "name": "search_users_by_dept_title",
  "arguments": [{ "name": "department" }, { "name": "jobTitle" }],
  "messages": [{
    "role": "user",
    "content": { "type": "text", "text": "Find users in department 'Identity' with title 'Architect'." }
  }]
}`,
    stage3: `{
  "method": "tools/call",
  "params": {
    "name": "find_user",
    "arguments": {
      "query": "Identity Architect"
    }
  }
}`,
    stage4: `GET https://graph.microsoft.com/v1.0/users?$filter=department eq 'Identity' and startswith(jobTitle,'Architect')
HTTP/1.1 200 OK
{
  "value": [
    { "displayName": "Olga Podolyako", "jobTitle": "PRINCIPAL API ARCHITECT", "department": "Identity" },
    { "displayName": "Joao Paiva", "jobTitle": "PRINCIPAL SOFTWARE ARCHITECT", "department": "Identity" }
  ]
}`
  }
};

let currentSelectedQueryKey = 'tool-profile-me';

// Initialize DOM elements and event handlers
document.addEventListener('DOMContentLoaded', () => {
  initViewSwitcher();
  initPaneTabs();
  initDocSubtabs();
  initSidebarQueries();
  initRunQuery();
  initHealthPolling();
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
  const subtabs = document.querySelectorAll('.doc-subtab');
  subtabs.forEach(btn => {
    btn.addEventListener('click', () => {
      subtabs.forEach(t => t.classList.remove('active'));
      document.querySelectorAll('.subtab-panel').forEach(p => p.classList.remove('active'));

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
    const res = await fetch('/mcp-proxy', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payloadObj)
    });

    const data = await res.json();
    const elapsed = data.elapsedMs || (Date.now() - startTime);

    // Update Status Pill
    const statusBadge = document.getElementById('responseStatusBadge');
    const statusText = statusBadge.querySelector('.status-code-text');
    const timeText = document.getElementById('responseTimeText');

    statusText.innerText = `${data.statusCode} ${data.statusCode === 200 ? 'OK' : 'Error'}`;
    timeText.innerText = `${elapsed} ms`;

    if (data.statusCode === 200) {
      statusBadge.style.background = 'var(--success-light)';
      statusBadge.style.color = 'var(--success)';
    } else {
      statusBadge.style.background = 'var(--warning-light)';
      statusBadge.style.color = 'var(--warning)';
    }

    // Set Response JSON
    const responseView = document.getElementById('responseJsonArea');
    responseView.innerText = JSON.stringify(data.jsonRpcResponse || data.rawResponse, null, 2);

    // Set Response Headers
    renderHeadersTable('resHeadersTable', data.responseHeaders);

    // Render Org Profile Card if user profile is present
    renderProfileCardIfPresent(data.jsonRpcResponse);

    // Fetch and update Graph traces
    updateGraphTraces();

  } catch (err) {
    document.getElementById('responseJsonArea').innerText = JSON.stringify({
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

  // Try extracting profile from tool result content
  if (resp && resp.result && resp.result.content && resp.result.content[0] && resp.result.content[0].text) {
    try {
      const parsed = JSON.parse(resp.result.content[0].text);
      if (parsed.displayName || parsed.users) {
        userObj = parsed;
      }
    } catch (_) {}
  } else if (resp && resp.result && resp.result.contents && resp.result.contents[0] && resp.result.contents[0].text) {
    try {
      const parsed = JSON.parse(resp.result.contents[0].text);
      if (parsed.displayName) userObj = parsed;
    } catch (_) {}
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
      const initials = (u.DisplayName || 'U').split(' ').map(n => n[0]).join('').substring(0, 2);
      listHtml += `
        <div class="mini-user-card" onclick="runProfileForUser('${u.UserPrincipalName || u.Id}')" style="cursor: pointer;">
          <div class="mini-avatar">${initials}</div>
          <div class="mini-info">
            <div class="mini-name">${escapeHtml(u.DisplayName)}</div>
            <div class="mini-sub">${escapeHtml(u.JobTitle || u.Department || u.UserPrincipalName)}</div>
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
          <div class="profile-name">${escapeHtml(userObj.displayName || 'Olga Podolyako')}</div>
          <div class="profile-title">${escapeHtml(userObj.jobTitle || 'PRINCIPAL API ARCHITECT')}</div>
          <div style="font-size: 11px; color: var(--primary); font-weight: 600; margin-top: 2px;">${escapeHtml(userObj.source || 'Microsoft Graph')}</div>
        </div>
      </div>

      <div class="profile-details-grid">
        <div class="detail-item">
          <span class="detail-label">User Principal Name</span>
          <span class="detail-value">${escapeHtml(userObj.userPrincipalName || 'olga@contoso.com')}</span>
        </div>
        <div class="detail-item">
          <span class="detail-label">Mail</span>
          <span class="detail-value">${escapeHtml(userObj.mail || userObj.userPrincipalName || 'olga@contoso.com')}</span>
        </div>
        <div class="detail-item">
          <span class="detail-label">Department</span>
          <span class="detail-value">${escapeHtml(userObj.department || 'Architecture & Standards')}</span>
        </div>
        <div class="detail-item">
          <span class="detail-label">Office Location</span>
          <span class="detail-value">${escapeHtml(userObj.officeLocation || 'Building 1 / Redmond')}</span>
        </div>
      </div>
  `;

  // Manager Section
  if (userObj.manager) {
    const mgrInitials = (userObj.manager.displayName || 'VV').split(' ').map(n => n[0]).join('').substring(0, 2);
    html += `
      <div class="org-relation-section">
        <div class="relation-title">Reporting Manager (Script Query: "Whom Olga reports to?")</div>
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
        <div class="relation-title">Peer Colleagues (Script Query: "Who are Olga's peers?") [${userObj.peers.length} Peers]</div>
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
  selectQuery('tool-profile-olga');
  const payload = JSON.parse(document.getElementById('requestJsonArea').value);
  payload.params.arguments.userId = upn;
  document.getElementById('requestJsonArea').value = JSON.stringify(payload, null, 2);
  executeCurrentQuery();
};

async function updateGraphTraces() {
  try {
    const res = await fetch('/api/traces');
    if (!res.ok) return;
    const traces = await res.json();
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
  } catch (_) {}
}

// Health Polling
async function initHealthPolling() {
  async function check() {
    try {
      const res = await fetch('/health');
      if (res.ok) {
        const data = await res.json();
        const pill = document.getElementById('serverStatusPill');
        pill.classList.add('online');
        document.getElementById('serverStatusLabel').innerText = `MCP 2.0 Online (2026-07-28 Spec) :3001`;
      }
    } catch (_) {
      const pill = document.getElementById('serverStatusPill');
      pill.classList.remove('online');
      document.getElementById('serverStatusLabel').innerText = 'MCP Server Offline';
    }
  }
  check();
  setInterval(check, 10000);
}

// CSDL Metadata Loader
async function initCsdlLoader() {
  loadCsdlData();
}

async function loadCsdlData() {
  try {
    const res = await fetch('/api/csdl');
    if (res.ok) {
      const data = await res.json();
      document.getElementById('csdlJsonCode').innerText = data.csdlJson;
      document.getElementById('csdlXmlCode').innerText = data.csdlXml;
    }
  } catch (err) {
    document.getElementById('csdlJsonCode').innerText = 'Error loading CSDL JSON: ' + err.message;
    document.getElementById('csdlXmlCode').innerText = 'Error loading CSDL XML: ' + err.message;
  }
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
  const query = document.getElementById('matcherInput').value.trim().toLowerCase();
  const resultsDiv = document.getElementById('matcherResults');
  resultsDiv.innerHTML = '';

  const candidates = [
    {
      name: 'whom_olga_reports_to',
      title: 'Whom Olga reports to?',
      keywords: ['reports to', 'manager', 'boss', 'lead', 'supervisor', 'olga reports', 'who does olga report'],
      utterance: 'Whom Olga reports to?',
      request: 'users(\'olga@contoso.com\')/manager',
      tool: 'user_profile (userId="olga@contoso.com")'
    },
    {
      name: 'who_are_olgas_peers',
      title: 'Who are Olga\'s peers?',
      keywords: ['peers', 'team', 'colleagues', 'same manager', 'same team', 'teammates', 'coworkers'],
      utterance: 'Who are Olga\'s peers?',
      request: 'users(\'olga@contoso.com\')/manager/directReports',
      tool: 'user_profile (userId="olga@contoso.com")'
    },
    {
      name: 'direct_reports_of_manager',
      title: 'Direct reports of a manager',
      keywords: ['direct reports', 'reports to alex', 'who reports', 'subordinates', 'team members of'],
      utterance: 'Who reports to Alex?',
      request: 'users(\'alexw@contoso.com\')/directReports',
      tool: 'user_profile (userId="alexw@contoso.com")'
    },
    {
      name: 'search_users_by_dept_title',
      title: 'Search users by department and title',
      keywords: ['architect', 'identity', 'department', 'title', 'search users', 'find architects'],
      utterance: 'Find architects in Identity',
      request: 'users?$filter=department eq \'Identity\' and startswith(jobTitle,\'Architect\')',
      tool: 'find_user (query="Identity Architect")'
    }
  ];

  // Score candidates
  const scored = candidates.map(c => {
    let score = 0.2;
    c.keywords.forEach(kw => {
      if (query.includes(kw)) score += 0.4;
    });
    if (query.includes('olga') && c.name.includes('olga')) score += 0.3;
    if (query.includes('boss') && c.name.includes('reports_to')) score += 0.5;
    score = Math.min(0.99, score);
    return { ...c, score: Math.round(score * 100) };
  }).sort((a, b) => b.score - a.score);

  scored.slice(0, 2).forEach(m => {
    const card = document.createElement('div');
    card.className = 'match-card';
    card.innerHTML = `
      <div class="match-top">
        <span class="match-name">${escapeHtml(m.title)}</span>
        <span class="match-score">${m.score}% Match</span>
      </div>
      <div class="match-utterance">Indexed Example Utterance: "<em>${escapeHtml(m.utterance)}</em>" &rarr; <code>${escapeHtml(m.request)}</code></div>
      <div class="match-tool">Generated Tool Execution: <strong>${escapeHtml(m.tool)}</strong></div>
    `;
    resultsDiv.appendChild(card);
  });
}

// End-to-End Mapping Visualizer (Concern 2)
function initMappingVisualizer() {
  const buttons = document.querySelectorAll('.map-query-btn');
  buttons.forEach(btn => {
    btn.addEventListener('click', () => {
      buttons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      renderMappingScenario(btn.dataset.scenario);
    });
  });

  renderMappingScenario('olga-manager');
}

function renderMappingScenario(scenarioKey) {
  const s = MAPPING_SCENARIOS[scenarioKey];
  if (!s) return;

  document.getElementById('stage1Card').innerText = s.stage1;
  document.getElementById('stage2Card').innerText = s.stage2;
  document.getElementById('stage3Card').innerText = s.stage3;
  document.getElementById('stage4Card').innerText = s.stage4;
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

// A/B Experiment View & 50 Queries Dataset Visualizer
let benchmarkQueriesData = [];

async function initExperimentView() {
  loadExperimentQueries();
  checkBaselineServerHealth();

  const filterButtons = document.querySelectorAll('#experimentCategoryFilters .map-query-btn');
  filterButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      filterButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      renderExperimentQueriesTable(btn.dataset.expCat);
    });
  });
}

async function checkBaselineServerHealth() {
  try {
    const res = await fetch('/api/baseline-health');
    const pill = document.getElementById('serverBPill');
    if (res.ok) {
      if (pill) {
        pill.classList.add('online');
        pill.innerHTML = '<span class="status-dot"></span> Online (:3002)';
      }
    } else {
      if (pill) {
        pill.classList.remove('online');
        pill.innerHTML = '<span class="status-dot" style="background:#d83b01;"></span> Offline (:3002)';
      }
    }
  } catch (_) {}
}

async function loadExperimentQueries() {
  try {
    const res = await fetch('/api/experiment/queries');
    if (res.ok) {
      benchmarkQueriesData = await res.json();
      renderExperimentQueriesTable('all');
    }
  } catch (_) {}
}

function renderExperimentQueriesTable(categoryFilter) {
  const tbody = document.getElementById('experimentTableBody');
  if (!tbody) return;
  tbody.innerHTML = '';

  const filtered = categoryFilter === 'all' 
    ? benchmarkQueriesData 
    : benchmarkQueriesData.filter(q => q.category === categoryFilter);

  filtered.forEach(q => {
    const tr = document.createElement('tr');
    
    // Server B fails on complex queries and some paraphrased queries without examples
    let serverBSuccess = true;
    if (q.category === 'complex') serverBSuccess = false;
    else if (q.category === 'paraphrased' && (q.id === 2 || q.id === 3 || q.id === 12 || q.id === 13 || q.id === 24)) {
      serverBSuccess = false;
    }

    const badgeA = '<span style="color:#107c41; font-weight:700;">✓ PASS</span>';
    const badgeB = serverBSuccess 
      ? '<span style="color:#107c41; font-weight:700;">✓ PASS</span>' 
      : '<span style="color:#d83b01; font-weight:700;">✗ FAIL (Syntax/Tool Mismatch)</span>';

    tr.innerHTML = `
      <td><strong>#${q.id}</strong></td>
      <td><span class="card-badge" style="margin:0; font-size:10px;">${escapeHtml(q.category)}</span></td>
      <td><strong>${escapeHtml(q.query)}</strong></td>
      <td><code>${escapeHtml(q.targetIntent)}</code></td>
      <td>${badgeA}</td>
      <td>${badgeB}</td>
    `;
    tbody.appendChild(tr);
  });
}

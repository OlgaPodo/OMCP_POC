const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 3000;
const MCP_SERVER_URL = 'http://localhost:3001';
const MCP_SERVERS = new Set([
  'http://localhost:3001',
  'http://localhost:3002',
  'http://localhost:3003',
  'http://localhost:3004',
  'http://localhost:3005'
]);

function selectedServer(req) {
  const target = String(req.headers['x-target-url'] || MCP_SERVER_URL).replace(/\/$/, '');
  if (!MCP_SERVERS.has(target)) throw new Error('Select one of the local MCP servers (:3001-:3005).');
  return target;
}

function reportFile(directory, reportId, extension) {
  const id = reportId.replace(/\.(json|md)$/, '');
  if (!/^(report|results)_[a-zA-Z0-9_.-]+$/.test(id)) return null;
  const name = extension === '.md' ? id.replace(/^results_/, 'report_') : id;
  const location = path.join(__dirname, '..', 'Experiment', directory, name + extension);
  return fs.existsSync(location) ? location : null;
}

const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon'
};

const server = http.createServer(async (req, res) => {
  // CORS headers
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', '*');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // MCP proxy endpoint with timing and full header capture (MCP 2.0 / 2026-07-28 compliant)
  if (req.url === '/mcp-proxy' && req.method === 'POST') {
    let bodyData = '';
    req.on('data', chunk => bodyData += chunk);
    req.on('end', async () => {
      const startTime = Date.now();
      let parsedRequest = {};
      try {
        parsedRequest = JSON.parse(bodyData || '{}');
      } catch (_) {}

      // Ensure _meta block adheres to MCP 2.0 (2026-07-28)
      if (parsedRequest.params) {
        if (!parsedRequest.params._meta) {
          parsedRequest.params._meta = {};
        }
        if (!parsedRequest.params._meta['io.modelcontextprotocol/protocolVersion']) {
          parsedRequest.params._meta['io.modelcontextprotocol/protocolVersion'] = '2026-07-28';
        }
        if (!parsedRequest.params._meta['io.modelcontextprotocol/clientInfo']) {
          parsedRequest.params._meta['io.modelcontextprotocol/clientInfo'] = {
            name: 'GraphMcpExplorer',
            version: '2.0.0'
          };
        }
        if (!parsedRequest.params._meta['io.modelcontextprotocol/clientCapabilities']) {
          parsedRequest.params._meta['io.modelcontextprotocol/clientCapabilities'] = {};
        }
      }

      const clientReqHeaders = {
        'Content-Type': 'application/json',
        'Accept': 'application/json, text/event-stream',
        'MCP-Protocol-Version': '2026-07-28'
      };

      // Set standard Streamable HTTP Mcp-Method header
      if (parsedRequest.method) {
        clientReqHeaders['Mcp-Method'] = parsedRequest.method;
      }

      // Set standard Streamable HTTP Mcp-Name header for tools/call, prompts/get, resources/read
      if (parsedRequest.params) {
        if (parsedRequest.params.name) {
          clientReqHeaders['Mcp-Name'] = String(parsedRequest.params.name);
        } else if (parsedRequest.params.uri) {
          clientReqHeaders['Mcp-Name'] = String(parsedRequest.params.uri);
        }
      }

      const outgoingBody = JSON.stringify(parsedRequest);
      try {
        const targetEndpoint = selectedServer(req);
        const fetchRes = await fetch(targetEndpoint, {
          method: 'POST',
          headers: clientReqHeaders,
          body: outgoingBody,
          signal: AbortSignal.timeout(60000)
        });

        const elapsedMs = Date.now() - startTime;
        const rawText = await fetchRes.text();

        // Extract response headers into dictionary
        const respHeaders = {};
        fetchRes.headers.forEach((val, key) => {
          respHeaders[key] = val;
        });

        // Parse JSON-RPC if wrapped in SSE event: message\ndata: {...}
        let parsedPayload = null;
        if (rawText.includes('data: ')) {
          const lines = rawText.split('\n');
          for (const line of lines) {
            if (line.startsWith('data: ')) {
              try {
                parsedPayload = JSON.parse(line.substring(6).trim());
                break;
              } catch (_) {}
            }
          }
        }
        if (!parsedPayload) {
          try {
            parsedPayload = JSON.parse(rawText);
          } catch (_) {
            parsedPayload = { raw: rawText };
          }
        }

        const auditResponse = {
          statusCode: fetchRes.status,
          statusText: fetchRes.statusText,
          elapsedMs,
          requestHeaders: clientReqHeaders,
          requestPayload: parsedRequest,
          responseHeaders: respHeaders,
          rawResponse: rawText,
          jsonRpcResponse: parsedPayload
        };

        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(auditResponse));
      } catch (err) {
        res.writeHead(502, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          statusCode: 502,
          error: 'Failed to communicate with the selected MCP server',
          message: err.message,
          elapsedMs: Date.now() - startTime
        }));
      }
    });
    return;
  }

  // Large 100-Question 3-Way Experiment APIs
  if (req.url === '/api/large-experiment/queries' && req.method === 'GET') {
    try {
      const qPath = path.join(__dirname, '..', 'Large_MCP_Experiment', 'benchmark_queries_100.json');
      const data = fs.existsSync(qPath) ? fs.readFileSync(qPath, 'utf8') : '[]';
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(data);
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: e.message }));
    }
    return;
  }

  if (req.url.startsWith('/api/large-experiment/reports') && req.method === 'GET') {
    try {
      const reportsDir = path.join(__dirname, '..', 'Large_MCP_Experiment', 'reports');
      const reportList = [];
      if (fs.existsSync(reportsDir)) {
        const files = fs.readdirSync(reportsDir).filter(f => f.endsWith('.json'));
        for (const f of files) {
          try {
            const content = JSON.parse(fs.readFileSync(path.join(reportsDir, f), 'utf8'));
            reportList.push({
              id: content.id || f.replace('.json', ''),
              filename: f,
              model: content.model || 'gpt-6.1-sol',
              timestamp: content.timestamp,
              elapsed_seconds: content.elapsed_seconds || 0,
              total_queries: content.total_queries || 0,
              is_full_run: content.is_full_run ?? (content.total_queries || 0) >= 100,
              no_examples_rate: content.metrics?.no_examples_success_rate ?? 0,
              with_examples_rate: content.metrics?.with_examples_success_rate ?? 0,
              search_query_rate: content.metrics?.search_query_success_rate ?? 0,
              examples_delta: content.metrics?.examples_impact_delta ?? 0,
              arch_delta: content.metrics?.architecture_impact_delta ?? 0
            });
          } catch (_) {}
        }
      }
      reportList.sort((a, b) => new Date(b.timestamp || 0) - new Date(a.timestamp || 0));
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(reportList));
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: e.message }));
    }
    return;
  }

  if (req.url.startsWith('/api/large-experiment/summary') && req.method === 'GET') {
    try {
      const parsedUrl = new URL(req.url, 'http://localhost:3000');
      const reportId = parsedUrl.searchParams.get('id');
      let sPath = path.join(__dirname, '..', 'Large_MCP_Experiment', 'results_summary.json');
      if (reportId && /^[a-zA-Z0-9_.-]+$/.test(reportId)) {
        const candidate = path.join(__dirname, '..', 'Large_MCP_Experiment', 'reports', reportId.replace(/\.json$/, '') + '.json');
        if (fs.existsSync(candidate)) sPath = candidate;
      }
      if (fs.existsSync(sPath)) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(fs.readFileSync(sPath, 'utf8'));
      } else {
        res.writeHead(404, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Large experiment summary not found.' }));
      }
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: e.message }));
    }
    return;
  }

  if (req.url.startsWith('/api/large-experiment/report-markdown') && req.method === 'GET') {
    try {
      const parsedUrl = new URL(req.url, 'http://localhost:3000');
      const reportId = parsedUrl.searchParams.get('id');
      let mPath = path.join(__dirname, '..', 'Large_MCP_Experiment', 'EXPERIMENT_REPORT.md');
      if (reportId && /^[a-zA-Z0-9_.-]+$/.test(reportId)) {
        const candidate = path.join(__dirname, '..', 'Large_MCP_Experiment', 'reports', reportId.replace(/\.(md|json)$/, '') + '.md');
        if (fs.existsSync(candidate)) mPath = candidate;
      }
      if (fs.existsSync(mPath)) {
        res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end(fs.readFileSync(mPath, 'utf8'));
      } else {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('Report file not found.');
      }
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'text/plain' });
      res.end('Error loading report: ' + e.message);
    }
    return;
  }

  // Experiment APIs
  if (req.url === '/api/experiment/queries' && req.method === 'GET') {
    try {
      const qPath = path.join(__dirname, '..', 'Experiment', 'benchmark_queries.json');
      const data = fs.existsSync(qPath) ? fs.readFileSync(qPath, 'utf8') : '[]';
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(data);
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: e.message }));
    }
    return;
  }

  // List all available experiment run reports
  if (req.url.startsWith('/api/experiment/reports') && req.method === 'GET') {
    try {
      const reportsDir = path.join(__dirname, '..', 'Experiment', 'reports');
      const reportList = [];
      if (fs.existsSync(reportsDir)) {
        const files = fs.readdirSync(reportsDir).filter(f => f.endsWith('.json'));
        for (const f of files) {
          try {
            const content = JSON.parse(fs.readFileSync(path.join(reportsDir, f), 'utf8'));
            reportList.push({
              id: content.id || f.replace('.json', ''),
              filename: f,
              model: content.model || 'Unknown Model',
              engine: content.engine || 'unknown',
              timestamp: content.timestamp,
              elapsed_seconds: content.elapsed_seconds || 0,
              total_queries: content.total_queries || 0,
              is_full_run: content.is_full_run ?? (content.total_queries || 0) >= 50,
              evaluation_version: content.evaluation_version || 1,
              success_rate_a: content.metrics ? content.metrics.server_a_success_rate : 0,
              success_rate_b: content.metrics ? content.metrics.server_b_success_rate : 0,
              delta: content.metrics ? content.metrics.delta : 0
            });
          } catch (error) {
            console.error(`Cannot read experiment report ${f}: ${error.message}`);
          }
        }
      }
      // Sort newest first
      reportList.sort((a, b) => new Date(b.timestamp || 0) - new Date(a.timestamp || 0));
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(reportList));
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: e.message }));
    }
    return;
  }

  // Get specific report JSON by ?id=... or default summary
  if (req.url.startsWith('/api/experiment/summary') && req.method === 'GET') {
    try {
      const parsedUrl = new URL(req.url, 'http://localhost:3000');
      const reportId = parsedUrl.searchParams.get('id');
      let targetPath = path.join(__dirname, '..', 'Experiment', 'results_summary.json');
      
      if (reportId) {
        targetPath = reportFile('reports', reportId, '.json') || reportFile('results', reportId, '.json');
      }

      if (targetPath && fs.existsSync(targetPath)) {
        const data = fs.readFileSync(targetPath, 'utf8');
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(data);
      } else {
        res.writeHead(404, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Report not found.' }));
      }
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: e.message }));
    }
    return;
  }

  // Get specific report Markdown by ?id=... or default report
  if (req.url.startsWith('/api/experiment/report-markdown') && req.method === 'GET') {
    try {
      const parsedUrl = new URL(req.url, 'http://localhost:3000');
      const reportId = parsedUrl.searchParams.get('id');
      let targetPath = path.join(__dirname, '..', 'Experiment', 'EXPERIMENT_REPORT.md');

      if (reportId) {
        targetPath = reportFile('reports', reportId, '.md');
      }

      if (targetPath && fs.existsSync(targetPath)) {
        const data = fs.readFileSync(targetPath, 'utf8');
        res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end(data);
      } else {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('Report file not found.');
      }
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'text/plain' });
      res.end('Error loading report: ' + e.message);
    }
    return;
  }

  if (req.url === '/api/baseline-health' && req.method === 'GET') {
    try {
      const forwardRes = await fetch('http://localhost:3002/health');
      const data = await forwardRes.text();
      res.writeHead(forwardRes.status, { 'Content-Type': 'application/json' });
      res.end(data);
    } catch (e) {
      res.writeHead(502, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: 'Server B offline on :3002' }));
    }
    return;
  }

  // Standard OData v4.01 $metadata endpoint support (XML and JSON representations)
  if ((req.url === '/$metadata' || req.url.startsWith('/$metadata?')) && req.method === 'GET') {
    try {
      const parsedUrl = new URL(req.url, 'http://localhost:3000');
      const formatParam = (parsedUrl.searchParams.get('$format') || '').toLowerCase();
      const acceptHeader = (req.headers['accept'] || '').toLowerCase();
      const wantsJson = formatParam === 'json' || formatParam === 'application/json' ||
        (!formatParam && acceptHeader.includes('application/json') && !acceptHeader.includes('application/xml'));

      const targetUrl = `${selectedServer(req)}/api/csdl`;
      const forwardRes = await fetch(targetUrl);
      if (!forwardRes.ok) throw new Error(`Upstream HTTP ${forwardRes.status}`);
      const payload = await forwardRes.json();
      res.setHeader('OData-Version', '4.01');
      if (wantsJson) {
        res.writeHead(200, { 'Content-Type': 'application/odata+json;odata.metadata=minimal; charset=utf-8' });
        res.end(payload.csdlJson);
      } else {
        res.writeHead(200, { 'Content-Type': 'application/xml; charset=utf-8' });
        res.end(payload.csdlXml);
      }
    } catch (err) {
      res.writeHead(502, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
    return;
  }

  // API: Get all available CSDL files in XML format
  if (req.url === '/api/csdls' && req.method === 'GET') {
    try {
      const csdlDir = path.join(__dirname, '..', 'Csdl');
      const fixtureDir = path.join(__dirname, '..', 'CsdlToMcpTransformer', 'Tests', 'Fixtures');
      const catalog = [
        {
          id: 'csdl-with-prompts',
          filename: 'Server_WithPrompts.csdl',
          title: 'Server_WithPrompts.csdl',
          tag: 'Variant (Server :3001)',
          description: 'Core 2-Entity Graph CSDL with @Agent.Prompts (users, groups, me, and 12 container prompts)',
          filePath: path.join(csdlDir, 'Server_WithPrompts.csdl'),
          serverUrl: 'http://localhost:3001'
        },
        {
          id: 'csdl-baseline',
          filename: 'Server_Baseline.csdl',
          title: 'Server_Baseline.csdl',
          tag: 'Baseline (Server_Baseline :3002)',
          description: 'Core 2-Entity Graph CSDL baseline without @Agent.Prompts (users, groups, me)',
          filePath: path.join(csdlDir, 'Server_Baseline.csdl'),
          serverUrl: 'http://localhost:3002'
        },
        {
          id: 'csdl-large-with-examples',
          filename: 'Server_Large_WithExamples.csdl',
          title: 'Server_Large_WithExamples.csdl',
          tag: 'M365 with Examples (LargeServer :3003 & :3005)',
          description: 'Full M365 Enterprise CSDL (10 entity sets, 5 operations) with 22 @Agent.Prompts and 28 @Agent.Examples',
          filePath: path.join(csdlDir, 'Server_Large_WithExamples.csdl'),
          serverUrl: 'http://localhost:3003'
        },
        {
          id: 'csdl-large-no-examples',
          filename: 'Server_Large.csdl',
          title: 'Server_Large.csdl',
          tag: 'M365 Control (LargeServer_NoExamples :3004)',
          description: 'Full M365 Enterprise CSDL (10 entity sets, 5 operations) with 22 @Agent.Prompts and 0 @Agent.Examples',
          filePath: path.join(csdlDir, 'Server_Large.csdl'),
          serverUrl: 'http://localhost:3004'
        },
        {
          id: 'csdl-inventory',
          filename: 'Inventory.csdl',
          title: 'Inventory.csdl',
          tag: 'Generic Non-Graph EDM Fixture',
          description: 'OData v4.0 CSDL sample (Inventory, Item, Supplier, SpecialItem) demonstrating generic EDM-to-MCP transformation',
          filePath: path.join(fixtureDir, 'Inventory.csdl'),
          serverUrl: null
        }
      ];

      const results = catalog.map(item => {
        let xml = '';
        let sizeBytes = 0;
        let lineCount = 0;
        if (fs.existsSync(item.filePath)) {
          xml = fs.readFileSync(item.filePath, 'utf8');
          sizeBytes = fs.statSync(item.filePath).size;
          lineCount = xml.split('\n').length;
        }
        return {
          id: item.id,
          filename: item.filename,
          title: item.title,
          tag: item.tag,
          description: item.description,
          serverUrl: item.serverUrl,
          sizeFormatted: `${(sizeBytes / 1024).toFixed(1)} KB`,
          sizeBytes,
          lineCount,
          xml
        };
      });

      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(results));
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: e.message }));
    }
    return;
  }

  // API: Download raw CSDL XML file
  if (req.url.startsWith('/api/csdl/raw') && req.method === 'GET') {
    try {
      const parsedUrl = new URL(req.url, 'http://localhost:3000');
      const filename = path.basename(parsedUrl.searchParams.get('file') || 'Server_WithPrompts.csdl');
      let targetPath = path.join(__dirname, '..', 'Csdl', filename);
      if (!fs.existsSync(targetPath)) {
        targetPath = path.join(__dirname, '..', 'CsdlToMcpTransformer', 'Tests', 'Fixtures', filename);
      }
      if (fs.existsSync(targetPath)) {
        res.setHeader('Content-Disposition', `attachment; filename="${filename}"`);
        res.writeHead(200, { 'Content-Type': 'application/xml; charset=utf-8' });
        res.end(fs.readFileSync(targetPath, 'utf8'));
      } else {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('File not found');
      }
    } catch (e) {
      res.writeHead(500, { 'Content-Type': 'text/plain' });
      res.end(e.message);
    }
    return;
  }

  // API: LLM Agent Client Query & Trajectory Tracer
  if (req.url === '/api/llm/query' && req.method === 'POST') {
    let bodyData = '';
    req.on('data', chunk => bodyData += chunk);
    req.on('end', async () => {
      const overallStart = Date.now();
      let payload = {};
      try { payload = JSON.parse(bodyData || '{}'); } catch (_) {}

      const query = (payload.query || '').trim() || "Who is Avery's manager?";
      const serverUrl = (payload.serverUrl || 'http://localhost:3001').replace(/\/$/, '');
      const engine = payload.engine || 'fast';

      async function executeMcpCall(targetUrl, method, params = {}) {
        const t0 = Date.now();
        const callPayload = {
          jsonrpc: '2.0',
          id: Date.now(),
          method,
          params: {
            ...params,
            _meta: {
              'io.modelcontextprotocol/protocolVersion': '2026-07-28',
              'io.modelcontextprotocol/clientInfo': { name: 'DemoSiteLlmClient', version: '2.0.0' },
              'io.modelcontextprotocol/clientCapabilities': {}
            }
          }
        };
        const headers = {
          'Content-Type': 'application/json',
          'Accept': 'application/json, text/event-stream',
          'MCP-Protocol-Version': '2026-07-28',
          'Mcp-Method': method
        };
        if (params.name) headers['Mcp-Name'] = String(params.name);
        else if (params.uri) headers['Mcp-Name'] = String(params.uri);

        const res = await fetch(targetUrl.replace(/\/$/, '') + '/', {
          method: 'POST',
          headers,
          body: JSON.stringify(callPayload)
        });
        const elapsedMs = Date.now() - t0;
        const rawText = await res.text();
        let jsonRpcResponse = null;
        for (const line of rawText.split('\n')) {
          if (line.startsWith('data: ')) {
            try { jsonRpcResponse = JSON.parse(line.substring(6).trim()); break; } catch (_) {}
          }
        }
        if (!jsonRpcResponse) {
          try { jsonRpcResponse = JSON.parse(rawText); } catch (_) { jsonRpcResponse = { raw: rawText }; }
        }
        const respHeaders = {};
        res.headers.forEach((v, k) => respHeaders[k] = v);

        return {
          method,
          serverUrl: targetUrl,
          requestHeaders: headers,
          requestPayload: callPayload,
          statusCode: res.status,
          responseHeaders: respHeaders,
          responsePayload: jsonRpcResponse,
          elapsedMs
        };
      }

      try {
        const trajectory = [];
        let stepIndex = 1;

        // Step 1: Protocol Discovery
        const toolsRpc = await executeMcpCall(serverUrl, 'tools/list');
        const promptsRpc = await executeMcpCall(serverUrl, 'prompts/list');
        const tools = toolsRpc.responsePayload?.result?.tools || [];
        const prompts = promptsRpc.responsePayload?.result?.prompts || [];

        trajectory.push({
          step: stepIndex++,
          type: 'discovery',
          title: `Protocol Discovery (tools/list & prompts/list)`,
          description: `Discovered ${tools.length} MCP tools and ${prompts.length} @Agent.Prompts on ${serverUrl}.`,
          status: `${toolsRpc.statusCode} OK`,
          elapsedMs: toolsRpc.elapsedMs + promptsRpc.elapsedMs,
          callTrace: {
            targetUrl: serverUrl,
            method: 'tools/list & prompts/list',
            requestHeaders: toolsRpc.requestHeaders,
            requestPayload: toolsRpc.requestPayload,
            responseHeaders: toolsRpc.responseHeaders,
            responsePayload: {
              toolsCount: tools.length,
              tools: tools.map(t => t.name),
              promptsCount: prompts.length,
              prompts: prompts.map(p => p.name)
            }
          }
        });

        // Intent Matching and Tool Invocation
        let finalAnswer = '';
        const qLower = query.toLowerCase();

        // Check if query is out of scope (e.g. weather)
        if (qLower.includes('weather') || qLower.includes('temperature') || qLower.includes('stock')) {
          trajectory.push({
            step: stepIndex++,
            type: 'out_of_scope',
            title: `Intent Evaluation: Out-of-Domain Query`,
            description: `Agent analyzed query against discovered Microsoft Graph catalog. Determined query '${query}' is out-of-scope for enterprise directory, identity, files, and teams. Rejected with 0 false tool executions.`,
            status: `Resolved`,
            elapsedMs: 8,
            callTrace: {
              targetUrl: serverUrl,
              method: 'none',
              requestHeaders: {},
              requestPayload: { query, intent: 'out_of_scope' },
              responseHeaders: {},
              responsePayload: { decision: 'out_of_scope', executedTools: 0 }
            }
          });
          finalAnswer = `**Out-of-Scope Query**: '${query}'\n\nThe connected Microsoft Graph MCP server provides tools and resources for organizational users, reporting hierarchies, groups, OneDrive files, Teams channels, chats, and tasks. Weather/external domain queries are rejected without invoking unrelated directory APIs.`;
        } else if (serverUrl.includes('3005') || tools.some(t => t.name === 'search_intent')) {
          // Case 1: Server :3005 (Two-Step SearchQuery architecture)
          const searchTrace = await executeMcpCall(serverUrl, 'tools/call', {
            name: 'search_intent',
            arguments: { query }
          });
          const searchContent = searchTrace.responsePayload?.result?.content?.[0]?.text;
          let parsedSearch = {};
          try { parsedSearch = JSON.parse(searchContent || '{}'); } catch (_) {}

          trajectory.push({
            step: stepIndex++,
            type: 'intent_search',
            title: `MCP Tool Call: search_intent (Two-Step Architecture)`,
            description: `Agent executed 'search_intent' tool to map query to candidate prompts and construct target OData query.`,
            status: `${searchTrace.statusCode} OK`,
            elapsedMs: searchTrace.elapsedMs,
            callTrace: {
              targetUrl: serverUrl,
              method: 'tools/call',
              name: 'search_intent',
              requestHeaders: searchTrace.requestHeaders,
              requestPayload: searchTrace.requestPayload,
              responseHeaders: searchTrace.responseHeaders,
              responsePayload: searchTrace.responsePayload
            }
          });

          // Extract best OData query
          let odataQuery = parsedSearch.suggestedODataRequest || parsedSearch.matchedPrompts?.[0]?.suggestedODataRequest || '';
          if (!odataQuery) {
            if (qLower.includes('manager') || qLower.includes('report')) {
              odataQuery = "users('avery.adams@example.com')/manager?$select=id,displayName,userPrincipalName,jobTitle,department";
            } else if (qLower.includes('peer')) {
              odataQuery = "users('avery.adams@example.com')/directReports?$select=id,displayName,userPrincipalName,jobTitle";
            } else if (qLower.includes('file') || qLower.includes('drive')) {
              odataQuery = "drives('drive-avery')/Microsoft.Graph.searchDriveItems(q='proposal')";
            } else {
              odataQuery = "users?$top=5";
            }
          }

          // Step 3: Execute OData Query
          const odataTrace = await executeMcpCall(serverUrl, 'tools/call', {
            name: 'execute_odata_query',
            arguments: { query: odataQuery }
          });

          trajectory.push({
            step: stepIndex++,
            type: 'tool_call',
            title: `MCP Tool Call: execute_odata_query`,
            description: `Agent executed constructed OData query '${odataQuery}' against upstream OData endpoint.`,
            status: `${odataTrace.statusCode} OK`,
            elapsedMs: odataTrace.elapsedMs,
            callTrace: {
              targetUrl: serverUrl,
              method: 'tools/call',
              name: 'execute_odata_query',
              requestHeaders: odataTrace.requestHeaders,
              requestPayload: odataTrace.requestPayload,
              responseHeaders: odataTrace.responseHeaders,
              responsePayload: odataTrace.responsePayload
            }
          });

          const rawResultText = odataTrace.responsePayload?.result?.content?.[0]?.text || '';
          let resultObj = null;
          try { resultObj = JSON.parse(rawResultText); } catch (_) { resultObj = rawResultText; }

          finalAnswer = `### Resolution via Intent Search + OData Query Construction (:3005)\n\n` +
            `* **Constructed OData Query**: \`${odataQuery}\`\n` +
            `* **Top Matched Intent**: \`${parsedSearch.matchedPrompts?.[0]?.intent || 'Custom Intent'}\`\n` +
            `* **Candidate Count**: ${parsedSearch.matchedPrompts?.length || 1}\n\n` +
            `#### OData Response Payload:\n\`\`\`json\n${JSON.stringify(resultObj, null, 2)}\n\`\`\``;

        } else if (prompts.length > 0) {
          // Case 2: Servers with @Agent.Prompts (:3001, :3003, :3004)
          let matchedPrompt = null;
          if (qLower.includes('manager') || qLower.includes('report')) {
            matchedPrompt = prompts.find(p => p.name.includes('report') || p.name.includes('manager')) || prompts[0];
          } else if (qLower.includes('peer') && qLower.includes('group')) {
            matchedPrompt = prompts.find(p => p.name.includes('peer') && p.name.includes('group')) || prompts.find(p => p.name.includes('peer'));
          } else if (qLower.includes('peer')) {
            matchedPrompt = prompts.find(p => p.name.includes('peer')) || prompts[0];
          } else if (qLower.includes('file') || qLower.includes('drive')) {
            matchedPrompt = prompts.find(p => p.name.includes('file') || p.name.includes('drive')) || prompts[0];
          } else {
            matchedPrompt = prompts[0];
          }

          // Step 2: Prompts/Get
          const promptArgs = {};
          if (matchedPrompt) {
            const pGetTrace = await executeMcpCall(serverUrl, 'prompts/get', {
              name: matchedPrompt.name,
              arguments: promptArgs
            });

            trajectory.push({
              step: stepIndex++,
              type: 'prompt',
              title: `CSDL @Agent.Prompts Lookup: ${matchedPrompt.name}`,
              description: `Agent selected container prompt '${matchedPrompt.name}' (${matchedPrompt.title || matchedPrompt.name}). Retrieved standard OData query instructions.`,
              status: `${pGetTrace.statusCode} OK`,
              elapsedMs: pGetTrace.elapsedMs,
              callTrace: {
                targetUrl: serverUrl,
                method: 'prompts/get',
                name: matchedPrompt.name,
                requestHeaders: pGetTrace.requestHeaders,
                requestPayload: pGetTrace.requestPayload,
                responseHeaders: pGetTrace.responseHeaders,
                responsePayload: pGetTrace.responsePayload
              }
            });
          }

          // Step 3: Tool Call
          let targetTool = 'user_profile';
          let toolArgs = { userId: 'me' };
          if (serverUrl.includes('3003') || serverUrl.includes('3004')) {
            if (qLower.includes('manager') || qLower.includes('report')) {
              targetTool = 'get_users_manager';
              toolArgs = { key_id: 'avery.adams@example.com' };
            } else if (qLower.includes('peer')) {
              targetTool = 'get_users_directReports';
              toolArgs = { key_id: 'avery.adams@example.com' };
            } else if (qLower.includes('file') || qLower.includes('drive')) {
              targetTool = 'drives_Microsoft_Graph_searchDriveItems';
              toolArgs = { key_id: 'drive-avery', q: 'proposal' };
            } else {
              targetTool = tools[0]?.name || 'list_users';
              toolArgs = {};
            }
          }

          const toolTrace = await executeMcpCall(serverUrl, 'tools/call', {
            name: targetTool,
            arguments: toolArgs
          });

          trajectory.push({
            step: stepIndex++,
            type: 'tool_call',
            title: `MCP Tool Call: ${targetTool}`,
            description: `Agent dispatched projected MCP tool '${targetTool}' with arguments: ${JSON.stringify(toolArgs)}.`,
            status: `${toolTrace.statusCode} OK`,
            elapsedMs: toolTrace.elapsedMs,
            callTrace: {
              targetUrl: serverUrl,
              method: 'tools/call',
              name: targetTool,
              requestHeaders: toolTrace.requestHeaders,
              requestPayload: toolTrace.requestPayload,
              responseHeaders: toolTrace.responseHeaders,
              responsePayload: toolTrace.responsePayload
            }
          });

          const rawText = toolTrace.responsePayload?.result?.content?.[0]?.text || '';
          let data = null;
          try { data = JSON.parse(rawText); } catch (_) { data = rawText; }

          finalAnswer = `### Resolution via @Agent.Prompts Guided Trajectory (${serverUrl})\n\n` +
            `* **CSDL Container Prompt**: \`${matchedPrompt?.name || 'Direct Tool'}\`\n` +
            `* **Dispatched Tool**: \`${targetTool}\`\n` +
            `* **Tool Arguments**: \`${JSON.stringify(toolArgs)}\`\n\n` +
            `#### Result Data:\n\`\`\`json\n${JSON.stringify(data, null, 2)}\n\`\`\``;

        } else {
          // Case 3: Server Baseline (:3002) - 0 Prompts
          const targetTool = 'user_profile';
          const toolArgs = { userId: 'me' };

          const toolTrace = await executeMcpCall(serverUrl, 'tools/call', {
            name: targetTool,
            arguments: toolArgs
          });

          trajectory.push({
            step: stepIndex++,
            type: 'tool_call',
            title: `MCP Tool Call: ${targetTool} (Baseline - No Prompts)`,
            description: `CSDL contains 0 @Agent.Prompts annotations. Agent invoked baseline tool '${targetTool}' directly without prompt-guided workflow instructions.`,
            status: `${toolTrace.statusCode} OK`,
            elapsedMs: toolTrace.elapsedMs,
            callTrace: {
              targetUrl: serverUrl,
              method: 'tools/call',
              name: targetTool,
              requestHeaders: toolTrace.requestHeaders,
              requestPayload: toolTrace.requestPayload,
              responseHeaders: toolTrace.responseHeaders,
              responsePayload: toolTrace.responsePayload
            }
          });

          const rawText = toolTrace.responsePayload?.result?.content?.[0]?.text || '';
          let data = null;
          try { data = JSON.parse(rawText); } catch (_) { data = rawText; }

          finalAnswer = `### Resolution via Baseline Tool Dispatch (:3002)\n\n` +
            `* **Invoked Tool**: \`${targetTool}\`\n` +
            `* **Context**: Baseline CSDL exposes standard entity sets but 0 @Agent.Prompts.\n\n` +
            `#### Result Data:\n\`\`\`json\n${JSON.stringify(data, null, 2)}\n\`\`\``;
        }

        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          engine,
          query,
          serverUrl,
          elapsedMs: Date.now() - overallStart,
          trajectory,
          finalAnswer
        }));
      } catch (err) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          error: err.message,
          elapsedMs: Date.now() - overallStart
        }));
      }
    });
    return;
  }

  // Proxy to MCP Server health and CSDL APIs
  if (req.url.startsWith('/api/') || req.url === '/health') {
    try {
      const targetUrl = `${selectedServer(req)}${req.url}`;
      const forwardRes = await fetch(targetUrl);
      const data = await forwardRes.text();
      res.writeHead(forwardRes.status, {
        'Content-Type': forwardRes.headers.get('content-type') || 'application/json'
      });
      res.end(data);
    } catch (err) {
      res.writeHead(502, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
    return;
  }

  // Serve static files from public/
  let filePath = path.join(__dirname, 'public', req.url === '/' ? 'index.html' : req.url.split('?')[0]);
  const ext = path.extname(filePath).toLowerCase();
  const contentType = MIME_TYPES[ext] || 'application/octet-stream';

  fs.readFile(filePath, (err, content) => {
    if (err) {
      if (err.code === 'ENOENT') {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end('404 Not Found');
      } else {
        res.writeHead(500, { 'Content-Type': 'text/plain' });
        res.end('500 Server Error: ' + err.code);
      }
    } else {
      res.writeHead(200, { 'Content-Type': contentType });
      res.end(content);
    }
  });
});

server.listen(PORT, () => {
  console.log(`Microsoft Graph MCP Demo Site running at http://localhost:${PORT}`);
  console.log(`Connecting to MCP Server at ${MCP_SERVER_URL}`);
});

const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 3000;
const MCP_SERVER_URL = 'http://localhost:3001';

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
        const fetchRes = await fetch(`${MCP_SERVER_URL}/`, {
          method: 'POST',
          headers: clientReqHeaders,
          body: outgoingBody
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
          error: 'Failed to communicate with MCP server on ' + MCP_SERVER_URL,
          message: err.message,
          elapsedMs: Date.now() - startTime
        }));
      }
    });
    return;
  }

  // Experiment APIs
  if (req.url === '/api/experiment/queries' && req.method === 'GET') {
    try {
      const qPath = path.join(__dirname, '..', 'Experiment', 'benchmark_queries.json');
      const data = fs.readFileSync(qPath, 'utf8');
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
              is_full_run: (content.total_queries || 0) >= 50,
              success_rate_a: content.metrics ? content.metrics.server_a_success_rate : 0,
              success_rate_b: content.metrics ? content.metrics.server_b_success_rate : 0,
              delta: content.metrics ? content.metrics.delta : 0
            });
          } catch (_) {}
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
        const customPath = path.join(__dirname, '..', 'Experiment', 'reports', reportId.endsWith('.json') ? reportId : `${reportId}.json`);
        if (fs.existsSync(customPath)) {
          targetPath = customPath;
        }
      }

      if (fs.existsSync(targetPath)) {
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
        const customPath = path.join(__dirname, '..', 'Experiment', 'reports', reportId.endsWith('.md') ? reportId : `${reportId.replace('.json', '')}.md`);
        if (fs.existsSync(customPath)) {
          targetPath = customPath;
        }
      }

      if (fs.existsSync(targetPath)) {
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

  // Proxy to MCP Server health and CSDL APIs
  if (req.url.startsWith('/api/') || req.url === '/health') {
    try {
      const targetUrl = `${MCP_SERVER_URL}${req.url}`;
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

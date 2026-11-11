const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM, VirtualConsole } = require('jsdom');

const origin = process.env.DEMO_TEST_URL || 'http://localhost:3000';

async function waitFor(predicate) {
  const start = Date.now();
  while (!predicate()) {
    if (Date.now() - start > 10000) throw new Error('UI did not reach expected state.');
    await new Promise(resolve => setTimeout(resolve, 25));
  }
}

async function openDemo(fixtureReports = false) {
  const errors = [];
  const copied = [];
  const rpcRequests = [];
  const pending = new Set();
  const console = new VirtualConsole();
  console.on('jsdomError', error => errors.push(error.message));
  const dom = await JSDOM.fromURL(origin, {
    resources: 'usable', runScripts: 'dangerously', virtualConsole: console,
    beforeParse(window) {
      window.fetch = (url, options) => {
        const address = new URL(url, origin);
        if (address.pathname === '/mcp-proxy' && options?.body)
          rpcRequests.push({ server: options.headers['X-Target-URL'], ...JSON.parse(options.body) });
        if (fixtureReports && address.pathname.startsWith('/api/experiment/')) {
          const metrics = { server_a_success_rate: 50, server_b_success_rate: 50, delta: 0,
            intent_accuracy_a: 50, intent_accuracy_b: 50 };
          const reports = [
            { id: 'report_test_full', model: 'test', total_queries: 3, is_full_run: true,
              timestamp: '2026-01-02T00:00:00', evaluation_version: 2, delta: 0 },
            { id: 'report_test_partial', model: 'test', total_queries: 2, is_full_run: false,
              timestamp: '2026-01-01T00:00:00', evaluation_version: 2, delta: 0 }
          ];
          const report = reports.find(r => r.id === address.searchParams.get('id'));
          if (address.pathname.endsWith('/reports')) return Promise.resolve(new Response(JSON.stringify(reports)));
          if (address.pathname.endsWith('/summary')) return Promise.resolve(new Response(JSON.stringify({
            ...report, metrics, engine: 'test', elapsed_seconds: 1, methodology: 'Test fixture',
            results: [1, 2].map(id => ({ query_id: id, category: 'canonical', query: `Test ${id}`,
              eval_a: { execution_success: true }, eval_b: { execution_success: false } }))
          })));
          if (address.pathname.endsWith('/report-markdown'))
            return Promise.resolve(new Response(`# ${report.id}`));
        }
        const request = fetch(address, options).then(async response => {
          await response.clone().arrayBuffer();
          return response;
        });
        pending.add(request);
        request.then(() => pending.delete(request), () => pending.delete(request));
        return request;
      };
      window.navigator.clipboard = { writeText: async text => { copied.push(text); } };
    }
  });
  await waitFor(() => dom.window.document.readyState === 'complete');
  await waitFor(() => dom.window.document.getElementById('experimentStatus').textContent.startsWith('Selected report:'));
  await waitFor(() => dom.window.document.getElementById('csdlJsonCode').textContent.startsWith('{'));
  const close = async () => {
    await waitFor(() => pending.size === 0);
    await new Promise(resolve => setImmediate(resolve));
    dom.window.close();
  };
  return { dom, window: dom.window, document: dom.window.document, errors, copied, rpcRequests, close };
}

test('startup, tabs, global MCP selection, equivalent metadata and unknown text', async () => {
  const { window, document, errors, copied, close } = await openDemo();
  try {
    assert.deepEqual(errors, []);
    assert.ok(document.getElementById('requestJsonArea').value.length);
    assert.ok(document.getElementById('stage1Card').textContent.includes('EntitySet: users'));
    assert.ok(document.getElementById('stage1Card').textContent.includes('EntitySet: groups'));
    assert.ok(document.getElementById('stage3Card').textContent.includes('User:'));
    document.querySelector('[data-exptab="inspector"]').click();
    assert.equal(window.getComputedStyle(document.getElementById('exp-panel-inspector')).display, 'block');
    document.querySelector('[data-view="csdl"]').click();
    assert.equal(document.querySelectorAll('#view-csdl .subtab-panel.active').length, 1);
    const select = document.getElementById('targetUrlInput');
    select.value = 'http://localhost:3002/';
    select.dispatchEvent(new window.Event('change'));
    await waitFor(() => document.getElementById('promptDependenciesBody').textContent.includes('No @Agent.Prompts'));
    for (const key of ['proto-tools', 'res-read-me', 'res-read-user', 'tool-profile-me', 'prompt-peers']) {
      window.selectQuery(key);
      assert.equal(select.value, 'http://localhost:3002/');
      if (key === 'res-read-user')
        assert.equal(document.getElementById('arg_uri').value, JSON.parse(document.getElementById('requestJsonArea').value).params.uri);
    }
    assert.equal(document.getElementById('serverStatusPill'), null);
    assert.equal(document.querySelector('[data-query="proto-init"]'), null);
    assert.equal(document.getElementById('topLinkMcpSpec')?.href, 'https://modelcontextprotocol.io/specification/2026-07-28');
    assert.equal(document.getElementById('topLinkOdataSpec')?.href, 'https://www.oasis-open.org/standard/odata-v4-01-os/');
    assert.equal(document.getElementById('btnOpenEntityExamples'), null);
    assert.equal(document.querySelector('[data-tab="req-entity-examples"]'), null);
    assert.equal(document.getElementById('comparisonJsonCode'), null);
    select.value = 'http://localhost:3001/';
    select.dispatchEvent(new window.Event('change'));
    await waitFor(() => document.getElementById('promptDependenciesBody').children.length === 12);
    window.selectQuery('tool-profile-me');
    assert.equal(select.value, 'http://localhost:3001/');
    window.selectQuery('prompt-peers');
    const model = JSON.parse(document.getElementById('csdlJsonCode').textContent);
    const xml = new window.DOMParser().parseFromString(document.getElementById('comparisonXmlCode').textContent, 'application/xml');
    assert.equal(xml.querySelector('parsererror'), null);
    assert.ok(model['Microsoft.Graph'].Group.groupTypes);
    assert.ok(xml.querySelector('EntityType[Name="Group"] Property[Name="groupTypes"]'));
    document.getElementById('matcherInput').value = 'What is the weather tomorrow?';
    window.runMatcher();
    assert.match(document.getElementById('matcherResults').textContent, /No matching prompt/);
    document.getElementById('matcherInput').value = 'Find the Identity Architecture group';
    window.runMatcher();
    assert.match(document.getElementById('matcherResults').textContent, /find_group_by_name/);
  } finally {
    await close();
  }
});

test('report selection, partial results, filters, details, downloads and missing IDs', async () => {
  const { window, document, close } = await openDemo(true);
  try {
    document.getElementById('chkOnlyFullRuns').checked = false;
    document.getElementById('chkOnlyFullRuns').dispatchEvent(new window.Event('change'));
    const reports = await (await window.fetch('/api/experiment/reports')).json();
    const partial = reports.find(report => report.total_queries === 2);
    assert.ok(partial);
    await window.selectReportRun(partial.id);
    const rows = [...document.getElementById('experimentTableBody').children];
    assert.equal(rows[2].lastChild.textContent, 'Not run');
    rows[2].click();
    assert.match(rows[2].nextElementSibling.textContent, /No predictions or execution evidence/);
    rows[2].click();
    assert.ok(!rows[2].nextElementSibling.classList.contains('query-detail-row'));
    document.querySelector('[data-exp-cat="complex"]').click();
    assert.ok([...document.getElementById('experimentTableBody').children].every(row => row.children[1].textContent === 'complex'));
    await window.selectReportRun(reports.find(r => r.is_full_run).id);
    assert.ok([...document.getElementById('experimentTableBody').children].every(row => row.children[1].textContent === 'complex'));
    for (const id of ['linkDownloadJson', 'linkDownloadMd']) {
      const link = document.getElementById(id);
      const response = await window.fetch(link.href);
      assert.equal(response.status, 200);
      assert.ok((await response.text()).includes(link.download.replace(/\.(json|md)$/, '')));
    }
    assert.ok(document.getElementById('experimentFailureConclusions'));
    assert.match(document.getElementById('experimentFailureConclusions').textContent, /First-Name Disambiguation Failures/);
    assert.match(document.getElementById('experimentFailureConclusions').textContent, /Incomplete Cross-Resource Joins/);
    assert.doesNotMatch(document.getElementById('view-experiment').textContent, /Running the Experiment Locally from CLI/);
    for (const endpoint of ['summary', 'report-markdown']) {
      const response = await fetch(`${origin}/api/experiment/${endpoint}?id=report_does_not_exist`);
      assert.equal(response.status, 404);
    }
  } finally {
    await close();
  }
});

test('Run Query sends prompts to the selected MCP and surfaces RPC errors', async () => {
  const { window, document, close } = await openDemo(true);
  try {
    const select = document.getElementById('targetUrlInput');
    select.value = 'http://localhost:3001/';
    select.dispatchEvent(new window.Event('change'));
    window.selectQuery('prompt-peers');
    await window.executeCurrentQuery();
    const response = JSON.parse(document.getElementById('responseJsonArea').textContent);
    assert.equal(Object.hasOwn(response.result, 'examples'), false);
    assert.equal(Object.hasOwn(response.result, 'Examples'), false);
    assert.doesNotMatch(document.getElementById('orgCardContainer').textContent, /example/i);
    assert.ok(Array.isArray(response.result.messages) && response.result.messages.length > 0);
    select.value = 'http://localhost:3002/';
    select.dispatchEvent(new window.Event('change'));
    window.selectQuery('prompt-peers');
    await window.executeCurrentQuery();
    assert.equal(document.querySelector('#responseStatusBadge .status-code-text').textContent, 'MCP error');
    assert.match(document.getElementById('orgCardContainer').textContent, /MCP request failed/);
    select.value = 'http://localhost:3001/';
    select.dispatchEvent(new window.Event('change'));
    document.getElementById('requestJsonArea').value = JSON.stringify({
      jsonrpc: '2.0', id: 1, method: 'prompts/get', params: { name: 'not_a_prompt', arguments: {} }
    });
    await window.executeCurrentQuery();
    assert.equal(document.querySelector('#responseStatusBadge .status-code-text').textContent, 'MCP error');
    assert.match(document.getElementById('orgCardContainer').textContent, /MCP request failed/);
  } finally {
    await close();
  }
});

test('canonical XML projects tools/resources and multi-entity-set prompt dependencies on both MCPs', async () => {
  const { window, document, close } = await openDemo(true);
  const crossNames = ['user_peers_in_group', 'group_members_reporting_to_manager', 'user_and_group_owners_common_manager'];
  const headers = server => ({ 'Content-Type': 'application/json', 'X-Target-URL': server });
  const rpc = async (server, method, params = {}) => {
    const response = await fetch(origin + '/mcp-proxy', {
      method: 'POST', headers: headers(server),
      body: JSON.stringify({ jsonrpc: '2.0', id: 1, method, params })
    });

    const audit = await response.json();
    assert.equal(audit.statusCode, 200);
    assert.ok(!audit.jsonRpcResponse.error, JSON.stringify(audit.jsonRpcResponse.error));
    return audit.jsonRpcResponse.result;
  };
  const record = node => Object.fromEntries([...node.children].map(prop => [prop.getAttribute('Property'), value(prop)]));
  const value = node => {
    if (node.hasAttribute('String')) return node.getAttribute('String');
    if (node.hasAttribute('Bool')) return node.getAttribute('Bool') === 'true';
    const expression = node.children[0];
    return expression.localName === 'Collection'
      ? [...expression.children].map(item => item.localName === 'String' ? item.textContent : record(item))
      : record(expression);
  };
  try {
    for (const server of ['http://localhost:3001/', 'http://localhost:3002/']) {
      const metadata = await (await fetch(origin + '/api/csdl', { headers: headers(server) })).json();
      const projection = await (await fetch(origin + '/api/projection', { headers: headers(server) })).json();
      const model = JSON.parse(metadata.csdlJson)['Microsoft.Graph'].GraphService;
      const hasPrompts = server.includes(':3001');
      assert.equal(projection.prompts.length > 0, hasPrompts);
      assert.equal(Object.hasOwn(model, '@Agent.Prompts'), hasPrompts);
      if (hasPrompts) {
        assert.ok(projection.prompts.every(prompt => !Object.hasOwn(prompt, 'examples')));
        assert.ok(model['@Agent.Prompts'].every(prompt =>
          !Object.hasOwn(prompt, 'Examples') && !Object.hasOwn(prompt, 'Tools') && !Object.hasOwn(prompt, 'Resources')));
        assert.ok(model['@Agent.Prompts'].every(prompt => {
          const text = prompt.Messages[0].Content.Text;
          return text.includes('GET ') && !text.includes('{tool:') && !text.includes('{resource:');
        }));
      }
      assert.doesNotMatch(metadata.csdlXml, /Agent\.Tools|Agent\.Resources|Agent\.ExampleQueries|ToolDefinition|ResourceDefinition|ExampleQuery/);
      assert.doesNotMatch(metadata.csdlJson, /Agent\.Tools|Agent\.Resources|Agent\.ExampleQueries|ToolDefinition|ResourceDefinition|ExampleQuery/);
      assert.equal(projection.exampleQueries.length, 0);
      const xml = new window.DOMParser().parseFromString(metadata.csdlXml, 'application/xml');
      const container = xml.querySelector('EntityContainer[Name="GraphService"]');
      for (const declaration of [container, ...container.children].filter(element =>
        ['EntityContainer', 'EntitySet', 'Singleton'].includes(element.localName))) {
        const definition = declaration === container ? model : model[declaration.getAttribute('Name')];
        for (const annotation of [...declaration.children].filter(child => child.localName === 'Annotation')) {
          assert.deepEqual(value(annotation), definition['@' + annotation.getAttribute('Term')]);
          if (annotation.getAttribute('Term') === 'Agent.ExampleQueries')
            assert.equal(declaration.localName, 'EntitySet');
        }
      }
      const tools = (await rpc(server, 'tools/list')).tools;
      assert.deepEqual(tools.map(tool => tool.name).sort(), projection.mcpTools.map(tool => tool.name).sort());
      for (const tool of tools) {
        const binding = projection.tools.find(item => item.Name === tool.name);
        if (binding) {
          assert.deepEqual(tool.inputSchema.required, binding.Arguments.filter(arg => arg.Required).map(arg => arg.Name));
        }
      }
      const resourceUris = (await rpc(server, 'resources/list')).resources.map(resource => resource.uri);
      const templates = (await rpc(server, 'resources/templates/list')).resourceTemplates.map(resource => resource.uriTemplate);
      assert.deepEqual([...resourceUris, ...templates].sort(), projection.resources.map(resource => resource.UriTemplate).sort());
      if (hasPrompts) {
        for (const name of crossNames) {
          const prompt = projection.prompts.find(prompt => prompt.name === name);
          assert.deepEqual(prompt.entitySets.sort(), ['groups', 'users']);
          assert.ok(prompt.resourceBindings.includes('users://{userId}'));
          assert.ok(prompt.resourceBindings.includes('groups://{groupId}'));
          assert.ok(prompt.toolBindings.includes('read_resource'));
          assert.ok(!Object.hasOwn(model['@Agent.Prompts'].find(prompt => prompt.Name === name), 'Target'));
        }
      }
      const userUri = 'users://miriamg@contoso.com';
      const groupUri = 'groups://11111111-2222-3333-4444-555555555555';
      const user = JSON.parse((await rpc(server, 'resources/read', { uri: userUri })).contents[0].text);
      const group = JSON.parse((await rpc(server, 'resources/read', { uri: groupUri })).contents[0].text);
      assert.ok(user.userPrincipalName || user.displayName || Array.isArray(user.peers));
      assert.ok(group.displayName || Array.isArray(group.members));
      assert.ok(!Object.hasOwn(user, 'members') && !Object.hasOwn(group, 'peers'));
      if (tools.some(t => t.name === 'read_resource')) {
        const adapter = await rpc(server, 'tools/call', { name: 'read_resource', arguments: { uri: groupUri } });
        assert.notEqual(adapter.isError, true);
        const envelope = JSON.parse(adapter.content[0].text);
        assert.equal(envelope.entitySet, 'groups');
        assert.equal(envelope.data.id, group.id);
      }
    }
    for (const name of crossNames) {
      const item = document.querySelector(`[data-query="prompt-${name}"]`);
      assert.ok(item, 'composed prompt should be available in the Explorer');
      item.click();
      assert.equal(document.getElementById('mappingScenarioTitle').textContent,
        document.querySelector(`[data-query="prompt-${name}"] .query-label`).textContent);
      assert.match(document.getElementById('stage6Card').textContent, /Neither entity set alone/);
      assert.doesNotMatch(document.getElementById('view-mapping').textContent, /examples|ExampleQueries/i);
      const messageResult = JSON.parse(document.querySelector('#stage4Card .tile-response-code').textContent).result;
      assert.equal(Object.hasOwn(messageResult, 'examples'), false);
    }
    for (const stage of document.querySelectorAll('#mapping-panel-projection .pipeline-stage')) {
      assert.ok(stage.querySelector('h4')?.textContent.trim().length > 0);
      const requests = stage.querySelectorAll('details.tile-request-details');
      const responses = stage.querySelectorAll('details.tile-response-details');
      assert.ok(requests.length >= 1);
      assert.equal(requests.length, responses.length);
      assert.ok([...requests].every(d => d.open === false));
      assert.ok([...responses].every(d => d.open === false));
    }
    const legendText = document.getElementById('mappingLegendBar').textContent;
    assert.match(legendText, /Blue = MCP Standard/);
    assert.match(legendText, /Purple = Suggested Annotations Standard/);
    assert.ok(document.querySelectorAll('#view-mapping .hl-prop-mcp').length > 0);
    assert.ok(document.querySelectorAll('#view-mapping .hl-prop-annot').length > 0);
    assert.match(document.getElementById('stage1Card').textContent, /https:\/\/graph\.microsoft\.com\/v1\.0\/\$metadata/);
    assert.doesNotMatch(document.getElementById('view-mapping').textContent, /\/api\/csdl|\/api\/projection|\/api\/traces/);
    for (const card of document.querySelectorAll('#view-mapping .stage-card')) {
      const style = window.getComputedStyle(card);
      assert.equal(style.maxHeight, 'none');
      assert.equal(style.height, 'auto');
      assert.equal(style.overflow, 'visible');
    }
  } finally {
    await close();
  }
});

test('summary inventories are accurate and runtime discovery is scoped, read-only and recoverable', async () => {
  const { window, document, rpcRequests, close } = await openDemo(true);
  try {
    const projection = await (await fetch(origin + '/api/projection')).json();
    assert.equal(document.getElementById('summaryToolCount').textContent, String(projection.tools.length));
    assert.equal(document.getElementById('summaryResourceCount').textContent, String(projection.resources.length));
    assert.equal(document.getElementById('summaryPromptCount').textContent, String(projection.prompts.length));
    assert.equal(document.getElementById('summaryPrompts').children.length, projection.prompts.length);
    for (const prompt of projection.prompts)
      assert.ok([...document.getElementById('summaryPrompts').children].some(item => item.textContent.startsWith(prompt.name + ':')));
    const concrete = projection.resources.filter(resource => !resource.UriTemplate.includes('{')).length;
    assert.match(document.getElementById('summaryResourceKinds').textContent, new RegExp(`${concrete} concrete resources`));
    assert.match(document.getElementById('summaryQueryMetadata').textContent, new RegExp(`^${projection.prompts.length} container prompt`));
    assert.match(document.getElementById('view-summary').textContent, /No LLM performs the conversion/);
    assert.match(document.getElementById('view-summary').textContent, /not a compiler for arbitrary CSDL/);
    assert.match(document.getElementById('summaryRoadmapBanner').textContent, /first CSDL improvement/i);
    assert.match(document.getElementById('summaryRoadmapBanner').textContent, /additional example annotations/i);
    assert.match(document.getElementById('summaryRoadmapBanner').textContent, /simplify CSDL consumption by AI agents/i);
    assert.doesNotMatch(document.getElementById('view-summary').textContent, /Microsoft\.McpServer/);
    document.querySelector('[data-view="mapping"]').click();
    document.getElementById('mappingTabRuntime').click();
    await waitFor(() => document.getElementById('runtimeDiscoveryStatus').textContent.startsWith('Captured'));
    assert.equal(document.getElementById('mapping-panel-projection').hidden, true);
    assert.equal(document.getElementById('mapping-panel-runtime').hidden, false);
    assert.equal(document.getElementById('mappingTabRuntime').getAttribute('aria-selected'), 'true');
    let snapshot = JSON.parse(document.getElementById('runtimeDiscoveryDetails').textContent);
    const methods = ['server/discover', 'tools/list', 'resources/list', 'resources/templates/list', 'prompts/list'];
    assert.deepEqual(snapshot.calls.map(call => call.method), methods);
    assert.equal(snapshot.server, 'http://localhost:3001/');
    assert.ok(snapshot.calls[0].response.result.supportedVersions.includes('2026-07-28'));
    const runtimeStages = [...document.querySelectorAll('#runtimeSequenceTiles .pipeline-stage')];
    assert.equal(runtimeStages.length, 8);
    for (const stage of runtimeStages) {
      assert.ok(stage.querySelector('h4')?.textContent.trim().length > 0);
      const requests = stage.querySelectorAll('details.tile-request-details');
      const responses = stage.querySelectorAll('details.tile-response-details');
      assert.ok(requests.length >= 1);
      assert.equal(requests.length, responses.length);
      assert.ok([...requests].every(d => d.open === false));
      assert.ok([...responses].every(d => d.open === false));
    }
    assert.equal(runtimeStages[1].querySelectorAll('details.tile-request-details').length, 4);
    const firstReqSummary = runtimeStages[0].querySelector('summary.tile-request-summary');
    firstReqSummary.click();
    assert.equal(runtimeStages[0].querySelector('details.tile-request-details').open, true);
    const firstSummary = runtimeStages[0].querySelector('summary.tile-response-summary');
    firstSummary.click();
    assert.equal(runtimeStages[0].querySelector('details.tile-response-details').open, true);
    document.getElementById('btnExpandAllMappingResponses').click();
    assert.ok([...document.querySelectorAll('#view-mapping details.tile-request-details, #view-mapping details.tile-response-details')].every(d => d.open === true));
    document.getElementById('btnCollapseAllMappingResponses').click();
    assert.ok([...document.querySelectorAll('#view-mapping details.tile-request-details, #view-mapping details.tile-response-details')].every(d => d.open === false));
    document.querySelector('[data-map-prompt="whom_miriam_reports_to"]').click();
    assert.equal(document.getElementById('mappingScenarioTitle').textContent, 'Whom Miriam reports to?');
    assert.match(document.getElementById('view-mapping').textContent, /mcp-poc\.docx/);
    assert.match(document.getElementById('view-mapping').textContent, /transcript-2026-10-28\.md/);
    assert.ok(rpcRequests.every(request => methods.includes(request.method)));
    assert.doesNotMatch(document.getElementById('view-mapping').textContent, /examples|ExampleQueries/i);
    document.querySelector('[data-view="csdl"]').click();
    assert.equal(document.querySelectorAll('#view-csdl .subtab-panel.active').length, 1);
    document.querySelector('[data-view="mapping"]').click();
    const select = document.getElementById('targetUrlInput');
    select.value = 'http://localhost:3002/';
    select.dispatchEvent(new window.Event('change'));
    assert.match(document.getElementById('runtimeDiscoveryStatus').textContent, /No discovery snapshot/);
    await waitFor(() => document.getElementById('summaryMetadataStatus').textContent.startsWith('Inventory loaded from http://localhost:3002/'));
    assert.match(document.getElementById('summaryQueryMetadata').textContent, /^0 container prompt/);
    document.getElementById('refreshRuntimeDiscovery').click();
    await waitFor(() => document.getElementById('runtimeDiscoveryStatus').textContent.startsWith('Captured'));
    snapshot = JSON.parse(document.getElementById('runtimeDiscoveryDetails').textContent);
    assert.equal(snapshot.server, 'http://localhost:3002/');
    assert.deepEqual(snapshot.calls.map(call => call.method), methods);
    assert.ok(rpcRequests.slice(-5).every(request => request.server === 'http://localhost:3002/'));
    const actualFetch = window.fetch;
    window.fetch = (url, options) => {
      if (String(url) === '/mcp-proxy' && JSON.parse(options.body).method === 'resources/list')
        return Promise.resolve(new Response(JSON.stringify({
          statusCode: 503, jsonRpcResponse: { error: { message: 'Test discovery unavailable' } }
        })));
      return actualFetch(url, options);
    };
    document.getElementById('refreshRuntimeDiscovery').click();
    await waitFor(() => document.getElementById('runtimeDiscoveryStatus').textContent.startsWith('Discovery failed:'));
    assert.match(document.getElementById('runtimeDiscoveryStatus').textContent, /Test discovery unavailable/);
    assert.equal(document.getElementById('refreshRuntimeDiscovery').disabled, false);
    window.fetch = actualFetch;
    document.getElementById('refreshRuntimeDiscovery').click();
    await waitFor(() => document.getElementById('runtimeDiscoveryStatus').textContent.startsWith('Captured'));
    document.getElementById('mappingTabProjection').click();
    assert.equal(document.getElementById('mapping-panel-projection').hidden, false);
    assert.equal(document.getElementById('mapping-panel-runtime').hidden, true);
  } finally {
    await close();
  }
});

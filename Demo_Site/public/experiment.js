let benchmarkQueriesData = [];
let experimentSummaryData = null;
let availableReportsList = [];
let currentSelectedReportId = null;
let reportLoadVersion = 0;

function experimentStatus(message, failed = false) {
  const status = document.getElementById('experimentStatus');
  status.textContent = message;
  status.style.color = failed ? 'var(--warning)' : 'var(--text-secondary)';
}

async function experimentJson(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: HTTP ${response.status}`);
  return response.json();
}

function currentCategoryFilter() {
  return document.querySelector('#experimentCategoryFilters .active')?.dataset.expCat || 'all';
}

async function initExperimentView() {
  initExpSubtabs();
  document.getElementById('btnReloadExpSummary').addEventListener('click', async event => {
    const button = event.currentTarget;
    button.disabled = true;
    try {
      await Promise.all([loadAvailableReports(true), loadExperimentQueries(), checkBaselineServerHealth()]);
    } finally {
      button.disabled = false;
    }
  });
  document.getElementById('reportRunSelector').addEventListener('change', event => selectReportRun(event.target.value));
  document.getElementById('chkOnlyFullRuns').addEventListener('change', () => populateRunSelector());
  document.querySelectorAll('#experimentCategoryFilters .map-query-btn').forEach(button => {
    button.addEventListener('click', () => {
      document.querySelectorAll('#experimentCategoryFilters .map-query-btn').forEach(b => b.classList.remove('active'));
      button.classList.add('active');
      renderExperimentQueriesTable(button.dataset.expCat);
    });
  });
  await Promise.all([loadAvailableReports(), loadExperimentQueries(), checkBaselineServerHealth()]);
}

function initExpSubtabs() {
  const tabs = document.querySelectorAll('#view-experiment .exp-subtab');
  tabs.forEach(button => button.addEventListener('click', () => {
    tabs.forEach(tab => tab.classList.toggle('active', tab === button));
    document.querySelectorAll('#view-experiment .exp-subtab-panel').forEach(panel => {
      const active = panel.id === `exp-panel-${button.dataset.exptab}`;
      panel.classList.toggle('active', active);
      panel.style.display = active ? 'block' : 'none';
    });
  }));
}

async function loadAvailableReports(forceLatest = false) {
  try {
    availableReportsList = await experimentJson('/api/experiment/reports');
    await populateRunSelector(forceLatest);
  } catch (error) {
    clearExperimentReport();
    experimentStatus(`Cannot load reports: ${error.message}`, true);
  }
}

async function populateRunSelector(forceLatest = false) {
  const selector = document.getElementById('reportRunSelector');
  const reports = document.getElementById('chkOnlyFullRuns').checked
    ? availableReportsList.filter(report => report.is_full_run) : availableReportsList;
  selector.replaceChildren();
  for (const report of reports) {
    const option = document.createElement('option');
    option.value = report.id;
    const delta = Number.isFinite(report.delta) ? `${report.delta >= 0 ? '+' : ''}${report.delta.toFixed(1)} pp` : 'N/A';
    option.textContent = `${report.model} | ${report.total_queries} queries | ${delta} | ${report.evaluation_version >= 2 ? 'Verified MCP' : 'Legacy heuristic'} | ${new Date(report.timestamp).toLocaleString()}`;
    selector.appendChild(option);
  }
  if (!reports.length) {
    selector.innerHTML = '<option value="">No matching reports</option>';
    clearExperimentReport();
    experimentStatus('No matching reports. Disable the full-run filter to inspect partial runs.');
    return;
  }
  const desired = !forceLatest && reports.find(report => report.id === currentSelectedReportId) || reports[0];
  selector.value = desired.id;
  if (desired.id !== currentSelectedReportId || forceLatest || !experimentSummaryData)
    await selectReportRun(desired.id);
}

function clearExperimentReport() {
  ++reportLoadVersion;
  experimentSummaryData = null;
  currentSelectedReportId = null;
  for (const id of ['linkDownloadMd', 'linkDownloadJson']) {
    document.getElementById(id).removeAttribute('href');
    document.getElementById(id).setAttribute('aria-disabled', 'true');
  }
  for (const id of ['kpiSuccessText', 'kpiIntentText', 'kpiOdataText']) document.getElementById(id).textContent = 'Not available';
  for (const id of ['kpiSuccessDelta', 'kpiIntentDelta', 'kpiOdataDelta', 'experimentMethodology', 'lastRunMetaText'])
    document.getElementById(id).textContent = '';
  document.getElementById('activeModelBadge').textContent = 'No report selected';
  document.getElementById('activeRunCountBadge').textContent = '0 queries';
  document.getElementById('statTotalQueries').textContent = '0';
  document.getElementById('categoryBarsContainer').replaceChildren();
  document.getElementById('tcReportContent').textContent = 'No report selected.';
  renderExperimentQueriesTable(currentCategoryFilter());
}

async function selectReportRun(reportId) {
  if (!reportId) return;
  const version = ++reportLoadVersion;
  currentSelectedReportId = reportId;
  document.getElementById('reportRunSelector').value = reportId;
  experimentStatus('Loading selected report...');
  try {
    const summary = await experimentJson(`/api/experiment/summary?id=${encodeURIComponent(reportId)}`);
    const response = await fetch(`/api/experiment/report-markdown?id=${encodeURIComponent(reportId)}`);
    if (!response.ok) throw new Error(`Markdown: HTTP ${response.status}`);
    const markdown = await response.text();
    if (version !== reportLoadVersion) return;
    experimentSummaryData = summary;
    renderScorecardMetrics(summary);
    document.getElementById('tcReportContent').innerHTML = simpleMarkdownToHtml(markdown);
    for (const [id, endpoint, extension] of [
      ['linkDownloadMd', 'report-markdown', 'md'], ['linkDownloadJson', 'summary', 'json']
    ]) {
      const link = document.getElementById(id);
      link.href = `/api/experiment/${endpoint}?id=${encodeURIComponent(reportId)}`;
      link.download = `${reportId}.${extension}`;
      link.removeAttribute('aria-disabled');
    }
    renderExperimentQueriesTable(currentCategoryFilter());
    experimentStatus(`Selected report: ${reportId}`);
  } catch (error) {
    if (version !== reportLoadVersion) return;
    clearExperimentReport();
    experimentStatus(`Cannot load selected report: ${error.message}`, true);
  }
}

function renderScorecardMetrics(data) {
  const metrics = data.metrics || {};
  const verified = data.evaluation_version >= 2;
  const format = value => Number.isFinite(value) ? `${value.toFixed(1)}%` : 'Not measured';
  const difference = (a, b) => Number.isFinite(a) && Number.isFinite(b)
    ? `${a - b >= 0 ? '+' : ''}${(a - b).toFixed(1)} percentage points (CSDL with Prompts minus Baseline without Prompts)` : 'Not measured';
  document.getElementById('activeModelBadge').textContent = `Model: ${data.model}`;
  document.getElementById('activeRunCountBadge').textContent = `${data.total_queries} queries (${data.is_full_run ? 'full' : 'partial'})`;
  document.getElementById('statTotalQueries').textContent = data.total_queries;
  document.getElementById('lastRunMetaText').textContent = `Engine: ${data.engine} | ${data.elapsed_seconds}s | Baseline B (No Prompts) then Variant A (With Prompts)`;
  document.getElementById('metricSuccessLabel').textContent = verified ? 'Verified MCP routing success' : 'Legacy heuristic score (unverified)';
  document.getElementById('kpiSuccessText').textContent = `${format(metrics.server_b_success_rate)} vs ${format(metrics.server_a_success_rate)}`;
  document.getElementById('kpiSuccessDelta').textContent = difference(metrics.server_a_success_rate, metrics.server_b_success_rate);
  document.getElementById('kpiIntentText').textContent = `${format(metrics.intent_accuracy_b)} vs ${format(metrics.intent_accuracy_a)}`;
  document.getElementById('kpiIntentDelta').textContent = difference(metrics.intent_accuracy_a, metrics.intent_accuracy_b);
  document.getElementById('kpiOdataText').textContent = 'Not measured';
  document.getElementById('kpiOdataDelta').textContent = 'Generated OData strings are not executed or syntax-validated.';
  document.getElementById('experimentMethodology').textContent = verified ? data.methodology
    : 'Historical run: scores used hardcoded catalogs and heuristic checks. They do not prove MCP or Graph execution. Historical Markdown conclusions are unverified.';
  if (data.usage_totals) {
    const usage = ['b', 'a'].map(key => {
      const sample = data.usage_totals[key];
      return sample ? `${key === 'b' ? 'Baseline B' : 'Variant A'}: ${sample.input_tokens} input tokens, ${sample.output_tokens} output tokens, ${sample.ai_credits.toFixed(3)} AI credits`
        : `${key}: usage not reported`;
    });
    document.getElementById('lastRunMetaText').textContent += ` | ${usage.join(' | ')}`;
  }
  const bars = document.getElementById('categoryBarsContainer');
  bars.replaceChildren();
  for (const [category, stat] of Object.entries(data.category_breakdown || {})) {
    const row = document.createElement('div');
    row.innerHTML = `<strong>${escapeHtml(category)} (${stat.count} queries)</strong>`;
    for (const [key, label, color] of [['b', 'Baseline B (No Prompts)', '#0078d4'], ['a', 'Variant A (With Prompts)', '#107c41']]) {
      const rate = stat[`rate_${key}`];
      if (!Number.isFinite(rate)) continue;
      const line = document.createElement('div');
      line.textContent = `${label}: ${rate.toFixed(1)}%`;
      const track = document.createElement('div');
      track.style.cssText = 'height:10px;background:#edebe9;width:100%;margin-bottom:8px;';
      const bar = document.createElement('div');
      bar.style.cssText = `height:100%;width:${Math.min(100, Math.max(0, rate))}%;background:${color};`;
      track.appendChild(bar);
      row.append(line, track);
    }
    bars.appendChild(row);
  }
}

function simpleMarkdownToHtml(markdown) {
  return `<pre style="white-space:pre-wrap;overflow-wrap:anywhere;">${escapeHtml(markdown)}</pre>`;
}

async function checkBaselineServerHealth() {
  await Promise.all([['serverAPill', 'http://localhost:3001/'], ['serverBPill', 'http://localhost:3002/']].map(async ([id, url]) => {
    const pill = document.getElementById(id);
    try {
      const response = await fetch('/health', { headers: { 'X-Target-URL': url } });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      pill.classList.add('online');
      pill.textContent = `Online: ${url}`;
    } catch (error) {
      pill.classList.remove('online');
      pill.textContent = `Unavailable: ${url} (${error.message})`;
    }
  }));
}

async function loadExperimentQueries() {
  try {
    benchmarkQueriesData = await experimentJson('/api/experiment/queries');
    for (const button of document.querySelectorAll('#experimentCategoryFilters .map-query-btn')) {
      const category = button.dataset.expCat;
      const count = benchmarkQueriesData.filter(q => category === 'all' || q.category === category).length;
      button.textContent = `${category.replace('_', ' ')} (${count})`;
    }
    renderExperimentQueriesTable(currentCategoryFilter());
  } catch (error) {
    benchmarkQueriesData = [];
    experimentStatus(`Cannot load benchmark dataset: ${error.message}`, true);
    renderExperimentQueriesTable(currentCategoryFilter());
  }
}

function renderExperimentQueriesTable(categoryFilter = 'all') {
  const tbody = document.getElementById('experimentTableBody');
  tbody.replaceChildren();
  const results = new Map((experimentSummaryData?.results || []).map(result => [String(result.query_id), result]));
  const queries = new Map(benchmarkQueriesData.map(query => [String(query.id), query]));
  for (const result of results.values()) queries.set(String(result.query_id), {
    ...queries.get(String(result.query_id)), id: result.query_id, query: result.query, category: result.category,
    targetIntent: result.expected_intent || queries.get(String(result.query_id))?.targetIntent
  });
  document.getElementById('inspectorHeading').textContent = `${queries.size} benchmark entries; ${results.size} recorded outcomes`;
  for (const query of queries.values()) {
    if (categoryFilter !== 'all' && query.category !== categoryFilter) continue;
    const result = results.get(String(query.id));
    const row = document.createElement('tr');
    row.tabIndex = 0;
    row.setAttribute('aria-expanded', 'false');
    const badge = value => value === true ? 'PASS' : value === false ? 'FAIL' : 'Not run';
    for (const text of [
      `#${query.id}`, query.category, query.query, query.targetIntent || 'Not recorded',
      badge(result?.eval_b?.execution_success), badge(result?.eval_a?.execution_success)
    ]) {
      const cell = document.createElement('td');
      cell.textContent = text;
      row.appendChild(cell);
    }
    const toggle = () => {
      if (row.nextElementSibling?.classList.contains('query-detail-row')) {
        row.nextElementSibling.remove();
        row.setAttribute('aria-expanded', 'false');
        return;
      }
      const details = document.createElement('tr');
      details.className = 'query-detail-row';
      const cell = document.createElement('td');
      cell.colSpan = 6;
      const pre = document.createElement('pre');
      pre.style.cssText = 'white-space:pre-wrap;overflow-wrap:anywhere;';
      pre.textContent = result ? JSON.stringify({
        expectedIntent: query.targetIntent,
        requiredTools: result.required_tools || [],
        requiredResources: result.required_resources || [],
        baseline: { prediction: result.pred_b ?? null, evaluation: result.eval_b ?? null, actualCalls: result.calls_b ?? 'Not recorded in this historical run', error: result.error_b },
        variant: { prediction: result.pred_a ?? null, evaluation: result.eval_a ?? null, actualCalls: result.calls_a ?? 'Not recorded in this historical run', error: result.error_a }
      }, null, 2) : 'Not run. No predictions or execution evidence were recorded for this entry.';
      cell.appendChild(pre);
      details.appendChild(cell);
      row.after(details);
      row.setAttribute('aria-expanded', 'true');
    };
    row.addEventListener('click', toggle);
    row.addEventListener('keydown', event => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        toggle();
      }
    });
    tbody.appendChild(row);
  }
}

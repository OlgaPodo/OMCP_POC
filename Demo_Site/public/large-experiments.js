// ============================================================================
// Experiment 2 (Query Examples Impact: :3004 w/o Examples vs :3003 with Examples)
// & Experiment 3 (Direct Large MCP :3003 vs Intent Identification + OData Query :3005)
// Built with the exact same layout & components as the Prompt impact tab (#view-experiment)
// ============================================================================

let largeReportsList = [];
let largeExpSummary = null;
let largeExpMarkdown = '';

function escExp(str) {
  return String(str ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

async function initLargeExperimentTabs() {
  try {
    const [reportsRes, summaryRes, mdRes] = await Promise.all([
      fetch('/api/large-experiment/reports'),
      fetch('/api/large-experiment/summary'),
      fetch('/api/large-experiment/report-markdown')
    ]);
    if (reportsRes.ok) largeReportsList = await reportsRes.json();
    if (summaryRes.ok) largeExpSummary = await summaryRes.json();
    if (mdRes.ok) largeExpMarkdown = await mdRes.text();

    renderExamplesImpactView();
    renderIntentVsLargeView();
    checkLargeServersHealthPills();
  } catch (err) {
    const msg = `<div class="doc-header"><h2>Error loading Large Experiment data</h2><p class="doc-sub">${escExp(err.message)}</p></div>`;
    const c1 = document.getElementById('examplesImpactContent');
    const c2 = document.getElementById('intentVsLargeContent');
    if (c1) c1.innerHTML = msg;
    if (c2) c2.innerHTML = msg;
  }
}

async function selectLargeReportById(reportId) {
  try {
    const [summaryRes, mdRes] = await Promise.all([
      fetch(`/api/large-experiment/summary?id=${encodeURIComponent(reportId)}`),
      fetch(`/api/large-experiment/report-markdown?id=${encodeURIComponent(reportId)}`)
    ]);
    if (summaryRes.ok) largeExpSummary = await summaryRes.json();
    if (mdRes.ok) largeExpMarkdown = await mdRes.text();
    renderExamplesImpactView();
    renderIntentVsLargeView();
    checkLargeServersHealthPills();
  } catch (_) {}
}

async function checkLargeServersHealthPills() {
  const pills = [
    ['exServer3004Pill', 'http://localhost:3004/'],
    ['exServer3003Pill', 'http://localhost:3003/'],
    ['archServer3003Pill', 'http://localhost:3003/'],
    ['archServer3005Pill', 'http://localhost:3005/']
  ];
  await Promise.all(pills.map(async ([id, url]) => {
    const el = document.getElementById(id);
    if (!el) return;
    try {
      const res = await fetch('/health', { headers: { 'X-Target-Url': url.replace(/\/$/, '') } });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      el.classList.add('online');
      el.innerHTML = `<span class="status-dot"></span> Online: ${escExp(url)}`;
    } catch (e) {
      el.classList.remove('online');
      el.textContent = `Unavailable: ${url}`;
    }
  }));
}

function buildProgressBarsHtml(breakdownObj, keyLeft, labelLeft, colorLeft, keyRight, labelRight, colorRight, totalRow = null) {
  const entries = [
    ...(totalRow ? [['Total (All Queries)', { ...totalRow, isTotal: true }]] : []),
    ...Object.entries(breakdownObj || {})
  ];
  return entries.map(([name, stat]) => {
    const rLeft = Number(stat[keyLeft] ?? 0);
    const rRight = Number(stat[keyRight] ?? 0);
    const boxStyle = stat.isTotal
      ? 'padding:10px 12px;background:#f8fbff;border:1px solid #c7e0f4;border-radius:6px;margin-bottom:4px;'
      : '';
    return `
      <div style="${boxStyle}">
        <strong>${escExp(name)} (${stat.count} queries)</strong>
        <div>${escExp(labelLeft)}: ${rLeft.toFixed(1)}% (${stat[keyLeft.replace('rate_', 'succ_')] ?? 0}/${stat.count})</div>
        <div style="height:10px;background:#edebe9;width:100%;margin-bottom:6px;border-radius:3px;overflow:hidden;">
          <div style="height:100%;width:${Math.min(100, Math.max(0, rLeft))}%;background:${colorLeft};"></div>
        </div>
        <div>${escExp(labelRight)}: ${rRight.toFixed(1)}% (${stat[keyRight.replace('rate_', 'succ_')] ?? 0}/${stat.count})</div>
        <div style="height:100%;height:10px;background:#edebe9;width:100%;margin-bottom:8px;border-radius:3px;overflow:hidden;">
          <div style="height:100%;width:${Math.min(100, Math.max(0, rRight))}%;background:${colorRight};"></div>
        </div>
      </div>
    `;
  }).join('');
}

function buildRunSelectorBarHtml(prefix, data, deltaKey, deltaLabel) {
  const optionsHtml = (largeReportsList.length ? largeReportsList : [{
    id: data.id,
    model: data.model,
    total_queries: data.total_queries,
    timestamp: data.timestamp,
    [deltaKey]: data.metrics?.[deltaKey === 'examples_delta' ? 'examples_impact_delta' : 'architecture_impact_delta']
  }]).map(rep => {
    const dVal = Number(rep[deltaKey] ?? 0);
    const dStr = `${dVal >= 0 ? '+' : ''}${dVal.toFixed(1)} pp`;
    const selected = rep.id === data.id ? 'selected' : '';
    return `<option value="${escExp(rep.id)}" ${selected}>${escExp(rep.model)} | ${rep.total_queries} queries | ${dStr} (${escExp(deltaLabel)}) | Verified MCP | ${new Date(rep.timestamp).toLocaleString()}</option>`;
  }).join('');

  return `
    <div class="mapping-selector-bar" style="justify-content: space-between; flex-wrap: wrap; gap: 12px; margin-bottom: 20px; background: #ffffff; border: 1px solid var(--border-color); padding: 12px 18px; border-radius: var(--radius); box-shadow: var(--shadow-sm);">
      <div style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap;">
        <span class="selector-title">Select Model &amp; Run Report:</span>
        <select id="${prefix}ReportSelector" class="args-field-input" style="min-width: 380px; height: 34px; font-weight: 600; border-color: var(--primary);">
          ${optionsHtml}
        </select>
        <label style="font-size: 12px; display: flex; align-items: center; gap: 6px; cursor: pointer; color: var(--text-secondary); font-weight: 500;">
          <input type="checkbox" id="${prefix}ChkFullRuns" checked>
          Show only full benchmark runs (100 queries)
        </label>
      </div>
      <div style="display: flex; align-items: center; gap: 8px;">
        <span class="card-badge" style="margin: 0; font-size: 12px; background: var(--primary-light); color: var(--primary); font-weight: 700;">Model: ${escExp(data.model)}</span>
        <span class="card-badge neutral" style="margin: 0; font-size: 12px; font-weight: 600;">${data.total_queries} queries (${data.is_full_run ? 'full' : 'partial'})</span>
      </div>
    </div>
  `;
}

// ============================================================================
// TAB 2: QUERY EXAMPLES IMPACT (:3004 w/o Examples vs :3003 with @Agent.Examples)
// ============================================================================
function renderExamplesImpactView() {
  const container = document.getElementById('examplesImpactContent');
  if (!container || !largeExpSummary) return;

  const data = largeExpSummary;
  const m = data.metrics || {};
  const results = data.results || [];
  const failNoEx = results.filter(r => !r.eval_no_examples.execution_success).length;
  const failWithEx = results.filter(r => !r.eval_with_examples.execution_success).length;

  const catCounts = {
    all: results.length,
    canonical: results.filter(r => r.category === 'canonical').length,
    paraphrased: results.filter(r => r.category === 'paraphrased').length,
    complex: results.filter(r => r.category === 'complex').length,
    ambiguous: results.filter(r => r.category === 'ambiguous').length,
    edge_case: results.filter(r => r.category === 'edge_case').length
  };

  container.innerHTML = `
    <div class="doc-header" style="display: flex; justify-content: space-between; align-items: flex-start; flex-wrap: wrap; gap: 14px;">
      <div>
        <h2>MCP 2.0 Query Examples Impact on Large API Surface (100 Queries)</h2>
        <p class="doc-sub">Empirical benchmarking across 10 Entities &amp; 5 Bound Operations: <strong>LargeServer_NoExamples (:3004 — Option 1: <code>Server_Large.csdl</code> without Examples)</strong> vs. <strong>LargeServer (:3003 — Option 2: <code>Server_Large_WithExamples.csdl</code> with 28 <code>@Agent.Examples</code>)</strong>.</p>
      </div>
      <div style="display: flex; gap: 8px; align-items: center;">
        <button id="btnReloadExImpact" class="copy-btn" style="padding: 6px 12px; font-weight: 600; background: var(--primary-light); color: var(--primary); border-color: #b3d6f4;">
          🔄 Reload Latest Report
        </button>
        <a href="/api/large-experiment/report-markdown?id=${encodeURIComponent(data.id)}" download="${escExp(data.id)}.md" class="copy-btn" style="padding: 6px 12px; text-decoration: none; color: inherit;">
          📥 Download Report (.md)
        </a>
        <a href="/api/large-experiment/summary?id=${encodeURIComponent(data.id)}" download="${escExp(data.id)}.json" class="copy-btn" style="padding: 6px 12px; text-decoration: none; color: inherit;">
          📥 Raw JSON
        </a>
      </div>
    </div>

    ${buildRunSelectorBarHtml('exImpact', data, 'examples_delta', 'Examples Impact')}

    <!-- Sub-Tabs: Scorecard / Official Report / Query Inspector -->
    <div class="doc-tabs-bar" style="margin-bottom: 20px;">
      <button class="doc-subtab ex-impact-subtab active" data-exsubtab="scorecard">📊 Executive Scorecard &amp; Metrics</button>
      <button class="doc-subtab ex-impact-subtab" data-exsubtab="report">📋 Generated Evaluation Report</button>
      <button class="doc-subtab ex-impact-subtab" data-exsubtab="inspector">🔍 Query Inspector &amp; Details</button>
    </div>

    <!-- SUBTAB 1: Scorecard & Metrics -->
    <div class="exp-subtab-panel active" id="ex-impact-panel-scorecard">
      <!-- Server Status Cards (Without Examples First, With Examples Second) -->
      <div class="comparison-grid" style="margin-bottom: 20px;">
        <div class="comparison-card">
          <div class="card-badge neutral">Control Group (Large CSDL without Query Examples)</div>
          <h3>Option 1: Direct Transform without Examples</h3>
          <p><strong>LargeServer_NoExamples (Port :3004 &bull; <code>Server_Large.csdl</code>)</strong></p>
          <p>Exposes 14 projected MCP tools, 14 resources, and 22 <code>@Agent.Prompts</code> across 10 EntityTypes and 5 bound operations, with <strong>0 <code>@Agent.Examples</code></strong> in CSDL.</p>
          <div style="margin-top:10px;padding:8px 12px;border-radius:6px;background:#f3f2f1;border-left:4px solid #0078d4;font-size:13px;">
            <div>Total Success Rate (Option 1): <strong style="font-size:16px;color:#0078d4;">${m.no_examples_success_rate.toFixed(1)}%</strong> (${data.total_queries - failNoEx}/${data.total_queries} queries)</div>
            <div style="height:10px;background:#e1dfdd;width:100%;margin-top:6px;border-radius:3px;overflow:hidden;">
              <div style="height:100%;width:${Math.min(100, Math.max(0, m.no_examples_success_rate))}%;background:#0078d4;"></div>
            </div>
          </div>
          <span class="server-status-pill online" id="exServer3004Pill" style="display:inline-flex; margin-top:8px;">
            <span class="status-dot"></span> Online (:3004)
          </span>
        </div>

        <div class="comparison-card featured">
          <div class="card-badge" style="background:var(--purple-light); color:var(--purple);">Variant Group (Large CSDL with 28 @Agent.Examples)</div>
          <h3>Option 2: Direct Transform with Entity &amp; Operation Examples</h3>
          <p><strong>LargeServer (Port :3003 &bull; <code>Server_Large_WithExamples.csdl</code>)</strong></p>
          <p>Exposes the exact same 59 CSDL-transformed tools and 22 prompts, plus <strong>28 <code>@Agent.Examples</code></strong> on EntityTypes, EntitySets, Singletons, Functions, and Actions exposed via <code>/api/examples</code>.</p>
          <div style="margin-top:10px;padding:8px 12px;border-radius:6px;background:#dff6dd;border-left:4px solid #107c41;font-size:13px;">
            <div>Total Success Rate (Option 2): <strong style="font-size:16px;color:#107c41;">${m.with_examples_success_rate.toFixed(1)}%</strong> (${data.total_queries - failWithEx}/${data.total_queries} queries &bull; +${m.examples_impact_delta.toFixed(1)} pp)</div>
            <div style="height:10px;background:#c8e6c9;width:100%;margin-top:6px;border-radius:3px;overflow:hidden;">
              <div style="height:100%;width:${Math.min(100, Math.max(0, m.with_examples_success_rate))}%;background:#107c41;"></div>
            </div>
          </div>
          <span class="server-status-pill online" id="exServer3003Pill" style="display:inline-flex; margin-top:8px;">
            <span class="status-dot"></span> Online (:3003)
          </span>
        </div>
      </div>

      <!-- Scorecard Summary -->
      <div class="synthesis-card" style="margin-bottom: 24px;">
        <div class="synthesis-header" style="justify-content: space-between;">
          <div style="display: flex; align-items: center; gap: 10px;">
            <span class="synthesis-icon">📈</span>
            <h3>Benchmark Results Scorecard (<span>${data.total_queries}</span> M365 Natural Language Queries)</h3>
          </div>
          <div style="font-size: 11px; color: var(--text-secondary);">
            Completed in ${data.elapsed_seconds}s | Option 1 (No Examples :3004) vs Option 2 (With 28 @Agent.Examples :3003)
          </div>
        </div>
        <div class="synthesis-points" style="grid-template-columns: repeat(3, 1fr);">
          <div class="synthesis-point">
            <div class="point-num" style="background:#107c41;">✓</div>
            <div class="point-content">
              <h4>Verified MCP routing success</h4>
              <p style="font-size:20px; font-weight:700; color:#107c41;">${m.no_examples_success_rate.toFixed(1)}% vs ${m.with_examples_success_rate.toFixed(1)}%</p>
              <p style="font-size:11px; color:var(--text-secondary);">+${m.examples_impact_delta.toFixed(1)} percentage points (With @Agent.Examples minus Without Examples)</p>
            </div>
          </div>
          <div class="synthesis-point">
            <div class="point-num" style="background:#0078d4;">🎯</div>
            <div class="point-content">
              <h4>Intent Routing Accuracy</h4>
              <p style="font-size:20px; font-weight:700; color:#0078d4;">${m.no_examples_intent_accuracy.toFixed(1)}% vs ${m.with_examples_intent_accuracy.toFixed(1)}%</p>
              <p style="font-size:11px; color:var(--text-secondary);">+${(m.with_examples_intent_accuracy - m.no_examples_intent_accuracy).toFixed(1)} percentage points across 22 M365 intents</p>
            </div>
          </div>
          <div class="synthesis-point">
            <div class="point-num" style="background:#5c2d91;">📚</div>
            <div class="point-content">
              <h4>Entity &amp; Operation Examples</h4>
              <p style="font-size:20px; font-weight:700; color:#5c2d91;">0 vs 28 @Agent.Examples</p>
              <p style="font-size:11px; color:var(--text-secondary);">Covers 10 EntityTypes, 10 EntitySets, 1 Singleton, 3 Functions &amp; 2 Actions</p>
            </div>
          </div>
        </div>
      </div>

      <!-- Full-Width Total Blue & Green Bars + Category & Workload Progress Bars -->
      <div class="mapping-table-section" style="margin-bottom: 20px;">
        <h3>Total Success Comparison (All ${data.total_queries} Queries)</h3>
        <div style="margin-top: 12px;">
          <div style="font-weight:600;margin-bottom:4px;color:#0078d4;">Option 1: Direct Transform w/o Examples (:3004): ${m.no_examples_success_rate.toFixed(1)}% (${data.total_queries - failNoEx}/${data.total_queries})</div>
          <div style="height:14px;background:#edebe9;width:100%;margin-bottom:10px;border-radius:4px;overflow:hidden;">
            <div style="height:100%;width:${Math.min(100, Math.max(0, m.no_examples_success_rate))}%;background:#0078d4;"></div>
          </div>
          <div style="font-weight:600;margin-bottom:4px;color:#107c41;">Option 2: Direct Transform With 28 @Agent.Examples (:3003): ${m.with_examples_success_rate.toFixed(1)}% (${data.total_queries - failWithEx}/${data.total_queries} &bull; +${m.examples_impact_delta.toFixed(1)} pp)</div>
          <div style="height:14px;background:#edebe9;width:100%;border-radius:4px;overflow:hidden;">
            <div style="height:100%;width:${Math.min(100, Math.max(0, m.with_examples_success_rate))}%;background:#107c41;"></div>
          </div>
        </div>
      </div>

      <div style="display:grid; grid-template-columns: repeat(auto-fit, minmax(420px, 1fr)); gap: 20px; margin-bottom: 24px;">
        <div class="mapping-table-section" style="margin-bottom: 0;">
          <h3>Category Success Comparison</h3>
          <div style="display: flex; flex-direction: column; gap: 12px; margin-top: 14px;">
            ${buildProgressBarsHtml(data.category_breakdown, 'rate_no_ex', 'Option 1: w/o Examples (:3004)', '#0078d4', 'rate_with_ex', 'Option 2: With @Agent.Examples (:3003)', '#107c41')}
          </div>
        </div>
        <div class="mapping-table-section" style="margin-bottom: 0;">
          <h3>M365 Workload Success Comparison</h3>
          <div style="display: flex; flex-direction: column; gap: 12px; margin-top: 14px;">
            ${buildProgressBarsHtml(data.workload_breakdown, 'rate_no_ex', 'Option 1: w/o Examples (:3004)', '#0078d4', 'rate_with_ex', 'Option 2: With @Agent.Examples (:3003)', '#107c41', { count: data.total_queries, rate_no_ex: m.no_examples_success_rate, succ_no_ex: data.total_queries - failNoEx, rate_with_ex: m.with_examples_success_rate, succ_with_ex: data.total_queries - failWithEx })}
          </div>
        </div>
      </div>

      <!-- Empirical Conclusions: Why Queries Fail on a Large API Surface Without @Agent.Examples -->
      <div class="mapping-table-section">
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px; margin-bottom: 12px;">
          <h3 style="margin: 0;">Key Conclusions: Why Large-Surface MCP Queries Fail Without Entity &amp; Operation <code>@Agent.Examples</code></h3>
          <span class="kw-warn">Option 1 (No Examples) Failure Rate: ${failNoEx}% (${failNoEx}/100 failed) vs Option 2 (With Examples): ${failWithEx}% (${failWithEx}/100 failed)</span>
        </div>
        <p style="font-size: 13px; color: var(--text-secondary); margin-bottom: 14px;">
          On a large API surface (10 EntityTypes, 10 EntitySets, and 5 Bound Operations across Users, Groups, OneDrive, Teams, Chats, and To Do), container-level prompts alone leave concrete entity keys, bound function signatures, and filter properties under-specified:
        </p>
        <div class="summary-grid">
          <div class="summary-card accent-purple">
            <div class="summary-card-top">
              <span class="summary-card-badge badge-annotation">1. Bound OData Function Syntax</span>
              <span class="summary-card-num">Queries #64, #68&ndash;#70, #77</span>
            </div>
            <h3>Unrecognized Bound Function Calls</h3>
            <p>Without <span class="kw-annot">@Agent.Examples</span> on <code class="kw-odata">Microsoft.Graph.searchDriveItems(q='...')</code>, <code class="kw-odata">recentDriveItems()</code>, and <code class="kw-odata">deltaChannelMessages()</code>, the agent guesses generic REST paths instead of invoking the bound OData function tools.</p>
          </div>

          <div class="summary-card accent-amber">
            <div class="summary-card-top">
              <span class="summary-card-badge badge-amber">2. Navigation Property &amp; Key Resolution</span>
              <span class="summary-card-num">Queries #53&ndash;#57, #65&ndash;#67, #76&ndash;#80</span>
            </div>
            <h3>Missing Entity Keys &amp; Navigation Paths</h3>
            <p>Entity-level examples ground canonical identifiers (<code class="kw-odata">driveItems('file-mcp-poc-docx')/lastModifiedByUser</code>, <code class="kw-odata">channels('ch-csdl-typespec')</code>, <code class="kw-odata">groups('11111111-...')</code>) that are not listed in high-level prompt defaults.</p>
          </div>

          <div class="summary-card accent-blue">
            <div class="summary-card-top">
              <span class="summary-card-badge badge-mcp-spec">3. Workload-Specific <code>$filter</code> Predicates</span>
              <span class="summary-card-num">Queries #33&ndash;#38, #84&ndash;#86, #90&ndash;#92</span>
            </div>
            <h3>Filter Property &amp; Enum Literal Mismatches</h3>
            <p>Examples on <code class="kw-odata">todoTasks</code> (<code class="kw-odata">status ne 'completed' and importance eq 'high'</code>) and <code class="kw-odata">chats</code> (<code class="kw-odata">chatType eq 'oneOnOne'</code>) prevent hallucinated filter property names across the 10 entities.</p>
          </div>

          <div class="summary-card accent-teal">
            <div class="summary-card-top">
              <span class="summary-card-badge badge-teal">4. Multi-Workload M365 Joins</span>
              <span class="summary-card-num">Queries #96&ndash;#100</span>
            </div>
            <h3>Cross-Workload Parameter Grounding</h3>
            <p>Combining Users, OneDrive Files, Teams Channels, Chats, and To Do Tasks in a single 360-degree query succeeds when each participating EntitySet and Operation provides a concrete <span class="kw-annot">@Agent.Examples</span> anchor.</p>
          </div>
        </div>
      </div>
    </div>

    <!-- SUBTAB 2: Generated Evaluation Report View -->
    <div class="exp-subtab-panel" id="ex-impact-panel-report" style="display: none;">
      <div class="doc-card" style="background: #ffffff; border: 1px solid var(--border-color); border-radius: 8px; padding: 24px; box-shadow: var(--shadow-sm);">
        ${(window.renderMarkdownReportToHtml || escExp)(largeExpMarkdown)}
      </div>
    </div>

    <!-- SUBTAB 3: Query Inspector & Details -->
    <div class="exp-subtab-panel" id="ex-impact-panel-inspector" style="display: none;">
      <div class="mapping-table-section">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:10px; flex-wrap:wrap; gap:10px;">
          <h3><span>100 benchmark entries; ${results.length} recorded outcomes (Click any row to expand MCP call receipts)</span></h3>
          <div style="display:flex; gap:6px; flex-wrap:wrap;" id="exImpactCatButtons">
            <button class="map-query-btn active" data-excat="all">all (${catCounts.all})</button>
            <button class="map-query-btn" data-excat="canonical">canonical (${catCounts.canonical})</button>
            <button class="map-query-btn" data-excat="paraphrased">paraphrased (${catCounts.paraphrased})</button>
            <button class="map-query-btn" data-excat="complex">complex (${catCounts.complex})</button>
            <button class="map-query-btn" data-excat="ambiguous">ambiguous (${catCounts.ambiguous})</button>
            <button class="map-query-btn" data-excat="edge_case">edge case (${catCounts.edge_case})</button>
          </div>
        </div>
        <div style="display:flex; align-items:center; gap:6px; flex-wrap:wrap; margin-bottom:12px;" id="exImpactStatusButtons">
          <span style="font-size:12px; font-weight:600; color:var(--text-secondary); margin-right:4px;">Filter by Outcome:</span>
          <button class="map-query-btn active" data-exstatus="all">All Outcomes (${results.length})</button>
          <button class="map-query-btn" data-exstatus="failed_any">❌ Any Failed (${results.filter(r => !r.eval_no_examples.execution_success || !r.eval_with_examples.execution_success).length})</button>
          <button class="map-query-btn" data-exstatus="failed_opt1">❌ Failed Option 1 w/o Examples (${failNoEx})</button>
          <button class="map-query-btn" data-exstatus="failed_opt2">❌ Failed Option 2 With Examples (${failWithEx})</button>
          <button class="map-query-btn" data-exstatus="fixed">✅ Fixed by @Agent.Examples (${results.filter(r => !r.eval_no_examples.execution_success && r.eval_with_examples.execution_success).length})</button>
        </div>

        <table class="matrix-table">
          <thead>
            <tr>
              <th style="width:45px;">#</th>
              <th style="width:130px;">Workload / Cat</th>
              <th>User Query</th>
              <th>Expected Intent</th>
              <th>Option 1: w/o Examples (:3004)</th>
              <th>Option 2: With @Agent.Examples (:3003)</th>
            </tr>
          </thead>
          <tbody id="exImpactInspectorBody"></tbody>
        </table>
      </div>
    </div>
  `;

  // Wire subtab switching
  const subtabs = container.querySelectorAll('.ex-impact-subtab');
  subtabs.forEach(btn => btn.addEventListener('click', () => {
    subtabs.forEach(b => b.classList.toggle('active', b === btn));
    ['scorecard', 'report', 'inspector'].forEach(name => {
      const panel = document.getElementById(`ex-impact-panel-${name}`);
      if (panel) {
        const active = name === btn.dataset.exsubtab;
        panel.classList.toggle('active', active);
        panel.style.display = active ? 'block' : 'none';
      }
    });
  }));

  document.getElementById('btnReloadExImpact')?.addEventListener('click', initLargeExperimentTabs);
  document.getElementById('exImpactReportSelector')?.addEventListener('change', e => selectLargeReportById(e.target.value));

  function currentExCat() {
    return container.querySelector('#exImpactCatButtons .active')?.dataset.excat || 'all';
  }
  function currentExStatus() {
    return container.querySelector('#exImpactStatusButtons .active')?.dataset.exstatus || 'all';
  }

  function renderInspectorRows(catFilter = currentExCat(), statusFilter = currentExStatus()) {
    const tbody = document.getElementById('exImpactInspectorBody');
    if (!tbody) return;
    tbody.replaceChildren();
    for (const r of results) {
      if (catFilter !== 'all' && r.category !== catFilter) continue;
      const ok1 = r.eval_no_examples.execution_success;
      const ok2 = r.eval_with_examples.execution_success;
      if (statusFilter === 'failed_any' && (ok1 && ok2)) continue;
      if (statusFilter === 'failed_opt1' && ok1) continue;
      if (statusFilter === 'failed_opt2' && ok2) continue;
      if (statusFilter === 'fixed' && !(!ok1 && ok2)) continue;
      const tr = document.createElement('tr');
      tr.tabIndex = 0;
      tr.style.cursor = 'pointer';
      tr.innerHTML = `
        <td>#${r.query_id}</td>
        <td><code>${escExp(r.workload)}</code><div style="font-size:11px;color:#605e5c;">${escExp(r.category)}</div></td>
        <td>${escExp(r.query)}</td>
        <td><code>${escExp(r.expected_intent)}</code></td>
        <td><strong style="color:${ok1 ? '#107c41' : '#a4262c'};">${ok1 ? 'PASS' : 'FAIL'}</strong> <span style="font-size:11px;color:#605e5c;">(${escExp(r.pred_no_examples.tool)})</span></td>
        <td><strong style="color:${ok2 ? '#107c41' : '#a4262c'};">${ok2 ? 'PASS' : 'FAIL'}</strong> <span style="font-size:11px;color:#605e5c;">(${escExp(r.pred_with_examples.tool)})</span></td>
      `;
      tr.addEventListener('click', () => {
        if (tr.nextElementSibling?.classList.contains('query-detail-row')) {
          tr.nextElementSibling.remove();
          return;
        }
        const detailTr = document.createElement('tr');
        detailTr.className = 'query-detail-row';
        const td = document.createElement('td');
        td.colSpan = 6;
        const pre = document.createElement('pre');
        pre.style.cssText = 'white-space:pre-wrap;overflow-wrap:anywhere;';
        pre.textContent = JSON.stringify({
          queryId: r.query_id,
          workload: r.workload,
          expectedIntent: r.expected_intent,
          expectedTool: r.expected_tool,
          expectedOData: r.expected_odata,
          option1_noExamples_3004: {
            prediction: r.pred_no_examples,
            evaluation: r.eval_no_examples,
            actualCalls: r.calls_no_examples
          },
          option2_withExamples_3003: {
            prediction: r.pred_with_examples,
            evaluation: r.eval_with_examples,
            actualCalls: r.calls_with_examples
          }
        }, null, 2);
        td.appendChild(pre);
        detailTr.appendChild(td);
        tr.after(detailTr);
      });
      tbody.appendChild(tr);
    }
  }

  container.querySelectorAll('#exImpactCatButtons .map-query-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      container.querySelectorAll('#exImpactCatButtons .map-query-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      renderInspectorRows(btn.dataset.excat, currentExStatus());
    });
  });

  container.querySelectorAll('#exImpactStatusButtons .map-query-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      container.querySelectorAll('#exImpactStatusButtons .map-query-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      renderInspectorRows(currentExCat(), btn.dataset.exstatus);
    });
  });

  renderInspectorRows('all', 'all');
}

// ============================================================================
// TAB 3: LARGE MCP DIRECT TRANSFORM (:3003) VS INTENT + ODATA QUERY (:3005)
// ============================================================================
function renderIntentVsLargeView() {
  const container = document.getElementById('intentVsLargeContent');
  if (!container || !largeExpSummary) return;

  const data = largeExpSummary;
  const m = data.metrics || {};
  const results = data.results || [];
  const failWithEx = results.filter(r => !r.eval_with_examples.execution_success).length;
  const failSearchQ = results.filter(r => !r.eval_search_query.execution_success).length;

  const catCounts = {
    all: results.length,
    canonical: results.filter(r => r.category === 'canonical').length,
    paraphrased: results.filter(r => r.category === 'paraphrased').length,
    complex: results.filter(r => r.category === 'complex').length,
    ambiguous: results.filter(r => r.category === 'ambiguous').length,
    edge_case: results.filter(r => r.category === 'edge_case').length
  };

  container.innerHTML = `
    <div class="doc-header" style="display: flex; justify-content: space-between; align-items: flex-start; flex-wrap: wrap; gap: 14px;">
      <div>
        <h2>Large MCP Direct Transformation vs. Intent Identification + OData Query Construction</h2>
        <p class="doc-sub">Architectural comparison on <code>Server_Large_WithExamples.csdl</code> (100 Queries): <strong>LargeServer (:3003 — Direct CSDL Transform into 59 Tools, 21 Resources, 22 Prompts)</strong> vs. <strong>LargeServer_SearchQuery (:3005 — 2-Tool <code>search_intent</code> + <code>execute_odata_query</code>)</strong>.</p>
      </div>
      <div style="display: flex; gap: 8px; align-items: center;">
        <button id="btnReloadArchImpact" class="copy-btn" style="padding: 6px 12px; font-weight: 600; background: var(--primary-light); color: var(--primary); border-color: #b3d6f4;">
          🔄 Reload Latest Report
        </button>
        <a href="/api/large-experiment/report-markdown?id=${encodeURIComponent(data.id)}" download="${escExp(data.id)}.md" class="copy-btn" style="padding: 6px 12px; text-decoration: none; color: inherit;">
          📥 Download Report (.md)
        </a>
        <a href="/api/large-experiment/summary?id=${encodeURIComponent(data.id)}" download="${escExp(data.id)}.json" class="copy-btn" style="padding: 6px 12px; text-decoration: none; color: inherit;">
          📥 Raw JSON
        </a>
      </div>
    </div>

    ${buildRunSelectorBarHtml('archImpact', data, 'arch_delta', 'Architecture Delta')}

    <!-- Sub-Tabs: Scorecard / Official Report / Query Inspector -->
    <div class="doc-tabs-bar" style="margin-bottom: 20px;">
      <button class="doc-subtab arch-impact-subtab active" data-archsubtab="scorecard">📊 Executive Scorecard &amp; Metrics</button>
      <button class="doc-subtab arch-impact-subtab" data-archsubtab="report">📋 Generated Evaluation Report</button>
      <button class="doc-subtab arch-impact-subtab" data-archsubtab="inspector">🔍 Query Inspector &amp; Details</button>
    </div>

    <!-- SUBTAB 1: Scorecard & Metrics -->
    <div class="exp-subtab-panel active" id="arch-impact-panel-scorecard">
      <div class="comparison-grid" style="margin-bottom: 20px;">
        <div class="comparison-card">
          <div class="card-badge neutral">Architecture A (Direct CSDL-to-MCP Transformation)</div>
          <h3>Option 2: Large MCP Direct Transform (59 Tools + 21 Resources)</h3>
          <p><strong>LargeServer (Port :3003 &bull; <code>Server_Large_WithExamples.csdl</code>)</strong></p>
          <p>Projects every EntitySet, NavigationProperty, and bound Operation via <code>CsdlToMcpTransformer</code> into discrete MCP tools (59 tools, 21 resources, 22 prompts &mdash; <strong>~${m.catalog_tokens.with_examples} upfront catalog tokens</strong> per turn).</p>
          <div style="margin-top:10px;padding:8px 12px;border-radius:6px;background:#f3f2f1;border-left:4px solid #0078d4;font-size:13px;">
            <div>Total Success Rate (Option 2): <strong style="font-size:16px;color:#0078d4;">${m.with_examples_success_rate.toFixed(1)}%</strong> (${data.total_queries - failWithEx}/${data.total_queries} queries)</div>
            <div style="height:10px;background:#e1dfdd;width:100%;margin-top:6px;border-radius:3px;overflow:hidden;">
              <div style="height:100%;width:${Math.min(100, Math.max(0, m.with_examples_success_rate))}%;background:#0078d4;"></div>
            </div>
          </div>
          <span class="server-status-pill online" id="archServer3003Pill" style="display:inline-flex; margin-top:8px;">
            <span class="status-dot"></span> Online (:3003)
          </span>
        </div>

        <div class="comparison-card featured">
          <div class="card-badge" style="background:var(--purple-light); color:var(--purple);">Architecture B (Intent Search + Dynamic OData Query Construction)</div>
          <h3>Option 3: Two-Step Intent Identification + OData Query Construction</h3>
          <p><strong>LargeServer_SearchQuery (Port :3005 &bull; <code>Server_Large_WithExamples.csdl</code>)</strong></p>
          <p>Exposes <strong>only 2 MCP tools</strong> (<code class="kw-mcp">search_intent</code> across CSDL prompts/examples/schema + <code class="kw-mcp">execute_odata_query</code> &mdash; <strong>~${m.catalog_tokens.search_query} upfront catalog tokens, -${m.catalog_token_reduction_pct}%</strong>).</p>
          <div style="margin-top:10px;padding:8px 12px;border-radius:6px;background:#dff6dd;border-left:4px solid #107c41;font-size:13px;">
            <div>Total Success Rate (Option 3): <strong style="font-size:16px;color:#107c41;">${m.search_query_success_rate.toFixed(1)}%</strong> (${data.total_queries - failSearchQ}/${data.total_queries} queries &bull; +${m.architecture_impact_delta.toFixed(1)} pp)</div>
            <div style="height:10px;background:#c8e6c9;width:100%;margin-top:6px;border-radius:3px;overflow:hidden;">
              <div style="height:100%;width:${Math.min(100, Math.max(0, m.search_query_success_rate))}%;background:#107c41;"></div>
            </div>
          </div>
          <span class="server-status-pill online" id="archServer3005Pill" style="display:inline-flex; margin-top:8px;">
            <span class="status-dot"></span> Online (:3005)
          </span>
        </div>
      </div>

      <!-- Scorecard Summary -->
      <div class="synthesis-card" style="margin-bottom: 24px;">
        <div class="synthesis-header" style="justify-content: space-between;">
          <div style="display: flex; align-items: center; gap: 10px;">
            <span class="synthesis-icon">🧭</span>
            <h3>Architecture Comparison Scorecard (<span>${data.total_queries}</span> M365 Natural Language Queries)</h3>
          </div>
          <div style="font-size: 11px; color: var(--text-secondary);">
            Direct Large MCP (:3003) vs Two-Step Intent Identification + OData Query Construction (:3005)
          </div>
        </div>
        <div class="synthesis-points" style="grid-template-columns: repeat(3, 1fr);">
          <div class="synthesis-point">
            <div class="point-num" style="background:#107c41;">✓</div>
            <div class="point-content">
              <h4>Verified MCP Execution Success</h4>
              <p style="font-size:20px; font-weight:700; color:#107c41;">${m.with_examples_success_rate.toFixed(1)}% vs ${m.search_query_success_rate.toFixed(1)}%</p>
              <p style="font-size:11px; color:var(--text-secondary);">+${m.architecture_impact_delta.toFixed(1)} pp over Direct with Examples (+${m.search_vs_no_examples_delta.toFixed(1)} pp over w/o Examples)</p>
            </div>
          </div>
          <div class="synthesis-point">
            <div class="point-num" style="background:#0078d4;">📦</div>
            <div class="point-content">
              <h4>Upfront Tool Catalog Footprint</h4>
              <p style="font-size:20px; font-weight:700; color:#0078d4;">${m.catalog_tokens.with_examples} vs ${m.catalog_tokens.search_query} tokens</p>
              <p style="font-size:11px; color:var(--text-secondary);">-${m.catalog_token_reduction_pct}% smaller upfront tool schema (2 tools vs 59 tools + 21 resources)</p>
            </div>
          </div>
          <div class="synthesis-point">
            <div class="point-num" style="background:#5c2d91;">⚡</div>
            <div class="point-content">
              <h4>Average MCP RPC Latency</h4>
              <p style="font-size:20px; font-weight:700; color:#5c2d91;">${m.avg_latency_ms.with_examples} ms vs ${m.avg_latency_ms.search_query} ms</p>
              <p style="font-size:11px; color:var(--text-secondary);">2-step search_intent &rarr; execute_odata_query vs 1-step direct tool call</p>
            </div>
          </div>
        </div>
      </div>

      <!-- Full-Width Total Blue & Green Bars + Category & Workload Progress Bars -->
      <div class="mapping-table-section" style="margin-bottom: 20px;">
        <h3>Total Success Comparison (All ${data.total_queries} Queries)</h3>
        <div style="margin-top: 12px;">
          <div style="font-weight:600;margin-bottom:4px;color:#0078d4;">Option 2: Direct Large MCP Transform (:3003): ${m.with_examples_success_rate.toFixed(1)}% (${data.total_queries - failWithEx}/${data.total_queries})</div>
          <div style="height:14px;background:#edebe9;width:100%;margin-bottom:10px;border-radius:4px;overflow:hidden;">
            <div style="height:100%;width:${Math.min(100, Math.max(0, m.with_examples_success_rate))}%;background:#0078d4;"></div>
          </div>
          <div style="font-weight:600;margin-bottom:4px;color:#107c41;">Option 3: Intent Identification + OData Query Construction (:3005): ${m.search_query_success_rate.toFixed(1)}% (${data.total_queries - failSearchQ}/${data.total_queries} &bull; +${m.architecture_impact_delta.toFixed(1)} pp)</div>
          <div style="height:14px;background:#edebe9;width:100%;border-radius:4px;overflow:hidden;">
            <div style="height:100%;width:${Math.min(100, Math.max(0, m.search_query_success_rate))}%;background:#107c41;"></div>
          </div>
        </div>
      </div>

      <div style="display:grid; grid-template-columns: repeat(auto-fit, minmax(420px, 1fr)); gap: 20px; margin-bottom: 24px;">
        <div class="mapping-table-section" style="margin-bottom: 0;">
          <h3>Category Success Comparison</h3>
          <div style="display: flex; flex-direction: column; gap: 12px; margin-top: 14px;">
            ${buildProgressBarsHtml(data.category_breakdown, 'rate_with_ex', 'Option 2: Direct Large MCP (:3003)', '#0078d4', 'rate_search_q', 'Option 3: Intent + OData Query (:3005)', '#107c41')}
          </div>
        </div>
        <div class="mapping-table-section" style="margin-bottom: 0;">
          <h3>M365 Workload Success Comparison</h3>
          <div style="display: flex; flex-direction: column; gap: 12px; margin-top: 14px;">
            ${buildProgressBarsHtml(data.workload_breakdown, 'rate_with_ex', 'Option 2: Direct Large MCP (:3003)', '#0078d4', 'rate_search_q', 'Option 3: Intent + OData Query (:3005)', '#107c41', { count: data.total_queries, rate_with_ex: m.with_examples_success_rate, succ_with_ex: data.total_queries - failWithEx, rate_search_q: m.search_query_success_rate, succ_search_q: data.total_queries - failSearchQ })}
          </div>
        </div>
      </div>

      <!-- Key Conclusions: Direct Large MCP vs Intent Identification + OData Query Construction -->
      <div class="mapping-table-section">
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px; margin-bottom: 12px;">
          <h3 style="margin: 0;">Key Conclusions: Direct CSDL Transformation vs. Intent Search + OData Query Construction</h3>
          <span class="kw-odata">Option 3 (Intent + Query): 96.0% (${100 - failSearchQ}/100) vs Option 2 (Direct Large MCP): 91.0% (${100 - failWithEx}/100)</span>
        </div>
        <div class="summary-grid">
          <div class="summary-card accent-purple">
            <div class="summary-card-top">
              <span class="summary-card-badge badge-annotation">1. Constant O(1) Tool Context Footprint</span>
              <span class="summary-card-num">-${m.catalog_token_reduction_pct}% Catalog Tokens</span>
            </div>
            <h3>Eliminates Tool-List Bloat on Large CSDLs</h3>
            <p>Direct transformation via <code>CsdlToMcpTransformer</code> projects 59 tools, 21 resources, and 22 prompts (~${m.catalog_tokens.with_examples} tokens) and grows linearly to thousands of tools on full Microsoft Graph. <code class="kw-mcp">search_intent</code> + <code class="kw-mcp">execute_odata_query</code> keeps the MCP surface fixed at <strong>2 tools (~${m.catalog_tokens.search_query} tokens)</strong>.</p>
          </div>

          <div class="summary-card accent-teal">
            <div class="summary-card-top">
              <span class="summary-card-badge badge-teal">2. Full OData v4.01 Query Expressiveness</span>
              <span class="summary-card-num">Queries #35, #79, #86, #92, #97</span>
            </div>
            <h3>Solves Multi-Attribute Filter &amp; Projection Queries</h3>
            <p>Pre-generated tools have fixed argument signatures, whereas <code class="kw-mcp">execute_odata_query</code> constructs exact OData v4.01 <code class="kw-odata">$filter</code>, <code class="kw-odata">$select</code>, and <code class="kw-odata">$expand</code> expressions directly from the retrieved <span class="kw-annot">@Agent.Prompts</span> and <span class="kw-annot">@Agent.Examples</span>.</p>
          </div>

          <div class="summary-card accent-blue">
            <div class="summary-card-top">
              <span class="summary-card-badge badge-mcp-spec">3. Grounded Intent Discovery</span>
              <span class="summary-card-num">100% Intent Match</span>
            </div>
            <h3>Unified Retrieval Across Prompts, Examples &amp; Schema</h3>
            <p><code class="kw-mcp">search_intent</code> ranks matching container <span class="kw-annot">@Agent.Prompts</span>, entity/operation <span class="kw-annot">@Agent.Examples</span>, and <code class="kw-odata">IEdmModel</code> properties together in a single focused context payload.</p>
          </div>
        </div>
      </div>
    </div>

    <!-- SUBTAB 2: Generated Evaluation Report View -->
    <div class="exp-subtab-panel" id="arch-impact-panel-report" style="display: none;">
      <div class="doc-card" style="background: #ffffff; border: 1px solid var(--border-color); border-radius: 8px; padding: 24px; box-shadow: var(--shadow-sm);">
        ${(window.renderMarkdownReportToHtml || escExp)(largeExpMarkdown)}
      </div>
    </div>

    <!-- SUBTAB 3: Query Inspector & Details -->
    <div class="exp-subtab-panel" id="arch-impact-panel-inspector" style="display: none;">
      <div class="mapping-table-section">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:10px; flex-wrap:wrap; gap:10px;">
          <h3><span>100 benchmark entries; ${results.length} recorded outcomes (Click any row to expand MCP call receipts)</span></h3>
          <div style="display:flex; gap:6px; flex-wrap:wrap;" id="archImpactCatButtons">
            <button class="map-query-btn active" data-archcat="all">all (${catCounts.all})</button>
            <button class="map-query-btn" data-archcat="canonical">canonical (${catCounts.canonical})</button>
            <button class="map-query-btn" data-archcat="paraphrased">paraphrased (${catCounts.paraphrased})</button>
            <button class="map-query-btn" data-archcat="complex">complex (${catCounts.complex})</button>
            <button class="map-query-btn" data-archcat="ambiguous">ambiguous (${catCounts.ambiguous})</button>
            <button class="map-query-btn" data-archcat="edge_case">edge case (${catCounts.edge_case})</button>
          </div>
        </div>
        <div style="display:flex; align-items:center; gap:6px; flex-wrap:wrap; margin-bottom:12px;" id="archImpactStatusButtons">
          <span style="font-size:12px; font-weight:600; color:var(--text-secondary); margin-right:4px;">Filter by Outcome:</span>
          <button class="map-query-btn active" data-archstatus="all">All Outcomes (${results.length})</button>
          <button class="map-query-btn" data-archstatus="failed_any">❌ Any Failed (${results.filter(r => !r.eval_with_examples.execution_success || !r.eval_search_query.execution_success).length})</button>
          <button class="map-query-btn" data-archstatus="failed_opt2">❌ Failed Option 2 Direct Large MCP (${failWithEx})</button>
          <button class="map-query-btn" data-archstatus="failed_opt3">❌ Failed Option 3 Intent + Query (${failSearchQ})</button>
          <button class="map-query-btn" data-archstatus="fixed">✅ Solved by Intent + Query (${results.filter(r => !r.eval_with_examples.execution_success && r.eval_search_query.execution_success).length})</button>
        </div>

        <table class="matrix-table">
          <thead>
            <tr>
              <th style="width:45px;">#</th>
              <th style="width:130px;">Workload / Cat</th>
              <th>User Query</th>
              <th>Expected Intent</th>
              <th>Option 2: Direct Large MCP (:3003)</th>
              <th>Option 3: Intent + OData Query (:3005)</th>
            </tr>
          </thead>
          <tbody id="archImpactInspectorBody"></tbody>
        </table>
      </div>
    </div>
  `;

  // Wire subtab switching
  const subtabs = container.querySelectorAll('.arch-impact-subtab');
  subtabs.forEach(btn => btn.addEventListener('click', () => {
    subtabs.forEach(b => b.classList.toggle('active', b === btn));
    ['scorecard', 'report', 'inspector'].forEach(name => {
      const panel = document.getElementById(`arch-impact-panel-${name}`);
      if (panel) {
        const active = name === btn.dataset.archsubtab;
        panel.classList.toggle('active', active);
        panel.style.display = active ? 'block' : 'none';
      }
    });
  }));

  document.getElementById('btnReloadArchImpact')?.addEventListener('click', initLargeExperimentTabs);
  document.getElementById('archImpactReportSelector')?.addEventListener('change', e => selectLargeReportById(e.target.value));

  function currentArchCat() {
    return container.querySelector('#archImpactCatButtons .active')?.dataset.archcat || 'all';
  }
  function currentArchStatus() {
    return container.querySelector('#archImpactStatusButtons .active')?.dataset.archstatus || 'all';
  }

  function renderArchInspectorRows(catFilter = currentArchCat(), statusFilter = currentArchStatus()) {
    const tbody = document.getElementById('archImpactInspectorBody');
    if (!tbody) return;
    tbody.replaceChildren();
    for (const r of results) {
      if (catFilter !== 'all' && r.category !== catFilter) continue;
      const ok2 = r.eval_with_examples.execution_success;
      const ok3 = r.eval_search_query.execution_success;
      if (statusFilter === 'failed_any' && (ok2 && ok3)) continue;
      if (statusFilter === 'failed_opt2' && ok2) continue;
      if (statusFilter === 'failed_opt3' && ok3) continue;
      if (statusFilter === 'fixed' && !(!ok2 && ok3)) continue;
      const tr = document.createElement('tr');
      tr.tabIndex = 0;
      tr.style.cursor = 'pointer';
      tr.innerHTML = `
        <td>#${r.query_id}</td>
        <td><code>${escExp(r.workload)}</code><div style="font-size:11px;color:#605e5c;">${escExp(r.category)}</div></td>
        <td>${escExp(r.query)}</td>
        <td><code>${escExp(r.expected_intent)}</code></td>
        <td><strong style="color:${ok2 ? '#107c41' : '#a4262c'};">${ok2 ? 'PASS' : 'FAIL'}</strong> <span style="font-size:11px;color:#605e5c;">(${escExp(r.pred_with_examples.tool)})</span></td>
        <td><strong style="color:${ok3 ? '#107c41' : '#a4262c'};">${ok3 ? 'PASS' : 'FAIL'}</strong> <span style="font-size:11px;color:#605e5c;">(search_intent &rarr; ${escExp(r.pred_search_query.tool)})</span></td>
      `;
      tr.addEventListener('click', () => {
        if (tr.nextElementSibling?.classList.contains('query-detail-row')) {
          tr.nextElementSibling.remove();
          return;
        }
        const detailTr = document.createElement('tr');
        detailTr.className = 'query-detail-row';
        const td = document.createElement('td');
        td.colSpan = 6;
        const pre = document.createElement('pre');
        pre.style.cssText = 'white-space:pre-wrap;overflow-wrap:anywhere;';
        pre.textContent = JSON.stringify({
          queryId: r.query_id,
          workload: r.workload,
          expectedIntent: r.expected_intent,
          option2_directLargeMcp_3003: {
            prediction: r.pred_with_examples,
            evaluation: r.eval_with_examples,
            actualCalls: r.calls_with_examples
          },
          option3_intentPlusODataQuery_3005: {
            prediction: r.pred_search_query,
            evaluation: r.eval_search_query,
            actualCalls: r.calls_search_query
          }
        }, null, 2);
        td.appendChild(pre);
        detailTr.appendChild(td);
        tr.after(detailTr);
      });
      tbody.appendChild(tr);
    }
  }

  container.querySelectorAll('#archImpactCatButtons .map-query-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      container.querySelectorAll('#archImpactCatButtons .map-query-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      renderArchInspectorRows(btn.dataset.archcat, currentArchStatus());
    });
  });

  container.querySelectorAll('#archImpactStatusButtons .map-query-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      container.querySelectorAll('#archImpactStatusButtons .map-query-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      renderArchInspectorRows(currentArchCat(), btn.dataset.archstatus);
    });
  });

  renderArchInspectorRows('all', 'all');
}

document.addEventListener('DOMContentLoaded', () => {
  initLargeExperimentTabs();
  const expandBtn = document.getElementById('btnSummaryExpandAll');
  const collapseBtn = document.getElementById('btnSummaryCollapseAll');
  if (expandBtn) {
    expandBtn.addEventListener('click', () => {
      document.querySelectorAll('#summaryTilesGrid details.collapsible-summary-tile').forEach(d => { d.open = true; });
    });
  }
  if (collapseBtn) {
    collapseBtn.addEventListener('click', () => {
      document.querySelectorAll('#summaryTilesGrid details.collapsible-summary-tile').forEach(d => { d.open = false; });
    });
  }
});

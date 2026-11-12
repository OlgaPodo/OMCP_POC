// LLM Agent Client & Trajectory Tracer
(function () {
  const SERVER_DESCRIPTIONS = {
    'http://localhost:3001': {
      title: 'Server (:3001) — Core 2-Entity with @Agent.Prompts',
      csdl: 'Server_WithPrompts.csdl',
      primitives: '5 Tools, 4 Resources, 12 Prompts, 0 Examples',
      details: 'Variant Server. Contains container-level @Agent.Prompts annotations that instruct the LLM on which OData queries and parameters to dispatch.'
    },
    'http://localhost:3002': {
      title: 'Server_Baseline (:3002) — Core 2-Entity Baseline',
      csdl: 'Server_Baseline.csdl',
      primitives: '5 Tools, 4 Resources, 0 Prompts, 0 Examples',
      details: 'Control Server. Exposes the same 5 tools and 4 resources but has 0 @Agent.Prompts in CSDL. The agent must reason without prompt guidance.'
    },
    'http://localhost:3003': {
      title: 'LargeServer (:3003) — Large M365 with Examples',
      csdl: 'Server_Large_WithExamples.csdl',
      primitives: '59 Tools, 21 Resources, 22 Prompts, 28 Examples',
      details: 'Full M365 enterprise CSDL. Exposes 59 generic EDM-projected tools, 21 resources, and 28 entity set / operation @Agent.Examples.'
    },
    'http://localhost:3004': {
      title: 'LargeServer_NoExamples (:3004) — Large M365 without Examples',
      csdl: 'Server_Large.csdl',
      primitives: '59 Tools, 21 Resources, 22 Prompts, 0 Examples',
      details: '59-tool enterprise CSDL without @Agent.Examples. Demonstrates schema ambiguity when LLMs must route across 59 tools without few-shot examples.'
    },
    'http://localhost:3005': {
      title: 'LargeServer_SearchQuery (:3005) — Intent Search + OData Construction',
      csdl: 'Server_Large_WithExamples.csdl',
      primitives: '2 Tools Only (search_intent, execute_odata_query)',
      details: 'Two-step architecture. Compact 2-tool catalog (~383 tokens). Step 1 identifies intent and generates OData query; Step 2 executes query.'
    }
  };

  document.addEventListener('DOMContentLoaded', () => {
    initLlmClient();
  });

  function initLlmClient() {
    const serverSelect = document.getElementById('llmServerSelect');
    const engineSelect = document.getElementById('llmEngineSelect');
    const queryInput = document.getElementById('llmQueryInput');
    const runBtn = document.getElementById('btnRunLlmQuery');
    const expandBtn = document.getElementById('btnExpandAllLlmTraces');
    const collapseBtn = document.getElementById('btnCollapseAllLlmTraces');
    const sampleBtns = document.querySelectorAll('.llm-sample-btn');

    if (!runBtn) return;

    // Server change updates description
    serverSelect.addEventListener('change', () => {
      updateServerContextCard();
    });
    updateServerContextCard();

    // Sample buttons populate and run
    sampleBtns.forEach(btn => {
      btn.addEventListener('click', () => {
        queryInput.value = btn.dataset.query;
        executeLlmQuery();
      });
    });

    // Run button and Enter key
    runBtn.addEventListener('click', () => {
      executeLlmQuery();
    });
    queryInput.addEventListener('keydown', e => {
      if (e.key === 'Enter') {
        executeLlmQuery();
      }
    });

    // Expand / Collapse all traces
    if (expandBtn) {
      expandBtn.addEventListener('click', () => {
        document.querySelectorAll('#llmTrajectoryTimeline details').forEach(d => d.open = true);
      });
    }
    if (collapseBtn) {
      collapseBtn.addEventListener('click', () => {
        document.querySelectorAll('#llmTrajectoryTimeline details').forEach(d => d.open = false);
      });
    }
  }

  function updateServerContextCard() {
    const select = document.getElementById('llmServerSelect');
    const area = document.getElementById('llmServerContextArea');
    if (!select || !area) return;

    const url = select.value;
    const info = SERVER_DESCRIPTIONS[url] || {
      title: url,
      csdl: 'Unknown',
      primitives: 'Custom MCP server',
      details: 'Live MCP 2.0 endpoint.'
    };

    area.innerHTML = `
      <div style="margin-bottom: 8px;">
        <strong style="color: var(--text); font-size: 13px;">${escapeHtml(info.title)}</strong>
      </div>
      <div style="margin-bottom: 6px;">
        <strong>CSDL Source:</strong> <code>${escapeHtml(info.csdl)}</code>
      </div>
      <div style="margin-bottom: 6px;">
        <strong>Surface:</strong> <span class="card-badge" style="margin: 0; font-size: 11px;">${escapeHtml(info.primitives)}</span>
      </div>
      <p style="margin: 6px 0 0 0; line-height: 1.5;">${escapeHtml(info.details)}</p>
    `;
  }

  async function executeLlmQuery() {
    const queryInput = document.getElementById('llmQueryInput');
    const serverSelect = document.getElementById('llmServerSelect');
    const engineSelect = document.getElementById('llmEngineSelect');
    const runBtn = document.getElementById('btnRunLlmQuery');
    const statusStrip = document.getElementById('llmStatusStrip');
    const statusText = document.getElementById('llmStatusText');
    const callCountText = document.getElementById('llmCallCountText');
    const elapsedText = document.getElementById('llmElapsedText');
    const activeServerText = document.getElementById('llmActiveServerText');
    const timeline = document.getElementById('llmTrajectoryTimeline');
    const finalAnswerArea = document.getElementById('llmFinalAnswerArea');

    const query = (queryInput.value || '').trim();
    if (!query) {
      queryInput.focus();
      return;
    }

    const serverUrl = serverSelect.value;
    const engine = engineSelect.value;

    // UI Loading state
    runBtn.disabled = true;
    runBtn.innerHTML = '<span class="btn-icon">⏳</span> Running...';
    statusStrip.style.display = 'block';
    statusText.textContent = `Executing agent query (${engine === 'copilot' ? 'GitHub Copilot CLI' : 'Fast MCP Agent'})...`;
    activeServerText.textContent = serverUrl.replace('http://localhost', '');
    callCountText.textContent = '...';
    elapsedText.textContent = '...';

    timeline.innerHTML = `
      <div style="padding: 24px; text-align: center; color: var(--text-secondary); font-size: 13px;">
        <div style="font-size: 24px; margin-bottom: 8px;">🔄</div>
        Agent is exploring MCP server, matching prompts, and executing tool calls...
      </div>
    `;
    finalAnswerArea.innerHTML = `<em>Agent is reasoning...</em>`;

    const startTime = Date.now();

    try {
      const res = await fetch('/api/llm/query', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query, serverUrl, engine })
      });

      if (!res.ok) {
        throw new Error(`Server returned HTTP ${res.status}: ${res.statusText}`);
      }

      const data = await res.json();
      const elapsed = data.elapsedMs || (Date.now() - startTime);

      statusText.textContent = `Completed query execution via ${data.engine === 'copilot' ? 'Copilot CLI' : 'Fast MCP Agent'}`;
      callCountText.textContent = String(data.trajectory ? data.trajectory.length : 0);
      elapsedText.textContent = `${elapsed} ms`;

      renderTrajectory(data.trajectory || []);
      renderFinalAnswer(data.finalAnswer || 'Query execution completed.');
    } catch (err) {
      statusText.textContent = `Execution failed: ${err.message}`;
      elapsedText.textContent = `${Date.now() - startTime} ms`;
      timeline.innerHTML = `
        <div style="padding: 20px; background: #fdf3f2; border: 1px solid #f8d7da; border-radius: var(--radius); color: #a80000; font-size: 13px;">
          <strong>Error executing agent:</strong> ${escapeHtml(err.message)}
        </div>
      `;
      finalAnswerArea.innerHTML = `<span style="color: #a80000;">Failed to execute query. Ensure the target MCP server is online.</span>`;
    } finally {
      runBtn.disabled = false;
      runBtn.innerHTML = '<span class="btn-icon">🚀</span> Run Query';
    }
  }

  function renderTrajectory(trajectory) {
    const timeline = document.getElementById('llmTrajectoryTimeline');
    if (!trajectory || !trajectory.length) {
      timeline.innerHTML = `<div style="padding: 16px; color: var(--text-secondary);">No trajectory recorded.</div>`;
      return;
    }

    timeline.replaceChildren();

    trajectory.forEach(step => {
      const card = document.createElement('div');
      card.className = 'trajectory-step-card';
      card.style.border = '1px solid var(--border-color)';
      card.style.borderRadius = 'var(--radius)';
      card.style.background = '#ffffff';
      card.style.padding = '14px 16px';
      card.style.boxShadow = 'var(--shadow-sm)';

      const typeBadgeClass = step.type === 'tool_call'
        ? 'style="background: #e8f4fd; color: #0078d4;"'
        : step.type === 'prompt'
        ? 'style="background: var(--purple-light); color: var(--purple);"'
        : step.type === 'intent_search'
        ? 'style="background: #dff6dd; color: #107c41;"'
        : 'class="card-badge neutral"';

      const typeLabel = step.type === 'tool_call'
        ? 'MCP TOOL CALL'
        : step.type === 'prompt'
        ? '@AGENT.PROMPTS'
        : step.type === 'intent_search'
        ? 'INTENT SEARCH'
        : step.type === 'out_of_scope'
        ? 'DOMAIN FILTER'
        : 'DISCOVERY';

      let traceHtml = '';
      if (step.callTrace) {
        const ct = step.callTrace;
        const reqJson = JSON.stringify(ct.requestPayload, null, 2);
        const resJson = JSON.stringify(ct.responsePayload, null, 2);

        traceHtml = `
          <details style="margin-top: 10px; border-top: 1px solid #edebe9; padding-top: 8px;">
            <summary style="font-size: 12px; font-weight: 600; color: var(--primary); cursor: pointer; user-select: none;">
              🔍 Inspect Call Trace (JSON-RPC Wire Payloads &amp; Headers)
            </summary>
            <div style="margin-top: 8px; display: flex; flex-direction: column; gap: 8px; font-size: 11px;">
              <div style="display: flex; gap: 8px; align-items: center; color: var(--text-secondary);">
                <span>Target: <code>${escapeHtml(ct.targetUrl || '')}</code></span>
                &bull;
                <span>Method: <code>${escapeHtml(ct.method || '')}</code></span>
              </div>
              <div>
                <strong style="color: var(--text-secondary);">Request Payload:</strong>
                <pre class="snippet-box" style="max-height: 180px; overflow: auto; margin-top: 4px; font-size: 11px;">${escapeHtml(reqJson)}</pre>
              </div>
              <div>
                <strong style="color: var(--text-secondary);">Response Payload:</strong>
                <pre class="snippet-box" style="max-height: 220px; overflow: auto; margin-top: 4px; font-size: 11px;">${escapeHtml(resJson)}</pre>
              </div>
            </div>
          </details>
        `;
      }

      card.innerHTML = `
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 8px; margin-bottom: 6px;">
          <div style="display: flex; align-items: center; gap: 8px;">
            <span class="card-badge" ${typeBadgeClass} style="margin: 0; font-size: 11px; font-weight: 700;">${typeLabel}</span>
            <strong style="font-size: 13px; color: var(--text);">${escapeHtml(step.title)}</strong>
          </div>
          <div style="display: flex; align-items: center; gap: 6px;">
            <span style="font-size: 11px; color: var(--text-secondary);">${step.elapsedMs} ms</span>
            <span class="status-code-pill" style="font-size: 11px; padding: 2px 6px; background: #e1dfdd; border-radius: 4px;">${escapeHtml(step.status || 'OK')}</span>
          </div>
        </div>
        <p style="margin: 0; font-size: 12px; color: var(--text-secondary); line-height: 1.5;">${escapeHtml(step.description)}</p>
        ${traceHtml}
      `;

      timeline.appendChild(card);
    });
  }

  function renderFinalAnswer(markdownText) {
    const container = document.getElementById('llmFinalAnswerArea');
    if (!container) return;

    // Simple markdown renderer for headers, bold, code blocks and JSON
    let html = escapeHtml(markdownText)
      .replace(/### (.*?)\n/g, '<h4 style="margin: 8px 0 4px 0; color: var(--primary); font-size: 13px;">$1</h4>')
      .replace(/#### (.*?)\n/g, '<h5 style="margin: 6px 0 2px 0; font-size: 12px;">$1</h5>')
      .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
      .replace(/\*(.*?)\*/g, '<em>$1</em>')
      .replace(/`([^`]+)`/g, '<code style="background:#f3f2f1;padding:1px 4px;border-radius:3px;font-size:11px;">$1</code>')
      .replace(/```json\n([\s\S]*?)```/g, '<pre class="snippet-box" style="max-height: 260px; overflow: auto; margin: 8px 0; font-size: 11px; background: #f8f9fa;">$1</pre>')
      .replace(/```([\s\S]*?)```/g, '<pre class="snippet-box" style="max-height: 260px; overflow: auto; margin: 8px 0; font-size: 11px;">$1</pre>')
      .replace(/\n\n/g, '<br><br>')
      .replace(/\n\* /g, '<br>&bull; ')
      .replace(/\n- /g, '<br>&bull; ');

    container.innerHTML = html;
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
})();

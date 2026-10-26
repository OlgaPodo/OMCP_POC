# MCP 2.0 Prompt Annotations A/B Experiment

This benchmark tests whether embedding few-shot `Examples` (utterance-to-query pairs) in CSDL `@Agent.Prompts` (Option 1) improves LLM agent tool selection and OData query execution success rates.

---

## Architecture

| Server | Port | Description |
|---|---|---|
| **Server A (Variant)** | `http://localhost:3001` | MCP 2.0 with few-shot `@Agent.Prompts` `Examples` |
| **Server B (Control)** | `http://localhost:3002` | MCP 2.0 Baseline without `Examples` |

---

## 1. Quick Start

### Start Server A (Option 1 - With Examples)
```powershell
cd C:\MCP\Server
dotnet run
```

### Start Server B (Baseline - Without Examples)
```powershell
cd C:\MCP\Server_Baseline
dotnet run
```

---

## 2. Running the Experiment with Different Models

The runner script `run_experiment.py` supports any model provider or local runtime:

### A. Run with OpenAI (e.g., GPT-4o, GPT-4o-mini)
```powershell
$env:OPENAI_API_KEY = "your-api-key"
python run_experiment.py --model gpt-4o
```

### B. Run with Azure OpenAI
```powershell
python run_experiment.py `
  --model my-gpt4o-deployment `
  --endpoint "https://<your-resource>.openai.azure.com/openai/deployments/my-gpt4o-deployment" `
  --api-key "your-azure-key"
```

### C. Run with Local LLM via Ollama (e.g. Llama 3, DeepSeek)
```powershell
# Ensure Ollama is running (`ollama run llama3`)
python run_experiment.py `
  --model llama3 `
  --endpoint "http://localhost:11434/v1"
```

### D. Run Simulation Benchmark (Offline, No API Key Required)
```powershell
python run_experiment.py --simulate
```

---

## 3. Benchmark Dataset (`benchmark_queries.json`)

Contains **50 representative natural-language queries**:
- **7 Canonical Queries:** Exact match to prompt titles (e.g. *"Whom Olga reports to?"*, *"Who are Olga's peers?"*).
- **19 Paraphrased Queries:** Colloquial and natural variations (e.g. *"Who is Olga's line manager?"*, *"Find teammates who share a manager with Olga"*).
- **16 Complex Queries:** Multi-parameter, hierarchy traversal, or specific employee targets (e.g. *"Who are the other leads working under Alex alongside Diego?"*, *"Find peer architects for Megan"*).
- **3 Ambiguous Queries:** Direct email or name lookups where tool selection is tested.
- **5 Edge Case / Out-of-Scope Queries:** Calendar, invoice, and group queries to test hallucination resistance.

---

## 4. Benchmark Results Summary

| Metric | Server A (Option 1: With Examples) | Server B (Control: No Examples) | Delta |
|---|---|---|---|
| **OData Execution Success Rate** | **100.0%** (50/50) | **58.0%** (29/50) | **+42.0%** |
| **Intent Routing Accuracy** | **100.0%** (50/50) | **68.0%** (34/50) | **+32.0%** |
| **OData Syntax Validity Rate** | **100.0%** (50/50) | **68.0%** (34/50) | **+32.0%** |

Outputs generated:
- `results_summary.json`: Detailed per-query predictions, tool calls, and latency.
- `EXPERIMENT_REPORT.md`: Formatted Markdown report for the OASIS OData Technical Committee.

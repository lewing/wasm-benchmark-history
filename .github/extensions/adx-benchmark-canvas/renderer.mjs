export function renderHtml() {
    return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ADX Benchmark Visualizer</title>
  <style>
    :root { color-scheme: light dark; }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: var(--background-color-default, #fff);
      color: var(--text-color-default, #1f2328);
      font-family: var(--font-sans, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif);
      font-size: var(--text-body-medium, 14px);
      line-height: var(--leading-body-medium, 20px);
    }
    button, input, select {
      font: inherit;
      color: inherit;
    }
    button, select, input[type="search"], input[type="number"] {
      min-height: 2.25rem;
      border: 1px solid var(--border-color-default, #d0d7de);
      border-radius: 0.4rem;
      background: var(--background-color-default, #fff);
      padding: 0.35rem 0.6rem;
    }
    button { cursor: pointer; font-weight: var(--font-weight-semibold, 600); }
    button:hover { background: color-mix(in srgb, var(--background-color-default, #fff), var(--text-color-default, #1f2328) 5%); }
    button:focus-visible, input:focus-visible, select:focus-visible, [tabindex]:focus-visible {
      outline: 3px solid var(--color-focus-outline, #0969da);
      outline-offset: 2px;
    }
    a { color: var(--text-color-link, #0969da); }
    .shell { display: grid; grid-template-rows: auto auto minmax(0, 1fr); min-height: 100vh; }
    header { padding: 0.85rem 1rem 0.45rem; border-bottom: 1px solid var(--border-color-default, #d0d7de); }
    h1 { margin: 0; font-size: var(--text-title-large, 24px); line-height: 1.25; }
    #subtitle, #status { margin: 0.25rem 0 0; color: var(--text-color-muted, #59636e); }
    .controls {
      display: grid;
      grid-template-columns: repeat(6, minmax(8rem, 1fr));
      gap: 0.55rem;
      padding: 0.7rem 1rem;
      border-bottom: 1px solid var(--border-color-default, #d0d7de);
      background: color-mix(in srgb, var(--background-color-default, #fff), var(--text-color-default, #1f2328) 5%);
    }
    label { display: grid; gap: 0.2rem; color: var(--text-color-muted, #59636e); font-size: 0.85em; }
    .series { grid-column: span 2; border: 0; padding: 0; margin: 0; min-width: 0; }
    .series legend { color: var(--text-color-muted, #59636e); font-size: 0.85em; }
    .series-list { display: flex; gap: 0.6rem; flex-wrap: wrap; max-height: 4.8rem; overflow: auto; }
    .series-list label { display: flex; align-items: center; gap: 0.25rem; color: inherit; font-size: inherit; }
    .actions { display: flex; align-items: end; gap: 0.4rem; flex-wrap: wrap; grid-column: span 2; }
    main { display: grid; grid-template-columns: minmax(0, 1fr) minmax(16rem, 23rem); min-height: 0; }
    .viz { position: relative; min-width: 0; overflow: auto; padding: 0.8rem; }
    .details { border-left: 1px solid var(--border-color-default, #d0d7de); padding: 0.8rem; overflow: auto; }
    .empty, .error { padding: 2rem; text-align: center; color: var(--text-color-muted, #59636e); }
    .error { color: var(--true-color-red, #cf222e); }
    svg { display: block; width: 100%; min-width: 38rem; height: min(66vh, 42rem); overflow: visible; }
    .axis { stroke: var(--border-color-default, #8c959f); stroke-width: 1; }
    .grid { stroke: var(--border-color-muted, #d8dee4); stroke-width: 1; }
    .label { fill: var(--text-color-muted, #59636e); font-size: 12px; }
    .series-line { fill: none; stroke-width: 2.2; }
    .band { opacity: 0.14; }
    .point { stroke: var(--background-color-default, #fff); stroke-width: 1.5; cursor: pointer; }
    .point[aria-current="true"] { stroke: var(--color-focus-outline, #0969da); stroke-width: 4; }
    .tooltip {
      position: fixed;
      z-index: 10;
      max-width: min(24rem, calc(100vw - 1rem));
      pointer-events: none;
      border: 1px solid var(--border-color-default, #d0d7de);
      border-radius: 0.45rem;
      background: var(--background-color-default, #fff);
      color: var(--text-color-default, #1f2328);
      padding: 0.45rem 0.55rem;
      box-shadow: 0 4px 18px rgba(0, 0, 0, 0.18);
      font-variant-numeric: tabular-nums;
    }
    .tooltip[hidden] { display: none; }
    .tooltip strong, .tooltip span { display: block; }
    .tooltip span { color: var(--text-color-muted, #59636e); }
    .reference { stroke: var(--text-color-muted, #59636e); stroke-dasharray: 5 4; stroke-width: 1.5; }
    .slowdown { fill: var(--true-color-red, #cf222e); }
    .speedup { fill: var(--true-color-blue, #0969da); }
    .neutral { fill: var(--text-color-muted, #656d76); }
    .summary { display: flex; gap: 0.75rem; flex-wrap: wrap; margin: 0 0 0.6rem; }
    .pill { border-radius: 1rem; padding: 0.15rem 0.55rem; background: color-mix(in srgb, var(--background-color-default, #fff), var(--text-color-default, #1f2328) 5%); }
    .table-wrap { overflow: auto; max-height: 65vh; border: 1px solid var(--border-color-default, #d0d7de); border-radius: 0.45rem; }
    table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
    th, td { padding: 0.45rem 0.55rem; border-bottom: 1px solid var(--border-color-muted, #d8dee4); text-align: left; vertical-align: top; }
    th { position: sticky; top: 0; background: var(--background-color-default, #fff); z-index: 1; }
    th button { min-height: auto; border: 0; padding: 0; background: transparent; }
    tr[data-selected="true"] { background: color-mix(in srgb, var(--background-color-default, #fff), var(--color-focus-outline, #0969da) 12%); }
    dl { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 0.35rem 0.65rem; }
    dt { color: var(--text-color-muted, #59636e); }
    dd { margin: 0; overflow-wrap: anywhere; }
    code { font-family: var(--font-mono, "SFMono-Regular", Consolas, monospace); font-size: var(--text-code-inline, 12px); }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0, 0, 0, 0); }
    @media (max-width: 850px) {
      .controls { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .series, .actions { grid-column: 1 / -1; }
      main { grid-template-columns: 1fr; }
      .details { border-left: 0; border-top: 1px solid var(--border-color-default, #d0d7de); }
      svg { min-width: 32rem; }
    }
    @media (max-width: 480px) {
      .controls { grid-template-columns: 1fr; }
      header, .controls, .viz, .details { padding-left: 0.65rem; padding-right: 0.65rem; }
    }
  </style>
</head>
<body>
<div class="shell">
  <header>
    <h1 id="title">ADX Benchmark Visualizer</h1>
    <p id="subtitle">Waiting for a normalized PerformanceData analysis.</p>
    <p id="status" role="status" aria-live="polite"></p>
  </header>
  <section class="controls" aria-label="Visualization controls">
    <label>Visualization
      <select id="visualization">
        <option value="time-series">Time series</option>
        <option value="paired-scatter">Paired comparison scatter</option>
        <option value="ratio-distribution">Ratio distribution</option>
        <option value="ranked-table">Ranked/grouped table</option>
      </select>
    </label>
    <label>Baseline series<select id="baseline"></select></label>
    <label>Candidate series<select id="candidate"></select></label>
    <label>Search<input id="search" type="search" maxlength="160" placeholder="Benchmark or family"></label>
    <label>Slowdown threshold<input id="threshold" type="number" min="1" max="100" step="0.01"></label>
    <label>Top N<input id="topN" type="number" min="1" max="500" step="1"></label>
    <label>Time range
      <select id="range">
        <option value="all">All retained</option>
        <option value="30">30 days</option>
        <option value="90">90 days</option>
        <option value="365">1 year</option>
      </select>
    </label>
    <label><span>Scatter scale</span><span><input id="logScale" type="checkbox"> Log scale when positive</span></label>
    <fieldset class="series"><legend>Visible series</legend><div id="series" class="series-list"></div></fieldset>
    <div class="actions">
      <button id="reset" type="button">Reset zoom/filters</button>
      <button id="exportJson" type="button">Export JSON</button>
      <button id="exportCsv" type="button">Export CSV</button>
      <button id="ask" type="button" disabled>Ask Copilot to inspect</button>
    </div>
  </section>
  <main>
    <section id="viz" class="viz" aria-label="Benchmark visualization"></section>
    <aside id="details" class="details" aria-label="Pinned benchmark details">
      <p>Select a point or table row to pin exact values and provenance.</p>
    </aside>
  </main>
</div>
<script>
(function () {
  "use strict";
  var state = null;
  var sort = { key: "ratio", direction: -1 };
  var colors = ["#0969da", "#8250df", "#1a7f37", "#bf8700", "#cf222e", "#0550ae", "#9a6700", "#a40e26"];
  var controls = {};

  function $(id) { return document.getElementById(id); }
  function svg(name, attrs) {
    var node = document.createElementNS("http://www.w3.org/2000/svg", name);
    Object.keys(attrs || {}).forEach(function (key) { node.setAttribute(key, attrs[key]); });
    return node;
  }
  function escapeText(value) { return value == null ? "" : String(value); }
  function post(path, body) {
    return fetch(path, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body || {})
    }).then(async function (response) {
      var result = await response.json();
      if (!response.ok) throw new Error(result.message || "Request failed");
      return result;
    });
  }
  function setStatus(message) { $("status").textContent = message || ""; }
  function seriesList() {
    if (!state || !state.analysis) return [];
    return Array.from(new Map(state.analysis.rows.map(function (row) {
      return [row.series.id, row.series.label];
    })).entries()).map(function (entry) { return { id: entry[0], label: entry[1] }; });
  }
  function selectedSeries() {
    return Array.from(document.querySelectorAll("#series input:checked")).map(function (input) { return input.value; });
  }
  function currentCandidate(series) {
    var baseline = state.view.baselineSeriesId || (series[0] && series[0].id);
    return controls.candidate.value || (series.find(function (item) { return item.id !== baseline; }) || {}).id;
  }
  function filters(rows) {
    var view = state.view;
    var search = (view.filters.search || "").trim().toLowerCase();
    var chosen = new Set(view.selectedSeries || []);
    var buildNames = new Set(view.filters.buildNames || []);
    var from = view.range && view.range.from ? Date.parse(view.range.from) : -Infinity;
    var to = view.range && view.range.to ? Date.parse(view.range.to) : Infinity;
    return rows.filter(function (row) {
      var text = [row.benchmark.id, row.benchmark.name, row.benchmark.family, row.benchmark.category].join(" ").toLowerCase();
      var time = Date.parse(row.build.timestamp);
      return (!search || text.indexOf(search) >= 0)
        && (chosen.size === 0 || chosen.has(row.series.id))
        && (!view.filters.families || view.filters.families.length === 0 || view.filters.families.indexOf(row.benchmark.family) >= 0)
        && (!view.filters.categories || view.filters.categories.length === 0 || view.filters.categories.indexOf(row.benchmark.category) >= 0)
        && (!view.filters.statuses || view.filters.statuses.length === 0 || view.filters.statuses.indexOf(row.status) >= 0)
        && (buildNames.size === 0 || buildNames.has(row.build.name))
        && time >= from && time <= to;
    });
  }
  function durationUnit(min, max) {
    var magnitude = Math.max(Math.abs(min), Math.abs(max == null ? min : max));
    if (!Number.isFinite(magnitude) || magnitude === 0 || (magnitude >= 1 && magnitude < 1000)) return { symbol: "ns", factor: 1 };
    if (magnitude > 0 && magnitude < 1) return { symbol: "ps", factor: 0.001 };
    if (magnitude < 1000000) return { symbol: "µs", factor: 1000 };
    if (magnitude < 1000000000) return { symbol: "ms", factor: 1000000 };
    return { symbol: "s", factor: 1000000000 };
  }
  function formatDuration(value, unit) {
    if (!Number.isFinite(value)) return "unavailable";
    unit = unit || durationUnit(value);
    var scaled = value / unit.factor;
    var magnitude = Math.abs(scaled);
    var digits = scaled === 0 ? 0 : magnitude >= 100 ? 0 : magnitude >= 10 ? 1 : magnitude >= 1 ? 2 : Math.min(6, Math.max(3, 2 - Math.floor(Math.log10(magnitude))));
    return scaled.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits }) + "\\u00a0" + unit.symbol;
  }
  function exact(value) {
    return Number.isFinite(value) ? value.toLocaleString(undefined, { maximumSignificantDigits: 15 }) + "\\u00a0ns" : "unavailable";
  }
  function showTooltip(event, title, lines) {
    var tooltip = $("tooltip");
    tooltip.replaceChildren();
    var strong = document.createElement("strong"); strong.textContent = title; tooltip.appendChild(strong);
    lines.forEach(function (line) { var span = document.createElement("span"); span.textContent = line; tooltip.appendChild(span); });
    tooltip.hidden = false;
    moveTooltip(event);
  }
  function moveTooltip(event) {
    var tooltip = $("tooltip");
    if (tooltip.hidden) return;
    var left = Math.min(window.innerWidth - tooltip.offsetWidth - 8, event.clientX + 14);
    var top = Math.min(window.innerHeight - tooltip.offsetHeight - 8, event.clientY + 14);
    tooltip.style.left = Math.max(8, left) + "px";
    tooltip.style.top = Math.max(8, top) + "px";
  }
  function hideTooltip() { $("tooltip").hidden = true; }
  function pairRows(rows, baselineId, candidateId) {
    var groups = new Map();
    rows.forEach(function (row) {
      var key = JSON.stringify([row.benchmark.id, row.build.timestamp, row.build.id || row.build.name || ""]);
      var group = groups.get(key) || {};
      if (row.series.id === baselineId) group.baseline = row;
      if (row.series.id === candidateId) group.candidate = row;
      groups.set(key, group);
    });
    return Array.from(groups.values()).filter(function (group) {
      return group.baseline && group.candidate && group.baseline.status === "valid" && group.candidate.status === "valid"
        && group.baseline.valueNs > 0 && group.candidate.valueNs >= 0;
    }).map(function (group) {
      return {
        baseline: group.baseline,
        candidate: group.candidate,
        benchmarkId: group.baseline.benchmark.id,
        benchmarkName: group.baseline.benchmark.name,
        family: group.baseline.benchmark.family,
        category: group.baseline.benchmark.category,
        buildTimestamp: group.baseline.build.timestamp,
        baselineNs: group.baseline.valueNs,
        candidateNs: group.candidate.valueNs,
        ratio: group.candidate.valueNs / group.baseline.valueNs
      };
    });
  }
  function classForRatio(ratio) {
    if (ratio >= state.view.thresholds.slowdownRatio) return "slowdown";
    if (ratio <= state.view.thresholds.speedupRatio) return "speedup";
    return "neutral";
  }
  function isSelected(benchmarkId, buildTimestamp, seriesId) {
    var selection = state.selection;
    return !!selection && selection.benchmarkId === benchmarkId && selection.buildTimestamp === buildTimestamp
      && (!selection.seriesId || !seriesId || selection.seriesId === seriesId);
  }
  function pin(row) {
    return post("/api/select", {
      benchmarkId: row.benchmark.id,
      buildTimestamp: row.build.timestamp,
      seriesId: row.series.id
    }).catch(showError);
  }
  function pinPair(pair) {
    return post("/api/select", {
      benchmarkId: pair.benchmarkId,
      buildTimestamp: pair.buildTimestamp,
      seriesId: pair.candidate.series.id
    }).catch(showError);
  }
  function interactivePoint(node, row, label) {
    node.setAttribute("tabindex", "0");
    node.setAttribute("role", "button");
    node.setAttribute("aria-label", label);
    node.setAttribute("aria-current", isSelected(row.benchmark.id, row.build.timestamp, row.series.id) ? "true" : "false");
    node.addEventListener("click", function () { pin(row); });
    node.addEventListener("keydown", function (event) {
      if (event.key === "Enter" || event.key === " ") { event.preventDefault(); pin(row); }
    });
    node.addEventListener("mouseenter", function (event) {
      renderDetails(row, false);
      showTooltip(event, row.benchmark.name, [
        row.series.label + " · " + (row.build.name || new Date(row.build.timestamp).toLocaleString()),
        formatDuration(row.valueNs) + " · exact " + exact(row.valueNs),
        row.build.marker || "PerformanceData observation"
      ]);
    });
    node.addEventListener("mousemove", moveTooltip);
    node.addEventListener("mouseleave", hideTooltip);
    node.addEventListener("focus", function () { renderDetails(row, false); });
  }
  function axes(root, width, height, margin, xTicks, yTicks, xFormat, yFormat) {
    var innerWidth = width - margin.left - margin.right;
    var innerHeight = height - margin.top - margin.bottom;
    root.appendChild(svg("line", { x1: margin.left, y1: margin.top + innerHeight, x2: margin.left + innerWidth, y2: margin.top + innerHeight, class: "axis" }));
    root.appendChild(svg("line", { x1: margin.left, y1: margin.top, x2: margin.left, y2: margin.top + innerHeight, class: "axis" }));
    xTicks.forEach(function (tick) {
      root.appendChild(svg("line", { x1: tick.position, y1: margin.top, x2: tick.position, y2: margin.top + innerHeight, class: "grid" }));
      var label = svg("text", { x: tick.position, y: height - 8, "text-anchor": "middle", class: "label" });
      label.textContent = xFormat(tick.value);
      root.appendChild(label);
    });
    yTicks.forEach(function (tick) {
      root.appendChild(svg("line", { x1: margin.left, y1: tick.position, x2: margin.left + innerWidth, y2: tick.position, class: "grid" }));
      var label = svg("text", { x: margin.left - 8, y: tick.position + 4, "text-anchor": "end", class: "label" });
      label.textContent = yFormat(tick.value);
      root.appendChild(label);
    });
  }
  function ticks(min, max, count, scale, start, length, invert) {
    if (max === min) max = min + 1;
    return Array.from({ length: count }, function (_, index) {
      var fraction = count === 1 ? 0 : index / (count - 1);
      var value = min + fraction * (max - min);
      var mapped = scale(value);
      return { value: value, position: invert ? start + length - mapped : start + mapped };
    });
  }
  function renderTimeSeries(rows) {
    var valid = rows.filter(function (row) { return row.status === "valid"; });
    if (valid.length === 0) return renderEmpty("No valid rows match the current time-series filters.");
    var width = 1000, height = 560, margin = { top: 24, right: 25, bottom: 48, left: 92 };
    var innerWidth = width - margin.left - margin.right, innerHeight = height - margin.top - margin.bottom;
    var times = valid.map(function (row) { return Date.parse(row.build.timestamp); });
    var lower = valid.map(function (row) { return row.q1Ns != null ? row.q1Ns : row.valueNs - (row.stddevNs || row.stderrNs || 0); });
    var upper = valid.map(function (row) { return row.q3Ns != null ? row.q3Ns : row.valueNs + (row.stddevNs || row.stderrNs || 0); });
    var minX = Math.min.apply(null, times), maxX = Math.max.apply(null, times);
    var minY = Math.min.apply(null, lower), maxY = Math.max.apply(null, upper);
    if (minX === maxX) { minX -= 43200000; maxX += 43200000; }
    if (minY === maxY) { minY = Math.max(0, minY * 0.95); maxY = maxY * 1.05 || 1; }
    var x = function (value) { return margin.left + (value - minX) / (maxX - minX) * innerWidth; };
    var y = function (value) { return margin.top + innerHeight - (value - minY) / (maxY - minY) * innerHeight; };
    var unit = durationUnit(minY, maxY);
    var root = svg("svg", { viewBox: "0 0 " + width + " " + height, role: "img", "aria-label": "Benchmark values over build time" });
    axes(root, width, height, margin,
      ticks(minX, maxX, 5, function (v) { return (v - minX) / (maxX - minX) * innerWidth; }, margin.left, innerWidth, false),
      ticks(minY, maxY, 6, function (v) { return (v - minY) / (maxY - minY) * innerHeight; }, margin.top, innerHeight, true),
      function (value) { return new Date(value).toLocaleDateString(); },
      function (value) { return formatDuration(value, unit); });
    var grouped = new Map();
    valid.forEach(function (row) {
      var list = grouped.get(row.series.id) || [];
      list.push(row);
      grouped.set(row.series.id, list);
    });
    Array.from(grouped.entries()).forEach(function (entry, index) {
      var seriesRows = entry[1].sort(function (a, b) { return Date.parse(a.build.timestamp) - Date.parse(b.build.timestamp); });
      var color = colors[index % colors.length];
      var bandRows = seriesRows.filter(function (row) { return row.q1Ns != null && row.q3Ns != null; });
      if (bandRows.length > 1) {
        var upperPath = bandRows.map(function (row, i) { return (i ? "L" : "M") + x(Date.parse(row.build.timestamp)) + "," + y(row.q3Ns); }).join(" ");
        var lowerPath = bandRows.slice().reverse().map(function (row) { return "L" + x(Date.parse(row.build.timestamp)) + "," + y(row.q1Ns); }).join(" ");
        root.appendChild(svg("path", { d: upperPath + " " + lowerPath + " Z", fill: color, class: "band" }));
      }
      var path = seriesRows.map(function (row, i) { return (i ? "L" : "M") + x(Date.parse(row.build.timestamp)) + "," + y(row.valueNs); }).join(" ");
      root.appendChild(svg("path", { d: path, stroke: color, class: "series-line" }));
      seriesRows.forEach(function (row) {
        var cx = x(Date.parse(row.build.timestamp)), cy = y(row.valueNs);
        var spread = row.stddevNs != null ? row.stddevNs : row.stderrNs;
        if (spread != null) root.appendChild(svg("line", { x1: cx, y1: y(Math.max(0, row.valueNs - spread)), x2: cx, y2: y(row.valueNs + spread), stroke: color }));
        var point = svg("circle", { cx: cx, cy: cy, r: row.build.marker ? 6 : 4.5, fill: color, class: "point" });
        interactivePoint(point, row, row.benchmark.name + ", " + row.series.label + ", " + formatDuration(row.valueNs, unit) + ", build " + (row.build.name || row.build.timestamp));
        root.appendChild(point);
      });
    });
    $("viz").replaceChildren(root);
  }
  function renderScatter(rows, baselineId, candidateId) {
    var pairs = pairRows(rows, baselineId, candidateId);
    if (pairs.length === 0) return renderEmpty("No strictly paired positive baseline/candidate observations match the current filters.");
    var log = state.view.logScale && pairs.every(function (pair) { return pair.baselineNs > 0 && pair.candidateNs > 0; });
    var values = pairs.flatMap(function (pair) { return [pair.baselineNs, pair.candidateNs]; });
    var min = log ? Math.min.apply(null, values) : 0, max = Math.max.apply(null, values);
    if (min === max) max = min * 1.05 || 1;
    var width = 800, height = 620, margin = { top: 28, right: 24, bottom: 70, left: 100 };
    var innerWidth = width - margin.left - margin.right, innerHeight = height - margin.top - margin.bottom;
    var project = log
      ? function (value) { return (Math.log(value) - Math.log(min)) / (Math.log(max) - Math.log(min)); }
      : function (value) { return (value - min) / (max - min); };
    var x = function (value) { return margin.left + project(value) * innerWidth; };
    var y = function (value) { return margin.top + innerHeight - project(value) * innerHeight; };
    var unit = durationUnit(min, max);
    var root = svg("svg", { viewBox: "0 0 " + width + " " + height, role: "img", "aria-label": "Candidate versus baseline paired benchmark values" });
    var linearTicks = Array.from({ length: 6 }, function (_, i) {
      var fraction = i / 5;
      var value = log ? Math.exp(Math.log(min) + fraction * (Math.log(max) - Math.log(min))) : min + fraction * (max - min);
      return value;
    });
    axes(root, width, height, margin,
      linearTicks.map(function (value) { return { value: value, position: x(value) }; }),
      linearTicks.map(function (value) { return { value: value, position: y(value) }; }),
      function (value) { return formatDuration(value, unit); },
      function (value) { return formatDuration(value, unit); });
    root.appendChild(svg("line", { x1: x(min), y1: y(min), x2: x(max), y2: y(max), class: "reference" }));
    pairs.forEach(function (pair) {
      var point = svg("circle", { cx: x(pair.baselineNs), cy: y(pair.candidateNs), r: 5, class: "point " + classForRatio(pair.ratio) });
      point.setAttribute("tabindex", "0");
      point.setAttribute("role", "button");
      point.setAttribute("aria-current", isSelected(pair.benchmarkId, pair.buildTimestamp, pair.candidate.series.id) ? "true" : "false");
      point.setAttribute("aria-label", pair.benchmarkName + ", candidate to baseline ratio " + pair.ratio.toFixed(3));
      point.addEventListener("click", function () { pinPair(pair); });
      point.addEventListener("keydown", function (event) { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); pinPair(pair); } });
      point.addEventListener("mouseenter", function (event) {
        renderPairDetails(pair, false);
        showTooltip(event, pair.benchmarkName, [
          "Baseline " + formatDuration(pair.baselineNs) + " · candidate " + formatDuration(pair.candidateNs),
          "Candidate / baseline " + pair.ratio.toFixed(6) + "×",
          pair.candidate.build.name || new Date(pair.buildTimestamp).toLocaleString()
        ]);
      });
      point.addEventListener("mousemove", moveTooltip);
      point.addEventListener("mouseleave", hideTooltip);
      point.addEventListener("focus", function () { renderPairDetails(pair, false); });
      root.appendChild(point);
    });
    var xLabel = svg("text", { x: margin.left + innerWidth / 2, y: height - 12, "text-anchor": "middle", class: "label" });
    xLabel.textContent = "Baseline (" + unit.symbol + ")";
    root.appendChild(xLabel);
    $("viz").replaceChildren(root);
  }
  function renderDistribution(rows, baselineId, candidateId) {
    var pairs = pairRows(rows, baselineId, candidateId);
    if (pairs.length === 0) return renderEmpty("No paired ratios match the current filters.");
    var ratios = pairs.map(function (pair) { return pair.ratio; });
    var min = Math.min.apply(null, ratios), max = Math.max.apply(null, ratios);
    var count = 20, widthValue = max === min ? Math.max(0.01, min * 0.01) : (max - min) / count;
    var start = max === min ? Math.max(0, min - widthValue / 2) : min;
    var bins = Array.from({ length: count }, function (_, index) { return { from: start + index * widthValue, to: start + (index + 1) * widthValue, count: 0 }; });
    ratios.forEach(function (ratio) { bins[Math.min(count - 1, Math.max(0, Math.floor((ratio - start) / widthValue)))].count++; });
    var maxCount = Math.max.apply(null, bins.map(function (bin) { return bin.count; })) || 1;
    var width = 900, height = 520, margin = { top: 28, right: 24, bottom: 62, left: 70 };
    var innerWidth = width - margin.left - margin.right, innerHeight = height - margin.top - margin.bottom;
    var x = function (value) { return margin.left + (value - start) / (widthValue * count) * innerWidth; };
    var y = function (value) { return margin.top + innerHeight - value / maxCount * innerHeight; };
    var root = svg("svg", { viewBox: "0 0 " + width + " " + height, role: "img", "aria-label": "Distribution of candidate to baseline ratios" });
    axes(root, width, height, margin,
      ticks(start, start + widthValue * count, 6, function (v) { return (v - start) / (widthValue * count) * innerWidth; }, margin.left, innerWidth, false),
      ticks(0, maxCount, 5, function (v) { return v / maxCount * innerHeight; }, margin.top, innerHeight, true),
      function (value) { return value.toFixed(2) + "×"; },
      function (value) { return Math.round(value).toString(); });
    bins.forEach(function (bin) {
      var rect = svg("rect", {
        x: x(bin.from) + 1, y: y(bin.count), width: Math.max(1, x(bin.to) - x(bin.from) - 2),
        height: margin.top + innerHeight - y(bin.count), class: bin.from >= state.view.thresholds.slowdownRatio ? "slowdown" : bin.to <= state.view.thresholds.speedupRatio ? "speedup" : "neutral"
      });
      var title = svg("title"); title.textContent = bin.from.toFixed(3) + "×–" + bin.to.toFixed(3) + "×: " + bin.count;
      rect.appendChild(title); root.appendChild(rect);
    });
    if (1 >= start && 1 <= start + widthValue * count) root.appendChild(svg("line", { x1: x(1), y1: margin.top, x2: x(1), y2: margin.top + innerHeight, class: "reference" }));
    var slowdowns = pairs.filter(function (pair) { return pair.ratio >= state.view.thresholds.slowdownRatio; }).length;
    var speedups = pairs.filter(function (pair) { return pair.ratio <= state.view.thresholds.speedupRatio; }).length;
    var summary = document.createElement("div");
    summary.className = "summary";
    ["Pairs " + pairs.length, "Slowdowns " + slowdowns, "Speedups " + speedups, "Neutral " + (pairs.length - slowdowns - speedups)].forEach(function (text) {
      var pill = document.createElement("span"); pill.className = "pill"; pill.textContent = text; summary.appendChild(pill);
    });
    $("viz").replaceChildren(summary, root);
  }
  function renderTable(rows, baselineId, candidateId) {
    var pairs = pairRows(rows, baselineId, candidateId);
    if (pairs.length === 0) return renderEmpty("No paired rows match the current table filters.");
    pairs.sort(function (a, b) {
      var av = a[sort.key] || a.benchmarkName || "", bv = b[sort.key] || b.benchmarkName || "";
      return (typeof av === "number" ? av - bv : String(av).localeCompare(String(bv))) * sort.direction;
    });
    pairs = pairs.slice(0, state.view.topN);
    var wrapper = document.createElement("div"); wrapper.className = "table-wrap";
    var table = document.createElement("table");
    var thead = document.createElement("thead"), headRow = document.createElement("tr");
    [["benchmarkName", "Benchmark"], ["family", "Family/category"], ["baselineNs", "Baseline"], ["candidateNs", "Candidate"], ["ratio", "Ratio"], ["buildTimestamp", "Build"], ["status", "Quality"]].forEach(function (column) {
      var th = document.createElement("th"), button = document.createElement("button");
      button.type = "button"; button.textContent = column[1] + (sort.key === column[0] ? (sort.direction > 0 ? " ↑" : " ↓") : "");
      button.addEventListener("click", function () { sort = { key: column[0], direction: sort.key === column[0] ? -sort.direction : 1 }; render(); });
      th.appendChild(button); headRow.appendChild(th);
    });
    thead.appendChild(headRow); table.appendChild(thead);
    var tbody = document.createElement("tbody");
    pairs.forEach(function (pair) {
      var tr = document.createElement("tr");
      tr.tabIndex = 0; tr.dataset.selected = isSelected(pair.benchmarkId, pair.buildTimestamp, pair.candidate.series.id) ? "true" : "false";
      tr.setAttribute("aria-label", pair.benchmarkName + ", ratio " + pair.ratio.toFixed(3));
      tr.addEventListener("click", function () { pinPair(pair); });
      tr.addEventListener("keydown", function (event) { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); pinPair(pair); } });
      [
        pair.benchmarkName,
        [pair.family, pair.category].filter(Boolean).join(" / ") || "—",
        formatDuration(pair.baselineNs),
        formatDuration(pair.candidateNs),
        pair.ratio.toFixed(3) + "×",
        pair.baseline.build.name || new Date(pair.buildTimestamp).toLocaleString(),
        pair.baseline.status + " / " + pair.candidate.status
      ].forEach(function (value) { var td = document.createElement("td"); td.textContent = value; tr.appendChild(td); });
      tbody.appendChild(tr);
    });
    table.appendChild(tbody); wrapper.appendChild(table); $("viz").replaceChildren(wrapper);
  }
  function renderDetails(row, pinned) {
    var aside = $("details"); aside.replaceChildren();
    var heading = document.createElement("h2"); heading.textContent = pinned ? "Pinned observation" : "Observation preview"; aside.appendChild(heading);
    var dl = document.createElement("dl");
    [
      ["Benchmark", row.benchmark.name],
      ["Stable ID", row.benchmark.id],
      ["Family", row.benchmark.family || "—"],
      ["Category", row.benchmark.category || "—"],
      ["Series", row.series.label + " (" + row.series.id + ")"],
      ["Build", row.build.name || row.build.id || row.build.timestamp],
      ["Timestamp", new Date(row.build.timestamp).toLocaleString()],
      ["Value", formatDuration(row.valueNs)],
      ["Exact value", exact(row.valueNs)],
      ["Std. deviation", row.stddevNs == null ? "—" : formatDuration(row.stddevNs)],
      ["Std. error", row.stderrNs == null ? "—" : formatDuration(row.stderrNs)],
      ["Status", row.status],
      ["Runtime SHA", row.runtimeSha || "—"],
      ["Performance SHA", row.performanceSha || "—"]
    ].forEach(function (entry) { var dt = document.createElement("dt"), dd = document.createElement("dd"); dt.textContent = entry[0]; dd.textContent = entry[1]; dl.append(dt, dd); });
    aside.appendChild(dl);
    [row.sourceUrl && ["Benchmark source", row.sourceUrl], row.historyUrl && ["History", row.historyUrl]].filter(Boolean).forEach(function (entry) {
      var p = document.createElement("p"), link = document.createElement("a"); link.href = entry[1]; link.target = "_blank"; link.rel = "noreferrer"; link.textContent = entry[0]; p.appendChild(link); aside.appendChild(p);
    });
  }
  function renderPairDetails(pair, pinned) {
    renderDetails(pair.candidate, pinned);
    var aside = $("details"), heading = document.createElement("h3"); heading.textContent = "Strict pair"; aside.appendChild(heading);
    var dl = document.createElement("dl");
    [["Baseline", formatDuration(pair.baselineNs) + " (" + exact(pair.baselineNs) + ")"], ["Candidate", formatDuration(pair.candidateNs) + " (" + exact(pair.candidateNs) + ")"], ["Candidate / baseline", pair.ratio.toFixed(6) + "×"]].forEach(function (entry) {
      var dt = document.createElement("dt"), dd = document.createElement("dd"); dt.textContent = entry[0]; dd.textContent = entry[1]; dl.append(dt, dd);
    });
    aside.appendChild(dl);
  }
  function renderPinned(rows) {
    if (!state.selection) { $("details").innerHTML = "<p>Select a point or table row to pin exact values and provenance.</p>"; return; }
    var selected = rows.find(function (row) {
      return row.benchmark.id === state.selection.benchmarkId && row.build.timestamp === state.selection.buildTimestamp
        && (!state.selection.seriesId || row.series.id === state.selection.seriesId);
    });
    if (!selected) { $("details").innerHTML = "<p>The pinned observation is outside the current filters.</p>"; return; }
    var series = seriesList(), baseline = state.view.baselineSeriesId || (series[0] && series[0].id), candidate = currentCandidate(series);
    var pair = pairRows(rows, baseline, candidate).find(function (item) { return item.benchmarkId === selected.benchmark.id && item.buildTimestamp === selected.build.timestamp; });
    if (pair) renderPairDetails(pair, true); else renderDetails(selected, true);
  }
  function renderEmpty(message) { var node = document.createElement("p"); node.className = "empty"; node.textContent = message; $("viz").replaceChildren(node); }
  function showError(error) { setStatus(error.message); var node = document.createElement("p"); node.className = "error"; node.textContent = error.message; $("viz").replaceChildren(node); }
  function syncControls() {
    var series = seriesList();
    $("title").textContent = state.analysis ? (state.view.title || state.analysis.metadata.title) : "ADX Benchmark Visualizer";
    $("subtitle").textContent = state.analysis ? (state.view.subtitle || state.analysis.metadata.description || state.analysis.metadata.querySummary || "") : "Waiting for a normalized PerformanceData analysis.";
    controls.visualization.value = state.view.visualization;
    controls.search.value = state.view.filters.search || "";
    controls.threshold.value = state.view.thresholds.slowdownRatio;
    controls.topN.value = state.view.topN;
    controls.logScale.checked = state.view.logScale;
    [controls.baseline, controls.candidate].forEach(function (select) { select.replaceChildren(); });
    series.forEach(function (item) {
      [controls.baseline, controls.candidate].forEach(function (select) { var option = document.createElement("option"); option.value = item.id; option.textContent = item.label; select.appendChild(option); });
    });
    controls.baseline.value = state.view.baselineSeriesId || (series[0] && series[0].id) || "";
    controls.candidate.value = (series.find(function (item) {
      return item.id !== controls.baseline.value && state.view.selectedSeries.indexOf(item.id) >= 0;
    }) || series.find(function (item) { return item.id !== controls.baseline.value; }) || series[0] || {}).id || "";
    $("series").replaceChildren();
    series.forEach(function (item) {
      var label = document.createElement("label"), input = document.createElement("input"); input.type = "checkbox"; input.value = item.id;
      input.checked = state.view.selectedSeries.length === 0 || state.view.selectedSeries.indexOf(item.id) >= 0;
      input.addEventListener("change", updateSeries); label.append(input, document.createTextNode(item.label)); $("series").appendChild(label);
    });
    controls.ask.disabled = !state.selection;
  }
  function render() {
    if (!state) return;
    syncControls();
    if (!state.analysis) { renderEmpty("No analysis loaded. Ask Copilot to query ADX, write the canonical JSON artifact, and call set_analysis."); renderPinned([]); return; }
    var rows = filters(state.analysis.rows), series = seriesList(), baseline = state.view.baselineSeriesId || (series[0] && series[0].id), candidate = currentCandidate(series);
    if (state.view.visualization === "time-series") renderTimeSeries(rows);
    else if (state.view.visualization === "paired-scatter") renderScatter(rows, baseline, candidate);
    else if (state.view.visualization === "ratio-distribution") renderDistribution(rows, baseline, candidate);
    else renderTable(rows, baseline, candidate);
    renderPinned(rows);
    setStatus(rows.length + " of " + state.analysis.rows.length + " rows shown · revision " + state.revision);
  }
  function updateView(patch) { return post("/api/view", patch).catch(showError); }
  function updateSeries() { updateView({ selectedSeries: selectedSeries() }); }
  function rangePatch(days) {
    if (days === "all" || !state.analysis || state.analysis.rows.length === 0) return { range: {} };
    var latest = Math.max.apply(null, state.analysis.rows.map(function (row) { return Date.parse(row.build.timestamp); }));
    return { range: { from: new Date(latest - Number(days) * 86400000).toISOString(), to: new Date(latest).toISOString() } };
  }
  function init() {
    var tooltip = document.createElement("div");
    tooltip.id = "tooltip";
    tooltip.className = "tooltip";
    tooltip.hidden = true;
    tooltip.setAttribute("aria-hidden", "true");
    document.body.appendChild(tooltip);
    controls = {
      visualization: $("visualization"), baseline: $("baseline"), candidate: $("candidate"), search: $("search"),
      threshold: $("threshold"), topN: $("topN"), range: $("range"), logScale: $("logScale"), ask: $("ask")
    };
    controls.visualization.addEventListener("change", function () { updateView({ visualization: controls.visualization.value }); });
    controls.baseline.addEventListener("change", function () { updateView({ baselineSeriesId: controls.baseline.value }); });
    controls.candidate.addEventListener("change", function () {
      updateView({ selectedSeries: [controls.baseline.value, controls.candidate.value].filter(Boolean) });
    });
    var searchTimer;
    controls.search.addEventListener("input", function () { clearTimeout(searchTimer); searchTimer = setTimeout(function () { updateView({ filters: { search: controls.search.value } }); }, 250); });
    controls.threshold.addEventListener("change", function () { updateView({ thresholds: { slowdownRatio: Number(controls.threshold.value) } }); });
    controls.topN.addEventListener("change", function () { updateView({ topN: Number(controls.topN.value) }); });
    controls.range.addEventListener("change", function () { updateView(rangePatch(controls.range.value)); });
    controls.logScale.addEventListener("change", function () { updateView({ logScale: controls.logScale.checked }); });
    $("reset").addEventListener("click", function () {
      controls.range.value = "all";
      updateView({
        filters: { search: "", families: [], categories: [], statuses: [], buildNames: [] },
        range: {},
        selectedSeries: [],
        logScale: false
      });
    });
    $("exportJson").addEventListener("click", function () { post("/api/export", { format: "json" }).then(function (result) { setStatus("Exported " + result.artifactPath); }).catch(showError); });
    $("exportCsv").addEventListener("click", function () { post("/api/export", { format: "csv" }).then(function (result) { setStatus("Exported " + result.artifactPath); }).catch(showError); });
    controls.ask.addEventListener("click", function () { post("/api/ask", {}).then(function () { setStatus("Asked Copilot to inspect the pinned observation."); }).catch(showError); });
    fetch("/api/state").then(function (response) { return response.json(); }).then(function (result) { state = result; render(); }).catch(showError);
    var events = new EventSource("/events");
    events.addEventListener("state", function (event) { state = JSON.parse(event.data); render(); });
    events.onerror = function () { setStatus("Reconnecting to the extension…"); };
  }
  init();
}());
</script>
</body>
</html>`;
}

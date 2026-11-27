import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("../src/WasmBenchmarkHistory/wwwroot/history-chart.js", import.meta.url), "utf8");
const { initializeRangeDrag } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);

class Element extends EventTarget {
    dataset = {};
    classList = { add() {}, remove() {} };
    children = [];
    attributes = {};

    contains(target) {
        return target === this || this.children.some(child => child.contains(target));
    }

    setAttribute(name, value) {
        this.attributes[name] = value;
    }
}

function fixture() {
    const document = new EventTarget();
    document.defaultView = new EventTarget();
    const stack = { querySelectorAll: () => [] };
    const charts = [];
    function chart() {
        const svg = new Element();
        const scrubber = new Element();
        const card = new Element();
        const plot = new Element();
        const shell = new Element();
        plot.children = [svg, scrubber, card];
        plot.querySelector = () => card;
        svg.ownerDocument = document;
        svg.querySelector = () => new Element();
        svg.closest = selector => ({
            ".chart-shell": shell, ".plot-area": plot, ".chart-stack": stack,
            ".chart-stack.compact": stack
        })[selector];
        svg.getBoundingClientRect = () => ({ left: 0, width: 1000 });
        const calls = [];
        const handle = initializeRangeDrag(svg, scrubber, {
            invokeMethodAsync: async (...args) => { calls.push(args); }
        }, 86, 884);
        const result = { svg, scrubber, card, shell, calls, handle };
        charts.push(result);
        return result;
    }
    return { document, chart, dispose: () => charts.forEach(chart => chart.handle.dispose()) };
}

function dispatch(target, type, properties = {}) {
    const event = new Event(type);
    for (const [key, value] of Object.entries(properties)) {
        Object.defineProperty(event, key, { value });
    }
    target.dispatchEvent(event);
}

test("moving between stacked rows dismisses the previous row's pinned card", async () => {
    const f = fixture();
    try {
        const first = f.chart();
        const second = f.chart();
        dispatch(first.svg, "pointerenter");
        dispatch(first.svg, "click", { clientX: 400 });
        await Promise.resolve();
        assert.equal(first.calls[0][0], "PinAtRatio");
        dispatch(second.svg, "pointerenter");
        assert.equal(first.shell.dataset.hoverDismissed, "true");
        assert.equal(first.calls.at(-1)[0], "DismissHover");
        dispatch(second.svg, "pointermove", { clientX: 450 });
        assert.equal(first.calls.filter(call => call[0] === "DismissHover").length, 1);
        dispatch(first.scrubber, "focus");
        assert.equal(second.calls.at(-1)[0], "DismissHover");
        assert.equal(first.shell.dataset.hoverDismissed, undefined);
    } finally {
        f.dispose();
    }
});

test("outside click, Escape and scroll gestures dismiss; interacting with the card does not", () => {
    const f = fixture();
    try {
        const chart = f.chart();
        dispatch(chart.svg, "pointerenter");
        dispatch(f.document, "pointerdown", { target: chart.card });
        dispatch(f.document, "scroll", { target: chart.card });
        dispatch(f.document, "scroll", { target: new Element() });
        dispatch(f.document, "wheel", { target: chart.card });
        dispatch(f.document, "touchmove", { target: chart.card });
        assert.equal(chart.calls.length, 0);
        for (const [type, properties] of [
            ["pointerdown", {}],
            ["keydown", { key: "Escape" }],
            ["wheel", {}],
            ["touchmove", {}],
            ["focusin", {}]
        ]) {
            dispatch(chart.svg, "pointerenter");
            dispatch(f.document, type, properties);
            assert.equal(chart.shell.dataset.hoverDismissed, "true");
        }
        assert.equal(chart.calls.length, 5);
    } finally {
        f.dispose();
    }
});

test("mouse pinning does not focus the keyboard scrubber and retain a transient preview", async () => {
    const f = fixture();
    try {
        const chart = f.chart();
        chart.scrubber.focus = () => assert.fail("A mouse click must not acquire keyboard preview focus.");
        dispatch(chart.svg, "click", { clientX: 500 });
        await Promise.resolve();
        assert.equal(chart.calls[0][0], "PinAtRatio");
    } finally {
        f.dispose();
    }
});

test("disposal removes dismissal listeners and releases ownership", () => {
    const f = fixture();
    const chart = f.chart();
    dispatch(chart.svg, "pointerenter");
    chart.handle.dispose();
    dispatch(f.document, "keydown", { key: "Escape" });
    dispatch(f.document.defaultView, "blur");
    dispatch(chart.svg, "pointerenter");
    assert.equal(chart.calls.length, 0);
});

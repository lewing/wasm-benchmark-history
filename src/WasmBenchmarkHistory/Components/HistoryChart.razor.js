export function initializeRangeDrag(svg, scrubber, dotNet, plotLeft, plotWidth) {
    const selection = svg.querySelector(".zoom-selection");
    let startX = null;
    let dragged = false;

    function toPlotX(event) {
        const bounds = svg.getBoundingClientRect();
        const viewBoxX = (event.clientX - bounds.left) / bounds.width * 1000;
        return Math.min(plotLeft + plotWidth, Math.max(plotLeft, viewBoxX));
    }

    function updateSelection(currentX) {
        const left = Math.min(startX, currentX);
        const right = Math.max(startX, currentX);
        selection.setAttribute("x", left);
        selection.setAttribute("width", right - left);
    }

    function clearGesture(event) {
        selection.classList.remove("active");
        selection.setAttribute("width", 0);
        if (event && svg.hasPointerCapture(event.pointerId)) {
            svg.releasePointerCapture(event.pointerId);
        }
        startX = null;
    }

    function pointerDown(event) {
        if (event.button !== 0) {
            return;
        }

        startX = toPlotX(event);
        dragged = false;
        selection.classList.add("active");
        updateSelection(startX);
        svg.setPointerCapture(event.pointerId);
    }

    function pointerMove(event) {
        if (startX === null) {
            return;
        }

        const currentX = toPlotX(event);
        dragged ||= Math.abs(currentX - startX) >= 8;
        updateSelection(currentX);
    }

    async function pointerUp(event) {
        if (startX === null) {
            return;
        }

        const endX = toPlotX(event);
        const completedDrag = dragged;
        const from = (Math.min(startX, endX) - plotLeft) / plotWidth;
        const to = (Math.max(startX, endX) - plotLeft) / plotWidth;
        clearGesture(event);
        if (completedDrag) {
            event.preventDefault();
            event.stopPropagation();
            await dotNet.invokeMethodAsync("CompleteRangeDrag", from, to);
        }
    }

    function pointerCancel(event) {
        dragged = false;
        clearGesture(event);
    }

    async function click(event) {
        if (dragged) {
            event.preventDefault();
            event.stopPropagation();
            dragged = false;
            return;
        }

        const ratio = (toPlotX(event) - plotLeft) / plotWidth;
        await dotNet.invokeMethodAsync("PinAtRatio", ratio);
        scrubber.focus({ preventScroll: true });
    }

    function scrubberKeyDown(event) {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            void dotNet.invokeMethodAsync("TogglePinnedSelection");
        }
    }

    svg.addEventListener("pointerdown", pointerDown, true);
    svg.addEventListener("pointermove", pointerMove, true);
    svg.addEventListener("pointerup", pointerUp, true);
    svg.addEventListener("pointercancel", pointerCancel, true);
    svg.addEventListener("click", click, true);
    scrubber.addEventListener("keydown", scrubberKeyDown);

    return {
        dispose() {
            svg.removeEventListener("pointerdown", pointerDown, true);
            svg.removeEventListener("pointermove", pointerMove, true);
            svg.removeEventListener("pointerup", pointerUp, true);
            svg.removeEventListener("pointercancel", pointerCancel, true);
            svg.removeEventListener("click", click, true);
            scrubber.removeEventListener("keydown", scrubberKeyDown);
        }
    };
}

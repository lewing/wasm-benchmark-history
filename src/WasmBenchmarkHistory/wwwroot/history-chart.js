const hoverOwners = new WeakMap();

export function initializeRangeDrag(svg, scrubber, dotNet, plotLeft, plotWidth) {
    const selection = svg.querySelector(".zoom-selection");
    const shell = svg.closest(".chart-shell");
    const plot = svg.closest(".plot-area");
    const scope = svg.closest(".chart-stack") ?? svg.ownerDocument;
    const document = svg.ownerDocument;
    let startX = null;
    let dragged = false;

    function dismissHover() {
        if (hoverOwners.get(scope) !== dismissHover) {
            return;
        }

        hoverOwners.delete(scope);
        // Hide immediately while Blazor processes the dismissal.
        shell.dataset.hoverDismissed = "true";
        hideCrosshair();
        void dotNet.invokeMethodAsync("DismissHover");
    }

    function claimHover() {
        if (hoverOwners.get(scope) !== dismissHover) {
            hoverOwners.get(scope)?.();
            hoverOwners.set(scope, dismissHover);
        }
        delete shell.dataset.hoverDismissed;
    }

    function outsidePointerDown(event) {
        if (!plot.contains(event.target)) {
            dismissHover();
        }
    }

    function focusIn(event) {
        if (!plot.contains(event.target)) {
            dismissHover();
        }
    }

    function keyDown(event) {
        if (event.key === "Escape") {
            dismissHover();
        }
    }

    function scrollGesture(event) {
        if (!plot.querySelector(".hover-card")?.contains(event.target)) {
            dismissHover();
        }
    }

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

    function siblingCrosshairs() {
        const stack = svg.closest(".chart-stack.compact");
        return stack
            ? [...stack.querySelectorAll("svg .sync-crosshair")].filter(line => line.ownerSVGElement !== svg)
            : [];
    }

    // Stacked rows share one X domain, so a viewBox X maps directly onto every sibling plot.
    function syncCrosshair(event) {
        const x = toPlotX(event);
        for (const line of siblingCrosshairs()) {
            line.setAttribute("x1", x);
            line.setAttribute("x2", x);
            line.classList.add("visible");
        }
    }

    function hideCrosshair() {
        for (const line of siblingCrosshairs()) {
            line.classList.remove("visible");
        }
    }

    function pointerMove(event) {
        claimHover();
        syncCrosshair(event);
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
        claimHover();
        await dotNet.invokeMethodAsync("PinAtRatio", ratio);
    }

    function scrubberKeyDown(event) {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            void dotNet.invokeMethodAsync("TogglePinnedSelection");
        }
    }

    svg.addEventListener("pointerenter", claimHover);
    svg.addEventListener("pointerdown", pointerDown, true);
    svg.addEventListener("pointermove", pointerMove, true);
    svg.addEventListener("pointerup", pointerUp, true);
    svg.addEventListener("pointercancel", pointerCancel, true);
    svg.addEventListener("click", click, true);
    svg.addEventListener("pointerleave", hideCrosshair);
    scrubber.addEventListener("focus", claimHover);
    scrubber.addEventListener("input", claimHover);
    scrubber.addEventListener("keydown", scrubberKeyDown);
    document.addEventListener("pointerdown", outsidePointerDown, true);
    document.addEventListener("focusin", focusIn);
    document.addEventListener("keydown", keyDown);
    document.addEventListener("wheel", scrollGesture, { capture: true, passive: true });
    document.addEventListener("touchmove", scrollGesture, { capture: true, passive: true });
    document.defaultView.addEventListener("blur", dismissHover);

    return {
        dispose() {
            if (hoverOwners.get(scope) === dismissHover) {
                hoverOwners.delete(scope);
            }
            svg.removeEventListener("pointerenter", claimHover);
            svg.removeEventListener("pointerdown", pointerDown, true);
            svg.removeEventListener("pointermove", pointerMove, true);
            svg.removeEventListener("pointerup", pointerUp, true);
            svg.removeEventListener("pointercancel", pointerCancel, true);
            svg.removeEventListener("click", click, true);
            svg.removeEventListener("pointerleave", hideCrosshair);
            hideCrosshair();
            scrubber.removeEventListener("focus", claimHover);
            scrubber.removeEventListener("input", claimHover);
            scrubber.removeEventListener("keydown", scrubberKeyDown);
            document.removeEventListener("pointerdown", outsidePointerDown, true);
            document.removeEventListener("focusin", focusIn);
            document.removeEventListener("keydown", keyDown);
            document.removeEventListener("wheel", scrollGesture, true);
            document.removeEventListener("touchmove", scrollGesture, true);
            document.defaultView.removeEventListener("blur", dismissHover);
        }
    };
}

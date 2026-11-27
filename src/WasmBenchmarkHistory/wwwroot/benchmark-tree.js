export function initializeTree(root) {
    let focusIndex = 0;
    let pendingFocus = null;
    const rows = () => [...root.querySelectorAll("[data-tree-index]")];
    const indexOf = row => Number(row.dataset.treeIndex);
    const isLeaf = row => row.classList.contains("tree-benchmark");
    const select = (row, modifiers) =>
        row.dispatchEvent(new MouseEvent("click", { bubbles: true, ...modifiers }));

    function reconcile() {
        const rendered = rows();
        const count = Number(root.dataset.itemCount);
        if (pendingFocus && count > 0) {
            pendingFocus.index = Math.min(pendingFocus.index, count - 1);
            const target = rendered.find(row => indexOf(row) === pendingFocus.index);
            if (target) {
                const request = pendingFocus;
                pendingFocus = null;
                focusIndex = indexOf(target);
                target.focus({ preventScroll: true });
                if (request.select && isLeaf(target)) select(target, request.modifiers);
            }
        }
        if (!pendingFocus && !rendered.some(row => indexOf(row) === focusIndex)) {
            focusIndex = rendered.length ? indexOf(rendered[0]) : 0;
        }
        root.tabIndex = rendered.length ? -1 : 0;
        for (const row of rendered) row.tabIndex = indexOf(row) === focusIndex ? 0 : -1;
    }

    function focusAt(index, shouldSelect = false, modifiers = {}) {
        pendingFocus = { index, select: shouldSelect, modifiers };
        if (!rows().some(row => indexOf(row) === index)) {
            root.scrollTop = Math.max(0, index * 39 - root.clientHeight / 2);
        }
        reconcile();
        if (!pendingFocus) {
            rows().find(row => indexOf(row) === focusIndex)?.scrollIntoView({ block: "nearest" });
        }
    }

    function onFocus(event) {
        const row = event.target.closest("[data-tree-index]");
        if (row) {
            focusIndex = indexOf(row);
            reconcile();
        }
    }

    function onClick(event) {
        const row = event.target.closest("[data-tree-index]");
        if (row) {
            focusIndex = indexOf(row);
            row.focus({ preventScroll: true });
            reconcile();
        }
    }

    function onKeyDown(event) {
        const row = event.target.closest("[data-tree-index]");
        if (!row) return;
        const count = Number(root.dataset.itemCount);
        const index = pendingFocus?.index ?? indexOf(row);
        const modifiers = { ctrlKey: event.ctrlKey, metaKey: event.metaKey, shiftKey: event.shiftKey };
        let target;
        let changeSelection = false;
        switch (event.key) {
            case "ArrowDown": target = Math.min(count - 1, index + 1); changeSelection = true; break;
            case "ArrowUp": target = Math.max(0, index - 1); changeSelection = true; break;
            case "Home": target = 0; changeSelection = true; break;
            case "End": target = count - 1; changeSelection = true; break;
            case "ArrowRight":
                if (!row.hasAttribute("aria-expanded")) return;
                if (row.getAttribute("aria-expanded") === "false") {
                    select(row, {});
                } else {
                    target = Math.min(count - 1, index + 1);
                }
                break;
            case "ArrowLeft":
                if (row.dataset.expandable && row.getAttribute("aria-expanded") === "true") {
                    select(row, {});
                } else if (Number(row.dataset.parentIndex) >= 0) {
                    target = Number(row.dataset.parentIndex);
                }
                break;
            case "Enter":
            case " ":
                select(row, event.target.matches("input") ? { ...modifiers, ctrlKey: true } : modifiers);
                break;
            default: return;
        }
        event.preventDefault();
        if (target !== undefined) {
            focusAt(target, changeSelection && (!event.ctrlKey && !event.metaKey || event.shiftKey), modifiers);
        }
    }

    const observer = new MutationObserver(reconcile);
    observer.observe(root, {
        childList: true,
        characterData: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["data-item-count"]
    });
    root.addEventListener("keydown", onKeyDown);
    root.addEventListener("focusin", onFocus);
    root.addEventListener("click", onClick);
    reconcile();
    return {
        dispose() {
            observer.disconnect();
            root.removeEventListener("keydown", onKeyDown);
            root.removeEventListener("focusin", onFocus);
            root.removeEventListener("click", onClick);
        }
    };
}

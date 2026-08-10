(() => {
    const storageKey = "competition:postback-position";

    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== "post" || form.dataset.resetPosition === "true") {
            return;
        }

        const active = document.activeElement;
        const postForms = Array.from(document.querySelectorAll('form[method="post"]'));
        const state = {
            path: window.location.pathname,
            scrollX: window.scrollX,
            scrollY: window.scrollY,
            formId: form.id || null,
            formIndex: postForms.indexOf(form),
            focusId: form.dataset.postbackFocusId || (active instanceof HTMLElement ? active.id || null : null),
            focusName: active instanceof HTMLElement ? active.getAttribute("name") : null,
            modalId: form.dataset.closeModalAfterPost === "true"
                ? null
                : active instanceof HTMLElement ? active.closest(".modal-backdrop")?.id ?? null : null
        };

        sessionStorage.setItem(storageKey, JSON.stringify(state));
    });

    const serializedState = sessionStorage.getItem(storageKey);
    if (!serializedState) {
        return;
    }

    sessionStorage.removeItem(storageKey);

    let state;
    try {
        state = JSON.parse(serializedState);
    } catch {
        return;
    }

    if (state.path !== window.location.pathname) {
        return;
    }

    requestAnimationFrame(() => requestAnimationFrame(() => {
        const modal = state.modalId ? document.getElementById(state.modalId) : null;
        if (modal) {
            modal.hidden = false;
        }

        const postForms = Array.from(document.querySelectorAll('form[method="post"]'));
        const form = (state.formId && document.getElementById(state.formId)) || postForms[state.formIndex] || null;
        let focusedControl = state.focusId ? document.getElementById(state.focusId) : null;
        if (!focusedControl && form && state.focusName) {
            focusedControl = Array.from(form.elements).find(control => control.name === state.focusName) ?? null;
        }

        focusedControl?.focus({ preventScroll: true });
        window.scrollTo(state.scrollX ?? 0, state.scrollY ?? 0);
    }));
})();

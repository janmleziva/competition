(() => {
    const dismissDelayMs = 5000;
    const fadeDurationMs = 300;
    const selector = ".status-message, .validation-panel:not(:empty), .error-message";

    const dismiss = element => {
        if (element.dataset.autoDismissScheduled === "true") {
            return;
        }

        element.dataset.autoDismissScheduled = "true";
        window.setTimeout(() => {
            element.classList.add("feedback-message-fading");
            window.setTimeout(() => {
                element.hidden = true;
            }, fadeDurationMs);
        }, dismissDelayMs);
    };

    document.addEventListener("DOMContentLoaded", () => {
        document.querySelectorAll(selector).forEach(dismiss);
    });
})();

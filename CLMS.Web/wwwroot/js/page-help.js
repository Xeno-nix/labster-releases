document.addEventListener("click", event => {
    const button = event.target.closest?.("[data-open-page-help], [data-close-page-help]");
    if (!button) return;

    const { openPageHelp, closePageHelp } = button.dataset;
    const dialog = document.getElementById(openPageHelp ?? closePageHelp);
    if (!(dialog instanceof HTMLDialogElement)) return;

    if (openPageHelp && !dialog.open) {
        button.focus();
        dialog.showModal();
    } else if (closePageHelp && dialog.open) {
        dialog.close();
    }
});

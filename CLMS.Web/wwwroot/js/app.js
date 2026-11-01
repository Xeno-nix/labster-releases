// Small app-wide helpers shared across pages (no libraries).

window.clms = window.clms || {};

// Smoothly bring an element to the top of the viewport. Used by the class-list
// pager after navigating to a new page.
window.clms.scrollToTop = function (elementId) {
    const el = document.getElementById(elementId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};
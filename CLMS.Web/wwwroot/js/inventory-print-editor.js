class InventoryPrintEditor extends HTMLElement {
    connectedCallback() {
        // Blazor may insert the custom element before inserting its children.
        queueMicrotask(() => {
            if (!this.isConnected || this.events) return;
            this.events = new AbortController();
            const options = { signal: this.events.signal };
            const edit = this.querySelector("[data-edit]");
            const footerToggle = this.querySelector("[data-footer-toggle]");
            const footer = this.querySelector("[data-editable-footer]");
            const defaultFooter = footer.querySelector("[data-default-footer]");
            const image = footer.querySelector("[data-custom-footer]");
            const picker = this.querySelector('input[type="file"]');
            const remove = this.querySelector("[data-remove-footer]");
            const status = this.querySelector('[role="status"]');

            edit.addEventListener("click", () => {
                const editing = edit.getAttribute("aria-pressed") !== "true";
                const fields = this.querySelectorAll("[data-print-field], .letterhead-table td, .masterlist-table :is(th, td)");
                for (const field of fields) {
                    field.classList.add("print-field");
                    field.contentEditable = editing ? "plaintext-only" : "false";
                }
                edit.setAttribute("aria-pressed", String(editing));
                edit.textContent = editing ? "Done editing" : (edit.dataset.edit || "Edit print list");
                footerToggle.hidden = !editing;
                remove.hidden = !editing || footer.hidden;
            }, options);

            this.querySelector("[data-print]")?.addEventListener("click", async () => {
                await Promise.all([...this.querySelectorAll("img[src]")].map(image => image.decode().catch(() => {})));
                if (this.isConnected) window.print();
            }, options);

            this.addEventListener("input", event => {
                const key = event.target.dataset?.repeatField;
                if (!key) return;
                for (const field of this.querySelectorAll("[data-repeat-field]")) {
                    if (field !== event.target && field.dataset.repeatField === key) {
                        field.textContent = event.target.textContent;
                    }
                }
            }, options);

            footerToggle.addEventListener("click", () => picker.click(), options);
            picker.addEventListener("change", async () => {
                const file = picker.files[0];
                if (!file) return;
                picker.value = "";
                const request = this.footerRequest = Symbol();
                if (!["image/png", "image/jpeg"].includes(file.type)) {
                    status.textContent = "Choose a PNG or JPG footer image.";
                    return;
                }
                const url = URL.createObjectURL(file);
                try {
                    const preview = new Image();
                    preview.src = url;
                    await preview.decode();
                    if (this.footerRequest !== request) return;
                    this.releaseFooter();
                    this.footerUrl = url;
                    defaultFooter.hidden = true;
                    image.src = url;
                    image.hidden = false;
                    footer.hidden = false;
                    remove.hidden = footerToggle.hidden;
                    status.textContent = "";
                } catch {
                    if (this.footerRequest === request) status.textContent = "This image could not be opened.";
                } finally {
                    if (this.footerUrl !== url) URL.revokeObjectURL(url);
                }
            }, options);

            remove.addEventListener("click", () => {
                this.footerRequest = null;
                image.hidden = true;
                image.removeAttribute("src");
                footer.hidden = true;
                this.releaseFooter();
                remove.hidden = true;
                status.textContent = "";
            }, options);
        });
    }

    releaseFooter() {
        if (this.footerUrl) URL.revokeObjectURL(this.footerUrl);
        this.footerUrl = null;
    }

    disconnectedCallback() {
        this.events?.abort();
        this.events = null;
        this.footerRequest = null;
        this.releaseFooter();
    }
}

customElements.define("inventory-print-editor", InventoryPrintEditor);

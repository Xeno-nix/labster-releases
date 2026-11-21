class InventoryPrintEditor extends HTMLElement {
    connectedCallback() {
        // Blazor may insert the custom element before inserting its children.
        queueMicrotask(() => {
            if (!this.isConnected || this.events) return;
            this.events = new AbortController();
            const options = { signal: this.events.signal };
            const edit = this.querySelector("[data-edit]");
            const footerToggle = this.querySelector("[data-footer-toggle]");
            const footer = this.querySelector(".print-footer");
            const image = footer.querySelector("img");
            const picker = this.querySelector('input[type="file"]');
            const remove = this.querySelector("[data-remove-footer]");
            const status = this.querySelector('[role="status"]');
            const printStyle = document.createElement("style");
            this.append(printStyle);
            this.printStyle = printStyle;

            edit.addEventListener("click", () => {
                const editing = edit.getAttribute("aria-pressed") !== "true";
                const fields = this.querySelectorAll(
                    ".univ-info-cell > div, .meta-table td, .doc-title-row span, .masterlist-table :is(th, td)"
                );
                for (const field of fields) {
                    field.classList.add("print-field");
                    field.contentEditable = editing ? "plaintext-only" : "false";
                }
                edit.setAttribute("aria-pressed", String(editing));
                edit.textContent = editing ? "Done editing" : "Edit print list";
                footerToggle.hidden = !editing;
                remove.hidden = !editing || footer.hidden;
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
                    // Generated images in page margins use their intrinsic dimensions.
                    const canvas = document.createElement("canvas");
                    canvas.width = 720;
                    canvas.height = 86;
                    const scale = Math.min(canvas.width / preview.naturalWidth, canvas.height / preview.naturalHeight);
                    const height = preview.naturalHeight * scale;
                    canvas.getContext("2d").drawImage(preview, 0, (canvas.height - height) / 2, preview.naturalWidth * scale, height);
                    const printImage = canvas.toDataURL("image/png");
                    this.releaseFooter();
                    this.footerUrl = url;
                    image.src = url;
                    printStyle.textContent = `@page inventory-with-footer { @bottom-left { content: url("${printImage}"); } }`;
                    footer.hidden = false;
                    remove.hidden = footerToggle.hidden;
                    footerToggle.textContent = "Replace footer";
                    status.textContent = "";
                } catch {
                    if (this.footerRequest === request) status.textContent = "This image could not be opened.";
                } finally {
                    if (this.footerUrl !== url) URL.revokeObjectURL(url);
                }
            }, options);

            remove.addEventListener("click", () => {
                this.footerRequest = null;
                printStyle.textContent = "";
                image.removeAttribute("src");
                this.releaseFooter();
                footer.hidden = remove.hidden = true;
                footerToggle.textContent = "Add footer";
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
        this.printStyle?.remove();
        this.releaseFooter();
    }
}

customElements.define("inventory-print-editor", InventoryPrintEditor);

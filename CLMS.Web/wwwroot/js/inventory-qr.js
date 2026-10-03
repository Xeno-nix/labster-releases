// Inventory QR helpers — powered by davidshimjs/qrcodejs
// https://davidshimjs.github.io/qrcodejs/
//
// Called from CLMS.Web/Components/Pages/Inventory/Inventory.razor.cs via IJSRuntime.

window.clmsInventoryQr = {
    /**
     * Renders a QR code into the element with the given id.
     * @param {string} elementId - id of the empty container div to render into.
     * @param {string} text - payload to encode (e.g. a URL to the equipment record).
     */
    render: function (elementId, text) {
        const el = document.getElementById(elementId);
        if (!el || typeof QRCode === "undefined") {
            return;
        }

        el.innerHTML = "";

        new QRCode(el, {
            text: text,
            width: 280,
            height: 280,
            colorDark: "#000000",
            colorLight: "#ffffff",
            correctLevel: QRCode.CorrectLevel.M
        });
    },

    /**
     * Downloads the QR code image previously rendered into elementId.
     * @param {string} elementId - id of the container the QR canvas lives in.
     * @param {string} filename - suggested download filename (e.g. "QR-EQ-001.png").
     */
    download: function (elementId, filename) {
        const container = document.getElementById(elementId);
        const canvas = container ? container.querySelector("canvas") : null;
        if (!canvas) {
            return;
        }

        const link = document.createElement("a");
        link.download = filename;
        link.href = canvas.toDataURL("image/png");
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    }
};
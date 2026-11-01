// Inventory camera QR scanner — powered by nimiq/qr-scanner
// https://github.com/nimiq/qr-scanner
//
// Called from CLMS.Web/Components/Pages/Inventory/Inventory.razor.cs via IJSRuntime.

window.clmsInventoryScanner = {
    _instance: null,
    _video: null,
    _unmirrorVideo: null,

    /**
     * Checks whether the device exposes any camera at all.
     * Does NOT prompt for permission — just enumerates devices.
     * @returns {Promise<boolean>}
     */
    hasCamera: async function () {
        if (typeof QrScanner === "undefined") {
            return false;
        }
        try {
            return await QrScanner.hasCamera();
        } catch {
            return false;
        }
    },

    /**
     * Starts the live camera scanner against the given <video> element.
     * Calls back into Blazor via dotNetRef.OnQrScanned(text) on every decode.
     * @param {string} videoElementId
     * @param {any} dotNetRef - a DotNetObjectReference from Blazor
     * @returns {Promise<{ok: boolean, error?: string}>}
     */
    start: async function (videoElementId, dotNetRef) {
        if (typeof QrScanner === "undefined") {
            return { ok: false, error: "Scanner library failed to load." };
        }

        const video = document.getElementById(videoElementId);
        if (!video) {
            return { ok: false, error: "Video element not found." };
        }

        this.stop();

        try {
            this._video = video;
            this._unmirrorVideo = () => {
                video.style.setProperty("transform", "none", "important");
                video.style.setProperty("-webkit-transform", "none", "important");
            };
            video.addEventListener("playing", this._unmirrorVideo);

            this._instance = new QrScanner(
                video,
                (result) => {
                    const text = result?.data ?? result;
                    dotNetRef.invokeMethodAsync("OnQrScanned", text);
                },
                {
                    highlightScanRegion: false,
                    highlightCodeOutline: true,
                    maxScansPerSecond: 5,
                    calculateScanRegion: (video) => ({
                        x: 0,
                        y: 0,
                        width: video.videoWidth,
                        height: video.videoHeight,
                        downScaledWidth: 800,
                        downScaledHeight: 800
                    })
                }
            );
            await this._instance.start();
            this._unmirrorVideo();
            return { ok: true };
        } catch (e) {
            console.error("QR scanner start failed:", e);
            this.stop();
            // Most commonly: permission denied, or camera in use by another app/tab.
            return { ok: false, error: "Could not access the camera. Check camera permissions." };
        }
    },

    /**
     * Stops and releases the camera stream.
     */
    stop: function () {
        if (this._video && this._unmirrorVideo) {
            this._video.removeEventListener("playing", this._unmirrorVideo);
        }

        if (this._instance) {
            try {
                this._instance.stop();
                this._instance.destroy();
            } catch {
                // ignore
            }
            this._instance = null;
        }

        this._video = null;
        this._unmirrorVideo = null;
    }
};
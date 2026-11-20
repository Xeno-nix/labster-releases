// Browser camera QR scanner, backed by the bundled qr-scanner library.
window.clmsInventoryScanner = {
    instance: null,
    video: null,

    async start(videoElementId, dotNetRef, scanId) {
        this.stop();
        const video = document.getElementById(videoElementId);
        if (typeof QrScanner === 'undefined' || !video) {
            return { ok: false, error: 'Camera scanning is unavailable. Enter the equipment tag below.' };
        }

        let scanner;
        try {
            this.video = video;
            scanner = new QrScanner(video, result => {
                if (this.instance !== scanner) return;
                // Detach and release the camera before reporting. Repeated frames cannot submit the same scan.
                this.stop();
                void dotNetRef.invokeMethodAsync('OnQrScanned', result?.data ?? result, scanId).catch(() => {});
            }, { preferredCamera: 'environment', highlightScanRegion: false, maxScansPerSecond: 5 });
            this.instance = scanner;
            await scanner.start();
            if (this.instance === scanner) video.style.setProperty('transform', 'none', 'important');
            return { ok: true };
        } catch (error) {
            if (this.video === video && (!scanner || this.instance === scanner)) this.stop();
            const unavailable = error?.name === 'NotFoundError' || /camera not found/i.test(String(error));
            return {
                ok: false,
                error: unavailable
                    ? 'No camera was found. Enter the equipment tag below.'
                    : 'Allow camera access in your browser and retry, or enter the equipment tag below.'
            };
        }
    },

    stop() {
        const scanner = this.instance;
        const video = this.video;
        this.instance = null;
        this.video = null;
        for (const track of video?.srcObject?.getTracks() ?? []) track.stop();
        if (video?.srcObject) video.srcObject = null;
        scanner?.destroy();
    }
};

window.addEventListener('pagehide', () => window.clmsInventoryScanner.stop());

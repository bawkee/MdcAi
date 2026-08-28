// Test-only webview harness. The real host is CoreWebView2, which exposes
// window.chrome.webview.{addEventListener,removeEventListener,postMessage}.
// We hand App a fake with the same surface so tests can feed it host messages
// and assert what the renderer posts back.

export class FakeWebView {
    constructor() {
        this.listeners = new Set();
        this.postMessage = jest.fn();
    }

    addEventListener(type, fn) {
        if (type === 'message')
            this.listeners.add(fn);
    }

    removeEventListener(type, fn) {
        this.listeners.delete(fn);
    }

    // Fires a host->JS message exactly like CoreWebView2: e.data is a JSON string
    // when the host called PostWebMessageAsJson. Pass a string or an object.
    emit(data) {
        const payload = typeof data === 'string' ? data : JSON.stringify(data);
        for (const fn of this.listeners)
            fn({ data: payload });
    }
}

export function installFakeWebView() {
    if (!window.chrome)
        window.chrome = {};
    const webView = new FakeWebView();
    window.chrome.webview = webView;
    return webView;
}

export function getWebView() {
    return window.chrome?.webview;
}

// Clears recorded postMessage calls but keeps the listener registry intact
// (clearing listeners would break the Ready handler already attached by App).
export function resetWebView() {
    getWebView()?.postMessage.mockClear();
}

// Convenience: feed a payload to the renderer's registered message handler.
export function emitWebMessage(payload) {
    getWebView()?.emit(payload);
}
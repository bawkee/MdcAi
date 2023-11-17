import { tryStringify } from './util';

export function log(data, level, clog = true) {
    if (clog) {
        logConsole(`${level}: ${tryStringify(data)}`);
    }

    if (!window.chrome.webview)
        return;

    const req = {
        Name: `Log${level}`,
        Data: data
    };

    window.chrome.webview.postMessage(req);
}

export function logConsole(data) {
    console.log(tryStringify(data));
}

export const logDebug = (data) => log(data, 'Debug');

export const logError = (error) => {
    const errorInfo = {
        Message: error.message,
        Stack: error.stack,
        Name: error.name,
    };

    log(errorInfo, 'Error');
}

export const logInfo = (data) => log(data, 'Info');

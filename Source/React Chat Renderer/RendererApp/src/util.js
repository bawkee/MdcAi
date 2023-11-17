export function isElementFullyVisible(element) {
    const elRect = element.getBoundingClientRect();

    return (
        elRect.top >= 0 &&
        elRect.bottom <= window.innerHeight
    );
}

export function tryStringify(data) {
    let output = data;
    try {
        if (typeof data === 'object' && data !== null)
            output = JSON.stringify(data, null, 2); // Pretty print        
    }
    finally {
        return output;
    }
}
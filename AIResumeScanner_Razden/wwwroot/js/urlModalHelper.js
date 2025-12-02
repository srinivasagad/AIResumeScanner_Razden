// urlModalHelper.js - JavaScript helper for URL Modal functionality

// Global function to open URL in modal from dynamically generated buttons
window.openUrlInModal = function (urlKey) {
    // This will be called by Blazor buttons
    // The actual modal opening is handled by Blazor component
    console.log('Opening URL with key:', urlKey);

    // Try to trigger Blazor method
    if (window.blazorUrlModalReference) {
        window.blazorUrlModalReference.invokeMethodAsync('OpenUrlInModal', urlKey);
    }
};

// Helper to initialize the Blazor reference
window.setBlazorUrlModalReference = function (dotnetHelper) {
    window.blazorUrlModalReference = dotnetHelper;
};

// Alternative: Direct URL opening without modal (fallback)
window.openUrlDirectly = function (url) {
    window.open(url, '_blank');
};

// Extract URLs from text content
window.extractUrls = function (text) {
    const urlRegex = /(https?:\/\/[^\s<>"]+|blob:https?:\/\/[^\s<>"]+)/g;
    const urls = text.match(urlRegex);
    return urls || [];
};

// Auto-detect and make URLs clickable
window.makeUrlsClickable = function (elementId) {
    const element = document.getElementById(elementId);
    if (!element) return;

    const urlRegex = /(https?:\/\/[^\s<>"]+|blob:https?:\/\/[^\s<>"]+)/g;
    element.innerHTML = element.innerHTML.replace(urlRegex, function (url) {
        const urlKey = 'url-' + Math.random().toString(36).substring(7);
        return `<button class="view-url-link" onclick="openUrlInModal('${urlKey}', '${url}')">🔗 View</button>`;
    });
};

// Copy text to clipboard
window.copyToClipboard = function (text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(text);
    } else {
        // Fallback for older browsers
        const textArea = document.createElement("textarea");
        textArea.value = text;
        textArea.style.position = "fixed";
        textArea.style.left = "-999999px";
        document.body.appendChild(textArea);
        textArea.select();
        try {
            document.execCommand('copy');
            document.body.removeChild(textArea);
            return Promise.resolve();
        } catch (err) {
            document.body.removeChild(textArea);
            return Promise.reject(err);
        }
    }
};

console.log('URL Modal Helper loaded successfully');
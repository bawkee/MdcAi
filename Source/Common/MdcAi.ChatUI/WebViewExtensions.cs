namespace MdcAi.ChatUI;

using Microsoft.UI.Xaml.Controls;

public static class WebViewExtensions
{
    public static readonly Dictionary<string, string> MimeTypes = new()
    {
        // Text
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".js", "application/javascript" },
        { ".css", "text/css" },
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".txt", "text/plain" },

        // Images
        { ".png", "image/png" },
        { ".ico", "image/x-icon" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif", "image/gif" },
        { ".bmp", "image/bmp" },
        { ".svg", "image/svg+xml" },
        { ".tiff", "image/tiff" },
        { ".webp", "image/webp" },

        // Video
        { ".mp4", "video/mp4" },
        { ".mpeg", "video/mpeg" },
        { ".avi", "video/x-msvideo" },
        { ".mov", "video/quicktime" },
        { ".wmv", "video/x-ms-wmv" },
        { ".flv", "video/x-flv" },
        { ".webm", "video/webm" },

        // Audio
        { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" },
        { ".ogg", "audio/ogg" },
        { ".m4a", "audio/x-m4a" },
        { ".flac", "audio/flac" },

        // Documents
        { ".pdf", "application/pdf" },
        { ".doc", "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { ".xls", "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".csv", "text/csv" },

        // Archives
        { ".zip", "application/zip" },
        { ".rar", "application/x-rar-compressed" },
        { ".7z", "application/x-7z-compressed" },
        { ".gz", "application/gzip" },
        { ".tar", "application/x-tar" },

        // Fonts
        { ".woff", "font/woff" },
        { ".woff2", "font/woff2" },
        { ".ttf", "font/ttf" },
        { ".otf", "font/otf" },

        // Other
        { ".eot", "application/vnd.ms-fontobject" },
        { ".sfnt", "font/sfnt" },
    };

    public static async Task<bool> IsScrolledToBottom(this WebView2 webView)
    {
        var r = await webView.ExecuteScriptAsync("(window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 5");
        return Convert.ToBoolean(r);
    }

    public static async Task ScrollToBottom(this WebView2 webView, bool smooth = false)
    {
        await webView.ExecuteScriptAsync($"window.scrollTo({{top: 100000, behavior: '{(smooth ? "smooth" : "instant")}'}});");
    }
}
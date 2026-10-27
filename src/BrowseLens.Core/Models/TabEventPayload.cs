namespace BrowseLens.Core.Models
{
    public class TabEventPayload
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public bool IsWindowFocused { get; set; }
        public bool IsIncognito { get; set; }

        public TabEventPayload()
        {
            Url = string.Empty;
            Title = string.Empty;
            IsWindowFocused = true;
            IsIncognito = false;
        }

        public TabEventPayload(string url, string title, bool isWindowFocused, bool isIncognito)
        {
            Url = url ?? string.Empty;
            Title = title ?? string.Empty;
            IsWindowFocused = isWindowFocused;
            IsIncognito = isIncognito;
        }
    }
}


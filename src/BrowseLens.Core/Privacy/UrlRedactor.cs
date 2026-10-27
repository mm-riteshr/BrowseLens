using System;

namespace BrowseLens.Core.Privacy
{
    public static class UrlRedactor
    {
        public static string ExtractDomain(string rawUrl)
        {
            if (string.IsNullOrEmpty(rawUrl)) return string.Empty;

            try
            {
                Uri uri;
                if (Uri.TryCreate(rawUrl, UriKind.Absolute, out uri))
                {
                    return uri.Host.ToLowerInvariant();
                }
            }
            catch
            {
                // Fall back to empty on unparseable
            }

            return string.Empty;
        }

        public static string RedactUrl(string rawUrl, bool removeQueryAndFragment)
        {
            if (string.IsNullOrEmpty(rawUrl)) return string.Empty;

            try
            {
                Uri uri;
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out uri))
                {
                    return string.Empty;
                }

                if (!removeQueryAndFragment)
                {
                    return uri.AbsoluteUri;
                }

                // Strip Query and Fragment
                string redacted = uri.GetLeftPart(UriPartial.Path);
                return redacted;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}


using System;
using System.Collections.Generic;

namespace BrowseLens.Core.Privacy
{
    public static class ExclusionPolicy
    {
        private static readonly string[] AllowedSchemes = new string[] { "http", "https" };

        public static bool IsRestrictedUrl(string rawUrl)
        {
            if (string.IsNullOrEmpty(rawUrl)) return true;

            try
            {
                Uri uri;
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out uri))
                {
                    return true;
                }

                string scheme = uri.Scheme.ToLowerInvariant();
                foreach (string allowed in AllowedSchemes)
                {
                    if (scheme == allowed) return false;
                }

                // Any non-http/https URL (chrome://, edge://, about:, file:, etc.) is restricted
                return true;
            }
            catch
            {
                return true;
            }
        }

        public static bool IsDomainExcluded(string rawUrl, IEnumerable<string> excludedPatterns)
        {
            if (string.IsNullOrEmpty(rawUrl) || excludedPatterns == null) return false;

            string domain = UrlRedactor.ExtractDomain(rawUrl);
            if (string.IsNullOrEmpty(domain)) return false;

            foreach (string pattern in excludedPatterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                string cleanPattern = pattern.Trim().ToLowerInvariant();

                if (domain.Equals(cleanPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (cleanPattern.StartsWith("*."))
                {
                    string suffix = cleanPattern.Substring(2);
                    if (domain.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase) || domain.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (domain.EndsWith("." + cleanPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}


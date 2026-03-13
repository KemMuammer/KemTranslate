using System;
using System.Collections.Generic;
using System.Linq;

namespace KemTranslate
{
    internal static class TargetLanguageHelper
    {
        public static bool TryGetDifferentTargetCode(IEnumerable<LtLanguage> languages, string effectiveSource, string currentTarget, out string newTarget)
        {
            newTarget = currentTarget;
            var list = languages?.ToList() ?? new List<LtLanguage>();

            if (!effectiveSource.Equals("en", StringComparison.OrdinalIgnoreCase) && list.Any(x => x.code.Equals("en", StringComparison.OrdinalIgnoreCase)))
            {
                newTarget = "en";
                return true;
            }

            string[] preferences = ["de", "es", "fr"];
            foreach (var preferred in preferences)
            {
                if (!effectiveSource.Equals(preferred, StringComparison.OrdinalIgnoreCase)
                    && list.Any(x => x.code.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
                {
                    newTarget = preferred;
                    return true;
                }
            }

            var fallback = list.FirstOrDefault(x => !x.code.Equals(effectiveSource, StringComparison.OrdinalIgnoreCase));
            if (fallback != null)
            {
                newTarget = fallback.code;
                return true;
            }

            return false;
        }
    }
}

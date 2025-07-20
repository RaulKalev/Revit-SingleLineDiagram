using System.Collections.Generic;
using System.Globalization;

namespace Schema.Helpers
{
    public class LevelNameComparer : IComparer<string>
    {
        private readonly CompareInfo _compareInfo = CultureInfo.CurrentCulture.CompareInfo;

        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Extract leading number (e.g., from "1. korrus", "-1 Kelder")
            var xMatch = System.Text.RegularExpressions.Regex.Match(x, @"-?\d+(\.\d+)?");
            var yMatch = System.Text.RegularExpressions.Regex.Match(y, @"-?\d+(\.\d+)?");

            if (xMatch.Success && yMatch.Success)
            {
                if (double.TryParse(xMatch.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double xVal) &&
                    double.TryParse(yMatch.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double yVal))
                {
                    return xVal.CompareTo(yVal);
                }
            }

            return _compareInfo.Compare(x, y, CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols);
        }
    }
}

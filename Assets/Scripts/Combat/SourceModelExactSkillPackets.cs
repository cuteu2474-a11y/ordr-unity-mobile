using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // One exact, recursive field gate for source-tooltip model packets. Missing
    // fields, added fields, reordered components and altered scalar types all
    // invalidate approval; it is not a native Warcraft parity certificate.
    internal static class SourceModelExactSkillPackets
    {
        internal static bool Matches(object actual, object expected)
        {
            if (actual == null || expected == null) return actual == expected;
            var leftObject = actual as IDictionary<string, object>;
            var rightObject = expected as IDictionary<string, object>;
            if (leftObject != null || rightObject != null)
            {
                if (leftObject == null || rightObject == null ||
                    leftObject.Count != rightObject.Count) return false;
                foreach (var field in rightObject)
                {
                    object value;
                    if (!leftObject.TryGetValue(field.Key, out value) ||
                        !Matches(value, field.Value)) return false;
                }
                return true;
            }
            var leftArray = actual as IList<object>;
            var rightArray = expected as IList<object>;
            if (leftArray != null || rightArray != null)
            {
                if (leftArray == null || rightArray == null ||
                    leftArray.Count != rightArray.Count) return false;
                for (int i = 0; i < leftArray.Count; i++)
                    if (!Matches(leftArray[i], rightArray[i])) return false;
                return true;
            }
            return Equals(actual, expected);
        }
    }
}

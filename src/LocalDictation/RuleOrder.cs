using System.Collections.Generic;
namespace LocalDictation {
    public static class RuleOrder {
        public static bool Move(List<ReplacementRule> rules, ReplacementRule source, ReplacementRule target, bool after) {
            int from=rules.IndexOf(source), to=rules.IndexOf(target);
            if(from<0||to<0||from==to)return false;
            if(after)to++; if(from<to)to--;
            rules.RemoveAt(from);rules.Insert(to,source);return true;
        }
    }
}

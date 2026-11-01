using System.Collections.Generic;
namespace LocalDictation {
    public static class RuleOrder {
        public static bool Move<T>(List<T> rules, T source, T target, bool after) where T : class {
            int from=rules.IndexOf(source), to=rules.IndexOf(target);
            if(from<0||to<0||from==to)return false;
            if(after)to++; if(from<to)to--;
            rules.RemoveAt(from);rules.Insert(to,source);return true;
        }
        // Slot for a dragged row moved by offset: a neighbour gives way once the row's leading edge passes its middle, so the first and last slots stay reachable.
        public static int Slot(IReadOnlyList<double> sizes,int from,double offset) {
            double top=0;for(int i=0;i<from;i++)top+=sizes[i];double upper=top+offset,lower=upper+sizes[from],y=0;int target=from;
            for(int i=0;i<sizes.Count;i++){double mid=y+sizes[i]/2;if(i<from&&upper<mid&&target==from)target=i;if(i>from&&lower>mid)target=i;y+=sizes[i];}
            return target;
        }
    }
}

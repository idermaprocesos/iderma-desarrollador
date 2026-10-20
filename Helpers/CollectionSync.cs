using System.Collections.ObjectModel;

namespace Consola.Helpers;

internal static class CollectionSync
{
    public static void MergeByPid<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, Func<T, int> pid)
        where T : class
    {
        var wanted = new Dictionary<int, T>(desired.Count);
        foreach (var item in desired)
            wanted[pid(item)] = item;

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.ContainsKey(pid(target[i])))
                target.RemoveAt(i);
        }

        var have = new HashSet<int>(target.Count);
        foreach (var item in target)
            have.Add(pid(item));

        foreach (var item in desired)
        {
            var id = pid(item);
            if (have.Add(id))
                target.Add(item);
        }
    }

    public static bool SamePidSet<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, int> pid)
    {
        if (a.Count != b.Count) return false;
        var keys = new HashSet<int>(a.Count);
        foreach (var item in a) keys.Add(pid(item));
        foreach (var item in b)
            if (!keys.Contains(pid(item))) return false;
        return true;
    }
}

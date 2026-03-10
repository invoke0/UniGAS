// 记录一帧内所有的内存分配，在一帧的最后统一回收
using System.Collections.Generic;
public interface IFramePooledObject {
    void OnEnterPool();
}

public static class FrameObjectPool<T> where T : class, IFramePooledObject, new() {
    public static T Claim() {
        return FrameObjectPoolSimple<T>.Claim();
    }

    public static void Release(T obj) {
        // obj will be set to null so we need to copy the reference
        var tmp = obj;

        FrameObjectPoolSimple<T>.Release(ref obj);
        tmp.OnEnterPool();
    }
}

public static class FrameObjectPoolSimple<T> where T : class, new() {
    /** Internal pool */
    static List<T> pool = new List<T>();

    public static T Claim() {
        lock (pool) {
            if (pool.Count > 0) {
                T ls = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                return ls;
            } else {
                return new T();
            }
        }
    }

    /** Releases an object.
     * After the object has been released it should not be used anymore.
     * The variable will be set to null to prevent silly mistakes.
     *
     * \throws System.InvalidOperationException
     * Releasing an object when it has already been released will cause an exception to be thrown.
     * However enabling ASTAR_OPTIMIZE_POOLING will prevent this check.
     *
     * \see Claim
     */
    public static void Release(ref T obj) {
        lock (pool) {
            pool.Add(obj);
        }
        obj = null;
    }

    /** Clears the pool for objects of this type.
     * This is an O(n) operation, where n is the number of pooled objects.
     */
    public static void Clear() {
        lock (pool) {
            pool.Clear();
        }
    }

    /** Number of objects of this type in the pool */
    public static int GetSize() {
        return pool.Count;
    }
}

public static class FrameListPool<T> {
    /** Internal pool */
    static readonly List<List<T>> pool = new List<List<T>>();

    static readonly List<List<T>> largePool = new List<List<T>>();

    // 这个变量没用了
    static readonly HashSet<List<T>> inPool = new HashSet<List<T>>();

    /** When requesting a list with a specified capacity, search max this many lists in the pool before giving up.
     * Must be greater or equal to one.
     */
    const int MaxCapacitySearchLength = 8;
    const int LargeThreshold = 5000;
    const int MaxLargePoolSize = 8;

    /** Claim a list.
     * Returns a pooled list if any are in the pool.
     * Otherwise it creates a new one.
     * After usage, this list should be released using the Release function (though not strictly necessary).
     */
    public static List<T> Claim() {
        lock (pool) {
            if (pool.Count > 0) {
                List<T> ls = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                inPool.Remove(ls);
                return ls;
            }

            return new List<T>();
        }
    }

    static int FindCandidate(List<List<T>> pool, int capacity) {
        // Loop through the last MaxCapacitySearchLength items
        // and check if any item has a capacity greater or equal to the one that
        // is desired. If so return it.
        // Otherwise take the largest one or if there are no lists in the pool
        // then allocate a new one with the desired capacity
        List<T> list = null;
        int listIndex = -1;
        for (int i = 0; i < pool.Count && i < MaxCapacitySearchLength; i++) {
            // ith last item
            var candidate = pool[pool.Count - 1 - i];

            // Find the largest list that is not too large (arbitrary decision to try to prevent some memory bloat if the list was not just a temporary list).
            if ((list == null || candidate.Capacity > list.Capacity) && candidate.Capacity < capacity * 16) {
                list = candidate;
                listIndex = pool.Count - 1 - i;

                if (list.Capacity >= capacity) {
                    return listIndex;
                }
            }
        }

        return listIndex;
    }

    public static List<T> Claim(int capacity) {
        lock (pool) {
            var currentPool = pool;
            var listIndex = FindCandidate(pool, capacity);

            if (capacity > LargeThreshold) {
                var largeListIndex = FindCandidate(largePool, capacity);
                if (largeListIndex != -1) {
                    currentPool = largePool;
                    listIndex = largeListIndex;
                }
            }

            if (listIndex == -1) {
                return new List<T>(capacity);
            } else {
                var list = currentPool[listIndex];
                // Swap current item and last item to enable a more efficient removal
                inPool.Remove(list);
                currentPool[listIndex] = currentPool[currentPool.Count - 1];
                currentPool.RemoveAt(currentPool.Count - 1);
                return list;
            }
        }
    }

    /** Makes sure the pool contains at least \a count pooled items with capacity \a size.
     * This is good if you want to do all allocations at start.
     */
    public static void Warmup(int count, int size) {
        lock (pool) {
            var tmp = new List<T>[count];
            for (int i = 0; i < count; i++) tmp[i] = Claim(size);
            for (int i = 0; i < count; i++) Release(tmp[i]);
        }
    }

    /** Releases a list.
     * After the list has been released it should not be used anymore.
     *
     * \throws System.InvalidOperationException
     * Releasing a list when it has already been released will cause an exception to be thrown.
     *
     * \see Claim
     */
    public static void Release(List<T> list) {
        // It turns out that the Clear method will clear all elements in the underlaying array
        // not just the ones up to Count. If the list only has a few elements, but the capacity
        // is huge, this can cause performance problems. Using the RemoveRange method to remove
        // all elements in the list does not have this problem, however it is implemented in a
        // stupid way, so it will clear the elements twice (completely unnecessarily) so it will
        // only be faster than using the Clear method if the number of elements in the list is
        // less than half of the capacity of the list.
        if (list.Count * 2 < list.Capacity) {
            list.RemoveRange(0, list.Count);
        } else {
            list.Clear();
        }

        lock (pool) {
            if (list.Capacity > LargeThreshold) {
                largePool.Add(list);

                // Remove the list which was used the longest time ago from the pool if it
                // exceeds the maximum size as it probably just contributes to memory bloat
                if (largePool.Count > MaxLargePoolSize) {
                    largePool.RemoveAt(0);
                }
            } else {
                pool.Add(list);
            }
        }
    }

    /** Clears the pool for lists of this type.
     * This is an O(n) operation, where n is the number of pooled lists.
     */
    public static void Clear() {
        lock (pool) {
            pool.Clear();
        }
    }

    /** Number of lists of this type in the pool */
    public static int GetSize() {
        // No lock required since int writes are atomic
        return pool.Count;
    }
}

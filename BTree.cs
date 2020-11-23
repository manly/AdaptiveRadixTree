//#define IMPLEMENT_DICTIONARY_INTERFACES // might want to disable due to System.Linq.Enumerable extensions clutter
 
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Specialized
{
    /// <summary>
    ///    Implements a B+Tree using roughly AvlTree&lt;TKey, SortedArray&lt;KeyValuePair&gt;&gt;.
    ///    Guarantees a fill_ratio of 66%+ on inserts, and 50%+ on deletes.
    /// </summary>
    /// <remarks>
    ///    Using an optimal self-balanced tree (for query times) since most queries will be purely lookups.
    ///    The nodes are sorted arrays.
    /// </remarks>
    public sealed class BTree<TKey, TValue> : ICollection 
#if IMPLEMENT_DICTIONARY_INTERFACES
        , IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
#endif
    {
        private static readonly int DEFAULT_ITEMS_PER_NODE = Math.Max(4096 / (IntPtr.Size * 2), 16); // assume TKey/TValue are classes
        private const int MIN_VALID_COUNT = 5;        // 5 because Rebalance2FullNodesInto3() needs the last node containing overflows to still have 1 remaining space for the potential new item
 
        private readonly AvlTree<TKey, Node> m_tree;  // BST where key=node.Items.First()
        private readonly IComparer<TKey> m_comparer;
        private readonly int m_itemsPerNode;          // recommended: Max(4096/(sizeof(TKey) + sizeof(TValue)), 16)
        private readonly int m_halfItemsPerNode;
        private readonly int m_twoThirdsItemsPerNode; // 66%
 
        public int Count { get; private set; }
 
        #region constructors
        /// <param name="items_per_node">Default: -1 = 4096/(IntPtr.Size*2). Recommended: Math.Max(4096/(sizeof(TKey) + sizeof(TValue)), 16)</param>
        public BTree(int items_per_node = -1) : this(Comparer<TKey>.Default, items_per_node) { }
        /// <param name="items_per_node">Default: -1 = 4096/(IntPtr.Size*2). Recommended: Math.Max(4096/(sizeof(TKey) + sizeof(TValue)), 16)</param>
        public BTree(IComparer<TKey> comparer, int items_per_node = -1) : base() { 
            m_comparer              = comparer ?? throw new ArgumentNullException(nameof(comparer));
            m_itemsPerNode          = items_per_node < 0 ? DEFAULT_ITEMS_PER_NODE : items_per_node;
            m_halfItemsPerNode      = m_itemsPerNode / 2;
            m_twoThirdsItemsPerNode = (int)Math.Floor((m_itemsPerNode / 3d) * 2d);
            m_tree                  = new AvlTree<TKey, Node>(comparer);
 
            if(m_itemsPerNode < MIN_VALID_COUNT)
                throw new ArgumentOutOfRangeException(nameof(items_per_node));
 
            this.Clear();
        }
        #endregion
 
        #region Keys
        /// <summary>
        ///     O(n)
        ///     Returns keys in order.
        /// </summary>
        public IEnumerable<TKey> Keys {
            get {
                foreach(var node in this.GetChildrenNodes())
                    yield return node.Key;
            }
        }
        #endregion
        #region Values
        /// <summary>
        ///     O(n)
        ///     Returns values in key order.
        /// </summary>
        public IEnumerable<TValue> Values {
            get {
                foreach(var node in this.GetChildrenNodes())
                    yield return node.Value;
            }
        }
        #endregion
        #region Items
        /// <summary>
        ///     O(n)
        ///     Returns items in key order.
        /// </summary>
        public IEnumerable<KeyValuePair> Items => this.GetChildrenNodes();
        #endregion
        #region this[]
        /// <summary>
        ///    O(log n)
        /// </summary>
        public TValue this[in TKey key] {
            get{
                if(!this.TryGetValue(key, out var value))
                    throw new KeyNotFoundException();
                return value;
            }
            set {
                var x = this.BinarySearch(key);
                if(!this.TryAdd(in x, key, value, out _))
                    // update
                    x.Items[x.Index] = new KeyValuePair(key, value);
            }
        }
        #endregion
 
        #region Minimum
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public KeyValuePair Minimum => m_tree.Minimum.Value.Items[0];
        #endregion
        #region Maximum
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public KeyValuePair Maximum {
            get {
                var x = m_tree.Maximum.Value;
                return x.Items[x.Count - 1];
            }
        }
        #endregion
     
        #region Add()
        /// <summary>
        ///     O(log n)
        ///     
        ///     Returns the insert location data.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public BinarySearchResult Add(in TKey key, in TValue value) {
            var x = this.BinarySearch(key);
            if(!this.TryAdd(in x, key, value, out var insertLocation))
                throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
            return insertLocation;
        }
        /// <summary>
        ///     O(1)
        ///     
        ///     Returns the insert location data.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public BinarySearchResult Add(in BinarySearchResult bsr, in TKey key, in TValue value) {
            if(!this.TryAdd(in bsr, key, value, out var insertLocation))
                throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
            return insertLocation;
        }
        #endregion
        #region AddRange()
        /// <summary>
        ///     O(m log n)
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public void AddRange(IEnumerable<KeyValuePair> values) {
            if(this.Count > 0) {
                foreach(var value in values)
                    this.Add(value.Key, value.Value);
            } else {
                // note: dont do this on all inserts
                var ordered = System.Linq.Enumerable.OrderBy(values, o => o.Key, m_comparer);
                this.AddRangeOrdered(ordered);
            }
        }
        /// <summary>
        ///     O(m log n)
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public void AddRange(IEnumerable<System.Collections.Generic.KeyValuePair<TKey, TValue>> values) {
            if(this.Count > 0) {
                foreach(var value in values)
                    this.Add(value.Key, value.Value);
            } else {
                // note: dont do this on all inserts
                var ordered = System.Linq.Enumerable.OrderBy(values, o => o.Key, m_comparer);
                this.AddRangeOrdered(ordered);
            }
        }
        #endregion
        #region AddRangeOrdered()
        /// <summary>
        ///     O(m log log n)
        ///     
        ///     Note that almost everytime you will not add faster if you sort your items and then add.
        ///     This is because the sorting time eats up any minimal optimisations done here, which assumes you know your data is ordered ahead of time.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        /// <param name="itemsOrderedByKey">Items must be ordered by the same IComparer used in the constructor.</param>
        public void AddRangeOrdered(IEnumerable<KeyValuePair> itemsOrderedByKey) {
            var enumerator = itemsOrderedByKey.GetEnumerator();
            if(!enumerator.MoveNext())
                return;
 
            // important implementation note:
            // since this is likely to be called when Count==0, you should know that the last node of the tree has no way to know it is the last node
            // this matters when adding a lot of items, because BinarySearchNearby is 2x slower when starting from the end while attempting to find
            // the following node
            // as a consequence, a lot of care has been put to avoid that specific case
 
            var current  = enumerator.Current;
            var x        = this.Add(current.Key, current.Value);
            var nextNode = x.Node?.Next();
             
            while(enumerator.MoveNext()) {
                current = enumerator.Current;
 
                // see if we can fit on current node
                if(x.Node != null) {
                    var node_count = x.Node.Value.Count;
                    if(node_count < m_itemsPerNode) {
                        bool insert_on_current_node;
                        if(nextNode != null) {
                            var last = nextNode.Value.Items[0];
                            insert_on_current_node = m_comparer.Compare(current.Key, last.Key) <= 0;
                        } else if((x.Index < 0 && ~x.Index >= node_count) || (x.Index >= 0 && x.Index >= node_count)) {
                            ReachedPastMaximumDumpEverything(x.Node);
                            return;
                        } else
                            insert_on_current_node = true;
                        if(insert_on_current_node) {
                            var index = x.Node.Value.BinarySearch(current.Key, x.Index >= 0 ? x.Index : ~x.Index, m_comparer.Compare);
                            if(index < 0) {
                                index = ~index;
                                KeyValuePair kvp = current;
                                x.Node.Value.InsertAt(index, in kvp);
                                if(index == 0)
                                    x.Node.UpdateKey(current.Key);
                                x = new BinarySearchResult(x.Node, index + 1); // make next one start after
                                nextNode = x.Node.Next();
                                this.Count++;
                                continue;
                            } else
                                throw new ArgumentException($"Duplicate key ({current.Key}).", nameof(itemsOrderedByKey));
                        }
                    }
                }

                // depending on data, this might be more efficient:  x = this.BinarySearch(current.Key);
                x        = this.BinarySearchNearby(x, current.Key);
                x        = this.Add(x, current.Key, current.Value);
                nextNode = x.Node?.Next();
            }
            /// <summary>
            ///     On we're inserting past the Maximum, then we just dump all the data.
            /// </summary>
            void ReachedPastMaximumDumpEverything(AvlTree<TKey, Node>.Node node) {
                var node_count = node.Value.Count;
                while(true) {
                    while(node_count < m_itemsPerNode) {
                        node.Value.Items[node_count] = enumerator.Current;
                        node.Value.Count = ++node_count;
                        this.Count++;
                        if(!enumerator.MoveNext())
                            return;
                    }
                    node_count = 0;
                    node       = m_tree.Add(enumerator.Current.Key, new Node(m_itemsPerNode));
                }
            }
        }
        /// <summary>
        ///     O(m log log n)
        ///     
        ///     Note that almost everytime you will not add faster if you sort your items and then add.
        ///     This is because the sorting time eats up any minimal optimisations done here, which assumes you know your data is ordered ahead of time.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        /// <param name="itemsOrderedByKey">Items must be ordered by the same IComparer used in the constructor.</param>
        public void AddRangeOrdered(IEnumerable<System.Collections.Generic.KeyValuePair<TKey, TValue>> itemsOrderedByKey) {
            var enumerator = itemsOrderedByKey.GetEnumerator();
            if(!enumerator.MoveNext())
                return;
 
            // important implementation note:
            // since this is likely to be called when Count==0, you should know that the last node of the tree has no way to know it is the last node
            // this matters when adding a lot of items, because BinarySearchNearby is 2x slower when starting from the end while attempting to find
            // the following node
            // as a consequence, a lot of care has been put to avoid that specific case
 
            var current  = enumerator.Current;
            var x        = this.Add(current.Key, current.Value);
            var nextNode = x.Node?.Next();
             
            while(enumerator.MoveNext()) {
                current = enumerator.Current;
 
                // see if we can fit on current node
                if(x.Node != null) {
                    var node_count = x.Node.Value.Count;
                    if(node_count < m_itemsPerNode) {
                        bool insert_on_current_node;
                        if(nextNode != null) {
                            var last = nextNode.Value.Items[0];
                            insert_on_current_node = m_comparer.Compare(current.Key, last.Key) <= 0;
                        } else if((x.Index < 0 && ~x.Index >= node_count) || (x.Index >= 0 && x.Index >= node_count)) {
                            ReachedPastMaximumDumpEverything(x.Node);
                            return;
                        } else
                            insert_on_current_node = true;
                        if(insert_on_current_node) {
                            var index = x.Node.Value.BinarySearch(current.Key, x.Index >= 0 ? x.Index : ~x.Index, m_comparer.Compare);
                            if(index < 0) {
                                index = ~index;
                                KeyValuePair kvp = current;
                                x.Node.Value.InsertAt(index, in kvp);
                                if(index == 0)
                                    x.Node.UpdateKey(current.Key);
                                x = new BinarySearchResult(x.Node, index + 1); // make next one start after
                                nextNode = x.Node.Next();
                                this.Count++;
                                continue;
                            } else
                                throw new ArgumentException($"Duplicate key ({current.Key}).", nameof(itemsOrderedByKey));
                        }
                    }
                }
                x        = this.BinarySearchNearby(x, current.Key);
                x        = this.Add(x, current.Key, current.Value);
                nextNode = x.Node?.Next();
            }
            /// <summary>
            ///     On we're inserting past the Maximum, then we just dump all the data.
            /// </summary>
            void ReachedPastMaximumDumpEverything(AvlTree<TKey, Node>.Node node) {
                var node_count = node.Value.Count;
                while(true) {
                    while(node_count < m_itemsPerNode) {
                        node.Value.Items[node_count] = enumerator.Current;
                        node.Value.Count = ++node_count;
                        this.Count++;
                        if(!enumerator.MoveNext())
                            return;
                    }
                    node_count = 0;
                    node       = m_tree.Add(enumerator.Current.Key, new Node(m_itemsPerNode));
                }
            }
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     O(log n)
        /// </summary>
        public bool Remove(in TKey key) {
            var x = this.BinarySearch(key);
 
            return this.Remove(x);
        }
        /// <summary>
        ///     O(1)
        /// </summary>
        public bool Remove(in BinarySearchResult bsr) {
            if(bsr.Index >= 0 && bsr.Node != null) {
                bsr.Node.Value.RemoveAt(bsr.Index);
                if(bsr.Index == 0)
                    bsr.Node.UpdateKey(bsr.Node.Value.Items[0].Key);
 
                var node_count = bsr.Node.Value.Count;
 
                if(node_count <= m_halfItemsPerNode && node_count > 0)
                   this.TryMoveAllItemsToAdjacentNodes(bsr.Node);
                else if(node_count == 0)
                    // this case should be rare
                    m_tree.Remove(bsr.Node);
                 
                this.Count--;
                return true;
            }
 
            return false;
        }
        #endregion
        #region RemoveRange()
        /// <summary>
        ///     O(m log n)
        /// </summary>
        public void RemoveRange(IEnumerable<TKey> keys) {
            foreach(var key in keys)
                this.Remove(key);

            // this isnt worth doing as the orderby eats more performance than it gives
            //var ordered = System.Linq.Enumerable.OrderBy(keys, o => o, m_comparer);
            //this.RemoveRangeOrdered(ordered);
        }
        #endregion
        #region RemoveRangeOrdered()
        // note: commented out because it gave worse performance than just foreach(){delete} both on ordered items and un-ordered.

        ///// <summary>
        /////     O(m log log n)
        ///// </summary>
        ///// <param name="orderedKeys">Keys must be ordered by the same IComparer used in the constructor.</param>
        //public void RemoveRangeOrdered(IEnumerable<TKey> orderedKeys) {
        //    var enumerator = orderedKeys.GetEnumerator();
        //    if(!enumerator.MoveNext())
        //        return;
        //
        //    var x = this.BinarySearch(enumerator.Current);
        //    if(x.Node == null) // if count==0
        //        return;
        //
        //    int index = x.Index;
        //    int node_count;
        //    if(index >= 0) {
        //        x.Node.Value.RemoveAt(index);
        //        if(index == 0)
        //            x.Node.UpdateKey(x.Node.Value.Items[0].Key);
        //        this.Count--;
        //    } else
        //        index = ~index;
        //
        //    while(enumerator.MoveNext()) {
        //        index = x.Node.Value.BinarySearch(enumerator.Current, index, m_comparer.Compare);
        //        if(index >= 0) {
        //            x.Node.Value.RemoveAt(index);
        //            if(index == 0)
        //                x.Node.UpdateKey(x.Node.Value.Items[0].Key);
        //            this.Count--;
        //        } else {
        //            index = ~index;
        //            node_count = x.Node.Value.Count;
        //            if(index >= node_count) {
        //                // item isnt within the current node
        //                if(node_count <= m_halfItemsPerNode && node_count > 0) {
        //                    var prev = this.TryMoveAllItemsToAdjacentNodes(x.Node);
        //                    if(prev != null)
        //                        x = new BinarySearchResult(prev, prev.Value.Count);
        //                } else if(node_count == 0) {
        //                    x = new BinarySearchResult(x.Node.Next(), 0);
        //                    m_tree.Remove(x.Node);
        //                }
        //
        //                // switch to next node, and remove
        //                x = this.BinarySearchNearby(x, enumerator.Current);
        //                if(x.Node == null) // if count==0
        //                    return;
        //                     
        //                index = x.Index;
        //                if(index >= 0) {
        //                    x.Node.Value.RemoveAt(index);
        //                    if(index == 0)
        //                        x.Node.UpdateKey(x.Node.Value.Items[0].Key);
        //                    this.Count--;
        //                } else
        //                    index = ~index;
        //            }
        //        }
        //    }
        //
        //    node_count = x.Node.Value.Count;
        //    if(node_count <= m_halfItemsPerNode && node_count > 0)
        //        this.TryMoveAllItemsToAdjacentNodes(x.Node);
        //    else if(node_count == 0)
        //        m_tree.Remove(x.Node);
        //
        //    // basic version with no per-node optimisation
        //    //var x = this.BinarySearch(enumerator.Current);
        //    //this.Remove(x);
        //    //
        //    //while(enumerator.MoveNext()) {
        //    //    x = this.BinarySearchNearby(x, enumerator.Current);
        //    //    this.Remove(x);
        //    //}
        //}
        #endregion
        #region Clear()
        /// <summary>
        ///     O(1)
        /// </summary>
        public void Clear() {
            m_tree.Clear();
            this.Count = 0;
        }
        #endregion

        #region TryGetValue()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool TryGetValue(in TKey key, out TValue value) {
            if(!this.TryGetItem(key, out var item)) {
                value = default;
                return false;
            }
            value = item.Value;
            return true;
        }
        #endregion
        #region TryGetItem()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool TryGetItem(in TKey key, out KeyValuePair item) {
            var x = this.BinarySearch(key);
            if(x.Index >= 0 && x.Node != null) {
                item = x.Item;
                return true;
            } else {
                item = default;
                return false;
            }
        }
        #endregion
        #region ContainsKey()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool ContainsKey(in TKey key) {
            //return this.TryGetItem(key, out _);
            var x = this.BinarySearch(key);
            return x.Index >= 0 && x.Node != null;
        }
        #endregion
        #region BinarySearch()
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearch(in TKey key) {
            return this.BinarySearch(key, m_comparer.Compare);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(in TKey key, IComparer<TKey> comparer) {
            return this.BinarySearch(key, comparer.Compare);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(in TKey key, Comparison<TKey> comparer) {
            var res = m_tree.BinarySearch(key, comparer);
 
            if(res.Diff > 0) {
                var node  = res.Node;
                var index = node.Value.BinarySearch(key, comparer);
                return new BinarySearchResult(node, index);
            } else if(res.Diff < 0) {
                var node = res.Node.Previous();
 
                if(node != null) {
                    var index = node.Value.BinarySearch(key, comparer);
                    return new BinarySearchResult(node, index);
                } else
                    // if node==null, it means we need to add as the minimum item
                    return new BinarySearchResult(res.Node, ~0);
            } else
                // this can either be an exact match, or "default" if count==0
                return new BinarySearchResult(res.Node, 0);
        }
        public readonly ref struct BinarySearchResult {
            internal readonly AvlTree<TKey, Node>.Node Node;
            public readonly int Index;
 
            public KeyValuePair Item => this.Node.Value.Items[this.Index]; // this.Node.Value.Items[this.Index >= 0 ? this.Index : ~this.Index]
            public KeyValuePair[] Items => this.Node?.Value.Items;
            public int NodeCount => this.Node.Value.Count;

            #region constructors
            internal BinarySearchResult(AvlTree<TKey, Node>.Node node, int index) : this() {
                this.Node  = node;
                this.Index = index;
            }
            #endregion

            #region Next()
            /// <summary>
            ///     O(1)
            ///     Returns the next item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult Next() {
                if(this.Node != null) {
                    if(this.Index < this.Node.Value.Count)
                        return new BinarySearchResult(this.Node, this.Index + 1);
                    return new BinarySearchResult(this.Node.Next(), 0);
                }
                return default;
            }
            #endregion
            #region Previous()
            /// <summary>
            ///     O(1)
            ///     Returns the previous item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult Previous() {
                if(this.Node != null) {
                    if(this.Index > 0)
                        return new BinarySearchResult(this.Node, this.Index - 1);
                    var node = this.Node.Previous();
                    if(node != null)
                        return new BinarySearchResult(node, node.Value.Count - 1);
                }
                return default;
            }
            #endregion
            #region Update()
            /// <summary>
            ///     O(1)
            ///     Change the key without updating the tree.
            ///     This is an "unsafe" operation; it can break the tree if you don't know what you're doing.
            ///     Safe to change if [key &gt; this.Previous() && key &lt; this.Next()].
            /// </summary>
            public void Update(in TKey key, in TValue value) {
                this.Node.Value.Items[this.Index] = new KeyValuePair(key, value);
                if(this.Index == 0)
                    this.Node.UpdateKey(key);
            }
            #endregion
            #region UpdateKey()
            /// <summary>
            ///     O(1)
            ///     Change the key without updating the tree.
            ///     This is an "unsafe" operation; it can break the tree if you don't know what you're doing.
            ///     Safe to change if [key &gt; this.Previous() && key &lt; this.Next()].
            /// </summary>
            public void UpdateKey(in TKey key) {
                var items = this.Node.Value.Items;
                var x     = items[this.Index];
                items[this.Index] = new KeyValuePair(key, x.Value);
                if(this.Index == 0)
                    this.Node.UpdateKey(key);
            }
            #endregion
            #region UpdateValue()
            /// <summary>
            ///     O(1)
            /// </summary>
            public void UpdateValue(in TValue value) {
                var items = this.Node.Value.Items;
                var x     = items[this.Index];
                items[this.Index] = new KeyValuePair(x.Key, value);
            }
            #endregion
            #region BitwiseNot()
            /// <summary>
            ///     O(1)
            /// </summary>
            public BinarySearchResult BitwiseNot() {
                return new BinarySearchResult(this.Node, ~this.Index);
            }
            #endregion
        }
        public readonly struct BinarySearchResult_Storeable {
            internal readonly AvlTree<TKey, Node>.Node Node;
            public readonly int Index;

            public KeyValuePair Item => this.Node.Value.Items[this.Index]; // this.Node.Value.Items[this.Index >= 0 ? this.Index : ~this.Index]
            public KeyValuePair[] Items => this.Node?.Value.Items;
            public int NodeCount => this.Node.Value.Count;

            #region constructors
            internal BinarySearchResult_Storeable(AvlTree<TKey, Node>.Node node, int index) : this() {
                this.Node  = node;
                this.Index = index;
            }
            public BinarySearchResult_Storeable(in BinarySearchResult bsr) : this(bsr.Node, bsr.Index) { }
            #endregion

            #region Next()
            /// <summary>
            ///     O(1)
            ///     Returns the next item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult_Storeable Next() {
                if(this.Node != null) {
                    if(this.Index < this.Node.Value.Count)
                        return new BinarySearchResult_Storeable(this.Node, this.Index + 1);
                    return new BinarySearchResult_Storeable(this.Node.Next(), 0);
                }
                return default;
            }
            #endregion
            #region Previous()
            /// <summary>
            ///     O(1)
            ///     Returns the previous item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult_Storeable Previous() {
                if(this.Node != null) {
                    if(this.Index > 0)
                        return new BinarySearchResult_Storeable(this.Node, this.Index - 1);
                    var node = this.Node.Previous();
                    if(node != null)
                        return new BinarySearchResult_Storeable(node, node.Value.Count - 1);
                }
                return default;
            }
            #endregion
            #region Update()
            /// <summary>
            ///     O(1)
            ///     Change the key without updating the tree.
            ///     This is an "unsafe" operation; it can break the tree if you don't know what you're doing.
            ///     Safe to change if [key &gt; this.Previous() && key &lt; this.Next()].
            /// </summary>
            public void Update(in TKey key, in TValue value) {
                this.Node.Value.Items[this.Index] = new KeyValuePair(key, value);
                if(this.Index == 0)
                    this.Node.UpdateKey(key);
            }
            #endregion
            #region UpdateKey()
            /// <summary>
            ///     O(1)
            ///     Change the key without updating the tree.
            ///     This is an "unsafe" operation; it can break the tree if you don't know what you're doing.
            ///     Safe to change if [key &gt; this.Previous() && key &lt; this.Next()].
            /// </summary>
            public void UpdateKey(in TKey key) {
                var items = this.Node.Value.Items;
                var x     = items[this.Index];
                items[this.Index] = new KeyValuePair(key, x.Value);
                if(this.Index == 0)
                    this.Node.UpdateKey(key);
            }
            #endregion
            #region UpdateValue()
            /// <summary>
            ///     O(1)
            /// </summary>
            public void UpdateValue(in TValue value) {
                var items = this.Node.Value.Items;
                var x     = items[this.Index];
                items[this.Index] = new KeyValuePair(x.Key, value);
            }
            #endregion
            #region BitwiseNot()
            /// <summary>
            ///     O(1)
            /// </summary>
            public BinarySearchResult_Storeable BitwiseNot() {
                return new BinarySearchResult_Storeable(this.Node, ~this.Index);
            }
            #endregion

            #region implicit casts
            public static implicit operator BinarySearchResult_Storeable(in BinarySearchResult value) {
                return new BinarySearchResult_Storeable(value);
            }
            public static implicit operator BinarySearchResult(in BinarySearchResult_Storeable value) {
                return new BinarySearchResult(value.Node, value.Index);
            }
            #endregion
        }
        #endregion
        #region BinarySearchNearby()
        /// <summary>
        ///    Worst: O(2 log n + log items_per_node)
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearchNearby(in BinarySearchResult start, in TKey key) {
            return this.BinarySearchNearby(start, key, m_comparer.Compare);
        }
        /// <summary>
        ///    Worst: O(2 log n + log items_per_node)
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearchNearby(in BinarySearchResult start, in TKey key, Comparison<TKey> comparer) {
            // note: maybe check [key is between start.Node.Value.Items[0].Key and start.Node.Value.Items[max].Key] to avoid tree binarysearchnearby() ?
             
            var res = m_tree.BinarySearchNearby(start.Node, key);
 
            if(res.Diff > 0) {
                var node  = res.Node;
                var index = node.Value.BinarySearch(key, comparer);
                return new BinarySearchResult(node, index);
            } else if(res.Diff < 0) {
                var node = res.Node.Previous();
 
                if(node != null) {
                    var index = node.Value.BinarySearch(key, comparer);
                    return new BinarySearchResult(node, index);
                } else
                    // if node==null, it means we need to add as the minimum item
                    return new BinarySearchResult(res.Node, ~0);
            } else
                // this can either be an exact match, or "default" if count==0
                return new BinarySearchResult(res.Node, 0);
        }
        #endregion
        #region Range()
        /// <summary>
        ///     O(n)
        ///     
        ///     Returns all nodes between the 2 keys.
        /// </summary>
        public IEnumerable<KeyValuePair> Range(TKey start, TKey end, bool include_start = true, bool include_end = true) {
            if(this.Count == 0)
                yield break;
 
            var search_start = this.BinarySearch(start);
            var search_end   = this.BinarySearch(end);
 
            var index_start = search_start.Index < 0 ? ~search_start.Index : search_start.Index + (include_start ? 0 : 1);
            var index_end   = search_end.Index < 0 ? ~search_end.Index : search_end.Index + (include_end ? 1 : 0);
 
            var search_end_node = search_end.Node;
            if(search_start.Node == search_end_node) {
                var items = search_start.Items;
                for(int i = index_start; i < index_end; i++)
                    yield return items[i];
                yield break;
            }
 
            var range = m_tree.Range(search_start.Items[0].Key, search_end.Items[0].Key).GetEnumerator();
            if(!range.MoveNext())
                yield break;
 
            int max                = search_start.Node.Value.Count;
            var search_start_items = search_start.Items;
            for(int i = index_start; i < max; i++)
                yield return search_start_items[i];
 
            while(range.MoveNext()) {
                var node  = range.Current;
                var items = node.Value.Items;
 
                max = node != search_end_node ? node.Value.Count : index_end;
                 
                for(int i = 0; i < max; i++)
                    yield return items[i];
            }
 
            //var node = search_start.Node;
            //while(node != search_end.Node) {
            //    node = node.Next();
            //}
        }
        #endregion
        #region internal static BTree<string, TValue>.StartsWith()
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        /// </summary>
        internal static IEnumerable<BTree<string, TValue>.KeyValuePair> StartsWith(BTree<string, TValue> tree, string key) {
            if(tree.Count == 0)
                yield break;
 
            var search_start = new BTree<string, TValue>.BinarySearchResult_Storeable(tree.BinarySearch(key));
            var index_start  = search_start.Index < 0 ? ~search_start.Index : search_start.Index;

            int max                = search_start.Node.Value.Count;
            var search_start_items = search_start.Items;
            for(int i = index_start; i < max; i++) {
                var item = search_start_items[i];
                if(item.Key.Length < key.Length || string.CompareOrdinal(item.Key, 0, key, 0, key.Length) != 0)
                    yield break;
                yield return item;
            }

            var node = search_start.Node;
            
            // if the node.Next() is too slow (ie: often requesting large ranges of results)
            // then consider using tree.m_tree.Range(key, tree.m_tree.MaxKey) instead
            
            while((node = node.Next()) != null) {
                max       = node.Value.Count;
                var items = node.Value.Items;

                for(int i = 0; i < max; i++) {
                    var item = items[i];
                    if(item.Key.Length < key.Length || string.CompareOrdinal(item.Key, 0, key, 0, key.Length) != 0)
                        yield break;
                    yield return item;
                }
            }
        }
        #endregion
        #region GetAppender()
        /// <summary>
        ///     Generates a builder that allows efficient appending of items at the end of the list.
        ///     This assumes the BTree is not being modified as calls are made to the appender/builder,
        ///     or at least, that the last node will not change while using the appender.
        /// </summary>
        public Appender GetAppender() {
            return new Appender(this);
        }
        public sealed class Appender {
            public readonly BTree<TKey, TValue> m_owner;
            private Node m_lastNode;
            private TKey m_maximum;
            private readonly Comparison<TKey> m_comparer;
            public Appender(BTree<TKey, TValue> owner) {
                m_owner    = owner;
                m_comparer = owner.m_comparer.Compare;
            }
            /// <summary>
            ///     O(1)
            ///     
            ///     Adds an item to the BTree at the end of it.
            ///     Items must be provided in the same sorting order as the BTree.
            ///     
            ///     Throws ArgumentException() if keys are &lt;= this.Maximum.
            ///     Throws ArgumentException() on duplicate key.
            /// </summary>
            /// <exception cref="ArgumentException" />
            public void AddOrdered(in TKey key, in TValue value) {
                var item = new KeyValuePair(key, value);

                if(m_lastNode != null)
                    this.Add(in item);
                else {
                    // first call to AddOrdered()
                    if(m_owner.Count == 0) {
                        var lastNode      = new Node(m_owner.m_itemsPerNode);
                        lastNode.Count    = 1;
                        lastNode.Items[0] = item;
                        m_owner.m_tree.Add(key, lastNode);
                        m_lastNode        = lastNode;
                        m_maximum         = key;
                        m_owner.Count++;
                    } else {
                        // read previous max
                        var lastNode = m_owner.m_tree.Maximum.Value;
                        m_lastNode   = lastNode;
                        m_maximum    = lastNode.Items[lastNode.Count - 1].Key;

                        this.Add(in item);
                    }
                }
            }
            private void Add(in KeyValuePair item) {
                var cmp = m_comparer(item.Key, m_maximum);
                if(cmp > 0) {
                    var lastNode = m_lastNode;
                    if(lastNode.Count == lastNode.Items.Length) {
                        lastNode   = new Node(m_owner.m_itemsPerNode);
                        m_owner.m_tree.Add(item.Key, lastNode);
                        m_lastNode = lastNode;
                    }
                    var count             = lastNode.Count;
                    lastNode.Items[count] = item;
                    lastNode.Count        = count + 1;
                    m_maximum             = item.Key;
                    m_owner.Count++;
                } else if(cmp < 0)
                    throw new ArgumentException($"{this.GetType().Name} can only add items that are > btree.Maximum (ie: {item.Key} > {m_maximum}).", nameof(item));
                else
                    throw new ArgumentException($"Duplicate key ({item.Key}).", nameof(item));
            }
        }
        #endregion
        #region Optimize()
        /// <summary>
        ///     O(n)
        ///     Fills the nodes to 100% capacity where possible.
        ///     This will break all iterators currently in use.
        /// </summary>
        public void Optimize() {
            // technically, could be <=2, but this ensures the balance is left-most regardless by rebalancing even on 2 nodes
            if(m_tree.Count <= 1)
                return;

            var totalItemCount = this.Count;

            int index = 0;
            var nodes = new Node[m_tree.Count];
            foreach(var node in m_tree.Values)
                nodes[index++] = node;
            m_tree.Clear();
            
            int writeIndex = 0;
            var writeNode  = nodes[writeIndex];

            for(int readIndex = 1; readIndex < nodes.Length; readIndex++) {
                var readNode      = nodes[readIndex];
                int readRemaining = readNode.Count;
                int readPos       = 0;

                while(readRemaining > 0) {
                    var copyCount = Math.Min(m_itemsPerNode - writeNode.Count, readRemaining);

                    if(copyCount > 0) {
                        // if the arrays+indices are the same, then it will do nothing anyway
                        Array.Copy(readNode.Items, readPos, writeNode.Items, writeNode.Count, copyCount);
                        writeNode.Count += copyCount;
                        readPos         += copyCount;
                        readRemaining   -= copyCount;
                    }

                    if(writeNode.Count == m_itemsPerNode) {
                        writeNode = nodes[++writeIndex];

                        // case example [100% fill first node, 80% fill second node]
                        // this makes sure that you basically do readIndex++
                        if(writeIndex == readIndex && readPos == 0)
                            break;
                    }
                }
            }

            // just to be safe and avoid leaks, we clear the arrays
            Array.Clear(writeNode.Items, writeNode.Count, m_itemsPerNode - writeNode.Count);
            // technically not needed
            //for(int i = writeIndex + 1; i < nodes.Length; i++) {
            //    var node = nodes[i];
            //    Array.Clear(node.Items, 0, node.Count);
            //    node.Count = 0;
            //}

            // dont rely on current writeIndex value, since it could be one higher than what we actually wrote to
            writeIndex = totalItemCount / m_itemsPerNode + (totalItemCount % m_itemsPerNode != 0 ? 1 : 0);

            // rebuild tree
            for(int i = 0; i < writeIndex; i++) {
                var node = nodes[i];
                // shouldnt be possible, but just in case
                if(node.Count <= 0)
                    continue;
                m_tree.Add(node.Items[0].Key, node);
            }
        }
        #endregion

        #region private TryAdd()
        /// <summary>
        ///     O(node.Items.Count / 2)     O(1) operation, but memcpy() of half the items to insert.
        ///     Returns false if key found.
        /// </summary>
        private bool TryAdd(in BinarySearchResult searchResult, in TKey key, in TValue value, out BinarySearchResult insertLocation) {
            var x = searchResult;

            if(x.Index >= 0 && x.Node != null) {
                insertLocation = default;
                return false;
            }
             
            var _new = new KeyValuePair(key, value);

            // add
            if(x.Node != null) {
                var node  = x.Node.Value;
                var index = ~x.Index;

                // if insert in middle of node
                if(index > 0 && index < m_itemsPerNode) {
                    // if the space exist
                    if(node.Count < m_itemsPerNode) {
                        // if the space exist
                        node.InsertAt(index, in _new);
                        insertLocation = new BinarySearchResult(x.Node, index);
                    } else if(this.TryAdd_TryMove1ItemToPreviousOrNextNode(x.Node, index, in _new)) {
                        insertLocation = new BinarySearchResult(x.Node, index);
                    } else
                        insertLocation = this.TryAdd_HandlePreviousCurrentAndNextFull(x, in _new);
                } else if(index == 0) {
                    // if inserting at the start of the node

                    if(node.Count < m_itemsPerNode) {
                        // if the space exist
                        node.InsertAt(0, in _new);
                        x.Node.UpdateKey(key);
                        insertLocation = new BinarySearchResult(x.Node, 0);
                    } else if(this.TryAdd_TryInsertOnPreviousNode(x.Node, in _new, out insertLocation)) {
                        // intentionally empty     //node.InsertAt(0, in _new);
                    } else
                        insertLocation = this.TryAdd_HandlePreviousAndCurrentFull(x, in _new);
                } else { // index == m_itemsPerNode
                    // if inserting at the end of the node
                    // note: current is full

                    if(this.TryAdd_TryInsertOnNextNode(x.Node, in _new, out insertLocation)) {
                        // intentionally empty     //node.InsertAt(0, in _new);
                    } else
                        insertLocation = this.TryAdd_HandleCurrentAndNextFull(x, in _new);
                }
            } else {
                var insert_node = this.TryAdd_HandleNoRoot(in _new);
                insertLocation = new BinarySearchResult(insert_node, 0);
            }

            this.Count++;
            return true;
        }
        private bool TryAdd_TryMove1ItemToPreviousOrNextNode(AvlTree<TKey, Node>.Node node, int index, in KeyValuePair _new) {
            var next = node.Next();
            if(next != null && next.Value.Count < m_itemsPerNode) {
                Move1Next(in _new);
                return true;
            }

            var prev = node.Previous();
            if(prev != null && prev.Value.Count < m_itemsPerNode) {
                Move1Prev(in _new);
                return true;
            }

            if(next == null) {
                next = m_tree.Add(node.Value.Items[node.Value.Count - 1].Key, new Node(m_itemsPerNode));
                Move1Next(in _new);
                return true;
            }

            if(prev == null) {
                // need the key to differ from the one were about to add
                node.UpdateKey(node.Value.Items[1].Key);
                prev = m_tree.Add(node.Value.Items[0].Key, new Node(m_itemsPerNode));
                Move1Prev(in _new);
                return true;
            }

            // prev and next are full
            return false;

            void Move1Prev(in KeyValuePair new_item) {
                prev.Value.InsertAt(prev.Value.Count, in node.Value.Items[0]);
                Array.Copy(node.Value.Items, 1, node.Value.Items, 0, index - 1); //node.Value.RemoveAt(0);
                node.Value.Items[index - 1] = new_item; //node.Value.InsertAt(index - 1, in _new);
                node.UpdateKey(node.Value.Items[0].Key);
            }
            void Move1Next(in KeyValuePair new_item) {
                var count = node.Value.Count;

                next.Value.InsertAt(0, in node.Value.Items[count - 1]);
                node.Value.RemoveAt(count - 1);
                next.UpdateKey(next.Value.Items[0].Key);
                node.Value.InsertAt(index, in new_item);
            }
        }
        private bool TryAdd_TryInsertOnPreviousNode(AvlTree<TKey, Node>.Node node, in KeyValuePair item, out BinarySearchResult insertLocation) {
            var prev = node.Previous();
             
            if(prev != null) {
                // if full
                if(prev.Value.Count == m_itemsPerNode) {
                    insertLocation = default;
                    return false;
                }
                prev.Value.InsertAt(prev.Value.Count, in item);
            } else {
                // note sure if should rebalance items across the 2 nodes
                // if you do 50%/50%, then on any delete it will just re-merge
                // keep in mind this case only applies on inserts <= to this.Minimum, so its preferable to make a new (empty-ish) node
                var new_node      = new Node(m_itemsPerNode);
                new_node.Items[0] = item;
                new_node.Count    = 1;
                m_tree.Add(item.Key, new_node);
            }
            insertLocation = new BinarySearchResult(node, 0);
            return true;
        }
        private bool TryAdd_TryInsertOnNextNode(AvlTree<TKey, Node>.Node node, in KeyValuePair item, out BinarySearchResult insertLocation) {
            var next = node.Next();
 
            if(next != null) {
                // if full
                if(next.Value.Count == m_itemsPerNode) {
                    insertLocation = default;
                    return false;
                }
                next.Value.InsertAt(0, in item);
                next.UpdateKey(item.Key);
                insertLocation = new BinarySearchResult(next, 0);
            } else {
                // note sure if should rebalance items across the 2 nodes
                // if you do 50%/50%, then on any delete it will just re-merge
                // keep in mind this case only applies on inserts >= to this.Maximum, so its preferable to make a new (empty-ish) node
                var new_node         = new Node(m_itemsPerNode);
                new_node.Items[0]    = item;
                new_node.Count       = 1;
                var insert_tree_node = m_tree.Add(item.Key, new_node);
                insertLocation = new BinarySearchResult(insert_tree_node, 0);
            }
            return true;
        }
        private AvlTree<TKey, Node>.Node TryAdd_HandleNoRoot(in KeyValuePair item) {
            var new_node      = new Node(m_itemsPerNode);
            new_node.Items[0] = item;
            new_node.Count    = 1;
            return m_tree.Add(item.Key, new_node);
        }
        private BinarySearchResult TryAdd_HandlePreviousCurrentAndNextFull(in BinarySearchResult bsr, in KeyValuePair item) {
            // we could split the 3x nodes into 4x nodes at 75%, 
            // instead we split 2 nodes into 3x nodes at 66%
            return this.TryAdd_HandlePreviousAndCurrentFull(bsr, in item);
        }
        private BinarySearchResult TryAdd_HandlePreviousAndCurrentFull(in BinarySearchResult bsr, in KeyValuePair item) {
            var index = ~bsr.Index;
            // note: we know current and prev() are full
            var nodes = this.TryAdd_Rebalance2FullNodesInto3(bsr.Node.Previous(), bsr.Node);
            // the insert index needs to be mapped unto its destination
            var index_within_current_node = m_itemsPerNode + index - m_twoThirdsItemsPerNode;
            var insert_within_current     = index_within_current_node <= m_twoThirdsItemsPerNode;
            AvlTree<TKey, Node>.Node insert_node;
            if(insert_within_current) {
                insert_node = nodes[1];
                index       = index_within_current_node;
            } else {
                insert_node = nodes[2];
                index       = index_within_current_node - m_twoThirdsItemsPerNode;
            }
                                 
            insert_node.Value.InsertAt(index, in item);
            if(index == 0)
                insert_node.UpdateKey(item.Key);
            return new BinarySearchResult(insert_node, index);
        }
        private BinarySearchResult TryAdd_HandleCurrentAndNextFull(in BinarySearchResult bsr, in KeyValuePair item) {
            var index = ~bsr.Index;
            // note: we know current and next() are full
            var nodes = this.TryAdd_Rebalance2FullNodesInto3(bsr.Node, bsr.Node.Next());
            // the insert index needs to be mapped unto its destination
            var index_within_current_node = index - m_twoThirdsItemsPerNode;
            var insert_within_current     = index_within_current_node <= m_twoThirdsItemsPerNode;
            AvlTree<TKey, Node>.Node insert_node;
            if(insert_within_current) {
                insert_node = nodes[1];
                index       = index_within_current_node;
            } else {
                insert_node = nodes[2];
                index       = index_within_current_node - m_twoThirdsItemsPerNode;
            }
                                 
            insert_node.Value.InsertAt(index, in item);
            if(index == 0)
                insert_node.UpdateKey(item.Key);
            return new BinarySearchResult(insert_node, index);
        }
        /// <summary>
        ///     Rebalance 2 nodes at 100% capacity into 3 nodes all at 66% capacity.
        ///     Returns {first,second,new}.
        /// </summary>
        private AvlTree<TKey, Node>.Node[] TryAdd_Rebalance2FullNodesInto3(AvlTree<TKey, Node>.Node first, AvlTree<TKey, Node>.Node second) {
            // important note: rounding overflow MUST be sent to last node, since caller assumes that
            // ie: result must be {[m_twoThirdsItemsPerNode], [m_twoThirdsItemsPerNode], [m_twoThirdsItemsPerNode + remainder]}

            var new_node = new Node(m_itemsPerNode) {
                Count = (m_itemsPerNode * 2) - (m_twoThirdsItemsPerNode * 2)
            };
            // copy node "overflow"
            Array.Copy(second.Value.Items, m_itemsPerNode - new_node.Count, new_node.Items, 0, new_node.Count);
            // move up the values in node to accept first "overflow"
            Array.Copy(second.Value.Items, 0, second.Value.Items, m_itemsPerNode - m_twoThirdsItemsPerNode, m_twoThirdsItemsPerNode - (m_itemsPerNode - m_twoThirdsItemsPerNode));
            // move up first into node
            Array.Copy(first.Value.Items, m_twoThirdsItemsPerNode, second.Value.Items, 0, m_itemsPerNode - m_twoThirdsItemsPerNode);
            Array.Clear(first.Value.Items, m_twoThirdsItemsPerNode, m_itemsPerNode - m_twoThirdsItemsPerNode);
            Array.Clear(second.Value.Items, m_twoThirdsItemsPerNode, m_itemsPerNode - m_twoThirdsItemsPerNode);
            first.Value.Count  = m_twoThirdsItemsPerNode;
            second.Value.Count = m_twoThirdsItemsPerNode;
            var _new = m_tree.Add(new_node.Items[0].Key, new_node);
            second.UpdateKey(second.Value.Items[0].Key);
            return new[] { first, second, _new };
        }
        #endregion
        #region private TryMoveAllItemsToAdjacentNodes()
        /// <summary>
        ///     Tries to redistribute the node items to Previous()/Next() nodes if they fit, 
        ///     then delete the node itself.
        ///     Returns either the prev node if removed, or null;
        /// </summary>
        private AvlTree<TKey, Node>.Node TryMoveAllItemsToAdjacentNodes(AvlTree<TKey, Node>.Node node) {
            int nodes_to_move   = node.Value.Count;
            var prev            = node.Previous();
            var prev_space_left = prev != null ? m_itemsPerNode - prev.Value.Count : 0;
            var space_avail     = prev_space_left;
 
            // if all the current node items can fit in the prev node, then do so
            if(space_avail >= nodes_to_move) {
                Array.Copy(node.Value.Items, 0, prev.Value.Items, prev.Value.Count, nodes_to_move);
                prev.Value.Count += nodes_to_move;
                //Array.Clear(node.Value.Items, 0, node.Value.Count); // do this ?
                m_tree.Remove(node);
                return prev;
            }
 
            var next            = node.Next();
            var next_space_left = next != null ? m_itemsPerNode - next.Value.Count : 0;
            space_avail        += next_space_left;
 
            // if all the current node items can fit in the prev+next node, then do so
            if(space_avail >= nodes_to_move) {
                if(prev != null) {
                    Array.Copy(node.Value.Items, 0, prev.Value.Items, prev.Value.Count, prev_space_left);
                    prev.Value.Count = m_itemsPerNode;
                }
                Array.Copy(next.Value.Items, 0, next.Value.Items, nodes_to_move - prev_space_left, next.Value.Count);
                Array.Copy(node.Value.Items, prev_space_left, next.Value.Items, 0, nodes_to_move - prev_space_left);
                next.Value.Count += nodes_to_move - prev_space_left;
                //Array.Clear(node.Value.Items, 0, node.Value.Count); // do this ?
                m_tree.Remove(node);
                // this is important; it signals the first key of the next() node changed
                // this is kind of a hack since we know we dont need to tree.remove()/tree.add() the key in order to update it, 
                // because its relative position has not changed and would not affect the tree
                next.UpdateKey(next.Value.Items[0].Key);
                return prev;
            }
            return null;
        }
        #endregion
 
        #region private GetChildrenNodes()
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IEnumerable<KeyValuePair> GetChildrenNodes() {
            foreach(var item in m_tree.Items) {
                int max   = item.Value.Count;
                var items = item.Value.Items;
 
                for(int i = 0; i < max; i++)
                    yield return items[i];
            }
        }
        #endregion
 
        #region explicit interface(s) implementations
        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetChildrenNodes().GetEnumerator();
        }
 
        object ICollection.SyncRoot => this;
        bool ICollection.IsSynchronized => false;
 
        void ICollection.CopyTo(Array array, int arrayIndex) {
            if(array == null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
 
            foreach(var node in this.GetChildrenNodes())
                array.SetValue(node, arrayIndex++);
        }
 
#if IMPLEMENT_DICTIONARY_INTERFACES
        /// <summary>
        ///     O(n)
        ///     Returns keys in order.
        /// </summary>
        ICollection<TKey> IDictionary<TKey, TValue>.Keys {
            get {
                var keys = new List<TKey>(this.Count);
                foreach(var node in this.GetChildrenNodes())
                    keys.Add(node.Key);
 
                return keys;
            }
        }
 
        /// <summary>
        ///     O(n)
        ///     Returns values in key order.
        /// </summary>
        ICollection<TValue> IDictionary<TKey, TValue>.Values {
            get {
                var values = new List<TValue>(this.Count);
                foreach(var node in this.GetChildrenNodes())
                    values.Add(node.Value);
 
                return values;
            }
        }
 
        /// <summary>
        ///     O(log n)
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        void ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.Add(in System.Collections.Generic.KeyValuePair<TKey, TValue> item) {
            this.Add(item.Key, item.Value);
        }
 
        /// <summary>
        ///     O(log n)
        /// </summary>
        bool ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.Remove(in System.Collections.Generic.KeyValuePair<TKey, TValue> item) {
            return this.Remove(item.Key);
        }
         
        IEnumerator<System.Collections.Generic.KeyValuePair<TKey, TValue>> IEnumerable<System.Collections.Generic.KeyValuePair<TKey, TValue>>.GetEnumerator() {
            foreach(var node in this.GetChildrenNodes())
                yield return new System.Collections.Generic.KeyValuePair<TKey, TValue>(node.Key, node.Value);
        }
 
        bool ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.IsReadOnly => false;
 
        bool ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.Contains(in System.Collections.Generic.KeyValuePair<TKey, TValue> item) {
            return this.TryGetValue(item.Key, out TValue value) && object.Equals(item.Value, value);
        }
 
        void ICollection<System.Collections.Generic.KeyValuePair<TKey, TValue>>.CopyTo(System.Collections.Generic.KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
            if(array == null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
 
            foreach(var item in this.GetChildrenNodes())
                array[arrayIndex++] = item;
        }
#endif
        #endregion
 
        internal sealed class Node {
            public readonly KeyValuePair[] Items;
            public int Count;
 
            #region constructors
            public Node(int count) {
                this.Items = new KeyValuePair[count];
            }
            #endregion
 
            #region BinarySearch()
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int BinarySearch(in TKey key, Comparison<TKey> comparer) {
                int min = 0;
                int max = this.Count - 1;
             
                while(min <= max) {
                    int median = (min + max) >> 1;
                    var diff   = comparer(this.Items[median].Key, key);
                     
                    if(diff < 0)
                        min = median + 1;
                    else if(diff > 0)
                        max = median - 1;
                    else
                        return median;
                }
                 
                return ~min;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int BinarySearch(in TKey key, int min, Comparison<TKey> comparer) {
                int max = this.Count - 1;
             
                while(min <= max) {
                    int median = (min + max) >> 1;
                    var diff   = comparer(this.Items[median].Key, key);
                     
                    if(diff < 0)
                        min = median + 1;
                    else if(diff > 0)
                        max = median - 1;
                    else
                        return median;
                }
                 
                return ~min;
            }
            #endregion
            #region InsertAt()
            public void InsertAt(int index, in KeyValuePair item) {
                var count  = this.Count;
                this.Count = count + 1;
                Array.Copy(this.Items, index, this.Items, index + 1, count - index);
                this.Items[index] = item;
            }
            #endregion
            #region RemoveAt()
            public void RemoveAt(int index) {
                var count  = this.Count - 1;
                this.Count = count;
                Array.Copy(this.Items, index + 1, this.Items, index, count - index);
                this.Items[count] = default;
            }
            #endregion
        }
        /// <summary>
        ///     Same as System.Collections.Generic.KeyValuePair&lt;&gt; but forced readonly
        ///     (for better compiler optimisations).
        /// </summary>
        public readonly struct KeyValuePair {
            public readonly TKey Key;
            public readonly TValue Value;
            #region constructors
            public KeyValuePair(in TKey key, in TValue value) {
                this.Key   = key;
                this.Value = value;
            }
            #endregion
            #region implicit casts
            public static implicit operator System.Collections.Generic.KeyValuePair<TKey, TValue>(in KeyValuePair value) {
                return new System.Collections.Generic.KeyValuePair<TKey, TValue>(value.Key, value.Value);
            }
            public static implicit operator KeyValuePair(in System.Collections.Generic.KeyValuePair<TKey, TValue> value) {
                return new KeyValuePair(value.Key, value.Value);
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return $"[{this.Key}] {this.Value}";
            }
            #endregion
        }
    }


    /// <summary>
    ///    Implements a B+Tree using roughly AvlTree&lt;TKey, SortedArray&lt;Key&gt;&gt;.
    ///    Guarantees a fill_ratio of 66%+ on inserts, and 50%+ on deletes.
    /// </summary>
    /// <remarks>
    ///    Using an optimal self-balanced tree (for query times) since most queries will be purely lookups.
    ///    The nodes are sorted arrays.
    /// </remarks>
    public sealed class BTree<TKey> : ICollection {
        private static readonly int DEFAULT_ITEMS_PER_NODE = Math.Max(4096 / IntPtr.Size, 16); // assume TKey is a class
        private const int MIN_VALID_COUNT = 5;        // 5 because Rebalance2FullNodesInto3() needs the last node containing overflows to still have 1 remaining space for the potential new item
 
        private readonly AvlTree<TKey, Node> m_tree;  // BST where key=node.Items.First()
        private readonly IComparer<TKey> m_comparer;
        private readonly int m_itemsPerNode;          // recommended: Max(4096/sizeof(TKey), 16)
        private readonly int m_halfItemsPerNode;
        private readonly int m_twoThirdsItemsPerNode; // 66%
 
        public int Count { get; private set; }
 
        #region constructors
        /// <param name="items_per_node">Default: -1 = 4096/IntPtr.Size. Recommended: Math.Max(4096/sizeof(TKey), 16)</param>
        public BTree(int items_per_node = -1) : this(Comparer<TKey>.Default, items_per_node) { }
        /// <param name="items_per_node">Default: -1 = 4096/IntPtr.Size. Recommended: Math.Max(4096/sizeof(TKey), 16)</param>
        public BTree(IComparer<TKey> comparer, int items_per_node = -1) : base() { 
            m_comparer              = comparer ?? throw new ArgumentNullException(nameof(comparer));
            m_itemsPerNode          = items_per_node < 0 ? DEFAULT_ITEMS_PER_NODE : items_per_node;
            m_halfItemsPerNode      = m_itemsPerNode / 2;
            m_twoThirdsItemsPerNode = (int)Math.Floor((m_itemsPerNode / 3d) * 2d);
            m_tree                  = new AvlTree<TKey, Node>(comparer);
 
            if(m_itemsPerNode < MIN_VALID_COUNT)
                throw new ArgumentOutOfRangeException(nameof(items_per_node));
 
            this.Clear();
        }
        #endregion
 
        #region Keys
        /// <summary>
        ///     O(n)
        ///     Returns keys in order.
        /// </summary>
        public IEnumerable<TKey> Keys => this.GetChildrenNodes();
        #endregion
 
        #region Minimum
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public TKey Minimum => m_tree.Minimum.Value.Items[0];
        #endregion
        #region Maximum
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public TKey Maximum {
            get {
                var x = m_tree.Maximum.Value;
                return x.Items[x.Count - 1];
            }
        }
        #endregion
     
        #region Add()
        /// <summary>
        ///     O(log n)
        ///     
        ///     Returns the insert location data.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public BinarySearchResult Add(in TKey key) {
            var x = this.BinarySearch(key);
            if(!this.TryAdd(in x, key, out var insertLocation))
                throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
            return insertLocation;
        }
        /// <summary>
        ///     O(1)
        ///     
        ///     Returns the insert location data.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public BinarySearchResult Add(in BinarySearchResult bsr, in TKey key) {
            if(!this.TryAdd(in bsr, key, out var insertLocation))
                throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
            return insertLocation;
        }
        #endregion
        #region AddRange()
        /// <summary>
        ///     O(m log n)
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public void AddRange(IEnumerable<TKey> keys) {
            if(this.Count > 0) {
                foreach(var key in keys)
                    this.Add(key);
            } else {
                // note: dont do this on all inserts
                var ordered = System.Linq.Enumerable.OrderBy(keys, o => o, m_comparer);
                this.AddRangeOrdered(ordered);
            }
        }
        #endregion
        #region AddRangeOrdered()
        /// <summary>
        ///     O(m log log n)
        ///     
        ///     Note that almost everytime you will not add faster if you sort your items and then add.
        ///     This is because the sorting time eats up any minimal optimisations done here, which assumes you know your data is ordered ahead of time.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        /// <param name="orderedKeys">Items must be ordered by the same IComparer used in the constructor.</param>
        public void AddRangeOrdered(IEnumerable<TKey> orderedKeys) {
            var enumerator = orderedKeys.GetEnumerator();
            if(!enumerator.MoveNext())
                return;
 
            // important implementation note:
            // since this is likely to be called when Count==0, you should know that the last node of the tree has no way to know it is the last node
            // this matters when adding a lot of items, because BinarySearchNearby is 2x slower when starting from the end while attempting to find
            // the following node
            // as a consequence, a lot of care has been put to avoid that specific case
 
            var current  = enumerator.Current;
            var x        = this.Add(current);
            var nextNode = x.Node?.Next();
             
            while(enumerator.MoveNext()) {
                current = enumerator.Current;
 
                // see if we can fit on current node
                if(x.Node != null) {
                    var node_count = x.Node.Value.Count;
                    if(node_count < m_itemsPerNode) {
                        bool insert_on_current_node;
                        if(nextNode != null) {
                            var last = nextNode.Value.Items[0];
                            insert_on_current_node = m_comparer.Compare(current, last) <= 0;
                        } else if((x.Index < 0 && ~x.Index >= node_count) || (x.Index >= 0 && x.Index >= node_count)) {
                            ReachedPastMaximumDumpEverything(x.Node);
                            return;
                        } else
                            insert_on_current_node = true;
                        if(insert_on_current_node) {
                            var index = x.Node.Value.BinarySearch(current, x.Index >= 0 ? x.Index : ~x.Index, m_comparer.Compare);
                            if(index < 0) {
                                index = ~index;
                                x.Node.Value.InsertAt(index, current);
                                if(index == 0)
                                    x.Node.UpdateKey(current);
                                x = new BinarySearchResult(x.Node, index + 1); // make next one start after
                                nextNode = x.Node.Next();
                                this.Count++;
                                continue;
                            } else
                                throw new ArgumentException($"Duplicate key ({current}).", nameof(orderedKeys));
                        }
                    }
                }

                // depending on data, this might be more efficient:  x = this.BinarySearch(current);
                x        = this.BinarySearchNearby(x, current);
                x        = this.Add(x, current);
                nextNode = x.Node?.Next();
            }
            /// <summary>
            ///     On we're inserting past the Maximum, then we just dump all the data.
            /// </summary>
            void ReachedPastMaximumDumpEverything(AvlTree<TKey, Node>.Node node) {
                var node_count = node.Value.Count;
                while(true) {
                    while(node_count < m_itemsPerNode) {
                        node.Value.Items[node_count] = enumerator.Current;
                        node.Value.Count = ++node_count;
                        this.Count++;
                        if(!enumerator.MoveNext())
                            return;
                    }
                    node_count = 0;
                    node       = m_tree.Add(enumerator.Current, new Node(m_itemsPerNode));
                }
            }
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     O(log n)
        /// </summary>
        public bool Remove(in TKey key) {
            var x = this.BinarySearch(key);
 
            return this.Remove(x);
        }
        /// <summary>
        ///     O(1)
        /// </summary>
        public bool Remove(in BinarySearchResult bsr) {
            if(bsr.Index >= 0 && bsr.Node != null) {
                bsr.Node.Value.RemoveAt(bsr.Index);
                if(bsr.Index == 0)
                    bsr.Node.UpdateKey(bsr.Node.Value.Items[0]);
 
                var node_count = bsr.Node.Value.Count;
 
                if(node_count <= m_halfItemsPerNode && node_count > 0)
                   this.TryMoveAllItemsToAdjacentNodes(bsr.Node);
                else if(node_count == 0)
                    // this case should be rare
                    m_tree.Remove(bsr.Node);
                 
                this.Count--;
                return true;
            }
 
            return false;
        }
        #endregion
        #region RemoveRange()
        /// <summary>
        ///     O(m log n)
        /// </summary>
        public void RemoveRange(IEnumerable<TKey> keys) {
            foreach(var key in keys)
                this.Remove(key);

            // this isnt worth doing as the orderby eats more performance than it gives
            //var ordered = System.Linq.Enumerable.OrderBy(keys, o => o, m_comparer);
            //this.RemoveRangeOrdered(ordered);
        }
        #endregion
        #region RemoveRangeOrdered()
        // note: commented out because it gave worse performance than just foreach(){delete} both on ordered items and un-ordered.

        ///// <summary>
        /////     O(m log log n)
        ///// </summary>
        ///// <param name="orderedKeys">Keys must be ordered by the same IComparer used in the constructor.</param>
        //public void RemoveRangeOrdered(IEnumerable<TKey> orderedKeys) {
        //    var enumerator = orderedKeys.GetEnumerator();
        //    if(!enumerator.MoveNext())
        //        return;
        //
        //    var x = this.BinarySearch(enumerator.Current);
        //    if(x.Node == null) // if count==0
        //        return;
        //
        //    int index = x.Index;
        //    int node_count;
        //    if(index >= 0) {
        //        x.Node.Value.RemoveAt(index);
        //        if(index == 0)
        //            x.Node.UpdateKey(x.Node.Value.Items[0]);
        //        this.Count--;
        //    } else
        //        index = ~index;
        //
        //    while(enumerator.MoveNext()) {
        //        index = x.Node.Value.BinarySearch(enumerator.Current, index, m_comparer.Compare);
        //        if(index >= 0) {
        //            x.Node.Value.RemoveAt(index);
        //            if(index == 0)
        //                x.Node.UpdateKey(x.Node.Value.Items[0]);
        //            this.Count--;
        //        } else {
        //            index = ~index;
        //            node_count = x.Node.Value.Count;
        //            if(index >= node_count) {
        //                // item isnt within the current node
        //                if(node_count <= m_halfItemsPerNode && node_count > 0) {
        //                    var prev = this.TryMoveAllItemsToAdjacentNodes(x.Node);
        //                    if(prev != null)
        //                        x = new BinarySearchResult(prev, prev.Value.Count);
        //                } else if(node_count == 0) {
        //                    x = new BinarySearchResult(x.Node.Next(), 0);
        //                    m_tree.Remove(x.Node);
        //                }
        //
        //                // switch to next node, and remove
        //                x = this.BinarySearchNearby(x, enumerator.Current);
        //                if(x.Node == null) // if count==0
        //                    return;
        //                     
        //                index = x.Index;
        //                if(index >= 0) {
        //                    x.Node.Value.RemoveAt(index);
        //                    if(index == 0)
        //                        x.Node.UpdateKey(x.Node.Value.Items[0]);
        //                    this.Count--;
        //                } else
        //                    index = ~index;
        //            }
        //        }
        //    }
        //
        //    node_count = x.Node.Value.Count;
        //    if(node_count <= m_halfItemsPerNode && node_count > 0)
        //        this.TryMoveAllItemsToAdjacentNodes(x.Node);
        //    else if(node_count == 0)
        //        m_tree.Remove(x.Node);
        //
        //    // basic version with no per-node optimisation
        //    //var x = this.BinarySearch(enumerator.Current);
        //    //this.Remove(x);
        //    //
        //    //while(enumerator.MoveNext()) {
        //    //    x = this.BinarySearchNearby(x, enumerator.Current);
        //    //    this.Remove(x);
        //    //}
        //}
        #endregion
        #region Clear()
        /// <summary>
        ///     O(1)
        /// </summary>
        public void Clear() {
            m_tree.Clear();
            this.Count = 0;
        }
        #endregion
 
        #region ContainsKey()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool ContainsKey(in TKey key) {
            var x = this.BinarySearch(key);
            return x.Index >= 0 && x.Node != null;
        }
        #endregion
        #region BinarySearch()
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearch(in TKey key) {
            return this.BinarySearch(key, m_comparer.Compare);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(in TKey key, IComparer<TKey> comparer) {
            return this.BinarySearch(key, comparer.Compare);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(in TKey key, Comparison<TKey> comparer) {
            var res = m_tree.BinarySearch(key, comparer);
 
            if(res.Diff > 0) {
                var node  = res.Node;
                var index = node.Value.BinarySearch(key, comparer);
                return new BinarySearchResult(node, index);
            } else if(res.Diff < 0) {
                var node = res.Node.Previous();
 
                if(node != null) {
                    var index = node.Value.BinarySearch(key, comparer);
                    return new BinarySearchResult(node, index);
                } else
                    // if node==null, it means we need to add as the minimum item
                    return new BinarySearchResult(res.Node, ~0);
            } else
                // this can either be an exact match, or "default" if count==0
                return new BinarySearchResult(res.Node, 0);
        }
        public readonly ref struct BinarySearchResult {
            internal readonly AvlTree<TKey, Node>.Node Node;
            public readonly int Index;
 
            public TKey Item => this.Node.Value.Items[this.Index]; // this.Node.Value.Items[this.Index >= 0 ? this.Index : ~this.Index]
            public TKey[] Items => this.Node?.Value.Items;
            public int NodeCount => this.Node.Value.Count;

            #region constructors
            internal BinarySearchResult(AvlTree<TKey, Node>.Node node, int index) : this() {
                this.Node  = node;
                this.Index = index;
            }
            #endregion

            #region Next()
            /// <summary>
            ///     O(1)
            ///     Returns the next item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult Next() {
                if(this.Node != null) {
                    if(this.Index < this.Node.Value.Count)
                        return new BinarySearchResult(this.Node, this.Index + 1);
                    return new BinarySearchResult(this.Node.Next(), 0);
                }
                return default;
            }
            #endregion
            #region Previous()
            /// <summary>
            ///     O(1)
            ///     Returns the previous item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult Previous() {
                if(this.Node != null) {
                    if(this.Index > 0)
                        return new BinarySearchResult(this.Node, this.Index - 1);
                    var node = this.Node.Previous();
                    if(node != null)
                        return new BinarySearchResult(node, node.Value.Count - 1);
                }
                return default;
            }
            #endregion
            #region UpdateKey()
            /// <summary>
            ///     O(1)
            ///     Change the key without updating the tree.
            ///     This is an "unsafe" operation; it can break the tree if you don't know what you're doing.
            ///     Safe to change if [key &gt; this.Previous() && key &lt; this.Next()].
            /// </summary>
            public void UpdateKey(in TKey key) {
                this.Node.Value.Items[this.Index] = key;
                if(this.Index == 0)
                    this.Node.UpdateKey(key);
            }
            #endregion
            #region BitwiseNot()
            /// <summary>
            ///     O(1)
            /// </summary>
            public BinarySearchResult BitwiseNot() {
                return new BinarySearchResult(this.Node, ~this.Index);
            }
            #endregion
        }
        public readonly struct BinarySearchResult_Storeable {
            internal readonly AvlTree<TKey, Node>.Node Node;
            public readonly int Index;

            public TKey Item => this.Node.Value.Items[this.Index]; // this.Node.Value.Items[this.Index >= 0 ? this.Index : ~this.Index]
            public TKey[] Items => this.Node?.Value.Items;
            public int NodeCount => this.Node.Value.Count;

            #region constructors
            internal BinarySearchResult_Storeable(AvlTree<TKey, Node>.Node node, int index) : this() {
                this.Node  = node;
                this.Index = index;
            }
            public BinarySearchResult_Storeable(in BinarySearchResult bsr) : this(bsr.Node, bsr.Index) { }
            #endregion

            #region Next()
            /// <summary>
            ///     O(1)
            ///     Returns the next item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult_Storeable Next() {
                if(this.Node != null) {
                    if(this.Index < this.Node.Value.Count)
                        return new BinarySearchResult_Storeable(this.Node, this.Index + 1);
                    return new BinarySearchResult_Storeable(this.Node.Next(), 0);
                }
                return default;
            }
            #endregion
            #region Previous()
            /// <summary>
            ///     O(1)
            ///     Returns the previous item.
            ///     Returns {null, 0} when done.
            /// </summary>
            public BinarySearchResult_Storeable Previous() {
                if(this.Node != null) {
                    if(this.Index > 0)
                        return new BinarySearchResult_Storeable(this.Node, this.Index - 1);
                    var node = this.Node.Previous();
                    if(node != null)
                        return new BinarySearchResult_Storeable(node, node.Value.Count - 1);
                }
                return default;
            }
            #endregion
            #region UpdateKey()
            /// <summary>
            ///     O(1)
            ///     Change the key without updating the tree.
            ///     This is an "unsafe" operation; it can break the tree if you don't know what you're doing.
            ///     Safe to change if [key &gt; this.Previous() && key &lt; this.Next()].
            /// </summary>
            public void UpdateKey(in TKey key) {
                this.Node.Value.Items[this.Index] = key;
                if(this.Index == 0)
                    this.Node.UpdateKey(key);
            }
            #endregion
            #region BitwiseNot()
            /// <summary>
            ///     O(1)
            /// </summary>
            public BinarySearchResult_Storeable BitwiseNot() {
                return new BinarySearchResult_Storeable(this.Node, ~this.Index);
            }
            #endregion

            #region implicit casts
            public static implicit operator BinarySearchResult_Storeable(in BinarySearchResult value) {
                return new BinarySearchResult_Storeable(value);
            }
            public static implicit operator BinarySearchResult(in BinarySearchResult_Storeable value) {
                return new BinarySearchResult(value.Node, value.Index);
            }
            #endregion
        }
        #endregion
        #region BinarySearchNearby()
        /// <summary>
        ///    Worst: O(2 log n + log items_per_node)
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearchNearby(in BinarySearchResult start, in TKey key) {
            return this.BinarySearchNearby(start, key, m_comparer.Compare);
        }
        /// <summary>
        ///    Worst: O(2 log n + log items_per_node)
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearchNearby(in BinarySearchResult start, in TKey key, Comparison<TKey> comparer) {
            // note: maybe check [key is between start.Node.Value.Items[0].Key and start.Node.Value.Items[max].Key] to avoid tree binarysearchnearby() ?
             
            var res = m_tree.BinarySearchNearby(start.Node, key);
 
            if(res.Diff > 0) {
                var node  = res.Node;
                var index = node.Value.BinarySearch(key, comparer);
                return new BinarySearchResult(node, index);
            } else if(res.Diff < 0) {
                var node = res.Node.Previous();
 
                if(node != null) {
                    var index = node.Value.BinarySearch(key, comparer);
                    return new BinarySearchResult(node, index);
                } else
                    // if node==null, it means we need to add as the minimum item
                    return new BinarySearchResult(res.Node, ~0);
            } else
                // this can either be an exact match, or "default" if count==0
                return new BinarySearchResult(res.Node, 0);
        }
        #endregion
        #region Range()
        /// <summary>
        ///     O(n)
        ///     
        ///     Returns all nodes between the 2 keys.
        /// </summary>
        public IEnumerable<TKey> Range(TKey start, TKey end, bool include_start = true, bool include_end = true) {
            if(this.Count == 0)
                yield break;
 
            var search_start = this.BinarySearch(start);
            var search_end   = this.BinarySearch(end);
 
            var index_start = search_start.Index < 0 ? ~search_start.Index : search_start.Index + (include_start ? 0 : 1);
            var index_end   = search_end.Index < 0 ? ~search_end.Index : search_end.Index + (include_end ? 1 : 0);
 
            var search_end_node = search_end.Node;
            if(search_start.Node == search_end_node) {
                var items = search_start.Items;
                for(int i = index_start; i < index_end; i++)
                    yield return items[i];
                yield break;
            }
 
            var range = m_tree.Range(search_start.Items[0], search_end.Items[0]).GetEnumerator();
            if(!range.MoveNext())
                yield break;
 
            int max                = search_start.Node.Value.Count;
            var search_start_items = search_start.Items;
            for(int i = index_start; i < max; i++)
                yield return search_start_items[i];
 
            while(range.MoveNext()) {
                var node  = range.Current;
                var items = node.Value.Items;
 
                max = node != search_end_node ? node.Value.Count : index_end;
                 
                for(int i = 0; i < max; i++)
                    yield return items[i];
            }
 
            //var node = search_start.Node;
            //while(node != search_end.Node) {
            //    node = node.Next();
            //}
        }
        #endregion
        #region internal static BTree<string>.StartsWith()
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        /// </summary>
        internal static IEnumerable<string> StartsWith(BTree<string> tree, string key) {
            if(tree.Count == 0)
                yield break;
 
            var search_start = new BTree<string>.BinarySearchResult_Storeable(tree.BinarySearch(key));
            var index_start  = search_start.Index < 0 ? ~search_start.Index : search_start.Index;

            int max                = search_start.Node.Value.Count;
            var search_start_items = search_start.Items;
            for(int i = index_start; i < max; i++) {
                var item = search_start_items[i];
                if(item.Length < key.Length || string.CompareOrdinal(item, 0, key, 0, key.Length) != 0)
                    yield break;
                yield return item;
            }

            var node = search_start.Node;

            // if the node.Next() is too slow (ie: often requesting large ranges of results)
            // then consider using tree.m_tree.Range(key, tree.m_tree.MaxKey) instead
            
            while((node = node.Next()) != null) {
                max       = node.Value.Count;
                var items = node.Value.Items;

                for(int i = 0; i < max; i++) {
                    var item = items[i];
                    if(item.Length < key.Length || string.CompareOrdinal(item, 0, key, 0, key.Length) != 0)
                        yield break;
                    yield return item;
                }
            }
        }
        #endregion
        #region GetAppender()
        /// <summary>
        ///     Generates a builder that allows efficient appending of items at the end of the list.
        ///     This assumes the BTree is not being modified as calls are made to the appender/builder,
        ///     or at least, that the last node will not change while using the appender.
        /// </summary>
        public Appender GetAppender() {
            return new Appender(this);
        }
        public sealed class Appender {
            public readonly BTree<TKey> m_owner;
            private Node m_lastNode;
            private TKey m_maximum;
            private readonly Comparison<TKey> m_comparer;
            public Appender(BTree<TKey> owner) {
                m_owner    = owner;
                m_comparer = owner.m_comparer.Compare;
            }
            /// <summary>
            ///     O(1)
            ///     
            ///     Adds an item to the BTree at the end of it.
            ///     Items must be provided in the same sorting order as the BTree.
            ///     
            ///     Throws ArgumentException() if keys are &lt;= this.Maximum.
            ///     Throws ArgumentException() on duplicate key.
            /// </summary>
            /// <exception cref="ArgumentException" />
            public void AddOrdered(in TKey key) {
                if(m_lastNode != null)
                    this.Add(key);
                else {
                    // first call to AddOrdered()
                    if(m_owner.Count == 0) {
                        var lastNode      = new Node(m_owner.m_itemsPerNode);
                        lastNode.Count    = 1;
                        lastNode.Items[0] = key;
                        m_owner.m_tree.Add(key, lastNode);
                        m_lastNode        = lastNode;
                        m_maximum         = key;
                        m_owner.Count++;
                    } else {
                        // read previous max
                        var lastNode = m_owner.m_tree.Maximum.Value;
                        m_lastNode   = lastNode;
                        m_maximum    = lastNode.Items[lastNode.Count - 1];

                        this.Add(key);
                    }
                }
            }
            private void Add(in TKey key) {
                var cmp = m_comparer(key, m_maximum);
                if(cmp > 0) {
                    var lastNode = m_lastNode;
                    if(lastNode.Count == lastNode.Items.Length) {
                        lastNode   = new Node(m_owner.m_itemsPerNode);
                        m_owner.m_tree.Add(key, lastNode);
                        m_lastNode = lastNode;
                    }
                    var count             = lastNode.Count;
                    lastNode.Items[count] = key;
                    lastNode.Count        = count + 1;
                    m_maximum             = key;
                    m_owner.Count++;
                } else if(cmp < 0)
                    throw new ArgumentException($"{this.GetType().Name} can only add items that are > btree.Maximum (ie: {key} > {m_maximum}).", nameof(key));
                else
                    throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
            }
        }
        #endregion
        #region Optimize()
        /// <summary>
        ///     O(n)
        ///     Fills the nodes to 100% capacity where possible.
        ///     This will break all iterators currently in use.
        /// </summary>
        public void Optimize() {
            // technically, could be <=2, but this ensures the balance is left-most regardless by rebalancing even on 2 nodes
            if(m_tree.Count <= 1)
                return;

            var totalItemCount = this.Count;

            int index = 0;
            var nodes = new Node[m_tree.Count];
            foreach(var node in m_tree.Values)
                nodes[index++] = node;
            m_tree.Clear();
            
            int writeIndex = 0;
            var writeNode  = nodes[writeIndex];

            for(int readIndex = 1; readIndex < nodes.Length; readIndex++) {
                var readNode      = nodes[readIndex];
                int readRemaining = readNode.Count;
                int readPos       = 0;

                while(readRemaining > 0) {
                    var copyCount = Math.Min(m_itemsPerNode - writeNode.Count, readRemaining);

                    if(copyCount > 0) {
                        // if the arrays+indices are the same, then it will do nothing anyway
                        Array.Copy(readNode.Items, readPos, writeNode.Items, writeNode.Count, copyCount);
                        writeNode.Count += copyCount;
                        readPos         += copyCount;
                        readRemaining   -= copyCount;
                    }

                    if(writeNode.Count == m_itemsPerNode) {
                        writeNode = nodes[++writeIndex];

                        // case example [100% fill first node, 80% fill second node]
                        // this makes sure that you basically do readIndex++
                        if(writeIndex == readIndex && readPos == 0)
                            break;
                    }
                }
            }

            // just to be safe and avoid leaks, we clear the arrays
            Array.Clear(writeNode.Items, writeNode.Count, m_itemsPerNode - writeNode.Count);
            // technically not needed
            //for(int i = writeIndex + 1; i < nodes.Length; i++) {
            //    var node = nodes[i];
            //    Array.Clear(node.Items, 0, node.Count);
            //    node.Count = 0;
            //}

            // dont rely on current writeIndex value, since it could be one higher than what we actually wrote to
            writeIndex = totalItemCount / m_itemsPerNode + (totalItemCount % m_itemsPerNode != 0 ? 1 : 0);

            // rebuild tree
            for(int i = 0; i < writeIndex; i++) {
                var node = nodes[i];
                // shouldnt be possible, but just in case
                if(node.Count <= 0)
                    continue;
                m_tree.Add(node.Items[0], node);
            }
        }
        #endregion
 
        #region private TryAdd()
        /// <summary>
        ///     O(node.Items.Count / 2)     O(1) operation, but memcpy() of half the items to insert.
        ///     Returns false if key found.
        /// </summary>
        private bool TryAdd(in BinarySearchResult searchResult, in TKey key, out BinarySearchResult insertLocation) {
            var x = searchResult;

            if(x.Index >= 0 && x.Node != null) {
                insertLocation = default;
                return false;
            }

            // add
            if(x.Node != null) {
                var node  = x.Node.Value;
                var index = ~x.Index;

                // if insert in middle of node
                if(index > 0 && index < m_itemsPerNode) {
                    // if the space exist
                    if(node.Count < m_itemsPerNode) {
                        // if the space exist
                        node.InsertAt(index, key);
                        insertLocation = new BinarySearchResult(x.Node, index);
                    } else if(this.TryAdd_TryMove1ItemToPreviousOrNextNode(x.Node, index, key)) {
                        insertLocation = new BinarySearchResult(x.Node, index);
                    } else
                        insertLocation = this.TryAdd_HandlePreviousCurrentAndNextFull(x, key);
                } else if(index == 0) {
                    // if inserting at the start of the node

                    if(node.Count < m_itemsPerNode) {
                        // if the space exist
                        node.InsertAt(0, key);
                        x.Node.UpdateKey(key);
                        insertLocation = new BinarySearchResult(x.Node, 0);
                    } else if(this.TryAdd_TryInsertOnPreviousNode(x.Node, key, out insertLocation)) {
                        // intentionally empty     //node.InsertAt(0, key);
                    } else
                        insertLocation = this.TryAdd_HandlePreviousAndCurrentFull(x, key);
                } else { // index == m_itemsPerNode
                    // if inserting at the end of the node
                    // note: current is full

                    if(this.TryAdd_TryInsertOnNextNode(x.Node, key, out insertLocation)) {
                        // intentionally empty     //node.InsertAt(0, key);
                    } else
                        insertLocation = this.TryAdd_HandleCurrentAndNextFull(x, key);
                }
            } else {
                var insert_node = this.TryAdd_HandleNoRoot(key);
                insertLocation = new BinarySearchResult(insert_node, 0);
            }

            this.Count++;
            return true;
        }
        private bool TryAdd_TryMove1ItemToPreviousOrNextNode(AvlTree<TKey, Node>.Node node, int index, in TKey _new) {
            var next = node.Next();
            if(next != null && next.Value.Count < m_itemsPerNode) {
                Move1Next(_new);
                return true;
            }

            var prev = node.Previous();
            if(prev != null && prev.Value.Count < m_itemsPerNode) {
                Move1Prev(_new);
                return true;
            }

            if(next == null) {
                next = m_tree.Add(node.Value.Items[node.Value.Count - 1], new Node(m_itemsPerNode));
                Move1Next(_new);
                return true;
            }

            if(prev == null) {
                // need the key to differ from the one were about to add
                node.UpdateKey(node.Value.Items[1]);
                prev = m_tree.Add(node.Value.Items[0], new Node(m_itemsPerNode));
                Move1Prev(_new);
                return true;
            }

            // prev and next are full
            return false;

            void Move1Prev(in TKey new_item) {
                prev.Value.InsertAt(prev.Value.Count, node.Value.Items[0]);
                Array.Copy(node.Value.Items, 1, node.Value.Items, 0, index - 1); //node.Value.RemoveAt(0);
                node.Value.Items[index - 1] = new_item; //node.Value.InsertAt(index - 1, in _new);
                node.UpdateKey(node.Value.Items[0]);
            }
            void Move1Next(in TKey new_item) {
                var count = node.Value.Count;

                next.Value.InsertAt(0, node.Value.Items[count - 1]);
                node.Value.RemoveAt(count - 1);
                next.UpdateKey(next.Value.Items[0]);
                node.Value.InsertAt(index, new_item);
            }
        }
        private bool TryAdd_TryInsertOnPreviousNode(AvlTree<TKey, Node>.Node node, in TKey item, out BinarySearchResult insertLocation) {
            var prev = node.Previous();
             
            if(prev != null) {
                // if full
                if(prev.Value.Count == m_itemsPerNode) {
                    insertLocation = default;
                    return false;
                }
                prev.Value.InsertAt(prev.Value.Count, item);
            } else {
                // note sure if should rebalance items across the 2 nodes
                // if you do 50%/50%, then on any delete it will just re-merge
                // keep in mind this case only applies on inserts <= to this.Minimum, so its preferable to make a new (empty-ish) node
                var new_node      = new Node(m_itemsPerNode);
                new_node.Items[0] = item;
                new_node.Count    = 1;
                m_tree.Add(item, new_node);
            }
            insertLocation = new BinarySearchResult(node, 0);
            return true;
        }
        private bool TryAdd_TryInsertOnNextNode(AvlTree<TKey, Node>.Node node, in TKey item, out BinarySearchResult insertLocation) {
            var next = node.Next();
 
            if(next != null) {
                // if full
                if(next.Value.Count == m_itemsPerNode) {
                    insertLocation = default;
                    return false;
                }
                next.Value.InsertAt(0, item);
                next.UpdateKey(item);
                insertLocation = new BinarySearchResult(next, 0);
            } else {
                // note sure if should rebalance items across the 2 nodes
                // if you do 50%/50%, then on any delete it will just re-merge
                // keep in mind this case only applies on inserts >= to this.Maximum, so its preferable to make a new (empty-ish) node
                var new_node         = new Node(m_itemsPerNode);
                new_node.Items[0]    = item;
                new_node.Count       = 1;
                var insert_tree_node = m_tree.Add(item, new_node);
                insertLocation = new BinarySearchResult(insert_tree_node, 0);
            }
            return true;
        }
        private AvlTree<TKey, Node>.Node TryAdd_HandleNoRoot(in TKey item) {
            var new_node      = new Node(m_itemsPerNode);
            new_node.Items[0] = item;
            new_node.Count    = 1;
            return m_tree.Add(item, new_node);
        }
        private BinarySearchResult TryAdd_HandlePreviousCurrentAndNextFull(in BinarySearchResult bsr, in TKey item) {
            // we could split the 3x nodes into 4x nodes at 75%, 
            // instead we split 2 nodes into 3x nodes at 66%
            return this.TryAdd_HandlePreviousAndCurrentFull(bsr, item);
        }
        private BinarySearchResult TryAdd_HandlePreviousAndCurrentFull(in BinarySearchResult bsr, in TKey item) {
            var index = ~bsr.Index;
            // note: we know current and prev() are full
            var nodes = this.TryAdd_Rebalance2FullNodesInto3(bsr.Node.Previous(), bsr.Node);
            // the insert index needs to be mapped unto its destination
            var index_within_current_node = m_itemsPerNode + index - m_twoThirdsItemsPerNode;
            var insert_within_current     = index_within_current_node <= m_twoThirdsItemsPerNode;
            AvlTree<TKey, Node>.Node insert_node;
            if(insert_within_current) {
                insert_node = nodes[1];
                index       = index_within_current_node;
            } else {
                insert_node = nodes[2];
                index       = index_within_current_node - m_twoThirdsItemsPerNode;
            }
                                 
            insert_node.Value.InsertAt(index, item);
            if(index == 0)
                insert_node.UpdateKey(item);
            return new BinarySearchResult(insert_node, index);
        }
        private BinarySearchResult TryAdd_HandleCurrentAndNextFull(in BinarySearchResult bsr, in TKey item) {
            var index = ~bsr.Index;
            // note: we know current and next() are full
            var nodes = this.TryAdd_Rebalance2FullNodesInto3(bsr.Node, bsr.Node.Next());
            // the insert index needs to be mapped unto its destination
            var index_within_current_node = index - m_twoThirdsItemsPerNode;
            var insert_within_current     = index_within_current_node <= m_twoThirdsItemsPerNode;
            AvlTree<TKey, Node>.Node insert_node;
            if(insert_within_current) {
                insert_node = nodes[1];
                index       = index_within_current_node;
            } else {
                insert_node = nodes[2];
                index       = index_within_current_node - m_twoThirdsItemsPerNode;
            }
                                 
            insert_node.Value.InsertAt(index, item);
            if(index == 0)
                insert_node.UpdateKey(item);
            return new BinarySearchResult(insert_node, index);
        }
        /// <summary>
        ///     Rebalance 2 nodes at 100% capacity into 3 nodes all at 66% capacity.
        ///     Returns {first,second,new}.
        /// </summary>
        private AvlTree<TKey, Node>.Node[] TryAdd_Rebalance2FullNodesInto3(AvlTree<TKey, Node>.Node first, AvlTree<TKey, Node>.Node second) {
            // important note: rounding overflow MUST be sent to last node, since caller assumes that
            // ie: result must be {[m_twoThirdsItemsPerNode], [m_twoThirdsItemsPerNode], [m_twoThirdsItemsPerNode + remainder]}

            var new_node = new Node(m_itemsPerNode) {
                Count = (m_itemsPerNode * 2) - (m_twoThirdsItemsPerNode * 2)
            };
            // copy node "overflow"
            Array.Copy(second.Value.Items, m_itemsPerNode - new_node.Count, new_node.Items, 0, new_node.Count);
            // move up the values in node to accept first "overflow"
            Array.Copy(second.Value.Items, 0, second.Value.Items, m_itemsPerNode - m_twoThirdsItemsPerNode, m_twoThirdsItemsPerNode - (m_itemsPerNode - m_twoThirdsItemsPerNode));
            // move up first into node
            Array.Copy(first.Value.Items, m_twoThirdsItemsPerNode, second.Value.Items, 0, m_itemsPerNode - m_twoThirdsItemsPerNode);
            Array.Clear(first.Value.Items, m_twoThirdsItemsPerNode, m_itemsPerNode - m_twoThirdsItemsPerNode);
            Array.Clear(second.Value.Items, m_twoThirdsItemsPerNode, m_itemsPerNode - m_twoThirdsItemsPerNode);
            first.Value.Count  = m_twoThirdsItemsPerNode;
            second.Value.Count = m_twoThirdsItemsPerNode;
            var _new = m_tree.Add(new_node.Items[0], new_node);
            second.UpdateKey(second.Value.Items[0]);
            return new[] { first, second, _new };
        }
        #endregion
        #region private TryMoveAllItemsToAdjacentNodes()
        /// <summary>
        ///     Tries to redistribute the node items to Previous()/Next() nodes if they fit, 
        ///     then delete the node itself.
        ///     Returns either the prev node if removed, or null;
        /// </summary>
        private AvlTree<TKey, Node>.Node TryMoveAllItemsToAdjacentNodes(AvlTree<TKey, Node>.Node node) {
            int nodes_to_move   = node.Value.Count;
            var prev            = node.Previous();
            var prev_space_left = prev != null ? m_itemsPerNode - prev.Value.Count : 0;
            var space_avail     = prev_space_left;
 
            // if all the current node items can fit in the prev node, then do so
            if(space_avail >= nodes_to_move) {
                Array.Copy(node.Value.Items, 0, prev.Value.Items, prev.Value.Count, nodes_to_move);
                prev.Value.Count += nodes_to_move;
                //Array.Clear(node.Value.Items, 0, node.Value.Count); // do this ?
                m_tree.Remove(node);
                return prev;
            }
 
            var next            = node.Next();
            var next_space_left = next != null ? m_itemsPerNode - next.Value.Count : 0;
            space_avail        += next_space_left;
 
            // if all the current node items can fit in the prev+next node, then do so
            if(space_avail >= nodes_to_move) {
                if(prev != null) {
                    Array.Copy(node.Value.Items, 0, prev.Value.Items, prev.Value.Count, prev_space_left);
                    prev.Value.Count = m_itemsPerNode;
                }
                Array.Copy(next.Value.Items, 0, next.Value.Items, nodes_to_move - prev_space_left, next.Value.Count);
                Array.Copy(node.Value.Items, prev_space_left, next.Value.Items, 0, nodes_to_move - prev_space_left);
                next.Value.Count += nodes_to_move - prev_space_left;
                //Array.Clear(node.Value.Items, 0, node.Value.Count); // do this ?
                m_tree.Remove(node);
                // this is important; it signals the first key of the next() node changed
                // this is kind of a hack since we know we dont need to tree.remove()/tree.add() the key in order to update it, 
                // because its relative position has not changed and would not affect the tree
                next.UpdateKey(next.Value.Items[0]);
                return prev;
            }
            return null;
        }
        #endregion
 
        #region private GetChildrenNodes()
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IEnumerable<TKey> GetChildrenNodes() {
            foreach(var item in m_tree.Items) {
                int max   = item.Value.Count;
                var items = item.Value.Items;
 
                for(int i = 0; i < max; i++)
                    yield return items[i];
            }
        }
        #endregion
 
        #region explicit interface(s) implementations
        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetChildrenNodes().GetEnumerator();
        }
 
        object ICollection.SyncRoot => this;
        bool ICollection.IsSynchronized => false;
 
        void ICollection.CopyTo(Array array, int arrayIndex) {
            if(array == null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
 
            foreach(var node in this.GetChildrenNodes())
                array.SetValue(node, arrayIndex++);
        }
        #endregion
 
        internal sealed class Node {
            public readonly TKey[] Items;
            public int Count;
 
            #region constructors
            public Node(int count) {
                this.Items = new TKey[count];
            }
            #endregion
 
            #region BinarySearch()
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int BinarySearch(in TKey key, Comparison<TKey> comparer) {
                int min = 0;
                int max = this.Count - 1;
             
                while(min <= max) {
                    int median = (min + max) >> 1;
                    var diff   = comparer(this.Items[median], key);
                     
                    if(diff < 0)
                        min = median + 1;
                    else if(diff > 0)
                        max = median - 1;
                    else
                        return median;
                }
                 
                return ~min;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int BinarySearch(in TKey key, int min, Comparison<TKey> comparer) {
                int max = this.Count - 1;
             
                while(min <= max) {
                    int median = (min + max) >> 1;
                    var diff   = comparer(this.Items[median], key);
                     
                    if(diff < 0)
                        min = median + 1;
                    else if(diff > 0)
                        max = median - 1;
                    else
                        return median;
                }
                 
                return ~min;
            }
            #endregion
            #region InsertAt()
            public void InsertAt(int index, in TKey item) {
                var count  = this.Count;
                this.Count = count + 1;
                Array.Copy(this.Items, index, this.Items, index + 1, count - index);
                this.Items[index] = item;
            }
            #endregion
            #region RemoveAt()
            public void RemoveAt(int index) {
                var count  = this.Count - 1;
                this.Count = count;
                Array.Copy(this.Items, index + 1, this.Items, index, count - index);
                this.Items[count] = default;
            }
            #endregion
        }
    }


    public static class BTreeExtensions {
        #region static BTree<string>.StartsWith()
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        /// </summary>
        public static IEnumerable<string> StartsWith(BTree<string> tree, string key) {
            return BTree<string>.StartsWith(tree, key);
        }
        #endregion
        #region static BTree<string, TValue>.StartsWith()
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        /// </summary>
        public static IEnumerable<BTree<string, TValue>.KeyValuePair> StartsWith<TValue>(BTree<string, TValue> tree, string key) {
            return BTree<string, TValue>.StartsWith(tree, key);
        }
        #endregion
    }
}
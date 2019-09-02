//#define IMPLEMENT_DICTIONARY_INTERFACES // might want to disable due to System.Linq.Enumerable extensions clutter

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Specialized
{
    /// <summary>
    ///    Implements a B+Tree using an AvlTree&lt;TKey, KeyValuePair&lt;TKey,TValue&gt;[]&gt;.
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
        private readonly Comparison<TKey> m_comparer;
        private readonly int m_itemsPerNode;          // recommended: Max(4096/(sizeof(TKey) + sizeof(TValue)), 16)
        private readonly int m_halfItemsPerNode;
        private readonly int m_twoThirdsItemsPerNode; // 66%

        public int Count { get; private set; }

        #region constructors
        /// <param name="items_per_node">Default: -1 = 4096/(IntPtr.Size*2). Recommended: Math.Max(4096/(sizeof(TKey) + sizeof(TValue)), 16)</param>
        public BTree(int items_per_node = -1) : this(Comparer<TKey>.Default.Compare, items_per_node) { }
        /// <param name="items_per_node">Default: -1 = 4096/(IntPtr.Size*2). Recommended: Math.Max(4096/(sizeof(TKey) + sizeof(TValue)), 16)</param>
        public BTree(IComparer<TKey> comparer, int items_per_node = -1) : this(comparer.Compare, items_per_node) { }
        /// <param name="items_per_node">Default: -1 = 4096/(IntPtr.Size*2). Recommended: Math.Max(4096/(sizeof(TKey) + sizeof(TValue)), 16)</param>
        public BTree(Comparison<TKey> comparer, int items_per_node = -1) {
            m_comparer              = comparer ?? throw new ArgumentNullException(nameof(comparer));
            m_itemsPerNode          = items_per_node < 0 ? DEFAULT_ITEMS_PER_NODE : items_per_node;
            m_halfItemsPerNode      = items_per_node / 2;
            m_twoThirdsItemsPerNode = (int)Math.Floor((items_per_node / 3d) * 2d);
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
        public IEnumerable<KeyValuePair<TKey, TValue>> Items => this.GetChildrenNodes();
        #endregion
        #region this[]
        /// <summary>
        ///    O(log n)
        /// </summary>
        public TValue this[TKey key] {
            get{
                if(!this.TryGetValue(key, out var value))
                    throw new KeyNotFoundException();
                return value;
            }
            set {
                if(!this.TryAdd(key, value, out var x))
                    // update
                    x.Items[x.Index] = new KeyValuePair<TKey, TValue>(key, value);
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
        public KeyValuePair<TKey, TValue> Minimum => m_tree.Minimum.Value.Items[0];
        #endregion
        #region Maximum
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public KeyValuePair<TKey, TValue> Maximum {
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
        ///     Returns the added item.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public void Add(TKey key, TValue value) {
            if(!this.TryAdd(key, value, out _))
                throw new ArgumentException($"Duplicate key ({key}).", nameof(key));;
        }
        #endregion
        #region AddRange()
        /// <summary>
        ///     O(m log n)
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> values) {
            foreach(var value in values)
                this.Add(value.Key, value.Value);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     O(log n)
        /// </summary>
        public bool Remove(TKey key) {
            var x = this.BinarySearch(key);

            if(x.Index >= 0 && x.Node != null) {
                x.Node.Value.RemoveAt(x.Index);

                var node_count = x.Node.Value.Count;

                if(node_count <= m_halfItemsPerNode && node_count > 0)
                   this.TryMoveAllItemsToAdjacentNodes(x.Node);
                else if(node_count == 0)
                    // this case should be rare
                    m_tree.Remove(x.Node);
                
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
        }
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
        public bool TryGetValue(TKey key, out TValue value) {
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
        public bool TryGetItem(TKey key, out KeyValuePair<TKey, TValue> item) {
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
        public bool ContainsKey(TKey key) {
            return this.TryGetItem(key, out _);
        }
        #endregion
        #region BinarySearch()
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearch(TKey key) {
            return this.BinarySearch(key, m_comparer);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(TKey key, Comparison<TKey> comparer) {
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

            public KeyValuePair<TKey, TValue> Item => this.Node.Value.Items[this.Index]; // this.Node.Value.Items[this.Index >= 0 ? this.Index : ~this.Index]
            public KeyValuePair<TKey, TValue>[] Items => this.Node?.Value.Items;

            internal BinarySearchResult(AvlTree<TKey, Node>.Node node, int index) : this() {
                this.Node  = node;
                this.Index = index;
            }
        }
        #endregion

        #region private TryAdd()
        /// <summary>
        ///     O(log n)
        ///     Returns false if key found
        /// </summary>
        private bool TryAdd(TKey key, TValue value, out BinarySearchResult searchResult) {
            var x        = this.BinarySearch(key);
            searchResult = x;

            if(x.Node != null && x.Index >= 0)
                return false;
            
            var _new = new KeyValuePair<TKey, TValue>(key, value);

            // add
            if(x.Node != null) {
                var node  = x.Node.Value;
                var index = ~x.Index;

                // if insert in middle of node
                if(index > 0 && index < m_itemsPerNode) {
                    // if the space exist
                    if(node.Count < m_itemsPerNode)
                        // if the space exist
                        node.InsertAt(index, in _new);
                    else if(this.TryMove1ItemToPreviousNode(x.Node))
                        node.InsertAt(index - 1, in _new);
                    else if(this.TryMove1ItemToNextNode(x.Node))
                        node.InsertAt(index, in _new);
                    else
                        this.TryAdd_HandlePreviousCurrentAndNextFull(x, in _new);
                } else if(index == 0) {
                    // if inserting at the start of the node

                    if(node.Count < m_itemsPerNode) {
                        // if the space exist
                        node.InsertAt(0, in _new);
                        x.Node.UpdateKey(key);
                    } else if(this.TryInsertOnPreviousNode(x.Node, in _new)) {
                        // intentionally empty     //node.InsertAt(0, in _new);
                    } else
                        this.TryAdd_HandlePreviousAndCurrentFull(x, in _new);
                } else { // index == m_itemsPerNode
                    // if inserting at the end of the node
                    // note: current is full

                    if(this.TryInsertOnNextNode(x.Node, in _new)) {
                        // intentionally empty     //node.InsertAt(0, in _new);
                    } else
                        this.TryAdd_HandleCurrentAndNextFull(x, in _new);
                }
            } else
                this.TryAdd_HandleNoRoot(in _new);
                    
            this.Count++;
            return true;
        }
        private void TryAdd_HandleNoRoot(in KeyValuePair<TKey, TValue> item) {
            var new_node      = new Node(m_itemsPerNode);
            new_node.Items[0] = item;
            new_node.Count    = 1;
            m_tree.Add(item.Key, new_node);
        }
        private void TryAdd_HandlePreviousCurrentAndNextFull(BinarySearchResult bsr, in KeyValuePair<TKey, TValue> item) {
            // we could split the 3x nodes into 4x nodes at 75%, 
            // instead we split 2 nodes into 3x nodes at 66%
            this.TryAdd_HandlePreviousAndCurrentFull(bsr, in item);
        }
        private void TryAdd_HandlePreviousAndCurrentFull(BinarySearchResult bsr, in KeyValuePair<TKey, TValue> item) {
            var index = ~bsr.Index;
            // note: we know current and prev() are full
            var nodes = this.Rebalance2FullNodesInto3(bsr.Node.Previous(), bsr.Node);
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
        }
        private void TryAdd_HandleCurrentAndNextFull(BinarySearchResult bsr, in KeyValuePair<TKey, TValue> item) {
            var index = ~bsr.Index;
            // note: we know current and next() are full
            var nodes = this.Rebalance2FullNodesInto3(bsr.Node, bsr.Node.Next());
            // the insert index needs to be mapped unto its destination
            var index_within_current_node = index - m_twoThirdsItemsPerNode;
            var insert_within_current     = index_within_current_node <= m_twoThirdsItemsPerNode;
            AvlTree<TKey, Node>.Node insert_node;
            if(insert_within_current) {
                insert_node = nodes[0];
                index       = index_within_current_node;
            } else {
                insert_node = nodes[1];
                index       = index_within_current_node - m_twoThirdsItemsPerNode;
            }
                                
            insert_node.Value.InsertAt(index, in item);
            if(index == 0)
                insert_node.UpdateKey(item.Key);
        }
        #endregion
        #region private TryMove1ItemToPreviousNode()
        private bool TryMove1ItemToPreviousNode(AvlTree<TKey, Node>.Node node) {
            var prev = node.Previous();
            
            if(prev == null || prev.Value.Count == m_itemsPerNode)
                return false;

            prev.Value.InsertAt(prev.Value.Count, in node.Value.Items[0]);
            node.Value.RemoveAt(0);
            node.UpdateKey(node.Value.Items[0].Key);
            return true;
        }
        #endregion
        #region private TryMove1ItemToNextNode()
        private bool TryMove1ItemToNextNode(AvlTree<TKey, Node>.Node node) {
            var next = node.Next();
            
            if(next == null || next.Value.Count == m_itemsPerNode)
                return false;

            var count     = node.Value.Count;

            next.Value.InsertAt(0, in node.Value.Items[count - 1]);
            node.Value.RemoveAt(count - 1);
            next.UpdateKey(next.Value.Items[0].Key);
            return true;
        }
        #endregion
        #region private TryMoveAllItemsToAdjacentNodes()
        /// <summary>
        ///     Tries to redistribute the node items to Previous()/Next() nodes if they fit, 
        ///     then delete the node itself.
        /// </summary>
        private void TryMoveAllItemsToAdjacentNodes(AvlTree<TKey, Node>.Node node) {
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
                return;
            }

            var next            = node.Next();
            var next_space_left = next != null ? m_itemsPerNode - next.Value.Count : 0;
            space_avail        += next_space_left;

            // if all the current node items can fit in the prev+next node, then do so
            if(space_avail >= nodes_to_move) {
                Array.Copy(node.Value.Items, 0, prev.Value.Items, prev.Value.Count, prev_space_left);
                prev.Value.Count = m_itemsPerNode;
                Array.Copy(next.Value.Items, 0, next.Value.Items, nodes_to_move - prev_space_left, next.Value.Count);
                Array.Copy(node.Value.Items, prev_space_left, next.Value.Items, 0, nodes_to_move - prev_space_left);
                next.Value.Count += nodes_to_move - prev_space_left;
                //Array.Clear(node.Value.Items, 0, node.Value.Count); // do this ?
                m_tree.Remove(node);
                // this is important; it signals the first key of the next() node changed
                // this is kind of a hack since we know we dont need to tree.remove()/tree.add() the key in order to update it, 
                // because its relative position has not changed and would not affect the tree
                next.UpdateKey(next.Value.Items[0].Key);
                return;
            }
        }
        #endregion
        #region private TryInsertOnPreviousNode()
        private bool TryInsertOnPreviousNode(AvlTree<TKey, Node>.Node node, in KeyValuePair<TKey, TValue> item) {
            var prev = node.Previous();
            
            if(prev != null) {
                // if full
                if(prev.Value.Count == m_itemsPerNode)
                    return false;
                prev.Value.InsertAt(prev.Value.Count, in node.Value.Items[0]);
                node.Value.Items[0] = item;
                node.UpdateKey(item.Key);
            } else {
                // note sure if should rebalance items across the 2 nodes
                // if you do 50%/50%, then on any delete it will just re-merge
                // keep in mind this case only applies on inserts <= to this.Minimum, so its preferable to make a new (empty-ish) node
                var new_node        = new Node(m_itemsPerNode);
                new_node.Items[0]   = node.Value.Items[0];
                new_node.Count      = 1;
                node.Value.Items[0] = item;
                node.UpdateKey(item.Key);
                m_tree.Add(new_node.Items[0].Key, new_node);
            }
            return true;
        }
        #endregion
        #region private TryInsertOnNextNode()
        private bool TryInsertOnNextNode(AvlTree<TKey, Node>.Node node, in KeyValuePair<TKey, TValue> item) {
            var next = node.Next();

            if(next != null) {
                // if full
                if(next.Value.Count == m_itemsPerNode)
                    return false;
                next.Value.InsertAt(0, in item);
                next.UpdateKey(item.Key);
            } else {
                // note sure if should rebalance items across the 2 nodes
                // if you do 50%/50%, then on any delete it will just re-merge
                // keep in mind this case only applies on inserts >= to this.Maximum, so its preferable to make a new (empty-ish) node
                var new_node      = new Node(m_itemsPerNode);
                new_node.Items[0] = item;
                new_node.Count    = 1;
                m_tree.Add(item.Key, new_node);
            }
            return true;
        }
        #endregion
        #region private Rebalance2FullNodesInto3()
        /// <summary>
        ///     Rebalance 2 nodes at 100% capacity into 3 nodes all at 66% capacity.
        ///     Returns {first,second,new}.
        /// </summary>
        private AvlTree<TKey, Node>.Node[] Rebalance2FullNodesInto3(AvlTree<TKey, Node>.Node first, AvlTree<TKey, Node>.Node second) {
            // important note: rounding overflow MUST be sent to last node, since caller assumes that
            // ie: result must be {[m_twoThirdsItemsPerNode], [m_twoThirdsItemsPerNode], [m_twoThirdsItemsPerNode + remainder]}

            var new_node   = new Node(m_itemsPerNode);
            new_node.Count = (m_itemsPerNode * 2) - (m_twoThirdsItemsPerNode * 2);
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

        #region private GetChildrenNodes()
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IEnumerable<KeyValuePair<TKey, TValue>> GetChildrenNodes() {
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
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) {
            this.Add(item.Key, item.Value);
        }

        /// <summary>
        ///     O(log n)
        /// </summary>
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) {
            return this.Remove(item.Key);
        }
        
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() {
            foreach(var node in this.GetChildrenNodes())
                yield return new KeyValuePair<TKey, TValue>(node.Key, node.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) {
            return this.TryGetValue(item.Key, out TValue value) && object.Equals(item.Value, value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
            if(array == null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));

            foreach(var node in (IEnumerable<KeyValuePair<TKey, TValue>>)this)
                array[arrayIndex++] = node;
        }
#endif
        #endregion

        internal sealed class Node {
            public readonly KeyValuePair<TKey, TValue>[] Items;
            public int Count;

            #region constructors
            public Node(int count) {
                this.Items = new KeyValuePair<TKey, TValue>[count];
            }
            #endregion

            #region BinarySearch()
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int BinarySearch(TKey key, Comparison<TKey> comparer) {
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
            #endregion
            #region InsertAt()
            public void InsertAt(int index, in KeyValuePair<TKey, TValue> item) {
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
}
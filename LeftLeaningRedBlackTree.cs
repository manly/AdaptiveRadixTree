//#define IMPLEMENT_DICTIONARY_INTERFACES // might want to disable due to System.Linq.Enumerable extensions clutter

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace System.Collections.Specialized
{
    /// <summary>
    ///    Implements a left-leaning red-black tree (dictionary).
    ///    This is a self-balancing binary search tree that takes 1 extra bit per node over a binary search tree.
    ///    Search/Insert/Delete() run in O(log n).
    /// </summary>
    /// <remarks>
    ///    Based on the research paper "Left-leaning Red-Black Trees" by Robert Sedgewick
    ///    http://www.cs.princeton.edu/~rs/talks/LLRB/RedBlack.pdf
    ///    http://www.cs.princeton.edu/~rs/talks/LLRB/08Penn.pdf
    ///    
    ///    worst case       |   RB tree     |   AVL tree
    ///    =======================================
    ///    height           | 2 log(n + 1)  | 1.44 log n
    ///    update           | log n         | log n
    ///    lookup           | log n         | log n  (faster)
    ///    insert rotations | 2             | 2
    ///    delete rotations | 3             | log n
    ///    
    ///    Basically use AvlTree if you care more about lookup speed and dont generally update the tree.
    /// </remarks>
    public sealed class LeftLeaningRedBlackTree<TKey, TValue> : ICollection
#if IMPLEMENT_DICTIONARY_INTERFACES
        , IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
#endif
    {
        private Node m_root;
        private readonly Comparison<TKey> m_comparer;

        public int Count { get; private set; }
    
        #region constructors
        public LeftLeaningRedBlackTree() : this(Comparer<TKey>.Default) { }
        public LeftLeaningRedBlackTree(IComparer<TKey> comparer) : this(comparer.Compare) { }
        public LeftLeaningRedBlackTree(Comparison<TKey> comparer) {
            m_comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
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
        public IEnumerable<Node> Items => this.GetChildrenNodes();
        #endregion
        #region this[]
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public TValue this[TKey key] {
            get{
                var current = m_root;
                while(current != null) {
                    int diff = m_comparer(key, current.Key);
    
                    if(diff < 0)
                        current = current.Left;
                    else if(diff > 0)
                        current = current.Right;
                    else
                        return current.Value;
                }
    
                throw new KeyNotFoundException();
            }
            set {
                var current = m_root;
                while(current != null) {
                    int diff = m_comparer(key, current.Key);
    
                    if(diff < 0)
                        current = current.Left;
                    else if(diff > 0)
                        current = current.Right;
                    else {
                        current.Value = value;
                        return;
                    }
                }
    
                this.Add(key, value);
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
        public Node Minimum {
            get {
                var current = m_root;
                if(current == null)
                    throw new KeyNotFoundException();

                Node parent = null;
                while(current != null) {
                    parent  = current;
                    current = current.Left;
                }
                 
                return parent;
            }
        }
        #endregion
        #region Maximum
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public Node Maximum {
            get {
                var current = m_root;
                if(current == null)
                    throw new KeyNotFoundException();

                Node parent = null;
                while(current != null) {
                    parent  = current;
                    current = current.Right;
                }
                 
                return parent;
            }
        }
        #endregion
    
        #region Add()
        /// <summary>
        ///     O(log n)
        ///     
        ///     Returns the newly added node.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public Node Add(TKey key, TValue value) {
            var new_node     = new Node(key, value);
            var new_root     = this.AddRecursive(m_root, key, new_node);
            new_root.IsBlack = true;
            m_root           = new_root;
            this.Count++;
            return new_node;
        }
        private Node AddRecursive(Node node, TKey key, Node new_node) {
            if(node == null)
                return new_node;
    
            if(IsRed(node.Left) && IsRed(node.Right))
                // split node with two red children
                FlipColor(node);
    
            int diff = m_comparer(key, node.Key);
            if(diff < 0)
                node.Left = this.AddRecursive(node.Left, key, new_node);
            else if(diff > 0)
                node.Right = this.AddRecursive(node.Right, key, new_node);
            else
                throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
    
            if(IsRed(node.Right))
                // rotate to prevent red node on right
                node = RotateLeft(node);
    
            if(IsRed(node.Left) && IsRed(node.Left.Left))
                // rotate to prevent consecutive red nodes
                node = RotateRight(node);
            
            return node;
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
            if(m_root != null) {
                var oldCount = this.Count;
                m_root = this.RemoveRecursive(m_root, key);
                if(m_root != null)
                    m_root.IsBlack = true;
                return oldCount != this.Count;
            } else
                return false;
        }
        private Node RemoveRecursive(Node node, TKey key) {
            if(m_comparer(key, node.Key) < 0) {
                // continue search if left is present
                if(node.Left != null) {
                    if(!IsRed(node.Left) && !IsRed(node.Left.Left))
                        node = MoveRedLeft(node);
    
                    // remove from left
                    node.Left = this.RemoveRecursive(node.Left, key);
                }
            } else {
                if(IsRed(node.Left))
                    // flip a 3 node or unbalance a 4 node
                    node = RotateRight(node);
                
                if(m_comparer(key, node.Key) == 0 && node.Right == null) {
                    // remove leaf
                    this.Count--;
                    return null;
                }
                // continue search if right is present
                if(node.Right != null) {
                    if(!IsRed(node.Right) && !IsRed(node.Right.Left))
                        // move a red node over
                        node = MoveRedRight(node);
                    
                    if(m_comparer(key, node.Key) == 0) {
                        // remove leaf 
                        this.Count--;

                        // find the smallest node on the right, swap, and remove it
                        var min = node.Right;
                        while(min != null)
                            min = min.Left;
                
                        node.UpdateKey(min.Key);
                        node.Value = min.Value;
                        node.Right = DeleteMinimum(node.Right);
                    } else
                        // remove from right
                        node.Right = this.RemoveRecursive(node.Right, key);
                }
            }
    
            return FixUp(node);
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
            m_root = null;
            this.Count = 0;
        }
        #endregion
    
        #region TryGetValue()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value) {
            var current = m_root;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
    
                if(diff < 0)
                    current = current.Left;
                else if(diff > 0)
                    current = current.Right;
                else {
                    value = current.Value;
                    return true;
                }
            }
    
            value = default;
            return false;
        }
        #endregion
        #region ContainsKey()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool ContainsKey(TKey key) {
            var current = m_root;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
    
                if(diff < 0)
                    current = current.Left;
                else if(diff > 0)
                    current = current.Right;
                else
                    return true;
            }
            return false;
        }
        #endregion
        #region BinarySearch()
        /// <summary>
        ///    O(log n)
        ///    
        ///    This lets you know the nearest match to your key.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed lowest_or_equal result; 
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearch(TKey key) {
            // inline this since this is usually called in hot paths
            //return this.BinarySearch(key, m_comparer);

            var current   = m_root;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                prev_diff = m_comparer(key, current.Key);

                if(prev_diff < 0) {
                    prev    = current;
                    current = current.Left;
                } else if(prev_diff > 0) {
                    prev    = current;
                    current = current.Right;
                } else
                    return new BinarySearchResult(current, 0);
            }
            return new BinarySearchResult(prev, prev_diff);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    This lets you know the nearest match to your key.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed lowest_or_equal result; 
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(TKey key, Comparison<TKey> comparer) {
            var current   = m_root;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                prev_diff = comparer(key, current.Key);

                if(prev_diff < 0) {
                    prev    = current;
                    current = current.Left;
                } else if(prev_diff > 0) {
                    prev    = current;
                    current = current.Right;
                } else
                    return new BinarySearchResult(current, 0);
            }
            return new BinarySearchResult(prev, prev_diff);
        }
        public readonly ref struct BinarySearchResult {
            /// <summary>
            ///    -1: key &lt; node.key
            ///     0: key ==   node.key
            ///     1: key &gt; node.key
            /// </summary>
            public readonly int Diff;
            public readonly Node Node;
            public BinarySearchResult(Node node, int diff) : this() {
                this.Node = node;
                this.Diff = diff;
            }
        }
        #endregion
        #region Depth()
        /// <summary>
        ///     O(n)
        /// </summary>
        public int Depth() {
            return DepthRecursive(m_root);
        }
        private static int DepthRecursive(Node node) {
            if(node != null)
                return Math.Max(
                    (node.Left != null ? DepthRecursive(node.Left) : 0) + 1,
                    (node.Right != null ? DepthRecursive(node.Right) : 0) + 1);
            return 0;
        }
        #endregion

        #region private static IsRed()
        private static bool IsRed(Node node) {
            if(node == null)
                // virtual leaf nodes are always black
                return false;
            
            return !node.IsBlack;
        }
        #endregion
        #region private static FlipColor()
        private static void FlipColor(Node node) {
            node.IsBlack       = !node.IsBlack;
            node.Left.IsBlack  = !node.Left.IsBlack;
            node.Right.IsBlack = !node.Right.IsBlack;
        }
        #endregion
        #region private static RotateLeft()
        private static Node RotateLeft(Node node) {
            var right     = node.Right;
            node.Right    = right.Left;
            right.Left    = node;
            right.IsBlack = node.IsBlack;
            node.IsBlack  = false;
            return right;
        }
        #endregion
        #region private static RotateRight()
        private static Node RotateRight(Node node) {
            var left     = node.Left;
            node.Left    = left.Right;
            left.Right   = node;
            left.IsBlack = node.IsBlack;
            node.IsBlack = false;
            return left;
        }
        #endregion
        #region private static MoveRedLeft()
        /// <summary>
        ///     Moves a red node from the right child to the left child.
        ///     Returns new root.
        /// </summary>
        private static Node MoveRedLeft(Node node) {
            FlipColor(node);
            if(IsRed(node.Right.Left)) {
                node.Right = RotateRight(node.Right);
                node       = RotateLeft(node);
                FlipColor(node);
    
                // avoid creating right-leaning nodes
                if(IsRed(node.Right.Right))
                    node.Right = RotateLeft(node.Right);
            }
            return node;
        }
        #endregion
        #region private static MoveRedRight()
        /// <summary>
        ///     Moves a red node from the left child to the right child.
        ///     Returns new root.
        /// </summary>
        private static Node MoveRedRight(Node node) {
            FlipColor(node);
            if(IsRed(node.Left.Left)) {
                node = RotateRight(node);
                FlipColor(node);
            }
            return node;
        }
        #endregion
        #region private static DeleteMinimum()
        /// <summary>
        ///     Returns new root.
        /// </summary>
        private static Node DeleteMinimum(Node node) {
            if(node.Left == null)
                return null;
    
            if(!IsRed(node.Left) && !IsRed(node.Left.Left))
                node = MoveRedLeft(node);
    
            node.Left = DeleteMinimum(node.Left);
    
            // maintain invariants
            return FixUp(node);
        }
        #endregion
        #region private static FixUp()
        /// <summary>
        ///     Maintains invariants by adjusting the specified nodes children.
        ///     Returns new root.
        /// </summary>
        private static Node FixUp(Node node) {
            if(IsRed(node.Right))
                // avoid right-leaning node
                node = RotateLeft(node);
    
            if(IsRed(node.Left) && IsRed(node.Left.Left))
                // balance 4-node
                node = RotateRight(node);
    
            if(IsRed(node.Left) && IsRed(node.Right))
                // push red up
                FlipColor(node);
    
            // avoid right-leaning nodes
            if(node.Left != null && IsRed(node.Left.Right) && !IsRed(node.Left.Left)) {
                node.Left = RotateLeft(node.Left);
                if(IsRed(node.Left))
                    // balance 4-node
                    node = RotateRight(node);
            }
    
            return node;
        }
        #endregion

        #region private GetChildrenNodes()
        /// <summary>
        ///     O(n)
        ///     Returns items in key order.
        ///     Use ChildrenNodesEnumerator instead for efficient re-use.
        /// </summary>
        private IEnumerable<Node> GetChildrenNodes() {
            return new ChildrenNodesEnumerator().Run(m_root);
        }
        /// <summary>
        ///     O(n)
        ///     Returns items in key order.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        /// </summary>
        private sealed class ChildrenNodesEnumerator {
            // manually handled stack for better performance
            private Node[] m_stack = new Node[16];
            private int m_stackIndex = 0;

            public IEnumerable<Node> Run(Node node) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }

                while(node != null) {
                    if(node.Left != null) {
                        this.Push(node);
                        node = node.Left;
                    } else {
                        do {
                            yield return node;
                            node = node.Right;
                        } while(node == null && m_stackIndex > 0 && (node = this.Pop()) != null);
                    }
                }
            }
            private void Push(Node value) {
                m_stack[m_stackIndex++] = value;
                if(m_stackIndex > m_stack.Length)
                    Array.Resize(ref m_stack, (m_stackIndex - 1) * 2);
            }
            private Node Pop() {
                var node = m_stack[--m_stackIndex];
                m_stack[m_stackIndex] = default;
                return node;
            }
        }
        #endregion

        #region Explicit Interfaces
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
        ///     O(log n)
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) {
            this.Add(key, value);
        }

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

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
        
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() {
            foreach(var node in this.GetChildrenNodes())
                yield return new KeyValuePair<TKey, TValue>(node.Key, node.Value);
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


        public sealed class Node {
            public TKey Key { get; private set; }
            public TValue Value;
    
            internal Node Left;
            internal Node Right;
            internal bool IsBlack;

            // todo: code Next()/Previous() to be able to use BinarySearch()
            //#region Next()
            ///// <summary>
            /////     O(1)
            /////     Returns the next node.
            /////     This behaves like an iterator, but will keep working even as the tree is being changed.
            /////     This will run roughly half the speed as using the iterator if iterating through the entire tree.
            ///// </summary>
            //public Node Next() {
            //    
            //}
            //#endregion
            //#region Previous()
            ///// <summary>
            /////     O(1)
            /////     Returns the previous node.
            /////     This behaves like an iterator, but will keep working even as the tree is being changed.
            /////     This will run roughly half the speed as using the iterator if iterating through the entire tree.
            ///// </summary>
            //public Node Previous() {
            //    
            //}
            //#endregion
            #region UpdateKey()
            /// <summary>
            ///     Change the key without updating the tree.
            ///     This is an "unsafe" operation; it can break the tree if you don't know what you're doing.
            ///     Safe to change if [key &gt; this.Previous() && key &lt; this.Next()].
            /// </summary>
            public void UpdateKey(TKey key) {
                this.Key = key;
            }
            #endregion

            #region constructors
            public Node(TKey key, TValue value) {
                this.Key   = key;
                this.Value = value;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return string.Format("[{0}] {1}", this.Key, this.Value);
            }
            #endregion
        }
    }
}
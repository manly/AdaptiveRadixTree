using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Collections.Specialized
{
    /// <summary>
    ///    Implements a red-black tree (dictionary).
    ///    This is a self-balancing binary search tree that takes 1 extra bit per node over a binary search tree.
    ///    Search/Insert/Delete() run in O(log n).
    /// </summary>
    /// <remarks>
    ///    Based on "Introduction to Algorithms" by Cormen, Leiserson & Rivest.
    ///    
    ///    This is intentionally *not* a left-leaning red-black tree as they perform worse than the base red-black trees.
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
    public sealed class RedBlackTree<TKey, TValue>  : ICollection
#if IMPLEMENT_DICTIONARY_INTERFACES
        , IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
#endif
    {
        private Node m_root;
        private readonly Comparison<TKey> m_comparer;
     
        #region constructors
        public RedBlackTree() : this(Comparer<TKey>.Default) { }
        public RedBlackTree(IComparer<TKey> comparer) : this(comparer.Compare) { }
        public RedBlackTree(Comparison<TKey> comparer) {
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
 
        public int Count { get; private set; }
     
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
            Node parent = null;
            var current = m_root;
            while(current != null) {
                parent   = current;
                var diff = m_comparer(key, current.Key);

                if(diff < 0)
                    current = current.Left;
                else if(diff > 0)
                    current = current.Right;
                else
                    throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
            }

            var newNode = new Node(key, value){
                Parent  = parent,
                IsBlack = false,
            };

            if(parent != null) {
                if(m_comparer(key, parent.Key) < 0)
                    parent.Left = newNode;
                else
                    parent.Right = newNode;
            } else
                m_root = newNode;

            this.Balance(newNode);
            this.Count++;

            return newNode;
        }
        private void Balance(Node node) {
            while(node != m_root && !node.Parent.IsBlack) {
                if(node.Parent == node.Parent.Parent.Left) {
                    var right = node.Parent.Parent.Right;
                    if(right != null && !right.IsBlack) {
                        node.Parent.IsBlack        = true;
                        right.IsBlack              = true;
                        node.Parent.Parent.IsBlack = false;
                        node                       = node.Parent.Parent;
                    } else {
                        if(node == node.Parent.Right) {
                            node = node.Parent;
                            this.RotateLeft(node);
                        }

                        node.Parent.IsBlack        = true;
                        node.Parent.Parent.IsBlack = false;
                        this.RotateRight(node.Parent.Parent);
                    }
                } else {
                    var left = node.Parent.Parent.Left;
                    if(left != null && !left.IsBlack) {
                        node.Parent.IsBlack        = true;
                        left.IsBlack               = true;
                        node.Parent.Parent.IsBlack = false;
                        node                       = node.Parent.Parent;
                    } else {
                        if(node == node.Parent.Left) {
                            node = node.Parent;
                            this.RotateRight(node);
                        }

                        node.Parent.IsBlack        = true;
                        node.Parent.Parent.IsBlack = false;
                        this.RotateLeft(node.Parent.Parent);
                    }
                }
            }
            m_root.IsBlack = true;
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
            var current = m_root;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
     
                if(diff < 0)
                    current = current.Left;
                else if(diff > 0)
                    current = current.Right;
                else
                    return this.Remove(current);
            }
            return false;
        }
        /// <summary>
        ///     O(1)
        /// </summary>
        public bool Remove(Node node) {
            var y = node.Left == null || node.Right == null ?
                node : 
                node.Next();

            var x = y.Left ?? y.Right;

            if(x != null)
                x.Parent = y.Parent;

            if(y.Parent != null) {
                if(y == y.Parent.Left)
                    y.Parent.Left = x;
                else
                    y.Parent.Right = x;
            } else
                m_root = x;

            if(node != y) {
                CopyFrom(y, node);

                if(node == m_root)
                    m_root = y;
            }

            if(y.IsBlack && x != null)
                this.RemoveFix(x);

            this.Count--;
            return true;
        }
        private void RemoveFix(Node node) {
            while(node != m_root && node.IsBlack) {
                if(node == node.Parent.Left) {
                    var w = node.Parent.Right;
                    if(w == null) {
                        node = node.Parent;
                        continue;
                    }

                    if(!w.IsBlack) {
                        w.IsBlack           = true;
                        node.Parent.IsBlack = false;
                        this.RotateLeft(node.Parent);
                        w = node.Parent.Right;
                    }

                    if(w == null) {
                        node = node.Parent;
                        continue;
                    }

                    if((w.Left == null || w.Left.IsBlack) &&
                        (w.Right == null || w.Right.IsBlack)) {
                        w.IsBlack = false;
                        node      = node.Parent;
                    } else {
                        if(w.Right == null || w.Right.IsBlack) {
                            if(w.Left != null)
                                w.Left.IsBlack = true;
                            w.IsBlack = false;
                            this.RotateRight(w);
                            w = node.Parent.Right;
                        }

                        w.IsBlack           = node.Parent.IsBlack;
                        node.Parent.IsBlack = true;
                        if(w.Right != null)
                            w.Right.IsBlack = true;
                        this.RotateLeft(node.Parent);
                        node = m_root;
                    }
                } else {
                    var w = node.Parent.Left;
                    if(w == null) {
                        node = node.Parent;
                        continue;
                    }

                    if(!w.IsBlack) {
                        w.IsBlack           = true;
                        node.Parent.IsBlack = false;
                        this.RotateRight(node.Parent);
                        w = node.Parent.Left;
                    }

                    if(w == null) {
                        node = node.Parent;
                        continue;
                    }

                    if((w.Right == null || w.Right.IsBlack) &&
                        (w.Left == null || w.Left.IsBlack)) {
                        w.IsBlack = false;
                        node      = node.Parent;
                    } else {
                        if(w.Left == null || w.Left.IsBlack) {
                            if(w.Right != null)
                                w.Right.IsBlack = true;
                            w.IsBlack = false;
                            this.RotateLeft(w);
                            w = node.Parent.Left;
                        }

                        w.IsBlack           = node.Parent.IsBlack;
                        node.Parent.IsBlack = true;
                        if(w.Left != null)
                            w.Left.IsBlack = true;
                        this.RotateRight(node.Parent);
                        node = m_root;
                    }
                }
            }
            node.IsBlack = true;
        }
        private static void CopyFrom(Node current, Node node) {
            if(node.Left != null)
                node.Left.Parent = current;
            current.Left = node.Left;

            if(node.Right != null)
                node.Right.Parent = current;
            current.Right = node.Right;

            if(node.Parent != null) {
                if(node.Parent.Left == node)
                    node.Parent.Left = current;
                else
                    node.Parent.Right = current;
            }

            current.IsBlack = node.IsBlack;
            current.Parent  = node.Parent;
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
        #region TryGetNode()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool TryGetNode(TKey key, out Node value) {
            var current = m_root;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
     
                if(diff < 0)
                    current = current.Left;
                else if(diff > 0)
                    current = current.Right;
                else {
                    value = current;
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
        /// </summary>
        public BinarySearchResult BinarySearch(TKey key) {
            // inline this since this is usually called in hot paths
            //return BinarySearch(key, m_comparer);

            var current   = m_root;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
 
                if(diff < 0) {
                    prev      = current;
                    prev_diff = -1;
                    current   = current.Left;
                } else if(diff > 0) {
                    prev      = current;
                    prev_diff = 1;
                    current   = current.Right;
                } else
                    return new BinarySearchResult() { Node = current, Diff = 0 };
            }
            return new BinarySearchResult() { Node = prev, Diff = prev_diff };
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    This lets you know the nearest match to your key.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed lowest_or_equal result; 
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(TKey key, Comparison<TKey> comparer) {
            var current   = m_root;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                int diff = comparer(key, current.Key);

                if(diff < 0) {
                    prev      = current;
                    prev_diff = -1;
                    current   = current.Left;
                } else if(diff > 0) {
                    prev      = current;
                    prev_diff = 1;
                    current   = current.Right;
                } else
                    return new BinarySearchResult() { Node = current, Diff = 0 };
            }
            return new BinarySearchResult() { Node = prev, Diff = prev_diff };
        }
        public ref struct BinarySearchResult {
            /// <summary>
            ///    -1: key &lt; lookup_key
            ///     0: key == lookup_key
            ///     1: key &gt; lookup_key
            /// </summary>
            public int Diff;
            public Node Node;
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
        
        #region private RotateLeft()
        ///<summary>
        ///       X           rotate            Y
        ///     /   \                         /   \
        ///    A     Y                       X     C
        ///        /   \                   /   \
        ///       B     C                 A     B
        ///<summary>
        private void RotateLeft(Node x) {
            var y   = x.Right;
            x.Right = y.Left;

            if(y.Left != null)
                y.Left.Parent = x;

            y.Parent = x.Parent;

            if(x.Parent != null) {
                if(x == x.Parent.Left)
                    x.Parent.Left  = y;
                else
                    x.Parent.Right = y;
            } else 
                m_root = y;

            y.Left   = x;
            x.Parent = y;
        }
        #endregion
        #region private RotateRight()
        ///<summary>
        ///          Y        rotate        X      
        ///        /   \                  /   \    
        ///       X     C                A     Y   
        ///     /   \                  /   \  
        ///    A     B                B     C
        ///<summary>
        private void RotateRight(Node y) {
            var x  = y.Left;
            y.Left = x.Right;

            if(x.Right != null)
                x.Right.Parent = y;

            x.Parent = y.Parent;

            if(y.Parent != null) {
                if(y == y.Parent.Left)
                    y.Parent.Left  = x;
                else
                    y.Parent.Right = x;
            } else 
                m_root = x;

            x.Right  = y;
            y.Parent = x;
        }
        #endregion
        #region private GetChildrenNodes()
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
        ///     Use ChildrenNodesEnumerator instead for efficient re-use.
        /// </summary>
        private IEnumerable<Node> GetChildrenNodes() {
            return new ChildrenNodesEnumerator().Run(m_root);
        }
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
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
        ///     O(n)
        ///     Returns keys in order.
        /// </summary>
        ICollection<TKey> IDictionary<TKey, TValue>.Keys {
            get {
                var keys = new List<TKey>(this.Count);
                foreach(var node in this.GetChildrenNodes(m_root))
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
                foreach(var node in this.GetChildrenNodes(m_root))
                    values.Add(node.Value);
 
                return values;
            }
        }
 
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
         
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() {
            foreach(var node in this.GetChildrenNodes(m_root))
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
            public TKey Key { get; internal set; }
            public TValue Value;
     
            internal Node Left;
            internal Node Right;
            internal Node Parent;
            internal bool IsBlack;
 
            #region Next()
            /// <summary>
            ///     Best:  O(1)
            ///     Worst: O(log n)
            ///     
            ///     Returns the next node.
            ///     This behaves like an iterator, but will keep working even as the tree is being changed.
            ///     This will run roughly half the speed as using the iterator if iterating through the entire tree.
            /// </summary>
            public Node Next() {
                var node = this;
 
                if(node.Right != null) {
                    node = node.Right;
                    while(node.Left != null)
                        node = node.Left;
                } else {
                    var parent = node.Parent;
                    while(parent != null && node == parent.Right) { 
                        node   = parent; 
                        parent = parent.Parent;
                    }
                    node = parent;
                }
                return node;
            }
            #endregion
            #region Previous()
            /// <summary>
            ///     Best:  O(1)
            ///     Worst: O(log n)
            ///     
            ///     Returns the previous node.
            ///     This behaves like an iterator, but will keep working even as the tree is being changed.
            ///     This will run roughly half the speed as using the iterator if iterating through the entire tree.
            /// </summary>
            public Node Previous() {
                var node = this;
                if(node.Left != null) {
                    node = node.Left;
                    while(node.Right != null)
                        node = node.Right;
                } else {
                    var parent = node.Parent;
                    while(parent != null && node == parent.Left) {
                        node   = parent; 
                        parent = parent.Parent;
                    }
                    node = parent;
                }
                return node;
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

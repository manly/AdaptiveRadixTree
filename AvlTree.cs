//#define IMPLEMENT_DICTIONARY_INTERFACES // might want to disable due to System.Linq.Enumerable extensions clutter
//#define MAINTAIN_MINIMUM_AND_MAXIMUM   // if enabled, will maintain a pointer to .Minimum and .Maximum allowing O(1)

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Specialized
{
    /// <summary>
    ///    Implements an AVL tree (Adelson-Velsky and Landis).
    ///    This is a self-balancing binary search tree that takes 2 extra bits per node over a binary search tree.
    ///    Search/Insert/Delete() run in O(log n).
    ///    This tree is optimized for lookup times. For heavy updates a RBTree is favored.
    /// </summary>
    /// <remarks>
    ///    More strictly balanced than Red-Black Trees, leading to better lookup times.
    ///    
    ///    worst case       |   AVL tree      |   RB tree
    ///    =======================================
    ///    height           | 1.44 log n      | 2 log(n + 1)
    ///    update           | log n           | log n
    ///    lookup           | log n  (faster) | log n
    ///    insert rotations | 2               | 2
    ///    delete rotations | log n           | 3
    ///    
    ///    Inserting in AVL tree may imply a rebalance. After inserting, updating the ancestors has to be done up to the root, 
    ///    or up to a point where the 2 subtrees are of equal depth. The probability of having to update n nodes is 1/3^k. 
    ///    Rebalancing is O(1). Removing an element may imply more than one rebalancing (up to half the tree depth).
    /// </remarks>
    public sealed class AvlTree<TKey, TValue> : ICollection
#if IMPLEMENT_DICTIONARY_INTERFACES
        , IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
#endif
    {
        private Node m_header; // note: root = m_header.Parent
        private readonly Comparison<TKey> m_comparer;

        public int Count { get; private set; }

        #region constructors
        public AvlTree() : this(Comparer<TKey>.Default.Compare) { }
        public AvlTree(IComparer<TKey> comparer) : this(comparer.Compare) { }
        public AvlTree(Comparison<TKey> comparer) {
            m_comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            this.Clear(); // sets m_header
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
        /// </summary>
        public TValue this[TKey key] {
            get{
                var current = m_header.Parent;
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
                var current = m_header.Parent;
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
#if !MAINTAIN_MINIMUM_AND_MAXIMUM
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public Node Minimum {
            get {
                var current = m_header.Parent;
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
#else
        /// <summary>
        ///    O(1)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public Node Minimum {
            get {
                return m_header.Left ?? throw new KeyNotFoundException();
            }
        }
#endif
        #endregion
        #region Maximum
#if !MAINTAIN_MINIMUM_AND_MAXIMUM
        /// <summary>
        ///    O(log n)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public Node Maximum {
            get {
                var current = m_header.Parent;
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
#else
        /// <summary>
        ///    O(1)
        ///    
        ///    Throws KeyNotFoundException.
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        public Node Maximum {
            get {
                return m_header.Right ?? throw new KeyNotFoundException();
            }
        }
#endif
        #endregion
    
        #region Add()
        /// <summary>
        ///     O(log n)
        ///     
        ///     Returns the added node.
        ///     
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public Node Add(TKey key, TValue value) {
            var node = m_header.Parent;
            if(node != null) {
                while(true) {
                    var diff = m_comparer(key, node.Key);

                    if(diff < 0) {
                        if(node.Left != null)
                            node = node.Left;
                        else {
                            node.Left = new Node(key, value){
                                Parent  = node,
                                Balance = State.Balanced,
                            };
#if MAINTAIN_MINIMUM_AND_MAXIMUM
                            if(m_header.Left == node)
                                m_header.Left = node.Left;
#endif
                            BalanceSet(node, Direction.Left);
                            break;
                        }
                    } else if(diff > 0) {
                        if(node.Right != null)
                            node = node.Right;
                        else {
                            node.Right = new Node(key, value){
                                Parent  = node,
                                Balance = State.Balanced,
                            };
#if MAINTAIN_MINIMUM_AND_MAXIMUM
                            if(m_header.Right == node)
                                m_header.Right = node.Right;
#endif
                            BalanceSet(node, Direction.Right);
                            break;
                        }
                    } else
                        throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
                }
            } else {
                var root = new Node(key, value) {
                    Parent  = m_header,
                    Balance = State.Balanced,
                };
                m_header.Parent = root;
                node            = root;

#if MAINTAIN_MINIMUM_AND_MAXIMUM
                m_header.Left   = root;
                m_header.Right  = root;
#endif
            }

            this.Count++;
            return node;
        }
        /// <summary>
        ///     Balance the tree by walking the tree upwards.
        /// </summary>
        private static void BalanceSet(Node node, Direction direction) {
            var is_taller = true;

            while(is_taller) {
                var parent = node.Parent;
                var next   = parent.Left == node ? Direction.Left : Direction.Right;

                if(direction == Direction.Left) {
                    switch(node.Balance) {
                        case State.LeftHigh:
                            if(parent.Balance == State.Header)
                                BalanceLeft(ref parent.Parent);
                            else if(parent.Left == node)
                                BalanceLeft(ref parent.Left);
                            else
                                BalanceLeft(ref parent.Right);
                            return;

                        case State.Balanced:
                            node.Balance = State.LeftHigh;
                            break;

                        case State.RightHigh:
                            node.Balance = State.Balanced;
                            return;
                    }
                } else {
                    switch(node.Balance) {
                        case State.LeftHigh:
                            node.Balance = State.Balanced;
                            return;

                        case State.Balanced:
                            node.Balance = State.RightHigh;
                            break;

                        case State.RightHigh:
                            if(parent.Balance == State.Header)
                                BalanceRight(ref parent.Parent);
                            else if(parent.Left == node)
                                BalanceRight(ref parent.Left);
                            else
                                BalanceRight(ref parent.Right);
                            return;
                    }
                }

                if(is_taller) {
                    if(parent.Balance == State.Header)
                        return;

                    node      = parent;
                    direction = next;
                }
            }
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
            var root = m_header.Parent;

            while(true) {
                if(root == null)
                    return false;
            
                int diff = m_comparer(key, root.Key);
            
                if(diff < 0)
                    root = root.Left;
                else if(diff > 0)
                    root = root.Right;
                else
                    return this.Remove(root);
            }
        }
        /// <summary>
        ///     Average:  O(1)
        ///     Worst:    O(log n)
        /// </summary>
        public bool Remove(Node node) {
            if(node == null)
                return false;
            
            if(node.Left != null && node.Right != null) {
                var replacement = node.Left;
                while(replacement.Right != null)
                    replacement = replacement.Right;
                SwapNodes(node, replacement);
            }
            
            var parent    = node.Parent;
            var direction = parent.Left == node ? Direction.Left : Direction.Right;

#if MAINTAIN_MINIMUM_AND_MAXIMUM
            if(m_header.Left == node) {
                var next = node.Next();

                if(next.Balance == State.Header) { 
                    m_header.Left  = m_header;
                    m_header.Right = m_header;
                } else
                    m_header.Left  = next;
            } else if(m_header.Right == node) {
                var prev = node.Previous();

                if(prev.Balance == State.Header) {
                    m_header.Left  = m_header;
                    m_header.Right = m_header;
                } else
                    m_header.Right = prev;
            }
#endif

            if(node.Left == null) {
                if(parent == m_header)
                    m_header.Parent = node.Right;
                else if(parent.Left == node)
                    parent.Left = node.Right;
                else
                    parent.Right = node.Right;
            
                if(node.Right != null)
                    node.Right.Parent = parent;
            } else {
                if(parent == m_header)
                    m_header.Parent = node.Left;
                else if(parent.Left == node)
                    parent.Left = node.Left;
                else
                    parent.Right = node.Left;
            
                if(node.Left != null)
                    node.Left.Parent = parent;
            }
            
            BalanceSetRemove(parent, direction);
            this.Count--;
            return true;
        }
        private static void SwapNodes(Node x, Node y) {
            if(x.Left == y) {
                if(y.Left != null)  y.Left.Parent  = x;
                if(y.Right != null) y.Right.Parent = x;
                if(x.Right != null) x.Right.Parent = y;

                if(x.Parent.Balance != State.Header) {
                    if(x.Parent.Left == x)
                        x.Parent.Left = y;
                    else
                        x.Parent.Right = y;
                } else
                    x.Parent.Parent = y;

                y.Parent = x.Parent;
                x.Parent = y;
                x.Left   = y.Left;
                y.Left   = x;

                Swap(ref x.Right, ref y.Right);
            } else if(x.Right == y) {
                if(y.Right != null) y.Right.Parent = x;
                if(y.Left != null)  y.Left.Parent  = x;
                if(x.Left != null)  x.Left.Parent  = y;

                if(x.Parent.Balance != State.Header) {
                    if(x.Parent.Left == x)
                        x.Parent.Left = y;
                    else
                        x.Parent.Right = y;
                } else
                    x.Parent.Parent = y;

                y.Parent = x.Parent;
                x.Parent = y;
                x.Right  = y.Right;
                y.Right  = x;

                Swap(ref x.Left, ref y.Left);
            } else if(x == y.Left) {
                if(x.Left != null)  x.Left.Parent  = y;
                if(x.Right != null) x.Right.Parent = y;
                if(y.Right != null) y.Right.Parent = x;

                if(y.Parent.Balance != State.Header) {
                    if(y.Parent.Left == y)
                        y.Parent.Left = x;
                    else
                        y.Parent.Right = x;
                } else
                    y.Parent.Parent = x;

                x.Parent = y.Parent;
                y.Parent = x;
                y.Left   = x.Left;
                x.Left   = y;

                Swap(ref x.Right, ref y.Right);
            } else if(x == y.Right) {
                if(x.Right != null) x.Right.Parent = y;
                if(x.Left != null)  x.Left.Parent  = y;
                if(y.Left != null)  y.Left.Parent  = x;

                if(y.Parent.Balance != State.Header) {
                    if(y.Parent.Left == y)
                        y.Parent.Left = x;
                    else
                        y.Parent.Right = x;
                } else
                    y.Parent.Parent = x;

                x.Parent = y.Parent;
                y.Parent = x;
                y.Right  = x.Right;
                x.Right  = y;

                Swap(ref x.Left, ref y.Left);
            } else {
                if(x.Parent == y.Parent)
                    Swap(ref x.Parent.Left, ref x.Parent.Right);
                else {
                    if(x.Parent.Balance != State.Header) {
                        if(x.Parent.Left == x)
                            x.Parent.Left = y;
                        else
                            x.Parent.Right = y;
                    } else 
                        x.Parent.Parent = y;

                    if(y.Parent.Balance != State.Header) {
                        if(y.Parent.Left == y)
                            y.Parent.Left = x;
                        else
                            y.Parent.Right = x;
                    } else
                        y.Parent.Parent = x;
                }

                if(y.Left != null)  y.Left.Parent  = x;
                if(y.Right != null) y.Right.Parent = x;
                if(x.Left != null)  x.Left.Parent  = y;
                if(x.Right != null) x.Right.Parent = y;

                Swap(ref x.Left, ref y.Left);
                Swap(ref x.Right, ref y.Right);
                Swap(ref x.Parent, ref y.Parent);
            }

            var balance = x.Balance;
            x.Balance   = y.Balance;
            y.Balance   = balance;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap(ref Node node1, ref Node node2) {
            var temp = node1;
            node1    = node2;
            node2    = temp;
        }
        /// <summary>
        ///     Balance the tree by walking the tree upwards.
        /// </summary>
        private static void BalanceSetRemove(Node node, Direction direction) {
            if(node.Balance == State.Header)
                return;

            var is_shorter = true;

            while(is_shorter) {
                var parent = node.Parent;
                var next   = parent.Left == node ? Direction.Left : Direction.Right;

                if(direction == Direction.Left) {
                    switch(node.Balance) {
                        case State.LeftHigh:
                            node.Balance = State.Balanced;
                            break;

                        case State.Balanced:
                            node.Balance = State.RightHigh;
                            return;

                        case State.RightHigh:
                            if(node.Right.Balance == State.Balanced)
                                is_shorter = false;

                            if(parent.Balance == State.Header)
                                BalanceRight(ref parent.Parent);
                            else if(parent.Left == node)
                                BalanceRight(ref parent.Left);
                            else
                                BalanceRight(ref parent.Right);
                            break;
                    }
                } else {
                    switch(node.Balance) {
                        case State.RightHigh:
                            node.Balance = State.Balanced;
                            break;

                        case State.Balanced:
                            node.Balance = State.LeftHigh;
                            return;

                        case State.LeftHigh:
                            if(node.Left.Balance == State.Balanced)
                                is_shorter = false;

                            if(parent.Balance == State.Header)
                                BalanceLeft(ref parent.Parent);
                            else if(parent.Left == node)
                                BalanceLeft(ref parent.Left);
                            else
                                BalanceLeft(ref parent.Right);
                            break;
                    }
                }

                if(is_shorter) {
                    if(parent.Balance == State.Header)
                        return;
                    
                    direction = next;
                    node      = parent;
                }
            }
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
            var header = new Node(default, default) {
                Balance = State.Header,
                Parent  = null,
            };
            header.Left  = header;
            header.Right = header;
            m_header     = header;

            this.Count = 0;
        }
        #endregion

        #region TryGetValue()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value) {
            var current = m_header.Parent;
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

            // dont want to force an extra branching
            //if(!this.TryGetNode(key, out Node node)) {
            //    value = default;
            //    return false;
            //}
            //value = node.Value;
            //return true;
        }
        #endregion
        #region TryGetNode()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool TryGetNode(TKey key, out Node node) {
            var current = m_header.Parent;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
    
                if(diff < 0)
                    current = current.Left;
                else if(diff > 0)
                    current = current.Right;
                else {
                    node = current;
                    return true;
                }
            }
    
            node = default;
            return false;
        }
        #endregion
        #region ContainsKey()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool ContainsKey(TKey key) {
            var current = m_header.Parent;
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

            var current   = m_header.Parent;
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
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(TKey key, Comparison<TKey> comparer) {
            var current   = m_header.Parent;
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
                    return new BinarySearchResult(current, 0);
            }
            return new BinarySearchResult(prev, prev_diff);
        }
        public readonly ref struct BinarySearchResult {
            /// <summary>
            ///    -1: key &lt; lookup_key
            ///     0: key == lookup_key
            ///     1: key &gt; lookup_key
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
            return DepthRecursive(m_header.Parent);
        }
        private static int DepthRecursive(Node node) {
            if(node != null)
                return Math.Max(
                    (node.Left != null ? DepthRecursive(node.Left) : 0) + 1,
                    (node.Right != null ? DepthRecursive(node.Right) : 0) + 1);
            return 0;
        }
        #endregion

        #region Range() *non-functional*
        ///// <summary>
        /////     O(2 log n + m)   m = number of items returned
        /////     
        /////     Returns all nodes between the 2 keys, including from/to.
        /////     Use RangeEnumerator instead for efficient re-use.
        ///// </summary>
        ///// <param name="start">Default: Minimum.Key.</param>
        ///// <param name="end">Default: Maximum.Key.</param>
        //public IEnumerable<Node> Range(TKey start, TKey end) {
        //    return new RangeEnumerator(this).Run(start, end);
        //}
        ///// <summary>
        /////     O(2 log n + m)   m = number of items returned
        /////     
        /////     Returns all nodes between the 2 keys, including from/to.
        /////     This enumerator is made for re-use, to avoid array reallocations.
        ///// </summary>
        //public sealed class RangeEnumerator {
        //    // manually handled stack for better performance
        //    private Node[] m_stack = new Node[16];
        //    private int m_stackIndex = 0;
        //
        //    private readonly AvlTree<TKey, TValue> m_owner;
        //
        //    public RangeEnumerator(AvlTree<TKey, TValue> owner) {
        //        m_owner = owner;
        //    }
        //
        //    /// <param name="start">Default: Minimum.Key.</param>
        //    /// <param name="end">Default: Maximum.Key.</param>
        //    public IEnumerable<Node> Run(TKey start, TKey end) {
        //        if(m_stackIndex > 0) {
        //            Array.Clear(m_stack, 0, m_stackIndex);
        //            m_stackIndex = 0;
        //        }
        //
        //        var comparer  = m_owner.m_comparer;
        //        var has_start = comparer(start, default) != 0;
        //        var has_end   = comparer(end, default) != 0;
        //        var node      = m_owner.m_header.Parent;
        //        Node end_node = null;
        //
        //        if(has_start)
        //            node = this.FindStartNode(start);
        //        if(has_end)
        //            end_node = this.FindEndNode(end);
        //
        //        // todo: code doesnt work when start != default
        //        //throw new NotImplementedException();
        //
        //        while(node != null) {
        //            if(node.Left != null) {
        //                this.Push(node);
        //                node = node.Left;
        //            } else {
        //                do {
        //                    yield return node;
        //                    if(has_end && object.ReferenceEquals(node, end_node))
        //                        yield break;
        //                    node = node.Right;
        //                } while(node == null && m_stackIndex > 0 && (node = this.Pop()) != null);
        //            }
        //        }
        //    }
        //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //    private void Push(Node value) {
        //        if(m_stackIndex == m_stack.Length)
        //            Array.Resize(ref m_stack, m_stackIndex * 2);
        //        m_stack[m_stackIndex++] = value;
        //    }
        //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //    private Node Pop() {
        //        var node = m_stack[--m_stackIndex];
        //        m_stack[m_stackIndex] = default;
        //        return node;
        //    }
        //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //    private Node FindStartNode(TKey start) {
        //        this.TryGetPath(start);
        //        var node = this.Pop();
        //        if(m_owner.m_comparer(start, node.Key) > 0)
        //            node = node.Next();
        //        return node;
        //    }
        //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //    private Node FindEndNode(TKey end) {
        //        var x    = m_owner.BinarySearch(end);
        //        var node = x.Node;
        //        if(x.Diff < 0)
        //            node = x.Node.Previous();
        //        return node;
        //    }
        //    private void TryGetPath(TKey key) {
        //        var current = m_owner.m_header.Parent;
        //        while(current != null) {
        //            this.Push(current);
        //
        //            int diff = m_owner.m_comparer(key, current.Key);
        //
        //            if(diff < 0)
        //                current = current.Left;
        //            else if(diff > 0)
        //                current = current.Right;
        //            else
        //                return;
        //        }
        //    }
        //}
        #endregion

        #region private static RotateLeft()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RotateLeft(ref Node node) {
            var right    = node.Right;
            var parent   = node.Parent;

            right.Parent = parent;
            node.Parent  = right;
            if(right.Left != null)
                right.Left.Parent = node;

            node.Right = right.Left;
            right.Left = node;
            node       = right;
        }
        #endregion
        #region private static RotateRight()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RotateRight(ref Node node) {
            var left    = node.Left;
            var parent  = node.Parent;

            left.Parent = parent;
            node.Parent = left;
            if(left.Right != null)
                left.Right.Parent = node;

            node.Left  = left.Right;
            left.Right = node;
            node       = left;
        }
        #endregion
        #region private static BalanceLeft()
        private static void BalanceLeft(ref Node node) {
            var left = node.Left;

            switch(left.Balance) {
                case State.LeftHigh:
                    left.Balance = State.Balanced;
                    node.Balance = State.Balanced;
                    RotateRight(ref node);
                    break;

                case State.RightHigh: 
                    var sub_right = left.Right;
                    switch(sub_right.Balance) {
                        case State.Balanced:
                            left.Balance = State.Balanced;
                            node.Balance = State.Balanced;
                            break;

                        case State.RightHigh:
                            left.Balance = State.LeftHigh;
                            node.Balance = State.Balanced;
                            break;

                        case State.LeftHigh:
                            left.Balance = State.Balanced;
                            node.Balance = State.RightHigh;
                            break;
                    }
                    sub_right.Balance = State.Balanced;
                    RotateLeft(ref left);
                    node.Left = left;
                    RotateRight(ref node);
                    break;

                case State.Balanced:
                    left.Balance = State.RightHigh;
                    node.Balance = State.LeftHigh;
                    RotateRight(ref node);
                    break;
            }
        }
        #endregion
        #region private static BalanceRight()
        private static void BalanceRight(ref Node node) {
            var right = node.Right;

            switch(right.Balance) {
                case State.RightHigh:
                    right.Balance = State.Balanced;
                    node.Balance  = State.Balanced;
                    RotateLeft(ref node);
                    break;

                case State.LeftHigh:
                    var sub_left = right.Left;
                    switch(sub_left.Balance) {
                        case State.Balanced:
                            right.Balance = State.Balanced;
                            node.Balance  = State.Balanced;
                            break;

                        case State.LeftHigh:
                            right.Balance = State.RightHigh;
                            node.Balance  = State.Balanced;
                            break;

                        case State.RightHigh:
                            right.Balance = State.Balanced;
                            node.Balance  = State.LeftHigh;
                            break;
                    }
                    sub_left.Balance = State.Balanced;
                    RotateRight(ref right);
                    node.Right = right;
                    RotateLeft(ref node);
                    break;

                case State.Balanced:
                    right.Balance = State.LeftHigh;
                    node.Balance  = State.RightHigh;
                    RotateLeft(ref node);
                    break;
            }
        }
        #endregion

        #region private GetChildrenNodes()
        /// <summary>
        ///     O(n)
        ///     Returns items in key order.
        ///     Use ChildrenNodesEnumerator instead for efficient re-use.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IEnumerable<Node> GetChildrenNodes() {
            return new ChildrenNodesEnumerator().Run(m_header.Parent);
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void Push(Node value) {
                if(m_stackIndex == m_stack.Length)
                    Array.Resize(ref m_stack, m_stackIndex * 2);
                m_stack[m_stackIndex++] = value;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Node Pop() {
                var node = m_stack[--m_stackIndex];
                m_stack[m_stackIndex] = default;
                return node;
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
        ///     O(log n)
        ///     
        ///     Returns the added node.
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


        private enum Direction {
            Left, 
            Right
        }
        internal enum State : byte { 
            Balanced, 
            Header, 
            LeftHigh, 
            RightHigh 
        }

        public sealed class Node {
            internal Node  Left;
            internal Node  Right;
            internal Node  Parent;
            internal State Balance = State.Balanced;

            public TKey Key { get; private set; }
            public TValue Value;

            #region Next()
            /// <summary>
            ///     O(1)
            ///     Returns the next node.
            ///     This behaves like an iterator, but will keep working even as the tree is being changed.
            ///     This will run roughly half the speed as using the iterator if iterating through the entire tree.
            /// </summary>
            public Node Next() {
                var node = this;
                if(node.Balance == State.Header)
                    return node.Left;

                if(node.Right != null) {
                    node = node.Right;
                    while(node.Left != null)
                        node = node.Left;
                } else {
                    var parent = node.Parent;
                    if(parent.Balance == State.Header)
                        return parent;
                    while(node == parent.Right) { 
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
            ///     O(1)
            ///     Returns the previous node.
            ///     This behaves like an iterator, but will keep working even as the tree is being changed.
            ///     This will run roughly half the speed as using the iterator if iterating through the entire tree.
            /// </summary>
            public Node Previous() {
                var node = this;
                if(node.Balance == State.Header) 
                    return node.Right;

                if(node.Left != null) {
                    node = node.Left;
                    while(node.Right != null)
                        node = node.Right;
                } else {
                    var parent = node.Parent;
                    if(parent.Balance == State.Header)
                        return parent;
                    while(node == parent.Left) {
                        node   = parent; 
                        parent = parent.Parent;
                    }
                    node = parent;
                }
                return node;
            }
            #endregion
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
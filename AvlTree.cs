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
    ///    Despite many claims to the contrary, practical tests show much better performance from this over Red-Black Trees.
    /// </summary>
    /// <remarks>
    ///    More strictly balanced than Red-Black Trees, leading to better lookup times.
    ///    Typically, AvlTrees are wrongly considered slower because they enforce stricter balance, or because they require more balancing operations.
    ///    Empyrical testing shows that number_of_rotations is a poor measure of performance, as it yields little difference.
    ///    Likewise, maintaining an additional parent pointer should be a lot slower than an implementation without one, yet, the performance impact
    ///    is negligible.
    ///    So the overall benefit from more strictly enforced tree height results in more overall benefits than the cost of the rotations, 
    ///    making AvlTree better suited for general use than Red-Black Trees. The only difference is the one extra bit required, 
    ///    but .NET memory alignments prevent that bit saving to come into effect anyway.
    ///    
    ///    worst case       |   AVL tree      |   RB tree
    ///    =======================================
    ///    height           | 1.44 log n      | 2 log(n + 1)
    ///    update           | log n           | log n
    ///    lookup           | log n  (faster) | log n
    ///    insert rotations | 2               | 2               (very poor measure of performance)
    ///    delete rotations | log n           | 3               (very poor measure of performance)
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
        public TValue this[in TKey key] {
            get{
                var current = m_header.Parent;
                while(current != null) {
                    int diff = m_comparer(key, current.Key);
     
                    if(diff > 0)
                        current = current.Right;
                    else if(diff < 0)
                        current = current.Left;
                    else
                        return current.Value;
                }
     
                throw new KeyNotFoundException();
            }
            set {
                var current = m_header.Parent;
                while(current != null) {
                    int diff = m_comparer(key, current.Key);
     
                    if(diff > 0)
                        current = current.Right;
                    else if(diff < 0)
                        current = current.Left;
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
        public Node Add(in TKey key, TValue value) {
            var node = m_header.Parent;
            if(node != null) {
                while(true) {
                    var diff = m_comparer(key, node.Key);
 
                    if(diff > 0) {
                        if(node.Right != null)
                            node = node.Right;
                        else {
                            node = CreateRightNodeRare(key, value, node);
                            break;
                        }
                    } else if(diff < 0) {
                        if(node.Left != null)
                            node = node.Left;
                        else {
                            node = CreateLeftNodeRare(key, value, node);
                            break;
                        }
                    } else
                        throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
                }
            } else
                node = this.CreateRootNodeRare(key, value);

            this.Count++;
            return node;
        }

        private static Node CreateLeftNodeRare(in TKey key, TValue value, Node parent) {
            var _new = new Node(key, value){
                Parent  = parent,
                Balance = State.Balanced,
            };
            parent.Left = _new;
#if MAINTAIN_MINIMUM_AND_MAXIMUM
            if(m_header.Left == parent)
                m_header.Left = _new;
#endif
            BalanceSet(parent, Direction.Left);
            return _new;
        }
        private static Node CreateRightNodeRare(in TKey key, TValue value, Node parent) {
            var _new = new Node(key, value){
                Parent  = parent,
                Balance = State.Balanced,
            };
            parent.Right = _new;
#if MAINTAIN_MINIMUM_AND_MAXIMUM
            if(m_header.Right == parent)
                m_header.Right = _new;
#endif
            BalanceSet(parent, Direction.Right);
            return _new;
        }
        private Node CreateRootNodeRare(in TKey key, TValue value) {
            var root = new Node(key, value) {
                Parent  = m_header,
                Balance = State.Balanced,
            };
            m_header.Parent = root;

#if MAINTAIN_MINIMUM_AND_MAXIMUM
            m_header.Left   = root;
            m_header.Right  = root;
#endif
            return root;
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
        public bool Remove(in TKey key) {
            var root = m_header.Parent;
 
            while(true) {
                if(root == null)
                    return false;
             
                int diff = m_comparer(key, root.Key);
             
                if(diff > 0)
                    root = root.Right;
                else if(diff < 0)
                    root = root.Left;
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
        public bool TryGetValue(in TKey key, out TValue value) {
            var current = m_header.Parent;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
     
                if(diff > 0)
                    current = current.Right;
                else if(diff < 0)
                    current = current.Left;
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
        public bool TryGetNode(in TKey key, out Node node) {
            var current = m_header.Parent;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
     
                if(diff > 0)
                    current = current.Right;
                else if(diff < 0)
                    current = current.Left;
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
        public bool ContainsKey(in TKey key) {
            var current = m_header.Parent;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
     
                if(diff > 0)
                    current = current.Right;
                else if(diff < 0)
                    current = current.Left;
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
        ///    Search the nearest match to your key.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearch(in TKey key) {
            // inline this since this is usually called in hot paths
            //return this.BinarySearch(key, m_comparer);
 
            var current   = m_header.Parent;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                prev_diff = m_comparer(key, current.Key);
 
                if(prev_diff > 0) {
                    prev    = current;
                    current = current.Right;
                } else if(prev_diff < 0) {
                    prev    = current;
                    current = current.Left;
                } else
                    return new BinarySearchResult(current, 0);
            }
            return new BinarySearchResult(prev, prev_diff);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match to your key.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(in TKey key, Comparison<TKey> comparer) {
            var current   = m_header.Parent;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                prev_diff = comparer(key, current.Key);
 
                if(prev_diff > 0) {
                    prev    = current;
                    current = current.Right;
                } else if(prev_diff < 0) {
                    prev    = current;
                    current = current.Left;
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
        public readonly struct BinarySearchResult_Storeable {
            /// <summary>
            ///    -1: key &lt; node.key
            ///     0: key ==   node.key
            ///     1: key &gt; node.key
            /// </summary>
            public readonly int Diff;
            public readonly Node Node;
            public BinarySearchResult_Storeable(Node node, int diff) : this() {
                this.Node = node;
                this.Diff = diff;
            }
            public BinarySearchResult_Storeable(BinarySearchResult bsr) : this(bsr.Node, bsr.Diff) { }
            public static implicit operator BinarySearchResult_Storeable(BinarySearchResult value) {
                return new BinarySearchResult_Storeable(value);
            }
            public static implicit operator BinarySearchResult(BinarySearchResult_Storeable value) {
                return new BinarySearchResult(value.Node, value.Diff);
            }
        }
        #endregion
        #region BinarySearch_GreaterOrEqualTo()
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is greater or equal to key.
        ///    
        ///    Returns "1 diff" if not found.
        /// </summary>
        public BinarySearchResult BinarySearch_GreaterOrEqualTo(in TKey key) {
            // inline this since this is usually called in hot paths
            //return this.BinarySearch_GreaterOrEqualTo(key, m_comparer);

            // this is basically an inlined version of AvlTree + node.Next() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff <= 0) return bsr;
            //     var node = bsr.Node.Next();
            //     if(m_comparer(key, node.Key) < 0) return new BinarySearchResult(node, -1);
            //     return new BinarySearchResult(null, 1); // not found

            var current           = m_header.Parent;
            var prev              = current;
            var prev_diff         = 0;
            var last_greater_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = m_comparer(key, current.Key);
 
                if(prev_diff > 0)
                    current = current.Right;
                else if(prev_diff < 0) {
                    last_greater_than = current;
                    current           = current.Left;
                } else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff < 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are smaller: last_greater_than == null
                return new BinarySearchResult(last_greater_than, last_greater_than != null ? -1 : 1);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is greater or equal to key.
        ///    
        ///    Returns "1 diff" if not found.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch_GreaterOrEqualTo(in TKey key, Comparison<TKey> comparer) {
            // this is basically an inlined version of AvlTree + node.Next() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff <= 0) return bsr;
            //     var node = bsr.Node.Next();
            //     if(comparer(key, node.Key) < 0) return new BinarySearchResult(node, -1);
            //     return new BinarySearchResult(null, 1); // not found

            var current           = m_header.Parent;
            var prev              = current;
            var prev_diff         = 0;
            var last_greater_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = comparer(key, current.Key);
 
                if(prev_diff > 0)
                    current = current.Right;
                else if(prev_diff < 0) {
                    last_greater_than = current;
                    current           = current.Left;
                } else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff < 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are smaller: last_greater_than == null
                return new BinarySearchResult(last_greater_than, last_greater_than != null ? -1 : 1);
        }
        #endregion
        #region BinarySearch_LesserOrEqualTo()
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is lesser or equal to key.
        ///    
        ///    Returns "-1 diff" if not found.
        /// </summary>
        public BinarySearchResult BinarySearch_LesserOrEqualTo(in TKey key) {
            // inline this since this is usually called in hot paths
            //return this.BinarySearch_LesserOrEqualTo(key, m_comparer);

            // this is basically an inlined version of AvlTree + node.Previous() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff >= 0) return bsr;
            //     var node = bsr.Node.Previous();
            //     if(m_comparer(key, node.Key) > 0) return new BinarySearchResult(node, 1);
            //     return new BinarySearchResult(null, -1); // not found
 
            var current          = m_header.Parent;
            var prev             = current;
            var prev_diff        = 0;
            var last_lesser_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = m_comparer(key, current.Key);

                if(prev_diff > 0) {
                    last_lesser_than = current;
                    current          = current.Right;
                } else if(prev_diff < 0)
                    current = current.Left;
                else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff > 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are bigger: last_lesser_than == null
                return new BinarySearchResult(last_lesser_than, last_lesser_than != null ? 1 : -1);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is lesser or equal to key.
        ///    
        ///    Returns "-1 diff" if not found.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch_LesserOrEqualTo(in TKey key, Comparison<TKey> comparer) {
            // this is basically an inlined version of AvlTree + node.Previous() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff >= 0) return bsr;
            //     var node = bsr.Node.Previous();
            //     if(m_comparer(key, node.Key) > 0) return new BinarySearchResult(node, 1);
            //     return new BinarySearchResult(null, -1); // not found
 
            var current          = m_header.Parent;
            var prev             = current;
            var prev_diff        = 0;
            var last_lesser_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = comparer(key, current.Key);

                if(prev_diff > 0) {
                    last_lesser_than = current;
                    current          = current.Right;
                } else if(prev_diff < 0)
                    current = current.Left;
                else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff > 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are bigger: last_lesser_than == null
                return new BinarySearchResult(last_lesser_than, last_lesser_than != null ? 1 : -1);
        }
        #endregion
        #region BinarySearchNearby()
        /// <summary>
        ///    Worst: O(2 log n)
        ///    
        ///    Search the nearest match to your key, starting from a given node.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearchNearby(Node start, in TKey key) {
            return this.BinarySearchNearby(start, key, m_comparer);
        }
        /// <summary>
        ///    Worst: O(2 log n)
        ///    
        ///    Search the nearest match to your key, starting from a given node.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearchNearby(Node start, in TKey key, Comparison<TKey> comparer) {
            var node = start;
            var prev = start;
 
            if(start != null) {
                // go up until we cross key, then go down
                var diff = comparer(key, start.Key);
 
                if(diff > 0) {
                    while(node != null) {
                        var parent = node.Parent;
                        if(parent.Balance != State.Header) {
                            if(node == parent.Left) {
                                diff = comparer(key, parent.Key);
                                if(diff < 0) {
                                    // go down from here
                                    node = parent;
                                    break;
                                } else if(diff == 0)
                                    return new BinarySearchResult(parent, 0);
                            }
                            node = parent;
                        } else
                            return this.BinarySearch(key);
                    }
                } else if(diff < 0) {
                    while(node != null) {
                        var parent = node.Parent;
                        if(parent.Balance != State.Header) {
                            if(node == parent.Right) {
                                diff = comparer(key, parent.Key);
                                if(diff > 0) {
                                    // go down from here
                                    node = parent;
                                    break;
                                } else if(diff == 0)
                                    return new BinarySearchResult(parent, 0);
                            }
                            node = parent;
                        } else
                            return this.BinarySearch(key);
                    }
                } else
                    return new BinarySearchResult(node, 0);
            } else {
                node = m_header.Parent;
                prev = node;
            }
             
            // then go down as normal
            int prev_diff = 0;
            while(node != null) {
                prev_diff = comparer(key, node.Key);
 
                if(prev_diff > 0) {
                    prev = node;
                    node = node.Right;
                } else if(prev_diff < 0) {
                    prev = node;
                    node = node.Left;
                } else
                    return new BinarySearchResult(node, 0);
            }
 
            return new BinarySearchResult(prev, prev_diff);
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
        #region Range()
        /// <summary>
        ///     O(2 log n + m)   m = number of items returned
        ///     
        ///     Returns all nodes between the 2 keys.
        ///     Use RangeEnumerator instead for efficient re-use.
        /// </summary>
        public IEnumerable<Node> Range(in TKey start, in TKey end, bool include_start = true, bool include_end = true) {
            return new RangeEnumerator(this).Run(start, end, include_start, include_end);
        }
        /// <summary>
        ///     O(2 log n + m)   m = number of items returned
        ///     
        ///     Returns all nodes between the 2 keys.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        /// </summary>
        public sealed class RangeEnumerator {
            // manually handled stack for better performance
            private Node[] m_stack = new Node[16];
            private int m_stackIndex = 0;
         
            private readonly AvlTree<TKey, TValue> m_owner;
         
            public RangeEnumerator(AvlTree<TKey, TValue> owner) {
                m_owner = owner;
            }
         
            public IEnumerable<Node> Run(TKey start, TKey end, bool include_start = true, bool include_end = true) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }
         
                var start_path = new HashSet<Node>();
                var node       = this.FindStartNode(start, start_path, include_start);
                var end_node   = this.FindEndNode(end, include_end);
         
                while(node != null) {
                    if(node.Left != null && !start_path.Contains(node)) {
                        this.Push(node);
                        node = node.Left;
                    } else {
                        do {
                            yield return node;
                            if(object.ReferenceEquals(node, end_node))
                                yield break;
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Node FindStartNode(in TKey start, HashSet<Node> path, bool include_start) {
                var node = this.TryGetPath(start, path);
                var diff = m_owner.m_comparer(start, node.Key);
                if(diff > 0 || (!include_start && diff == 0))
                    node = node.Next();
                return node;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Node FindEndNode(in TKey end, bool include_end) {
                var x    = m_owner.BinarySearch(end);
                var node = x.Node;
                if(x.Diff < 0 || (!include_end && x.Diff == 0))
                    node = node.Previous();
                return node;
            }
            private Node TryGetPath(in TKey key, HashSet<Node> path) {
                var current = m_owner.m_header.Parent;
                var prev    = current;
                while(current != null) {
                    path.Add(current);
                    prev = current;
         
                    int diff = m_owner.m_comparer(key, current.Key);

                    if(diff > 0)
                        current = current.Right;
                    else if(diff < 0) {
                        this.Push(current);
                        current = current.Left;
                    } else
                        return current;
                }
                return prev;
            }
        }
        #endregion
        #region AvlTree<string, TValue>.StartsWith()
        ///// <summary>
        /////     O(log n + m)   m = number of items returned
        /////     
        /////     Use StartsWithEnumerator instead for efficient re-use.
        ///// </summary>
        //internal static IEnumerable<AvlTree<string, TValue>.Node> StartsWith(AvlTree<string, TValue> tree, string key) {
        //    return new StartsWithEnumerator(tree).Run(key);
        //}
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        ///     
        ///     This enumerator is made for re-use, to avoid array reallocations.
        /// </summary>
        public sealed class StartsWithEnumerator {
            // manually handled stack for better performance
            private AvlTree<string, TValue>.Node[] m_stack = new AvlTree<string, TValue>.Node[16];
            private int m_stackIndex = 0;
         
            private readonly AvlTree<string, TValue> m_owner;
         
            public StartsWithEnumerator(AvlTree<string, TValue> owner) {
                m_owner = owner;
            }
         
            public IEnumerable<AvlTree<string, TValue>.Node> Run(string key) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }
         
                var start_path = new HashSet<AvlTree<string, TValue>.Node>();
                var node       = this.FindStartNode(key, start_path);
         
                while(node != null) {
                    if(node.Left != null && !start_path.Contains(node)) {
                        this.Push(node);
                        node = node.Left;
                    } else {
                        do {
                            if(node.Key.Length < key.Length || string.CompareOrdinal(node.Key, 0, key, 0, key.Length) != 0)
                                yield break;

                            yield return node;
                                
                            node = node.Right;
                        } while(node == null && m_stackIndex > 0 && (node = this.Pop()) != null);
                    }
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void Push(AvlTree<string, TValue>.Node value) {
                if(m_stackIndex == m_stack.Length)
                    Array.Resize(ref m_stack, m_stackIndex * 2);
                m_stack[m_stackIndex++] = value;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private AvlTree<string, TValue>.Node Pop() {
                var node = m_stack[--m_stackIndex];
                m_stack[m_stackIndex] = default;
                return node;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private AvlTree<string, TValue>.Node FindStartNode(string key, HashSet<AvlTree<string, TValue>.Node> path) {
                var node = this.TryGetPath(key, path);
                var diff = m_owner.m_comparer(key, node.Key);
                if(diff > 0)
                    node = node.Next();
                return node;
            }
            private AvlTree<string, TValue>.Node TryGetPath(string key, HashSet<AvlTree<string, TValue>.Node> path) {
                var current = m_owner.m_header.Parent;
                var prev    = current;
                while(current != null) {
                    path.Add(current);
                    prev = current;
         
                    int diff = m_owner.m_comparer(key, current.Key);

                    if(diff > 0)
                        current = current.Right;
                    else if(diff < 0) {
                        this.Push(current);
                        current = current.Left;
                    } else
                        return current;
                }
                return prev;
            }
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
        ///     Throws ArgumentException() on duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        void IDictionary<TKey, TValue>.Add(in TKey key, TValue value) {
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
 
        private enum Direction : byte {
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
                System.Diagnostics.Debug.Assert(node.Balance != State.Header);
 
                if(node.Right != null) {
                    node     = node.Right;
                    // avoid reading redundant node.Left if possible
                    var left = node.Left;
                    while(left != null) {
                        node = left;
                        left = node.Left;
                    }
                } else {
                    var parent = node.Parent;
                    while(node == parent.Right) { 
                        node   = parent; 
                        parent = parent.Parent;
                    }
                    if(parent.Balance == State.Header)
                        return null;
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
                System.Diagnostics.Debug.Assert(node.Balance != State.Header);
 
                if(node.Left != null) {
                    node = node.Left;
                    // avoid reading redundant node.Left if possible
                    var right = node.Right;
                    while(right != null) {
                        node  = right;
                        right = node.Right;
                    }
                } else {
                    var parent = node.Parent;
                    while(node == parent.Left) {
                        node   = parent; 
                        parent = parent.Parent;
                    }
                    if(parent.Balance == State.Header)
                        return null;
                    node = parent;
                }
                return node;
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
                this.Key = key;
            }
            #endregion
 
            #region constructors
            public Node(in TKey key, TValue value) {
                this.Key   = key;
                this.Value = value;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                if(this.Balance == State.Header)
                    return "{header}";
                return string.Format("[{0}] {1}", this.Key, this.Value);
            }
            #endregion
        }
    }


    /// <summary>
    ///    Implements an AVL tree (Adelson-Velsky and Landis).
    ///    This is a self-balancing binary search tree that takes 2 extra bits per node over a binary search tree.
    ///    Search/Insert/Delete() run in O(log n).
    ///    Despite many claims to the contrary, practical tests show much better performance from this over Red-Black Trees.
    /// </summary>
    /// <remarks>
    ///    More strictly balanced than Red-Black Trees, leading to better lookup times.
    ///    Typically, AvlTrees are wrongly considered slower because they enforce stricter balance, or because they require more balancing operations.
    ///    Empyrical testing shows that number_of_rotations is a poor measure of performance, as it yields little difference.
    ///    Likewise, maintaining an additional parent pointer should be a lot slower than an implementation without one, yet, the performance impact
    ///    is negligible.
    ///    So the overall benefit from more strictly enforced tree height results in more overall benefits than the cost of the rotations, 
    ///    making AvlTree better suited for general use than Red-Black Trees. The only difference is the one extra bit required, 
    ///    but .NET memory alignments prevent that bit saving to come into effect anyway.
    ///    
    ///    worst case       |   AVL tree      |   RB tree
    ///    =======================================
    ///    height           | 1.44 log n      | 2 log(n + 1)
    ///    update           | log n           | log n
    ///    lookup           | log n  (faster) | log n
    ///    insert rotations | 2               | 2               (very poor measure of performance)
    ///    delete rotations | log n           | 3               (very poor measure of performance)
    /// </remarks>
    public sealed class AvlTree<TKey> : ICollection {
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
        #region Items
        /// <summary>
        ///     O(n)
        ///     Returns items in key order.
        /// </summary>
        public IEnumerable<Node> Items => this.GetChildrenNodes();
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
        public Node Add(in TKey key) {
            var node = m_header.Parent;
            if(node != null) {
                while(true) {
                    var diff = m_comparer(key, node.Key);
 
                    if(diff > 0) {
                        if(node.Right != null)
                            node = node.Right;
                        else {
                            node = CreateRightNodeRare(key, node);
                            break;
                        }
                    } else if(diff < 0) {
                        if(node.Left != null)
                            node = node.Left;
                        else {
                            node = CreateLeftNodeRare(key, node);
                            break;
                        }
                    } else
                        throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
                }
            } else
                node = this.CreateRootNodeRare(key);

            this.Count++;
            return node;
        }

        private static Node CreateLeftNodeRare(in TKey key, Node parent) {
            var _new = new Node(key){
                Parent  = parent,
                Balance = State.Balanced,
            };
            parent.Left = _new;
#if MAINTAIN_MINIMUM_AND_MAXIMUM
            if(m_header.Left == parent)
                m_header.Left = _new;
#endif
            BalanceSet(parent, Direction.Left);
            return _new;
        }
        private static Node CreateRightNodeRare(in TKey key, Node parent) {
            var _new = new Node(key){
                Parent  = parent,
                Balance = State.Balanced,
            };
            parent.Right = _new;
#if MAINTAIN_MINIMUM_AND_MAXIMUM
            if(m_header.Right == parent)
                m_header.Right = _new;
#endif
            BalanceSet(parent, Direction.Right);
            return _new;
        }
        private Node CreateRootNodeRare(in TKey key) {
            var root = new Node(key) {
                Parent  = m_header,
                Balance = State.Balanced,
            };
            m_header.Parent = root;

#if MAINTAIN_MINIMUM_AND_MAXIMUM
            m_header.Left   = root;
            m_header.Right  = root;
#endif
            return root;
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
        public void AddRange(IEnumerable<TKey> keys) {
            foreach(var key in keys)
                this.Add(key);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     O(log n)
        /// </summary>
        public bool Remove(in TKey key) {
            var root = m_header.Parent;
 
            while(true) {
                if(root == null)
                    return false;
             
                int diff = m_comparer(key, root.Key);
             
                if(diff > 0)
                    root = root.Right;
                else if(diff < 0)
                    root = root.Left;
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
            var header = new Node(default) {
                Balance = State.Header,
                Parent  = null,
            };
            header.Left  = header;
            header.Right = header;
            m_header     = header;
 
            this.Count = 0;
        }
        #endregion
 
        #region TryGetNode()
        /// <summary>
        ///    O(log n)
        /// </summary>
        public bool TryGetNode(in TKey key, out Node node) {
            var current = m_header.Parent;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
     
                if(diff > 0)
                    current = current.Right;
                else if(diff < 0)
                    current = current.Left;
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
        public bool ContainsKey(in TKey key) {
            var current = m_header.Parent;
            while(current != null) {
                int diff = m_comparer(key, current.Key);
     
                if(diff > 0)
                    current = current.Right;
                else if(diff < 0)
                    current = current.Left;
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
        ///    Search the nearest match to your key.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearch(in TKey key) {
            // inline this since this is usually called in hot paths
            //return this.BinarySearch(key, m_comparer);
 
            var current   = m_header.Parent;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                prev_diff = m_comparer(key, current.Key);
 
                if(prev_diff > 0) {
                    prev    = current;
                    current = current.Right;
                } else if(prev_diff < 0) {
                    prev    = current;
                    current = current.Left;
                } else
                    return new BinarySearchResult(current, 0);
            }
            return new BinarySearchResult(prev, prev_diff);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match to your key.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch(in TKey key, Comparison<TKey> comparer) {
            var current   = m_header.Parent;
            var prev      = current;
            var prev_diff = 0;
            while(current != null) {
                prev_diff = comparer(key, current.Key);
 
                if(prev_diff > 0) {
                    prev    = current;
                    current = current.Right;
                } else if(prev_diff < 0) {
                    prev    = current;
                    current = current.Left;
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
        public readonly struct BinarySearchResult_Storeable {
            /// <summary>
            ///    -1: key &lt; node.key
            ///     0: key ==   node.key
            ///     1: key &gt; node.key
            /// </summary>
            public readonly int Diff;
            public readonly Node Node;
            public BinarySearchResult_Storeable(Node node, int diff) : this() {
                this.Node = node;
                this.Diff = diff;
            }
            public BinarySearchResult_Storeable(BinarySearchResult bsr) : this(bsr.Node, bsr.Diff) { }
            public static implicit operator BinarySearchResult_Storeable(BinarySearchResult value) {
                return new BinarySearchResult_Storeable(value);
            }
            public static implicit operator BinarySearchResult(BinarySearchResult_Storeable value) {
                return new BinarySearchResult(value.Node, value.Diff);
            }
        }
        #endregion
        #region BinarySearch_GreaterOrEqualTo()
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is greater or equal to key.
        ///    
        ///    Returns "1 diff" if not found.
        /// </summary>
        public BinarySearchResult BinarySearch_GreaterOrEqualTo(in TKey key) {
            // inline this since this is usually called in hot paths
            //return this.BinarySearch_GreaterOrEqualTo(key, m_comparer);

            // this is basically an inlined version of AvlTree + node.Next() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff <= 0) return bsr;
            //     var node = bsr.Node.Next();
            //     if(m_comparer(key, node.Key) < 0) return new BinarySearchResult(node, -1);
            //     return new BinarySearchResult(null, 1); // not found

            var current           = m_header.Parent;
            var prev              = current;
            var prev_diff         = 0;
            var last_greater_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = m_comparer(key, current.Key);
 
                if(prev_diff > 0)
                    current = current.Right;
                else if(prev_diff < 0) {
                    last_greater_than = current;
                    current           = current.Left;
                } else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff < 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are smaller: last_greater_than == null
                return new BinarySearchResult(last_greater_than, last_greater_than != null ? -1 : 1);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is greater or equal to key.
        ///    
        ///    Returns "1 diff" if not found.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch_GreaterOrEqualTo(in TKey key, Comparison<TKey> comparer) {
            // this is basically an inlined version of AvlTree + node.Next() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff <= 0) return bsr;
            //     var node = bsr.Node.Next();
            //     if(comparer(key, node.Key) < 0) return new BinarySearchResult(node, -1);
            //     return new BinarySearchResult(null, 1); // not found

            var current           = m_header.Parent;
            var prev              = current;
            var prev_diff         = 0;
            var last_greater_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = comparer(key, current.Key);
 
                if(prev_diff > 0)
                    current = current.Right;
                else if(prev_diff < 0) {
                    last_greater_than = current;
                    current           = current.Left;
                } else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff < 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are smaller: last_greater_than == null
                return new BinarySearchResult(last_greater_than, last_greater_than != null ? -1 : 1);
        }
        #endregion
        #region BinarySearch_LesserOrEqualTo()
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is lesser or equal to key.
        ///    
        ///    Returns "-1 diff" if not found.
        /// </summary>
        public BinarySearchResult BinarySearch_LesserOrEqualTo(in TKey key) {
            // inline this since this is usually called in hot paths
            //return this.BinarySearch_LesserOrEqualTo(key, m_comparer);

            // this is basically an inlined version of AvlTree + node.Previous() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff >= 0) return bsr;
            //     var node = bsr.Node.Previous();
            //     if(m_comparer(key, node.Key) > 0) return new BinarySearchResult(node, 1);
            //     return new BinarySearchResult(null, -1); // not found
 
            var current          = m_header.Parent;
            var prev             = current;
            var prev_diff        = 0;
            var last_lesser_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = m_comparer(key, current.Key);

                if(prev_diff > 0) {
                    last_lesser_than = current;
                    current          = current.Right;
                } else if(prev_diff < 0)
                    current = current.Left;
                else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff > 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are bigger: last_lesser_than == null
                return new BinarySearchResult(last_lesser_than, last_lesser_than != null ? 1 : -1);
        }
        /// <summary>
        ///    O(log n)
        ///    
        ///    Search the nearest match that is lesser or equal to key.
        ///    
        ///    Returns "-1 diff" if not found.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearch_LesserOrEqualTo(in TKey key, Comparison<TKey> comparer) {
            // this is basically an inlined version of AvlTree + node.Previous() to avoid re-reads
            // code intent:
            //     var bsr = this.BinarySearch(key);
            //     if(bsr.Diff >= 0) return bsr;
            //     var node = bsr.Node.Previous();
            //     if(m_comparer(key, node.Key) > 0) return new BinarySearchResult(node, 1);
            //     return new BinarySearchResult(null, -1); // not found
 
            var current          = m_header.Parent;
            var prev             = current;
            var prev_diff        = 0;
            var last_lesser_than = (Node)null;

            while(current != null) {
                prev      = current;
                prev_diff = comparer(key, current.Key);

                if(prev_diff > 0) {
                    last_lesser_than = current;
                    current          = current.Right;
                } else if(prev_diff < 0)
                    current = current.Left;
                else
                    return new BinarySearchResult(current, 0);
            }

            if(prev_diff > 0) // dont do == 0 because this would cover .Count==0 case
                return new BinarySearchResult(prev, prev_diff);
            else
                // if all stored values are bigger: last_lesser_than == null
                return new BinarySearchResult(last_lesser_than, last_lesser_than != null ? 1 : -1);
        }
        #endregion
        #region BinarySearchNearby()
        /// <summary>
        ///    Worst: O(2 log n)
        ///    
        ///    Search the nearest match to your key, starting from a given node.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        public BinarySearchResult BinarySearchNearby(Node start, in TKey key) {
            return this.BinarySearchNearby(start, key, m_comparer);
        }
        /// <summary>
        ///    Worst: O(2 log n)
        ///    
        ///    Search the nearest match to your key, starting from a given node.
        ///    This isn't like a Array.BinarySearch() returning a guaranteed greater_or_equal result.
        ///    you have to manually check the diff and use result.Node.Next()/Previous().
        ///    
        ///    This method is mostly meant for tree traversal of nearby items on deep trees.
        ///    If the items are not nearby, you could get 2x the performance just calling BinarySearch().
        ///    
        ///    Returns {null, 0} if this.Count==0.
        /// </summary>
        /// <param name="comparer">Custom comparer. This can be used for various speed optimisation tricks comparing only some values out of everything normally compared.</param>
        public BinarySearchResult BinarySearchNearby(Node start, in TKey key, Comparison<TKey> comparer) {
            var node = start;
            var prev = start;
 
            if(start != null) {
                // go up until we cross key, then go down
                var diff = comparer(key, start.Key);
 
                if(diff > 0) {
                    while(node != null) {
                        var parent = node.Parent;
                        if(parent.Balance != State.Header) {
                            if(node == parent.Left) {
                                diff = comparer(key, parent.Key);
                                if(diff < 0) {
                                    // go down from here
                                    node = parent;
                                    break;
                                } else if(diff == 0)
                                    return new BinarySearchResult(parent, 0);
                            }
                            node = parent;
                        } else
                            return this.BinarySearch(key);
                    }
                } else if(diff < 0) {
                    while(node != null) {
                        var parent = node.Parent;
                        if(parent.Balance != State.Header) {
                            if(node == parent.Right) {
                                diff = comparer(key, parent.Key);
                                if(diff > 0) {
                                    // go down from here
                                    node = parent;
                                    break;
                                } else if(diff == 0)
                                    return new BinarySearchResult(parent, 0);
                            }
                            node = parent;
                        } else
                            return this.BinarySearch(key);
                    }
                } else
                    return new BinarySearchResult(node, 0);
            } else {
                node = m_header.Parent;
                prev = node;
            }
             
            // then go down as normal
            int prev_diff = 0;
            while(node != null) {
                prev_diff = comparer(key, node.Key);
 
                if(prev_diff > 0) {
                    prev = node;
                    node = node.Right;
                } else if(prev_diff < 0) {
                    prev = node;
                    node = node.Left;
                } else
                    return new BinarySearchResult(node, 0);
            }
 
            return new BinarySearchResult(prev, prev_diff);
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
        #region Range()
        /// <summary>
        ///     O(2 log n + m)   m = number of items returned
        ///     
        ///     Returns all nodes between the 2 keys.
        ///     Use RangeEnumerator instead for efficient re-use.
        /// </summary>
        public IEnumerable<Node> Range(in TKey start, in TKey end, bool include_start = true, bool include_end = true) {
            return new RangeEnumerator(this).Run(start, end, include_start, include_end);
        }
        /// <summary>
        ///     O(2 log n + m)   m = number of items returned
        ///     
        ///     Returns all nodes between the 2 keys.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        /// </summary>
        public sealed class RangeEnumerator {
            // manually handled stack for better performance
            private Node[] m_stack = new Node[16];
            private int m_stackIndex = 0;
         
            private readonly AvlTree<TKey> m_owner;
         
            public RangeEnumerator(AvlTree<TKey> owner) {
                m_owner = owner;
            }
         
            public IEnumerable<Node> Run(TKey start, TKey end, bool include_start = true, bool include_end = true) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }
         
                var start_path = new HashSet<Node>();
                var node       = this.FindStartNode(start, start_path, include_start);
                var end_node   = this.FindEndNode(end, include_end);
         
                while(node != null) {
                    if(node.Left != null && !start_path.Contains(node)) {
                        this.Push(node);
                        node = node.Left;
                    } else {
                        do {
                            yield return node;
                            if(object.ReferenceEquals(node, end_node))
                                yield break;
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Node FindStartNode(in TKey start, HashSet<Node> path, bool include_start) {
                var node = this.TryGetPath(start, path);
                var diff = m_owner.m_comparer(start, node.Key);
                if(diff > 0 || (!include_start && diff == 0))
                    node = node.Next();
                return node;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Node FindEndNode(in TKey end, bool include_end) {
                var x    = m_owner.BinarySearch(end);
                var node = x.Node;
                if(x.Diff < 0 || (!include_end && x.Diff == 0))
                    node = node.Previous();
                return node;
            }
            private Node TryGetPath(in TKey key, HashSet<Node> path) {
                var current = m_owner.m_header.Parent;
                var prev    = current;
                while(current != null) {
                    path.Add(current);
                    prev = current;
         
                    int diff = m_owner.m_comparer(key, current.Key);

                    if(diff > 0)
                        current = current.Right;
                    else if(diff < 0) {
                        this.Push(current);
                        current = current.Left;
                    } else
                        return current;
                }
                return prev;
            }
        }
        #endregion
        #region AvlTree<string>.StartsWith()
        ///// <summary>
        /////     O(log n + m)   m = number of items returned
        /////     
        /////     Use StartsWithEnumerator instead for efficient re-use.
        ///// </summary>
        //internal static IEnumerable<AvlTree<string>.Node> StartsWith(AvlTree<string> tree, string key) {
        //    return new StartsWithEnumerator(tree).Run(key);
        //}
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        ///     
        ///     This enumerator is made for re-use, to avoid array reallocations.
        /// </summary>
        public sealed class StartsWithEnumerator {
            // manually handled stack for better performance
            private AvlTree<string>.Node[] m_stack = new AvlTree<string>.Node[16];
            private int m_stackIndex = 0;
         
            private readonly AvlTree<string> m_owner;
         
            public StartsWithEnumerator(AvlTree<string> owner) {
                m_owner = owner;
            }
         
            public IEnumerable<AvlTree<string>.Node> Run(string key) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }
         
                var start_path = new HashSet<AvlTree<string>.Node>();
                var node       = this.FindStartNode(key, start_path);
         
                while(node != null) {
                    if(node.Left != null && !start_path.Contains(node)) {
                        this.Push(node);
                        node = node.Left;
                    } else {
                        do {
                            if(node.Key.Length < key.Length || string.CompareOrdinal(node.Key, 0, key, 0, key.Length) != 0)
                                yield break;

                            yield return node;
                                
                            node = node.Right;
                        } while(node == null && m_stackIndex > 0 && (node = this.Pop()) != null);
                    }
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void Push(AvlTree<string>.Node value) {
                if(m_stackIndex == m_stack.Length)
                    Array.Resize(ref m_stack, m_stackIndex * 2);
                m_stack[m_stackIndex++] = value;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private AvlTree<string>.Node Pop() {
                var node = m_stack[--m_stackIndex];
                m_stack[m_stackIndex] = default;
                return node;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private AvlTree<string>.Node FindStartNode(string key, HashSet<AvlTree<string>.Node> path) {
                var node = this.TryGetPath(key, path);
                var diff = m_owner.m_comparer(key, node.Key);
                if(diff > 0)
                    node = node.Next();
                return node;
            }
            private AvlTree<string>.Node TryGetPath(string key, HashSet<AvlTree<string>.Node> path) {
                var current = m_owner.m_header.Parent;
                var prev    = current;
                while(current != null) {
                    path.Add(current);
                    prev = current;
         
                    int diff = m_owner.m_comparer(key, current.Key);

                    if(diff > 0)
                        current = current.Right;
                    else if(diff < 0) {
                        this.Push(current);
                        current = current.Left;
                    } else
                        return current;
                }
                return prev;
            }
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
        #endregion
 
        private enum Direction : byte {
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
 
            #region Next()
            /// <summary>
            ///     O(1)
            ///     Returns the next node.
            ///     This behaves like an iterator, but will keep working even as the tree is being changed.
            ///     This will run roughly half the speed as using the iterator if iterating through the entire tree.
            /// </summary>
            public Node Next() {
                var node = this;
                System.Diagnostics.Debug.Assert(node.Balance != State.Header);
 
                if(node.Right != null) {
                    node     = node.Right;
                    // avoid reading redundant node.Left if possible
                    var left = node.Left;
                    while(left != null) {
                        node = left;
                        left = node.Left;
                    }
                } else {
                    var parent = node.Parent;
                    while(node == parent.Right) { 
                        node   = parent; 
                        parent = parent.Parent;
                    }
                    if(parent.Balance == State.Header)
                        return null;
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
                System.Diagnostics.Debug.Assert(node.Balance != State.Header);
 
                if(node.Left != null) {
                    node = node.Left;
                    // avoid reading redundant node.Left if possible
                    var right = node.Right;
                    while(right != null) {
                        node  = right;
                        right = node.Right;
                    }
                } else {
                    var parent = node.Parent;
                    while(node == parent.Left) {
                        node   = parent; 
                        parent = parent.Parent;
                    }
                    if(parent.Balance == State.Header)
                        return null;
                    node = parent;
                }
                return node;
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
                this.Key = key;
            }
            #endregion
 
            #region constructors
            public Node(in TKey key) {
                this.Key = key;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                if(this.Balance == State.Header)
                    return "{header}";
                return this.Key?.ToString();
            }
            #endregion
        }
    }


    public static class AvlTreeExtensions {
        #region static CalculateTreeHeight()
        /// <summary>
        ///     Calculates the possible AVL tree height ranges for a given item count.
        /// </summary>
        public static HeightRange CalculateTreeHeight(int item_count) {
            const double golden_ratio_phi = 1.6180339887;

            return new HeightRange(){
                Min = Math.Log(item_count + 1, 2) - 1,
                Max = Math.Log(item_count + 2, golden_ratio_phi) - 1.3277,
            };
        }
        public struct HeightRange {
            /// <summary>
            /// Inclusive
            /// </summary>
            public double Min;
            /// <summary>
            /// Exclusive
            /// </summary>
            public double Max;
        }
        #endregion

        #region static AvlTree<string>.StartsWith()
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        ///     
        ///     Use StartsWithEnumerator instead for efficient re-use.
        /// </summary>
        public static IEnumerable<AvlTree<string>.Node> StartsWith(this AvlTree<string> tree, string key) {
            return new AvlTree<string>.StartsWithEnumerator(tree).Run(key);
        }
        #endregion
        #region static AvlTree<string, TValue>.StartsWith()
        /// <summary>
        ///     O(log n + m)   m = number of items returned
        ///     
        ///     Use StartsWithEnumerator instead for efficient re-use.
        /// </summary>
        public static IEnumerable<AvlTree<string, TValue>.Node> StartsWith<TValue>(this AvlTree<string, TValue> tree, string key) {
            return new AvlTree<string, TValue>.StartsWithEnumerator(tree).Run(key);
        }
        #endregion
    }
}
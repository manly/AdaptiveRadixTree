using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;


namespace System.Collections.Specialized
{
    /// <summary>
    ///     A min Priority Queue using binary heap.
    ///     This implementation is meant for speed and will give unstable results with TPriority ties.
    /// </summary>
    /// <remarks>
    ///     Implemented using a binary heap (on a 1-based array).
    ///     
    ///     The following properties are always respected:
    ///     heap property:  childrenNode.Priority >= parentNode.Priority.
    ///     shape property: a binary heap is a complete binary tree; that is, all levels of the tree, except possibly the last one (deepest) are fully filled, and, 
    ///                     if the last level of the tree is not complete, the nodes of that level are filled from left to right.
    ///     
    ///     example:    binary tree           heap storage
    ///                      1                1--\--\
    ///                     / \                  v  v
    ///                    /   \                 5  3--------\
    ///                   5     3                |           |
    ///                  / \   /                 \-----\--\  |
    ///                 7   9 8                        v  v  v
    ///                                                7  9  8
    ///                                      [1, 5, 3, 7, 9, 8]
    /// </remarks>
    public sealed class FastPriorityQueue<TValue, TPriority> {
        private const int MIN_CAPACITY  = 3;
        private const int INIT_CAPACITY = 16;

        private Node[] m_nodes; // first entry is ignored
        private readonly Comparison<TPriority> m_comparer;

        public int Count { get; private set; }

        #region constructors
        public FastPriorityQueue() : this(INIT_CAPACITY) { }
        public FastPriorityQueue(IComparer<TPriority> comparer) : this(comparer, INIT_CAPACITY) { }
        public FastPriorityQueue(Comparison<TPriority> comparer) : this(comparer, INIT_CAPACITY) { }
        public FastPriorityQueue(int capacity) : this(Comparer<TPriority>.Default, capacity) { }
        public FastPriorityQueue(IComparer<TPriority> comparer, int capacity) : this(comparer.Compare, capacity) { }
        public FastPriorityQueue(Comparison<TPriority> comparer, int capacity) {
            if(capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            
            m_nodes    = new Node[Math.Max(capacity, MIN_CAPACITY) + 1];
            m_comparer = comparer;
        }
        /// <summary>
        ///    O(n)
        /// </summary>
        public FastPriorityQueue(TValue[] values, TPriority[] priorities) : this(values, priorities, Comparer<TPriority>.Default) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public FastPriorityQueue(TValue[] values, TPriority[] priorities, IComparer<TPriority> comparer) : this(values, priorities, comparer.Compare) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public FastPriorityQueue(TValue[] values, TPriority[] priorities, Comparison<TPriority> comparer) : this(comparer, values.Length) {
            this.EnqueueRange(values, priorities);
        }
        /// <summary>
        ///    O(n)
        /// </summary>
        public FastPriorityQueue(Node[] nodes, TPriority[] priorities) : this(nodes, priorities, Comparer<TPriority>.Default) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public FastPriorityQueue(Node[] nodes, TPriority[] priorities, IComparer<TPriority> comparer) : this(nodes, priorities, comparer.Compare) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public FastPriorityQueue(Node[] nodes, TPriority[] priorities, Comparison<TPriority> comparer) : this(comparer, nodes.Length){
            this.EnqueueRange(nodes, priorities);
        }
        #endregion

        #region First
        /// <summary>
        ///    O(1)   
        ///    Returns the lowest priority node.
        ///    Functionally the same as Peek().
        /// </summary>
        public Node First => m_nodes[1];
        #endregion
        #region Capacity
        public int Capacity => m_nodes.Length - 1;
        #endregion
        #region Nodes
        /// <summary>
        ///     O(n)
        ///     Returns the nodes, in heap order.
        ///     The entries are mostly sorted, but not quite. 
        ///     Use Dequeue() to get items in sorted order (HeapSort, worst case O(n log n)).
        ///     If you want an in-place (unstable) HeapSort of m_nodes, then "m_nodes[this.Count]=Dequeue()" to get in-place descending order sorting. Change compare method to make it ascending.
        /// </summary>
        public IEnumerator<Node> Nodes {
            get {
                var count = this.Count;
                var nodes = m_nodes;
                for(int i = 1; i <= count; i++)
                    yield return nodes[i];
            }
        }
        #endregion

        #region Enqueue()
        /// <summary>
        ///    O(log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Node Enqueue(TValue value, TPriority priority) {
            var node = new Node(value);
            this.Enqueue(node, priority);
            return node;
        }
        /// <summary>
        ///    O(log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(Node node, TPriority priority) {
            var count = this.Count;
            if(count == m_nodes.Length - 1)
                this.EnsureCapacity();

            count++;
            node.Priority  = priority;
            node.Index     = count;
            m_nodes[count] = node;
            this.Count     = count;

            this.SiftUp(node);
        }
        #endregion
        #region EnqueueRange()
        /// <summary>
        ///    O(n) if empty, otherwise O(k log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueRange(TValue[] values, TPriority[] priorities) {
            if(priorities.Length < values.Length)
                throw new ArgumentException("Priorities must contain at least as many entries as there are values.", nameof(priorities));

            var count = this.Count;

            if(count + values.Length >= m_nodes.Length)
                this.EnsureCapacity(values.Length);

            // must be set before SiftDown()
            this.Count += values.Length;

            // if nearly empty, use [Floyd's algorithm O(n)] instead of [Enqueue() on each item O(k log n)]
            if(count <= 1) {
                for(int i = 0; i < values.Length; i++) {
                    var node       = new Node(values[i]);
                    node.Priority  = priorities[i];
                    node.Index     = ++count;
                    m_nodes[count] = node;
                }
                this.BuildHeap();
            } else {
                for(int i = 0; i < values.Length; i++) {
                    var node       = new Node(values[i]);
                    node.Priority  = priorities[i];
                    node.Index     = ++count;
                    m_nodes[count] = node;

                    this.SiftUp(node);
                }
            }
        }
        /// <summary>
        ///    O(n) if empty, otherwise O(k log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueRange(Node[] nodes, TPriority[] priorities) {
            if(priorities.Length < nodes.Length)
                throw new ArgumentException("Priorities must contain at least as many entries as there are nodes.", nameof(priorities));
            
            var count = this.Count;

            if(count + nodes.Length >= m_nodes.Length)
                this.EnsureCapacity(nodes.Length);

            Array.Copy(nodes, 0, m_nodes, count + 1, nodes.Length);

            // must be set before SiftDown()
            this.Count += nodes.Length;

            // if nearly empty, use [Floyd's algorithm O(n)] instead of [Enqueue() on each item O(k log n)]
            if(count <= 1) {
                for(int i = 0; i < nodes.Length; i++) {
                    var node      = nodes[i];
                    node.Priority = priorities[i];
                    node.Index    = ++count;
                }
                this.BuildHeap();
            } else {
                for(int i = 0; i < nodes.Length; i++) {
                    var node      = nodes[i];
                    node.Priority = priorities[i];
                    node.Index    = ++count;

                    this.SiftUp(node);
                }
            }
        }
        #endregion
        #region Dequeue()
        /// <summary>
        ///    O(log n)
        ///    Returns the lowest priority node.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Node Dequeue() {
            var res   = m_nodes[1];
            var count = this.Count;

            if(count == 1) {
                m_nodes[1] = null;
                this.Count = 0;
                return res;
            }

            // swap with last node
            var last       = m_nodes[count];
            m_nodes[1]     = last;
            last.Index     = 1;
            m_nodes[count] = null;
            this.Count     = count - 1;

            this.SiftDown(last);

            return res;
        }
        #endregion

        #region FindNode()
        /// <summary>
        ///     O(n/2) on average, O(n) worst case
        ///     Returns the first node matching the value.
        ///     Returns null if not found.
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public Node FindNode(TValue value) {
            return this.FindNodes(value).FirstOrDefault();
        }
        /// <summary>
        ///     O(log n) best case, O(n) worst case. Prunes out every branch with a higher priority.
        ///     Returns the first node matching the priority.
        ///     Returns null if not found.
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public Node FindNode(TPriority priority) {
            return this.FindNodes(priority).FirstOrDefault();
        }
        #endregion
        #region FindNodes()
        /// <summary>
        ///     O(n)
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public IEnumerable<Node> FindNodes(TValue value) {
            var nodes    = m_nodes;
            var count    = this.Count;
            var comparer = EqualityComparer<TValue>.Default;

            if(count == 0)
                yield break;

            var remaining = new Stack<Node>();
            remaining.Push(nodes[1]);

            while(remaining.Count > 0) {
                var currentNode = remaining.Pop();

                // note: this should return in insertion order as defined in HasHigherPriority()
                if(comparer.Equals(currentNode.Value, value))
                    yield return currentNode;

                var left  = currentNode.Index * 2;
                var right = left + 1;

                if(left > count)
                    continue;

                if(right <= count)
                    remaining.Push(nodes[right]);

                remaining.Push(nodes[left]);
            }

            //// if insertion order isnt important, this code would run faster
            //for(int i = 1; i <= count; i++) {
            //    var c = nodes[i];
            //    if(comparer.Equals(c.Value, value))
            //        yield return c;
            //}
        }
        /// <summary>
        ///     O(log n) best case, O(n) worst case. Prunes out every branch with a higher priority.
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public IEnumerable<Node> FindNodes(TPriority priority) {
            var nodes = m_nodes;
            var count = this.Count;

            if(count == 0)
                yield break;

            var remaining = new Stack<Node>();
            remaining.Push(nodes[1]);

            while(remaining.Count > 0) {
                var currentNode = remaining.Pop();

                if(this.HasEqualPriority(currentNode.Priority, priority))
                    yield return currentNode;

                var left  = currentNode.Index * 2;
                var right = left + 1;

                if(left > count)
                    continue;

                if(right <= count) {
                    var rightNode = nodes[right];
                    if(!this.HasHigherPriority(rightNode.Priority, priority))
                        remaining.Push(rightNode);
                }

                var leftNode = nodes[left];
                if(!this.HasHigherPriority(leftNode.Priority, priority))
                    remaining.Push(leftNode);
            }
        }
        #endregion

        #region Remove()
        /// <summary>
        ///    O(log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(Node node) {
            var count = this.Count;

            // last node
            if(node.Index == count) {
                m_nodes[count] = null;
                this.Count     = count - 1;
                return;
            }

            // swap the node with the last node
            var last            = m_nodes[count];
            last.Index          = node.Index;
            m_nodes[node.Index] = last;
            m_nodes[count]      = null;
            this.Count          = count - 1;

            this.UpdateNode(last);
        }
        #endregion
        #region Contains()
        /// <summary>
        ///    O(1)
        /// </summary>
        public bool Contains(Node node) {
            return m_nodes[node.Index] == node;
        }
        #endregion
        #region UpdatePriority()
        /// <summary>
        ///    O(log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdatePriority(Node node, TPriority priority) {
            node.Priority = priority;
            this.UpdateNode(node);
        }
        #endregion
        #region Clear()
        public void Clear() {
            Array.Clear(m_nodes, 1, this.Count);
            this.Count = 0;
        }
        #endregion
        #region SetCapacity()
        public void SetCapacity(int capacity) {
            capacity = Math.Max(capacity, MIN_CAPACITY);

            if(capacity < this.Count)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if(capacity == m_nodes.Length + 1)
                return;

            var _new = new Node[capacity + 1];
            Array.Copy(m_nodes, 1, _new, 1, capacity);
            m_nodes = _new;
        }
        #endregion

        #region private SiftUp()
        /// <summary>
        ///    O(log n)
        /// </summary>
        private void SiftUp(Node node) {
            int parent;
            var nodes = m_nodes;
            if(node.Index > 1) {
                parent         = node.Index >> 1;
                var parentNode = nodes[parent];

                if(this.HasHigherOrEqualPriority(parentNode, node))
                    return;

                nodes[node.Index] = parentNode;
                parentNode.Index  = node.Index;
                node.Index        = parent;
            } else
                return;

            while(parent > 1) {
                parent       >>= 1;
                var parentNode = nodes[parent];

                if(this.HasHigherOrEqualPriority(parentNode, node))
                    break;

                nodes[node.Index] = parentNode;
                parentNode.Index  = node.Index;
                node.Index        = parent;
            }

            nodes[node.Index] = node;
        }
        #endregion
        #region private SiftDown()
        /// <summary>
        ///    O(log n)
        /// </summary>
        private void SiftDown(Node node) {
            var final = node.Index;
            var left  = final * 2;
            var count = this.Count;
            var nodes = m_nodes;

            if(left > count)
                return;

            int right    = left + 1;
            var leftNode = nodes[left];
            if(this.HasHigherPriority(leftNode, node)) {
                if(right > count) {
                    node.Index     = left;
                    leftNode.Index = final;
                    nodes[final]   = leftNode;
                    nodes[left]    = node;
                    return;
                }
                var rightNode = nodes[right];
                if(this.HasHigherPriority(leftNode, rightNode)) {
                    leftNode.Index = final;
                    nodes[final]   = leftNode;
                    final          = left;
                } else {
                    rightNode.Index = final;
                    nodes[final]    = rightNode;
                    final           = right;
                }
            } else if(right > count)
                return;
            else {
                var rightNode = nodes[right];
                if(this.HasHigherPriority(rightNode, node)) {
                    rightNode.Index = final;
                    nodes[final]    = rightNode;
                    final           = right;
                } else
                    return;
            }

            while(true) {
                left = final * 2;

                if(left > count) {
                    node.Index   = final;
                    nodes[final] = node;
                    return;
                }

                right    = left + 1;
                leftNode = nodes[left];
                if(this.HasHigherPriority(leftNode, node)) {
                    if(right > count) {
                        node.Index     = left;
                        leftNode.Index = final;
                        nodes[final]   = leftNode;
                        nodes[left]    = node;
                        return;
                    }
                    var rightNode = nodes[right];
                    if(this.HasHigherPriority(leftNode, rightNode)) {
                        leftNode.Index = final;
                        nodes[final]   = leftNode;
                        final          = left;
                    } else {
                        rightNode.Index = final;
                        nodes[final]    = rightNode;
                        final           = right;
                    }
                } else if(right > count) {
                    node.Index   = final;
                    nodes[final] = node;
                    return;
                } else {
                    var rightNode = nodes[right];
                    if(this.HasHigherPriority(rightNode, node)) {
                        rightNode.Index = final;
                        nodes[final]    = rightNode;
                        final           = right;
                    } else {
                        node.Index   = final;
                        nodes[final] = node;
                        return;
                    }
                }
            }
        }
        #endregion

        #region private BuildHeap()
        /// <summary>
        ///     O(n) using Floyd's optimisation.
        ///     Rebuilds the heap using the Floyd's algorithm.
        ///     This assumes the nodes are stored but unsorted.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BuildHeap() {
            // this would take O(k log n) if using Enqueue()/SiftUp()
            var nodes = m_nodes;
            for(int i = nodes.Length >> 1; i >= 1; i--)
                this.SiftDown(nodes[i]);
        }
        #endregion
        #region private UpdateNode()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateNode(Node node) {
            var parent = node.Index >> 1;

            if(parent > 0 && this.HasHigherPriority(node, m_nodes[parent]))
                this.SiftUp(node);
            else
                this.SiftDown(node);
        }
        #endregion
        #region private EnsureCapacity()
        /// <summary>
        ///     Ensures we have the capacity for 1 more item.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity() {
            var capacity = this.Count * 2;
            this.SetCapacity(capacity);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int added) {
            var capacity = this.Count + added;

            // crude roundup
            capacity = capacity <= 4096 ?
                capacity * 2 :
                (capacity & 0xFFFFE00) + ((capacity & 0x00001FF) != 0 ? 4096 : 0); // ceiling(4096)
            
            this.SetCapacity(capacity);
        }
        #endregion

        #region private HasHigherPriority()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHigherPriority(Node higher, Node lower) {
            return m_comparer(higher.Priority, lower.Priority) < 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHigherPriority(TPriority higher, TPriority lower) {
            return m_comparer(higher, lower) < 0;
        }
        #endregion
        #region private HasHigherOrEqualPriority()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHigherOrEqualPriority(Node higher, Node lower) {
            return m_comparer(higher.Priority, lower.Priority) <= 0;
        }
        #endregion
        #region private HasEqualPriority()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasEqualPriority(TPriority p1, TPriority p2) {
            return m_comparer(p1, p2) == 0;
        }
        #endregion

        // debugging
        #region internal IsValidConsistency()
        internal bool IsValidConsistency() {
            for(int i = 1; i < m_nodes.Length; i++) {
                if(m_nodes[i] == null) 
                    continue;

                int left = 2 * i;
                if(left < m_nodes.Length && m_nodes[left] != null && this.HasHigherPriority(m_nodes[left], m_nodes[i]))
                    return false;

                int right = left + 1;
                if(right < m_nodes.Length && m_nodes[right] != null && this.HasHigherPriority(m_nodes[right], m_nodes[i]))
                    return false;
            }
            return true;
        }
        #endregion

        public sealed class Node {
            public TValue Value;
            public TPriority Priority { get; internal set; }
            /// <summary>
            ///     The index within the priority queue.
            /// </summary>
            public int Index { get; internal set; }

            #region constructors
            public Node(TValue value) {
                this.Value = value;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return string.Format("[{0}] {1}", this.Priority, this.Value);
            }
            #endregion
        }
    }

    /// <summary>
    ///     A stable min Priority Queue using binary heap.
    ///     Uses FIFO ordering for ties.
    /// </summary>
    /// <remarks>
    ///     Implemented using a binary heap (on a 1-based array).
    ///     
    ///     The following properties are always respected:
    ///     heap property:  childrenNode.Priority >= parentNode.Priority.
    ///     shape property: a binary heap is a complete binary tree; that is, all levels of the tree, except possibly the last one (deepest) are fully filled, and, 
    ///                     if the last level of the tree is not complete, the nodes of that level are filled from left to right.
    ///     
    ///     example:    binary tree           heap storage
    ///                      1                1--\--\
    ///                     / \                  v  v
    ///                    /   \                 5  3--------\
    ///                   5     3                |           |
    ///                  / \   /                 \-----\--\  |
    ///                 7   9 8                        v  v  v
    ///                                                7  9  8
    ///                                      [1, 5, 3, 7, 9, 8]
    /// </remarks>
    public sealed class StablePriorityQueue<TValue, TPriority> {
        private const int MIN_CAPACITY  = 3;
        private const int INIT_CAPACITY = 16;

        private Node[] m_nodes; // first entry is ignored
        private readonly Comparison<TPriority> m_comparer;
        private long m_insertSequence = 0; // unique ID to handle TPriority ties

        public int Count { get; private set; }

        #region constructors
        public StablePriorityQueue() : this(INIT_CAPACITY) { }
        public StablePriorityQueue(IComparer<TPriority> comparer) : this(comparer, INIT_CAPACITY) { }
        public StablePriorityQueue(Comparison<TPriority> comparer) : this(comparer, INIT_CAPACITY) { }
        public StablePriorityQueue(int capacity) : this(Comparer<TPriority>.Default, capacity) { }
        public StablePriorityQueue(IComparer<TPriority> comparer, int capacity) : this(comparer.Compare, capacity) { }
        public StablePriorityQueue(Comparison<TPriority> comparer, int capacity) {
            if(capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            m_nodes    = new Node[Math.Max(capacity, MIN_CAPACITY) + 1];
            m_comparer = comparer;
        }
        /// <summary>
        ///    O(n)
        /// </summary>
        public StablePriorityQueue(TValue[] values, TPriority[] priorities) : this(values, priorities, Comparer<TPriority>.Default) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public StablePriorityQueue(TValue[] values, TPriority[] priorities, IComparer<TPriority> comparer) : this(values, priorities, comparer.Compare) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public StablePriorityQueue(TValue[] values, TPriority[] priorities, Comparison<TPriority> comparer) : this(comparer, values.Length) {
            this.EnqueueRange(values, priorities);
        }
        /// <summary>
        ///    O(n)
        /// </summary>
        public StablePriorityQueue(Node[] nodes, TPriority[] priorities) : this(nodes, priorities, Comparer<TPriority>.Default) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public StablePriorityQueue(Node[] nodes, TPriority[] priorities, IComparer<TPriority> comparer) : this(nodes, priorities, comparer.Compare) { }
        /// <summary>
        ///    O(n)
        /// </summary>
        public StablePriorityQueue(Node[] nodes, TPriority[] priorities, Comparison<TPriority> comparer) : this(comparer, nodes.Length) {
            this.EnqueueRange(nodes, priorities);
        }
        #endregion

        #region First
        /// <summary>
        ///    O(1)
        ///    Returns the lowest priority node.
        ///    Functionally the same as Peek().
        /// </summary>
        public Node First => m_nodes[1];
        #endregion
        #region Capacity
        public int Capacity => m_nodes.Length - 1;
        #endregion
        #region Nodes
        /// <summary>
        ///     O(n)
        ///     Returns the nodes, in heap order.
        ///     The entries are mostly sorted, but not quite. 
        ///     Use Dequeue() to get items in sorted order (HeapSort, worst case O(n log n)).
        ///     If you want an in-place (stable) HeapSort of m_nodes, then "m_nodes[this.Count]=Dequeue()" to get in-place descending order sorting. Change compare method to make it ascending.
        /// </summary>
        public IEnumerator<Node> Nodes {
            get {
                var count = this.Count;
                var nodes = m_nodes;
                for(int i = 1; i <= count; i++)
                    yield return nodes[i];
            }
        }
        #endregion

        #region Enqueue()
        /// <summary>
        ///    O(log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Node Enqueue(TValue value, TPriority priority) {
            var node = new Node(value);
            this.Enqueue(node, priority);
            return node;
        }
        /// <summary>
        ///    O(log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(Node node, TPriority priority) {
            var count = this.Count;
            if(count == m_nodes.Length - 1)
                this.EnsureCapacity();

            count++;
            node.Priority    = priority;
            node.Index       = count;
            node.InsertIndex = m_insertSequence++;
            m_nodes[count]   = node;
            this.Count       = count;

            this.SiftUp(node);
        }
        #endregion
        #region EnqueueRange()
        /// <summary>
        ///    O(n) if empty, otherwise O(k log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueRange(TValue[] values, TPriority[] priorities) {
            if(priorities.Length < values.Length)
                throw new ArgumentException("Priorities must contain at least as many entries as there are values.", nameof(priorities));

            var count = this.Count;

            if(count + values.Length >= m_nodes.Length)
                this.EnsureCapacity(values.Length);

            // must be set before SiftDown()
            this.Count     += values.Length;
            var insertIndex = m_insertSequence;
            m_insertSequence  += values.Length;

            // if nearly empty, use [Floyd's algorithm O(n)] instead of [Enqueue() on each item O(k log n)]
            if(count <= 1) {
                for(int i = 0; i < values.Length; i++) {
                    var node         = new Node(values[i]);
                    node.Priority    = priorities[i];
                    node.Index       = ++count;
                    node.InsertIndex = insertIndex++;
                    m_nodes[count]   = node;
                }
                this.BuildHeap();
            } else {
                for(int i = 0; i < values.Length; i++) {
                    var node         = new Node(values[i]);
                    node.Priority    = priorities[i];
                    node.Index       = ++count;
                    node.InsertIndex = insertIndex++;
                    m_nodes[count]   = node;

                    this.SiftUp(node);
                }
            }
        }
        /// <summary>
        ///    O(n) if empty, otherwise O(k log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueRange(Node[] nodes, TPriority[] priorities) {
            if(priorities.Length < nodes.Length)
                throw new ArgumentException("Priorities must contain at least as many entries as there are nodes.", nameof(priorities));
            
            var count = this.Count;

            if(count + nodes.Length >= m_nodes.Length)
                this.EnsureCapacity(nodes.Length);

            Array.Copy(nodes, 0, m_nodes, count + 1, nodes.Length);

            // must be set before SiftDown()
            this.Count += nodes.Length;

            // if nearly empty, use [Floyd's algorithm O(n)] instead of [Enqueue() on each item O(k log n)]
            if(count <= 1) {
                for(int i = 0; i < nodes.Length; i++) {
                    var node      = nodes[i];
                    node.Priority = priorities[i];
                    node.Index    = ++count;
                }
                this.BuildHeap();
            } else {
                for(int i = 0; i < nodes.Length; i++) {
                    var node      = nodes[i];
                    node.Priority = priorities[i];
                    node.Index    = ++count;

                    this.SiftUp(node);
                }
            }
        }
        #endregion
        #region Dequeue()
        /// <summary>
        ///    O(log n)
        ///    Returns the lowest priority node.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Node Dequeue() {
            var res   = m_nodes[1];
            var count = this.Count;

            if(count == 1) {
                m_nodes[1] = null;
                this.Count = 0;
                return res;
            }

            // swap with last node
            var last       = m_nodes[count];
            m_nodes[1]     = last;
            last.Index     = 1;
            m_nodes[count] = null;
            this.Count     = count - 1;

            this.SiftDown(last);

            return res;
        }
        #endregion

        #region FindNode()
        /// <summary>
        ///     O(n/2) on average, O(n) worst case
        ///     Returns the first node matching the value, not accounting for insertion order (ignoring FIFO ordering for ties).
        ///     Returns null if not found.
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public Node FindNode(TValue value) {
            return this.FindNodes(value).FirstOrDefault();
        }
        /// <summary>
        ///     O(log n) best case, O(n) worst case. Prunes out every branch with a higher priority.
        ///     Returns the first node matching the priority, not accounting for insertion order (ignoring FIFO ordering for ties).
        ///     Returns null if not found.
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public Node FindNode(TPriority priority) {
            return this.FindNodes(priority).FirstOrDefault();
        }
        #endregion
        #region FindNodes()
        /// <summary>
        ///     O(n)
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public IEnumerable<Node> FindNodes(TValue value) {
            var nodes    = m_nodes;
            var count    = this.Count;
            var comparer = EqualityComparer<TValue>.Default;

            if(count == 0)
                yield break;

            var remaining = new Stack<Node>();
            remaining.Push(nodes[1]);

            while(remaining.Count > 0) {
                var currentNode = remaining.Pop();

                // note: this should return in insertion order as defined in HasHigherPriority()
                if(comparer.Equals(currentNode.Value, value))
                    yield return currentNode;

                var left  = currentNode.Index * 2;
                var right = left + 1;

                if(left > count)
                    continue;

                if(right <= count)
                    remaining.Push(nodes[right]);

                remaining.Push(nodes[left]);
            }

            //// if insertion order isnt important, this code would run faster
            //for(int i = 1; i <= count; i++) {
            //    var c = nodes[i];
            //    if(comparer.Equals(c.Value, value))
            //        yield return c;
            //}
        }
        /// <summary>
        ///     O(log n) best case, O(n) worst case. Prunes out every branch with a higher priority.
        ///     This is added for convenience, but the class expects the caller to maintain a list of node for faster processing rather than rely on this method.
        ///     Or at least have a dictionary if nothing else, to avoid calling this method.
        /// </summary>
        public IEnumerable<Node> FindNodes(TPriority priority) {
            var nodes = m_nodes;
            var count = this.Count;

            if(count == 0)
                yield break;

            var remaining = new Stack<Node>();
            remaining.Push(nodes[1]);

            while(remaining.Count > 0) {
                var currentNode = remaining.Pop();

                // note: this should return in insertion order as defined in HasHigherPriority()
                if(this.HasEqualPriority(currentNode.Priority, priority))
                    yield return currentNode;

                var left  = currentNode.Index * 2;
                var right = left + 1;

                if(left > count)
                    continue;

                if(right <= count) {
                    var rightNode = nodes[right];
                    if(!this.HasHigherPriority(rightNode.Priority, priority))
                        remaining.Push(rightNode);
                }

                var leftNode = nodes[left];
                if(!this.HasHigherPriority(leftNode.Priority, priority))
                    remaining.Push(leftNode);
            }
        }
        #endregion

        #region Remove()
        /// <summary>
        ///    O(log n)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(Node node) {
            var count = this.Count;

            // last node
            if(node.Index == count) {
                m_nodes[count] = null;
                this.Count     = count - 1;
                return;
            }

            // swap the node with the last node
            var last            = m_nodes[count];
            last.Index          = node.Index;
            m_nodes[node.Index] = last;
            m_nodes[count]      = null;
            this.Count          = count - 1;

            this.UpdateNode(last);
        }
        #endregion
        #region Contains()
        /// <summary>
        ///    O(1)
        /// </summary>
        public bool Contains(Node node) {
            return m_nodes[node.Index] == node;
        }
        #endregion
        #region UpdatePriority()
        /// <summary>
        ///    O(log n)
        ///    Conserves the InsertionIndex of the node.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdatePriority(Node node, TPriority priority) {
            node.Priority = priority;
            this.UpdateNode(node);
        }
        #endregion
        #region Clear()
        public void Clear() {
            Array.Clear(m_nodes, 1, this.Count);
            this.Count = 0;
        }
        #endregion
        #region SetCapacity()
        public void SetCapacity(int capacity) {
            capacity = Math.Max(capacity, MIN_CAPACITY);

            if(capacity < this.Count)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if(capacity == m_nodes.Length + 1)
                return;

            var _new = new Node[capacity + 1];
            Array.Copy(m_nodes, 1, _new, 1, capacity);
            m_nodes = _new;
        }
        #endregion

        #region private SiftUp()
        /// <summary>
        ///    O(log n)
        /// </summary>
        private void SiftUp(Node node) {
            int parent;
            var nodes = m_nodes;
            if(node.Index > 1) {
                parent         = node.Index >> 1;
                var parentNode = nodes[parent];

                if(this.HasHigherPriority(parentNode, node))
                    return;

                nodes[node.Index] = parentNode;
                parentNode.Index  = node.Index;
                node.Index        = parent;
            } else
                return;

            while(parent > 1) {
                parent       >>= 1;
                var parentNode = nodes[parent];

                if(this.HasHigherPriority(parentNode, node))
                    break;

                nodes[node.Index] = parentNode;
                parentNode.Index  = node.Index;
                node.Index        = parent;
            }

            nodes[node.Index] = node;
        }
        #endregion
        #region private SiftDown()
        /// <summary>
        ///    O(log n)
        /// </summary>
        private void SiftDown(Node node) {
            var final = node.Index;
            var left  = final * 2;
            var count = this.Count;
            var nodes = m_nodes;

            if(left > count)
                return;

            int right    = left + 1;
            var leftNode = nodes[left];
            if(this.HasHigherPriority(leftNode, node)) {
                if(right > count) {
                    node.Index     = left;
                    leftNode.Index = final;
                    nodes[final]   = leftNode;
                    nodes[left]    = node;
                    return;
                }
                var rightNode = nodes[right];
                if(this.HasHigherPriority(leftNode, rightNode)) {
                    leftNode.Index = final;
                    nodes[final]   = leftNode;
                    final          = left;
                } else {
                    rightNode.Index = final;
                    nodes[final]    = rightNode;
                    final           = right;
                }
            } else if(right > count)
                return;
            else {
                var rightNode = nodes[right];
                if(this.HasHigherPriority(rightNode, node)) {
                    rightNode.Index = final;
                    nodes[final]    = rightNode;
                    final           = right;
                } else
                    return;
            }

            while(true) {
                left = final * 2;

                if(left > count) {
                    node.Index   = final;
                    nodes[final] = node;
                    return;
                }

                right    = left + 1;
                leftNode = nodes[left];
                if(this.HasHigherPriority(leftNode, node)) {
                    if(right > count) {
                        node.Index     = left;
                        leftNode.Index = final;
                        nodes[final]   = leftNode;
                        nodes[left]    = node;
                        return;
                    }
                    var rightNode = nodes[right];
                    if(this.HasHigherPriority(leftNode, rightNode)) {
                        leftNode.Index = final;
                        nodes[final]   = leftNode;
                        final          = left;
                    } else {
                        rightNode.Index = final;
                        nodes[final]    = rightNode;
                        final           = right;
                    }
                } else if(right > count) {
                    node.Index   = final;
                    nodes[final] = node;
                    return;
                } else {
                    var rightNode = nodes[right];
                    if(this.HasHigherPriority(rightNode, node)) {
                        rightNode.Index = final;
                        nodes[final]    = rightNode;
                        final           = right;
                    } else {
                        node.Index   = final;
                        nodes[final] = node;
                        return;
                    }
                }
            }
        }
        #endregion

        #region private BuildHeap()
        /// <summary>
        ///     O(n) using Floyd's optimisation.
        ///     Rebuilds the heap using the Floyd's algorithm.
        ///     This assumes the nodes are stored but unsorted.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BuildHeap() {
            // this would take O(k log n) if using Enqueue()/SiftUp()
            var nodes = m_nodes;
            for(int i = nodes.Length >> 1; i >= 1; i--)
                this.SiftDown(nodes[i]);
        }
        #endregion
        #region private UpdateNode()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateNode(Node node) {
            var parent = node.Index >> 1;

            if(parent > 0 && this.HasHigherPriority(node, m_nodes[parent]))
                this.SiftUp(node);
            else
                this.SiftDown(node);
        }
        #endregion
        #region private EnsureCapacity()
        /// <summary>
        ///     Ensures we have the capacity for 1 more item.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity() {
            var capacity = this.Count * 2;
            this.SetCapacity(capacity);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int added) {
            var capacity = this.Count + added;

            // crude roundup
            capacity = capacity <= 4096 ?
                capacity * 2 :
                (capacity & 0xFFFFE00) + ((capacity & 0x00001FF) != 0 ? 4096 : 0); // ceiling(4096)
            
            this.SetCapacity(capacity);
        }
        #endregion

        #region private HasHigherPriority()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHigherPriority(Node higher, Node lower) {
            var diff = m_comparer(higher.Priority, lower.Priority);
            // if you want to change FIFO to LIFO, only need to change this line of code
            return diff < 0 || (diff == 0 && higher.InsertIndex < lower.InsertIndex);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHigherPriority(TPriority higher, TPriority lower) {
            // this code intentionally ignores FIFO/LIFO ordering rules because the context calling it willfully ignores it
            return m_comparer(higher, lower) < 0;
        }
        #endregion
        #region private HasEqualPriority()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasEqualPriority(TPriority p1, TPriority p2) {
            return m_comparer(p1, p2) == 0;
        }
        #endregion

        // debugging
        #region internal IsValidConsistency()
        internal bool IsValidConsistency() {
            for(int i = 1; i < m_nodes.Length; i++) {
                if(m_nodes[i] == null) 
                    continue;

                int left = 2 * i;
                if(left < m_nodes.Length && m_nodes[left] != null && this.HasHigherPriority(m_nodes[left], m_nodes[i]))
                    return false;

                int right = left + 1;
                if(right < m_nodes.Length && m_nodes[right] != null && this.HasHigherPriority(m_nodes[right], m_nodes[i]))
                    return false;
            }
            return true;
        }
        #endregion

        public sealed class Node {
            public TValue Value;
            public TPriority Priority { get; internal set; }
            /// <summary>
            ///     The index within the priority queue.
            /// </summary>
            public int Index { get; internal set; }
            /// <summary>
            ///     The unique sequence ID.
            ///     Used for TPriority ties.
            /// </summary>
            public long InsertIndex { get; internal set; }

            #region constructors
            public Node(TValue value) {
                this.Value = value;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return string.Format("[{0}] {1}", this.Priority, this.Value);
            }
            #endregion
        }
    }
}
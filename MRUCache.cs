using System.Collections.Generic;
 
namespace System.Collections.Specialized
{
    /// <summary>
    ///    Implements a MRU (Most Recently Used) Dictionary with a limited number of entries.
    ///    Entries are evicted in Least-Recently-Used order.
    /// </summary>
    public class MRUCache<TKey, TValue> : ICollection, IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue> {
        private const int DEFAULT_CAPACITY = 256;

        private readonly Dictionary<TKey, NodePointer> m_dict;
        private Node[] m_nodes; // circular double linked list

        private int m_mostRecentlyUsedIndex;
 
        public int Count { get; private set; }
        public int Capacity { get; private set; }
 
        #region constructors
        public MRUCache(int capacity = DEFAULT_CAPACITY) : this(capacity, new Dictionary<TKey, NodePointer>(capacity)){ }
        public MRUCache(int capacity, IEqualityComparer<TKey> comparer) : this(capacity, new Dictionary<TKey, NodePointer>(capacity, comparer)){ }
        private MRUCache(int capacity, Dictionary<TKey, NodePointer> dict) {
            m_dict = dict;
            this.SetCapacity(capacity);

            this.Init();
            this.Count = 0;
        }
        #endregion
 
        #region Keys
        /// <summary>
        ///     Returns the keys in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned items.
        /// </summary>
        public IEnumerable<TKey> Keys {
            get {
                int max   = this.Count;
                var index = m_mostRecentlyUsedIndex;
                for(int i = 0; i < max; i++) {
                    var current = m_nodes[index];
                    yield return current.Key;
                    index = current.Next;
                }

                //foreach(var item in m_dict.Keys)
                //    yield return item;
            }
        }
        #endregion
        #region Values
        /// <summary>
        ///     Returns the values in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned items.
        /// </summary>
        public IEnumerable<TValue> Values {
            get {
                int max   = this.Count;
                var index = m_mostRecentlyUsedIndex;
                for(int i = 0; i < max; i++) {
                    var current = m_nodes[index];
                    yield return m_dict[current.Key].Value;
                    index = current.Next;
                }

                //foreach(var item in m_dict.Values)
                //    yield return item.Value;
            }
        }
        #endregion
        #region Items
        /// <summary>
        ///     Returns the items in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned items.
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> Items {
            get {
                int max   = this.Count;
                var index = m_mostRecentlyUsedIndex;
                for(int i = 0; i < max; i++) {
                    var current = m_nodes[index];
                    yield return new KeyValuePair<TKey, TValue>(current.Key, m_dict[current.Key].Value);
                    index = current.Next;
                }

                //foreach(var item in m_dict)
                //    yield return new KeyValuePair<TKey, TValue>(item.Key, item.Value.Value);
            }
        }
        #endregion
        #region this[]
        /// <summary>
        ///     O(1)
        ///     Gets or sets the value associated with the specified key.
        /// </summary>
        /// <returns>
        ///     The value associated with the specified key. If the specified key is not found,
        ///     a get operation throws a System.Collections.Generic.KeyNotFoundException, and
        ///     a set operation creates a new element with the specified key.
        /// </returns>
        /// <param name="key">The key of the value to get or set.</param>
        /// <exception cref="KeyNotFoundException">The Key does not exist</exception>
        public TValue this[in TKey key] {
            get {
                if(this.TryGetValue(key, out var value))
                    return value;
                throw new KeyNotFoundException();
            }
            set {
                this.AddOrUpdate(key, value);
            }
        }
        #endregion
 
        #region MostRecentlyUsed
        /// <summary>
        ///    O(1)
        ///    Does not bump MRU of returned items.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Count == 0</exception>
        public TKey MostRecentlyUsed {
            get {
                if(this.Count == 0)
                    throw new KeyNotFoundException("Collection is empty.");
                return m_nodes[m_mostRecentlyUsedIndex].Key;
            }
        }
        #endregion
        #region LeastRecentlyUsed
        /// <summary>
        ///    O(1)
        ///    Does not bump MRU of returned items.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Count == 0</exception>
        public TKey LeastRecentlyUsed {
            get {
                if(this.Count == 0)
                    throw new KeyNotFoundException("Collection is empty.");

                var most_recently_used  = m_nodes[m_mostRecentlyUsedIndex];
                var least_recently_used = m_nodes[most_recently_used.Prev];
                return least_recently_used.Key;
            }
        }
        #endregion

        #region Add()
        /// <summary>
        ///     O(1)
        ///     Adds the specified key and value to the dictionary.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="value">The value of the element to add. The value can be null for reference types.</param>
        /// <exception cref="ArgumentNullException">key is null.</exception>
        /// <exception cref="ArgumentException">An element with the same key already exists</exception>
        public void Add(in TKey key, in TValue value) {
            if(!this.TryAdd(key, value))
                throw new ArgumentException("An element with the same key already exists", nameof(key));
        }
        #endregion
        #region AddRange()
        /// <summary>
        ///     O(n)
        ///     Adds the items to the collection.
        ///     Keep in mind that means the added item MRU priority will be in reverse insertion order.
        /// </summary>
        public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> values) {
            foreach(var value in values)
                this.Add(value.Key, value.Value);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     O(1)
        ///     Removes the value with the specified key
        /// </summary>
        /// <returns>
        ///     true if the element is successfully found and removed; otherwise, false.
        ///     This method returns false if key is not found.
        /// </returns>
        /// <param name="key">The key of the element to remove.</param>
        /// <exception cref="ArgumentNullException">key is null.</exception>
        public bool Remove(in TKey key) {
            return this.TryRemove(key, out _);
        }
        #endregion
        #region RemoveRange()
        /// <summary>
        ///     O(n)
        /// </summary>
        public void RemoveRange(IEnumerable<TKey> keys) {
            foreach(var key in keys)
                this.Remove(key);
        }
        #endregion
        #region Clear()
        /// <summary>
        ///     O(n)
        ///     Removes all keys and values.
        /// </summary>
        /// <remarks>
        ///     This will evict all the entries in no particular ordering.
        /// </remarks>
        public void Clear() {
            // make sure to clear the keys as they could contain pointers
            int max = this.Count;
            for(int i = 0; i < max; i++) {
                ref var node = ref m_nodes[i];
                
                //var key    = node.Key;
                //var value  = m_dict[key].Value;

                node.Key = default;
                
                //this.OnItemEvicted(key, value);
            }

            m_dict.Clear();
            
            this.Init();
            this.Count = 0;
        }
        #endregion

        #region TryAdd()
        /// <summary>
        ///     O(1)
        ///     Attempts to add the specified key and value.
        /// </summary>
        /// <returns>
        ///     true if the key/value pair was added successfully. 
        ///     If the key already exists, this method returns false.
        /// </returns>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="value">The value of the element to add. The value can be a null reference for reference types.</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool TryAdd(in TKey key, in TValue value) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(m_dict.TryGetValue(key, out var nodePointer))
                return false;

            if(this.Count >= this.Capacity) {
                // evict LRU
                var lru_key = this.LeastRecentlyUsed;
                if(this.TryRemove(lru_key, out var lru_value))
                    this.OnItemEvicted(lru_key, lru_value);
            }

            // note: if count==0, then m_mostRecentlyUsedIndex=0, and m_nodes[0].Prev/Next = 0

            // always add at the end
            nodePointer.Index = this.Count;
            nodePointer.Value = value;
            
            ref var mru  = ref m_nodes[m_mostRecentlyUsedIndex];
            ref var _new = ref m_nodes[nodePointer.Index];
            
            _new.Key               = key;
            _new.Next              = m_mostRecentlyUsedIndex;
            _new.Prev              = mru.Prev;
            m_nodes[mru.Prev].Next = nodePointer.Index;
            mru.Prev               = nodePointer.Index;

            m_dict.Add(key, nodePointer);
            m_mostRecentlyUsedIndex = nodePointer.Index;

            this.Count++;
            return true;
        }
        #endregion
        #region TryRemove()
        /// <summary>
        ///     O(1)
        ///     Attempts to remove and return the value with the specified key
        /// </summary>
        /// <returns>
        ///     true if an object was removed successfully; otherwise, false.
        /// </returns>
        /// <param name="key">The key of the element to remove and return.</param>
        /// <param name="value">When this method returns, value contains the object removed or the default value of if the operation failed.</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool TryRemove(in TKey key, out TValue value) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(!m_dict.TryGetValue(key, out var nodePointer)) {
                value = default;
                return false;
            }

            value = nodePointer.Value;
            m_dict.Remove(key);

            ref var old = ref m_nodes[nodePointer.Index];
            
            // remove from circular double linked list
            m_nodes[old.Prev].Next = old.Next;
            m_nodes[old.Next].Prev = old.Prev;
            old.Key                = default;

            if(m_mostRecentlyUsedIndex == nodePointer.Index)
                m_mostRecentlyUsedIndex = old.Next;

            if(nodePointer.Index < this.Count - 1) {
                // this code is a lot more complex than expected, mostly because we need to make no entries are at this.Count(+) in m_nodes[]
                // this is because we dont want to search for what index are available when adding a new item

                ref var last = ref m_nodes[this.Count - 1];

                // copy last unto the deleted entry
                m_nodes[nodePointer.Index] = m_nodes[this.Count - 1];
                if(m_mostRecentlyUsedIndex == this.Count - 1)
                    m_mostRecentlyUsedIndex = nodePointer.Index;

                var x = m_dict[last.Key];
                x.Index = nodePointer.Index;
                m_dict[last.Key] = x;

                last.Key = default;
                m_nodes[last.Prev].Next = nodePointer.Index;
                m_nodes[last.Next].Prev = nodePointer.Index;
            } else if(this.Count == 1)
                this.Init();

            this.Count--;
            return true;
        }
        #endregion
        #region TryGetValue()
        /// <summary>
        ///     O(1)
        ///     Gets the value associated with the specified key.
        /// </summary>
        /// <returns>
        ///     true if the key was found 
        ///     otherwise, false.
        /// </returns>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">When this method returns, value contains the object with the specified key or the default value of, if the operation failed.</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool TryGetValue(in TKey key, out TValue value) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(!m_dict.TryGetValue(key, out var nodePointer)) {
                value = default;
                return false;
            }
            
            value = nodePointer.Value;
            this.BumpValidAndExisting(nodePointer.Index);
            return true;
        }
        #endregion
        #region ContainsKey()
        /// <summary>
        ///     O(1)
        ///     Determines whether the class contains the specified key.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     true if contains an element with the specified key; otherwise, false.
        /// </returns>
        /// <param name="key">The key to locate</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool ContainsKey(in TKey key) {
            return m_dict.ContainsKey(key);
        }
        #endregion
        #region ContainsValue()
        /// <summary>
        ///     O(n)
        ///     Determines whether the cache contains a specific value.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     true if the cache contains an element with the specified value; otherwise, false.
        /// </returns>
        /// <param name="value">The value to locate. The value can be null for reference types.</param>
        public bool ContainsValue(in TValue value) {
            var comparer = EqualityComparer<TValue>.Default;

            foreach(var item in m_dict)
                if(comparer.Equals(item.Value.Value, value))
                    return true;

            return false;
        }
        #endregion

        #region GetOrAdd()
        /// <summary>
        ///     O(1)
        ///     Adds a key/value pair if the key does not already exist.
        /// </summary>
        /// <returns>
        ///     The value for the key. This will be either the existing value for the key if
        ///     the key is already in the dictionary, or the new value for the key as returned
        ///     by valueFactory if the key was not in the dictionary.
        /// </returns>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="value">the value to be added, if the key does not already exist</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public TValue GetOrAdd(in TKey key, in TValue value) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(this.TryGetValue(key, out var res))
                return res;

            this.TryAdd(key, value);
            return value;
        }
        /// <summary>
        ///     O(1)
        ///     Adds a key/value pair if the key does not already exist.
        /// </summary>
        /// <returns>
        ///     The value for the key. This will be either the existing value for the key if
        ///     the key is already in the dictionary, or the new value for the key as returned
        ///     by valueFactory if the key was not in the dictionary.
        /// </returns>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="valueFactory">The function used to generate a value for the key</param>
        /// <exception cref="ArgumentNullException">key is null -or- valueFactory is null</exception>
        public TValue GetOrAdd(in TKey key, Func<TKey, TValue> valueFactory) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));
            if(valueFactory == null)
                throw new ArgumentNullException(nameof(valueFactory));

            if(this.TryGetValue(key, out var res))
                return res;

            var value = valueFactory(key);
            this.TryAdd(key, value);
            return value;
        }
        #endregion
        #region AddOrUpdate()
        /// <summary>
        ///     O(1)
        ///     Adds a key/value pair if the key does not already exist, or updates a key/value pair if the key already exists.
        /// </summary>
        /// <param name="key">The key to be added or whose value should be updated</param>
        /// <param name="value">The value to be added for an absent key</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public void AddOrUpdate(in TKey key, in TValue value) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            // if update
            if(m_dict.TryGetValue(key, out var nodePointer)) {
                nodePointer.Value = value;
                m_dict[key] = nodePointer;
                this.BumpValidAndExisting(nodePointer.Index);
                return;
            }
            
            this.TryAdd(key, value);
        }
        /// <summary>
        ///     O(1)
        ///     Adds a key/value pair if the key does not already exist, or updates a key/value pair if the key already exists.
        /// </summary>
        /// <returns>
        ///     The new value for the key. This will be either be the result of addValueFactory (if the key was absent) 
        ///     or the result of updateValueFactory (if the key was present).
        /// </returns>
        /// <param name="key">The key to be added or whose value should be updated</param>
        /// <param name="addValue">The value to be added for an absent key</param>
        /// <param name="updateValueFactory">The function used to generate a new value for an existing key based on the key's existing value</param>
        /// <exception cref="ArgumentNullException">key is null -or- updateValueFactory is null.</exception>
        public TValue AddOrUpdate(in TKey key, in TValue addValue, Func<TKey, TValue, TValue> updateValueFactory) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));
            if(updateValueFactory == null)
                throw new ArgumentNullException(nameof(updateValueFactory));

            // if update
            if(m_dict.TryGetValue(key, out var nodePointer)) {
                var newValue = updateValueFactory(key, nodePointer.Value);
                nodePointer.Value = newValue;
                m_dict[key] = nodePointer;
                this.BumpValidAndExisting(nodePointer.Index);
                return newValue;
            }
            
            this.TryAdd(key, addValue);
            return addValue;
        }
        #endregion

        #region GetKeysInLRUOrder()
        /// <summary>
        ///     O(n)
        ///     Returns the keys in LRU (Least Recently Used) ordering.
        ///     Does not bump MRU of returned item.
        /// </summary>
        public IEnumerable<TKey> GetKeysInLRUOrder() {
            int max   = this.Count;
            var index = m_nodes[m_mostRecentlyUsedIndex].Prev;
            for(int i = 0; i < max; i++) {
                var current = m_nodes[index];
                yield return current.Key;
                index = current.Prev;
            }
        }
        #endregion
 
        #region Bump()
        /// <summary>
        ///     O(1)
        ///     Moves the item on top of most recently used list.
        ///     Returns false if not found.
        /// </summary>
        /// <param name="key">The key to locate</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool Bump(in TKey key) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(!m_dict.TryGetValue(key, out var nodePointer))
                return false;
            
            this.BumpValidAndExisting(nodePointer.Index);
            return true;
        }
        /// <summary>
        ///     O(1)
        ///     Moves the item on top of most recently used list.
        /// </summary>
        private void BumpValidAndExisting(int index) {
            if(index == m_mostRecentlyUsedIndex) 
                return;
            
            ref var old = ref m_nodes[index];
            
            // remove old MRU from circular double linked list
            m_nodes[old.Prev].Next = old.Next;
            m_nodes[old.Next].Prev = old.Prev;

            ref var mru  = ref m_nodes[m_mostRecentlyUsedIndex];
            
            // bump up
            old.Prev               = mru.Prev;
            old.Next               = m_mostRecentlyUsedIndex;
            m_nodes[mru.Prev].Next = index;
            mru.Prev               = index;

            m_mostRecentlyUsedIndex = index;
        }
        #endregion
        #region SetCapacity()
        /// <summary>
        ///     O(n)
        ///     Sets a new capacity.
        ///     This will evict all the least recently used entries if the capacity is smaller.
        /// </summary>
        /// <param name="capacity">The new capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException">Capacity must be > 0.</exception>
        public void SetCapacity(int capacity) {
            if(capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0.");

            // if we need to evict some entries
            if(capacity < this.Count) {
                this.SetCapacityDownsizeRare(capacity);
                return;
            }

            Array.Resize(ref m_nodes, capacity);
            this.Capacity = capacity;
        }
        private void SetCapacityDownsizeRare(int capacity) {
            // if we need to evict some entries, then we need to rebuild the entire nodes to respect the MRU ordering
            var new_nodes = new Node[capacity];
            var visited   = new bool[this.Count];

            var index = m_mostRecentlyUsedIndex;
            for(int i = 0; i < capacity; i++) {
                visited[index] = true;

                var current = m_nodes[index];

                new_nodes[i] = new Node() {
                    Prev = i - 1,
                    Next = i + 1,
                    Key  = current.Key,
                };

                var node_pointer = m_dict[current.Key];
                node_pointer.Index = i;
                m_dict[current.Key] = node_pointer;

                index = current.Next;
            }

            new_nodes[0].Prev            = capacity - 1;
            new_nodes[capacity - 1].Next = 0;

            var old_nodes = m_nodes;
            var old_count = this.Count;

            m_nodes       = new_nodes;
            this.Count    = capacity;
            this.Capacity = capacity;

            // then do the evictions for all entries that arent in the MRU
            for(int i = 0; i < old_count; i++) {
                if(!visited[i]) {
                    var evicted_key   = old_nodes[i].Key;
                    var evicted_value = m_dict[evicted_key].Value;

                    //old_nodes[i].Key = default;
                    if(m_dict.Remove(evicted_key))
                        this.OnItemEvicted(evicted_key, evicted_value);
                }
            }
        }
        #endregion

        #region CopyTo()
        /// <summary>
        ///     O(n)
        ///     Copies the keys to a new array.
        ///     Returns the items in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     A new array containing a snapshot of keys.
        /// </returns>
        public void CopyTo(TKey[] array, int arrayIndex) {
            int max   = this.Count;
            var index = m_mostRecentlyUsedIndex;
            for(int i = 0; i < max; i++) {
                var current = m_nodes[index];
                array[i + arrayIndex] = current.Key;
                index = current.Next;
            }
        }
        /// <summary>
        ///     O(n)
        ///     Copies the keys to a new array.
        ///     Returns the items in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     A new array containing a snapshot of keys.
        /// </returns>
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
            int max   = this.Count;
            var index = m_mostRecentlyUsedIndex;
            for(int i = 0; i < max; i++) {
                var current = m_nodes[index];
                array[i + arrayIndex] = new KeyValuePair<TKey, TValue>(current.Key, m_dict[current.Key].Value);
                index = current.Next;
            }

            //int i = 0;
            //foreach(var item in m_dict)
            //    res[i++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value.Value);
        }
        #endregion
        #region ToArray()
        /// <summary>
        ///     O(n)
        ///     Copies the key and value pairs to a new array.
        ///     Returns the items in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     A new array containing a snapshot of key and value pairs.
        /// </returns>
        public KeyValuePair<TKey, TValue>[] ToArray() {
            var res = new KeyValuePair<TKey, TValue>[this.Count];
            this.CopyTo(res, 0);
            return res;
        }
        #endregion

        #region protected virtual OnItemEvicted()
        /// <summary>
        ///     Invoked whenever an item gets evicted 
        ///     (ie: removed without Remove()/Clear() called).
        ///     
        ///     Add()/SetCapacity() can invoke this, if this.Count > this.Capacity.
        /// </summary>
        /// <param name="key">The key being evicted.</param>
        /// <param name="value">The value being evicted.</param>
        protected virtual void OnItemEvicted(in TKey key, in TValue value) {
            // intentionally empty
        }
        #endregion

        #region private Init()
        private void Init() {
            // special case: first item must always have its prev/next set to itself
            m_mostRecentlyUsedIndex = 0; // important, this lets add() behave properly

            ref var first = ref m_nodes[0];
            first.Next = 0;
            first.Prev = 0;
        }
        #endregion

        #region explicit interface(s) implementations
        IEnumerator IEnumerable.GetEnumerator() {
            return this.Items.GetEnumerator();
        }
 
        object ICollection.SyncRoot => this;
        bool ICollection.IsSynchronized => false;
 
        void ICollection.CopyTo(Array array, int arrayIndex) {
            if(array == null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
 
            foreach(var item in this.Items)
                array.SetValue(item, arrayIndex++);
        }
 
        /// <summary>
        ///     O(1)
        /// </summary>
        /// <exception cref="ArgumentException">An element with the same key already exists</exception>
        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) {
            this.Add(key, value);
        }
 
        /// <summary>
        ///     O(n)
        /// </summary>
        ICollection<TKey> IDictionary<TKey, TValue>.Keys {
            get {
                var res = new List<TKey>(this.Count);
                foreach(var item in this.Keys)
                    res.Add(item);
                return res;
            }
        }
 
        /// <summary>
        ///     O(n)
        /// </summary>
        ICollection<TValue> IDictionary<TKey, TValue>.Values {
            get {
                var res = new List<TValue>(this.Count);
                foreach(var item in this.Values)
                    res.Add(item);
                return res;
            }
        }
 
        /// <summary>
        ///     O(1)
        /// </summary>
        /// <exception cref="ArgumentException">An element with the same key already exists</exception>
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) {
            this.Add(item.Key, item.Value);
        }
 
        /// <summary>
        ///     O(1)
        /// </summary>
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) {
            return this.Remove(item.Key);
        }
         
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() {
            return this.Items.GetEnumerator();
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
 
            foreach(var item in this.Items)
                array[arrayIndex++] = item;
        }

        bool IDictionary<TKey, TValue>.ContainsKey(TKey key) {
            return this.ContainsKey(key);
        }

        bool IDictionary<TKey, TValue>.Remove(TKey key) {
            return this.Remove(key);
        }

        bool IDictionary<TKey, TValue>.TryGetValue(TKey key, out TValue value) {
            return this.TryGetValue(key, out value);
        }

        TValue IDictionary<TKey, TValue>.this[TKey key] {
            get {
                return this[key];
            }
            set {
                this[key] = value;
            }
        }

        bool IReadOnlyDictionary<TKey, TValue>.ContainsKey(TKey key) {
            return this.ContainsKey(key);
        }

        bool IReadOnlyDictionary<TKey, TValue>.TryGetValue(TKey key, out TValue value) {
            return this.TryGetValue(key, out value);
        }

        TValue IReadOnlyDictionary<TKey, TValue>.this[TKey key] {
            get {
                return this[key];
            }
        }
        #endregion

        private struct NodePointer {
            public TValue Value;
            public int Index;
        }
        // use a struct in order to make efficient use of cache lines
        // basically we're forcing the items to be allocated nearby, since we know the capacity should mostly not change ever, 
        // which the default .NET allocator cannot know
        // also this saves 20 bytes per item vs using a class;
        //     "class Node{TKey,TValue,Prev,Next}" = 32 bytes + sizeof(TKey) + sizeof(TValue)    (because 16 bytes overhead)
        //     "struct NodePointer/Node"           = 12 bytes + sizeof(TKey) + sizeof(TValue)
        private struct Node {
            public int Prev;
            public int Next;
            public TKey Key;
        }
    }

    /// <summary>
    ///    Implements a MRU (Most Recently Used) HashSet with a limited number of entries.
    ///    Entries are evicted in Least-Recently-Used order.
    /// </summary>
    public class MRUCache<TKey> : ICollection, ICollection<TKey>, IEnumerable<TKey>, IEnumerable, IReadOnlyCollection<TKey> {
        private const int DEFAULT_CAPACITY = 256;

        private readonly Dictionary<TKey, int> m_dict;
        private Node[] m_nodes; // circular double linked list

        private int m_mostRecentlyUsedIndex;
 
        public int Count { get; private set; }
        public int Capacity { get; private set; }
 
        #region constructors
        public MRUCache(int capacity = DEFAULT_CAPACITY) : this(capacity, new Dictionary<TKey, int>(capacity)){ }
        public MRUCache(int capacity, IEqualityComparer<TKey> comparer) : this(capacity, new Dictionary<TKey, int>(capacity, comparer)){ }
        private MRUCache(int capacity, Dictionary<TKey, int> dict) {
            m_dict = dict;
            this.SetCapacity(capacity);

            this.Init();
            this.Count = 0;
        }
        #endregion
 
        #region Keys
        /// <summary>
        ///     Returns the keys in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned items.
        /// </summary>
        public IEnumerable<TKey> Keys {
            get {
                int max   = this.Count;
                var index = m_mostRecentlyUsedIndex;
                for(int i = 0; i < max; i++) {
                    var current = m_nodes[index];
                    yield return current.Key;
                    index = current.Next;
                }

                //foreach(var item in m_dict.Keys)
                //    yield return item;
            }
        }
        #endregion
 
        #region MostRecentlyUsed
        /// <summary>
        ///    O(1)
        ///    Does not bump MRU of returned items.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Count == 0</exception>
        public TKey MostRecentlyUsed {
            get {
                if(this.Count == 0)
                    throw new KeyNotFoundException("Collection is empty.");
                return m_nodes[m_mostRecentlyUsedIndex].Key;
            }
        }
        #endregion
        #region LeastRecentlyUsed
        /// <summary>
        ///    O(1)
        ///    Does not bump MRU of returned items.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Count == 0</exception>
        public TKey LeastRecentlyUsed {
            get {
                if(this.Count == 0)
                    throw new KeyNotFoundException("Collection is empty.");

                var most_recently_used  = m_nodes[m_mostRecentlyUsedIndex];
                var least_recently_used = m_nodes[most_recently_used.Prev];
                return least_recently_used.Key;
            }
        }
        #endregion

        #region Add()
        /// <summary>
        ///     O(1)
        ///     Adds the specified key to the hashset.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <exception cref="ArgumentNullException">key is null.</exception>
        /// <exception cref="ArgumentException">An element with the same key already exists</exception>
        public void Add(in TKey key) {
            if(!this.TryAdd(key))
                throw new ArgumentException("An element with the same key already exists", nameof(key));
        }
        #endregion
        #region AddRange()
        /// <summary>
        ///     O(n)
        ///     Adds the keys to the hashset.
        ///     Keep in mind that means the added item MRU priority will be in reverse insertion order.
        /// </summary>
        public void AddRange(IEnumerable<TKey> keys) {
            foreach(var key in keys)
                this.Add(key);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     O(1)
        ///     Attempts to remove the specified key
        /// </summary>
        /// <returns>
        ///     true if an object was removed successfully; otherwise, false.
        /// </returns>
        /// <param name="key">The key of the element to remove and return.</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool Remove(in TKey key) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(!m_dict.TryGetValue(key, out var nodePointer))
                return false;

            m_dict.Remove(key);

            ref var old = ref m_nodes[nodePointer];
            
            // remove from circular double linked list
            m_nodes[old.Prev].Next = old.Next;
            m_nodes[old.Next].Prev = old.Prev;
            old.Key                = default;

            if(m_mostRecentlyUsedIndex == nodePointer)
                m_mostRecentlyUsedIndex = old.Next;

            if(nodePointer < this.Count - 1) {
                // this code is a lot more complex than expected, mostly because we need to make no entries are at this.Count(+) in m_nodes[]
                // this is because we dont want to search for what index are available when adding a new item

                ref var last = ref m_nodes[this.Count - 1];

                // copy last unto the deleted entry
                m_nodes[nodePointer] = m_nodes[this.Count - 1];
                if(m_mostRecentlyUsedIndex == this.Count - 1)
                    m_mostRecentlyUsedIndex = nodePointer;

                m_dict[last.Key] = nodePointer;

                last.Key = default;
                m_nodes[last.Prev].Next = nodePointer;
                m_nodes[last.Next].Prev = nodePointer;
            } else if(this.Count == 1)
                this.Init();

            this.Count--;
            return true;
        }
        #endregion
        #region RemoveRange()
        /// <summary>
        ///     O(n)
        /// </summary>
        public void RemoveRange(IEnumerable<TKey> keys) {
            foreach(var key in keys)
                this.Remove(key);
        }
        #endregion
        #region Clear()
        /// <summary>
        ///     O(n)
        ///     Removes all keys.
        /// </summary>
        /// <remarks>
        ///     This will evict all the entries in no particular ordering.
        /// </remarks>
        public void Clear() {
            // make sure to clear the keys as they could contain pointers
            int max = this.Count;
            for(int i = 0; i < max; i++) {
                ref var node = ref m_nodes[i];
                
                //var key    = node.Key;

                node.Key = default;
                
                //this.OnItemEvicted(key);
            }

            m_dict.Clear();
            
            this.Init();
            this.Count = 0;
        }
        #endregion

        #region TryAdd()
        /// <summary>
        ///     O(1)
        ///     Attempts to add the specified key.
        /// </summary>
        /// <returns>
        ///     true if the key/value pair was added successfully. 
        ///     If the key already exists, this method returns false.
        /// </returns>
        /// <param name="key">The key of the element to add.</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool TryAdd(in TKey key) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(m_dict.TryGetValue(key, out _))
                return false;

            if(this.Count >= this.Capacity) {
                // evict LRU
                var lru_key = this.LeastRecentlyUsed;
                if(this.Remove(lru_key))
                    this.OnItemEvicted(lru_key);
            }

            // note: if count==0, then m_mostRecentlyUsedIndex=0, and m_nodes[0].Prev/Next = 0

            // always add at the end
            var nodePointer = this.Count;
            
            ref var mru  = ref m_nodes[m_mostRecentlyUsedIndex];
            ref var _new = ref m_nodes[nodePointer];
            
            _new.Key               = key;
            _new.Next              = m_mostRecentlyUsedIndex;
            _new.Prev              = mru.Prev;
            m_nodes[mru.Prev].Next = nodePointer;
            mru.Prev               = nodePointer;

            m_dict.Add(key, nodePointer);
            m_mostRecentlyUsedIndex = nodePointer;

            this.Count++;
            return true;
        }
        #endregion
        #region Contains()
        /// <summary>
        ///     O(1)
        ///     Determines whether the class contains the specified key.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     true if contains an element with the specified key; otherwise, false.
        /// </returns>
        /// <param name="key">The key to locate</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool Contains(in TKey key) {
            return m_dict.ContainsKey(key);
        }
        #endregion

        #region GetKeysInLRUOrder()
        /// <summary>
        ///     O(n)
        ///     Returns the keys in LRU (Least Recently Used) ordering.
        ///     Does not bump MRU of returned item.
        /// </summary>
        public IEnumerable<TKey> GetKeysInLRUOrder() {
            int max   = this.Count;
            var index = m_nodes[m_mostRecentlyUsedIndex].Prev;
            for(int i = 0; i < max; i++) {
                var current = m_nodes[index];
                yield return current.Key;
                index = current.Prev;
            }
        }
        #endregion
 
        #region Bump()
        /// <summary>
        ///     O(1)
        ///     Moves the item on top of most recently used list.
        ///     Returns false if not found.
        /// </summary>
        /// <param name="key">The key to locate</param>
        /// <exception cref="ArgumentNullException">key is null</exception>
        public bool Bump(in TKey key) {
            if(key == null)
                throw new ArgumentNullException(nameof(key));

            if(!m_dict.TryGetValue(key, out var nodePointer))
                return false;
            
            this.BumpValidAndExisting(nodePointer);
            return true;
        }
        /// <summary>
        ///     O(1)
        ///     Moves the item on top of most recently used list.
        /// </summary>
        private void BumpValidAndExisting(int index) {
            if(index == m_mostRecentlyUsedIndex) 
                return;
            
            ref var old = ref m_nodes[index];
            
            // remove old MRU from circular double linked list
            m_nodes[old.Prev].Next = old.Next;
            m_nodes[old.Next].Prev = old.Prev;

            ref var mru  = ref m_nodes[m_mostRecentlyUsedIndex];
            
            // bump up
            old.Prev               = mru.Prev;
            old.Next               = m_mostRecentlyUsedIndex;
            m_nodes[mru.Prev].Next = index;
            mru.Prev               = index;

            m_mostRecentlyUsedIndex = index;
        }
        #endregion
        #region SetCapacity()
        /// <summary>
        ///     O(n)
        ///     Sets a new capacity.
        ///     This will evict all the least recently used entries if the capacity is smaller.
        /// </summary>
        /// <param name="capacity">The new capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException">Capacity must be > 0.</exception>
        public void SetCapacity(int capacity) {
            if(capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0.");

            // if we need to evict some entries
            if(capacity < this.Count) {
                this.SetCapacityDownsizeRare(capacity);
                return;
            }

            Array.Resize(ref m_nodes, capacity);
            this.Capacity = capacity;
        }
        private void SetCapacityDownsizeRare(int capacity) {
            // if we need to evict some entries, then we need to rebuild the entire nodes to respect the MRU ordering
            var new_nodes = new Node[capacity];
            var visited   = new bool[this.Count];

            var index = m_mostRecentlyUsedIndex;
            for(int i = 0; i < capacity; i++) {
                visited[index] = true;

                var current = m_nodes[index];

                new_nodes[i] = new Node() {
                    Prev = i - 1,
                    Next = i + 1,
                    Key  = current.Key,
                };

                m_dict[current.Key] = i;

                index = current.Next;
            }

            new_nodes[0].Prev            = capacity - 1;
            new_nodes[capacity - 1].Next = 0;

            var old_nodes = m_nodes;
            var old_count = this.Count;

            m_nodes       = new_nodes;
            this.Count    = capacity;
            this.Capacity = capacity;

            // then do the evictions for all entries that arent in the MRU
            for(int i = 0; i < old_count; i++) {
                if(!visited[i]) {
                    var evicted_key = old_nodes[i].Key;

                    //old_nodes[i].Key = default;
                    if(m_dict.Remove(evicted_key))
                        this.OnItemEvicted(evicted_key);
                }
            }
        }
        #endregion

        #region CopyTo()
        /// <summary>
        ///     O(n)
        ///     Copies the keys to a new array.
        ///     Returns the items in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     A new array containing a snapshot of keys.
        /// </returns>
        public void CopyTo(TKey[] array, int arrayIndex) {
            int max   = this.Count;
            var index = m_mostRecentlyUsedIndex;
            for(int i = 0; i < max; i++) {
                var current = m_nodes[index];
                array[i + arrayIndex] = current.Key;
                index = current.Next;
            }
        }
        #endregion
        #region ToArray()
        /// <summary>
        ///     O(n)
        ///     Copies the keys to a new array.
        ///     Returns the items in MRU (Most Recently Used) order.
        ///     Does not bump MRU of returned item.
        /// </summary>
        /// <returns>
        ///     A new array containing a snapshot of keys.
        /// </returns>
        public TKey[] ToArray() {
            var res = new TKey[this.Count];
            this.CopyTo(res, 0);
            return res;
        }
        #endregion

        #region protected virtual OnItemEvicted()
        /// <summary>
        ///     Invoked whenever an item gets evicted 
        ///     (ie: removed without Remove()/Clear() called).
        ///     
        ///     Add()/SetCapacity() can invoke this, if this.Count > this.Capacity.
        /// </summary>
        /// <param name="key">The key being evicted.</param>
        /// <param name="value">The value being evicted.</param>
        protected virtual void OnItemEvicted(in TKey key) {
            // intentionally empty
        }
        #endregion

        #region private Init()
        private void Init() {
            // special case: first item must always have its prev/next set to itself
            m_mostRecentlyUsedIndex = 0; // important, this lets add() behave properly

            ref var first = ref m_nodes[0];
            first.Next = 0;
            first.Prev = 0;
        }
        #endregion

        #region explicit interface(s) implementations
        IEnumerator IEnumerable.GetEnumerator() {
            return this.Keys.GetEnumerator();
        }
 
        object ICollection.SyncRoot => this;
        bool ICollection.IsSynchronized => false;
 
        void ICollection.CopyTo(Array array, int arrayIndex) {
            if(array == null)
                throw new ArgumentNullException(nameof(array));
            if(arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
 
            foreach(var item in this.Keys)
                array.SetValue(item, arrayIndex++);
        }

        public IEnumerator<TKey> GetEnumerator() {
            return this.Keys.GetEnumerator();
        }

        void ICollection<TKey>.Add(TKey item) {
            this.Add(item);
        }

        bool ICollection<TKey>.Contains(TKey item) {
            return this.Contains(item);
        }

        bool ICollection<TKey>.Remove(TKey item) {
            return this.Remove(item);
        }

        bool ICollection<TKey>.IsReadOnly => false;
        #endregion

        // use a struct in order to make efficient use of cache lines
        // basically we're forcing the items to be allocated nearby, since we know the capacity should mostly not change ever, 
        // which the default .NET allocator cannot know
        // also this saves 20 bytes per item vs using a class;
        //     "class Node{TKey,Prev,Next}"   = 32 bytes + sizeof(TKey)     (because 16 bytes overhead)
        //     "struct Node/Dict<TKey,int>"   = 12 bytes + sizeof(TKey) 
        private struct Node {
            public int Prev;
            public int Next;
            public TKey Key;
        }
    }
}
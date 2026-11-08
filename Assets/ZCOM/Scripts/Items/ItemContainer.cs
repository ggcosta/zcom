namespace Zcom
{
    /// <summary>
    /// A fixed number of item slots (backpack, weapon loadout, a crate). Handles stacking, merging, swapping, splitting.
    /// </summary>
    public class ItemContainer
    {
        public readonly string name;
        public readonly ItemStack[] slots;
        readonly System.Func<ItemId, bool> filter;

        /// <summary>Bumped on every change so UI can redraw only when needed.</summary>
        public int Version { get; private set; }

        public ItemContainer(string name, int size, System.Func<ItemId, bool> filter = null)
        {
            this.name = name;
            slots = new ItemStack[size];
            this.filter = filter;
        }

        public int Size => slots.Length;
        public ItemStack this[int i] => slots[i];
        public bool Accepts(ItemId id) => id != ItemId.None && (filter == null || filter(id));
        public void Touch() => Version++;

        public bool IsEmpty
        {
            get
            {
                foreach (var s in slots) if (!s.IsEmpty) return false;
                return true;
            }
        }

        public int CountOf(ItemId id)
        {
            int n = 0;
            foreach (var s in slots) if (s.id == id) n += s.count;
            return n;
        }

        public int FirstEmpty()
        {
            for (int i = 0; i < slots.Length; i++) if (slots[i].IsEmpty) return i;
            return -1;
        }

        public void Set(int i, ItemStack s)
        {
            slots[i] = s.IsEmpty ? ItemStack.Empty : s;
            Version++;
        }

        /// <summary>Adds as much as fits (tops up existing stacks first). Returns the amount that did not fit.</summary>
        public int Add(ItemStack s)
        {
            if (s.IsEmpty) return 0;
            if (!Accepts(s.id)) return s.count;
            int left = s.count;
            int max = Items.Get(s.id).maxStack;
            for (int i = 0; i < slots.Length && left > 0; i++)
            {
                if (slots[i].id != s.id || slots[i].count >= max) continue;
                int n = System.Math.Min(left, max - slots[i].count);
                slots[i].count += n;
                left -= n;
            }
            for (int i = 0; i < slots.Length && left > 0; i++)
            {
                if (!slots[i].IsEmpty) continue;
                int n = System.Math.Min(left, max);
                slots[i] = new ItemStack(s.id, n);
                left -= n;
            }
            if (left != s.count) Version++;
            return left;
        }

        /// <summary>Removes up to amount from a slot and returns what was removed.</summary>
        public ItemStack Take(int i, int amount)
        {
            var s = slots[i];
            if (s.IsEmpty) return ItemStack.Empty;
            int n = amount < 0 ? s.count : System.Math.Min(amount, s.count);
            slots[i].count -= n;
            if (slots[i].count <= 0) slots[i] = ItemStack.Empty;
            Version++;
            return new ItemStack(s.id, n);
        }

        /// <summary>
        /// Drag-and-drop rule set. Empty target: move. Same item: merge (overflow stays). Different item: swap
        /// (only when moving the whole stack and both containers accept the other item).
        /// </summary>
        public static bool Move(ItemContainer from, int fi, ItemContainer to, int ti, int amount = -1)
        {
            if (from == to && fi == ti) return false;
            var src = from.slots[fi];
            if (src.IsEmpty || !to.Accepts(src.id)) return false;
            int n = amount < 0 ? src.count : System.Math.Min(amount, src.count);
            var dst = to.slots[ti];
            int max = Items.Get(src.id).maxStack;

            if (dst.IsEmpty)
            {
                to.slots[ti] = new ItemStack(src.id, n);
                from.slots[fi].count -= n;
            }
            else if (dst.id == src.id)
            {
                int m = System.Math.Min(n, max - dst.count);
                if (m <= 0) return false;
                to.slots[ti].count += m;
                from.slots[fi].count -= m;
            }
            else
            {
                if (n != src.count || !from.Accepts(dst.id)) return false;
                to.slots[ti] = src;
                from.slots[fi] = dst;
            }
            if (from.slots[fi].count <= 0) from.slots[fi] = ItemStack.Empty;
            from.Version++;
            to.Version++;
            return true;
        }
    }
}

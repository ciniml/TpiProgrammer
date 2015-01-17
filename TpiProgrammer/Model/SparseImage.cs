using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TpiProgrammer.Model
{
    public class SparseImage<T> : IEnumerable<KeyValuePair<ulong, T>>
    {
        private readonly SortedDictionary<ulong, T> memory = new SortedDictionary<ulong, T>();

        public int Count
        {
            get { return this.memory.Count; }
        }

        public T DefaultValue { get; set; }

        public void Read(ulong start, IList<T> buffer, int offset, int count)
        {
            var defaultValue = this.DefaultValue;
            for (var index = 0; index < count; index++)
            {
                T value;
                buffer[offset + index] = this.memory.TryGetValue(start + (ulong) index, out value)
                    ? value
                    : defaultValue;
            }
        }

        public void Read(int start, IList<T> buffer, int offset, int count)
        {
            this.Read((ulong) start, buffer, offset, count);
        }

        public void Write(ulong start, IList<T> buffer, int offset, int count)
        {
            for (var index = 0; index < count; index++)
            {
                this.memory[start + (ulong) index] = buffer[offset + index];
            }
        }

        public void Write(int start, IList<T> buffer, int offset, int count)
        {
            this.Write((ulong)start, buffer, offset, count);
        }

        public T this[int address]
        {
            get { return this[(ulong) address]; }
            set { this[(ulong)address] = value; }
        }
        public T this[ulong address]
        {
            get
            {
                T value;
                return this.memory.TryGetValue(address, out value)
                    ? value
                    : this.DefaultValue;
            }
            set { this.memory[address] = value; }
        }

        public IEnumerator<KeyValuePair<ulong, T>> GetEnumerator()
        {
            return this.memory.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) this.memory).GetEnumerator();
        }
    }
}

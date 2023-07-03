using System.Collections.Generic;
using UnityEngine;

namespace MightyTerrainMesh
{
    public static class MTLog
    {
        public static void LogError(object message)
        {
            Debug.LogError(message);
        }
    }

    //array
    public class MTArray<T>
    {
        public T[] Data;
        private HashSet<T> _data = new HashSet<T>();
        public int Length { get; private set; }

        public MTArray(int len)
        {
            Reallocate(len);
        }

        private void Reallocate(int len)
        {
            if (Data != null && len < Data.Length)
                return;
            Data = new T[len];
            Length = 0;
            _data.Clear();
        }

        public void Reset()
        {
            Length = 0;
            _data.Clear();
        }

        public void Add(T item)
        {
            if (Data == null || Length >= Data.Length)
            {
                MTLog.LogError("MTArray overflow : " + typeof(T));
            }

            Data[Length] = item;
            _data.Add(item);
            ++Length;
        }

        public bool Contains(T item)
        {
            return _data.Contains(item);
        }
    }
}
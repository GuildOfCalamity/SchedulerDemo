﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SchedulerDemo.Support
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "File name is correct, ignore generics")]
    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        private object _collectionChangedLock = new object();
        private bool _suppressNotification;

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            lock (_collectionChangedLock)
            {
                if (!_suppressNotification)
                {
                    base.OnCollectionChanged(e);
                }
            }
        }

        public void AddRange(IEnumerable<T> list)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            _suppressNotification = true;

            foreach (T item in list)
            {
                Add(item);
            }

            _suppressNotification = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void AddWithoutNotification(T item)
        {
            lock (_collectionChangedLock)
            {
                _suppressNotification = true;
                Add(item);
            }
        }

        public void ReleaseNotification()
        {
            lock (_collectionChangedLock)
            {
                _suppressNotification = false;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        public void ClearWithoutNotification()
        {
            lock (_collectionChangedLock)
            {
                _suppressNotification = true;
                Clear();
            }
        }
    }
}

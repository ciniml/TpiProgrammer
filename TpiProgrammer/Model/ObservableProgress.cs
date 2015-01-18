using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TpiProgrammer.Model
{
    public class ObservableProgress<T> : IObservable<T>, IProgress<T>
    {
        private readonly Subject<T> subject = new Subject<T>();
        public void Report(T value)
        {
            this.subject.OnNext(value);
        }
        public IDisposable Subscribe(IObserver<T> observer)
        {
            return this.subject.Subscribe(observer);
        }
    }

}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleApplication4;

namespace ConsoleApplication4
{
    public abstract class Message
    {
        public Guid Correlation { get; set; }
    }

    public interface IHandle<T> where T : Message
    {
        void Handle(T msg);
    }

    public interface IPublisher
    {
        void Publish(Message msg);
    }

    public interface IProcessor
    {
        bool Process();
    }

    public interface IBus : IPublisher
    {

    }

    class TaskBridge
    {
        private readonly IBus _bus;

        public TaskBridge(IBus bus)
        {
            _bus = bus;
        }

        public Task Create<TRequest, TResponse>(Func<TRequest> createRequest)
            where TRequest : Message
            where TResponse : Message
        {
            return Create<TRequest, TResponse, object>(createRequest, _ => null);
        }

        public Task<TResult> Create<TRequest, TResponse, TResult>(
            Func<TRequest> createRequest,
            Func<TResponse, TResult> createResult
            )
            where TRequest : Message
            where TResponse : Message
        {
            var tcs = new TaskCompletionSource<TResult>();
            var request = createRequest();
            var correlation = request.Correlation;
            _bus.Subscribe(correlation,new CompletionHandler<TResult, TResponse>(_bus, tcs, createResult));
            _bus.Publish(request);
            return tcs.Task;
        }

        class CompletionHandler<TResult, TResponse> : IHandle<TResponse> where TResponse : Message
        {
            private readonly IBus _bus;
            private readonly TaskCompletionSource<TResult> _tcs;
            private readonly Func<TResponse, TResult> _createResult;

            public CompletionHandler(IBus bus, TaskCompletionSource<TResult> tcs, Func<TResponse, TResult> createResult)
            {
                _bus = bus;
                _tcs = tcs;
                _createResult = createResult;
            }

            public void Handle(TResponse msg)
            {
                _bus.Unsubscribe(msg.Correlation, this);
                _tcs.TrySetResult(_createResult(msg));
            }
        }
    }


    public class CorrelationManager :
    IHandle<CorrelationManager.SubscribeTo>,
    IHandle<CorrelationManager.Unsubscribefrom>
    {
        private readonly IBus _bus;
        private readonly Dictionary<Type, IHandleCorrelations> _correlationManagers;

        public CorrelationManager(IBus bus)
        {
            _bus = bus;
            _correlationManagers = new Dictionary<Type, IHandleCorrelations>();
        }

        public static Message Subscribe<T>(Guid correlation, IHandle<T> handler) where T : Message
        {
            return new SubscribeTo(typeof(T), correlation,
                CreateTypedCorrelationManager<T>,
                handler
                );
        }

        private static IHandleCorrelations CreateTypedCorrelationManager<T>(IBus bus) where T : Message
        {
            var cm = new TypedCorrelationManager<T>();
            bus.Subscribe(cm);
            return cm;
        }

        void IHandle<SubscribeTo>.Handle(SubscribeTo msg)
        {
            IHandleCorrelations correlationManager;
            if (!_correlationManagers.TryGetValue(msg.Type, out correlationManager))
            {
                correlationManager = msg.CreateCorrelationManager(_bus);
                _correlationManagers.Add(msg.Type, correlationManager);
            }

            correlationManager.Handle(msg);
        }

        public static Message Unsubscribe<T>(Guid correlation, IHandle<T> handler) where T : Message
        {
            return new Unsubscribefrom(typeof(T), correlation, handler);
        }

        void IHandle<Unsubscribefrom>.Handle(Unsubscribefrom msg)
        {
            IHandleCorrelations correlationManager;
            if (_correlationManagers.TryGetValue(msg.Type, out correlationManager))
            {
                correlationManager.Handle(msg);
            }
        }

        class TypedCorrelationManager<T> :
            IHandleCorrelations,
            IHandle<T> where T : Message
        {
            private readonly Dictionary<Guid, HashSet<IHandle<T>>> _subscriptions;

            public TypedCorrelationManager()
            {
                _subscriptions = new Dictionary<Guid, HashSet<IHandle<T>>>();
            }
            public void Handle(SubscribeTo msg)
            {
                HashSet<IHandle<T>> handlers;
                if (!_subscriptions.TryGetValue(msg.CorrelationValue, out handlers))
                {
                    handlers = new HashSet<IHandle<T>>();
                    _subscriptions.Add(msg.CorrelationValue, handlers);
                }
                var handler = (IHandle<T>)msg.Handler;
                handlers.Add(handler);
            }

            public void Handle(Unsubscribefrom msg)
            {
                HashSet<IHandle<T>> handlers;
                if (_subscriptions.TryGetValue(msg.CorrelationValue, out handlers))
                {
                    handlers.Remove((IHandle<T>)msg.Handler);
                }
            }

            public void Handle(T msg)
            {
                HashSet<IHandle<T>> handlers;
                if (_subscriptions.TryGetValue(msg.Correlation, out handlers))
                    foreach (var handler in handlers)
                        handler.Handle(msg);
            }
        }

        interface IHandleCorrelations
        {
            void Handle(SubscribeTo msg);
            void Handle(Unsubscribefrom msg);
        }

        class SubscribeTo : Message
        {
            public readonly Type Type;
            public readonly Guid CorrelationValue;
            public readonly Func<IBus, IHandleCorrelations> CreateCorrelationManager;
            public readonly object Handler;

            public SubscribeTo(Type type, Guid correlationValue, Func<IBus, IHandleCorrelations> createCorrelationManager, object handler)
            {
                Type = type;
                CorrelationValue = correlationValue;
                CreateCorrelationManager = createCorrelationManager;
                Handler = handler;
            }
        }

        class Unsubscribefrom : Message
        {
            public readonly Type Type;
            public readonly Guid CorrelationValue;
            public readonly object Handler;

            public Unsubscribefrom(Type type, Guid correlationValue, object handler)
            {
                Type = type;
                CorrelationValue = correlationValue;
                Handler = handler;
            }
        }

    }

    public static class BusExtensions
    {
        public static void Subscribe<T>(this IBus bus, IHandle<T> handler) where T : Message
        {
            bus.Publish(Dispatcher.Subscribe<T>(handler));
        }

        public static Action SubscribeWithUnsubscribe<T>(this IBus bus, IHandle<T> handler) where T : Message
        {
            bus.Subscribe(handler);
            return () => bus.Unsubscribe(handler);
        }

        public static Action SubscribeWithUnsubscribe<T>(this IBus bus, Guid correlation,
            IHandle<T> handler) where T : Message
        {
            bus.Subscribe(correlation, handler);
            return () => bus.Unsubscribe(correlation, handler);
        }

        public static void Subscribe<T>(this IBus bus, Guid correlation, IHandle<T> handler)
            where T : Message
        {
            bus.Publish(CorrelationManager.Subscribe(correlation, handler));
        }

        public static void Unsubscribe<T>(this IBus bus, IHandle<T> handler) where T : Message
        {
            bus.Publish(Dispatcher.Unsubscribe(handler));
        }

        public static void Unsubscribe<T>(this IBus bus, Guid correlation, IHandle<T> handler)
            where T : Message
        {
            bus.Publish(CorrelationManager.Unsubscribe(correlation, handler));
        }

        private static readonly MethodInfo _subscribeMethod = typeof (Dispatcher).GetMethod("Subscribe",
            BindingFlags.Static | BindingFlags.Public);
        
        public static void SubscribeAll(this IBus bus, object instance)
        {
            if (instance == null) throw new ArgumentNullException("instance");
            var implemented = instance.GetType()
                .GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof (IHandle<>));
            foreach (var type in implemented)
            {
                var typedSubscribe = _subscribeMethod.MakeGenericMethod(type.GetGenericArguments());
                var msg = (Message)typedSubscribe.Invoke(null, new[] { instance });
                bus.Publish(msg);
            }
        }

        public static void PublishAll(this IBus bus, IEnumerable<Message> messages)
        {
            foreach (var message in messages)
            {
                bus.Publish(message);
            }
        }
    }
    

    class InMemoryBus : IBus
    {
        private readonly QueuedHandler _queue;
        private readonly ThreadBasedPump _thread;

        public InMemoryBus()
        {
            _queue = new QueuedHandler(new Dispatcher());
            _thread = new ThreadBasedPump(_queue);
        }
        public void Publish(Message msg)
        {
            _queue.Handle(msg);
        }

        public void Start()
        {
            _thread.Start();
        }
    }

    class Dispatcher : IHandle<Dispatcher.SubscribeTo>, IHandle<Dispatcher.UnsubscribeFrom>, IHandle<Message>
    {
        private readonly Dictionary<Type, List<IWrappedHandler>> _handlers;

        public Dispatcher()
        {
            _handlers = new Dictionary<Type, List<IWrappedHandler>>();
            var s = (IHandle<Dispatcher.SubscribeTo>) this;
            s.Handle((SubscribeTo)Subscribe<SubscribeTo>(this));
            s.Handle((SubscribeTo)Subscribe<UnsubscribeFrom>(this));
        }
        public void Handle  (Message msg)
        {
            var msgType = msg.GetType();
            do
            {
                List<IWrappedHandler> handlers;
                if (_handlers.TryGetValue(msgType, out handlers))
                {
                    foreach (var handler in handlers)
                        handler.Handle(msg);
                }
                msgType = msgType.BaseType;
            } while (msgType != typeof (object) && msgType != null);
        }
        public static Message Subscribe<T>(IHandle<T> handler) where T : Message
        {
            return new SubscribeTo(typeof(T), handler, () => new WrappedHandler<T>(handler));
        }

        public static Message Unsubscribe<T>(IHandle<T> handler) where T : Message
        {
            return new UnsubscribeFrom(typeof(T), handler);
        }

        interface IWrappedHandler
        {
            void Handle(Message msg);
            bool IsSame(object other);
        }

        class WrappedHandler<T> : IWrappedHandler where T : Message
        {
            private readonly IHandle<T> _handler;

            public WrappedHandler(IHandle<T> handler)
            {
                _handler = handler;
            }

            public void Handle(Message msg)
            {
                _handler.Handle((T)msg);
            }

            public bool IsSame(object other)
            {
                return ReferenceEquals(_handler, other);
            }
        }

        class SubscribeTo : Message
        {
            private readonly Type _type;
            private readonly object _handler;
            private readonly Func<IWrappedHandler> _createHandler;

            public SubscribeTo(Type type, object handler, Func<IWrappedHandler> createHandler )
            {
                _type = type;
                _handler = handler;
                _createHandler = createHandler;
            }

            public Type Type
            {
                get { return _type; }
            }

            public object Handler
            {
                get { return _handler; }
            }

            public Func<IWrappedHandler> CreateHandler
            {
                get { return _createHandler; }
            }
        }

        class UnsubscribeFrom : Message
        {
            private readonly Type _type;
            private readonly object _handler;

            public UnsubscribeFrom(Type type, object handler)
            {
                _type = type;
                _handler = handler;
            }

            public Type Type
            {
                get { return _type; }
            }

            public object Handler
            {
                get { return _handler; }
            }
        }

        void IHandle<SubscribeTo>.Handle(SubscribeTo msg)
        {
            List<IWrappedHandler> handlers;
            if (!_handlers.TryGetValue(msg.Type, out handlers))
            {
                handlers = new List<IWrappedHandler>();
                _handlers.Add(msg.Type, handlers);
            }
            if (!handlers.Any(x => x.IsSame(msg.Handler)))
            {
                handlers.Add(msg.CreateHandler());
            }
        }

        void IHandle<UnsubscribeFrom>.Handle(UnsubscribeFrom msg)
        {
            List<IWrappedHandler> handlers;
            if (_handlers.TryGetValue(msg.Type, out handlers))
            {
                var handler = handlers.FirstOrDefault(x => x.IsSame(msg.Handler));
                handlers.Remove(handler);
            }
        }
    }

    class QueuedHandler : IHandle<Message>, IProcessor
    {
        private readonly IHandle<Message> _handler;
        private readonly ConcurrentQueue<Message> _queue;

        public QueuedHandler(IHandle<Message> handler)
        {
            _handler = handler;
            _queue = new ConcurrentQueue<Message>();
        }

        public void Handle(Message msg)
        {
            _queue.Enqueue(msg);
        }

        public bool Process()
        {
            Message msg;
            if (_queue.TryDequeue(out msg))
            {
                _handler.Handle(msg);
                return true;
            }

            return false;
        }
    }

    class ThreadBasedPump
    {
        private readonly IProcessor _processor;
        private readonly Thread _thread;

        public ThreadBasedPump(IProcessor processor)
        {
            _processor = processor;
            _thread = new Thread(Process);
            
        }

        public void Start()
        {
            _thread.Start();
        }

        private void Process()
        {
            while (true)
            {
                while (_processor.Process()){}
                Thread.Sleep(1);
            }
        }
    }
}

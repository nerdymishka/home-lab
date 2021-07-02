using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mettle.Sdk;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Mettle
{
    /// <summary>
    /// The default <see cref="IServiceProvider" /> implementation that ships
    /// with Mettle.
    /// </summary>
    public class SimpleServiceProvider : IServiceProvider, IDisposable
    {
        private readonly ScopedLifetime scopedLifetime;

        private ConcurrentDictionary<Type, Func<IServiceProvider, object?>>? factories =
            new ConcurrentDictionary<Type, Func<IServiceProvider, object?>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleServiceProvider"/> class.
        /// </summary>
        public SimpleServiceProvider()
        {
            this.scopedLifetime = new ScopedLifetime();
            this.factories.TryAdd(typeof(ScopedLifetime), _ => this.scopedLifetime);
            this.factories.TryAdd(typeof(IScopedServiceProviderLifetime), _ => new SimpleScopedServiceLifetime(this));
            this.AddScoped(typeof(ITestOutputHelper), _ => new TestOutputHelper());
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            this.Dispose(true);
        }

        /// <summary>
        /// Gets an instance of the specified service type.
        /// </summary>
        /// <param name="serviceType">The clr type that should be injected.</param>
        /// <returns>
        /// The instance of the service type; otherwise, <see langword="null" />.
        /// </returns>
        public object? GetService(Type serviceType)
        {
            if (this.factories is null)
                throw new ObjectDisposedException(nameof(SimpleServiceProvider));

            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));

            if (this.factories.TryGetValue(serviceType, out var factory))
                return factory(this);

            if (!serviceType.IsValueType)
            {
                var ctors = serviceType.GetConstructors();
                if (ctors.Length > 1 || ctors.Length == 0)
                    return null;

                var ctor = ctors[0];
                var parameters = ctor.GetParameters();

                if (parameters == null || parameters.Length == 0)
                {
                    this.factories.TryAdd(serviceType, _ => Activator.CreateInstance(serviceType));
                    return Activator.CreateInstance(serviceType);
                }

                this.factories.TryAdd(serviceType, (s) =>
                {
                    var args = new List<object>();
                    foreach (var p in parameters)
                    {
                        args.Add(s.GetService(p.ParameterType));
                    }

                    return Activator.CreateInstance(serviceType, args.ToArray());
                });

                var args = new List<object?>();
                foreach (var p in parameters)
                {
                    args.Add(this.GetService(p.ParameterType));
                }

                return Activator.CreateInstance(serviceType, args.ToArray());
            }

            return Activator.CreateInstance(serviceType);
        }

        /// <summary>
        /// Adds a single instance of the specified type.
        /// </summary>
        /// <param name="type">The clr type that should be injected.</param>
        /// <param name="instance">The live object instance.</param>
        public void AddSingleton(Type type, object instance)
        {
            if (this.factories is null)
                throw new ObjectDisposedException(nameof(SimpleServiceProvider));

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            this.scopedLifetime.SetState(type, instance);
            this.factories.TryAdd(type, s => instance);
        }

        /// <summary>
        /// Adds a single instance of the specified type.
        /// </summary>
        /// <param name="type">The clr type that should be injected.</param>
        /// <param name="activator">The factory method used to create the instance.</param>
        public void AddSingleton(Type type, Func<IServiceProvider, object> activator)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            this.AddScoped(type, activator);
        }

        /// <summary>
        /// Adds a service type that will be created once per lifetime scope
        /// when <see cref="GetService(Type)" /> is called.
        /// </summary>
        /// <param name="type">The clr type that should be injected.</param>
        /// <param name="activator">The factory method used to create the instance.</param>
        public void AddScoped(Type type, Func<IServiceProvider, object> activator)
        {
            if (this.factories is null)
                throw new ObjectDisposedException(nameof(SimpleServiceProvider));

            this.factories.TryAdd(type, s =>
            {
                var sl = s.GetService(typeof(ScopedLifetime));
                if (sl == null)
                    return null;

                var scope = (ScopedLifetime)sl;
                if (scope.ContainsKey(type))
                    return scope.GetState(type);

                var r = activator(s);
                scope.SetState(type, r);
                return r;
            });
        }

        /// <summary>
        /// Adds a service type that will be created each time <see cref="GetService(Type)" />
        /// is called.
        /// </summary>
        /// <param name="type">The clr type that should be injected.</param>
        public void AddTransient(Type type)
        {
            if (this.factories is null)
                throw new ObjectDisposedException(nameof(SimpleServiceProvider));

            this.factories.TryAdd(type, s => Activator.CreateInstance(type));
        }

        /// <summary>
        /// Adds a service type that will be created each time <see cref="GetService(Type)" />
        /// is called.
        /// </summary>
        /// <param name="type">The clr type that should be injected.</param>
        /// <param name="activator">The factory method used to create the instance.</param>
        public void AddTransient(Type type, Func<IServiceProvider, object> activator)
        {
            if (this.factories is null)
                throw new ObjectDisposedException(nameof(SimpleServiceProvider));

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (activator == null)
                throw new ArgumentNullException(nameof(activator));

            this.factories.TryAdd(type, activator);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && this.factories != null)
            {
                if (this.scopedLifetime == null)
                    return;

                foreach (var disposable in this.scopedLifetime.GetDisposables())
                    disposable.Dispose();

                this.scopedLifetime?.Clear();
                this.factories?.Clear();
                this.factories = null;
            }
        }

        public class ScopedLifetime
        {
            private readonly ConcurrentDictionary<Type, object> state =
                new ConcurrentDictionary<Type, object>();

            public bool ContainsKey(Type type)
            {
                return this.state.ContainsKey(type);
            }

            public void SetState(Type type, object instance)
            {
                this.state[type] = instance;
            }

            public object GetState(Type type)
            {
                this.state.TryGetValue(type, out var instance);
                return instance;
            }

            public void Clear()
            {
                this.state.Clear();
            }

            public IEnumerable<IDisposable> GetDisposables()
            {
                var list = new List<IDisposable>();
                foreach (var kv in this.state)
                {
                    if (kv.Value is IDisposable disposable)
                        list.Add(disposable);
                }

                return list;
            }
        }

#pragma warning disable S3881

        // this is a private class, so fully implementing IDispose is overkill.
        private class SimpleScopedServiceLifetime : IScopedServiceProviderLifetime
        {
            private readonly SimpleServiceProvider provider;

            public SimpleScopedServiceLifetime([NotNull] SimpleServiceProvider provider)
            {
                this.provider = provider ?? new SimpleServiceProvider();
                if (provider is null)
                    throw new ArgumentNullException(nameof(provider));

                if (provider.factories is null)
                    throw new ArgumentNullException(nameof(provider));

                var provider2 = new SimpleServiceProvider();
                if (provider2.factories is null)
                    return;

                foreach (var kv in provider.factories)
                {
                    if (kv.Key == typeof(ScopedLifetime))
                        continue;

                    if (provider2.factories.ContainsKey(kv.Key))
                        continue;

                    provider2.factories.TryAdd(kv.Key, kv.Value);
                }

                this.provider = provider;
            }

            public IServiceProvider Provider => this.provider;

            /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
            public void Dispose()
            {
                this.provider?.Dispose();
            }
        }
    }
}
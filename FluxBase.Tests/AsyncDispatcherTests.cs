﻿using FluxBase.Tests.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxBase.Tests
{
    [TestClass]
    public class AsyncDispatcherTests
    {
        private AsyncDispatcher _Dispatcher { get; set; }

        [TestInitialize]
        public void TestInitialize()
        {
            _Dispatcher = new AsyncDispatcher();
        }

        [TestMethod]
        public async Task RegisteringToDispatcherInvokesCallback()
        {
            var invocationCount = 0;

            _Dispatcher.Register(
                action => Interlocked.Increment(ref invocationCount)
            );
            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(1, invocationCount);
        }

        [TestMethod]
        public async Task RegisteringStoreToDispatcherInvokesHandler()
        {
            var invocationCount = 0;
            var store = new MockDelegateStore(
                action => Interlocked.Increment(ref invocationCount)
            );

            _Dispatcher.Register(store);
            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(1, invocationCount);
        }

        [TestMethod]
        public async Task RegisteringToDispatcherTwiceInvokesCallbackOnce()
        {
            var invocationCount = 0;

            void Callback(object action) => Interlocked.Increment(ref invocationCount);

            var firstRegistrationId = _Dispatcher.Register(Callback);
            var secondRegistrationId = _Dispatcher.Register(Callback);
            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(1, invocationCount);
            Assert.AreEqual(firstRegistrationId, secondRegistrationId);
        }

        [TestMethod]
        public async Task RegisteringStoreTwiceToDispatcherInvokesHandlerOnce()
        {
            var invocationCount = 0;
            var store = new MockDelegateStore(
                action => Interlocked.Increment(ref invocationCount)
            );

            var firstRegistrationId = _Dispatcher.Register(store);
            var secondRegistrationId = _Dispatcher.Register(store);
            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(1, invocationCount);
            Assert.AreEqual(firstRegistrationId, secondRegistrationId);
        }

        [TestMethod]
        public async Task UnregisteringFromDispatcherNoLongerInvokesCallback()
        {
            var invocationCount = 0;

            var registrationId = _Dispatcher.Register(
                action => Interlocked.Increment(ref invocationCount)
            );
            _Dispatcher.Unregister(registrationId);
            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(0, invocationCount);
        }

        [TestMethod]
        public async Task UnregisteringStoreFromDispatcherNoLongerInvokesHandler()
        {
            var invocationCount = 0;
            var store = new MockDelegateStore(
                action => Interlocked.Increment(ref invocationCount)
            );

            _Dispatcher.Register(store);
            _Dispatcher.Unregister(store);
            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(0, invocationCount);
        }

        [TestMethod]
        public void UnregisteringTwiceFromDispatcherReturnsFalseTheSecondTime()
        {
            var invocationCount = 0;

            var registrationId = _Dispatcher.Register(
                action => Interlocked.Increment(ref invocationCount)
            );
            Assert.IsTrue(_Dispatcher.Unregister(registrationId));

            Assert.IsFalse(_Dispatcher.Unregister(registrationId));
        }

        [TestMethod]
        public void UnregisteringStoreTwiceFromDispatcherReturnsFalseTheSecondTime()
        {
            var invocationCount = 0;
            var store = new MockDelegateStore(
                action => Interlocked.Increment(ref invocationCount)
            );

            var registrationId = _Dispatcher.Register(store);

            Assert.IsTrue(_Dispatcher.Unregister(store));
            Assert.IsFalse(_Dispatcher.Unregister(store));
        }

        [TestMethod]
        public async Task DispatchingNullPassesNull()
        {
            object actualAction = null;

            _Dispatcher.Register(
                action => Interlocked.Exchange(ref actualAction, action)
            );
            await _Dispatcher.DispatchAsync(null);

            Assert.IsNull(actualAction);
        }

        [TestMethod]
        public async Task DispatchPassesSameAction()
        {
            var expectedAction = new object();
            object actualAction = null;

            _Dispatcher.Register(
                action => Interlocked.Exchange(ref actualAction, action)
            );
            await _Dispatcher.DispatchAsync(expectedAction);

            Assert.AreSame(expectedAction, actualAction);
        }

        [TestMethod]
        public async Task WaitForBlocksUntilAwaitedHandlerCompletes()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            object secondSubscriptionId = null;
            var firstSubscriptionId = _Dispatcher.Register(
                action =>
                {
                    _Dispatcher.WaitFor(secondSubscriptionId);
                    invocationsList.Add(first);
                }
            );
            secondSubscriptionId = _Dispatcher.Register(
                action => invocationsList.Add(second)
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(2, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(first, invocationsList[1]);
        }

        [TestMethod]
        public async Task WaitForBlocksUntilHandlersThatThemselvesWaitAwaitsTheirCompletion()
        {
            const int chainedHandlersCount = 500;
            var registrationIds = new List<object>();
            var invocationsList = new List<string>();

            for (var index = 0; index < chainedHandlersCount; index++)
            {
                var indexCopy = index;
                registrationIds.Add(
                    _Dispatcher.Register(
                        action =>
                        {
                            if (indexCopy < chainedHandlersCount - 1)
                                _Dispatcher.WaitFor(registrationIds[indexCopy + 1]);
                            invocationsList.Add($"Blocked {indexCopy}");
                        }
                    )
                );
                _Dispatcher.Register(
                    action => invocationsList.Add($"Not blocked {indexCopy}")
                );
            }

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(chainedHandlersCount * 2, invocationsList.Count);
            for (var index = 0; index < chainedHandlersCount; index++)
            {
                Assert.AreEqual($"Blocked {chainedHandlersCount - index - 1}", invocationsList[index]);
                Assert.AreEqual($"Not blocked {index}", invocationsList[index + chainedHandlersCount]);
            }
        }

        [TestMethod]
        public async Task WaitForCausingDeadlockIsDetected()
        {
            object firstSubscriptionId = null;
            object secondSubscriptionId = null;
            firstSubscriptionId = _Dispatcher.Register(
                action => _Dispatcher.WaitFor(secondSubscriptionId)
            );
            secondSubscriptionId = _Dispatcher.Register(
                action => _Dispatcher.WaitFor(firstSubscriptionId)
            );

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _Dispatcher.DispatchAsync(null));

            Assert.AreEqual(
                new InvalidOperationException("Deadlock detected. Two handlers are waiting on each other (directly or indirectly) to complete.").Message,
                exception.Message
            );
        }

        [TestMethod]
        public async Task WaitForCausingDeadlockThroughChainedBlocksIsDetected()
        {
            const int chainedHandlersCount = 500;
            var registrationIds = new List<object>();

            for (var index = 0; index < chainedHandlersCount; index++)
            {
                var indexCopy = index;
                registrationIds.Add(
                    _Dispatcher.Register(
                        action => _Dispatcher.WaitFor(registrationIds[(indexCopy + 1) % chainedHandlersCount])
                    )
                );
            }

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _Dispatcher.DispatchAsync(null));

            Assert.AreEqual(
                new InvalidOperationException("Deadlock detected. Two handlers are waiting on each other (directly or indirectly) to complete.").Message,
                exception.Message
            );
        }

        [TestMethod]
        public async Task WaitForBlocksUntilAwaitedHandlerCompletesWithTwoSeparateDependencyChains()
        {
            const string first = "first";
            const string second = "second";
            const string third = "third";
            const string fourth = "fourth";
            var invocationsList = new List<string>();

            object secondSubscriptionId = null;
            object fourthSubscriptionId = null;
            var firstSubscriptionId = _Dispatcher.Register(
                action =>
                {
                    _Dispatcher.WaitFor(secondSubscriptionId);
                    invocationsList.Add(first);
                }
            );
            secondSubscriptionId = _Dispatcher.Register(
                action => invocationsList.Add(second)
            );
            var thirdSubscriptionId = _Dispatcher.Register(
                action =>
                {
                    _Dispatcher.WaitFor(fourthSubscriptionId);
                    invocationsList.Add(third);
                }
            );
            fourthSubscriptionId = _Dispatcher.Register(
                action => invocationsList.Add(fourth)
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(4, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(first, invocationsList[1]);
            Assert.AreEqual(fourth, invocationsList[2]);
            Assert.AreEqual(third, invocationsList[3]);
        }

        [TestMethod]
        public async Task WaitForDoesNotBlockIfHandlerWasAlreadyExecuted()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            var firstSubscriptionId = _Dispatcher.Register(
                action => invocationsList.Add(first)
            );
            var secondSubscriptionId = _Dispatcher.Register(
                action =>
                {
                    _Dispatcher.WaitFor(firstSubscriptionId);
                    invocationsList.Add(second);
                }
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(2, invocationsList.Count);
            Assert.AreEqual(first, invocationsList[0]);
            Assert.AreEqual(second, invocationsList[1]);
        }

        [TestMethod]
        public async Task WaitForDoesNotBlockIfHandlerWasUnregistered()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            var firstSubscriptionId = _Dispatcher.Register(
                action => invocationsList.Add(first)
            );
            var secondSubscriptionId = _Dispatcher.Register(
                action =>
                {
                    _Dispatcher.WaitFor(firstSubscriptionId);
                    invocationsList.Add(second);
                }
            );
            _Dispatcher.Unregister(firstSubscriptionId);

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(1, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
        }

        [TestMethod]
        public async Task WaitForStoreBlocksUntilAwaitedHandlerCompletes()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            object secondSubscriptionId = null;
            var firstSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action =>
                    {
                        _Dispatcher.WaitFor(secondSubscriptionId);
                        invocationsList.Add(first);
                    }
                )
            );
            secondSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => invocationsList.Add(second)
                )
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(2, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(first, invocationsList[1]);
        }

        [TestMethod]
        public async Task WaitForStoreBlocksUntilHandlersThatThemselvesWaitAwaitsTheirCompletion()
        {
            const int chainedHandlersCount = 500;
            var registrationIds = new List<object>();
            var invocationsList = new List<string>();

            for (var index = 0; index < chainedHandlersCount; index++)
            {
                var indexCopy = index;
                registrationIds.Add(
                    _Dispatcher.Register(
                        new MockDelegateStore(
                            action =>
                            {
                                if (indexCopy < chainedHandlersCount - 1)
                                    _Dispatcher.WaitFor(registrationIds[indexCopy + 1]);
                                invocationsList.Add($"Blocked {indexCopy}");
                            }
                        )
                    )
                );
                _Dispatcher.Register(
                    new MockDelegateStore(
                        action => invocationsList.Add($"Not blocked {indexCopy}")
                    )
                );
            }

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(chainedHandlersCount * 2, invocationsList.Count);
            for (var index = 0; index < chainedHandlersCount; index++)
            {
                Assert.AreEqual($"Blocked {chainedHandlersCount - index - 1}", invocationsList[index]);
                Assert.AreEqual($"Not blocked {index}", invocationsList[index + chainedHandlersCount]);
            }
        }

        [TestMethod]
        public async Task WaitForStoreCausingDeadlockIsDetected()
        {
            object firstSubscriptionId = null;
            object secondSubscriptionId = null;
            firstSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => _Dispatcher.WaitFor(secondSubscriptionId)
                )
            );
            secondSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => _Dispatcher.WaitFor(firstSubscriptionId)
                )
            );

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _Dispatcher.DispatchAsync(null));

            Assert.AreEqual(
                new InvalidOperationException("Deadlock detected. Two handlers are waiting on each other (directly or indirectly) to complete.").Message,
                exception.Message
            );
        }

        [TestMethod]
        public async Task WaitForStoreCausingDeadlockThroughChainedBlocksIsDetected()
        {
            const int chainedHandlersCount = 500;
            var registrationIds = new List<object>();

            for (var index = 0; index < chainedHandlersCount; index++)
            {
                var indexCopy = index;
                registrationIds.Add(
                    _Dispatcher.Register(
                        new MockDelegateStore(
                            action => _Dispatcher.WaitFor(registrationIds[(indexCopy + 1) % chainedHandlersCount])
                        )
                    )
                );
            }

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _Dispatcher.DispatchAsync(null));

            Assert.AreEqual(
                new InvalidOperationException("Deadlock detected. Two handlers are waiting on each other (directly or indirectly) to complete.").Message,
                exception.Message
            );
        }

        [TestMethod]
        public async Task WaitForStoreBlocksUntilAwaitedHandlerCompletesWithTwoSeparateDependencyChains()
        {
            const string first = "first";
            const string second = "second";
            const string third = "third";
            const string fourth = "fourth";
            var invocationsList = new List<string>();

            object secondSubscriptionId = null;
            object fourthSubscriptionId = null;
            var firstSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action =>
                    {
                        _Dispatcher.WaitFor(secondSubscriptionId);
                        invocationsList.Add(first);
                    }
                )
            );
            secondSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => invocationsList.Add(second)
                )
            );
            var thirdSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action =>
                    {
                        _Dispatcher.WaitFor(fourthSubscriptionId);
                        invocationsList.Add(third);
                    }
                )
            );
            fourthSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => invocationsList.Add(fourth)
                )
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(4, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(first, invocationsList[1]);
            Assert.AreEqual(fourth, invocationsList[2]);
            Assert.AreEqual(third, invocationsList[3]);
        }

        [TestMethod]
        public async Task WaitForStoreDoesNotBlockIfHandlerWasAlreadyExecuted()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            var firstSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => invocationsList.Add(first)
                )
            );
            var secondSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action =>
                    {
                        _Dispatcher.WaitFor(firstSubscriptionId);
                        invocationsList.Add(second);
                    }
                )
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(2, invocationsList.Count);
            Assert.AreEqual(first, invocationsList[0]);
            Assert.AreEqual(second, invocationsList[1]);
        }

        [TestMethod]
        public async Task WaitForStoreDoesNotBlockIfHandlerWasUnregistered()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            var firstSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => invocationsList.Add(first)
                )
            );
            var secondSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action =>
                    {
                        _Dispatcher.WaitFor(firstSubscriptionId);
                        invocationsList.Add(second);
                    }
                )
            );
            _Dispatcher.Unregister(firstSubscriptionId);

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(1, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
        }

        [TestMethod]
        public async Task HandlerWaitingForStoreBlocksUntilAwaitedStoreCompletes()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            var store = new MockDelegateStore(
                action => invocationsList.Add(second)
            );
            var firstSubscriptionId = _Dispatcher.Register(
                action =>
                {
                    _Dispatcher.WaitFor(store);
                    invocationsList.Add(first);
                }
            );
            _Dispatcher.Register(store);

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(2, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(first, invocationsList[1]);
        }

        [TestMethod]
        public async Task StoreWaitingForHandlerBlocksUntilAwaitedHandlerCompletes()
        {
            const string first = "first";
            const string second = "second";
            var invocationsList = new List<string>();

            object secondSubscriptionId = null;
            _Dispatcher.Register(
                new MockDelegateStore(
                    action =>
                    {
                        _Dispatcher.WaitFor(secondSubscriptionId);
                        invocationsList.Add(first);
                    }
                )
            );
            secondSubscriptionId = _Dispatcher.Register(
                new MockDelegateStore(
                    action => invocationsList.Add(second)
                )
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(2, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(first, invocationsList[1]);
        }

        [TestMethod]
        public async Task WaitingForMultipleIdsWaitsUntilEachCompletes()
        {
            const string first = "first";
            const string second = "second";
            const string third = "second";
            var invocationsList = new List<string>();

            object secondSubscriptionId = null;
            object thirdSubscriptionId = null;
            _Dispatcher.Register(
                action =>
                {
                    _Dispatcher.WaitFor(secondSubscriptionId, thirdSubscriptionId);
                    invocationsList.Add(first);
                }
            );
            secondSubscriptionId = _Dispatcher.Register(
                action => invocationsList.Add(second)
            );
            thirdSubscriptionId = _Dispatcher.Register(
                action => invocationsList.Add(third)
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(3, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(third, invocationsList[1]);
            Assert.AreEqual(first, invocationsList[2]);
        }

        [TestMethod]
        public async Task WaitingForMultipleStoresWaitsUntilEachCompletes()
        {
            const string first = "first";
            const string second = "second";
            const string third = "second";
            var invocationsList = new List<string>();

            Store secondStore = null;
            Store thirdStore = null;
            _Dispatcher.Register(
                new MockDelegateStore(
                    action =>
                    {
                        _Dispatcher.WaitFor(secondStore, thirdStore);
                        invocationsList.Add(first);
                    }
                )
            );
            secondStore = new MockDelegateStore(
                action => invocationsList.Add(second)
            );
            thirdStore = new MockDelegateStore(
                action => invocationsList.Add(third)
            );
            _Dispatcher.Register(secondStore);
            _Dispatcher.Register(thirdStore);

            await _Dispatcher.DispatchAsync(null);

            Assert.AreEqual(3, invocationsList.Count);
            Assert.AreEqual(second, invocationsList[0]);
            Assert.AreEqual(third, invocationsList[1]);
            Assert.AreEqual(first, invocationsList[2]);
        }

        [TestMethod]
        public async Task IsDispatchingIsUpdatedWhileNotifyingSubscribers()
        {
            Assert.IsFalse(_Dispatcher.IsDispatching);

            _Dispatcher.Register(
                action => Assert.IsTrue(_Dispatcher.IsDispatching)
            );
            await _Dispatcher.DispatchAsync(null);

            Assert.IsFalse(_Dispatcher.IsDispatching);
        }

        [TestMethod]
        public async Task IsDispatchingIsSetToFalseEvenIfAHandlerThrowsExcetpion()
        {
            Assert.IsFalse(_Dispatcher.IsDispatching);

            _Dispatcher.Register(
                action => throw new Exception()
            );
            await Assert.ThrowsExceptionAsync<Exception>(() => _Dispatcher.DispatchAsync(null));

            Assert.IsFalse(_Dispatcher.IsDispatching);
        }

        [TestMethod]
        public void RegisteringNullCallbackThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.Register(callback: null));
            Assert.AreEqual(new ArgumentNullException("callback").Message, exception.Message);
        }

        [TestMethod]
        public void RegisteringNullStoreThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.Register(store: null));
            Assert.AreEqual(new ArgumentNullException("store").Message, exception.Message);
        }

        [TestMethod]
        public void UnregisteringNullIdThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.Unregister(id: null));
            Assert.AreEqual(new ArgumentNullException("id").Message, exception.Message);
        }

        [TestMethod]
        public void UnregisteringNullStoreThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.Unregister(store: null));
            Assert.AreEqual(new ArgumentNullException("store").Message, exception.Message);
        }

        [TestMethod]
        public void WaitForNullThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.WaitFor(id: null));
            Assert.AreEqual(new ArgumentNullException("id").Message, exception.Message);
        }

        [TestMethod]
        public void WaitForNullStoreThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.WaitFor(store: null));
            Assert.AreEqual(new ArgumentNullException("store").Message, exception.Message);
        }

        [TestMethod]
        public void WaitForMultipleIdsWithNullCollectionThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.WaitFor(ids: null));
            Assert.AreEqual(new ArgumentNullException("ids").Message, exception.Message);
        }

        [TestMethod]
        public void WaitForMultipleIdsContainingNullValuesThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentException>(() => _Dispatcher.WaitFor(new object[] { null }));
            Assert.AreEqual(new ArgumentException("Cannot contain 'null' ids.", "ids").Message, exception.Message);
        }

        [TestMethod]
        public void WaitForMultipleStoresWithNullCollectionThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentNullException>(() => _Dispatcher.WaitFor(stores: null));
            Assert.AreEqual(new ArgumentNullException("stores").Message, exception.Message);
        }

        [TestMethod]
        public void WaitForMultipleStoresContainingNullValuesThrowsException()
        {
            var exception = Assert.ThrowsException<ArgumentException>(() => _Dispatcher.WaitFor(new Store[] { null }));
            Assert.AreEqual(new ArgumentException("Cannot contain 'null' stores.", "stores").Message, exception.Message);
        }

        [TestMethod]
        public async Task MiddlewareIsBeingCalledBeforeActualDispatch()
        {
            var callList = new List<string>();

            _Dispatcher.Register(action => callList.Add("dispatch"));
            _Dispatcher.Use(
                new MockAsyncMiddleware(
                    async (context, cancellationToken) =>
                    {
                        callList.Add("middleware-before-1");
                        await context.NextAsync(cancellationToken);
                        callList.Add("middleware-after-1");
                    }
                )
            );
            _Dispatcher.Use(
                new MockAsyncMiddleware(
                    async (context, cancellationToken) =>
                    {
                        callList.Add("middleware-before-2");
                        await context.NextAsync(new object(), cancellationToken);
                        callList.Add("middleware-after-2");
                    }
                )
            );
            _Dispatcher.Use(
                new MockAsyncMiddleware(
                    async (context, cancellationToken) =>
                    {
                        await Task.Yield();
                        callList.Add("middleware-before-3");
                        context.Dispatch(new object());
                        callList.Add("middleware-after-3");
                    }
                )
            );
            _Dispatcher.Use(
                new MockAsyncMiddleware(
                    (context, cancellationToken) => throw new InvalidOperationException()
                )
            );

            await _Dispatcher.DispatchAsync(null);

            Assert.IsTrue(
                callList.SequenceEqual(new[]
                {
                    "middleware-before-1",
                    "middleware-before-2",
                    "middleware-before-3",
                    "dispatch",
                    "middleware-after-3",
                    "middleware-after-2",
                    "middleware-after-1"
                }),
                $"Actual: {string.Join(", ", callList)}"
            );
        }

        [TestMethod]
        public async Task ModifyingTheActionPropagatesToAllFutureMiddleware()
        {
            var initialAction = new object();
            object firstAction = null;
            object secondAction = null;

            _Dispatcher.Use(
                new MockAsyncMiddleware(
                    async (context, cancellationToken) =>
                    {
                        firstAction = context.Action;
                        await context.NextAsync(new object(), cancellationToken);
                    }
                )
            );
            _Dispatcher.Use(
                new MockAsyncMiddleware(
                    (context, cancellationToken) =>
                    {
                        secondAction = context.Action;
                        return Task.FromResult<object>(null);
                    }
                )
            );

            await _Dispatcher.DispatchAsync(initialAction);

            Assert.AreSame(initialAction, firstAction);
            Assert.AreNotSame(firstAction, secondAction);
        }

        [TestMethod]
        public async Task UsingConcreteActionMiddlewareGetsCalledOnlyWhenCompatible()
        {
            var callList = new List<string>();

            _Dispatcher.Register(action => callList.Add("dispatch"));
            _Dispatcher.Use(
                new MockAsyncMiddleware<int?>(
                    async (context, cancellationToken) =>
                    {
                        callList.Add("middleware-1");
                        await context.NextAsync(cancellationToken);
                    }
                )
            );
            _Dispatcher.Use(
                new MockAsyncMiddleware<object>(
                    (context, cancellationToken) =>
                    {
                        callList.Add("middleware-2");
                        return context.NextAsync(cancellationToken);
                    }
                )
            );
            _Dispatcher.Use(
                new MockAsyncMiddleware<string>(
                    async (context, cancellationToken) =>
                    {
                        callList.Add("middleware-3");
                        await context.NextAsync(cancellationToken);
                    }
                )
            );
            _Dispatcher.Use(
                new MockAsyncMiddleware<int>(
                    (context, cancellationToken) =>
                    {
                        callList.Add("middleware-4");
                        return context.NextAsync(cancellationToken);
                    }
                )
            );

            await _Dispatcher.DispatchAsync(null);
            await _Dispatcher.DispatchAsync(string.Empty);

            Assert.IsTrue(
                callList.SequenceEqual(new[]
                {
                    "middleware-1",
                    "middleware-2",
                    "middleware-3",
                    "dispatch",
                    "middleware-2",
                    "middleware-3",
                    "dispatch"
                }),
                $"Actual: {string.Join(", ", callList)}"
            );
        }

        [TestMethod]
        public async Task CallingDispatchNextWithInvalidIdThrowsException()
        {
            var invalidId = new LinkedList<IAsyncMiddleware>(
                new[]
                {
                    new MockAsyncMiddleware((context, cancellationToken) => context.NextAsync(cancellationToken))
                }).First;

            var dispatcher = new RevealingAsyncDispatcher();

            var exception = await Assert.ThrowsExceptionAsync<ArgumentException>(() => dispatcher.DispatchNextAsync(invalidId, new object()));
            Assert.AreEqual(new ArgumentException("The provided id does not correspond to a configured middleware.", "id").Message, exception.Message);
        }

        private sealed class RevealingAsyncDispatcher : AsyncDispatcher
        {
            public Task DispatchNextAsync(object id, object action)
                => DispatchNextAsync(id, action, CancellationToken.None);
        }
    }
}
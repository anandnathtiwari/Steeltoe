// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reactive.Linq;
using System.Reactive.Observable.Aliases;
using Steeltoe.CircuitBreaker.Hystrix.Util;
using Steeltoe.Common.Util;

namespace Steeltoe.CircuitBreaker.Hystrix.Metric.Sample;

public class HystrixUtilizationStream
{
    private const int DataEmissionIntervalInMs = 500;

    // The data emission interval is looked up on startup only
    private static readonly HystrixUtilizationStream Instance = new(DataEmissionIntervalInMs);

    private readonly IObservable<HystrixUtilization> _allUtilizationStream;
    private readonly AtomicBoolean _isSourceCurrentlySubscribed = new(false);

    private static Func<long, HystrixUtilization> AllUtilization { get; } = timestamp =>
        HystrixUtilization.From(AllCommandUtilization(timestamp), AllThreadPoolUtilization(timestamp));

    private static Func<long, Dictionary<IHystrixCommandKey, HystrixCommandUtilization>> AllCommandUtilization { get; } = _ =>
    {
        var commandUtilizationPerKey = new Dictionary<IHystrixCommandKey, HystrixCommandUtilization>();

        foreach (HystrixCommandMetrics commandMetrics in HystrixCommandMetrics.GetInstances())
        {
            IHystrixCommandKey commandKey = commandMetrics.CommandKey;
            commandUtilizationPerKey.Add(commandKey, SampleCommandUtilization(commandMetrics));
        }

        return commandUtilizationPerKey;
    };

    private static Func<long, Dictionary<IHystrixThreadPoolKey, HystrixThreadPoolUtilization>> AllThreadPoolUtilization { get; } = _ =>
    {
        var threadPoolUtilizationPerKey = new Dictionary<IHystrixThreadPoolKey, HystrixThreadPoolUtilization>();

        foreach (HystrixThreadPoolMetrics threadPoolMetrics in HystrixThreadPoolMetrics.GetInstances())
        {
            IHystrixThreadPoolKey threadPoolKey = threadPoolMetrics.ThreadPoolKey;
            threadPoolUtilizationPerKey.Add(threadPoolKey, SampleThreadPoolUtilization(threadPoolMetrics));
        }

        return threadPoolUtilizationPerKey;
    };

    private static Func<HystrixUtilization, Dictionary<IHystrixCommandKey, HystrixCommandUtilization>> OnlyCommandUtilization { get; } = hystrixUtilization =>
        hystrixUtilization.CommandUtilizationMap;

    private static Func<HystrixUtilization, Dictionary<IHystrixThreadPoolKey, HystrixThreadPoolUtilization>> OnlyThreadPoolUtilization { get; } =
        hystrixUtilization => hystrixUtilization.ThreadPoolUtilizationMap;

    public int IntervalInMilliseconds { get; }

    public bool IsSourceCurrentlySubscribed => _isSourceCurrentlySubscribed.Value;

    public HystrixUtilizationStream(int intervalInMilliseconds)
    {
        IntervalInMilliseconds = intervalInMilliseconds;

        _allUtilizationStream = Observable.Interval(TimeSpan.FromMilliseconds(intervalInMilliseconds)).Map(t => AllUtilization(t)).OnSubscribe(() =>
        {
            _isSourceCurrentlySubscribed.Value = true;
        }).OnDispose(() =>
        {
            _isSourceCurrentlySubscribed.Value = false;
        }).Publish().RefCount();
    }

    public static HystrixUtilizationStream GetInstance()
    {
        return Instance;
    }

    // Return a ref-counted stream that will only do work when at least one subscriber is present
    public IObservable<HystrixUtilization> Observe()
    {
        return _allUtilizationStream;
    }

    public IObservable<Dictionary<IHystrixCommandKey, HystrixCommandUtilization>> ObserveCommandUtilization()
    {
        return _allUtilizationStream.Map(a => OnlyCommandUtilization(a));
    }

    public IObservable<Dictionary<IHystrixThreadPoolKey, HystrixThreadPoolUtilization>> ObserveThreadPoolUtilization()
    {
        return _allUtilizationStream.Map(a => OnlyThreadPoolUtilization(a));
    }

    internal static HystrixUtilizationStream GetNonSingletonInstanceOnlyUsedInUnitTests(int delayInMs)
    {
        return new HystrixUtilizationStream(delayInMs);
    }

    private static HystrixCommandUtilization SampleCommandUtilization(HystrixCommandMetrics commandMetrics)
    {
        return HystrixCommandUtilization.Sample(commandMetrics);
    }

    private static HystrixThreadPoolUtilization SampleThreadPoolUtilization(HystrixThreadPoolMetrics threadPoolMetrics)
    {
        return HystrixThreadPoolUtilization.Sample(threadPoolMetrics);
    }
}
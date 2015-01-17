// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http.Core;
using Microsoft.AspNet.Http.Interfaces;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.DependencyInjection.Fallback;
using Xunit;

namespace Microsoft.AspNet.Http.Extensions.Tests
{
    public class UseWithServicesTests
    {
        [Fact]
        public async Task CallingUseThatAlsoTakesServices()
        {
            var builder = new ApplicationBuilder(new ServiceCollection()
                .AddScoped<ITestService, TestService>()
                .BuildServiceProvider());

            ITestService theService = null;
            builder.Use<ITestService>(async (ctx, next, testService) =>
            {
                theService = testService;
                await next();
            });

            var app = builder.Build();
            await app(new DefaultHttpContext());

            Assert.IsType<TestService>(theService);
        }

        [Fact]
        public async Task ServicesArePerRequest()
        {
            var services = new ServiceCollection()
                .AddScoped<ITestService, TestService>()
                .BuildServiceProvider();
            var builder = new ApplicationBuilder(services);

            builder.Use(async (ctx, next) =>
            {
                var serviceScopeFactory = services.GetRequiredService<IServiceScopeFactory>();
                using (var serviceScope = serviceScopeFactory.CreateScope())
                {
                    var priorApplicationServices = ctx.ApplicationServices;
                    var priorRequestServices = ctx.ApplicationServices;
                    ctx.ApplicationServices = services;
                    ctx.RequestServices = serviceScope.ServiceProvider;
                    try
                    {
                        await next();
                    }
                    finally
                    {
                        ctx.ApplicationServices = priorApplicationServices;
                        ctx.RequestServices = priorRequestServices;
                    }
                }
            });

            var testServicesA = new List<ITestService>();
            builder.Use(async (HttpContext ctx, Func<Task> next, ITestService testService) =>
            {
                testServicesA.Add(testService);
                await next();
            });

            var testServicesB = new List<ITestService>();
            builder.Use<ITestService>(async (ctx, next, testService) =>
            {
                testServicesB.Add(testService);
                await next();
            });

            var app = builder.Build();
            await app(new DefaultHttpContext());
            await app(new DefaultHttpContext());

            Assert.Equal(2, testServicesA.Count);
            Assert.IsType<TestService>(testServicesA[0]);
            Assert.IsType<TestService>(testServicesA[1]);

            Assert.Equal(2, testServicesB.Count);
            Assert.IsType<TestService>(testServicesB[0]);
            Assert.IsType<TestService>(testServicesB[1]);

            Assert.Same(testServicesA[0], testServicesB[0]);
            Assert.Same(testServicesA[1], testServicesB[1]);

            Assert.NotSame(testServicesA[0], testServicesA[1]);
            Assert.NotSame(testServicesB[0], testServicesB[1]);
        }

        [Fact]
        public async Task InvokeMethodWillAllowPerRequestServices()
        {
            var services = new ServiceCollection()
                .AddScoped<ITestService, TestService>()
                .BuildServiceProvider();
            var builder = new ApplicationBuilder(services);
            builder.UseMiddleware<TestMiddleware>();
            var app = builder.Build();

            var ctx1 = new DefaultHttpContext();
            await app(ctx1);

            var testService = ctx1.Items[typeof(ITestService)];
            Assert.IsType<TestService>(testService);
        }

        [Fact]
        public async Task UseMiddlewareCanActivateWithoutCustomActivator()
        {
            var services = new ServiceCollection()
                .AddScoped<ITestService, TestService>()
                .BuildServiceProvider();
            var builder = new ApplicationBuilder(services);
            builder.UseMiddleware<TestActivatorMiddleware>();
            var app = builder.Build();

            var ctx1 = new DefaultHttpContext();
            await app(ctx1);

            var testService = ctx1.Items[typeof(ITestService)];
            var castService = Assert.IsType<TestService>(testService);
            Assert.False(castService.IsCustomized);
        }

        [Fact]
        public async Task UseMiddlewareCanActivateWithCustomActivator()
        {
            var services = new ServiceCollection()
                .AddScoped<ITestService, TestService>()
                .AddTransient<IMiddlewareActivator, TestActivator>()
                .BuildServiceProvider();
            var builder = new ApplicationBuilder(services);
            builder.UseMiddleware<TestActivatorMiddleware>();
            var app = builder.Build();

            var ctx1 = new DefaultHttpContext();
            await app(ctx1);

            var testService = ctx1.Items[typeof(ITestService)];
            var castService = Assert.IsType<TestService>(testService);
            Assert.True(castService.IsCustomized);
        }
    }

    public interface ITestService
    {
    }

    public class TestService : ITestService
    {
        public bool IsCustomized { get; set; }
    }

    public class TestMiddleware
    {
        RequestDelegate _next;

        public TestMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext context, ITestService testService)
        {
            context.Items[typeof(ITestService)] = testService;
            return Task.FromResult(0);
        }
    }

    public class TestActivatorMiddleware
    {
        private readonly ITestService _testService;

        public TestActivatorMiddleware(RequestDelegate next, ITestService testService)
        {
            _testService = testService;
        }

        public async Task Invoke(HttpContext context)
        {
            context.Items[typeof(ITestService)] = _testService;
        }
    }

    public class TestActivator : IMiddlewareActivator
    {
        private readonly IServiceProvider _provider;

        public TestActivator(IServiceProvider provider)
        {
            _provider = provider;
        }

        public object CreateInstance(Type middlewareType, object[] parameters)
        {
            var testService = new TestService();
            testService.IsCustomized = true;

            return ActivatorUtilities.CreateInstance(
                _provider, middlewareType, parameters.Concat(new[] { testService }).ToArray());
        }
    }
}
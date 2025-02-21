using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Odin.Hosting.Tests;

public class AsyncAwaitExceptionHandling
{
    //
    // These unit tests illustrate a subtle difference in when exceptions 
    // are thrown when calling and awaiting a function that is
    //
    // When dealing with a *sync* function that implements an *async* interface,
    // it basically comes down to if it is smarter to:
    //
    // (1) Tell the compiler to ignore warning CS1998;
    // (2) Remove the async keyword and explicitly return Task.FromResult(...) from the function;
    // (3) Keep the async keyword and explicitly return await Task.FromResult(...) from the function;
    //
    // Only (2) actually does something unexpectedly, so we should keep away from that one.
    // (1) and (3) do what you would expect, but introduces a slight overhead for the CPU.
    //
    // Arguments for (1): https://github.com/dotnet/csharplang/discussions/5457  
    // Arguments for (3): https://blog.stephencleary.com/2016/12/eliding-async-await.html
    // 
    
    [Test]
    public async Task ExpectedExceptionFlow1A()
    {
        var ex = new Exception();
        try
        {
            // Async methods "collects" exceptions and throws when being awaited, so below *will* throw here:
            await DoSomethingAsync1();
        }
        catch (IOException e)
        {
            ex = e;
        }
        ClassicAssert.AreEqual("I'm a moon!", ex!.Message);
    }
    
    //
    
    [Test]
    public async Task ExpectedExceptionFlow1B()
    {
        var ex = new Exception();
        
        // Async methods "collects" exceptions and throws when being awaited, so below will *not* throw here:
        var task = DoSomethingAsync1();

        try
        {
            // Now we await, which *will* throw just because we added the "async" keyword to the function
            await task;
        }
        catch (IOException e)
        {
            ex = e;
        }
        ClassicAssert.AreEqual("I'm a moon!", ex!.Message);
    } 
    
    //

    
    [Test]
    public async Task ExpectedExceptionFlow2A()
    {
        var ex = new Exception();
        try
        {
            // Async methods "collects" exceptions and throws when being awaited, so below *will* throw here:
            await DoSomethingAsync2();
        }
        catch (IOException e)
        {
            ex = e;
        }
        ClassicAssert.AreEqual("I'm a moon!", ex!.Message);
    }
    
    //
    
    [Test]
    public async Task UnexpectedExceptionFlow2A()
    {
        var ex = new Exception();
        var task = Task.FromResult(false);

        try
        {
            // Async methods "collects" exceptions and throws when being awaited, so below should *not* throw here:
            task = DoSomethingAsync2();
        }
        catch (IOException e)
        {
            // But it *does* because of the missing 'async' keyword on the called function.
            // This is probably not what you expected!
            ex = e;
        }
        ClassicAssert.AreEqual("I'm a moon!", ex!.Message);
        
        // Now we await, which unexpectedly does not throw (see above)
        await task;
    } 
    
    //
    
    [Test]
    public async Task ExpectedExceptionFlow3A()
    {
        var ex = new Exception();
        try
        {
            // Async methods "collects" exceptions and throws when being awaited, so below *will* throw here:
            await DoSomethingAsync3(true);
        }
        catch (IOException e)
        {
            ex = e;
        }
        ClassicAssert.AreEqual("I'm a moon!", ex!.Message);
    }
    
    //
    
    [Test]
    public async Task ExpectedExceptionFlow3B()
    {
        var ex = new Exception();
        
        // Async methods "collects" exceptions and throws when being awaited, so below will *not* throw here:
        var task = DoSomethingAsync3(true);

        try
        {
            // Now we await, which *will* throw just because we added the "async" keyword to the function
            await task;
        }
        catch (IOException e)
        {
            ex = e;
        }
        ClassicAssert.AreEqual("I'm a moon!", ex!.Message);
    } 
    
    //
    
    
#pragma warning disable CS1998
    private async Task<bool> DoSomethingAsync1()
    {
        throw new IOException("I'm a moon!");
    }
#pragma warning restore CS1998
    
    //

    private Task<bool> DoSomethingAsync2()
    {
        throw new IOException("I'm a moon!");
    }
    
    //
    
    private async Task<bool> DoSomethingAsync3(bool shouldThrow)
    {
        if (shouldThrow)
        {
            throw new IOException("I'm a moon!");
        }
        return await Task.FromResult(true);
    }
    
    
}


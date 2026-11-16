using System.Windows;
using System.Windows.Threading;
using Kazuko.Shared.UI.Behaviors;
using Xunit;

namespace Kazuko.Shared.UI.Tests;

public sealed class DeferredWindowCloseTests
{
    [Theory]
    [InlineData("clean")]
    [InlineData("timeout")]
    [InlineData("offline")]
    public void Logout_with_inline_flush_returns_from_Closing_before_opening_login_and_closing_shell(string flush)
    {
        RunSta(() =>
        {
            var shell=NewWindow();var login=NewWindow();
            var insideInitialClose=false;var prepared=0;var logins=0;var disposed=0;
            try
            {
                DeferredWindowCloseBehavior.Attach(shell,async () =>
                {
                    prepared++;
                    try
                    {
                        await (flush switch
                        {
                            "timeout" => Task.FromCanceled(new CancellationToken(true)),
                            "offline" => Task.FromException(new System.IO.IOException("offline")),
                            _ => Task.CompletedTask
                        });
                    }
                    catch(Exception) { /* Same policy as App: retain local outbox and continue closing. */ }
                },() =>
                {
                    Assert.False(insideInitialClose);
                    Assert.Equal(0,disposed);
                    logins++;login.Show();
                });
                shell.Closed+=(_,_)=>disposed++;
                shell.Show();
                insideInitialClose=true;shell.Close();insideInitialClose=false;
                Assert.Equal(0,prepared);
                Assert.Equal(0,disposed);
                Assert.True(shell.IsVisible);
                PumpUntil(()=>disposed==1);
                Assert.False(shell.IsVisible);
                Assert.True(login.IsVisible);
                Assert.Equal(1,prepared);Assert.Equal(1,logins);Assert.Equal(1,disposed);
            }
            finally { if(shell.IsVisible)shell.Close();login.Close(); }
        });
    }

    [Fact]
    public void Repeated_close_requests_while_flush_is_pending_prepare_and_finalize_once()
    {
        RunSta(() =>
        {
            var shell=NewWindow();var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var prepared=0;var finalized=0;var closed=0;
            DeferredWindowCloseBehavior.Attach(shell,()=>{prepared++;return pending.Task;},()=>finalized++);
            shell.Closed+=(_,_)=>closed++;
            shell.Show();shell.Close();PumpUntil(()=>prepared==1);
            Assert.False(shell.IsEnabled);
            shell.Close();shell.Close();
            Assert.Equal(1,prepared);Assert.Equal(0,finalized);Assert.True(shell.IsVisible);
            pending.SetResult();PumpUntil(()=>closed==1);
            Assert.Equal(1,finalized);Assert.Equal(1,closed);
        });
    }

    [Fact]
    public void Unexpected_preparation_failure_reenables_the_window_and_allows_retry()
    {
        RunSta(() =>
        {
            var shell=NewWindow();var attempts=0;var closed=0;
            DeferredWindowCloseBehavior.Attach(shell,()=>++attempts==1?Task.FromException(new InvalidOperationException("failure")):Task.CompletedTask,()=>{});
            shell.Closed+=(_,_)=>closed++;
            shell.Show();shell.Close();PumpUntil(()=>attempts==1&&shell.IsEnabled);
            Assert.True(shell.IsVisible);Assert.Equal(0,closed);
            shell.Close();PumpUntil(()=>closed==1);Assert.Equal(2,attempts);
        });
    }

    private static Window NewWindow()=>new(){Width=200,Height=100,Left=-32000,Top=-32000,
        ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
    private static void PumpUntil(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(5)};
        timer.Tick+=(_,_)=>{if(condition()||DateTime.UtcNow>=deadline)frame.Continue=false;};
        timer.Start();Dispatcher.PushFrame(frame);timer.Stop();Assert.True(condition(),"Deferred window close timed out.");
    }
    private static void RunSta(Action action)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try{action();}catch(Exception ex){failure=ex;}
            finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)),"Window close regression timed out.");
        if(failure is not null)throw new Exception("Deferred close regression failed.",failure);
    }
}

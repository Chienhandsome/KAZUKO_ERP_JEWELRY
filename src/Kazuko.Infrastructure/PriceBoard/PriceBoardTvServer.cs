using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Infrastructure.PriceBoard;

public sealed class PriceBoardTvServerFactory : IPriceBoardTvServerFactory
{
    public IPriceBoardTvServer Start(int port,bool allowLan,Func<PriceBoardTvSnapshot> readSnapshot)=>new PriceBoardTvServer(port,allowLan,readSnapshot);
}

internal sealed class PriceBoardTvServer : IPriceBoardTvServer
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop=new();
    private readonly SemaphoreSlim _clients=new(16,16);
    private readonly Func<PriceBoardTvSnapshot> _read;
    private readonly string _prefix="/"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()+"/";
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    public string LocalUrl{get;}
    public IReadOnlyList<string> LanUrls{get;}
    private readonly Task _acceptTask;

    internal PriceBoardTvServer(int port,bool allowLan,Func<PriceBoardTvSnapshot> readSnapshot)
    {
        if(port!=0 && port is <1024 or >65535)throw new ArgumentOutOfRangeException(nameof(port));
        _read=readSnapshot;
        _listener=new TcpListener(allowLan?IPAddress.Any:IPAddress.Loopback,port);
        try{_listener.Start(16);}catch(SocketException ex){throw new InvalidOperationException($"Không mở được cổng TV {port}. Cổng có thể đang được sử dụng; hãy chọn cổng khác.",ex);}
        port=((IPEndPoint)_listener.LocalEndpoint).Port;
        LocalUrl=$"http://127.0.0.1:{port}{_prefix}";
        LanUrls=allowLan?NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up)
            .SelectMany(x=>x.GetIPProperties().UnicastAddresses).Select(x=>x.Address)
            .Where(x=>x.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(x)&&!x.ToString().StartsWith("169.254.",StringComparison.Ordinal))
            .Distinct().Select(x=>$"http://{x}:{port}{_prefix}").ToArray():[];
        _acceptTask=AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            while(!_stop.IsCancellationRequested)
            {
                var client=await _listener.AcceptTcpClientAsync(_stop.Token);
                if(!await _clients.WaitAsync(0,_stop.Token)){client.Dispose();continue;}
                _=ServeAsync(client);
            }
        }
        catch(OperationCanceledException){}catch(SocketException)when(_stop.IsCancellationRequested){}catch(ObjectDisposedException)when(_stop.IsCancellationRequested){}
    }
    private async Task ServeAsync(TcpClient client)
    {
        using(client)
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));var ct=timeout.Token;
            try
            {
                var stream=client.GetStream();var buffer=new byte[8192];var used=0;var complete=false;
                while(used<buffer.Length)
                {
                    var read=await stream.ReadAsync(buffer.AsMemory(used),ct);if(read==0)return;used+=read;
                    if(Encoding.ASCII.GetString(buffer,0,used).Contains("\r\n\r\n",StringComparison.Ordinal)){complete=true;break;}
                }
                if(!complete){await Reply(stream,431,"text/plain","Request too large"u8.ToArray(),ct);return;}
                var first=Encoding.ASCII.GetString(buffer,0,used).Split("\r\n",2)[0].Split(' ');
                if(first.Length!=3||first[0]!="GET"){await Reply(stream,405,"text/plain","Read only"u8.ToArray(),ct);return;}
                var path=first[1].Split('?',2)[0];
                if(!path.StartsWith(_prefix,StringComparison.Ordinal)){await Reply(stream,404,"text/plain",[],ct);return;}
                var resource=path[_prefix.Length..];
                if(resource=="data")
                {
                    await Reply(stream,200,"application/json; charset=utf-8",JsonSerializer.SerializeToUtf8Bytes(_read(),JsonOptions),ct);return;
                }
                var asset=resource switch{""=>"index.html","app.js"=>"app.js","style.css"=>"style.css",_=>null};
                if(asset is null){await Reply(stream,404,"text/plain",[],ct);return;}
                using var input=typeof(PriceBoardTvServer).Assembly.GetManifestResourceStream("Kazuko.Tv."+asset)!;
                using var bytes=new MemoryStream();await input.CopyToAsync(bytes,ct);
                await Reply(stream,200,asset.EndsWith(".js",StringComparison.Ordinal)?"application/javascript; charset=utf-8":asset.EndsWith(".css",StringComparison.Ordinal)?"text/css; charset=utf-8":"text/html; charset=utf-8",bytes.ToArray(),ct);
            }
            catch(OperationCanceledException){}catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}
            catch(Exception){/* Never return exception/connection details to a public TV client. */}
            finally{_clients.Release();}
        }
    }
    private static async Task Reply(NetworkStream stream,int status,string type,byte[] body,CancellationToken ct)
    {
        var headers=$"HTTP/1.1 {status} {(status==200?"OK":"Error")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; object-src 'none'; base-uri 'none'\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers),ct);await stream.WriteAsync(body,ct);
    }
    public void Dispose(){_stop.Cancel();_listener.Stop();_ = _acceptTask.Exception;}
}

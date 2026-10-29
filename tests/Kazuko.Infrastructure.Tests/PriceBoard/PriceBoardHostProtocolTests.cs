using System.Buffers.Binary;
using System.Text.Json;
using Kazuko.Core.Authentication;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.PriceBoard;
using Xunit;

namespace Kazuko.Infrastructure.Tests.PriceBoard;

public sealed class PriceBoardHostProtocolTests
{
    [Fact]
    public async Task Framed_requests_round_trip_without_connection_credentials()
    {
        var request=new PriceBoardHostRequest("configure",new(1,"admin","Admin","ADMIN",2,"CN","CN"),3,new(ShopName:"Tiệm vàng"));
        using var stream=new MemoryStream();await PriceBoardHostProtocol.WriteAsync(stream,request,default);
        stream.Position=0;
        var result=await PriceBoardHostProtocol.ReadAsync<PriceBoardHostRequest>(stream,default);
        Assert.Equal(request,result);
        Assert.DoesNotContain("Password",System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
    [Theory]
    [InlineData(-1)][InlineData(0)][InlineData(1048577)]
    public async Task Untrusted_frame_lengths_are_rejected_before_allocation(int size)
    {
        var bytes=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(bytes,size);
        using var stream=new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>PriceBoardHostProtocol.ReadAsync<PriceBoardHostRequest>(stream,default));
    }
    [Fact]
    public void Scope_separates_databases_and_branches_but_does_not_depend_on_password()
    {
        var profile=new ConnectionProfile("localhost",3050,"D:/fixture.fdb","SYSDBA","not-a-real-secret");
        var scope=PriceBoardHostProtocol.Scope(profile,1);
        Assert.Equal(scope,PriceBoardHostProtocol.Scope(profile with{Password="different"},1));
        Assert.NotEqual(scope,PriceBoardHostProtocol.Scope(profile,2));
        Assert.NotEqual(scope,PriceBoardHostProtocol.Scope(profile with{DatabasePath="D:/other.fdb"},1));
        Assert.Throws<ArgumentException>(()=>PriceBoardHostProtocol.PipeName("../invalid"));
    }
}

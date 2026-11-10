#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class CafeLoopbackHttpAcceptance
{
    public static IEnumerator Run(Action<bool,string> check)
    {
        check(!CafeLoopbackHttp.CanHandle("https://example.com/"),"remote HTTPS retains original transport");
        check(!CafeLoopbackHttp.CanHandle("http://user@127.0.0.1/"),"loopback transport rejects URL credentials");
        foreach(int status in new[]{302,401})
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            int port=((IPEndPoint)listener.LocalEndpoint).Port;
            var server=Respond(listener,status);
            try
            {
                using(var request=new CafeLoopbackHttp("http://127.0.0.1:"+port+"/test","POST","{}","fixture-only",2))
                {
                    float until=Time.realtimeSinceStartup+4;
                    while(!request.Pending.IsCompleted&&Time.realtimeSinceStartup<until)yield return null;
                    check(request.Pending.IsCompleted,"loopback response finishes "+status);
                    if(request.Pending.IsCompleted)
                    {
                        var result=request.Pending.GetAwaiter().GetResult();
                        check(result.Status==status&&!result.Success&&result.Body=="{\"error\":\"FIXTURE\"}",status==302?"redirect is returned without forwarding credentials":"unauthorized JSON and status are preserved");
                    }
                }
                float serverUntil=Time.realtimeSinceStartup+4;
                while(!server.IsCompleted&&Time.realtimeSinceStartup<serverUntil)yield return null;
                check(server.IsCompleted&&server.GetAwaiter().GetResult(),"local request preserves POST JSON and authorization "+status);
            }
            finally {listener.Stop();}
        }
        var stalled=new TcpListener(IPAddress.Loopback,0);stalled.Start();
        try
        {
            using(var request=new CafeLoopbackHttp("http://127.0.0.1:"+((IPEndPoint)stalled.LocalEndpoint).Port+"/wait","GET"))
            {
                yield return null;
                check(!request.Pending.IsCompleted,"cancel fixture has an active unanswered request");
                request.Dispose();request.Dispose();
                float until=Time.realtimeSinceStartup+2;
                while(!request.Pending.IsCompleted&&Time.realtimeSinceStartup<until)yield return null;
                string error=request.Pending.IsCompleted?request.Pending.GetAwaiter().GetResult().Error:"";
                check(error=="TaskCanceledException"||error=="OperationCanceledException"||error=="ObjectDisposedException","cancel finishes pending request and repeated cleanup is safe");
            }
        }
        finally {stalled.Stop();}
    }
    static async Task<bool> Respond(TcpListener listener,int status)
    {
        try
        {
        using(var peer=await listener.AcceptTcpClientAsync().ConfigureAwait(false))
        using(var stream=peer.GetStream())
        using(var deadline=new System.Threading.CancellationTokenSource(2000))
        {
            var data=new byte[8192];string received="";
            while(!received.Contains("\r\n\r\n")||!received.EndsWith("{}",StringComparison.Ordinal))
            {
                int count=await stream.ReadAsync(data,0,data.Length,deadline.Token).ConfigureAwait(false);
                if(count==0)return false;
                received+=Encoding.UTF8.GetString(data,0,count);
            }
            string body="{\"error\":\"FIXTURE\"}";
            var response=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+" Test\r\nLocation: http://127.0.0.1:1/never-follow\r\nContent-Type: application/json\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n"+body);
            await stream.WriteAsync(response,0,response.Length).ConfigureAwait(false);
            return received.StartsWith("POST /test ",StringComparison.Ordinal)&&received.IndexOf("Authorization: Bearer fixture-only",StringComparison.OrdinalIgnoreCase)>=0;
        }
        }
        catch(Exception error) when(error is SocketException||error is ObjectDisposedException||error is System.IO.IOException||error is OperationCanceledException)
        {return false;}
    }
}
#endif

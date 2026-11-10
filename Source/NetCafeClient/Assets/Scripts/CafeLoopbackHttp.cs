using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Local room services must stay on loopback, including when Windows has a proxy configured.
// Remote HTTPS requests retain the existing Unity transport and its platform configuration.
public sealed class CafeLoopbackHttp : IDisposable
{
    public sealed class Result
    {
        public int Status;
        public string Body="",Error="";
        public bool Success=>Status>=200&&Status<300;
    }
    readonly HttpClient client;
    readonly CancellationTokenSource cancellation=new();
    int disposed;
    public Task<Result> Pending {get;}
    public static bool CanHandle(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttp&&uri.IsLoopback&&string.IsNullOrEmpty(uri.UserInfo);
    public CafeLoopbackHttp(string url,string method,string body=null,string credential=null,int timeoutSeconds=15)
    {
        if(!CanHandle(url))throw new ArgumentException("Only loopback HTTP is supported",nameof(url));
        client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(timeoutSeconds)};
        Pending=Send(url,method,body,credential);
    }
    async Task<Result> Send(string url,string method,string body,string credential)
    {
        try
        {
            using(var request=new HttpRequestMessage(new HttpMethod(method),url))
            {
                if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
                if(!string.IsNullOrEmpty(credential))request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",credential);
                using(var response=await client.SendAsync(request,cancellation.Token).ConfigureAwait(false))
                    return new Result{Status=(int)response.StatusCode,Body=await response.Content.ReadAsStringAsync().ConfigureAwait(false)};
            }
        }
        catch(Exception error) when(error is HttpRequestException||error is OperationCanceledException||error is ObjectDisposedException||error is System.IO.IOException)
        {return new Result{Error=error.GetType().Name};}
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        cancellation.Cancel();client.Dispose();cancellation.Dispose();
    }
}

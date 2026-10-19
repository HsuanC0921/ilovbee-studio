using System.Net;
using System.Net.Sockets;
using System.Text;

namespace iLovbeeStudio;

public sealed record HttpRequest(string Method,string Path,string Query,Dictionary<string,string> Headers,byte[] Body);
public sealed record HttpResponse(int Status,string ContentType,byte[] Body,string? SetCookie=null) {
    public static HttpResponse Text(int status,string text)=>new(status,"text/plain; charset=utf-8",Encoding.UTF8.GetBytes(text));
}

/// <summary>只監聽本機回送位址的極簡 HTTP/1.1 伺服器，每個連線處理一個請求。</summary>
public sealed class MiniHttp {
    const int MaxHeader=16*1024,MaxBody=2*1024*1024;
    readonly TcpListener listener=new(IPAddress.Loopback,0);
    readonly Func<HttpRequest,HttpResponse> handler;
    public int Port{get;private set;}
    public MiniHttp(Func<HttpRequest,HttpResponse> handler){this.handler=handler;}
    public void Start(){
        listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;
        new Thread(Accept){IsBackground=true,Name="http-accept"}.Start();
    }
    public void Stop()=>listener.Stop();
    void Accept(){
        while(true){
            TcpClient client;
            try{client=listener.AcceptTcpClient();}catch(Exception e)when(e is SocketException or ObjectDisposedException or InvalidOperationException){return;}
            ThreadPool.QueueUserWorkItem(_=>Serve(client));
        }
    }
    void Serve(TcpClient client){
        using(client){
            try{
                client.ReceiveTimeout=client.SendTimeout=5000;var stream=client.GetStream();
                var request=Read(stream);var response=request==null?HttpResponse.Text(400,"bad request"):Safe(request);
                var head=new StringBuilder($"HTTP/1.1 {response.Status} {(response.Status==200?"OK":"Error")}\r\nContent-Type: {response.ContentType}\r\nContent-Length: {response.Body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n");
                if(response.SetCookie!=null)head.Append("Set-Cookie: ").Append(response.SetCookie).Append("\r\n");
                head.Append("\r\n");var bytes=Encoding.ASCII.GetBytes(head.ToString());
                stream.Write(bytes);stream.Write(response.Body);
            }catch(Exception e)when(e is IOException or SocketException or ObjectDisposedException){}
        }
    }
    HttpResponse Safe(HttpRequest request){try{return handler(request);}catch(Exception e){return HttpResponse.Text(500,e.Message);}}
    static HttpRequest? Read(NetworkStream stream){
        var buffer=new byte[MaxHeader];int length=0,headerEnd=-1;
        while(headerEnd<0){
            if(length==buffer.Length)return null;
            int n=stream.Read(buffer,length,buffer.Length-length);if(n<=0)return null;length+=n;
            headerEnd=Encoding.ASCII.GetString(buffer,0,length).IndexOf("\r\n\r\n",StringComparison.Ordinal);
        }
        var lines=Encoding.ASCII.GetString(buffer,0,headerEnd).Split("\r\n");
        var first=lines[0].Split(' ');if(first.Length<3)return null;
        var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in lines.Skip(1)){int i=line.IndexOf(':');if(i>0)headers[line[..i].Trim()]=line[(i+1)..].Trim();}
        int contentLength=headers.TryGetValue("Content-Length",out var cl)&&int.TryParse(cl,out var parsed)?parsed:0;
        if(contentLength<0||contentLength>MaxBody)return null;
        var body=new byte[contentLength];int have=Math.Min(contentLength,length-(headerEnd+4));
        Array.Copy(buffer,headerEnd+4,body,0,have);
        while(have<contentLength){int n=stream.Read(body,have,contentLength-have);if(n<=0)return null;have+=n;}
        string target=first[1];int q=target.IndexOf('?');
        return new(first[0],q<0?target:target[..q],q<0?"":target[(q+1)..],headers,body);
    }
}

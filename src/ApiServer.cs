using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace BatteryViewer
{
    // 本地 HTTP API：只监听 127.0.0.1 回环，供本机其它程序/脚本查询电量。
    // GET /api/battery  -> JSON 设备电量数组
    // GET /api/ping     -> {"ok":true}
    public class ApiServer
    {
        public class Snapshot
        {
            public string Name;
            public string VidPid;
            public bool Ok;
            public int? Level;
            public string Status;
            public int? VoltageMv;
            public string Source;
            public string Detail;
        }

        readonly Func<List<Snapshot>> provider;
        readonly Func<int> thresholdProvider;
        HttpListener listener;
        Thread worker;
        volatile bool running;

        public int Port { get; private set; }
        public string LastError = "";

        public ApiServer(int port, Func<List<Snapshot>> provider, Func<int> thresholdProvider = null)
        {
            Port = port;
            this.provider = provider;
            this.thresholdProvider = thresholdProvider;
        }

        public bool Start()
        {
            Stop();
            try
            {
                listener = new HttpListener();
                // "localhost" 前缀非管理员即可监听；127.0.0.1 需要管理员配 URLACL
                listener.Prefixes.Add("http://localhost:" + Port + "/");
                listener.Start();
                running = true;
                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Start();
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                try { if (listener != null) listener.Close(); } catch { }
                listener = null;
                running = false;
                return false;
            }
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) { listener.Stop(); listener.Close(); } } catch { }
            listener = null;
        }

        void Loop()
        {
            while (running && listener != null)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = listener.GetContext();
                    Handle(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (Exception) { }
                finally { try { if (ctx != null) ctx.Response.Close(); } catch { } }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            bool onlyReadable = (ctx.Request.QueryString["readable"] == "1");
            string json;
            if (path == "/api/battery")
            {
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"devices\":[");
                List<Snapshot> snap = null;
                try { snap = provider(); } catch { }
                if (snap != null)
                {
                    if (onlyReadable) snap = snap.FindAll(d => d.Ok);
                    bool first = true;
                    foreach (var d in snap)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"name\":\"").Append(JsonEsc(d.Name))
                          .Append("\",\"vidpid\":\"").Append(JsonEsc(d.VidPid))
                          .Append("\",\"ok\":").Append(d.Ok ? "true" : "false")
                          .Append(",\"level\":").Append(d.Level.HasValue ? d.Level.Value.ToString() : "null")
                          .Append(",\"status\":\"").Append(JsonEsc(d.Status))
                          .Append("\",\"voltage_mv\":").Append(d.VoltageMv.HasValue ? d.VoltageMv.Value.ToString() : "null")
                          .Append(",\"source\":\"").Append(JsonEsc(d.Source))
                          .Append("\",\"detail\":\"").Append(JsonEsc(d.Detail))
                          .Append("\"}");
                    }
                }
                sb.Append("],\"time\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\"}");
                json = sb.ToString();
            }
            else if (path == "/api/ping")
            {
                json = "{\"ok\":true,\"app\":\"BatteryViewer\"}";
            }
            else if (path == "/api/alerts")
            {
                int th = 20;
                try { if (thresholdProvider != null) th = thresholdProvider(); } catch { }
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"threshold\":").Append(th).Append(",\"alerts\":[");
                List<Snapshot> snap = null;
                try { snap = provider(); } catch { }
                bool first = true;
                if (snap != null)
                    foreach (var d in snap)
                    {
                        if (!d.Ok || !d.Level.HasValue || d.Level.Value > th) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"name\":\"").Append(JsonEsc(d.Name))
                          .Append("\",\"level\":").Append(d.Level.Value).Append("}");
                    }
                sb.Append("]}");
                json = sb.ToString();
            }
            else
            {
                json = "{\"ok\":false,\"error\":\"unknown endpoint\",\"endpoints\":[\"/api/battery\",\"/api/ping\",\"/api/alerts\"]}";
                ctx.Response.StatusCode = 404;
            }

            byte[] buf = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            // 允许本机网页直接 fetch
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            try { ctx.Response.OutputStream.Write(buf, 0, buf.Length); } catch { }
        }

        static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
